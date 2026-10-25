using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>Where the active theme came from, which decides whether theme match may write into it.</summary>
public enum ThemeOrigin { BuiltIn, Pack, User }

/// <summary>The Desk's own settings that Match now and Follow off write (theme match).</summary>
public sealed record ThemeMatchOwn(DeskSettings Desk, DeskInkSettings Ink, DeskMessageSettings Message, NowPlayingSettings NowPlaying);

/// <summary>
/// The pure part of "one theme for the widget and the Desk": who owns a theme, the name and file key of a theme Follow or "Save as a
/// new theme" makes, a theme carrying a Desk block as its canonical document, the matched look spelled out, and the Desk's own settings
/// Match now writes. No IO: the App's ThemeMatchService writes. Not linked into the MCP server.
/// </summary>
public static partial class ThemeMatch
{
    public const string DeskSuffix = " + Desk";
    private const int MaxTries = 99;

    [GeneratedRegex(@" \+ Desk( \d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex DeskSuffixRe();

    /// <summary>A built-in (even when a user file of the same name shadows it) and a pack's theme (<c>pack:stem</c>) are never edited.</summary>
    public static ThemeOrigin Origin(string key) =>
        ThemeCatalog.BuiltIns.ContainsKey(key) ? ThemeOrigin.BuiltIn : key.Contains(':') ? ThemeOrigin.Pack : ThemeOrigin.User;

    /// <summary>"&lt;Theme&gt; + Desk", then " + Desk 2", " + Desk 3"… while <paramref name="taken"/>; the theme's own name is cut so the
    /// whole fits <see cref="ThemeLint.MaxNameLength"/>, and a name that already ends in "+ Desk" is not suffixed twice.</summary>
    public static string NewThemeName(string themeName, Func<string, bool> taken)
    {
        string bare = DeskSuffixRe().Replace(CleanName(themeName) ?? "Theme", "");
        if (bare.Length == 0) bare = "Theme";
        string name = "";
        for (int n = 1; n <= MaxTries; n++)
        {
            string suffix = n == 1 ? DeskSuffix : $"{DeskSuffix} {n}";
            string head = bare.Length + suffix.Length > ThemeLint.MaxNameLength ? bare[..(ThemeLint.MaxNameLength - suffix.Length)].TrimEnd() : bare;
            name = head + suffix;
            if (!taken(name)) return name;
        }
        return name;
    }

    /// <summary>The file key for a new theme: the name slugged, then -2, -3… until it is free; never a built-in's key.</summary>
    public static string NewThemeKey(string name, Func<string, bool> taken)
    {
        string slug = ThemeHandoff.Slugify(name);
        for (int n = 1; n <= MaxTries; n++)
        {
            string suffix = n == 1 ? "" : "-" + n;
            string key = (slug.Length + suffix.Length > 40 ? slug[..(40 - suffix.Length)].TrimEnd('-') : slug) + suffix;
            if (ThemeHandoff.IsValidKey(key) && !ThemeCatalog.BuiltIns.ContainsKey(key) && !taken(key)) return key;
        }
        return "theme-" + Guid.NewGuid().ToString("N")[..8];
    }

    /// <summary>A name a person typed, as a theme name: control characters dropped, trimmed, cut to the strip's limit; null when empty.</summary>
    public static string? CleanName(string? raw)
    {
        if (raw is null) return null;
        var clean = new string(raw.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length > ThemeLint.MaxNameLength) clean = clean[..ThemeLint.MaxNameLength].TrimEnd();
        return clean.Length == 0 ? null : clean;
    }

    /// <summary><paramref name="def"/> carrying <paramref name="desk"/> (null or empty: none), renamed when <paramref name="name"/> is
    /// given, as its canonical document (<see cref="ThemeDraft"/>), so every other field of the theme is kept.</summary>
    public static JsonObject WithDesk(ThemeDefinition def, ThemeDesk? desk, string? name = null)
    {
        var draft = ThemeDraft.FromDefinition("theme", def);
        draft.Desk = desk is { IsEmpty: false } ? desk : null;
        if (name is not null) draft.Name = name;
        return draft.ToJson();
    }

    /// <summary>
    /// The theme's matched look as a desk block, every part spelled out (what Follow writes): ink, message, glow, plate, plate tint and
    /// shadow. <c>bars</c> and <c>font</c> only when the theme set them: writing bars would turn the widget's ramp into a gradient.
    /// </summary>
    public static ThemeDesk SpelledOut(ThemeDefinition def)
    {
        var look = ThemeDeskLook.From(def);
        static IReadOnlyList<string> Hashed(IReadOnlyList<string> stops) => [.. stops.Select(s => "#" + s)];
        return new ThemeDesk
        {
            Ink = Hashed(look.Ink),
            Message = Hashed(look.Message),
            Glow = "#" + look.Glow,
            Plate = look.Plate,
            PlateColor = "#" + look.PlateColor,
            Shadow = "#" + look.Shadow,
            Bars = def.Desk?.Bars,
            Font = def.Desk?.Font,
        };
    }

    /// <summary>
    /// What Match now (and Follow off) write into the Desk's own settings: the ink's stops as a Custom ink with its shadow on, the font
    /// (the Desk keeps its own when the look has none or this PC lacks it; <paramref name="fontAvailable"/> null = present), the plate, the message's colors with its glow on, and gradient bars. The plate
    /// tint, shadow shade and glow color have no Desk setting, so they draw as before once the Desk has its own look again.
    /// </summary>
    public static ThemeMatchOwn OwnSettings(ThemeDeskLook look, DeskSettings desk, DeskInkSettings ink, DeskMessageSettings message, NowPlayingSettings np, Func<string, bool>? fontAvailable = null) => new(
        desk with
        {
            Font = look.Font is { } f && (fontAvailable?.Invoke(f) ?? true) ? DeskDesignApply.FontSetting(new DeskFontChoice(f)) : desk.Font,
            Plate = look.Plate,
        },
        ink with { Mode = DeskInkMode.Custom, Color = string.Join(",", look.Ink), Shadow = true },
        message with { Color = string.Join(",", look.Message), Glow = true },
        np with { BarsGradient = true });
}