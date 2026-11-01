using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// Drawers' settings (5.0 drawers spec), settings.json <c>drawers</c>: on or off, the Drawers root when the user moved it, the drawers,
/// the rules in order, and auto-tidy. Auto-tidy reads as off until a rule plan has been accepted once (<see cref="AutoTidyUnlocked"/>).
/// A missing key is off with the built-in rules, so an upgrade changes nothing.
/// </summary>
public sealed record DrawerSettings(bool Enabled, string? Root, IReadOnlyList<DrawerDef> Drawers, IReadOnlyList<DrawerRule> Rules,
    bool AutoTidy, bool AutoTidyUnlocked)
{
    public const string Key = "drawers";

    /// <summary>The drawers shown folded (title bar and count only), by id.</summary>
    public IReadOnlyList<string> Folded { get; init; } = [];

    /// <summary>Where each free drawer sits, by id (drawers amendment 1). A drawer with no entry arrives top-right.</summary>
    public IReadOnlyDictionary<string, DrawerPlace> Places { get; init; } = new Dictionary<string, DrawerPlace>();

    /// <summary>The drawers hidden from the Desk, by id; their files stay where they are.</summary>
    public IReadOnlyList<string> Hidden { get; init; } = [];

    /// <summary>How every drawer blends in, unless <see cref="Looks"/> names one (look and placement spec, section 1).</summary>
    public DrawerLookKind Look { get; init; } = DrawerLookKind.Solid;

    /// <summary>Solid's and Glass's fill, 20 to 100 percent.</summary>
    public int Opacity { get; init; } = DrawerStyle.DefaultOpacity;

    /// <summary>A drawer's own look, by id, when it differs from <see cref="Look"/>.</summary>
    public IReadOnlyDictionary<string, DrawerLookKind> Looks { get; init; } = new Dictionary<string, DrawerLookKind>();

    /// <summary>Drawers snap while dragged (spec, section 2).</summary>
    public bool Snap { get; init; } = true;

    /// <summary>Drawers keep clear of the desktop icons when dropped, arriving or tidied.</summary>
    public bool KeepClear { get; init; } = true;

    /// <summary>Where Tidy and arrival line drawers up.</summary>
    public DrawerTidy Tidy { get; init; } = DrawerTidy.Default;

    /// <summary>The file was written before 4.5 (a <c>drawers</c> object with no <c>tidy</c> key): drawers with no saved place arrive in today's
    /// column rule, once, and are saved, so an upgrader's drawers sit exactly as before. Cleared by that first save; never encoded.
    /// Any drawers save that lands before the first render also clears it on the next load, because <c>Encode</c> always writes the
    /// <c>tidy</c> key and that key is the test. Low risk: the startup render comes first.</summary>
    public bool LegacyArrival { get; init; }

    public DrawerLookKind LookFor(string id) => Looks.TryGetValue(id, out var k) ? k : Look;

    public static DrawerSettings Default { get; } = new(false, null, [], DrawerRules.Defaults, false, false);

    /// <summary>The default root (decision KNT1mDtN3kQ0rlz2sePq): a Drawers folder on the Desktop itself, so after any uninstall,
    /// the Store's included, the files sit in one visible folder; and a synced Desktop keeps them synced.</summary>
    public static string DefaultRoot(string desktop) => Path.Combine(desktop, "Drawers");

    /// <summary>The Drawers root in force: the user's, or the Desktop default.</summary>
    public string RootOr(string desktop) => string.IsNullOrWhiteSpace(Root) ? DefaultRoot(desktop) : Root;

    /// <summary>The Drawers root is Sanduhr's own Desktop\Drawers (no folder picked, or the default picked by its path), which turning off
    /// may remove once it is empty. A folder the user picked is theirs and always stays.</summary>
    public bool RootIsDefault(string desktop) =>
        string.IsNullOrWhiteSpace(Root)
        || (DrawerPaths.Normalize(Root) is { } r && string.Equals(r, DrawerPaths.Normalize(DefaultRoot(desktop)), StringComparison.OrdinalIgnoreCase));

    public static DrawerSettings Decode(JsonNode? node)
    {
        if (node is not JsonObject o) return Default;
        bool unlocked = SettingsJson.Bool(o, "auto_tidy_unlocked", false);
        var root = SettingsJson.String(o, "root", "");
        return new DrawerSettings(
            SettingsJson.Bool(o, "enabled", false),
            root.Length == 0 ? null : root,
            o["drawers"] is JsonArray ds ? ds.OfType<JsonObject>().Select(DecodeDrawer).OfType<DrawerDef>().DistinctBy(d => d.Id).ToArray() : [],
            o["rules"] is JsonArray rs ? rs.OfType<JsonObject>().Select(DecodeRule).OfType<DrawerRule>().ToArray() : DrawerRules.Defaults,
            unlocked && SettingsJson.Bool(o, "auto_tidy", false),
            unlocked)
        {
            Folded = o["folded"] is JsonArray fs
                ? [.. fs.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var s) ? s : null).Where(DrawerIds.IsValid).OfType<string>().Distinct()]
                : [],
            Places = DecodePlaces(o["places"]),
            Hidden = o["hidden"] is JsonArray hs
                ? [.. hs.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var s) ? s : null).Where(DrawerIds.IsValid).OfType<string>().Distinct()]
                : [],
            Look = DrawerStyle.Look(SettingsJson.String(o, "look", "")) ?? DrawerLookKind.Solid,
            Opacity = DecodeOpacity(o["opacity"]),
            Looks = DecodeLooks(o["looks"]),
            Snap = SettingsJson.Bool(o, "snap", true),
            KeepClear = SettingsJson.Bool(o, "keep_clear", true),
            Tidy = DecodeTidy(o["tidy"]),
            LegacyArrival = o["tidy"] is null,
        };
    }

    private static int DecodeOpacity(JsonNode? n)
    {
        if (n is not JsonValue v || !v.TryGetValue<double>(out var d) || !double.IsFinite(d) || d < DrawerStyle.MinOpacity)
            return DrawerStyle.DefaultOpacity;
        return (int)Math.Round(Math.Min(d, DrawerStyle.MaxOpacity));
    }

    private static IReadOnlyDictionary<string, DrawerLookKind> DecodeLooks(JsonNode? n)
    {
        var looks = new Dictionary<string, DrawerLookKind>(StringComparer.Ordinal);
        if (n is JsonObject o)
            foreach (var (id, v) in o)
                if (DrawerIds.IsValid(id) && v is JsonValue s && s.TryGetValue<string>(out var w) && DrawerStyle.Look(w) is { } k)
                    looks[id] = k;
        return looks;
    }

    private static DrawerTidy DecodeTidy(JsonNode? n)
    {
        if (n is not JsonObject o) return DrawerTidy.Default;
        return new DrawerTidy(
            DrawerStyle.Corner(SettingsJson.String(o, "corner", "")) ?? DrawerTidy.Default.Corner,
            DrawerStyle.Flow(SettingsJson.String(o, "direction", "")) ?? DrawerTidy.Default.Flow);
    }

    private static Dictionary<string, DrawerPlace> DecodePlaces(JsonNode? node)
    {
        var places = new Dictionary<string, DrawerPlace>(StringComparer.Ordinal);
        if (node is not JsonObject o) return places;
        foreach (var (id, v) in o)
        {
            if (!DrawerIds.IsValid(id) || v is not JsonObject p) continue;
            var monitor = SettingsJson.String(p, "monitor", "");
            double x = SettingsJson.Number(p, "x", double.NaN), y = SettingsJson.Number(p, "y", double.NaN);
            if (monitor.Length == 0 || monitor.Length > DeskMonitors.MaxIdLength || !double.IsFinite(x) || !double.IsFinite(y)) continue;
            places[id] = new DrawerPlace(monitor, x, y);
        }
        return places;
    }

    public JsonObject Encode() => new()
    {
        ["enabled"] = Enabled,
        ["root"] = Root ?? "",
        ["drawers"] = new JsonArray([.. Drawers.Select(d => (JsonNode?)new JsonObject
        {
            ["id"] = d.Id, ["name"] = d.Name, ["home"] = d.Home, ["owned"] = d.Owned,
        })]),
        ["rules"] = new JsonArray([.. Rules.Select(r => (JsonNode?)new JsonObject
        {
            ["kind"] = KindWord(r.Kind), ["drawer"] = r.DrawerId, ["on"] = r.On, ["text"] = r.Text ?? "", ["days"] = r.Days,
        })]),
        ["auto_tidy"] = AutoTidy,
        ["auto_tidy_unlocked"] = AutoTidyUnlocked,
        ["folded"] = new JsonArray([.. Folded.Select(f => (JsonNode?)f)]),
        ["places"] = new JsonObject(Places.Select(kv => KeyValuePair.Create<string, JsonNode?>(kv.Key,
            new JsonObject { ["monitor"] = kv.Value.Monitor, ["x"] = kv.Value.X, ["y"] = kv.Value.Y }))),
        ["hidden"] = new JsonArray([.. Hidden.Select(h => (JsonNode?)h)]),
        ["look"] = DrawerStyle.Word(Look),
        ["opacity"] = Opacity,
        ["looks"] = new JsonObject(Looks.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, (JsonNode?)DrawerStyle.Word(p.Value)))),
        ["snap"] = Snap,
        ["keep_clear"] = KeepClear,
        ["tidy"] = new JsonObject { ["corner"] = DrawerStyle.Word(Tidy.Corner), ["direction"] = DrawerStyle.Word(Tidy.Flow) },
    };

    private static DrawerDef? DecodeDrawer(JsonObject o)
    {
        var id = SettingsJson.String(o, "id", "");
        var name = SettingsJson.String(o, "name", "");
        var home = SettingsJson.String(o, "home", "");
        if (!DrawerIds.IsValid(id) || !DrawerNames.IsValid(name) || home.Length == 0) return null;
        return new DrawerDef(id, name, home, SettingsJson.Bool(o, "owned", false));
    }

    private static DrawerRule? DecodeRule(JsonObject o)
    {
        if (ParseKind(SettingsJson.String(o, "kind", "")) is not { } kind) return null;
        var drawer = SettingsJson.String(o, "drawer", "");
        if (!DrawerIds.IsValid(drawer)) return null;
        var days = SettingsJson.Number(o, "days", 30);
        var text = SettingsJson.String(o, "text", "");
        return new DrawerRule(kind, drawer, SettingsJson.Bool(o, "on", true), text.Length == 0 ? null : text,
            double.IsFinite(days) ? (int)Math.Clamp(Math.Round(days), 1, 3650) : 30);
    }

    /// <summary>snake_case words, so the file reads like the rest of settings.json.</summary>
    private static string KindWord(DrawerRuleKind k) => k switch
    {
        DrawerRuleKind.OlderThan => "older_than",
        DrawerRuleKind.NameContains => "name_contains",
        DrawerRuleKind.NameStartsWith => "name_starts_with",
        _ => k.ToString().ToLowerInvariant(),
    };

    private static DrawerRuleKind? ParseKind(string word) =>
        Enum.GetValues<DrawerRuleKind>().Cast<DrawerRuleKind?>().FirstOrDefault(k => KindWord(k!.Value) == word);
}
