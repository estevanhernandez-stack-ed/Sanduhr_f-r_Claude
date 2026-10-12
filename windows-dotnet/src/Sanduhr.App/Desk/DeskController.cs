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
/// at quit, everything stops. Nothing here touches an HWND.
/// </summary>
internal sealed class DeskController : IDisposable
{
    private readonly SettingsStore _store;
    private readonly Func<IDeskHost> _hostFactory;
    private readonly Func<DeskUsage> _usageSource;
    private readonly NowPlayingHub _nowPlaying;
    private readonly DeskMessageSource _messages;
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
    private string? _trackKey;
    private bool _stopped;

    public DeskController(SettingsStore store, Func<IDeskHost> hostFactory, Func<DeskUsage> usageSource,
        NowPlayingHub nowPlaying, DeskMessageSource messages)
    {
        _store = store;
        _hostFactory = hostFactory;
        _usageSource = usageSource;
        _nowPlaying = nowPlaying;
        _messages = messages;
        _minute = new DispatcherTimer(DispatcherPriority.Background);
        _minute.Tick += (_, _) => OnMinute();
        _weather.Updated += () => Render();
        _messages.Changed += () => Render();
        _nowPlaying.Changed += OnTrackChanged;
    }

    /// <summary>True while the Desk is on screen.</summary>
    public bool Running => _host?.IsShowing == true;

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
        _messages.Reload();
        if (!_settings.Enabled)
        {
            StopHost();
            return;
        }
        if (_host is null)
        {
            _host = _hostFactory();
            _host.ScreensChanged += OnScreensChanged;
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

    private void OnScreensChanged()
    {
        _wallpaper.Invalidate();
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

    /// <summary>Starts the sources whose pieces are placed and stops the rest.</summary>
    private void SyncSources(NowPlayingInfo? track)
    {
        bool on = _host is not null && !_stopped;
        if (on && Placed("telemetry")) _telemetry.Start(); else _telemetry.Stop();
        _nowPlaying.Want(this, on && Placed("nowPlaying"));
        bool bars = on && _np.Bars && Placed("nowPlaying") && track?.State == NowPlayingState.Playing;
        if (bars) _audio.Start(); else if (_audio.Running) _audio.Stop();
        if (on && Placed("weather") && _weatherSettings.Place is { } place)
            _weather.Start(place, _weatherSettings.UnitOr(RegionInfo.CurrentRegion.IsMetric));
        else _weather.Stop();
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
        if (_host is null || _stopped || _screens.Count == 0) return;
        _fonts ??= new DeskFonts();
        var now = DateTimeOffset.Now;
        var track = _np.Prefs.Visible(_nowPlaying.Shown);
        _trackKey = track is null ? null : $"{track.State}|{track.Title}|{track.Artist}|{track.AppId}|{track.Duration}";
        SyncSources(track);
        var frame = new DeskFrame
        {
            Usage = _usage,
            Now = now,
            Message = _messages.Frame(now, _settings),
            Track = track,
            Bars = _np.Bars,
            Audio = _audio,
            Telemetry = Placed("telemetry") ? _telemetry : null,
            WeatherOn = Placed("weather"),
            Place = _weatherSettings.Place,
            Weather = _weatherSettings.Place is null ? null : _weather.Latest,
        };
        foreach (var old in _views) old.Dispose();
        _views.Clear();
        var surfaces = new List<DeskSurface>();
        foreach (var screen in _screens)
        {
            var view = new DeskView(screen, _settings, _fonts);
            view.Update(frame, InkFor(view, screen, frame));
            _views.Add(view);
            surfaces.Add(new DeskSurface(screen, view.TakeIslands()));
        }
        try
        {
            _host.Show(surfaces);
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // A window went away under us (Explorer restarting mid-redraw): start over with new ones
            // on the next rebuild rather than let a timer tick take the app down.
            System.Diagnostics.Debug.WriteLine($"[Sanduhr] Desk redraw failed: {e.GetType().Name}");
            _host.Hide();
        }
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
        view.Update(frame, DeskInkMap.Light);
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
        Render();
        ScheduleMinute();
    }

    private void StopHost()
    {
        _minute.Stop();
        _screens = [];
        foreach (var v in _views) v.Dispose();
        _views.Clear();
        _telemetry.Stop();
        if (_audio.Running) _audio.Stop();
        _weather.Stop();
        _nowPlaying.Want(this, false);
        if (_host is null) return;
        _host.ScreensChanged -= OnScreensChanged;
        _host.Dispose();
        _host = null;
    }
}

/// <summary>
/// One <see cref="NowPlayingService"/> shared by the widget line and the Desk piece: it runs while
/// either wants it and stops when neither does, so the media-session listener costs nothing unused.
/// </summary>
internal sealed class NowPlayingHub : IDisposable
{
    private readonly NowPlayingService _service;
    private readonly HashSet<object> _wanted = [];

    /// <summary>The app's one hub (Settings, Now playing reads the apps seen from it); null in tests and the harness.</summary>
    public static NowPlayingHub? Current { get; set; }

    public NowPlayingHub(NowPlayingService? service = null)
    {
        _service = service ?? new NowPlayingService();
        _service.Changed += () => Changed?.Invoke();
    }

    public NowPlayingService Service => _service;
    public NowPlayingInfo? Shown => _service.Shown;
    public event Action? Changed;

    /// <summary><paramref name="who"/> wants the service running (or not).</summary>
    public void Want(object who, bool want)
    {
        bool changed = want ? _wanted.Add(who) : _wanted.Remove(who);
        if (!changed) return;
        if (_wanted.Count > 0) _service.Start(); else _service.Stop();
    }

    public void Dispose() => _service.Dispose();
}
