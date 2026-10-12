namespace Sanduhr.Core;

/// <summary>One handwriting family that ships inside Sanduhr: its family name and the face names of
/// its Regular and Bold files (each file is <c>&lt;face&gt;.ttf</c>).</summary>
public sealed record BundledFamily(string Name, string RegularFace, string BoldFace)
{
    /// <summary>The Regular and Bold file names.</summary>
    public IReadOnlyList<string> FileNames => [$"{RegularFace}.ttf", $"{BoldFace}.ttf"];
}

/// <summary>
/// The author's handwriting, shipped inside the app. EsteFont Pro is the default; EsteFont 26 stays
/// beside it as the heritage choice (the Mac's <c>BundledFonts</c>).
/// </summary>
public static class BundledFonts
{
    public static readonly BundledFamily EsteFontPro = new("EsteFont Pro", "EsteFontPro-Regular", "EsteFontPro-Bold");
    public static readonly BundledFamily EsteFont26 = new("EsteFont 26", "EsteFont26-Regular", "EsteFont26-Bold");

    /// <summary>Every bundled family, in picker order: the default first, the heritage choice after it.</summary>
    public static readonly IReadOnlyList<BundledFamily> Families = [EsteFontPro, EsteFont26];

    /// <summary>The default family (new installs, fallback).</summary>
    public static string Family => EsteFontPro.Name;

    /// <summary>The folder the files sit in beside the app.</summary>
    public const string Folder = "Fonts";

    /// <summary>Every bundled file name, in family order.</summary>
    public static IReadOnlyList<string> FileNames { get; } = Families.SelectMany(f => f.FileNames).ToArray();

    /// <summary>The bundled family by name, null for any other family.</summary>
    public static BundledFamily? Bundled(string family) => Families.FirstOrDefault(f => f.Name == family);

    /// <summary>The face to draw a family in: a bundled family by face name, Bold where the design
    /// wants weight; any other family by its family name.</summary>
    public static string Face(string family, bool bold) =>
        Bundled(family) is { } b ? (bold ? b.BoldFace : b.RegularFace) : family;
}

/// <summary>The Desk's font: new installs draw in EsteFont Pro; "" is the system font; a saved
/// family that is no longer installed falls back to EsteFont Pro.</summary>
public static class DeskFont
{
    public static string DefaultFamily => BundledFonts.Family;
    public static string HeritageFamily => BundledFonts.EsteFont26.Name;

    /// <summary>The family the Desk draws in for a saved value. null (never chosen) is EsteFont Pro;
    /// "" (System) stays the system font; a bundled family always stays; a family that is not
    /// available is EsteFont Pro.</summary>
    public static string Resolve(string? saved, Func<string, bool> available)
    {
        if (saved is null) return DefaultFamily;
        if (saved.Length == 0 || BundledFonts.Bundled(saved) is not null || available(saved)) return saved;
        return DefaultFamily;
    }

    /// <summary>The font families for the pickers: the bundled families first (EsteFont Pro, then
    /// EsteFont 26), then everything installed, sorted case-insensitively, without a second copy of either.</summary>
    public static IReadOnlyList<string> PickerFamilies(IEnumerable<string> installed)
    {
        var bundled = BundledFonts.Families.Select(f => f.Name).ToList();
        var rest = installed.Where(f => !bundled.Contains(f)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        return [.. bundled, .. rest];
    }
}
