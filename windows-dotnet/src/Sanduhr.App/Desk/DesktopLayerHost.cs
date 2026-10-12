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
/// Show desktop (Win+D, the taskbar corner) raises the desktop icons host above the normal windows.
/// Ownership by the shell window carries the Desk up with it (Este's live run, 2026-10-11), and the
/// z-order guard lets that happen while a sentinel kept just above the host at rest shows the host
/// raised. As a fallback only, when a Desk window is measured below the icons host with the desktop in
/// front and no app (<see cref="ShowDesktopRule"/>), it is re-seated just above the host, never
/// topmost (Windows hides topmost windows in Show desktop); it goes back to the bottom as soon as an
/// app is in front. Checked on the foreground hook and on a timer: 250 ms while re-seated or just after
/// the desktop took the foreground, 2 s otherwise.
///
/// The sentinel also hears <c>TaskbarCreated</c> (Explorer restarted, which destroys the owned Desk
/// windows with their owner), display changes and work-area changes; each raises
/// <see cref="ScreensChanged"/> once per 500 ms burst and the controller rebuilds into new windows.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DesktopLayerHost : IDeskHost
{
    private static readonly TimeSpan Coalesce = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SlowWatch = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FastWatch = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FastFor = TimeSpan.FromSeconds(3);

    /// <summary>Room around an island for the ink's shadow, in DIPs.</summary>
    private const double Pad = 16;

    private readonly List<HwndSource> _windows = [];
    /// <summary>The screen each window in <see cref="_windows"/> draws on.</summary>
    private readonly List<DeskScreen> _screens = [];
    private readonly HwndSource _sentinel;
    private readonly uint _taskbarCreated;
    private readonly DispatcherTimer _coalesce;
    private readonly DispatcherTimer _watch;
    private readonly DeskCoverageWatch _coverage;
    private DeskNative.WinEventProc? _proc;
    private IntPtr _hook;
    private bool _reordering;
    private bool _closing;
    private bool _disposed;
    /// <summary>A Desk window died without being asked (Explorer restarted) or the shell window changed:
    /// the next <see cref="Show"/> builds new windows instead of reusing these.</summary>
    private bool _stale;
    private DateTime _fastUntil;
    /// <summary>The sentinel could not be put above the icons host, so z-order cannot tell Show desktop
    /// apart from rest; the lift is never applied (the Desk stays at the bottom, owned by the shell).</summary>
    private bool _sentinelUnreliable;

    /// <summary>True while the fallback has re-seated the Desk above the raised desktop.</summary>
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

    public IReadOnlyList<bool> Coverage => _coverage.Covered;

    public event Action? CoverageChanged;

    /// <summary>The Desk windows' handles, in island order (the runner queries them).</summary>
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
        _coverage = new DeskCoverageWatch(
            () => [.. _windows.Select(w => w.IsDisposed ? IntPtr.Zero : w.Handle)],
            () => _sentinel.Handle);
        _coverage.Changed += () => CoverageChanged?.Invoke();
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
        // The minute's redraw hands over the same islands on the same screens: keep the windows, but
        // only while every one is alive (Explorer restarting destroys them with their owner).
        bool reuse = !_stale
            && wanted.Count == _windows.Count
            && wanted.Select(w => w.Screen).SequenceEqual(_screens)
            && _windows.All(w => !w.IsDisposed && DeskNative.IsWindow(w.Handle));
        if (!reuse)
        {
            CloseWindows();
            _stale = false;
            foreach (var (screen, _) in wanted)
            {
                _windows.Add(Create(screen));
                _screens.Add(screen);
            }
        }
        for (int i = 0; i < wanted.Count; i++)
            Place(_windows[i], wanted[i].Screen, wanted[i].Island, first: !reuse);
        if (reuse)
        {
            // Same windows, but an island may have been re-fitted: read the z-order again soon.
            _coverage.Schedule();
            return;
        }
        ShowDesktopLifted = false;
        SeatSentinel();
        StartWatching();
        _coverage.Start(_windows.Count);
        _coverage.Recompute();
        Log($"showing {_windows.Count} window(s)");
    }

    public void Hide()
    {
        _coverage.Stop();
        StopWatching();
        CloseWindows();
        ShowDesktopLifted = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Hide();
        _coalesce.Stop();
        _coverage.Dispose();
        _disposed = true;
        _sentinel.Dispose();
    }

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
        // The minute's in-place tick hands the same content back (spec 6.10): keep its frame and only
        // re-fit it, so nothing else in the island is rebuilt.
        if (src.RootVisual is Border same && ReferenceEquals(same.Child, content))
        {
            same.Width = w;
            same.Height = h;
            same.LayoutTransform = Math.Abs(k - 1) < 0.001 ? Transform.Identity : new ScaleTransform(k, k);
            Fit(src, screen, left, top, w, h, first);
            return;
        }
        if (content.Parent is Border previous) previous.Child = null;
        var root = new Border
        {
            Child = content,
            Padding = new Thickness(Pad),
            Width = w,
            Height = h,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = !ClickThrough,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
            LayoutTransform = Math.Abs(k - 1) < 0.001 ? Transform.Identity : new ScaleTransform(k, k),
        };
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Ideal);
        src.RootVisual = root;
        Fit(src, screen, left, top, w, h, first);
    }

    /// <summary>Moves and sizes an island's window to its content, in the monitor's physical pixels.</summary>
    private void Fit(HwndSource src, DeskScreen screen, double left, double top, double w, double h, bool first)
    {
        int x = screen.Bounds.X + (int)Math.Round(left * screen.Scale);
        int y = screen.Bounds.Y + (int)Math.Round(top * screen.Scale);
        int pw = (int)Math.Ceiling(w * screen.Scale), ph = (int)Math.Ceiling(h * screen.Scale);
        var hwnd = src.Handle;
        // Physical pixels in, physical pixels out: the same per-monitor context the monitors were read in.
        DeskNative.PerMonitor(() =>
        {
            if (first)
                Reorder(() => DeskNative.SetWindowPos(hwnd, DeskNative.HWND_BOTTOM, x, y, pw, ph,
                    DeskNative.SWP_NOACTIVATE | DeskNative.SWP_SHOWWINDOW));
            else if (DeskNative.Rect(hwnd) != new PxRect(x, y, pw, ph))
                DeskNative.SetWindowPos(hwnd, IntPtr.Zero, x, y, pw, ph,
                    DeskNative.SWP_NOACTIVATE | DeskNative.SWP_NOZORDER | DeskNative.SWP_NOOWNERZORDER);
            return true;
        });
    }

    private void CloseWindows()
    {
        _closing = true;
        try
        {
            foreach (var w in _windows)
                if (!w.IsDisposed) w.Dispose();
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

    private static bool IsDesktopWindow(IntPtr w)
    {
        if (w == IntPtr.Zero) return false;
        var shell = DeskNative.GetShellWindow();
        return w == shell || (DeskNative.ClassName(w) == "WorkerW" && DeskNative.SameProcess(shell, w));
    }

    /// <summary>Some other visible, non-tool window is the foreground: any app, borderless and
    /// full-screen ones and Sanduhr's own Settings included. Only the desktop and the Desk windows
    /// themselves are not "an app in front".</summary>
    private bool AppWindowInFront()
    {
        var fg = DeskNative.GetForegroundWindow();
        if (fg == IntPtr.Zero || IsDesktopWindow(fg) || fg == _sentinel.Handle) return false;
        foreach (var w in _windows)
            if (w.Handle == fg) return false;
        return DeskNative.IsWindowVisible(fg)
            && (DeskNative.GetWindowLong(fg, DeskNative.GWL_EXSTYLE) & DeskNative.WS_EX_TOOLWINDOW) == 0;
    }

    /// <summary>Puts the sentinel just above the icons host (the window above the host becomes the
    /// sentinel's insert-after), so at rest the host is never above it and only Show desktop, which
    /// raises the host over the normal windows, puts it there.</summary>
    private void SeatSentinel()
    {
        var host = DeskNative.IconsHost();
        if (host == IntPtr.Zero) return;
        var above = DeskNative.Above(host);
        if (above == _sentinel.Handle) { _sentinelUnreliable = false; return; }
        DeskNative.PerMonitor(() =>
        {
            Reorder(() => DeskNative.SetWindowPos(_sentinel.Handle, above == IntPtr.Zero ? IntPtr.Zero : above, 0, 0, 0, 0,
                DeskNative.SWP_NOMOVE | DeskNative.SWP_NOSIZE | DeskNative.SWP_NOACTIVATE));
            return true;
        });
        _sentinelUnreliable = DeskNative.IsBelow(_sentinel.Handle, host);
        if (_sentinelUnreliable)
            Log("the sentinel could not be put above the icons host; Show desktop lift disabled");
    }

    private void StartWatching()
    {
        if (!WatchShowDesktop) return;
        if (_hook == IntPtr.Zero)
        {
            _proc = OnForeground;
            _hook = DeskNative.SetWinEventHook(DeskNative.EVENT_SYSTEM_FOREGROUND, DeskNative.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _proc, 0, 0, DeskNative.WINEVENT_OUTOFCONTEXT | DeskNative.WINEVENT_SKIPOWNPROCESS);
        }
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
        if (IsDesktopWindow(hwnd)) _fastUntil = DateTime.UtcNow + FastFor;
        CheckShowDesktop(IsDesktopWindow(hwnd) ? "desktop became the foreground" : "foreground changed");
    }

    /// <summary>Read the z-order and the foreground, ask <see cref="ShowDesktopRule"/>, and lift, drop
    /// or keep the Desk windows; re-seat the sentinel while at rest.</summary>
    public void CheckShowDesktop(string why)
    {
        if (_disposed || !IsShowing)
        {
            _watch.Interval = SlowWatch;
            return;
        }
        var host = DeskNative.IconsHost();
        bool desktopFg = IsDesktopWindow(DeskNative.GetForegroundWindow());
        bool appInFront = AppWindowInFront();
        if (!WatchShowDesktop)
        {
            if (ShowDesktopLifted) { ShowDesktopLifted = false; Drop(); }
            return;
        }
        if (ShowDesktopRule.ShouldReseatSentinel(ShowDesktopLifted, desktopFg) && appInFront)
            SeatSentinel();
        // The measured failure, not an inference: a Desk window sits below the icons host.
        bool deskBelow = host != IntPtr.Zero && _windows.Any(w => !w.IsDisposed && DeskNative.IsBelow(w.Handle, host));
        switch (ShowDesktopRule.Decide(deskBelow, desktopFg, appInFront, ShowDesktopLifted))
        {
            case ShowDesktopAction.Lift:
                ShowDesktopLifted = true;
                Lift(host);
                Log($"Show desktop ({why}): the Desk was below the desktop; re-seated just above it");
                break;
            case ShowDesktopAction.Drop:
                ShowDesktopLifted = false;
                Drop();
                Log($"Show desktop off ({why}): back to the bottom");
                break;
        }
        _watch.Interval = ShowDesktopLifted || DateTime.UtcNow < _fastUntil ? FastWatch : SlowWatch;
    }

    /// <summary>Re-seats the Desk windows just above the icons host, in the normal band: inserted after
    /// the window directly above the host (or at the top when the host is topmost of the band).</summary>
    private void Lift(IntPtr host)
    {
        var above = DeskNative.Above(host);
        // Never after a topmost window (that would make the Desk topmost, which Show desktop hides):
        // with the host at the top of the normal band, the top of that band is just above it.
        var after = above == IntPtr.Zero || _windows.Any(w => w.Handle == above)
            || (DeskNative.GetWindowLong(above, DeskNative.GWL_EXSTYLE) & DeskNative.WS_EX_TOPMOST) != 0
            ? DeskNative.HWND_NOTOPMOST : above;
        DeskNative.PerMonitor(() =>
        {
            foreach (var w in _windows)
            {
                if (w.IsDisposed) continue;
                var h = w.Handle;
                Reorder(() => DeskNative.SetWindowPos(h, after, 0, 0, 0, 0,
                    DeskNative.SWP_NOMOVE | DeskNative.SWP_NOSIZE | DeskNative.SWP_NOACTIVATE));
            }
            return true;
        });
    }

    private void Drop()
    {
        DeskNative.PerMonitor(() =>
        {
            foreach (var w in _windows)
            {
                if (w.IsDisposed) continue;
                var h = w.Handle;
                Reorder(() =>
                {
                    DeskNative.SetWindowPos(h, DeskNative.HWND_NOTOPMOST, 0, 0, 0, 0,
                        DeskNative.SWP_NOMOVE | DeskNative.SWP_NOSIZE | DeskNative.SWP_NOACTIVATE);
                    DeskNative.SetWindowPos(h, DeskNative.HWND_BOTTOM, 0, 0, 0, 0,
                        DeskNative.SWP_NOMOVE | DeskNative.SWP_NOSIZE | DeskNative.SWP_NOACTIVATE);
                });
            }
            return true;
        });
    }

    // ---- messages ----

    private IntPtr DeskHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case DeskNative.WM_WINDOWPOSCHANGING:
                KeepZOrder(lParam, desk: true);
                break;
            case DeskNative.WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(DeskNative.MA_NOACTIVATE);
            case DeskNative.WM_DPICHANGED:
                Schedule("DPI changed");
                break;
            case DeskNative.WM_DESTROY:
                if (!_closing)
                {
                    _stale = true;
                    Schedule("a Desk window was destroyed");
                }
                break;
        }
        return IntPtr.Zero;
    }

    private IntPtr SentinelHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == DeskNative.WM_WINDOWPOSCHANGING)
            KeepZOrder(lParam, desk: false);
        else if ((uint)msg == _taskbarCreated && _taskbarCreated != 0)
        {
            // A new shell window: the old windows were owned by the old one. Build new windows.
            _stale = true;
            Schedule("Explorer restarted (TaskbarCreated)");
        }
        else if (msg == DeskNative.WM_DISPLAYCHANGE)
            Schedule("display changed");
        else if (msg == DeskNative.WM_SETTINGCHANGE && wParam.ToInt64() == DeskNative.SPI_SETWORKAREA)
            Schedule("work area changed");
        else if (msg == DeskNative.WM_SETTINGCHANGE && wParam.ToInt64() == DeskNative.SPI_SETDESKWALLPAPER)
            Schedule("wallpaper changed"); // Automatic ink samples the new wallpaper (spec 6.7)
        return IntPtr.Zero;
    }

    /// <summary>
    /// Restacks the host did not ask for. A Desk window may move down toward the desktop (to the bottom,
    /// or just above its owner) and may go wherever the system takes it while Show desktop has the
    /// icons host raised (the owner carrying its owned windows up with it); any other move up, such as a
    /// click or another app's reordering, is refused. The sentinel is never moved by anyone else.
    /// </summary>
    private void KeepZOrder(IntPtr lParam, bool desk)
    {
        if (_reordering || lParam == IntPtr.Zero) return;
        var pos = Marshal.PtrToStructure<DeskNative.WINDOWPOS>(lParam);
        if ((pos.flags & DeskNative.SWP_NOZORDER) != 0) return;
        if (desk)
        {
            var after = pos.hwndInsertAfter;
            if (after == DeskNative.HWND_BOTTOM || after == DeskNative.GetShellWindow()) return;
            var host = DeskNative.IconsHost();
            if (host != IntPtr.Zero && DeskNative.IsBelow(_sentinel.Handle, host)) return;
        }
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
