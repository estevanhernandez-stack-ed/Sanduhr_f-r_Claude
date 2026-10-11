using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The Desk's settings: off by default, the Mac's margins and clock size, values kept in range.</summary>
public class DeskSettingsTests
{
    [Fact]
    public void Default_IsOff_WithTheStandardLayout()
    {
        var d = DeskSettings.Default;
        Assert.False(d.Enabled);
        Assert.Equal("clock:bl claude:bl", d.Layout);
        Assert.Equal(new DeskMarginSettings(52, 52, 40, 60), d.Margins);
        Assert.Null(d.Font);
        Assert.Equal(112, d.TimeSize);
    }

    [Theory]
    [InlineData(10, 48)]
    [InlineData(500, 200)]
    [InlineData(120.4, 120)]
    [InlineData(double.NaN, 112)]
    [InlineData(-3, 112)]
    public void TimeSize_IsKeptInRange(double saved, double expected) =>
        Assert.Equal(expected, DeskSettings.ClampTimeSize(saved));

    [Fact]
    public void Margins_AreKeptInRange()
    {
        var m = new DeskMarginSettings(-5, 9000, double.NaN, 33.6).Clamped();
        Assert.Equal(new DeskMarginSettings(0, 400, 40, 34), m);
    }

    [Fact]
    public void Normalized_WritesTheLayoutCanonically_AndKeepsFont()
    {
        var d = (DeskSettings.Default with { Layout = "clock:bl:1.00 bogus:tr claude:zz", Font = "" }).Normalized();
        Assert.Equal("clock:bl claude:bl", d.Layout);
        Assert.Equal("", d.Font);
    }
}
