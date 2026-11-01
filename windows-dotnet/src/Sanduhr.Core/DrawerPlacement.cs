namespace Sanduhr.Core;

/// <summary>Where a free drawer sits (drawers amendment 1): its monitor (a <see cref="DeskMonitors"/> word, "main" for the primary) and
/// its top-left's offset from that monitor's work area, in DIPs.</summary>
public sealed record DrawerPlace(string Monitor, double X, double Y);

/// <summary>Where drawers go when nobody has moved them, and how one is kept on its screen.</summary>
public static class DrawerPlacement
{
    public const double Width = 360;
    public const double OpenHeight = 330;
    public const double FoldedHeight = 42;
    public const double Margin = 24;
    public const double Gap = 12;

    public static double Height(bool folded) => folded ? FoldedHeight : OpenHeight;

    /// <summary>Places for every drawer in <paramref name="drawers"/> that has none. With no <paramref name="tidy"/>, no
    /// <paramref name="blocked"/> areas and no <paramref name="workHeight"/> it is today's rule: the top-right of the main monitor, each
    /// under the last, below any drawer already standing in that column. Otherwise each takes the next free Tidy slot for that corner and
    /// direction (default top right, column) on a work area <paramref name="workWidth"/> by <paramref name="workHeight"/> (pass the real
    /// height for a bottom corner), skipping <paramref name="blocked"/> (the icon area) and the drawers already placed on the main
    /// monitor. A drawer with no room anywhere arrives at the top right. Placed drawers keep theirs.</summary>
    public static IReadOnlyDictionary<string, DrawerPlace> Arrive(IReadOnlyList<DrawerDef> drawers, IReadOnlyDictionary<string, DrawerPlace> placed,
        double workWidth, IReadOnlyCollection<string> folded, DrawerTidy? tidy = null, double workHeight = double.PositiveInfinity,
        IReadOnlyList<DrawerRect>? blocked = null)
    {
        if (tidy is null && blocked is not { Count: > 0 } && double.IsPositiveInfinity(workHeight))
            return ArriveColumn(drawers, placed, workWidth, folded);
        var work = new DrawerRect(0, 0, workWidth, double.IsPositiveInfinity(workHeight) ? 100_000 : workHeight);
        var held = placed.Where(p => p.Value.Monitor == DeskMonitors.Main)
            .Select(p => new DrawerRect(p.Value.X, p.Value.Y, Width, Height(folded.Contains(p.Key))));
        var fresh = drawers.Where(d => !placed.ContainsKey(d.Id)).Select(d => (d.Id, H: Height(folded.Contains(d.Id)))).ToList();
        var slots = DrawerTidyPlan.Slots(fresh, work, tidy ?? DrawerTidy.Default, [.. blocked ?? [], .. held]);
        var places = new Dictionary<string, DrawerPlace>(placed, StringComparer.Ordinal);
        foreach (var (id, _) in fresh)
            places[id] = slots.TryGetValue(id, out var r) ? new DrawerPlace(DeskMonitors.Main, r.X, r.Y)
                : new DrawerPlace(DeskMonitors.Main, Math.Max(0, workWidth - Margin - Width), Margin);
        return places;
    }

    private static IReadOnlyDictionary<string, DrawerPlace> ArriveColumn(IReadOnlyList<DrawerDef> drawers, IReadOnlyDictionary<string, DrawerPlace> placed,
        double workWidth, IReadOnlyCollection<string> folded)
    {
        var places = new Dictionary<string, DrawerPlace>(placed, StringComparer.Ordinal);
        double x = Math.Max(0, workWidth - Margin - Width);
        double y = Margin;
        foreach (var (id, p) in placed)
            if (p.Monitor == DeskMonitors.Main && Math.Abs(p.X - x) < 1)
                y = Math.Max(y, p.Y + Height(folded.Contains(id)) + Gap);
        foreach (var d in drawers)
        {
            if (places.ContainsKey(d.Id)) continue;
            places[d.Id] = new DrawerPlace(DeskMonitors.Main, x, y);
            y += Height(folded.Contains(d.Id)) + Gap;
        }
        return places;
    }

    /// <summary>Keeps a drawer on its screen: its title bar stays inside the work area, so it can always be grabbed again.</summary>
    public static DrawerPlace Clamp(DrawerPlace p, double workWidth, double workHeight) => p with
    {
        X = Math.Clamp(p.X, 0, Math.Max(0, workWidth - Width)),
        Y = Math.Clamp(p.Y, 0, Math.Max(0, workHeight - FoldedHeight)),
    };
}
