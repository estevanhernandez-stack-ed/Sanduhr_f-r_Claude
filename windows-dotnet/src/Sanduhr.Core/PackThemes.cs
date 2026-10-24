namespace Sanduhr.Core;

/// <summary>
/// The themes of installed theme packs (3.9.1). Kept out of <see cref="ThemeCatalog"/> on purpose: that file is source-linked into
/// the network-free MCP process, which must not learn about the widget store.
/// </summary>
public static class PackThemes
{
    /// <summary>
    /// The themes of <paramref name="packs"/>, keyed <c>&lt;packId&gt;:&lt;stem&gt;</c>. The colon can't be in a file name or in a
    /// <see cref="ThemeHandoff.IsValidKey"/> key, so a pack theme never shadows a built-in or a user theme. Each file goes through the
    /// same <see cref="ThemeLint"/> gate as <see cref="ThemeCatalog.LoadUserThemes"/>; a pack without a <c>themes</c> folder adds
    /// nothing, and none is created.
    /// </summary>
    public static IReadOnlyDictionary<string, ThemeDefinition> Load(IEnumerable<InstalledPack> packs, Action<string>? warn = null)
    {
        var loaded = new Dictionary<string, ThemeDefinition>();
        foreach (var pack in packs)
        {
            string dir = Path.Combine(pack.Dir, "themes");
            if (!Directory.Exists(dir)) continue;
            foreach (var (stem, def) in ThemeCatalog.LoadUserThemes(dir, warn is null ? null : m => warn($"[{pack.Row.Id}] {m}")))
                loaded[pack.Row.Id + ":" + stem] = def;
        }
        return loaded;
    }

    /// <summary>The theme list: the built-ins, the user's drop-ins (which override a built-in of the same key), then the installed
    /// theme packs' themes.</summary>
    public static Dictionary<string, ThemeDefinition> Merged(string themesDir, WidgetStore widgets, Action<string>? warn = null)
    {
        var map = new Dictionary<string, ThemeDefinition>(ThemeCatalog.BuiltIns);
        foreach (var (key, def) in ThemeCatalog.LoadUserThemes(themesDir, warn)) map[key] = def;
        foreach (var (key, def) in Load(widgets.InstalledOfKind("theme"), warn)) map[key] = def;
        return map;
    }
}
