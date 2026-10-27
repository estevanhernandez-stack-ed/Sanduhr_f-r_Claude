namespace Sanduhr.Core;

/// <summary>One surface Settings draws text on, as ThemePalette paints it.</summary>
public sealed record ContrastSurface(string Name, (double R, double G, double B) Color);

/// <summary>
/// Settings v2, slice 8 (WF37; spec, Keyboard and screen reader, Contrast): the surfaces Settings draws text on, computed from a theme's
/// tokens the way ThemePalette paints them, and the ratios the built-in themes are held to. The window is opaque, so Mica never shows
/// under it: a page is a Glass card (solid), lists and fields sit on Bg wells, and the sidebar is TitleBg at the chrome alpha over Bg
/// (slice 8 decision 8). Pure (colors and arithmetic); not on the MCP link list. It calls ThemeLint's luminance, which is.
/// A hex that does not parse reads as magenta, like ThemePalette, so nothing here throws on a hand-edited theme.
/// </summary>
public static class ThemeContrast
{
    /// <summary>WCAG AA for body text.</summary>
    public const double TextMin = 4.5;
    /// <summary>TextDim's floor, one step above TextMuted's <see cref="TextMin"/>: the ramp keeps two visible steps (owner taste 1).</summary>
    public const double DimFloor = 5.0;
    /// <summary>WCAG AA for a non-text indicator (the selected row's accent bar, the banner's border).</summary>
    public const double NonTextMin = 3.0;
    /// <summary>The chrome strips' glass alpha over Mica; a theme that opts out of Mica paints them solid.</summary>
    public const double ChromeAlphaOverMica = 0.92;
    /// <summary>The primary button's text in ThemedDialog (ThemedDialog.xaml), drawn on the theme's accent.</summary>
    public const string PrimaryButtonText = "#0d0d0d";

    public const string Page = "page";
    public const string Well = "well";
    public const string Sidebar = "sidebar";

    /// <summary>The chrome strips' alpha (TitleBg, GlassChrome, FooterBg). ThemePalette reads it here.</summary>
    public static double ChromeAlpha(ThemeDefinition def) => def.OptsOutOfMica ? 1.0 : ChromeAlphaOverMica;

    /// <summary>Page (Glass), well (Bg) and sidebar (TitleBg at <see cref="ChromeAlpha"/> over Bg).</summary>
    public static IReadOnlyList<ContrastSurface> SettingsSurfaces(ThemeDefinition def)
    {
        var bg = Rgb(def.Bg);
        return
        [
            new(Page, Rgb(def.Glass)),
            new(Well, bg),
            new(Sidebar, ThemeLint.Composite(Rgb(def.TitleBg), ChromeAlpha(def), bg)),
        ];
    }

    /// <summary>The WCAG ratio of <paramref name="hex"/> on <paramref name="on"/>, 1 to 21.</summary>
    public static double Ratio(string hex, (double R, double G, double B) on) =>
        ThemeLint.Contrast(ThemeLint.Luminance(Rgb(hex)), ThemeLint.Luminance(on));

    /// <summary>The lowest ratio of <paramref name="hex"/> on any of <see cref="SettingsSurfaces"/>.</summary>
    public static double Worst(string hex, ThemeDefinition def) => SettingsSurfaces(def).Min(s => Ratio(hex, s.Color));

    /// <summary>The selected sidebar row's label (spec: Obsidian's selected label uses Text with an accent bar): Accent where it reads
    /// <see cref="TextMin"/> on the selected row, which is Glass, else Text. Computed, so any theme gets it (slice 8 decision 11).
    /// Returns Text when either color does not parse (P8).</summary>
    public static string SelectedLabel(ThemeDefinition def) =>
        ThemeLint.TryParseHex(def.Accent, out var accent) && ThemeLint.TryParseHex(def.Glass, out var glass)
            && ThemeLint.Contrast(ThemeLint.Luminance(accent), ThemeLint.Luminance(glass)) >= TextMin
            ? def.Accent : def.Text;

    /// <summary>A surface as <c>#rrggbb</c>, for messages and the harness.</summary>
    public static string Hex((double R, double G, double B) c) => $"#{Byte(c.R):x2}{Byte(c.G):x2}{Byte(c.B):x2}";

    private static int Byte(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);

    private static (double R, double G, double B) Rgb(string hex) =>
        ThemeLint.TryParseHex(hex, out var rgb) ? rgb : (1, 0, 1);
}
