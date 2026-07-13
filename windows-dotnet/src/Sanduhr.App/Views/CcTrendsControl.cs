using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// Weekly-bars strip for Trends. Grammar (spec, binding): current week hatched
/// ("week in progress"); a zero-total week WITH a coverage gap gets the
/// "no record" texture, never a zero-height bar (a widget-off fortnight must
/// not read as a vacation); a covered zero week gets the 1px baseline tick; a
/// non-zero gap week gets its bar plus a textured underline. Sanduhr.Brush.*
/// palette only, pushed via SetData.
/// </summary>
public sealed class CcTrendsControl : FrameworkElement
{
    private IReadOnlyList<VaultWeek> _weeks = Array.Empty<VaultWeek>();
    private ThemePalette _palette = ThemePalette.Obsidian;

    public CcTrendsControl()
    {
        MinHeight = 120;
    }

    public void SetData(IReadOnlyList<VaultWeek> weeks, ThemePalette palette)
    {
        _weeks = weeks ?? Array.Empty<VaultWeek>();
        _palette = palette;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        if (_weeks.Count == 0)
        {
            DrawCenteredText(dc, "No vault history yet.", w, h, dpi);
            return;
        }

        long maxV = _weeks.Max(x => x.Total);
        const int gap = 4;
        double labelBand = 16;
        double baselineY = h - 6 - labelBand;
        double barArea = Math.Max(1, w - 4);
        double barW = Math.Max(4, (barArea - gap * (_weeks.Count - 1)) / _weeks.Count);

        var barBrush = Frozen(_palette.Accent);
        var tickBrush = Frozen(WithAlpha(_palette.TextDim, 80));
        var gapBrush = NoRecordBrush();
        var hatchBrush = HatchBrush();

        double x = 2;
        foreach (var week in _weeks)
        {
            if (week.Total == 0 && week.HasNoRecordGap)
            {
                // "No record" texture band — visibly not a zero.
                dc.DrawRectangle(gapBrush, null, new Rect(x, baselineY - 14, barW, 14));
            }
            else if (week.Total == 0)
            {
                dc.DrawRectangle(tickBrush, null, new Rect(x, baselineY, barW, 1));
            }
            else
            {
                double barH = maxV == 0 ? 0 : Math.Max(3, week.Total / (double)maxV * (baselineY - 10));
                var rect = new Rect(x, baselineY - barH, barW, barH);
                dc.DrawRectangle(week.IsCurrent ? hatchBrush : barBrush, null, rect);
                if (week.HasNoRecordGap)
                    dc.DrawRectangle(gapBrush, null, new Rect(x, baselineY + 2, barW, 4));
            }
            x += barW + gap;
        }

        // Sparse labels: first and current week starts.
        DrawLabel(dc, _weeks[0].WeekStart, 2, baselineY + 4, dpi, alignRight: false);
        DrawLabel(dc, _weeks[^1].WeekStart, w - 2, baselineY + 4, dpi, alignRight: true);
    }

    private void DrawLabel(DrawingContext dc, DateOnly day, double x, double y, double dpi, bool alignRight)
    {
        var ft = new FormattedText(
            day.ToString("MMM d", CultureInfo.InvariantCulture),
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9,
            Frozen(_palette.TextDim), dpi);

        if (alignRight)
        {
            // DrawText's origin is the layout box's TOP-LEFT; TextAlignment.Right
            // aligns within [origin.X, origin.X + MaxTextWidth]. Shift the origin
            // left by MaxTextWidth so the text's right edge lands exactly at x.
            ft.TextAlignment = TextAlignment.Right;
            ft.MaxTextWidth = 70;
            dc.DrawText(ft, new Point(x - ft.MaxTextWidth, y));
            return;
        }

        dc.DrawText(ft, new Point(x, y));
    }

    private void DrawCenteredText(DrawingContext dc, string text, double w, double h, double dpi)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, Frozen(_palette.TextDim), dpi)
        {
            TextAlignment = TextAlignment.Center,
            MaxTextWidth = w,
        };
        dc.DrawText(ft, new Point(0, (h - ft.Height) / 2));
    }

    /// <summary>Diagonal accent hatch — the "week in progress" fill.</summary>
    private Brush HatchBrush()
    {
        var pen = new Pen(Frozen(_palette.Accent), 2);
        var group = new DrawingGroup();
        using (var ctx = group.Open())
        {
            ctx.DrawRectangle(Frozen(WithAlpha(_palette.Accent, 50)), null, new Rect(0, 0, 8, 8));
            ctx.DrawLine(pen, new Point(0, 8), new Point(8, 0));
        }
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>Dotted TextDim texture — "no record", visually distinct from
    /// both bars and baseline ticks.</summary>
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
