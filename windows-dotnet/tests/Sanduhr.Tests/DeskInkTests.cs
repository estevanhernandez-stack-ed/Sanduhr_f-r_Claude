using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Desk ink: the hex spec helpers ported from the Mac's DeskThemeTests, the six resolve rules, and the settings JSON.</summary>
public class DeskInkTests
{
    // ---- Ported from DeskThemeTests.swift (inkHexes / normalized) ----

    [Fact]
    public void OneColorIsDoubled()
    {
        var ink = "9ad7ff";
        Assert.Equal(new[] { ink, ink }, InkHex.Stops(ink));
    }

    [Fact]
    public void AGradientKeepsEveryStopAndTrimsSpacesAndHash()
    {
        Assert.Equal(new[] { "531b93", "012089", "00fdff" }, InkHex.Stops("531b93, 012089,#00FDFF"));
        Assert.Equal(new[] { "531b93", "00fdff" }, InkHex.Stops("531b93,00fdff"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" , ")]
    [InlineData("zzz,#12,1234567")]
    public void EmptyOrInvalidInkFallsBackToWhite(string spec)
    {
        Assert.Equal(new[] { "ffffff", "ffffff" }, InkHex.Stops(spec));
    }

    [Fact]
    public void BadEntriesAreSkippedAndShortHexExpands()
    {
        Assert.Equal(new[] { "00aaff", "00aaff" }, InkHex.Stops("nope, #0AF"));
        Assert.Null(InkHex.Normalize("nope"));
        Assert.Equal("9ad7ff", InkHex.Normalize("#9AD7FF"));
        Assert.Equal("ffffff", InkHex.Normalize("fff"));
        Assert.Equal("ffffff", InkHex.Normalize("  FFF  "));
        Assert.Null(InkHex.Normalize(null));
        Assert.Null(InkHex.Normalize("12345"));
        Assert.Null(InkHex.Normalize("gggggg"));
    }

    [Fact]
    public void StopsUsesTheGivenFallbackWhenNothingIsValid()
    {
        Assert.Equal(new[] { "9ad7ff", "9ad7ff" }, InkHex.Stops("???", "9ad7ff"));
    }

    [Fact]
    public void PresetsMatchTheMac()
    {
        var all = DeskInkPresets.All;
        Assert.Equal(5, all.Count);
        Assert.Equal(("Ice gradient", "8f5bd6,3a63e0,33fdff"), all[0]);
        Assert.Equal(("Ice gradient, exact", "531b93,012089,00fdff"), all[1]);
        Assert.Equal(("Sky", "9ad7ff"), all[2]);
        Assert.Equal(("Marquee", "ffd08a"), all[3]);
        Assert.Equal(("White", "ffffff"), all[4]);
        Assert.All(all, p => Assert.NotEmpty(InkHex.Stops(p.Value)));
    }

    // ---- Luminance ----

    [Fact]
    public void LuminanceMatchesWcagEndpoints()
    {
        Assert.Equal(1.0, InkHex.Luminance("ffffff"), 4);
        Assert.Equal(0.0, InkHex.Luminance("000000"), 4);
        Assert.Equal(0.2159, InkHex.Luminance("808080"), 3);
        Assert.Equal(0.2126, InkHex.Luminance("ff0000"), 4);
        Assert.Equal(0.7152, InkHex.Luminance("00ff00"), 4);
        Assert.Equal(0.0722, InkHex.Luminance("0000ff"), 4);
        Assert.Equal(InkHex.Luminance("ffffff"), InkHex.Luminance("#FFF"), 6);
    }

    // ---- Settings JSON ----

    [Fact]
    public void DefaultIsAutoWhiteShadowOn()
    {
        var d = DeskInkSettings.Default;
        Assert.Equal(DeskInkMode.Auto, d.Mode);
        Assert.Equal("ffffff", d.Color);
        Assert.True(d.Shadow);
        Assert.Equal(d, DeskInkSettings.Decode(null));
        Assert.Equal(d, DeskInkSettings.Decode(new JsonObject()));
        Assert.Equal(d, DeskInkSettings.Decode(JsonValue.Create("light")));
    }

    [Theory]
    [InlineData("auto", DeskInkMode.Auto)]
    [InlineData("light", DeskInkMode.Light)]
    [InlineData("dark", DeskInkMode.Dark)]
    [InlineData("theme", DeskInkMode.Theme)]
    [InlineData("custom", DeskInkMode.Custom)]
    [InlineData("Dark", DeskInkMode.Dark)]
    [InlineData("sepia", DeskInkMode.Auto)]
    [InlineData("", DeskInkMode.Auto)]
    public void ModeWordsDecode(string word, DeskInkMode expected)
    {
        var node = JsonNode.Parse($$"""{"mode":"{{word}}"}""");
        Assert.Equal(expected, DeskInkSettings.Decode(node).Mode);
    }

    [Fact]
    public void DecodeReadsAllThreeMembers()
    {
        var s = DeskInkSettings.Decode(JsonNode.Parse("""{"mode":"custom","color":"531b93,00fdff","shadow":false}"""));
        Assert.Equal(new DeskInkSettings(DeskInkMode.Custom, "531b93,00fdff", false), s);
    }

    [Fact]
    public void OddMembersFallBackToTheirDefaults()
    {
        var s = DeskInkSettings.Decode(JsonNode.Parse("""{"mode":3,"color":7,"shadow":"yes"}"""));
        Assert.Equal(DeskInkSettings.Default, s);
        var partial = DeskInkSettings.Decode(JsonNode.Parse("""{"mode":"dark","shadow":false}"""));
        Assert.Equal(new DeskInkSettings(DeskInkMode.Dark, "ffffff", false), partial);
    }

    [Fact]
    public void EncodeWritesAllThreeAndRoundTrips()
    {
        var s = new DeskInkSettings(DeskInkMode.Theme, "9ad7ff", false);
        var o = s.Encode();
        Assert.Equal("theme", (string?)o["mode"]);
        Assert.Equal("9ad7ff", (string?)o["color"]);
        Assert.False((bool?)o["shadow"]);
        Assert.Equal(s, DeskInkSettings.Decode(JsonNode.Parse(o.ToJsonString())));
        Assert.Equal("auto", (string?)DeskInkSettings.Default.Encode()["mode"]);
    }

    // ---- Resolve ----

    private static void SameInk(ResolvedInk expected, ResolvedInk actual)
    {
        Assert.Equal(expected.Stops, actual.Stops);
        Assert.Equal(expected with { Stops = actual.Stops }, actual);
    }

    private static DeskInkSettings S(DeskInkMode mode, string color = "ffffff", bool shadow = true) => new(mode, color, shadow);

    [Fact]
    public void LightIsWhiteWithADarkShadow()
    {
        var r = DeskInk.Resolve(S(DeskInkMode.Light), null, 0.9);
        Assert.Equal(new[] { "ffffff", "ffffff" }, r.Stops);
        Assert.False(r.DarkInk);
        Assert.Equal("000000", r.ShadowHex);
        Assert.Equal(0.65, r.ShadowOpacity);
        Assert.Equal(0.92, r.TextOpacity);
    }

    [Fact]
    public void DarkIsSlateWithALightShadow()
    {
        var r = DeskInk.Resolve(S(DeskInkMode.Dark), null, 0.1);
        Assert.Equal(new[] { "1b1f27", "1b1f27" }, r.Stops);
        Assert.True(r.DarkInk);
        Assert.Equal("ffffff", r.ShadowHex);
        Assert.Equal(0.45, r.ShadowOpacity);
        Assert.Equal(0.92, r.TextOpacity);
    }

    [Fact]
    public void ShadowOffZeroesTheShadowInEveryMode()
    {
        foreach (var mode in Enum.GetValues<DeskInkMode>())
            Assert.Equal(0, DeskInk.Resolve(S(mode, "336699", shadow: false), "112233", 0.7).ShadowOpacity);
    }

    [Fact]
    public void ThemeUsesTheThemeTextAndFlipsTheShadowByBrightness()
    {
        var light = DeskInk.Resolve(S(DeskInkMode.Theme), "#F0F0F0", null);
        Assert.Equal(new[] { "f0f0f0", "f0f0f0" }, light.Stops);
        Assert.False(light.DarkInk);
        Assert.Equal("000000", light.ShadowHex);

        var dark = DeskInk.Resolve(S(DeskInkMode.Theme), "202830", null);
        Assert.Equal(new[] { "202830", "202830" }, dark.Stops);
        Assert.True(dark.DarkInk);
        Assert.Equal("ffffff", dark.ShadowHex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a color")]
    public void ThemeWithoutAUsableColorReadsAsLight(string? text)
    {
        var r = DeskInk.Resolve(S(DeskInkMode.Theme), text, 0.9);
        SameInk(DeskInk.Resolve(S(DeskInkMode.Light), null, null), r);
    }

    [Fact]
    public void CustomUsesTheSpecAndJudgesDarknessByTheFirstStop()
    {
        var ice = DeskInk.Resolve(S(DeskInkMode.Custom, "8f5bd6,3a63e0,33fdff"), null, null);
        Assert.Equal(new[] { "8f5bd6", "3a63e0", "33fdff" }, ice.Stops);
        Assert.True(ice.DarkInk);

        var sky = DeskInk.Resolve(S(DeskInkMode.Custom, "9ad7ff"), null, null);
        Assert.Equal(new[] { "9ad7ff", "9ad7ff" }, sky.Stops);
        Assert.False(sky.DarkInk);

        var firstLight = DeskInk.Resolve(S(DeskInkMode.Custom, "ffffff,000000"), null, null);
        Assert.False(firstLight.DarkInk);

        var bad = DeskInk.Resolve(S(DeskInkMode.Custom, "???"), null, null);
        Assert.Equal(new[] { "ffffff", "ffffff" }, bad.Stops);
    }

    [Fact]
    public void AutoPicksDarkInkOnlyAboveTheThreshold()
    {
        Assert.Equal(0.55, DeskInk.AutoThreshold);
        Assert.True(DeskInk.Resolve(S(DeskInkMode.Auto), null, 0.56).DarkInk);
        Assert.True(DeskInk.Resolve(S(DeskInkMode.Auto), null, 1.0).DarkInk);
        Assert.False(DeskInk.Resolve(S(DeskInkMode.Auto), null, 0.55).DarkInk);
        Assert.False(DeskInk.Resolve(S(DeskInkMode.Auto), null, 0.2).DarkInk);
        Assert.Equal(new[] { "1b1f27", "1b1f27" }, DeskInk.Resolve(S(DeskInkMode.Auto), null, 0.8).Stops);
    }

    [Fact]
    public void AutoWithUnknownBrightnessIsLight()
    {
        var light = DeskInk.Resolve(S(DeskInkMode.Light), null, null);
        SameInk(light, DeskInk.Resolve(S(DeskInkMode.Auto), null, null));
        SameInk(light, DeskInk.Resolve(S(DeskInkMode.Auto), null, double.NaN));
    }

    // ---- MessageColor ----

    [Theory]
    [InlineData("", false, "9ad7ff")]
    [InlineData("", true, "1f4f86")]
    [InlineData("  ", false, "9ad7ff")]
    [InlineData("nope", true, "1f4f86")]
    [InlineData("#FFD08A", false, "ffd08a")]
    [InlineData("#FFD08A", true, "ffd08a")]
    [InlineData("f80", true, "ff8800")]
    [InlineData("5b8cff, A86BFF", false, "5b8cff,a86bff")]
    public void MessageColorFollowsTheInkUnlessTheUserSetOne(string setting, bool darkInk, string expected)
    {
        Assert.Equal(expected, DeskInk.MessageColor(setting, darkInk));
    }

    [Fact]
    public void MessageColorToleratesNull()
    {
        Assert.Equal("9ad7ff", DeskInk.MessageColor(null, false));
    }
}

/// <summary>3.9.1: the palettes of installed palette packs join the Desk ink presets, after the built-ins.</summary>
public sealed class DeskInkPackPresetsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-packink-" + Guid.NewGuid().ToString("N")[..10]);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private WidgetStore Store() => new(Path.Combine(_root, "widgets"), WidgetTestKit.EncodePng);

    private static void Install(WidgetStore store, string group, string name, WidgetSource source = WidgetSource.Catalog)
    {
        var files = WidgetPackFixtures.Load(group, name);
        var v = PackValidator.Validate(files, name, strict: true);
        Assert.True(v.Ok, string.Join(" | ", v.Errors));
        Assert.True(store.Install(files, v.Manifest!, source).Ok);
    }

    [Fact]
    public void Pack_palettes_follow_the_built_ins_named_for_their_pack()
    {
        var store = Store();
        Install(store, "good", "good-palette");

        var merged = PackInkPresets.Merged(store);

        Assert.Equal(DeskInkPresets.All, merged.Take(5));
        Assert.Equal(6, merged.Count);
        Assert.Equal(("plain · Fixture palette", "ff2a6d,05d9e8"), merged[5]);
    }

    [Fact]
    public void The_built_in_list_itself_stays_five()
    {
        var store = Store();
        Install(store, "good", "good-palette");
        _ = PackInkPresets.Merged(store);
        Assert.Equal(5, DeskInkPresets.All.Count);
    }

    [Fact]
    public void The_first_party_palette_pack_adds_every_palette_with_hashless_values()
    {
        var store = Store();
        Install(store, "packs", "oled-neon-palettes");

        var added = PackInkPresets.Merged(store).Skip(5).ToList();

        Assert.NotEmpty(added);
        Assert.All(added, p =>
        {
            Assert.Contains(" · ", p.Name);
            Assert.DoesNotContain('#', p.Value);
            Assert.NotEmpty(InkHex.Stops(p.Value));
        });
        Assert.Contains(added, p => p.Value == "ff2a6d,d16ba5,05d9e8");
    }

    [Fact]
    public void A_claude_made_palette_flows_through_and_a_disabled_or_broken_pack_does_not()
    {
        var store = Store();
        Install(store, "good", "good-palette", WidgetSource.Claude);
        Assert.Equal(6, PackInkPresets.Merged(store).Count);

        File.WriteAllText(Path.Combine(store.PackDir("good-palette"), "palettes.json"), "{ not json");
        Assert.Equal(5, PackInkPresets.Merged(store).Count);

        File.WriteAllText(store.RegistryFile, File.ReadAllText(store.RegistryFile).Replace("\"enabled\": true", "\"enabled\": false"));
        Assert.Equal(5, PackInkPresets.Merged(store).Count);
    }
}
