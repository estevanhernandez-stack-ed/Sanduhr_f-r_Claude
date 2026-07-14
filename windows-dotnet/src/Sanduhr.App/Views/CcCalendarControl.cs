using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// Rolling 5-week calendar under the Overview bar strip: Monday-start weekday
/// columns, the 5 Monday-start weeks ending with today's week — 29-35 past
/// days depending on weekday. Heat = accent alpha in four perceptual steps
/// (any nonzero day stays visibly nonzero); covered zero days get a faint
/// tick dot; uncovered days get the dotted no-record texture (never a blank
/// that reads as zero). Days before the feed's window start render fully
/// empty — genuinely feed-less, not merely uncovered. Today wears a 1px
/// accent outline. Hover shows "{MMM d} — {compact}" via mouse-move hit-testing.
/// </summary>
public sealed class CcCalendarControl : FrameworkElement
{
    private const int Rows = 5;
    private const int Cols = 7;
    private const double HeaderBand = 14;

    private IReadOnlyDictionary<DateOnly, long> _byDay = new Dictionary<DateOnly, long>();
    private IReadOnlySet<DateOnly> _uncovered = new HashSet<DateOnly>();
    private DateOnly _windowStart = DateOnly.MinValue;
    private ThemePalette _palette = ThemePalette.Obsidian;

    /// <summary>Raised when a covered, clickable day cell is left-clicked — the
    /// owning window bridges this to the Ledger's day scope.</summary>
    public event Action<DateOnly>? DayClicked;

    public CcCalendarControl()
    {
        MinHeight = 96;
    }

    public void SetData(
        IReadOnlyDictionary<DateOnly, long> byDay,
        IReadOnlySet<DateOnly> uncovered,
        DateOnly windowStart,
        ThemePalette palette)
    {
        _byDay = byDay ?? new Dictionary<DateOnly, long>();
        _uncovered = uncovered ?? new HashSet<DateOnly>();
        _windowStart = windowStart;
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
        double cellW = (w - (Cols - 1) * 2) / Cols;

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
            if (day > today || day < _windowStart)
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

    /// <summary>Cell geometry: 5 rows x 7 Monday-start columns, end-anchored so
    /// the bottom row is ALWAYS today's Monday-start week. Grid start is 4 full
    /// weeks before the Monday of today's week, so: the last yielded day is at
    /// most 6 days after today (trailing future days of the current week —
    /// callers skip those via `day &gt; today`), and every yielded day is
    /// &gt;= today-34. That's always within the vault's calendar feed (which
    /// starts at today-34 too); on the live/degraded path the feed only knows
    /// the last 30 days, so callers also skip `day &lt; windowStart` — those
    /// leading cells are genuinely feed-less, not merely uncovered.</summary>
    private IEnumerable<(DateOnly Day, Rect Rect)> Cells(DateOnly today, double w, double h)
    {
        double cellW = (w - (Cols - 1) * 2) / Cols;
        double cellH = (h - HeaderBand - (Rows - 1) * 2) / Rows;
        int todayDow = ((int)today.DayOfWeek + 6) % 7;          // Monday = 0
        var gridStart = today.AddDays(-todayDow).AddDays(-28);  // 4 weeks before this week's Monday
        for (int i = 0; i < Rows * Cols; i++)
        {
            var day = gridStart.AddDays(i);
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

    /// <summary>Hit-tests via the SAME Cells() geometry + guards as the
    /// tooltip — uncovered days are still clickable (the ledger honestly shows
    /// empty for them), only the window/feed bounds gate the click.</summary>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var p = e.GetPosition(this);
        foreach (var (day, rect) in Cells(today, ActualWidth, ActualHeight))
        {
            if (day <= today && day >= _windowStart && rect.Contains(p))
            {
                DayClicked?.Invoke(day);
                return;
            }
        }
    }

    private void UpdateTooltip(Point p)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        foreach (var (day, rect) in Cells(today, ActualWidth, ActualHeight))
        {
            if (day <= today && day >= _windowStart && rect.Contains(p))
            {
                long v = _byDay.GetValueOrDefault(day);
                ToolTip = _uncovered.Contains(day)
                    ? $"{day.ToString("MMM d", CultureInfo.InvariantCulture)} — no record"
                    : $"{day.ToString("MMM d", CultureInfo.InvariantCulture)} — {TokenFormat.Compact(v)} tokens";
                Cursor = Cursors.Hand;
                return;
            }
        }
        ToolTip = null;
        Cursor = null;
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
