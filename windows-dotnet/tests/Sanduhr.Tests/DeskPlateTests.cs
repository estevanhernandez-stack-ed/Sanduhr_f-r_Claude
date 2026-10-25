using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DeskPlateTests
{
    [Fact]
    public void KeyTitleAndDefaultAreFrozen()
    {
        Assert.Equal("desk_plate", DeskPlate.Key);
        Assert.False(DeskPlate.DefaultOn);
        Assert.Equal("Plate behind the text", DeskPlate.Title);
        Assert.Equal(DeskPlate.Title, SettingsNames.DeskPlateSwitch);
    }

    [Fact]
    public void AnExistingSettingsFileWithoutTheKeyReadsOff()
    {
        var existing = new JsonObject { ["desk_enabled"] = true, ["desk_font"] = "Segoe UI" };
        Assert.False(DeskPlate.Read(existing));
        Assert.False(DeskPlate.Read(new JsonObject()));
        Assert.False(DeskPlate.Read(null));
    }

    [Fact]
    public void ARepeatLaunchOfAnExistingInstallSeedsNothing()
    {
        var existing = new JsonObject { ["desk_enabled"] = true };
        Assert.False(DeskPlate.Seed(existing, freshInstall: false));
        Assert.False(existing.ContainsKey(DeskPlate.Key));
        Assert.False(DeskPlate.Read(existing));
    }

    [Fact]
    public void AFreshInstallWritesItOn()
    {
        var fresh = new JsonObject();
        Assert.True(DeskPlate.Seed(fresh, freshInstall: true));
        Assert.True(DeskPlate.Read(fresh));
    }

    [Fact]
    public void SeedingNeverOverwritesAChoice()
    {
        var chose = new JsonObject { [DeskPlate.Key] = false };
        Assert.False(DeskPlate.Seed(chose, freshInstall: true));
        Assert.False(DeskPlate.Read(chose));
    }

    [Fact]
    public void TurningItOffSurvivesAnotherSeed()
    {
        var root = new JsonObject();
        DeskPlate.Seed(root, freshInstall: true);
        DeskPlate.Write(root, false);
        Assert.False(DeskPlate.Seed(root, freshInstall: true));
        Assert.False(DeskPlate.Read(root));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ItRoundTripsThroughTheSettingsObject(bool on)
    {
        var root = JsonNode.Parse("{\"desk_enabled\":true}")!.AsObject();
        DeskPlate.Write(root, on);
        var back = JsonNode.Parse(root.ToJsonString())!.AsObject();
        Assert.Equal(on, DeskPlate.Read(back));
        Assert.True(back["desk_enabled"]!.GetValue<bool>());
    }

    [Fact]
    public void AnOddValueReadsOff()
    {
        Assert.False(DeskPlate.Read(new JsonObject { [DeskPlate.Key] = "yes" }));
    }

    [Fact]
    public void PlateIsDarkBehindLightInkAndLightBehindDarkInk()
    {
        Assert.Equal("000000", DeskPlate.ColorHex(darkInk: false));
        Assert.Equal("ffffff", DeskPlate.ColorHex(darkInk: true));
    }

    [Fact]
    public void ATwoPieceStackGetsOnePlateNotOnePerPiece()
    {
        // One span over both pieces: a single plate, so no two plates overlap into a brighter seam.
        Assert.Equal((0, 1), DeskPlate.Span([false, false]));
    }

    [Fact]
    public void ASinglePieceStillGetsItsPlate()
    {
        Assert.Equal((0, 0), DeskPlate.Span([false]));
    }

    [Fact]
    public void TheGlowSpotAtEitherEndIsLeftOutOfThePlate()
    {
        Assert.Equal((1, 2), DeskPlate.Span([true, false, false]));
        Assert.Equal((0, 1), DeskPlate.Span([false, false, true]));
        Assert.Equal((1, 1), DeskPlate.Span([true, false, true]));
    }

    [Fact]
    public void AGlowBetweenPiecesStaysInsideTheSingleSpan()
    {
        Assert.Equal((0, 2), DeskPlate.Span([false, true, false]));
    }

    [Fact]
    public void AStackOfOnlyTheGlowOrNothingDrawsNoPlate()
    {
        Assert.Null(DeskPlate.Span([true]));
        Assert.Null(DeskPlate.Span([]));
    }

    [Fact]
    public void ReachAndBlurFitTheRoomADeskWindowLeaves()
    {
        Assert.True(DeskPlate.Reach + DeskPlate.Blur <= 16);
    }

    [Fact]
    public void TheSwitchIsFoundFromSettingsSearch()
    {
        var hits = SettingsIndex.Search("plate");
        Assert.Contains(hits, h => h.Entry.Page == SettingsPage.DeskLook && h.Entry.Anchor == SettingsIndex.DeskPlate && h.Rank == 0);
    }
}
