using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// The Desk companion's choices (spec 3.8 section 4), settings.json <c>desk_companion</c>: which installed sprite pack is
/// the companion (one at a time) and whether it roams. Whether it shows at all is the <c>companion</c> piece being placed
/// in the Desk layout, like every other piece. Absent reads as no pack chosen (the first installed sprite is used) and
/// roaming on.
/// </summary>
public sealed record CompanionSettings(string? Pack, bool Roam)
{
    public const string Key = "desk_companion";

    /// <summary>The Desk piece's widget word.</summary>
    public const string Widget = "companion";

    public static CompanionSettings Default { get; } = new(null, true);

    public static CompanionSettings Decode(JsonNode? node)
    {
        if (node is not JsonObject o) return Default;
        string pack = SettingsJson.String(o, "pack", "");
        return new CompanionSettings(WidgetStore.IsSafeId(pack) ? pack : null, SettingsJson.Bool(o, "roam", Default.Roam));
    }

    public JsonObject Encode()
    {
        var o = new JsonObject();
        if (Pack is { } p) o["pack"] = p;
        o["roam"] = Roam;
        return o;
    }

    /// <summary>
    /// The pack the companion draws: the chosen one while it is installed as a sprite, otherwise the first installed sprite
    /// pack by id (so installing the hourglass and placing the piece is enough). Null when no sprite pack is installed.
    /// <paramref name="kindOf"/> reads an installed pack's kind from its own pack.json.
    /// </summary>
    public string? ActivePack(WidgetRegistry registry, Func<string, string?> kindOf)
    {
        var sprites = registry.Packs.Where(p => kindOf(p.Id) == "sprite").Select(p => p.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
        if (Pack is { } chosen && sprites.Contains(chosen)) return chosen;
        return sprites.FirstOrDefault();
    }
}
