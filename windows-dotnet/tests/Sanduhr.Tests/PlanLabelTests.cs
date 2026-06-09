using System.Text.Json;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Parity tests for Core/PlanLabel.cs — ported from the Python build's
/// test_plan.py (PR #25). Pure-function coverage of the subscription-tier
/// mapping plus a redacted real-payload fixture so the parse is pinned to an
/// actual /api/organizations response shape.
/// </summary>
public class PlanLabelTests
{
    // The visible badge name for Max x20. Built from the explicit U+00D7
    // codepoint (the multiplication sign) so this file stays pure-ASCII and
    // encoding-independent — it also proves PlanLabel emits the exact
    // codepoint at runtime rather than a mojibake'd literal.
    private static readonly string Max20x = "Max " + (char)0xD7 + "20";

    [Fact]
    public void Max20x_subscription()
    {
        var b = PlanLabel.Resolve("default_claude_max_20x", "stripe_subscription", new[] { "claude_max", "chat" });
        Assert.NotNull(b);
        Assert.Equal(Max20x, b!.Name);
        Assert.Contains("Galaxy Brain Max", b.Riffs);
        Assert.True(b.Riffs.Count >= 2);
    }

    [Fact]
    public void Max5x_subscription()
    {
        var b = PlanLabel.Resolve("default_claude_max_5x", "stripe_subscription", new[] { "claude_max" });
        Assert.NotNull(b);
        Assert.Equal("Max", b!.Name);
        Assert.Contains("Maximum Effort", b.Riffs);
    }

    [Fact]
    public void Pro_subscription()
    {
        var b = PlanLabel.Resolve("default_claude_pro", "stripe_subscription", new[] { "claude_pro", "chat" });
        Assert.NotNull(b);
        Assert.Equal("Pro", b!.Name);
        Assert.Empty(b.Riffs);
    }

    [Fact]
    public void Team_subscription()
    {
        var b = PlanLabel.Resolve("default_claude_team", "stripe_subscription", new[] { "claude_team" });
        Assert.NotNull(b);
        Assert.Equal("Team", b!.Name);
        Assert.Empty(b.Riffs);
    }

    [Fact]
    public void Prepaid_api_org_returns_null()
        => Assert.Null(PlanLabel.Resolve("auto_prepaid_tier_0", "prepaid", new[] { "api", "api_individual" }));

    [Fact]
    public void Unknown_subscription_tier_returns_null()
        => Assert.Null(PlanLabel.Resolve("some_future_tier", "stripe_subscription", new[] { "chat" }));

    [Fact]
    public void Missing_tier_returns_null()
        => Assert.Null(PlanLabel.Resolve(null));

    [Fact]
    public void Empty_tier_returns_null()
        => Assert.Null(PlanLabel.Resolve(""));

    [Fact]
    public void Non_subscription_billing_blocks_badge()
        // Even with "max" in the string, a non-subscription billing type must
        // never render a subscription badge.
        => Assert.Null(PlanLabel.Resolve("weird_max_thing", "prepaid", new[] { "api" }));

    [Fact]
    public void Lenient_when_billing_absent()
    {
        // Older callers may pass only the tier string — best-effort parse.
        var b = PlanLabel.Resolve("default_claude_max_20x");
        Assert.NotNull(b);
        Assert.Equal(Max20x, b!.Name);
    }

    [Fact]
    public void Riffs_are_copies_not_shared_state()
    {
        var a = PlanLabel.Resolve("default_claude_max_20x", "stripe_subscription", new[] { "claude_max" });
        var b = PlanLabel.Resolve("default_claude_max_20x", "stripe_subscription", new[] { "claude_max" });
        Assert.NotNull(a);
        Assert.NotNull(b);
        // Each call hands back a distinct list, never an alias of the module's
        // internal template — so a caller mutating one can't corrupt another.
        Assert.False(ReferenceEquals(a!.Riffs, b!.Riffs));
        ((List<string>)a.Riffs).Add("MUTATED");
        Assert.DoesNotContain("MUTATED", b!.Riffs);
    }

    [Fact]
    public void Real_payload_fixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "organizations_sample.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var orgs = doc.RootElement;

        JsonElement sub = default, api = default;
        foreach (var org in orgs.EnumerateArray())
        {
            var billing = org.GetProperty("billing_type").GetString();
            if (billing == "stripe_subscription") sub = org;
            else if (billing == "prepaid") api = org;
        }

        var subBadge = PlanLabel.Resolve(
            sub.GetProperty("rate_limit_tier").GetString(),
            sub.GetProperty("billing_type").GetString(),
            sub.GetProperty("capabilities").EnumerateArray().Select(e => e.GetString()!).ToArray());
        Assert.NotNull(subBadge);
        Assert.Equal(Max20x, subBadge!.Name);

        var apiBadge = PlanLabel.Resolve(
            api.GetProperty("rate_limit_tier").GetString(),
            api.GetProperty("billing_type").GetString(),
            api.GetProperty("capabilities").EnumerateArray().Select(e => e.GetString()!).ToArray());
        Assert.Null(apiBadge);
    }
}
