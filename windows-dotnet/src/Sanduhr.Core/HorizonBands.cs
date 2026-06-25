namespace Sanduhr.Core;

/// <summary>One translucent bar of a horizon sparkline: a rectangle the App layer
/// fills with the sparkline color at <see cref="Alpha"/>.</summary>
public readonly record struct HorizonBar(double X, double Y, double Width, double Height, double Alpha);

/// <summary>
/// Pure geometry for the layered "horizon" sparkline. Inspired by the Heer/Tufte
/// horizon chart, but tuned for a usage widget where values are often low and
/// flat: the data is <b>normalized to its own [min, max]</b> (the same scale the
/// classic line uses, so the sparkline fills the height and reads as data rather
/// than a faint sliver), then folded into stacked translucent bands so peaks darken
/// and lulls fade. A small baseline floor keeps the lowest column visible. The App
/// layer draws each <see cref="HorizonBar"/> with the sparkline color at its alpha.
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

        int mn = int.MaxValue, mx = int.MinValue;
        foreach (var v in values)
        {
            if (v < mn) mn = v;
            if (v > mx) mx = v;
        }
        double rng = mx != mn ? mx - mn : 1; // flat data → uniform baseline, never invisible

        double bandFrac = 1.0 / bands;
        for (int band = 0; band < bands; band++)
        {
            double floorFrac = band * bandFrac;
            double ceilFrac = (band + 1) * bandFrac;
            double alpha = 0.32 + 0.16 * band; // 0.32 / 0.48 / 0.64 / 0.80 for 4 bands
            for (int i = 0; i < n; i++)
            {
                // Normalize to the data range, then lift into [0.12, 1.0] so the lowest
                // column still shows a visible baseline (flat data renders a thin band
                // rather than nothing).
                double frac = 0.12 + 0.88 * (values[i] - mn) / rng;
                if (frac <= floorFrac)
                    continue;
                double eff = Math.Min(frac, ceilFrac);
                double barH = Math.Max(1, eff * h);
                bars.Add(new HorizonBar(i * colW, h - barH, Math.Max(1, colW), barH, alpha));
            }
        }
        return bars;
    }
}
