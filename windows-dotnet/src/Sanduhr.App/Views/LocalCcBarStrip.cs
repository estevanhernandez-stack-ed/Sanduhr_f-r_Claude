using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Sanduhr.App.Theming;

namespace Sanduhr.App.Views;

/// <summary>
/// The 30-day daily token bar strip for the Local CC tab, ported from
/// <c>local_cc_dialog._DailyBarStrip.paintEvent</c>. One bar per day for the
/// trailing 30 days, height proportional to that day's token total vs the window
/// max; zero-usage days render as a 1px baseline tick so the strip still reads as
/// "30 days" instead of a few sparse events. Empty-state message when the whole
/// window is zero. Custom <see cref="OnRender"/> control; the host pushes data +
/// palette via <see cref="SetData"/>.
/// </summary>
public sealed class LocalCcBarStrip : FrameworkElement
{
    private const int Lookback = 30;

    private IReadOnlyDictionary<DateOnly, long> _byDay = new Dictionary<DateOnly, long>();
    private ThemePalette _palette = ThemePalette.Obsidian;

    public LocalCcBarStrip()
    {
        MinHeight = 60;
    }

    public void SetData(IReadOnlyDictionary<DateOnly, long> byDay, ThemePalette palette)
    {
        _byDay = byDay ?? new Dictionary<DateOnly, long>();
        _palette = palette;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;

        // Contiguous date list so zero days still render a baseline marker.
        var today = DateOnly.FromDateTime(DateTime.Now);
        var days = new List<DateOnly>(Lookback);
        for (int i = Lookback - 1; i >= 0; i--)
            days.Add(today.AddDays(-i));

        long maxV = 0;
        foreach (var v in _byDay.Values)
            if (v > maxV) maxV = v;

        if (maxV == 0)
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var brush = Frozen(_palette.TextDim);
            var ft = new FormattedText(
                "No local CC activity in the last 30 days.",
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, brush, dpi)
            {
                TextAlignment = TextAlignment.Center,
                MaxTextWidth = w,
            };
            dc.DrawText(ft, new Point(0, (h - ft.Height) / 2));
            return;
        }

        const int gap = 1;
        int barArea = Math.Max(1, (int)w - 4);
        int barW = Math.Max(2, (barArea - gap * (Lookback - 1)) / Lookback);
        double x = 2;
        double baselineY = h - 4;

        var barColor = Frozen(_palette.Accent);
        var baselineColor = Frozen(WithAlpha(_palette.TextDim, 80));

        foreach (var d in days)
        {
            long v = _byDay.GetValueOrDefault(d);
            if (v == 0)
            {
                dc.DrawRectangle(baselineColor, null, new Rect(x, baselineY, barW, 1));
            }
            else
            {
                double barH = Math.Max(2, v / (double)maxV * (h - 8));
                dc.DrawRectangle(barColor, null, new Rect(x, baselineY - barH, barW, barH));
            }
            x += barW + gap;
        }
    }

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
