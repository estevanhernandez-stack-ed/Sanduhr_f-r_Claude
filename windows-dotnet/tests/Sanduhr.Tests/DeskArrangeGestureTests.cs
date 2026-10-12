using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The drag-to-place rule's gesture: a drop survives the capture loss its own release reports
/// (the S7 live run's r2: every drop was cancelled by it).</summary>
public class DeskArrangeGestureTests
{
    private sealed record Drag(string Widget);

    [Fact]
    public void ACaptureLossReportedWhileReleasingStillDrops()
    {
        var g = new DeskArrangeGesture<Drag>();
        g.Down(new Drag("clock"));
        Drag? cancelled = null;
        var dropped = g.Up(() => cancelled = g.CaptureLost());
        Assert.Equal("clock", dropped?.Widget);
        Assert.Null(cancelled);
        Assert.Null(g.Current);
    }

    [Fact]
    public void ACaptureLostBeforeTheButtonComesUpCancelsTheDrag()
    {
        var g = new DeskArrangeGesture<Drag>();
        g.Down(new Drag("clock"));
        Assert.Equal("clock", g.CaptureLost()?.Widget);
        Assert.Null(g.Up(() => { }));
    }

    [Fact]
    public void UpWithNoDragDropsNothingButStillReleases()
    {
        var g = new DeskArrangeGesture<Drag>();
        bool released = false;
        Assert.Null(g.Up(() => released = true));
        Assert.True(released);
    }

    [Fact]
    public void TheDropThenMovesThePieceToTheTargetAnchor()
    {
        // The drop the gesture hands back lands through Put on the working layout.
        var a = new DeskArrangement("clock:bl claude:bl");
        var g = new DeskArrangeGesture<Drag>();
        g.Down(new Drag("clock"));
        if (g.Up(() => g.CaptureLost()) is { } d) a.Put(d.Widget, DeskAnchor.TR, null);
        Assert.Equal(DeskAnchor.TR, a.Placement("clock")!.Anchor);
    }
}
