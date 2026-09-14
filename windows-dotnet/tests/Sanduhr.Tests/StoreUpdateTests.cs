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
    public void The_person_facing_text_says_why_quitting_is_the_fix()
    {
        // "An update is available" would read as noise next to a Store that has
        // already tried and failed; the whole point is the reason.
        Assert.Contains("quit", StoreUpdate.StatusNotice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("running", StoreUpdate.ToastBody, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("ms-windows-store://", StoreUpdate.StoreUpdatesUri, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(StoreUpdate.QuitAction));
    }
}
