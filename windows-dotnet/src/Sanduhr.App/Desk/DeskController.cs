using System.Windows.Threading;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// Runs the Desk (spec 5.1): off until <c>desk_enabled</c> is switched on. On, it builds a
/// <see cref="DeskView"/> per screen from the saved layout, font, sizes and the widget's latest numbers
/// and hands them to the <see cref="IDeskHost"/>, which owns the windows. It redraws on the minute and
/// on each new reading, and rebuilds when the screens change or a setting does. Off, at Reset and at
/// quit, the host goes away. Nothing here touches an HWND.
/// </summary>
internal sealed class DeskController : IDisposable
{
    private readonly SettingsStore _store;
    private readonly Func<IDeskHost> _hostFactory;
    private readonly DispatcherTimer _minute;
    private IDeskHost? _host;
    private DeskFonts? _fonts;
    private DeskSettings _settings = DeskSettings.Default;
    private DeskUsage _usage = DeskUsage.Empty;
    private readonly List<DeskView> _views = [];
    private bool _stopped;

    public DeskController(SettingsStore store, Func<IDeskHost> hostFactory)
    {
        _store = store;
        _hostFactory = hostFactory;
        _minute = new DispatcherTimer(DispatcherPriority.Background);
        _minute.Tick += (_, _) => OnMinute();
    }

    /// <summary>True while the Desk is on screen.</summary>
    public bool Running => _host?.IsShowing == true;

    /// <summary>Read the settings again and start, rebuild or stop to match.</summary>
    public void Reload()
    {
        if (_stopped) return;
        _settings = _store.LoadDesk();
        if (!_settings.Enabled)
        {
            StopHost();
            return;
        }
        if (_host is null)
        {
            _host = _hostFactory();
            _host.ScreensChanged += Rebuild;
        }
        Rebuild();
    }

    /// <summary>The widget's latest numbers (after each render and sign-in change).</summary>
    public void SetUsage(DeskUsage usage)
    {
        _usage = usage;
        var now = DateTimeOffset.Now;
        foreach (var v in _views) v.Update(_usage, now);
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
    }

    private void Rebuild()
    {
        if (_host is null || _stopped) return;
        _fonts ??= new DeskFonts();
        _views.Clear();
        var now = DateTimeOffset.Now;
        var surfaces = new List<DeskSurface>();
        foreach (var screen in _host.Screens())
        {
            var view = new DeskView(screen, _settings, _fonts);
            view.Update(_usage, now);
            _views.Add(view);
            surfaces.Add(new DeskSurface(screen, view));
        }
        _host.Show(surfaces);
        ScheduleMinute();
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
        var now = DateTimeOffset.Now;
        foreach (var v in _views) v.Update(_usage, now);
        ScheduleMinute();
    }

    private void StopHost()
    {
        _minute.Stop();
        _views.Clear();
        if (_host is null) return;
        _host.ScreensChanged -= Rebuild;
        _host.Dispose();
        _host = null;
    }
}
