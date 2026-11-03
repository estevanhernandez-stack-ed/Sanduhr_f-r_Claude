using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Sanduhr.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.DirectComposition;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;
using static Sanduhr.App.Desk.UnderIcons.InkNative;

namespace Sanduhr.App.Desk.UnderIcons;

/// <summary>
/// The under-icons ink (spec 5.0d): one child window of <c>Progman</c>'s <c>WorkerW</c> per ink island, each showing a premultiplied
/// BGRA frame through DirectComposition, and the low-level mouse hook. All of it lives on one dedicated thread with its own message loop,
/// never the WPF UI thread: a child window under Explorer's window attaches its thread's input queue to Explorer's, and a low-level hook
/// whose thread is busy stalls the mouse everywhere. Callers on any thread post work here; nothing here ever waits on the UI thread.
///
/// The layer keeps what it was asked for (each island's rect and last frame), so when Explorer restarts (the children die with the
/// <c>WorkerW</c>) it finds the new <c>WorkerW</c>, asking <c>Progman</c> for one with <c>0x052C</c> when there is none, and puts every
/// island back without the caller doing anything. When it cannot, <see cref="Failed"/> says why and the Desk falls back to V3.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class InkLayer : IDisposable
{
    private const int FindTries = 10;
    private const int FindPauseMs = 500;
    private const uint WM_WORK = WM_APP + 1;

    private readonly Action<UnderIconsDecision, PxPoint> _press;
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly Dictionary<int, Slot> _slots = [];
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;
    private uint _threadId;
    private string? _startFailure;
    private bool _disposed;

    // Ink thread only.
    private IntPtr _workerW;
    private IntPtr _sentinel;
    private uint _taskbarCreated;
    private IntPtr _hook;
    private WndProc? _childProc, _sentinelProc;
    private HookProc? _hookProc;
    private ID3D11Device? _d3d;
    private ID3D11DeviceContext? _ctx;
    private IDCompositionDevice? _dcomp;
    private UnderIconsDecision?[] _swallowing = new UnderIconsDecision?[2];
    private bool _rebuilding;
    private bool _presented;

    // Written by any thread, read by the hook: replaced whole, never mutated.
    private volatile IReadOnlyList<UnderIconsIsland> _islands = [];
    private volatile IReadOnlyList<PxRect> _icons = [];
    private volatile bool _menuOpen;

    /// <param name="press">Called on the ink thread for a press the hook took, at its release; it must only post, never block.</param>
    public InkLayer(Action<UnderIconsDecision, PxPoint> press) => _press = press;

    /// <summary>What the layer did and why (never window text). Raised on the ink thread.</summary>
    public event Action<string>? Trace;

    /// <summary>The route broke and could not be rebuilt: the Desk should draw above the icons. Raised on the ink thread, once.</summary>
    public event Action<string>? Failed;

    /// <summary>Accept injected presses too (the runner's self-test); off in the app.</summary>
    public bool AcceptInjected { get; init; }

    private long _hookCalls;

    /// <summary>How many mouse events the hook has seen (moves included).</summary>
    public long HookCalls => Interlocked.Read(ref _hookCalls);

    /// <summary>The slowest hook call so far, in milliseconds.</summary>
    public double SlowestHookMs { get; private set; }

    /// <summary>Starts the ink thread and waits until it found the <c>WorkerW</c> and made its device. Null when ready, else why not.</summary>
    public string? Start(TimeSpan wait)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread is not null) return _startFailure;
        _thread = new Thread(Run) { IsBackground = true, Name = "Sanduhr ink" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(wait)) return _startFailure = "the ink thread did not get ready in time";
        return _startFailure;
    }

    /// <summary>Puts island <paramref name="id"/> at <paramref name="rect"/> (physical screen pixels), making its window when new.</summary>
    public void Place(int id, PxRect rect) => Post(() =>
    {
        if (!_slots.TryGetValue(id, out var slot)) _slots[id] = slot = new Slot();
        slot.Rect = rect;
        Seat(slot);
    });

    /// <summary>Removes island <paramref name="id"/> and its window.</summary>
    public void Remove(int id) => Post(() =>
    {
        if (!_slots.Remove(id, out var slot)) return;
        Drop(slot);
    });

    /// <summary>Removes every island.</summary>
    public void Clear() => Post(() =>
    {
        foreach (var slot in _slots.Values) Drop(slot);
        _slots.Clear();
    });

    /// <summary>Shows a frame on island <paramref name="id"/>: <paramref name="pixels"/> is premultiplied BGRA, top-down, <paramref name="w"/>
    /// by <paramref name="h"/>. The array is the layer's from here on; the caller hands over a fresh one each frame.</summary>
    public void Frame(int id, int w, int h, byte[] pixels) => Post(() =>
    {
        if (!_slots.TryGetValue(id, out var slot)) return;
        slot.Pixels = pixels;
        slot.W = w;
        slot.H = h;
        slot.Dirty = true;
    });

    /// <summary>What the hook decides on: the islands as last drawn, in island order.</summary>
    public void SetIslands(IReadOnlyList<UnderIconsIsland> islands) => _islands = islands;

    /// <summary>The desktop icons' rects, physical screen pixels.</summary>
    public void SetIcons(IReadOnlyList<PxRect> icons) => _icons = icons;

    /// <summary>A Desk menu is open: every press passes, so it closes the menu the normal way.</summary>
    public bool MenuOpen { set => _menuOpen = value; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_thread is null) return;
        Post(() => PostQuitMessage(0));
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Post(Action work)
    {
        if (_disposed && _thread is null) return;
        _work.Enqueue(work);
        if (_threadId != 0) PostThreadMessageW(_threadId, WM_WORK, IntPtr.Zero, IntPtr.Zero);
    }

    // ---- the ink thread ----

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        // Explorer's windows are per-monitor v2; a child under them is created in the same context and sized in physical pixels.
        SetThreadDpiAwarenessContext(PerMonitorV2);
        try
        {
            RegisterClasses();
            _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
            _sentinel = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, "SanduhrInkSentinel", null, WS_POPUP, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
            _startFailure = FindWorkerW() ?? MakeDevice();
            if (_startFailure is null) InstallHook();
        }
        catch (Exception e)
        {
            _startFailure = $"the ink thread failed to start: {e.GetType().Name}";
        }
        _ready.Set();
        if (_startFailure is not null)
        {
            Log(_startFailure);
            return;
        }
        Log("ready");
        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.hwnd == IntPtr.Zero && msg.message == WM_WORK)
            {
                Drain();
                continue;
            }
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
        Uninstall();
        foreach (var slot in _slots.Values) Drop(slot);
        _slots.Clear();
        ReleaseDevice();
        if (_sentinel != IntPtr.Zero) DestroyWindow(_sentinel);
    }

    private void Drain()
    {
        while (_work.TryDequeue(out var work))
        {
            try { work(); }
            catch (Exception e) { Log($"work failed: {e.GetType().Name}"); }
        }
        // Nothing may escape the loop: the hook lives on this thread, and a dead thread would leave the mouse waiting on it.
        try { Present(); }
        catch (Exception e) { Log($"present failed: {e.GetType().Name}"); }
    }

    private void RegisterClasses()
    {
        var inst = GetModuleHandleW(null);
        _childProc = ChildProc;
        _sentinelProc = SentinelProc;
        var child = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_childProc),
            hInstance = inst,
            lpszClassName = "SanduhrInk",
        };
        RegisterClassExW(ref child);
        var sentinel = child with { lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_sentinelProc), lpszClassName = "SanduhrInkSentinel" };
        RegisterClassExW(ref sentinel);
    }

    /// <summary>Finds <c>Progman</c>'s <c>WorkerW</c>, asking for one with <c>0x052C</c> when it has none. Null when found, else why not.
    /// Blocks the ink thread for up to <see cref="FindTries"/> pauses, so the hook is never installed while this runs.</summary>
    private string? FindWorkerW()
    {
        for (int i = 0; i < FindTries; i++)
        {
            var shell = GetShellWindow();
            if (shell != IntPtr.Zero && ClassName(shell) == "Progman")
            {
                if (FindWindowExW(shell, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero)
                    return "the desktop is laid out the pre-24H2 way (the icons are not a child of Progman)";
                var ww = FindWindowExW(shell, IntPtr.Zero, "WorkerW", null);
                if (ww != IntPtr.Zero)
                {
                    _workerW = ww;
                    Log($"WorkerW found on try {i + 1}");
                    return null;
                }
                // Explorer makes the wallpaper WorkerW on its own after a restart; ask only once it has had a moment.
                if (i == 1) SendMessageTimeoutW(shell, 0x052C, 0xD, 1, SMTO_ABORTIFHUNG, 500, out _);
            }
            Thread.Sleep(FindPauseMs);
        }
        return "Progman has no WorkerW";
    }

    private string? MakeDevice()
    {
        string step = "D3D11CreateDevice";
        try
        {
            PInvoke.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, default, D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                null, PInvoke.D3D11_SDK_VERSION, out var d3d, out var ctx).ThrowOnFailure();
            _d3d = d3d;
            _ctx = ctx;
            step = "DCompositionCreateDevice";
            PInvoke.DCompositionCreateDevice((IDXGIDevice)d3d, typeof(IDCompositionDevice).GUID, out var dev).ThrowOnFailure();
            _dcomp = (IDCompositionDevice)dev;
            return null;
        }
        catch (Exception e)
        {
            ReleaseDevice();
            return $"DirectComposition is not available ({step}: {e.GetType().Name})";
        }
    }

    private void ReleaseDevice()
    {
        foreach (var slot in _slots.Values) ReleaseComposition(slot);
        Release(ref _dcomp);
        Release(ref _ctx);
        Release(ref _d3d);
    }

    /// <summary>Makes or moves an island's window to its rect, inside the <c>WorkerW</c>'s client coordinates.</summary>
    private void Seat(Slot slot)
    {
        if (_workerW == IntPtr.Zero) return;
        var p = new POINT { X = slot.Rect.X, Y = slot.Rect.Y };
        MapWindowPoints(IntPtr.Zero, _workerW, ref p, 1);
        int w = Math.Max(1, slot.Rect.Width), h = Math.Max(1, slot.Rect.Height);
        if (slot.Hwnd == IntPtr.Zero || !IsWindow(slot.Hwnd))
        {
            slot.Hwnd = CreateWindowExW(WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT, "SanduhrInk", null, WS_CHILD | WS_VISIBLE | WS_DISABLED,
                p.X, p.Y, w, h, _workerW, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
            ReleaseComposition(slot);
            slot.Dirty = slot.Pixels is not null;
            Log(slot.Hwnd == IntPtr.Zero ? $"an ink window could not be made (error {Marshal.GetLastWin32Error()})" : $"ink window made at ({p.X},{p.Y}) {w}x{h}");
            return;
        }
        SetWindowPos(slot.Hwnd, IntPtr.Zero, p.X, p.Y, w, h, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    private void Drop(Slot slot)
    {
        ReleaseComposition(slot);
        if (slot.Hwnd != IntPtr.Zero && IsWindow(slot.Hwnd))
        {
            var h = slot.Hwnd;
            slot.Hwnd = IntPtr.Zero;
            DestroyWindow(h);
        }
        slot.Hwnd = IntPtr.Zero;
    }

    /// <summary>Uploads every island with a new frame, then commits once.</summary>
    private void Present()
    {
        if (_dcomp is null || _ctx is null) return;
        bool any = false;
        foreach (var slot in _slots.Values)
        {
            if (!slot.Dirty || slot.Pixels is null || slot.Hwnd == IntPtr.Zero) continue;
            slot.Dirty = false;
            try
            {
                Upload(slot);
                any = true;
            }
            catch (COMException e)
            {
                // The GPU went away (a driver update or reset): start the device over and draw every island again.
                Log($"an ink frame failed (0x{e.HResult:X8}); making the device again");
                ReleaseDevice();
                if (MakeDevice() is { } why) { Fail(why); return; }
                foreach (var s in _slots.Values) s.Dirty = s.Pixels is not null;
                return;
            }
        }
        if (!any) return;
        _dcomp.Commit();
        if (!_presented) { _presented = true; Log("first frame committed"); }
    }

    private unsafe void Upload(Slot slot)
    {
        var dcomp = _dcomp!;
        if (slot.Target is null)
        {
            dcomp.CreateTargetForHwnd(new HWND(slot.Hwnd), true, out var target);
            dcomp.CreateVisual(out var visual);
            target.SetRoot(visual);
            slot.Target = target;
            slot.Visual = visual;
        }
        if (slot.Surface is null || slot.SW != slot.W || slot.SH != slot.H)
        {
            Release(ref slot.Surface);
            dcomp.CreateSurface((uint)slot.W, (uint)slot.H, DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_PREMULTIPLIED, out var surface);
            slot.Surface = surface;
            slot.SW = slot.W;
            slot.SH = slot.H;
            slot.Visual!.SetContent(surface);
        }
        slot.Surface.BeginDraw(null, typeof(ID3D11Texture2D).GUID, out var drawn, out var offset);
        try
        {
            var box = new D3D11_BOX
            {
                left = (uint)offset.X, top = (uint)offset.Y, front = 0,
                right = (uint)(offset.X + slot.W), bottom = (uint)(offset.Y + slot.H), back = 1,
            };
            fixed (byte* px = slot.Pixels)
                _ctx!.UpdateSubresource((ID3D11Resource)drawn, 0, box, px, (uint)(slot.W * 4), 0);
        }
        finally
        {
            Marshal.ReleaseComObject(drawn);
            slot.Surface.EndDraw();
        }
    }

    private static void ReleaseComposition(Slot slot)
    {
        Release(ref slot.Surface);
        Release(ref slot.Visual);
        Release(ref slot.Target);
        slot.SW = slot.SH = 0;
    }

    private static void Release<T>(ref T? com) where T : class
    {
        if (com is not null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
        com = null;
    }

    /// <summary>Explorer restarted, or an island's window died with the <c>WorkerW</c>: find the new one and put every island back.</summary>
    private void Rebuild(string why)
    {
        if (_rebuilding) return;
        _rebuilding = true;
        try
        {
            Log($"rebuild: {why}");
            Uninstall();
            foreach (var slot in _slots.Values) Drop(slot);
            _workerW = IntPtr.Zero;
            if (FindWorkerW() is { } failure) { Fail(failure); return; }
            foreach (var slot in _slots.Values)
            {
                Seat(slot);
                slot.Dirty = slot.Pixels is not null;
            }
            InstallHook();
            Present();
            Log("rebuild ok");
        }
        finally { _rebuilding = false; }
    }

    private void Fail(string why)
    {
        Log($"route failed: {why}");
        Uninstall();
        Failed?.Invoke(why);
    }

    private IntPtr ChildProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                return HTTRANSPARENT;
            case WM_DESTROY:
                foreach (var slot in _slots.Values)
                    if (slot.Hwnd == hwnd)
                    {
                        // Not ours to destroy: the WorkerW went (Explorer restarted). Rebuild once the messages settle.
                        slot.Hwnd = IntPtr.Zero;
                        ReleaseComposition(slot);
                        Post(() => { if (!_rebuilding && (_workerW == IntPtr.Zero || !IsWindow(_workerW))) Rebuild("the WorkerW went away"); });
                    }
                break;
        }
        return DefWindowProcW(hwnd, msg, w, l);
    }

    private IntPtr SentinelProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
    {
        if (msg == _taskbarCreated && _taskbarCreated != 0)
            Post(() => Rebuild("Explorer restarted (TaskbarCreated)"));
        return DefWindowProcW(hwnd, msg, w, l);
    }

    // ---- the hook ----

    private void InstallHook()
    {
        if (_hook != IntPtr.Zero) return;
        // Compiled now, not on the first press: the first call would otherwise spend milliseconds in the JIT with the mouse waiting.
        System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(typeof(InkLayer).GetMethod(nameof(HookCall),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.MethodHandle);
        System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(typeof(UnderIconsClick).GetMethod(nameof(UnderIconsClick.Decide))!.MethodHandle);
        _ = UnderIconsClick.Decide(DeskButton.Right, default, false, [], [], false);
        _hookProc = HookCall;
        _hook = SetWindowsHookExW(WH_MOUSE_LL, _hookProc, GetModuleHandleW(null), 0);
        Log(_hook == IntPtr.Zero ? $"the mouse hook could not be installed (error {Marshal.GetLastWin32Error()})" : "mouse hook installed");
    }

    private void Uninstall()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _swallowing = new UnderIconsDecision?[2];
    }

    private IntPtr HookCall(int code, IntPtr w, IntPtr l)
    {
        Interlocked.Increment(ref _hookCalls);
        if (code < 0) return CallNextHookEx(_hook, code, w, l);
        int msg = (int)w;
        int button = msg is WM_LBUTTONDOWN or WM_LBUTTONUP ? 0 : msg is WM_RBUTTONDOWN or WM_RBUTTONUP ? 1 : -1;
        if (button < 0) return CallNextHookEx(_hook, code, w, l);
        long t0 = Stopwatch.GetTimestamp();
        try
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(l);
            var at = new PxPoint(info.pt.X, info.pt.Y);
            bool down = msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN;
            if (!down)
            {
                if (_swallowing[button] is not { } taken) return CallNextHookEx(_hook, code, w, l);
                _swallowing[button] = null;
                _press(taken, at);
                return 1;
            }
            if ((info.flags & LLMHF_INJECTED) != 0 && !AcceptInjected) return CallNextHookEx(_hook, code, w, l);
            bool overShell = IsShellRoot(GetAncestor(WindowFromPoint(info.pt), GA_ROOT));
            var decision = UnderIconsClick.Decide(button == 0 ? DeskButton.Left : DeskButton.Right, at, overShell, _icons, _islands, _menuOpen);
            if (decision.Act == UnderIconsAct.Pass) return CallNextHookEx(_hook, code, w, l);
            _swallowing[button] = decision;
            return 1;
        }
        catch
        {
            return CallNextHookEx(_hook, code, w, l);
        }
        finally
        {
            double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            if (ms > SlowestHookMs) SlowestHookMs = ms;
        }
    }

    private void Log(string message) => Trace?.Invoke(message);

    private sealed class Slot
    {
        public PxRect Rect;
        public IntPtr Hwnd;
        public byte[]? Pixels;
        public int W, H, SW, SH;
        public bool Dirty;
        public IDCompositionTarget? Target;
        public IDCompositionVisual? Visual;
        public IDCompositionSurface? Surface;
    }
}
