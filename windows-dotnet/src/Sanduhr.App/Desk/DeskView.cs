using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// One screen's Desk, as the Mac's DeskView lays it out (spec 5.1): the side columns hug their edges
/// with a top, middle and bottom stack each; with a piece at top or bottom center a center column sits
/// on the middle of the screen and the sides draw only in the room it leaves
/// (<see cref="DeskGeometry.ColumnWidths"/>). The margins are the saved ones plus the taskbar's reach,
/// less the inner inset that gives handwritten glyphs room. Pieces in S5: the clock and date, the Claude
/// line and the meters, in white ink with a soft shadow. Nothing here takes a click.
/// </summary>
internal sealed class DeskView : Grid
{
    /// <summary>The widget's over-limit red (<c>ThemeCatalog.UsageColor</c> at 90% and up).</summary>
    private static readonly Color WarningRed = Color.FromRgb(0xF8, 0x71, 0x71);

    private readonly DeskScreen _screen;
    private readonly DeskSettings _settings;
    private readonly FontFamily _family;
    private readonly FontFamily _bold;
    private DeskUsage _usage = DeskUsage.Empty;
    private DateTimeOffset _now = DateTimeOffset.Now;

    public DeskView(DeskScreen screen, DeskSettings settings, DeskFonts fonts)
    {
        _screen = screen;
        _settings = settings;
        (_family, _bold) = fonts.Families(settings.Font);
        Background = null; // fully transparent: clicks pass through to the desktop
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
    }

    /// <summary>Redraw with new numbers or a new minute.</summary>
    public void Update(DeskUsage usage, DateTimeOffset now)
    {
        _usage = usage;
        _now = now;
        Build();
    }

    private double TimeSize => _settings.TimeSize;
    private double Inset => TimeSize * 0.18;

    private readonly List<(StackPanel Stack, DeskColumn Column, DeskRow Row)> _stacks = [];

    /// <summary>
    /// Lays the whole screen out once, then hands each stack of pieces out on its own, with the point
    /// it hangs from (its column's edge or middle, its row's top, middle or bottom) in screen DIPs. The
    /// host draws each in a window just its size, so a redraw repaints a few small surfaces instead of
    /// the whole screen. The view is spent afterwards: build a new one for the next redraw.
    /// </summary>
    public IReadOnlyList<DeskIsland> TakeIslands()
    {
        var size = _screen.SizeDips;
        Measure(size);
        Arrange(new Rect(size));
        UpdateLayout();
        var islands = new List<DeskIsland>();
        foreach (var (stack, column, row) in _stacks)
        {
            if (stack.Children.Count == 0 || stack.Parent is not Panel parent) continue;
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
            double maxWidth = stack.Parent is FrameworkElement col && col.MaxWidth is > 0 and < double.PositiveInfinity
                ? col.MaxWidth : (parent as FrameworkElement)?.ActualWidth ?? r.Width;
            parent.Children.Remove(stack);
            stack.MaxWidth = maxWidth;
            islands.Add(new DeskIsland(stack, new Point(x, y), h, v));
        }
        Children.Clear();
        _stacks.Clear();
        return islands;
    }

    private void Build()
    {
        Children.Clear();
        _stacks.Clear();
        var arrangement = new DeskArrangement(_settings.Layout);
        var stacks = arrangement.Stacks();
        var m = _settings.Margins;
        var margins = DeskGeometry.Margins(m.Left, m.Right, m.Top, m.Bottom, _screen.Taskbar, Inset);
        var size = _screen.SizeDips;
        var content = DeskGeometry.Content(size.Width, size.Height, margins, Inset);

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
            center = Column(DeskColumn.Center, stacks, HorizontalAlignment.Center);
            center.Measure(new Size(colWidth, double.PositiveInfinity));
            var widths = DeskGeometry.ColumnWidths(content.Width, colWidth, center.DesiredSize.Width);
            side = widths.Side;
            center.MaxWidth = Math.Max(0, widths.Center);
            center.HorizontalAlignment = HorizontalAlignment.Center;
        }
        var left = Column(DeskColumn.Left, stacks, HorizontalAlignment.Left);
        left.MaxWidth = side;
        left.HorizontalAlignment = HorizontalAlignment.Left;
        var right = Column(DeskColumn.Right, stacks, HorizontalAlignment.Right);
        right.MaxWidth = side;
        right.HorizontalAlignment = HorizontalAlignment.Right;
        area.Children.Add(left);
        area.Children.Add(right);
        if (center is not null) area.Children.Add(center);
    }

    /// <summary>A column: top, middle and bottom stacks, the middle halfway between the other two.</summary>
    private FrameworkElement Column(DeskColumn column, IReadOnlyDictionary<DeskAnchor, IReadOnlyList<DeskPlacement>> stacks,
        HorizontalAlignment align)
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
            _stacks.Add((stack, column, row));
            foreach (var p in rows[i].Pieces)
                if (Piece(p, align) is { } piece)
                {
                    piece.Margin = new Thickness(0, stack.Children.Count > 0 ? TimeSize * 0.12 : 0, 0, 0);
                    stack.Children.Add(piece);
                }
            SetRow(stack, i);
            grid.Children.Add(stack);
        }
        return grid;
    }

    private FrameworkElement? Piece(DeskPlacement p, HorizontalAlignment align)
    {
        double ts = TimeSize * p.Scale;
        FrameworkElement? piece = p.Widget switch
        {
            "clock" => Clock(ts, align),
            "claude" => ClaudeLine(ts),
            "meters" => Meters(ts, align),
            _ => null,
        };
        if (piece is null) return null;
        piece.HorizontalAlignment = align;
        // The Desk ink: white at 0.92 with a soft dark shadow, readable on any wallpaper.
        piece.Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.45, BlurRadius = 10, ShadowDepth = 2, Direction = 270 };
        return piece;
    }

    private TextBlock Text(string text, double size, double opacity, bool bold = false, TextAlignment alignment = TextAlignment.Left) => new()
    {
        Text = text,
        FontFamily = bold ? _bold : _family,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        FontSize = Math.Max(1, size),
        Foreground = Brushes.White,
        Opacity = 0.92 * opacity,
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
        var local = _now.ToLocalTime();
        var panel = new StackPanel { HorizontalAlignment = align };
        var time = Text(local.ToString("h:mm", CultureInfo.CurrentCulture), ts, 1, bold: true, TextAlign(align));
        time.HorizontalAlignment = align;
        var date = Text(local.ToString("dddd, MMMM d", CultureInfo.CurrentCulture), ts * 0.3, 0.85, alignment: TextAlign(align));
        date.HorizontalAlignment = align;
        date.Margin = new Thickness(0, 2, 0, 0);
        panel.Children.Add(time);
        panel.Children.Add(date);
        return panel;
    }

    private FrameworkElement? ClaudeLine(double ts)
    {
        var line = DeskClaudeText.Line(_usage);
        if (line is null) return null;
        return Text(line, ts * 0.19, _usage.IsStale(_now) ? 0.45 : 0.75);
    }

    private FrameworkElement? Meters(double ts, HorizontalAlignment align)
    {
        var rows = DeskMeters.Rows(_usage, _now);
        double size = ts * 0.17;
        double width = ts * 3.2;
        if (rows.Count == 0)
        {
            if (!_usage.SignInNeeded) return null;
            return Text(DeskClaudeText.SignInLine(_usage.FirstSignIn), size, 0.75);
        }
        var panel = new StackPanel { HorizontalAlignment = align, Opacity = _usage.IsStale(_now) ? 0.5 : 1 };
        foreach (var row in rows)
        {
            var block = new StackPanel { Width = width, Margin = new Thickness(0, panel.Children.Count > 0 ? size * 0.6 : 0, 0, 0) };
            var head = new DockPanel { LastChildFill = true };
            var pct = Text($"{row.Percent}%", size, 1);
            if (row.Warning) pct.Foreground = new SolidColorBrush(WarningRed);
            DockPanel.SetDock(pct, Dock.Right);
            head.Children.Add(pct);
            var label = Text(row.Label, size, 0.85);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            head.Children.Add(label);
            block.Children.Add(head);

            double barHeight = Math.Max(4, size * 0.38);
            var bar = new Grid { Width = width, Height = barHeight * 2, Margin = new Thickness(0, size * 0.22, 0, 0) };
            bar.Children.Add(new Rectangle
            {
                Height = barHeight, RadiusX = barHeight / 2, RadiusY = barHeight / 2,
                Fill = Brushes.White, Opacity = 0.22, VerticalAlignment = VerticalAlignment.Center,
            });
            bar.Children.Add(new Rectangle
            {
                Width = width * row.Fill, Height = barHeight, RadiusX = barHeight / 2, RadiusY = barHeight / 2,
                Fill = row.Warning ? new SolidColorBrush(WarningRed) : Brushes.White, Opacity = 0.92,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            });
            if (row.Pace is { } pace)
                bar.Children.Add(new Rectangle
                {
                    Width = 2, Height = barHeight * 2, Fill = Brushes.White, Opacity = 0.92,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(Math.Max(0, Math.Min(width - 2, pace * width)), 0, 0, 0),
                });
            block.Children.Add(bar);
            if (row.Reset.Length > 0)
                block.Children.Add(Text($"resets {row.Reset}", size * 0.8, 0.7));
            panel.Children.Add(block);
        }
        return panel;
    }
}
