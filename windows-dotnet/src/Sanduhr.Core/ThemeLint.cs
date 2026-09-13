using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

public enum ThemeFindingLevel { Error, Warning }

/// <summary>One thing the linter has to say about a theme: which field, how
/// bad, and a sentence that names the fix. Errors block the theme; warnings
/// ride along with an applied theme so an author (human or agent) can do a
/// second pass.</summary>
public sealed record ThemeFinding(ThemeFindingLevel Level, string Field, string Message);

public sealed record ThemeLintResult(ThemeDefinition? Theme, IReadOnlyList<ThemeFinding> Findings)
{
    /// <summary>True when no Error-level finding was raised and the theme parsed.</summary>
    public bool Ok => Theme is not null;

    public IEnumerable<ThemeFinding> Errors => Findings.Where(f => f.Level == ThemeFindingLevel.Error);
    public IEnumerable<ThemeFinding> Warnings => Findings.Where(f => f.Level == ThemeFindingLevel.Warning);
}

/// <summary>
/// The theme validator behind the paste box, the drop-in loader, the Theme
/// Studio, and the <c>propose_theme</c> MCP tool. Pure: JSON in, a
/// <see cref="ThemeDefinition"/> plus findings out, no IO and no WPF, so the
/// MCP server links this file and can refuse a broken palette without a widget
/// round trip.
///
/// Errors enforce the schema (the required fields, <c>#rrggbb</c> colors, dial
/// ranges). Warnings enforce the design rules the agent prompt asks for, as
/// measurements: a dark base (relative luminance), text contrast over the card
/// the widget actually composites, a monochrome text ramp, a pace marker that
/// stands out on the bar fills, one accent hue. The thresholds are documented
/// in <c>docs/themes/AGENT_PROMPT.md</c>; the six built-ins lint with zero
/// findings, which is the calibration test.
/// </summary>
public static class ThemeLint
{
    public const int MaxNameLength = 24;
    public const double DarkBaseMaxLuminance = 0.25;
    public const double TextContrastMin = 4.5;
    public const double TextSecondaryContrastMin = 3.0;
    public const double PaceMarkerContrastMin = 1.4;
    public const double PaceMarkerHueMin = 60;
    public const double HueToleranceDegrees = 30;
    /// <summary>Hue is only compared between colors this saturated or more:
    /// a gray has no hue to agree or disagree about.</summary>
    public const double HueSaturationFloor = 0.15;

    /// <summary>The neutral mid-gray desktop the card is assumed to sit on when
    /// measuring text contrast through a translucent card.</summary>
    private static readonly (double R, double G, double B) Desktop = (0x80 / 255.0, 0x80 / 255.0, 0x80 / 255.0);
    /// <summary>The lowest usage-ramp bar fill (green); the pace marker must read on it.</summary>
    private const string BarFillGreen = "#4ade80";

    private static readonly string[] ColorFields =
    {
        "bg", "glass", "glass_on_mica", "title_bg", "border",
        "text", "text_secondary", "text_dim", "text_muted",
        "accent", "bar_bg", "footer_bg", "pace_marker", "sparkline",
    };

    /// <summary>Lint raw JSON text. A parse failure or a non-object document is a
    /// single Error finding on the field <c>json</c>.</summary>
    public static ThemeLintResult Lint(string json)
    {
        JsonObject? data;
        try
        {
            data = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException e)
        {
            return new ThemeLintResult(null, new[] { Error("json", $"Not valid JSON: {e.Message}") });
        }
        if (data is null)
            return new ThemeLintResult(null, new[] { Error("json", "Theme JSON must be an object.") });
        return Lint(data);
    }

    public static ThemeLintResult Lint(JsonObject data)
    {
        var findings = new List<ThemeFinding>();

        // -- schema (errors) ---------------------------------------------------
        string? name = StringOrNull(data["name"]);
        if (data["name"] is null || name is null)
            findings.Add(Error("name", "name is required (1 to 12 characters, Title Case)."));
        else if (name.Trim().Length == 0)
            findings.Add(Error("name", "name must not be empty."));
        else if (name.Length > MaxNameLength)
            findings.Add(Error("name", $"name is {name.Length} characters; the strip shows 12, {MaxNameLength} is the limit."));

        var colors = new Dictionary<string, (double R, double G, double B)>(StringComparer.Ordinal);
        foreach (var field in ColorFields)
        {
            if (data[field] is null)
            {
                findings.Add(Error(field, $"{field} is required (a #rrggbb color)."));
                continue;
            }
            string? hex = StringOrNull(data[field]);
            if (hex is null || !TryParseHex(hex, out var rgb))
            {
                findings.Add(Error(field, $"{field} must be a #rrggbb color (six hex digits), got {Describe(data[field])}."));
                continue;
            }
            colors[field] = rgb;
        }

        double glassAlpha = CheckRange(data, "glass_alpha", 0, 1, 0.80, findings);
        CheckRange(data, "border_alpha", 0, 1, 0.40, findings);
        CheckOptionalHex(data, "border_tint", findings);
        if (data["accent_bloom"] is JsonNode ab)
        {
            if (ab is JsonObject abo)
            {
                CheckRange(abo, "blur", 0, 20, 4, findings, "accent_bloom.blur");
                CheckRange(abo, "alpha", 0, 1, 0.45, findings, "accent_bloom.alpha");
            }
            else
            {
                findings.Add(Error("accent_bloom", "accent_bloom must be an object {\"blur\": 3-8, \"alpha\": 0.25-0.65}."));
            }
        }
        if (data["inner_highlight"] is JsonNode ih)
        {
            if (ih is JsonObject iho)
            {
                if (iho["color"] is null)
                    findings.Add(Error("inner_highlight.color", "inner_highlight needs a color (#rrggbb) or the whole field set to null."));
                else
                    CheckOptionalHex(iho, "color", findings, "inner_highlight.color");
                CheckRange(iho, "alpha", 0, 1, 0.20, findings, "inner_highlight.alpha");
            }
            else
            {
                findings.Add(Error("inner_highlight", "inner_highlight must be {\"color\": \"#rrggbb\", \"alpha\": 0.15-0.30} or null."));
            }
        }
        CheckRange(data, "card_corner_radius", 0, 24, 10, findings);
        CheckRange(data, "breath_period_ms", 500, 20000, 4000, findings);
        bool optsOut = data["opts_out_of_mica"] is JsonNode oom && BoolOr(oom, false);

        if (findings.Any(f => f.Level == ThemeFindingLevel.Error))
            return new ThemeLintResult(null, findings);

        // -- design rules (warnings) ---------------------------------------------
        foreach (var field in new[] { "bg", "glass", "glass_on_mica" })
        {
            double l = Luminance(colors[field]);
            if (l > DarkBaseMaxLuminance)
                findings.Add(Warning(field, $"{field} is light (luminance {l:0.00}, keep it under {DarkBaseMaxLuminance:0.00}); translucent glass layering does not work on light surfaces."));
        }

        var card = Composite(colors["glass_on_mica"], optsOut ? 1.0 : glassAlpha, Desktop);
        double cardL = Luminance(card);
        double textRatio = Contrast(Luminance(colors["text"]), cardL);
        if (textRatio < TextContrastMin)
            findings.Add(Warning("text", $"text reads at {textRatio:0.0}:1 on the card (needs {TextContrastMin:0.0}:1); pick a brighter text or a darker glass_on_mica."));
        double secondaryRatio = Contrast(Luminance(colors["text_secondary"]), cardL);
        if (secondaryRatio < TextSecondaryContrastMin)
            findings.Add(Warning("text_secondary", $"text_secondary reads at {secondaryRatio:0.0}:1 on the card (needs {TextSecondaryContrastMin:0.0}:1)."));

        var ramp = new[] { "text", "text_secondary", "text_dim", "text_muted" };
        for (int i = 1; i < ramp.Length; i++)
        {
            if (Luminance(colors[ramp[i]]) >= Luminance(colors[ramp[i - 1]]))
                findings.Add(Warning(ramp[i], $"{ramp[i]} is not darker than {ramp[i - 1]}; the text ramp is one hue at decreasing luminance."));
        }
        for (int i = 1; i < ramp.Length; i++)
        {
            if (HueDistance(colors["text"], colors[ramp[i]]) is { } d && d > HueToleranceDegrees)
                findings.Add(Warning(ramp[i], $"{ramp[i]} is a different hue from text ({d:0} degrees apart); keep the ramp monochrome."));
        }

        // The marker stands out by luminance OR by hue (Ember's amber tick on the
        // green fill is a hue win, not a luminance one).
        TryParseHex(BarFillGreen, out var green);
        var marker = colors["pace_marker"];
        bool hiddenOnGreen = Contrast(Luminance(marker), Luminance(green)) < PaceMarkerContrastMin
                             && (HueDistance(marker, green) ?? 0) < PaceMarkerHueMin;
        bool hiddenOnBar = Contrast(Luminance(marker), Luminance(colors["bar_bg"])) < PaceMarkerContrastMin
                           && (HueDistance(marker, colors["bar_bg"]) ?? 0) < PaceMarkerHueMin;
        if (hiddenOnGreen || hiddenOnBar)
            findings.Add(Warning("pace_marker", $"pace_marker disappears on the {(hiddenOnGreen ? "green bar fill" : "empty bar")}; it should stand out on every fill by brightness or by a complementary hue."));

        if (HueDistance(colors["accent"], colors["sparkline"]) is { } sd && sd > HueToleranceDegrees)
            findings.Add(Warning("sparkline", $"sparkline is a different hue from accent ({sd:0} degrees apart); one accent hue reads as one brand."));
        if (StringOrNull(data["border_tint"]) is { } tint && TryParseHex(tint, out var tintRgb)
            && HueDistance(colors["accent"], tintRgb) is { } td && td > HueToleranceDegrees)
            findings.Add(Warning("border_tint", $"border_tint is a different hue from accent ({td:0} degrees apart); tint borders with the accent or leave it null."));

        if (optsOut && glassAlpha < 1.0)
            findings.Add(Warning("glass_alpha", "opts_out_of_mica is true, so set glass_alpha to 1.0; cards render translucent on non-Mica fallbacks otherwise."));

        return new ThemeLintResult(ThemeCatalog.BuildDefinition(data), findings);
    }

    /// <summary>Findings as <c>[{level, field, message}]</c> for tool results and files.</summary>
    public static JsonArray ToJson(IReadOnlyList<ThemeFinding> findings)
    {
        var arr = new JsonArray();
        foreach (var f in findings)
        {
            // Non-generic Add: the trimmed MCP server carries no serializer metadata.
            arr.Add((JsonNode)new JsonObject
            {
                ["level"] = f.Level == ThemeFindingLevel.Error ? "error" : "warning",
                ["field"] = f.Field,
                ["message"] = f.Message,
            });
        }
        return arr;
    }

    // -- color math (internal for the tests) --------------------------------------

    public static bool TryParseHex(string? hex, out (double R, double G, double B) rgb)
    {
        rgb = default;
        if (hex is null || hex.Length != 7 || hex[0] != '#')
            return false;
        for (int i = 1; i < 7; i++)
        {
            if (!Uri.IsHexDigit(hex[i]))
                return false;
        }
        int r = int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int g = int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int b = int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        rgb = (r / 255.0, g / 255.0, b / 255.0);
        return true;
    }

    /// <summary>WCAG relative luminance of an sRGB color, 0 (black) to 1 (white).</summary>
    public static double Luminance((double R, double G, double B) c)
        => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    private static double Linear(double v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

    /// <summary>WCAG contrast ratio between two luminances, 1 to 21.</summary>
    public static double Contrast(double l1, double l2)
    {
        double hi = Math.Max(l1, l2), lo = Math.Min(l1, l2);
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>Alpha-composite <paramref name="top"/> at <paramref name="alpha"/> over <paramref name="under"/>.</summary>
    public static (double R, double G, double B) Composite((double R, double G, double B) top, double alpha, (double R, double G, double B) under)
    {
        alpha = Math.Clamp(alpha, 0, 1);
        return (top.R * alpha + under.R * (1 - alpha), top.G * alpha + under.G * (1 - alpha), top.B * alpha + under.B * (1 - alpha));
    }

    /// <summary>HSV saturation, 0 to 1.</summary>
    public static double Saturation((double R, double G, double B) c)
    {
        double max = Math.Max(c.R, Math.Max(c.G, c.B));
        double min = Math.Min(c.R, Math.Min(c.G, c.B));
        return max <= 0 ? 0 : (max - min) / max;
    }

    /// <summary>HSV hue in degrees, 0 to 360.</summary>
    public static double Hue((double R, double G, double B) c)
    {
        double max = Math.Max(c.R, Math.Max(c.G, c.B));
        double min = Math.Min(c.R, Math.Min(c.G, c.B));
        double d = max - min;
        if (d <= 0) return 0;
        double h;
        if (max == c.R) h = ((c.G - c.B) / d) % 6;
        else if (max == c.G) h = (c.B - c.R) / d + 2;
        else h = (c.R - c.G) / d + 4;
        h *= 60;
        return h < 0 ? h + 360 : h;
    }

    /// <summary>Angular hue distance in degrees, or null when either color is
    /// too gray for hue to mean anything.</summary>
    public static double? HueDistance((double R, double G, double B) a, (double R, double G, double B) b)
    {
        if (Saturation(a) < HueSaturationFloor || Saturation(b) < HueSaturationFloor)
            return null;
        double d = Math.Abs(Hue(a) - Hue(b)) % 360;
        return d > 180 ? 360 - d : d;
    }

    // -- helpers ----------------------------------------------------------------

    private static ThemeFinding Error(string field, string message) => new(ThemeFindingLevel.Error, field, message);
    private static ThemeFinding Warning(string field, string message) => new(ThemeFindingLevel.Warning, field, message);

    private static string? StringOrNull(JsonNode? n)
        => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>Read any JSON number as a double. A node parsed from text answers
    /// GetValue&lt;double&gt; for every numeric token, but a node built in code
    /// (<c>["blur"] = 4</c>) only answers the CLR type it was created with, so
    /// the studio's documents and the MCP tool's hand-built JSON need the
    /// widening here. Strings, booleans and nulls are not numbers.</summary>
    internal static bool TryReadNumber(JsonNode? n, out double value)
    {
        value = 0;
        if (n is not JsonValue v)
            return false;
        if (v.TryGetValue<double>(out value)) return true;
        if (v.TryGetValue<int>(out var i)) { value = i; return true; }
        if (v.TryGetValue<long>(out var l)) { value = l; return true; }
        if (v.TryGetValue<float>(out var f)) { value = f; return true; }
        if (v.TryGetValue<decimal>(out var m)) { value = (double)m; return true; }
        if (v.TryGetValue<short>(out var sh)) { value = sh; return true; }
        if (v.TryGetValue<byte>(out var b)) { value = b; return true; }
        return false;
    }

    private static bool BoolOr(JsonNode n, bool fallback)
    {
        try { return n.GetValue<bool>(); }
        catch { return fallback; }
    }

    private static string Describe(JsonNode? n) => n switch
    {
        null => "null",
        JsonValue v when v.TryGetValue<string>(out var s) => $"\"{s}\"",
        JsonValue v => v.ToJsonString(),
        JsonObject => "an object",
        JsonArray => "an array",
        _ => n.ToJsonString(),
    };

    /// <summary>Range-check a numeric dial when present; returns the value (or
    /// the default when absent or invalid) so later rules can use it.</summary>
    private static double CheckRange(JsonObject o, string key, double min, double max, double fallback, List<ThemeFinding> findings, string? label = null)
    {
        label ??= key;
        if (o[key] is not JsonNode n)
            return fallback;
        if (!TryReadNumber(n, out double value))
        {
            findings.Add(Error(label, $"{label} must be a number between {min:0.##} and {max:0.##}, got {Describe(n)}."));
            return fallback;
        }
        if (double.IsNaN(value) || value < min || value > max)
        {
            findings.Add(Error(label, $"{label} must be between {min:0.##} and {max:0.##}, got {value:0.###}."));
            return fallback;
        }
        return value;
    }

    private static void CheckOptionalHex(JsonObject o, string key, List<ThemeFinding> findings, string? label = null)
    {
        label ??= key;
        if (o[key] is not JsonNode n)
            return;
        // JSON null is a JsonNode? of null in System.Text.Json.Nodes; a present null is allowed.
        string? s = StringOrNull(n);
        if (s is null || !TryParseHex(s, out _))
            findings.Add(Error(label, $"{label} must be a #rrggbb color or null, got {Describe(n)}."));
    }
}
