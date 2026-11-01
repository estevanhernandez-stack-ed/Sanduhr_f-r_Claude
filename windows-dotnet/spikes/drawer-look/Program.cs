using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfMedia = System.Windows.Media;

namespace DrawerLook;

/// <summary>THROWAWAY SPIKE. Modes: monitors | windows | blur &lt;none|sbt|acrylic|blurbehind&gt; [x y] [top] [nolayer]
/// [flags=HEX] [tint=AABBGGRR] [rgn] [corner] | icons [tag] [thumb] [pw=N] [pwdefview|pwprogman].
/// Writes PNGs and probe.log under out/ beside the project (ignored); the kept evidence is in shots/.</summary>
public static class Program
{
    static readonly string Out = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "out"));
    static readonly StringBuilder LogText = new();

    static void Log(string s) { Console.WriteLine(s); LogText.AppendLine(s); }

    [STAThread]
    public static int Main(string[] args)
    {
        Directory.CreateDirectory(Out);
        // Physical pixels everywhere (the Desk creates its windows per-monitor aware on the thread; same effect here).
        N.SetProcessDpiAwarenessContext(new IntPtr(-4));
        string mode = args.Length > 0 ? args[0] : "icons";
        Log($"== {mode} {string.Join(' ', args.Skip(1))}  {DateTime.Now:yyyy-MM-dd HH:mm:ss}  OS {Environment.OSVersion.Version}");
        try
        {
            switch (mode)
            {
                case "monitors": Monitors(); break;
                case "windows": Windows(); break;
                case "blur": Blur(args); break;
                case "icons": Icons(args.Length > 1 ? args[1] : "run"); break;
                default: Log("unknown mode"); return 2;
            }
        }
        catch (Exception e) { Log("ERROR " + e); return 1; }
        finally { File.AppendAllText(Path.Combine(Out, "probe.log"), LogText.ToString()); }
        return 0;
    }

    // ---------- monitors / windows ----------

    static void Monitors()
    {
        N.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr _, ref N.RECT _, IntPtr _) =>
        {
            var mi = new N.MONITORINFOEX { cbSize = Marshal.SizeOf<N.MONITORINFOEX>() };
            N.GetMonitorInfoW(m, ref mi);
            N.GetDpiForMonitor(m, 0, out uint dx, out _);
            Log($"monitor {mi.szDevice} bounds {mi.rcMonitor} work {mi.rcWork} dpi {dx} ({dx / 96.0:P0}) primary {(mi.dwFlags & 1) != 0}");
            return true;
        }, IntPtr.Zero);
    }

    static List<(IntPtr H, string Cls, string Title, N.RECT R)> VisibleWindows()
    {
        var list = new List<(IntPtr, string, string, N.RECT)>();
        N.EnumWindows((h, _) =>
        {
            if (!N.IsWindowVisible(h) || N.IsIconic(h)) return true;
            N.DwmGetWindowAttribute(h, 14, out int cloaked, 4);
            if (cloaked != 0) return true;
            var cls = new StringBuilder(256); N.GetClassName(h, cls, 256);
            var t = new StringBuilder(256); N.GetWindowText(h, t, 256);
            string c = cls.ToString();
            if (c is "Progman" or "WorkerW") return true;
            if (N.DwmGetWindowAttribute(h, 9, out N.RECT r, Marshal.SizeOf<N.RECT>()) != 0) N.GetWindowRect(h, out r);
            if (r.Right - r.Left <= 0 || r.Bottom - r.Top <= 0) return true;
            list.Add((h, c, t.ToString(), r));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    static void Windows()
    {
        foreach (var w in VisibleWindows()) Log($"  {w.H:X} {w.Cls,-32} {w.R}  \"{Trunc(w.Title, 40)}\"");
        Log($"free spot (primary work area): {FreeSpot(360, 330)?.ToString() ?? "none"}");
    }

    static (int X, int Y)? FreeSpot(int w, int h)
    {
        var mon = N.MonitorFromPoint(new DesktopIconsNative.POINT(), 1);
        var mi = new N.MONITORINFOEX { cbSize = Marshal.SizeOf<N.MONITORINFOEX>() };
        N.GetMonitorInfoW(mon, ref mi);
        var obstacles = VisibleWindows().Select(x => x.R).ToList();
        var a = mi.rcWork;
        for (int y = a.Top + 20; y + h <= a.Bottom - 20; y += 20)
            for (int x = a.Right - w - 20; x >= a.Left + 20; x -= 20)
                if (!obstacles.Any(r => r.Left < x + w && r.Right > x && r.Top < y + h && r.Bottom > y))
                    return (x, y);
        return null;
    }

    // ---------- Q1: blur ----------

    static void Blur(string[] args)
    {
        string method = args.Length > 1 ? args[1] : "none";
        int x, y;
        if (args.Length > 3) { x = int.Parse(args[2]); y = int.Parse(args[3]); }
        else if (FreeSpot(360, 330) is { } s) { x = s.X; y = s.Y; }
        else { Log("no free spot over the wallpaper; pass x y"); return; }
        const int W = 360, H = 330;
        bool top = args.Contains("top");
        Log($"blur method {method} at {x},{y} size {W}x{H} {(top ? "topmost over a pattern window (no wallpaper visible: every monitor has a maximized app)" : "bottom (Desk z-order)")}");

        HwndSource? pattern = null;
        if (top)
        {
            // A busy stand-in for the wallpaper: a plain opaque window, topmost, just under the probe.
            pattern = new HwndSource(new HwndSourceParameters("DrawerLookPattern")
            {
                WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x00000080 | 0x08000000 | 0x00000008,
                PositionX = x - 30, PositionY = y - 30, Width = W + 60, Height = H + 60,
            });
            var canvas = new WpfControls.Canvas { Width = W + 60, Height = H + 60, Background = WpfMedia.Brushes.White };
            var rnd = new Random(7);
            for (int i = 0; i < 260; i++)
            {
                var c = WpfMedia.Color.FromRgb((byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256));
                var r = new Wpf.Shapes.Rectangle { Width = rnd.Next(4, 40), Height = rnd.Next(4, 40), Fill = new WpfMedia.SolidColorBrush(c) };
                WpfControls.Canvas.SetLeft(r, rnd.Next(W + 60)); WpfControls.Canvas.SetTop(r, rnd.Next(H + 60));
                canvas.Children.Add(r);
            }
            for (int i = 0; i < 18; i++)
            {
                var t = new WpfControls.TextBlock { Text = "The quick brown fox jumps over the lazy dog 0123456789", FontSize = 13, Foreground = WpfMedia.Brushes.Black };
                WpfControls.Canvas.SetLeft(t, 4); WpfControls.Canvas.SetTop(t, 6 + i * 21);
                canvas.Children.Add(t);
            }
            pattern.RootVisual = canvas;
            N.SetWindowPos(pattern.Handle, new IntPtr(-1), x - 30, y - 30, W + 60, H + 60, 0x0010 | 0x0040);
            Pump(800);
        }

        using (var before = Capture(x, y, W, H)) before.Save(Path.Combine(Out, $"blur-{method}-before.png"));

        // Exactly the Desk's window: HwndSource, WS_POPUP, WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, per-pixel transparent, owned by the shell window.
        var src = new HwndSource(new HwndSourceParameters("DrawerLookProbe")
        {
            WindowStyle = unchecked((int)0x80000000),
            ExtendedWindowStyle = 0x00000080 | 0x08000000,
            UsesPerPixelTransparency = !args.Contains("nolayer"),
            PositionX = x, PositionY = y, Width = W, Height = H,
        });
        N.SetWindowLongPtr(src.Handle, -8, N.GetShellWindow());
        double k = src.CompositionTarget.TransformToDevice.M11;
        var panel = new WpfControls.Border
        {
            Width = W / k, Height = H / k,
            CornerRadius = new Wpf.CornerRadius(12 / k),
            Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0x59, 0x1E, 0x1E, 0x24)), // ~35% fill
            BorderBrush = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Wpf.Thickness(1 / k),
            Child = new WpfControls.TextBlock { Text = "drawer look probe: " + method, Foreground = WpfMedia.Brushes.White, Margin = new Wpf.Thickness(12 / k), FontSize = 14 / k },
        };
        src.RootVisual = panel;
        Log($"hwnd {src.Handle:X} exstyle {N.GetWindowLongPtr(src.Handle, -20):X} owner {N.GetWindow(src.Handle, 4):X} shell {N.GetShellWindow():X} wpfScale {k}");

        switch (method)
        {
            case "sbt":
            {
                var m = new N.MARGINS { L = -1, R = -1, T = -1, B = -1 };
                int hr1 = N.DwmExtendFrameIntoClientArea(src.Handle, ref m);
                int v = 3; // DWMSBT_TRANSIENTWINDOW
                int hr2 = N.DwmSetWindowAttribute(src.Handle, 38, ref v, 4);
                N.DwmGetWindowAttribute(src.Handle, 38, out int got, 4);
                Log($"DwmExtendFrameIntoClientArea(-1) hr 0x{hr1:X8}; DWMWA_SYSTEMBACKDROP_TYPE=3 hr 0x{hr2:X8}; read back {got}");
                break;
            }
            case "acrylic":
            case "blurbehind":
            {
                var accent = new N.ACCENT_POLICY
                {
                    AccentState = method == "acrylic" ? 4 : 3,
                    AccentFlags = Opt(args, "flags", 0), // 0 blurs; 2 (tint from GradientColor) drew a flat fill here, see VERDICT.md
                    GradientColor = Opt(args, "tint", 0x01000000), // AABBGGRR, near-clear tint: the panel brush does the tint
                };
                int size = Marshal.SizeOf<N.ACCENT_POLICY>();
                IntPtr p = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(accent, p, false);
                var data = new N.WINDOWCOMPOSITIONATTRIBDATA { Attrib = 19, Data = p, SizeOfData = size };
                bool ok = N.SetWindowCompositionAttribute(src.Handle, ref data);
                Log($"SetWindowCompositionAttribute(ACCENT {accent.AccentState}) returned {ok}, lastError {Marshal.GetLastWin32Error()}");
                Marshal.FreeHGlobal(p);
                break;
            }
        }

        if (args.Contains("rgn"))
            Log($"SetWindowRgn(round 24) {N.SetWindowRgn(src.Handle, N.CreateRoundRectRgn(0, 0, W + 1, H + 1, 24, 24), true)}");
        if (args.Contains("corner"))
        {
            int pref = 2; // DWMWCP_ROUND
            Log($"DWMWA_WINDOW_CORNER_PREFERENCE=2 hr 0x{N.DwmSetWindowAttribute(src.Handle, 33, ref pref, 4):X8}");
        }

        // Shown the way the Desk shows: no activate, sent to the bottom (above the desktop, under apps).
        N.SetWindowPos(src.Handle, new IntPtr(top ? -1 : 1), x, y, W, H, 0x0010 | 0x0040);
        Pump(1500);
        N.GetWindowRect(src.Handle, out var wr);
        Log($"window rect {wr}");
        using (var after = Capture(x, y, W, H))
        {
            after.Save(Path.Combine(Out, $"blur-{method}{Tag(args)}-after.png"));
            using var b = (Drawing.Bitmap)Drawing.Image.FromFile(Path.Combine(Out, $"blur-{method}-before.png"));
            double eb = Energy(b, 24), ea = Energy(after, 24);
            Log($"edge energy inner area: before {eb:F2} after {ea:F2} ratio {ea / Math.Max(eb, 1e-9):F3} (no-blur expectation ~{1 - 0x59 / 255.0:F2})");
        }
        src.Dispose();
        pattern?.Dispose();
    }

    static string Tag(string[] args) => string.Concat(args.Skip(4).Where(a => a != "top").Select(a => "-" + a.Replace("=", "")));

    static int Opt(string[] args, string key, int dflt) =>
        args.FirstOrDefault(a => a.StartsWith(key + "=")) is { } a ? Convert.ToInt32(a[(key.Length + 1)..], 16) : dflt;

    static Drawing.Bitmap Capture(int x, int y, int w, int h)
    {
        var bmp = new Drawing.Bitmap(w, h, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Drawing.Graphics.FromImage(bmp);
        g.CopyFromScreen(x, y, 0, 0, new Drawing.Size(w, h));
        return bmp;
    }

    /// <summary>Mean absolute luminance difference between horizontal and vertical neighbours, inset to skip the
    /// rounded edge and the label's row band; a blur collapses it far below the plain alpha scaling.</summary>
    static double Energy(Drawing.Bitmap b, int inset)
    {
        double sum = 0; long n = 0;
        double L(int px, int py) { var c = b.GetPixel(px, py); return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B; }
        for (int py = inset + 40; py < b.Height - inset - 1; py++)
            for (int px = inset; px < b.Width - inset - 1; px++)
            {
                double l = L(px, py);
                sum += Math.Abs(l - L(px + 1, py)) + Math.Abs(l - L(px, py + 1)); n++;
            }
        return sum / n;
    }

    static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); frame.Continue = false; };
        t.Start();
        Dispatcher.PushFrame(frame);
    }

    // ---------- Q2: icons ----------

    static void Icons(string tag)
    {
        var opened = DesktopIconsNative.Open();
        if (opened is not { } o) { Log("desktop view: not found"); return; }
        var view = o.View;
        IntPtr defView = o.DefView;
        IntPtr parent = N.GetParent(defView);
        IntPtr lv = N.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        Log($"DefView {defView:X} parent {parent:X} ({Cls(parent)}), shell window {N.GetShellWindow():X}, list view {lv:X} ({Cls(lv)})");
        foreach (var (name, h) in new[] { ("parent", parent), ("DefView", defView), ("listview", lv) })
        {
            if (h == IntPtr.Zero) continue;
            N.GetWindowRect(h, out var r);
            var p0 = new DesktopIconsNative.POINT();
            N.MapWindowPoints(h, IntPtr.Zero, ref p0, 1);
            Log($"  {name,-8} window rect {r}  client(0,0)->screen ({p0.X},{p0.Y})");
        }
        view.GetCurrentViewMode(out uint vm);
        view.GetSpacing(out var sp);
        view.GetDefaultSpacing(out var dsp);
        int aa = view.GetAutoArrange();
        view.ItemCount(DesktopIconsNative.SVGIO_ALLVIEW, out int count);
        Log($"view mode {vm}, spacing {sp.X}x{sp.Y}, default spacing {dsp.X}x{dsp.Y}, auto-arrange {(aa == 0 ? "on" : aa == 1 ? "off" : $"hr 0x{aa:X}")}, items {count}");

        var items = DesktopIconsNative.Items(view);

        // Independent check: UI Automation reads each icon's bounding box in screen pixels.
        var uia = new Dictionary<string, Wpf.Rect>();
        if (lv != IntPtr.Zero)
        {
            var root = AutomationElement.FromHandle(lv);
            foreach (AutomationElement e in root.FindAll(TreeScope.Children, Condition.TrueCondition))
                uia.TryAdd(e.Current.Name, e.Current.BoundingRectangle);
        }
        Log($"UIA list items: {uia.Count}");
        var p00 = new DesktopIconsNative.POINT();
        if (lv != IntPtr.Zero) N.MapWindowPoints(lv, IntPtr.Zero, ref p00, 1);
        int matched = 0, exact = 0;
        foreach (var (name, at) in items)
        {
            string cmp = "";
            if (uia.TryGetValue(name, out var r))
            {
                matched++;
                int dx = (int)r.Left - at.X, dy = (int)r.Top - at.Y;
                int mx = (int)r.Left - (at.X + p00.X), my = (int)r.Top - (at.Y + p00.Y);
                if (mx == 0 && my == 0) exact++;
                cmp = $"uia ({r.Left},{r.Top} {r.Width}x{r.Height})  uia-raw ({dx},{dy})  uia-mapped ({mx},{my})";
            }
            Log($"  {Trunc(name, 34),-34} pos ({at.X},{at.Y})  {cmp}");
        }
        Log($"matched by name {matched}/{items.Count}; exact after MapWindowPoints(listview->screen) {exact}");

        // A picture of the icon layer itself (not occluded by apps), with each reported position marked.
        if (lv != IntPtr.Zero)
        {
            N.GetClientRect(lv, out var cr);
            using var bmp = new Drawing.Bitmap(cr.Right, cr.Bottom, Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Drawing.Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                uint pwFlags = (uint)Opt(Environment.GetCommandLineArgs().Skip(1).ToArray(), "pw", 3);
                var cl = Environment.GetCommandLineArgs();
                IntPtr target = cl.Contains("pwdefview") ? defView : cl.Contains("pwprogman") ? parent : lv;
                bool ok = N.PrintWindow(target, hdc, pwFlags);
                g.ReleaseHdc(hdc);
                Log($"PrintWindow(list view) {ok}");
                using var pen = new Drawing.Pen(Drawing.Color.Red, 3);
                using var blue = new Drawing.Pen(Drawing.Color.Cyan, 1);
                foreach (var (_, at) in items) g.DrawRectangle(pen, at.X - 2, at.Y - 2, 4, 4);       // reported position
                foreach (var r in uia.Values) g.DrawRectangle(blue, (float)r.Left - p00.X, (float)r.Top - p00.Y, (float)r.Width, (float)r.Height); // UIA box
            }
            var bounds = items.Count == 0 ? new Drawing.Rectangle(0, 0, 10, 10) : Drawing.Rectangle.FromLTRB(
                Math.Max(0, items.Min(i => i.At.X) - 10), Math.Max(0, items.Min(i => i.At.Y) - 10),
                Math.Min(bmp.Width, items.Max(i => i.At.X) + sp.X + 10), Math.Min(bmp.Height, items.Max(i => i.At.Y) + sp.Y + 10));
            using var crop = bmp.Clone(bounds, bmp.PixelFormat);
            crop.Save(Path.Combine(Out, $"icons-{tag}-marked.png"));
            Log($"PrintWindow flags {Opt(Environment.GetCommandLineArgs().Skip(1).ToArray(), "pw", 3)}; marked crop {bounds} -> icons-{tag}-marked.png");
        }
        if (Environment.GetCommandLineArgs().Contains("thumb")) Thumb(parent, items, uia, sp);
        Marshal.ReleaseComObject(view);
    }

    /// <summary>A DWM thumbnail of the desktop window (icons included, apps not) shown briefly in a topmost window
    /// and captured, with each reported position (red) and each UIA box (cyan) drawn on the capture.</summary>
    static void Thumb(IntPtr desktop, List<(string Name, DesktopIconsNative.POINT At)> items, Dictionary<string, Wpf.Rect> uia, DesktopIconsNative.POINT sp)
    {
        const int W = 720, H = 820, X = 2600, Y = 200;
        var host = new HwndSource(new HwndSourceParameters("DrawerLookThumb")
        {
            WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x00000080 | 0x08000000 | 0x00000008,
            PositionX = X, PositionY = Y, Width = W, Height = H,
        });
        host.RootVisual = new WpfControls.Border { Background = WpfMedia.Brushes.Magenta };
        N.SetWindowPos(host.Handle, new IntPtr(-1), X, Y, W, H, 0x0010 | 0x0040);
        int hr = N.DwmRegisterThumbnail(host.Handle, desktop, out IntPtr thumb);
        var props = new N.DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = 0x1 | 0x2 | 0x4 | 0x8 | 0x10,
            rcDestination = new N.RECT { Left = 0, Top = 0, Right = W, Bottom = H },
            rcSource = new N.RECT { Left = 0, Top = 0, Right = W, Bottom = H },
            opacity = 255, fVisible = true, fSourceClientAreaOnly = false,
        };
        int hr2 = N.DwmUpdateThumbnailProperties(thumb, ref props);
        Pump(1000);
        using var bmp = Capture(X, Y, W, H);
        N.DwmUnregisterThumbnail(thumb);
        host.Dispose();
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            using var red = new Drawing.Pen(Drawing.Color.Red, 2);
            using var cyan = new Drawing.Pen(Drawing.Color.Cyan, 1);
            foreach (var (_, at) in items) g.DrawRectangle(red, at.X - 2, at.Y - 2, 4, 4);
            foreach (var r in uia.Values) g.DrawRectangle(cyan, (float)r.Left, (float)r.Top, (float)r.Width, (float)r.Height);
        }
        string tag = Environment.GetCommandLineArgs().Skip(2).FirstOrDefault() ?? "run";
        bmp.Save(Path.Combine(Out, $"icons-{tag}-thumb.png"));
        Log($"DwmRegisterThumbnail(Progman) hr 0x{hr:X8}, update hr 0x{hr2:X8} -> icons-{tag}-thumb.png (desktop 0,0 {W}x{H})");
    }

    static string Cls(IntPtr h) { var s = new StringBuilder(256); N.GetClassName(h, s, 256); return s.ToString(); }
    static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];
}

internal static class N
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; public override string ToString() => $"[{Left},{Top} {Right - Left}x{Bottom - Top}]"; }
    [StructLayout(LayoutKind.Sequential)] public struct MARGINS { public int L, R, T, B; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }
    [StructLayout(LayoutKind.Sequential)] public struct ACCENT_POLICY { public int AccentState, AccentFlags, GradientColor, AnimationId; }
    [StructLayout(LayoutKind.Sequential)] public struct WINDOWCOMPOSITIONATTRIBDATA { public int Attrib; public IntPtr Data; public int SizeOfData; }

    public delegate bool MonitorEnum(IntPtr m, IntPtr hdc, ref RECT r, IntPtr data);
    public delegate bool WindowEnum(IntPtr h, IntPtr data);

    [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnum cb, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfoW(IntPtr m, ref MONITORINFOEX mi);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr m, int type, out uint x, out uint y);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(DesktopIconsNative.POINT p, int flags);
    [DllImport("user32.dll")] public static extern bool EnumWindows(WindowEnum cb, IntPtr data);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);
    [DllImport("user32.dll")] public static extern int MapWindowPoints(IntPtr from, IntPtr to, ref DesktopIconsNative.POINT p, uint n);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowCompositionAttribute(IntPtr h, ref WINDOWCOMPOSITIONATTRIBDATA d);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT v, int size);
    [StructLayout(LayoutKind.Sequential)]
    public struct DWM_THUMBNAIL_PROPERTIES { public uint dwFlags; public RECT rcDestination, rcSource; public byte opacity; [MarshalAs(UnmanagedType.Bool)] public bool fVisible, fSourceClientAreaOnly; }
    [DllImport("dwmapi.dll")] public static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
    [DllImport("dwmapi.dll")] public static extern int DwmUnregisterThumbnail(IntPtr thumb);
    [DllImport("dwmapi.dll")] public static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DWM_THUMBNAIL_PROPERTIES p);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
    [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
}
