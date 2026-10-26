using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// The waiting-Store-update cadence. The WinRT query and the toast live in
/// Sanduhr.App and need a Store identity; the decisions do not, so they are
/// tested here against a fixed clock.
/// </summary>
public class StoreUpdateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Unpackaged_builds_never_ask_the_store()
    {
        // Velopack and dev builds have no Store identity; StoreContext would throw.
        Assert.False(StoreUpdate.ShouldCheck(isPackaged: false, lastCheck: null, now: Now));
        Assert.False(StoreUpdate.ShouldCheck(isPackaged: false, lastCheck: Now.AddDays(-30), now: Now));
    }

    [Fact]
    public void With_update_check_off_the_store_is_never_asked()
    {
        // Settings, About, Updates' switch gates both probes (slice 6, decision 6).
        Assert.False(StoreUpdate.ShouldCheck(true, null, Now, enabled: false));
        Assert.False(StoreUpdate.ShouldCheck(true, Now.AddDays(-30), Now, enabled: false));
        Assert.True(StoreUpdate.ShouldCheck(true, null, Now, enabled: true));
    }

    [Fact]
    public void First_check_happens_then_waits_out_the_interval()
    {
        Assert.True(StoreUpdate.ShouldCheck(true, null, Now));
        Assert.False(StoreUpdate.ShouldCheck(true, Now, Now));
        Assert.False(StoreUpdate.ShouldCheck(true, Now.Add(-StoreUpdate.CheckInterval).AddMinutes(1), Now));
        Assert.True(StoreUpdate.ShouldCheck(true, Now - StoreUpdate.CheckInterval, Now));
        Assert.True(StoreUpdate.ShouldCheck(true, Now.AddDays(-1), Now));
    }

    [Fact]
    public void A_clock_that_moved_backwards_does_not_park_the_check()
    {
        // A resumed laptop or a timezone change can stamp the last check in the
        // future; without this the widget would never look again.
        Assert.True(StoreUpdate.ShouldCheck(true, Now.AddHours(3), Now));
        Assert.True(StoreUpdate.ShouldNotify(true, Now.AddHours(3), Now));
    }

    [Fact]
    public void Nothing_is_said_when_no_update_waits()
    {
        Assert.False(StoreUpdate.ShouldNotify(updatePending: false, lastNotified: null, now: Now));
        Assert.False(StoreUpdate.ShouldNotify(false, Now.AddDays(-7), Now));
    }

    [Fact]
    public void A_waiting_update_is_mentioned_once_then_at_the_renotify_cadence()
    {
        Assert.True(StoreUpdate.ShouldNotify(true, null, Now));
        Assert.False(StoreUpdate.ShouldNotify(true, Now, Now));
        Assert.False(StoreUpdate.ShouldNotify(true, Now.AddHours(-1), Now));
        Assert.True(StoreUpdate.ShouldNotify(true, Now - StoreUpdate.RenotifyInterval, Now));
    }

    [Fact]
    public void Intervals_are_overridable_so_the_cadence_is_testable_and_tunable()
    {
        var oneMinute = TimeSpan.FromMinutes(1);
        Assert.False(StoreUpdate.ShouldCheck(true, Now.AddSeconds(-30), Now, oneMinute));
        Assert.True(StoreUpdate.ShouldCheck(true, Now.AddSeconds(-61), Now, oneMinute));
        Assert.True(StoreUpdate.ShouldNotify(true, Now.AddSeconds(-61), Now, oneMinute));
    }

    [Fact]
    public void An_older_builds_quit_toast_still_has_an_action_to_match()
    {
        // 3.4 to 3.7 toasts carry this argument; 3.8 routes it to Restart to update.
        Assert.Equal("sanduhr-quit-for-update", StoreUpdate.LegacyQuitAction);
        Assert.NotEqual(StoreUpdate.LegacyQuitAction, RestartToUpdate.RestartAction);
    }
}
