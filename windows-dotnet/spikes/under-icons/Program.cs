using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace UnderIcons;

/// <summary>
/// THROWAWAY SPIKE, cart run mac-parity-3-6-0 item 1: the Desk drawn behind the desktop icons.
/// A layered test piece is a child of the WorkerW that is a child of Progman (below SHELLDLL_DefView
/// on 24H2+), and a separate V3-style click layer (top-level, owned by Progman, bottom of the normal
/// band, alpha 1, sized to the piece only) logs every click it receives. Everything runs on one thread.
///
/// dcomp (2026-11-02): a plain child of the WorkerW carrying a DirectComposition visual with a premultiplied
/// alpha ramp (magenta, alpha 0 at the left edge to 1 at the right); the shot compares the screen with a baseline
/// taken before the piece existed, so per-pixel alpha reads as a blend of the wallpaper and the ink.
/// Options: --piece-mode ulw|attr|plain|dcomp  --seconds N (default 180)  --log PATH  --shot PATH  --x PX --y PX  --simulate-restart
/// </summary>
internal static class Program
{
    private const uint T_QUIT = 1, T_HEART = 2, T_REBUILD = 3, T_SHOT = 4, T_SIM = 5, T_SETTLE = 6, T_ICONS = 7;
    /// <summary>--shape (2026-11-02 part 2): the click layer covers the whole piece minus the icons, and a right-click on it opens a stand-in Desk menu.</summary>
    private static bool _shape;
    /// <summary>--hook (part 2b): no click layer; RightHook takes right presses on exposed parts of the piece. --selftest injects clicks.</summary>
    private static bool _hook, _selftest;
    private static List<Rectangle> _icons = new();
    private const uint WM_APP_MENU = 0x8001;
    private const uint T_SELF = 8, T_ESC = 9, T_SELF2 = 10, T_ESC2 = 11;
    private static (int X, int Y) _openPt, _iconPt;
    private static string _iconKey = "";
    private static List<Rectangle> _cut = new();

    private static readonly N.WndProc SentinelProcRef = SentinelProc;
    private static readonly N.WndProc PieceProcRef = PieceProc;
    private static readonly N.WndProc ClickProcRef = ClickProc;

    private static IntPtr _sentinel, _piece, _click, _workerW;
    private static uint _taskbarCreated;
    private static int _px, _py, _pw = 360, _ph = 120;
    private static bool _pieceLayered;
    private static IntPtr _memDc, _dib;
    private static int _clicksOnClickLayer, _clicksOnPiece, _rebuilds, _rebuildAttempts;
    private static int _exit;
    private static StreamWriter? _log;
    private static readonly object LogLock = new();
    private static string? _shotPath;
    private static int _seconds = 180;
    private static bool _simulateRestart;
    private static bool _quitting;
    /// <summary>attr (default): layered child, SetLayeredWindowAttributes 255, WM_PAINT; the only mode whose pixels
    /// showed on Neb (2026-10-21). ulw: UpdateLayeredWindow per-pixel alpha (drew nothing on Neb); plain: not layered (drew nothing).</summary>
    private static string _mode = "attr";
    private static DCompPiece? _dcomp;
    private static Bitmap? _baseline;

    private static int Main(string[] args)
    {
        string logPath = "under-icons.log";
        int? ox = null, oy = null;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string? next = i + 1 < args.Length ? args[i + 1] : null;
            switch (a)
            {
                case "--seconds" when next != null: _seconds = int.Parse(next, CultureInfo.InvariantCulture); i++; break;
                case "--log" when next != null: logPath = next; i++; break;
                case "--shot" when next != null: _shotPath = next; i++; break;
                case "--x" when next != null: ox = int.Parse(next, CultureInfo.InvariantCulture); i++; break;
                case "--y" when next != null: oy = int.Parse(next, CultureInfo.InvariantCulture); i++; break;
                case "--piece-mode" when next != null: _mode = next; i++; break;
                case "--simulate-restart": _simulateRestart = true; break;
                case "--shape": _shape = true; break;
                case "--hook": _hook = true; break;
                case "--selftest": _selftest = true; break;
            }
        }
        if (args.Contains("--w")) _pw = int.Parse(args[Array.IndexOf(args, "--w") + 1], CultureInfo.InvariantCulture);
        if (args.Contains("--h")) _ph = int.Parse(args[Array.IndexOf(args, "--h") + 1], CultureInfo.InvariantCulture);
        _log = new StreamWriter(logPath, append: false, new UTF8Encoding(false)) { AutoFlush = true };
        Log($"under-icons spike starting, pid {Environment.ProcessId}, {_seconds}s, log {Path.GetFullPath(logPath)}");
        Log($"OS {Environment.OSVersion.Version}, process DPI awareness via manifest (PerMonitorV2)");

        // Default piece spot: top-left of the primary work area, where desktop icons usually start.
        var mi = new N.MONITORINFO { cbSize = Marshal.SizeOf<N.MONITORINFO>() };
        N.GetMonitorInfoW(N.MonitorFromPoint(new N.POINT(), 1), ref mi);
        _px = ox ?? mi.rcWork.Left + 24;
        _py = oy ?? mi.rcWork.Top + 24;
        Log($"primary monitor {mi.rcMonitor.Left},{mi.rcMonitor.Top} {mi.rcMonitor.W}x{mi.rcMonitor.H}, work {mi.rcWork.Left},{mi.rcWork.Top} {mi.rcWork.W}x{mi.rcWork.H}; piece target ({_px},{_py}) {_pw}x{_ph}");

        var inst = N.GetModuleHandleW(null);
        Register("UnderIconsSentinel", SentinelProcRef, 0);
        Register("UnderIconsPiece", PieceProcRef, 0);
        Register("UnderIconsClick", ClickProcRef, N.CS_DBLCLKS);
        _taskbarCreated = N.RegisterWindowMessageW("TaskbarCreated");

        // Hidden top-level (never message-only: message-only windows miss broadcasts like TaskbarCreated).
        _sentinel = N.CreateWindowExW(N.WS_EX_TOOLWINDOW | N.WS_EX_NOACTIVATE, "UnderIconsSentinel", "", N.WS_POPUP,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
        if (_sentinel == IntPtr.Zero) { Log($"FATAL sentinel create failed, error {N.GetLastError()}"); return 4; }

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; N.PostMessageW(_sentinel, N.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); };

        if (_mode.StartsWith("dcomp")) _baseline = Capture(_px, _py, _pw, _ph);
        if (!Build())
        {
            if (_exit == 2) { Log("RESULT: not applicable on this layout; exiting"); Cleanup(); return 2; }
            Log("initial build failed; will keep retrying (rebuild path)");
            StartRebuild();
        }
        else
        {
            LogStructure("after build");
            N.SetTimer(_sentinel, T_SETTLE, 2000, IntPtr.Zero);
        }

        N.SetTimer(_sentinel, T_QUIT, (uint)(_seconds * 1000), IntPtr.Zero);
        N.SetTimer(_sentinel, T_HEART, 10000, IntPtr.Zero);
        if (_shape || _hook) N.SetTimer(_sentinel, T_ICONS, 2000, IntPtr.Zero);
        if (_hook)
        {
            _icons = ClickShape.IconRects(out string note);
            Log($"icons: {note}");
            RightHook.Piece = () => new Rectangle(_px, _py, _pw, _ph);
            RightHook.Icons = () => _icons;
            RightHook.AcceptInjected = _selftest;
            RightHook.Log = Log;
            RightHook.OnMenu = (x, y) => N.PostMessageW(_sentinel, WM_APP_MENU, (IntPtr)x, (IntPtr)y);
            Log(RightHook.Install() ? "hook: WH_MOUSE_LL installed (right button only)" : $"hook: install FAILED, error {N.GetLastError()}");
            if (_selftest) N.SetTimer(_sentinel, T_SELF, 2500, IntPtr.Zero);
        }
        if (_shotPath != null) N.SetTimer(_sentinel, T_SHOT, 7000, IntPtr.Zero);
        if (_simulateRestart) N.SetTimer(_sentinel, T_SIM, 8000, IntPtr.Zero);

        while (N.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            N.TranslateMessage(ref msg);
            N.DispatchMessageW(ref msg);
        }

        Cleanup();
        Log($"exit: clicks on click layer {_clicksOnClickLayer}, clicks on piece {_clicksOnPiece}, rebuilds {_rebuilds}");
        _log?.Dispose();
        return _exit;
    }

    private static void Register(string name, N.WndProc proc, uint style)
    {
        var wc = new N.WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<N.WNDCLASSEX>(),
            style = style,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
            hInstance = N.GetModuleHandleW(null),
            hCursor = N.LoadCursorW(IntPtr.Zero, (IntPtr)32512),
            lpszClassName = name,
        };
        if (N.RegisterClassExW(ref wc) == 0) throw new InvalidOperationException($"RegisterClassEx {name} failed, error {N.GetLastError()}");
    }

    // ---- build / teardown ------------------------------------------------------------------------

    private static bool Build()
    {
        var shell = N.GetShellWindow();
        if (shell == IntPtr.Zero) { Log("build: no shell window (Explorer not running yet)"); return false; }
        var def = N.FindWindowExW(shell, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (def == IntPtr.Zero)
        {
            // Pre-24H2: the icons view lives in a top-level WorkerW and the wallpaper host is a sibling
            // WorkerW behind it. A different spike; report and stop instead of sending 0x052C.
            Log($"build: Progman {N.Desc(shell)} has no SHELLDLL_DefView child: pre-24H2 layout (top-level WorkerW holds the icons). Not applicable to this spike.");
            if (_rebuilds == 0 && _rebuildAttempts == 0) _exit = 2;
            return false;
        }
        var ww = N.FindWindowExW(shell, IntPtr.Zero, "WorkerW", null);
        if (ww == IntPtr.Zero)
        {
            Log("build: Progman has DefView but no WorkerW child yet");
            if (_rebuildAttempts == 5)
            {
                Log("build: asking Progman for a wallpaper WorkerW with 0x052C (retry 5)");
                N.SendMessageTimeoutW(shell, 0x052C, (IntPtr)0xD, (IntPtr)1, N.SMTO_ABORTIFHUNG, 1000, out _);
            }
            return false;
        }
        _workerW = ww;
        Log($"build: Progman {N.Desc(shell)} rect {N.RectStr(shell)}, DefView {N.Desc(def)}, WorkerW {N.Desc(ww)} rect {N.RectStr(ww)} exstyle 0x{N.GetWindowLong(ww, N.GWL_EXSTYLE):X}");

        DestroyWindows();
        BuildDib();

        // 1. The piece: a layered child of the WorkerW (below the icons).
        var inst = N.GetModuleHandleW(null);
        var pc = new N.POINT { X = _px, Y = _py };
        N.ScreenToClient(ww, ref pc);
        _pieceLayered = _mode == "ulw";
        // dcomp-plain: no layered style; dcomp-attr: layered at alpha 255 (the style that showed in 5.0b); dcomp-nrb: WS_EX_NOREDIRECTIONBITMAP.
        uint exStyle = _mode switch { "plain" or "dcomp-plain" => 0u, "dcomp-nrb" => 0x00200000u, _ => N.WS_EX_LAYERED };
        _piece = N.CreateWindowExW(exStyle, "UnderIconsPiece", "UNDER-ICONS SPIKE", N.WS_CHILD | N.WS_VISIBLE,
            pc.X, pc.Y, _pw, _ph, ww, IntPtr.Zero, inst, IntPtr.Zero);
        if (_piece == IntPtr.Zero)
        {
            Log($"piece: layered child create failed, Win32 error {N.GetLastError()}; falling back to a plain child painted in WM_PAINT");
            _pieceLayered = false;
        }
        else if (_mode is "attr" or "dcomp-attr")
        {
            if (!N.SetLayeredWindowAttributes(_piece, 0, 255, N.LWA_ALPHA)) Log($"piece: SetLayeredWindowAttributes failed, error {N.GetLastError()}");
        }
        if (_piece != IntPtr.Zero && _mode.StartsWith("dcomp"))
        {
            _dcomp?.Dispose();
            _dcomp = new DCompPiece();
            string? err = _dcomp.Attach(_piece, _pw, _ph, RampPixels(_pw, _ph));
            Log(err == null ? "piece: DirectComposition visual committed (premultiplied alpha ramp)" : $"piece: DirectComposition FAILED at {err}");
        }
        else if (_mode == "ulw" && !PushLayered())
        {
            Log($"piece: UpdateLayeredWindow on the child failed, Win32 error {N.GetLastError()}; falling back to a plain child");
            N.DestroyWindow(_piece);
            _piece = IntPtr.Zero;
            _pieceLayered = false;
        }
        if (_piece == IntPtr.Zero)
        {
            _piece = N.CreateWindowExW(0, "UnderIconsPiece", "UNDER-ICONS SPIKE", N.WS_CHILD | N.WS_VISIBLE,
                pc.X, pc.Y, _pw, _ph, ww, IntPtr.Zero, inst, IntPtr.Zero);
            if (_piece == IntPtr.Zero) { Log($"piece: plain child create failed too, Win32 error {N.GetLastError()}"); return false; }
        }
        Log($"piece: created {N.Hex(_piece)} {(_pieceLayered ? "layered (per-pixel alpha)" : "WM_PAINT")} (mode {_mode}), rect {N.RectStr(_piece)}, parent {N.Desc(N.GetParent(_piece))}, visible {N.IsWindowVisible(_piece)}");
        N.SetWindowPos(_piece, IntPtr.Zero, 0, 0, 0, 0, N.SWP_NOMOVE | N.SWP_NOSIZE | N.SWP_NOZORDER | N.SWP_NOACTIVATE | N.SWP_SHOWWINDOW);

        if (_hook) return true;

        // 2. The click layer: V3 shape, top-level, owned by Progman, bottom of the normal band, alpha 1.
        _click = N.CreateWindowExW(N.WS_EX_TOOLWINDOW | N.WS_EX_NOACTIVATE | N.WS_EX_LAYERED, "UnderIconsClick", "", N.WS_POPUP,
            _px, _py, _pw, _ph, shell, IntPtr.Zero, inst, IntPtr.Zero);
        if (_click == IntPtr.Zero) { Log($"click layer: create failed, Win32 error {N.GetLastError()}"); return false; }
        if (!N.SetLayeredWindowAttributes(_click, 0, 1, N.LWA_ALPHA)) Log($"click layer: SetLayeredWindowAttributes failed, error {N.GetLastError()}");
        N.ShowWindow(_click, N.SW_SHOWNOACTIVATE);
        N.SetWindowPos(_click, N.HWND_BOTTOM, 0, 0, 0, 0, N.SWP_NOMOVE | N.SWP_NOSIZE | N.SWP_NOACTIVATE);
        if (_shape) { _iconKey = ""; Reshape("build"); }
        Log($"click layer: created {N.Hex(_click)} rect {N.RectStr(_click)}, owner {N.Desc(N.GetWindow(_click, N.GW_OWNER))}, exstyle 0x{N.GetWindowLong(_click, N.GWL_EXSTYLE):X}, visible {N.IsWindowVisible(_click)}");
        return true;
    }

    private static void BuildDib()
    {
        if (_dib != IntPtr.Zero) { N.DeleteObject(_dib); _dib = IntPtr.Zero; }
        if (_memDc != IntPtr.Zero) { N.DeleteDC(_memDc); _memDc = IntPtr.Zero; }

        using var bmp = new Bitmap(_pw, _ph, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using var path = RoundedRect(new RectangleF(1, 1, _pw - 2, _ph - 2), 22);
            using (var fill = new SolidBrush(Color.FromArgb(255, 255, 0, 255))) g.FillPath(fill, path);
            using (var pen = new Pen(Color.White, 3)) g.DrawPath(pen, path);
            using var big = new Font("Segoe UI", 20, FontStyle.Bold, GraphicsUnit.Pixel);
            using var small = new Font("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);
            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("UNDER-ICONS SPIKE", big, Brushes.White, new RectangleF(0, 22, _pw, 40), center);
            g.DrawString("child of WorkerW, below the icons", small, Brushes.White, new RectangleF(0, 58, _pw, 24), center);
            g.DrawString($"{_pw}x{_ph} at ({_px},{_py})", small, Brushes.White, new RectangleF(0, 82, _pw, 24), center);
        }

        var hdr = new N.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<N.BITMAPINFOHEADER>(), biWidth = _pw, biHeight = -_ph, biPlanes = 1, biBitCount = 32,
        };
        var screenDc = N.GetDC(IntPtr.Zero);
        _dib = N.CreateDIBSection(screenDc, ref hdr, 0, out var bits, IntPtr.Zero, 0);
        _memDc = N.CreateCompatibleDC(screenDc);
        N.ReleaseDC(IntPtr.Zero, screenDc);
        N.SelectObject(_memDc, _dib);

        var data = bmp.LockBits(new Rectangle(0, 0, _pw, _ph), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var buf = new byte[_pw * _ph * 4];
            for (int y = 0; y < _ph; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, buf, y * _pw * 4, _pw * 4);
            Marshal.Copy(buf, 0, bits, buf.Length);
        }
        finally { bmp.UnlockBits(data); }
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static bool PushLayered()
    {
        var size = new N.SIZE { CX = _pw, CY = _ph };
        var src = new N.POINT();
        var blend = new N.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        var screenDc = N.GetDC(IntPtr.Zero);
        try { return N.UpdateLayeredWindow(_piece, screenDc, IntPtr.Zero, ref size, _memDc, ref src, 0, ref blend, N.ULW_ALPHA); }
        finally { N.ReleaseDC(IntPtr.Zero, screenDc); }
    }

    private static void DestroyWindows()
    {
        if (_piece != IntPtr.Zero && N.IsWindow(_piece)) N.DestroyWindow(_piece);
        if (_click != IntPtr.Zero && N.IsWindow(_click)) N.DestroyWindow(_click);
        _piece = _click = IntPtr.Zero;
        _dcomp?.Dispose();
        _dcomp = null;
    }

    private static void Cleanup()
    {
        DestroyWindows();
        if (_hook) { RightHook.Uninstall(); Log($"hook: removed; {RightHook.Calls} right-button events, slowest {RightHook.MaxTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.00} ms"); }
        if (_sentinel != IntPtr.Zero && N.IsWindow(_sentinel)) N.DestroyWindow(_sentinel);
        if (_dib != IntPtr.Zero) N.DeleteObject(_dib);
        if (_memDc != IntPtr.Zero) N.DeleteDC(_memDc);
        _dib = _memDc = IntPtr.Zero;
        Log("cleanup: windows destroyed, GDI objects released");
    }

    // ---- window procedures -----------------------------------------------------------------------

    private static IntPtr SentinelProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        if (msg == WM_APP_MENU)
        {
            int x = (int)w, y = (int)l;
            Log($"Desk menu requested at ({x},{y}); opening");
            if (_selftest) N.SetTimer(_sentinel, T_ESC, 1200, IntPtr.Zero);
            string? pick = ClickShape.ShowMenu(_sentinel, x, y);
            Log($"Desk menu: {pick ?? "dismissed"}; foreground now {N.Desc(N.GetForegroundWindow())}");
            if (pick?.StartsWith("Done") == true) N.PostMessageW(_sentinel, N.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            if (_selftest) N.SetTimer(_sentinel, T_SELF2, 800, IntPtr.Zero);
            return IntPtr.Zero;
        }
        if (_taskbarCreated != 0 && msg == _taskbarCreated)
        {
            _rebuilds++;
            Log($"TaskbarCreated received (Explorer restarted, or simulated); shell is now {N.Desc(N.GetShellWindow())}. Rebuilding both windows (rebuild #{_rebuilds}).");
            StartRebuild();
            return IntPtr.Zero;
        }
        switch (msg)
        {
            case N.WM_TIMER:
                OnTimer((uint)(ulong)w);
                return IntPtr.Zero;
            case N.WM_CLOSE:
                if (!_quitting) { _quitting = true; Log("closing"); }
                N.DestroyWindow(h);
                return IntPtr.Zero;
            case N.WM_DESTROY:
                N.PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return N.DefWindowProcW(h, msg, w, l);
    }

    private static void OnTimer(uint id)
    {
        switch (id)
        {
            case T_QUIT:
                N.KillTimer(_sentinel, T_QUIT);
                Log($"timed run over ({_seconds}s)");
                if (_piece != IntPtr.Zero) LogStructure("final");
                N.PostMessageW(_sentinel, N.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                break;
            case T_HEART:
                Heartbeat();
                break;
            case T_ICONS:
                if (_click != IntPtr.Zero) Reshape("timer");
                if (_hook) _icons = ClickShape.IconRects(out _);
                break;
            case T_SELF:
                N.KillTimer(_sentinel, T_SELF);
                SelfTestStart();
                break;
            case T_ESC:
                N.KillTimer(_sentinel, T_ESC);
                Task.Run(Inject.Escape);
                break;
            case T_SELF2:
                N.KillTimer(_sentinel, T_SELF2);
                if (_iconPt.X >= 0) { Log($"selftest: injected right-click ON AN ICON at ({_iconPt.X},{_iconPt.Y}); Explorer's own menu should open"); var ip = _iconPt; Task.Run(() => Inject.RightClick(ip.X, ip.Y)); N.SetTimer(_sentinel, T_ESC2, 1200, IntPtr.Zero); }
                break;
            case T_ESC2:
                N.KillTimer(_sentinel, T_ESC2);
                Log($"selftest: foreground before Escape is {N.Desc(N.GetForegroundWindow())}");
                Task.Run(Inject.Escape);
                break;
            case T_SETTLE:
                N.KillTimer(_sentinel, T_SETTLE);
                if (_piece != IntPtr.Zero) LogStructure("after 2s settle");
                break;
            case T_SHOT:
                N.KillTimer(_sentinel, T_SHOT);
                TakeShot(_shotPath!);
                break;
            case T_SIM:
                N.KillTimer(_sentinel, T_SIM);
                Log("SIMULATED restart: destroying both windows as Explorer's exit would, then posting TaskbarCreated to this process's sentinel only");
                DestroyWindows();
                N.PostMessageW(_sentinel, _taskbarCreated, IntPtr.Zero, IntPtr.Zero);
                break;
            case T_REBUILD:
                RebuildTick();
                break;
        }
    }

    private static void StartRebuild()
    {
        _rebuildAttempts = 0;
        DestroyWindows();
        N.SetTimer(_sentinel, T_REBUILD, 500, IntPtr.Zero);
    }

    private static void RebuildTick()
    {
        _rebuildAttempts++;
        if (Build())
        {
            N.KillTimer(_sentinel, T_REBUILD);
            Log($"rebuild ok after {_rebuildAttempts} attempt(s)");
            if (_shotPath != null) N.SetTimer(_sentinel, T_SHOT, 3000, IntPtr.Zero);   // re-probe the pixels under the new Explorer
            if (_selftest) N.SetTimer(_sentinel, T_SELF, 5000, IntPtr.Zero);       // and the hook
            LogStructure("after rebuild");
        }
        else if (_exit == 2 || _rebuildAttempts >= 40)
        {
            N.KillTimer(_sentinel, T_REBUILD);
            Log($"rebuild gave up after {_rebuildAttempts} attempt(s)");
        }
    }

    private static IntPtr PieceProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        switch (msg)
        {
            case N.WM_LBUTTONDOWN or N.WM_RBUTTONDOWN or N.WM_LBUTTONUP or N.WM_RBUTTONUP:
                _clicksOnPiece++;
                Log($"PIECE received mouse message 0x{msg:X} (unexpected: the icons view should sit above it)");
                return IntPtr.Zero;
            case N.WM_ERASEBKGND:
                return (IntPtr)1;
            case N.WM_PAINT:
                {
                    var dc = N.BeginPaint(h, out var ps);
                    if (!_pieceLayered && !_mode.StartsWith("dcomp") && _memDc != IntPtr.Zero) N.BitBlt(dc, 0, 0, _pw, _ph, _memDc, 0, 0, 0x00CC0020);
                    N.EndPaint(h, ref ps);
                    return IntPtr.Zero;
                }
        }
        return N.DefWindowProcW(h, msg, w, l);
    }

    private static IntPtr ClickProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        switch (msg)
        {
            case N.WM_MOUSEACTIVATE:
                return (IntPtr)N.MA_NOACTIVATE;
            case N.WM_ERASEBKGND:
                return (IntPtr)1;
            case N.WM_PAINT:
                {
                    var dc = N.BeginPaint(h, out var ps);
                    var r = ps.rcPaint;
                    N.FillRect(dc, ref r, N.GetStockObject(4)); // BLACK_BRUSH, at alpha 1 of 255
                    N.EndPaint(h, ref ps);
                    return IntPtr.Zero;
                }
            case N.WM_LBUTTONDOWN or N.WM_LBUTTONUP or N.WM_LBUTTONDBLCLK or N.WM_RBUTTONDOWN or N.WM_RBUTTONUP or N.WM_MBUTTONDOWN or N.WM_MOUSEWHEEL:
                {
                    string name = msg switch
                    {
                        N.WM_LBUTTONDOWN => "LBUTTONDOWN", N.WM_LBUTTONUP => "LBUTTONUP", N.WM_LBUTTONDBLCLK => "LBUTTONDBLCLK",
                        N.WM_RBUTTONDOWN => "RBUTTONDOWN", N.WM_RBUTTONUP => "RBUTTONUP", N.WM_MBUTTONDOWN => "MBUTTONDOWN",
                        _ => "MOUSEWHEEL",
                    };
                    if (msg is N.WM_LBUTTONDOWN or N.WM_RBUTTONDOWN or N.WM_MBUTTONDOWN or N.WM_LBUTTONDBLCLK) _clicksOnClickLayer++;
                    N.GetCursorPos(out var cur);
                    if (_shape && msg == N.WM_RBUTTONUP)
                    {
                        Log($"CLICK RBUTTONUP on the shaped click layer at screen ({cur.X},{cur.Y}); opening the stand-in Desk menu");
                        Log($"Desk menu: {ClickShape.ShowMenu(_sentinel, cur.X, cur.Y) ?? "dismissed"}");
                        return IntPtr.Zero;
                    }
                    if (msg != N.WM_MOUSEWHEEL)
                    {
                        long lp = l.ToInt64();
                        int cx = (short)(lp & 0xFFFF), cy = (short)((lp >> 16) & 0xFFFF);
                        Log($"CLICK {name} on click layer, client ({cx},{cy}), screen ({cur.X},{cur.Y})");
                    }
                    else Log($"CLICK {name} on click layer, screen ({cur.X},{cur.Y})");
                    return IntPtr.Zero;
                }
        }
        return N.DefWindowProcW(h, msg, w, l);
    }

    // ---- structural log --------------------------------------------------------------------------

    private static void Heartbeat()
    {
        var shell = N.GetShellWindow();
        bool pieceOk = _piece != IntPtr.Zero && N.IsWindow(_piece);
        bool clickOk = _click != IntPtr.Zero && N.IsWindow(_click);
        string above = clickOk ? (IsAbove(_click, shell) ? "above Progman" : "NOT above Progman") : "n/a";
        string parent = pieceOk ? N.Desc(N.GetParent(_piece)) : "n/a";
        Log($"heartbeat: piece alive {pieceOk} visible {(pieceOk && N.IsWindowVisible(_piece))} parent {parent}; click layer alive {clickOk} {above}; clicks {_clicksOnClickLayer}");
    }

    private static List<IntPtr> TopLevelOrder()
    {
        var list = new List<IntPtr>();
        for (var w = N.GetTopWindow(IntPtr.Zero); w != IntPtr.Zero && list.Count < 5000; w = N.GetWindow(w, N.GW_HWNDNEXT)) list.Add(w);
        return list;
    }

    private static bool IsAbove(IntPtr a, IntPtr b)
    {
        var order = TopLevelOrder();
        int ia = order.IndexOf(a), ib = order.IndexOf(b);
        return ia >= 0 && ib >= 0 && ia < ib;
    }

    private static void LogStructure(string tag)
    {
        Log($"--- structure [{tag}] ---");
        var shell = N.GetShellWindow();
        Log($"shell window {N.Desc(shell)} rect {N.RectStr(shell)} exstyle 0x{N.GetWindowLong(shell, N.GWL_EXSTYLE):X}");

        // Progman's children, top to bottom (FindWindowEx walks the sibling z-order).
        int n = 0;
        for (var c = N.FindWindowExW(shell, IntPtr.Zero, null, null); c != IntPtr.Zero; c = N.FindWindowExW(shell, c, null, null), n++)
            Log($"  Progman child #{n} (top to bottom): {N.Desc(c)} rect {N.RectStr(c)} visible {N.IsWindowVisible(c)} exstyle 0x{N.GetWindowLong(c, N.GWL_EXSTYLE):X}");

        // Piece parent chain.
        var chain = new StringBuilder();
        for (var p = _piece; p != IntPtr.Zero; p = N.GetParent(p))
            chain.Append(chain.Length == 0 ? "" : " -> ").Append(N.Desc(p));
        Log($"piece parent chain: {chain}");
        Log($"piece rect {N.RectStr(_piece)}, layered {_pieceLayered}, visible {N.IsWindowVisible(_piece)}, DPI {N.GetDpiForWindow(_piece)}");

        // Icons view.
        var def = N.FindWindowExW(shell, IntPtr.Zero, "SHELLDLL_DefView", null);
        var lv = N.FindWindowExW(def, IntPtr.Zero, "SysListView32", null);
        if (lv != IntPtr.Zero && N.SendMessageTimeoutW(lv, N.LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero, N.SMTO_ABORTIFHUNG, 300, out var count) != IntPtr.Zero)
            Log($"desktop icon count (LVM_GETITEMCOUNT) {count}");

        // Click layer z-order.
        var order = TopLevelOrder();
        int ic = order.IndexOf(_click), ip = order.IndexOf(shell);
        Log($"click layer {N.Desc(_click)} rect {N.RectStr(_click)} (piece rect {N.RectStr(_piece)}), owner {N.Desc(N.GetWindow(_click, N.GW_OWNER))}");
        Log($"top-level z-order: {order.Count} windows; click layer index {ic}, Progman (SHELLDLL_DefView's host) index {ip}; click layer ABOVE Progman: {(ic >= 0 && ip >= 0 && ic < ip)}");
        if (ic >= 0)
        {
            var span = new StringBuilder();
            for (int i = Math.Max(0, ic - 2); i <= Math.Min(order.Count - 1, Math.Max(ic + 2, Math.Min(ip, ic + 10))); i++)
                span.Append(i == ic ? "[CLICK] " : "").Append(N.Desc(order[i])).Append(i == ip ? " [PROGMAN]" : "").Append(" | ");
            Log($"z-order around click layer: {span}");
        }

        if (_shape) ShapeSweep();

        // Hit tests.
        int cxs = _px + _pw / 2, cys = _py + _ph / 2;
        HitTest("piece centre", cxs, cys, shell, def);
        HitTest("outside, right of piece (+80px)", _px + _pw + 80, cys, shell, def);
        HitTest("outside, below piece (+80px)", cxs, _py + _ph + 80, shell, def);
    }

    private static void HitTest(string label, int x, int y, IntPtr shell, IntPtr def)
    {
        var pt = new N.POINT { X = x, Y = y };
        var wfp = N.WindowFromPoint(pt);
        var root = wfp == IntPtr.Zero ? IntPtr.Zero : N.GetAncestor(wfp, N.GA_ROOT);
        var cp = pt;
        N.ScreenToClient(shell, ref cp);
        var cwfAll = N.ChildWindowFromPointEx(shell, cp, 0);
        var cwfSkip = N.ChildWindowFromPointEx(shell, cp, N.CWP_SKIPINVISIBLE | N.CWP_SKIPTRANSPARENT);
        var real = N.RealChildWindowFromPoint(shell, cp);
        string nc(IntPtr target)
        {
            if (target == IntPtr.Zero) return "n/a";
            return N.SendMessageTimeoutW(target, N.WM_NCHITTEST, IntPtr.Zero, N.MakeLParam(x, y), N.SMTO_ABORTIFHUNG, 300, out var r) != IntPtr.Zero
                ? $"{(short)(r.ToInt64() & 0xFFFF)}" : "timeout";
        }
        Log($"hit test, {label} ({x},{y}): WindowFromPoint {N.Desc(wfp)} (root {N.Desc(root)}); " +
            $"ChildWindowFromPointEx(Progman) all {N.Desc(cwfAll)}, skip-invisible+transparent {N.Desc(cwfSkip)}; RealChildWindowFromPoint {N.Desc(real)}; " +
            $"WM_NCHITTEST DefView {nc(def)} (1=HTCLIENT), click layer {nc(_click)}");
    }

    // ---- screenshot ------------------------------------------------------------------------------

    private static void TakeShot(string path)
    {
        try
        {
            int vx = N.GetSystemMetrics(N.SM_XVIRTUALSCREEN), vy = N.GetSystemMetrics(N.SM_YVIRTUALSCREEN);
            int vw = N.GetSystemMetrics(N.SM_CXVIRTUALSCREEN), vh = N.GetSystemMetrics(N.SM_CYVIRTUALSCREEN);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var bmp = new Bitmap(vw, vh, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(vx, vy, 0, 0, new Size(vw, vh));
            bmp.Save(path, ImageFormat.Png);

            // Pixel probe over the piece's rect: sample inside the rounded corners, away from the text.
            int total = 0, magenta = 0;
            for (int row = 0; row < 2; row++)
                for (int ix = 30; ix <= _pw - 30; ix += 30)
                {
                    int sx = _px + ix - vx, sy = (row == 0 ? _py + 12 : _py + _ph - 12) - vy;
                    if (sx < 0 || sy < 0 || sx >= vw || sy >= vh) continue;
                    var c = bmp.GetPixel(sx, sy);
                    total++;
                    if (c.R > 200 && c.B > 200 && c.G < 80) magenta++;
                }
            var centre = new N.POINT { X = _px + _pw / 2, Y = _py + _ph / 2 };
            var top = N.WindowFromPoint(centre);
            if (_mode.StartsWith("dcomp")) RampProbe(bmp, vx, vy);
            Log($"screenshot {Path.GetFullPath(path)} ({vw}x{vh} at {vx},{vy}); magenta samples in the piece rect {magenta}/{total}; topmost window at piece centre: {N.Desc(N.GetAncestor(top, N.GA_ROOT))}");
            Log(magenta == 0 && total > 0
                ? "screenshot: NO magenta pixels in the piece rect (covered by an app window, or the WorkerW child draws nothing visible)"
                : "screenshot: magenta pixels ARE present in the piece rect");
        }
        catch (Exception e) { Log($"screenshot failed: {e.GetType().Name}: {e.Message}"); }
    }

    /// <summary>Magenta whose alpha climbs from 0 at the left edge to 1 at the right, premultiplied BGRA, top-down.</summary>
    private static byte[] RampPixels(int w, int h)
    {
        var buf = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte a = (byte)Math.Round(255.0 * x / (w - 1));
                int i = (y * w + x) * 4;
                buf[i] = a; buf[i + 1] = 0; buf[i + 2] = a; buf[i + 3] = a;   // B G R A, premultiplied magenta
            }
        return buf;
    }

    private static Bitmap Capture(int x, int y, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
        return bmp;
    }

    /// <summary>Per sample: expected = baseline * (1 - a) + magenta * a. "blend" means within 20 per channel of that
    /// and, for 0.2 &lt; a &lt; 0.8, clearly unlike both the baseline and opaque magenta: only real per-pixel alpha
    /// gives that. Rows at a quarter, half and three quarters of the height, every 8 px.</summary>
    private static void RampProbe(Bitmap shot, int vx, int vy)
    {
        if (_baseline == null) { Log("ramp probe: no baseline"); return; }
        int match = 0, mid = 0, midBlend = 0, unchanged = 0, opaque = 0, total = 0;
        var sb = new StringBuilder();
        foreach (int ry in new[] { _ph / 4, _ph / 2, _ph * 3 / 4 })
            for (int rx = 0; rx < _pw; rx += 8)
            {
                double a = (double)rx / (_pw - 1);
                var b = _baseline.GetPixel(rx, ry);
                var s = shot.GetPixel(_px + rx - vx, _py + ry - vy);
                double er = b.R * (1 - a) + 255 * a, eg = b.G * (1 - a), eb = b.B * (1 - a) + 255 * a;
                double d = Math.Max(Math.Abs(s.R - er), Math.Max(Math.Abs(s.G - eg), Math.Abs(s.B - eb)));
                int db = Math.Max(Math.Abs(s.R - b.R), Math.Max(Math.Abs(s.G - b.G), Math.Abs(s.B - b.B)));
                int dm = Math.Max(Math.Abs(s.R - 255), Math.Max(s.G, Math.Abs(s.B - 255)));
                total++;
                if (d <= 20) match++;
                if (db <= 6) unchanged++;
                if (dm <= 12) opaque++;
                if (a > 0.2 && a < 0.8) { mid++; if (d <= 20 && db > 20 && dm > 20) midBlend++; }
                if (ry == _ph / 2 && rx % 64 == 0) sb.Append($" a={a:0.00} base=({b.R},{b.G},{b.B}) screen=({s.R},{s.G},{s.B}) want=({er:0},{eg:0},{eb:0});");
            }
        Log($"ramp probe: {match}/{total} samples match the alpha blend; mid-ramp true blends {midBlend}/{mid}; unchanged from baseline {unchanged}/{total}; opaque magenta {opaque}/{total}");
        Log($"ramp probe, middle row:{sb}");
        Log(midBlend * 10 >= mid * 8 ? "VERDICT: per-pixel alpha SHOWS below the icons" : unchanged * 10 >= total * 8 ? "VERDICT: the piece draws NOTHING below the icons" : "VERDICT: mixed, read the samples");
    }

    private static void SelfTestStart()
    {
        var piece = new Rectangle(_px, _py, _pw, _ph);
        _openPt = (-1, -1); _iconPt = (-1, -1);
        var ic = _icons.FirstOrDefault(r => piece.Contains(r.X + r.Width / 2, r.Y + r.Height / 2));
        if (!ic.IsEmpty) _iconPt = (ic.X + ic.Width / 2, ic.Y + ic.Height / 2);
        for (int y = _py + 10; y < _py + _ph && _openPt.X < 0; y += 15)
            for (int x = _px + 10; x < _px + _pw && _openPt.X < 0; x += 15)
                if (RightHook.Decide(x, y) == "") _openPt = (x, y);
        Log($"selftest: Decide(open point {_openPt}) '{(_openPt.X < 0 ? "none found" : RightHook.Decide(_openPt.X, _openPt.Y))}', Decide(icon {_iconPt}) '{(_iconPt.X < 0 ? "no icon" : RightHook.Decide(_iconPt.X, _iconPt.Y))}', Decide(outside) '{RightHook.Decide(_px + _pw + 50, _py + 10)}'");
        if (_openPt.X >= 0) { Log($"selftest: injected right-click on an exposed point {_openPt}"); var op = _openPt; Task.Run(() => Inject.RightClick(op.X, op.Y)); }
    }

    private static void Reshape(string why)
    {
        var icons = ClickShape.IconRects(out string note);
        string key = string.Join(";", icons.Select(r => $"{r.X},{r.Y},{r.Width},{r.Height}"));
        if (key == _iconKey) return;
        _iconKey = key;
        _cut = ClickShape.Apply(_click, new Rectangle(_px, _py, _pw, _ph), icons);
        Log($"click layer shaped ({why}): {note}; {_cut.Count} cut out of the piece");
    }

    /// <summary>Every 20 px over the piece: which top-level window takes a click there. An icon point must never
    /// reach the click layer; an exposed point should, unless another app's window (Fences, say) is in front.</summary>
    private static void ShapeSweep()
    {
        int iconPts = 0, iconOnLayer = 0, openPts = 0, openOnLayer = 0;
        var others = new Dictionary<string, int>();
        for (int y = _py + 5; y < _py + _ph; y += 20)
            for (int x = _px + 5; x < _px + _pw; x += 20)
            {
                var hit = N.WindowFromPoint(new N.POINT { X = x, Y = y });
                var root = N.GetAncestor(hit, N.GA_ROOT);
                bool onLayer = root == _click;
                bool onIcon = _cut.Any(r => Rectangle.Inflate(r, 2, 2).Contains(x, y));
                if (onIcon) { iconPts++; if (onLayer) iconOnLayer++; continue; }
                openPts++;
                if (onLayer) { openOnLayer++; continue; }
                string k = $"{N.Class(hit)} in {N.Class(root)}";
                others[k] = others.GetValueOrDefault(k) + 1;
            }
        Log($"shape sweep: icon points {iconPts}, of them on the click layer {iconOnLayer} (want 0); exposed points {openPts}, on the click layer {openOnLayer}; elsewhere: {string.Join(", ", others.Select(kv => $"{kv.Key} x{kv.Value}"))}");
    }

    private static void Log(string line)
    {
        string s = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {line}";
        lock (LogLock)
        {
            Console.WriteLine(s);
            _log?.WriteLine(s);
        }
    }
}
