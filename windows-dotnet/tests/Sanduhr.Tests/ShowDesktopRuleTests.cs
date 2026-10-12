using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The Desk's Show-desktop decision (PR #209 review, blocker 2; Este's live run, check e1).</summary>
public class ShowDesktopRuleTests
{
    /// <summary>Check e1 as Este's run logged it: Win+D raised the icons host and ownership carried the
    /// Desk up with it (Desk above the host, desktop foreground, no app). Lifting it to topmost then made
    /// it vanish; the rule must leave it alone.</summary>
    [Fact]
    public void E1_OwnershipCarriedTheDeskUp_Stays()
    {
        Assert.Equal(ShowDesktopAction.Stay,
            ShowDesktopRule.Decide(deskBelowHost: false, desktopForeground: true, appInFront: false, lifted: false));
        // And across the whole Show desktop, tick after tick.
        for (int tick = 0; tick < 20; tick++)
            Assert.Equal(ShowDesktopAction.Stay, ShowDesktopRule.Decide(false, true, false, lifted: false));
    }

    [Fact]
    public void ReSeats_OnlyWhenTheDeskIsMeasuredBelowTheHost_TheDesktopForeground_AndNoApp()
    {
        Assert.Equal(ShowDesktopAction.Lift, ShowDesktopRule.Decide(true, true, false, lifted: false));
        Assert.Equal(ShowDesktopAction.Stay, ShowDesktopRule.Decide(true, false, false, lifted: false));
        Assert.Equal(ShowDesktopAction.Stay, ShowDesktopRule.Decide(true, true, true, lifted: false));
        Assert.Equal(ShowDesktopAction.Stay, ShowDesktopRule.Decide(false, false, false, lifted: false));
    }

    /// <summary>Check s: shown with the desktop in front, then an app comes forward. At rest the
    /// shell-owned Desk sits above the host; even a misread "below" never lifts it past an app.</summary>
    [Fact]
    public void S_AnAppInFront_NeverLifts_OverManyTicks()
    {
        bool lifted = false;
        for (int tick = 0; tick < 40; tick++)
        {
            foreach (var below in new[] { false, true })
            {
                var a = ShowDesktopRule.Decide(below, desktopForeground: false, appInFront: true, lifted);
                Assert.NotEqual(ShowDesktopAction.Lift, a);
                if (a == ShowDesktopAction.Drop) lifted = false;
            }
        }
        Assert.False(lifted);
    }

    [Fact]
    public void ReSeated_DropsWhenAnAppComesToTheFront_AndOtherwiseHolds()
    {
        Assert.Equal(ShowDesktopAction.Drop, ShowDesktopRule.Decide(false, false, true, lifted: true));
        Assert.Equal(ShowDesktopAction.Drop, ShowDesktopRule.Decide(true, false, true, lifted: true));
        // The taskbar or Start taking the foreground (no app) keeps it where it is.
        Assert.Equal(ShowDesktopAction.Stay, ShowDesktopRule.Decide(false, false, false, lifted: true));
        // Covered again while still in Show desktop: re-seat again (idempotent).
        Assert.Equal(ShowDesktopAction.Lift, ShowDesktopRule.Decide(true, true, false, lifted: true));
    }

    [Fact]
    public void Sentinel_IsReseatedOnlyAtRest()
    {
        Assert.True(ShowDesktopRule.ShouldReseatSentinel(lifted: false, desktopForeground: false));
        Assert.False(ShowDesktopRule.ShouldReseatSentinel(lifted: false, desktopForeground: true));
        Assert.False(ShowDesktopRule.ShouldReseatSentinel(lifted: true, desktopForeground: false));
    }
}
