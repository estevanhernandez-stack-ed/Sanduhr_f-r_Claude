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
    /// <summary>How far the now-playing piece is up, 0 (gone) to 1 (up): its opacity and the share of its
    /// height the stack leaves it (<see cref="NowPlayingPresence.Amount"/>). The live value then arrives
    /// through <see cref="DeskView.SetPresence"/>; this is where a redraw starts.</summary>
    public double Presence { get; init; } = 1;
    /// <summary>The look the playing song wears (a gradient and a letter style); null draws the line in the Desk's own ink.</summary>
    public NowPlayingLookDraw? Look { get; init; }
    /// <summary>Draw the bars beside the track (spec 6.6).</summary>
    public bool Bars { get; init; }
    /// <summary>Gradient bars (spec 3.8 G): the ink's colors spread across the row, each bar fading toward its base.</summary>
    public bool BarsGradient { get; init; }
    public AudioLevelSource? Audio { get; init; }
    public TelemetrySource? Telemetry { get; init; }
    /// <summary>The weather piece: the place (null: not set up) and its latest reading.</summary>
    public WeatherPlace? Place { get; init; }
    public WeatherReading? Weather { get; init; }
    public bool WeatherOn { get; init; }
    /// <summary>The name the weather piece shows instead of the place's (#315, "Show it as"); empty for the place's own name.</summary>
    public string WeatherNickname { get; init; } = "";
    public IReadOnlyList<Watcher> Watchers { get; init; } = [];
    /// <summary>The Launcher piece's buttons and what they draw from (null: none to draw).</summary>
    public DeskLauncherFrame? Launcher { get; init; }
    /// <summary>The companion piece's sprite (spec 3.8 section 4); null when no sprite is installed or the piece is not placed.</summary>
    public CompanionService? Companion { get; init; }
    /// <summary>The alert thresholds the meters' signal uses (theme match): Alerts' Warn at and Urgent at.</summary>
    public int WarnAt { get; init; } = MeterSignal.DefaultWarnAt;
    public int UrgentAt { get; init; } = MeterSignal.DefaultUrgentAt;

    // -- theme match: what a followed theme draws. Null is the Desk's own look, exactly as before. --
    /// <summary>The plate's tint (six hex digits); null is black behind light ink, white behind dark ink.</summary>
    public string? PlateHex { get; init; }
    /// <summary>The plate's strength when the readability guard raises it over a bright wallpaper; null is <see cref="DeskPlate.Opacity"/>.</summary>
    public double? PlateOpacity { get; init; }
    /// <summary>Now playing's bars and position bar when no song look shows: the theme gradient. Null: the ink (and Gradient bars).</summary>
    public IReadOnlyList<string>? NowPlayingStops { get; init; }
    /// <summary>The meters' colors (desk.bars, else the theme gradient), their track (bar_bg) and pace tick (pace_marker). Null: the ink.</summary>
    public IReadOnlyList<string>? MeterStops { get; init; }
    public string? MeterTrackHex { get; init; }
    public string? PaceHex { get; init; }
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
internal sealed partial class DeskView : Grid, IDisposable
{
    /// <summary>The widget's over-limit red (<c>ThemeCatalog.UsageColor</c> at 90% and up): a failed watcher's title. Meters take
    /// <see cref="MeterSignal"/> instead.</summary>
    private static readonly Color WarningRed = Color.FromRgb(0xF8, 0x71, 0x71);

    /// <summary>The pieces this build draws (watchers: only from the harness until their tools ship).</summary>
    public static readonly IReadOnlyList<string> Drawn = ["message", "clock", "claude", "meters", "nowPlaying", "telemetry", "weather", "watchers", "launcher", "glow", "companion"];

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
    /// <summary>The now-playing pieces whose size follows the presence (one per view), and what else
    /// follows it (a plate that would be left behind as an empty blur).</summary>
    private readonly List<ReflowHost> _hosts = [];
    private readonly List<Action<double>> _presenceSinks = [];
    private DeskRect _content = new(0, 0, 0, 0);
    /// <summary>The roaming companion, drawn as its own island that moves along its route (null when it sits in a stack).</summary>
    private (FrameworkElement Piece, DeskIslandMotion Motion, DeskPause Pause)? _roaming;

    /// <summary>
    /// Arrange mode's drawing (spec 7.3): a piece that would draw nothing (nothing playing, an empty
    /// message, weather unset, no Claude numbers, the glow's spot) draws its name instead, so every
    /// placed piece can be grabbed. Set before <see cref="Update(DeskFrame, DeskInkMap)"/>.
    /// </summary>
    public bool Arranging { get; set; }

    /// <summary>The content area the layout used, in this view's DIPs (the anchor points hang from it).</summary>
    public DeskRect ContentRect => _content;

    /// <summary>Each drawn piece's outline in this view's DIPs after layout, by widget key: the four corners after its stack's
    /// slant (the fold, #314), so Arrange Desk hit-tests and outlines what is drawn. An upright piece's quad is its frame.</summary>
    public IReadOnlyDictionary<string, DeskQuad> PieceQuads()
    {
        LayOut();
        var quads = new Dictionary<string, DeskQuad>(StringComparer.Ordinal);
        foreach (var (widget, element) in _pieces)
        {
            if (element.RenderSize.Width <= 0 || element.RenderSize.Height <= 0) continue;
            var t = element.TransformToAncestor(this);
            var s = element.RenderSize;
            DeskPoint P(double x, double y) { var p = t.Transform(new Point(x, y)); return new DeskPoint(p.X, p.Y); }
            quads[widget] = new DeskQuad(P(0, 0), P(s.Width, 0), P(s.Width, s.Height), P(0, s.Height));
        }
        return quads;
    }

    /// <summary>Each drawn piece's frame in this view's DIPs after layout, by widget key (around its slant when its stack has one).</summary>
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
            var hosts = _hosts.Where(rh => ReferenceEquals(rh.Parent, stack)).ToList();
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
            FrameworkElement content = top;
            if (hosts.Count > 0)
            {
                // The island's window is sized once, for the stack with the now-playing piece fully up,
                // and the stack sits against its anchor edge inside it: the piece growing or shrinking
                // moves the neighbours inside the window, never the window.
                double openHeight = top.DesiredSize.Height;
                foreach (var rh in hosts) openHeight += rh.FullHeight - rh.DesiredSize.Height;
                openHeight = Math.Ceiling(openHeight);
                content = new ReserveFrame(top, openHeight, v);
            }
            islands.Add(new DeskIsland(content, new Point(x, y), h, v) { Pause = pause, Widgets = widgets });
        }
        if (_roaming is { } walker)
        {
            if (walker.Piece.Parent is Panel holder) holder.Children.Remove(walker.Piece);
            islands.Add(new DeskIsland(walker.Piece, walker.Motion.Anchor, HorizontalAlignment.Center, VerticalAlignment.Bottom)
            {
                Pause = walker.Pause,
                Widgets = [CompanionSettings.Widget],
                Motion = walker.Motion,
            });
            _roaming = null;
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

    /// <summary>
    /// Moves the now-playing piece to <paramref name="amount"/> (0 gone, 1 up) in place: it fades, and the
    /// pieces beside it close the gap or open it (the stack reflows inside the island's window; nothing
    /// is laid out again and no window moves).
    /// </summary>
    public void SetPresence(double amount)
    {
        foreach (var host in _hosts) host.Presence = amount;
        foreach (var sink in _presenceSinks) sink(amount);
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
        _hosts.Clear();
        _presenceSinks.Clear();
        _roaming = null;
        var arrangement = new DeskArrangement(_settings.Layout);
        // A roaming companion is not in a stack: it walks the screen's edges in an island of its own.
        bool roams = !Arranging && _frame.Companion is { Ready: true, Roaming: true } && arrangement.Placement(CompanionSettings.Widget) is not null;
        var stacks = arrangement.Stacks(roams ? Drawn.Where(w => w != CompanionSettings.Widget).ToList() : Drawn);
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
        if (roams && arrangement.Placement(CompanionSettings.Widget) is { } companion)
            RoamingCompanion(companion, size, content);
    }

    /// <summary>
    /// The roaming companion (spec 3.8 section 4): its route runs along the pack's edges of this monitor, less the taskbar's
    /// reach, and it starts from the point of the route nearest its anchor. It is laid out in this view (so it has a size)
    /// and handed out by <see cref="TakeIslands"/> as an island the host moves.
    /// </summary>
    private void RoamingCompanion(DeskPlacement p, Size size, DeskRect content)
    {
        var service = _frame.Companion!;
        var t = _screen.Taskbar;
        var area = new DeskRect(t.Left, t.Top, Math.Max(0, size.Width - t.Left - t.Right), Math.Max(0, size.Height - t.Top - t.Bottom));
        var cell = service.CellSize;
        var path = CompanionPath.Build(service.Sprite!.Edges, area, cell.Width * p.Scale, cell.Height * p.Scale);
        if (path is null) return;
        double ax = DeskAnchors.Column(p.Anchor) switch
        {
            DeskColumn.Left => content.Left,
            DeskColumn.Right => content.Right,
            _ => content.MidX,
        };
        double ay = DeskAnchors.Row(p.Anchor) switch
        {
            DeskRow.Top => content.Top,
            DeskRow.Middle => content.MidY,
            _ => content.Bottom,
        };
        service.StartAt(path.Nearest(ax, ay));
        var spot = path.At(service.Distance);
        var motion = new DeskIslandMotion(new Point(spot.X, spot.Y));
        var pause = new DeskPause();
        var piece = CompanionPieceVisual.Roaming(service, p.Scale, path, motion, pause, _live);
        piece.Tag = p.Widget;
        Children.Add(piece);
        _pieces[p.Widget] = piece;
        _roaming = (piece, motion, pause);
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
                    stack.Children.Add(piece);
                    widgets.Add(p.Widget);
                    _pieces[p.Widget] = piece;
                    kinds.Add((p.Widget == "glow", piece.Height));
                }
            SpaceOut(stack);
            FrameworkElement placed = _settings.Plate ? Plated(stack, kinds, align) : stack;
            // The fold (#314): the stack, its plate with it, slants as one face. A LayoutTransform, so the island's window is sized
            // for the slanted stack; the host's DPI scale on the island root composes with it.
            if (Fold(rows[i].Pieces) is { } fold) placed.LayoutTransform = fold;
            // A stack whose only real piece is the now-playing one has nothing else to hold its plate
            // up: the plate fades with the piece instead of lingering as an empty blur.
            if (stack.Children.OfType<ReflowHost>().FirstOrDefault() is { } reflow
                && kinds.Count(k => !k.Glow) == 1
                && placed is PlateHost { Children.Count: > 0 } wrap && wrap.Children[0] is Border plate)
            {
                plate.Opacity = reflow.Presence;
                _presenceSinks.Add(v => plate.Opacity = v);
            }
            SetRow(placed, i);
            grid.Children.Add(placed);
        }
        return grid;
    }

    /// <summary>The slant of a stack (#314): its first piece's tilt (the pieces of one anchor share one), as a vertical shear;
    /// null when it stands upright.</summary>
    internal static Transform? Fold(IReadOnlyList<DeskPlacement> stack)
    {
        double tilt = stack.Count == 0 ? 0 : DeskArrangement.ClampTilt(stack[0].Tilt);
        if (tilt == 0) return null;
        var skew = new SkewTransform(0, tilt);
        skew.Freeze();
        return skew;
    }

    /// <summary>
    /// The gap between neighbours: <c>TimeSize * 0.12</c> above every piece but the first. A now-playing
    /// piece (<see cref="ReflowHost"/>) carries its own gap so it closes with the piece: the gap above it
    /// when it is not first, the gap below it when it is first (then the piece after it takes none, as it
    /// is the first once the piece is gone). Fully gone, the stack is exactly the stack drawn without it.
    /// </summary>
    private void SpaceOut(StackPanel stack)
    {
        double gap = TimeSize * 0.12;
        for (int i = 0; i < stack.Children.Count; i++)
        {
            var e = (FrameworkElement)stack.Children[i];
            if (e is ReflowHost host)
            {
                host.Margin = new Thickness(0);
                host.LeadGap = i > 0 ? gap : 0;
                host.TrailGap = i == 0 && stack.Children.Count > 1 ? gap : 0;
            }
            else
            {
                bool afterFirstHost = i == 1 && stack.Children[0] is ReflowHost;
                e.Margin = new Thickness(0, i > 0 && !afterFirstHost ? gap : 0, 0, 0);
            }
        }
    }

    private FrameworkElement? Piece(DeskPlacement p, HorizontalAlignment align, double width)
    {
        double ts = TimeSize * p.Scale;
        FrameworkElement? piece = p.Widget switch
        {
            "message" => _frame.Message is { } msg ? DeskMessagePiece.Create(msg, p.Scale, _ink, _fonts, _settings.Font, align, width, _live, _gate) : null,
            "clock" => Clock(ts, align),
            "claude" => p.Style == DeskMeterStyle.Bar ? ClaudeLine(ts) : StyledClaude(ts, align, p.Style),
            "meters" => p.Style == DeskMeterStyle.Bar ? Meters(ts, align) : StyledMeters(ts, align, p.Style),
            "nowPlaying" => NowPlaying(ts, align),
            "telemetry" => p.Style == DeskMeterStyle.Bar ? TelemetryPiece(ts, align) : StyledTelemetry(ts, align, p.Style),
            "weather" => WeatherPiece(ts, align),
            "watchers" => WatchersPiece(ts, align),
            "launcher" => LauncherPiece(ts),
            "glow" => GlowSlot(ts),
            "companion" => _frame.Companion is { Ready: true } c ? CompanionPieceVisual.Create(c, p.Scale, _gate, _live) : null,
            _ => null,
        };
        if (piece is null && Arranging && p.Widget != "glow")
            piece = Text(SettingsNames.DeskPieceName(p.Widget), ts * 0.17, 0.75, alignment: TextAlign(align));
        if (piece is null) return null;
        piece.HorizontalAlignment = align;
        piece.Tag = p.Widget; // which piece this is, for the Desk's clicks (DeskClicks, spec 7.4)
        // The message carries its own glow (spec 6.2) and the glow's spot is clear at rest; every other
        // piece takes the ink's shadow.
        if (p.Widget is not ("message" or "nowPlaying" or "glow" or "launcher" or "companion") && _ink.ShadowOpacity > 0)
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
        var fill = new SolidColorBrush(Color.FromArgb((byte)Math.Round((_frame.PlateOpacity ?? DeskPlate.Opacity) * 255), 0, 0, 0));
        var c = Rgb(_frame.PlateHex ?? DeskPlate.ColorHex(_ink.DarkInk));
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

    /// <summary>A meter's colors: the frame's (a followed theme's bars) or the ink.</summary>
    private IReadOnlyList<string> MeterStops => _frame.MeterStops ?? _ink.Stops;

    /// <summary>A meter's fill (theme match, the usage signal): its colors, and past Warn at or Urgent at with a yellow or red stop in
    /// their middle. Calm in the Desk's own look, it is <see cref="Ink"/> itself, so such a meter draws exactly as it always has.</summary>
    private Brush MeterFill(MeterLevel level) =>
        level == MeterLevel.Calm && _frame.MeterStops is null ? Ink : InkBrush(MeterSignal.Stops(MeterStops, level));

    /// <summary>A meter's track: a followed theme's bar_bg, else the ink (at <see cref="MeterTrackOpacity"/>).</summary>
    private Brush MeterTrack => _frame.MeterTrackHex is { } hex ? SolidInk(hex) : Ink;

    /// <summary>The ink's track is the ink at 22%; a theme's bar_bg is its own color, nearly solid.</summary>
    private double MeterTrackOpacity => _frame.MeterTrackHex is null ? 0.22 : 0.9;

    /// <summary>A meter's pace tick: a followed theme's pace_marker, else the ink.</summary>
    private Brush PaceInk => _frame.PaceHex is { } hex ? SolidInk(hex) : Ink;

    /// <summary>A meter's value past Warn at or Urgent at takes the signal's color; under it, the ink as before.</summary>
    private static void Signal(TextBlock value, MeterLevel level)
    {
        if (MeterSignal.Hex(level) is { } hex) value.Foreground = SolidInk(hex);
    }

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
        MeterLevel level, bool first, double? pace = null)
    {
        var block = new StackPanel { Width = width, Margin = new Thickness(0, first ? 0 : size * 0.6, 0, 0) };
        var head = new DockPanel { LastChildFill = true };
        var pct = Text($"{percent}%", size, 1);
        Signal(pct, level);
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
            Fill = MeterTrack, Opacity = MeterTrackOpacity, VerticalAlignment = VerticalAlignment.Center,
        });
        var rect = new Rectangle
        {
            Width = width * Math.Clamp(fill, 0, 1), Height = barHeight, RadiusX = barHeight / 2, RadiusY = barHeight / 2,
            Fill = MeterFill(level), Opacity = _ink.TextOpacity,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
        };
        bar.Children.Add(rect);
        if (pace is { } p)
            bar.Children.Add(new Rectangle
            {
                Width = 2, Height = barHeight * 2, Fill = PaceInk, Opacity = _ink.TextOpacity,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(Math.Max(0, Math.Min(width - 2, p * width)), 0, 0, 0),
            });
        block.Children.Add(bar);
        return (block, pct, rect);
    }

    private FrameworkElement? Meters(double ts, HorizontalAlignment align)
    {
        var rows = DeskMeters.Rows(_frame.Usage, _frame.Now, warnAt: _frame.WarnAt, urgentAt: _frame.UrgentAt);
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
            var (block, _, _) = BarRow(row.Label, row.Percent, row.Fill, size, width, row.Level, panel.Children.Count == 0, row.Pace);
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
            // Telemetry is no usage: it never carries the signal.
            var (block, pct, fill) = BarRow(row.Label, row.Percent, row.Fill, size, width, MeterLevel.Calm, panel.Children.Count == 0);
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
        // The nickname stands in for the place's name (#315); the forecast was still asked for with the place's coordinates.
        string heading = string.IsNullOrWhiteSpace(_frame.WeatherNickname) ? place.Name : _frame.WeatherNickname.Trim();
        if (_frame.Weather is not { } reading)
        {
            Add(Text(heading, size * 1.6, 1, bold: true, ta));
            Add(Text("…", size, 0.6, alignment: ta));
        }
        else
        {
            var lines = WeatherLines.For(place, reading, _frame.WeatherNickname);
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
        var look = _frame.Look;
        var column = new StackPanel { Width = width };
        if (look is null)
        {
            var text = Text(line, size, 1, alignment: TextAlign(align));
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Width = width;
            column.Children.Add(text);
        }
        else
        {
            column.Children.Add(StyledLine(line, size, width, align, look));
        }
        // With a look the position bar takes its gradient across the whole bar, however much of it is filled.
        // A followed theme's gradient (theme match) when no song look shows.
        Brush barInk = look is not null ? AbsoluteGradient(look.Stops, width)
            : _frame.NowPlayingStops is { } themed ? AbsoluteGradient(themed, width) : Ink;
        double barHeight = Math.Max(2, size * 0.14);
        var bar = new Grid { Width = width, Height = barHeight, Margin = new Thickness(0, size * 0.3, 0, 0) };
        bar.Children.Add(new Rectangle { Fill = barInk, Opacity = 0.22, RadiusX = barHeight / 2, RadiusY = barHeight / 2 });
        var fill = new Rectangle
        {
            Fill = barInk, Opacity = _ink.TextOpacity, RadiusX = barHeight / 2, RadiusY = barHeight / 2,
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

        FrameworkElement content = column;
        if (_frame.Bars && _frame.Audio is { } audio)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var bars = BarsPiece(size, audio, look, _frame.BarsGradient || (look is null && _frame.NowPlayingStops is not null));
            bars.Margin = new Thickness(align == HorizontalAlignment.Right ? 0 : 0, 0, size * 0.6, 0);
            if (align == HorizontalAlignment.Right) { row.Children.Add(column); bars.Margin = new Thickness(size * 0.6, 0, 0, 0); row.Children.Add(bars); }
            else { row.Children.Add(bars); row.Children.Add(column); }
            content = row;
        }
        // Whole piece in a host that fades it and gives its height back to the stack as it goes.
        var host = new ReflowHost { Child = content, Presence = Math.Clamp(_frame.Presence, 0, 1) };
        _hosts.Add(host);
        return host;
    }

    /// <summary>
    /// The now-playing line in a song's look (spec, a look per song): the play-state glyph stays plain in the Desk's ink; the rest, title and
    /// artist, draws in the look's gradient and letter style. Bold, italic and bold italic are the Desk font's own weight and slant,
    /// small caps the phonetic capitals. The gradient runs over the text as drawn, and the text trims with an ellipsis inside the line's
    /// width. A screen reader reads the plain words.
    /// </summary>
    private FrameworkElement StyledLine(string line, double size, double width, HorizontalAlignment align, NowPlayingLookDraw look)
    {
        var (glyph, rest) = NowPlayingText.SplitGlyph(line);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = align };
        double used = 0;
        if (glyph is not null)
        {
            var g = Text(glyph, size, 1);
            g.Margin = new Thickness(0, 0, size * 0.4, 0);
            g.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            used = g.DesiredSize.Width + g.Margin.Right;
            row.Children.Add(g);
        }
        bool bold = LookStyles.IsBold(look.Style), italic = LookStyles.IsItalic(look.Style);
        var styled = new TextBlock
        {
            Text = LookStyles.Text(rest, look.Style),
            FontFamily = bold ? _bold : _family,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
            FontSize = Math.Max(1, size),
            Foreground = InkBrush(look.Stops),
            Opacity = _ink.TextOpacity,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = Math.Max(10, width - used),
        };
        System.Windows.Automation.AutomationProperties.SetName(styled, rest);
        row.Children.Add(styled);
        // The row is as wide as its words (so the gradient spans them) inside a frame of the line's width.
        var frame = new Grid { Width = width };
        frame.Children.Add(row);
        return frame;
    }

    /// <summary>A left-to-right gradient over <paramref name="width"/> device-independent pixels that stays put whatever the filled shape's size is.</summary>
    private static Brush AbsoluteGradient(IReadOnlyList<string> stops, double width)
    {
        if (stops.Count < 2 || stops.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1) return InkBrush(stops);
        var g = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(Math.Max(1, width), 0),
        };
        for (int i = 0; i < stops.Count; i++)
            g.GradientStops.Add(new GradientStop(Rgb(stops[i]), (double)i / (stops.Count - 1)));
        g.Freeze();
        return g;
    }

    private static Brush SolidInk(string hex)
    {
        var b = new SolidColorBrush(Rgb(hex));
        b.Freeze();
        return b;
    }

    /// <summary>
    /// The Command Center-style bars: the output peak level's recent history, in the ink, rising from a baseline, redrawn on each
    /// frame of the level source (only while it runs). Livelier since 3.8 (G): each bar eases up and falls off
    /// (<see cref="AudioBarMotion"/>), and a peak cap holds over it and drops; with <paramref name="gradient"/> the ink's colors
    /// spread across the row and each bar fades toward its base. Calm (Windows' animations off): gentler easing, no caps, and the
    /// source ticks five times a second. A level only: no audio is captured.
    /// </summary>
    private FrameworkElement BarsPiece(double size, AudioLevelSource audio, NowPlayingLookDraw? look = null, bool gradient = false)
    {
        int count = audio.Bars.Levels.Count;
        double barW = Math.Max(2, size * 0.22), gap = Math.Max(1, size * 0.1);
        double height = size * 2.1;
        double capH = Math.Max(1.5, barW * 0.4), capGap = Math.Max(1, capH * 0.6);
        var canvas = new Canvas { Width = count * (barW + gap) - gap, Height = height, VerticalAlignment = VerticalAlignment.Center };
        var rects = new Rectangle[count];
        var caps = new Border[count];
        var motion = new AudioBarMotion(count) { Calm = audio.Calm };
        // The colors along the row: a song's look, or with gradient bars the ink's own stops; null draws every bar in the ink.
        IReadOnlyList<string>? stops = look?.Stops ?? _frame.NowPlayingStops ?? (gradient ? _ink.Stops : null);
        var ink = Ink;
        for (int i = 0; i < count; i++)
        {
            double at = count <= 1 ? 0 : (double)i / (count - 1);
            Brush fill = stops is { Count: > 0 } ? SolidInk(NowPlayingLooks.ColorAt(stops, at)) : ink;
            Brush capFill = fill;
            if (gradient)
            {
                var top = stops is { Count: > 0 } ? Rgb(NowPlayingLooks.ColorAt(stops, at)) : (_ink.Stops.Count > 0 ? Rgb(_ink.Stops[0]) : Colors.White);
                fill = BarFade(top);
                var solid = new SolidColorBrush(top);
                solid.Freeze();
                capFill = solid;
            }
            rects[i] = new Rectangle { Width = barW, Height = 1, Fill = fill, Opacity = _ink.TextOpacity, RadiusX = barW / 3, RadiusY = barW / 3 };
            Canvas.SetLeft(rects[i], i * (barW + gap));
            Canvas.SetBottom(rects[i], 0);
            canvas.Children.Add(rects[i]);
            // A cap is a Border, not a Rectangle: the bars are the canvas's rectangles, the caps ride over them.
            caps[i] = new Border
            {
                Width = barW, Height = capH, Background = capFill, Opacity = _ink.TextOpacity * 0.9, CornerRadius = new CornerRadius(capH / 2),
                Visibility = motion.ShowsCaps ? Visibility.Visible : Visibility.Collapsed,
                Tag = "cap",
            };
            Canvas.SetLeft(caps[i], i * (barW + gap));
            Canvas.SetBottom(caps[i], 0);
            canvas.Children.Add(caps[i]);
        }
        void Draw()
        {
            var heights = motion.Heights;
            var tops = motion.Caps;
            for (int i = 0; i < count; i++)
            {
                double h = Math.Max(1, heights[i] * (height - capH - capGap));
                rects[i].Height = h;
                if (!motion.ShowsCaps) continue;
                // A cap sits a hair above its bar's top and never leaves the piece.
                double capAt = Math.Min(height - capH, Math.Max(h + capGap, tops[i] * (height - capH - capGap) + capGap));
                Canvas.SetBottom(caps[i], capAt);
                caps[i].Visibility = tops[i] > 0.01 ? Visibility.Visible : Visibility.Hidden;
            }
        }
        void Frame()
        {
            motion.Calm = audio.Calm;
            if (!audio.Running && audio.Bars.IsIdle(DateTimeOffset.Now)) motion.Reset();
            else motion.Step(audio.Bars.Levels);
            Draw();
        }
        // The first draw starts where the levels are, so a redraw mid-song does not grow the bars from nothing.
        for (int k = 0; k < 12; k++) motion.Step(audio.Bars.Levels);
        Draw();
        audio.Frame += Frame;
        _live.Add(new Unhook(() => audio.Frame -= Frame));
        return canvas;
    }

    /// <summary>A bar's fill for gradient bars: its color at the top, fading to a third of it at the base.</summary>
    private static Brush BarFade(Color top)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        g.GradientStops.Add(new GradientStop(top, 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, top.R, top.G, top.B), 1));
        g.Freeze();
        return g;
    }

    /// <summary>The Launcher (spec, Desk Launcher): up to eight buttons in a row or a column, drawn by
    /// <see cref="DeskLauncherPiece"/>; null with no buttons. The labels take the ink's shadow, the images none.</summary>
    private FrameworkElement? LauncherPiece(double ts) =>
        _frame.Launcher is { } launcher
            ? DeskLauncherPiece.Create(launcher, ts, _family, Ink, _ink.Stops.Count > 0 ? Rgb(_ink.Stops[0]) : Colors.White,
                _ink.TextOpacity, _ink.ShadowOpacity > 0 ? Shadow(_ink) : null)
            : null;

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

    /// <summary>
    /// The now-playing piece's wrapper: opacity and height follow <see cref="Presence"/> (0 gone, 1 up).
    /// Its layout height is the piece's (plus the gap it owns, <see cref="LeadGap"/> or
    /// <see cref="TrailGap"/>) times the presence, so the pieces beside it move up to close the gap as it
    /// fades and move back down as it returns. The content keeps its full size and is clipped to the
    /// shrinking frame (only while it shrinks). Width never changes.
    /// </summary>
    internal sealed class ReflowHost : Decorator
    {
        public static readonly DependencyProperty PresenceProperty = DependencyProperty.Register(
            nameof(Presence), typeof(double), typeof(ReflowHost),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange,
                (d, e) => ((ReflowHost)d).Apply((double)e.NewValue)));

        public double Presence
        {
            get => (double)GetValue(PresenceProperty);
            set => SetValue(PresenceProperty, Math.Clamp(value, 0, 1));
        }

        /// <summary>The gap above the piece that closes with it (when it is not first in its stack).</summary>
        public double LeadGap { get; set; }

        /// <summary>The gap below the piece that closes with it (when it is first and has company).</summary>
        public double TrailGap { get; set; }

        /// <summary>The height this host takes fully up: the piece and its gap.</summary>
        public double FullHeight => Child is null ? 0 : LeadGap + Child.DesiredSize.Height + TrailGap;

        private void Apply(double p)
        {
            Opacity = p;
            ClipToBounds = p < 1;
        }

        protected override Size MeasureOverride(Size constraint)
        {
            if (Child is null) return default;
            Child.Measure(new Size(constraint.Width, double.PositiveInfinity));
            var d = Child.DesiredSize;
            return new Size(d.Width, FullHeight * Presence);
        }

        protected override Size ArrangeOverride(Size arrange)
        {
            if (Child is { } child)
                child.Arrange(new Rect(0, LeadGap * Presence, Math.Max(arrange.Width, child.DesiredSize.Width), child.DesiredSize.Height));
            return arrange;
        }
    }

    /// <summary>
    /// The island's frame when a now-playing piece can move in it: as tall as the stack with the piece
    /// fully up, with the stack against its anchor edge (top, bottom or middle). The window is fitted to
    /// this once; the stack growing or shrinking inside never moves the window.
    /// </summary>
    internal sealed class ReserveFrame : Decorator
    {
        private readonly double _height;
        private readonly VerticalAlignment _anchor;

        public ReserveFrame(FrameworkElement stack, double height, VerticalAlignment anchor)
        {
            _height = height;
            _anchor = anchor;
            Child = stack;
        }

        protected override Size MeasureOverride(Size constraint)
        {
            if (Child is null) return default;
            Child.Measure(constraint);
            return new Size(Child.DesiredSize.Width, Math.Max(Child.DesiredSize.Height, _height));
        }

        protected override Size ArrangeOverride(Size arrange)
        {
            if (Child is { } child)
            {
                double h = child.DesiredSize.Height;
                double y = _anchor switch
                {
                    VerticalAlignment.Bottom => arrange.Height - h,
                    VerticalAlignment.Center => (arrange.Height - h) / 2,
                    _ => 0,
                };
                child.Arrange(new Rect(0, Math.Max(0, y), Math.Max(arrange.Width, child.DesiredSize.Width), h));
            }
            return arrange;
        }
    }

    private sealed class Unhook(Action undo) : IDisposable
    {
        private Action? _undo = undo;
        public void Dispose() { _undo?.Invoke(); _undo = null; }
    }
}
