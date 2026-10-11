using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Sanduhr.App.Interop;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// The Desk's windows on Este's ruled route (V3, spec 5.1), Rainmeter's "On desktop": top-level,
/// per-pixel-transparent tool windows, one per island (a stack of pieces) on each monitor, owned by
/// the shell window and kept at the bottom of the z-order, just above the desktop and below every
/// normal window. Never activated, never in Alt+Tab or the taskbar.
///
/// Show desktop (Win+D, the taskbar corner) raises the desktop icons host above the normal windows. As
/// Rainmeter does, a hidden sentinel stays at the bottom; when the icons host sits above it, Show desktop
/// is on and the Desk windows go just below the backmost topmost window, so they stay visible over the
/// raised desktop; when it ends they go back to the bottom. Checked on the foreground hook (the shell
/// becoming the foreground) and on a timer: 250 ms while Show desktop is on or just after a desktop
/// foreground change, 2 s otherwise.
///
/// The sentinel is also the window that hears <c>TaskbarCreated</c> (Explorer restarted, which takes the
/// owned Desk windows with it), display changes and work-area changes; each raises
/// <see cref="ScreensChanged"/> once per 500 ms burst and the controller rebuilds.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DesktopLayerHost : IDeskHost
{
    private static readonly TimeSpan Coalesce = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SlowWatch = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FastWatch = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FastFor = TimeSpan.FromSeconds(3);

    private readonly List<HwndSource> _windows = [];
    /// <summary>The screen each window in <see cref="_windows"/> draws on.</summary>
    private readonly List<DeskScreen> _screens = [];
    private readonly HwndSource _sentinel;
    private readonly uint _taskbarCreated;
    private readonly DispatcherTimer _coalesce;
    private readonly DispatcherTimer _watch;
    private DeskNative.WinEventProc? _proc;
    private IntPtr _hook;
    private bool _reordering;
    private bool _closing;
    private bool _disposed;
    private DateTime _fastUntil;
    /// <summary>The sentinel sat below the icons host at rest, so z-order cannot tell Show desktop
    /// apart; the lift is then never applied (the Desk stays at the bottom, owned by the shell).</summary>
    private bool _sentinelUnreliable;

    /// <summary>True while Show desktop is on and the Desk windows are lifted over the desktop.</summary>
    public bool ShowDesktopLifted { get; private set; }

    /// <summary>Follow Show desktop (on in the app; the runner turns it off to see what ownership
    /// alone does).</summary>
    public bool WatchShowDesktop { get; set; } = true;

    /// <summary>Every drawn pixel lets clicks through too (S5 has no Desk clicks; S7 turns this off).</summary>
    public bool ClickThrough { get; init; } = true;

    /// <summary>Own the Desk windows by the shell window (Este's ruling); off only in the runner.</summary>
    public bool OwnedByShell { get; init; } = true;

    /// <summary>What the host did and why, for the runner and debug logs. Never window text.</summary>
    public event Action<string>? Trace;

    public event Action? ScreensChanged;

    public bool IsShowing => _windows.Count > 0;

    /// <summary>The Desk windows' handles, in screen order (the runner queries them).</summary>
    public IReadOnlyList<IntPtr> Handles => [.. _windows.Select(w => w.Handle)];

    /// <summary>The sentinel's handle (the runner checks its z-order).</summary>
    public IntPtr SentinelHandle => _sentinel.Handle;

    public DesktopLayerHost()
    {
        _taskbarCreated = DeskNative.RegisterWindowMessageW("TaskbarCreated");
        // Hidden, top-level and never message-only: message-only windows miss broadcasts.
        var p = new HwndSourceParameters("SanduhrDeskSentinel")
        {
            WindowStyle = DeskNative.WS_POPUP,
            ExtendedWindowStyle = DeskNative.WS_EX_TOOLWINDOW | DeskNative.WS_EX_NOACTIVATE,
            Width = 0,
            Height = 0,
        };
        _sentinel = new HwndSource(p);
        _sentinel.AddHook(SentinelHook);
        _coalesce = new DispatcherTimer(DispatcherPriority.Background) { Interval = Coalesce };
        _coalesce.Tick += (_, _) =>
        {
            _coalesce.Stop();
            if (!_disposed) ScreensChanged?.Invoke();
        };
        _watch = new DispatcherTimer(DispatcherPriority.Background) { Interval = SlowWatch };
        _watch.Tick += (_, _) => CheckShowDesktop("timer");
    }

    public IReadOnlyList<DeskScreen> Screens()
    {
        var monitors = DeskNative.Monitors();
        var bars = DeskNative.TaskbarAutoHides() ? DeskNative.TaskbarRects() : [];
        return [.. monitors.Select(m => new DeskScreen(m.Device, m.Bounds, m.Work, m.Scale, m.Primary,
            TaskbarClearance.Insets(m.Bounds, m.Work, bars, m.Scale)))];
    }

    public void Show(IReadOnlyList<DeskSurface> surfaces)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var wanted = surfaces.SelectMany(s => s.Islands.Select(i => (s.Screen, Island: i))).ToList();
        // The minute's redraw hands over the same islands on the same screens: keep the windows.
        bool reuse = wanted.Count == _windows.Count
            && wanted.Select(w => w.Screen).SequenceEqual(_screens);
        if (!reuse)
        {
            CloseWindows();
            foreach (var (screen, _) in wanted)
            {
                _windows.Add(Create(screen));
                _screens.Add(screen);
            }
        }
        for (int i = 0; i < wanted.Count; i++)
            Place(_windows[i], wanted[i].Screen, wanted[i].Island, first: !reuse);
        if (reuse) return;
        Reorder(() => DeskNative.SetWindowPos(_sentinel.Handle, DeskNative.HWND_BOTTOM, 0, 0, 0, 0,
            DeskNative.SWP_NOMOVE | DeskNative.SWP_NOSIZE | DeskNative.SWP_NOACTIVATE));
        // Showing happens from Settings or at launch, never during Show desktop: a sentinel already
        // below the icons host here means z-order cannot tell the two apart on this machine.
        var host = DeskNative.IconsHost();
        _sentinelUnreliable = host != IntPtr.Zero && DeskNative.IsBelow(_sentinel.Handle, host)
            && !IsDesktopForeground(DeskNative.GetForegroundWindow());
        if (_sentinelUnreliable)
            Log("sentinel sits below the icons host at rest; Show desktop lift disabled");
        ShowDesktopLifted = false;
        StartWatching();
        Log($"showing {_windows.Count} window(s)");
    }

    public void Hide()
    {
        StopWatching();
        CloseWindows();
        ShowDesktopLifted = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Hide();
        _coalesce.Stop();
        _disposed = true;
        _sentinel.Dispose();
    }

    /// <summary>Room around an island for the ink's shadow, in DIPs.</summary>
    private const double Pad = 16;

    private HwndSource Create(DeskScreen screen)
    {
        var b = screen.Bounds;
        // Created per-monitor aware so each window draws crisply at its own monitor's DPI while the
        // process stays system aware. Born 1x1 on its monitor (for the DPI), sized by Place.
        var src = DeskNative.PerMonitor(() => new HwndSource(new HwndSourceParameters("SanduhrDesk")
        {
            WindowStyle = DeskNative.WS_POPUP,
            ExtendedWindowStyle = DeskNative.WS_EX_TOOLWINDOW | DeskNative.WS_EX_NOACTIVATE
                | (ClickThrough ? DeskNative.WS_EX_TRANSPARENT : 0),
            UsesPerPixelTransparency = true,
            PositionX = b.X,
            PositionY = b.Y,
            Width = 1,
            Height = 1,
        }));
        src.AddHook(DeskHook);
        if (OwnedByShell)
            DeskNative.SetWindowLongPtr(src.Handle, DeskNative.GWLP_HWNDPARENT, DeskNative.GetShellWindow());
        return src;
    }

    /// <summary>Puts an island in its window: the window just its size (plus the shadow's room), with
    /// the island's aligned edge on its anchor point.</summary>
    private void Place(HwndSource src, DeskScreen screen, DeskIsland island, bool first)
    {
        // WPF draws in its own DPI for this window; scale the content so a DIP is the monitor's.
        double wpf = src.CompositionTarget?.TransformToDevice.M11 is > 0 and var t ? t : 1.0;
        double k = screen.Scale / wpf;
        var content = island.Content;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double cw = Math.Ceiling(content.DesiredSize.Width), ch = Math.Ceiling(content.DesiredSize.Height);
        double left = island.Anchor.X - Pad - island.Horizontal switch
        {
            HorizontalAlignment.Right => cw,
            HorizontalAlignment.Center => cw / 2,
            _ => 0,
        };
        double top = island.Anchor.Y - Pad - island.Vertical switch
        {
            VerticalAlignment.Bottom => ch,
            VerticalAlignment.Center => ch / 2,
            _ => 0,
        };
        double w = cw + 2 * Pad, h = ch + 2 * Pad;
        src.RootVisual = new Border
        {
            Child = content,
            Padding = new Thickness(Pad),
            Width = w,
            Height = h,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = !ClickThrough,
            LayoutTransform = Math.Abs(k - 1) < 0.001 ? Transform.Identity : new ScaleTransform(k, k),
        };
        int x = screen.Bounds.X + (int)Math.Round(left * screen.Scale);
        int y = screen.Bounds.Y + (int)Math.Round(top * screen.Scale);
        int pw = (int)Math.Ceiling(w * screen.Scale), ph = (int)Math.Ceiling(h * screen.Scale);
        var hwnd = src.Handle;
        if (first)
            Reorder(() => DeskNative.SetWindowPos(hwnd, DeskNative.HWND_BOTTOM, x, y, pw, ph,
                DeskNative.SWP_NOACTIVATE | DeskNative.SWP_SHOWWINDOW));
        else if (DeskNative.Rect(hwnd) != new PxRect(x, y, pw, ph))
            DeskNative.SetWindowPos(hwnd, IntPtr.Zero, x, y, pw, ph,
                DeskNative.SWP_NOACTIVATE | DeskNative.SWP_NOZORDER | DeskNative.SWP_NOOWNERZORDER);
    }

    private void CloseWindows()
    {
        _closing = true;
        try
        {
            foreach (var w in _windows)
                w.Dispose();
            _windows.Clear();
            _screens.Clear();
        }
        finally { _closing = false; }
    }

    // ---- z-order ----

    private void Reorder(Action move)
    {
        _reordering = true;
        try { move(); }
        finally { _reordering = false; }
    }

    private static bool IsDesktopForeground(IntPtr fg)
    {
        if (fg == IntPtr.Zero) return false;
        var shell = DeskNative.GetShellWindow();
        return fg == shell || (DeskNative.ClassName(fg) == "WorkerW" && DeskNative.SameProcess(shell, fg));
    }

    private void StartWatching()
    {
        if (!WatchShowDesktop || _hook != IntPtr.Zero) { _watch.Start(); return; }
        _proc = OnForeground;
        _hook = DeskNative.SetWinEventHook(DeskNative.EVENT_SYSTEM_FOREGROUND, DeskNative.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _proc, 0, 0, DeskNative.WINEVENT_OUTOFCONTEXT | DeskNative.WINEVENT_SKIPOWNPROCESS);
        _watch.Interval = SlowWatch;
        _watch.Start();
    }

    private void StopWatching()
    {
        _watch.Stop();
        if (_hook != IntPtr.Zero) DeskNative.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
        _proc = null;
    }

    private void OnForeground(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (IsDesktopForeground(hwnd))
        {
            _fastUntil = DateTime.UtcNow + FastFor;
            CheckShowDesktop("desktop became the foreground");
        }
        else if (ShowDesktopLifted)
        {
            CheckShowDesktop("foreground left the desktop");
        }
    }

    /// <summary>Compare the sentinel with the icons host and lift or drop the Desk windows.</summary>
    public void CheckShowDesktop(string why)
    {
        if (_disposed || !IsShowing || !WatchShowDesktop || _sentinelUnreliable)
        {
            _watch.Interval = SlowWatch;
            return;
        }
        var host = DeskNative.IconsHost();
        bool raised = host != IntPtr.Zero && DeskNative.IsBelow(_sentinel.Handle, host);
        if (raised && !ShowDesktopLifted)
        {
            ShowDesktopLifted = true;
            Lift(host);
            Log($"Show desktop on ({why}): lifted over the desktop");
        }
        else if (ShowDesktopLifted && (!raised || AppWindowInFront()))
        {
            ShowDesktopLifted = false;
            Drop();
            Log($"Show desktop off ({why}): back to the bottom");
        }
        _watch.Interval = ShowDesktopLifted || DateTime.UtcNow < _fastUntil ? FastWatch : SlowWatch;
    }

    /// <summary>A captioned window of another app is the foreground: whatever z-order says, the
    /// Desk never stays lifted over an app the person is using.</summary>
    private static bool AppWindowInFront()
    {
        var fg = DeskNative.GetForegroundWindow();
        if (fg == IntPtr.Zero || IsDesktopForeground(fg)) return false;
        DeskNative.GetWindowThreadProcessId(fg, out uint pid);
        if (pid == (uint)Environment.ProcessId) return false;
        return (DeskNative.GetWindowLong(fg, DeskNative.GWL_STYLE) & DeskNative.WS_CAPTION) == DeskNative.WS_CAPTION
            && DeskNative.IsWindowVisible(fg);
    }

    private void Lift(IntPtr host)
    {
        var after = DeskNative.BackmostTopmostAbove(host);
        foreach (var w in _windows)
        {
            var h = w.Handle;
            Reorder(() => DeskNative.SetWindowPos(h, after, 0, 0, 0, 0,
                DeskNative.SWP_NOMOVE | DeskNative.SWP_NOSIZE | DeskNative.SWP_NOACTIVATE));
        }
    }

    private void Drop()
    {
        foreach (var w in _windows)
        {
            var h = w.Handle;
            Reorder(() =>
            {
                DeskNative.SetWindowPos(h, DeskNative.HWND_NOTOPMOST, 0, 0, 0, 0,
                    DeskNative.SWP_NOMOVE | DeskNative.SWP_NOSIZE | DeskNative.SWP_NOACTIVATE);
                DeskNative.SetWindowPos(h, DeskNative.HWND_BOTTOM, 0, 0, 0, 0,
                    DeskNative.SWP_NOMOVE | DeskNative.SWP_NOSIZE | DeskNative.SWP_NOACTIVATE);
            });
        }
    }

    // ---- messages ----

    private IntPtr DeskHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case DeskNative.WM_WINDOWPOSCHANGING:
                KeepZOrder(lParam);
                break;
            case DeskNative.WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(DeskNative.MA_NOACTIVATE);
            case DeskNative.WM_DPICHANGED:
                Schedule("DPI changed");
                break;
            case DeskNative.WM_DESTROY:
                if (!_closing) Schedule("a Desk window was destroyed");
                break;
        }
        return IntPtr.Zero;
    }

    private IntPtr SentinelHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == DeskNative.WM_WINDOWPOSCHANGING)
            KeepZOrder(lParam);
        else if ((uint)msg == _taskbarCreated && _taskbarCreated != 0)
            Schedule("Explorer restarted (TaskbarCreated)");
        else if (msg == DeskNative.WM_DISPLAYCHANGE)
            Schedule("display changed");
        else if (msg == DeskNative.WM_SETTINGCHANGE && wParam.ToInt64() == DeskNative.SPI_SETWORKAREA)
            Schedule("work area changed");
        return IntPtr.Zero;
    }

    /// <summary>Anyone but the host asking to restack a Desk window or the sentinel is refused, so a
    /// click or another app's reordering never raises it.</summary>
    private void KeepZOrder(IntPtr lParam)
    {
        if (_reordering || lParam == IntPtr.Zero) return;
        var pos = Marshal.PtrToStructure<DeskNative.WINDOWPOS>(lParam);
        if ((pos.flags & DeskNative.SWP_NOZORDER) != 0) return;
        pos.flags |= DeskNative.SWP_NOZORDER;
        Marshal.StructureToPtr(pos, lParam, false);
    }

    private void Schedule(string why)
    {
        if (_disposed) return;
        Log($"rebuild: {why}");
        _coalesce.Stop();
        _coalesce.Start();
    }

    private void Log(string message) => Trace?.Invoke(message);
}
