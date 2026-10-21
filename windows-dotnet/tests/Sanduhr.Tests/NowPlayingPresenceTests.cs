using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// The now-playing piece's life (spec 6.3, 6.6): up while the media session plays, gone 5 s after it
/// stops or pauses, never opened by a sound that has no playing session. Clock injected; nothing sleeps.
/// </summary>
public class NowPlayingPresenceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    private static readonly NowPlayingInfo Playing = new() { Title = "Tune", Artist = "Singer", Playing = true, AppId = "Spotify.exe" };
    private static readonly NowPlayingInfo Paused = Playing with { Playing = false };

    // ---- the rule: shows when playing, hides 5 s after it stops ----

    [Fact]
    public void StartsHidden()
    {
        var p = new NowPlayingPresence();
        Assert.False(p.Wanted);
        Assert.Equal(PresencePhase.Hidden, p.Phase(T0));
        Assert.Equal(0, p.Amount(T0));
        Assert.False(p.OnScreen(T0));
    }

    [Fact]
    public void Playing_ShowsAtOnce()
    {
        var p = new NowPlayingPresence();
        Assert.Null(p.Observe(true, T0));
        Assert.True(p.Wanted);
        Assert.Equal(PresencePhase.Opening, p.Phase(T0));
        Assert.True(p.OnScreen(T0));
        Assert.Equal(PresencePhase.Shown, p.Phase(At(1)));
        Assert.Equal(1, p.Amount(At(1)));
    }

    [Fact]
    public void Paused_HidesOnlyAfterFiveSeconds()
    {
        var prefs = new NowPlayingPrefs();
        var p = new NowPlayingPresence();
        p.Observe(prefs.IsActive(Playing), T0);

        var wait = p.Observe(prefs.IsActive(Paused), At(10));
        Assert.Equal(NowPlayingPresence.Grace, wait);
        Assert.True(p.Wanted);

        Assert.Equal(TimeSpan.FromSeconds(1), p.Settle(At(14)));
        Assert.True(p.Wanted);
        Assert.Null(p.Settle(At(15)));
        Assert.False(p.Wanted);
        Assert.Equal(PresencePhase.Closing, p.Phase(At(15)));
        Assert.Equal(PresencePhase.Hidden, p.Phase(At(16)));
    }

    [Fact]
    public void Stopped_HidesAfterFiveSeconds_NoSessionAtAll()
    {
        var prefs = new NowPlayingPrefs();
        var p = new NowPlayingPresence();
        p.Observe(prefs.IsActive(Playing), T0);
        // Stopped, Closed, a session that went away: all read as no track.
        Assert.Equal(NowPlayingPresence.Grace, p.Observe(prefs.IsActive(new NowPlayingInfo()), At(1)));
        Assert.Null(p.Settle(At(6)));
        Assert.False(p.Wanted);
    }

    [Fact]
    public void ResumingInsideTheGraceNeverHides()
    {
        var p = new NowPlayingPresence();
        p.Observe(true, T0);
        p.Observe(false, At(10));
        Assert.Null(p.Observe(true, At(14.9)));     // a skip, a brief pause
        Assert.Null(p.Settle(At(30)));               // the old deadline passes: nothing happens
        Assert.True(p.Wanted);
        Assert.Equal(PresencePhase.Shown, p.Phase(At(30)));
    }

    [Fact]
    public void RepeatedInactiveReadingsDoNotRestartTheGrace()
    {
        var p = new NowPlayingPresence();
        p.Observe(true, T0);
        p.Observe(false, At(10));
        // A timeline event every second while paused must not push the deadline out.
        for (int s = 11; s <= 14; s++) p.Observe(false, At(s));
        Assert.True(p.Wanted);
        p.Observe(false, At(15));
        Assert.False(p.Wanted);
    }

    [Fact]
    public void ASystemSoundWithNoPlayingSessionNeverShowsIt()
    {
        // The presence has no audio input at all: chimes arrive as nothing but "the session is not active".
        var prefs = new NowPlayingPrefs();
        var p = new NowPlayingPresence();
        for (int s = 0; s < 60; s++)
        {
            Assert.Null(p.Observe(prefs.IsActive(new NowPlayingInfo()), At(s)));
            Assert.False(p.Wanted);
            Assert.Equal(PresencePhase.Hidden, p.Phase(At(s)));
            Assert.Equal(0, p.Amount(At(s)));
        }
        // And a session that is only paused (a browser tab with a ding) does not open it either.
        for (int s = 60; s < 120; s++)
        {
            p.Observe(prefs.IsActive(Paused), At(s));
            Assert.False(p.Wanted);
        }
    }

    [Fact]
    public void KeepShowingWhilePaused_HoldsItThroughAPause_ButNotAStop()
    {
        var keep = new NowPlayingPrefs { KeepWhilePaused = true };
        var p = new NowPlayingPresence();
        p.Observe(keep.IsActive(Playing), T0);
        for (int s = 1; s <= 600; s++) p.Observe(keep.IsActive(Paused), At(s));
        Assert.True(p.Wanted);
        Assert.Equal(PresencePhase.Shown, p.Phase(At(600)));

        // Stopping (no track) still lets it go after the grace.
        p.Observe(keep.IsActive(new NowPlayingInfo()), At(601));
        Assert.Null(p.Settle(At(606)));
        Assert.False(p.Wanted);
    }

    [Fact]
    public void KeepShowingWhilePaused_TurnedOnMidPause_HoldsIt()
    {
        var p = new NowPlayingPresence();
        p.Observe(true, T0);
        p.Observe(new NowPlayingPrefs().IsActive(Paused), At(10));
        p.Observe(new NowPlayingPrefs { KeepWhilePaused = true }.IsActive(Paused), At(12));
        Assert.Null(p.Settle(At(30)));
        Assert.True(p.Wanted);
    }

    [Fact]
    public void ExcludedApp_NeverOpensIt_AndDismissGoesAtOnce()
    {
        var prefs = new NowPlayingPrefs { Excluded = ["Spotify.exe"] };
        var p = new NowPlayingPresence();
        Assert.Null(p.Observe(prefs.IsActive(Playing), T0));
        Assert.False(p.Wanted);

        // Up for another app, then the user excludes it: Dismiss starts the fade with no grace.
        p.Observe(true, At(1));
        p.Dismiss(At(2));
        Assert.False(p.Wanted);
        Assert.Equal(PresencePhase.Closing, p.Phase(At(2.1)));
        Assert.Null(p.Settle(At(100)));
    }

    // ---- the fade and the gap: how far, how fast ----

    [Fact]
    public void Opening_TakesAboutASecond_ClosingIsFaster()
    {
        Assert.Equal(1000, NowPlayingPresence.OpenDuration.TotalMilliseconds);
        Assert.True(NowPlayingPresence.CloseDuration < NowPlayingPresence.OpenDuration);
        Assert.InRange(NowPlayingPresence.CloseDuration.TotalMilliseconds, 400, 700);

        var open = new NowPlayingPresence();
        open.Observe(true, T0);
        Assert.Equal(0, open.Amount(T0), 5);
        Assert.InRange(open.Amount(T0.AddMilliseconds(500)), 0.45, 0.55);   // eased: the middle is the middle
        Assert.Equal(PresencePhase.Opening, open.Phase(T0.AddMilliseconds(999)));
        Assert.Equal(PresencePhase.Shown, open.Phase(T0.AddMilliseconds(1000)));
        Assert.Equal(1, open.Amount(T0.AddMilliseconds(1000)), 5);

        var close = new NowPlayingPresence();
        close.Observe(true, T0);
        close.Observe(false, At(10));
        close.Settle(At(15));
        Assert.Equal(1, close.Amount(At(15)), 5);
        Assert.Equal(PresencePhase.Closing, close.Phase(At(15).AddMilliseconds(599)));
        Assert.Equal(PresencePhase.Hidden, close.Phase(At(15).AddMilliseconds(600)));
        Assert.Equal(0, close.Amount(At(15).AddMilliseconds(600)), 5);
    }

    [Fact]
    public void Amount_IsMonotoneAndStaysInRange()
    {
        var p = new NowPlayingPresence();
        p.Observe(true, T0);
        double last = -1;
        for (int ms = 0; ms <= 1200; ms += 25)
        {
            double a = p.Amount(T0.AddMilliseconds(ms));
            Assert.InRange(a, 0, 1);
            Assert.True(a >= last);
            last = a;
        }
    }

    [Fact]
    public void ComingBackMidFade_ContinuesFromWhereItIsWithoutAJump()
    {
        var p = new NowPlayingPresence();
        p.Observe(true, T0);
        p.Observe(false, At(10));
        p.Settle(At(15));                                     // closing begins
        double mid = p.Amount(At(15).AddMilliseconds(300));
        Assert.InRange(mid, 0.1, 0.9);

        p.Observe(true, At(15).AddMilliseconds(300));          // playing again
        Assert.True(p.Wanted);
        Assert.Equal(mid, p.Amount(At(15).AddMilliseconds(300)), 5);   // no jump
        Assert.True(p.Amount(At(15).AddMilliseconds(500)) > mid);       // now heading up
        Assert.Equal(PresencePhase.Shown, p.Phase(At(17)));
    }

    [Fact]
    public void IsMoving_OnlyWhileOpeningOrClosing()
    {
        var p = new NowPlayingPresence();
        Assert.False(p.IsMoving(T0));
        p.Observe(true, T0);
        Assert.True(p.IsMoving(T0.AddMilliseconds(100)));
        Assert.False(p.IsMoving(At(2)));
        p.Observe(false, At(3));
        p.Settle(At(8));
        Assert.True(p.IsMoving(At(8).AddMilliseconds(100)));
        Assert.False(p.IsMoving(At(10)));
    }

    [Fact]
    public void AnimationsOff_SnapsInAndOut()
    {
        var p = new NowPlayingPresence { Animate = false };
        p.Observe(true, T0);
        Assert.Equal(PresencePhase.Shown, p.Phase(T0));
        Assert.Equal(1, p.Amount(T0));
        Assert.False(p.IsMoving(T0));

        p.Observe(false, At(1));
        p.Settle(At(6));
        Assert.Equal(PresencePhase.Hidden, p.Phase(At(6)));
        Assert.Equal(0, p.Amount(At(6)));
        Assert.False(p.OnScreen(At(6)));
    }

    [Fact]
    public void Reset_GoesAtOnce()
    {
        var p = new NowPlayingPresence();
        p.Observe(true, T0);
        p.Reset();
        Assert.False(p.Wanted);
        Assert.Equal(PresencePhase.Hidden, p.Phase(At(0.1)));
        Assert.Equal(0, p.Amount(At(0.1)));
    }

    [Fact]
    public void Grace_IsFiveSeconds() => Assert.Equal(TimeSpan.FromSeconds(5), NowPlayingPresence.Grace);

    // ---- the setting ----

    [Fact]
    public void KeepPaused_DefaultsOff_AndRoundTrips()
    {
        Assert.False(NowPlayingSettings.Default.KeepPaused);
        Assert.False(NowPlayingSettings.Default.Prefs.KeepWhilePaused);

        var on = NowPlayingSettings.Default with { KeepPaused = true };
        var back = NowPlayingSettings.Decode(on.Encode());
        Assert.True(back.KeepPaused);
        Assert.True(back.Prefs.KeepWhilePaused);
        Assert.True(on.Encode().ContainsKey("keep_paused"));
        Assert.False(on.Encode().ContainsKey("hide_paused"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LegacyHidePausedKey_IsIgnored_PausedHidesAfterTheGraceForEveryone(bool legacy)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse($"{{\"widget\":true,\"hide_paused\":{legacy.ToString().ToLowerInvariant()},\"excluded\":[\"chrome\"],\"visualizer\":false}}");
        var s = NowPlayingSettings.Decode(node);
        Assert.False(s.KeepPaused);
        Assert.True(s.Widget);
        Assert.False(s.Bars);
        Assert.Equal(["chrome"], s.Excluded);
        Assert.DoesNotContain("hide_paused", s.Encode().Select(kv => kv.Key));
    }
}
