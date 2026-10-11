using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>EsteFont Pro and EsteFont 26: faces, bundle lookup, the Desk default and fallback, picker order
/// (parity with the Mac's BundledFontsTests and DeskFontTests).</summary>
public class DeskFontTests
{
    [Fact]
    public void FileNames_AreEveryFace_InFamilyOrder() =>
        Assert.Equal(
            ["EsteFontPro-Regular.ttf", "EsteFontPro-Bold.ttf", "EsteFont26-Regular.ttf", "EsteFont26-Bold.ttf"],
            BundledFonts.FileNames);

    [Fact]
    public void BoldFace_ForBothBundledFamilies()
    {
        Assert.Equal("EsteFontPro-Bold", BundledFonts.Face("EsteFont Pro", true));
        Assert.Equal("EsteFontPro-Regular", BundledFonts.Face("EsteFont Pro", false));
        Assert.Equal("EsteFont26-Bold", BundledFonts.Face("EsteFont 26", true));
        Assert.Equal("EsteFont26-Regular", BundledFonts.Face("EsteFont 26", false));
        Assert.Equal("Avenir", BundledFonts.Face("Avenir", true));
        Assert.Equal("", BundledFonts.Face("", true));
    }

    [Fact]
    public void Bundled_FindsOnlyBundledFamilies()
    {
        Assert.Same(BundledFonts.EsteFontPro, BundledFonts.Bundled("EsteFont Pro"));
        Assert.Same(BundledFonts.EsteFont26, BundledFonts.Bundled("EsteFont 26"));
        Assert.Null(BundledFonts.Bundled("EsteFont 2.1"));
        Assert.Equal("Fonts", BundledFonts.Folder);
    }

    [Fact]
    public void DefaultIsEsteFontPro()
    {
        Assert.Equal("EsteFont Pro", BundledFonts.Family);
        Assert.Equal("EsteFont Pro", DeskFont.DefaultFamily);
        Assert.Equal("EsteFont 26", DeskFont.HeritageFamily);
        Assert.Equal(["EsteFont Pro", "EsteFont 26"], BundledFonts.Families.Select(f => f.Name));
    }

    [Fact]
    public void Resolve_NothingSaved_IsEsteFontPro() =>
        Assert.Equal("EsteFont Pro", DeskFont.Resolve(null, _ => false));

    [Fact]
    public void Resolve_SystemStaysSystem() =>
        Assert.Equal("", DeskFont.Resolve("", _ => false));

    [Fact]
    public void Resolve_AnInstalledChoiceStays()
    {
        Assert.Equal("Avenir", DeskFont.Resolve("Avenir", f => f == "Avenir"));
        Assert.Equal("EsteFont 2.1", DeskFont.Resolve("EsteFont 2.1", f => f == "EsteFont 2.1"));
        Assert.Equal("EsteFont 26", DeskFont.Resolve("EsteFont 26", _ => true));
    }

    [Fact]
    public void Resolve_AChoiceNoLongerInstalled_FallsBackToEsteFontPro()
    {
        Assert.Equal("EsteFont Pro", DeskFont.Resolve("EsteFont 2.1", _ => false));
        Assert.Equal("EsteFont Pro", DeskFont.Resolve("Gone Font", _ => false));
        // The bundled families never fall back to anything else.
        Assert.Equal("EsteFont 26", DeskFont.Resolve("EsteFont 26", _ => false));
        Assert.Equal("EsteFont Pro", DeskFont.Resolve("EsteFont Pro", _ => false));
    }

    [Fact]
    public void Picker_ListsEsteFontProThenEsteFont26FirstOnce()
    {
        Assert.Equal(
            ["EsteFont Pro", "EsteFont 26", "avenir", "Menlo", "Zapfino"],
            DeskFont.PickerFamilies(["Zapfino", "EsteFont 26", "avenir", "EsteFont Pro", "Menlo"]));
        Assert.Equal(["EsteFont Pro", "EsteFont 26"], DeskFont.PickerFamilies([]));
    }
}
