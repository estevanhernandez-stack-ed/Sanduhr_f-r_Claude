namespace Sanduhr.Core;

/// <summary>A rectangle in a monitor's work-area DIPs, the units <see cref="DrawerPlace"/> uses.</summary>
public readonly record struct DrawerRect(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public bool Overlaps(DrawerRect o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
    public DrawerRect At(double x, double y) => this with { X = x, Y = y };
    public DrawerRect Inflate(double d) => new(X - d, Y - d, W + 2 * d, H + 2 * d);
}

/// <summary>
/// Where a dragged drawer settles (look and placement spec, section 2): each axis on its own, the first rule with a
/// candidate within <see cref="Threshold"/> wins (other drawers, then Desk pieces, then the screen's margin), nearest
/// within it; with none, the 8 DIP grid. The title bar always stays on the work area.
/// </summary>
public static class DrawerSnap
{
    public const double Threshold = 12;
    public const double Grid = 8;

    public static DrawerRect Snap(DrawerRect moving, DrawerRect work, IReadOnlyList<DrawerRect> drawers, IReadOnlyList<DrawerRect> pieces)
    {
        double x = Axis(moving.X, moving.W, work.X, work.Right,
            drawers.Select(d => (d.X, d.Right)), pieces.Select(p => (p.X, p.Right)));
        double y = Axis(moving.Y, moving.H, work.Y, work.Bottom,
            drawers.Select(d => (d.Y, d.Bottom)), pieces.Select(p => (p.Y, p.Bottom)));
        return Keep(moving.At(x, y), work);
    }

    public static DrawerRect Keep(DrawerRect r, DrawerRect work) => r.At(
        Math.Clamp(r.X, work.X, Math.Max(work.X, work.Right - r.W)),
        Math.Clamp(r.Y, work.Y, Math.Max(work.Y, work.Bottom - DrawerPlacement.FoldedHeight)));

    private static double Axis(double start, double size, double lo, double hi,
        IEnumerable<(double Start, double End)> drawers, IEnumerable<(double Start, double End)> pieces)
    {
        foreach (var group in new[] { drawers, pieces })
            if (Nearest(start, Candidates(size, group)) is { } hit) return hit;
        if (Nearest(start, [lo + DrawerPlacement.Margin, hi - DrawerPlacement.Margin - size]) is { } edge) return edge;
        return lo + Math.Round((start - lo) / Grid) * Grid;
    }

    private static IEnumerable<double> Candidates(double size, IEnumerable<(double Start, double End)> others)
    {
        foreach (var (s, e) in others)
        {
            yield return s;                              // start edges align
            yield return e - size;                       // end edges align
            yield return e + DrawerPlacement.Gap;        // after it, at the gap
            yield return s - DrawerPlacement.Gap - size; // before it, at the gap
        }
    }

    private static double? Nearest(double at, IEnumerable<double> candidates)
    {
        double? best = null;
        foreach (var c in candidates)
            if (Math.Abs(c - at) <= Threshold && (best is null || Math.Abs(c - at) < Math.Abs(best.Value - at))) best = c;
        return best;
    }
}
