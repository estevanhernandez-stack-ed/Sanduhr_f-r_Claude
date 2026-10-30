using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Sanduhr.Core;

namespace Sanduhr.App.Interop;

/// <summary>
/// Win32 for the Desk's desktop-layer windows (spec 5.1): the shell window and the desktop icons host,
/// z-order walks, the foreground hook, monitors and their DPI in physical pixels, and the taskbars.
/// Hand-written <c>[DllImport]</c>, same pattern as <see cref="GlowNative"/>. Reads class names,
/// rectangles and styles only, never window text.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DeskNative
{
    public const int GWL_EXSTYLE = -20;
    public const int GWL_STYLE = -16;
    public const int GWLP_HWNDPARENT = -8;
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_VISIBLE = 0x10000000;
    public const int WS_CAPTION = 0x00C00000;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOOWNERZORDER = 0x0200;
    public static readonly IntPtr HWND_BOTTOM = new(1);
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);

    public const int WM_DESTROY = 0x0002;
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int WM_ACTIVATE = 0x0006;
    public const int WA_INACTIVE = 0;
    public const int WM_KEYDOWN = 0x0100;
    public const int VK_RETURN = 0x0D;
    public const int VK_ESCAPE = 0x1B;
    public const int WM_WINDOWPOSCHANGING = 0x0046;
    public const int WM_DISPLAYCHANGE = 0x007E;
    public const int WM_DPICHANGED = 0x02E0;
    public const int MA_NOACTIVATE = 3;
    public const int SPI_SETWORKAREA = 0x002F;
    public const int SPI_SETDESKWALLPAPER = 0x0014;

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint EVENT_OBJECT_HIDE = 0x8003;
    public const uint EVENT_OBJECT_CLOAKED = 0x8017;
    public const uint EVENT_OBJECT_UNCLOAKED = 0x8018;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    private const uint GW_HWNDNEXT = 2;
    private const uint GW_HWNDPREV = 3;
    private const uint ABM_GETSTATE = 0x4;
    private const int ABS_AUTOHIDE = 0x1;
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public PxRect ToPx() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string? cls, string? name);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong(IntPtr h, int index);
    // SetWindowLongPtrW exists only in 64-bit user32 (32-bit exports SetWindowLongW alone); Sanduhr ships
    // x64 only (win-x64 publish and Store package), so this binding is safe there and nowhere else.
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfoW(IntPtr m, ref MONITORINFOEX mi);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr m, int type, out uint x, out uint y);
    [DllImport("shell32.dll")] private static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);

    public static string ClassName(IntPtr h)
    {
        if (h == IntPtr.Zero) return "";
        var sb = new StringBuilder(64);
        return GetClassNameW(h, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    public static PxRect? Rect(IntPtr h) => GetWindowRect(h, out var r) ? r.ToPx() : null;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);

    /// <summary>A window's rectangle in physical pixels, read per-monitor aware (the Desk's windows are placed in these).</summary>
    public static PxRect? RectPx(IntPtr h) => PerMonitor(() => Rect(h));

    /// <summary>The cursor in physical pixels, read per-monitor aware; far off every screen when Windows would not say.</summary>
    public static PxPoint CursorPx() => PerMonitor(() => GetCursorPos(out var p) ? new PxPoint(p.X, p.Y) : new PxPoint(int.MinValue / 2, int.MinValue / 2));

    /// <summary>Any physical mouse button is down now (left, right or middle, whichever way the buttons are swapped).</summary>
    public static bool AnyMouseButtonDown() =>
        (((ushort)GetAsyncKeyState(0x01) | (ushort)GetAsyncKeyState(0x02) | (ushort)GetAsyncKeyState(0x04)) & 0x8000) != 0;

    /// <summary>Moves a Desk window to <paramref name="at"/> (physical pixels) without touching its size, z-order or activation.</summary>
    public static void MoveTo(IntPtr h, PxPoint at) => PerMonitor(() =>
        SetWindowPos(h, IntPtr.Zero, at.X, at.Y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE));

    public static bool SameProcess(IntPtr a, IntPtr b)
    {
        GetWindowThreadProcessId(a, out uint pa);
        GetWindowThreadProcessId(b, out uint pb);
        return pa != 0 && pa == pb;
    }

    /// <summary>The window holding the desktop icons (<c>SHELLDLL_DefView</c>): the shell window
    /// itself on 24H2 and later, before that a <c>WorkerW</c> of the shell's process. Null when
    /// Explorer is not running.</summary>
    public static IntPtr IconsHost()
    {
        var shell = GetShellWindow();
        if (shell == IntPtr.Zero) return IntPtr.Zero;
        if (FindWindowExW(shell, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return shell;
        var w = IntPtr.Zero;
        while ((w = FindWindowExW(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
        {
            if (IsWindowVisible(w) && SameProcess(shell, w)
                && FindWindowExW(w, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                return w;
        }
        return IntPtr.Zero;
    }

    /// <summary>True when <paramref name="target"/> sits anywhere below <paramref name="above"/> in the
    /// top-level z-order.</summary>
    public static bool IsBelow(IntPtr target, IntPtr above)
    {
        if (target == IntPtr.Zero || above == IntPtr.Zero) return false;
        for (var w = GetWindow(above, GW_HWNDNEXT); w != IntPtr.Zero; w = GetWindow(w, GW_HWNDNEXT))
            if (w == target) return true;
        return false;
    }

    /// <summary>The window just above <paramref name="h"/> in the z-order, or zero at the top.</summary>
    public static IntPtr Above(IntPtr h) => h == IntPtr.Zero ? IntPtr.Zero : GetWindow(h, GW_HWNDPREV);

    /// <summary>Runs <paramref name="body"/> with this thread per-monitor DPI aware (v2), so monitor
    /// rectangles are physical pixels and windows created inside draw crisply on every monitor. The
    /// process stays system aware; only the Desk's windows are created this way.</summary>
    public static T PerMonitor<T>(Func<T> body)
    {
        IntPtr old = IntPtr.Zero;
        try { old = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); }
        catch (EntryPointNotFoundException) { }
        try { return body(); }
        finally { if (old != IntPtr.Zero) SetThreadDpiAwarenessContext(old); }
    }

    /// <summary>One monitor in physical pixels: device name, bounds, work area, DPI scale, primary.</summary>
    public sealed record Monitor(string Device, PxRect Bounds, PxRect Work, double Scale, bool Primary);

    /// <summary>Every monitor, read per-monitor aware (physical pixels).</summary>
    public static IReadOnlyList<Monitor> Monitors() => PerMonitor(() =>
    {
        var list = new List<Monitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfoW(m, ref mi)) return true;
            double scale = 1.0;
            try
            {
                if (GetDpiForMonitor(m, 0, out uint x, out _) == 0 && x > 0) scale = x / 96.0;
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
            list.Add(new Monitor(mi.szDevice, mi.rcMonitor.ToPx(), mi.rcWork.ToPx(), scale, (mi.dwFlags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return (IReadOnlyList<Monitor>)list;
    });

    /// <summary>The taskbar auto-hides (one setting for every taskbar on Windows 11).</summary>
    public static bool TaskbarAutoHides()
    {
        var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        return ((long)SHAppBarMessage(ABM_GETSTATE, ref abd) & ABS_AUTOHIDE) != 0;
    }

    /// <summary>Every taskbar's rectangle (the primary and each secondary), physical pixels.</summary>
    public static IReadOnlyList<PxRect> TaskbarRects() => PerMonitor(() =>
    {
        var list = new List<PxRect>();
        foreach (var cls in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
        {
            var w = IntPtr.Zero;
            while ((w = FindWindowExW(IntPtr.Zero, w, cls, null)) != IntPtr.Zero)
                if (Rect(w) is { IsEmpty: false } r) list.Add(r);
        }
        return (IReadOnlyList<PxRect>)list;
    });
}
