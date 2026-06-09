using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>One drawable line for a tier row: a color plus its history points.
/// In single-account mode the color is the theme accent; in all-accounts mode it
/// is the per-account palette color.</summary>
public sealed record ChartSeries(Color Color, IReadOnlyList<HistoryPoint> Points);

/// <summary>One tier row in the chart: its display label and the series to overlay
/// (one in single-account mode, one per account in all-accounts mode).</summary>
public sealed record ChartRow(string TierLabel, IReadOnlyList<ChartSeries> Series);

/// <summary>
/// The 30-day (or 7-day) per-tier usage chart, ported from <c>history_chart.py</c>'s
/// <c>HistoryChart.paintEvent</c>. One mini line+area chart per tier stacked
/// vertically, sharing a single time x-axis at the bottom. Single-account mode
/// draws one accent line per tier with a right-edge "X% left / resets-in" state
/// label; all-accounts mode overlays one palette-colored line per account (no
/// state label). Reference gridlines at 25/50/75/100%, tier labels in the left
/// gutter, time-axis ticks + "older / now" anchors along the bottom.
///
/// A custom <see cref="OnRender"/> control (same pattern as
/// <see cref="Controls.SparklineControl"/> / <see cref="Controls.UsageBar"/>); the
/// host pushes data + the active <see cref="ThemePalette"/> via <see cref="SetData"/>.
/// </summary>
public sealed class HistoryChart : FrameworkElement
{
    private IReadOnlyList<ChartRow> _rows = Array.Empty<ChartRow>();
    private bool _weekMode;
    private bool _aggregate;
    private ThemePalette _palette = ThemePalette.Obsidian;

    public HistoryChart()
    {
        MinHeight = 280;
    }

    /// <summary>Push the prepared rows + render flags + palette and repaint.
    /// <paramref name="aggregate"/> true = all-accounts overlay (no right-edge
    /// state label); false = single account (accent line + state label).</summary>
    public void SetData(IReadOnlyList<ChartRow> rows, bool weekMode, bool aggregate, ThemePalette palette)
    {
        _rows = rows ?? Array.Empty<ChartRow>();
        _weekMode = weekMode;
        _aggregate = aggregate;
        _palette = palette;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI");
        const double FontSize = 11;

        var textSecondary = Frozen(_palette.TextSecondary);
        var textDim = Frozen(_palette.TextDim);

        // Stable left-gutter width from the FULL canonical label set (parity with
        // Python computing label_w over all _TIER_LABELS, not just rows present),
        // so the gutter doesn't jitter as tiers gain/lose data.
        double labelW = 0;
        foreach (var key in TierModel.CanonicalOrder)
            labelW = Math.Max(labelW, MeasureWidth(TierModel.Label(key), typeface, FontSize, dpi));
        labelW += 12;

        double valueW = Math.Max(
            MeasureWidth("100% left", typeface, FontSize, dpi),
            MeasureWidth("30d 23h 59m", typeface, FontSize, dpi)) + 8;

        if (w < 180 || h < 40)
            return;

        if (_rows.Count == 0)
        {
            var ft = MakeText("No history yet.\nData accumulates as you use Sanduhr.",
                typeface, FontSize, textDim, dpi);
            ft.TextAlignment = TextAlignment.Center;
            ft.MaxTextWidth = w;
            dc.DrawText(ft, new Point(0, (h - ft.Height) / 2));
            return;
        }

        const double axisH = 22;
        double rowsH = h - axisH;
        int n = _rows.Count;
        double rowH = rowsH / n;
        double chartX = labelW + 8;
        double chartW = w - chartX - valueW - 8;
        if (chartW < 8)
            chartW = 8;

        var gridColor = WithAlpha(_palette.TextDim, 60);
        var gridPen = new Pen(Frozen(gridColor), 1.0) { DashStyle = DashStyles.Dot };
        gridPen.Freeze();
        int[] gridPcts = { 25, 50, 75, 100 };

        for (int idx = 0; idx < _rows.Count; idx++)
        {
            var row = _rows[idx];
            double yTop = idx * rowH;
            double yBottom = (idx + 1) * rowH - 4;

            // Tier label (left gutter, vertically centered in the row).
            var lblFt = MakeText(row.TierLabel, typeface, FontSize, textSecondary, dpi);
            dc.DrawText(lblFt, new Point(8, yTop + Math.Max(0, (rowH - 4 - lblFt.Height) / 2)));

            // Reference gridlines, drawn behind the fills/lines.
            foreach (var pct in gridPcts)
            {
                double gy = yBottom - pct / 100.0 * (rowH - 8);
                dc.DrawLine(gridPen, new Point(chartX, gy), new Point(chartX + chartW, gy));
            }

            (Color color, HistoryPoint last)? mostRecent = null;

            foreach (var series in row.Series)
            {
                var points = series.Points;
                if (points.Count < 2)
                    continue;

                var color = series.Color;
                int denom = points.Count - 1;
                double firstX = chartX, lastX = chartX, lastY = yBottom;

                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    for (int i = 0; i < points.Count; i++)
                    {
                        double x = chartX + (double)i / denom * chartW;
                        int util = Math.Clamp(points[i].V, 0, 100);
                        double y = yBottom - util / 100.0 * (rowH - 8);
                        if (i == 0) { ctx.BeginFigure(new Point(x, y), false, false); firstX = x; }
                        else ctx.LineTo(new Point(x, y), true, true);
                        lastX = x; lastY = y;
                    }
                }
                geo.Freeze();

                // Area fill under the line (alpha 40) for visual mass.
                var fillGeo = new StreamGeometry();
                using (var ctx = fillGeo.Open())
                {
                    for (int i = 0; i < points.Count; i++)
                    {
                        double x = chartX + (double)i / denom * chartW;
                        int util = Math.Clamp(points[i].V, 0, 100);
                        double y = yBottom - util / 100.0 * (rowH - 8);
                        if (i == 0) ctx.BeginFigure(new Point(x, y), true, true);
                        else ctx.LineTo(new Point(x, y), true, true);
                    }
                    ctx.LineTo(new Point(lastX, yBottom), true, true);
                    ctx.LineTo(new Point(firstX, yBottom), true, true);
                }
                fillGeo.Freeze();
                dc.DrawGeometry(Frozen(WithAlpha(color, 40)), null, fillGeo);

                // Stroke the line on top of the fill.
                var linePen = new Pen(Frozen(color), 2.0)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round,
                };
                linePen.Freeze();
                dc.DrawGeometry(null, linePen, geo);

                if (!_aggregate)
                    mostRecent = (color, points[^1]);
            }

            // Right-edge state label — single-account mode only.
            if (mostRecent is { } mr)
            {
                var valueColor = Frozen(mr.color);
                int util = Math.Clamp(mr.last.V, 0, 100);
                var resetsAt = mr.last.ResetsAt;
                double labelX = chartX + chartW + 4;
                double labelWidth = valueW - 4;

                if (!string.IsNullOrEmpty(resetsAt))
                {
                    double hPerLine = (rowH - 4) / 2;
                    // "{100-util}% left" — bottom-aligned in the upper half.
                    var pctFt = MakeText($"{100 - util}% left", typeface, FontSize, valueColor, dpi);
                    pctFt.TextAlignment = TextAlignment.Right;
                    pctFt.MaxTextWidth = labelWidth;
                    dc.DrawText(pctFt, new Point(labelX, yTop + Math.Max(0, hPerLine - pctFt.Height)));
                    // time-until — top-aligned in the lower half.
                    var timeFt = MakeText(Pacing.TimeUntil(resetsAt), typeface, FontSize, valueColor, dpi);
                    timeFt.TextAlignment = TextAlignment.Right;
                    timeFt.MaxTextWidth = labelWidth;
                    dc.DrawText(timeFt, new Point(labelX, yTop + hPerLine));
                }
                else
                {
                    var pctFt = MakeText($"{util}%", typeface, FontSize, valueColor, dpi);
                    pctFt.TextAlignment = TextAlignment.Right;
                    pctFt.MaxTextWidth = labelWidth;
                    dc.DrawText(pctFt, new Point(labelX, yTop + Math.Max(0, (rowH - 4 - pctFt.Height) / 2)));
                }
            }
        }

        // Shared time-axis ticks + "older / now" anchors along the bottom.
        int tickCount = _weekMode ? 7 : 4;
        double tickY = rowsH - 1;
        var axisPen = new Pen(Frozen(WithAlpha(_palette.TextDim, 140)), 1.0);
        axisPen.Freeze();
        for (int i = 0; i <= tickCount; i++)
        {
            double x = chartX + (double)i / tickCount * chartW;
            dc.DrawLine(axisPen, new Point(x, tickY), new Point(x, tickY + 4));
        }

        double anchorY = tickY + 5;
        var olderFt = MakeText(_weekMode ? "7 days ago" : "30 days ago", typeface, FontSize, textDim, dpi);
        olderFt.MaxTextWidth = Math.Max(1, chartW / 2);
        olderFt.TextAlignment = TextAlignment.Left;
        dc.DrawText(olderFt, new Point(chartX, anchorY));

        var nowFt = MakeText("now", typeface, FontSize, textDim, dpi);
        nowFt.MaxTextWidth = Math.Max(1, chartW / 2);
        nowFt.TextAlignment = TextAlignment.Right;
        dc.DrawText(nowFt, new Point(chartX + chartW / 2, anchorY));
    }

    private static double MeasureWidth(string text, Typeface tf, double size, double dpi)
        => new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            tf, size, Brushes.Black, dpi).WidthIncludingTrailingWhitespace;

    private static FormattedText MakeText(string text, Typeface tf, double size, Brush brush, double dpi)
        => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, size, brush, dpi);

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
