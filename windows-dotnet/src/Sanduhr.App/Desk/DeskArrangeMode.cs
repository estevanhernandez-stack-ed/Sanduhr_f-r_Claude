using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Sanduhr.App.Interop;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// One monitor's Arrange surface (spec 7.3), apart from the window that carries it so the render
/// harness can draw it off screen: a scrim over the screen, the Desk laid out from the working
/// arrangement with the same <see cref="DeskView"/> (in its arranging mode, where every placed piece
/// draws something), each placed piece outlined and named, the eight anchor points with the drop target
/// lit, and on the primary screen the <see cref="DeskArrangeBar"/>. Dragging a piece moves a ghost, lights
/// the target (<see cref="DeskArrange.Target"/>) and on release puts the piece there
/// (<see cref="DeskArrangement.Put"/> before <see cref="DeskArrange.PieceAfter"/>). Sized in the
/// screen's DIPs, so the hosting window scales it like an island.
/// </summary>
internal sealed class DeskArrangeContent : Grid, IDisposable
{
    /// <summary>How far an outline sits outside its piece, in DIPs (part of <see cref="DeskArrange.StackReach"/>).</summary>
    public const double OutlinePad = 6;

    private static readonly Brush Scrim = Frozen(new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0)));
    private static readonly Brush GhostFill = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)));

    private readonly DeskScreen _screen;
    private readonly DeskSettings _settings;
    private readonly DeskFonts _fonts;
    private readonly DeskFrame _frame;
    private readonly DeskArrangement _working;
    private readonly Canvas _adorn = new() { IsHitTestVisible = false };
    private readonly Dictionary<DeskAnchor, (Ellipse Dot, double X, double Y)> _dots = [];
    private DeskView? _view;
    private IReadOnlyDictionary<string, Rect> _rects = new Dictionary<string, Rect>();
    private IReadOnlyDictionary<string, DeskRect> _frames = new Dictionary<string, DeskRect>();
    private DeskRect _content;
    private DeskAnchor? _lit;
    private Drag? _drag;
    private Rectangle? _ghost;

    private sealed record Drag(string Widget, Size Size, Vector Grab, DeskAnchor? Own);

    public DeskArrangeContent(DeskScreen screen, DeskSettings settings, DeskFonts fonts, DeskFrame frame,
        DeskArrangement working, bool primary)
    {
        _screen = screen;
        _settings = settings;
        _fonts = fonts;
        _frame = frame;
        _working = working;
        var size = screen.SizeDips;
        Width = size.Width;
        Height = size.Height;
        Background = Scrim;
        Focusable = true;
        FocusVisualStyle = null;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Cursor = Cursors.Arrow;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        Children.Add(_adorn);
        if (primary)
        {
            Bar = new DeskArrangeBar { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Bar.DoneRequested += () => EndRequested?.Invoke(DeskArrangeEnd.Done);
            Bar.CancelRequested += () => EndRequested?.Invoke(DeskArrangeEnd.Cancel);
            Children.Add(Bar);
        }
        Relayout();
    }

    /// <summary>The bar, on the primary monitor's surface only.</summary>
    public DeskArrangeBar? Bar { get; }

    /// <summary>A piece was dropped and the working arrangement changed (or was put back where it was).</summary>
    public event Action? Dropped;

    /// <summary>Done or Cancel was asked for here (the bar, Return or Escape).</summary>
    public event Action<DeskArrangeEnd>? EndRequested;

    /// <summary>The anchor lit as the drop target while a drag runs.</summary>
    public DeskAnchor? Lit => _lit;

    /// <summary>The widget being dragged, null when none is.</summary>
    public string? Dragging => _drag?.Widget;

    /// <summary>The pieces' frames in screen DIPs (the harness reads them to place a ghost).</summary>
    public IReadOnlyDictionary<string, Rect> PieceRects => _rects;

    private static Brush Frozen(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    /// <summary>Lays the Desk out again from the working arrangement and redraws the outlines and points.</summary>
    public void Relayout()
    {
        var view = new DeskView(_screen, _settings with { Layout = _working.Layout }, _fonts)
        {
            Arranging = true,
            IsHitTestVisible = false,
        };
        view.Update(_frame, DeskInkMap.Light);
        _rects = view.PieceFrames();
        _frames = _rects.ToDictionary(kv => kv.Key, kv => new DeskRect(kv.Value.X, kv.Value.Y, kv.Value.Width, kv.Value.Height));
        _content = view.ContentRect;
        if (_view is { } old)
        {
            Children.Remove(old);
            old.Dispose();
        }
        _view = view;
        Children.Insert(0, view);
        BuildAdorners();
    }

    private void Ref(FrameworkElement el, DependencyProperty dp, string key) => el.SetResourceReference(dp, key);

    private void BuildAdorners()
    {
        _adorn.Children.Clear();
        _dots.Clear();
        _ghost = null;
        foreach (var (widget, r) in _rects)
        {
            var name = SettingsNames.DeskPieceName(widget);
            var outline = new Rectangle
            {
                Width = r.Width + OutlinePad * 2,
                Height = r.Height + OutlinePad * 2,
                RadiusX = 8,
                RadiusY = 8,
                StrokeThickness = 1.5,
                StrokeDashArray = [4, 3],
                Fill = Brushes.Transparent,
            };
            Ref(outline, Shape.StrokeProperty, "Sanduhr.Brush.Accent");
            AutomationProperties.SetName(outline, name + ", piece");
            Canvas.SetLeft(outline, r.X - OutlinePad);
            Canvas.SetTop(outline, r.Y - OutlinePad);
            _adorn.Children.Add(outline);

            var text = new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeights.SemiBold };
            Ref(text, TextBlock.ForegroundProperty, "Sanduhr.Brush.Text");
            var label = new Border
            {
                Child = text,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(7, 2, 7, 2),
                Opacity = 0.92,
            };
            Ref(label, Border.BackgroundProperty, "Sanduhr.Brush.Bg");
            AutomationProperties.SetName(label, name);
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double y = r.Y - OutlinePad - label.DesiredSize.Height - 3;
            if (y < 2) y = r.Bottom + OutlinePad + 3;
            Canvas.SetLeft(label, Math.Max(2, r.X - OutlinePad));
            Canvas.SetTop(label, y);
            _adorn.Children.Add(label);
        }
        foreach (var anchor in DeskAnchors.All)
        {
            var (x, y) = DeskGeometry.Point(anchor, _content);
            var dot = new Ellipse();
            _dots[anchor] = (dot, x, y);
            _adorn.Children.Add(dot);
            StyleDot(anchor);
        }
    }

    private void StyleDot(DeskAnchor anchor)
    {
        var (dot, x, y) = _dots[anchor];
        bool lit = _lit == anchor;
        double d = lit ? 16 : 8;
        dot.Width = dot.Height = d;
        dot.Opacity = lit ? 1 : 0.8;
        Ref(dot, Shape.FillProperty, lit ? "Sanduhr.Brush.Accent" : "Sanduhr.Brush.TextDim");
        dot.StrokeThickness = lit ? 2 : 0;
        Ref(dot, Shape.StrokeProperty, "Sanduhr.Brush.Bg");
        Canvas.SetLeft(dot, x - d / 2);
        Canvas.SetTop(dot, y - d / 2);
    }

    private void SetLit(DeskAnchor? anchor)
    {
        if (_lit == anchor) return;
        var before = _lit;
        _lit = anchor;
        if (before is { } b && _dots.ContainsKey(b)) StyleDot(b);
        if (anchor is { } a && _dots.ContainsKey(a)) StyleDot(a);
    }

    // ---- dragging ----

    private bool IsInBar(object? source)
    {
        if (Bar is null || source is not DependencyObject d) return false;
        while (d is not null)
        {
            if (ReferenceEquals(d, Bar)) return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    /// <summary>The piece under <paramref name="p"/> (its frame grown by the outline's pad); the smallest
    /// when pieces overlap.</summary>
    private string? PieceAt(Point p)
    {
        string? best = null;
        double area = double.PositiveInfinity;
        foreach (var (widget, r) in _rects)
        {
            var hit = new Rect(r.X - OutlinePad, r.Y - OutlinePad, r.Width + OutlinePad * 2, r.Height + OutlinePad * 2);
            if (!hit.Contains(p) || hit.Width * hit.Height >= area) continue;
            best = widget;
            area = hit.Width * hit.Height;
        }
        return best;
    }

    /// <summary>Starts dragging <paramref name="widget"/>, grabbed at <paramref name="at"/>: a ghost the size of the piece follows.</summary>
    public void BeginDrag(string widget, Point at)
    {
        if (!_rects.TryGetValue(widget, out var r)) return;
        CancelDrag();
        _drag = new Drag(widget, r.Size, at - r.TopLeft, _working.Placement(widget)?.Anchor);
        _ghost = new Rectangle
        {
            Width = r.Width + OutlinePad * 2,
            Height = r.Height + OutlinePad * 2,
            RadiusX = 8,
            RadiusY = 8,
            StrokeThickness = 2,
            Opacity = 0.85,
            IsHitTestVisible = false,
        };
        _ghost.Fill = GhostFill;
        Ref(_ghost, Shape.StrokeProperty, "Sanduhr.Brush.Accent");
        _adorn.Children.Add(_ghost);
        MoveDrag(at);
    }

    /// <summary>Moves the ghost to <paramref name="at"/> and lights the anchor a drop there would land on.</summary>
    public void MoveDrag(Point at)
    {
        if (_drag is not { } drag || _ghost is null) return;
        Canvas.SetLeft(_ghost, at.X - drag.Grab.X - OutlinePad);
        Canvas.SetTop(_ghost, at.Y - drag.Grab.Y - OutlinePad);
        SetLit(DeskArrange.Target(at.X, at.Y, _content, DeskArrange.StackBounds(_working, _frames), drag.Own));
    }

    /// <summary>Drops the dragged piece at <paramref name="at"/>: onto the lit anchor, before the piece
    /// whose middle is at or below the pointer.</summary>
    public void EndDrag(Point at)
    {
        if (_drag is not { } drag) return;
        var target = DeskArrange.Target(at.X, at.Y, _content, DeskArrange.StackBounds(_working, _frames), drag.Own);
        var stack = _working.Stack(target)
            .Where(p => p.Widget != drag.Widget)
            .Select(p => (p.Widget, _frames.TryGetValue(p.Widget, out var f) ? f : default))
            .ToList();
        var before = DeskArrange.PieceAfter(at.Y, stack);
        CancelDrag();
        _working.Put(drag.Widget, target, before);
        Dropped?.Invoke();
    }

    /// <summary>Drops the ghost and the lit point without moving anything.</summary>
    public void CancelDrag()
    {
        if (_ghost is not null) _adorn.Children.Remove(_ghost);
        _ghost = null;
        _drag = null;
        SetLit(null);
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.Handled || IsInBar(e.OriginalSource)) return;
        var p = e.GetPosition(this);
        if (PieceAt(p) is not { } widget) return;
        Focus();
        BeginDrag(widget, p);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        if (_drag is not null)
        {
            MoveDrag(p);
            return;
        }
        Cursor = !IsInBar(e.OriginalSource) && PieceAt(p) is not null ? Cursors.SizeAll : Cursors.Arrow;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag is null) return;
        var p = e.GetPosition(this);
        ReleaseMouseCapture();
        EndDrag(p);
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        // Capture taken away mid-drag (another window grabbed the mouse): the drag ends where it stood.
        if (_drag is not null) CancelDrag();
    }

    // ---- keys ----

    /// <summary>
    /// A key reached this surface. Escape during a drag cancels only the drag; otherwise Return and
    /// Enter ask for Done and Escape for Cancel (<see cref="DeskArrange.EndKey"/>). True when the key was used.
    /// </summary>
    public bool HandleKey(string keyName)
    {
        if (keyName == "Escape" && _drag is not null)
        {
            CancelDrag();
            return true;
        }
        if (DeskArrange.EndKey(keyName) is not { } how) return false;
        EndRequested?.Invoke(how);
        return true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        string? name = e.Key switch { Key.Return => "Return", Key.Escape => "Escape", _ => null };
        if (name is not null && HandleKey(name)) e.Handled = true;
    }

    public void Dispose()
    {
        _drag = null;
        _view?.Dispose();
        _view = null;
    }
}

/// <summary>
/// Arrange mode's windows (spec 7.3): one per monitor, at its full bounds, above every other window
/// (<c>WS_EX_TOPMOST</c>) and, unlike the Desk's islands, activatable so it takes the keys; per-pixel
/// transparent with a scrim that is never fully clear, so the whole surface takes the mouse. Created
/// per-monitor aware the way <see cref="DesktopLayerHost"/> does it. Return and Escape are caught at the
/// window (<c>WM_KEYDOWN</c>) so they end the mode wherever the focus is; ending is handed to the
/// dispatcher, never run from inside a window's own message.
/// </summary>
internal sealed class DeskArrangeMode : IDisposable
{
    private sealed record Surface(DeskScreen Screen, HwndSource Source, DeskArrangeContent Content);

    private readonly List<Surface> _surfaces = [];
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly Action<DeskArrangeEnd> _ended;
    private readonly Action _changed;
    private bool _requested;
    private bool _disposed;

    /// <param name="ended">Done or Cancel was asked for; the owner calls <see cref="Dispose"/> in answer.</param>
    /// <param name="changed">A drop changed the working arrangement.</param>
    public DeskArrangeMode(IReadOnlyList<DeskScreen> screens, DeskSettings settings, DeskFonts fonts, DeskFrame frame,
        DeskArrangement working, Action<DeskArrangeEnd> ended, Action changed)
    {
        _ended = ended;
        _changed = changed;
        bool barPlaced = false;
        // The bar goes on the primary monitor, else the first.
        var primary = screens.FirstOrDefault(s => s.Primary) ?? screens.FirstOrDefault();
        foreach (var screen in screens)
        {
            bool bar = !barPlaced && ReferenceEquals(screen, primary);
            barPlaced |= bar;
            var content = new DeskArrangeContent(screen, settings, fonts, frame, working, bar);
            content.Dropped += OnDropped;
            content.EndRequested += Request;
            var source = Create(screen, content);
            _surfaces.Add(new Surface(screen, source, content));
        }
    }

    /// <summary>The windows' handles, primary first is not promised (the runner may query them).</summary>
    public IReadOnlyList<IntPtr> Handles => [.. _surfaces.Select(s => s.Source.Handle)];

    /// <summary>Brings the primary surface to the front and gives it the keys. The foreground can be
    /// refused to a process that did not just receive input; the buttons and the tray still end the mode then.</summary>
    public void Activate()
    {
        var first = _surfaces.FirstOrDefault(s => s.Content.Bar is not null) ?? _surfaces.FirstOrDefault();
        if (first is null) return;
        DeskNative.PerMonitor(() => DeskNative.SetForegroundWindow(first.Source.Handle));
        Keyboard.Focus(first.Content);
        first.Content.Focus();
    }

    private HwndSource Create(DeskScreen screen, DeskArrangeContent content)
    {
        var b = screen.Bounds;
        var src = DeskNative.PerMonitor(() => new HwndSource(new HwndSourceParameters("SanduhrDeskArrange")
        {
            WindowStyle = DeskNative.WS_POPUP | DeskNative.WS_VISIBLE,
            ExtendedWindowStyle = DeskNative.WS_EX_TOOLWINDOW | DeskNative.WS_EX_TOPMOST,
            UsesPerPixelTransparency = true,
            PositionX = b.X,
            PositionY = b.Y,
            Width = 1,
            Height = 1,
        }));
        src.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg != DeskNative.WM_KEYDOWN) return IntPtr.Zero;
            string? name = (int)(wParam.ToInt64() & 0xFFFF) switch
            {
                DeskNative.VK_RETURN => "Return",
                DeskNative.VK_ESCAPE => "Escape",
                _ => null,
            };
            if (name is not null && content.HandleKey(name)) handled = true;
            return IntPtr.Zero;
        });
        // WPF draws in its own DPI for this window; scale the content so a DIP is the monitor's.
        double wpf = src.CompositionTarget?.TransformToDevice.M11 is > 0 and var t ? t : 1.0;
        double k = screen.Scale / wpf;
        src.RootVisual = new Border
        {
            Child = content,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            LayoutTransform = Math.Abs(k - 1) < 0.001 ? Transform.Identity : new ScaleTransform(k, k),
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
        };
        DeskNative.PerMonitor(() => DeskNative.SetWindowPos(src.Handle, DeskNative.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height,
            DeskNative.SWP_NOACTIVATE | DeskNative.SWP_SHOWWINDOW));
        return src;
    }

    private void OnDropped()
    {
        foreach (var s in _surfaces) s.Content.Relayout();
        _changed();
    }

    private void Request(DeskArrangeEnd how)
    {
        if (_requested || _disposed) return;
        _requested = true;
        _ui.BeginInvoke(() =>
        {
            if (!_disposed) _ended(how);
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var s in _surfaces)
        {
            s.Content.Dropped -= OnDropped;
            s.Content.EndRequested -= Request;
            s.Content.CancelDrag();
            if (!s.Source.IsDisposed) s.Source.RootVisual = null;
            s.Content.Dispose();
            if (!s.Source.IsDisposed) s.Source.Dispose();
        }
        _surfaces.Clear();
    }
}
