using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>A real font pack for tests: EsteFont Pro Regular from the repo, wrapped in the good-font fixture's manifest.</summary>
internal static class FontPackKit
{
    public static string RepoFont()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "mac", "Resources", "Fonts", "EsteFontPro-Regular.ttf")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return Path.Combine(dir!, "mac", "Resources", "Fonts", "EsteFontPro-Regular.ttf");
    }

    /// <summary>Installs the pack <c>esteface</c> (kind font, one real .ttf) into the store, through the validator and the real install.</summary>
    public static void Install(WidgetStore store)
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "widget-packs", "good", "good-font");
        var manifest = File.ReadAllText(Path.Combine(fixture, "pack.json"))
            .Replace("good-font", "esteface").Replace("fixture.ttf", "font.ttf");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["pack.json"] = Encoding.UTF8.GetBytes(manifest),
            ["LICENSE"] = File.ReadAllBytes(Path.Combine(fixture, "LICENSE")),
            ["font.ttf"] = File.ReadAllBytes(RepoFont()),
        };
        var v = PackValidator.Validate(files, "esteface", strict: true);
        Assert.True(v.Ok, string.Join(" | ", v.Errors));
        var r = store.Install(files, v.Manifest!, WidgetSource.Catalog);
        Assert.True(r.Ok, r.Error);
    }
}

/// <summary>
/// 3.9.1, the risk named before the font wiring: does WPF keep a font file locked once a glyph from it has been drawn? In a fresh
/// process it does: a pack folder it loaded a font from could not be moved (Directory.Move threw "Access ... denied") with every
/// reference still held, and again after they were dropped and the GC had run. It is not pinned here as a test of its own, because
/// whether the file is held depends on what else the process has already loaded. So the Desk loads pack fonts from a copy
/// (<see cref="PackFonts"/>), and this pins the part that matters: draw from the copy, then Uninstall.
/// </summary>
public sealed class PackFontLockTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-fontlock-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private WidgetStore Store() => new(Path.Combine(_root, "widgets"), WidgetCatalogService.EncodePng);

    /// <summary>A folder holding the repo font as <c>font.ttf</c>.</summary>
    private string FontFolder(string name)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        File.Copy(FontPackKit.RepoFont(), Path.Combine(dir, "font.ttf"));
        return dir;
    }

    /// <summary>Loads the family from the folder and forces a glyph out of it: a FormattedText drawn to a bitmap, a GlyphTypeface read.</summary>
    private static (FontFamily Family, GlyphTypeface Glyphs) DrawWith(string folder)
    {
        var baseUri = new Uri(folder + Path.DirectorySeparatorChar);
        var name = Assert.Single(Fonts.GetFontFamilies(baseUri)).FamilyNames.Values.First();
        var family = new FontFamily(baseUri, $"./#{name}");
        var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        Assert.True(typeface.TryGetGlyphTypeface(out var glyphs));
        Assert.True(glyphs.CharacterToGlyphMap.ContainsKey('A'));
        var text = new FormattedText("Sanduhr 12:34", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 32, Brushes.White, 1.0);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawText(text, new Point(2, 2));
        var bitmap = new RenderTargetBitmap(320, 64, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        Assert.True(text.Width > 10);
        return (family, glyphs);
    }

    [Fact]
    public void ControlAFolderNeverLoadedFromMovesAndDeletesFine()
    {
        string folder = FontFolder("control");
        string gone = Path.Combine(_root, "gone");

        Directory.Move(folder, gone);
        Directory.Delete(gone, recursive: true);

        Assert.False(Directory.Exists(gone));
    }

    [Fact]
    public void AFontDrawnFromTheCopyDoesNotStopUninstall()
    {
        Sta.Run(() =>
        {
            var store = Store();
            FontPackKit.Install(store);
            var folder = Assert.Single(PackFonts.Folders(store));
            var (family, glyphs) = DrawWith(folder.Dir);   // both references stay alive through Uninstall, as DeskFonts' cache would

            var result = store.Uninstall("esteface");

            Assert.True(result.Ok, result.Error);
            Assert.False(Directory.Exists(store.PackDir("esteface")));
            Assert.Empty(store.Load().Packs);
            GC.KeepAlive(family);
            GC.KeepAlive(glyphs);
        });
    }
}
