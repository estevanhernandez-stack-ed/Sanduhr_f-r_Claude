using System.IO;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>3.9.1: families from installed font packs are in the Desk's font list and the design service's installed list; a saved
/// font whose pack is gone draws in EsteFont Pro; a bundled or an installed family is never shadowed by a pack.</summary>
public sealed class DeskFontsPackTests : IDisposable
{
    private static readonly Uri PackFolder = new(Path.Combine(Path.GetTempPath(), "sanduhr-nofonts") + Path.DirectorySeparatorChar);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-deskfonts-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private Paths ScratchPaths() => new(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));

    private static Dictionary<string, Uri> Packs(params string[] families) => families.ToDictionary(f => f, _ => PackFolder);

    [Fact]
    public void APackFamilyIsInThePickerAfterTheBundledOnesAndKeptWhenSaved()
    {
        var fonts = new DeskFonts(["Segoe UI", "Zapfino"], Packs("Pack Face"));

        Assert.Equal(["EsteFont Pro", "EsteFont 26", "Pack Face", "Segoe UI", "Zapfino"], fonts.PickerFamilies());
        var (regular, bold) = fonts.Families("Pack Face");
        Assert.Contains("Pack Face", regular.Source);
        Assert.StartsWith(PackFolder.AbsoluteUri, regular.BaseUri.AbsoluteUri);
        Assert.Same(regular, bold);
    }

    [Fact]
    public void ASavedFontWhosePackIsGoneDrawsInEsteFontPro()
    {
        var withPack = new DeskFonts(["Segoe UI"], Packs("Pack Face"));
        var without = new DeskFonts(["Segoe UI"]);

        Assert.Contains("EsteFont Pro", without.Families("Pack Face").Regular.Source);
        Assert.DoesNotContain("Pack Face", without.PickerFamilies());
        Assert.DoesNotContain("EsteFont Pro", withPack.Families("Pack Face").Regular.Source);
    }

    [Fact]
    public void AnInstalledOrBundledFamilyIsNeverShadowedByAPackOfTheSameName()
    {
        var fonts = new DeskFonts(["Segoe UI"], Packs("Segoe UI", "EsteFont Pro", "segoe ui"));

        Assert.Equal("Segoe UI", fonts.Families("Segoe UI").Regular.Source);
        Assert.Contains("EsteFont Pro", fonts.Families("EsteFont Pro").Regular.Source);
        Assert.DoesNotContain(fonts.PickerFamilies(), f => f.StartsWith("segoe", StringComparison.Ordinal));
        Assert.Equal(["EsteFont Pro", "EsteFont 26", "Segoe UI"], fonts.PickerFamilies());
    }

    [Fact]
    public void TheCatalogNamesTheFamilyInAnInstalledFontPackAndLeavesItWhenItIsRemoved()
    {
        Sta.Run(() =>
        {
            var store = InstalledPackKit.Store(ScratchPaths());
            Assert.Empty(PackFontCatalog.Families(store));
            FontPackKit.Install(store);

            var families = PackFontCatalog.Families(store);

            var family = Assert.Single(families);
            Assert.Equal("EsteFont Pro", family.Key);
            Assert.StartsWith(Path.Combine(store.Root, ".fonts"), family.Value.LocalPath);

            Assert.True(store.Uninstall("esteface").Ok);
            Assert.Empty(PackFontCatalog.Families(store));
        });
    }

    [Fact]
    public void AFontWithAGoodHeaderAndABrokenBodyIsSkippedAndNeverThrows()
    {
        Sta.Run(() =>
        {
            var store = InstalledPackKit.Store(ScratchPaths());
            // The validator checks the size and the magic number only. This is the real font cut 100 bytes short: WPF finds its
            // family name and then throws FileFormatException when it builds the glyph typeface (at draw time, on the UI thread).
            byte[] truncated = File.ReadAllBytes(FontPackKit.RepoFont())[..34000];
            FontPackKit.Install(store, "brokenface", truncated);
            Assert.Empty(PackFontCatalog.Families(store));

            FontPackKit.Install(store);   // a good pack beside it still loads
            Assert.Equal(["EsteFont Pro"], PackFontCatalog.Families(store).Keys);
            Assert.Single(PackFonts.Folders(store), f => f.PackId == "brokenface");   // copied, just not listed
        });
    }

    [Fact]
    public void TheDesksFontsAreReadFromTheGivenPathsNotThePcsOwnWidgetsFolder()
    {
        Sta.Run(() =>
        {
            var empty = ScratchPaths();
            Assert.Empty(PackFontCatalog.ForThisPc(empty));

            FontPackKit.Install(InstalledPackKit.Store(empty));
            var family = Assert.Single(PackFontCatalog.ForThisPc(empty));
            Assert.StartsWith(Path.Combine(empty.WidgetsDir, ".fonts"), family.Value.LocalPath);
            Assert.NotNull(DeskFonts.ForThisPc(empty));
        });
    }

    [Fact]
    public void TheDesignServiceListsAPackFontAsInstalled()
    {
        Sta.Run(() =>
        {
            var paths = ScratchPaths();
            FontPackKit.Install(InstalledPackKit.Store(paths));
            using var service = new DeskDesignService(paths, new SettingsStore(paths));

            Assert.Contains("EsteFont Pro", service.InstalledFonts(), StringComparer.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void TheUnionKeepsOneCopyOfASharedNameSortedWithoutCase()
    {
        var union = PackFontCatalog.Union(["Segoe UI", "arial"], Packs("Pack Face", "ARIAL"));
        Assert.Equal(["arial", "Pack Face", "Segoe UI"], union);
    }
}
