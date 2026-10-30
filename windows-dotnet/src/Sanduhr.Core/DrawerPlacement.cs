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

    /// <summary>Places for every drawer in <paramref name="drawers"/> that has none: the top-right of the main monitor, away from where
    /// Windows lines up desktop icons, each under the last, below any drawer already standing in that column. Placed drawers keep theirs.</summary>
    public static IReadOnlyDictionary<string, DrawerPlace> Arrive(IReadOnlyList<DrawerDef> drawers, IReadOnlyDictionary<string, DrawerPlace> placed,
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
