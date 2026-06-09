using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Parity tests for Core/ClaudeApiClient.cs — ported from the Python build's
/// test_api.py (PR #25 version, which adds the rate_limit_tier / billing_type /
/// capabilities capture). cloudscraper's HTTP is replaced with an injected stub
/// <see cref="HttpMessageHandler"/> serving canned responses — no live network,
/// exactly as the Python tests swap cloudscraper for a requests-mock session.
/// </summary>
public class ClaudeApiClientTests
{
    private const string OrgsUrl = "https://claude.ai/api/organizations";
    private const string RoutinesUrl = "https://claude.ai/v1/code/routines/run-budget";
    private static string UsageUrl(string orgId) => $"https://claude.ai/api/organizations/{orgId}/usage";

    // -- stub handler ---------------------------------------------------------

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string?> CookieHeaders { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            CookieHeaders.Add(request.Headers.TryGetValues("Cookie", out var v) ? string.Join("; ", v) : null);
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Text(HttpStatusCode code, string body)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "text/html") };

    // -- happy path -----------------------------------------------------------

    [Fact]
    public async Task GetUsage_happy_path()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsoluteUri switch
        {
            OrgsUrl => Json("[{\"uuid\":\"org-123\"}]"),
            var u when u == UsageUrl("org-123") =>
                Json("{\"five_hour\":{\"utilization\":42,\"resets_at\":\"2030-01-01T00:00:00Z\"}}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var client = new ClaudeApiClient("abc", innerHandler: handler);

        var result = await client.GetUsageAsync();

        Assert.Equal(42, result["five_hour"]!["utilization"]!.GetValue<int>());
    }

    [Fact]
    public async Task GetUsage_caches_org_id()
    {
        int orgCalls = 0;
        var handler = new StubHandler(req =>
        {
            var u = req.RequestUri!.AbsoluteUri;
            if (u == OrgsUrl) { orgCalls++; return Json("[{\"uuid\":\"org-x\"}]"); }
            if (u == UsageUrl("org-x")) return Json("{}");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var client = new ClaudeApiClient("abc", innerHandler: handler);

        await client.GetUsageAsync();
        await client.GetUsageAsync();
        await client.GetUsageAsync();

        Assert.Equal(1, orgCalls);
    }

    // -- typed error mapping --------------------------------------------------

    [Fact]
    public async Task Status401_throws_session_expired()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = new ClaudeApiClient("abc", innerHandler: handler);
        await Assert.ThrowsAsync<SessionExpiredException>(() => client.GetUsageAsync());
    }

    [Fact]
    public async Task Status403_with_cloudflare_body_throws_cloudflare_blocked()
    {
        var handler = new StubHandler(_ => Text(HttpStatusCode.Forbidden,
            "<html><title>Just a moment...</title><body>cf-challenge</body></html>"));
        using var client = new ClaudeApiClient("abc", innerHandler: handler);
        await Assert.ThrowsAsync<CloudflareBlockedException>(() => client.GetUsageAsync());
    }

    [Fact]
    public async Task Status403_without_cloudflare_signature_is_session_expired()
    {
        var handler = new StubHandler(_ => Text(HttpStatusCode.Forbidden, "{}"));
        using var client = new ClaudeApiClient("abc", innerHandler: handler);
        await Assert.ThrowsAsync<SessionExpiredException>(() => client.GetUsageAsync());
    }

    [Fact]
    public async Task Status5xx_throws_network_error()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        using var client = new ClaudeApiClient("abc", innerHandler: handler);
        await Assert.ThrowsAsync<NetworkException>(() => client.GetUsageAsync());
    }

    [Fact]
    public async Task No_orgs_throws_network_error()
    {
        var handler = new StubHandler(_ => Json("[]"));
        using var client = new ClaudeApiClient("abc", innerHandler: handler);
        await Assert.ThrowsAsync<NetworkException>(() => client.GetUsageAsync());
    }

    [Fact]
    public async Task NonJson_orgs_throws_network_error()
    {
        var handler = new StubHandler(_ => Text(HttpStatusCode.OK, "<not json>"));
        using var client = new ClaudeApiClient("abc", innerHandler: handler);
        await Assert.ThrowsAsync<NetworkException>(() => client.GetUsageAsync());
    }

    [Fact]
    public async Task NonJson_usage_throws_network_error()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsoluteUri == OrgsUrl
            ? Json("[{\"uuid\":\"org-x\"}]")
            : Text(HttpStatusCode.OK, "<not json>"));
        using var client = new ClaudeApiClient("abc", innerHandler: handler);
        await Assert.ThrowsAsync<NetworkException>(() => client.GetUsageAsync());
    }

    // -- cookie auth ----------------------------------------------------------

    [Fact]
    public async Task Cookie_header_includes_session_key()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsoluteUri == OrgsUrl
            ? Json("[{\"uuid\":\"org-123\"}]")
            : Json("{}"));
        using var client = new ClaudeApiClient("the-key", innerHandler: handler);

        await client.GetUsageAsync();

        Assert.All(handler.CookieHeaders, c => Assert.Contains("sessionKey=the-key", c));
    }

    [Fact]
    public async Task Cookie_header_includes_cf_clearance_when_provided()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsoluteUri == OrgsUrl
            ? Json("[{\"uuid\":\"org-123\"}]")
            : Json("{}"));
        using var client = new ClaudeApiClient("sk", cfClearance: "cf-cookie", innerHandler: handler);

        await client.GetUsageAsync();

        var cookie = handler.CookieHeaders[0]!;
        Assert.Contains("sessionKey=sk", cookie);
        Assert.Contains("cf_clearance=cf-cookie", cookie);
    }

    // -- _account capture (PR #25) -------------------------------------------

    [Fact]
    public async Task GetUsage_attaches_account_plan_fields()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsoluteUri == OrgsUrl
            ? Json("""
                [{
                    "uuid": "org-123",
                    "rate_limit_tier": "default_claude_max_20x",
                    "billing_type": "stripe_subscription",
                    "capabilities": ["claude_max", "chat"]
                }]
                """)
            : Json("{\"five_hour\":{\"utilization\":42,\"resets_at\":null}}"));
        using var client = new ClaudeApiClient("abc", innerHandler: handler);

        var result = await client.GetUsageAsync();
        var account = result["_account"]!;

        Assert.Equal("default_claude_max_20x", (string?)account["rate_limit_tier"]);
        Assert.Equal("stripe_subscription", (string?)account["billing_type"]);
        Assert.Contains("claude_max", account["capabilities"]!.AsArray().Select(n => (string?)n));
        // Usage buckets pass through untouched alongside the reserved key.
        Assert.Equal(42, result["five_hour"]!["utilization"]!.GetValue<int>());

        // The typed Account property mirrors the injected node.
        Assert.NotNull(client.Account);
        Assert.Equal("default_claude_max_20x", client.Account!.RateLimitTier);
        Assert.Contains("claude_max", client.Account.Capabilities!);
    }

    [Fact]
    public async Task GetUsage_account_fields_default_none_when_absent()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsoluteUri == OrgsUrl
            ? Json("[{\"uuid\":\"org-x\"}]")
            : Json("{\"seven_day\":{\"utilization\":5}}"));
        using var client = new ClaudeApiClient("abc", innerHandler: handler);

        var result = await client.GetUsageAsync();

        // _account is attached but every field is null -> the badge stays hidden.
        Assert.Null(result["_account"]!["rate_limit_tier"]);
        Assert.Equal(5, result["seven_day"]!["utilization"]!.GetValue<int>());
    }

    [Fact]
    public async Task GetUsage_account_node_is_independent_per_call()
    {
        // A JsonNode can only have one parent — each GetUsage must clone the
        // captured _account or the second call would throw.
        var handler = new StubHandler(req => req.RequestUri!.AbsoluteUri == OrgsUrl
            ? Json("[{\"uuid\":\"org-x\",\"rate_limit_tier\":\"default_claude_pro\",\"billing_type\":\"stripe_subscription\"}]")
            : Json("{}"));
        using var client = new ClaudeApiClient("abc", innerHandler: handler);

        var first = await client.GetUsageAsync();
        var second = await client.GetUsageAsync();

        Assert.False(ReferenceEquals(first["_account"], second["_account"]));
        Assert.Equal("default_claude_pro", (string?)second["_account"]!["rate_limit_tier"]);
    }

    // -- Routines budget ------------------------------------------------------

    [Fact]
    public async Task RoutineBudget_happy_path_returns_used_limit()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsoluteUri switch
        {
            OrgsUrl => Json("[{\"uuid\":\"org-r\"}]"),
            RoutinesUrl => Json("{\"used\":3,\"limit\":15}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var client = new ClaudeApiClient("abc", innerHandler: handler);

        var budget = await client.GetRoutineBudgetAsync();

        Assert.NotNull(budget);
        Assert.Equal(3, budget!.Used);
        Assert.Equal(15, budget.Limit);
    }

    [Fact]
    public async Task RoutineBudget_404_returns_null()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsoluteUri == OrgsUrl
            ? Json("[{\"uuid\":\"org-r\"}]")
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new ClaudeApiClient("abc", innerHandler: handler);

        Assert.Null(await client.GetRoutineBudgetAsync());
    }

    [Fact]
    public async Task RoutineBudget_non_json_returns_null()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsoluteUri == OrgsUrl
            ? Json("[{\"uuid\":\"org-r\"}]")
            : Text(HttpStatusCode.OK, "<not json>"));
        using var client = new ClaudeApiClient("abc", innerHandler: handler);

        Assert.Null(await client.GetRoutineBudgetAsync());
    }

    [Fact]
    public async Task RoutineBudget_sends_anthropic_and_org_headers()
    {
        HttpRequestMessage? routineReq = null;
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.AbsoluteUri == OrgsUrl) return Json("[{\"uuid\":\"org-r\"}]");
            routineReq = req;
            return Json("{\"used\":1,\"limit\":10}");
        });
        using var client = new ClaudeApiClient("abc", innerHandler: handler);

        await client.GetRoutineBudgetAsync();

        Assert.NotNull(routineReq);
        Assert.Equal("org-r", routineReq!.Headers.GetValues("x-organization-uuid").Single());
        Assert.Equal("web_claude_ai", routineReq.Headers.GetValues("anthropic-client-platform").Single());
        Assert.Equal("2023-06-01", routineReq.Headers.GetValues("anthropic-version").Single());
        Assert.Equal("ccr-triggers-2026-01-30", routineReq.Headers.GetValues("anthropic-beta").Single());
    }

    // -- Cloudflare-aware handler --------------------------------------------

    [Fact]
    public async Task CloudflareAwareHandler_adds_browser_headers()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(req =>
        {
            captured = req;
            return req.RequestUri!.AbsoluteUri == OrgsUrl ? Json("[{\"uuid\":\"o\"}]") : Json("{}");
        });
        using var client = new ClaudeApiClient("abc", innerHandler: handler);

        await client.GetUsageAsync();

        Assert.NotNull(captured);
        // User-Agent is a structured header; GetValues tokenizes on the comma in
        // "(KHTML, like Gecko)", so assert against the joined value.
        Assert.Contains("Chrome", string.Join(" ", captured!.Headers.GetValues("User-Agent")));
        Assert.True(captured.Headers.Contains("Sec-Fetch-Site"));
        Assert.True(captured.Headers.Contains("sec-ch-ua-platform"));
    }

    [Theory]
    [InlineData("<title>Just a moment...</title>", true)]
    [InlineData("Attention Required! | Cloudflare", true)]
    [InlineData("<div id=cf-challenge>", true)]
    [InlineData("{\"five_hour\": {}}", false)]
    [InlineData("", false)]
    public void LooksLikeCloudflare_detects_challenge_markup(string body, bool expected)
        => Assert.Equal(expected, ClaudeApiClient.LooksLikeCloudflare(body));

    // -- fixture parity (reuse item 2's organizations_sample.json) ------------

    [Fact]
    public async Task OrgDiscovery_captures_plan_from_redacted_fixture()
    {
        var fixture = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "organizations_sample.json"));
        var handler = new StubHandler(req => req.RequestUri!.AbsoluteUri == OrgsUrl
            ? Json(fixture)
            : Json("{\"five_hour\":{\"utilization\":12}}"));
        using var client = new ClaudeApiClient("abc", innerHandler: handler);

        var result = await client.GetUsageAsync();

        // Discovery selects the first org (the Max x20 stripe subscription).
        Assert.Equal("default_claude_max_20x", client.Account!.RateLimitTier);
        Assert.Equal("stripe_subscription", client.Account.BillingType);
        Assert.Equal("default_claude_max_20x", (string?)result["_account"]!["rate_limit_tier"]);
        // And the captured fields resolve to the expected badge via item 2's PlanLabel.
        var badge = PlanLabel.Resolve(
            client.Account.RateLimitTier, client.Account.BillingType, client.Account.Capabilities);
        Assert.NotNull(badge);
        Assert.Equal("Max " + (char)0xD7 + "20", badge!.Name);
    }
}
