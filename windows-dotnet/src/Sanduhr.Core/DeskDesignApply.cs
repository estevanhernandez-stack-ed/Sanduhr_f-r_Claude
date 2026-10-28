using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>What a design changes in the Desk's settings: the layout (with each stack's tilt), font and plate (<see cref="Desk"/>), the
/// ink, the Launcher and the weather nickname (null: the design leaves it alone). The preview draws these from memory; Apply saves them.</summary>
/// <param name="FollowsTheme">"Follow the widget theme" as the design sets it (theme match); null leaves it alone.</param>
public sealed record DeskDesignSettings(DeskSettings Desk, DeskInkSettings Ink, LauncherModel Launcher, string? WeatherNickname = null,
    bool? FollowsTheme = null);

/// <summary>
/// Turns a full design (<see cref="DeskDesign.Merge"/>) into the settings the Desk reads (spec, the agent-designed Desk, the live
/// review). Pure: the preview and Apply both go through here, so what is previewed is what is applied. Margins, the clock size
/// and whether the Desk is on are not part of a design and stay as they are.
/// </summary>
public static class DeskDesignApply
{
    /// <summary>The settings a design asks for, over the current ones. A field the design leaves null keeps its current value.</summary>
    public static DeskDesignSettings Settings(DeskSettings desk, DeskInkSettings ink, LauncherModel launcher, DeskDesign design)
    {
        var nextDesk = desk with
        {
            Layout = design.Layout is null ? desk.Layout : Layout(design.Layout),
            Font = design.Font is null ? desk.Font : FontSetting(design.Font),
            Plate = design.Plate ?? desk.Plate,
        };
        return new DeskDesignSettings(nextDesk.Normalized(), Ink(ink, design), design.Launcher ?? launcher, design.Weather?.Nickname,
            design.FollowsTheme);
    }

    /// <summary>The layout string for a design's pieces: the placed ones, in the design's order, with their sizes, tilts and meter styles.</summary>
    public static string Layout(IReadOnlyList<DeskPieceLayout> pieces)
    {
        var placed = pieces
            .Where(p => !p.IsHidden && DeskArrangement.Known.Contains(p.Piece, StringComparer.Ordinal))
            .Select(p => new DeskPlacement(p.Piece, p.Anchor ?? DeskArrangement.DefaultAnchor(p.Piece), DeskArrangement.ClampScale(p.Size ?? 1), DeskMonitors.FromWord(p.Monitor),
                DeskArrangement.ClampTilt(p.Tilt ?? 0), DeskMeterStyles.For(p.Piece, p.Style ?? DeskMeterStyle.Bar)));
        return new DeskArrangement(placed).Layout;
    }

    /// <summary>The <c>desk_font</c> value for a font: null is the default family, "" the system font, anything else a family name.</summary>
    public static string? FontSetting(DeskFontChoice font) =>
        font.Family.Length == 0 ? "" : string.Equals(font.Family, DeskFont.DefaultFamily, StringComparison.Ordinal) ? null : font.Family;

    /// <summary>The ink settings a design asks for. A custom ink is one to four colors without the #; the other modes keep the
    /// current custom colors, so choosing Custom again later finds them.</summary>
    public static DeskInkSettings Ink(DeskInkSettings current, DeskDesign design)
    {
        var next = current with { Shadow = design.Shadow ?? current.Shadow };
        if (design.Ink is not { } choice) return next;
        switch (choice.Kind)
        {
            case DeskInkKind.Light: return next with { Mode = DeskInkMode.Light };
            case DeskInkKind.Dark: return next with { Mode = DeskInkMode.Dark };
            case DeskInkKind.Theme: return next with { Mode = DeskInkMode.Theme };
            case DeskInkKind.Custom:
                var colors = choice.Colors.Select(InkHex.Normalize).OfType<string>().Take(4).ToList();
                return colors.Count == 0 ? next with { Mode = DeskInkMode.Auto } : next with { Mode = DeskInkMode.Custom, Color = string.Join(",", colors) };
            default: return next with { Mode = DeskInkMode.Auto };
        }
    }

    /// <summary>The family to draw a preview in when the design's font is not installed: the nearest installed look, so the preview
    /// still reads. The design keeps the family it asked for; only the preview substitutes.</summary>
    public static string PreviewFont(string family, Func<string, bool> installed)
    {
        if (family.Length == 0 || BundledFonts.Bundled(family) is not null || installed(family)) return family;
        string lower = family.ToLowerInvariant();
        string[] want =
            lower.Contains("mono") || lower.Contains("code") || lower.Contains("consol") || lower.Contains("courier") ? ["Cascadia Mono", "Consolas", "Courier New"]
            : lower.Contains("script") || lower.Contains("hand") || lower.Contains("brush") || lower.Contains("marker") || lower.Contains("pen") ? [DeskFont.DefaultFamily]
            : lower.Contains("serif") && !lower.Contains("sans") || lower.Contains("times") || lower.Contains("georgia") || lower.Contains("garamond") || lower.Contains("playfair") || lower.Contains("book")
                ? ["Georgia", "Cambria", "Times New Roman"]
            : ["Segoe UI"];
        foreach (var w in want)
            if (BundledFonts.Bundled(w) is not null || installed(w)) return w;
        return "Segoe UI";
    }
}

/// <summary>Which theme file a design's widget theme becomes.</summary>
/// <param name="Key">The theme key (a built-in's, or the file name under themes\).</param>
/// <param name="Write">True when the theme is not on this PC yet and must be saved as <c>themes\&lt;Key&gt;.json</c>.</param>
/// <param name="Definition">The theme, linted.</param>
public sealed record DesignThemePlan(string Key, bool Write, ThemeDefinition Definition);

/// <summary>Finds the theme a design's widget theme already is, or the name to save it under. Pure.</summary>
public static class DesignTheme
{
    private const int MaxTries = 30;

    /// <summary>
    /// Null when the theme does not pass the lint. Otherwise the key of a theme with exactly these colors if one exists (a
    /// built-in, or a user theme, so a design saved before is not saved twice), else a free key from the theme's name:
    /// <c>dusk</c>, then <c>dusk-2</c>, <c>dusk-3</c>... A built-in or user theme of the same name with other colors is never
    /// overwritten.
    /// </summary>
    /// <param name="byKey">The theme with this key on this PC (built-ins and user themes), or null.</param>
    public static DesignThemePlan? Plan(JsonObject theme, Func<string, ThemeDefinition?> byKey)
    {
        var lint = ThemeLint.Lint(theme);
        if (lint.Theme is not { } def) return null;
        string canonical = Canonical(def);
        string slug = ThemeHandoff.Slugify(def.Name);
        for (int n = 1; n <= MaxTries; n++)
        {
            string suffix = n == 1 ? "" : "-" + n;
            string key = (slug.Length + suffix.Length > 40 ? slug[..(40 - suffix.Length)].TrimEnd('-') : slug) + suffix;
            if (!ThemeHandoff.IsValidKey(key)) continue;
            var existing = byKey(key);
            if (existing is null) return new DesignThemePlan(key, true, def);
            if (Canonical(existing) == canonical) return new DesignThemePlan(key, false, def);
        }
        return null;
    }

    /// <summary>A theme as one comparable string: the draft document of its values, whatever order its file listed them in.</summary>
    public static string Canonical(ThemeDefinition def) => ThemeDraft.FromDefinition("x", def).ToJson().ToJsonString();

    /// <summary>A theme document in its canonical shape (for comparing designs); the document itself when it does not pass the lint.</summary>
    public static JsonObject Canonical(JsonObject theme) =>
        ThemeLint.Lint(theme).Theme is { } def ? ThemeDraft.FromDefinition("x", def).ToJson() : theme;
}
