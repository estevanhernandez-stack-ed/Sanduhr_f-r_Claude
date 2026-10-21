using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Which media session the piece follows (the pure rule behind NowPlayingService).</summary>
public class NowPlayingSessionChoiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static NowPlayingInfo Track(string app, bool playing) =>
        new() { Title = "Tune", Artist = "Singer", Playing = playing, AppId = app };

    private static NowPlayingCandidate Session(int index, string app, bool playing, bool current = false, double? startedAtSeconds = null) =>
        new(index, Track(app, playing), current, startedAtSeconds is { } s ? T0.AddSeconds(s) : null);

    private static NowPlayingCandidate Empty(int index, bool current = false) => new(index, new NowPlayingInfo(), current, null);

    private static readonly NowPlayingPrefs Default = new();
    private static readonly NowPlayingPrefs Keep = new() { KeepWhilePaused = true };

    [Fact]
    public void APlayingSessionBeatsACurrentPausedTab()
    {
        var pick = NowPlayingSessionChoice.Choose(
            [Session(0, "chrome", playing: false, current: true), Session(1, "Spotify.exe", playing: true, startedAtSeconds: 5)], Default);
        Assert.Equal(1, pick?.Index);
    }

    [Fact]
    public void APlayingSessionBeatsACurrentPausedTab_EvenWithKeepShowing()
    {
        var pick = NowPlayingSessionChoice.Choose(
            [Session(0, "chrome", playing: false, current: true), Session(1, "Spotify.exe", playing: true)], Keep);
        Assert.Equal(1, pick?.Index);
    }

    [Fact]
    public void TwoPlaying_TheCurrentOneWins()
    {
        var pick = NowPlayingSessionChoice.Choose(
            [Session(0, "Spotify.exe", true, startedAtSeconds: 100), Session(1, "chrome", true, current: true, startedAtSeconds: 1)], Default);
        Assert.Equal(1, pick?.Index);
    }

    [Fact]
    public void TwoPlaying_NeitherCurrent_TheMostRecentlyStartedWins()
    {
        var pick = NowPlayingSessionChoice.Choose(
            [Session(0, "Spotify.exe", true, startedAtSeconds: 10), Session(1, "chrome", true, startedAtSeconds: 50),
             Session(2, "vlc", true, startedAtSeconds: 20), Session(3, "msedge", false, current: true)], Default);
        Assert.Equal(1, pick?.Index);
    }

    [Fact]
    public void AnUnknownStartIsTheOldest()
    {
        var pick = NowPlayingSessionChoice.Choose(
            [Session(0, "Spotify.exe", true), Session(1, "chrome", true, startedAtSeconds: 1)], Default);
        Assert.Equal(1, pick?.Index);
    }

    [Fact]
    public void AnExcludedPlayingAppIsSkipped()
    {
        var prefs = new NowPlayingPrefs { Excluded = ["chrome"] };
        var pick = NowPlayingSessionChoice.Choose(
            [Session(0, "chrome", true, current: true), Session(1, "Spotify.exe", true, startedAtSeconds: 1)], prefs);
        Assert.Equal(1, pick?.Index);
    }

    [Fact]
    public void AnExcludedPlayingAppAlone_IsNone_EvenWhenKeepingAndCurrent()
    {
        var prefs = new NowPlayingPrefs { Excluded = ["chrome"], KeepWhilePaused = true };
        Assert.Null(NowPlayingSessionChoice.Choose([Session(0, "chrome", true, current: true)], prefs));
        Assert.Null(NowPlayingSessionChoice.Choose([Session(0, "chrome", false, current: true)], prefs));
    }

    [Fact]
    public void NonePlaying_IsNone()
    {
        Assert.Null(NowPlayingSessionChoice.Choose([], Default));
        Assert.Null(NowPlayingSessionChoice.Choose([Session(0, "chrome", false, current: true), Session(1, "vlc", false)], Default));
        Assert.Null(NowPlayingSessionChoice.Choose([Empty(0, current: true)], Default));
    }

    [Fact]
    public void NonePlaying_KeepShowing_TheCurrentPausedSession()
    {
        var pick = NowPlayingSessionChoice.Choose(
            [Session(0, "vlc", false), Session(1, "chrome", false, current: true)], Keep);
        Assert.Equal(1, pick?.Index);
    }

    [Fact]
    public void NonePlaying_KeepShowing_NeverAPausedSessionThatIsNotCurrent()
    {
        Assert.Null(NowPlayingSessionChoice.Choose([Session(0, "vlc", false), Empty(1, current: true)], Keep));
    }

    [Fact]
    public void SessionsWithoutATrackNeverWin()
    {
        var pick = NowPlayingSessionChoice.Choose([Empty(0, current: true), Session(1, "Spotify.exe", true)], Default);
        Assert.Equal(1, pick?.Index);
    }
}
