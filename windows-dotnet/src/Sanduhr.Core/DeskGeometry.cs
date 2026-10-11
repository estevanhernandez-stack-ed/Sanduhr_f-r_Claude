namespace Sanduhr.Core;

/// <summary>Per-side distances (taskbar reach), in whatever unit the caller works in.</summary>
public readonly record struct DeskInsets(double Left, double Top, double Right, double Bottom)
{
    public static DeskInsets None => new(0, 0, 0, 0);

    /// <summary>The larger of each side.</summary>
    public static DeskInsets Max(DeskInsets a, DeskInsets b) =>
        new(Math.Max(a.Left, b.Left), Math.Max(a.Top, b.Top), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));

    /// <summary>Every side divided by <paramref name="divisor"/> (pixels to DIPs); a divisor of zero or less counts as 1.</summary>
    public DeskInsets Scaled(double divisor)
    {
        var d = divisor > 0 ? divisor : 1;
        return new(Left / d, Top / d, Right / d, Bottom / d);
    }
}

/// <summary>A rectangle with a top-left origin.</summary>
public readonly record struct DeskRect(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double MidX => X + Width / 2;
    public double MidY => Y + Height / 2;
}

/// <summary>The Desk outer margins (leading is left, trailing is right).</summary>
public readonly record struct DeskMargins(double Top, double Leading, double Bottom, double Trailing);

/// <summary>
/// The geometry the anchors share between the Desk and the Layout pane map, in DIPs, with no notch.
/// Ported from the Mac DeskAnchorGeometry.
/// </summary>
public static class DeskGeometry
{
    /// <summary>Room kept between the center column and each side column.</summary>
    public const double ColumnGap = 24;

    /// <summary>The saved margins plus the taskbar reach on each side, less the inner inset, never negative.</summary>
    public static DeskMargins Margins(double left, double right, double top, double bottom, DeskInsets taskbar, double inset) =>
        new(Top: Math.Max(0, top + taskbar.Top - inset),
            Leading: Math.Max(0, left + taskbar.Left - inset),
            Bottom: Math.Max(0, bottom + taskbar.Bottom - inset),
            Trailing: Math.Max(0, right + taskbar.Right - inset));

    /// <summary>The rectangle the stacks hang in, in the Desk window coordinates.</summary>
    public static DeskRect Content(double width, double height, DeskMargins m, double inset) =>
        new(m.Leading + inset, m.Top + inset,
            Math.Max(0, width - m.Leading - m.Trailing - inset * 2),
            Math.Max(0, height - m.Top - m.Bottom - inset * 2));

    /// <summary>
    /// How wide each column may draw when the centers have pieces: the center takes its own width
    /// (at most <paramref name="colWidth"/>), each side gets what is left on its half less
    /// <see cref="ColumnGap"/>. side + gap + center + gap + side never exceeds the total.
    /// </summary>
    public static (double Side, double Center) ColumnWidths(double total, double colWidth, double center)
    {
        var mid = Math.Max(0, Math.Min(Math.Min(center, colWidth), total - 2 * ColumnGap));
        var side = Math.Max(0, Math.Min(colWidth, (total - mid) / 2 - ColumnGap));
        return (side, mid);
    }

    /// <summary>The point an anchor stack hangs from: its outer corner or edge middle in <paramref name="content"/>.</summary>
    public static (double X, double Y) Point(DeskAnchor anchor, DeskRect content)
    {
        var x = DeskAnchors.Column(anchor) switch
        {
            DeskColumn.Left => content.Left,
            DeskColumn.Center => content.MidX,
            _ => content.Right,
        };
        var y = DeskAnchors.Row(anchor) switch
        {
            DeskRow.Top => content.Top,
            DeskRow.Middle => content.MidY,
            _ => content.Bottom,
        };
        return (x, y);
    }
}
