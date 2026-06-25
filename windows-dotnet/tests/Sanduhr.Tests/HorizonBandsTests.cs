using System;
using System.Linq;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Guards the horizon sparkline math: peaks must accumulate more ink than
/// lulls, and (the live-test lesson) low / flat usage data must still render visibly
/// rather than looking empty.</summary>
public class HorizonBandsTests
{
    [Fact]
    public void Peak_column_accumulates_more_ink_than_a_lull()
    {
        var values = new[] { 5, 5, 5, 5, 100, 100, 100, 100, 5, 5, 5, 5 };
        var bars = HorizonBands.Compute(values, w: 120, h: 16);

        double colW = Math.Max(1, 120.0 / values.Length);
        double InkAt(int i) => bars.Where(b => Math.Abs(b.X - i * colW) < 0.5).Sum(b => b.Alpha);

        double peak = InkAt(5); // the 100 run
        double lull = InkAt(0); // the 5 run
        Assert.True(peak > lull * 1.5, $"peak ink {peak} should exceed 1.5x lull ink {lull}");
        Assert.True(peak > 0 && lull > 0);
    }

    [Fact]
    public void Flat_data_renders_a_visible_baseline()
    {
        // A tier sitting at a steady value must read as data, not an empty graph — the
        // baseline floor + flat-range fallback guarantee a visible band.
        var bars = HorizonBands.Compute(new[] { 9, 9, 9 }, w: 30, h: 16);
        Assert.NotEmpty(bars);
        Assert.All(bars, b => Assert.True(b.Height >= 1));
    }

    [Fact]
    public void Low_but_varying_data_fills_the_height()
    {
        // Normalized to its own range, low single-digit usage should still reach the
        // top band (alpha 0.80) at its peak instead of a faint sliver.
        var bars = HorizonBands.Compute(new[] { 2, 5, 3, 9, 4 }, w: 50, h: 16);
        Assert.Contains(bars, b => Math.Abs(b.Alpha - 0.80) < 0.001);
    }

    [Fact]
    public void Bails_below_two_points()
        => Assert.Empty(HorizonBands.Compute(new[] { 50 }, w: 30, h: 16));
}
