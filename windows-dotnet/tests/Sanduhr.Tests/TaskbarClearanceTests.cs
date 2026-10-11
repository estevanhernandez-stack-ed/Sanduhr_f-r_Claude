using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class TaskbarClearanceTests
{
    private static readonly PxRect Mon = new(0, 0, 3840, 2160);
    private static readonly PxRect Mon2 = new(3840, 0, 2560, 1440);

    [Fact]
    public void ReservedForABottom48PxTaskbar()
    {
        var work = new PxRect(0, 0, 3840, 2112);
        Assert.Equal(new DeskInsets(0, 0, 0, 48), TaskbarClearance.Reserved(Mon, work));
    }

    [Fact]
    public void ReservedIsZeroWhenTheWorkAreaIsTheMonitor()
    {
        Assert.Equal(DeskInsets.None, TaskbarClearance.Reserved(Mon, Mon));
    }

    [Fact]
    public void ReservedUsesTheMonitorsOwnOrigin()
    {
        var work = new PxRect(3840, 0, 2560, 1392);
        Assert.Equal(new DeskInsets(0, 0, 0, 48), TaskbarClearance.Reserved(Mon2, work));
    }

    [Fact]
    public void ReservedOnTheSides()
    {
        Assert.Equal(new DeskInsets(64, 0, 0, 0), TaskbarClearance.Reserved(Mon, new PxRect(64, 0, 3776, 2160)));
        Assert.Equal(new DeskInsets(0, 0, 64, 0), TaskbarClearance.Reserved(Mon, new PxRect(0, 0, 3776, 2160)));
        Assert.Equal(new DeskInsets(0, 48, 0, 0), TaskbarClearance.Reserved(Mon, new PxRect(0, 48, 3840, 2112)));
    }

    [Fact]
    public void ForBarBottomShown()
    {
        Assert.Equal((TaskbarEdge.Bottom, 48), TaskbarClearance.ForBar(new PxRect(0, 2112, 3840, 48), Mon));
    }

    [Fact]
    public void ForBarBottomAutoHiddenSlidAway()
    {
        Assert.Equal((TaskbarEdge.Bottom, 48), TaskbarClearance.ForBar(new PxRect(0, 2158, 3840, 48), Mon));
    }

    [Fact]
    public void ForBarLeft()
    {
        Assert.Equal((TaskbarEdge.Left, 64), TaskbarClearance.ForBar(new PxRect(0, 0, 64, 2160), Mon));
    }

    [Fact]
    public void ForBarRight()
    {
        Assert.Equal((TaskbarEdge.Right, 64), TaskbarClearance.ForBar(new PxRect(3776, 0, 64, 2160), Mon));
    }

    [Fact]
    public void ForBarTop()
    {
        Assert.Equal((TaskbarEdge.Top, 48), TaskbarClearance.ForBar(new PxRect(0, 0, 3840, 48), Mon));
    }

    [Fact]
    public void ForBarOnAnotherMonitorIsNull()
    {
        var barOnSecond = new PxRect(3840, 1392, 2560, 48);
        Assert.Null(TaskbarClearance.ForBar(barOnSecond, new PxRect(0, 0, 3840, 1440)));
        Assert.NotNull(TaskbarClearance.ForBar(barOnSecond, Mon2));
        Assert.Null(TaskbarClearance.ForBar(new PxRect(0, 2112, 3840, 48), Mon2));
    }

    [Fact]
    public void ForBarWithinFourPxOfTheMonitorCounts()
    {
        // Slid off just below the edge: 3 px gap.
        Assert.NotNull(TaskbarClearance.ForBar(new PxRect(0, 2163, 3840, 48), Mon));
        // 5 px gap: not on this monitor.
        Assert.Null(TaskbarClearance.ForBar(new PxRect(0, 2165, 3840, 48), Mon));
    }

    [Fact]
    public void InsetsTakeTheLargerOfReservedAndAutoHide()
    {
        var work = new PxRect(0, 0, 3840, 2112);
        var bars = new[] { new PxRect(0, 2158, 3840, 60) };
        Assert.Equal(new DeskInsets(0, 0, 0, 60), TaskbarClearance.Insets(Mon, work, bars, 1));
    }

    [Fact]
    public void InsetsForAnAutoHiddenBarWithNoReservation()
    {
        var bars = new[] { new PxRect(0, 2158, 3840, 48) };
        Assert.Equal(new DeskInsets(0, 0, 0, 48), TaskbarClearance.Insets(Mon, Mon, bars, 1));
    }

    [Fact]
    public void InsetsDivideByScale()
    {
        var work = new PxRect(0, 0, 3840, 2112);
        Assert.Equal(new DeskInsets(0, 0, 0, 32), TaskbarClearance.Insets(Mon, work, [], 1.5));
        var left = TaskbarClearance.Insets(Mon, Mon, [new PxRect(0, 0, 60, 2160)], 1.5);
        Assert.Equal(new DeskInsets(40, 0, 0, 0), left);
    }

    [Fact]
    public void InsetsIgnoreBarsOnOtherMonitors()
    {
        var other = new PxRect(3840, 1392, 2560, 48);
        Assert.Equal(DeskInsets.None, TaskbarClearance.Insets(Mon, Mon, [other], 1));
    }

    [Fact]
    public void AnchorsForEachEdge()
    {
        Assert.Equal([DeskAnchor.BL, DeskAnchor.BC, DeskAnchor.BR], TaskbarClearance.AnchorsFor(TaskbarEdge.Bottom));
        Assert.Equal([DeskAnchor.TL, DeskAnchor.ML, DeskAnchor.BL], TaskbarClearance.AnchorsFor(TaskbarEdge.Left));
        Assert.Equal([DeskAnchor.TR, DeskAnchor.MR, DeskAnchor.BR], TaskbarClearance.AnchorsFor(TaskbarEdge.Right));
        Assert.Equal([DeskAnchor.TL, DeskAnchor.TC, DeskAnchor.TR], TaskbarClearance.AnchorsFor(TaskbarEdge.Top));
    }
}
