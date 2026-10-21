using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Which audio sessions the Desk bars follow (spec 6.6): the playing app's, never System Sounds, never
/// the whole output. The App reads the sessions; the choosing is here.
/// </summary>
public class AudioSessionMatchTests
{
    // ---- AppUserModelId to process names ----

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("Spotify.exe", "spotify.exe")]
    [InlineData("SPOTIFY.EXE", "spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [InlineData("chrome", "chrome")]
    [InlineData("Chrome", "chrome")]
    [InlineData("MSEdge", "msedge")]
    [InlineData("msedge.exe", "msedge")]
    [InlineData("308046B0AF4A39CB", "firefox")]
    [InlineData("firefox.exe", "firefox")]
    [InlineData("Brave", "brave")]
    [InlineData("BraveSoftware.Brave-Browser", "brave")]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", "AppleMusic")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Microsoft.Media.Player")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "ZuneMusic")]
    [InlineData("C:\\Program Files\\VideoLAN\\VLC\\vlc.exe", "vlc")]
    [InlineData("com.squirrel.Discord.Discord", "Discord")]
    public void Matches_TheAppsOwnProcess(string appId, string process) =>
        Assert.True(AudioSessionMatch.Matches(appId, process));

    [Theory]
    [InlineData("Spotify.exe", "chrome")]
    [InlineData("Spotify.exe", "spot")]            // a prefix too short to mean it
    [InlineData("chrome", "msedge")]
    [InlineData("308046B0AF4A39CB", "chrome")]
    [InlineData("Spotify.exe", "")]
    [InlineData("Spotify.exe", null)]
    [InlineData(null, "spotify")]
    [InlineData("", "spotify")]
    [InlineData("   ", "spotify")]
    public void DoesNotMatch_OtherProcesses(string? appId, string? process) =>
        Assert.False(AudioSessionMatch.Matches(appId, process));

    [Fact]
    public void Candidates_AreLowerCaseUniqueAndNeverEmpty()
    {
        Assert.Empty(AudioSessionMatch.Candidates(null));
        Assert.Empty(AudioSessionMatch.Candidates("  "));
        var c = AudioSessionMatch.Candidates("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify");
        Assert.Contains("spotify", c);
        Assert.Contains("spotifymusic", c);
        Assert.All(c, x => { Assert.NotEmpty(x); Assert.Equal(x.ToLowerInvariant(), x); });
        Assert.Equal(c.Count, c.Distinct().Count());
        Assert.Equal(["spotify"], AudioSessionMatch.Candidates("Spotify.exe"));
    }

    // ---- which level to draw ----

    private static AudioSessionReading Session(string? process, float peak, bool system = false, bool own = false) =>
        new(process, system, own, peak);

    [Fact]
    public void Pick_TakesThePlayingAppsSession_NotTheLouderOne()
    {
        var pick = AudioSessionMatch.Pick("Spotify.exe",
        [
            Session("chrome", 0.9f),
            Session("Spotify", 0.3f),
        ]);
        Assert.True(pick.Matched);
        Assert.Equal(0.3f, pick.Peak);
    }

    [Fact]
    public void Pick_ABrowserHasOneSessionPerAudioProcess_TheLoudestCounts()
    {
        var pick = AudioSessionMatch.Pick("chrome",
        [
            Session("chrome", 0.1f),
            Session("chrome", 0.6f),
            Session("Spotify", 0.9f),
        ]);
        Assert.True(pick.Matched);
        Assert.Equal(0.6f, pick.Peak);
    }

    [Fact]
    public void Pick_SystemSoundsNeverMoveTheBars()
    {
        // A chime at full scale while the playing app is quiet: only the app counts.
        var pick = AudioSessionMatch.Pick("Spotify.exe",
        [
            Session(null, 1f, system: true),
            Session("Spotify", 0.05f),
        ]);
        Assert.Equal(0.05f, pick.Peak);
        Assert.True(pick.Matched);
    }

    [Fact]
    public void Pick_OnlySystemSounds_IsSilence_WithOrWithoutAnApp()
    {
        var sessions = new[] { Session(null, 1f, system: true), Session("Sanduhr", 1f, own: true) };
        Assert.Equal(new AudioSessionPick(0, false), AudioSessionMatch.Pick("Spotify.exe", sessions));
        Assert.Equal(new AudioSessionPick(0, false), AudioSessionMatch.Pick(null, sessions));
    }

    [Fact]
    public void Pick_SanduhrsOwnChimesNeverMoveTheBars()
    {
        var pick = AudioSessionMatch.Pick("firefox.exe",
        [
            Session("Sanduhr", 1f, own: true),
            Session("firefox", 0.2f),
        ]);
        Assert.Equal(0.2f, pick.Peak);
    }

    [Fact]
    public void Pick_NoMatch_FallsBackToTheLoudestNonSystemSession_NotTheEndpoint()
    {
        var pick = AudioSessionMatch.Pick("SomePlayerWeHaveNeverHeardOf",
        [
            Session(null, 1f, system: true),
            Session("mpv", 0.4f),
            Session("chrome", 0.7f),
        ]);
        Assert.False(pick.Matched);
        Assert.Equal(0.7f, pick.Peak);
    }

    [Fact]
    public void Pick_NoAppId_FallsBackTheSameWay()
    {
        var pick = AudioSessionMatch.Pick(null, [Session("mpv", 0.4f), Session(null, 1f, system: true)]);
        Assert.False(pick.Matched);
        Assert.Equal(0.4f, pick.Peak);
    }

    [Fact]
    public void Pick_MatchedButSilent_IsSilence_NotTheOthersLevel()
    {
        // The playing app's session is found and quiet (paused between tracks); a louder stranger does not borrow it.
        var pick = AudioSessionMatch.Pick("Spotify.exe", [Session("Spotify", 0f), Session("chrome", 0.8f)]);
        Assert.True(pick.Matched);
        Assert.Equal(0f, pick.Peak);
    }

    [Fact]
    public void Pick_NoSessions_IsZero()
    {
        Assert.Equal(new AudioSessionPick(0, false), AudioSessionMatch.Pick("Spotify.exe", []));
    }

    [Fact]
    public void Pick_ClampsAndIgnoresNaN()
    {
        var pick = AudioSessionMatch.Pick(null, [Session("a", float.NaN), Session("b", 7f), Session("c", -1f)]);
        Assert.Equal(1f, pick.Peak);
        Assert.Equal(0f, AudioSessionMatch.Pick(null, [Session("a", float.NaN)]).Peak);
    }

    [Fact]
    public void Pick_UnknownProcessName_StillCountsInTheFallback()
    {
        // A protected process whose name could not be read: no match by name, but it is a real stream.
        var pick = AudioSessionMatch.Pick("Spotify.exe", [Session(null, 0.5f)]);
        Assert.False(pick.Matched);
        Assert.Equal(0.5f, pick.Peak);
    }
}
