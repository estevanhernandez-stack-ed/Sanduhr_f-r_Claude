namespace Sanduhr.Core;

/// <summary>Where drawer plans may reach (5.0 drawers spec, section 1): the user's Desktop and drawer homes as sources, the Public
/// Desktop listed but never moved (writing it needs an administrator, and Sanduhr never elevates), and the Drawers root for new homes.</summary>
public sealed record DrawerRoots(string Desktop, string PublicDesktop, string DrawersRoot);

/// <summary>Path rules for drawers. Everything is normalized first, so case, forward slashes, "." and ".." cannot slip a path past a root.</summary>
public static class DrawerPaths
{
    /// <summary>The full, normalized form of a rooted local path with no trailing separator, or null for a relative, UNC or
    /// device (<c>\\?\</c>) path, or one with a colon past the drive letter (an NTFS stream such as <c>name::$INDEX_ALLOCATION</c>,
    /// which names a folder by another spelling), none of which a drawer plan may use.</summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var p = path.Replace('/', '\\');
        if (p.StartsWith(@"\\", StringComparison.Ordinal) || !Path.IsPathFullyQualified(p)) return null;
        if (p.IndexOf(':', 2) >= 0) return null;
        try { return Path.GetFullPath(p).TrimEnd('\\'); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    /// <summary>A Drawers root turning drawers off can safely empty and remove: not the Desktop, not a folder that holds it (a
    /// profile, a drive root), and not inside the Public Desktop.</summary>
    public static bool IsSafeRoot(string root, string desktop, string publicDesktop)
    {
        var r = Normalize(root);
        var d = Normalize(desktop);
        var p = Normalize(publicDesktop);
        if (r is null || d is null || p is null) return false;
        if (Path.GetPathRoot(r + "\\")?.TrimEnd('\\') is { } drive && string.Equals(r, drive, StringComparison.OrdinalIgnoreCase)) return false;
        return !IsInside(d, r) && !IsInside(r, p);
    }

    /// <summary>True when <paramref name="path"/> sits directly in <paramref name="dir"/>. Both must already be normalized.</summary>
    public static bool IsChildOf(string path, string dir) =>
        string.Equals(Path.GetDirectoryName(path), dir, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="path"/> is <paramref name="root"/> or anywhere under it. Both must already be normalized.</summary>
    public static bool IsInside(string path, string root) =>
        string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The check each move makes just before it runs, on <b>final</b> paths (junctions, links and 8.3 names resolved by the App), so a
    /// link swapped in after the plan was checked is caught (spec section 1). The source must sit directly on the Desktop or in a home;
    /// the target must be a home, or the Desktop when <paramref name="toDesktop"/> (undo and turning off). Null allows the move.
    /// </summary>
    public static string? MoveAllowed(string finalSource, string finalTargetDir, string finalDesktop, IReadOnlyList<string> finalHomes, bool toDesktop)
    {
        var source = Normalize(finalSource);
        var target = Normalize(finalTargetDir);
        var desktop = Normalize(finalDesktop);
        if (source is null || target is null || desktop is null) return DrawerSkip.Outside;
        var homes = finalHomes.Select(Normalize).OfType<string>().ToArray();
        bool fromOk = IsChildOf(source, desktop) || homes.Any(h => IsChildOf(source, h));
        bool toOk = homes.Any(h => string.Equals(h, target, StringComparison.OrdinalIgnoreCase))
            || (toDesktop && string.Equals(desktop, target, StringComparison.OrdinalIgnoreCase));
        // A source that is or holds a home would swallow a drawer.
        bool swallows = homes.Any(h => IsInside(h, source));
        return fromOk && toOk && !swallows ? null : DrawerSkip.Outside;
    }
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
        // A decoder bug or a hand-built plan must refuse, never throw (plan 1's final review, deferred minor).
        if (plan.Drawers is null || plan.Moves is null || plan.Drawers.Any(d => d is null) || plan.Moves.Any(m => m is null))
            return Refuse("A plan entry is empty.");
        if (plan.Source == DrawerPlanSource.Agent && (string.IsNullOrWhiteSpace(plan.Agent) || plan.Agent.Length > MaxAgentName))
            return Refuse("An agent plan needs the agent's name, at most 40 characters.");
        if (plan.Drawers.Count > MaxDrawers) return Refuse($"A plan can hold at most {MaxDrawers} drawers.");
        if (plan.Moves.Count > MaxMoves) return Refuse($"A plan can hold at most {MaxMoves} moves.");

        var desktop = DrawerPaths.Normalize(roots.Desktop);
        var shared = DrawerPaths.Normalize(roots.PublicDesktop);
        var root = DrawerPaths.Normalize(roots.DrawersRoot);
        if (desktop is null || shared is null || root is null) return Refuse("The drawer roots are not local folders.");
        if (!DrawerPaths.IsSafeRoot(root, desktop, shared)) return Refuse("The Drawers folder can't be the Desktop or a folder that holds it.");

        // First wins on a duplicated id (a hand-edited settings.json), as the rule engine does.
        var known = new Dictionary<string, DrawerDef>(StringComparer.Ordinal);
        foreach (var d in existing) known.TryAdd(d.Id, d);
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
            // A new drawer's folder is its own name in the root, so the name an agent shows is the folder it gets.
            if (!d.Owned || home is null || !string.Equals(home, Path.Combine(root, d.Name), StringComparison.OrdinalIgnoreCase))
                return Refuse($"The drawer \"{d.Name}\" must live in the Drawers folder, in a folder of its own name.");
            if (known.Values.Any(k => string.Equals(DrawerPaths.Normalize(k.Home), home, StringComparison.OrdinalIgnoreCase)))
                return Refuse($"Another drawer already uses the folder \"{d.Name}\".");
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
            // The root sits on the Desktop by default, so it (or a folder holding it) would otherwise pass as a desktop folder.
            if (DrawerPaths.IsInside(root, source))
                return Refuse($"\"{m.Source}\" is or holds the Drawers folder, which can't go into a drawer.");
            if (homes.Any(h => DrawerPaths.IsInside(h, source)))
                return Refuse($"\"{m.Source}\" is or holds a drawer's own folder.");
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
