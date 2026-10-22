namespace Sanduhr.Core;

/// <summary>
/// Points between one monitor's DIPs and the virtual screen's physical pixels (spec, Desk pieces per monitor): what lets a drag that
/// started on one monitor's Arrange surface be read on another's, even where the two monitors have different scales.
/// </summary>
public static class DeskScreenMath
{
    /// <summary>A point in a monitor's DIPs (origin at the monitor's top left) as a point in physical pixels on the virtual screen.</summary>
    public static (double X, double Y) ToPixels(PxRect bounds, double scale, double dipX, double dipY) =>
        (bounds.X + dipX * Safe(scale), bounds.Y + dipY * Safe(scale));

    /// <summary>A point in physical pixels on the virtual screen as a point in a monitor's DIPs.</summary>
    public static (double X, double Y) ToDips(PxRect bounds, double scale, double px, double py) =>
        ((px - bounds.X) / Safe(scale), (py - bounds.Y) / Safe(scale));

    /// <summary>Whether a physical-pixel point is inside the monitor (left and top edges in, right and bottom out).</summary>
    public static bool Contains(PxRect bounds, double px, double py) =>
        px >= bounds.X && px < bounds.X + bounds.Width && py >= bounds.Y && py < bounds.Y + bounds.Height;

    private static double Safe(double scale) => double.IsFinite(scale) && scale > 0 ? scale : 1;
}
