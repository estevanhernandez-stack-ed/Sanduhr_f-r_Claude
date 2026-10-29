using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>Accent-bloom glow dial: a soft glow around the percentage numbers.
/// Ported from the <c>accent_bloom</c> entry in <c>themes.py</c>
/// (<c>{"blur": int, "alpha": float}</c>).</summary>
public sealed record AccentBloom(int Blur, double Alpha);

/// <summary>1-px top-edge light-catch on cards. Ported from the
/// <c>inner_highlight</c> entry in <c>themes.py</c>
/// (<c>{"color": "#..", "alpha": float}</c> or <c>null</c>).</summary>
public sealed record InnerHighlight(string Color, double Alpha);

/// <summary>
/// The Desk part of a theme (theme match, the theme JSON's <c>desk</c> object). Every field is optional; null leaves that part to
/// ThemeDeskLook's rules. Stops and colors are <c>#rrggbb</c> as the file has them (<see cref="ThemeLint"/> accepted them).
/// Equality compares the stop lists element by element, so a definition read twice equals itself.
/// </summary>
public sealed record ThemeDesk
{
    /// <summary>1 to 4 stops for the clock, the date and the Claude line.</summary>
    public IReadOnlyList<string>? Ink { get; init; }
    /// <summary>1 to 4 stops for the meter bars, on the widget and the Desk.</summary>
    public IReadOnlyList<string>? Bars { get; init; }
    /// <summary>1 to 4 stops for the Message line.</summary>
    public IReadOnlyList<string>? Message { get; init; }
    /// <summary>The message glow's color.</summary>
    public string? Glow { get; init; }
    /// <summary>Whether the back plate shows.</summary>
    public bool? Plate { get; init; }
    /// <summary>The plate's tint.</summary>
    public string? PlateColor { get; init; }
    /// <summary>The text shadow's color.</summary>
    public string? Shadow { get; init; }
    /// <summary>A family for the Desk only, when it should differ from the theme's <c>font</c>.</summary>
    public string? Font { get; init; }

    /// <summary>The fields a desk object may carry, in the order a file writes them.</summary>
    public static IReadOnlyList<string> Fields { get; } = ["ink", "bars", "message", "glow", "plate", "plate_color", "shadow", "font"];

    public bool IsEmpty =>
        Ink is null && Bars is null && Message is null && Glow is null && Plate is null && PlateColor is null && Shadow is null && Font is null;

    /// <summary>The desk object: only the fields that are set.</summary>
    public JsonObject ToJson()
    {
        var o = new JsonObject();
        if (Ink is not null) o["ink"] = Stops(Ink);
        if (Bars is not null) o["bars"] = Stops(Bars);
        if (Message is not null) o["message"] = Stops(Message);
        if (Glow is not null) o["glow"] = Glow;
        if (Plate is { } plate) o["plate"] = plate;
        if (PlateColor is not null) o["plate_color"] = PlateColor;
        if (Shadow is not null) o["shadow"] = Shadow;
        if (Font is not null) o["font"] = Font;
        return o;
    }

    /// <summary>The desk object of a theme the lint accepted; null when it is absent or sets nothing. Unknown fields are left behind
    /// (the lint names them).</summary>
    internal static ThemeDesk? Read(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        var d = new ThemeDesk
        {
            Ink = List(o["ink"]),
            Bars = List(o["bars"]),
            Message = List(o["message"]),
            Glow = Str(o["glow"]),
            Plate = o["plate"] is JsonValue v && v.TryGetValue<bool>(out var on) ? on : null,
            PlateColor = Str(o["plate_color"]),
            Shadow = Str(o["shadow"]),
            Font = Str(o["font"]),
        };
        return d.IsEmpty ? null : d;
    }

    public bool Equals(ThemeDesk? other) =>
        other is not null && Same(Ink, other.Ink) && Same(Bars, other.Bars) && Same(Message, other.Message)
        && Glow == other.Glow && Plate == other.Plate && PlateColor == other.PlateColor && Shadow == other.Shadow && Font == other.Font;

    public override int GetHashCode() =>
        HashCode.Combine(Glow, Plate, PlateColor, Shadow, Font, Ink?.Count ?? -1, Bars?.Count ?? -1, Message?.Count ?? -1);

    private static bool Same(IReadOnlyList<string>? a, IReadOnlyList<string>? b) =>
        a is null ? b is null : b is not null && a.SequenceEqual(b, StringComparer.Ordinal);

    private static JsonArray Stops(IReadOnlyList<string> stops)
    {
        var a = new JsonArray();
        // Non-generic Add: the trimmed MCP server carries no serializer metadata.
        foreach (var s in stops) a.Add((JsonNode?)s);
        return a;
    }

    private static IReadOnlyList<string>? List(JsonNode? n) =>
        n is JsonArray a ? [.. a.Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : "")] : null;

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}

/// <summary>
/// A single Sanduhr theme — every color field plus the glass-tuning dials and the
/// Matrix/Blueprint terminal extras. Pure data, no WPF: colors stay as
/// <c>#rrggbb</c> strings so this lives in Core and is unit-testable. The App's
/// <c>ThemePalette</c> converts to WPF <c>Color</c>/<c>Brush</c> resources.
///
/// Ported 1:1 from <c>themes.py</c>'s theme dicts + <c>_DEFAULT_GLASS_TUNING</c>:
/// the dial defaults below match the Python defaults so a user JSON that omits
/// them resolves identically across both builds.
/// </summary>
public sealed record ThemeDefinition
{
    // -- required color fields (themes._REQUIRED_COLOR_FIELDS) ----------------
    public required string Name { get; init; }
    public required string Bg { get; init; }
    public required string Glass { get; init; }
    public required string GlassOnMica { get; init; }
    public required string TitleBg { get; init; }
    public required string Border { get; init; }
    public required string Text { get; init; }
    public required string TextSecondary { get; init; }
    public required string TextDim { get; init; }
    public required string TextMuted { get; init; }
    public required string Accent { get; init; }
    public required string BarBg { get; init; }
    public required string FooterBg { get; init; }
    public required string PaceMarker { get; init; }
    public required string Sparkline { get; init; }

    // -- glass-tuning dials (defaults from _DEFAULT_GLASS_TUNING) -------------
    public double GlassAlpha { get; init; } = 0.80;
    public double BorderAlpha { get; init; } = 0.40;
    public string? BorderTint { get; init; }
    public AccentBloom AccentBloom { get; init; } = new(4, 0.45);
    public InnerHighlight? InnerHighlight { get; init; }

    // -- Matrix / Blueprint terminal extras ----------------------------------
    public bool OptsOutOfMica { get; init; }
    public string? MonospaceFont { get; init; }
    public string? MonospaceFallback { get; init; }
    public int? CardCornerRadius { get; init; }
    public int? BreathPeriodMs { get; init; }
    public bool BgGrid { get; init; }

    // -- one theme for the widget and the Desk (theme match) -------------------
    /// <summary>The family the widget's text is drawn in, and the Desk's while it follows the theme; null keeps Segoe UI.</summary>
    public string? Font { get; init; }
    /// <summary>The theme's Desk look; null leaves every part of it to ThemeDeskLook's rules.</summary>
    public ThemeDesk? Desk { get; init; }
}

/// <summary>
/// The 6 built-in palettes, the usage color ramp, and the user-theme loader —
/// ported 1:1 from <c>themes.py</c>. Pure Core: the App's <c>ThemePalette</c>
/// adapts a <see cref="ThemeDefinition"/> into live WPF resources.
/// </summary>
public static class ThemeCatalog
{
    /// <summary>The fields a user theme JSON must contain to be accepted
    /// (themes._REQUIRED_COLOR_FIELDS, verbatim + same order).</summary>
    public static readonly IReadOnlyList<string> RequiredColorFields = new[]
    {
        "name", "bg", "glass", "glass_on_mica", "title_bg", "border",
        "text", "text_secondary", "text_dim", "text_muted",
        "accent", "bar_bg", "footer_bg", "pace_marker", "sparkline",
    };

    /// <summary>Built-in theme keys in display order (drives the theme strip):
    /// obsidian, aurora, ember, mint, matrix, blueprint.</summary>
    public static readonly IReadOnlyList<string> BuiltInOrder = new[]
    {
        "obsidian", "aurora", "ember", "mint", "matrix", "blueprint",
    };

    /// <summary>The 6 shipped palettes, keyed lowercase. Ported value-for-value
    /// from <c>themes.THEMES</c>.</summary>
    public static readonly IReadOnlyDictionary<string, ThemeDefinition> BuiltIns =
        new Dictionary<string, ThemeDefinition>
        {
            ["obsidian"] = new ThemeDefinition
            {
                Name = "Obsidian",
                Bg = "#0d0d0d",
                Glass = "#1c1c1c",
                GlassOnMica = "#1a1a1c",
                TitleBg = "#161616",
                Border = "#333333",
                Text = "#e8e4dc",
                TextSecondary = "#b8b4ac",
                TextDim = "#8b8b8b",
                TextMuted = "#848484",
                Accent = "#6c63ff",
                BarBg = "#2a2a2a",
                FooterBg = "#111111",
                PaceMarker = "#ff6b6b",
                Sparkline = "#6c63ff",
                GlassAlpha = 0.85,
                BorderAlpha = 0.30,
                BorderTint = null,
                AccentBloom = new(4, 0.35),
                InnerHighlight = null,
            },
            ["aurora"] = new ThemeDefinition
            {
                Name = "Aurora",
                Bg = "#0a0f1a",
                Glass = "#161e30",
                GlassOnMica = "#141d2e",
                TitleBg = "#0f172a",
                Border = "#334155",
                Text = "#e2e8f0",
                TextSecondary = "#94a3b8",
                TextDim = "#808ea3",
                TextMuted = "#7487a1",
                Accent = "#38bdf8",
                BarBg = "#1e293b",
                FooterBg = "#0c1220",
                PaceMarker = "#f472b6",
                Sparkline = "#38bdf8",
                GlassAlpha = 0.80,
                BorderAlpha = 0.50,
                BorderTint = "#38bdf8",
                AccentBloom = new(6, 0.55),
                InnerHighlight = new("#38bdf8", 0.20),
            },
            ["ember"] = new ThemeDefinition
            {
                Name = "Ember",
                Bg = "#1a0a0a",
                Glass = "#261414",
                GlassOnMica = "#211010",
                TitleBg = "#1f0e0e",
                Border = "#442222",
                Text = "#f5e6e0",
                TextSecondary = "#d4a89c",
                TextDim = "#a18277",
                TextMuted = "#a47767",
                Accent = "#f97316",
                BarBg = "#2d1a1a",
                FooterBg = "#150808",
                PaceMarker = "#fbbf24",
                Sparkline = "#f97316",
                GlassAlpha = 0.82,
                BorderAlpha = 0.40,
                BorderTint = "#f97316",
                AccentBloom = new(6, 0.55),
                InnerHighlight = new("#f97316", 0.18),
            },
            ["mint"] = new ThemeDefinition
            {
                Name = "Mint",
                Bg = "#0a1a14",
                Glass = "#122a1e",
                GlassOnMica = "#0e2419",
                TitleBg = "#0c1f14",
                Border = "#22543d",
                Text = "#e0f5ec",
                TextSecondary = "#9cd4b8",
                TextDim = "#5fa17e",
                TextMuted = "#4a9b70",
                Accent = "#34d399",
                BarBg = "#163020",
                FooterBg = "#081510",
                PaceMarker = "#f472b6",
                Sparkline = "#34d399",
                GlassAlpha = 0.78,
                BorderAlpha = 0.45,
                BorderTint = "#34d399",
                AccentBloom = new(4, 0.35),
                InnerHighlight = new("#34d399", 0.22),
            },
            ["matrix"] = new ThemeDefinition
            {
                Name = "Matrix",
                Bg = "#020a02",
                Glass = "#0a140a",
                GlassOnMica = "#0a140a",
                TitleBg = "#040d04",
                Border = "#0f2a0f",
                Text = "#00ff41",
                TextSecondary = "#00cc33",
                TextDim = "#009933",
                TextMuted = "#009030",
                Accent = "#00ff41",
                BarBg = "#0a1a0a",
                FooterBg = "#020802",
                PaceMarker = "#ff0040",
                Sparkline = "#00ff41",
                GlassAlpha = 1.0,
                BorderAlpha = 0.50,
                BorderTint = "#00ff41",
                AccentBloom = new(4, 0.60),
                InnerHighlight = null,
                OptsOutOfMica = true,
                MonospaceFont = "Cascadia Code",
                MonospaceFallback = "Consolas",
                CardCornerRadius = 2,
                BreathPeriodMs = 1800,
            },
            ["blueprint"] = new ThemeDefinition
            {
                Name = "Blueprint",
                Bg = "#1a1625",
                Glass = "#2a2438",
                GlassOnMica = "#241f30",
                TitleBg = "#15121e",
                Border = "#3a324d",
                Text = "#e0e2f5",
                TextSecondary = "#a2a6cc",
                TextDim = "#9194b3",
                TextMuted = "#878bb2",
                Accent = "#4dffc4",
                BarBg = "#1f1a2e",
                FooterBg = "#100d16",
                PaceMarker = "#ff4d88",
                Sparkline = "#4dffc4",
                GlassAlpha = 0.85,
                BorderAlpha = 0.70,
                BorderTint = "#4dffc4",
                AccentBloom = new(8, 0.65),
                InnerHighlight = new("#4dffc4", 0.15),
                BgGrid = true,
            },
        };

    /// <summary>
    /// The bar-fill / percentage color for a utilization %, 1:1 with
    /// <c>themes.usage_color</c>: &lt;50 green, &lt;75 yellow, &lt;90 orange, else red.
    /// Returns a <c>#rrggbb</c> hex string.
    /// </summary>
    public static string UsageColor(int pct)
    {
        if (pct < 50) return "#4ade80";
        if (pct < 75) return "#facc15";
        if (pct < 90) return "#fb923c";
        return "#f87171";
    }

    /// <summary>
    /// Scan <paramref name="themesDir"/> for <c>*.json</c> theme files, validate
    /// each against <see cref="RequiredColorFields"/>, and return the loaded user
    /// themes keyed by lowercased file stem (e.g. <c>Sunset.json</c> → "sunset").
    /// Ported from <c>themes.load_user_themes</c>: invalid JSON or a missing
    /// required field skips the file (reported via <paramref name="warn"/>); the
    /// first file wins for a duplicate stem; the directory is created if absent.
    /// Pure read — merging into the live catalog is the caller's job.
    /// </summary>
    public static IReadOnlyDictionary<string, ThemeDefinition> LoadUserThemes(
        string themesDir, Action<string>? warn = null)
    {
        var loaded = new Dictionary<string, ThemeDefinition>();
        Directory.CreateDirectory(themesDir);

        var files = Directory.GetFiles(themesDir, "*.json");
        Array.Sort(files, StringComparer.Ordinal);

        foreach (var path in files)
        {
            var key = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (string.IsNullOrEmpty(key) || loaded.ContainsKey(key))
                continue;

            JsonObject? data;
            try
            {
                data = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            }
            catch (Exception e) when (e is JsonException or IOException)
            {
                warn?.Invoke($"Could not read user theme {Path.GetFileName(path)}: {e.Message}");
                continue;
            }
            if (data is null)
            {
                warn?.Invoke($"User theme {Path.GetFileName(path)} is not a JSON object");
                continue;
            }

            var theme = TryValidate(key, data, warn);
            if (theme is null)
                continue;

            loaded[key] = theme;
        }
        return loaded;
    }

    /// <summary>
    /// Validate + normalize a parsed theme JSON. Delegates to <see cref="ThemeLint"/>
    /// (the schema errors) and reports each error via <paramref name="warn"/>;
    /// returns null when the theme is rejected, otherwise the definition with the
    /// <c>_DEFAULT_GLASS_TUNING</c> defaults applied for omitted dials. Design-rule
    /// warnings are not reported here; the paste box, the studio, and the MCP tool
    /// show them, a drop-in file just loads.
    /// </summary>
    public static ThemeDefinition? TryValidate(string key, JsonObject data, Action<string>? warn = null)
    {
        var result = ThemeLint.Lint(data);
        if (result.Theme is not null)
            return result.Theme;

        var missing = RequiredColorFields.Where(f => !data.ContainsKey(f)).ToList();
        if (missing.Count > 0)
            warn?.Invoke($"User theme '{key}' missing required fields: {string.Join(", ", missing)}");
        foreach (var e in result.Errors.Where(e => data.ContainsKey(e.Field) || e.Field.Contains('.')))
            warn?.Invoke($"User theme '{key}' has a malformed field: {e.Message}");
        return null;
    }

    /// <summary>Build the definition from JSON that <see cref="ThemeLint"/> has
    /// already accepted: every required color present and well-formed, dials in
    /// range. Omitted dials take the <c>_DEFAULT_GLASS_TUNING</c> defaults.</summary>
    internal static ThemeDefinition BuildDefinition(JsonObject data)
    {
        var bloom = new AccentBloom(4, 0.45);
        if (data["accent_bloom"] is JsonObject ab)
            bloom = new AccentBloom(IntOr(ab["blur"], 4), DoubleOr(ab["alpha"], 0.45));

        InnerHighlight? inner = null;
        if (data["inner_highlight"] is JsonObject ih && ih["color"] is JsonNode)
            inner = new InnerHighlight(Str(ih, "color"), DoubleOr(ih["alpha"], 0.20));

        return new ThemeDefinition
        {
            Name = Str(data, "name"),
            Bg = Str(data, "bg"),
            Glass = Str(data, "glass"),
            GlassOnMica = Str(data, "glass_on_mica"),
            TitleBg = Str(data, "title_bg"),
            Border = Str(data, "border"),
            Text = Str(data, "text"),
            TextSecondary = Str(data, "text_secondary"),
            TextDim = Str(data, "text_dim"),
            TextMuted = Str(data, "text_muted"),
            Accent = Str(data, "accent"),
            BarBg = Str(data, "bar_bg"),
            FooterBg = Str(data, "footer_bg"),
            PaceMarker = Str(data, "pace_marker"),
            Sparkline = Str(data, "sparkline"),
            GlassAlpha = DoubleOr(data["glass_alpha"], 0.80),
            BorderAlpha = DoubleOr(data["border_alpha"], 0.40),
            BorderTint = StrOrNull(data, "border_tint"),
            AccentBloom = bloom,
            InnerHighlight = inner,
            OptsOutOfMica = BoolOr(data["opts_out_of_mica"], false),
            MonospaceFont = StrOrNull(data, "monospace_font"),
            MonospaceFallback = StrOrNull(data, "monospace_fallback"),
            CardCornerRadius = IntOrNull(data["card_corner_radius"]),
            BreathPeriodMs = IntOrNull(data["breath_period_ms"]),
            BgGrid = BoolOr(data["bg_grid"], false),
            Font = StrOrNull(data, "font"),
            Desk = ThemeDesk.Read(data["desk"]),
        };
    }

    private static string Str(JsonObject o, string key) => o[key]!.GetValue<string>();

    private static string? StrOrNull(JsonObject o, string key)
        => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static double DoubleOr(JsonNode? n, double fallback)
        => ThemeLint.TryReadNumber(n, out var v) ? v : fallback;

    private static bool BoolOr(JsonNode? n, bool fallback)
    {
        if (n is null) return fallback;
        try { return n.GetValue<bool>(); }
        catch { return fallback; }
    }

    private static int IntOr(JsonNode? n, int fallback)
        => ThemeLint.TryReadNumber(n, out var v) ? (int)Math.Round(v) : fallback;

    private static int? IntOrNull(JsonNode? n)
        => ThemeLint.TryReadNumber(n, out var v) ? (int)Math.Round(v) : null;
}
