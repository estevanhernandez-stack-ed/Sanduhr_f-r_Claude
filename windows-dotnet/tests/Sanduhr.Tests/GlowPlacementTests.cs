using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class GlowPlacementTests
{
    private static readonly PxRect Mon = new(0, 0, 1920, 1080);
    private static readonly PxRect Mon2 = new(1920, 0, 2560, 1440);

    private static GlowTarget T(PxRect frame, PxRect? mon = null, bool caption = true, string? cls = null,
        bool min = false, bool cloaked = false, bool own = false) =>
        new(frame, mon ?? Mon, min, cloaked, cls, own, caption);

    [Fact]
    public void PxRect_contains_a_rect_wholly_inside_edges_touching_and_nothing_else()
    {
        var r = new PxRect(100, 50, 200, 100);
        Assert.True(r.Contains(new PxRect(100, 50, 200, 100)));
        Assert.True(r.Contains(new PxRect(150, 60, 10, 10)));
        Assert.False(r.Contains(new PxRect(99, 60, 10, 10)));
        Assert.False(r.Contains(new PxRect(250, 60, 51, 10)));
        Assert.False(r.Contains(new PxRect(150, 140, 10, 11)));
        Assert.False(r.Contains(new PxRect(150, 60, 0, 10)));
        Assert.False(new PxRect(0, 0, 0, 0).Contains(new PxRect(0, 0, 0, 0)));
    }

    [Fact]
    public void MaximizedWindowGetsMiddleHalfAtItsTop()
    {
        var r = GlowPlacement.Compute(T(new PxRect(0, 0, 1920, 1040)), Mon, 1.0);
        Assert.Equal(new PxRect(480, 0, 960, 24), r);
    }

    [Fact]
    public void WindowedTargetIsCenteredOnItsOwnWidth()
    {
        var r = GlowPlacement.Compute(T(new PxRect(200, 150, 900, 600)), Mon, 1.0);
        Assert.Equal(new PxRect(425, 150, 450, 24), r);
    }

    [Theory]
    [InlineData(1.0, 24)]
    [InlineData(1.5, 36)]
    [InlineData(2.0, 48)]
    public void HeightScalesWithDpi(double dpi, int expected)
    {
        var r = GlowPlacement.Compute(T(new PxRect(100, 100, 800, 600)), Mon, dpi);
        Assert.Equal(expected, r.Height);
        Assert.Equal(400, r.Width);
    }

    [Fact]
    public void WindowPartlyAboveTheScreenClampsToMonitorTop()
    {
        var r = GlowPlacement.Compute(T(new PxRect(100, -30, 800, 600)), Mon, 1.0);
        Assert.Equal(0, r.Y);
    }

    [Fact]
    public void WindowPartlyOffTheRightEdgeClampsInside()
    {
        // Frame spans 1500..2300; the centered band is 1700..2100, past the 1920 edge.
        var r = GlowPlacement.Compute(T(new PxRect(1500, 100, 800, 600)), Mon, 1.0);
        Assert.Equal(1520, r.X);
        Assert.Equal(400, r.Width);
        Assert.True(r.Right <= Mon.Right);
    }

    [Fact]
    public void WindowPartlyOffTheLeftEdgeClampsInside()
    {
        var r = GlowPlacement.Compute(T(new PxRect(-600, 100, 800, 600)), Mon, 1.0);
        Assert.Equal(0, r.X);
    }

    [Fact]
    public void SecondMonitorUsesItsOwnBounds()
    {
        var r = GlowPlacement.Compute(T(new PxRect(2000, 40, 2400, 1300), Mon2), Mon, 1.5);
        Assert.Equal(new PxRect(2600, 40, 1200, 36), r);
    }

    [Fact]
    public void WindowBottomOffScreenStillKeepsBandInsideMonitor()
    {
        var r = GlowPlacement.Compute(T(new PxRect(100, 1100, 800, 600)), Mon, 1.0);
        Assert.Equal(1080 - 24, r.Y);
    }

    [Fact]
    public void NoTargetFallsBackToMonitorTopEdge()
    {
        var r = GlowPlacement.Compute(null, Mon2, 2.0);
        Assert.Equal(new PxRect(1920 + 640, 0, 1280, 48), r);
    }

    [Fact]
    public void UnusableTargetFallsBack()
    {
        var r = GlowPlacement.Compute(T(new PxRect(10, 10, 800, 600), min: true), Mon, 1.0);
        Assert.Equal(new PxRect(480, 0, 960, 24), r);
    }

    [Fact]
    public void BadDpiTreatedAsOne()
    {
        Assert.Equal(24, GlowPlacement.Compute(null, Mon, 0).Height);
        Assert.Equal(24, GlowPlacement.Compute(null, Mon, double.NaN).Height);
    }

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    public void ShellClassesAreNotUsable(string cls) =>
        Assert.False(GlowPlacement.IsUsable(T(new PxRect(0, 0, 1920, 1080), cls: cls)));

    [Fact]
    public void ExcludesMinimizedCloakedOwnAndEmpty()
    {
        var f = new PxRect(10, 10, 800, 600);
        Assert.False(GlowPlacement.IsUsable(T(f, min: true)));
        Assert.False(GlowPlacement.IsUsable(T(f, cloaked: true)));
        Assert.False(GlowPlacement.IsUsable(T(f, own: true)));
        Assert.False(GlowPlacement.IsUsable(T(new PxRect(10, 10, 0, 600))));
        Assert.False(GlowPlacement.IsUsable(T(new PxRect(10, 10, 800, -5))));
        Assert.False(GlowPlacement.IsUsable(null));
        Assert.True(GlowPlacement.IsUsable(T(f, cls: "Notepad")));
    }

    [Fact]
    public void FullScreenExclusiveNeedsExactMonitorFrameAndNoCaption()
    {
        Assert.False(GlowPlacement.IsUsable(T(Mon, caption: false)));
        Assert.True(GlowPlacement.IsUsable(T(Mon, caption: true)));
        Assert.True(GlowPlacement.IsUsable(T(new PxRect(0, 0, 1920, 1040), caption: false)));
    }
}
