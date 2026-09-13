using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// The publish payload against the dashboard's manage_usage/record contract,
/// the consent filter (a root not marked shareable never leaves the machine),
/// stale-quota handling (never a stale percentage), and the POST result
/// mapping over a fake handler — no live network.
/// </summary>
public class UsagePublisherTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 6, 45, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 9, 12);

    private static RootDayBurn Burn(string root, params (string Project, string Tier, long Tokens)[] rows)
    {
        var byProject = new Dictionary<string, long>(StringComparer.Ordinal);
        var byTier = new Dictionary<string, long>(StringComparer.Ordinal);
        long total = 0;
        foreach (var (p, t, n) in rows)
        {
            total += n;
            byProject[p] = byProject.GetValueOrDefault(p) + n;
            byTier[t] = byTier.GetValueOrDefault(t) + n;
        }
        return new RootDayBurn(root, total, byProject, byTier);
    }

    private static HashSet<string> Roots(params string[] names) => new(names, StringComparer.Ordinal);

    private static string Iso(DateTimeOffset t)
        => t.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture);

    private static string SnapshotJson(DateTimeOffset capturedAt, string status = "ok", string? errorKind = null) => $$"""
        {"schema_version":1,"writer_version":"3.4.0","captured_at":"{{Iso(capturedAt)}}",
         "account_ref":"d6ab2208","plan":"Max 20x","status":"{{status}}","error_kind":{{(errorKind is null ? "null" : $"\"{errorKind}\"")}},
         "tiers":[
           {"key":"five_hour","utilization":42,"resets_at":"{{Iso(capturedAt.AddHours(3))}}","used":null,"limit":null},
           {"key":"seven_day","utilization":62,"resets_at":"{{Iso(capturedAt.AddDays(5))}}","used":null,"limit":null}
         ]}
        """;

    // -- payload shape ----------------------------------------------------------

    [Fact]
    public void Sanduhr_format_posts_the_snapshot_object_with_no_action_wrapper()
    {
        var burn = new[] { Burn(".claude-personal", ("Sanduhr", "seven_day_fable", 1200)) };
        var payload = UsagePublisher.BuildPayload(Day, "DESKTOP-X", burn, Roots(".claude-personal"),
            PublishQuota.NoData, PublishBodyFormat.Sanduhr);
        var wire = (JsonObject)JsonNode.Parse(payload.ToJsonString())!;

        Assert.Null(wire["action"]);
        Assert.Equal(new[] { "date", "source", "machine", "homes", "totals", "byTier", "byProject", "caveat", "quota" },
            wire.Select(p => p.Key).ToArray());
        Assert.Equal("2026-09-12", (string?)wire["date"]);
        Assert.Equal("sanduhr", (string?)wire["source"]);
        Assert.Equal(1200, wire["totals"]!["tokens"]!.GetValue<long>());
        Assert.Equal("no_data", (string?)wire["quota"]!["status"]);
    }

    [Fact]
    public void Default_format_is_sanduhr()
    {
        var payload = UsagePublisher.BuildPayload(Day, "m", Array.Empty<RootDayBurn>(), Roots(), null);
        Assert.Null(payload["action"]);
        Assert.Equal("2026-09-12", (string?)payload["date"]);
    }

    [Fact]
    public void Labs626_format_matches_the_manage_usage_record_contract()
    {
        var burn = new[]
        {
            Burn(".claude-personal", ("Sanduhr", "seven_day_fable", 1200), ("626Labs", "seven_day_sonnet", 300)),
        };
        var quota = new PublishQuota("ok", Iso(Now), new[]
        {
            new PublishQuotaTier("seven_day", 62, Iso(Now.AddDays(5))),
        });

        var payload = UsagePublisher.BuildPayload(Day, "DESKTOP-X", burn, Roots(".claude-personal"), quota, PublishBodyFormat.Labs626);
        // Round-trip through the serializer: the wire shape is what the dashboard sees.
        var wire = (JsonObject)JsonNode.Parse(payload.ToJsonString())!;

        Assert.Equal("record", (string?)wire["action"]);
        Assert.Equal("action", wire.First().Key);
        Assert.Equal("2026-09-12", (string?)wire["date"]);
        Assert.Equal("sanduhr", (string?)wire["source"]);
        Assert.Equal("DESKTOP-X", (string?)wire["machine"]);
        Assert.Equal(new[] { ".claude-personal" }, ((JsonArray)wire["homes"]!).Select(n => (string?)n).ToArray());
        Assert.Equal(1500, wire["totals"]!["tokens"]!.GetValue<long>());
        Assert.Equal(1200, wire["byTier"]!["seven_day_fable"]!.GetValue<long>());
        Assert.Equal(300, wire["byTier"]!["seven_day_sonnet"]!.GetValue<long>());

        var projects = (JsonArray)wire["byProject"]!;
        Assert.Equal(2, projects.Count);
        Assert.Equal("Sanduhr", (string?)projects[0]!["name"]);   // desc by tokens
        Assert.Equal(1200, projects[0]!["tokens"]!.GetValue<long>());
        Assert.Equal("626Labs", (string?)projects[1]!["name"]);

        Assert.Equal("ok", (string?)wire["quota"]!["status"]);
        Assert.Equal(62, wire["quota"]!["tiers"]![0]!["utilizationPct"]!.GetValue<int>());
        Assert.Equal("seven_day", (string?)wire["quota"]!["tiers"]![0]!["key"]);
        Assert.Equal(UsagePublisher.Caveat, (string?)wire["caveat"]);
        Assert.Contains("lower bound", (string?)wire["caveat"]);
    }

    [Fact]
    public void Quota_is_omitted_when_null_and_present_as_no_data_when_unknown()
    {
        var none = UsagePublisher.BuildPayload(Day, "m", Array.Empty<RootDayBurn>(), Roots(), null);
        Assert.Null(none["quota"]);

        var noData = UsagePublisher.BuildPayload(Day, "m", Array.Empty<RootDayBurn>(), Roots(), PublishQuota.NoData);
        Assert.Equal("no_data", (string?)noData["quota"]!["status"]);
        Assert.Null(noData["quota"]!["asOf"]?.GetValue<string>());
        Assert.Empty((JsonArray)noData["quota"]!["tiers"]!);
    }

    // -- consent filter ---------------------------------------------------------

    [Fact]
    public void A_root_not_marked_shareable_never_appears_in_homes_or_projects()
    {
        var burn = new[]
        {
            Burn(".claude", ("wbp", "seven_day_opus", 9000), ("marcus-secret", "seven_day", 100)),
            Burn(".claude-personal", ("Sanduhr", "seven_day_fable", 50)),
        };

        var payload = UsagePublisher.BuildPayload(Day, "m", burn, Roots(".claude-personal"), null);
        string json = payload.ToJsonString();

        Assert.Equal(new[] { ".claude-personal" }, ((JsonArray)payload["homes"]!).Select(n => (string?)n).ToArray());
        Assert.Equal(50, payload["totals"]!["tokens"]!.GetValue<long>());
        Assert.Single((JsonArray)payload["byProject"]!);
        Assert.DoesNotContain("wbp", json);
        Assert.DoesNotContain("marcus-secret", json);
        Assert.DoesNotContain("seven_day_opus", json);
        Assert.DoesNotContain(".claude\"", json);
    }

    [Fact]
    public void Nothing_shareable_yields_an_empty_but_valid_payload()
    {
        var burn = new[] { Burn(".claude", ("x", "seven_day", 10)) };
        var payload = UsagePublisher.BuildPayload(Day, "m", burn, Roots(), null);
        Assert.Empty((JsonArray)payload["homes"]!);
        Assert.Equal(0, payload["totals"]!["tokens"]!.GetValue<long>());
        Assert.Empty((JsonArray)payload["byProject"]!);
    }

    [Fact]
    public void Projects_merge_across_shareable_roots_cap_at_25_and_clamp_names()
    {
        var rowsA = Enumerable.Range(0, 30).Select(i => ($"proj-{i:D2}", "seven_day", (long)(100 - i))).ToArray();
        var burn = new[]
        {
            Burn(".claude", rowsA),
            Burn(".claude-personal", ("proj-00", "seven_day", 5), (new string('n', 200), "seven_day", 1)),
        };
        var payload = UsagePublisher.BuildPayload(Day, "m", burn, Roots(".claude", ".claude-personal"), null);

        var projects = (JsonArray)payload["byProject"]!;
        Assert.Equal(UsagePublisher.MaxProjects, projects.Count);
        Assert.Equal("proj-00", (string?)projects[0]!["name"]);
        Assert.Equal(105, projects[0]!["tokens"]!.GetValue<long>());   // merged across roots
        Assert.All(projects, p => Assert.True(((string)p!["name"]!.GetValue<string>()).Length <= UsagePublisher.MaxProjectNameLength));
        Assert.Equal(2, ((JsonArray)payload["homes"]!).Count);
    }

    // -- quota staleness --------------------------------------------------------

    [Fact]
    public void Fresh_ok_snapshot_reads_as_ok_with_percentages()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "snapshot.json");
        File.WriteAllText(path, SnapshotJson(Now.AddMinutes(-2)));

        var q = UsagePublisher.ReadQuota(path, Now);

        Assert.Equal("ok", q.Status);
        Assert.NotNull(q.AsOf);
        Assert.Equal(2, q.Tiers.Count);
        Assert.Equal(42, q.Tiers[0].UtilizationPct);
        Assert.Equal(62, q.Tiers[1].UtilizationPct);
    }

    [Fact]
    public void Dead_snapshot_reads_as_stale_with_null_percentages_never_the_old_numbers()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "snapshot.json");
        File.WriteAllText(path, SnapshotJson(Now.AddDays(-48)));   // the 2026-09-13 case: weeks old

        var q = UsagePublisher.ReadQuota(path, Now);
        var payload = UsagePublisher.BuildPayload(Day, "m", Array.Empty<RootDayBurn>(), Roots(), q);

        Assert.Equal("stale", q.Status);
        Assert.All(q.Tiers, t => Assert.Null(t.UtilizationPct));
        Assert.Equal("stale", (string?)payload["quota"]!["status"]);
        Assert.All((JsonArray)payload["quota"]!["tiers"]!, t => Assert.Null(t!["utilizationPct"]?.GetValue<int>()));
    }

    [Fact]
    public void Fresh_but_errored_snapshot_is_stale()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "snapshot.json");
        File.WriteAllText(path, SnapshotJson(Now.AddMinutes(-1), status: "error", errorKind: "session_expired"));
        var q = UsagePublisher.ReadQuota(path, Now);
        Assert.Equal("stale", q.Status);
        Assert.All(q.Tiers, t => Assert.Null(t.UtilizationPct));
    }

    [Fact]
    public void Missing_or_malformed_snapshot_is_no_data()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "snapshot.json");
        Assert.Equal("no_data", UsagePublisher.ReadQuota(path, Now).Status);
        File.WriteAllText(path, "{ not json");
        Assert.Equal("no_data", UsagePublisher.ReadQuota(path, Now).Status);
    }

    [Fact]
    public void Default_date_is_yesterday_local()
    {
        var nowLocal = new DateTimeOffset(2026, 9, 13, 0, 10, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 13)));
        Assert.Equal(new DateOnly(2026, 9, 12), UsagePublisher.DefaultDate(nowLocal));
    }

    // -- PublishAsync over a fake handler ----------------------------------------

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string?> Bodies { get; } = new();
        public List<string?> AuthHeaders { get; } = new();
        public List<Dictionary<string, string[]>> Headers { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
            AuthHeaders.Add(request.Headers.Authorization?.ToString());
            Headers.Add(request.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
            return _responder(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static JsonObject SamplePayload(PublishBodyFormat format = PublishBodyFormat.Sanduhr)
        => UsagePublisher.BuildPayload(Day, "m", new[] { Burn(".claude-personal", ("Sanduhr", "seven_day", 1)) },
            Roots(".claude-personal"), null, format);

    private const string Url = "https://collector.example/usage";
    private static readonly PublishTarget Bearer = new(Url, PublishAuthScheme.Bearer, "Authorization");
    private static readonly PublishTarget Header = new(Url, PublishAuthScheme.Header, "X-Api-Key");
    private static readonly PublishTarget NoAuth = new(Url, PublishAuthScheme.None, "Authorization");

    [Fact]
    public async Task Publish_200_is_ok_and_posts_bearer_json_to_the_configured_endpoint()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"ok":true}"""));

        var r = await UsagePublisher.PublishAsync(SamplePayload(PublishBodyFormat.Labs626), Bearer, "sk-626-secret", handler);

        Assert.True(r.Ok);
        Assert.Equal(200, r.StatusCode);
        Assert.Equal("ok", r.Status);
        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal(Url, req.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer sk-626-secret", handler.AuthHeaders[0]);
        Assert.Equal("application/json", req.Content!.Headers.ContentType!.MediaType);
        var body = (JsonObject)JsonNode.Parse(handler.Bodies[0]!)!;
        Assert.Equal("record", (string?)body["action"]);
        Assert.Equal("2026-09-12", (string?)body["date"]);
    }

    [Fact]
    public async Task Header_scheme_sends_the_token_as_the_named_header_and_no_Authorization()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));

        var r = await UsagePublisher.PublishAsync(SamplePayload(), Header, "tok-123", handler);

        Assert.True(r.Ok);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("tok-123", Assert.Single(handler.Headers[0]["X-Api-Key"]));
        Assert.Null(handler.AuthHeaders[0]);
        var body = (JsonObject)JsonNode.Parse(handler.Bodies[0]!)!;
        Assert.Null(body["action"]);
        Assert.Equal("2026-09-12", (string?)body["date"]);
    }

    [Fact]
    public async Task None_scheme_sends_no_credential_and_needs_no_token()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));

        var r = await UsagePublisher.PublishAsync(SamplePayload(), NoAuth, null, handler);

        Assert.True(r.Ok);
        var req = Assert.Single(handler.Requests);
        Assert.Null(handler.AuthHeaders[0]);
        Assert.DoesNotContain(handler.Headers[0].Keys, k => k.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task None_scheme_never_leaks_a_stored_token()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));
        await UsagePublisher.PublishAsync(SamplePayload(), NoAuth, "leftover-secret", handler);
        Assert.DoesNotContain(handler.Headers[0], h => h.Value.Any(v => v.Contains("leftover-secret")));
    }

    [Fact]
    public async Task Publish_401_is_a_token_error_that_never_echoes_the_token()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Unauthorized, """{"error":"unauthorized"}"""));
        var r = await UsagePublisher.PublishAsync(SamplePayload(), Bearer, "sk-626-secret", handler);
        Assert.False(r.Ok);
        Assert.Equal(401, r.StatusCode);
        Assert.Contains("token", r.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-626-secret", r.Message);
        Assert.DoesNotContain("626", r.Message);   // the message is vendor-agnostic
    }

    [Fact]
    public async Task Publish_429_is_rate_limited()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.TooManyRequests, "{}"));
        var r = await UsagePublisher.PublishAsync(SamplePayload(), Bearer, "k", handler);
        Assert.False(r.Ok);
        Assert.Equal(429, r.StatusCode);
        Assert.Contains("Rate limited", r.Message);
    }

    [Fact]
    public async Task Publish_400_carries_the_validation_message()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.BadRequest, """{"error":"date must be YYYY-MM-DD"}"""));
        var r = await UsagePublisher.PublishAsync(SamplePayload(), Bearer, "k", handler);
        Assert.False(r.Ok);
        Assert.Equal(400, r.StatusCode);
        Assert.Contains("date must be", r.Message);
    }

    [Fact]
    public async Task Publish_network_failure_is_status_zero_not_an_exception()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("boom"));
        var r = await UsagePublisher.PublishAsync(SamplePayload(), Bearer, "k", handler);
        Assert.False(r.Ok);
        Assert.Equal(0, r.StatusCode);
        Assert.Contains("Network error", r.Message);
    }

    [Theory]
    [InlineData(PublishAuthScheme.Bearer)]
    [InlineData(PublishAuthScheme.Header)]
    public async Task Publish_without_a_token_never_hits_the_network_when_the_scheme_needs_one(PublishAuthScheme scheme)
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));
        var r = await UsagePublisher.PublishAsync(SamplePayload(), new PublishTarget(Url, scheme, "X-Key"), "  ", handler);
        Assert.False(r.Ok);
        Assert.Equal(0, r.StatusCode);
        Assert.Contains("token", r.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("ftp://collector.example/usage")]
    [InlineData("/relative/path")]
    public async Task Publish_without_a_usable_endpoint_never_hits_the_network(string url)
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));
        var r = await UsagePublisher.PublishAsync(SamplePayload(), new PublishTarget(url, PublishAuthScheme.Bearer, "Authorization"), "k", handler);
        Assert.False(r.Ok);
        Assert.Equal(0, r.StatusCode);
        Assert.Contains("endpoint", r.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Endpoint_validation_accepts_http_and_https_absolute_urls_only()
    {
        Assert.True(UsagePublisher.IsUsableEndpoint("https://collector.example/usage"));
        Assert.True(UsagePublisher.IsUsableEndpoint("http://127.0.0.1:8080/ingest"));
        Assert.False(UsagePublisher.IsUsableEndpoint(""));
        Assert.False(UsagePublisher.IsUsableEndpoint("file:///c:/x.json"));
        Assert.False(UsagePublisher.IsUsableEndpoint("collector.example/usage"));
    }
}
