using System.Runtime.InteropServices;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Issue #331: the usage transport's browser process exited and the widget never came back. The
/// rebuild schedule (5 s, 30 s, then every 5 minutes, a restart offer after two failures) and the
/// dead-browser test are decided here, away from WebView2.
/// </summary>
public class TransportRecoveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 21, 11, 0, TimeSpan.Zero);

    [Fact]
    public void The_backoff_is_5_seconds_then_30_then_every_5_minutes()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), TransportRecovery.DelayAfter(0));
        Assert.Equal(TimeSpan.FromSeconds(30), TransportRecovery.DelayAfter(1));
        Assert.Equal(TimeSpan.FromMinutes(5), TransportRecovery.DelayAfter(2));
        Assert.Equal(TimeSpan.FromMinutes(5), TransportRecovery.DelayAfter(9));
    }

    [Fact]
    public void A_lost_browser_is_rebuilt_5_seconds_later()
    {
        var r = new TransportRecovery();
        Assert.False(r.Due(Now));

        Assert.True(r.BrowserLost("Failed", Now));

        Assert.True(r.Lost);
        Assert.Equal("Failed", r.LastExitKind);
        Assert.False(r.Due(Now.AddSeconds(4)));
        Assert.True(r.Due(Now.AddSeconds(5)));
        Assert.False(r.OffersRestart);
    }

    [Fact]
    public void The_event_and_the_exception_for_one_exit_count_once()
    {
        var r = new TransportRecovery();
        Assert.True(r.BrowserLost("Failed", Now));
        Assert.False(r.BrowserLost("invalid-control", Now.AddSeconds(3)));
        Assert.Equal(Now.AddSeconds(5), r.NextAttemptAt);
        Assert.Equal("Failed", r.LastExitKind);
    }

    [Fact]
    public void Two_failed_rebuilds_offer_a_restart_and_the_retries_go_on()
    {
        var r = new TransportRecovery();
        r.BrowserLost("Failed", Now);

        var t1 = Now.AddSeconds(5);
        r.RebuildFailed(t1);
        Assert.False(r.OffersRestart);
        Assert.Equal(t1.AddSeconds(30), r.NextAttemptAt);

        var t2 = t1.AddSeconds(30);
        r.RebuildFailed(t2);
        Assert.True(r.OffersRestart);
        Assert.Equal(t2.AddMinutes(5), r.NextAttemptAt);

        var t3 = t2.AddMinutes(5);
        r.RebuildFailed(t3);
        Assert.True(r.OffersRestart);
        Assert.Equal(t3.AddMinutes(5), r.NextAttemptAt);
    }

    [Fact]
    public void A_rebuild_that_works_clears_the_offer()
    {
        var r = new TransportRecovery();
        r.BrowserLost("Failed", Now);
        r.RebuildFailed(Now.AddSeconds(5));
        r.RebuildFailed(Now.AddSeconds(35));
        Assert.True(r.OffersRestart);

        r.Recovered();

        Assert.False(r.Lost);
        Assert.False(r.OffersRestart);
        Assert.Null(r.NextAttemptAt);
        Assert.False(r.Due(Now.AddHours(1)));

        // The next exit starts the schedule over.
        Assert.True(r.BrowserLost("Normal", Now.AddHours(2)));
        Assert.Equal(Now.AddHours(2).AddSeconds(5), r.NextAttemptAt);
    }

    [Fact]
    public void A_clock_that_moved_backwards_does_not_park_the_rebuild()
    {
        var r = new TransportRecovery();
        r.BrowserLost("Failed", Now);
        Assert.True(r.Due(Now.AddHours(-2)));
    }

    [Fact]
    public void A_rebuild_failure_with_nothing_lost_changes_nothing()
    {
        var r = new TransportRecovery();
        r.RebuildFailed(Now);
        Assert.False(r.Lost);
        Assert.Equal(0, r.FailedRebuilds);
    }

    [Fact]
    public void The_controls_no_longer_valid_error_is_a_dead_browser()
    {
        // The text 3.7.1 showed on the widget (issue #331).
        Assert.True(TransportRecovery.IsDeadBrowserError(new InvalidOperationException(
            "The WebView control is no longer valid because the browser process exited.")));
        Assert.True(TransportRecovery.IsDeadBrowserError(new InvalidOperationException(
            "The browser process crashed. To work around this, listen for ProcessFailed.")));
        Assert.True(TransportRecovery.IsDeadBrowserError(new COMException("closed", TransportRecovery.ErrorInvalidState)));
        Assert.True(TransportRecovery.IsDeadBrowserError(new COMException("gone", TransportRecovery.ErrorProcessAborted)));
        Assert.True(TransportRecovery.IsDeadBrowserError(new AggregateException(
            new InvalidOperationException("The WebView control is no longer valid"))));
    }

    [Fact]
    public void Ordinary_failures_are_not_a_dead_browser()
    {
        Assert.False(TransportRecovery.IsDeadBrowserError(null));
        Assert.False(TransportRecovery.IsDeadBrowserError(new NetworkException("usage fetch failed: Failed to fetch")));
        Assert.False(TransportRecovery.IsDeadBrowserError(new TimeoutException("fetch timed out")));
        Assert.False(TransportRecovery.IsDeadBrowserError(new COMException("other", unchecked((int)0x80004005))));
        Assert.False(TransportRecovery.IsDeadBrowserError(new InvalidOperationException("The browser process is starting")));
    }

    [Fact]
    public void The_stopped_notice_offers_a_restart()
    {
        Assert.StartsWith("Sanduhr's usage connection stopped.", TransportRecovery.StoppedNotice, StringComparison.Ordinal);
        Assert.Contains("Restart Sanduhr", TransportRecovery.StoppedNotice, StringComparison.Ordinal);
        Assert.Equal("Restart", TransportRecovery.RestartButton);
    }
}
