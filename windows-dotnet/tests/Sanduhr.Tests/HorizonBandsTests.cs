using System;
using System.Linq;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Port of windows/tests/test_horizon_sparkline.py — guards the absolute-scale
/// horizon math (the easiest porting bug: reusing the classic line's min/max
/// normalization would flatten the bands).</summary>
public class HorizonBandsTests
{
    [Fact]
    public void Peak_column_accumulates_more_ink_than_a_lull()
    {
        var values = new[] { 5, 5, 5, 5, 100, 100, 100, 100, 5, 5, 5, 5 };
        var bars = HorizonBands.Compute(values, w: 120, h: 16);

        double colW = Math.Max(1, 120.0 / values.Length);
        double InkAt(int i) => bars.Where(b => Math.Abs(b.X - i * colW) < 0.5).Sum(b => b.Alpha);

        double peak = InkAt(5); // v = 100
        double lull = InkAt(0); // v = 5
        Assert.True(peak > lull * 1.5, $"peak ink {peak} should exceed 1.5x lull ink {lull}");
        Assert.True(peak > 0 && lull > 0);
    }

    [Fact]
    public void Uses_absolute_scale_not_min_max_normalized()
    {
        // All-equal 100s reach the top band (alpha 0.78). A min/max-normalized scale
        // (all equal → flat) would never produce a top-band bar.
        var bars = HorizonBands.Compute(new[] { 100, 100, 100 }, w: 30, h: 16);
        Assert.Contains(bars, b => Math.Abs(b.Alpha - 0.78) < 0.001);
    }

    [Fact]
    public void Bails_below_two_points()
        => Assert.Empty(HorizonBands.Compute(new[] { 50 }, w: 30, h: 16));
}
