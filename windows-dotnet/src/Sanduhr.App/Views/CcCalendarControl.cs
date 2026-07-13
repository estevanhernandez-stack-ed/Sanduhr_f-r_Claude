using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// Rolling 5-week calendar under the Overview bar strip: Monday-start weekday
/// columns, the last 35 local days ending today. Heat = accent alpha in four
/// perceptual steps (any nonzero day stays visibly nonzero); covered zero days
/// get a faint tick dot; uncovered days get the dotted no-record texture
/// (never a blank that reads as zero). Today wears a 1px accent outline.
/// Hover shows "{MMM d} — {compact}" via mouse-move hit-testing.
/// </summary>
public sealed class CcCalendarControl : FrameworkElement
{
    private const int DaysBack = 34;   // 35 days inclusive of today
    private const int Rows = 5;
    private const int Cols = 7;
    private const double HeaderBand = 14;

    private IReadOnlyDictionary<DateOnly, long> _byDay = new Dictionary<DateOnly, long>();
    private IReadOnlySet<DateOnly> _uncovered = new HashSet<DateOnly>();
    private ThemePalette _palette = ThemePalette.Obsidian;

    public CcCalendarControl()
    {
        MinHeight = 96;
    }

    public void SetData(
        IReadOnlyDictionary<DateOnly, long> byDay,
        IReadOnlySet<DateOnly> uncovered,
        ThemePalette palette)
    {
        _byDay = byDay ?? new Dictionary<DateOnly, long>();
        _uncovered = uncovered ?? new HashSet<DateOnly>();
        _palette = palette;
        InvalidateVisual();
        UpdateTooltip(Mouse.GetPosition(this));
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var gridTop = HeaderBand;
        double cellW = (w - (Cols - 1) * 2) / Cols;
        double cellH = (h - gridTop - (Rows - 1) * 2) / Rows;

        // Weekday header (Monday-start initials).
        string[] initials = { "M", "T", "W", "T", "F", "S", "S" };
        var dim = Frozen(_palette.TextDim);
        for (int c = 0; c < Cols; c++)
        {
            var ft = new FormattedText(initials[c], CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, dim, dpi)
            {
                TextAlignment = TextAlignment.Center,
                MaxTextWidth = cellW,
            };
            dc.DrawText(ft, new Point(c * (cellW + 2), 0));
        }

        long maxV = 0;
        foreach (var v in _byDay.Values)
            if (v > maxV) maxV = v;

        var gapBrush = NoRecordBrush();
        var tickBrush = Frozen(WithAlpha(_palette.TextDim, 80));
        var outlinePen = new Pen(Frozen(_palette.Accent), 1);
        outlinePen.Freeze();

        foreach (var (day, rect) in Cells(today, w, h))
        {
            if (day > today)
                continue;
            if (_uncovered.Contains(day))
            {
                dc.DrawRectangle(gapBrush, null, rect);
                continue;
            }
            long v = _byDay.GetValueOrDefault(day);
            if (v == 0)
            {
                dc.DrawRectangle(tickBrush, null,
                    new Rect(rect.X + rect.Width / 2 - 1, rect.Y + rect.Height / 2 - 1, 2, 2));
            }
            else
            {
                // Four perceptual steps of accent — quartiles of the window max.
                byte alpha = v >= maxV * 0.75 ? (byte)230
                           : v >= maxV * 0.50 ? (byte)170
                           : v >= maxV * 0.25 ? (byte)110
                           : (byte)60;
                dc.DrawRectangle(Frozen(WithAlpha(_palette.Accent, alpha)), null, rect);
            }
            if (day == today)
                dc.DrawRectangle(null, outlinePen, rect);
        }
    }

    /// <summary>Cell geometry: 5 rows x 7 Monday-start columns, bottom row ends
    /// at today's week; the top row leads with empty cells before day 1.</summary>
    private IEnumerable<(DateOnly Day, Rect Rect)> Cells(DateOnly today, double w, double h)
    {
        double cellW = (w - (Cols - 1) * 2) / Cols;
        double cellH = (h - HeaderBand - (Rows - 1) * 2) / Rows;
        var first = today.AddDays(-DaysBack);
        // Monday-align the grid start (may precede `first`; those cells skip).
        int firstDow = ((int)first.DayOfWeek + 6) % 7;
        var gridStart = first.AddDays(-firstDow);
        for (int i = 0; i < Rows * Cols; i++)
        {
            var day = gridStart.AddDays(i);
            if (day < first)
                continue;
            int row = i / Cols, col = i % Cols;
            yield return (day, new Rect(
                col * (cellW + 2), HeaderBand + row * (cellH + 2), cellW, cellH));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateTooltip(e.GetPosition(this));
    }

    private void UpdateTooltip(Point p)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        foreach (var (day, rect) in Cells(today, ActualWidth, ActualHeight))
        {
            if (day <= today && rect.Contains(p))
            {
                long v = _byDay.GetValueOrDefault(day);
                ToolTip = _uncovered.Contains(day)
                    ? $"{day.ToString("MMM d", CultureInfo.InvariantCulture)} — no record"
                    : $"{day.ToString("MMM d", CultureInfo.InvariantCulture)} — {TokenFormat.Compact(v)} tokens";
                return;
            }
        }
        ToolTip = null;
    }

    /// <summary>Dotted TextDim texture — identical recipe to CcTrendsControl's
    /// "no record" brush so the two surfaces speak one language.</summary>
    private Brush NoRecordBrush()
    {
        var dot = Frozen(WithAlpha(_palette.TextDim, 110));
        var group = new DrawingGroup();
        using (var ctx = group.Open())
        {
            ctx.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 6, 6));
            ctx.DrawRectangle(dot, null, new Rect(2, 2, 2, 2));
        }
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 6, 6),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
