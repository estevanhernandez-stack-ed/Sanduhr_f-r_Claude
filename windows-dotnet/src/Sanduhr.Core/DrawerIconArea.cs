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

    /// <summary><paramref name="r"/> (kept on screen) when it overlaps nothing blocked; else the nearest spot beside a blocked area that
    /// stays on the work area and overlaps nothing; else <paramref name="r"/> only kept on screen.</summary>
    public static DrawerRect NearestFree(DrawerRect r, DrawerRect work, IReadOnlyList<DrawerRect> blocked)
    {
        var kept = DrawerSnap.Keep(r, work);
        if (!blocked.Any(kept.Overlaps)) return kept;
        var candidates = new List<DrawerRect>();
        foreach (var b in blocked)
        {
            candidates.Add(kept.At(b.Right + DrawerPlacement.Gap, kept.Y));
            candidates.Add(kept.At(b.X - DrawerPlacement.Gap - kept.W, kept.Y));
            candidates.Add(kept.At(kept.X, b.Bottom + DrawerPlacement.Gap));
            candidates.Add(kept.At(kept.X, b.Y - DrawerPlacement.Gap - kept.H));
        }
        var free = candidates
            .Where(c => c.X >= work.X && c.Right <= work.Right && c.Y >= work.Y && c.Bottom <= work.Bottom && !blocked.Any(c.Overlaps))
            .OrderBy(c => Math.Abs(c.X - kept.X) + Math.Abs(c.Y - kept.Y))
            .ToList();
        return free.Count > 0 ? free[0] : kept;
    }
}
