namespace Sanduhr.Core;

/// <summary>What an installed item is (spec, Mods &amp; Widgets; mods-parity §4).</summary>
public enum InstalledKind { SanduhrIntegration, WidgetPack, ClaudeCodeMod, ClaudeCodePlugin }

/// <summary>How Sanduhr can switch an item: its own meters mod through the receipt (per folder), a widget pack through widgets.json's
/// <c>enabled</c>, or not at all (read-only in 4.0).</summary>
public enum SwitchRoute { None, OwnModReceipt, PackEnabled }

/// <summary>An item's state in one Claude Code folder. <see cref="Unreadable"/> is the folder's settings.json failing to parse, which
/// Sanduhr never writes through (slice 4, decision 8).</summary>
public enum FolderState { On, Off, Missing, NotInstalled, Unreadable }

/// <summary>One Claude Code folder as the App read it: its report, Sanduhr's mod state there and the meters entry's status.</summary>
public sealed record FolderMods(string Folder, string Display, FolderReport Report, OwnModState? Own, IntegrationStatus Meters);

/// <summary>An item's state in one folder, with the folder as Settings shows it.</summary>
public sealed record FolderScope(string Folder, string Display, FolderState State);

/// <summary>
/// One row of the Installed list. <paramref name="Scope"/> is null for an app-wide widget pack and one entry per known folder for a Claude
/// Code item. <paramref name="Update"/> is the catalog's newer version of an installed catalog pack.
/// </summary>
public sealed record InstalledItem(
    string Id,
    string Name,
    InstalledKind Kind,
    string? PackKind,
    string Source,
    string? Version,
    string? Update,
    IReadOnlyList<FolderScope>? Scope,
    bool Enabled,
    SwitchRoute Route,
    string? ReadOnlyReason,
    bool MadeHere,
    bool IsMod,
    string Description,
    ModTouches? Touches = null)
{
    public bool CanSwitch => Route != SwitchRoute.None;
    public bool AppScoped => Scope is null;

    /// <summary>What the item touches, in one short line ("Draws a band; can run programs."): only a Claude Code mod or plugin has one, and
    /// only when the scan found something. A widget pack and Sanduhr's own meters mod have none, and neither does a plugin that touches nothing.</summary>
    public string TouchesLine =>
        Kind is InstalledKind.ClaudeCodeMod or InstalledKind.ClaudeCodePlugin && Touches is { } t ? ModsPageText.DescribeTouches(t) : "";
}

/// <summary>
/// Settings v2, Mods &amp; Widgets: everything installed for Sanduhr and for Claude Code in one list, built from what the App has already
/// read (<see cref="ModInventory.Read"/> per folder, <see cref="ClaudeCodeIntegrations.OwnModStateOf"/> and the meters status, the widget
/// registry, the catalog). Pure: no disk, no network. App-side only: not linked into the MCP server, whose link list is fixed
/// (TrustBoundaryTests).
/// </summary>
public static partial class InstalledItems
{
    public const string SanduhrModId = ModInventory.SanduhrModName;

    /// <summary>The list, in order: Sanduhr's meters mod (while any folder has it), the widget packs by name, then every other Claude Code
    /// plugin and mod by name, one row per id across the folders.</summary>
    public static IReadOnlyList<InstalledItem> Build(IReadOnlyList<FolderMods> folders, WidgetRegistry widgets,
        Func<string, InstalledWidgetInfo?> info, WidgetCatalogDocument? catalog)
    {
        var items = new List<InstalledItem>();

        var meters = folders.Select(f => new FolderScope(f.Folder, f.Display, MetersState(f))).ToList();
        if (meters.Any(s => s.State != FolderState.NotInstalled))
        {
            var version = folders.SelectMany(f => f.Report.Mods).FirstOrDefault(m => m.IsSanduhr && m.Version is not null)?.Version;
            items.Add(new InstalledItem(SanduhrModId, SettingsNames.MetersSection, InstalledKind.SanduhrIntegration, null, SettingsNames.SourceSanduhr,
                version, null, meters, meters.Any(s => s.State == FolderState.On), SwitchRoute.OwnModReceipt, null, false, true, ""));
        }

        var packs = new List<InstalledItem>();
        foreach (var row in widgets.Packs)
        {
            var i = info(row.Id);
            string? update = null;
            if (!row.MadeHere && catalog?.Packs.FirstOrDefault(e => e.Id == row.Id) is { } entry
                && WidgetCatalog.CompareVersions(entry.Version, row.Version) > 0)
                update = entry.Version;
            packs.Add(new InstalledItem(row.Id, i?.Name ?? row.Id, InstalledKind.WidgetPack, i?.Kind ?? "",
                row.MadeHere ? SettingsNames.WidgetMadeHere : SettingsNames.SourceFromCatalog, row.Version, update, null, row.Enabled,
                SwitchRoute.PackEnabled, null, row.MadeHere, false, i?.Description ?? ""));
        }
        items.AddRange(packs.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Id, StringComparer.Ordinal));

        var byId = new Dictionary<string, List<(string Folder, ModItem Item)>>(StringComparer.Ordinal);
        foreach (var f in folders)
            foreach (var m in f.Report.Mods.Where(m => !m.IsSanduhr))
            {
                var id = m.Key ?? m.Name;
                if (!byId.TryGetValue(id, out var seen)) byId[id] = seen = [];
                seen.Add((f.Folder, m));
            }
        var claude = new List<InstalledItem>();
        foreach (var (id, seen) in byId)
        {
            var first = seen[0].Item;
            var scope = folders.Select(f =>
            {
                var here = seen.FirstOrDefault(s => string.Equals(s.Folder, f.Folder, StringComparison.OrdinalIgnoreCase)).Item;
                // A row with no files to load (ModsPageText.HasFiles, the note's own check) is Files missing whatever settings.json says:
                // switched on with its folder gone, it is not on (Task 12 review I1).
                var state = here is null ? (f.Report.Settings.Kind == SettingsKind.Unreadable ? FolderState.Unreadable : FolderState.NotInstalled)
                    : !ModsPageText.HasFiles(here) ? FolderState.Missing
                    // Slice 4 row 13: with settings.json unreadable, "no entry" is unknown, not on.
                    : f.Report.Settings.Kind == SettingsKind.Unreadable ? FolderState.Unreadable : here.State switch
                {
                    ModState.On => FolderState.On,
                    ModState.Off => FolderState.Off,
                    _ => FolderState.Missing,
                };
                return new FolderScope(f.Folder, f.Display, state);
            }).ToList();
            bool isMod = seen.Any(s => s.Item.IsMod);
            string source = first.Marketplace ?? (first.Origin == ModOriginKind.SkillsDir ? SettingsNames.SourceSkillsFolder : SettingsNames.SourcePluginFolder);
            claude.Add(new InstalledItem(id, first.Name, isMod ? InstalledKind.ClaudeCodeMod : InstalledKind.ClaudeCodePlugin, null,
                source, seen.Select(s => s.Item.Version).FirstOrDefault(v => v is not null), null, scope,
                scope.Any(s => s.State == FolderState.On), SwitchRoute.None, SettingsNames.ReadOnlyClaudeCode, false, isMod,
                seen.Select(s => s.Item.Description).FirstOrDefault(d => d.Length > 0) ?? "", MergeTouches(seen.Select(s => s.Item.Touches))));
        }
        items.AddRange(claude.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Id, StringComparer.Ordinal));
        return items;
    }

    /// <summary>What an id touches across the folders that hold it: every surface and capability any of them has, in the scan's fixed order.</summary>
    private static ModTouches MergeTouches(IEnumerable<ModTouches> all)
    {
        var list = all.ToList();
        return new ModTouches(
            Enum.GetValues<ModSurface>().Where(s => list.Any(t => t.Surfaces.Contains(s))).ToList(),
            Enum.GetValues<ModCapability>().Where(c => list.Any(t => t.Capabilities.Contains(c))).ToList(),
            [.. list.SelectMany(t => t.Commands).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
    }

    /// <summary>Where Sanduhr's meters mod stands in one folder: an unreadable settings.json first, then files gone, then on, off or absent.</summary>
    public static FolderState MetersState(FolderMods folder)
    {
        if (folder.Report.Settings.Kind == SettingsKind.Unreadable || folder.Own is { Readable: false }
            || folder.Meters == IntegrationStatus.Unreadable)
            return FolderState.Unreadable;
        if (folder.Report.Mods.Any(m => m.IsSanduhr && m.State == ModState.Missing))
            return FolderState.Missing;
        if (folder.Own is not { Listed: true } own)
            return FolderState.NotInstalled;
        return own.IsOn ? FolderState.On : FolderState.Off;
    }

    /// <summary>A state in words, as a scope with one folder reads it.</summary>
    public static string StateWord(FolderState state) => state switch
    {
        FolderState.On => SettingsNames.FolderOn,
        FolderState.Off => SettingsNames.FolderOff,
        FolderState.Missing => SettingsNames.FolderMissing,
        FolderState.Unreadable => SettingsNames.FolderUnreadable,
        _ => SettingsNames.FolderNotInstalled,
    };

    /// <summary>"On in 2 of 3 folders"; one folder reads its state alone; an app-wide pack has none.</summary>
    public static string ScopeSummary(InstalledItem item) => item.Scope switch
    {
        null or { Count: 0 } => "",
        { Count: 1 } one => StateWord(one[0].State),
        var many => SettingsNames.OnInFolders(many.Count(s => s.State == FolderState.On), many.Count),
    };
}

/// <summary>What a catalog row's one control does (spec, Catalog: "Install, Update, or the switch").</summary>
public enum CatalogAction { Install, Update, Switch, None }

/// <summary>A catalog pack merged with its installed row by id, and the line under it.</summary>
public sealed record CatalogRow(WidgetCatalogEntry Entry, CatalogAction Action, string State, InstalledItem? Installed);

/// <summary>Where an installed pack shows (WF3), and the link that opens that place.</summary>
public sealed record WhereItShows(string Text, string? Link, string? LinkTitle);

/// <summary>The Installed list's filter chips.</summary>
public enum InstalledFilter { All, ClaudeCode, Widgets }

public static partial class InstalledItems
{
    /// <summary>
    /// The catalog's packs in catalog order, each once, with its control: Install when not installed, Update when the catalog is newer and
    /// this Sanduhr meets its minimum, the switch otherwise. An id held by a pack made here offers nothing. <paramref name="kind"/> shows one
    /// pack kind (the companion row's link shows sprites).
    /// </summary>
    public static IReadOnlyList<CatalogRow> CatalogRows(WidgetCatalogDocument catalog, IReadOnlyList<InstalledItem> items, Version running,
        string? kind = null)
    {
        var rows = new List<CatalogRow>();
        foreach (var entry in catalog.Packs)
        {
            if (kind is not null && entry.Kind != kind) continue;
            var installed = items.FirstOrDefault(i => i.Kind == InstalledKind.WidgetPack && i.Id == entry.Id);
            bool meets = WidgetCatalog.MeetsMinimum(entry.SanduhrMin, running);
            rows.Add(installed switch
            {
                { MadeHere: true } => new(entry, CatalogAction.None, SettingsNames.CatalogIdTaken, installed),
                null => meets
                    ? new(entry, CatalogAction.Install, "", null)
                    : new(entry, CatalogAction.None, SettingsNames.CatalogNeedsSanduhr(entry.SanduhrMin), null),
                _ when WidgetCatalog.CompareVersions(entry.Version, installed.Version) > 0 => meets
                    ? new(entry, CatalogAction.Update, SettingsNames.CatalogVersionInstalled(installed.Version ?? ""), installed)
                    : new(entry, CatalogAction.Switch,
                        SettingsNames.CatalogUpdateNeedsSanduhr(installed.Version ?? "", entry.Version, entry.SanduhrMin), installed),
                _ => new(entry, CatalogAction.Switch, SettingsNames.CatalogInstalled, installed),
            });
        }
        return rows;
    }

    /// <summary>Where a pack of <paramref name="packKind"/> shows, with "&lt;Page&gt; settings…" to that section. Effects have no consumer yet.</summary>
    public static WhereItShows Where(string? packKind) => packKind switch
    {
        "theme" => new(SettingsNames.ShowsInThemes, Link(SettingsPage.Themes, SettingsIndex.Gallery), SettingsNames.LinkTitle(SettingsPage.Themes)),
        "palette" => new(SettingsNames.ShowsInInk, Link(SettingsPage.DeskLook, SettingsIndex.DeskInk), SettingsNames.LinkTitle(SettingsPage.DeskLook)),
        "font" => new(SettingsNames.ShowsInFonts, Link(SettingsPage.DeskLook, SettingsIndex.DeskLook), SettingsNames.LinkTitle(SettingsPage.DeskLook)),
        "sprite" => new(SettingsNames.ShowsAsCompanion, Link(SettingsPage.Desk, SettingsIndex.DeskPieces), SettingsNames.LinkTitle(SettingsPage.Desk)),
        "effects" => new(SettingsNames.EffectsUnused, null, null),
        _ => new("", null, null),
    };

    private static string Link(SettingsPage page, string anchor) => SettingsIndex.PageId(page) + "#" + anchor;

    /// <summary>The chips (All, Claude Code, Widgets) and the text filter, any case, over the name, id, pack kind, source and mod tag.</summary>
    public static bool Matches(InstalledItem item, InstalledFilter filter, string? text)
    {
        bool kind = filter switch
        {
            InstalledFilter.ClaudeCode => item.Kind != InstalledKind.WidgetPack,
            InstalledFilter.Widgets => item.Kind == InstalledKind.WidgetPack,
            _ => true,
        };
        if (!kind) return false;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var t = text.Trim();
        return new[] { item.Name, item.Id, item.PackKind ?? "", item.Source }
            .Any(s => s.Contains(t, StringComparison.OrdinalIgnoreCase))
            // Slice 4 row 21: "mod" is the tag as a whole word, not a substring ("mo" no longer matches every mod).
            || (item.IsMod && t.Equals("mod", StringComparison.OrdinalIgnoreCase));
    }
}
