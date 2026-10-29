using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// The reference's mutation tests (test_validate.py Mutations, PixelSheets, TrademarkRule), each one change away from
/// a good fixture, plus the places this port follows FORMAT where the Python does not, and the never-throw contract.
/// </summary>
public class PackValidatorTests
{
    // -- helpers ------------------------------------------------------------------------------------------------------

    private static Dictionary<string, byte[]> Good(string name) => WidgetPackFixtures.Load("good", name);

    private static IReadOnlyList<string> Errors(Dictionary<string, byte[]> pack, string folder)
        => PackValidator.Validate(pack, folder).Errors;

    private static void EditJson(Dictionary<string, byte[]> pack, string rel, Action<JsonNode> edit)
    {
        var node = JsonNode.Parse(pack[rel])!;
        edit(node);
        // Relaxed escaping writes < and > as themselves, as Python's json.dumps does (the snippet scan reads raw bytes).
        pack[rel] = Encoding.UTF8.GetBytes(node.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
    }

    private static void SetText(Dictionary<string, byte[]> pack, string rel, string text) => pack[rel] = Encoding.UTF8.GetBytes(text);

    private static void AssertHas(IReadOnlyList<string> errors, string needle)
        => Assert.True(errors.Any(e => e.Contains(needle, StringComparison.Ordinal)), $"no error containing \"{needle}\" in: {string.Join(" | ", errors)}");

    /// <summary>A minimal RGBA PNG (filter 0 rows), as tools/pngutil.py writes them.</summary>
    internal static byte[] Png(int w, int h, byte[] rgba)
    {
        var raw = new MemoryStream();
        for (int y = 0; y < h; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgba, y * w * 4, w * 4);
        }
        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true))
            zs.Write(raw.ToArray());
        var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        WriteU32(ihdr, 0, (uint)w);
        WriteU32(ihdr, 4, (uint)h);
        ihdr[8] = 8; ihdr[9] = 6;
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", z.ToArray());
        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void Chunk(Stream s, string tag, byte[] body)
    {
        var len = new byte[4];
        WriteU32(len, 0, (uint)body.Length);
        s.Write(len);
        var tb = Encoding.ASCII.GetBytes(tag).Concat(body).ToArray();
        s.Write(tb);
        var crc = new byte[4];
        WriteU32(crc, 0, PackValidator.Crc32(tb, 0, tb.Length));
        s.Write(crc);
    }

    private static void WriteU32(byte[] b, int at, uint v)
    {
        b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
    }

    // -- the trademark rule -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Claude", "claude")]
    [InlineData("my-ANTHROPIC-pack", "anthropic")]
    [InlineData("Opus Neon", "opus")]
    [InlineData("the haiku pack", "haiku")]
    [InlineData("sonnet-theme", "sonnet")]
    [InlineData("Fable Gold", "fable")]
    [InlineData("claudette", "claude")]
    public void Trademarks_are_found(string text, string hit) => Assert.Equal(hit, PackValidator.FindTrademark(text));

    [Theory]
    [InlineData("Hourglass")]
    [InlineData("OLED Neon")]
    [InlineData("magnopus-free")]
    [InlineData("Unfableish")]
    [InlineData("palette")]
    [InlineData("sonnets")]
    public void Ordinary_words_pass(string text) => Assert.Null(PackValidator.FindTrademark(text));

    [Fact]
    public void Trademarks_are_scanned_in_file_names_and_every_json_string()
    {
        var p = Good("good-palette");
        p["haiku.json"] = p["palettes.json"];
        EditJson(p, "pack.json", m => m["files"]!.AsArray().Add("haiku.json"));
        AssertHas(Errors(p, "good-palette"), "trademark: file name 'haiku.json' contains 'haiku'");

        p = Good("good-palette");
        EditJson(p, "palettes.json", d => d["palettes"]![0]!["mood"] = "Anthropic");
        AssertHas(Errors(p, "good-palette"), "trademark: palettes.json value 'Anthropic' contains 'anthropic'");
    }

    // -- pack.json and files ----------------------------------------------------------------------------------------------

    [Fact]
    public void Duplicate_json_key_is_an_error()
    {
        var p = Good("good-palette");
        SetText(p, "palettes.json", "{\"schema_version\": 1, \"schema_version\": 1, \"palettes\": []}");
        AssertHas(Errors(p, "good-palette"), "palettes.json: invalid JSON (duplicate key 'schema_version')");
    }

    [Fact]
    public void Byte_order_mark_is_rejected()
    {
        var p = Good("good-palette");
        p["palettes.json"] = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(p["palettes.json"]).ToArray();
        AssertHas(Errors(p, "good-palette"), "byte-order mark");
    }

    [Fact]
    public void Invalid_utf8_is_rejected()
    {
        var p = Good("good-palette");
        p["palettes.json"] = new byte[] { (byte)'{', 0xC3, 0x28, (byte)'}' };
        AssertHas(Errors(p, "good-palette"), "palettes.json: not valid UTF-8");
    }

    [Fact]
    public void Unknown_manifest_key()
    {
        var p = Good("good-palette");
        EditJson(p, "pack.json", m => m["homepage"] = "https://example.com");
        AssertHas(Errors(p, "good-palette"), "unknown key 'homepage'");
    }

    [Fact]
    public void Bad_version_and_id()
    {
        var p = Good("good-palette");
        EditJson(p, "pack.json", m => { m["version"] = "1.0"; m["id"] = "Bad_Id"; });
        var errs = Errors(p, "good-palette");
        AssertHas(errs, "version '1.0'");
        AssertHas(errs, "kebab-case");
    }

    [Fact]
    public void Description_too_long()
    {
        var p = Good("good-palette");
        EditJson(p, "pack.json", m => m["description"] = new string('x', 201));
        AssertHas(Errors(p, "good-palette"), "description must be a string of 1 to 200");
    }

    [Fact]
    public void Control_characters_in_a_manifest_string()
    {
        var p = Good("good-palette");
        EditJson(p, "pack.json", m => m["name"] = "Two\nLines");
        AssertHas(Errors(p, "good-palette"), "pack.json: name contains control characters");
    }

    [Fact]
    public void Path_traversal_in_files()
    {
        var p = Good("good-palette");
        EditJson(p, "pack.json", m => m["files"]!.AsArray().Add("../escape.json"));
        AssertHas(Errors(p, "good-palette"), "not a safe relative path");
    }

    [Theory]
    [InlineData("../x.json")]
    [InlineData("/abs.json")]
    [InlineData("C:/x.json")]
    [InlineData("a\\b.json")]
    [InlineData("has space.json")]
    [InlineData(".hidden.json")]
    public void Unsafe_file_names_in_the_pack_are_rejected(string rel)
    {
        var p = Good("good-palette");
        p[rel] = Encoding.UTF8.GetBytes("{}");
        AssertHas(Errors(p, "good-palette"), rel + ": unsafe file name");
    }

    [Fact]
    public void Listed_file_missing()
    {
        var p = Good("good-palette");
        p.Remove("palettes.json");
        AssertHas(Errors(p, "good-palette"), "missing from the pack");
    }

    [Fact]
    public void Missing_pack_json_stops_early()
    {
        var p = Good("good-palette");
        p.Remove("pack.json");
        Assert.Equal(new[] { "pack.json is missing" }, Errors(p, "good-palette"));
    }

    [Theory]
    [InlineData(".svg")]
    [InlineData(".js")]
    [InlineData(".html")]
    [InlineData(".ps1")]
    public void Script_and_markup_extensions_are_named(string ext)
    {
        var p = Good("good-palette");
        p["extra" + ext] = Encoding.UTF8.GetBytes("x");
        AssertHas(Errors(p, "good-palette"), ext + " files are banned");
    }

    [Fact]
    public void Unknown_extension_is_rejected()
    {
        var p = Good("good-palette");
        p["notes.txt"] = Encoding.UTF8.GetBytes("hello");
        AssertHas(Errors(p, "good-palette"), "notes.txt: file type '.txt' is not allowed");
    }

    [Fact]
    public void Uppercase_extension_is_rejected()
    {
        var p = Good("good-sprite");
        p["preview.PNG"] = p["preview.png"];
        p.Remove("preview.png");
        EditJson(p, "pack.json", m => m["preview"] = "preview.PNG");
        var errs = Errors(p, "good-sprite");
        AssertHas(errs, "preview.PNG: file extension must be lowercase");
    }

    [Theory]
    [InlineData(new byte[] { (byte)'M', (byte)'Z', 0, 0 }, "Windows executable")]
    [InlineData(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' }, "ELF executable")]
    [InlineData(new byte[] { (byte)'#', (byte)'!', (byte)'/', (byte)'b' }, "a script (shebang)")]
    public void Executable_signature_in_a_data_file(byte[] head, string what)
    {
        var p = Good("good-palette");
        p["palettes.json"] = head.Concat(new byte[10]).ToArray();
        AssertHas(Errors(p, "good-palette"), what);
    }

    [Theory]
    [InlineData("javascript:alert(1)", "javascript:")]
    [InlineData("<IFRAME src=x>", "<iframe")]
    [InlineData("<!DOCTYPE HTML>", "<!doctype html")]
    public void Active_content_snippets_are_found_in_any_case(string text, string snippet)
    {
        var p = Good("good-palette");
        EditJson(p, "palettes.json", d => d["palettes"]![0]!["mood"] = text.Length > 24 ? text[..24] : text);
        AssertHas(Errors(p, "good-palette"), $"contains '{snippet}'; active content is banned");
    }

    [Fact]
    public void Png_over_two_megabytes()
    {
        var p = Good("good-palette");
        EditJson(p, "pack.json", m => m["preview"] = "preview.png");
        p["preview.png"] = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(new byte[2 * 1024 * 1024 + 10]).ToArray();
        AssertHas(Errors(p, "good-palette"), "preview.png: image is 2.0 MB, limit is 2 MB");
    }

    [Fact]
    public void Png_with_trailing_data()
    {
        var p = Good("good-palette");
        EditJson(p, "pack.json", m => m["preview"] = "preview.png");
        p["preview.png"] = Png(2, 2, new byte[16]).Concat(Encoding.ASCII.GetBytes("junk")).ToArray();
        AssertHas(Errors(p, "good-palette"), "trailing data after IEND");
    }

    [Fact]
    public void Png_with_short_image_data()
    {
        var p = Good("good-palette");
        EditJson(p, "pack.json", m => m["preview"] = "preview.png");
        byte[] png = Png(4, 4, new byte[64]);
        // Claim 8x8 in IHDR (and fix its CRC): the data inflates to the 4x4 size.
        png[16 + 3] = 8; png[20 + 3] = 8;
        uint crc = PackValidator.Crc32(png, 12, 17);
        png[29] = (byte)(crc >> 24); png[30] = (byte)(crc >> 16); png[31] = (byte)(crc >> 8); png[32] = (byte)crc;
        p["preview.png"] = png;
        AssertHas(Errors(p, "good-palette"), "preview.png: image data is 68 bytes, expected 264 for 8x8");
    }

    [Fact]
    public void A_good_png_passes()
    {
        var p = Good("good-palette");
        EditJson(p, "pack.json", m => m["preview"] = "preview.png");
        p["preview.png"] = Png(3, 2, Enumerable.Repeat((byte)90, 24).ToArray());
        Assert.Empty(Errors(p, "good-palette"));
    }

    [Fact]
    public void Pack_over_ten_megabytes()
    {
        var p = Good("good-font");
        for (int i = 0; i < 3; i++)
        {
            p[$"big{i}.ttf"] = new byte[] { 0, 1, 0, 0 }.Concat(new byte[4 * 1024 * 1024]).ToArray();
            int n = i;
            EditJson(p, "pack.json", m => m["files"]!.AsArray().Add($"big{n}.ttf"));
        }
        AssertHas(Errors(p, "good-font"), "limit is 10 MB");
    }

    [Fact]
    public void Font_collections_and_oversized_fonts()
    {
        var p = Good("good-font");
        p["fixture.ttf"] = Encoding.ASCII.GetBytes("ttcf").Concat(new byte[16]).ToArray();
        AssertHas(Errors(p, "good-font"), "fixture.ttf: font collections (.ttc) are not allowed");
        p["fixture.ttf"] = new byte[] { 0, 1, 0, 0 }.Concat(new byte[5 * 1024 * 1024]).ToArray();
        AssertHas(Errors(p, "good-font"), "fixture.ttf: font is 5.0 MB, limit is 5 MB");
    }

    [Fact]
    public void Folder_name_is_checked_only_when_given()
    {
        var p = Good("good-palette");
        Assert.True(PackValidator.Validate(p, folderName: null).Ok);
        AssertHas(PackValidator.Validate(p, "somewhere-else").Errors, "does not match the folder name 'somewhere-else'");
    }

    [Fact]
    public void Pack_json_can_be_given_apart_from_the_other_files()
    {
        var p = Good("good-palette");
        byte[] manifest = p["pack.json"];
        p.Remove("pack.json");
        var r = PackValidator.Validate(manifest, p, "good-palette");
        Assert.True(r.Ok);
        Assert.Equal("palette", r.Manifest!.Kind);
        Assert.Equal(new[] { "palettes.json" }, r.Manifest.Files);

        p["pack.json"] = manifest;
        Assert.Equal(new[] { "pack.json is given twice" }, PackValidator.Validate(manifest, p).Errors);
    }

    // -- themes -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Theme_text_color_must_be_a_hex()
    {
        var p = Good("good-theme");
        EditJson(p, "themes/fixture-dark.json", t => t["text"] = "white");
        AssertHas(Errors(p, "good-theme"), "text must be a #rrggbb color");
    }

    [Fact]
    public void Theme_unknown_key_is_an_error()
    {
        var p = Good("good-theme");
        EditJson(p, "themes/fixture-dark.json", t => t["script"] = "x");
        AssertHas(Errors(p, "good-theme"), "unknown key 'script'");
    }

    [Fact]
    public void Theme_dial_out_of_range()
    {
        var p = Good("good-theme");
        EditJson(p, "themes/fixture-dark.json", t => t["glass_alpha"] = 1.5);
        AssertHas(Errors(p, "good-theme"), "glass_alpha must be between");
    }

    [Fact]
    public void Theme_file_name_pattern()
    {
        var p = Good("good-theme");
        p["themes/Bad-Name.json"] = p["themes/fixture-dark.json"];
        p.Remove("themes/fixture-dark.json");
        EditJson(p, "pack.json", m => m["files"] = new JsonArray("themes/Bad-Name.json"));
        AssertHas(Errors(p, "good-theme"), "themes/Bad-Name.json: theme files must be themes/<kebab-name>.json");
    }

    /// <summary>FORMAT 5.1 rules Core's ThemeLint lets through for the app's own themes; the catalog does not.</summary>
    [Theory]
    [InlineData("accent_bloom", "null", "accent_bloom must be an object")]
    [InlineData("opts_out_of_mica", "\"yes\"", "opts_out_of_mica must be true or false")]
    [InlineData("bg_grid", "1", "bg_grid must be true or false")]
    [InlineData("monospace_font", "3", "monospace_font must be a string")]
    public void Catalog_theme_rules_beyond_the_app_lint(string key, string json, string needle)
    {
        var p = Good("good-theme");
        EditJson(p, "themes/fixture-dark.json", t => t[key] = JsonNode.Parse(json));
        AssertHas(Errors(p, "good-theme"), needle);
    }

    // -- palettes and effects --------------------------------------------------------------------------------------------

    [Fact]
    public void Effects_color_must_be_hex_or_ink()
    {
        var p = Good("good-effects");
        EditJson(p, "effects.json", e => e["effects"]![0]!["color"] = "red");
        AssertHas(Errors(p, "good-effects"), "color must be a #rrggbb color or \"ink\"");
    }

    [Fact]
    public void Effects_type_enum()
    {
        var p = Good("good-effects");
        EditJson(p, "effects.json", e => e["effects"]![0]!["type"] = "explode");
        AssertHas(Errors(p, "good-effects"), "type must be one of glow, sweep, shimmer, pulse");
    }

    [Fact]
    public void Booleans_are_not_numbers()
    {
        var p = Good("good-effects");
        EditJson(p, "effects.json", e => e["effects"]![0]!["intensity"] = true);
        AssertHas(Errors(p, "good-effects"), "intensity must be a number");
    }

    [Fact]
    public void An_integer_field_rejects_a_float_with_the_same_value()
    {
        var p = Good("good-effects");
        SetText(p, "effects.json", "{\"schema_version\": 1, \"effects\": [{\"name\": \"breathe\", \"type\": \"pulse\", "
                                   + "\"intensity\": 0.3, \"period_ms\": 4000.0, \"color\": \"ink\"}]}");
        AssertHas(Errors(p, "good-effects"), "period_ms must be an integer from 400 to 10000");
    }

    [Fact]
    public void Palette_unknown_keys_and_duplicate_names()
    {
        var p = Good("good-palette");
        EditJson(p, "palettes.json", d =>
        {
            d["extra"] = 1;
            d["palettes"]!.AsArray().Add(JsonNode.Parse("{\"name\": \"plain\", \"colors\": [\"#000000\"], \"tag\": 1}"));
        });
        var errs = Errors(p, "good-palette");
        AssertHas(errs, "palettes.json: unknown keys ['extra']");
        AssertHas(errs, "palettes.json: palettes[1] has unknown keys ['tag']");
        AssertHas(errs, "palettes.json: palettes[1]: duplicate palette name 'plain'");
    }

    [Fact]
    public void Palette_color_messages_repr_the_value_like_the_reference()
    {
        var p = Good("good-palette");
        EditJson(p, "palettes.json", d => d["palettes"]![0]!["colors"] = JsonNode.Parse("[\"red\", 5, null, {\"a\": true}]"));
        var errs = Errors(p, "good-palette");
        AssertHas(errs, "palettes[0]: 'red' is not a #rrggbb color");
        AssertHas(errs, "palettes[0]: 5 is not a #rrggbb color");
        AssertHas(errs, "palettes[0]: None is not a #rrggbb color");
        AssertHas(errs, "palettes[0]: {'a': True} is not a #rrggbb color");
    }

    // -- sprites ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Sprite_requires_exactly_sheet_or_stages()
    {
        var p = Good("good-sprite");
        EditJson(p, "sprite.json", s => s["stages"] = JsonNode.Parse(
            "[{\"id\": \"aa\", \"name\": \"A\", \"min_growth\": 0, \"sheet\": \"sprite.png\", \"frame_w\": 8, \"frame_h\": 8}]"));
        AssertHas(Errors(p, "good-sprite"), "exactly one of");
    }

    [Fact]
    public void Sprite_first_stage_must_start_at_zero()
    {
        var p = Good("good-sprite-stages");
        EditJson(p, "sprite.json", s => s["stages"]![0]!["min_growth"] = 0.2);
        AssertHas(Errors(p, "good-sprite-stages"), "first stage must have min_growth 0");
    }

    [Fact]
    public void Sprite_stage_order_message_formats_like_the_reference()
    {
        var p = Good("good-sprite-stages");
        EditJson(p, "sprite.json", s => s["stages"]![1]!["min_growth"] = 0);
        AssertHas(Errors(p, "good-sprite-stages"),
            "sprite.json: stages[1]: min_growth 0 must be greater than the previous stage's 0");
    }

    [Fact]
    public void Sprite_stage_zero_matches_base_frame()
    {
        var p = Good("good-sprite-stages");
        EditJson(p, "sprite.json", s => { s["frame_w"] = 16; s["frame_h"] = 16; });
        AssertHas(Errors(p, "good-sprite-stages"), "must equal the top-level");
    }

    [Fact]
    public void Sprite_stage_sheet_must_be_listed()
    {
        var p = Good("good-sprite-stages");
        EditJson(p, "pack.json", m => m["files"] = new JsonArray("sprite.json", "sprites/one.png"));
        AssertHas(Errors(p, "good-sprite-stages"), "pack.json: files must list sprites/two.png");
    }

    [Fact]
    public void Sprite_trigger_names_must_exist()
    {
        var p = Good("good-sprite");
        EditJson(p, "sprite.json", s => s["triggers"]!["idle"] = "missing");
        AssertHas(Errors(p, "good-sprite"), "triggers.idle names animation 'missing'");
    }

    [Fact]
    public void Sprite_roam_speed_range_and_edges()
    {
        var p = Good("good-sprite");
        EditJson(p, "sprite.json", s => { s["roam"]!["speed_px_s"] = 500; s["roam"]!["edges"] = new JsonArray("bottom", "bottom"); });
        var errs = Errors(p, "good-sprite");
        AssertHas(errs, "roam.speed_px_s");
        AssertHas(errs, "roam.edges must be a non-empty unique subset of bottom, left, right");
    }

    [Fact]
    public void Sprite_six_stage_limit()
    {
        var p = Good("good-sprite-stages");
        EditJson(p, "sprite.json", s =>
        {
            var first = s["stages"]![0]!;
            var arr = new JsonArray();
            for (int i = 0; i < 7; i++)
            {
                var st = first.DeepClone();
                st["id"] = $"st{i}";
                st["min_growth"] = i / 10.0;
                arr.Add(st);
            }
            s["stages"] = arr;
        });
        AssertHas(Errors(p, "good-sprite-stages"), "1 to 6 entries");
    }

    [Fact]
    public void Sprite_integer_fields_reject_floats()
    {
        var p = Good("good-sprite");
        EditJson(p, "sprite.json", s => { s["scale"] = 2.5; s["fps"] = 0; });
        var errs = Errors(p, "good-sprite");
        AssertHas(errs, "scale must be an integer from 1 to 4");
        AssertHas(errs, "fps must be an integer from 1 to 24");
    }

    [Fact]
    public void Sprite_animation_name_and_frame_rules()
    {
        var p = Good("good-sprite");
        EditJson(p, "sprite.json", s => { s["animations"]!["Bad Name"] = new JsonArray(0); s["animations"]!["walk"] = new JsonArray(-1); });
        var errs = Errors(p, "good-sprite");
        AssertHas(errs, "animation name 'Bad Name' must match [a-z][a-z0-9_]{1,23}");
        AssertHas(errs, "animation 'walk' must be 1 to 64 non-negative integer frame indexes");
    }

    // -- sprite v2 pixel sheets ----------------------------------------------------------------------------------------------

    [Fact]
    public void V2_with_png_sheets_only_is_valid()
    {
        var p = Good("good-sprite");
        EditJson(p, "sprite.json", s => s["schema_version"] = 2);
        Assert.Empty(Errors(p, "good-sprite"));
    }

    [Fact]
    public void Schema_version_three_is_rejected()
    {
        var p = Good("good-sprite-pixel");
        EditJson(p, "sprite.json", s => s["schema_version"] = 3);
        AssertHas(Errors(p, "good-sprite-pixel"), "schema_version must be 1 or 2");
    }

    [Fact]
    public void Pixel_frame_must_match_the_stage()
    {
        var p = Good("good-sprite-pixel-stages");
        EditJson(p, "sprites/one.json", s =>
        {
            s["frame_w"] = 16;
            s["frame_h"] = 16;
            var frames = new JsonArray();
            for (int f = 0; f < 4; f++)
            {
                var rows = new JsonArray();
                for (int r = 0; r < 16; r++) rows.Add(new string('0', 16));
                frames.Add(rows);
            }
            s["frames"] = frames;
        });
        AssertHas(Errors(p, "good-sprite-pixel-stages"), "frame size 16x16 does not match the stage 'one' frame size 8x8");
    }

    [Fact]
    public void Uppercase_index_is_rejected()
    {
        var p = Good("good-sprite-pixel");
        EditJson(p, "sheet.json", s => s["frames"]![1]![0] = "..0A00..");
        AssertHas(Errors(p, "good-sprite-pixel"), "frames[1][0][3] is 'A'");
    }

    [Fact]
    public void Pixel_sheet_must_be_listed()
    {
        var p = Good("good-sprite-pixel");
        EditJson(p, "pack.json", m => m["files"] = new JsonArray("sprite.json"));
        AssertHas(Errors(p, "good-sprite-pixel"), "must list sheet.json");
    }

    [Fact]
    public void Pixel_sheet_unknown_key_and_trademark_scan()
    {
        var p = Good("good-sprite-pixel");
        EditJson(p, "sheet.json", s => s["title"] = "Claude pet");
        var errs = Errors(p, "good-sprite-pixel");
        AssertHas(errs, "unknown keys ['title']");
        AssertHas(errs, "trademark: sheet.json value");
    }

    [Fact]
    public void Pixel_sheet_missing_keys_and_wrong_types()
    {
        var p = Good("good-sprite-pixel");
        SetText(p, "sheet.json", "{\"palette\": \"#22d3ee\", \"frames\": [[\"........\"], 7]}");
        var errs = Errors(p, "good-sprite-pixel");
        AssertHas(errs, "missing required key 'frame_w'");
        AssertHas(errs, "palette must be 1 to 16");
        AssertHas(errs, "frames[1] must be an array of strings");
    }

    [Fact]
    public void Sheet_must_be_png_or_json()
    {
        var p = Good("good-sprite-pixel");
        EditJson(p, "sprite.json", s => s["sheet"] = "sheet.txt");
        AssertHas(Errors(p, "good-sprite-pixel"), "must be a .png sheet or a .json pixel sheet");
    }

    [Fact]
    public void Sprite_json_cannot_be_its_own_sheet()
    {
        var p = Good("good-sprite-pixel");
        EditJson(p, "sprite.json", s => s["sheet"] = "sprite.json");
        AssertHas(Errors(p, "good-sprite-pixel"), "must not be sprite.json itself");
    }

    [Fact]
    public void A_broken_sheet_does_not_flood_the_report()
    {
        var p = Good("good-sprite-pixel");
        EditJson(p, "sheet.json", s =>
        {
            var frames = new JsonArray();
            for (int f = 0; f < 40; f++)
            {
                var rows = new JsonArray();
                for (int r = 0; r < 8; r++) rows.Add("zzzzzzzz");
                frames.Add(rows);
            }
            s["frames"] = frames;
        });
        var errs = Errors(p, "good-sprite-pixel");
        Assert.True(errs.Count <= PackValidator.PixelMaxErrors + 1);
        Assert.Contains("more errors in this pixel sheet are not shown", errs[^1]);
    }

    [Fact]
    public void Frame_limit()
    {
        var p = Good("good-sprite-pixel");
        EditJson(p, "sheet.json", s =>
        {
            var frames = s["frames"]!.AsArray();
            var many = new JsonArray();
            for (int i = 0; i < 65; i++)
                foreach (var f in frames) many.Add(f!.DeepClone());
            s["frames"] = many;
        });
        AssertHas(Errors(p, "good-sprite-pixel"), "frames must be an array of 1 to 256");
    }

    // -- null documents and trailing newlines (validate.py rejects both too since 61b4247) --------------------------------------------------------------

    /// <summary>A file whose JSON is the literal null is an error, worded as validate.py's load_json words it.</summary>
    [Theory]
    [InlineData("good-palette", "palettes.json", "palettes.json: must be a JSON object, got null")]
    [InlineData("good-effects", "effects.json", "effects.json: must be a JSON object, got null")]
    [InlineData("good-sprite", "sprite.json", "sprite.json: must be a JSON object, got null")]
    [InlineData("good-theme", "themes/fixture-dark.json", "themes/fixture-dark.json: must be a JSON object, got null")]
    [InlineData("good-sprite-pixel", "sheet.json", "sheet.json: must be a JSON object, got null")]
    [InlineData("good-palette", "pack.json", "pack.json: must be a JSON object, got null")]
    public void A_null_document_is_not_an_object(string pack, string rel, string needle)
    {
        var p = Good(pack);
        SetText(p, rel, "null");
        AssertHas(Errors(p, pack), needle);
    }

    /// <summary>A color with a trailing newline is not a color (validate.py uses fullmatch).</summary>
    [Fact]
    public void A_trailing_newline_does_not_satisfy_a_pattern()
    {
        var p = Good("good-palette");
        EditJson(p, "palettes.json", d => d["palettes"]![0]!["colors"]![0] = "#ff2a6d\n");
        AssertHas(Errors(p, "good-palette"), "palettes[0]: '#ff2a6d\\n' is not a #rrggbb color");
    }

    /// <summary>validate.py raises TypeError (unhashable list) here; the port reports it.</summary>
    [Fact]
    public void A_license_of_the_wrong_type_is_reported()
    {
        var p = Good("good-palette");
        EditJson(p, "pack.json", m => m["license"] = new JsonArray());
        AssertHas(Errors(p, "good-palette"), "pack.json: license [] is not allowed; use one of MIT, CC-BY-4.0, CC0-1.0, Apache-2.0, OFL-1.1, BSD-3-Clause, ISC");
    }

    /// <summary>validate.py raises TypeError when a sprite pack's files list holds an object; the port reports it.</summary>
    [Fact]
    public void A_sprite_files_entry_of_the_wrong_type_is_reported()
    {
        var p = Good("good-sprite");
        EditJson(p, "pack.json", m => m["files"]!.AsArray().Add(new JsonObject()));
        AssertHas(Errors(p, "good-sprite"), "pack.json: files entries must be strings");
    }

    // -- never throws ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Garbage_never_throws()
    {
        var rng = new Random(20261022);
        var good = Good("good-sprite-stages");
        for (int round = 0; round < 400; round++)
        {
            var p = new Dictionary<string, byte[]>(good, StringComparer.Ordinal);
            string victim = p.Keys.OrderBy(k => k, StringComparer.Ordinal).ElementAt(rng.Next(p.Count));
            byte[] bytes = (byte[])p[victim].Clone();
            switch (round % 4)
            {
                case 0: p[victim] = bytes[..rng.Next(bytes.Length)]; break;   // truncate
                case 1: for (int i = 0; i < 4 && bytes.Length > 0; i++) bytes[rng.Next(bytes.Length)] ^= (byte)rng.Next(1, 256); p[victim] = bytes; break;
                case 2: var junk = new byte[rng.Next(1, 64)]; rng.NextBytes(junk); p[victim] = junk; break;
                default: p[victim] = bytes.Concat(new byte[] { 0 }).ToArray(); break;
            }
            var r = PackValidator.Validate(p, "good-sprite-stages");   // must not throw
            Assert.All(r.Errors, e => Assert.False(string.IsNullOrWhiteSpace(e)));
            Assert.DoesNotContain(r.Errors, e => e.StartsWith("the validator could not finish", StringComparison.Ordinal));
        }
        Assert.Equal(new[] { "no files were given" }, PackValidator.Validate((IReadOnlyDictionary<string, byte[]>?)null).Errors);
    }

    [Fact]
    public void Deep_nesting_is_an_error_not_a_crash()
    {
        var p = Good("good-palette");
        SetText(p, "palettes.json", new string('[', 5000) + new string(']', 5000));
        AssertHas(Errors(p, "good-palette"), "palettes.json: invalid JSON");
    }

    [Fact]
    public void Theme_font_and_desk_are_documented_fields()
    {
        var p = Good("good-theme");
        EditJson(p, "themes/fixture-dark.json", t =>
        {
            t["font"] = "EsteFont Pro";
            t["desk"] = JsonNode.Parse("""{"ink": ["#ffffff"], "plate": true}""");
        });
        Assert.Empty(Errors(p, "good-theme"));
    }
}
