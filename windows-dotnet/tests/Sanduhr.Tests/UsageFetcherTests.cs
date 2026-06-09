using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Parity tests for Core/UsageFetcher.cs — ported from the Python build's
/// test_fetcher.py. The Python QObject's dataReady / fetchFailed signals map to
/// FetchAsync returning the payload / throwing the typed exception. The
/// ClaudeAPI monkeypatch becomes an injected fake <see cref="IClaudeApiClient"/>;
/// the history append (isolated to a temp APPDATA in Python) becomes a recording
/// fake <see cref="IUsageHistory"/>.
/// </summary>
public class UsageFetcherTests
{
    // -- fakes ----------------------------------------------------------------

    private sealed class FakeClient : IClaudeApiClient
    {
        public Func<JsonObject>? Usage;
        public Exception? UsageThrows;
        public Func<RoutineBudget?>? Routine;
        public Exception? RoutineThrows;
        public AccountPlan? Account { get; set; }

        public Task<JsonObject> GetUsageAsync(CancellationToken ct = default)
        {
            if (UsageThrows is not null) throw UsageThrows;
            return Task.FromResult(Usage!());
        }

        public Task<RoutineBudget?> GetRoutineBudgetAsync(CancellationToken ct = default)
        {
            if (RoutineThrows is not null) throw RoutineThrows;
            return Task.FromResult(Routine is null ? null : Routine());
        }
    }

    private sealed class RecordingHistory : IUsageHistory
    {
        public List<(string Tier, int Util, string? ResetsAt)> Appends { get; } = new();

        public IReadOnlyList<int> Append(string tierKey, int utilization, string? resetsAt = null)
        {
            Appends.Add((tierKey, utilization, resetsAt));
            return new[] { utilization };
        }
    }

    private static JsonObject Obj(string json) => (JsonObject)JsonNode.Parse(json)!;

    // -- success / data flow --------------------------------------------------

    [Fact]
    public async Task Fetch_returns_data_on_success()
    {
        var client = new FakeClient { Usage = () => Obj("{\"five_hour\":{\"utilization\":10}}") };
        var fetcher = new UsageFetcher(client, new RecordingHistory());

        var data = await fetcher.FetchAsync();

        Assert.Equal(10, data["five_hour"]!["utilization"]!.GetValue<int>());
    }

    [Fact]
    public async Task Fetch_synthesizes_routines_tier_when_budget_returned()
    {
        var client = new FakeClient
        {
            Usage = () => Obj("{\"five_hour\":{\"utilization\":10}}"),
            Routine = () => new RoutineBudget(3, 15),
        };
        var fetcher = new UsageFetcher(client, new RecordingHistory());

        var data = await fetcher.FetchAsync();

        Assert.NotNull(data["routines"]);
        Assert.Equal(3, data["routines"]!["used"]!.GetValue<int>());
        Assert.Equal(15, data["routines"]!["limit"]!.GetValue<int>());
        Assert.Null(data["routines"]!["resets_at"]); // daily quota, no reset time
        // 3/15 = 20% utilization
        Assert.True(Math.Abs(data["routines"]!["utilization"]!.GetValue<double>() - 20.0) < 0.01);
    }

    [Fact]
    public async Task Fetch_skips_routines_when_budget_unavailable()
    {
        // Account without Routines — endpoint 404s, client returns null.
        var client = new FakeClient
        {
            Usage = () => Obj("{\"five_hour\":{\"utilization\":10}}"),
            Routine = () => null,
        };
        var fetcher = new UsageFetcher(client, new RecordingHistory());

        var data = await fetcher.FetchAsync();

        Assert.Null(data["routines"]);
    }

    [Fact]
    public async Task Fetch_skips_routines_when_limit_not_positive()
    {
        // Guard parity: `if budget and budget.get("limit", 0) > 0`.
        var client = new FakeClient
        {
            Usage = () => Obj("{\"five_hour\":{\"utilization\":10}}"),
            Routine = () => new RoutineBudget(0, 0),
        };
        var fetcher = new UsageFetcher(client, new RecordingHistory());

        var data = await fetcher.FetchAsync();

        Assert.Null(data["routines"]);
    }

    [Fact]
    public async Task Fetch_routines_failure_is_nonfatal()
    {
        var client = new FakeClient
        {
            Usage = () => Obj("{\"five_hour\":{\"utilization\":10}}"),
            RoutineThrows = new NetworkException("boom"),
        };
        var fetcher = new UsageFetcher(client, new RecordingHistory());

        var data = await fetcher.FetchAsync(); // must not throw

        Assert.Null(data["routines"]);
        Assert.Equal(10, data["five_hour"]!["utilization"]!.GetValue<int>());
    }

    // -- typed error propagation ---------------------------------------------

    [Fact]
    public async Task Fetch_propagates_session_expired()
    {
        var client = new FakeClient { UsageThrows = new SessionExpiredException("401") };
        var fetcher = new UsageFetcher(client, new RecordingHistory());
        await Assert.ThrowsAsync<SessionExpiredException>(() => fetcher.FetchAsync());
    }

    [Fact]
    public async Task Fetch_propagates_cloudflare()
    {
        var client = new FakeClient { UsageThrows = new CloudflareBlockedException("cf") };
        var fetcher = new UsageFetcher(client, new RecordingHistory());
        await Assert.ThrowsAsync<CloudflareBlockedException>(() => fetcher.FetchAsync());
    }

    [Fact]
    public async Task Fetch_propagates_network()
    {
        var client = new FakeClient { UsageThrows = new NetworkException("boom") };
        var fetcher = new UsageFetcher(client, new RecordingHistory());
        await Assert.ThrowsAsync<NetworkException>(() => fetcher.FetchAsync());
    }

    // -- history append seam --------------------------------------------------

    [Fact]
    public async Task Fetch_appends_history_for_tiers_with_utilization()
    {
        var history = new RecordingHistory();
        var client = new FakeClient
        {
            Usage = () => Obj("""
                {
                    "five_hour": {"utilization": 42, "resets_at": "2030-01-01T00:00:00Z"},
                    "seven_day": {"utilization": 5},
                    "seven_day_opus": {"utilization": null},
                    "extra_usage": {}
                }
                """),
            Routine = () => new RoutineBudget(3, 15),
        };
        var fetcher = new UsageFetcher(client, history);

        await fetcher.FetchAsync();

        // five_hour + seven_day have utilization; the synthesized routines tier
        // (3/15 -> 20) also records. null-utilization and utilization-less tiers
        // are skipped.
        Assert.Contains(("five_hour", 42, "2030-01-01T00:00:00Z"), history.Appends);
        Assert.Contains(("seven_day", 5, (string?)null), history.Appends);
        Assert.Contains(("routines", 20, (string?)null), history.Appends);
        Assert.DoesNotContain(history.Appends, a => a.Tier == "seven_day_opus");
        Assert.DoesNotContain(history.Appends, a => a.Tier == "extra_usage");
    }

    [Fact]
    public async Task Fetch_history_append_failure_does_not_fail_fetch()
    {
        var client = new FakeClient { Usage = () => Obj("{\"five_hour\":{\"utilization\":10}}") };
        var throwingHistory = new ThrowingHistory();
        var fetcher = new UsageFetcher(client, throwingHistory);

        var data = await fetcher.FetchAsync(); // must not throw despite history blowing up

        Assert.Equal(10, data["five_hour"]!["utilization"]!.GetValue<int>());
    }

    private sealed class ThrowingHistory : IUsageHistory
    {
        public IReadOnlyList<int> Append(string tierKey, int utilization, string? resetsAt = null)
            => throw new InvalidOperationException("disk full");
    }
}
