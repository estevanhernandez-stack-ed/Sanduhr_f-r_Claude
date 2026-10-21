using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Mouse and touch on the Launcher: glow, the press dip, and when a lift launches.</summary>
public class LauncherPointerTests
{
    [Fact]
    public void HoverGlowsAndLeavingClearsIt()
    {
        var p = new LauncherPointer();
        p.MouseMove(2);
        Assert.Equal(2, p.Glow);
        Assert.Null(p.Dip);
        p.MouseMove(null);
        Assert.Null(p.Glow);
        p.MouseMove(1);
        p.MouseLeave();
        Assert.Null(p.Glow);
    }

    [Fact]
    public void PressDipsAndReleaseOnTheSameButtonLaunches()
    {
        var p = new LauncherPointer();
        p.MouseMove(1);
        p.MouseDown(1);
        Assert.Equal(1, p.Dip);
        Assert.Equal(1, p.Glow);
        Assert.Equal(1, p.MouseUp(1));
        Assert.Null(p.Dip);
        Assert.Equal(1, p.Glow); // still hovered
    }

    [Fact]
    public void ReleasingOnAnotherButtonOrTheGapDoesNotLaunch()
    {
        var p = new LauncherPointer();
        p.MouseDown(0);
        p.MouseMove(1);
        Assert.Null(p.Dip); // slid off: the dip ends
        Assert.Null(p.MouseUp(1));

        p.MouseDown(0);
        Assert.Null(p.MouseUp(null));
    }

    [Fact]
    public void SlidingOffAndBackOnWhilePressedStillLaunches()
    {
        var p = new LauncherPointer();
        p.MouseDown(2);
        p.MouseMove(null);
        Assert.Null(p.Dip);
        p.MouseMove(2);
        Assert.Equal(2, p.Dip);
        Assert.Equal(2, p.MouseUp(2));
    }

    [Fact]
    public void APressStartedOutsideAButtonNeverLaunchesOne()
    {
        var p = new LauncherPointer();
        p.MouseDown(null);
        Assert.Null(p.MouseUp(0));
    }

    [Fact]
    public void LeavingTheLauncherDropsAPressInFlight()
    {
        var p = new LauncherPointer();
        p.MouseDown(1);
        p.MouseLeave();
        Assert.Null(p.MouseUp(1));
    }

    [Fact]
    public void TouchDownGlowsAndDipsAndTouchUpInsideLaunches()
    {
        var p = new LauncherPointer();
        p.TouchDown(3);
        Assert.Equal(3, p.Glow);
        Assert.Equal(3, p.Dip);
        Assert.True(p.Touching);
        p.TouchMove(3);
        Assert.Equal(3, p.Glow);
        Assert.Equal(3, p.TouchUp(3));
        Assert.Null(p.Glow);
        Assert.Null(p.Dip);
        Assert.False(p.Touching);
    }

    [Fact]
    public void DraggingOutCancelsAndComingBackDoesNotRearm()
    {
        var p = new LauncherPointer();
        p.TouchDown(0);
        p.TouchMove(null);
        Assert.Null(p.Glow);
        Assert.Null(p.Dip);
        p.TouchMove(0);
        Assert.Null(p.Glow);
        Assert.Null(p.TouchUp(0));
        Assert.False(p.Touching);
    }

    [Fact]
    public void DraggingOntoAnotherButtonCancelsToo()
    {
        var p = new LauncherPointer();
        p.TouchDown(0);
        p.TouchMove(1);
        Assert.Null(p.Glow);
        Assert.Null(p.TouchUp(1));
    }

    [Fact]
    public void ATouchThatStartsOffAButtonDoesNothing()
    {
        var p = new LauncherPointer();
        p.TouchDown(null);
        Assert.Null(p.Glow);
        Assert.False(p.Touching);
        Assert.Null(p.TouchUp(0));
    }

    [Fact]
    public void ALostTouchEndsTheGlowWithoutLaunching()
    {
        var p = new LauncherPointer();
        p.TouchDown(2);
        p.Cancel();
        Assert.Null(p.Glow);
        Assert.Null(p.TouchUp(2));
    }

    [Fact]
    public void ResetForgetsEverything()
    {
        var p = new LauncherPointer();
        p.MouseMove(1);
        p.MouseDown(1);
        p.Reset();
        Assert.Null(p.Glow);
        Assert.Null(p.Dip);
        Assert.Null(p.MouseUp(1));
        p.TouchDown(1);
        p.Reset();
        Assert.False(p.Touching);
    }
}
