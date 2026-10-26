namespace Sanduhr.Core;

/// <summary>
/// What the plugin list on Mods &amp; Config counts, and the words it uses for them. In Claude Code these are
/// all plugins; a mod is a plugin that also carries mod hooks (<see cref="ModItem.IsMod"/>) and gets a small
/// "mod" tag. Kept in Core so the counts and wording are tested without a window.
/// </summary>
public sealed record PluginTally(int Listed, int Installed, int On, int NoFiles, int NoSwitch)
{
    public static PluginTally Empty { get; } = new(0, 0, 0, 0, 0);
}

public static class ModsPageText
{
    /// <summary>
    /// Counts the plugins a folder loads.
    /// <list type="bullet">
    /// <item><b>Listed</b>: every row.</item>
    /// <item><b>Installed</b>: rows whose files are in this folder (or the plugin folder it points at).</item>
    /// <item><b>On</b>: installed rows that load. A row switched on in settings.json whose files are gone is not on.</item>
    /// <item><b>NoFiles</b>: switched in settings.json (or listed in the install record) with no files to load.</item>
    /// <item><b>NoSwitch</b>: installed rows settings.json has no switch for: plugin-folder and skills-folder
    /// plugins, and plugins installed here but never switched.</item>
    /// </list>
    /// </summary>
    public static PluginTally Tally(IReadOnlyList<ModItem> items, IReadOnlyDictionary<string, bool> switches)
    {
        int installed = 0, on = 0, noFiles = 0, noSwitch = 0;
        foreach (var item in items)
        {
            if (!HasFiles(item)) { noFiles++; continue; }
            installed++;
            if (item.State == ModState.On) on++;
            if (item.Key is null || !switches.ContainsKey(item.Key)) noSwitch++;
        }
        return new PluginTally(items.Count, installed, on, noFiles, noSwitch);
    }

    /// <summary>The row has files to load: a path that exists, not "Files missing".</summary>
    public static bool HasFiles(ModItem item) => item.Path is not null && item.State != ModState.Missing;

    /// <summary>"Plugins (46 installed, 41 on)"; "Plugins (none)" for an empty folder.</summary>
    public static string ListTitle(PluginTally t) =>
        t.Listed == 0 ? "Plugins (none)" : $"Plugins ({t.Installed} installed, {t.On} on)";

    /// <summary>
    /// The note under the Installed list, over every folder, in the spec's plain words: how many plugins have no on/off entry in
    /// settings.json (Claude Code treats them as on), then how many are switched on with no files. Each count is of distinct plugin ids,
    /// so one plugin in three folders counts once, as the list shows one row. "" when both are 0.
    /// </summary>
    public static string PlainNote(IReadOnlyList<FolderMods> folders)
    {
        var noSwitch = new HashSet<string>(StringComparer.Ordinal);
        var noFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in folders)
            foreach (var m in f.Report.Mods.Where(m => !m.IsSanduhr))
            {
                var id = m.Key ?? m.Name;
                if (!HasFiles(m)) noFiles.Add(id);
                else if (m.Key is null || !f.Report.EnabledPlugins.ContainsKey(m.Key)) noSwitch.Add(id);
            }
        var parts = new List<string>();
        if (noSwitch.Count > 0) parts.Add(SettingsNames.NoSwitchNote(noSwitch.Count));
        if (noFiles.Count > 0) parts.Add(SettingsNames.NoFilesNote(noFiles.Count));
        return string.Join(" ", parts);
    }

    /// <summary>
    /// What a screen reader reads for one plugin row: "626labs, version 0.1.0, on, mod". <paramref name="state"/> is the
    /// row's state words ("On", "Off", "On, no files here", "Files missing"). A plugin with files but no known version
    /// reads "version unknown" here, and only here: the visible row shows nothing.
    /// </summary>
    public static string RowName(string name, string? version, string state, bool isMod, bool hasFiles = true)
    {
        var parts = new List<string> { name };
        if (VersionText(version) is { Length: > 0 } shown) parts.Add("version " + shown.TrimStart('v'));
        else if (hasFiles) parts.Add("version unknown");
        parts.Add(state.ToLowerInvariant());
        if (isMod) parts.Add("mod");
        return string.Join(", ", parts);
    }

    /// <summary>The version as the row shows it: "v0.1.0", or nothing when it is unknown.</summary>
    public static string VersionText(string? version) =>
        ModInventory.CleanVersion(version) is { } v ? "v" + v.TrimStart('v', 'V') : "";

    /// <summary>The settings.json line: "41 on, 2 off", "None" when there are no switches.</summary>
    public static string SwitchSummary(int on, int off) =>
        on + off == 0 ? "None" : $"{on} on, {off} off";
}
