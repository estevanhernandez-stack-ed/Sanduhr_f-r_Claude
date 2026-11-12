using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class SourceBarTests
{
    private static SourcePercent S(string s, double p, bool stale = false) => new(s, p, stale);

    [Fact]
    public void Primary_fills_and_a_source_further_out_continues_the_bar()
    {
        var l = SourceBar.Layout("claude", [S("claude", 40), S("codex", 70)]);
        Assert.Equal(new BarSegment("claude", 0, 0.40, false), l.Primary);
        Assert.Equal(new BarSegment("codex", 0.40, 0.70, false), l.Beyond);
        Assert.Equal(new[] { new BarTick("codex", 0.70, false) }, l.Ticks);
    }

    [Fact]
    public void A_source_behind_the_primary_is_only_a_tick()
    {
        var l = SourceBar.Layout("claude", [S("claude", 80), S("codex", 30)]);
        Assert.Null(l.Beyond);
        Assert.Equal(new[] { new BarTick("codex", 0.30, false) }, l.Ticks);
    }

    [Fact]
    public void Equal_percents_draw_no_beyond_segment()
    {
        Assert.Null(SourceBar.Layout("claude", [S("claude", 50), S("codex", 50)]).Beyond);
    }

    [Fact]
    public void A_missing_primary_draws_the_highest_as_the_fill()
    {
        var l = SourceBar.Layout("claude", [S("codex", 25)]);
        Assert.Equal("codex", l.Primary!.Source);
        Assert.Empty(l.Ticks);
    }

    [Fact]
    public void Percents_clamp_and_staleness_carries()
    {
        var l = SourceBar.Layout("codex", [S("codex", 120, stale: true), S("claude", -5)]);
        Assert.Equal(new BarSegment("codex", 0, 1, true), l.Primary);
        Assert.Equal(new[] { new BarTick("claude", 0, false) }, l.Ticks);
    }

    [Fact]
    public void No_sources_is_an_empty_bar()
    {
        var l = SourceBar.Layout("claude", []);
        Assert.Null(l.Primary);
        Assert.Null(l.Beyond);
        Assert.Empty(l.Ticks);
    }

    [Fact]
    public void Accessible_name_lists_every_source_in_a_fixed_order()
    {
        Assert.Equal("Weekly. Claude 40%. Codex 70%, as of earlier.",
            SourceBar.AccessibleName("Weekly", [S("codex", 70, stale: true), S("claude", 40)]));
    }

    [Fact]
    public void Accessible_name_truncates_like_the_visible_numbers_and_clamps()
    {
        Assert.Equal("Weekly. Claude 40%. Codex 70%.",
            SourceBar.AccessibleName("Weekly", [S("claude", 40.9), S("codex", 70.6)]));
        Assert.Equal("Weekly. Claude 29%. Codex 100%.",
            SourceBar.AccessibleName("Weekly", [S("claude", 0.29 * 100), S("codex", 140)]));   // 0.29 * 100 is 28.999999999999996: float noise must not cost a point
    }
}
