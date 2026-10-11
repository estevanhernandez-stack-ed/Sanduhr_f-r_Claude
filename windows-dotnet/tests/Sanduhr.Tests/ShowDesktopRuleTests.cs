using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The Desk's Show-desktop decision (PR #209 review, blocker 2).</summary>
public class ShowDesktopRuleTests
{
    [Fact]
    public void ShowDesktop_Lifts_OnlyWithTheHostRaised_TheDesktopForeground_AndNoAppInFront()
    {
        Assert.Equal(ShowDesktopAction.Lift, ShowDesktopRule.Decide(true, true, false, lifted: false));
        Assert.Equal(ShowDesktopAction.Stay, ShowDesktopRule.Decide(true, false, false, lifted: false));
        Assert.Equal(ShowDesktopAction.Stay, ShowDesktopRule.Decide(false, true, false, lifted: false));
        Assert.Equal(ShowDesktopAction.Stay, ShowDesktopRule.Decide(true, true, true, lifted: false));
    }

    /// <summary>The review's case: z-order reads "raised" at rest while an app is in front. Before the
    /// fix the Desk lifted, the safety check dropped it, the next tick lifted it again.</summary>
    [Fact]
    public void RaisedAtRest_WithAnAppInFront_NeverLifts_OverManyTicks()
    {
        bool lifted = false;
        for (int tick = 0; tick < 40; tick++)
        {
            var a = ShowDesktopRule.Decide(hostAboveSentinel: true, desktopForeground: false, appInFront: true, lifted);
            Assert.NotEqual(ShowDesktopAction.Lift, a);
            if (a == ShowDesktopAction.Drop) lifted = false;
        }
        Assert.False(lifted);
    }

    [Fact]
    public void Lifted_DropsWhenAnAppComesToTheFront_OrTheHostGoesBackDown()
    {
        Assert.Equal(ShowDesktopAction.Drop, ShowDesktopRule.Decide(true, false, true, lifted: true));
        Assert.Equal(ShowDesktopAction.Drop, ShowDesktopRule.Decide(false, true, false, lifted: true));
        Assert.Equal(ShowDesktopAction.Drop, ShowDesktopRule.Decide(false, false, false, lifted: true));
        // The taskbar or the Start menu taking the foreground (no app) keeps it lifted.
        Assert.Equal(ShowDesktopAction.Stay, ShowDesktopRule.Decide(true, false, false, lifted: true));
        Assert.Equal(ShowDesktopAction.Stay, ShowDesktopRule.Decide(true, true, false, lifted: true));
    }

    [Fact]
    public void Sentinel_IsReseatedOnlyAtRest()
    {
        Assert.True(ShowDesktopRule.ShouldReseatSentinel(lifted: false, desktopForeground: false));
        Assert.False(ShowDesktopRule.ShouldReseatSentinel(lifted: false, desktopForeground: true));
        Assert.False(ShowDesktopRule.ShouldReseatSentinel(lifted: true, desktopForeground: false));
    }
}
