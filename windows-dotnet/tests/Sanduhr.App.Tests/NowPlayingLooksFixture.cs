using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Sanduhr.App.Tests;

/// <summary>Scratch AppData and the helpers the song look tests share.</summary>
public abstract class NowPlayingLooksFixture : IDisposable
{
    protected static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-looks-app-" + Guid.NewGuid().ToString("N")[..8]);
    protected readonly Paths _paths;
    protected readonly SettingsStore _store;

    protected NowPlayingLooksFixture()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root);
        _store = new SettingsStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    protected static NowPlayingInfo Track(string title = "Blinding Lights", string artist = "The Weeknd", bool playing = true) => new()
    {
        Title = title, Artist = artist, Duration = 200, Elapsed = 50, ElapsedAt = Now, Playing = playing, AppId = "Spotify.exe",
    };

    protected static SongLook Song(string title, string artist = "The Weeknd", string[]? ink = null, LookStyle style = LookStyle.Bold, string? mood = "neon") =>
        new(SongKey.Of(title, artist)!, title, artist, new NowPlayingLook(ink ?? ["#ff2a6d", "#05d9e8"], style, mood));

    internal NowPlayingLookService Service() => new(_paths, _store, hub: null, context: null, now: () => Now);

    protected void Style(bool on, bool direct = false) =>
        _store.SaveNowPlaying(_store.LoadNowPlaying() with { Style = on, LooksDirect = direct });
}
