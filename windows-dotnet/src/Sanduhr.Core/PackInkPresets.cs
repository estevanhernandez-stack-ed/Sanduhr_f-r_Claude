namespace Sanduhr.Core;

/// <summary>
/// The Desk ink presets with the palettes of installed palette packs added (3.9.1). <see cref="DeskInkPresets.All"/> stays the Mac's
/// five; this is the list the Settings picker and its lookups use. Kept out of DeskInk.cs on purpose: that file is source-linked
/// into the network-free MCP process, which must not learn about the widget store.
/// </summary>
public static class PackInkPresets
{
    /// <summary>The label of a pack palette in the picker: <c>"&lt;palette&gt; · &lt;pack name&gt;"</c>.</summary>
    public static string Label(string palette, string packName) => $"{palette} · {packName}";

    /// <summary>
    /// The built-ins first, then each enabled palette pack's palettes (by pack id, then file order) as
    /// <c>("&lt;name&gt; · &lt;pack name&gt;", "ff2a6d,d16ba5,05d9e8")</c>: the stops as the ink stores them, hex without <c>#</c>.
    /// A pack whose <c>palettes.json</c> can't be read, and a palette without a usable color, are skipped.
    /// </summary>
    public static IReadOnlyList<(string Name, string Value)> Merged(WidgetStore widgets)
    {
        var list = new List<(string Name, string Value)>(DeskInkPresets.All);
        foreach (var pack in widgets.InstalledOfKind("palette"))
        {
            try
            {
                string file = Path.Combine(pack.Dir, "palettes.json");
                if (!File.Exists(file)) continue;
                if (!PackJson.TryParse(File.ReadAllBytes(file), out var v, out _) || v is not PackObject o
                    || o["palettes"] is not List<object?> palettes) continue;
                foreach (var palette in palettes.OfType<PackObject>())
                {
                    if (palette["name"] is not string name || palette["colors"] is not List<object?> colors) continue;
                    var hexes = colors.OfType<string>().Select(InkHex.Normalize).OfType<string>().ToList();
                    if (hexes.Count == 0) continue;
                    string label = Label(name, pack.Info.Name);
                    if (list.Any(p => p.Name == label)) continue;
                    list.Add((label, string.Join(",", hexes)));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // an unreadable pack adds nothing
            }
        }
        return list;
    }
}
