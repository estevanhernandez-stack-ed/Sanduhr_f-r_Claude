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
    private readonly Func<DeskUsage> _usageSource;
    private readonly DispatcherTimer _minute;
    private IDeskHost? _host;
    private DeskFonts? _fonts;
    private DeskSettings _settings = DeskSettings.Default;
    private DeskUsage _usage = DeskUsage.Empty;
    private bool _stopped;

    public DeskController(SettingsStore store, Func<IDeskHost> hostFactory, Func<DeskUsage> usageSource)
    {
        _store = store;
        _hostFactory = hostFactory;
        _usageSource = usageSource;
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

    /// <summary>Lays each screen out afresh and hands the host its islands; the host keeps its windows
    /// when the islands line up with the last ones.</summary>
    private void Render()
    {
        if (_host is null || _stopped || _screens.Count == 0) return;
        _fonts ??= new DeskFonts();
        var now = DateTimeOffset.Now;
        var surfaces = new List<DeskSurface>();
        foreach (var screen in _screens)
        {
            var view = new DeskView(screen, _settings, _fonts);
            view.Update(_usage, now);
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
        if (_host is null) return;
        _host.ScreensChanged -= Rebuild;
        _host.Dispose();
        _host = null;
    }
}
