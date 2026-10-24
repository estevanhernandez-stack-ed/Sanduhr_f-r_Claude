using System.Windows.Media;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// The Desk's font families. EsteFont Pro and EsteFont 26 ship inside the app as resources
/// (<c>Fonts/</c>, linked from <c>mac/Resources/Fonts</c>) and are drawn through a <c>pack://</c>
/// family, so nothing is installed system-wide. "" is the system font; any other saved family is
/// drawn when installed and falls back to EsteFont Pro when not (<see cref="DeskFont.Resolve"/>). Families from installed font packs
/// (3.9.1) come after those: a bundled or a system-installed family of the same name always wins, and a pack's family is drawn from
/// its folder through a <c>file:</c> URI.
/// </summary>
internal sealed class DeskFonts
{
    // The component form names this assembly, so it resolves the same in the app and in the off-screen
    // harness (where "pack://application:,,,/Fonts/" did not resolve: the glyph typeface came back empty).
    private static readonly Uri PackBase = new($"pack://application:,,,/{typeof(DeskFonts).Assembly.GetName().Name};component/");
    private readonly HashSet<string> _installed;
    private readonly Dictionary<string, Uri> _packs = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="installed">The installed families. Default: WPF's system families.</param>
    /// <param name="packFamilies">The families of installed font packs and where to load each from (<see cref="PackFontCatalog"/>).
    /// Default: none, so a test or the harness never reads this PC's widgets folder.</param>
    public DeskFonts(IEnumerable<string>? installed = null, IReadOnlyDictionary<string, Uri>? packFamilies = null)
    {
        _installed = new HashSet<string>(installed ?? Fonts.SystemFontFamilies.Select(f => f.Source),
            StringComparer.OrdinalIgnoreCase);
        foreach (var (name, baseUri) in packFamilies ?? new Dictionary<string, Uri>())
            if (BundledFonts.Bundled(name) is null && !_installed.Contains(name))
                _packs[name] = baseUri;
    }

    /// <summary>The Desk's fonts on this PC: the system's and the bundled ones, plus the installed font packs'.</summary>
    public static DeskFonts ForThisPc() => new(packFamilies: PackFontCatalog.ForThisPc());

    /// <summary>The installed families for Settings' picker, the bundled ones first, then the font packs' among the rest.</summary>
    public IReadOnlyList<string> PickerFamilies() => DeskFont.PickerFamilies(_installed.Concat(_packs.Keys));

    /// <summary>The family to draw a saved choice in, and the one for bold text (the clock's time).
    /// A bundled family resolves both faces from its resources; WPF picks the Bold file by weight.</summary>
    public (FontFamily Regular, FontFamily Bold) Families(string? saved)
    {
        var name = DeskFont.Resolve(saved, f => _installed.Contains(f) || _packs.ContainsKey(f));
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
        if (_packs.TryGetValue(name, out var baseUri))
        {
            var pack = new FontFamily(baseUri, $"./#{name}");
            return (pack, pack);
        }
        var family = new FontFamily(name);
        return (family, family);
    }
}
