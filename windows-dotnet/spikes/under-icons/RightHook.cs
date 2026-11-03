using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UnderIcons;

/// <summary>
/// THROWAWAY SPIKE, 2026-11-02 part 2b: a low-level mouse hook that acts on the right button only. A right press over
/// the piece, where the desktop itself is what the click would reach and no icon is under it, is swallowed (down and
/// up) and the Desk menu opens from a posted message. Every other event, left button included, passes untouched.
/// </summary>
internal static class RightHook
{
    private delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public N.POINT pt; public uint mouseData, flags, time; public IntPtr extra; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookExW(int id, HookProc proc, IntPtr mod, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);

    private const int WH_MOUSE_LL = 14;
    private const int WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
    private const uint LLMHF_INJECTED = 0x1;

    private static readonly HookProc ProcRef = Proc;
    private static IntPtr _hook;
    private static bool _swallowing;

    public static Func<Rectangle> Piece = () => Rectangle.Empty;
    public static Func<List<Rectangle>> Icons = () => new();
    public static bool AcceptInjected;
    /// <summary>Called on the hook thread with the screen point; must only post, never block.</summary>
    public static Action<int, int> OnMenu = (_, _) => { };
    public static Action<string> Log = _ => { };
    public static long Calls, MaxTicks;

    public static bool Install()
    {
        _hook = SetWindowsHookExW(WH_MOUSE_LL, ProcRef, N.GetModuleHandleW(null), 0);
        return _hook != IntPtr.Zero;
    }

    public static void Uninstall()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private static IntPtr Proc(int code, IntPtr w, IntPtr l)
    {
        if (code < 0) return CallNextHookEx(_hook, code, w, l);
        int msg = (int)w;
        if (msg != WM_RBUTTONDOWN && msg != WM_RBUTTONUP) return CallNextHookEx(_hook, code, w, l);
        long t0 = Stopwatch.GetTimestamp();
        Calls++;
        var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(l);
        try
        {
            if (msg == WM_RBUTTONUP && _swallowing)
            {
                _swallowing = false;
                OnMenu(info.pt.X, info.pt.Y);
                return (IntPtr)1;
            }
            if (msg != WM_RBUTTONDOWN) return CallNextHookEx(_hook, code, w, l);
            if ((info.flags & LLMHF_INJECTED) != 0 && !AcceptInjected) return CallNextHookEx(_hook, code, w, l);
            string why = Decide(info.pt.X, info.pt.Y);
            Log($"hook: right press at ({info.pt.X},{info.pt.Y}) {(why == "" ? "TAKEN for the Desk menu" : "passed: " + why)}");
            if (why != "") return CallNextHookEx(_hook, code, w, l);
            _swallowing = true;
            return (IntPtr)1;
        }
        finally
        {
            long ticks = Stopwatch.GetTimestamp() - t0;
            if (ticks > MaxTicks) MaxTicks = ticks;
        }
    }

    /// <summary>Empty when the press belongs to the Desk; otherwise why it passes.</summary>
    public static string Decide(int x, int y)
    {
        if (!Piece().Contains(x, y)) return "outside the widget";
        var root = N.GetAncestor(N.WindowFromPoint(new N.POINT { X = x, Y = y }), N.GA_ROOT);
        if (root != N.GetShellWindow()) return $"{N.Class(root)} is in front";
        if (Icons().Any(r => Rectangle.Inflate(r, 2, 2).Contains(x, y))) return "on an icon";
        return "";
    }
}
