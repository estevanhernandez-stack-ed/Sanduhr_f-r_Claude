using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>One reading a styled meter draws (#336): its label, the short name a ring carries, the percent, the fill (0 to 1), the
/// pace mark (0 to 1, a Claude limit only) and its level by the alert thresholds (theme match, the usage signal).</summary>
internal sealed record DeskGaugeRead(string Label, string Short, int Percent, double Fill, double? Pace = null, MeterLevel Level = MeterLevel.Calm);

/// <summary>
/// The meter styles (#336): <see cref="DeskMeterStyle.Slim"/>, <see cref="DeskMeterStyle.Ring"/> and <see cref="DeskMeterStyle.Ticks"/>
/// for the Claude limits, telemetry and the Claude line, so a stack fits its column upright or slanted. The bar style is the drawing
/// in <c>DeskView.cs</c>, untouched. Every style keeps a Claude limit's pace mark; nothing here animates (a reading changes in
/// place), so Windows' animations-off has nothing to calm, and OLED care dims the island these sit in like any other.
/// </summary>
internal sealed partial class DeskView
{
    /// <summary>The Claude limits in a style other than bar: the same rows, the sign-in line when there are none.</summary>
    private FrameworkElement? StyledMeters(double ts, HorizontalAlignment align, DeskMeterStyle style)
    {
        var rows = DeskMeters.Rows(_frame.Usage, _frame.Now, warnAt: _frame.WarnAt, urgentAt: _frame.UrgentAt);
        double size = ts * 0.17;
        if (rows.Count == 0)
        {
            if (!DeskMeters.SignInLineShown(_frame.Usage)) return null;
            return Text(DeskClaudeText.SignInLine(_frame.Usage.FirstSignIn), size, 0.75);
        }
        var reads = rows.Select(r => new DeskGaugeRead(r.Label, DeskMeters.ShortLabel(r.Key), r.Percent, r.Fill, r.Pace, r.Level)).ToList();
        var gauges = Gauges(reads, style, size, ts * 2.6, align, null);
        gauges.Opacity = _frame.Usage.IsStale(_frame.Now) ? 0.5 : 1;
        return gauges;
    }

    /// <summary>CPU, RAM and disk in a style other than bar, updated in place as the bar ones are.</summary>
    private FrameworkElement? StyledTelemetry(double ts, HorizontalAlignment align, DeskMeterStyle style)
    {
        if (_frame.Telemetry is not { } source) return null;
        double size = ts * 0.17;
        var sample = source.Latest ?? new TelemetrySample(0, 0, 0, _frame.Now);
        static DeskGaugeRead Read(TelemetryRow r) => new(r.Label, r.Label, r.Percent, r.Fill);
        var setters = new List<Action<DeskGaugeRead>>();
        var gauges = Gauges(Telemetry.Rows(sample).Select(Read).ToList(), style, size, ts * 2.0, align, setters);
        void Refresh()
        {
            if (source.Latest is not { } s) return;
            var rows = Telemetry.Rows(s);
            for (int i = 0; i < rows.Count && i < setters.Count; i++) setters[i](Read(rows[i]));
        }
        source.Updated += Refresh;
        _live.Add(new Unhook(() => source.Updated -= Refresh));
        return gauges;
    }

    /// <summary>
    /// The Claude line in a style other than bar. Slim is the compact two-row form ("claude   7% session" over "resets 3:00   63% week"),
    /// so it no longer runs past its column. Ring and ticks put the name over the session and week limits in that style, each with its
    /// pace mark; without numbers (a sign-in), they fall back to the two rows.
    /// </summary>
    private FrameworkElement? StyledClaude(double ts, HorizontalAlignment align, DeskMeterStyle style)
    {
        double size = ts * 0.19;
        double opacity = _frame.Usage.IsStale(_frame.Now) ? 0.45 : 0.75;
        var tiers = (_frame.Usage.LineTiers ?? _frame.Usage.Tiers).Where(t => t.Key is TierModel.FiveHour or TierModel.SevenDay).ToList();
        IReadOnlyList<DeskMeterRow> rows = style == DeskMeterStyle.Slim ? []
            : DeskMeters.Rows(_frame.Usage with { Tiers = tiers }, _frame.Now, warnAt: _frame.WarnAt, urgentAt: _frame.UrgentAt);
        if (rows.Count == 0)
        {
            if (DeskClaudeText.Compact(_frame.Usage) is not { } lines) return null;
            var panel = new StackPanel { HorizontalAlignment = align };
            for (int i = 0; i < lines.Count; i++)
            {
                var t = Text(lines[i], size, i == 0 ? opacity : opacity * 0.85, alignment: TextAlign(align));
                t.HorizontalAlignment = align;
                panel.Children.Add(t);
            }
            return panel;
        }
        double small = ts * 0.17;
        var column = new StackPanel { HorizontalAlignment = align, Opacity = _frame.Usage.IsStale(_frame.Now) ? 0.6 : 1 };
        var name = Text(_frame.Usage.Account ?? "claude", small, 0.75, alignment: TextAlign(align));
        name.HorizontalAlignment = align;
        name.Margin = new Thickness(0, 0, 0, small * 0.3);
        column.Children.Add(name);
        var reads = rows.Select(r => new DeskGaugeRead(r.Key == TierModel.FiveHour ? "session" : "week",
            r.Key == TierModel.FiveHour ? "session" : "week", r.Percent, r.Fill, r.Pace, r.Level)).ToList();
        column.Children.Add(Gauges(reads, style, small, ts * 2.6, align, null));
        return column;
    }

    /// <summary>The readings in one style: rings side by side in a row, slim or tick rows stacked. <paramref name="setters"/>, when
    /// given, gets one action per reading that redraws it in place.</summary>
    private FrameworkElement Gauges(IReadOnlyList<DeskGaugeRead> reads, DeskMeterStyle style, double size, double width,
        HorizontalAlignment align, List<Action<DeskGaugeRead>>? setters)
    {
        if (style == DeskMeterStyle.Ring)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = align };
            for (int i = 0; i < reads.Count; i++)
            {
                var (ring, set) = Ring(reads[i], size);
                ring.Margin = new Thickness(i == 0 ? 0 : size * 0.6, 0, 0, 0);
                row.Children.Add(ring);
                setters?.Add(set);
            }
            return row;
        }
        var panel = new StackPanel { HorizontalAlignment = align };
        for (int i = 0; i < reads.Count; i++)
        {
            var (block, set) = style == DeskMeterStyle.Ticks ? TickRow(reads[i], size, width, i == 0) : SlimRow(reads[i], size, width, i == 0);
            panel.Children.Add(block);
            setters?.Add(set);
        }
        return panel;
    }


    /// <summary>The label and the value on one line, the value at the end (yellow or red past the alert thresholds).</summary>
    private (DockPanel Head, TextBlock Percent) Head(DeskGaugeRead r, double size)
    {
        var head = new DockPanel { LastChildFill = true };
        var pct = Text($"{r.Percent}%", size, 1);
        Signal(pct, r.Level);
        DockPanel.SetDock(pct, Dock.Right);
        head.Children.Add(pct);
        var name = Text(r.Label, size, 0.85);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Margin = new Thickness(0, 0, size * 0.5, 0);
        head.Children.Add(name);
        return (head, pct);
    }

    /// <summary>Slim: the label and the value on one line over a thin bar, about half a bar row's height; the pace mark crosses the bar.</summary>
    private (FrameworkElement, Action<DeskGaugeRead>) SlimRow(DeskGaugeRead r, double size, double width, bool first)
    {
        double text = size * 0.9;
        var block = new StackPanel { Width = width, Margin = new Thickness(0, first ? 0 : size * 0.4, 0, 0) };
        var (head, pct) = Head(r, text);
        block.Children.Add(head);
        double barH = Math.Max(2, Math.Round(size * 0.16));
        double markH = r.Pace is null ? barH : Math.Max(6, barH * 3);
        var bar = new Grid { Width = width, Height = markH, Margin = new Thickness(0, size * 0.1, 0, 0) };
        bar.Children.Add(new Rectangle
        {
            Height = barH, RadiusX = barH / 2, RadiusY = barH / 2, Fill = MeterTrack, Opacity = MeterTrackOpacity, VerticalAlignment = VerticalAlignment.Center,
        });
        var fill = new Rectangle
        {
            Width = Math.Round(width * Math.Clamp(r.Fill, 0, 1)), Height = barH, RadiusX = barH / 2, RadiusY = barH / 2,
            Fill = MeterFill(r.Level), Opacity = _ink.TextOpacity,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
        };
        bar.Children.Add(fill);
        if (r.Pace is { } p) bar.Children.Add(PaceMark(p, width, markH));
        block.Children.Add(bar);
        return (block, next =>
        {
            var t = $"{next.Percent}%";
            if (pct.Text != t) pct.Text = t;
            double w = Math.Round(width * Math.Clamp(next.Fill, 0, 1));
            if (Math.Abs(fill.Width - w) >= 1) fill.Width = w;
        });
    }

    /// <summary>The pace mark: a 2 px upright line at the pace's place along the meter.</summary>
    private Rectangle PaceMark(double pace, double width, double height) => new()
    {
        Width = 2, Height = height, Fill = PaceInk, Opacity = _ink.TextOpacity,
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(Math.Max(0, Math.Min(width - 2, pace * width - 1)), 0, 0, 0),
    };

    /// <summary>
    /// Ticks: the label and the value on one line over ten cells. A lit cell is filled; an unlit one is a faint outline, so the meter
    /// lights far fewer pixels than a bar (the OLED-friendly style). The pace mark stands across the cells.
    /// </summary>
    private (FrameworkElement, Action<DeskGaugeRead>) TickRow(DeskGaugeRead r, double size, double width, bool first)
    {
        double text = size * 0.9;
        var block = new StackPanel { Width = width, Margin = new Thickness(0, first ? 0 : size * 0.4, 0, 0) };
        var (head, pct) = Head(r, text);
        block.Children.Add(head);
        int n = DeskMeters.Ticks;
        double gap = Math.Max(2, Math.Round(size * 0.16));
        double cw = (width - (n - 1) * gap) / n;
        double ch = Math.Max(4, Math.Round(size * 0.42));
        double over = r.Pace is null ? 0 : Math.Max(2, Math.Round(size * 0.18));
        var canvas = new Canvas { Width = width, Height = ch + 2 * over, Margin = new Thickness(0, size * 0.14, 0, 0) };
        var cells = new Rectangle[n];
        for (int i = 0; i < n; i++)
        {
            cells[i] = new Rectangle { Width = cw, Height = ch, RadiusX = Math.Min(cw, ch) * 0.2, RadiusY = Math.Min(cw, ch) * 0.2 };
            Canvas.SetLeft(cells[i], i * (cw + gap));
            Canvas.SetTop(cells[i], over);
            canvas.Children.Add(cells[i]);
        }
        // A gradient ink runs along the row, a color per cell, so each cell is not a little copy of the whole gradient; past Warn at
        // the signal's stop runs along it too (theme match).
        Brush[] CellColors(MeterLevel level)
        {
            var stops = MeterSignal.Stops(MeterStops, level);
            bool spread = stops.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
            var solid = _frame.MeterStops is null ? Ink : SolidInk(stops[0]);
            return [.. Enumerable.Range(0, n).Select(i => spread ? SolidInk(NowPlayingLooks.ColorAt(stops, (double)i / (n - 1))) : solid)];
        }
        // Past Warn at the signal runs along the LIT cells, not the whole row: its peak sits on the middle lit cell and the rest blend
        // toward it from the ink's ends, so a low custom Warn at never leaves the yellow on an unlit cell.
        Brush[] LitColors(MeterLevel level, int on)
        {
            var stops = MeterSignal.Stops(MeterStops, level);
            int baseCount = MeterSignal.Stops(MeterStops, MeterLevel.Calm).Count;
            double peak = (double)((Math.Max(baseCount, 2) + 1) / 2) / (stops.Count - 1);   // where the signal sits along the stops
            int p = on / 2, right = on - 1 - p;
            return [.. Enumerable.Range(0, on).Select(i =>
            {
                double t = i <= p
                    ? peak * (1 - (double)(p - i) / (p + 1))
                    : peak + (1 - peak) * ((double)(i - p) / (right + 1));
                return SolidInk(NowPlayingLooks.ColorAt(stops, t));
            })];
        }
        var colors = CellColors(MeterLevel.Calm);
        void Light(DeskGaugeRead read)
        {
            int on = DeskMeters.LitTicks(read.Fill, n);
            var lit = read.Level == MeterLevel.Calm || on == 0 ? colors : LitColors(read.Level, on);
            for (int i = 0; i < n; i++)
            {
                if (i < on)
                {
                    cells[i].Fill = lit[i];
                    cells[i].Stroke = null;
                    cells[i].Opacity = _ink.TextOpacity;
                }
                else
                {
                    cells[i].Fill = null;
                    cells[i].Stroke = colors[i];
                    cells[i].StrokeThickness = 1;
                    cells[i].Opacity = 0.3;
                }
            }
        }
        Light(r);
        if (r.Pace is { } p)
        {
            var mark = new Rectangle { Width = 2, Height = ch + 2 * over, Fill = PaceInk, Opacity = _ink.TextOpacity };
            Canvas.SetLeft(mark, Math.Max(0, Math.Min(width - 2, p * width - 1)));
            canvas.Children.Add(mark);
        }
        block.Children.Add(canvas);
        int shown = DeskMeters.LitTicks(r.Fill, n);
        return (block, next =>
        {
            var t = $"{next.Percent}%";
            if (pct.Text != t) pct.Text = t;
            int on = DeskMeters.LitTicks(next.Fill, n);
            if (on != shown) { shown = on; Light(next); }
        });
    }

    /// <summary>Where a ring's arc starts (degrees clockwise from three o'clock: lower left) and how far it sweeps, leaving the bottom open.</summary>
    internal const double RingStart = 135, RingSweep = 270;

    /// <summary>
    /// Ring: a small arc gauge (Rainmeter's Roundline) with the value inside and the short name in its open bottom. The track is the
    /// ink at 22%, the arc the ink (with the signal's middle stop past the alert thresholds), and a Claude limit's pace mark crosses the ring.
    /// </summary>
    private (FrameworkElement, Action<DeskGaugeRead>) Ring(DeskGaugeRead r, double size)
    {
        double d = Math.Round(size * 3.4);
        double t = Math.Max(3, Math.Round(size * 0.26));
        double radius = d / 2 - t;
        var c = new Point(d / 2, d / 2);
        var face = new Grid { Width = d, Height = d };
        face.Children.Add(new Path
        {
            Data = Arc(c, radius, 1), Stroke = MeterTrack, StrokeThickness = t, Opacity = MeterTrackOpacity,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });
        var arc = new Path
        {
            Data = Arc(c, radius, r.Fill), Stroke = MeterFill(r.Level), StrokeThickness = t, Opacity = _ink.TextOpacity,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };
        face.Children.Add(arc);
        if (r.Pace is { } p)
        {
            double a = (RingStart + RingSweep * Math.Clamp(p, 0, 1)) * Math.PI / 180;
            double inner = radius - t * 0.95, outer = radius + t * 0.95;
            face.Children.Add(new Line
            {
                X1 = c.X + inner * Math.Cos(a), Y1 = c.Y + inner * Math.Sin(a),
                X2 = c.X + outer * Math.Cos(a), Y2 = c.Y + outer * Math.Sin(a),
                Stroke = PaceInk, StrokeThickness = 2, Opacity = _ink.TextOpacity,
            });
        }
        var value = Text($"{r.Percent}", size, 1, bold: true, alignment: TextAlignment.Center);
        Signal(value, r.Level);
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.VerticalAlignment = VerticalAlignment.Center;
        face.Children.Add(value);
        var label = Text(r.Short, size * 0.68, 0.8, alignment: TextAlignment.Center);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.MaxWidth = d + size * 0.5;
        // The name tucks up under the ring's open bottom (clear of the arc's ends), so a row of rings is barely taller than the rings.
        label.Margin = new Thickness(0, -size * 0.3, 0, 0);
        var ring = new StackPanel();
        ring.Children.Add(face);
        ring.Children.Add(label);
        System.Windows.Automation.AutomationProperties.SetName(ring, $"{r.Label} {r.Percent}%");
        return (ring, next =>
        {
            var text = $"{next.Percent}";
            if (value.Text != text) value.Text = text;
            arc.Data = Arc(c, radius, next.Fill);
        });
    }

    /// <summary>The ring's arc for a fill of 0 to 1 around <paramref name="c"/>; empty for nothing.</summary>
    internal static Geometry Arc(Point c, double radius, double fill)
    {
        double f = Math.Clamp(double.IsFinite(fill) ? fill : 0, 0, 1);
        if (f <= 0.001) return Geometry.Empty;
        double sweep = Math.Min(RingSweep * f, 359.9);
        double a0 = RingStart * Math.PI / 180, a1 = (RingStart + sweep) * Math.PI / 180;
        var start = new Point(c.X + radius * Math.Cos(a0), c.Y + radius * Math.Sin(a0));
        var end = new Point(c.X + radius * Math.Cos(a1), c.Y + radius * Math.Sin(a1));
        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, true));
        var g = new PathGeometry([figure]);
        g.Freeze();
        return g;
    }
}
