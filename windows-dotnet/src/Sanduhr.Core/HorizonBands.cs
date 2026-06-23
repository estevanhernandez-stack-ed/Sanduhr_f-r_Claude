namespace Sanduhr.Core;

/// <summary>One translucent bar of a horizon sparkline: a rectangle the App layer
/// fills with the sparkline color at <see cref="Alpha"/>.</summary>
public readonly record struct HorizonBar(double X, double Y, double Width, double Height, double Alpha);

/// <summary>
/// Pure geometry for the Heer/Tufte horizon sparkline — ported from the Python
/// build's <c>_paint_horizon</c>. The values are absolute 0..100 utilization,
/// <b>NOT</b> min/max-normalized like the classic line; that distinction is the
/// whole point of the mode and the easiest porting bug, so the math lives here in
/// Core where it is unit-tested. Each band contributes one bar per column whose
/// value crosses into it; stacking the translucent bands (source-over) darkens
/// peaks. The App layer draws each <see cref="HorizonBar"/> with the sparkline
/// color at its alpha.
/// </summary>
public static class HorizonBands
{
    public const int DefaultBands = 4;

    public static IReadOnlyList<HorizonBar> Compute(IReadOnlyList<int> values, double w, double h, int bands = DefaultBands)
    {
        var bars = new List<HorizonBar>();
        if (values is null || values.Count < 2 || w < 1 || h < 1 || bands < 1)
            return bars;

        int n = values.Count;
        double colW = Math.Max(1, w / n);
        double bandSize = 100.0 / bands;

        for (int band = 0; band < bands; band++)
        {
            double bandFloor = band * bandSize;
            double bandCeil = (band + 1) * bandSize;
            double alpha = 0.18 + 0.20 * band; // 0.18 / 0.38 / 0.58 / 0.78 for 4 bands
            for (int i = 0; i < n; i++)
            {
                double v = values[i];
                if (v <= bandFloor)
                    continue;
                double eff = Math.Min(v, bandCeil);
                double barH = Math.Max(1, eff / 100.0 * h); // absolute scale, never min/max-normalized
                bars.Add(new HorizonBar(i * colW, h - barH, Math.Max(1, colW), barH, alpha));
            }
        }
        return bars;
    }
}
