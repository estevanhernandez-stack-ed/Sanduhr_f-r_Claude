using System.Windows;
using System.Windows.Media;

namespace Sanduhr.App.Controls;

/// <summary>
/// The utilization bar with an always-on pace-ghost tick, ported from the
/// Python <c>TierCard</c>'s <c>QProgressBar</c> + <c>_pace_tick</c>. Drawn in a
/// single <see cref="OnRender"/> pass: rounded track, color-ramped fill, then a
/// 2px ghost tick on top marking where pace says you "should" be. The tick is
/// painted last so it always composites above the fill (the Python build needed
/// a raised sibling widget to win that z-fight; a single render pass gets it for
/// free).
/// </summary>
public sealed class UsageBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(UsageBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillColorProperty = DependencyProperty.Register(
        nameof(FillColor), typeof(Color), typeof(UsageBar),
        new FrameworkPropertyMetadata(Colors.LimeGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackColorProperty = DependencyProperty.Register(
        nameof(TrackColor), typeof(Color), typeof(UsageBar),
        new FrameworkPropertyMetadata(Color.FromRgb(0x2a, 0x2a, 0x2a), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Pace fraction in [0,1]; <see cref="double.NaN"/> hides the tick.</summary>
    public static readonly DependencyProperty PaceFractionProperty = DependencyProperty.Register(
        nameof(PaceFraction), typeof(double), typeof(UsageBar),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PaceColorProperty = DependencyProperty.Register(
        nameof(PaceColor), typeof(Color), typeof(UsageBar),
        new FrameworkPropertyMetadata(Color.FromRgb(0xff, 0x6b, 0x6b), FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Color FillColor { get => (Color)GetValue(FillColorProperty); set => SetValue(FillColorProperty, value); }
    public Color TrackColor { get => (Color)GetValue(TrackColorProperty); set => SetValue(TrackColorProperty, value); }
    public double PaceFraction { get => (double)GetValue(PaceFractionProperty); set => SetValue(PaceFractionProperty, value); }
    public Color PaceColor { get => (Color)GetValue(PaceColorProperty); set => SetValue(PaceColorProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double radius = Math.Min(4, h / 2);
        var rect = new Rect(0, 0, w, h);

        var track = new SolidColorBrush(TrackColor); track.Freeze();
        dc.DrawRoundedRectangle(track, null, rect, radius, radius);

        double frac = Math.Clamp(Value / 100.0, 0, 1);
        if (frac > 0)
        {
            double fw = Math.Max(radius * 2, w * frac);
            var fill = new SolidColorBrush(FillColor); fill.Freeze();
            dc.PushClip(new System.Windows.Media.RectangleGeometry(rect, radius, radius));
            dc.DrawRoundedRectangle(fill, null, new Rect(0, 0, fw, h), radius, radius);
            dc.Pop();
        }

        double pace = PaceFraction;
        if (!double.IsNaN(pace))
        {
            double x = Math.Clamp(pace, 0, 1) * w;
            x = Math.Min(w - 2, Math.Max(0, x));
            var tick = new SolidColorBrush(PaceColor); tick.Freeze();
            dc.DrawRectangle(tick, null, new Rect(x, 0, 2, h));
        }
    }
}
