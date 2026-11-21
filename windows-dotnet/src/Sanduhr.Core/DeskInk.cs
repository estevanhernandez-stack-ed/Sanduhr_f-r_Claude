using System.Globalization;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>How the Desk's text is inked (settings.json <c>desk_ink.mode</c>, spec 6.7). Automatic is the default.</summary>
public enum DeskInkMode { Auto, Light, Dark, Theme, Custom }

/// <summary>
/// The neon glow's numbers (5.0 spec 5, section 7). A strength from 0.2 to 1.0 gives the blur radius of the copy drawn behind
/// the ink (6 to 22 DIPs) and that copy's opacity (0.45 to 0.9). Kept in this file because the MCP server links it.
/// </summary>
public static class DeskGlow
{
    public const double MinStrength = 0.2;
    public const double MaxStrength = 1.0;
    public const double DefaultStrength = 0.6;
    public const double MinRadius = 6;
    public const double MaxRadius = 22;
    public const double MinOpacity = 0.45;
    public const double MaxOpacity = 0.9;

    /// <summary>The room around an island for the ink's shadow, in DIPs: what the Desk has always left.</summary>
    public const double BasePad = 16;

    /// <summary>The room the glow needs beyond its blur radius, in DIPs.</summary>
    public const double PadBeyondRadius = 4;

    /// <summary>A strength held to the range and rounded to two places; not a number reads as the default.</summary>
    public static double Clamp(double strength) =>
        double.IsFinite(strength) ? Math.Round(Math.Clamp(strength, MinStrength, MaxStrength), 2) : DefaultStrength;

    private static double Along(double strength) => (Clamp(strength) - MinStrength) / (MaxStrength - MinStrength);

    /// <summary>The blur radius of the glowing copy, in DIPs.</summary>
    public static double Radius(double strength) => MinRadius + Along(strength) * (MaxRadius - MinRadius);

    /// <summary>The opacity of the glowing copy.</summary>
    public static double Opacity(double strength) => MinOpacity + Along(strength) * (MaxOpacity - MinOpacity);

    /// <summary>The room around an island: <see cref="BasePad"/> with the glow off (a strength of 0 or less), otherwise the larger of
    /// that and the blur radius plus <see cref="PadBeyondRadius"/>, so the glow is never clipped by its window.</summary>
    public static double Pad(double strength) => strength <= 0 ? BasePad : Math.Max(BasePad, Radius(strength) + PadBeyondRadius);
}

/// <summary>
/// The Desk ink setting (settings.json <c>desk_ink</c> <c>{mode, color, shadow, glow, glow_strength}</c>). <paramref name="Color"/>
/// is the Mac's hex spec (one to four colors, comma separated) and only matters in Custom mode;
/// <paramref name="Shadow"/> is "Drop shadow under the text"; <paramref name="Glow"/> is "Neon glow" (5.0 spec 5) at
/// <paramref name="GlowStrength"/>. Glow and shadow are either-or: use <see cref="WithShadow"/> and <see cref="WithGlow"/> to change
/// one. A missing or odd member reads as its default and an unknown mode as Automatic, so an existing install is unchanged.
/// </summary>
public sealed record DeskInkSettings(DeskInkMode Mode, string Color, bool Shadow, bool Glow = false, double GlowStrength = DeskGlow.DefaultStrength)
{
    public static DeskInkSettings Default { get; } = new(DeskInkMode.Auto, "ffffff", true);

    /// <summary>The shadow switched: on turns the glow off, off leaves the glow as it is.</summary>
    public DeskInkSettings WithShadow(bool on) => this with { Shadow = on, Glow = !on && Glow };

    /// <summary>The glow switched: on turns the shadow off, off leaves the shadow as it is.</summary>
    public DeskInkSettings WithGlow(bool on) => this with { Glow = on, Shadow = !on && Shadow };

    /// <summary>The glow's strength, clamped. It is kept while the glow is off.</summary>
    public DeskInkSettings WithGlowStrength(double strength) => this with { GlowStrength = DeskGlow.Clamp(strength) };

    /// <summary>The settings word for <paramref name="mode"/>: "auto", "light", "dark", "theme" or "custom".</summary>
    public static string ModeWord(DeskInkMode mode) => mode switch
    {
        DeskInkMode.Light => "light",
        DeskInkMode.Dark => "dark",
        DeskInkMode.Theme => "theme",
        DeskInkMode.Custom => "custom",
        _ => "auto",
    };

    /// <summary>The mode for a settings word (case-insensitive); null, empty or unknown is Automatic.</summary>
    public static DeskInkMode ParseMode(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "light" => DeskInkMode.Light,
        "dark" => DeskInkMode.Dark,
        "theme" => DeskInkMode.Theme,
        "custom" => DeskInkMode.Custom,
        _ => DeskInkMode.Auto,
    };

    /// <summary>Reads the <c>desk_ink</c> node. Anything that is not an object, and any member of the wrong type, is the default.
    /// A node with both the shadow and the glow on keeps the glow, the newer choice.</summary>
    public static DeskInkSettings Decode(JsonNode? node)
    {
        if (node is not JsonObject o) return Default;
        var mode = DeskInkMode.Auto;
        var color = Default.Color;
        var shadow = Default.Shadow;
        bool glow = false;
        double strength = DeskGlow.DefaultStrength;
        if (o["mode"] is JsonValue m && m.TryGetValue<string>(out var word)) mode = ParseMode(word);
        if (o["color"] is JsonValue c && c.TryGetValue<string>(out var spec)) color = spec;
        if (o["shadow"] is JsonValue s && s.TryGetValue<bool>(out var on)) shadow = on;
        if (o["glow"] is JsonValue g && g.TryGetValue<bool>(out var lit)) glow = lit;
        if (o["glow_strength"] is JsonValue gs)
        {
            if (gs.TryGetValue<double>(out var d)) strength = DeskGlow.Clamp(d);
            else if (gs.TryGetValue<int>(out var i)) strength = DeskGlow.Clamp(i);
        }
        return new DeskInkSettings(mode, color, shadow && !glow, glow, strength);
    }

    /// <summary>The <c>desk_ink</c> object. <c>glow</c> is written only when on and <c>glow_strength</c> only when it is not the
    /// default, so an install that never used the glow keeps the three members it always had.</summary>
    public JsonObject Encode()
    {
        var o = new JsonObject
        {
            ["mode"] = ModeWord(Mode),
            ["color"] = Color,
            ["shadow"] = Shadow,
        };
        if (Glow) o["glow"] = true;
        if (GlowStrength != DeskGlow.DefaultStrength) o["glow_strength"] = GlowStrength;
        return o;
    }
}

/// <summary>
/// Hex color helpers for the Desk ink, ported from the Mac's <c>DeskThemeMapping.normalized</c> and
/// <c>inkHexes</c> (mac/Sources/Sanduhr/Models/DeskTheme.swift), plus WCAG relative luminance.
/// </summary>
public static class InkHex
{
    public const string Fallback = "ffffff";

    /// <summary>"#9AD7FF" is "9ad7ff", "fff" is "ffffff"; null when it is not a 3 or 6 digit hex color.</summary>
    public static string? Normalize(string? raw)
    {
        if (raw is null) return null;
        var h = raw.Trim(' ', '\t').ToLowerInvariant();
        if (h.StartsWith('#')) h = h[1..];
        if (h.Length != 3 && h.Length != 6) return null;
        foreach (var ch in h)
            if (!Uri.IsHexDigit(ch)) return null;
        if (h.Length == 3) h = string.Concat(h.Select(ch => $"{ch}{ch}"));
        return h;
    }

    /// <summary>
    /// The spec's colors as six-digit lowercase hex, always two or more: one color is doubled; entries that
    /// are not hex colors are skipped, and with none left <paramref name="fallback"/> (white) is used.
    /// Spaces around the commas and a leading # are fine.
    /// </summary>
    public static IReadOnlyList<string> Stops(string? spec, string fallback = Fallback)
    {
        var hexes = (spec ?? "").Split(',').Select(Normalize).OfType<string>().ToList();
        if (hexes.Count == 0) hexes.Add(Normalize(fallback) ?? Fallback);
        if (hexes.Count == 1) hexes.Add(hexes[0]);
        return hexes;
    }

    /// <summary>WCAG relative luminance (0 black to 1 white) of a hex color; an invalid color reads as white (1).</summary>
    public static double Luminance(string? hex)
    {
        var h = Normalize(hex);
        if (h is null) return 1;
        int r = int.Parse(h.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int g = int.Parse(h.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int b = int.Parse(h.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Luminance(r, g, b);
    }

    /// <summary>WCAG relative luminance of sRGB bytes.</summary>
    public static double Luminance(int r, int g, int b) =>
        0.2126 * Lin[Math.Clamp(r, 0, 255)] + 0.7152 * Lin[Math.Clamp(g, 0, 255)] + 0.0722 * Lin[Math.Clamp(b, 0, 255)];

    /// <summary>
    /// <paramref name="hex"/> with its luminance held to <paramref name="maxLuminance"/> (WCAG relative luminance): a color above it
    /// is scaled toward black in linear light, which keeps its hue, and one at or under it comes back as it was. An invalid color
    /// reads as white. Six-digit lowercase hex out.
    /// </summary>
    public static string CapLuminance(string? hex, double maxLuminance)
    {
        var h = Normalize(hex) ?? Fallback;
        double l = Luminance(h);
        if (l <= maxLuminance || l <= 0) return h;
        double f = maxLuminance / l;
        var sb = new System.Text.StringBuilder(6);
        for (int i = 0; i < 3; i++)
        {
            int c = int.Parse(h.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            sb.Append(Encode(Lin[c] * f).ToString("x2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static int Encode(double linear)
    {
        double v = linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
        return Math.Clamp((int)Math.Round(v * 255), 0, 255);
    }

    /// <summary>The 256-entry sRGB to linear table, shared with <see cref="WallpaperSampler"/>.</summary>
    internal static readonly double[] Lin = BuildTable();

    private static double[] BuildTable()
    {
        var t = new double[256];
        for (int i = 0; i < 256; i++)
        {
            double v = i / 255.0;
            t[i] = v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return t;
    }
}

/// <summary>The Mac's ink presets (SettingsView.swift DeskLookSection.presets), as name and hex spec.</summary>
public static class DeskInkPresets
{
    public static IReadOnlyList<(string Name, string Value)> All { get; } =
    [
        ("Ice gradient", "8f5bd6,3a63e0,33fdff"),
        ("Ice gradient, exact", "531b93,012089,00fdff"),
        ("Sky", "9ad7ff"),
        ("Marquee", "ffd08a"),
        ("White", "ffffff"),
    ];
}

/// <summary>
/// The ink the Desk draws with: gradient stops (two or more, six-digit hex), whether the ink is dark (so
/// the shadow and the message's default color flip), the shadow color and opacity (0 when the shadow is off),
/// and the opacity the text is drawn at.
/// </summary>
public sealed record ResolvedInk(
    IReadOnlyList<string> Stops, bool DarkInk, string ShadowHex, double ShadowOpacity, double TextOpacity)
{
    /// <summary>The neon glow's strength (0.2 to 1.0), 0 when the glow is off. A glowing ink has no shadow.</summary>
    public double Glow { get; init; }

    /// <summary>Draw the glow in one color, the ink's middle stop, instead of a blurred copy of the ink. The Desk sets it for
    /// a surface where the copy costs too much (under the desktop icons, when the measurement said so); nothing in Core does.</summary>
    public bool GlowOneColor { get; init; }
}

/// <summary>Turns the ink setting plus what the desktop looks like into the ink to draw (spec 6.7).</summary>
public static class DeskInk
{
    /// <summary>Wallpaper luminance above this picks Dark ink in Automatic.</summary>
    public const double AutoThreshold = 0.55;

    /// <summary>S5's ink: white at 0.92. Light and Dark ink are both drawn at this opacity.</summary>
    public const double TextOpacityPreset = 0.92;

    private const string DarkInkHex = "1b1f27";
    private const double ShadowOnLightInk = 0.65;
    private const double ShadowOnDarkInk = 0.45;

    /// <summary>
    /// The ink for <paramref name="s"/>. <paramref name="themeTextHex"/> is the widget theme's text color
    /// (Theme mode; null or invalid reads as Light). <paramref name="wallpaperLuminance"/> is the sampled
    /// brightness behind the island (Automatic; null or NaN is unknown, so Light). Theme and Custom inks are
    /// the user's or the theme's own color, drawn at full opacity. With the glow on, the ink carries its strength
    /// and no shadow (<see cref="Glowing"/>).
    /// </summary>
    public static ResolvedInk Resolve(DeskInkSettings s, string? themeTextHex, double? wallpaperLuminance) =>
        Glowing(Plain(s, themeTextHex, wallpaperLuminance), s);

    /// <summary>The glow switch of <paramref name="s"/> put on an ink, whoever made it (the Desk's own, or a followed widget
    /// theme's): on gives the ink the strength and takes its shadow away, since the Desk draws one or the other. Off returns
    /// <paramref name="ink"/> itself.</summary>
    public static ResolvedInk Glowing(ResolvedInk ink, DeskInkSettings s) =>
        s.Glow ? ink with { Glow = DeskGlow.Clamp(s.GlowStrength), ShadowOpacity = 0 } : ink;

    /// <summary>The ink without the glow: the six rules of spec 6.7.</summary>
    private static ResolvedInk Plain(DeskInkSettings s, string? themeTextHex, double? wallpaperLuminance)
    {
        switch (s.Mode)
        {
            case DeskInkMode.Light:
                return Make(["ffffff", "ffffff"], false, s.Shadow, TextOpacityPreset);
            case DeskInkMode.Dark:
                return Make([DarkInkHex, DarkInkHex], true, s.Shadow, TextOpacityPreset);
            case DeskInkMode.Theme:
                if (InkHex.Normalize(themeTextHex) is { } text)
                    return Make([text, text], InkHex.Luminance(text) < 0.5, s.Shadow, 1.0);
                return Plain(s with { Mode = DeskInkMode.Light }, null, null);
            case DeskInkMode.Custom:
                var stops = InkHex.Stops(s.Color);
                return Make(stops, InkHex.Luminance(stops[0]) < 0.5, s.Shadow, 1.0);
            default:
                bool dark = wallpaperLuminance is { } l && !double.IsNaN(l) && l > AutoThreshold;
                return Plain(s with { Mode = dark ? DeskInkMode.Dark : DeskInkMode.Light }, null, null);
        }
    }

    /// <summary>The luminance OLED care holds the Desk's ink to: that of #e6e6e6, so pure white is drawn as #e6e6e6.</summary>
    public static readonly double WhiteCapLuminance = InkHex.Luminance("e6e6e6");

    /// <summary>
    /// OLED care's render-time rule: no held pure white. Every ink stop (and the dark ink's white shadow) above
    /// <see cref="WhiteCapLuminance"/> is scaled down to it, hue kept; everything under it is as it was. What
    /// <see cref="Resolve"/> returned, and the design that asked for it, are not touched: the Desk just draws a notch dimmer.
    /// </summary>
    public static ResolvedInk CapWhite(ResolvedInk ink) =>
        ink with
        {
            Stops = [.. ink.Stops.Select(h => InkHex.CapLuminance(h, WhiteCapLuminance))],
            ShadowHex = InkHex.CapLuminance(ink.ShadowHex, WhiteCapLuminance),
        };

    private static ResolvedInk Make(IReadOnlyList<string> stops, bool darkInk, bool shadow, double textOpacity) =>
        new(stops, darkInk,
            darkInk ? "ffffff" : "000000",
            shadow ? (darkInk ? ShadowOnDarkInk : ShadowOnLightInk) : 0,
            textOpacity);

    /// <summary>
    /// The Desk message's color spec. Empty or unusable follows the ink ("9ad7ff" on light ink, "1f4f86" on
    /// dark); otherwise the user's spec, valid entries normalized and comma-joined (a gradient stays one).
    /// </summary>
    public static string MessageColor(string? setting, bool darkInk)
    {
        var valid = (setting ?? "").Split(',').Select(InkHex.Normalize).OfType<string>().ToList();
        return valid.Count > 0 ? string.Join(",", valid) : darkInk ? "1f4f86" : "9ad7ff";
    }
}
