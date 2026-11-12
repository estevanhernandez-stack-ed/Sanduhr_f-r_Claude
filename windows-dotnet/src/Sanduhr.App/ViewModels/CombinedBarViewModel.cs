using System.Windows.Media;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// The combined bar for one window (agent layer, spec section 3): the primary source's percent in its color, the furthest source
/// past it continuing the bar in its own color, every other source a tick. Built by the widget for each window two or more
/// sources report. A stale source draws at 0.55 opacity.
/// </summary>
public sealed class CombinedBarViewModel
{
    public const double StaleOpacity = 0.55;

    public CombinedBarViewModel(string windowLabel, SourceBarLayout layout, string accessibleName,
        Brush primaryBrush, Brush? beyondBrush, IReadOnlyList<(double At, Brush Brush, bool Stale, string Tooltip)> ticks,
        string primaryTooltip = "", string beyondTooltip = "")
    {
        PrimaryTooltip = primaryTooltip;
        BeyondTooltip = beyondTooltip;
        WindowLabel = windowLabel;
        Layout = layout;
        AccessibleName = accessibleName;
        PrimaryBrush = primaryBrush;
        BeyondBrush = beyondBrush;
        Ticks = ticks;
    }

    public string WindowLabel { get; }

    /// <summary>"Claude session, 40%." and, for a stale source, " As of 3:42 PM."</summary>
    public string PrimaryTooltip { get; }

    public string BeyondTooltip { get; }

    public SourceBarLayout Layout { get; }

    public string AccessibleName { get; }

    public Brush PrimaryBrush { get; }

    public Brush? BeyondBrush { get; }

    public IReadOnlyList<(double At, Brush Brush, bool Stale, string Tooltip)> Ticks { get; }

    /// <summary>The bar's list item reads as its sentence, never as the type behind it.</summary>
    public override string ToString() => AccessibleName;

    /// <summary>What changes the drawing: two bars with the same signature draw the same, so the widget keeps the one it has.</summary>
    internal string Signature =>
        string.Join("|", WindowLabel, AccessibleName, Layout.Primary, Layout.Beyond, BrushKey(PrimaryBrush), BrushKey(BeyondBrush),
            string.Join(",", Ticks.Select(t => $"{t.At:0.####}:{BrushKey(t.Brush)}")));

    private static string BrushKey(Brush? b) => b is SolidColorBrush s ? $"{s.Color}@{s.Opacity:0.##}" : "";

    /// <summary>A frozen solid brush, dimmed when <paramref name="stale"/> (the same color at <see cref="StaleOpacity"/>).</summary>
    internal static Brush BrushFor(string hex, bool stale)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        var brush = new SolidColorBrush(color) { Opacity = stale ? StaleOpacity : 1 };
        brush.Freeze();
        return brush;
    }
}
