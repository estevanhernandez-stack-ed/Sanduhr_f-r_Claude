namespace Sanduhr.Core;

/// <summary>What a built-in rule looks at (5.0 drawers spec, section 3): a kind (by extension, screenshots by name too), age, or the name.</summary>
public enum DrawerRuleKind { Screenshots, Images, Documents, Installers, Archives, Shortcuts, OlderThan, NameContains, NameStartsWith }

/// <summary>One rule: a filter and the drawer it fills. <see cref="Text"/> is for the name rules, <see cref="Days"/> for the age rule.</summary>
public sealed record DrawerRule(DrawerRuleKind Kind, string DrawerId, bool On = true, string? Text = null, int Days = 30);

/// <summary>
/// The built-in sorter for people with no agent (5.0 drawers spec, section 3). Rules run top to bottom and the first match wins;
/// whatever no rule takes stays on the Desktop, with no Misc bucket. Rules match files only, never folders.
/// </summary>
public static class DrawerRules
{
    private static readonly HashSet<string> ImageExt = Set(".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff", ".svg");
    private static readonly HashSet<string> DocumentExt = Set(".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".rtf", ".odt", ".ods", ".odp", ".csv");
    private static readonly HashSet<string> InstallerExt = Set(".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle");
    private static readonly HashSet<string> ArchiveExt = Set(".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".iso");
    private static readonly HashSet<string> ShortcutExt = Set(".lnk", ".url");

    /// <summary>The built-in set, in order. Shortcuts start off (most desktops keep their app shortcuts on purpose); old files go last,
    /// so they take only what the kind rules left.</summary>
    public static IReadOnlyList<DrawerRule> Defaults { get; } =
    [
        new(DrawerRuleKind.Screenshots, "screenshots"),
        new(DrawerRuleKind.Images, "images"),
        new(DrawerRuleKind.Documents, "documents"),
        new(DrawerRuleKind.Installers, "installers"),
        new(DrawerRuleKind.Archives, "archives"),
        new(DrawerRuleKind.Shortcuts, "shortcuts", On: false),
        new(DrawerRuleKind.OlderThan, "older-files", Days: 30),
    ];

    /// <summary>The drawers the defaults fill, each owned and directly in <paramref name="drawersRoot"/>.</summary>
    public static IReadOnlyList<DrawerDef> DefaultDrawers(string drawersRoot) =>
        new[] { "Screenshots", "Images", "Documents", "Installers", "Archives", "Shortcuts", "Older files" }
            .Select(name => new DrawerDef(DrawerIds.From(name), name, Path.Combine(drawersRoot, name), true))
            .ToArray();

    public static bool Matches(DrawerRule rule, DesktopItem item, DateTime nowUtc)
    {
        if (!rule.On || item.IsFolder) return false;
        var name = Path.GetFileName(item.Path);
        var ext = Path.GetExtension(name);
        return rule.Kind switch
        {
            DrawerRuleKind.Screenshots => ImageExt.Contains(ext) && name.StartsWith("Screenshot", StringComparison.OrdinalIgnoreCase),
            DrawerRuleKind.Images => ImageExt.Contains(ext),
            DrawerRuleKind.Documents => DocumentExt.Contains(ext),
            DrawerRuleKind.Installers => InstallerExt.Contains(ext),
            DrawerRuleKind.Archives => ArchiveExt.Contains(ext),
            DrawerRuleKind.Shortcuts => ShortcutExt.Contains(ext),
            DrawerRuleKind.OlderThan => (nowUtc - item.WrittenUtc).TotalDays > rule.Days,
            DrawerRuleKind.NameContains => !string.IsNullOrEmpty(rule.Text) && name.Contains(rule.Text, StringComparison.OrdinalIgnoreCase),
            DrawerRuleKind.NameStartsWith => !string.IsNullOrEmpty(rule.Text) && name.StartsWith(rule.Text, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    /// <summary>The rules' plan for a listing: each file goes to the drawer of the first rule that matches it and names a drawer in
    /// <paramref name="drawers"/>. The plan carries only the drawers it fills; the validator decides which are new.</summary>
    public static DrawerPlan Plan(IEnumerable<DesktopItem> items, IReadOnlyList<DrawerRule> rules, IReadOnlyList<DrawerDef> drawers,
        string drawersRoot, DateTime nowUtc)
    {
        var byId = drawers.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var moves = new List<DrawerMove>();
        var used = new List<DrawerDef>();
        foreach (var item in items.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
        {
            var rule = rules.FirstOrDefault(r => byId.ContainsKey(r.DrawerId) && Matches(r, item, nowUtc));
            if (rule is null) continue;
            moves.Add(new DrawerMove(item.Path, rule.DrawerId));
            if (!used.Any(d => d.Id == rule.DrawerId)) used.Add(byId[rule.DrawerId]);
        }
        return new DrawerPlan(DrawerPlanSource.Rules, null, used, moves);
    }

    private static HashSet<string> Set(params string[] exts) => new(exts, StringComparer.OrdinalIgnoreCase);
}
