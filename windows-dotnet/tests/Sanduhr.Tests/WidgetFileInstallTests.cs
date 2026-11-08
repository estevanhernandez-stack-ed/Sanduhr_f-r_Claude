using System.Text;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Install widget from file… (#408): a pack zip from disk passes the catalog's gates and installs as the user's own.</summary>
public sealed class WidgetFileInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-wfile-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly WidgetStore _store;

    public WidgetFileInstallTests() => _store = new WidgetStore(Path.Combine(_root, "widgets"), WidgetTestKit.EncodePng);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { /* a stray temp folder is not a test failure */ }
    }

    private static Dictionary<string, byte[]> Good(string name) => WidgetPackFixtures.Load("good", name);
    private static Dictionary<string, byte[]> Bad(string name) => WidgetPackFixtures.Load("bad", name);

    private static Dictionary<string, byte[]> WithPackJson(Dictionary<string, byte[]> files, string from, string to)
    {
        var copy = new Dictionary<string, byte[]>(files, StringComparer.Ordinal);
        copy["pack.json"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(files["pack.json"]).Replace(from, to));
        return copy;
    }

    private WidgetFileResult Install(Dictionary<string, byte[]> files) =>
        WidgetFileInstall.Install(WidgetTestKit.ZipPack(files), _store, WidgetTestKit.V380);

    [Theory]
    [InlineData("good-palette")]
    [InlineData("good-sprite")]
    [InlineData("good-sprite-pixel")]
    [InlineData("good-theme")]
    public void A_good_pack_installs_as_the_users_own(string name)
    {
        var result = Install(Good(name));

        Assert.True(result.Ok, result.Message);
        Assert.Contains("installed from your file", result.Message);
        var row = _store.Load().Find(name)!;
        Assert.Equal(WidgetSource.Local, row.Source);
        Assert.True(row.MadeHere);
        Assert.True(row.Enabled);
        Assert.True(File.Exists(Path.Combine(_store.PackDir(name), "pack.json")));
    }

    [Fact]
    public void Installing_the_same_id_again_replaces_it()
    {
        Assert.True(Install(Good("good-palette")).Ok);

        var result = Install(WithPackJson(Good("good-palette"), "\"1.0.0\"", "\"1.1.0\""));

        Assert.True(result.Ok, result.Message);
        Assert.Contains("updated to 1.1.0", result.Message);
        var row = Assert.Single(_store.Load().Packs);
        Assert.Equal("1.1.0", row.Version);
        Assert.Equal(WidgetSource.Local, row.Source);
    }

    [Fact]
    public void A_file_never_replaces_a_catalog_pack_of_the_same_id()
    {
        var files = Good("good-palette");
        var manifest = PackValidator.Validate(files, "good-palette", strict: true).Manifest!;
        Assert.True(_store.Install(files, manifest, WidgetSource.Catalog).Ok);

        var result = Install(WithPackJson(files, "\"1.0.0\"", "\"9.0.0\""));

        Assert.False(result.Ok);
        Assert.Contains("A catalog widget is already installed as good-palette", result.Message);
        var row = Assert.Single(_store.Load().Packs);
        Assert.Equal(WidgetSource.Catalog, row.Source);
        Assert.Equal("1.0.0", row.Version);
    }

    [Theory]
    [InlineData("bad-no-license", "LICENSE")]
    [InlineData("bad-trademark-name", "Claude")]
    public void A_broken_pack_installs_nothing_and_says_why_in_the_validators_words(string name, string expected)
    {
        var files = Bad(name);
        var verdict = PackValidator.Validate(files, strict: true);

        var result = Install(files);

        Assert.False(result.Ok);
        Assert.StartsWith("Nothing was installed: the pack didn't pass Sanduhr's checks.", result.Message);
        Assert.Contains(verdict.Errors[0], result.Message);
        Assert.Contains(expected, result.Message);
        Assert.Empty(_store.Load().Packs);
        Assert.False(Directory.Exists(_store.PackDir(name)));
    }

    [Fact]
    public void A_sheet_over_4096_pixels_installs_nothing()
    {
        var files = new Dictionary<string, byte[]>(Good("good-sprite"), StringComparer.Ordinal)
        {
            ["sprite.png"] = PackValidatorTests.Png(PackValidator.MaxImageSide + 1, 8, new byte[(PackValidator.MaxImageSide + 1) * 8 * 4]),
        };

        var result = Install(files);

        Assert.False(result.Ok);
        Assert.Contains("limit is 4096 pixels on a side", result.Message);
        Assert.Empty(_store.Load().Packs);
    }

    [Fact]
    public void A_file_that_is_not_a_safe_zip_installs_nothing()
    {
        var result = WidgetFileInstall.Install(Encoding.UTF8.GetBytes("not a zip at all, just some text in a file"), _store, WidgetTestKit.V380);

        Assert.False(result.Ok);
        Assert.StartsWith("Nothing was installed: the zip isn't safe to open (", result.Message);
        Assert.DoesNotContain(SettingsNames.FileZipFormatHint, result.Message);   // not a format problem: no hint
        Assert.Empty(_store.Load().Packs);
    }

    /// <summary>A zip as Explorer's Send to, Compressed folder and most tools write it: deflated, optionally with the pack's folder
    /// as an entry. Refused, and the line says where a good one comes from (#459).</summary>
    private static byte[] ToolZip(Dictionary<string, byte[]> files, bool folderEntry)
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            if (folderEntry) zip.CreateEntry("pack/");
            foreach (var (name, bytes) in files)
            {
                var entry = zip.CreateEntry((folderEntry ? "pack/" : "") + name, System.IO.Compression.CompressionLevel.Optimal);
                using var s = entry.Open();
                s.Write(bytes);
            }
        }
        return ms.ToArray();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_zip_from_Explorer_or_a_zip_tool_is_refused_and_says_where_a_pack_zip_comes_from(bool folderEntry)
    {
        var result = WidgetFileInstall.Install(ToolZip(Good("good-palette"), folderEntry), _store, WidgetTestKit.V380);

        Assert.False(result.Ok);
        Assert.StartsWith("Nothing was installed: the zip isn't safe to open (", result.Message);
        Assert.EndsWith(SettingsNames.FileZipFormatHint, result.Message);
        Assert.Empty(_store.Load().Packs);
    }

    [Fact]
    public void A_pack_for_a_newer_Sanduhr_installs_nothing()
    {
        var result = Install(WithPackJson(Good("good-palette"), "\"sanduhr_min\": \"3.8.0\"", "\"sanduhr_min\": \"99.0.0\""));

        Assert.False(result.Ok);
        Assert.Equal("Fixture palette needs Sanduhr 99.0.0 or later, so nothing was installed.", result.Message);
        Assert.Empty(_store.Load().Packs);
    }

    [Fact]
    public void The_status_line_shows_three_errors_and_counts_the_rest()
    {
        Assert.Equal("a.", WidgetFileInstall.Errors(["a"]));
        Assert.Equal("a; b; c.", WidgetFileInstall.Errors(["a", "b", "c"]));
        Assert.Equal("a; b; c; and 1 more.", WidgetFileInstall.Errors(["a", "b", "c", "d"]));
        Assert.Equal("a; b; c; and 2 more.", WidgetFileInstall.Errors(["a", "b", "c", "d", "e"]));
    }
}
