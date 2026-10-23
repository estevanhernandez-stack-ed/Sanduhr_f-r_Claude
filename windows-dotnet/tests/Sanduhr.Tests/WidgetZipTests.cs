using System.IO.Compression;
using System.Text;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>FORMAT 7 rule 4: safe extraction. Every refusal is one lie away from a good zip.</summary>
public class WidgetZipTests
{
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    private static Dictionary<string, byte[]> Palette() => WidgetPackFixtures.Load("good", "good-palette");

    private static ZipSpec[] PaletteSpecs() =>
        [.. Palette().OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => new ZipSpec(f.Key, f.Value))];

    private static string Refused(byte[] zip)
    {
        var files = WidgetZip.Read(zip, out var error);
        Assert.Null(files);
        Assert.False(string.IsNullOrEmpty(error));
        return error;
    }

    // ---- the good ones ----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_pack_zip_built_in_the_test_reads_back_and_validates_strict()
    {
        var pack = Palette();
        var files = WidgetZip.Read(WidgetTestKit.ZipPack(pack), out var error);
        Assert.True(files is not null, error);
        Assert.Equal(pack.Keys.Order(), files!.Keys.Order());
        foreach (var (k, v) in pack) Assert.Equal(v, files[k]);
        var verdict = PackValidator.Validate(files, folderName: "good-palette", strict: true);
        Assert.True(verdict.Ok, string.Join(" | ", verdict.Errors));
    }

    [Theory]
    [InlineData("hourglass")]
    [InlineData("oled-neon-palettes")]
    public void A_zip_from_build_catalog_py_reads_and_validates(string id)
    {
        // Built by sanduhr-widgets tools/build_catalog.py zip_pack at 473597d; its hash is the catalog fixture's.
        var zip = File.ReadAllBytes(Path.Combine(WidgetTestKit.Fixtures, "widget-zips", id + "-1.0.0.zip"));
        var doc = WidgetCatalog.Parse(File.ReadAllBytes(Path.Combine(WidgetTestKit.Fixtures, "widget-zips", "catalog.json")), out _)!;
        Assert.Null(WidgetCatalog.CheckDownload(doc.Packs.Single(p => p.Id == id), zip));
        var files = WidgetZip.Read(zip, out var error);
        Assert.True(files is not null, error);
        var verdict = PackValidator.Validate(files!, folderName: id, strict: true);
        Assert.True(verdict.Ok, string.Join(" | ", verdict.Errors));
        Assert.Equal(WidgetPackFixtures.Load("packs", id).Keys.Order(), files!.Keys.Order());
    }

    // ---- paths ------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("../palettes.json")]
    [InlineData("themes/../../palettes.json")]
    [InlineData("/palettes.json")]
    [InlineData("themes\\palettes.json")]
    [InlineData("C:/palettes.json")]
    [InlineData("C:palettes.json")]
    [InlineData("./palettes.json")]
    [InlineData("themes//palettes.json")]
    public void Unsafe_names_are_refused(string name)
    {
        var specs = PaletteSpecs().Append(new ZipSpec(name, B("{}")));
        Refused(WidgetTestKit.Zip(specs));
    }

    [Fact]
    public void A_non_ascii_or_space_name_is_refused()
    {
        Refused(WidgetTestKit.Zip(PaletteSpecs().Append(new ZipSpec("pal\u00e9ttes.json", B("{}")))));
        Refused(WidgetTestKit.Zip(PaletteSpecs().Append(new ZipSpec("my palettes.json", B("{}")))));
    }

    [Fact]
    public void Names_that_differ_only_in_case_are_refused()
    {
        var specs = PaletteSpecs().Append(new ZipSpec("Palettes.json", B("{}")));
        Assert.Contains("twice", Refused(WidgetTestKit.Zip(specs)));
        Assert.Contains("twice", Refused(WidgetTestKit.Zip(PaletteSpecs().Append(PaletteSpecs()[0]))));
    }

    [Fact]
    public void A_local_header_naming_another_file_is_refused()
    {
        var specs = PaletteSpecs();
        specs[0] = specs[0] with { LocalName = "../evil.json" };
        Refused(WidgetTestKit.Zip(specs));
    }

    // ---- entry kinds ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Directory_entries_are_refused()
    {
        Assert.Contains("directory", Refused(WidgetTestKit.Zip(PaletteSpecs().Prepend(new ZipSpec("themes/", [])))));
        // A directory by its attributes, whatever the name says.
        Refused(WidgetTestKit.Zip(PaletteSpecs().Append(new ZipSpec("extra", []) { External = (0x41EDu << 16) | 0x10 })));
    }

    [Fact]
    public void Symlinks_and_other_non_regular_files_are_refused()
    {
        var link = new ZipSpec("link.json", B("palettes.json")) { External = 0xA1FFu << 16 };
        Assert.Contains("regular", Refused(WidgetTestKit.Zip(PaletteSpecs().Append(link))));
        var fifo = new ZipSpec("fifo.json", B("{}")) { External = 0x11A4u << 16 };
        Refused(WidgetTestKit.Zip(PaletteSpecs().Append(fifo)));
    }

    [Fact]
    public void A_regular_file_from_a_Windows_zip_tool_is_fine()
    {
        var specs = PaletteSpecs().Select(s => s with { MadeBy = 20, External = 0x20 }).ToArray();
        Assert.NotNull(WidgetZip.Read(WidgetTestKit.Zip(specs), out _));
    }

    [Fact]
    public void Compressed_entries_are_refused()
    {
        var specs = PaletteSpecs();
        var raw = specs[^1].Data;
        var deflated = new MemoryStream();
        using (var d = new DeflateStream(deflated, CompressionLevel.Optimal, leaveOpen: true)) d.Write(raw);
        specs[^1] = specs[^1] with { Method = 8, Data = deflated.ToArray(), Crc = PackValidator.Crc32(raw, 0, raw.Length), DeclaredSize = (uint)raw.Length };
        Assert.Contains("compressed", Refused(WidgetTestKit.Zip(specs)));
        // A real deflated zip from the platform's own writer is refused the same way.
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (k, v) in Palette())
            {
                using var s = z.CreateEntry(k, CompressionLevel.Optimal).Open();
                s.Write(v);
            }
        Refused(ms.ToArray());
    }

    [Theory]
    [InlineData(0x0001)]   // encrypted
    [InlineData(0x0008)]   // data descriptor
    [InlineData(0x0040)]   // strong encryption
    public void Encrypted_entries_and_data_descriptors_are_refused(ushort flag)
    {
        var specs = PaletteSpecs();
        specs[0] = specs[0] with { Flags = flag };
        Refused(WidgetTestKit.Zip(specs));
    }

    [Fact]
    public void The_utf8_name_flag_alone_is_fine()
    {
        var specs = PaletteSpecs().Select(s => s with { Flags = 0x0800 }).ToArray();
        Assert.NotNull(WidgetZip.Read(WidgetTestKit.Zip(specs), out _));
    }

    // ---- limits -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void An_entry_declared_over_5_MB_is_refused_before_any_data_is_read()
    {
        var specs = PaletteSpecs().Append(new ZipSpec("big.json", B("{}")) { DeclaredSize = (uint)WidgetZip.MaxEntryBytes + 1 });
        Assert.Contains("5 MB", Refused(WidgetTestKit.Zip(specs)));
    }

    [Fact]
    public void Entries_declared_over_10_MB_in_total_are_refused_before_any_data_is_read()
    {
        // Three small entries that each claim 4 MB: 12 MB declared, a few bytes actual.
        var specs = PaletteSpecs().Concat(
            Enumerable.Range(0, 3).Select(i => new ZipSpec($"f{i}.json", B("{}")) { DeclaredSize = 4 * 1024 * 1024 }));
        Assert.Contains("10 MB", Refused(WidgetTestKit.Zip(specs)));
    }

    [Fact]
    public void An_entry_bigger_than_its_directory_record_says_is_refused()
    {
        // The central directory says 2 bytes; the local header and the data say 3.
        var specs = PaletteSpecs().Append(new ZipSpec("x.json", B("{ }")) { DeclaredSize = 2, LocalDeclaredSize = 3 });
        Refused(WidgetTestKit.Zip(specs));
    }

    [Fact]
    public void A_zip_larger_than_a_pack_can_be_is_refused()
    {
        var huge = new byte[(int)WidgetCatalog.MaxZipBytes + 1];
        Assert.Contains("larger", Refused(huge));
    }

    [Fact]
    public void More_than_67_entries_are_refused()
    {
        var specs = PaletteSpecs().Concat(Enumerable.Range(0, WidgetZip.MaxEntries).Select(i => new ZipSpec($"p{i:00}.json", B("{}"))));
        Assert.Contains("entries", Refused(WidgetTestKit.Zip(specs)));
    }

    [Fact]
    public void A_crc_mismatch_is_refused()
    {
        var specs = PaletteSpecs();
        specs[0] = specs[0] with { Crc = 0xDEADBEEF };
        Assert.Contains("CRC", Refused(WidgetTestKit.Zip(specs)));
    }

    [Fact]
    public void Two_entries_sharing_one_body_are_refused()
    {
        var specs = PaletteSpecs().Append(new ZipSpec("copy.json", PaletteSpecs()[0].Data) { SharesLocalOf = 0 }).ToArray();
        Refused(WidgetTestKit.Zip(specs));
    }

    [Fact]
    public void A_wrong_entry_count_or_garbage_is_refused()
    {
        Refused(WidgetTestKit.Zip(PaletteSpecs(), declaredCount: 1));
        Refused(WidgetTestKit.Zip(PaletteSpecs(), declaredCount: 9));
        Refused(B("PK not really a zip at all, just some bytes"));
        Refused([]);
        var good = WidgetTestKit.ZipPack(Palette());
        Refused(good[..^5]);   // truncated end record
    }

    [Fact]
    public void Reading_never_throws_on_random_bytes()
    {
        var rng = new Random(38);
        var good = WidgetTestKit.ZipPack(Palette());
        for (int i = 0; i < 300; i++)
        {
            var bad = (byte[])good.Clone();
            for (int j = 0; j < 4; j++) bad[rng.Next(bad.Length)] = (byte)rng.Next(256);
            _ = WidgetZip.Read(bad, out _);
        }
    }
}
