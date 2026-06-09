using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Parity tests for Core/TierModel.cs — the model-level intents ported from
/// the Python build's tier logic (widget.py registry + fetcher.py Routines
/// synthesis + history_chart.py speculative-tier set), plus the non-rendering
/// assertions from test_tier_card.py / test_fetcher.py. Rendering is item 5
/// and out of scope here.
/// </summary>
public class TierModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // -- canonical registry ---------------------------------------------------

    [Fact]
    public void CanonicalOrder_matches_python_registry()
    {
        var expected = new[]
        {
            "five_hour",
            "seven_day",
            "seven_day_sonnet",
            "seven_day_opus",
            "seven_day_cowork",
            "seven_day_omelette",
            "seven_day_oauth_apps",
            "iguana_necktie",
            "extra_usage",
            "routines",
        };
        Assert.Equal(expected, TierModel.CanonicalOrder);
    }

    [Theory]
    [InlineData("five_hour", "Session (5hr)")]
    [InlineData("seven_day", "Weekly - All Models")]
    [InlineData("seven_day_sonnet", "Weekly - Sonnet")]
    [InlineData("seven_day_opus", "Weekly - Opus")]
    [InlineData("seven_day_cowork", "Weekly - Cowork")]
    [InlineData("seven_day_omelette", "Weekly - Design")]
    [InlineData("seven_day_oauth_apps", "Weekly - OAuth Apps")]
    [InlineData("iguana_necktie", "Weekly - Special")]
    [InlineData("extra_usage", "Capped Extra Usage")]
    [InlineData("routines", "Daily Routines")]
    public void Label_maps_each_tier(string key, string label)
        => Assert.Equal(label, TierModel.Label(key));

    [Fact]
    public void IsKnown_distinguishes_registered_keys()
    {
        Assert.True(TierModel.IsKnown("five_hour"));
        Assert.False(TierModel.IsKnown("bogus_tier"));
    }

    [Fact]
    public void Label_throws_for_unknown_key()
        => Assert.Throws<KeyNotFoundException>(() => TierModel.Label("bogus_tier"));

    // -- speculative tiers + future-use tag -----------------------------------

    [Fact]
    public void SpeculativeTiers_matches_python_set()
    {
        var expected = new HashSet<string>
        {
            "seven_day_cowork",
            "seven_day_omelette",
            "seven_day_oauth_apps",
            "iguana_necktie",
        };
        Assert.Equal(expected, TierModel.SpeculativeTiers);
    }

    [Theory]
    [InlineData("seven_day_cowork", true)]
    [InlineData("seven_day_omelette", true)]
    [InlineData("seven_day_oauth_apps", true)]
    [InlineData("iguana_necktie", true)]
    [InlineData("five_hour", false)]
    [InlineData("seven_day", false)]
    [InlineData("extra_usage", false)]
    [InlineData("routines", false)]
    public void IsSpeculative_flags_codename_and_gated_tiers(string key, bool speculative)
        => Assert.Equal(speculative, TierModel.IsSpeculative(key));

    [Fact]
    public void LabelWithTag_appends_future_use_for_speculative_tiers()
    {
        // settings_dialog.py: f"{label}  ·  future use" (two spaces, middle
        // dot U+00B7, two spaces). Built ASCII-side to stay encoding-clean.
        var expected = "Weekly - Cowork  " + (char)0xB7 + "  future use";
        Assert.Equal(expected, TierModel.LabelWithTag("seven_day_cowork"));
    }

    [Fact]
    public void LabelWithTag_leaves_normal_tiers_untagged()
    {
        Assert.Equal("Session (5hr)", TierModel.LabelWithTag("five_hour"));
        Assert.Equal("Capped Extra Usage", TierModel.LabelWithTag("extra_usage"));
    }

    // -- Routines synthesis (fetcher.py parity) -------------------------------

    [Fact]
    public void SynthesizeRoutines_builds_count_card()
    {
        var b = TierModel.SynthesizeRoutines(3, 15);
        Assert.NotNull(b);
        Assert.Equal(3, b!.Used);
        Assert.Equal(15, b.Limit);
        Assert.Equal(20.0, b.Utilization, 6); // 3/15 -> 20%
        Assert.Equal("3/15", b.CountLabel);
    }

    [Fact]
    public void SynthesizeRoutines_returns_null_when_limit_unavailable()
    {
        // Account without Routines: budget limit 0 (endpoint 404) -> no tier,
        // mirroring fetcher's `if budget and budget.get("limit", 0) > 0`.
        Assert.Null(TierModel.SynthesizeRoutines(0, 0));
    }

    // -- value-label derivation the card consumes -----------------------------

    [Fact]
    public void ValueLabel_percent_for_standard_tiers()
    {
        Assert.Equal("0%", TierModel.ValueLabel("five_hour", 0));
        Assert.Equal("42%", TierModel.ValueLabel("five_hour", 42));
    }

    [Fact]
    public void ValueLabel_count_for_routines_with_counts()
        => Assert.Equal("3/15", TierModel.ValueLabel("routines", 20, used: 3, limit: 15));

    [Fact]
    public void ValueLabel_routines_falls_back_to_percent_without_counts()
        => Assert.Equal("20%", TierModel.ValueLabel("routines", 20));

    // -- reset-countdown derivation -------------------------------------------

    [Fact]
    public void ResetCountdown_formats_future_reset()
    {
        var resetsAt = "2026-01-01T02:00:00Z"; // Now + 2h
        Assert.Equal("Resets in 2h", TierModel.ResetCountdown(resetsAt, Now));
    }

    [Fact]
    public void ResetCountdown_empty_when_missing()
    {
        Assert.Equal("", TierModel.ResetCountdown(null, Now));
        Assert.Equal("", TierModel.ResetCountdown("", Now));
    }

    // -- hide / reorder model -------------------------------------------------

    [Fact]
    public void ResolveOrder_defaults_to_canonical()
        => Assert.Equal(TierModel.CanonicalOrder, TierModel.ResolveOrder(null));

    [Fact]
    public void ResolveOrder_honors_saved_order_then_appends_remainder()
    {
        var saved = new[] { "routines", "five_hour", "bogus_key", "five_hour" };
        var resolved = TierModel.ResolveOrder(saved);
        // Saved valid keys first (deduped, unknown dropped)...
        Assert.Equal("routines", resolved[0]);
        Assert.Equal("five_hour", resolved[1]);
        // ...then the rest of the canonical registry, nothing lost or doubled.
        Assert.Equal(TierModel.CanonicalOrder.Count, resolved.Count);
        Assert.Equal(TierModel.CanonicalOrder.Count, resolved.Distinct().Count());
        Assert.DoesNotContain("bogus_key", resolved);
    }

    [Fact]
    public void ActiveTiers_keeps_data_bearing_tiers_in_order()
    {
        var util = new Dictionary<string, double?>
        {
            ["five_hour"] = 42.0,
            ["seven_day"] = null,        // present but no data -> no card
            ["seven_day_sonnet"] = 10.0,
            ["routines"] = 0.0,          // 0 is data (not null) -> still a card
            ["bogus_key"] = 99.0,        // unknown -> never appears
        };
        var active = TierModel.ActiveTiers(util);
        Assert.Equal(new[] { "five_hour", "seven_day_sonnet", "routines" }, active);
    }

    [Fact]
    public void ActiveTiers_drops_hidden_tiers()
    {
        var util = new Dictionary<string, double?>
        {
            ["five_hour"] = 42.0,
            ["seven_day_sonnet"] = 10.0,
            ["routines"] = 0.0,
        };
        var hidden = new HashSet<string> { "seven_day_sonnet" };
        var active = TierModel.ActiveTiers(util, savedOrder: null, hiddenTiers: hidden);
        Assert.Equal(new[] { "five_hour", "routines" }, active);
    }
}
