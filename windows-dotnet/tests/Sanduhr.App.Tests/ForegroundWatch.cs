using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sanduhr.App.Tests;

/// <summary>How a watch asks Windows who has the foreground. A seam: a fact hands in a fake to simulate an outside app.</summary>
internal sealed record ForegroundProbe(Func<IntPtr> Query, Func<IntPtr, (int Pid, string Name)> Describe)
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    public static ForegroundProbe Real { get; } = new(GetForegroundWindow, hwnd =>
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        try { return ((int)pid, Process.GetProcessById((int)pid).ProcessName); }
        catch (Exception) { return ((int)pid, "an unknown process"); }
    });
}

/// <summary>
/// The tests never take the foreground (NoActivate: WM_SETFOCUS only), so a foreground change during a run is an
/// outside app (an input tool, a game) pulling real keyboard focus away. This records the foreground when a run starts
/// and, if a focus test fails, says whether it changed since: polled at failure time, and through an
/// EVENT_SYSTEM_FOREGROUND hook so a change that reverted is still seen. The hook is out of context, so it delivers while the
/// app thread pumps. Our own process taking the foreground is not outside interference and never counts.
/// </summary>
internal sealed class ForegroundWatch : IDisposable
{
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    private const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0;

    /// <summary>The watch of the run in progress on the app thread; null outside <see cref="AppHost.Run"/>.</summary>
    public static ForegroundWatch? Current { get; private set; }

    private readonly ForegroundProbe _probe;
    private readonly IntPtr _start;
    private readonly WinEventProc _proc;   // held: the hook calls it for as long as it lives
    private IntPtr _hook;
    private IntPtr _seen;
    private int _events;

    private ForegroundWatch(ForegroundProbe probe)
    {
        _probe = probe;
        _start = probe.Query();
        _proc = (_, _, hwnd, _, _, _, _) => { _events++; Note(hwnd); };
        if (ReferenceEquals(probe, ForegroundProbe.Real))
            _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    public static ForegroundWatch Begin(ForegroundProbe? probe) => Current = new ForegroundWatch(probe ?? ForegroundProbe.Real);

    /// <summary>True once the run routes real keyboard focus (NoActivate.RouteKeyboard): only such a run can be skipped.</summary>
    public bool RoutesFocus { get; private set; }
    public void MarkFocusTest() => RoutesFocus = true;

    /// <summary>A foreground change the hook saw, or a fake in a fact. Our own process does not count.</summary>
    public void Note(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || hwnd == _start || _seen != IntPtr.Zero) return;
        if (_probe.Describe(hwnd).Pid == Environment.ProcessId) return;
        _seen = hwnd;
    }

    /// <summary>The skip reason when an outside app has taken the foreground since the run began, else null.</summary>
    public string? Interference()
    {
        var now = _probe.Query();
        if (_seen == IntPtr.Zero && now != IntPtr.Zero && now != _start)
            Note(now);
        if (_seen == IntPtr.Zero) return null;
        var (pid, name) = _probe.Describe(_seen);
        return $"Skipped: the foreground changed to {name} ({pid}) during the test; focus results are not reliable.";
    }

    /// <summary>
    /// What the foreground did during a focus test that failed without counting as interference, so the next such failure
    /// says whether an outside app was involved: who held the foreground at the start and now, and how many foreground
    /// events the hook saw (one that re-activates the same window still counts).
    /// </summary>
    public string Diagnosis()
    {
        string Who(IntPtr hwnd) => hwnd == IntPtr.Zero ? "none" : $"{_probe.Describe(hwnd).Name} ({_probe.Describe(hwnd).Pid})";
        return $"[foreground at start: {Who(_start)}; at failure: {Who(_probe.Query())}; foreground events during the test: {_events}]";
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
        if (ReferenceEquals(Current, this)) Current = null;
    }
}
