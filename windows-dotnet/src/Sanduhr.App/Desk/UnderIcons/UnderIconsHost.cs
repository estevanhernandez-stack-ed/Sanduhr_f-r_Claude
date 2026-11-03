using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sanduhr.App.Interop;
using Sanduhr.Core;

namespace Sanduhr.App.Desk.UnderIcons;

/// <summary>
/// The Desk under the desktop icons (spec 5.0d). Every island goes to a <see cref="DesktopLayerHost"/> as before; a widget island (not a
/// drawer, not the Launcher) is flagged <see cref="DeskIsland.UnderIcons"/>, so that host keeps its window cloaked and click-through, and
/// this host captures the window's content and hands it to the <see cref="InkLayer"/> below the icons. Coverage, drift, dim, Show desktop
/// and Explorer restarts stay the V3 host's; the ink layer rebuilds its own windows. Until the ink layer is ready, and for good once it
/// fails, nothing is flagged and the Desk draws above the icons exactly as V3 does.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class UnderIconsHost : IDeskHost
{
    /// <summary>The check at rest: a clock or a meter shows a change within a second, and an animation starts within one.</summary>
    private static readonly TimeSpan Rest = TimeSpan.FromSeconds(1);
    /// <summary>The check while frames keep changing (an animation runs).</summary>
    private static readonly TimeSpan Hot = TimeSpan.FromMilliseconds(33);
    /// <summary>Unchanged fast checks before going back to rest: a one-off change (a meter's tick) costs a few checks, not seconds of them.</summary>
    private const int QuietBeforeRest = 3;

    /// <summary>Why the route could not run this session, for Settings; null while it runs or has not been tried.</summary>
    public static string? Unavailable { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="Unavailable"/> changes.</summary>
    public static event Action? UnavailableChanged;

    private readonly DesktopLayerHost _above = new();
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _frames;
    private readonly IconRects _icons;
    private readonly Dictionary<int, Shot> _shots = [];
    private InkLayer? _ink;
    private bool _inkReady;
    private bool _disposed;
    private int _quiet = QuietBeforeRest;
    /// <summary>Per island: the capture bitmap and the buffer it is read into, reused while the island keeps its size.</summary>
    private readonly Dictionary<int, (RenderTargetBitmap Rtb, byte[] Scratch)> _canvas = [];
    private int _ticks, _sent;
    private double _shootMs;
    private DateTime _statsSince = DateTime.UtcNow;
    /// <summary>The islands of the last <see cref="Show"/>, in island order (the V3 host's window order).</summary>
    private List<DeskIsland> _islands = [];
    /// <summary>The island index of each entry in the snapshot the hook decides on.</summary>
    private int[] _snapIds = [];

    /// <summary>Who answers the Desk menu and clicks; the controller sets it.</summary>
    public IDeskClickActions? Actions { get; set; }

    /// <summary>What the host did and why, for debug logs. Never window text.</summary>
    public event Action<string>? Trace;

    public UnderIconsHost()
    {
        _above.ScreensChanged += () => ScreensChanged?.Invoke();
        _above.CoverageChanged += () => CoverageChanged?.Invoke();
        _frames = new DispatcherTimer(DispatcherPriority.Background) { Interval = Rest };
        _frames.Tick += (_, _) => Capture(force: false);
        _icons = new IconRects(icons => _ink?.SetIcons(icons));
        StartInk();
    }

    public event Action? ScreensChanged;
    public event Action? CoverageChanged;
    public bool IsShowing => _above.IsShowing;
    public IReadOnlyList<bool> Coverage => _above.Coverage;
    public IReadOnlyList<DeskScreen> Screens() => _above.Screens();
    public void SetDrift(int? step) => _above.SetDrift(step);
    public void SetDim(double opacity, TimeSpan ease) => _above.SetDim(opacity, ease);

    /// <summary>A widget island goes under the icons; a drawer and the Launcher (hover glow, touch) stay above them.</summary>
    internal static bool Eligible(DeskIsland island) => !island.Drawer && !island.Widgets.Contains("launcher");

    public void Show(IReadOnlyList<DeskSurface> surfaces)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool under = _inkReady && Unavailable is null;
        var mapped = surfaces.Select(s => s with
        {
            Islands = [.. s.Islands.Select(i => under && Eligible(i) ? i with { UnderIcons = true } : i)],
        }).ToList();
        _above.Show(mapped);
        _islands = [.. mapped.SelectMany(s => s.Islands)];
        if (_ink is { } ink)
        {
            foreach (var id in _shots.Keys.ToList())
                if (id >= _islands.Count || !_islands[id].UnderIcons)
                {
                    ink.Remove(id);
                    _shots.Remove(id);
                    _canvas.Remove(id);
                }
        }
        if (_islands.Any(i => i.UnderIcons))
        {
            _icons.Now();
            Capture(force: true);
            _frames.Start();
        }
        else
        {
            _frames.Stop();
            _icons.Stop();
            Publish();
        }
    }

    public void Hide()
    {
        _frames.Stop();
        _icons.Stop();
        _above.Hide();
        _ink?.Clear();
        _shots.Clear();
        _canvas.Clear();
        _islands = [];
        Publish();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Hide();
        _disposed = true;
        _icons.Dispose();
        _ink?.Dispose();
        _menuHost?.Dispose();
        _above.Dispose();
    }

    // ---- the ink layer ----

    /// <summary>Starts the ink layer off the UI thread; the Desk draws above the icons until it is ready, then redraws under them.</summary>
    private void StartInk()
    {
        if (Unavailable is not null) return;   // failed earlier this session: V3 until the app starts again
        var ink = new InkLayer((d, at) => _ui.BeginInvoke(() => OnPress(d, at)));
        ink.Trace += m => Log($"ink: {m}");
        ink.Failed += why => _ui.BeginInvoke(() => Fail(why));
        _ink = ink;
        Task.Run(() => ink.Start(TimeSpan.FromSeconds(10))).ContinueWith(t => _ui.BeginInvoke(() =>
        {
            if (_disposed) return;
            if (t.Result is { } why) { Fail(why); return; }
            _inkReady = true;
            Log("ready; the Desk moves under the icons");
            ScreensChanged?.Invoke();
        }), TaskScheduler.Default);
    }

    private void Fail(string why)
    {
        if (_disposed || Unavailable is not null) return;
        Log($"falling back above the icons: {why}");
        Unavailable = why;
        _inkReady = false;
        _ink?.Clear();
        _shots.Clear();
        _canvas.Clear();
        UnavailableChanged?.Invoke();
        ScreensChanged?.Invoke();   // the controller redraws, and nothing is flagged any more
    }

    // ---- frames ----

    /// <summary>Captures each visible ink island's window and sends a frame where its pixels or place changed.</summary>
    private void Capture(bool force)
    {
        if (_ink is not { } ink || !_inkReady) return;
        var sources = _above.Sources;
        var covered = _above.Coverage;
        bool changed = false;
        for (int i = 0; i < _islands.Count && i < sources.Count; i++)
        {
            if (!_islands[i].UnderIcons) continue;
            var src = sources[i];
            if (src.IsDisposed || src.RootVisual is not FrameworkElement root) continue;
            if (!force && i < covered.Count && covered[i]) continue;
            if (DeskNative.RectPx(src.Handle) is not { Width: > 0, Height: > 0 } rect) continue;
            try
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                if (Shoot(i, src, root, rect, force)) changed = true;
                _shootMs += System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            }
            catch (Exception e) when (e is OutOfMemoryException or InvalidOperationException or ArgumentException or COMException)
            {
                Log($"an ink frame was not captured: {e.GetType().Name}");
            }
        }
        _ticks++;
        if (changed)
        {
            _sent++;
            _quiet = 0;
            Publish();
        }
        if (DateTime.UtcNow - _statsSince >= TimeSpan.FromMinutes(10))
        {
            Log($"frames: {_ticks} checks, {_sent} sent, {_shootMs:F0} ms capturing in {(DateTime.UtcNow - _statsSince).TotalSeconds:F0} s");
            _ticks = _sent = 0;
            _shootMs = 0;
            _statsSince = DateTime.UtcNow;
        }
        else if (_quiet < QuietBeforeRest) _quiet++;
        _frames.Interval = _quiet < QuietBeforeRest ? Hot : Rest;
    }

    /// <summary>One island's frame: true when it changed and was sent.</summary>
    private bool Shoot(int id, HwndSource src, FrameworkElement root, PxRect rect, bool force)
    {
        if (root.ActualWidth <= 0 || root.ActualHeight <= 0) return false;
        if (!_canvas.TryGetValue(id, out var canvas) || canvas.Rtb.PixelWidth != rect.Width || canvas.Rtb.PixelHeight != rect.Height)
        {
            _canvas[id] = canvas = (new RenderTargetBitmap(rect.Width, rect.Height, 96, 96, PixelFormats.Pbgra32),
                new byte[rect.Width * rect.Height * 4]);
            double wpf = src.CompositionTarget?.TransformToDevice.M11 ?? 0;
            Log($"island {id}: window {rect.Width}x{rect.Height} px, root {root.ActualWidth:F0}x{root.ActualHeight:F0} DIPs, WPF scale {wpf:F2}");
        }
        // The root's own box, stretched onto exactly the window's physical pixels: right at any DPI, whatever mix of the window's DPI
        // and the Desk's own scale (the root's LayoutTransform) WPF applies. Rendering the root directly drew 1.25x too big at 125%.
        var brush = new VisualBrush(root)
        {
            Stretch = Stretch.Fill,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, root.ActualWidth, root.ActualHeight),
        };
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) dc.DrawRectangle(brush, null, new Rect(0, 0, rect.Width, rect.Height));
        canvas.Rtb.Clear();
        canvas.Rtb.Render(dv);
        canvas.Rtb.CopyPixels(canvas.Scratch, rect.Width * 4, 0);
        _shots.TryGetValue(id, out var last);
        bool moved = last is null || last.Rect != rect;
        if (!force && !moved && last!.Pixels.AsSpan().SequenceEqual(canvas.Scratch)) return false;
        // A frame that is sent gets its own array: the ink thread reads it after this returns, and the scratch is drawn over next check.
        var px = canvas.Scratch.ToArray();
        if (moved) _ink!.Place(id, rect);
        _ink!.Frame(id, rect.Width, rect.Height, px);
        var alpha = new byte[rect.Width * rect.Height];
        for (int i = 0, j = 3; i < alpha.Length; i++, j += 4) alpha[i] = px[j];
        _shots[id] = new Shot(rect, px, alpha, Regions(_islands[id], root, rect));
        return true;
    }

    /// <summary>The island's clickable pieces in screen pixels: the content's click areas, through the window's root to its pixels.</summary>
    private static IReadOnlyList<UnderIconsRegion> Regions(DeskIsland island, FrameworkElement root, PxRect rect)
    {
        if (DeskClicks.TargetOf(island.Content) is not { } target || root.ActualWidth <= 0 || root.ActualHeight <= 0) return [];
        double sx = rect.Width / root.ActualWidth, sy = rect.Height / root.ActualHeight;
        var toRoot = island.Content.TransformToAncestor(root);
        var regions = new List<UnderIconsRegion>();
        foreach (var (kind, area) in target.Areas())
        {
            var r = toRoot.TransformBounds(area);
            regions.Add(new UnderIconsRegion(kind, new PxRect(rect.X + (int)Math.Floor(r.X * sx), rect.Y + (int)Math.Floor(r.Y * sy),
                (int)Math.Ceiling(r.Width * sx), (int)Math.Ceiling(r.Height * sy))));
        }
        return regions;
    }

    /// <summary>Hands the hook a fresh snapshot of every ink island as last drawn.</summary>
    private void Publish()
    {
        var ids = _shots.Keys.Order().ToArray();
        _ink?.SetIslands([.. ids.Select(id => new UnderIconsIsland(_shots[id].Rect, _shots[id].Alpha, _shots[id].Regions))]);
        _snapIds = ids;
    }

    // ---- presses ----

    private void OnPress(UnderIconsDecision d, PxPoint at)
    {
        if (_disposed || d.Island < 0 || d.Island >= _snapIds.Length) return;
        int id = _snapIds[d.Island];
        if (id >= _islands.Count) return;
        var island = _islands[id];
        var target = DeskClicks.TargetOf(island.Content);
        if (d.Act == UnderIconsAct.Click)
        {
            if (d.Kind is { } kind) target?.Click(kind);
            return;
        }
        if (Actions is not { } actions) return;
        // The press that opens the menu was taken from Explorer, so Sanduhr is not the foreground app: a menu opened as it is closes at
        // once, or never shows. Take the foreground first, on a 1x1 invisible window at the press, and open the menu from it (the
        // pattern tray menus use; the spike's second fix).
        // A right press on ink while a Desk menu is open: that menu closes and this one opens where the press landed.
        if (_menu is { IsOpen: true } open) open.IsOpen = false;
        var host = MenuHost(at);
        bool foreground = InkNative.SetForegroundWindow(host.Handle);
        var menu = DeskClicks.OpenDeskMenu((FrameworkElement)host.RootVisual, d.Kind, actions);
        _menu = menu;
        _ink!.MenuOpen = menu.IsOpen;
        menu.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_menu, menu)) return;   // replaced by a newer menu, which owns the flag and the host now
            if (_ink is { } ink) ink.MenuOpen = false;
            if (!host.IsDisposed) InkNative.ShowWindow(host.Handle, 0);
        };
        // Never leave the hook passing presses for a menu that is not up.
        _ui.BeginInvoke(DispatcherPriority.Background, () => { if (!menu.IsOpen && _ink is { } ink) ink.MenuOpen = false; });
        Log($"Desk menu at ({at.X},{at.Y}){(d.Kind is { } piece ? $" on {piece}" : "")}{(foreground ? "" : ", foreground refused")}{(menu.IsOpen ? "" : ", not open")}");
    }

    private HwndSource? _menuHost;
    private System.Windows.Controls.ContextMenu? _menu;

    /// <summary>The menu's owner: a 1x1 fully transparent tool window, moved to the press and shown, so it can take the foreground.</summary>
    private HwndSource MenuHost(PxPoint at)
    {
        if (_menuHost is null || _menuHost.IsDisposed)
        {
            _menuHost = DeskNative.PerMonitor(() => new HwndSource(new HwndSourceParameters("SanduhrDeskMenu")
            {
                WindowStyle = DeskNative.WS_POPUP,
                ExtendedWindowStyle = DeskNative.WS_EX_TOOLWINDOW,
                UsesPerPixelTransparency = true,
                PositionX = at.X,
                PositionY = at.Y,
                Width = 1,
                Height = 1,
            }));
            _menuHost.RootVisual = new System.Windows.Controls.Border { Width = 1, Height = 1, Background = Brushes.Transparent };
        }
        var h = _menuHost.Handle;
        DeskNative.PerMonitor(() => InkNative.SetWindowPos(h, IntPtr.Zero, at.X, at.Y, 1, 1, InkNative.SWP_NOZORDER | InkNative.SWP_NOACTIVATE));
        InkNative.ShowWindow(h, 5);
        return _menuHost;
    }

    private void Log(string message) => Trace?.Invoke(message);

    private sealed record Shot(PxRect Rect, byte[] Pixels, byte[] Alpha, IReadOnlyList<UnderIconsRegion> Regions);
}
