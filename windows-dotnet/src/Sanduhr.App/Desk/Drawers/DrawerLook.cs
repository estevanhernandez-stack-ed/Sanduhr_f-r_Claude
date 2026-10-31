using System.Windows.Media;
using System.Windows.Media.Effects;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// What a drawer draws from (5.0 drawers spec, section 2): the frame is the plate (its color, or black behind light ink and white behind
/// dark ink, at no less than <see cref="MinPlateOpacity"/> so labels read over any wallpaper and the frame takes the mouse), the title
/// and labels are the ink, hover and selection the accent (a followed theme's, else the ink's last color), and the Desk font.
/// </summary>
internal sealed record DrawerLook(Color Plate, Color Ink, Color Accent, Effect? Shadow, FontFamily Font)
{
    public const double MinPlateOpacity = 0.55;

    public static DrawerLook From(ResolvedInk ink, string? plateHex, double? plateOpacity, string? accentHex, FontFamily font)
    {
        var rgb = plateHex is { } h ? DeskView.Rgb(h) : ink.DarkInk ? Colors.White : Colors.Black;
        double a = Math.Max(plateOpacity ?? DeskPlate.Opacity, MinPlateOpacity);
        var plate = Color.FromArgb((byte)Math.Round(a * 255), rgb.R, rgb.G, rgb.B);
        var accent = accentHex is { } x ? DeskView.Rgb(x) : DeskView.Rgb(ink.Stops[^1]);
        Effect? shadow = null;
        if (ink.ShadowOpacity > 0)
        {
            var s = DeskView.Shadow(ink);
            s.Freeze();
            shadow = s;
        }
        return new DrawerLook(plate, DeskView.Rgb(ink.Stops[0]), accent, shadow, font);
    }
}
