using System.Windows;
using System.Windows.Media.Animation;

namespace Sanduhr.App.Desk;

/// <summary>OLED care's dim, as one element's opacity: eased to a target, or snapped when there is no time to ease in.</summary>
internal static class CareOpacity
{
    /// <summary>
    /// Takes <paramref name="element"/>'s opacity to <paramref name="to"/> over <paramref name="ease"/>, from wherever it is now
    /// (a dim that is interrupted by an input turns round from its current level). A zero ease snaps. The animation lets go of
    /// the property when it ends, so the value left behind is the plain local one.
    /// </summary>
    public static void Ease(UIElement element, double to, TimeSpan ease)
    {
        double from = element.Opacity;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = to;
        if (ease <= TimeSpan.Zero || Math.Abs(from - to) < 0.001) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, to, ease)
        {
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        });
    }
}
