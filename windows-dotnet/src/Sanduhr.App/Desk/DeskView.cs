using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>The ink each stack is drawn in (spec 6.7): one for the whole screen, or per anchor when
/// Automatic ink read the wallpaper behind each island.</summary>
internal sealed record DeskInkMap(ResolvedInk Default, IReadOnlyDictionary<DeskAnchor, ResolvedInk>? ByAnchor = null)
{
    public ResolvedInk For(DeskAnchor a) => ByAnchor is not null && ByAnchor.TryGetValue(a, out var i) ? i : Default;

    /// <summary>S5's ink: white at 0.92 with a soft dark shadow.</summary>
    public static DeskInkMap Light { get; } = new(DeskInk.Resolve(DeskInkSettings.Default with { Mode = DeskInkMode.Light }, null, null));
}

/// <summary>Everything one redraw shows besides the layout and look: the numbers, the time, and each
/// S6 piece's content. The live sources update their pieces in place between redraws.</summary>
internal sealed record DeskFrame
{
    public DeskUsage Usage { get; init; } = DeskUsage.Empty;
    public DateTimeOffset Now { get; init; } = DateTimeOffset.Now;
    public DeskMessageFrame? Message { get; init; }
    /// <summary>The track to show, after the hide rules; null hides the piece.</summary>
    public NowPlayingInfo? Track { get; init; }
    /// <summary>The track as it stands now, for the position bar's tick (a seek moves the anchor
    /// without a redraw); null uses <see cref="Track"/>.</summary>
    public Func<NowPlayingInfo?>? TrackNow { get; init; }
    /// <summary>Draw the bars beside the track (spec 6.6).</summary>
    public bool Bars { get; init; }
    public AudioLevelSource? Audio { get; init; }
    public TelemetrySource? Telemetry { get; init; }
    /// <summary>The weather piece: the place (null: not set up) and its latest reading.</summary>
    public WeatherPlace? Place { get; init; }
    public WeatherReading? Weather { get; init; }
    public bool WeatherOn { get; init; }
    public IReadOnlyList<Watcher> Watchers { get; init; } = [];
}

/// <summary>
/// One screen's Desk, as the Mac's DeskView lays it out (spec 5.1): the side columns hug their edges
/// with a top, middle and bottom stack each; with a piece at top or bottom center a center column sits
/// on the middle of the screen and the sides draw only in the room it leaves
/// (<see cref="DeskGeometry.ColumnWidths"/>). The margins are the saved ones plus the taskbar's reach,
/// less the inner inset that gives handwritten glyphs room. S6 adds the message, now playing with its
/// bars, telemetry, weather and watchers, and draws each stack in its own ink (spec 6.7). Pieces whose
/// numbers move between redraws (the position bar, telemetry, the bars) update in place with fixed
/// sizes, so only their island repaints; <see cref="Dispose"/> unhooks them. Nothing here takes a click.
/// </summary>
internal sealed class DeskView : Grid, IDisposable
{
    /// <summary>The widget's over-limit red (<c>ThemeCatalog.UsageColor</c> at 90% and up).</summary>
    private static readonly Color WarningRed = Color.FromRgb(0xF8, 0x71, 0x71);

    /// <summary>The pieces this build draws (watchers: only from the harness until their tools ship).</summary>
    public static readonly IReadOnlyList<string> Drawn = ["message", "clock", "claude", "meters", "nowPlaying", "telemetry", "weather", "watchers", "glow"];

    private readonly DeskScreen _screen;
    private readonly DeskSettings _settings;
    private readonly DeskFonts _fonts;
    private readonly FontFamily _family;
    private readonly FontFamily _bold;
    private readonly List<IDisposable> _live = [];
    private readonly List<Action<DateTimeOffset>> _ticks = [];
    private DeskFrame _frame = new();
    private DeskInkMap _inks = DeskInkMap.Light;
    private ResolvedInk _ink = DeskInkMap.Light.Default;
    private readonly Dictionary<string, FrameworkElement> _pieces = new(StringComparer.Ordinal);
    private readonly List<(Rectangle Pill, RadialGradientBrush Brush)> _glows = [];
    private DeskRect _content = new(0, 0, 0, 0);

    /// <summary>
    /// Arrange mode's drawing (spec 7.3): a piece that would draw nothing (nothing playing, an empty
    /// message, weather unset, no Claude numbers, the glow's spot) draws its name instead, so every
    /// placed piece can be grabbed. Set before <see cref="Update(DeskFrame, DeskInkMap)"/>.
    /// </summary>
    public bool Arranging { get; set; }

    /// <summary>The content area the layout used, in this view's DIPs (the anchor points hang from it).</summary>
    public DeskRect ContentRect => _content;

    /// <summary>Each drawn piece's frame in this view's DIPs after layout, by widget key.</summary>
    public IReadOnlyDictionary<string, Rect> PieceFrames()
    {
        LayOut();
        var frames = new Dictionary<string, Rect>(StringComparer.Ordinal);
        foreach (var (widget, element) in _pieces)
        {
            // Laid out but maybe not yet in a window (IsVisible would read false then): the size decides.
            if (element.RenderSize.Width <= 0 || element.RenderSize.Height <= 0) continue;
            frames[widget] = element.TransformToAncestor(this).TransformBounds(new Rect(element.RenderSize));
        }
        return frames;
    }

    public DeskView(DeskScreen screen, DeskSettings settings, DeskFonts fonts)
    {
        _screen = screen;
        _settings = settings;
        _fonts = fonts;
        (_family, _bold) = fonts.Families(settings.Font);
        Background = null; // fully transparent: clicks pass through to the desktop
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
    }

    /// <summary>S5's redraw: new numbers or a new minute, in Light ink.</summary>
    public void Update(DeskUsage usage, DateTimeOffset now) => Update(new DeskFrame { Usage = usage, Now = now }, DeskInkMap.Light);

    /// <summary>Lay the screen out with this frame and these inks (a second call replaces the first).</summary>
    public void Update(DeskFrame frame, DeskInkMap inks)
    {
        _frame = frame;
        _inks = inks;
        Build();
    }

    private double TimeSize => _settings.TimeSize;
    private double Inset => TimeSize * 0.18;

    private readonly List<(StackPanel Stack, DeskColumn Column, DeskRow Row, DeskPause Pause, List<string> Widgets)> _stacks = [];
    /// <summary>The gate of the stack being built; the live pieces in it watch this one (S7, 7.1).</summary>
    private DeskPause _gate = new();

    /// <summary>Each non-empty stack's rect in screen DIPs after layout (Automatic ink samples the
    /// wallpaper behind these).</summary>
    public IReadOnlyDictionary<DeskAnchor, Rect> StackRects()
    {
        LayOut();
        var rects = new Dictionary<DeskAnchor, Rect>();
        foreach (var (stack, column, row, _, _) in _stacks)
        {
            if (stack.Children.Count == 0 || DeskAnchors.At(column, row) is not { } a) continue;
            rects[a] = stack.TransformToAncestor(this).TransformBounds(new Rect(stack.RenderSize));
        }
        return rects;
    }

    private void LayOut()
    {
        var size = _screen.SizeDips;
        Measure(size);
        Arrange(new Rect(size));
        UpdateLayout();
    }

    /// <summary>
    /// Lays the whole screen out once, then hands each stack of pieces out on its own, with the point
    /// it hangs from (its column's edge or middle, its row's top, middle or bottom) in screen DIPs. The
    /// host draws each in a window just its size, so a redraw repaints a few small surfaces instead of
    /// the whole screen. The view is spent afterwards (its live pieces keep running until
    /// <see cref="Dispose"/>): build a new one for the next redraw.
    /// </summary>
    public IReadOnlyList<DeskIsland> TakeIslands()
    {
        LayOut();
        var islands = new List<DeskIsland>();
        foreach (var (stack, column, row, pause, widgets) in _stacks)
        {
            // With the plate on, the stack sits in a PlateHost (same frame); the host is what moves.
            FrameworkElement top = stack.Parent is PlateHost host ? host : stack;
            if (stack.Children.Count == 0 || top.Parent is not Panel parent) continue;
            var r = stack.TransformToAncestor(this).TransformBounds(new Rect(stack.RenderSize));
            double x = column switch
            {
                DeskColumn.Left => r.Left,
                DeskColumn.Right => r.Right,
                _ => r.Left + r.Width / 2,
            };
            double y = row switch
            {
                DeskRow.Top => r.Top,
                DeskRow.Bottom => r.Bottom,
                _ => r.Top + r.Height / 2,
            };
            var h = column switch
            {
                DeskColumn.Left => HorizontalAlignment.Left,
                DeskColumn.Right => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Center,
            };
            var v = row switch
            {
                DeskRow.Top => VerticalAlignment.Top,
                DeskRow.Bottom => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Center,
            };
            double maxWidth = top.Parent is FrameworkElement col && col.MaxWidth is > 0 and < double.PositiveInfinity
                ? col.MaxWidth : (parent as FrameworkElement)?.ActualWidth ?? r.Width;
            parent.Children.Remove(top);
            stack.MaxWidth = maxWidth;
            islands.Add(new DeskIsland(top, new Point(x, y), h, v) { Pause = pause, Widgets = widgets });
        }
        Children.Clear();
        _stacks.Clear();
        return islands;
    }

    /// <summary>The minute's change without a new layout: the clock's time and date update in place
    /// (the host then re-fits only the windows whose content changed size).</summary>
    public void Tick(DateTimeOffset now)
    {
        foreach (var t in _ticks) t(now);
    }

    /// <summary>Unhooks the live pieces (their timers and sources).</summary>
    public void Dispose()
    {
        _ticks.Clear();
        foreach (var (pill, _) in _glows) pill.BeginAnimation(UIElement.OpacityProperty, null);
        foreach (var d in _live) d.Dispose();
        _live.Clear();
    }

    private void Build()
    {
        Dispose();
        Children.Clear();
        _stacks.Clear();
        _pieces.Clear();
        _glows.Clear();
        var arrangement = new DeskArrangement(_settings.Layout);
        var stacks = arrangement.Stacks(Drawn);
        var m = _settings.Margins;
        var margins = DeskGeometry.Margins(m.Left, m.Right, m.Top, m.Bottom, _screen.Taskbar, Inset);
        var size = _screen.SizeDips;
        var content = DeskGeometry.Content(size.Width, size.Height, margins, Inset);
        _content = content;

        var area = new Grid
        {
            Margin = new Thickness(content.X, content.Y, Math.Max(0, size.Width - content.Right), Math.Max(0, size.Height - content.Bottom)),
        };
        Children.Add(area);

        double colWidth = Math.Max(200, size.Width * 0.46);
        bool hasCenter = stacks.ContainsKey(DeskAnchor.TC) || stacks.ContainsKey(DeskAnchor.BC);
        double side = colWidth;
        FrameworkElement? center = null;
        if (hasCenter)
        {
            center = Column(DeskColumn.Center, stacks, HorizontalAlignment.Center, colWidth);
            center.Measure(new Size(colWidth, double.PositiveInfinity));
            var widths = DeskGeometry.ColumnWidths(content.Width, colWidth, center.DesiredSize.Width);
            side = widths.Side;
            center.MaxWidth = Math.Max(0, widths.Center);
            center.HorizontalAlignment = HorizontalAlignment.Center;
        }
        var left = Column(DeskColumn.Left, stacks, HorizontalAlignment.Left, side);
        left.MaxWidth = side;
        left.HorizontalAlignment = HorizontalAlignment.Left;
        var right = Column(DeskColumn.Right, stacks, HorizontalAlignment.Right, side);
        right.MaxWidth = side;
        right.HorizontalAlignment = HorizontalAlignment.Right;
        area.Children.Add(left);
        area.Children.Add(right);
        if (center is not null) area.Children.Add(center);
    }

    /// <summary>A column: top, middle and bottom stacks, the middle halfway between the other two.</summary>
    private FrameworkElement Column(DeskColumn column, IReadOnlyDictionary<DeskAnchor, IReadOnlyList<DeskPlacement>> stacks,
        HorizontalAlignment align, double width)
    {
        var grid = new Grid();
        IReadOnlyList<DeskPlacement> At(DeskRow row) =>
            DeskAnchors.At(column, row) is { } a && stacks.TryGetValue(a, out var s) ? s : [];
        var top = At(DeskRow.Top);
        var middle = At(DeskRow.Middle);
        var bottom = At(DeskRow.Bottom);
        var rows = new List<(IReadOnlyList<DeskPlacement> Pieces, bool Spacer)> { (top, false), ([], true) };
        if (middle.Count > 0) { rows.Add((middle, false)); rows.Add(([], true)); }
        rows.Add((bottom, false));
        for (int i = 0; i < rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = rows[i].Spacer ? new GridLength(1, GridUnitType.Star) : GridLength.Auto,
                MinHeight = rows[i].Spacer ? 32 : 0,
            });
            if (rows[i].Spacer) continue;
            var stack = new StackPanel { HorizontalAlignment = align };
            var row = rows.Count == 3 ? (i == 0 ? DeskRow.Top : DeskRow.Bottom)
                : i == 0 ? DeskRow.Top : i == 2 ? DeskRow.Middle : DeskRow.Bottom;
            var pause = new DeskPause();
            var widgets = new List<string>();
            _stacks.Add((stack, column, row, pause, widgets));
            _gate = pause;
            if (DeskAnchors.At(column, row) is { } anchor) _ink = _inks.For(anchor);
            var kinds = new List<(bool Glow, double Height)>();
            foreach (var p in rows[i].Pieces)
                if (Piece(p, align, width) is { } piece)
                {
                    piece.Margin = new Thickness(0, stack.Children.Count > 0 ? TimeSize * 0.12 : 0, 0, 0);
                    stack.Children.Add(piece);
                    widgets.Add(p.Widget);
                    _pieces[p.Widget] = piece;
                    kinds.Add((p.Widget == "glow", piece.Height));
                }
            FrameworkElement placed = _settings.Plate ? Plated(stack, kinds, align) : stack;
            SetRow(placed, i);
            grid.Children.Add(placed);
        }
        return grid;
    }

    private FrameworkElement? Piece(DeskPlacement p, HorizontalAlignment align, double width)
    {
        double ts = TimeSize * p.Scale;
        FrameworkElement? piece = p.Widget switch
        {
            "message" => _frame.Message is { } msg ? DeskMessagePiece.Create(msg, p.Scale, _ink, _fonts, _settings.Font, align, width, _live, _gate) : null,
            "clock" => Clock(ts, align),
            "claude" => ClaudeLine(ts),
            "meters" => Meters(ts, align),
            "nowPlaying" => NowPlaying(ts, align),
            "telemetry" => TelemetryPiece(ts, align),
            "weather" => WeatherPiece(ts, align),
            "watchers" => WatchersPiece(ts, align),
            "glow" => GlowSlot(ts),
            _ => null,
        };
        if (piece is null && Arranging && p.Widget != "glow")
            piece = Text(SettingsNames.DeskPieceName(p.Widget), ts * 0.17, 0.75, alignment: TextAlign(align));
        if (piece is null) return null;
        piece.HorizontalAlignment = align;
        piece.Tag = p.Widget; // which piece this is, for the Desk's clicks (DeskClicks, spec 7.4)
        // The message carries its own glow (spec 6.2) and the glow's spot is clear at rest; every other
        // piece takes the ink's shadow.
        if (p.Widget is not ("message" or "nowPlaying" or "glow") && _ink.ShadowOpacity > 0)
            piece.Effect = Shadow(_ink);
        return piece;
    }

    /// <summary>
    /// "Plate behind the text" (<see cref="DeskPlate"/>): ONE soft rounded plate behind the whole stack
    /// (not one per piece: neighbouring per-piece plates overlap and the 30% fills double into a bright
    /// seam). It reaches <see cref="DeskPlate.Reach"/> past the pieces, black behind light ink and white
    /// behind dark ink; a glow slot at the stack's top or bottom is left out of it
    /// (<see cref="DeskPlate.Span"/>). The plate takes no hit of its own and the wrapper's frame is the
    /// stack's, so the pieces keep their tags and click plates. A stack with nothing to plate comes back as is.
    /// </summary>
    private FrameworkElement Plated(StackPanel stack, List<(bool Glow, double Height)> kinds, HorizontalAlignment align)
    {
        if (DeskPlate.Span(kinds.Select(k => k.Glow).ToList()) is not { } span) return stack;
        // Pieces before the span and after it are glow slots: each is its height plus the gap above it.
        double gap = TimeSize * 0.12, topTrim = 0, bottomTrim = 0;
        for (int i = 0; i < span.First; i++) topTrim += kinds[i].Height + gap;
        for (int i = span.Last + 1; i < kinds.Count; i++) bottomTrim += kinds[i].Height + gap;
        var fill = new SolidColorBrush(Color.FromArgb((byte)Math.Round(DeskPlate.Opacity * 255), 0, 0, 0));
        var c = Rgb(DeskPlate.ColorHex(_ink.DarkInk));
        fill.Color = Color.FromArgb(fill.Color.A, c.R, c.G, c.B);
        fill.Freeze();
        var plate = new Border
        {
            Background = fill,
            CornerRadius = new CornerRadius(DeskPlate.CornerRadius),
            Margin = new Thickness(-DeskPlate.Reach, topTrim - DeskPlate.Reach, -DeskPlate.Reach, bottomTrim - DeskPlate.Reach),
            Effect = new BlurEffect { Radius = DeskPlate.Blur, KernelType = KernelType.Gaussian },
            IsHitTestVisible = false,
        };
        var wrap = new PlateHost { HorizontalAlignment = align };
        wrap.Children.Add(plate);
        wrap.Children.Add(stack);
        return wrap;
    }

    /// <summary>A stack and the one plate behind it; <see cref="TakeIslands"/> hands the pair out as the island.</summary>
    private sealed class PlateHost : Grid;

    /// <summary>
    /// The glow's spot (spec 7.3): a fixed-size slot, so a glow never moves its stack-mates, fully clear
    /// at rest. Inside, a pill filled with a radial wash whose opacity is 0 until <see cref="Glow"/>
    /// lights it. While arranging the slot carries its name so it can be seen and grabbed.
    /// </summary>
    private FrameworkElement GlowSlot(double ts)
    {
        double width = ts * 2.2, height = ts * 0.42;
        var slot = new Grid { Width = width, Height = height };
        var brush = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
        brush.GradientStops.Add(new GradientStop(Colors.White, 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x99, 255, 255, 255), 0.45));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        var pill = new Rectangle { RadiusX = height / 2, RadiusY = height / 2, Fill = brush, Opacity = 0, IsHitTestVisible = false };
        slot.Children.Add(pill);
        _glows.Add((pill, brush));
        if (Arranging)
            slot.Children.Add(Centered(Text(SettingsNames.DeskGlow, ts * 0.17, 0.75, alignment: TextAlignment.Center)));
        return slot;
    }

    private static TextBlock Centered(TextBlock t)
    {
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.VerticalAlignment = VerticalAlignment.Center;
        return t;
    }

    /// <summary>
    /// Lights the glow's spot in <paramref name="color"/>: the pill fades in over 0.2 s, holds, and is
    /// clear again by 1.5 s, at 30 frames a second. False when this screen has no spot.
    /// </summary>
    public bool Glow(Color color)
    {
        if (_glows.Count == 0) return false;
        foreach (var (pill, brush) in _glows)
        {
            brush.GradientStops[0].Color = color;
            brush.GradientStops[1].Color = Color.FromArgb(0x99, color.R, color.G, color.B);
            brush.GradientStops[2].Color = Color.FromArgb(0, color.R, color.G, color.B);
            var fade = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.2))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.9))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5))));
            Timeline.SetDesiredFrameRate(fade, 30);
            pill.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        return true;
    }

    internal static DropShadowEffect Shadow(ResolvedInk ink) => new()
    {
        Color = Rgb(ink.ShadowHex),
        Opacity = ink.ShadowOpacity,
        BlurRadius = 8,
        ShadowDepth = 1.5,
        Direction = 270,
    };

    internal static Color Rgb(string hex) =>
        InkHex.Normalize(hex) is { } h
            ? Color.FromRgb(Convert.ToByte(h[..2], 16), Convert.ToByte(h.Substring(2, 2), 16), Convert.ToByte(h.Substring(4, 2), 16))
            : Colors.White;

    /// <summary>The ink as a brush: one color solid, two or more a left-to-right gradient.</summary>
    internal static Brush InkBrush(IReadOnlyList<string> stops)
    {
        if (stops.Count == 0) return Brushes.White;
        if (stops.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
        {
            var solid = new SolidColorBrush(Rgb(stops[0]));
            solid.Freeze();
            return solid;
        }
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (int i = 0; i < stops.Count; i++)
            g.GradientStops.Add(new GradientStop(Rgb(stops[i]), stops.Count == 1 ? 0 : (double)i / (stops.Count - 1)));
        g.Freeze();
        return g;
    }

    private Brush Ink => InkBrush(_ink.Stops);

    private TextBlock Text(string text, double size, double opacity, bool bold = false, TextAlignment alignment = TextAlignment.Left) => new()
    {
        Text = text,
        FontFamily = bold ? _bold : _family,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        FontSize = Math.Max(1, size),
        Foreground = Ink,
        Opacity = _ink.TextOpacity * opacity,
        TextAlignment = alignment,
    };

    private static TextAlignment TextAlign(HorizontalAlignment a) => a switch
    {
        HorizontalAlignment.Right => TextAlignment.Right,
        HorizontalAlignment.Center => TextAlignment.Center,
        _ => TextAlignment.Left,
    };

    private FrameworkElement Clock(double ts, HorizontalAlignment align)
    {
        var local = _frame.Now.ToLocalTime();
        var panel = new StackPanel { HorizontalAlignment = align };
        var time = Text(local.ToString("h:mm", CultureInfo.CurrentCulture), ts, 1, bold: true, TextAlign(align));
        time.HorizontalAlignment = align;
        var date = Text(local.ToString("dddd, MMMM d", CultureInfo.CurrentCulture), ts * 0.3, 0.85, alignment: TextAlign(align));
        date.HorizontalAlignment = align;
        date.Margin = new Thickness(0, 2, 0, 0);
        panel.Children.Add(time);
        panel.Children.Add(date);
        _ticks.Add(now =>
        {
            var l = now.ToLocalTime();
            var t = l.ToString("h:mm", CultureInfo.CurrentCulture);
            var d = l.ToString("dddd, MMMM d", CultureInfo.CurrentCulture);
            if (time.Text != t) time.Text = t;
            if (date.Text != d) date.Text = d;
        });
        return panel;
    }

    private FrameworkElement? ClaudeLine(double ts)
    {
        var line = DeskClaudeText.Line(_frame.Usage);
        if (line is null) return null;
        return Text(line, ts * 0.19, _frame.Usage.IsStale(_frame.Now) ? 0.45 : 0.75);
    }

    /// <summary>A row in the meters' shape: a label and a percent over a bar in the ink. Returns the
    /// pieces a live row updates.</summary>
    private (StackPanel Block, TextBlock Percent, Rectangle Fill) BarRow(string label, int percent, double fill, double size, double width,
        bool warning, bool first, double? pace = null)
    {
        var block = new StackPanel { Width = width, Margin = new Thickness(0, first ? 0 : size * 0.6, 0, 0) };
        var head = new DockPanel { LastChildFill = true };
        var pct = Text($"{percent}%", size, 1);
        if (warning) pct.Foreground = new SolidColorBrush(WarningRed);
        DockPanel.SetDock(pct, Dock.Right);
        head.Children.Add(pct);
        var name = Text(label, size, 0.85);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(name);
        block.Children.Add(head);

        double barHeight = Math.Max(4, size * 0.38);
        var bar = new Grid { Width = width, Height = barHeight * 2, Margin = new Thickness(0, size * 0.22, 0, 0) };
        bar.Children.Add(new Rectangle
        {
            Height = barHeight, RadiusX = barHeight / 2, RadiusY = barHeight / 2,
            Fill = Ink, Opacity = 0.22, VerticalAlignment = VerticalAlignment.Center,
        });
        var rect = new Rectangle
        {
            Width = width * Math.Clamp(fill, 0, 1), Height = barHeight, RadiusX = barHeight / 2, RadiusY = barHeight / 2,
            Fill = warning ? new SolidColorBrush(WarningRed) : Ink, Opacity = _ink.TextOpacity,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
        };
        bar.Children.Add(rect);
        if (pace is { } p)
            bar.Children.Add(new Rectangle
            {
                Width = 2, Height = barHeight * 2, Fill = Ink, Opacity = _ink.TextOpacity,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(Math.Max(0, Math.Min(width - 2, p * width)), 0, 0, 0),
            });
        block.Children.Add(bar);
        return (block, pct, rect);
    }

    private FrameworkElement? Meters(double ts, HorizontalAlignment align)
    {
        var rows = DeskMeters.Rows(_frame.Usage, _frame.Now);
        double size = ts * 0.17;
        double width = ts * 3.2;
        if (rows.Count == 0)
        {
            if (!DeskMeters.SignInLineShown(_frame.Usage)) return null;
            return Text(DeskClaudeText.SignInLine(_frame.Usage.FirstSignIn), size, 0.75);
        }
        var panel = new StackPanel { HorizontalAlignment = align, Opacity = _frame.Usage.IsStale(_frame.Now) ? 0.5 : 1 };
        foreach (var row in rows)
        {
            var (block, _, _) = BarRow(row.Label, row.Percent, row.Fill, size, width, row.Warning, panel.Children.Count == 0, row.Pace);
            if (row.Reset.Length > 0)
                block.Children.Add(Text($"resets {row.Reset}", size * 0.8, 0.7));
            panel.Children.Add(block);
        }
        return panel;
    }

    // ---- S6 pieces ----

    /// <summary>Telemetry (spec 6.4): CPU, RAM and disk in the meters' shape, updated in place.</summary>
    private FrameworkElement? TelemetryPiece(double ts, HorizontalAlignment align)
    {
        if (_frame.Telemetry is not { } source) return null;
        double size = ts * 0.17;
        double width = ts * 2.4;
        var sample = source.Latest ?? new TelemetrySample(0, 0, 0, _frame.Now);
        var panel = new StackPanel { HorizontalAlignment = align };
        var cells = new List<(TextBlock Pct, Rectangle Fill)>();
        foreach (var row in Telemetry.Rows(sample))
        {
            var (block, pct, fill) = BarRow(row.Label, row.Percent, row.Fill, size, width, false, panel.Children.Count == 0);
            panel.Children.Add(block);
            cells.Add((pct, fill));
        }
        void Refresh()
        {
            if (source.Latest is not { } s) return;
            var rows = Telemetry.Rows(s);
            // Only what changed on screen: an unchanged reading repaints nothing (the island's window
            // redraws its whole surface on any change).
            for (int i = 0; i < rows.Count && i < cells.Count; i++)
            {
                var text = $"{rows[i].Percent}%";
                if (cells[i].Pct.Text != text) cells[i].Pct.Text = text;
                double w = Math.Round(width * Math.Clamp(rows[i].Fill, 0, 1));
                if (Math.Abs(cells[i].Fill.Width - w) >= 1) cells[i].Fill.Width = w;
            }
        }
        source.Updated += Refresh;
        _live.Add(new Unhook(() => source.Updated -= Refresh));
        return panel;
    }

    /// <summary>Weather (spec 6.5), Knighthawk's sandwich: the city, now, today's range, tomorrow, and
    /// the Open-Meteo credit its licence asks for beside its data.</summary>
    private FrameworkElement? WeatherPiece(double ts, HorizontalAlignment align)
    {
        if (!_frame.WeatherOn) return null;
        double size = ts * 0.17;
        var ta = TextAlign(align);
        var panel = new StackPanel { HorizontalAlignment = align };
        void Add(TextBlock t) { t.HorizontalAlignment = align; panel.Children.Add(t); }
        if (_frame.Place is not { } place)
        {
            Add(Text(Weather.NoPlaceLine, size, 0.75, alignment: ta));
            return panel;
        }
        if (_frame.Weather is not { } reading)
        {
            Add(Text(place.Name, size * 1.6, 1, bold: true, ta));
            Add(Text("…", size, 0.6, alignment: ta));
        }
        else
        {
            var lines = WeatherLines.For(place, reading);
            double dim = WeatherSchedule.IsStale(reading, _frame.Now) ? 0.5 : 1;
            Add(Text(lines.Heading, size * 1.6, dim, bold: true, ta));
            Add(Text(lines.Now, size * 1.15, 0.92 * dim, alignment: ta));
            Add(Text(lines.Range, size * 0.9, 0.8 * dim, alignment: ta));
            if (lines.Tomorrow is { } t) Add(Text(t, size * 0.9, 0.7 * dim, alignment: ta));
        }
        var credit = Text(Weather.Credit, Math.Max(9, size * 0.55), 0.55, alignment: ta);
        credit.Margin = new Thickness(0, size * 0.3, 0, 0);
        credit.Tag = DeskClicks.WeatherCreditTag;
        Add(credit);
        return panel;
    }

    /// <summary>Now playing (spec 6.3): "▶ Title · Artist" over a thin position bar that moves once a
    /// second while playing (in place); the bars beside it (6.6) when on. Gone with nothing playing.</summary>
    private FrameworkElement? NowPlaying(double ts, HorizontalAlignment align)
    {
        if (_frame.Track is not { } track || NowPlayingText.Desk(track) is not { } line) return null;
        double size = ts * 0.17;
        double width = ts * 3.2;
        var text = Text(line, size, 1, alignment: TextAlign(align));
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Width = width;
        var column = new StackPanel { Width = width };
        column.Children.Add(text);
        double barHeight = Math.Max(2, size * 0.14);
        var bar = new Grid { Width = width, Height = barHeight, Margin = new Thickness(0, size * 0.3, 0, 0) };
        bar.Children.Add(new Rectangle { Fill = Ink, Opacity = 0.22, RadiusX = barHeight / 2, RadiusY = barHeight / 2 });
        var fill = new Rectangle
        {
            Fill = Ink, Opacity = _ink.TextOpacity, RadiusX = barHeight / 2, RadiusY = barHeight / 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = width * (track.Progress(_frame.Now) ?? 0),
        };
        bar.Children.Add(fill);
        if (track.Duration is > 0) column.Children.Add(bar);
        // The shadow on the line and its bar only: the bars beside it redraw 20 times a second, and a
        // blur under them would be recomputed on every frame.
        if (_ink.ShadowOpacity > 0) column.Effect = Shadow(_ink);

        if (track.State == NowPlayingState.Playing && track.Duration is > 0)
        {
            // While the island is covered the bar rests; on resume it reads the current track at once
            // (a seek or a pause may have moved the anchor) and ticks again (S7, 7.1).
            var tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            var current = _frame.TrackNow;
            var gate = _gate;
            void SetBar() => fill.Width = width * ((current?.Invoke() ?? track).Progress(DateTimeOffset.Now) ?? 0);
            void OnGate()
            {
                if (gate.Paused) { tick.Stop(); return; }
                SetBar();
                tick.Start();
            }
            tick.Tick += (_, _) => SetBar();
            if (!gate.Paused) tick.Start();
            gate.Changed += OnGate;
            _live.Add(new Unhook(() => { tick.Stop(); gate.Changed -= OnGate; }));
        }

        if (!_frame.Bars || _frame.Audio is not { } audio) return column;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var bars = BarsPiece(size, audio);
        bars.Margin = new Thickness(align == HorizontalAlignment.Right ? 0 : 0, 0, size * 0.6, 0);
        if (align == HorizontalAlignment.Right) { row.Children.Add(column); bars.Margin = new Thickness(size * 0.6, 0, 0, 0); row.Children.Add(bars); }
        else { row.Children.Add(bars); row.Children.Add(column); }
        return row;
    }

    /// <summary>The Command Center-style bars: the output peak level's recent history, in the ink,
    /// rising from a baseline, redrawn on each frame of the level source (only while it runs).</summary>
    private FrameworkElement BarsPiece(double size, AudioLevelSource audio)
    {
        int count = audio.Bars.Levels.Count;
        double barW = Math.Max(2, size * 0.22), gap = Math.Max(1, size * 0.1);
        double height = size * 2.1;
        var canvas = new Canvas { Width = count * (barW + gap) - gap, Height = height, VerticalAlignment = VerticalAlignment.Center };
        var rects = new Rectangle[count];
        var ink = Ink;
        for (int i = 0; i < count; i++)
        {
            rects[i] = new Rectangle { Width = barW, Height = 1, Fill = ink, Opacity = _ink.TextOpacity, RadiusX = barW / 3, RadiusY = barW / 3 };
            Canvas.SetLeft(rects[i], i * (barW + gap));
            Canvas.SetBottom(rects[i], 0);
            canvas.Children.Add(rects[i]);
        }
        void Frame()
        {
            var levels = audio.Bars.Levels;
            for (int i = 0; i < count && i < levels.Count; i++)
                rects[i].Height = Math.Max(1, levels[i] * height);
        }
        Frame();
        audio.Frame += Frame;
        _live.Add(new Unhook(() => audio.Frame -= Frame));
        return canvas;
    }

    /// <summary>Watchers (spec 6.9): a card per watcher, most urgent first: the title, its progress,
    /// its state or note, and how long it has run.</summary>
    private FrameworkElement? WatchersPiece(double ts, HorizontalAlignment align)
    {
        if (_frame.Watchers.Count == 0) return null;
        double size = ts * 0.17;
        var ta = TextAlign(align);
        var panel = new StackPanel { HorizontalAlignment = align };
        foreach (var w in _frame.Watchers.Take(6))
        {
            var card = new StackPanel { Margin = new Thickness(0, panel.Children.Count > 0 ? size * 0.6 : 0, 0, 0), HorizontalAlignment = align };
            var head = Text(w.Progress is { } p ? $"{w.Title}  {p}" : w.Title, size, w.State == WatcherState.LostTouch ? 0.5 : 1, alignment: ta);
            head.HorizontalAlignment = align;
            if (w.State == WatcherState.Failed) head.Foreground = new SolidColorBrush(WarningRed);
            card.Children.Add(head);
            var note = WatcherText.DeskNote(w);
            var sub = Text(note is null ? WatcherText.Elapsed(w.Elapsed(_frame.Now)) : $"{note} · {WatcherText.Elapsed(w.Elapsed(_frame.Now))}",
                size * 0.8, 0.7, alignment: ta);
            sub.HorizontalAlignment = align;
            card.Children.Add(sub);
            panel.Children.Add(card);
        }
        return panel;
    }

    private sealed class Unhook(Action undo) : IDisposable
    {
        private Action? _undo = undo;
        public void Dispose() { _undo?.Invoke(); _undo = null; }
    }
}
