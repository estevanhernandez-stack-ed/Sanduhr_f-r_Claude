using System.Windows.Media;
using System.Windows.Media.Effects;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// What a drawer draws from (5.0 drawers spec, section 2; looks spec, section 1): the frame is the plate (its color, or black behind light
/// ink and white behind dark ink), the title and labels are the ink, hover and selection the accent (a followed theme's, else the ink's
/// last color), and the Desk font. <see cref="Kind"/> says how the plate is drawn: Solid at the opacity setting, Glass lighter with an edge
/// (and the host's blur), Outline a border and Bare a border on hover, both over a fill too faint to see but solid enough to take the click.
/// </summary>
internal sealed record DrawerLook(Color Plate, Color Ink, Color Accent, Effect? Shadow, FontFamily Font)
{
    public const double MinPlateOpacity = 0.55;
    /// <summary>Outline's and Bare's fill: invisible, but a per-pixel layered window still hands a fully transparent pixel's click to the
    /// desktop, so the frame keeps this much alpha and a click on an empty part of the drawer lands on the drawer. 5 of 255: OLED care's
    /// dimming (down to 0.40 of opacity) still leaves 2, where 0.01 (3 of 255) could round to 1 or 0.</summary>
    public const double HitFill = 0.02;

    public DrawerLookKind Kind { get; init; } = DrawerLookKind.Solid;
    /// <summary>Labels draw on chips: the plate is too faint for the ink to read against the wallpaper without one.</summary>
    public bool Chips { get; init; }
    public Color Border { get; init; } = Colors.Transparent;
    public Color HoverBorder { get; init; } = Colors.Transparent;
    /// <summary>Glass's top and left edge highlight.</summary>
    public Color Edge { get; init; } = Colors.Transparent;
    /// <summary>Outline's and Bare's label and title shadow: strong and in the opposite tone of the ink (dark behind light ink, light behind
    /// dark), whatever the theme's own shadow is, the way Windows keeps desktop icon labels readable on a busy wallpaper. Null for Solid and Glass.</summary>
    public Effect? LabelShadow { get; init; }
    /// <summary>What the labels and the title wear: <see cref="LabelShadow"/> where there is no plate, else the ink's own <see cref="Shadow"/>.</summary>
    public Effect? LabelEffect => LabelShadow ?? Shadow;

    /// <summary>The Solid look at the plate opacity a theme or the Desk gave (no less than <see cref="MinPlateOpacity"/>, the drawers'
    /// default of 55): what a drawer was before looks existed.</summary>
    public static DrawerLook From(ResolvedInk ink, string? plateHex, double? plateOpacity, string? accentHex, FontFamily font) =>
        From(ink, plateHex, accentHex, font, DrawerLookKind.Solid,
            (int)Math.Round(Math.Max(plateOpacity ?? DeskPlate.Opacity, MinPlateOpacity) * 100), 0);

    /// <param name="wallpaperLuminance">The wallpaper's relative luminance (0 to 1) under the drawer; decides the chips.</param>
    public static DrawerLook From(ResolvedInk ink, string? plateHex, string? accentHex, FontFamily font,
        DrawerLookKind kind, int opacity, double wallpaperLuminance)
    {
        var rgb = plateHex is { } h ? DeskView.Rgb(h) : ink.DarkInk ? Colors.White : Colors.Black;
        var inkColor = DeskView.Rgb(ink.Stops[0]);
        var accent = accentHex is { } x ? DeskView.Rgb(x) : DeskView.Rgb(ink.Stops[^1]);
        Effect? shadow = null;
        if (ink.ShadowOpacity > 0)
        {
            var s = DeskView.Shadow(ink);
            s.Freeze();
            shadow = s;
        }

        double fill = kind switch
        {
            DrawerLookKind.Solid => opacity / 100.0,
            DrawerLookKind.Glass => opacity / 100.0 * 0.8,
            _ => HitFill,
        };
        if (kind == DrawerLookKind.Glass) rgb = Blend(rgb, ink.DarkInk ? Colors.White : Colors.Black, 0.35);
        // Ratio is symmetric (lighter over darker), so the argument order does not matter; both are relative luminances.
        bool chips = fill < MinPlateOpacity
            && DeskDesign.Ratio(InkHex.Luminance(ink.Stops[0]), wallpaperLuminance) < DeskDesign.MinContrast;

        return new DrawerLook(Color.FromArgb((byte)Math.Round(fill * 255), rgb.R, rgb.G, rgb.B), inkColor, accent, shadow, font)
        {
            Kind = kind,
            Chips = chips,
            Border = kind == DrawerLookKind.Outline ? WithAlpha(inkColor, 0.7) : Colors.Transparent,
            HoverBorder = kind switch
            {
                DrawerLookKind.Outline => WithAlpha(inkColor, 0.7),
                DrawerLookKind.Bare => WithAlpha(inkColor, 0.35),
                _ => Colors.Transparent,
            },
            Edge = kind == DrawerLookKind.Glass ? WithAlpha(inkColor, 0.25) : Colors.Transparent,
            LabelShadow = kind is DrawerLookKind.Outline or DrawerLookKind.Bare ? OppositeShadow(ink.DarkInk) : null,
        };
    }

    private static Effect OppositeShadow(bool darkInk)
    {
        var e = new DropShadowEffect
        {
            Color = darkInk ? Colors.White : Colors.Black, BlurRadius = 4, ShadowDepth = 1, Direction = 270, Opacity = 0.9,
        };
        e.Freeze();
        return e;
    }

    private static Color WithAlpha(Color c, double a) => Color.FromArgb((byte)Math.Round(a * 255), c.R, c.G, c.B);

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
}
