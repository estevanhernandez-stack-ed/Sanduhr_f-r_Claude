namespace Sanduhr.Core;

/// <summary>Where drawer plans may reach (5.0 drawers spec, section 1): the user's Desktop and drawer homes as sources, the Public
/// Desktop listed but never moved (writing it needs an administrator, and Sanduhr never elevates), and the Drawers root for new homes.</summary>
public sealed record DrawerRoots(string Desktop, string PublicDesktop, string DrawersRoot);

/// <summary>Path rules for drawers. Everything is normalized first, so case, forward slashes, "." and ".." cannot slip a path past a root.</summary>
public static class DrawerPaths
{
    /// <summary>The full, normalized form of a rooted local path with no trailing separator, or null for a relative, UNC or
    /// device (<c>\\?\</c>) path, none of which a drawer plan may use.</summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var p = path.Replace('/', '\\');
        if (p.StartsWith(@"\\", StringComparison.Ordinal) || !Path.IsPathFullyQualified(p)) return null;
        try { return Path.GetFullPath(p).TrimEnd('\\'); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    /// <summary>True when <paramref name="path"/> sits directly in <paramref name="dir"/>. Both must already be normalized.</summary>
    public static bool IsChildOf(string path, string dir) =>
        string.Equals(Path.GetDirectoryName(path), dir, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="path"/> is <paramref name="root"/> or anywhere under it. Both must already be normalized.</summary>
    public static bool IsInside(string path, string root) =>
        string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A checked plan: when <see cref="Ok"/>, the drawers to create and the moves to run (no-ops dropped) and the shared
/// Public Desktop items the review names; otherwise one error and nothing to run.</summary>
public sealed record DrawerPlanCheck(bool Ok, string? Error, IReadOnlyList<DrawerDef> Drawers, IReadOnlyList<DrawerMove> Moves,
    IReadOnlyList<string> Shared);

/// <summary>
/// Checks a move plan however it arrived (5.0 drawers spec, section 1). Any entry outside the roots refuses the whole plan, so a
/// half-good plan never runs half. The App checks each path again just before its move, with the file system in hand, for reparse
/// points; this is the pure half.
/// </summary>
public static class DrawerPlanValidator
{
    public const int MaxDrawers = 24;
    public const int MaxMoves = 2000;
    public const int MaxAgentName = 40;

    public static DrawerPlanCheck Check(DrawerPlan plan, DrawerRoots roots, IReadOnlyList<DrawerDef> existing)
    {
        if (plan.Source == DrawerPlanSource.Agent && (string.IsNullOrWhiteSpace(plan.Agent) || plan.Agent.Length > MaxAgentName))
            return Refuse("An agent plan needs the agent's name, at most 40 characters.");
        if (plan.Drawers.Count > MaxDrawers) return Refuse($"A plan can hold at most {MaxDrawers} drawers.");
        if (plan.Moves.Count > MaxMoves) return Refuse($"A plan can hold at most {MaxMoves} moves.");

        var desktop = DrawerPaths.Normalize(roots.Desktop);
        var shared = DrawerPaths.Normalize(roots.PublicDesktop);
        var root = DrawerPaths.Normalize(roots.DrawersRoot);
        if (desktop is null || shared is null || root is null) return Refuse("The drawer roots are not local folders.");

        var known = existing.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var created = new List<DrawerDef>();
        foreach (var d in plan.Drawers)
        {
            if (!DrawerIds.IsValid(d.Id)) return Refuse($"\"{d.Id}\" is not a drawer id.");
            if (!DrawerNames.IsValid(d.Name)) return Refuse($"\"{d.Name}\" can't be a folder name.");
            var home = DrawerPaths.Normalize(d.Home);
            if (known.TryGetValue(d.Id, out var had))
            {
                if (!string.Equals(home, DrawerPaths.Normalize(had.Home), StringComparison.OrdinalIgnoreCase))
                    return Refuse($"The drawer \"{d.Name}\" already has a home.");
                continue;
            }
            if (!d.Owned || home is null || !DrawerPaths.IsChildOf(home, root))
                return Refuse($"The drawer \"{d.Name}\" must live directly in the Drawers folder.");
            var made = d with { Home = home };
            known[d.Id] = made;
            created.Add(made);
        }

        var homes = known.Values.Select(d => DrawerPaths.Normalize(d.Home)).OfType<string>().ToArray();
        var moves = new List<DrawerMove>();
        var sharedItems = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in plan.Moves)
        {
            if (!known.TryGetValue(m.DrawerId, out var target)) return Refuse($"There is no drawer \"{m.DrawerId}\".");
            var source = DrawerPaths.Normalize(m.Source);
            if (source is null) return Refuse($"\"{m.Source}\" is outside the Desktop and the drawers.");
            if (!seen.Add(source)) return Refuse($"\"{m.Source}\" is in the plan twice.");
            if (DrawerPaths.IsChildOf(source, shared)) { sharedItems.Add(source); continue; }
            // The root sits on the Desktop by default, so it would otherwise pass as a desktop folder.
            if (string.Equals(source, root, StringComparison.OrdinalIgnoreCase))
                return Refuse("The Drawers folder itself can't go into a drawer.");
            if (homes.Any(h => string.Equals(h, source, StringComparison.OrdinalIgnoreCase)))
                return Refuse($"\"{m.Source}\" is a drawer's own folder.");
            bool fromDesktop = DrawerPaths.IsChildOf(source, desktop);
            bool fromDrawer = homes.Any(h => DrawerPaths.IsChildOf(source, h));
            if (!fromDesktop && !fromDrawer) return Refuse($"\"{m.Source}\" is outside the Desktop and the drawers.");
            var targetHome = DrawerPaths.Normalize(target.Home);
            if (targetHome is not null && DrawerPaths.IsChildOf(source, targetHome)) continue; // already there
            moves.Add(new DrawerMove(source, m.DrawerId));
        }
        return new DrawerPlanCheck(true, null, created, moves, sharedItems);
    }

    private static DrawerPlanCheck Refuse(string error) => new(false, error, [], [], []);
}
