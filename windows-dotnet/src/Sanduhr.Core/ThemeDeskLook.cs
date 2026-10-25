using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// One theme's look for the Desk (theme match, spec "The rules"): the theme gradient, the ink for the clock, date and Claude line,
/// the meter bars, the message and its glow, the back plate and its tint, the text shadow, the Desk font, and the meter track and
/// pace tick. <see cref="From"/> turns any theme into one; a field of the theme's <c>desk</c> object wins over its rule. Colors are
/// six-digit lowercase hex without the #, the Desk's own form (<see cref="InkHex"/>); a list holds 1 to 4 stops. Pure (colors and
/// arithmetic; no IO, network, credentials or process): the MCP server links this file so get_desk and the design check compute
/// the same look as the widget.
/// </summary>
public sealed record ThemeDeskLook(
    IReadOnlyList<string> Gradient,
    IReadOnlyList<string> Ink,
    IReadOnlyList<string> Bars,
    IReadOnlyList<string> Message,
    string Glow,
    bool Plate,
    string PlateColor,
    string Shadow,
    string? Font,
    string BarTrack,
    string PaceTick)
{
    /// <summary>The text shadow: the theme's bg blended this far toward black (toward white behind dark ink, as the Desk's shadow
    /// flips today), a deep shade of the theme rather than black.</summary>
    public const double ShadowBlend = 0.5;

    /// <summary>The plate's tint: the theme's bg blended this far toward the plate's own color (black behind light ink, white behind
    /// dark ink, <see cref="DeskPlate.ColorHex"/>), drawn at <see cref="DeskPlate.Opacity"/>.</summary>
    public const double PlateBlend = 0.25;

    /// <summary>True when the ink is dark: its first stop under half luminance, as <see cref="DeskInk"/> decides for a custom ink.</summary>
    public bool DarkInk => InkHex.Luminance(Ink[0]) < 0.5;

    /// <summary>
    /// The theme's Desk look. The gradient is accent to sparkline (accent to text when they are one color). Ink: desk.ink, else text to
    /// accent. Bars and message: theirs, else the gradient. Glow: desk.glow, else accent. Plate: desk.plate, else on. Plate tint and
    /// shadow: theirs, else blends of bg. Font: desk.font, else font, else monospace_font, else none (the Desk keeps its own).
    /// Track and tick: bar_bg and pace_marker.
    /// </summary>
    public static ThemeDeskLook From(ThemeDefinition theme)
    {
        string accent = Hex(theme.Accent), sparkline = Hex(theme.Sparkline), text = Hex(theme.Text), bg = Hex(theme.Bg);
        var gradient = Stops([accent, accent == sparkline ? text : sparkline]);
        var d = theme.Desk;
        var ink = d?.Ink is { Count: > 0 } i ? Stops(i) : Stops([text, accent]);
        bool dark = InkHex.Luminance(ink[0]) < 0.5;
        return new ThemeDeskLook(
            gradient,
            ink,
            d?.Bars is { Count: > 0 } b ? Stops(b) : gradient,
            d?.Message is { Count: > 0 } m ? Stops(m) : gradient,
            d?.Glow is { } g ? Hex(g) : accent,
            d?.Plate ?? true,
            d?.PlateColor is { } pc ? Hex(pc) : Mix(bg, DeskPlate.ColorHex(dark), PlateBlend),
            d?.Shadow is { } s ? Hex(s) : Mix(bg, dark ? "ffffff" : "000000", ShadowBlend),
            d?.Font ?? theme.Font ?? theme.MonospaceFont,
            Hex(theme.BarBg),
            Hex(theme.PaceMarker));
    }

    /// <summary>
    /// The readability rule (theme match, the guard): the ink reads over a wallpaper of this luminance when its first stop, the color the
    /// clock's digits start in, keeps <see cref="DeskDesign.MinContrast"/> against it.
    /// </summary>
    public bool ReadsOver(double wallpaperLuminance) =>
        DeskDesign.Ratio(InkHex.Luminance(Ink[0]), wallpaperLuminance) >= DeskDesign.MinContrast;

    /// <summary>This look over a wallpaper of this luminance: the plate turned on when it is off and the ink would not read. No sample
    /// (null or NaN) leaves the look as the theme says.</summary>
    public ThemeDeskLook Guarded(double? wallpaperLuminance) =>
        Plate || wallpaperLuminance is not { } l || double.IsNaN(l) || ReadsOver(l) ? this : this with { Plate = true };

    /// <summary><paramref name="hex"/> blended <paramref name="amount"/> (0 to 1) of the way to <paramref name="toward"/>, per sRGB byte,
    /// rounding half away from zero. An unreadable color is white (<see cref="InkHex.Fallback"/>).</summary>
    public static string Mix(string hex, string toward, double amount)
    {
        string a = InkHex.Normalize(hex) ?? InkHex.Fallback, b = InkHex.Normalize(toward) ?? InkHex.Fallback;
        amount = Math.Clamp(amount, 0, 1);
        var sb = new StringBuilder(6);
        for (int i = 0; i < 3; i++)
        {
            int x = int.Parse(a.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int y = int.Parse(b.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int v = (int)Math.Round(x + (y - x) * amount, MidpointRounding.AwayFromZero);
            sb.Append(Math.Clamp(v, 0, 255).ToString("x2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>The look as get_desk reports it (<c>theme_look</c>): every color as <c>#rrggbb</c>.</summary>
    public JsonObject ToJson() => new()
    {
        ["gradient"] = Hashed(Gradient),
        ["ink"] = Hashed(Ink),
        ["bars"] = Hashed(Bars),
        ["message"] = Hashed(Message),
        ["glow"] = "#" + Glow,
        ["plate"] = Plate,
        ["plate_color"] = "#" + PlateColor,
        ["shadow"] = "#" + Shadow,
        ["font"] = Font,
        ["bar_track"] = "#" + BarTrack,
        ["pace_tick"] = "#" + PaceTick,
    };

    private static string Hex(string? h) => InkHex.Normalize(h) ?? InkHex.Fallback;

    /// <summary>Colors as stops: normalized, at most <see cref="ThemeLint.MaxDeskStops"/>, a color equal to the one before it dropped
    /// (a gradient of one color is that color); none usable is white.</summary>
    private static IReadOnlyList<string> Stops(IEnumerable<string> colors)
    {
        var list = new List<string>(ThemeLint.MaxDeskStops);
        foreach (var c in colors)
        {
            if (InkHex.Normalize(c) is not { } h) continue;
            if (list.Count > 0 && list[^1] == h) continue;
            if (list.Count == ThemeLint.MaxDeskStops) break;
            list.Add(h);
        }
        if (list.Count == 0) list.Add(InkHex.Fallback);
        return list;
    }

    private static JsonArray Hashed(IReadOnlyList<string> stops)
    {
        var a = new JsonArray();
        // Non-generic Add: the trimmed MCP server carries no serializer metadata.
        foreach (var s in stops) a.Add((JsonNode?)("#" + s));
        return a;
    }
}
