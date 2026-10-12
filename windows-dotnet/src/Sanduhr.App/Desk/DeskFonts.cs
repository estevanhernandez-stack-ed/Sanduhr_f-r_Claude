using System.Windows.Media;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// The Desk's font families. EsteFont Pro and EsteFont 26 ship inside the app as resources
/// (<c>Fonts/</c>, linked from <c>mac/Resources/Fonts</c>) and are drawn through a <c>pack://</c>
/// family, so nothing is installed system-wide. "" is the system font; any other saved family is
/// drawn when installed and falls back to EsteFont Pro when not (<see cref="DeskFont.Resolve"/>).
/// </summary>
internal sealed class DeskFonts
{
    // The component form names this assembly, so it resolves the same in the app and in the off-screen
    // harness (where "pack://application:,,,/Fonts/" did not resolve: the glyph typeface came back empty).
    private static readonly Uri PackBase = new($"pack://application:,,,/{typeof(DeskFonts).Assembly.GetName().Name};component/");
    private readonly HashSet<string> _installed;

    public DeskFonts(IEnumerable<string>? installed = null) =>
        _installed = new HashSet<string>(installed ?? Fonts.SystemFontFamilies.Select(f => f.Source),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>The installed families for Settings' picker, the bundled ones first.</summary>
    public IReadOnlyList<string> PickerFamilies() => DeskFont.PickerFamilies(_installed);

    /// <summary>The family to draw a saved choice in, and the one for bold text (the clock's time).
    /// A bundled family resolves both faces from its resources; WPF picks the Bold file by weight.</summary>
    public (FontFamily Regular, FontFamily Bold) Families(string? saved)
    {
        var name = DeskFont.Resolve(saved, _installed.Contains);
        if (name.Length == 0)
        {
            var system = new FontFamily("Segoe UI");
            return (system, system);
        }
        if (BundledFonts.Bundled(name) is not null)
        {
            var bundled = new FontFamily(PackBase, $"./{BundledFonts.Folder}/#{name}");
            return (bundled, bundled);
        }
        var family = new FontFamily(name);
        return (family, family);
    }
}
