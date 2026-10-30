namespace Sanduhr.Core;

/// <summary>
/// The drawers review bar's words (5.0 drawers spec, section 1): "14 files into 3 drawers", a line per drawer, the shared-items and
/// OneDrive lines, and after Apply the result line with Undo. Pure: the App's <c>DeskReviewBar</c> draws the model.
/// </summary>
public static class DrawerReview
{
    public const string BarName = "Review drawers";
    public const string ResultBarName = "Drawers result";
    public const string ChangesTitle = "Where files go";
    public const string NothingToSort = "Nothing on your Desktop matches a rule.";
    public const string FirstOnLine = "Drawer files live in the Drawers folder on your Desktop.";
    public const string Busy = "Finish the review on your Desk first.";
    public const string Undo = "Undo";
    public const string Done = "Done";
    public const string ApplyFailed = "The plan could not be applied. Nothing more was moved.";
    private const int NamedSkips = 3;

    public static string Title(int files, int drawers) =>
        $"{files} {(files == 1 ? "file" : "files")} into {drawers} {(drawers == 1 ? "drawer" : "drawers")}";

    /// <summary>The bar for a checked plan. <paramref name="existingIds"/> are the drawers the user already has (no "(new)").</summary>
    public static DeskReviewModel Model(DrawerPlan plan, DrawerPlanCheck check, IReadOnlyCollection<string> existingIds, string? note,
        IEnumerable<string> warnings)
    {
        var names = plan.Drawers.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
        var lines = check.Moves.GroupBy(m => m.DrawerId)
            .Select(g =>
            {
                var name = names.TryGetValue(g.Key, out var n) ? n : g.Key;
                var count = g.Count();
                return $"{name}{(existingIds.Contains(g.Key) ? "" : " (new)")}: {count} {(count == 1 ? "file" : "files")}";
            })
            .ToList();
        var all = new List<string>();
        if (SharedLine(check.Shared) is { } shared) all.Add(shared);
        all.AddRange(warnings);
        return new DeskReviewModel(Title(check.Moves.Count, lines.Count), note, lines, [], DeskOff: false, all)
        {
            Heading = plan.Source == DrawerPlanSource.Agent && !string.IsNullOrWhiteSpace(plan.Agent)
                ? $"{plan.Agent} suggests drawers" : "Sanduhr suggests drawers",
            ChangesTitle = ChangesTitle,
            BarName = BarName,
            CanEdit = true,
        };
    }

    /// <summary>The Public Desktop line (spec section 1): shortcuts when every item is one, items otherwise; null when there are none.</summary>
    public static string? SharedLine(IReadOnlyList<string> shared)
    {
        if (shared.Count == 0) return null;
        bool shortcuts = shared.All(p => Path.GetExtension(p).ToLowerInvariant() is ".lnk" or ".url");
        string noun = shortcuts ? (shared.Count == 1 ? "shortcut" : "shortcuts") : (shared.Count == 1 ? "item" : "items");
        return shared.Count == 1
            ? $"1 {noun} is shared with every user and needs an administrator to move."
            : $"{shared.Count} {noun} are shared with every user and need an administrator to move.";
    }

    /// <summary>"13 moved, 1 skipped: report.docx is open in Word." Names the first three skipped files, then counts the rest.</summary>
    public static string ResultLine(int moved, IReadOnlyList<DrawerSkipped> skipped)
    {
        if (skipped.Count == 0) return $"{moved} moved.";
        var named = string.Join(" ", skipped.Take(NamedSkips).Select(s => $"{s.Name} {s.Reason}"));
        var more = skipped.Count > NamedSkips ? $" And {skipped.Count - NamedSkips} more." : "";
        return $"{moved} moved, {skipped.Count} skipped: {named}{more}";
    }

    /// <summary>What turning off left behind: the folders kept because a file in them could not move (the root counts as one).</summary>
    public static string? KeptLine(int kept) => kept switch
    {
        <= 0 => null,
        1 => "1 drawer folder stays on your Desktop until its files can move. Turn drawers off again to finish.",
        _ => $"{kept} drawer folders stay on your Desktop until their files can move. Turn drawers off again to finish.",
    };

    /// <summary>The Drawers folder stayed although every drawer is back: it holds files that are not in a drawer, which turning off again
    /// cannot move.</summary>
    public const string RootKeptLine = "The Drawers folder stays on your Desktop: it holds files that are not in a drawer.";

    /// <summary>The bar after Apply: the result line, Undo (when anything moved) and Done.</summary>
    public static DeskReviewModel Result(string line, bool canUndo) =>
        new(line, null, [], [], DeskOff: false, [])
        {
            Heading = "Drawers",
            ChangesTitle = "",
            BarName = ResultBarName,
            ApplyLabel = Undo,
            DismissLabel = Done,
            ApplyOff = !canUndo,
            KeysLine = "Enter or Escape closes.",
            EnterDismisses = true,
        };
}

/// <summary>What Edit does to a plan before Apply (spec section 1): unticked moves drop, a new drawer can take another name (and the folder
/// of that name; its id stays, so its layout word does not change), and a drawer left with no moves drops.</summary>
public static class DrawerPlanEdit
{
    public static DrawerPlan Apply(DrawerPlan plan, IReadOnlySet<string> unticked, IReadOnlyDictionary<string, string> renames,
        IReadOnlyCollection<string> existingIds, string drawersRoot)
    {
        var moves = plan.Moves.Where(m => !unticked.Contains(m.Source)).ToArray();
        var used = moves.Select(m => m.DrawerId).ToHashSet(StringComparer.Ordinal);
        var drawers = plan.Drawers
            .Where(d => used.Contains(d.Id))
            .Select(d => !existingIds.Contains(d.Id) && renames.TryGetValue(d.Id, out var name) && DrawerNames.IsValid(name)
                ? d with { Name = name, Home = Path.Combine(drawersRoot, name) }
                : d)
            .ToArray();
        return plan with { Drawers = drawers, Moves = moves };
    }
}

/// <summary>The one OneDrive line (spec section 4): said when the Desktop and the Drawers root differ in whether they sync, naming both.</summary>
public static class DrawerSync
{
    public static string? Warning(string desktop, string root, IReadOnlyList<string> synced)
    {
        bool Synced(string path) => DrawerPaths.Normalize(path) is { } p
            && synced.Select(DrawerPaths.Normalize).OfType<string>().Any(s => DrawerPaths.IsInside(p, s));
        bool d = Synced(desktop), r = Synced(root);
        if (d == r) return null;
        return d
            ? $"Your Desktop syncs with OneDrive and {root} does not. Files moved into drawers there stop syncing."
            : $"{root} syncs with OneDrive and your Desktop does not.";
    }
}

/// <summary>How drawers a plan creates arrive: the first two open and the rest folded, so a six-drawer plan fits a 1080-pixel screen.
/// Where they arrive is <see cref="DrawerPlacement.Arrive"/>.</summary>
public static class DrawerArrival
{
    public const int OpenOnArrival = 2;

    public static IReadOnlyList<string> Folded(IReadOnlyList<DrawerDef> created) => [.. created.Skip(OpenOnArrival).Select(d => d.Id)];
}
