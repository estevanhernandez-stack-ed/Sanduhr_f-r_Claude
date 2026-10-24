using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// The font families of installed font packs (3.9.1), each with the folder to load it from. The folder is the copy
/// <see cref="PackFonts"/> makes, not the pack's own: WPF holds a font file open once it has drawn from it, and Uninstall renames the
/// pack folder.
/// </summary>
internal static class PackFontCatalog
{
    /// <summary>The family names in the enabled font packs, each with the base <c>file:</c> URI to draw it with
    /// (<c>new FontFamily(baseUri, "./#" + name)</c>). A font WPF can't read adds nothing.</summary>
    public static IReadOnlyDictionary<string, Uri> Families(WidgetStore widgets)
    {
        var map = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in PackFonts.Folders(widgets))
        {
            try
            {
                var baseUri = new Uri(folder.Dir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
                foreach (var family in Fonts.GetFontFamilies(baseUri))
                {
                    string? name = family.FamilyNames.TryGetValue(XmlLanguage.GetLanguage("en-us"), out var en)
                        ? en : family.FamilyNames.Values.FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(name) || map.ContainsKey(name)) continue;
                    // The validator only checks a font's size and magic number. A body WPF can't read still shows its family name,
                    // and then throws FileFormatException when the glyph typeface is built, which would be at draw time. The Desk draws
                    // Bold on every render (the clock, headings, letter styles) and Italic in letter styles, and a pack may carry a file
                    // for each: list the family only when every face it has, and the four the Desk asks for, build.
                    if (FacesBuild(family)) map[name] = baseUri;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or UriFormatException
                or NotSupportedException or InvalidOperationException or FormatException)
            {
                // an unreadable font adds nothing (FileFormatException is a FormatException)
            }
        }
        return map;
    }

    private static bool FacesBuild(FontFamily family)
    {
        var asked = new[]
        {
            new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            new Typeface(family, FontStyles.Italic, FontWeights.Normal, FontStretches.Normal),
            new Typeface(family, FontStyles.Italic, FontWeights.Bold, FontStretches.Normal),
        };
        foreach (var face in asked.Concat(family.GetTypefaces()))
        {
            try
            {
                if (!face.TryGetGlyphTypeface(out _)) return false;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                or InvalidOperationException or FormatException)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>The pack families of the installed packs under <paramref name="paths"/>.</summary>
    public static IReadOnlyDictionary<string, Uri> ForThisPc(Paths paths) =>
        Families(new WidgetStore(paths.WidgetsDir, WidgetCatalogService.EncodePng));

    /// <summary>The installed families and the packs', one copy of each name, sorted without case.</summary>
    public static IReadOnlyList<string> Union(IEnumerable<string> installed, IReadOnlyDictionary<string, Uri> packs) =>
        [.. installed.Concat(packs.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase)];
}
