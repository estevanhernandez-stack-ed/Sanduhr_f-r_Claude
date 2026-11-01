namespace Sanduhr.Core;

/// <summary>Where the desktop icons sit, as areas drawers keep clear of (look and placement spec, section 2).</summary>
public static class DrawerIconArea
{
    public static IReadOnlyList<DrawerRect> From(IReadOnlyList<DrawerRect> icons, double margin) =>
        [.. icons.Select(i => i.Inflate(margin))];

    /// <summary>When the icons can't be read: the left-hand columns Windows fills by default for <paramref name="itemCount"/> items.</summary>
    public static IReadOnlyList<DrawerRect> Fallback(DrawerRect work, int itemCount, double cellW, double cellH)
    {
        if (itemCount <= 0 || cellW <= 0 || cellH <= 0) return [];
        int rows = Math.Max(1, (int)Math.Floor(work.H / cellH));
        int cols = (itemCount + rows - 1) / rows;
        return [new DrawerRect(work.X, work.Y, Math.Min(work.W, cols * cellW), work.H)];
    }

    /// <summary><paramref name="r"/> (kept on screen the way <see cref="DrawerSnap.Keep"/> does) when it overlaps nothing blocked; else
    /// the nearest spot, kept on screen the same way, that overlaps nothing: each blocked area offers a position beside it on either
    /// axis, and the candidates are every combination of those x and y values (so an L-shaped area is cleared on both sides); else
    /// <paramref name="r"/> only kept on screen.</summary>
    public static DrawerRect NearestFree(DrawerRect r, DrawerRect work, IReadOnlyList<DrawerRect> blocked)
    {
        var kept = DrawerSnap.Keep(r, work);
        if (!blocked.Any(kept.Overlaps)) return kept;
        var xs = new HashSet<double> { kept.X };
        var ys = new HashSet<double> { kept.Y };
        foreach (var b in blocked)
        {
            xs.Add(b.Right + DrawerPlacement.Gap);
            xs.Add(b.X - DrawerPlacement.Gap - kept.W);
            ys.Add(b.Bottom + DrawerPlacement.Gap);
            ys.Add(b.Y - DrawerPlacement.Gap - kept.H);
        }
        var candidates = new HashSet<DrawerRect>();
        foreach (var x in xs)
            foreach (var y in ys)
                candidates.Add(DrawerSnap.Keep(kept.At(x, y), work));
        // Nearest first, and only the nearest few need the overlap test.
        foreach (var c in candidates.OrderBy(c => Math.Abs(c.X - kept.X) + Math.Abs(c.Y - kept.Y)))
            if (!blocked.Any(c.Overlaps)) return c;
        return kept;
    }
}
