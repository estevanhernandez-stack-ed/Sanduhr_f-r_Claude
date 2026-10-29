namespace Sanduhr.Core;

/// <summary>What turning drawers off does: the moves back to the Desktop, and the owned homes (then the root) to remove once each is empty.</summary>
public sealed record ReturnPlan(IReadOnlyList<UndoStep> Moves, IReadOnlyList<string> HomesToRemove);

/// <summary>
/// Turning drawers off puts everything back (5.0 drawers spec, section 4): every home Sanduhr owns empties onto the Desktop, files and
/// folders alike, whether the journal moved them or the user dragged them in. A home counts as owned only when it is flagged owned and
/// sits directly in the Drawers root. A view drawer (Downloads, a project folder) keeps its contents where they are. The App removes a
/// home only once it is empty, so a file that could not move keeps its home.
/// </summary>
public static class DrawerReturn
{
    public static ReturnPlan Plan(IReadOnlyList<DrawerDef> drawers, string drawersRoot, string desktop,
        Func<string, IEnumerable<DesktopItem>> childrenOf, Func<string, bool> exists)
    {
        var root = DrawerPaths.Normalize(drawersRoot);
        var desk = DrawerPaths.Normalize(desktop);
        var moves = new List<UndoStep>();
        var homes = new List<string>();
        if (root is null || desk is null) return new ReturnPlan(moves, homes);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in drawers)
        {
            var home = DrawerPaths.Normalize(d.Home);
            if (!d.Owned || home is null || !DrawerPaths.IsChildOf(home, root)) continue;
            foreach (var item in childrenOf(home).OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
            {
                var to = DrawerNames.Free(desk, Path.GetFileName(item.Path), item.IsFolder, p => exists(p) || claimed.Contains(p));
                claimed.Add(to);
                moves.Add(new UndoStep(item.Path, to));
            }
            homes.Add(home);
        }
        // The root last: the App removes each folder only once it is empty, so a root holding anything else stays.
        if (homes.Count > 0) homes.Add(root);
        return new ReturnPlan(moves, homes);
    }

    /// <summary>The confirmation's question, with the count.</summary>
    public static string Prompt(int count) => count == 1 ? "Put 1 file back on the Desktop?" : $"Put {count} files back on the Desktop?";
}
