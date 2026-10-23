using System.Windows.Media.Imaging;
using Sanduhr.App.Interop;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// #317: Settings, Now playing read "Playing in Chrome._crx_cinhimbnk…", the AppUserModelId of YouTube Music installed
/// as a Chrome app. The name comes from the Start menu, the way the Launcher's app list names the same id.
/// </summary>
public class NowPlayingAppNamesTests
{
    private const string YouTubeMusic = "Chrome._crx_cinhimbnkkghhklpknlkffjgod";

    /// <summary>A Start menu that answers a direct lookup by id and counts how often it was asked.</summary>
    private sealed class Catalog(params LauncherAppInfo[] apps) : ILauncherAppCatalog
    {
        public int Lookups;
        public IReadOnlyList<LauncherAppInfo> Enumerate() => apps;
        public bool Exists(string aumid) => apps.Any(a => a.Aumid == aumid);
        public BitmapSource? Icon(string aumid, int px) => null;
        public string? Name(string aumid)
        {
            Interlocked.Increment(ref Lookups);
            return apps.FirstOrDefault(a => a.Aumid == aumid)?.Name;
        }
    }

    [Fact]
    public void A_web_app_id_reads_as_its_Start_menu_name_before_any_listing()
    {
        var catalog = new Catalog(new LauncherAppInfo("YouTube Music", YouTubeMusic));
        using var cache = new LauncherAppCache(() => catalog);

        Assert.Equal("YouTube Music", cache.StartMenuName(YouTubeMusic));
        Assert.Equal("YouTube Music", NowPlayingApps.DisplayName(YouTubeMusic, cache.StartMenuName));
        Assert.Equal("Playing in YouTube Music",
            NowPlayingStatus.Text(true, new NowPlayingInfo { Title = "t", Playing = true, AppId = YouTubeMusic }, cache.StartMenuName));
    }

    [Fact]
    public void An_id_the_Start_menu_does_not_list_falls_back_and_is_asked_once()
    {
        var catalog = new Catalog(new LauncherAppInfo("YouTube Music", YouTubeMusic));
        using var cache = new LauncherAppCache(() => catalog);

        Assert.Null(cache.StartMenuName("Spotify.exe"));
        Assert.Null(cache.StartMenuName("Spotify.exe"));
        Assert.Equal(1, catalog.Lookups);
        Assert.Equal("Spotify", NowPlayingApps.DisplayName("Spotify.exe", cache.StartMenuName));
    }

    [Fact]
    public void A_listed_app_is_named_from_the_list_without_asking_the_shell()
    {
        var catalog = new Catalog(new LauncherAppInfo("YouTube Music", YouTubeMusic));
        using var cache = new LauncherAppCache(() => catalog);
        cache.Refresh([], force: true);
        for (int i = 0; i < 200 && !cache.Known; i++) Thread.Sleep(25);
        Assert.True(cache.Known);

        Assert.Equal("YouTube Music", cache.StartMenuName(YouTubeMusic.ToUpperInvariant()));
        Assert.Equal(0, catalog.Lookups);
    }
}
