namespace Sanduhr.Core;

/// <summary>
/// Where a window opens so it fits its monitor's work area (Settings v2, "Short windows"; WF41). Pure, in DIPs: at most
/// the designed size, at least the minimum, centred, and never above or left of the work area, so the window's foot and
/// Quit stay on screen whenever the minimum fits.
/// </summary>
public static class WindowFit
{
    /// <summary>A rectangle in DIPs.</summary>
    public readonly record struct Area(double Left, double Top, double Width, double Height);

    public static Area Fit(Area work, double width, double height, double minWidth, double minHeight)
    {
        double w = Math.Max(minWidth, Math.Min(width, work.Width));
        double h = Math.Max(minHeight, Math.Min(height, work.Height));
        double left = work.Left + Math.Max(0, (work.Width - w) / 2);
        double top = work.Top + Math.Max(0, (work.Height - h) / 2);
        return new Area(left, top, w, h);
    }
}
