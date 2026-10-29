using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Sanduhr.App.Interop;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// Claude Code's hooks touch <c>glow-waiting.signal</c> / <c>glow-done.signal</c> under
/// <c>%USERPROFILE%\.sanduhr</c>. This watches those two names (never reading what is in them), applies the
/// switches, the terminal rule and the limiter, then lights the top edge of the window in use and
/// flashes the widget's taskbar button. Stage 4.5.
/// </summary>
internal sealed class ClaudeCodeGlowService : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    private readonly SettingsStore _store;
    private readonly Paths _paths;
    private readonly Window _widget;
    private readonly Dispatcher _ui;
    private readonly Dictionary<ClaudeCodeEvent, DispatcherTimer> _pending = new();
    private FileSystemWatcher? _watcher;
    private GlowOverlayWindow? _overlay;
    private GlowSwitches _switches = new();
    private ClaudeCodeGlowLimiter _limiter;
    private bool _disposed;

    public ClaudeCodeGlowService(SettingsStore store, Paths paths, Window widget)
    {
        _store = store;
        _paths = paths;
        _widget = widget;
        _ui = Application.Current.Dispatcher;
    }

    /// <summary>Begin watching. Safe to call twice.</summary>
    public void Start()
    {
        if (_disposed || _watcher is not null) return;
        Reload();
        try
        {
            string dir = _paths.ExternalDir;   // streamer: notname - a file location used for IO, never drawn
            Directory.CreateDirectory(dir);
            _watcher = new FileSystemWatcher(dir, "glow-*.signal")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,   // streamer: notname - NotifyFilters.FileName is a watcher flag, not a name
                IncludeSubdirectories = false,
            };
            _watcher.Created += OnSignal;
            _watcher.Changed += OnSignal;
            _watcher.Renamed += (s, e) => OnSignal(s, e);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // No watcher means no glow; nothing else depends on it.
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    public void Stop()
    {
        if (_watcher is { } w)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
            _watcher = null;
        }
        foreach (var t in _pending.Values) t.Stop();
        _pending.Clear();
    }

    /// <summary>Re-read the switches (Settings changed them).</summary>
    public void Reload()
    {
        _switches = _store.LoadClaudeCodeGlow();
        _target = _store.LoadGlowTarget();
    }

    private GlowTargetSettings _target = GlowTargetSettings.Default;

    /// <summary>
    /// Lights the Desk's glow spot in a color; true when it drew (the Desk is on, the spot is placed and
    /// in sight), false when the glow should go to the window in use instead. Set by the app (spec 7.3).
    /// </summary>
    public Func<System.Windows.Media.Color, bool>? DeskGlow { get; set; }

    /// <summary>
    /// Show the glow for <paramref name="e"/> on the current target, bypassing the switches, the terminal
    /// rule and the limiter (a Settings "Try it" button).
    /// </summary>
    public void Preview(ClaudeCodeEvent e)
    {
        if (_disposed) return;
        if (!_ui.CheckAccess()) { _ui.BeginInvoke(() => Preview(e)); return; }
        Show(e, flash: false);
    }

    // Runs on a thread-pool thread: only hop to the UI thread, and never open the file.
    private void OnSignal(object sender, FileSystemEventArgs e)
    {
        if (ClaudeCodeSignals.EventForFileName(e.Name) is not { } kind) return;
        if (_disposed) return;
        _ui.BeginInvoke(() => Debounced(kind));
    }

    // One touch raises several events; the first starts a quiet window and the rest fold into it.
    private void Debounced(ClaudeCodeEvent kind)
    {
        if (_disposed || _watcher is null) return;
        if (_pending.ContainsKey(kind)) return;
        var timer = new DispatcherTimer(DispatcherPriority.Normal, _ui) { Interval = Debounce };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _pending.Remove(kind);
            Fire(kind);
        };
        _pending[kind] = timer;
        timer.Start();
    }

    private void Fire(ClaudeCodeEvent kind)
    {
        if (_disposed) return;
        var fg = GlowNative.GetForegroundWindow();
        if (!ClaudeCodeGlowRules.Decide(kind, _switches, ProcessNameOf(fg, out _), ref _limiter, DateTimeOffset.UtcNow))
            return;
        Show(kind, flash: true);
    }

    private void Show(ClaudeCodeEvent kind, bool flash)
    {
        try
        {
            // Where it lands (spec 6.8, 7.3): the window in use (S4), a chosen screen edge, the Desk's spot,
            // or the widget's taskbar flash alone. The Desk's spot draws when the Desk is on and the spot is
            // in sight; otherwise the glow goes to the window in use.
            if (_target.Kind == GlowTargetKind.Desk && DeskGlow?.Invoke(GlowOverlayContent.ColorFor(kind)) == true)
            {
                if (flash) GlowNative.FlashTray(new WindowInteropHelper(_widget).Handle);
                return;
            }
            var where = _target.Effective(false);
            if (where == GlowTargetKind.Edge && EdgeMonitor() is { } mon)
            {
                _overlay ??= new GlowOverlayWindow();
                _overlay.Glow(GlowPlacement.Edge(mon.Bounds, _target.Side, mon.Scale), GlowOverlayContent.ColorFor(kind), _target.Side);
            }
            else if (where != GlowTargetKind.Widget)
            {
                var (target, fallback, scale) = Observe();
                var rect = GlowPlacement.Compute(target, fallback, scale);
                _overlay ??= new GlowOverlayWindow();
                _overlay.Glow(rect, GlowOverlayContent.ColorFor(kind));
            }
            if (flash || where == GlowTargetKind.Widget) GlowNative.FlashTray(new WindowInteropHelper(_widget).Handle);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception
                                      or DllNotFoundException or EntryPointNotFoundException)
        {
            // A glow that cannot draw is not worth a crash.
        }
    }

    /// <summary>The chosen edge's monitor: the saved device name, else the primary one.</summary>
    private (PxRect Bounds, double Scale)? EdgeMonitor()
    {
        var monitors = Sanduhr.App.Interop.DeskNative.Monitors();
        var m = monitors.FirstOrDefault(x => _target.Monitor is { } name && string.Equals(x.Device, name, StringComparison.OrdinalIgnoreCase))
                ?? monitors.FirstOrDefault(x => x.Primary) ?? monitors.FirstOrDefault();
        return m is null ? null : (m.Bounds, m.Scale);
    }

    /// <summary>The foreground window as a target, the fallback monitor's bounds, and the DPI scale to use.</summary>
    private (GlowTarget? Target, PxRect Fallback, double Scale) Observe()
    {
        var widgetHandle = new WindowInteropHelper(_widget).Handle;
        var widgetMon = GlowNative.MonitorFromWindow(widgetHandle, GlowNative.MONITOR_DEFAULTTOPRIMARY);
        var fallback = GlowNative.MonitorBounds(widgetMon) ?? new PxRect(0, 0, 1920, 1080);
        double fallbackScale = GlowNative.MonitorScale(widgetMon);

        var fg = GlowNative.GetForegroundWindow();
        if (fg == IntPtr.Zero) return (null, fallback, fallbackScale);

        ProcessNameOf(fg, out uint pid);
        var mon = GlowNative.MonitorFromWindow(fg, GlowNative.MONITOR_DEFAULTTONEAREST);
        var frame = GlowNative.ExtendedFrame(fg);
        var bounds = GlowNative.MonitorBounds(mon);
        if (frame is null || bounds is null) return (null, fallback, fallbackScale);

        var target = new GlowTarget(
            Frame: frame.Value,
            MonitorBounds: bounds.Value,
            IsMinimized: GlowNative.IsIconic(fg),
            IsCloaked: GlowNative.IsCloaked(fg),
            ClassName: GlowNative.ClassName(fg),
            IsOwnProcess: pid == (uint)Environment.ProcessId,
            HasCaption: GlowNative.HasCaption(fg));
        return GlowPlacement.IsUsable(target)
            ? (target, fallback, GlowNative.MonitorScale(mon))
            : (null, fallback, fallbackScale);
    }

    private static string? ProcessNameOf(IntPtr hwnd, out uint pid)
    {
        pid = 0;
        if (hwnd == IntPtr.Zero) return null;
        GlowNative.GetWindowThreadProcessId(hwnd, out pid);
        if (pid == 0) return null;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        _overlay?.Dismiss();
        _overlay?.Close();
        _overlay = null;
    }
}
