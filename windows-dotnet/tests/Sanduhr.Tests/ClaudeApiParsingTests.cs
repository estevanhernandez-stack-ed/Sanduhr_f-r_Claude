using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Direct unit tests for the shared <see cref="ClaudeApiParsing"/> helpers — the
/// pure org/usage/routine parsing + status→error mapping extracted from
/// <c>ClaudeApiClient</c> so the HttpClient and WebView2 transports share one
/// tested implementation. The existing <c>ClaudeApiClientTests</c> cover these
/// transitively through the HttpClient path; these pin the helpers directly,
/// including the WebView2-only <see cref="ClaudeApiParsing.ThrowForStatus"/>.
///
/// <see cref="ClaudeApiParsing"/> is internal; visible here via Core's
/// <c>InternalsVisibleTo("Sanduhr.Tests")</c>.
/// </summary>
public class ClaudeApiParsingTests
{
    // -- ParseOrganizations ---------------------------------------------------

    [Fact]
    public void ParseOrganizations_captures_uuid_and_plan_fields()
    {
        var d = ClaudeApiParsing.ParseOrganizations("""
            [{
                "uuid": "org-123",
                "rate_limit_tier": "default_claude_max_20x",
                "billing_type": "stripe_subscription",
                "capabilities": ["claude_max", "chat"]
            }]
            """);

        Assert.Equal("org-123", d.OrgId);
        Assert.Equal("default_claude_max_20x", d.Account.RateLimitTier);
        Assert.Equal("stripe_subscription", d.Account.BillingType);
        Assert.Contains("claude_max", d.Account.Capabilities!);
        Assert.Equal("default_claude_max_20x", (string?)d.AccountNode["rate_limit_tier"]);
    }

    [Fact]
    public void ParseOrganizations_selects_first_org()
    {
        var d = ClaudeApiParsing.ParseOrganizations(
            "[{\"uuid\":\"first\"},{\"uuid\":\"second\"}]");
        Assert.Equal("first", d.OrgId);
    }

    [Fact]
    public void ParseOrganizations_absent_plan_fields_are_null()
    {
        var d = ClaudeApiParsing.ParseOrganizations("[{\"uuid\":\"org-x\"}]");
        Assert.Null(d.Account.RateLimitTier);
        Assert.Null(d.Account.BillingType);
        Assert.Null(d.Account.Capabilities);
        Assert.Null(d.AccountNode["rate_limit_tier"]);
    }

    [Fact]
    public void ParseOrganizations_non_json_throws_network()
        => Assert.Throws<NetworkException>(() => ClaudeApiParsing.ParseOrganizations("<not json>"));

    [Fact]
    public void ParseOrganizations_non_array_throws_network()
        => Assert.Throws<NetworkException>(() => ClaudeApiParsing.ParseOrganizations("{\"uuid\":\"x\"}"));

    [Fact]
    public void ParseOrganizations_empty_array_throws_network()
        => Assert.Throws<NetworkException>(() => ClaudeApiParsing.ParseOrganizations("[]"));

    [Fact]
    public void ParseOrganizations_missing_uuid_throws_network()
        => Assert.Throws<NetworkException>(() => ClaudeApiParsing.ParseOrganizations("[{\"name\":\"no uuid\"}]"));

    // -- ParseUsage -----------------------------------------------------------

    [Fact]
    public void ParseUsage_injects_account_node_clone()
    {
        var accountNode = new JsonObject
        {
            ["rate_limit_tier"] = "default_claude_pro",
            ["billing_type"] = "stripe_subscription",
        };

        var first = ClaudeApiParsing.ParseUsage("{\"five_hour\":{\"utilization\":42}}", accountNode);
        var second = ClaudeApiParsing.ParseUsage("{\"five_hour\":{\"utilization\":7}}", accountNode);

        Assert.Equal(42, first["five_hour"]!["utilization"]!.GetValue<int>());
        Assert.Equal("default_claude_pro", (string?)first["_account"]!["rate_limit_tier"]);
        // A JsonNode can only have one parent — each call must clone the node.
        Assert.False(ReferenceEquals(first["_account"], second["_account"]));
        Assert.Equal("default_claude_pro", (string?)second["_account"]!["rate_limit_tier"]);
    }

    [Fact]
    public void ParseUsage_without_account_node_omits_key()
    {
        var data = ClaudeApiParsing.ParseUsage("{\"seven_day\":{\"utilization\":5}}", null);
        Assert.Null(data["_account"]);
        Assert.Equal(5, data["seven_day"]!["utilization"]!.GetValue<int>());
    }

    [Fact]
    public void ParseUsage_non_json_throws_network()
        => Assert.Throws<NetworkException>(() => ClaudeApiParsing.ParseUsage("<not json>", null));

    [Fact]
    public void ParseUsage_non_object_throws_network()
        => Assert.Throws<NetworkException>(() => ClaudeApiParsing.ParseUsage("[1,2,3]", null));

    // -- ParseRoutineBudget ---------------------------------------------------

    [Fact]
    public void ParseRoutineBudget_happy_path()
    {
        var b = ClaudeApiParsing.ParseRoutineBudget("{\"used\":3,\"limit\":15}");
        Assert.NotNull(b);
        Assert.Equal(3, b!.Used);
        Assert.Equal(15, b.Limit);
    }

    [Fact]
    public void ParseRoutineBudget_missing_fields_default_zero()
    {
        var b = ClaudeApiParsing.ParseRoutineBudget("{}");
        Assert.NotNull(b);
        Assert.Equal(0, b!.Used);
        Assert.Equal(0, b.Limit);
    }

    [Fact]
    public void ParseRoutineBudget_non_json_returns_null()
        => Assert.Null(ClaudeApiParsing.ParseRoutineBudget("<not json>"));

    [Fact]
    public void ParseRoutineBudget_non_object_returns_null()
        => Assert.Null(ClaudeApiParsing.ParseRoutineBudget("[1,2,3]"));

    [Fact]
    public void ParseRoutineBudget_numeric_strings_parse()
    {
        // The live unified-billing body returns used/limit as numeric STRINGS,
        // which the old GetValue<double> path threw on (-> the routines-null bug).
        // Tolerant read handles strings as well as numbers.
        var b = ClaudeApiParsing.ParseRoutineBudget("{\"used\":\"3\",\"limit\":\"15\"}");
        Assert.NotNull(b);
        Assert.Equal(3, b!.Used);
        Assert.Equal(15, b.Limit);
    }

    [Fact]
    public void ParseRoutineBudget_garbage_value_falls_back_to_zero()
    {
        // Genuinely non-numeric values degrade to 0 (no throw, no null); a 0 limit
        // reads as "no routines" downstream rather than dropping the whole budget.
        var b = ClaudeApiParsing.ParseRoutineBudget("{\"used\":\"three\",\"limit\":15}");
        Assert.NotNull(b);
        Assert.Equal(0, b!.Used);
        Assert.Equal(15, b.Limit);
    }

    // -- ThrowForStatus (the WebView2-path status mapping) --------------------

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    public void ThrowForStatus_2xx_does_not_throw(int status)
        => ClaudeApiParsing.ThrowForStatus(status, "{}"); // no throw == pass

    [Fact]
    public void ThrowForStatus_401_is_session_expired()
        => Assert.Throws<SessionExpiredException>(() => ClaudeApiParsing.ThrowForStatus(401, null));

    [Fact]
    public void ThrowForStatus_403_with_cloudflare_body_is_cloudflare_blocked()
        => Assert.Throws<CloudflareBlockedException>(
            () => ClaudeApiParsing.ThrowForStatus(403, "<title>Just a moment...</title>"));

    [Fact]
    public void ThrowForStatus_403_without_cloudflare_body_is_session_expired()
        => Assert.Throws<SessionExpiredException>(() => ClaudeApiParsing.ThrowForStatus(403, "{}"));

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public void ThrowForStatus_5xx_is_network(int status)
        => Assert.Throws<NetworkException>(() => ClaudeApiParsing.ThrowForStatus(status, null));

    [Theory]
    [InlineData(404)]
    [InlineData(400)]
    [InlineData(429)]
    public void ThrowForStatus_other_4xx_is_network(int status)
        => Assert.Throws<NetworkException>(() => ClaudeApiParsing.ThrowForStatus(status, null));
}
