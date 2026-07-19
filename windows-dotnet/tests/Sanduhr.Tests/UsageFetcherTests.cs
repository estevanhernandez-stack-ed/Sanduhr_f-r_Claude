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
public class UsageFetcherTests : IDisposable
{
    // TierModel's dynamic scoped-tier registry is process-global static state
    // (see TierModel.RegisterScopedTier). The scoped-limits synthesis tests
    // below register dynamic tiers via UsageFetcher.FetchAsync; each such test
    // resets at its own opening line (parity with TierModelTests), and this
    // Dispose() closes the other side of the gate so no test leaves dynamic
    // state behind for whichever test runs next in the class.
    public void Dispose() => TierModel.ResetDynamicTiersForTests();

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

    /// <summary>Fetch helper for the scoped-limits synthesis tests: a stub
    /// client that parses <paramref name="body"/> as the usage payload and
    /// returns a null Routines budget (no code-access endpoint involved),
    /// against a fresh or caller-supplied <see cref="IUsageHistory"/>.</summary>
    private static Task<JsonObject> FetchWith(string body, IUsageHistory? history = null)
    {
        var client = new FakeClient { Usage = () => Obj(body) };
        var fetcher = new UsageFetcher(client, history ?? new RecordingHistory());
        return fetcher.FetchAsync();
    }

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

    // -- scoped-limits wave: synthesize seven_day_* from limits[] -------------
    //
    // Verbatim from the 2026-07-19 live probe of the owner's Max account —
    // this exact shape (weekly_scoped kind, null model.id, display_name-only
    // handle, opus/sonnet migrating to null) is the contract these tests pin.

    private const string LiveLimitsFixture = """
    {
      "five_hour": { "utilization": 12, "resets_at": "2026-07-20T03:00:00Z" },
      "seven_day": { "utilization": 34, "resets_at": "2026-07-26T05:59:59Z" },
      "seven_day_opus": null,
      "seven_day_sonnet": null,
      "extra_usage": { "utilization": 19, "resets_at": null },
      "limits": [
        { "kind": "session", "group": "session", "percent": 12, "severity": "normal",
          "resets_at": "2026-07-20T03:00:00Z", "scope": null, "is_active": true },
        { "kind": "weekly", "group": "weekly", "percent": 34, "severity": "normal",
          "resets_at": "2026-07-26T05:59:59Z", "is_active": true, "scope": null },
        { "kind": "weekly_scoped", "group": "weekly", "percent": 7, "severity": "normal",
          "resets_at": "2026-07-26T05:59:59Z", "is_active": false,
          "scope": { "model": { "id": null, "display_name": "Fable" }, "surface": null } }
      ]
    }
    """;

    [Fact]
    public async Task Synthesizes_SevenDayFable_FromWeeklyScopedLimit()
    {
        TierModel.ResetDynamicTiersForTests();
        var data = await FetchWith(LiveLimitsFixture);
        var fable = Assert.IsType<JsonObject>(data["seven_day_fable"]);
        Assert.Equal(7, (int)fable["utilization"]!.GetValue<double>());
        Assert.Equal("2026-07-26T05:59:59Z", (string?)fable["resets_at"]);
    }

    [Fact]
    public async Task NonScoped_And_Scopeless_Limits_AreIgnored()
    {
        TierModel.ResetDynamicTiersForTests();
        var data = await FetchWith(LiveLimitsFixture);
        // session/weekly group entries must NOT synthesize keys ("seven_day_" + nothing)
        Assert.All(data.Select(kv => kv.Key), k => Assert.False(k == "seven_day_"));
        Assert.Null(data["session"]);
    }

    [Fact]
    public async Task DoesNotOverwrite_NonNull_UpstreamKey()
    {
        TierModel.ResetDynamicTiersForTests();
        var body = LiveLimitsFixture.Replace("\"seven_day_opus\": null",
            "\"seven_day_opus\": null, \"seven_day_fable\": { \"utilization\": 99, \"resets_at\": null }");
        var data = await FetchWith(body);
        Assert.Equal(99, (int)data["seven_day_fable"]!["utilization"]!.GetValue<double>());
    }

    [Fact]
    public async Task Fills_JsonNull_UpstreamKey()
    {
        TierModel.ResetDynamicTiersForTests();
        var body = LiveLimitsFixture.Replace("\"seven_day_opus\": null",
            "\"seven_day_opus\": null, \"seven_day_fable\": null");
        var data = await FetchWith(body);
        Assert.Equal(7, (int)data["seven_day_fable"]!["utilization"]!.GetValue<double>());
    }

    [Fact]
    public async Task NullPercent_SynthesizesNullUtilization()
    {
        TierModel.ResetDynamicTiersForTests();
        var body = LiveLimitsFixture.Replace("\"percent\": 7", "\"percent\": null");
        var data = await FetchWith(body);
        var fable = Assert.IsType<JsonObject>(data["seven_day_fable"]);
        Assert.True(fable.ContainsKey("utilization")); // present-with-null, not omitted
        Assert.Null(fable["utilization"]);
    }

    [Fact]
    public async Task MissingOrMalformed_Limits_NoOp()
    {
        TierModel.ResetDynamicTiersForTests();
        var noLimits = await FetchWith("""{ "five_hour": { "utilization": 1, "resets_at": null } }""");
        Assert.Null(noLimits["seven_day_fable"]);
        var badLimits = await FetchWith("""{ "limits": { "not": "an array" } }""");
        Assert.Null(badLimits["seven_day_fable"]);
        var junkEntries = await FetchWith("""{ "limits": [ 42, null, { "kind": "weekly_scoped" } ] }""");
        Assert.Null(junkEntries["seven_day_fable"]);
    }

    [Theory]
    [InlineData("Fable", "fable")]
    [InlineData("Haiku 5", "haiku_5")]
    [InlineData("  Weird--Name!! ", "weird_name")]
    public void ScopedSlug_NormalizesDisplayNames(string display, string slug)
        => Assert.Equal(slug, UsageFetcher.ScopedSlug(display));

    [Fact]
    public async Task SynthesizedTier_RegistersAndPersistsToHistory()
    {
        TierModel.ResetDynamicTiersForTests();
        var history = new RecordingHistory();
        var data = await FetchWith(LiveLimitsFixture, history);
        Assert.True(TierModel.IsKnown("seven_day_fable"));
        Assert.Contains(history.Appends, a => a.Tier == "seven_day_fable" && a.Util == 7);
    }

    // "Fable" above is already a static canonical key (TierModel.SevenDayFable),
    // so its IsKnown assertion above is tautological and its history assertion
    // would pass even if RegisterScopedTier were a no-op. "Haiku 5" is NOT in
    // TierModel.CanonicalOrder — this is the proof that a truly dynamic tier
    // both registers AND persists to history within the SAME FetchAsync call.
    private const string DynamicScopedLimitsFixture = """
    {
      "five_hour": { "utilization": 12, "resets_at": "2026-07-20T03:00:00Z" },
      "limits": [
        { "kind": "weekly_scoped", "group": "weekly", "percent": 42, "severity": "normal",
          "resets_at": "2026-07-26T05:59:59Z", "is_active": true,
          "scope": { "model": { "id": null, "display_name": "Haiku 5" }, "surface": null } }
      ]
    }
    """;

    [Fact]
    public async Task DynamicScopedTier_RegistersAndPersistsToHistory_InSameFetch()
    {
        TierModel.ResetDynamicTiersForTests();
        Assert.False(TierModel.IsKnown("seven_day_haiku_5")); // sanity: truly dynamic, not statically known
        var history = new RecordingHistory();

        var data = await FetchWith(DynamicScopedLimitsFixture, history);

        Assert.True(TierModel.IsKnown("seven_day_haiku_5"));
        var haiku = Assert.IsType<JsonObject>(data["seven_day_haiku_5"]);
        Assert.Equal(42, (int)haiku["utilization"]!.GetValue<double>());
        Assert.Contains(history.Appends, a => a.Tier == "seven_day_haiku_5" && a.Util == 42);
    }

    [Fact]
    public async Task MalformedEntry_IsSkipped_ValidEntryStillSynthesizes()
    {
        // One junk `kind` (a number) and one junk `display_name` (a number)
        // must not dark the whole fetch — the valid Fable entry between them
        // still synthesizes. Regression for the pre-fix behavior where the
        // explicit JsonNode->string cast threw InvalidOperationException with
        // no try/catch, failing FetchAsync entirely on a single bad entry.
        TierModel.ResetDynamicTiersForTests();
        var body = """
        {
          "limits": [
            { "kind": 42 },
            { "kind": "weekly_scoped", "percent": 7, "resets_at": "2026-07-26T05:59:59Z",
              "scope": { "model": { "display_name": "Fable" } } },
            { "kind": "weekly_scoped", "scope": { "model": { "display_name": 7 } } }
          ]
        }
        """;

        var data = await FetchWith(body); // must not throw

        var fable = Assert.IsType<JsonObject>(data["seven_day_fable"]);
        Assert.Equal(7, (int)fable["utilization"]!.GetValue<double>());
    }

    [Fact]
    public async Task AllPunctuation_DisplayName_YieldsNoEmptySlugKey()
    {
        // "!!!" passes the IsNullOrWhiteSpace guard but slugs to "", which
        // would register the bogus "seven_day_" key without the length guard.
        TierModel.ResetDynamicTiersForTests();
        var body = """
        {
          "limits": [
            { "kind": "weekly_scoped", "percent": 5, "resets_at": null,
              "scope": { "model": { "display_name": "!!!" } } }
          ]
        }
        """;

        var data = await FetchWith(body);

        Assert.False(data.ContainsKey("seven_day_"));
        Assert.False(TierModel.IsKnown("seven_day_"));
    }
}
