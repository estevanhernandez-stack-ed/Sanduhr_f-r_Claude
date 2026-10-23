using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>"Use my own art…" (spec 3.8 section 6): the pack image rules on a picked PNG, the pack rebuilt around it and validated
/// whole, and the install that makes a Claude-made sprite the user's own (local-only). Temp folders only.</summary>
public sealed class WidgetOwnArtTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-ownart-" + Guid.NewGuid().ToString("N")[..8]);

    public WidgetOwnArtTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static readonly CompanionStage Stage8 = new("only", "only", 0, "sheet.json", 8, 8);

    /// <summary>An opaque RGBA sheet of <paramref name="w"/> x <paramref name="h"/>.</summary>
    internal static byte[] Sheet(int w, int h)
    {
        var rgba = new byte[w * h * 4];
        for (int i = 0; i < rgba.Length; i += 4) { rgba[i] = 0x22; rgba[i + 1] = 0xd3; rgba[i + 2] = 0xee; rgba[i + 3] = 255; }
        return PackValidatorTests.Png(w, h, rgba);
    }

    private static WidgetProposalPack ClaudeSprite() => WidgetProposal.Build(WidgetProposalTests.SpriteArgs(), WidgetProposalTests.Now).Pack!;

    [Fact]
    public void ThePackImageRulesAreCheckedBeforeAnythingIsCopied()
    {
        Assert.Null(WidgetOwnArt.CheckImage(Sheet(32, 8), Stage8, maxFrame: 3));
        Assert.Contains("isn't a PNG", WidgetOwnArt.CheckImage("GIF89a not a png at all, padded to length"u8.ToArray(), Stage8, 3));
        Assert.Contains("larger than 2 MB", WidgetOwnArt.CheckImage(new byte[PackValidator.MaxImageBytes + 1], Stage8, 3));
        Assert.Contains("isn't a whole number of 8x8 cells", WidgetOwnArt.CheckImage(Sheet(30, 8), Stage8, 3));
        Assert.Contains("needs at least 4", WidgetOwnArt.CheckImage(Sheet(16, 8), Stage8, 3));
        Assert.Contains("at most 4096 pixels", WidgetOwnArt.CheckImage(Sheet(8, 8)[..16].Concat(new byte[] { 0, 0, 0x20, 0, 0, 0, 0, 8 }).Concat(new byte[20]).ToArray(), Stage8, 0));
    }

    [Fact]
    public void TheArtReplacesThePixelSheetAndThePackIsValidatedWhole()
    {
        var pack = ClaudeSprite();
        var result = WidgetOwnArt.Apply(pack.Files, pack.Id, new Dictionary<string, byte[]> { ["only"] = Sheet(32, 8) });
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        Assert.Equal(["LICENSE", "art/sprite.png", "pack.json", "sprite.json"], result.Files!.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("art/sprite.png", (string?)JsonNode.Parse(result.Files["sprite.json"])!["sheet"]);
        Assert.Equal("1.0.1", result.Manifest!.Version);
        Assert.Equal(["art/sprite.png", "sprite.json"], result.Manifest.Files);
        Assert.True(PackValidator.Validate(result.Files, pack.Id, strict: true).Ok);
    }

    [Fact]
    public void AnImageWithABrokenChunkIsRefusedByTheWholePackCheck()
    {
        var pack = ClaudeSprite();
        var png = Sheet(32, 8);
        png[^5] ^= 0xff;   // the IEND CRC
        var result = WidgetOwnArt.Apply(pack.Files, pack.Id, new Dictionary<string, byte[]> { ["only"] = png });
        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.StartsWith("art/sprite.png", StringComparison.Ordinal));
    }

    [Fact]
    public void NoImageOrAnUnknownStageChangesNothing()
    {
        var pack = ClaudeSprite();
        Assert.False(WidgetOwnArt.Apply(pack.Files, pack.Id, new Dictionary<string, byte[]>()).Ok);
        Assert.Contains("no stage", WidgetOwnArt.Apply(pack.Files, pack.Id, new Dictionary<string, byte[]> { ["egg"] = Sheet(32, 8) }).Errors[0]);
    }

    [Fact]
    public void InstallingTheArtMakesAClaudeMadePackTheUsersOwnButNeverTakesACatalogPack()
    {
        var store = new WidgetStore(Path.Combine(_root, "widgets"), WidgetTestKit.EncodePng);
        var pack = ClaudeSprite();
        Assert.True(store.Install(pack.Files, pack.Manifest, WidgetSource.Claude).Ok);
        Assert.True(File.Exists(store.RasterPath(pack.Id, "sheet.json")));

        var files = store.ReadFiles(pack.Id)!;
        Assert.DoesNotContain(files.Keys, k => k.StartsWith(WidgetStore.RasterName, StringComparison.Ordinal));
        Assert.Equal(pack.Files.Keys.Order(StringComparer.Ordinal), files.Keys.Order(StringComparer.Ordinal));

        var art = WidgetOwnArt.Apply(files, pack.Id, new Dictionary<string, byte[]> { ["only"] = Sheet(32, 8) });
        Assert.False(store.Install(art.Files!, art.Manifest!, WidgetSource.Local).Ok);   // without the consent to change hands
        Assert.True(store.Install(art.Files!, art.Manifest!, WidgetSource.Local, replaceMadeHere: true).Ok);
        var row = store.Load().Find(pack.Id)!;
        Assert.Equal(WidgetSource.Local, row.Source);
        Assert.True(row.MadeHere);
        Assert.Equal("1.0.1", row.Version);
        Assert.True(File.Exists(Path.Combine(store.PackDir(pack.Id), "art", "sprite.png")));
        Assert.False(File.Exists(Path.Combine(store.PackDir(pack.Id), "sheet.json")));

        // A catalog pack is never replaced by one made here, whatever the flag says.
        var catalogFiles = Directory.GetFiles(Path.Combine(WidgetTestKit.Fixtures, "widget-packs", "good", "good-palette"))
            .ToDictionary(f => Path.GetFileName(f), f => File.ReadAllBytes(f), StringComparer.Ordinal);
        var catalog = PackValidator.Validate(catalogFiles, "good-palette");
        Assert.True(store.Install(catalogFiles, catalog.Manifest!, WidgetSource.Catalog).Ok);
        Assert.False(store.Install(catalogFiles, catalog.Manifest!, WidgetSource.Local, replaceMadeHere: true).Ok);
    }

    [Fact]
    public void BumpPatchStepsThePatchOnly()
    {
        Assert.Equal("1.0.1", WidgetOwnArt.BumpPatch("1.0.0"));
        Assert.Equal("2.3.10", WidgetOwnArt.BumpPatch("2.3.9"));
        Assert.Equal("odd", WidgetOwnArt.BumpPatch("odd"));
    }
}
