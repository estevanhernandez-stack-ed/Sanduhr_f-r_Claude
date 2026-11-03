namespace Sanduhr.Core;

/// <summary>The mouse buttons the under-icons hook looks at; it passes every other event untouched.</summary>
public enum DeskButton { Left, Right }

/// <summary>What the hook does with a press (spec 5.0d, the hook).</summary>
public enum UnderIconsAct
{
    /// <summary>Leave the press to Explorer.</summary>
    Pass,
    /// <summary>Take the press and its release, and open the Desk menu.</summary>
    Menu,
    /// <summary>Take the press and its release, and act on the piece at the release.</summary>
    Click,
}

/// <summary>The answer for one press: the act, the island it lands on (its index in the snapshot) and the piece, when one is under it.</summary>
public readonly record struct UnderIconsDecision(UnderIconsAct Act, int Island = -1, DeskElementKind? Kind = null)
{
    public static UnderIconsDecision Pass { get; } = new(UnderIconsAct.Pass);
}

/// <summary>A clickable piece's click area in screen pixels (its frame and <see cref="DeskHitTest.Slack"/>).</summary>
public readonly record struct UnderIconsRegion(DeskElementKind Kind, PxRect Area);

/// <summary>
/// One ink island as the hook sees it, frozen at its last frame: its screen rect in physical pixels, the alpha of each of its pixels
/// (row-major, <see cref="Rect"/>'s width by height; empty before the first frame) and its clickable pieces.
/// </summary>
public sealed record UnderIconsIsland(PxRect Rect, ReadOnlyMemory<byte> Alpha, IReadOnlyList<UnderIconsRegion> Regions)
{
    /// <summary>The alpha at a screen point inside <see cref="Rect"/>; 0 outside it or before the first frame.</summary>
    public byte AlphaAt(PxPoint p)
    {
        int x = p.X - Rect.X, y = p.Y - Rect.Y;
        if (x < 0 || y < 0 || x >= Rect.Width || y >= Rect.Height) return 0;
        long i = (long)y * Rect.Width + x;
        return i < Alpha.Length ? Alpha.Span[(int)i] : (byte)0;
    }

    /// <summary>The clickable piece under a screen point, by <see cref="DeskHitTest.Priority"/>; null when none.</summary>
    public DeskElementKind? KindAt(PxPoint p)
    {
        foreach (var kind in DeskHitTest.Priority)
            foreach (var r in Regions)
                if (r.Kind == kind && Contains(r.Area, p)) return kind;
        return null;
    }

    internal static bool Contains(PxRect r, PxPoint p) =>
        r.Width > 0 && r.Height > 0 && p.X >= r.X && p.Y >= r.Y && p.X < r.X + r.Width && p.Y < r.Y + r.Height;
}

/// <summary>
/// The under-icons hook's decision (spec 5.0d): pure, so the table is tested and the hook thread runs it on frozen snapshots in well
/// under a millisecond. Rules, in order: not over the shell or over an icon, pass; a Desk menu open, pass (the press closes it); a right
/// press on a drawn pixel of an island, the Desk menu; a left press on a clickable piece, a click; anything else, pass.
/// </summary>
public static class UnderIconsClick
{
    /// <summary>The least alpha (of 255) that counts as a drawn pixel for the Desk menu. The faint click plates (alpha 1) do not count,
    /// so a right press on a piece's empty frame reaches the menu only through the piece's own click region.</summary>
    public const byte DrawnAlpha = 8;

    /// <param name="button">The button pressed.</param>
    /// <param name="point">The press, in physical screen pixels.</param>
    /// <param name="overShell">The root of the window under the point is the shell (Progman, or a WorkerW of the shell's process).</param>
    /// <param name="icons">The desktop icons' rects in physical screen pixels.</param>
    /// <param name="islands">The ink islands' snapshots; a later one draws over an earlier one.</param>
    /// <param name="menuOpen">A Desk menu is open now.</param>
    public static UnderIconsDecision Decide(DeskButton button, PxPoint point, bool overShell, IReadOnlyList<PxRect> icons,
        IReadOnlyList<UnderIconsIsland> islands, bool menuOpen)
    {
        if (!overShell || menuOpen) return UnderIconsDecision.Pass;
        foreach (var icon in icons)
            if (UnderIconsIsland.Contains(icon, point)) return UnderIconsDecision.Pass;
        for (int i = islands.Count - 1; i >= 0; i--)
        {
            var island = islands[i];
            if (!UnderIconsIsland.Contains(island.Rect, point)) continue;
            var kind = island.KindAt(point);
            if (button == DeskButton.Left)
            {
                if (kind is { } k && k != DeskElementKind.LauncherButton) return new(UnderIconsAct.Click, i, k);
                continue;
            }
            if (kind is not null || island.AlphaAt(point) >= DrawnAlpha)
                return new(UnderIconsAct.Menu, i, DeskHitTest.HasMenu(kind is { } m ? new DeskElement(m, null, default) : null) ? kind : null);
        }
        return UnderIconsDecision.Pass;
    }
}
