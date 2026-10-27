using System.IO;
using System.Windows;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>What the Desk's now-playing line draws in: the gradient stops (six-digit hex, OLED care's white cap already applied) and the letter style.</summary>
internal sealed record NowPlayingLookDraw(IReadOnlyList<string> Stops, LookStyle Style);

/// <summary>
/// A look per song, the app side (spec, a look per song). Three jobs:
/// <list type="bullet">
/// <item><description>Gives the Desk the look a song wears while "Style what's playing" is on: the saved look for its key, else the one seeded from
/// the key (<see cref="DrawFor"/>).</description></item>
/// <item><description>Holds the saved looks and Claude's waiting suggestion (<see cref="NowPlayingLookHandoff"/>), and watches
/// <c>%USERPROFILE%\.sanduhr</c> for <c>now-playing-looks-request.json</c> (the MCP tool): a card in Settings, Now playing, or saved directly when
/// "Let Claude style songs directly" is on.</description></item>
/// <item><description>The bridge with the now-playing mod (<c>NowPlayingLookService.Bridge.cs</c>): <c>now-playing-look.json</c> and the mod's
/// <c>now-playing-look-offer.json</c>.</description></item>
/// </list>
/// A title is held in memory only for the song playing, and on disk only inside a look the user kept (PRIVACY.md).
/// </summary>
internal sealed partial class NowPlayingLookService : IDisposable
{
    /// <summary>The app's one service; null in tests and the harness.</summary>
    public static NowPlayingLookService? Current { get; set; }

    private readonly SettingsStore _store;
    private readonly NowPlayingHub? _hub;
    private FileSystemWatcher? _requestWatcher;
    private bool _started;
    private bool _disposed;

    /// <param name="context">What the widget's own check of a look knows about this PC (the wallpaper, for the contrast rule); null skips that rule.</param>
    public NowPlayingLookService(Paths paths, SettingsStore store, NowPlayingHub? hub = null,
        Func<DeskDesignContext?>? context = null, Func<DateTimeOffset>? now = null)
    {
        _store = store;
        _hub = hub;
        Handoff = new NowPlayingLookHandoff(new NowPlayingLookPaths(paths.ExternalDir, paths.NowPlayingLooksFile),
            styleOn: () => store.LoadNowPlaying().Style, direct: () => store.LoadNowPlaying().LooksDirect,
            context: context, now: now, canWrite: () => !ResetGuard.Done);
        Handoff.BookChanged += OnBookChanged;
        Handoff.PendingChanged += () => PendingChanged?.Invoke();
    }

    /// <summary>The shared media-session hub the bridge listens through; null in tests and the harness.</summary>
    internal NowPlayingHub? Hub => _hub;

    /// <summary>The saved looks, the waiting suggestion and the files.</summary>
    public NowPlayingLookHandoff Handoff { get; }

    /// <summary>What the Desk draws changed (the saved looks changed): it redraws.</summary>
    public event Action? Changed;

    /// <summary>A suggestion arrived, grew or was decided (Settings shows or drops its card).</summary>
    public event Action? PendingChanged;

    /// <summary>The saved looks changed (the count in Settings).</summary>
    public event Action? BookChanged;

    /// <summary>"Style what's playing" is on.</summary>
    public bool StyleOn => _store.LoadNowPlaying().Style;

    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;
        var dir = Handoff.Paths.Support;
        try
        {
            Directory.CreateDirectory(dir);
            _requestWatcher = Watch(dir, NowPlayingLookProposal.RequestFile);
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // No watcher: a request waits (for up to its ten minutes) until the next launch picks it up.
            _requestWatcher?.Dispose();
            _requestWatcher = null;
        }
        Check();
        StartBridge();
    }

    /// <summary>Watches one file in the external folder; a change takes whatever waits there.</summary>
    private FileSystemWatcher Watch(string dir, string file)
    {
        var w = new FileSystemWatcher(dir, file) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };   // streamer: notname - NotifyFilters.FileName is a watcher flag, not a name
        w.Created += (_, _) => Check();
        w.Changed += (_, _) => Check();
        w.Renamed += (_, _) => Check();
        w.Error += (_, _) => Check();
        w.EnableRaisingEvents = true;
        return w;
    }

    private void Check()
    {
        if (_disposed) return;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (!_disposed && !ResetGuard.Done) Handoff.Check();
        });
    }

    private void OnBookChanged()
    {
        BookChanged?.Invoke();
        Changed?.Invoke();
        SyncBridge();
    }

    /// <summary>Settings, Now playing, Looks saved a switch (the Desk redraws through its own reload): the file for the mod follows.</summary>
    public void SettingsChanged() => SyncBridge();

    /// <summary>Brings the file for the mod in line with the switches and the song (see the bridge).</summary>
    public void Sync() => SyncBridge();

    /// <summary>The look the playing song wears now: its saved look, else the one seeded from its key. Null when nothing with a title plays.</summary>
    public (string Key, NowPlayingLook Look, LookSource Source)? Resolve(NowPlayingInfo? track) =>
        track is null || track.State == NowPlayingState.None
            ? null
            : NowPlayingLooks.Resolve(Handoff.Book, track.Title, track.Artist);

    /// <summary>What the Desk's line draws in for this track: the look's stops (white capped when OLED care is on) and style. Null with Style off.</summary>
    public NowPlayingLookDraw? DrawFor(NowPlayingInfo? track, bool capWhite)
    {
        if (!StyleOn || Resolve(track) is not { } r) return null;
        return new NowPlayingLookDraw(NowPlayingLooks.DrawStops(r.Look, capWhite), r.Look.Style);
    }

    // The bridge with the now-playing mod lives in NowPlayingLookService.Bridge.cs. Without it these do nothing.
    partial void StartBridge();
    partial void SyncBridge();
    partial void StopBridge();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopBridge();
        _requestWatcher?.Dispose();
        _requestWatcher = null;
    }
}
