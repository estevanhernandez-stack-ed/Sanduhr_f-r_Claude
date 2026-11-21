using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The design format's <c>glow</c> field (5.0 spec 5, section 7): its three forms, the either-or with <c>shadow</c>, and
/// how it merges, compares and reaches the settings.</summary>
public class DeskDesignGlowTests
{
    private static JsonObject Obj(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static DeskDesign Design(string json) => DeskDesign.Parse(Obj(json)).Design!;

    [Fact]
    public void GlowIsAFieldRightAfterShadow()
    {
        var fields = DeskDesign.FieldNames.ToList();
        Assert.Equal(fields.IndexOf("shadow") + 1, fields.IndexOf("glow"));
    }

    [Fact]
    public void TheThreeFormsParse()
    {
        Assert.Equal(new DeskGlowChoice(false), Design("""{"glow":false}""").Glow);
        Assert.Equal(new DeskGlowChoice(true), Design("""{"glow":true}""").Glow);
        Assert.Equal(new DeskGlowChoice(true, 0.8), Design("""{"glow":0.8}""").Glow);
        Assert.Equal(new DeskGlowChoice(true, 0.2), Design("""{"glow":0.2}""").Glow);
        Assert.Equal(new DeskGlowChoice(true, 1.0), Design("""{"glow":1}""").Glow);
        Assert.Null(Design("""{"plate":true}""").Glow);
        Assert.Null(Design("""{"glow":null}""").Glow);
    }

    [Theory]
    [InlineData("""{"glow":0.1}""")]
    [InlineData("""{"glow":1.5}""")]
    [InlineData("""{"glow":0}""")]
    [InlineData("""{"glow":-1}""")]
    [InlineData("""{"glow":"on"}""")]
    [InlineData("""{"glow":[]}""")]
    [InlineData("""{"glow":{"strength":0.5}}""")]
    public void AnythingElseIsRefusedAtGlow(string json)
    {
        var parsed = DeskDesign.Parse(Obj(json));
        Assert.Null(parsed.Design);
        Assert.Contains(parsed.Reasons, r => r.Field == "glow");
    }

    [Theory]
    [InlineData("""{"glow":true,"shadow":true}""")]
    [InlineData("""{"glow":0.5,"shadow":true}""")]
    public void GlowOnWithShadowOnIsRefusedNamingBoth(string json)
    {
        var parsed = DeskDesign.Parse(Obj(json));
        Assert.Null(parsed.Design);
        var reason = Assert.Single(parsed.Reasons, r => r.Field == "glow");
        Assert.Contains("shadow", reason.Message);
    }

    [Theory]
    [InlineData("""{"glow":false,"shadow":true}""")]
    [InlineData("""{"glow":0.5,"shadow":false}""")]
    [InlineData("""{"glow":false,"shadow":false}""")]
    public void TheOtherPairsAreFine(string json) => Assert.NotNull(DeskDesign.Parse(Obj(json)).Design);

    [Theory]
    [InlineData("""{"glow":false}""")]
    [InlineData("""{"glow":true}""")]
    [InlineData("""{"glow":0.8}""")]
    public void TheDesignWritesBackWhatItRead(string json) =>
        Assert.Equal(json, Design(json).ToJson().ToJsonString());

    [Fact]
    public void AProposalThatTurnsTheGlowOnTurnsTheShadowOff()
    {
        var merged = DeskDesign.Merge(Design("""{"shadow":true,"glow":false}"""), Design("""{"glow":0.8}"""));
        Assert.Equal(new DeskGlowChoice(true, 0.8), merged.Glow);
        Assert.False(merged.Shadow);
    }

    [Fact]
    public void AProposalThatTurnsTheShadowOnTurnsTheGlowOff()
    {
        var merged = DeskDesign.Merge(Design("""{"shadow":false,"glow":0.8}"""), Design("""{"shadow":true}"""));
        Assert.True(merged.Shadow);
        Assert.Equal(DeskGlowChoice.Off, merged.Glow);
    }

    [Fact]
    public void GlowTrueKeepsTheStrengthAlreadySet()
    {
        var merged = DeskDesign.Merge(Design("""{"shadow":false,"glow":0.8}"""), Design("""{"glow":true}"""));
        Assert.Equal(new DeskGlowChoice(true, 0.8), merged.Glow);
    }

    [Fact]
    public void AProposalThatSaysNeitherLeavesBoth()
    {
        var current = Design("""{"shadow":false,"glow":0.8}""");
        var merged = DeskDesign.Merge(current, Design("""{"plate":true}"""));
        Assert.Equal(current.Glow, merged.Glow);
        Assert.False(merged.Shadow);
    }

    [Fact]
    public void TurningTheShadowOffLeavesTheGlowAlone()
    {
        var merged = DeskDesign.Merge(Design("""{"shadow":true,"glow":false}"""), Design("""{"shadow":false}"""));
        Assert.Equal(DeskGlowChoice.Off, merged.Glow);
        Assert.False(merged.Shadow);
    }

    [Fact]
    public void TheDiffNamesTheGlow()
    {
        var a = Design("""{"shadow":true,"glow":false}""");
        Assert.Contains("Neon glow: on at 80%", DeskDesign.Diff(a, DeskDesign.Merge(a, Design("""{"glow":0.8}"""))));
        Assert.Contains("Text shadow: off", DeskDesign.Diff(a, DeskDesign.Merge(a, Design("""{"glow":0.8}"""))));

        var lit = Design("""{"shadow":false,"glow":0.8}""");
        Assert.Contains("Neon glow: off", DeskDesign.Diff(lit, DeskDesign.Merge(lit, Design("""{"glow":false}"""))));
        Assert.DoesNotContain(DeskDesign.Diff(lit, DeskDesign.Merge(lit, Design("""{"plate":true}"""))), l => l.StartsWith("Neon glow"));
        // A Desk that never said anything about the glow is a Desk with it off.
        Assert.DoesNotContain(DeskDesign.Diff(Design("""{"shadow":true}"""), Design("""{"glow":false}""")), l => l.StartsWith("Neon glow"));
    }

    [Fact]
    public void ADesignFromBeforeTheGlowIsTheSameLookAsOneWithItOff()
    {
        Assert.Equal(DeskDesigns.Canonical(Design("""{"shadow":true}""")), DeskDesigns.Canonical(Design("""{"shadow":true,"glow":false}""")));
        Assert.NotEqual(DeskDesigns.Canonical(Design("""{"shadow":false}""")), DeskDesigns.Canonical(Design("""{"shadow":false,"glow":0.8}""")));
        Assert.NotEqual(DeskDesigns.Canonical(Design("""{"shadow":false,"glow":0.5}""")), DeskDesigns.Canonical(Design("""{"shadow":false,"glow":0.8}""")));
    }

    [Fact]
    public void TheSettingsADesignWritesCarryTheGlow()
    {
        var was = new DeskInkSettings(DeskInkMode.Auto, "ffffff", true, false, 0.4);

        var on = DeskDesignApply.Ink(was, Design("""{"glow":0.8,"shadow":false}"""));
        Assert.True(on.Glow);
        Assert.Equal(0.8, on.GlowStrength);
        Assert.False(on.Shadow);

        var keeps = DeskDesignApply.Ink(was, Design("""{"glow":true}"""));
        Assert.True(keeps.Glow);
        Assert.Equal(0.4, keeps.GlowStrength);
        Assert.False(keeps.Shadow);

        var off = DeskDesignApply.Ink(on, Design("""{"glow":false}"""));
        Assert.False(off.Glow);
        Assert.Equal(0.8, off.GlowStrength);

        var shadowed = DeskDesignApply.Ink(on, Design("""{"shadow":true}"""));
        Assert.True(shadowed.Shadow);
        Assert.False(shadowed.Glow);

        Assert.Equal(was, DeskDesignApply.Ink(was, Design("""{"plate":true}""")));
    }

    [Fact]
    public void GetDeskReportsTheGlow()
    {
        var off = DeskState.Current(DeskSettings.Default, DeskInkSettings.Default, LauncherModel.Empty, null);
        Assert.Equal(DeskGlowChoice.Off, off.Glow);
        Assert.False((bool)off.ToJson()["glow"]!);

        var on = DeskState.Current(DeskSettings.Default, DeskInkSettings.Default.WithGlow(true).WithGlowStrength(0.8), LauncherModel.Empty, null);
        Assert.Equal(0.8, (double)on.ToJson()["glow"]!);
        Assert.False((bool)on.ToJson()["shadow"]!);
    }

    [Fact]
    public void WhatGetDeskReportsRoundTripsThroughAProposal()
    {
        var ink = DeskInkSettings.Default.WithGlow(true).WithGlowStrength(0.8);
        var current = DeskState.Current(DeskSettings.Default, ink, LauncherModel.Empty, null);
        Assert.True(DeskDesign.Validate(current.ToJson(), DeskDesignContext.Unknown).Ok);
        Assert.Equal(ink, DeskDesignApply.Ink(DeskInkSettings.Default, DeskDesign.Merge(null, current)));
    }
}
