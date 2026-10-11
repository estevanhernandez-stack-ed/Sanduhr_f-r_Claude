namespace Sanduhr.Core;

/// <summary>How a sparkline is actually drawn once the data is considered.</summary>
public enum SparklineDraw
{
    Classic,
    Horizon,
    Level,
}

/// <summary>
/// Steady-data rule for the Horizon style: readings that barely move collapse into a
/// single level line instead of a flat band smear. Ported from the Mac SparklineView.
/// </summary>
public static class SparklineSteady
{
    /// <summary>A spread (max - min) strictly below this counts as steady.</summary>
    public const double SteadySpread = 2;

    public static SparklineDraw Drawn(SparklineStyle style, IReadOnlyList<double> values)
    {
        if (style != SparklineStyle.Horizon)
            return SparklineDraw.Classic;
        if (values.Count == 0)
            return SparklineDraw.Horizon;

        double mn = double.MaxValue, mx = double.MinValue;
        foreach (var v in values)
        {
            if (v < mn) mn = v;
            if (v > mx) mx = v;
        }
        return mx - mn < SteadySpread ? SparklineDraw.Level : SparklineDraw.Horizon;
    }

    /// <summary>Y of the level line on the same 0-100 scale as the horizon chart.</summary>
    public static double LevelY(IReadOnlyList<double> values, double height)
    {
        double sum = 0;
        foreach (var v in values) sum += v;
        double level = Math.Clamp(sum / values.Count, 0, 100);
        return height - level / 100 * (height - 2) - 1;
    }
}
