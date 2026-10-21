using System.Windows;
using System.Windows.Threading;
using Sanduhr.Core;
using Windows.Media.Control;

namespace Sanduhr.App.Services;

/// <summary>
/// Now playing (spec 6.3, W8) from Windows' media sessions
/// (<c>GlobalSystemMediaTransportControlsSessionManager</c>: public, no capability for a full-trust
/// app; Spotify, browsers and most players publish to it). Event driven, never polls. It watches
/// <b>every</b> session (<c>GetSessions</c> and <c>SessionsChanged</c>, and each session's own
/// playback, media and timeline events), not only the one Windows calls current, and shows the one
/// <see cref="NowPlayingSessionChoice"/> picks: a playing session beats a paused tab that happens to be
/// current. Started only while the widget line is on or the Desk shows the piece. <b>Titles, artists and
/// app ids live in this object's memory only: never written to a file, a log or settings.</b> The tracker
/// keeps a skip from blinking the line.
/// </summary>
internal sealed class NowPlayingService : IDisposable
{
    /// <summary>One media session and what it last said.</summary>
    private sealed class Tracked(GlobalSystemMediaTransportControlsSession session)
    {
        public GlobalSystemMediaTransportControlsSession Session { get; } = session;
        public NowPlayingInfo Info { get; set; } = new();
        public DateTimeOffset? PlayingSince { get; set; }
        public int Seq { get; set; }
    }

    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _settle;
    private readonly NowPlayingTracker _tracker = new();
    private readonly List<Tracked> _tracked = [];
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    /// <summary>The session shown (or, with none chosen, the current one): where clicks go.</summary>
    private GlobalSystemMediaTransportControlsSession? _session;
    private NowPlayingPrefs _prefs = new();
    private bool _running;
    private int _generation;

    public NowPlayingService(Dispatcher? ui = null)
    {
        _ui = ui ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _settle = new DispatcherTimer(DispatcherPriority.Background, _ui);
        _settle.Tick += (_, _) => { _settle.Stop(); Settle(); };
    }

    /// <summary>The track to show (before the user's hide rules), or null.</summary>
    public NowPlayingInfo? Shown => _tracker.Shown;

    /// <summary>The newest reading of the chosen session, before the tracker's clear delay: what the
    /// session says right now (the presence rule reads this, so a stop counts at once).</summary>
    public NowPlayingInfo Current => _tracker.Latest;

    /// <summary>Apps seen playing this session, first seen first (Settings' app list). Memory only.</summary>
    public IReadOnlyList<string> SeenApps { get; private set; } = [];

    /// <summary>The shown track changed (title, state or position anchor).</summary>
    public event Action? Changed;

    /// <summary>The service said something new (any session event, including a stop that the tracker
    /// still holds on screen for its clear delay). <see cref="Current"/> has the newest reading.</summary>
    public event Action? Observed;

    public bool Running => _running;

    /// <summary>The settings the choice follows (excluded apps, keep showing while paused). Chooses again.</summary>
    public void Configure(NowPlayingPrefs prefs)
    {
        _prefs = prefs;
        if (_running) Reselect();
    }

    public async void Start()
    {
        if (_running) return;
        _running = true;
        int gen = ++_generation;
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (gen != _generation || !_running) return;
            _manager = manager;
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            _manager.SessionsChanged += OnSessionsChanged;
            SyncSessions();
        }
        catch (Exception e)
        {
            // Media sessions unavailable (an N edition without Media Feature Pack, a policy): stay quiet.
            System.Diagnostics.Debug.WriteLine($"[Sanduhr] media sessions unavailable: {e.GetType().Name}");
        }
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _generation++;
        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager.SessionsChanged -= OnSessionsChanged;
        }
        _manager = null;
        foreach (var t in _tracked) Detach(t);
        _tracked.Clear();
        _session = null;
        _settle.Stop();
        _tracker.Reset();
        Observed?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>A click on the line: play or pause.</summary>
    public async void TogglePlayPause()
    {
        try { if (_session is { } s) await s.TryTogglePlayPauseAsync(); }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine($"[Sanduhr] play/pause failed: {e.GetType().Name}"); }
    }

    /// <summary>Next, shown while paused.</summary>
    public async void Next()
    {
        try { if (_session is { } s) await s.TrySkipNextAsync(); }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine($"[Sanduhr] next failed: {e.GetType().Name}"); }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        _ui.BeginInvoke(() => { if (_running && _manager is not null) Reselect(); });

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        _ui.BeginInvoke(() => { if (_running && _manager is not null) SyncSessions(); });

    /// <summary>Tracks exactly the sessions Windows lists now: new ones get their handlers and a first
    /// read, gone ones have their handlers detached.</summary>
    private void SyncSessions()
    {
        IReadOnlyList<GlobalSystemMediaTransportControlsSession> now;
        try { now = _manager?.GetSessions() ?? []; }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"[Sanduhr] media sessions list failed: {e.GetType().Name}");
            now = [];
        }
        for (int i = _tracked.Count - 1; i >= 0; i--)
        {
            if (now.Any(s => ReferenceEquals(s, _tracked[i].Session))) continue;
            Detach(_tracked[i]);
            _tracked.RemoveAt(i);
        }
        foreach (var s in now)
        {
            if (_tracked.Any(t => ReferenceEquals(t.Session, s))) continue;
            var t = new Tracked(s);
            _tracked.Add(t);
            s.MediaPropertiesChanged += OnSessionEvent;
            s.PlaybackInfoChanged += OnSessionEvent;
            s.TimelinePropertiesChanged += OnSessionEvent;
            Read(t);
        }
        Reselect();
    }

    private void Detach(Tracked t)
    {
        t.Seq++; // a read in flight is dropped
        t.Session.MediaPropertiesChanged -= OnSessionEvent;
        t.Session.PlaybackInfoChanged -= OnSessionEvent;
        t.Session.TimelinePropertiesChanged -= OnSessionEvent;
    }

    private void OnSessionEvent(GlobalSystemMediaTransportControlsSession sender, object args) =>
        _ui.BeginInvoke(() =>
        {
            if (!_running) return;
            var t = _tracked.Find(x => ReferenceEquals(x.Session, sender));
            if (t is not null) Read(t);
        });

    private async void Read(Tracked t)
    {
        int gen = _generation;
        int seq = ++t.Seq;
        var session = t.Session;
        NowPlayingInfo info;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            // A later event's read started after this one, or the session went away: drop this answer.
            if (gen != _generation || seq != t.Seq) return;
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var status = playback?.PlaybackStatus switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => SessionPlaybackStatus.Playing,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => SessionPlaybackStatus.Paused,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => SessionPlaybackStatus.Stopped,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => SessionPlaybackStatus.Opened,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => SessionPlaybackStatus.Changing,
                _ => SessionPlaybackStatus.Closed,
            };
            info = NowPlayingInfo.FromSession(props?.Title, props?.Artist, props?.AlbumTitle, status,
                playback?.PlaybackRate, timeline?.StartTime ?? TimeSpan.Zero, timeline?.EndTime ?? TimeSpan.Zero,
                timeline?.Position ?? TimeSpan.Zero, timeline?.LastUpdatedTime ?? default,
                session.SourceAppUserModelId, DateTimeOffset.Now);
        }
        catch (Exception e)
        {
            // A player that closed mid-read: treat as nothing playing.
            System.Diagnostics.Debug.WriteLine($"[Sanduhr] media session read failed: {e.GetType().Name}");
            info = new NowPlayingInfo();
        }
        if (gen != _generation || seq != t.Seq) return;

        // When it started playing, for "the most recently started" among several.
        if (info.State == NowPlayingState.Playing)
        {
            if (t.Info.State != NowPlayingState.Playing || t.PlayingSince is null) t.PlayingSince = DateTimeOffset.Now;
        }
        else t.PlayingSince = null;
        t.Info = info;
        if (info.State != NowPlayingState.None) SeenApps = NowPlayingApps.Seen(SeenApps, info.AppId);
        Reselect();
    }

    /// <summary>Picks the session to show (<see cref="NowPlayingSessionChoice"/>) and hands its reading
    /// to the tracker. With none chosen it shows the current session's reading, so a pause is still
    /// drawn during the presence's grace before the piece goes.</summary>
    private void Reselect()
    {
        GlobalSystemMediaTransportControlsSession? current = null;
        try { current = _manager?.GetCurrentSession(); }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine($"[Sanduhr] current session failed: {e.GetType().Name}"); }

        int currentIndex = _tracked.FindIndex(t => ReferenceEquals(t.Session, current));
        if (currentIndex < 0 && current is not null)
        {
            string? id = current.SourceAppUserModelId;
            currentIndex = _tracked.FindIndex(t => t.Session.SourceAppUserModelId == id);
        }

        var candidates = new List<NowPlayingCandidate>(_tracked.Count);
        for (int i = 0; i < _tracked.Count; i++)
            candidates.Add(new NowPlayingCandidate(i, _tracked[i].Info, i == currentIndex, _tracked[i].PlayingSince));

        Tracked? chosen = NowPlayingSessionChoice.Choose(candidates, _prefs) is { } pick
            ? _tracked[pick.Index]
            : currentIndex >= 0 ? _tracked[currentIndex] : null;
        _session = chosen?.Session;

        var before = _tracker.Shown;
        Schedule(_tracker.Replace(chosen?.Info ?? new NowPlayingInfo(), DateTimeOffset.Now));
        Observed?.Invoke();
        if (before != _tracker.Shown) Changed?.Invoke();
    }

    private void Settle()
    {
        var before = _tracker.Shown;
        Schedule(_tracker.Settle(DateTimeOffset.Now));
        if (before != _tracker.Shown) Changed?.Invoke();
    }

    private void Schedule(TimeSpan? wait)
    {
        _settle.Stop();
        if (wait is { } w)
        {
            _settle.Interval = w < TimeSpan.FromMilliseconds(10) ? TimeSpan.FromMilliseconds(10) : w;
            _settle.Start();
        }
    }

    public void Dispose() => Stop();
}
