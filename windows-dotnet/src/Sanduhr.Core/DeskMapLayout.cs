namespace Sanduhr.Core;

/// <summary>A monitor's rectangle on the layout map, in map DIPs.</summary>
public sealed record MapRect(double X, double Y, double Width, double Height);

/// <summary>
/// Where each monitor sits on Settings, Desk's layout map (4.0.1): every connected monitor at its place in Windows' display
/// arrangement, shifted so the leftmost and topmost start at zero, and scaled together to fit <see cref="MaxWidth"/> by
/// <see cref="MaxHeight"/>. One monitor comes out <see cref="MaxHeight"/> tall, as the single-screen map always was.
/// </summary>
public static class DeskMapLayout
{
    /// <summary>The map's height cap (the single-screen map's height).</summary>
    public const double MaxHeight = 132;

    /// <summary>The map's width cap, so two or three monitors side by side still fit the Settings page.</summary>
    public const double MaxWidth = 440;

    /// <summary>The map rectangles, in the order given. Bounds are physical pixels in the virtual desktop.</summary>
    public static IReadOnlyList<MapRect> Fit(IReadOnlyList<PxRect> monitors, double maxWidth = MaxWidth, double maxHeight = MaxHeight)
    {
        if (monitors.Count == 0) return [];
        int left = monitors.Min(m => m.X), top = monitors.Min(m => m.Y);
        double width = Math.Max(1, monitors.Max(m => m.Right) - left);
        double height = Math.Max(1, monitors.Max(m => m.Bottom) - top);
        double k = Math.Min(maxHeight / height, maxWidth / width);
        return [.. monitors.Select(m => new MapRect((m.X - left) * k, (m.Y - top) * k, m.Width * k, m.Height * k))];
    }
}
