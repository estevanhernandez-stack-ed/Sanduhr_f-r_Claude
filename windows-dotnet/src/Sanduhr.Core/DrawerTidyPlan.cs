namespace Sanduhr.Core;

/// <summary>Tidy drawers (look and placement spec, section 2): slots from a corner, a column or a row at a time, wrapping toward
/// the centre and stepping past the icon area. A drawer that fits nowhere is left out and keeps its place.</summary>
public static class DrawerTidyPlan
{
    /// <summary>The ids ordered by how near each drawer's corner-side corner is to the work area's corner; ties by id.</summary>
    public static IReadOnlyList<string> Order(IReadOnlyDictionary<string, DrawerRect> current, DrawerRect work, DrawerCorner corner)
    {
        var (cx, cy) = Point(work, corner);
        return [.. current
            .Select(p => (p.Key, Dist: Math.Abs(Near(p.Value, corner).X - cx) + Math.Abs(Near(p.Value, corner).Y - cy)))
            .OrderBy(p => p.Dist).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key)];
    }

    /// <summary>A slot for each drawer in <paramref name="ordered"/> (width <see cref="DrawerPlacement.Width"/>, the given height), from
    /// <paramref name="tidy"/>'s corner at the Desk margin and the drawer gap, never overlapping <paramref name="blocked"/> or each other.
    /// A drawer with no room anywhere is absent from the result.</summary>
    public static IReadOnlyDictionary<string, DrawerRect> Slots(IReadOnlyList<(string Id, double H)> ordered, DrawerRect work,
        DrawerTidy tidy, IReadOnlyList<DrawerRect> blocked)
    {
        bool left = tidy.Corner is DrawerCorner.TopLeft or DrawerCorner.BottomLeft;
        bool top = tidy.Corner is DrawerCorner.TopLeft or DrawerCorner.TopRight;
        bool column = tidy.Flow == DrawerFlow.Column;
        double m = DrawerPlacement.Margin, g = DrawerPlacement.Gap, w = DrawerPlacement.Width;
        const double Eps = 0.5;
        double minX = work.X + m - Eps, maxX = work.Right - m + Eps, minY = work.Y + m - Eps, maxY = work.Bottom - m + Eps;

        double lineX = left ? work.X + m : work.Right - m - w;   // a column's x (a row's slots run along x instead)
        double lineY = top ? work.Y + m : work.Bottom - m;       // a row's edge nearest the corner (a column runs along y instead)
        double rowStart = left ? work.X + m : work.Right - m;    // where a row begins along x
        double cursor = column ? lineY : rowStart;               // the next position along the current line
        double lane = 0;                                         // the tallest drawer placed in the current row
        double edge = double.NaN;                                // the nearest obstacle edge stepped past in the current row
        var placed = new List<DrawerRect>(blocked);
        var slots = new Dictionary<string, DrawerRect>(StringComparer.Ordinal);

        foreach (var (id, h) in ordered)
        {
            // Each pass steps past one obstacle, wraps one line or places the drawer. Stepping always moves the cursor strictly
            // past the obstacle (so it cannot meet it again on that line) and wrapping always moves a line toward the centre until
            // it runs out, so this ends; the bound is a backstop that leaves the drawer out rather than ever spinning.
            double lineSpan = column ? work.W / (w + g) : work.H / g;
            var (sx, sy, sc, sl, se) = (lineX, lineY, cursor, lane, edge);   // restored if this drawer is left out
            long guard = (placed.Count + 2L) * ((long)Math.Min(lineSpan, 100_000) + 2);
            for (; guard > 0; guard--)
            {
                DrawerRect r = column
                    ? new DrawerRect(lineX, top ? cursor : cursor - h, w, h)
                    : new DrawerRect(left ? cursor : cursor - w, top ? lineY : lineY - h, w, h);
                bool inside = r.X >= minX && r.Right <= maxX && r.Y >= minY && r.Bottom <= maxY;
                if (!inside)
                {
                    // The line is full: wrap toward the centre, and give up once there is no line left.
                    if (column)
                    {
                        lineX += left ? w + g : -(w + g);
                        cursor = lineY;
                        if (lineX < minX || lineX + w > maxX) break;
                    }
                    else
                    {
                        // Next row: past the tallest drawer here; with none placed, past the nearest obstacle this row stepped over
                        // (else past a row of this drawer's height).
                        lineY = lane > 0 ? lineY + (top ? lane + g : -(lane + g))
                            : !double.IsNaN(edge) ? (top ? edge + g : edge - g)
                            : lineY + (top ? h + g : -(h + g));
                        cursor = rowStart;
                        lane = 0;
                        edge = double.NaN;
                        if (lineY < minY || lineY > maxY) break;
                    }
                    continue;
                }
                int hit = placed.FindIndex(r.Overlaps);
                if (hit >= 0)
                {
                    // Step past whatever is in the way along the line.
                    var o = placed[hit];
                    cursor = column ? (top ? o.Bottom + g : o.Y - g) : (left ? o.Right + g : o.X - g);
                    if (!column)
                        edge = double.IsNaN(edge) ? (top ? o.Bottom : o.Y) : (top ? Math.Min(edge, o.Bottom) : Math.Max(edge, o.Y));
                    continue;
                }
                slots[id] = r;
                placed.Add(r);
                if (column) cursor = top ? r.Bottom + g : r.Y - g;
                else { cursor = left ? r.Right + g : r.X - g; lane = Math.Max(lane, h); }
                break;
            }
            // Left out: the next drawer starts from where this one did, not from the end of the lines this one ran off.
            if (!slots.ContainsKey(id)) (lineX, lineY, cursor, lane, edge) = (sx, sy, sc, sl, se);
        }
        return slots;
    }

    private static (double X, double Y) Point(DrawerRect work, DrawerCorner c) => c switch
    {
        DrawerCorner.TopLeft => (work.X, work.Y),
        DrawerCorner.TopRight => (work.Right, work.Y),
        DrawerCorner.BottomLeft => (work.X, work.Bottom),
        _ => (work.Right, work.Bottom),
    };

    private static (double X, double Y) Near(DrawerRect r, DrawerCorner c) => c switch
    {
        DrawerCorner.TopLeft => (r.X, r.Y),
        DrawerCorner.TopRight => (r.Right, r.Y),
        DrawerCorner.BottomLeft => (r.X, r.Bottom),
        _ => (r.Right, r.Bottom),
    };
}
