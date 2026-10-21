using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// Runs the Desk (spec 5.1, 6): off until <c>desk_enabled</c> is switched on. On, it builds a
/// <see cref="DeskView"/> per screen from the saved layout, look and content and hands them to the
/// <see cref="IDeskHost"/>, which owns the windows. A full redraw happens on the minute, on a new
/// reading, a track change, a weather reading, a message change, a setting change, or when the screens
/// or wallpaper change; between them the live pieces (telemetry, the position bar, the bars) update in
/// place. Each source runs only while its piece is placed and the Desk shows: telemetry samples, the
/// peak meter, the media-session listener and the weather fetch all stop otherwise. Off, at Reset and
/// at quit, everything stops. Arrange mode (spec 7.3) takes the screens over for its own surfaces while
/// it runs: the islands come down, nothing is laid out for the host, and Done writes the layout once.
/// The controller itself touches no HWND; the host and <see cref="DeskArrangeMode"/> own them.
/// </summary>
internal sealed class DeskController : IDisposable, IDeskClickActions
{
    private readonly SettingsStore _store;
    private readonly Func<IDeskHost> _hostFactory;
    private readonly Func<DeskUsage> _usageSource;
    private readonly NowPlayingHub _nowPlaying;
    private readonly DeskMessageSource _messages;
    private readonly WatcherStore? _watchers;
    private readonly DispatcherTimer _minute;
    private readonly TelemetrySource _telemetry = new();
    private readonly AudioLevelSource _audio = new();
    private readonly WeatherService _weather = new();
    private readonly WallpaperInk _wallpaper = new();
    private IDeskHost? _host;
    private DeskFonts? _fonts;
    private DeskSettings _settings = DeskSettings.Default;
    private DeskInkSettings _ink = DeskInkSettings.Default;
    private NowPlayingSettings _np = NowPlayingSettings.Default;
    private DeskWeatherSettings _weatherSettings = DeskWeatherSettings.Default;
    private DeskUsage _usage = DeskUsage.Empty;
    private readonly List<DeskView> _views = [];
    /// <summary>Message lines whose once-only motion has run (spec 6.2), kept across redraws.</summary>
    private readonly HashSet<string> _played = new(StringComparer.Ordinal);
    private bool _rendering;
    private IReadOnlyList<DeskSurface> _surfaces = [];
    private DateTimeOffset _fullAt = DateTimeOffset.MinValue;

    /// <summary>How often the minute's redraw lays the screens out again (the meters' pace tick, the
    /// stale dim, reset words, watcher times); in between only the clock updates in place, so the
    /// minute repaints one island instead of every one.</summary>
    private static readonly TimeSpan FullEvery = TimeSpan.FromMinutes(5);
    private string? _trackKey;
    private bool _stopped;
    private DeskArrangeSession? _session;
    private DeskArrangeMode? _arrangeMode;

    public DeskController(SettingsStore store, Func<IDeskHost> hostFactory, Func<DeskUsage> usageSource,
        NowPlayingHub nowPlaying, DeskMessageSource messages, WatcherStore? watchers = null)
    {
        _store = store;
        _hostFactory = hostFactory;
        _usageSource = usageSource;
        _nowPlaying = nowPlaying;
        _messages = messages;
        _watchers = watchers;
        _minute = new DispatcherTimer(DispatcherPriority.Background);
        _minute.Tick += (_, _) => OnMinute();
        _weather.Updated += () => Render();
        _messages.Changed += () => Render();
        if (_watchers is not null) _watchers.Changed += () => Render();
        _nowPlaying.Changed += OnTrackChanged;
    }

    /// <summary>True when the Watchers piece has a place on the Desk (the watcher glow fires only then).</summary>
    internal bool WatchersPlaced => Placed("watchers");

    /// <summary>The cards to draw: the board's, when the piece is placed.</summary>
    private IReadOnlyList<Watcher> WatchersNow() => _watchers is not null && Placed("watchers") ? _watchers.Shown : [];

    private bool _piecesTakeClicks = DeskPieceClicks.DefaultOn;

    /// <summary>A Desk menu item was chosen (spec 7.4); the app opens the page, arranges or turns the Desk off.</summary>
    public event Action<DeskMenuAction>? MenuRequested;

    public void PlayPause() => _nowPlaying.Service.TogglePlayPause();

    public void OpenWeatherCredit()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DeskClicks.WeatherCreditUrl) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No browser to open: nothing else to do.
        }
    }

    public void Menu(DeskMenuAction action) => MenuRequested?.Invoke(action);

    /// <summary>The system readings are sampling now (the live check reads it).</summary>
    internal bool TelemetryRunning => _telemetry.Running;

    /// <summary>The bars' level source is running now (the live check reads it).</summary>
    internal bool AudioRunning => _audio.Running;

    /// <summary>The arrange surfaces while Arrange mode runs (the live check reads them).</summary>
    internal DeskArrangeMode? ArrangeMode => _arrangeMode;

    /// <summary>True while the Desk is on screen.</summary>
    public bool Running => _host?.IsShowing == true;

    /// <summary>True while Arrange mode is up.</summary>
    public bool Arranging => _session is not null;

    /// <summary>The running Arrange edit (its working layout), null when none runs.</summary>
    public DeskArrangeSession? Session => _session;

    /// <summary>Raised when Arrange mode begins, after each drop and when it ends.</summary>
    public event Action? ArrangeChanged;

    /// <summary>Supplies the live widget theme's text color, for Theme ink. Null: Light ink.</summary>
    public Func<string?> ThemeText { get; set; } = () => null;

    /// <summary>Read the settings again and start, rebuild or stop to match.</summary>
    public void Reload()
    {
        if (_stopped) return;
        _settings = _store.LoadDesk();
        _ink = _store.LoadDeskInk();
        _np = _store.LoadNowPlaying();
        _weatherSettings = _store.LoadDeskWeather();
        _piecesTakeClicks = _store.LoadDeskPiecesTakeClicks();
        _messages.Reload();
        if (!_settings.Enabled)
        {
            StopHost(); // ends Arrange mode too, as Cancel
            return;
        }
        if (_host is null)
        {
            _host = _hostFactory();
            _host.ScreensChanged += OnScreensChanged;
            _host.CoverageChanged += OnCoverageChanged;
        }
        Rebuild();
    }

    /// <summary>The widget's theme changed: Theme ink follows it.</summary>
    public void ThemeChanged()
    {
        if (_ink.Mode == DeskInkMode.Theme) Render();
    }

    /// <summary>The widget has new numbers (after each render and sign-in change). Read only while the
    /// Desk is on, so an off Desk costs nothing.</summary>
    public void UsageChanged()
    {
        if (_host is null || _stopped) return;
        _usage = _usageSource();
        Render();
    }

    /// <summary>Reset Sanduhr…: the Desk comes down and stays down until Sanduhr quits.</summary>
    public void StopForReset()
    {
        _stopped = true;
        StopHost();
    }

    public void Dispose()
    {
        _stopped = true;
        StopHost();
        _nowPlaying.Changed -= OnTrackChanged;
        _audio.Dispose();
        _weather.Dispose();
    }

    /// <summary>
    /// Lights the glow's Desk spot in <paramref name="color"/> (spec 7.3). True only when the Desk is
    /// running, the spot is placed and at least one island holding it is in sight; then every screen's
    /// spot lights. False means the caller shows the glow at the window in use instead.
    /// </summary>
    public bool Glow(Color color)
    {
        if (_host is null || _stopped || Arranging || !Running || !Placed("glow")) return false;
        bool inSight = false, any = false;
        foreach (var surface in _surfaces)
            foreach (var island in surface.Islands)
            {
                if (!island.Widgets.Contains("glow")) continue;
                any = true;
                if (!island.Pause.Paused) inSight = true;
            }
        if (!any || !inSight) return false;
        bool lit = false;
        foreach (var view in _views) lit |= view.Glow(color);
        return lit;
    }

    // ---- Arrange mode (spec 7.3) ----

    /// <summary>
    /// Starts Arrange mode: only while the Desk is running. The islands come down, a surface per monitor
    /// goes up with the saved layout as the working one, and nothing is saved until Done.
    /// </summary>
    public void BeginArrange()
    {
        // Not tied to Running: a Desk that is on but draws nothing yet (an empty message, no numbers) can
        // still be arranged, since arrange mode draws every placed piece.
        if (_host is null || _stopped || Arranging || _screens.Count == 0) return;
        _fonts ??= new DeskFonts();
        var session = new DeskArrangeSession(_settings.Layout);
        var now = DateTimeOffset.Now;
        var track = _np.Prefs.Visible(_nowPlaying.Shown);
        var frame = new DeskFrame
        {
            Usage = _usage,
            Now = now,
            Message = _messages.Frame(now, _settings) is { } m ? m with { Played = new HashSet<string>(m.Played, StringComparer.Ordinal) } : null,
            Track = track,
            Telemetry = Placed("telemetry") ? _telemetry : null,
            WeatherOn = Placed("weather"),
            Place = _weatherSettings.Place,
            Weather = _weatherSettings.Place is null ? null : _weather.Latest,
            Watchers = WatchersNow(),
        };
        DeskArrangeMode mode;
        try
        {
            mode = new DeskArrangeMode(_screens, _settings, _fonts, frame, session.Working,
                how => EndArrange(how), () => ArrangeChanged?.Invoke());
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            System.Diagnostics.Debug.WriteLine($"[Sanduhr] Arrange mode did not start: {e.GetType().Name}");
            return;
        }
        _session = session;
        _arrangeMode = mode;
        // The islands go down while the arrange surfaces are up; coverage and sources have nothing to follow.
        foreach (var v in _views) v.Dispose();
        _views.Clear();
        _surfaces = [];
        _host.Hide();
        // The arrange view has no bars; the readings it shows keep sampling.
        SyncSources(track);
        mode.Activate();
        ArrangeChanged?.Invoke();
    }

    /// <summary>
    /// Ends Arrange mode. Done writes the working layout once, when it changed; Cancel writes nothing.
    /// Either way the arrange windows close and the islands come back with whatever layout is saved.
    /// </summary>
    public void EndArrange(DeskArrangeEnd how) => EndArrangeCore(how, reload: true);

    private void EndArrangeCore(DeskArrangeEnd how, bool reload)
    {
        if (_session is not { } session) return;
        // Done waits while every drawn piece is hidden; Cancel (and a forced end) still ends the mode.
        if (how == DeskArrangeEnd.Done && reload && !session.CanFinish(DeskView.Drawn)) return;
        var mode = _arrangeMode;
        _arrangeMode = null;
        _session = null;
        mode?.Dispose();
        if (how == DeskArrangeEnd.Done && session.ToWrite is { } layout && !_stopped)
        {
            try { _store.SaveDesk((_store.LoadDesk() with { Layout = layout }).Normalized()); }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"[Sanduhr] Arrange could not save: {e.GetType().Name}");
            }
        }
        if (reload) Reload();
        ArrangeChanged?.Invoke();
    }

    private void OnScreensChanged()
    {
        _wallpaper.Invalidate();
        // The arrange windows sit on the old monitors: drop the edit and lay out on the new ones.
        if (Arranging) EndArrangeCore(DeskArrangeEnd.Cancel, reload: false);
        Rebuild();
    }

    private void Rebuild()
    {
        if (_host is null || _stopped) return;
        _screens = _host.Screens();
        _usage = _usageSource();
        Render();
        ScheduleMinute();
    }

    private IReadOnlyList<DeskScreen> _screens = [];

    private bool Placed(string widget) => new DeskArrangement(_settings.Layout).Placement(widget) is not null;

    /// <summary>A widget's reading is wanted when its piece is placed and at least one island holding it
    /// is in sight. With no islands yet nothing is paused, so it is wanted (S7, 7.1).</summary>
    private bool InSight(string widget)
    {
        if (!Placed(widget)) return false;
        bool any = false;
        foreach (var surface in _surfaces)
            foreach (var island in surface.Islands)
            {
                if (!island.Widgets.Contains(widget)) continue;
                any = true;
                if (!island.Pause.Paused) return true;
            }
        return !any;
    }

    /// <summary>Starts the sources whose pieces are placed and in sight, and stops the rest.</summary>
    private void SyncSources(NowPlayingInfo? track)
    {
        bool on = _host is not null && !_stopped;
        if (on && InSight("telemetry")) _telemetry.Start(); else _telemetry.Stop();
        _nowPlaying.Want(this, on && Placed("nowPlaying"));
        bool bars = on && !Arranging && _np.Bars && InSight("nowPlaying") && track?.State == NowPlayingState.Playing;
        _audio.Follow(track?.AppId);
        _audio.Follow(track?.AppId);
        if (bars) _audio.Start(); else if (_audio.Running) _audio.Stop();
        if (on && Placed("weather") && _weatherSettings.Place is { } place)
            _weather.Start(place, _weatherSettings.UnitOr(RegionInfo.CurrentRegion.IsMetric));
        else _weather.Stop();
    }

    /// <summary>Sets each island's gate from the host's coverage. Islands the host has no flag for are in
    /// sight. A gate raises its change only when its value moves.</summary>
    private void ApplyCoverage()
    {
        var covered = _host?.Coverage ?? [];
        int i = 0;
        foreach (var surface in _surfaces)
            foreach (var island in surface.Islands)
            {
                island.Pause.Paused = DeskPauseRule.Paused(i < covered.Count && covered[i], Arranging);
                i++;
            }
    }

    /// <summary>Windows moved over or away from the Desk: pause or resume the islands and the sources
    /// that feed them. Nothing is laid out again.</summary>
    private void OnCoverageChanged()
    {
        if (_host is null || _stopped) return;
        ApplyCoverage();
        SyncSources(_np.Prefs.Visible(_nowPlaying.Shown));
    }

    /// <summary>A full redraw is due only when what the line says changes (the track, its app or
    /// play state); the position anchor moving is drawn by the piece's own tick.</summary>
    private void OnTrackChanged()
    {
        if (_host is null || _stopped) return;
        var t = _np.Prefs.Visible(_nowPlaying.Shown);
        var key = t is null ? null : $"{t.State}|{t.Title}|{t.Artist}|{t.AppId}|{t.Duration}";
        if (key == _trackKey) return;
        Render();
    }

    /// <summary>Lays each screen out afresh and hands the host its islands; the host keeps its windows
    /// when the islands line up with the last ones.</summary>
    private void Render()
    {
        // While arranging the arrange surfaces draw; the islands come back when it ends.
        if (_host is null || _stopped || _screens.Count == 0 || _rendering || Arranging) return;
        _rendering = true;
        try { RenderOnce(); }
        finally { _rendering = false; }
    }

    private void RenderOnce()
    {
        _fonts ??= new DeskFonts();
        var now = DateTimeOffset.Now;
        var track = _np.Prefs.Visible(_nowPlaying.Shown);
        _trackKey = track is null ? null : $"{track.State}|{track.Title}|{track.Artist}|{track.AppId}|{track.Duration}";
        SyncSources(track);
        var frame = new DeskFrame
        {
            Usage = _usage,
            Now = now,
            Message = PlayedFor(_messages.Frame(now, _settings)),
            TrackNow = () => _np.Prefs.Visible(_nowPlaying.Shown),
            Track = track,
            Bars = _np.Bars,
            Audio = _audio,
            Telemetry = Placed("telemetry") ? _telemetry : null,
            WeatherOn = Placed("weather"),
            Place = _weatherSettings.Place,
            Weather = _weatherSettings.Place is null ? null : _weather.Latest,
            Watchers = WatchersNow(),
        };
        _fullAt = now;
        foreach (var old in _views) old.Dispose();
        _views.Clear();
        var surfaces = new List<DeskSurface>();
        foreach (var screen in _screens)
        {
            var view = new DeskView(screen, _settings, _fonts);
            view.Update(frame, InkFor(view, screen, frame));
            _views.Add(view);
            surfaces.Add(new DeskSurface(screen, DeskClicks.Prepare(view.TakeIslands(), _piecesTakeClicks, this)));
        }
        _surfaces = surfaces;
        if (_host is not { } host) return;
        try
        {
            host.Show(surfaces);
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // A window went away under us (Explorer restarting mid-redraw): start over with new ones
            // on the next rebuild rather than let a timer tick take the app down.
            System.Diagnostics.Debug.WriteLine($"[Sanduhr] Desk redraw failed: {e.GetType().Name}");
            host.Hide();
            return;
        }
        // The new islands start in sight: take the host's reading and let the sources follow it.
        ApplyCoverage();
        SyncSources(track);
    }

    /// <summary>
    /// The inks for one screen (spec 6.7). Every mode but Automatic is one ink for the screen. Automatic
    /// lays the screen out once in Light ink, reads the wallpaper's brightness behind each stack (from the
    /// wallpaper file, never the screen), and the view draws each stack in the ink that reading picks.
    /// </summary>
    private DeskInkMap InkFor(DeskView view, DeskScreen screen, DeskFrame frame)
    {
        if (_ink.Mode != DeskInkMode.Auto)
            return new DeskInkMap(DeskInk.Resolve(_ink, ThemeText(), null));
        // The sampling pass draws nothing anyone sees: give its message a scratch copy of the played set
        // so the drawn pass still writes a new line in.
        var scratch = frame.Message is { } m ? frame with { Message = m with { Played = new HashSet<string>(m.Played, StringComparer.Ordinal) } } : frame;
        view.Update(scratch, DeskInkMap.Light);
        var by = new Dictionary<DeskAnchor, ResolvedInk>();
        foreach (var (anchor, r) in view.StackRects())
        {
            var px = new PxRect(
                screen.Bounds.X + (int)Math.Round(r.X * screen.Scale), screen.Bounds.Y + (int)Math.Round(r.Y * screen.Scale),
                Math.Max(1, (int)Math.Round(r.Width * screen.Scale)), Math.Max(1, (int)Math.Round(r.Height * screen.Scale)));
            by[anchor] = DeskInk.Resolve(_ink, ThemeText(), _wallpaper.Luminance(screen, px));
        }
        return new DeskInkMap(DeskInk.Resolve(_ink, ThemeText(), null), by);
    }

    /// <summary>Hands the message its played set, forgetting lines no longer showing (one that comes back
    /// later writes itself in again, as on the Mac when a line appears).</summary>
    private DeskMessageFrame? PlayedFor(DeskMessageFrame? frame)
    {
        if (frame is null) return null;
        var showing = new HashSet<string>(frame.Today.Lines, StringComparer.Ordinal);
        _played.RemoveWhere(k => !showing.Contains(k[(k.IndexOf('\n') + 1)..]));
        return frame with { Played = _played };
    }

    /// <summary>The clock shows h:mm, so it redraws once a minute, on the minute (no render loop).</summary>
    private void ScheduleMinute()
    {
        var now = DateTime.Now;
        var next = now.Date.AddHours(now.Hour).AddMinutes(now.Minute + 1);
        _minute.Interval = next - now + TimeSpan.FromMilliseconds(50);
        _minute.Start();
    }

    private void OnMinute()
    {
        _minute.Stop();
        if (_host is null || _stopped) return;
        if (Arranging)
        {
            ScheduleMinute();
            return;
        }
        var now = DateTimeOffset.Now;
        // A full layout every five minutes, and on the hour (hourly rotation, special-day windows, a new day).
        if (now - _fullAt >= FullEvery || _surfaces.Count == 0 || now.ToLocalTime().Hour != _fullAt.ToLocalTime().Hour
            || now.ToLocalTime().Date != _fullAt.ToLocalTime().Date)
            Render();
        else
        {
            foreach (var v in _views) v.Tick(now);
            try { _host.Show(_surfaces); }
            catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                System.Diagnostics.Debug.WriteLine($"[Sanduhr] Desk tick failed: {e.GetType().Name}");
                Render();
            }
        }
        ScheduleMinute();
    }

    private void StopHost()
    {
        if (Arranging) EndArrangeCore(DeskArrangeEnd.Cancel, reload: false);
        _minute.Stop();
        _screens = [];
        foreach (var v in _views) v.Dispose();
        _views.Clear();
        _surfaces = [];
        _telemetry.Stop();
        if (_audio.Running) _audio.Stop();
        _weather.Stop();
        _nowPlaying.Want(this, false);
        if (_host is null) return;
        _host.ScreensChanged -= OnScreensChanged;
        _host.CoverageChanged -= OnCoverageChanged;
        _host.Dispose();
        _host = null;
    }
}

/// <summary>
/// One <see cref="NowPlayingService"/> shared by the widget line and the Desk piece: it runs while
/// either wants it and stops when neither does, so the media-session listener costs nothing unused.
/// It also owns the one <see cref="NowPlayingPresence"/>: the piece is up while the media session is
/// playing (or paused, with "Keep showing while paused") and fades out after a 5 s grace, so the widget
/// line and the Desk piece come and go together. The session's state is the only input; audio levels
/// and system sounds never reach it.
/// </summary>
internal sealed class NowPlayingHub : IDisposable
{
    private readonly NowPlayingService _service;
    private readonly HashSet<object> _wanted = [];
    private readonly DispatcherTimer _grace;
    private NowPlayingPrefs _prefs = new();

    /// <summary>The app's one hub (Settings, Now playing reads the apps seen from it); null in tests and the harness.</summary>
    public static NowPlayingHub? Current { get; set; }

    public NowPlayingHub(NowPlayingService? service = null)
    {
        _service = service ?? new NowPlayingService();
        _service.Changed += () => Changed?.Invoke();
        _service.Observed += Evaluate;
        _grace = new DispatcherTimer(DispatcherPriority.Background);
        _grace.Tick += (_, _) => { _grace.Stop(); Evaluate(); };
    }

    public NowPlayingService Service => _service;
    public NowPlayingInfo? Shown => _service.Shown;

    /// <summary>Whether the piece is up, and how far (0 to 1) for the fade and the gap.</summary>
    public NowPlayingPresence Presence { get; } = new();

    /// <summary>The shown track changed.</summary>
    public event Action? Changed;

    /// <summary>The piece should now be up or gone (<see cref="NowPlayingPresence.Wanted"/> flipped).
    /// The amount then moves on its own; read <see cref="NowPlayingPresence.Amount"/> per frame.</summary>
    public event Action? PresenceChanged;

    /// <summary>The Now playing settings the presence rule follows (excluded apps, keep showing while
    /// paused). The widget line and the Desk both pass the saved ones.</summary>
    public void Configure(NowPlayingPrefs prefs)
    {
        _prefs = prefs;
        Evaluate();
    }

    /// <summary><paramref name="who"/> wants the service running (or not).</summary>
    public void Want(object who, bool want)
    {
        bool changed = want ? _wanted.Add(who) : _wanted.Remove(who);
        if (!changed) return;
        if (_wanted.Count > 0) _service.Start();
        else
        {
            _service.Stop();
            _grace.Stop();
            bool was = Presence.Wanted;
            Presence.Reset();
            if (was) PresenceChanged?.Invoke();
        }
    }

    private void Evaluate()
    {
        if (!_service.Running)
        {
            _grace.Stop();
            return;
        }
        // Windows' "Show animations" off: the piece snaps in and out.
        Presence.Animate = System.Windows.SystemParameters.ClientAreaAnimation;
        bool was = Presence.Wanted;
        var now = DateTimeOffset.Now;
        var current = _service.Current;
        TimeSpan? wait = null;
        // An app the user turned off goes at once; anything else that stops waits out the grace.
        if (current.State != NowPlayingState.None && _prefs.IsExcluded(current.AppId)) Presence.Dismiss(now);
        else wait = Presence.Observe(_prefs.IsActive(current), now);
        _grace.Stop();
        if (wait is { } w)
        {
            _grace.Interval = w < TimeSpan.FromMilliseconds(10) ? TimeSpan.FromMilliseconds(10) : w;
            _grace.Start();
        }
        if (was != Presence.Wanted) PresenceChanged?.Invoke();
    }

    public void Dispose()
    {
        _grace.Stop();
        _service.Dispose();
    }
}
