using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DeskGeometryTests
{
    private const double WinW = 1512;
    private const double WinH = 982;

    [Fact]
    public void MiddlesAreCenteredOnTheirSide()
    {
        var content = new DeskRect(52, 77, 1408, 845);
        Assert.Equal((content.Left, content.MidY), DeskGeometry.Point(DeskAnchor.ML, content));
        Assert.Equal((content.Right, content.MidY), DeskGeometry.Point(DeskAnchor.MR, content));
        Assert.Equal((content.MidX, content.Bottom), DeskGeometry.Point(DeskAnchor.BC, content));
        Assert.Equal((content.MidX, content.Top), DeskGeometry.Point(DeskAnchor.TC, content));
        Assert.Equal((content.Left, content.Top), DeskGeometry.Point(DeskAnchor.TL, content));
        Assert.Equal((content.Right, content.Bottom), DeskGeometry.Point(DeskAnchor.BR, content));
    }

    [Fact]
    public void ASideNeverReachesTheCenterColumn()
    {
        const double total = 1408;
        const double colWidth = 1512 * 0.46;
        const double meters = 112 * 3.2;
        var w = DeskGeometry.ColumnWidths(total, colWidth, meters);
        Assert.Equal(meters, w.Center);
        var centerStart = (total - w.Center) / 2;
        Assert.True(w.Side + DeskGeometry.ColumnGap <= centerStart + 0.001);
        Assert.True(w.Side * 2 + w.Center + DeskGeometry.ColumnGap * 2 <= total + 0.001);
        Assert.True(w.Side < colWidth);
    }

    [Fact]
    public void TheCenterColumnNeverOutgrowsTheSideCapOrTheScreen()
    {
        var wide = DeskGeometry.ColumnWidths(1408, 695, 2000);
        Assert.Equal(695, wide.Center);
        Assert.True(wide.Side * 2 + wide.Center + DeskGeometry.ColumnGap * 2 <= 1408 + 0.001);
        var narrow = DeskGeometry.ColumnWidths(300, 200, 280);
        Assert.True(narrow.Center <= 300 - DeskGeometry.ColumnGap * 2);
        Assert.True(narrow.Side >= 0);
        var small = DeskGeometry.ColumnWidths(2400, 400, 100);
        Assert.Equal(400, small.Side);
    }

    [Fact]
    public void ColumnGapIsTwentyFour()
    {
        Assert.Equal(24, DeskGeometry.ColumnGap);
    }

    [Theory]
    [InlineData(TaskbarEdge.Bottom)]
    [InlineData(TaskbarEdge.Left)]
    [InlineData(TaskbarEdge.Right)]
    public void TheTaskbarClearanceCoversEveryAnchorOnItsSide(TaskbarEdge edge)
    {
        const double reach = 70;
        const double inset = 112 * 0.18;
        var taskbar = edge switch
        {
            TaskbarEdge.Bottom => new DeskInsets(0, 0, 0, reach),
            TaskbarEdge.Left => new DeskInsets(reach, 0, 0, 0),
            _ => new DeskInsets(0, 0, reach, 0),
        };
        var m = DeskGeometry.Margins(52, 52, 40, 60, taskbar, inset);
        var content = DeskGeometry.Content(WinW, WinH, m, inset);
        foreach (var anchor in TaskbarClearance.AnchorsFor(edge))
        {
            var p = DeskGeometry.Point(anchor, content);
            switch (edge)
            {
                case TaskbarEdge.Bottom: Assert.True(WinH - p.Y >= reach + 60 - 0.5); break;
                case TaskbarEdge.Left: Assert.True(p.X >= reach + 52 - 0.5); break;
                default: Assert.True(WinW - p.X >= reach + 52 - 0.5); break;
            }
        }
        var plain = DeskGeometry.Content(WinW, WinH, DeskGeometry.Margins(52, 52, 40, 60, DeskInsets.None, inset), inset);
        Assert.Equal(52, plain.X, 6);
        Assert.True(Math.Abs(WinH - plain.Bottom - 60) < 0.001);
    }

    [Fact]
    public void TheMarginsMatchTheDesksPaddingFromBefore()
    {
        var m = DeskGeometry.Margins(10, 52, 40, 60, new DeskInsets(0, 24, 0, 70), 20);
        Assert.Equal(new DeskMargins(Top: 44, Leading: 0, Bottom: 110, Trailing: 32), m);
    }

    [Fact]
    public void ContentNeverGoesNegative()
    {
        var c = DeskGeometry.Content(100, 100, new DeskMargins(80, 80, 80, 80), 10);
        Assert.Equal(0, c.Width);
        Assert.Equal(0, c.Height);
    }

    [Fact]
    public void ContentSitsInsideTheMarginsAndInset()
    {
        var c = DeskGeometry.Content(1000, 800, new DeskMargins(10, 20, 30, 40), 5);
        Assert.Equal(new DeskRect(25, 15, 1000 - 20 - 40 - 10, 800 - 10 - 30 - 10), c);
    }

    [Fact]
    public void InsetsMaxAndScale()
    {
        var a = new DeskInsets(1, 5, 3, 9);
        var b = new DeskInsets(4, 2, 3, 6);
        Assert.Equal(new DeskInsets(4, 5, 3, 9), DeskInsets.Max(a, b));
        Assert.Equal(new DeskInsets(0.5, 2.5, 1.5, 4.5), a.Scaled(2));
        Assert.Equal(a, a.Scaled(0));
        Assert.Equal(a, a.Scaled(-3));
        Assert.Equal(new DeskInsets(0, 0, 0, 0), DeskInsets.None);
    }

    [Fact]
    public void RectEdges()
    {
        var r = new DeskRect(10, 20, 100, 60);
        Assert.Equal((10, 20, 110, 80, 60, 50), (r.Left, r.Top, r.Right, r.Bottom, r.MidX, r.MidY));
    }
}
