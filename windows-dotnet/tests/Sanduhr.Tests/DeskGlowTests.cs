using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The neon glow in the ink setting (5.0 spec 5, section 7): the numbers, the JSON, and glow and shadow as either-or.</summary>
public class DeskGlowTests
{
    private static JsonNode Node(string json) => JsonNode.Parse(json)!;

    [Fact]
    public void TheDefaultIsOffAtSixty()
    {
        Assert.False(DeskInkSettings.Default.Glow);
        Assert.Equal(0.6, DeskInkSettings.Default.GlowStrength);
        Assert.Equal(DeskInkSettings.Default, DeskInkSettings.Decode(Node("""{"mode":"auto","color":"ffffff","shadow":true}""")));
    }

    [Theory]
    [InlineData(0.2, 6, 0.45)]
    [InlineData(0.6, 14, 0.675)]
    [InlineData(1.0, 22, 0.9)]
    public void StrengthGivesTheRadiusAndTheOpacity(double strength, double radius, double opacity)
    {
        Assert.Equal(radius, DeskGlow.Radius(strength), 6);
        Assert.Equal(opacity, DeskGlow.Opacity(strength), 6);
    }

    [Theory]
    [InlineData(0, 16)]     // off
    [InlineData(0.2, 16)]   // radius 6 plus 4 is under the shadow's room
    [InlineData(0.6, 18)]
    [InlineData(1.0, 26)]
    public void ThePaddingIsTheLargerOfSixteenAndTheRadiusPlusFour(double strength, double pad) =>
        Assert.Equal(pad, DeskGlow.Pad(strength), 6);

    [Theory]
    [InlineData(5, 1.0)]
    [InlineData(0.01, 0.2)]
    [InlineData(double.NaN, 0.6)]
    [InlineData(0.456, 0.46)]
    public void AStrengthIsClampedAndRounded(double raw, double want) => Assert.Equal(want, DeskGlow.Clamp(raw));

    [Fact]
    public void DecodeReadsTheGlowAndItsStrength()
    {
        var s = DeskInkSettings.Decode(Node("""{"mode":"custom","color":"ffd08a","shadow":false,"glow":true,"glow_strength":0.8}"""));
        Assert.True(s.Glow);
        Assert.Equal(0.8, s.GlowStrength);
        Assert.False(s.Shadow);
    }

    [Fact]
    public void AFileWithBothOnKeepsTheGlow()
    {
        var s = DeskInkSettings.Decode(Node("""{"shadow":true,"glow":true}"""));
        Assert.True(s.Glow);
        Assert.False(s.Shadow);
    }

    [Theory]
    [InlineData("""{"glow":"yes"}""")]
    [InlineData("""{"glow":1}""")]
    [InlineData("""{"glow_strength":"high"}""")]
    public void AnOddMemberReadsAsItsDefault(string json)
    {
        var s = DeskInkSettings.Decode(Node(json));
        Assert.False(s.Glow);
        Assert.Equal(0.6, s.GlowStrength);
    }

    [Fact]
    public void DecodeClampsTheStrength() =>
        Assert.Equal(1.0, DeskInkSettings.Decode(Node("""{"glow_strength":7}""")).GlowStrength);

    [Fact]
    public void EncodeLeavesAnInstallWithoutGlowAsItWas() =>
        Assert.Equal("""{"mode":"auto","color":"ffffff","shadow":true}""", DeskInkSettings.Default.Encode().ToJsonString());

    [Fact]
    public void EncodeWritesTheGlowAndRoundTrips()
    {
        var s = new DeskInkSettings(DeskInkMode.Custom, "ffd08a", false, true, 0.8);
        var o = s.Encode();
        Assert.True((bool)o["glow"]!);
        Assert.Equal(0.8, (double)o["glow_strength"]!);
        Assert.Equal(s, DeskInkSettings.Decode(Node(o.ToJsonString())));
    }

    [Fact]
    public void AStrengthSetWhileTheGlowIsOffIsKept()
    {
        var s = DeskInkSettings.Default.WithGlowStrength(0.9);
        Assert.Null(s.Encode()["glow"]);
        Assert.Equal(s, DeskInkSettings.Decode(Node(s.Encode().ToJsonString())));
    }

    [Fact]
    public void GlowAndShadowAreEitherOr()
    {
        var glowing = DeskInkSettings.Default.WithGlow(true);
        Assert.True(glowing.Glow);
        Assert.False(glowing.Shadow);
        var shadowed = glowing.WithShadow(true);
        Assert.True(shadowed.Shadow);
        Assert.False(shadowed.Glow);
        // Turning one off never turns the other on.
        Assert.False(shadowed.WithShadow(false).Glow);
        Assert.False(glowing.WithGlow(false).Shadow);
    }

    [Fact]
    public void ResolveCarriesTheGlowAndDropsTheShadow()
    {
        var ink = DeskInk.Resolve(new DeskInkSettings(DeskInkMode.Custom, "ffd08a", false, true, 0.8), null, null);
        Assert.Equal(0.8, ink.Glow);
        Assert.Equal(0, ink.ShadowOpacity);

        var plain = DeskInk.Resolve(DeskInkSettings.Default, null, 0.1);
        Assert.Equal(0, plain.Glow);
        Assert.True(plain.ShadowOpacity > 0);
    }

    [Fact]
    public void AutomaticInkGlowsToo()
    {
        var ink = DeskInk.Resolve(DeskInkSettings.Default.WithGlow(true), null, 0.9);
        Assert.True(ink.DarkInk);
        Assert.Equal(0.6, ink.Glow);
    }

    [Fact]
    public void GlowingPutsTheSwitchOnAnInkMadeElsewhere()
    {
        var followed = DeskInk.Resolve(new DeskInkSettings(DeskInkMode.Custom, "33fdff", true), null, null);
        var on = DeskInk.Glowing(followed, DeskInkSettings.Default.WithGlow(true).WithGlowStrength(1));
        Assert.Equal(1.0, on.Glow);
        Assert.Equal(0, on.ShadowOpacity);
        Assert.Same(followed, DeskInk.Glowing(followed, DeskInkSettings.Default));
    }

    [Fact]
    public void OledCareKeepsTheGlow() =>
        Assert.Equal(0.6, DeskInk.CapWhite(DeskInk.Resolve(DeskInkSettings.Default.WithGlow(true), null, null)).Glow);

    [Fact]
    public void MatchNowKeepsTheGlowWhenItIsOn()
    {
        var look = ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"]);
        var own = ThemeMatch.OwnSettings(look, DeskSettings.Default, DeskInkSettings.Default.WithGlow(true).WithGlowStrength(0.8),
            DeskMessageSettings.Default, NowPlayingSettings.Default);
        Assert.True(own.Ink.Glow);
        Assert.Equal(0.8, own.Ink.GlowStrength);
        Assert.False(own.Ink.Shadow);
        Assert.Equal(DeskInkMode.Custom, own.Ink.Mode);
    }

    [Fact]
    public void MatchNowTurnsTheShadowOnWhenThereIsNoGlow()
    {
        var look = ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"]);
        var own = ThemeMatch.OwnSettings(look, DeskSettings.Default, DeskInkSettings.Default with { Shadow = false },
            DeskMessageSettings.Default, NowPlayingSettings.Default);
        Assert.True(own.Ink.Shadow);
        Assert.False(own.Ink.Glow);
    }
}
