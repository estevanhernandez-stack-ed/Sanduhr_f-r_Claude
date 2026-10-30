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
    private readonly IReadOnlyCollection<string> _connected;
    /// <summary>More than one monitor: each piece carries the Show on control.</summary>
    private readonly bool _showOn;
    private readonly Canvas _adorn = new() { IsHitTestVisible = false };
    /// <summary>The Hide buttons: the one part of the outlines that takes clicks and the keyboard.</summary>
    private readonly Canvas _controls = new();
    /// <summary>The Hidden strip (primary surface only): the pieces the layout leaves out, each with a Show button.</summary>
    private readonly Border _strip = new() { Visibility = Visibility.Collapsed };
    private readonly Dictionary<string, Button> _buttons = [];
    private readonly Dictionary<DeskAnchor, (Ellipse Dot, double X, double Y)> _dots = [];
    private DeskView? _view;
    private IReadOnlyDictionary<string, Rect> _rects = new Dictionary<string, Rect>();
    private IReadOnlyDictionary<string, DeskRect> _frames = new Dictionary<string, DeskRect>();
    private DeskRect _content;
    private DeskAnchor? _lit;
    /// <summary>The drag under way: held by the Core gesture, so the capture loss its own drop reports
    /// can't cancel it (spec 7.3; the S7 live run's r2).</summary>
    private readonly DeskArrangeGesture<Drag> _gesture = new();
    private Drag? _drag => _gesture.Current;
    private Shape? _ghost;
    /// <summary>Each piece's outline as drawn (its stack's slant included, #314): what a press hits and what the outline traces.</summary>
    private IReadOnlyDictionary<string, DeskQuad> _quads = new Dictionary<string, DeskQuad>();

    private sealed record Drag(string Widget, Size Size, Vector Grab, DeskAnchor? Own);

    public DeskArrangeContent(DeskScreen screen, DeskSettings settings, DeskFonts fonts, DeskFrame frame,
        DeskArrangement working, bool primary, IReadOnlyCollection<string>? connectedIds = null, bool showOn = false)
    {
        _connected = connectedIds ?? [];
        _showOn = showOn;
        _screen = screen;
        _settings = settings;
        _fonts = fonts;
        _frame = frame;
        _working = working;
        var size = screen.SizeDips;
        Width = size.Width;
        Height = size.Height;
        // The scrim covers the work area only: over the taskbar the surface stays fully clear, so its pixels
        // pass clicks through and the tray's "Finish arranging" stays in reach.
        Children.Add(new System.Windows.Shapes.Rectangle
        {
            Fill = Scrim,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness((screen.Work.X - screen.Bounds.X) / screen.Scale, (screen.Work.Y - screen.Bounds.Y) / screen.Scale, 0, 0),
            Width = screen.Work.Width / screen.Scale,
            Height = screen.Work.Height / screen.Scale,
        });
        Focusable = true;
        FocusVisualStyle = null;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Cursor = Cursors.Arrow;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        Children.Add(_adorn);
        Children.Add(_controls);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        if (primary)
        {
            _strip.HorizontalAlignment = HorizontalAlignment.Center;
            _strip.VerticalAlignment = VerticalAlignment.Bottom;
            // Above the taskbar's room, so it sits inside the work area the scrim covers.
            var below = (screen.Bounds.Y + screen.Bounds.Height - (screen.Work.Y + screen.Work.Height)) / screen.Scale;
            _strip.Margin = new Thickness(0, 0, 0, Math.Max(0, below) + 24);
            Children.Add(_strip);
            Bar = new DeskArrangeBar { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Bar.DoneRequested += () => { if (CanFinish) EndRequested?.Invoke(DeskArrangeEnd.Done); };
            Bar.CancelRequested += () => EndRequested?.Invoke(DeskArrangeEnd.Cancel);
            Children.Add(Bar);
        }
        Relayout();
    }

    /// <summary>The bar, on the primary monitor's surface only.</summary>
    public DeskArrangeBar? Bar { get; }

    /// <summary>A piece was dropped and the working arrangement changed (or was put back where it was).</summary>
    public event Action? Dropped;

    /// <summary>A Hide or Show button changed the working arrangement; the argument is the key of the control
    /// that should take the keyboard next (<see cref="HideKey"/> or <see cref="ShowKey"/>).</summary>
    public event Action<string>? Toggled;

    public static string ShowOnKey(string widget) => "showon:" + widget;
    public static string HideKey(string widget) => "hide:" + widget;
    public static string ShowKey(string widget) => "show:" + widget;

    /// <summary>The Hide and Show buttons by key (the live check clicks them).</summary>
    internal IReadOnlyDictionary<string, Button> Buttons => _buttons;

    /// <summary>Puts the keyboard on the control with <paramref name="key"/> when this surface has it.</summary>
    public bool TryFocus(string key)
    {
        if (!_buttons.TryGetValue(key, out var b)) return false;
        b.UpdateLayout();
        return b.Focus();
    }

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
        // Only the pieces this monitor draws: those on every monitor and those placed on it.
        var view = new DeskView(_screen, _settings with { Layout = _working.ForScreen(_screen.Ref, _connected).Layout }, _fonts)
        {
            Arranging = true,
            IsHitTestVisible = false,
        };
        view.Update(_frame, DeskInkMap.Light);
        _rects = view.PieceFrames();
        _quads = view.PieceQuads();
        _frames = _rects.ToDictionary(kv => kv.Key, kv => new DeskRect(kv.Value.X, kv.Value.Y, kv.Value.Width, kv.Value.Height));
        _content = view.ContentRect;
        if (_view is { } old)
        {
            Children.Remove(old);
            old.Dispose();
        }
        _view = view;
        Children.Insert(1, view); // above the scrim (child 0), below the outlines and the bar
        BuildAdorners();
        BuildControls();
        Bar?.ShowNothingPlaced(!CanFinish);
    }

    /// <summary>False while every drawn piece is hidden: Done and Return wait, Cancel works.</summary>
    private bool CanFinish => !_working.PlacesNothing(DeskView.DrawnFor(_working));

    /// <summary>The pieces the Desk draws that the working layout leaves out, in Settings order.</summary>
    private IReadOnlyList<string> HiddenPieces => _working.Hidden(DeskView.DrawnFor(_working));

    private Button ChromeButton(string text, string accessibleName)
    {
        var button = new Button
        {
            Content = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(7, 2, 7, 2),
            Cursor = Cursors.Hand,
            Focusable = true,
            Template = ChromeTemplate,
        };
        Ref(button, Control.ForegroundProperty, "Sanduhr.Brush.Text");
        Ref(button, Control.BackgroundProperty, "Sanduhr.Brush.Bg");
        Ref(button, Control.BorderBrushProperty, "Sanduhr.Brush.Border");
        AutomationProperties.SetName(button, accessibleName);   // streamer: notname - accessible name of an arrange-bar button, fixed copy
        ToolTipService.SetToolTip(button, accessibleName);
        return button;
    }

    /// <summary>A rounded chip with the bar's keyboard-focus ring (a 2 px border).</summary>
    private static readonly ControlTemplate ChromeTemplate = BuildChromeTemplate();

    private static ControlTemplate BuildChromeTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "bg");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.85, "bg"));
        var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "bg"));
        template.Triggers.Add(hover);
        template.Triggers.Add(focused);
        template.Seal();
        return template;
    }

    /// <summary>
    /// Each piece's Hide button beside its name tag, and on the primary surface the Hidden strip. A press edits the
    /// working arrangement only: Done writes it once, Cancel drops it (<see cref="DeskArrangeSession"/>).
    /// </summary>
    private void BuildControls()
    {
        _controls.Children.Clear();
        _buttons.Clear();
        foreach (var (widget, r) in _rects)
        {
            var name = DeskView.PieceName(widget);   // streamer: masked - DeskView.PieceName gives a drawer's codename while streamer mode is on
            var hide = ChromeButton("Hide", DeskArrangeCopy.HideLabel(name));
            var w = widget;
            hide.Click += (_, _) => Toggle(w, hide: true);
            hide.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            // Beside the name tag, which BuildAdorners sat above the outline (or below it when there is no room above).
            var tag = new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeights.SemiBold };
            tag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double tagWidth = tag.DesiredSize.Width + 14, tagHeight = tag.DesiredSize.Height + 4;
            double y = TagTop(widget, r) - OutlinePad - tagHeight - 3;
            if (y < 2) y = r.Bottom + OutlinePad + 3;
            double hideLeft = Math.Max(2, r.X - OutlinePad) + tagWidth + 4;
            Canvas.SetLeft(hide, hideLeft);
            Canvas.SetTop(hide, y + (tagHeight - hide.DesiredSize.Height) / 2);
            _controls.Children.Add(hide);
            _buttons[HideKey(widget)] = hide;
            if (_showOn && _working.Placement(widget) is { } placed)
            {
                // Show on: every monitor or just this one (Desk pieces per monitor).
                bool every = DeskMonitors.FromWord(placed.Monitor) is null;
                var showOn = ChromeButton(DeskArrangeCopy.ShowOnText(every), DeskArrangeCopy.ShowOnLabel(name, every));
                showOn.Click += (_, _) =>
                {
                    var now = _working.Placement(w)?.Monitor;
                    _working.SetMonitor(w, DeskMonitors.ToggleShowOn(now, _screen.Ref));
                    Toggled?.Invoke(ShowOnKey(w));
                };
                showOn.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(showOn, hideLeft + hide.DesiredSize.Width + 4);
                Canvas.SetTop(showOn, y + (tagHeight - showOn.DesiredSize.Height) / 2);
                _controls.Children.Add(showOn);
                _buttons[ShowOnKey(widget)] = showOn;
            }
        }
        BuildStrip();
    }

    private void BuildStrip()
    {
        var hidden = Bar is null ? [] : HiddenPieces;
        if (hidden.Count == 0)
        {
            _strip.Visibility = Visibility.Collapsed;
            _strip.Child = null;
            return;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var title = new TextBlock
        {
            Text = DeskArrangeCopy.HiddenStrip,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Ref(title, TextBlock.ForegroundProperty, "Sanduhr.Brush.Text");
        row.Children.Add(title);
        foreach (var widget in hidden)
        {
            var name = DeskView.PieceName(widget);   // streamer: masked - DeskView.PieceName gives a drawer's codename while streamer mode is on
            var label = new TextBlock { Text = name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            Ref(label, TextBlock.ForegroundProperty, "Sanduhr.Brush.TextSecondary");
            var show = ChromeButton("Show", DeskArrangeCopy.ShowLabel(name));
            var w = widget;
            show.Click += (_, _) => Toggle(w, hide: false);
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
            item.Children.Add(label);
            item.Children.Add(show);
            row.Children.Add(item);
            _buttons[ShowKey(widget)] = show;
        }
        _strip.Child = row;
        _strip.CornerRadius = new CornerRadius(8);
        _strip.BorderThickness = new Thickness(1);
        _strip.Padding = new Thickness(12, 6, 0, 6);
        Ref(_strip, Border.BackgroundProperty, "Sanduhr.Brush.Bg");
        Ref(_strip, Border.BorderBrushProperty, "Sanduhr.Brush.Border");
        AutomationProperties.SetName(_strip, DeskArrangeCopy.HiddenStrip);
        _strip.Visibility = Visibility.Visible;
    }

    private void Toggle(string widget, bool hide)
    {
        if (hide) _working.Hide(widget);
        else _working.Show(widget);
        Toggled?.Invoke(hide ? ShowKey(widget) : HideKey(widget));
    }

    private void Ref(FrameworkElement el, DependencyProperty dp, string key) => el.SetResourceReference(dp, key);

    private void BuildAdorners()
    {
        _adorn.Children.Clear();
        _dots.Clear();
        _ghost = null;
        foreach (var (widget, r) in _rects)
        {
            var name = DeskView.PieceName(widget);   // streamer: masked - DeskView.PieceName gives a drawer's codename while streamer mode is on
            // A slanted piece is outlined along its slant; an upright one keeps the rounded rectangle.
            var outline = OutlineShape(widget, r);
            outline.StrokeThickness = 1.5;
            outline.StrokeDashArray = [4, 3];
            outline.Fill = Brushes.Transparent;
            Ref(outline, Shape.StrokeProperty, "Sanduhr.Brush.Accent");
            AutomationProperties.SetName(outline, name + ", piece");   // streamer: notname - name from DeskView.PieceName: a piece type name, or a drawer's codename while streamer mode is on
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
            AutomationProperties.SetName(label, name);   // streamer: notname - name from DeskView.PieceName: a piece type name, or a drawer's codename while streamer mode is on
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double y = TagTop(widget, r) - OutlinePad - label.DesiredSize.Height - 3;
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
        if (source is not DependencyObject d) return false;
        while (d is not null)
        {
            // The bar, the Hide buttons and the Hidden strip take their own clicks: no drag starts there.
            if (ReferenceEquals(d, Bar) || ReferenceEquals(d, _controls) || ReferenceEquals(d, _strip)) return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    /// <summary>The piece under <paramref name="p"/> (its outline as drawn, slant included, grown by the outline's pad); the smallest
    /// when pieces overlap.</summary>
    internal string? PieceAt(Point p)
    {
        string? best = null;
        double area = double.PositiveInfinity;
        foreach (var (widget, r) in _rects)
        {
            var quad = DeskFold.Grow(_quads.TryGetValue(widget, out var q) ? q : DeskQuad.Of(new DeskRect(r.X, r.Y, r.Width, r.Height)), OutlinePad);
            double a = DeskFold.Area(quad);
            if (!DeskFold.Contains(quad, p.X, p.Y) || a >= area) continue;
            best = widget;
            area = a;
        }
        return best;
    }

    /// <summary>Where a piece's name tag hangs from: its top left corner as drawn, so a slanted piece's tag sits on its own corner
    /// rather than at the top of the box around the slant.</summary>
    private double TagTop(string widget, Rect r) => _quads.TryGetValue(widget, out var q) ? q.TopLeft.Y : r.Y;

    /// <summary>The outline of a piece whose frame is <paramref name="r"/>, grown by the pad and placed at the grown frame's top left:
    /// a rounded rectangle for an upright piece, the slanted quad for a piece in a slanted stack.</summary>
    private Shape OutlineShape(string widget, Rect r)
    {
        if (_quads.TryGetValue(widget, out var q) && !q.IsUpright)
        {
            var g = DeskFold.Grow(q, OutlinePad);
            double ox = r.X - OutlinePad, oy = r.Y - OutlinePad;
            var poly = new Polygon { StrokeLineJoin = PenLineJoin.Round };
            foreach (var c in g.Corners) poly.Points.Add(new Point(c.X - ox, c.Y - oy));
            return poly;
        }
        return new Rectangle { Width = r.Width + OutlinePad * 2, Height = r.Height + OutlinePad * 2, RadiusX = 8, RadiusY = 8 };
    }

    /// <summary>Starts dragging <paramref name="widget"/>, grabbed at <paramref name="at"/>: a ghost the size of the piece follows.</summary>
    public void BeginDrag(string widget, Point at)
    {
        if (!_rects.TryGetValue(widget, out var r)) return;
        CancelDrag();
        _gesture.Down(new Drag(widget, r.Size, at - r.TopLeft, _working.Placement(widget)?.Anchor));
        // The ghost is the piece's outline as drawn: slanted with its stack (#314).
        _ghost = OutlineShape(widget, r);
        _ghost.StrokeThickness = 2;
        _ghost.Opacity = 0.85;
        _ghost.IsHitTestVisible = false;
        _ghost.Fill = GhostFill;
        Ref(_ghost, Shape.StrokeProperty, "Sanduhr.Brush.Accent");
        _adorn.Children.Add(_ghost);
        MoveDrag(at);
    }

    /// <summary>Finds the other monitor's surface under a point of this one (past this screen's edge, with the mouse still captured here):
    /// that surface and the point in its own DIPs. Null while the point is on this screen. The mode sets it.</summary>
    public Func<DeskArrangeContent, Point, (DeskArrangeContent Target, Point Local)?>? Locate { get; set; }

    /// <summary>A drag from another monitor hovers here: a ghost and the anchor a drop would land on light up.</summary>
    public void Hover(Size size, Vector grab, Point local)
    {
        if (_ghost is null)
        {
            _ghost = new Rectangle
            {
                Width = size.Width + OutlinePad * 2,
                Height = size.Height + OutlinePad * 2,
                RadiusX = 8,
                RadiusY = 8,
                StrokeThickness = 2,
                Opacity = 0.85,
                IsHitTestVisible = false,
                Fill = GhostFill,
            };
            Ref(_ghost, Shape.StrokeProperty, "Sanduhr.Brush.Accent");
            _adorn.Children.Add(_ghost);
        }
        Canvas.SetLeft(_ghost, local.X - grab.X - OutlinePad);
        Canvas.SetTop(_ghost, local.Y - grab.Y - OutlinePad);
        SetLit(DeskArrange.Target(local.X, local.Y, _content, DeskArrange.StackBounds(_working, _frames), null));
    }

    /// <summary>The hover from another monitor ended (the drag moved on, dropped or was cancelled).</summary>
    public void ClearHover()
    {
        if (_drag is not null) return;   // a drag of this surface's own owns the ghost
        if (_ghost is not null) _adorn.Children.Remove(_ghost);
        _ghost = null;
        SetLit(null);
    }

    /// <summary>Moves the ghost to <paramref name="at"/> and lights the anchor a drop there would land on. Past this monitor's edge
    /// the other monitor's surface lights its anchor instead.</summary>
    public void MoveDrag(Point at)
    {
        if (_drag is not { } drag || _ghost is null) return;
        Canvas.SetLeft(_ghost, at.X - drag.Grab.X - OutlinePad);
        Canvas.SetTop(_ghost, at.Y - drag.Grab.Y - OutlinePad);
        var route = Locate?.Invoke(this, at);
        if (route is { } r)
        {
            SetLit(null);
            r.Target.Hover(drag.Size, drag.Grab, r.Local);
            return;
        }
        CancelHoverElsewhere?.Invoke(this);
        SetLit(DeskArrange.Target(at.X, at.Y, _content, DeskArrange.StackBounds(_working, _frames), drag.Own));
    }

    /// <summary>Drops the dragged piece at <paramref name="at"/>: onto the lit anchor, before the piece
    /// whose middle is at or below the pointer.</summary>
    public void EndDrag(Point at)
    {
        // Take the drag first, then release the capture: the loss that release reports finds no drag.
        if (_gesture.Up(() => { if (IsMouseCaptured) ReleaseMouseCapture(); }) is not { } drag) return;
        // Released over another monitor: the piece lands there, and a piece on one monitor moves to it.
        if (Locate?.Invoke(this, at) is { } route)
        {
            CancelDrag();
            route.Target.DropFrom(drag.Widget, route.Local);
            return;
        }
        var target = DeskArrange.Target(at.X, at.Y, _content, DeskArrange.StackBounds(_working, _frames), drag.Own);
        var before = BeforeFor(drag.Widget, target, at.Y);
        CancelDrag();
        _working.Put(drag.Widget, target, before);
        Dropped?.Invoke();
    }

    /// <summary>The piece a drop at height <paramref name="y"/> in <paramref name="anchor"/>'s stack goes in front of, among the pieces drawn here.</summary>
    private string? BeforeFor(string widget, DeskAnchor anchor, double y)
    {
        var stack = _working.Stack(anchor)
            .Where(p => p.Widget != widget)
            .Select(p => (p.Widget, _frames.TryGetValue(p.Widget, out var f) ? f : default))
            .ToList();
        return DeskArrange.PieceAfter(y, stack);
    }

    /// <summary>A piece dragged here from another monitor was dropped at <paramref name="at"/> (this surface's DIPs): it takes the anchor
    /// there, and a piece that was on one monitor is now on this one (a piece on every monitor stays on every monitor).</summary>
    public void DropFrom(string widget, Point at)
    {
        ClearHover();
        var own = _frames.ContainsKey(widget) ? _working.Placement(widget)?.Anchor : null;
        var target = DeskArrange.Target(at.X, at.Y, _content, DeskArrange.StackBounds(_working, _frames), own);
        var before = BeforeFor(widget, target, at.Y);
        if (_working.Placement(widget) is { } placed && DeskMonitors.FromWord(placed.Monitor) is not null)
            _working.SetMonitor(widget, DeskMonitors.ThisScreen(_screen.Ref));
        _working.Put(widget, target, before);
        Dropped?.Invoke();
    }

    /// <summary>Drops the ghost and the lit point without moving anything.</summary>
    public void CancelDrag()
    {
        if (_ghost is not null) _adorn.Children.Remove(_ghost);
        _ghost = null;
        _gesture.CaptureLost();
        SetLit(null);
        if (IsMouseCaptured) ReleaseMouseCapture();
        CancelHoverElsewhere?.Invoke(this);
    }

    /// <summary>Told when this surface's drag ends or moves back, so the other surfaces drop the hover it lit. The mode sets it.</summary>
    public Action<DeskArrangeContent>? CancelHoverElsewhere { get; set; }

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
        EndDrag(e.GetPosition(this));
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
        if (how == DeskArrangeEnd.Done && !CanFinish) return true; // used, but Done waits
        EndRequested?.Invoke(how);
        return true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        string? name = e.Key switch { Key.Return => "Return", Key.Escape => "Escape", _ => null };
        if (name is not null && HandleKey(name)) e.Handled = true;
    }

    /// <summary>
    /// The arrow keys (spec, Keyboard and screen reader). Tab picks a piece: its Hide or Show on button holds the keyboard. Then a
    /// plain arrow moves it to the next place that way and Shift+Up or Shift+Down moves it up or down its stack. Seen before the
    /// buttons see them, so the arrows never move focus instead.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        RouteArrow(e, Keyboard.Modifiers);
    }

    /// <summary>
    /// A key press, with the modifiers passed in (the tests cannot press real keys). Arrows act with no modifier or Shift alone;
    /// Ctrl, Alt and Win pass through. Marks <paramref name="e"/> handled, and returns true, only when the arrow changed the layout.
    /// </summary>
    internal bool RouteArrow(KeyEventArgs e, ModifierKeys modifiers)
    {
        if (e.Handled || _drag is not null || (modifiers & ~ModifierKeys.Shift) != 0) return false;
        var key = e.Key.ToString();
        bool shift = modifiers.HasFlag(ModifierKeys.Shift);
        if (DeskArrange.ArrowKey(key, shift) is null || PieceWithFocus() is not { } widget) return false;
        if (!HandleArrow(widget, key, shift)) return false;
        e.Handled = true;
        return true;
    }

    /// <summary>The piece whose Hide or Show on button holds the keyboard, null when none does.</summary>
    private string? PieceWithFocus()
    {
        foreach (var (key, button) in _buttons)
            if (button.IsKeyboardFocused && (key.StartsWith("hide:", StringComparison.Ordinal) || key.StartsWith("showon:", StringComparison.Ordinal)))
                return key[(key.IndexOf(':') + 1)..];
        return null;
    }

    /// <summary>
    /// An arrow on <paramref name="widget"/>: to the next place that way (<see cref="DeskArrange.Step"/>, joining the bottom of that
    /// stack), or up or down its own stack. True when the working layout changed; then every surface lays out again and the
    /// keyboard stays on the piece's Hide button (<see cref="Toggled"/>).
    /// </summary>
    public bool HandleArrow(string widget, string keyName, bool shift)
    {
        if (_drag is not null || DeskArrange.ArrowKey(keyName, shift) is not { } arrow || _working.Placement(widget) is not { } piece) return false;
        var before = _working.Layout;
        if (arrow.Kind == DeskArrangeArrowKind.Order)
            _working.Move(widget, arrow.Dy);
        else if (DeskArrange.Step(piece.Anchor, arrow.Dx, arrow.Dy) is { } to)
            _working.Put(widget, to, before: null);
        if (_working.Layout == before) return false;
        Toggled?.Invoke(HideKey(widget));
        return true;
    }

    public void Dispose()
    {
        _gesture.CaptureLost();
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
        DeskArrangement working, IReadOnlyCollection<string> connectedIds, Action<DeskArrangeEnd> ended, Action changed)
    {
        _ended = ended;
        _changed = changed;
        // The bar goes on the monitor that draws the most of the pieces, the primary on a tie (#319: it opened on the primary
        // while the pieces were on another monitor).
        int barAt = DeskMonitors.BarScreen([.. screens.Select(s => s.Ref)], working.Pieces, connectedIds);
        for (int index = 0; index < screens.Count; index++)
        {
            var screen = screens[index];
            bool bar = index == barAt;
            var content = new DeskArrangeContent(screen, settings, fonts, frame, working, bar, connectedIds, showOn: screens.Count > 1);
            content.Locate = Locate;
            content.CancelHoverElsewhere = from =>
            {
                foreach (var s in _surfaces) if (!ReferenceEquals(s.Content, from)) s.Content.ClearHover();
            };
            content.Dropped += OnDropped;
            content.Toggled += OnToggled;
            content.EndRequested += Request;
            var source = Create(screen, content);
            _surfaces.Add(new Surface(screen, source, content));
        }
    }

    /// <summary>The surface under a point that is past <paramref name="from"/>'s screen: the point is in <paramref name="from"/>'s DIPs, is turned
    /// into physical pixels on the virtual screen, and the screen that contains it (not <paramref name="from"/>'s own) answers with the point in its DIPs.</summary>
    private (DeskArrangeContent Target, Point Local)? Locate(DeskArrangeContent from, Point p)
    {
        var source = _surfaces.FirstOrDefault(s => ReferenceEquals(s.Content, from));
        if (source is null) return null;
        var (px, py) = DeskScreenMath.ToPixels(source.Screen.Bounds, source.Screen.Scale, p.X, p.Y);
        if (DeskScreenMath.Contains(source.Screen.Bounds, px, py)) return null;
        foreach (var s in _surfaces)
        {
            if (ReferenceEquals(s, source) || !DeskScreenMath.Contains(s.Screen.Bounds, px, py)) continue;
            var (lx, ly) = DeskScreenMath.ToDips(s.Screen.Bounds, s.Screen.Scale, px, py);
            return (s.Content, new Point(lx, ly));
        }
        return null;
    }

    /// <summary>The windows' handles, primary first is not promised (the runner may query them).</summary>
    public IReadOnlyList<IntPtr> Handles => [.. _surfaces.Select(s => s.Source.Handle)];

    /// <summary>Each surface's window, screen and content (the live check reads the pieces' frames).</summary>
    internal IReadOnlyList<(IntPtr Hwnd, DeskScreen Screen, DeskArrangeContent Content)> Surfaces =>
        [.. _surfaces.Select(s => (s.Source.Handle, s.Screen, s.Content))];

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

    private const int WM_CLOSE = 0x0010;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_CLOSE = 0xF060;

    /// <summary>
    /// The surface's rectangle: the monitor less the taskbar's room (reserved or auto-hide). Explorer drops
    /// the taskbar below a foreground window that covers the whole monitor, so stopping short of it keeps the
    /// tray's "Finish arranging" in reach, auto-hide included.
    /// </summary>
    private static PxRect SurfaceRect(DeskScreen screen)
    {
        var b = screen.Bounds;
        var t = screen.Taskbar;
        int l = (int)Math.Ceiling(t.Left * screen.Scale), tp = (int)Math.Ceiling(t.Top * screen.Scale);
        int r = (int)Math.Ceiling(t.Right * screen.Scale), bt = (int)Math.Ceiling(t.Bottom * screen.Scale);
        return new PxRect(b.X + l, b.Y + tp, Math.Max(1, b.Width - l - r), Math.Max(1, b.Height - tp - bt));
    }

    private HwndSource Create(DeskScreen screen, DeskArrangeContent content)
    {
        var b = SurfaceRect(screen);
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
            // Closed from outside (Alt+F4, a close sent by another program): end the edit as Cancel
            // instead of losing a surface while the session runs on with the islands hidden.
            if (msg == WM_CLOSE || (msg == WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == SC_CLOSE))
            {
                handled = true;
                Request(DeskArrangeEnd.Cancel);
                return IntPtr.Zero;
            }
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
        // The content is the whole screen in DIPs; the window starts past the taskbar, so shift it back.
        content.Margin = new Thickness(-(b.X - screen.Bounds.X) / screen.Scale, -(b.Y - screen.Bounds.Y) / screen.Scale, 0, 0);
        src.RootVisual = new Border
        {
            Child = content,
            ClipToBounds = true,
            Width = b.Width / screen.Scale,
            Height = b.Height / screen.Scale,
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

    /// <summary>A piece was hidden or shown: every surface lays out again, and the keyboard lands on the control that
    /// undoes the edit (the Show button of a piece just hidden, the Hide button of one just shown).</summary>
    private void OnToggled(string focusKey)
    {
        OnDropped();
        foreach (var s in _surfaces)
            if (s.Content.TryFocus(focusKey)) return;
        Activate(); // the control is gone (nothing is left to undo): never leave the keys nowhere
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
            s.Content.Toggled -= OnToggled;
            s.Content.EndRequested -= Request;
            s.Content.CancelDrag();
            if (!s.Source.IsDisposed) s.Source.RootVisual = null;
            s.Content.Dispose();
            if (!s.Source.IsDisposed) s.Source.Dispose();
        }
        _surfaces.Clear();
    }
}
