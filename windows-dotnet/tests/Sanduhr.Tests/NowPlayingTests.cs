using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Now playing's pure half: the track and its live position, the tracker, the hide rules,
/// the apps list, the text, and the WinRT session mapping (parity with the Mac's NowPlayingTests,
/// minus the stream parser, supervisor, watchdog and Music/Spotify fallback, which Windows lacks).</summary>
public class NowPlayingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static NowPlayingInfo Song(string title = "Song A", bool playing = true, double elapsed = 10,
        string app = "chrome") => new()
    {
        Title = title, Artist = "Band", Album = "Album", Duration = 200, Elapsed = elapsed, ElapsedAt = T0,
        Rate = playing ? 1 : 0, Playing = playing, AppId = app,
    };

    // Track

    [Fact]
    public void State_FromPlayingOrRate()
    {
        Assert.Equal(NowPlayingState.None, new NowPlayingInfo().State);
        Assert.Equal(NowPlayingState.None, new NowPlayingInfo { Title = "  ", Playing = true }.State);
        Assert.Equal(NowPlayingState.Paused, new NowPlayingInfo { Title = "x", Playing = false }.State);
        Assert.Equal(NowPlayingState.Playing, new NowPlayingInfo { Title = "x", Rate = 1 }.State);
        Assert.Equal(NowPlayingState.Paused, new NowPlayingInfo { Title = "x", Rate = 0 }.State);
        Assert.Equal(NowPlayingState.Paused, new NowPlayingInfo { Title = "x" }.State);
    }

    [Fact]
    public void Position_RunsWhilePlayingAndStopsAtTheEnd()
    {
        var info = Song();
        Assert.Equal(10, info.Position(T0));
        Assert.Equal(15, info.Position(T0.AddSeconds(5)));
        Assert.Equal(200, info.Position(T0.AddSeconds(1000)));
        // A clock that reads before the stamp never moves it back.
        Assert.Equal(10, info.Position(T0.AddSeconds(-5)));
        Assert.Equal(0.5, info.Progress(T0.AddSeconds(90)));
        Assert.Equal(10, Song(playing: false).Position(T0.AddSeconds(60)));
        Assert.Null(new NowPlayingInfo { Title = "x" }.Position(T0));
        Assert.Null(new NowPlayingInfo { Title = "x", Elapsed = 5 }.Progress(T0));
    }

    [Fact]
    public void Position_UsesTheRateAndFallsBackToOneForANonPositiveRate()
    {
        var fast = Song() with { Rate = 2 };
        Assert.Equal(20, fast.Position(T0.AddSeconds(5)));
        var odd = Song() with { Rate = null };
        Assert.Equal(15, odd.Position(T0.AddSeconds(5)));
        var zeroButPlaying = Song() with { Rate = 0, Playing = true };
        Assert.Equal(15, zeroButPlaying.Position(T0.AddSeconds(5)));
    }

    // FromSession

    private static NowPlayingInfo Session(SessionPlaybackStatus status, double? rate = 1,
        TimeSpan? start = null, TimeSpan? end = null, TimeSpan? position = null,
        DateTimeOffset? updated = null, string? app = "Spotify.exe", string? title = "Song A") =>
        NowPlayingInfo.FromSession(title, "Band", "Album", status, rate,
            start ?? TimeSpan.Zero, end ?? TimeSpan.FromSeconds(200), position ?? TimeSpan.FromSeconds(10),
            updated ?? T0, app, T0.AddSeconds(99));

    [Fact]
    public void FromSession_MapsAPlayingTrack()
    {
        var info = Session(SessionPlaybackStatus.Playing, rate: 1.5, updated: T0);
        Assert.Equal("Song A", info.Title);
        Assert.Equal("Band", info.Artist);
        Assert.Equal("Album", info.Album);
        Assert.Equal(200, info.Duration);
        Assert.Equal(10, info.Elapsed);
        Assert.Equal(T0, info.ElapsedAt);
        Assert.Equal(1.5, info.Rate);
        Assert.True(info.Playing);
        Assert.Equal("Spotify.exe", info.AppId);
        Assert.Equal(NowPlayingState.Playing, info.State);
    }

    [Fact]
    public void FromSession_PausedIsPausedAndHoldsItsPosition()
    {
        var info = Session(SessionPlaybackStatus.Paused);
        Assert.False(info.Playing);
        Assert.Equal(NowPlayingState.Paused, info.State);
        Assert.Equal(10, info.Position(T0.AddSeconds(60)));
    }

    [Theory]
    [InlineData(SessionPlaybackStatus.Stopped)]
    [InlineData(SessionPlaybackStatus.Closed)]
    [InlineData(SessionPlaybackStatus.Opened)]
    [InlineData(SessionPlaybackStatus.Changing)]
    public void FromSession_OtherStatusesGiveNoTrack(SessionPlaybackStatus status)
    {
        var info = Session(status);
        Assert.Equal(NowPlayingState.None, info.State);
        Assert.Equal(new NowPlayingInfo(), info);
    }

    [Fact]
    public void FromSession_TimelineStartShiftsElapsedAndDuration()
    {
        var info = Session(SessionPlaybackStatus.Playing, start: TimeSpan.FromSeconds(30),
            end: TimeSpan.FromSeconds(230), position: TimeSpan.FromSeconds(45));
        Assert.Equal(200, info.Duration);
        Assert.Equal(15, info.Elapsed);
    }

    [Fact]
    public void FromSession_NoLengthMeansNoDurationAndNoProgress()
    {
        var info = Session(SessionPlaybackStatus.Playing, end: TimeSpan.Zero, position: TimeSpan.Zero);
        Assert.Null(info.Duration);
        Assert.Equal(0, info.Elapsed);
        Assert.Null(info.Progress(T0));
        Assert.Null(Session(SessionPlaybackStatus.Playing, end: TimeSpan.FromSeconds(-5)).Duration);
    }

    [Fact]
    public void FromSession_AnUnstampedTimelineAnchorsAtNow()
    {
        var now = T0.AddSeconds(99);
        Assert.Equal(now, NowPlayingInfo.FromSession("x", null, null, SessionPlaybackStatus.Playing, 1,
            TimeSpan.Zero, TimeSpan.FromSeconds(10), TimeSpan.Zero, default, null, now).ElapsedAt);
        Assert.Equal(now, NowPlayingInfo.FromSession("x", null, null, SessionPlaybackStatus.Playing, 1,
            TimeSpan.Zero, TimeSpan.FromSeconds(10), TimeSpan.Zero, DateTimeOffset.MinValue, null, now).ElapsedAt);
    }

    [Fact]
    public void FromSession_ATrackWithoutATitleIsNone()
    {
        Assert.Equal(NowPlayingState.None, Session(SessionPlaybackStatus.Playing, title: " ").State);
        Assert.Equal(NowPlayingState.None, Session(SessionPlaybackStatus.Playing, title: null).State);
    }

    // Tracker

    [Fact]
    public void Tracker_PlayingPausedStopped()
    {
        var t = new NowPlayingTracker();
        Assert.Equal(NowPlayingState.None, t.State);
        Assert.Null(t.Replace(Song(), T0));
        Assert.Equal(NowPlayingState.Playing, t.State);
        Assert.Equal("Song A", t.Shown?.Title);
        t.Replace(Song(playing: false), T0);
        Assert.Equal(NowPlayingState.Paused, t.State);
        // Stopped: the player clears now playing. It counts after the delay.
        var wait = t.Replace(new NowPlayingInfo(), T0.AddSeconds(1));
        Assert.Equal(NowPlayingTracker.ClearDelay, wait);
        Assert.Equal(NowPlayingState.Paused, t.State);
        Assert.NotNull(t.Settle(T0.AddSeconds(2)));
        Assert.Null(t.Settle(T0.AddSeconds(2.6)));
        Assert.Equal(NowPlayingState.None, t.State);
        Assert.Null(t.Shown);
    }

    [Fact]
    public void Tracker_ASkipDoesNotBlink()
    {
        var t = new NowPlayingTracker();
        t.Replace(Song("Song A"), T0);
        // Between tracks: nothing, then the next song half a second later.
        Assert.NotNull(t.Replace(new NowPlayingInfo(), T0.AddSeconds(1)));
        Assert.Equal("Song A", t.Shown?.Title);
        Assert.Null(t.Replace(Song("Song B"), T0.AddSeconds(1.5)));
        Assert.Equal("Song B", t.Shown?.Title);
        Assert.Null(t.ClearingSince);
        // A late settle from the first clear changes nothing.
        Assert.Null(t.Settle(T0.AddSeconds(3)));
        Assert.Equal("Song B", t.Shown?.Title);
    }

    [Fact]
    public void Tracker_SourceChangesAndReset()
    {
        var t = new NowPlayingTracker();
        t.Replace(Song(app: "chrome"), T0);
        t.Replace(Song("Track", app: "Spotify.exe"), T0);
        Assert.Equal("Spotify.exe", t.Shown?.AppId);
        t.Reset();
        Assert.Null(t.Shown);
        Assert.Equal(new NowPlayingInfo(), t.Latest);
        Assert.Null(t.ClearingSince);
    }

    [Fact]
    public void Tracker_NothingToClearWhenNothingShowed()
    {
        var t = new NowPlayingTracker();
        Assert.Null(t.Replace(new NowPlayingInfo(), T0));
        Assert.Null(t.ClearingSince);
    }

    // Hide rules and apps

    private static readonly NowPlayingInfo Playing =
        new() { Title = "Tune", Artist = "Singer", Playing = true, AppId = "chrome" };

    private static readonly NowPlayingInfo Paused = Playing with { Playing = false };

    [Fact]
    public void Visible_NothingPlayingNeverShows()
    {
        var prefs = new NowPlayingPrefs();
        Assert.Null(prefs.Visible(null));
        Assert.Null(prefs.Visible(new NowPlayingInfo()));
        Assert.Equal(Playing, prefs.Visible(Playing));
        Assert.Equal(Paused, prefs.Visible(Paused));
    }

    [Fact]
    public void Visible_PausedStillDrawsDuringTheGrace()
    {
        // Whether a paused track holds the piece up is IsActive's business, not Visible's.
        var prefs = new NowPlayingPrefs();
        Assert.Equal(Paused, prefs.Visible(Paused));
        Assert.Equal(Playing, prefs.Visible(Playing));
    }

    [Fact]
    public void IsActive_PlayingYes_PausedOnlyWhenKept_NothingNever()
    {
        var prefs = new NowPlayingPrefs();
        Assert.True(prefs.IsActive(Playing));
        Assert.False(prefs.IsActive(Paused));
        Assert.False(prefs.IsActive(null));
        Assert.False(prefs.IsActive(new NowPlayingInfo()));

        var keep = new NowPlayingPrefs { KeepWhilePaused = true };
        Assert.True(keep.IsActive(Playing));
        Assert.True(keep.IsActive(Paused));
        Assert.False(keep.IsActive(new NowPlayingInfo()));
    }

    [Fact]
    public void IsActive_ExcludedAppNeverHoldsThePieceUp()
    {
        var prefs = new NowPlayingPrefs { Excluded = ["chrome"], KeepWhilePaused = true };
        Assert.False(prefs.IsActive(Playing));
        Assert.False(prefs.IsActive(Paused));
    }

    [Fact]
    public void Visible_ExcludedAppsNeverShow()
    {
        var prefs = new NowPlayingPrefs { Excluded = ["chrome"] };
        Assert.Null(prefs.Visible(Playing));
        var music = Playing with { AppId = "Spotify.exe" };
        Assert.Equal(music, prefs.Visible(music));
        // No app known: not excluded.
        var unknown = Playing with { AppId = null };
        Assert.Equal(unknown, prefs.Visible(unknown));
        // App ids compare ignoring case.
        Assert.Null(new NowPlayingPrefs { Excluded = ["CHROME"] }.Visible(Playing));
    }

    [Fact]
    public void Prefs_Defaults()
    {
        var p = new NowPlayingPrefs();
        Assert.False(p.KeepWhilePaused);
        Assert.Empty(p.Excluded);
    }

    [Fact]
    public void Apps_SeenInOrderWithoutRepeats()
    {
        IReadOnlyList<string> seen = [];
        seen = NowPlayingApps.Seen(seen, "chrome");
        seen = NowPlayingApps.Seen(seen, "Spotify.exe");
        seen = NowPlayingApps.Seen(seen, "chrome");
        seen = NowPlayingApps.Seen(seen, null);
        seen = NowPlayingApps.Seen(seen, "");
        Assert.Equal(["chrome", "Spotify.exe"], seen);
        Assert.Equal(["chrome", "Spotify.exe", "a.app", "z.app"],
            NowPlayingApps.Rows(seen, ["z.app", "Spotify.exe", "a.app"]));
    }

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("chrome", "Chrome")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "ZuneMusic")]
    [InlineData("MSEdge", "MSEdge")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "SpotifyMusic")]
    [InlineData("C:\\Program Files\\VLC\\vlc.exe", "Vlc")]
    [InlineData("firefox.exe", "Firefox")]
    [InlineData("Brave", "Brave")]
    [InlineData("  Foo.EXE ", "Foo")]
    public void DisplayName_IsFriendly(string appId, string expected) =>
        Assert.Equal(expected, NowPlayingApps.DisplayName(appId));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".exe")]
    [InlineData("!")]
    [InlineData("_x")]
    [InlineData("a.")]
    public void DisplayName_IsNeverEmpty(string? appId) =>
        Assert.False(string.IsNullOrWhiteSpace(NowPlayingApps.DisplayName(appId)));

    // Text

    private static readonly NowPlayingInfo SongInfo =
        new() { Title = "Breathe (In the Air)", Artist = "Pink Floyd", Playing = true };

    [Fact]
    public void Text_TitleArtistAndGlyph()
    {
        Assert.Equal($"{NowPlayingText.PlayingGlyph} Breathe (In the Air) \u00B7 Pink Floyd", NowPlayingText.Line(SongInfo));
        Assert.StartsWith(NowPlayingText.PausedGlyph, NowPlayingText.Line(SongInfo with { Playing = false }));
        Assert.Equal($"{NowPlayingText.PlayingGlyph} Breathe (In the Air)", NowPlayingText.Line(SongInfo with { Artist = " " }));
        Assert.Null(NowPlayingText.Line(null));
        Assert.Null(NowPlayingText.Line(new NowPlayingInfo()));
    }

    [Fact]
    public void Text_TheDeskGetsTheWholeLine()
    {
        var long_ = new NowPlayingInfo
        {
            Title = "Shine On You Crazy Diamond (Parts I-V)", Artist = "Pink Floyd and Friends", Playing = true,
        };
        var whole = $"{NowPlayingText.PlayingGlyph} Shine On You Crazy Diamond (Parts I-V) \u00B7 Pink Floyd and Friends";
        Assert.Equal(whole, NowPlayingText.Line(long_));
        Assert.Equal(whole, NowPlayingText.Desk(long_));
    }

    [Fact]
    public void Text_SplitGlyph()
    {
        var line = NowPlayingText.Line(SongInfo)!;
        Assert.Equal((NowPlayingText.PlayingGlyph, "Breathe (In the Air) \u00B7 Pink Floyd"), NowPlayingText.SplitGlyph(line));
        var paused = NowPlayingText.Line(SongInfo with { Playing = false })!;
        Assert.Equal(NowPlayingText.PausedGlyph, NowPlayingText.SplitGlyph(paused).Glyph);
        Assert.Equal((null, "plain"), NowPlayingText.SplitGlyph("plain"));
    }

    // Status

    [Fact]
    public void Status_NamesTheAppNeverTheTitle()
    {
        Assert.Equal("Off", NowPlayingStatus.Text(false, Playing));
        Assert.Equal("Nothing playing", NowPlayingStatus.Text(true, null));
        Assert.Equal("Nothing playing", NowPlayingStatus.Text(true, new NowPlayingInfo()));
        Assert.Equal("Playing in Chrome", NowPlayingStatus.Text(true, Playing));
        Assert.Equal("Paused in Spotify", NowPlayingStatus.Text(true, Paused with { AppId = "Spotify.exe" }));
        Assert.Equal("Playing", NowPlayingStatus.Text(true, Playing with { AppId = null }));
        Assert.DoesNotContain("Tune", NowPlayingStatus.Text(true, Playing));
    }

    // #317: a web app plays under an id no rule can name; the Start menu can.

    private const string YouTubeMusicId = "Chrome._crx_cinhimbnkkghhklpknlkffjgod";

    private static string? StartMenu(string id) => id == YouTubeMusicId ? "YouTube Music" : null;

    [Fact]
    public void DisplayName_TakesTheStartMenuNameWhenThereIsOne()
    {
        Assert.Equal("YouTube Music", NowPlayingApps.DisplayName(YouTubeMusicId, StartMenu));
        Assert.Equal("YouTube Music", NowPlayingApps.DisplayName("  " + YouTubeMusicId + " ", StartMenu));
    }

    [Fact]
    public void DisplayName_FallsBackToItsRuleThenTheId()
    {
        Assert.Equal("Spotify", NowPlayingApps.DisplayName("Spotify.exe", StartMenu));
        Assert.Equal("Spotify", NowPlayingApps.DisplayName("Spotify.exe", _ => "   "));
        Assert.Equal("Spotify", NowPlayingApps.DisplayName("Spotify.exe", _ => throw new InvalidOperationException()));
        Assert.Equal(YouTubeMusicId, NowPlayingApps.DisplayName(YouTubeMusicId, null)); // no name anywhere: the id, as before
        Assert.Equal("Unknown app", NowPlayingApps.DisplayName(null, StartMenu));
    }

    [Fact]
    public void Status_NamesAWebAppByItsStartMenuName() =>
        Assert.Equal("Playing in YouTube Music", NowPlayingStatus.Text(true, Playing with { AppId = YouTubeMusicId }, StartMenu));
}
