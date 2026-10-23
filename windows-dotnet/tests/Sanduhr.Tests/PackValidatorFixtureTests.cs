using System.Text;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>The fixtures and first-party packs of <c>sanduhr-widgets</c> (copied at 473597d, see
/// fixtures/widget-packs/README.md), read into the in-memory form <see cref="PackValidator"/> takes.</summary>
internal static class WidgetPackFixtures
{
    public static string Root => Path.Combine(AppContext.BaseDirectory, "fixtures", "widget-packs");

    public static IEnumerable<string> Names(string group)
        => Directory.GetDirectories(Path.Combine(Root, group)).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal)!;

    /// <summary>Every file in the pack folder, keyed by its relative POSIX path.</summary>
    public static Dictionary<string, byte[]> Load(string group, string name)
    {
        string dir = Path.Combine(Root, group, name);
        return Directory.GetFiles(dir, "*", SearchOption.AllDirectories).ToDictionary(
            f => Path.GetRelativePath(dir, f).Replace('\\', '/'),
            File.ReadAllBytes,
            StringComparer.Ordinal);
    }

    /// <summary>The reference validator's findings (python-expected.tsv): pack key "group/name" to (level, message).</summary>
    public static ILookup<string, (string Level, string Message)> Expected()
        => File.ReadAllLines(Path.Combine(Root, "python-expected.tsv"), Encoding.UTF8)
            .Where(l => l.Length > 0)
            .Select(l => l.Split('\t', 3))
            .ToLookup(p => p[0], p => (p[1], p[2]));

    public static TheoryData<string> Data(string group)
    {
        var data = new TheoryData<string>();
        foreach (var n in Names(group)) data.Add(n);
        return data;
    }
}

/// <summary>
/// Fixture parity with the reference validator (sanduhr-widgets tools/validate.py and test_validate.py at 473597d):
/// the same packs pass, the same packs fail, with the same messages. Theme schema and design messages come from
/// Core's ThemeLint, which words them as the app does; the Python's sentence is a prefix of each, so those compare
/// by prefix and everything else byte for byte.
/// </summary>
public class PackValidatorFixtureTests
{
    private static readonly ILookup<string, (string Level, string Message)> Python = WidgetPackFixtures.Expected();

    public static TheoryData<string> GoodPacks => WidgetPackFixtures.Data("good");
    public static TheoryData<string> BadPacks => WidgetPackFixtures.Data("bad");
    public static TheoryData<string> RealPacks => WidgetPackFixtures.Data("packs");

    [Fact]
    public void The_fixture_sets_are_all_there()
    {
        Assert.Equal(8, WidgetPackFixtures.Names("good").Count());
        Assert.Equal(31, WidgetPackFixtures.Names("bad").Count());
        Assert.Equal(new[] { "hourglass", "oled-neon", "oled-neon-effects", "oled-neon-palettes" }, WidgetPackFixtures.Names("packs"));
    }

    [Theory]
    [MemberData(nameof(GoodPacks))]
    public void Every_good_fixture_passes_strict_with_no_findings(string name)
    {
        var r = PackValidator.Validate(WidgetPackFixtures.Load("good", name), name, strict: true);
        Assert.Empty(r.Errors);
        Assert.Empty(r.Warnings);
        Assert.True(r.Ok);
        Assert.Equal(name, r.Manifest!.Id);
    }

    [Theory]
    [MemberData(nameof(RealPacks))]
    public void Every_first_party_pack_is_strict_clean(string name)
    {
        var r = PackValidator.Validate(WidgetPackFixtures.Load("packs", name), name, strict: true);
        Assert.Empty(r.Errors);
        Assert.Empty(r.Warnings);
        Assert.Equal(name, r.Manifest!.Id);
    }

    [Fact]
    public void Every_bad_fixture_has_an_expectation_from_the_reference()
    {
        foreach (var name in WidgetPackFixtures.Names("bad"))
            Assert.True(Python["bad/" + name].Any(), $"python-expected.tsv has no line for bad/{name}");
    }

    [Theory]
    [MemberData(nameof(BadPacks))]
    public void Every_bad_fixture_fails_with_exactly_the_reference_errors(string name)
    {
        var expected = Python["bad/" + name].Where(f => f.Level == "ERROR").Select(f => f.Message).ToList();
        if (expected.Count == 0)
            return;   // bad-theme-light-bg: warnings only, covered below
        var r = PackValidator.Validate(WidgetPackFixtures.Load("bad", name), name);
        Assert.False(r.Ok);
        Assert.Equal(expected.Count, r.Errors.Count);
        for (int i = 0; i < expected.Count; i++)
            AssertSameMessage(expected[i], r.Errors[i]);
    }

    [Fact]
    public void Light_theme_only_warns_and_fails_only_under_strict()
    {
        const string name = "bad-theme-light-bg";
        var expected = Python["bad/" + name].Select(f => f.Message).ToList();
        Assert.Equal(5, expected.Count);

        var lenient = PackValidator.Validate(WidgetPackFixtures.Load("bad", name), name);
        Assert.True(lenient.Ok);
        Assert.Empty(lenient.Errors);
        Assert.Equal(expected.Count, lenient.Warnings.Count);
        for (int i = 0; i < expected.Count; i++)
            AssertSameMessage(expected[i], lenient.Warnings[i]);
        Assert.Contains(lenient.Warnings, w => w.Contains("bg is light"));

        var strict = PackValidator.Validate(WidgetPackFixtures.Load("bad", name), name, strict: true);
        Assert.False(strict.Ok);
        Assert.Equal(5, strict.Errors.Count);
        Assert.Empty(strict.Warnings);
        Assert.Null(strict.Manifest);
    }

    /// <summary>test_validate.py's BAD table: the substring the one expected error must carry.</summary>
    [Theory]
    [InlineData("bad-trademark-name", "trademark: pack.json name contains 'claude'")]
    [InlineData("bad-trademark-model-word", "contains 'sonnet'")]
    [InlineData("bad-script-in-json", "contains '<script'")]
    [InlineData("bad-banned-extension", ".exe files are banned")]
    [InlineData("bad-unlisted-file", "not listed in pack.json files")]
    [InlineData("bad-preview-too-wide", "limit is 4096 pixels")]
    [InlineData("bad-fake-png", "not a PNG")]
    [InlineData("bad-png-crc", "bad CRC")]
    [InlineData("bad-font-license", "font packs must use OFL-1.1 or Apache-2.0")]
    [InlineData("bad-font-magic", "not a TrueType/OpenType font")]
    [InlineData("bad-sprite-missing-anim", "required animation 'celebrate' is missing")]
    [InlineData("bad-sprite-grid", "not a whole number of 8x8 frames")]
    [InlineData("bad-sprite-index", "frame index 3 but the sheet has only 2 frames")]
    [InlineData("bad-sprite-stage-order", "must be greater than the previous stage")]
    [InlineData("bad-sprite-mood", "moods.exhausted names animation 'snore'")]
    [InlineData("bad-theme-missing-field", "accent is required")]
    [InlineData("bad-id-mismatch", "does not match the folder name")]
    [InlineData("bad-no-license", "LICENSE is missing")]
    [InlineData("bad-license-text", "LICENSE text does not look like MIT")]
    [InlineData("bad-effects-range", "period_ms must be an integer from 400 to 10000")]
    [InlineData("bad-palette-stops", "colors must be 1 to 4")]
    [InlineData("bad-pixel-char", "sheet.json: frames[0][3][3] is 'g'; a pixel is '.' (transparent) or 0-9, a-f")]
    [InlineData("bad-pixel-index", "frames[0][2][3] is palette index '3' (3) but the palette has only 3 colors")]
    [InlineData("bad-pixel-row-length", "frames[0][5] is 7 characters, expected 8 (frame_w)")]
    [InlineData("bad-pixel-rows", "frames[0] has 7 rows, expected 8 (frame_h)")]
    [InlineData("bad-pixel-frames", "animations use frame index 3 but the pixel sheet has only 3 frames")]
    [InlineData("bad-pixel-white", "palette[1] is pure white (#FFFFFF)")]
    [InlineData("bad-pixel-colors", "palette must be 1 to 16 #rrggbb colors (got 17)")]
    [InlineData("bad-pixel-frame-size", "frame_w and frame_h must be integers from 8 to 32, got 40 x 40")]
    [InlineData("bad-sprite-v1-json-sheet", "'sheet.json' is a .json pixel sheet, which needs sprite.json schema_version 2")]
    public void Bad_fixture_fails_for_its_named_reason(string name, string needle)
    {
        var r = PackValidator.Validate(WidgetPackFixtures.Load("bad", name), name);
        Assert.Single(r.Errors, e => e.Contains(needle, StringComparison.Ordinal));
        // One mistake, one error; the .exe fixture also breaks the palette listing rule, as in the reference.
        Assert.Equal(name == "bad-banned-extension" ? 2 : 1, r.Errors.Count);
    }

    [Fact]
    public void Trademark_error_says_why()
    {
        var r = PackValidator.Validate(WidgetPackFixtures.Load("bad", "bad-trademark-name"), "bad-trademark-name");
        Assert.Contains("no Anthropic or Claude marks", r.Errors[0]);
        Assert.Contains("model names as branding", r.Errors[0]);
    }

    /// <summary>Theme files go through Core's ThemeLint, whose sentences extend the reference's (same facts, then the
    /// fix); every other message is the reference's exactly.</summary>
    private static void AssertSameMessage(string python, string csharp)
    {
        if (python.StartsWith("themes/", StringComparison.Ordinal))
            Assert.StartsWith(python, csharp);
        else
            Assert.Equal(python, csharp);
    }
}
