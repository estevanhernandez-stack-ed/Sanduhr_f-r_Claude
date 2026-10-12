using System.Windows;
using System.Windows.Threading;
using Sanduhr.Core;
using Windows.Media.Control;

namespace Sanduhr.App.Services;

/// <summary>
/// Now playing (spec 6.3, W8) from Windows' media sessions
/// (<c>GlobalSystemMediaTransportControlsSessionManager</c>: public, no capability for a full-trust
/// app; Spotify, browsers and most players publish to it). Event driven: it listens to the current
/// session's changes and never polls. Started only while the widget line is on or the Desk shows the
/// piece. <b>Titles, artists and app ids live in this object's memory only: never written to a file,
/// a log or settings.</b> The tracker keeps a skip from blinking the line.
/// </summary>
internal sealed class NowPlayingService : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _settle;
    private readonly NowPlayingTracker _tracker = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
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

    /// <summary>Apps seen playing this session, first seen first (Settings' app list). Memory only.</summary>
    public IReadOnlyList<string> SeenApps { get; private set; } = [];

    /// <summary>The shown track changed (title, state or position anchor).</summary>
    public event Action? Changed;

    public bool Running => _running;

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
            Attach(_manager.GetCurrentSession());
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
        if (_manager is not null) _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        _manager = null;
        Detach();
        _settle.Stop();
        _tracker.Reset();
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
        _ui.BeginInvoke(() => { if (_running && _manager is not null) Attach(_manager.GetCurrentSession()); });

    private void Attach(GlobalSystemMediaTransportControlsSession? session)
    {
        Detach();
        _session = session;
        if (session is not null)
        {
            session.MediaPropertiesChanged += OnSessionEvent;
            session.PlaybackInfoChanged += OnSessionEvent;
            session.TimelinePropertiesChanged += OnSessionEvent;
        }
        Refresh();
    }

    private void Detach()
    {
        if (_session is null) return;
        _session.MediaPropertiesChanged -= OnSessionEvent;
        _session.PlaybackInfoChanged -= OnSessionEvent;
        _session.TimelinePropertiesChanged -= OnSessionEvent;
        _session = null;
    }

    private void OnSessionEvent(GlobalSystemMediaTransportControlsSession sender, object args) =>
        _ui.BeginInvoke(() => { if (_running && ReferenceEquals(sender, _session)) Refresh(); });

    private async void Refresh()
    {
        int gen = _generation;
        var session = _session;
        NowPlayingInfo info;
        try
        {
            if (session is null)
            {
                info = new NowPlayingInfo();
            }
            else
            {
                var props = await session.TryGetMediaPropertiesAsync();
                if (gen != _generation || !ReferenceEquals(session, _session)) return;
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
        }
        catch (Exception e)
        {
            // A player that closed mid-read: treat as nothing playing.
            System.Diagnostics.Debug.WriteLine($"[Sanduhr] media session read failed: {e.GetType().Name}");
            info = new NowPlayingInfo();
        }
        if (gen != _generation) return;
        if (info.State != NowPlayingState.None) SeenApps = NowPlayingApps.Seen(SeenApps, info.AppId);
        var before = _tracker.Shown;
        Schedule(_tracker.Replace(info, DateTimeOffset.Now));
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
