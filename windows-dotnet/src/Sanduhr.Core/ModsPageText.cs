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
    /// Why the list and the settings.json switches do not add up to the same number, or "" when they do.
    /// </summary>
    public static string Reconcile(PluginTally t, int switchesOn, int switchesOff)
    {
        var parts = new List<string>();
        if (t.NoSwitch > 0)
            parts.Add($"{t.NoSwitch} {(t.NoSwitch == 1 ? "has" : "have")} no switch in settings.json " +
                      "(loaded from a plugin folder, or installed here and never switched)");
        if (t.NoFiles > 0)
            parts.Add($"{t.NoFiles} {(t.NoFiles == 1 ? "is" : "are")} switched in settings.json or listed as installed, " +
                      "but the files are not in this folder");
        if (parts.Count == 0) return "";
        return $"settings.json has {switchesOn + switchesOff} plugin switches ({switchesOn} on, {switchesOff} off). " +
               "The list differs because " + string.Join("; ", parts) + ".";
    }

    /// <summary>The settings.json line: "41 on, 2 off", "None" when there are no switches.</summary>
    public static string SwitchSummary(int on, int off) =>
        on + off == 0 ? "None" : $"{on} on, {off} off";
}
