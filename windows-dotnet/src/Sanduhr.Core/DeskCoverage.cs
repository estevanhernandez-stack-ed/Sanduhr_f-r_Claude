namespace Sanduhr.Core;

/// <summary>
/// What the Desk's coverage watch reads about one window above an island (S7, 7.1): plain values, so
/// the rule is testable. <paramref name="Frame"/> is the DWM extended frame (no invisible resize
/// borders), in virtual-screen pixels.
/// </summary>
public readonly record struct WindowFacts(
    bool Visible,
    bool Minimized,
    bool Cloaked,
    bool ExTransparent,
    bool ExLayered,
    bool IsDeskWindow,
    PxRect Frame);

/// <summary>Whether a Desk island is hidden behind other windows (S7, 7.1).</summary>
public static class DeskCoverage
{
    /// <summary>
    /// True only when nothing of <paramref name="target"/> is left after every rectangle in
    /// <paramref name="above"/> is cut away. An empty target is never covered; an occluder that only
    /// touches an edge covers nothing.
    /// </summary>
    public static bool FullyCovered(PxRect target, IEnumerable<PxRect> above)
    {
        if (target.IsEmpty) return false;
        var pieces = new List<PxRect> { target };
        var next = new List<PxRect>();
        foreach (var o in above)
        {
            if (o.IsEmpty) continue;
            next.Clear();
            foreach (var p in pieces) Subtract(p, o, next);
            (pieces, next) = (next, pieces);
            if (pieces.Count == 0) return true;
        }
        return pieces.Count == 0;
    }

    /// <summary>Adds the parts of <paramref name="p"/> outside <paramref name="o"/> to <paramref name="into"/>.</summary>
    private static void Subtract(PxRect p, PxRect o, List<PxRect> into)
    {
        int ix1 = Math.Max(p.X, o.X), iy1 = Math.Max(p.Y, o.Y);
        int ix2 = Math.Min(p.Right, o.Right), iy2 = Math.Min(p.Bottom, o.Bottom);
        if (ix1 >= ix2 || iy1 >= iy2) { into.Add(p); return; }
        if (iy1 > p.Y) into.Add(new PxRect(p.X, p.Y, p.Width, iy1 - p.Y));
        if (iy2 < p.Bottom) into.Add(new PxRect(p.X, iy2, p.Width, p.Bottom - iy2));
        if (ix1 > p.X) into.Add(new PxRect(p.X, iy1, ix1 - p.X, iy2 - iy1));
        if (ix2 < p.Right) into.Add(new PxRect(ix2, iy1, p.Right - ix2, iy2 - iy1));
    }

    /// <summary>
    /// Whether a window above the island counts as covering it: visible, not minimized, not cloaked
    /// (another virtual desktop, a suspended app), not click-through (<c>WS_EX_TRANSPARENT</c>), not
    /// layered (it may be see-through, and counting it could pause something still on screen, the
    /// wrong way to fail), not one of Sanduhr's own Desk windows, and with a non-empty frame.
    /// </summary>
    public static bool Occluder(WindowFacts f) =>
        f.Visible && !f.Minimized && !f.Cloaked && !f.ExTransparent && !f.ExLayered
        && !f.IsDeskWindow && !f.Frame.IsEmpty;
}

/// <summary>What a covered island does about it (S7, 7.1).</summary>
public static class DeskPauseRule
{
    /// <summary>Paused when covered, except while arranging: arranging never pauses.</summary>
    public static bool Paused(bool covered, bool arranging) => covered && !arranging;
}
