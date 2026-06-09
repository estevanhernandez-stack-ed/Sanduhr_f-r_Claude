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
                TextDim = "#777777",
                TextMuted = "#555555",
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
                TextDim = "#64748b",
                TextMuted = "#475569",
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
                TextDim = "#8b6b60",
                TextMuted = "#6b4b40",
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
                TextDim = "#5a9a78",
                TextMuted = "#3a7a58",
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
                TextDim = "#00802b",
                TextMuted = "#005a1e",
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
                TextDim = "#6a6e99",
                TextMuted = "#484b70",
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
    /// Validate + normalize a parsed theme JSON, applying the
    /// <c>_DEFAULT_GLASS_TUNING</c> defaults for omitted dials. Returns null
    /// (reported via <paramref name="warn"/>) when a required field is absent or
    /// the JSON is malformed — same skip behavior as <c>themes._validate_theme</c>.
    /// </summary>
    public static ThemeDefinition? TryValidate(string key, JsonObject data, Action<string>? warn = null)
    {
        var missing = RequiredColorFields.Where(f => !data.ContainsKey(f)).ToList();
        if (missing.Count > 0)
        {
            warn?.Invoke($"User theme '{key}' missing required fields: {string.Join(", ", missing)}");
            return null;
        }

        try
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
            };
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException)
        {
            warn?.Invoke($"User theme '{key}' has a malformed field: {e.Message}");
            return null;
        }
    }

    private static string Str(JsonObject o, string key) => o[key]!.GetValue<string>();

    private static string? StrOrNull(JsonObject o, string key)
        => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static double DoubleOr(JsonNode? n, double fallback)
    {
        if (n is null) return fallback;
        try { return n.GetValue<double>(); }
        catch { return fallback; }
    }

    private static bool BoolOr(JsonNode? n, bool fallback)
    {
        if (n is null) return fallback;
        try { return n.GetValue<bool>(); }
        catch { return fallback; }
    }

    private static int IntOr(JsonNode? n, int fallback)
    {
        if (n is null) return fallback;
        try { return (int)Math.Round(n.GetValue<double>()); }
        catch { return fallback; }
    }

    private static int? IntOrNull(JsonNode? n)
    {
        if (n is null) return null;
        try { return (int)Math.Round(n.GetValue<double>()); }
        catch { return null; }
    }
}
