using System.Windows;
using System.Windows.Media;
using Sanduhr.Core;

namespace Sanduhr.App.Controls;

/// <summary>
/// The classic smooth sparkline, ported from <c>sparkline.py</c>'s line mode.
/// Anti-aliased polyline over a transparent background so it sits on the card
/// glass. Horizon mode is item 9; line mode covers the milestone.
/// </summary>
public sealed class SparklineControl : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<int>), typeof(SparklineControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineColorProperty = DependencyProperty.Register(
        nameof(LineColor), typeof(Color), typeof(SparklineControl),
        new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<int>? Values
    {
        get => (IReadOnlyList<int>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Color LineColor
    {
        get => (Color)GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    // Named SparklineStyle, NOT Style — Style would shadow FrameworkElement.Style.
    public static readonly DependencyProperty SparklineStyleProperty = DependencyProperty.Register(
        nameof(SparklineStyle), typeof(SparklineStyle), typeof(SparklineControl),
        new FrameworkPropertyMetadata(SparklineStyle.Classic, FrameworkPropertyMetadataOptions.AffectsRender));

    public SparklineStyle SparklineStyle
    {
        get => (SparklineStyle)GetValue(SparklineStyleProperty);
        set => SetValue(SparklineStyleProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var values = Values;
        if (values is null || values.Count < 2)
            return;

        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 10 || h < 4)
            return;

        if (SparklineStyle == SparklineStyle.Horizon)
        {
            RenderHorizon(dc, values, w, h);
            return;
        }

        int mn = int.MaxValue, mx = int.MinValue;
        foreach (var v in values) { if (v < mn) mn = v; if (v > mx) mx = v; }
        double rng = mx != mn ? mx - mn : 1;

        var pen = new Pen(new SolidColorBrush(LineColor), 1.5)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            int denom = values.Count - 1;
            for (int i = 0; i < values.Count; i++)
            {
                double x = (double)i / denom * w;
                double y = h - (values[i] - mn) / rng * (h - 2) - 1;
                var p = new Point(x, y);
                if (i == 0) ctx.BeginFigure(p, false, false);
                else ctx.LineTo(p, true, true);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    /// <summary>Layered horizon mode — normalized, translucent bands (the geometry is
    /// the unit-tested <see cref="HorizonBands"/> in Core). Each bar is drawn in the
    /// sparkline color at the band's alpha; stacking darkens peaks, lulls fade.</summary>
    private void RenderHorizon(DrawingContext dc, IReadOnlyList<int> values, double w, double h)
    {
        var c = LineColor;
        foreach (var bar in HorizonBands.Compute(values, w, h))
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)(bar.Alpha * 255), c.R, c.G, c.B));
            brush.Freeze();
            dc.DrawRectangle(brush, null, new Rect(bar.X, bar.Y, bar.Width, bar.Height));
        }
    }
}
