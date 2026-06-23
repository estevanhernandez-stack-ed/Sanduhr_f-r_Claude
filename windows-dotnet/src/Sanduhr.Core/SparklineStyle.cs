namespace Sanduhr.Core;

/// <summary>
/// Sparkline rendering style, cycled by the widget's Graph control. Ported from the
/// Python build's graph modes: <see cref="Classic"/> is the smooth min/max-normalized
/// line; <see cref="Horizon"/> is the Heer/Tufte absolute-scale horizon band chart.
/// </summary>
public enum SparklineStyle
{
    Classic,
    Horizon,
}
