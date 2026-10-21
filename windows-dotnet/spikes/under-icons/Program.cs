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
/// Options: --piece-mode ulw|attr|plain  --seconds N (default 180)  --log PATH  --shot PATH  --x PX --y PX  --simulate-restart
/// </summary>
internal static class Program
{
    private const uint T_QUIT = 1, T_HEART = 2, T_REBUILD = 3, T_SHOT = 4, T_SIM = 5, T_SETTLE = 6;

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
            }
        }
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
        if (_shotPath != null) N.SetTimer(_sentinel, T_SHOT, 4000, IntPtr.Zero);
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
        _piece = N.CreateWindowExW(_mode == "plain" ? 0 : N.WS_EX_LAYERED, "UnderIconsPiece", "UNDER-ICONS SPIKE", N.WS_CHILD | N.WS_VISIBLE,
            pc.X, pc.Y, _pw, _ph, ww, IntPtr.Zero, inst, IntPtr.Zero);
        if (_piece == IntPtr.Zero)
        {
            Log($"piece: layered child create failed, Win32 error {N.GetLastError()}; falling back to a plain child painted in WM_PAINT");
            _pieceLayered = false;
        }
        else if (_mode == "attr")
        {
            if (!N.SetLayeredWindowAttributes(_piece, 0, 255, N.LWA_ALPHA)) Log($"piece: SetLayeredWindowAttributes failed, error {N.GetLastError()}");
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

        // 2. The click layer: V3 shape, top-level, owned by Progman, bottom of the normal band, alpha 1.
        _click = N.CreateWindowExW(N.WS_EX_TOOLWINDOW | N.WS_EX_NOACTIVATE | N.WS_EX_LAYERED, "UnderIconsClick", "", N.WS_POPUP,
            _px, _py, _pw, _ph, shell, IntPtr.Zero, inst, IntPtr.Zero);
        if (_click == IntPtr.Zero) { Log($"click layer: create failed, Win32 error {N.GetLastError()}"); return false; }
        if (!N.SetLayeredWindowAttributes(_click, 0, 1, N.LWA_ALPHA)) Log($"click layer: SetLayeredWindowAttributes failed, error {N.GetLastError()}");
        N.ShowWindow(_click, N.SW_SHOWNOACTIVATE);
        N.SetWindowPos(_click, N.HWND_BOTTOM, 0, 0, 0, 0, N.SWP_NOMOVE | N.SWP_NOSIZE | N.SWP_NOACTIVATE);
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
    }

    private static void Cleanup()
    {
        DestroyWindows();
        if (_sentinel != IntPtr.Zero && N.IsWindow(_sentinel)) N.DestroyWindow(_sentinel);
        if (_dib != IntPtr.Zero) N.DeleteObject(_dib);
        if (_memDc != IntPtr.Zero) N.DeleteDC(_memDc);
        _dib = _memDc = IntPtr.Zero;
        Log("cleanup: windows destroyed, GDI objects released");
    }

    // ---- window procedures -----------------------------------------------------------------------

    private static IntPtr SentinelProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
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
                    if (!_pieceLayered && _memDc != IntPtr.Zero) N.BitBlt(dc, 0, 0, _pw, _ph, _memDc, 0, 0, 0x00CC0020);
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
            Log($"screenshot {Path.GetFullPath(path)} ({vw}x{vh} at {vx},{vy}); magenta samples in the piece rect {magenta}/{total}; topmost window at piece centre: {N.Desc(N.GetAncestor(top, N.GA_ROOT))}");
            Log(magenta == 0 && total > 0
                ? "screenshot: NO magenta pixels in the piece rect (covered by an app window, or the WorkerW child draws nothing visible)"
                : "screenshot: magenta pixels ARE present in the piece rect");
        }
        catch (Exception e) { Log($"screenshot failed: {e.GetType().Name}: {e.Message}"); }
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
