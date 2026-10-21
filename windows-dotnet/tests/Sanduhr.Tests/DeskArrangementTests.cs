using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DeskArrangementTests
{
    /// <summary>The Mac's standard layout, which carries the later-stage widgets.</summary>
    private const string MacStandard = "message:tl clock:bl claude:bl meetings:bl";

    private static string[] Widgets(IEnumerable<DeskPlacement> ps) => ps.Select(p => p.Widget).ToArray();

    // Layout string

    [Fact]
    public void StandardParsesToClockThenClaudeAtBottomLeft()
    {
        var a = new DeskArrangement(DeskArrangement.Standard);
        Assert.Equal([new DeskPlacement("clock", DeskAnchor.BL), new DeskPlacement("claude", DeskAnchor.BL)], a.Pieces);
        Assert.Equal(DeskArrangement.Standard, a.Layout);
    }

    [Theory]
    [InlineData("message:tl clock:bl claude:bl meetings:bl")]
    [InlineData("message:tl clock:bl meters:bl meetings:bl")]
    [InlineData("meters:bl clock:bl message:tr meetings:br nowPlaying:bl watchers:tr")]
    [InlineData("message:tl meetings:bl meters:bl clock:bl")]
    [InlineData("clock:tr claude:tr meters:tr nowPlaying:tr watchers:tl meetings:tl message:br")]
    public void AnOlderLayoutIsWrittenBackUnchanged(string layout)
    {
        var a = new DeskArrangement(layout);
        Assert.Equal(layout, a.Layout);
        Assert.All(a.Pieces, p => Assert.Equal(1, p.Scale));
    }

    [Theory]
    [InlineData("  claude:br   clock  a:b:c :bl meters: message:tl ", "claude:br message:tl")]
    [InlineData("radar:tr clock:tl", "clock:tl")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void StrayWordsAreDropped(string layout, string expected)
    {
        Assert.Equal(expected, new DeskArrangement(layout).Layout);
    }

    [Fact]
    public void NullLayoutIsEmpty()
    {
        Assert.Empty(new DeskArrangement((string?)null).Pieces);
    }

    [Fact]
    public void ReadsAnchorsOrderAndSizes()
    {
        var a = new DeskArrangement("clock:tc:1.2 message:mr meters:bc:0.6 meetings:ml watchers:bc");
        Assert.Equal(
        [
            new DeskPlacement("clock", DeskAnchor.TC, 1.2),
            new DeskPlacement("message", DeskAnchor.MR),
            new DeskPlacement("meters", DeskAnchor.BC, 0.6),
            new DeskPlacement("meetings", DeskAnchor.ML),
            new DeskPlacement("watchers", DeskAnchor.BC),
        ], a.Pieces);
        Assert.Equal(["meters", "watchers"], Widgets(a.Stack(DeskAnchor.BC)));
        Assert.Equal("clock:tc:1.2 message:mr meters:bc:0.6 meetings:ml watchers:bc", a.Layout);
    }

    [Fact]
    public void EveryKnownWordRoundTrips()
    {
        foreach (var w in DeskArrangement.Known)
        {
            var layout = $"{w}:tr:1.4";
            Assert.Equal(layout, new DeskArrangement(layout).Layout);
        }
        var all = string.Join(' ', DeskArrangement.Known.Select(w => $"{w}:mr"));
        Assert.Equal(all, new DeskArrangement(all).Layout);
    }

    [Fact]
    public void KnownIsTheMacList()
    {
        Assert.Equal(["message", "clock", "claude", "meters", "nowPlaying", "telemetry", "weather", "watchers", "launcher", "meetings", "glow"], DeskArrangement.Known);
        Assert.Equal(["clock", "claude", "meters"], DeskArrangement.DrawnInS5);
    }

    [Fact]
    public void EveryPieceCanSitAtEveryAnchor()
    {
        foreach (var w in DeskArrangement.Known)
            foreach (var anchor in DeskAnchors.All)
            {
                var a = new DeskArrangement($"{w}:{DeskAnchors.Word(anchor)}");
                Assert.Equal([w], Widgets(a.Stacks(DeskArrangement.Known)[anchor]));
            }
    }

    [Fact]
    public void AnUnknownAnchorFallsBackToThePiecesDefault()
    {
        var a = new DeskArrangement("message:zz clock:center meters:tl:1.2:x meetings:BL");
        Assert.Equal(DeskAnchor.TL, a.Placement("message")?.Anchor);
        Assert.Equal(DeskAnchor.BL, a.Placement("clock")?.Anchor);
        Assert.Equal(DeskAnchor.BL, a.Placement("meetings")?.Anchor);
        // Extra parts after the size are ignored; the piece is still kept.
        Assert.Equal(new DeskPlacement("meters", DeskAnchor.TL, 1.2), a.Placement("meters"));
        Assert.Equal(4, a.Pieces.Count);
    }

    [Fact]
    public void ClockWithUnknownAnchorIsBottomLeft()
    {
        Assert.Equal(DeskAnchor.BL, new DeskArrangement("clock:zz").Placement("clock")?.Anchor);
        Assert.Equal(DeskAnchor.TL, new DeskArrangement("message:zz").Placement("message")?.Anchor);
    }

    [Fact]
    public void ARepeatedWidgetKeepsItsLastWordAtTheLastPosition()
    {
        var a = new DeskArrangement("clock:bl message:tl clock:tr:1.4");
        Assert.Equal(["message", "clock"], Widgets(a.Pieces));
        Assert.Equal(new DeskPlacement("clock", DeskAnchor.TR, 1.4), a.Placement("clock"));
    }

    [Fact]
    public void StacksDropUndrawnWidgetsByDefault()
    {
        var a = new DeskArrangement(MacStandard + " nowPlaying:br meters:tc");
        var s = a.Stacks();
        Assert.Equal(["clock", "claude"], Widgets(s[DeskAnchor.BL]));
        Assert.Equal(["meters"], Widgets(s[DeskAnchor.TC]));
        Assert.False(s.ContainsKey(DeskAnchor.TL));
        Assert.False(s.ContainsKey(DeskAnchor.BR));
        Assert.Equal(2, s.Count);
    }

    [Fact]
    public void StacksTakeAnExplicitDrawnSet()
    {
        var a = new DeskArrangement(MacStandard);
        var s = a.Stacks(["message", "meetings"]);
        Assert.Equal(["message"], Widgets(s[DeskAnchor.TL]));
        Assert.Equal(["meetings"], Widgets(s[DeskAnchor.BL]));
    }

    // Sizes

    [Fact]
    public void SizesStayWithinBoundsAndSnapToSteps()
    {
        Assert.Equal(0.6, DeskArrangement.ClampScale(0.1));
        Assert.Equal(1.6, DeskArrangement.ClampScale(5));
        Assert.Equal(1.2, DeskArrangement.ClampScale(1.23));
        Assert.Equal(0.7, DeskArrangement.ClampScale(0.66));
        Assert.Equal(1, DeskArrangement.ClampScale(double.NaN));
        Assert.Equal(1, DeskArrangement.ClampScale(double.PositiveInfinity));
        Assert.Equal(11, DeskArrangement.ScaleSteps.Count);
        Assert.Equal(0.6, DeskArrangement.ScaleSteps[0]);
        Assert.Equal(1.6, DeskArrangement.ScaleSteps[^1]);
        Assert.Contains(1.0, DeskArrangement.ScaleSteps);
    }

    [Fact]
    public void ASizeInTheStringIsClampedAndOneIsNotWritten()
    {
        Assert.Equal(1.6, new DeskArrangement("clock:bl:9").Placement("clock")?.Scale);
        Assert.Equal(0.6, new DeskArrangement("clock:bl:0").Placement("clock")?.Scale);
        Assert.Equal(1, new DeskArrangement("clock:bl:abc").Placement("clock")?.Scale);
        Assert.Equal("clock:bl", new DeskArrangement("clock:bl:1.0").Layout);
        Assert.Equal("clock:bl:1.2", new DeskArrangement("clock:bl:1.24").Layout);
    }

    [Fact]
    public void ScaleIsWrittenLikePrintfG()
    {
        Assert.Equal("clock:bl:1.2", new DeskArrangement([new DeskPlacement("clock", DeskAnchor.BL, 1.2)]).Layout);
        Assert.Equal("clock:bl:0.6", new DeskArrangement([new DeskPlacement("clock", DeskAnchor.BL, 0.6)]).Layout);
        Assert.Equal("clock:bl:1.6", new DeskArrangement([new DeskPlacement("clock", DeskAnchor.BL, 1.6)]).Layout);
    }

    [Fact]
    public void SetScaleChangesOnePlacedPiece()
    {
        var a = new DeskArrangement(MacStandard);
        a.SetScale("clock", 1.3);
        a.SetScale("meters", 1.3); // not placed: nothing to size
        Assert.Equal("message:tl clock:bl:1.3 claude:bl meetings:bl", a.Layout);
        a.SetScale("clock", 1);
        Assert.Equal(MacStandard, a.Layout);
    }

    // Order within an anchor

    [Fact]
    public void DragDownLandsAfterTheTarget()
    {
        var a = new DeskArrangement(MacStandard);
        a.Move("clock", "meetings");
        Assert.Equal(["claude", "meetings", "clock"], Widgets(a.Stack(DeskAnchor.BL)));
    }

    [Fact]
    public void DragUpLandsBeforeTheTarget()
    {
        var a = new DeskArrangement(MacStandard);
        a.Move("meetings", "clock");
        Assert.Equal(["meetings", "clock", "claude"], Widgets(a.Stack(DeskAnchor.BL)));
        Assert.Equal("message:tl meetings:bl clock:bl claude:bl", a.Layout);
    }

    [Fact]
    public void DragOntoAnotherAnchorJoinsIt()
    {
        var a = new DeskArrangement("message:tl clock:bl:1.2 claude:bl");
        a.Move("clock", "message");
        Assert.Equal(["clock", "message"], Widgets(a.Stack(DeskAnchor.TL)));
        Assert.Equal(1.2, a.Placement("clock")?.Scale);
        Assert.Equal(["claude"], Widgets(a.Stack(DeskAnchor.BL)));
    }

    [Fact]
    public void NothingMovesOntoItselfOrAHiddenPiece()
    {
        var a = new DeskArrangement(MacStandard);
        a.Move("clock", "clock");
        a.Move("clock", "meters");
        a.Move("meters", "clock");
        Assert.Equal(MacStandard, a.Layout);
    }

    [Fact]
    public void MoveUpAndDownStopAtTheEnds()
    {
        var a = new DeskArrangement(MacStandard);
        a.Move("clock", -1);
        Assert.Equal(MacStandard, a.Layout);
        a.Move("clock", 1);
        Assert.Equal(["claude", "clock", "meetings"], Widgets(a.Stack(DeskAnchor.BL)));
        a.Move("meetings", 1);
        a.Move("clock", 1);
        Assert.Equal(["claude", "meetings", "clock"], Widgets(a.Stack(DeskAnchor.BL)));
    }

    [Fact]
    public void PlacingKeepsTheSizeAndTheOtherPiecesOrder()
    {
        var a = new DeskArrangement("meetings:bl clock:bl:1.4 message:tl");
        a.Place("clock", DeskAnchor.TC);
        Assert.Equal("meetings:bl clock:tc:1.4 message:tl", a.Layout);
        a.Place("clock", DeskAnchor.TC);
        Assert.Equal("meetings:bl clock:tc:1.4 message:tl", a.Layout);
        a.Place("clock", null);
        Assert.Equal("meetings:bl message:tl", a.Layout);
        a.Place("clock", DeskAnchor.BL);
        Assert.Equal(["clock", "meetings"], Widgets(a.Stack(DeskAnchor.BL)));
        Assert.Equal(1, a.Placement("clock")?.Scale);
    }

    [Fact]
    public void PlacingANewPieceIntoAnEmptyAnchorGoesByRank()
    {
        var a = new DeskArrangement("message:tl meters:bl");
        a.Place("clock", DeskAnchor.TR);
        Assert.Equal("message:tl clock:tr meters:bl", a.Layout);
    }

    [Fact]
    public void PlacingIntoAnOccupiedAnchorInsertsByRankAmongThoseThere()
    {
        var a = new DeskArrangement("meters:bl message:tl meetings:bl");
        a.Place("claude", DeskAnchor.BL);
        Assert.Equal(["claude", "meters", "meetings"], Widgets(a.Stack(DeskAnchor.BL)));
        a.Place("watchers", DeskAnchor.TL);
        Assert.Equal(["message", "watchers"], Widgets(a.Stack(DeskAnchor.TL)));
    }

    // Anchors

    [Fact]
    public void EachAnchorHasAColumnAndARow()
    {
        Assert.Equal(8, DeskAnchors.All.Count);
        Assert.Equal(
            new[] { "tl", "tc", "tr", "ml", "mr", "bl", "bc", "br" },
            DeskAnchors.All.Select(DeskAnchors.Word));
        Assert.Null(DeskAnchors.At(DeskColumn.Center, DeskRow.Middle));
        foreach (var anchor in DeskAnchors.All)
            Assert.Equal(anchor, DeskAnchors.At(DeskAnchors.Column(anchor), DeskAnchors.Row(anchor)));
    }

    [Fact]
    public void ParseIsExactLowercase()
    {
        foreach (var a in DeskAnchors.All)
            Assert.Equal(a, DeskAnchors.Parse(DeskAnchors.Word(a)));
        Assert.Null(DeskAnchors.Parse("BL"));
        Assert.Null(DeskAnchors.Parse("zz"));
        Assert.Null(DeskAnchors.Parse(""));
        Assert.Null(DeskAnchors.Parse(null));
    }

    [Fact]
    public void NamesMatchSettings()
    {
        Assert.Equal(
            new[] { "Top left", "Top center", "Top right", "Middle left", "Middle right", "Bottom left", "Bottom center", "Bottom right" },
            DeskAnchors.All.Select(DeskAnchors.Name));
    }

    [Fact]
    public void ColumnsAndRows()
    {
        Assert.Equal(DeskColumn.Left, DeskAnchors.Column(DeskAnchor.ML));
        Assert.Equal(DeskColumn.Center, DeskAnchors.Column(DeskAnchor.BC));
        Assert.Equal(DeskColumn.Right, DeskAnchors.Column(DeskAnchor.TR));
        Assert.Equal(DeskRow.Top, DeskAnchors.Row(DeskAnchor.TC));
        Assert.Equal(DeskRow.Middle, DeskAnchors.Row(DeskAnchor.MR));
        Assert.Equal(DeskRow.Bottom, DeskAnchors.Row(DeskAnchor.BL));
    }

    // Auto-place (the Launcher's first button)

    [Theory]
    [InlineData("", DeskAnchor.BL)]                                    // nothing placed: bottom left
    [InlineData("clock:bl claude:bl", DeskAnchor.BR)]                  // the standard layout fills bottom left
    [InlineData("clock:bl message:br", DeskAnchor.TL)]
    [InlineData("clock:bl message:br weather:tl", DeskAnchor.TR)]
    [InlineData("clock:bc claude:ml", DeskAnchor.BL)]                  // only corners count
    [InlineData("clock:br", DeskAnchor.BL)]
    [InlineData("clock:bl glow:br message:tl telemetry:tr", DeskAnchor.BL)]   // every corner taken: bottom left, stacked
    public void TheFirstFreeCornerIsBottomLeftBottomRightTopLeftTopRight(string layout, DeskAnchor expected) =>
        Assert.Equal(expected, new DeskArrangement(layout).FirstFreeAnchor());

    [Fact]
    public void AutoPlacingTheLauncherSeatsItAmongItsNeighbors()
    {
        var a = new DeskArrangement("clock:bl claude:bl");
        a.Place("launcher", a.FirstFreeAnchor());
        Assert.Equal(DeskAnchor.BR, a.Placement("launcher")!.Anchor);
        Assert.Equal("clock:bl claude:bl launcher:br", a.Layout);

        var full = new DeskArrangement("message:tl clock:bl telemetry:tr claude:br");
        full.Place("launcher", full.FirstFreeAnchor());
        Assert.Equal(["clock", "launcher"], full.Stack(DeskAnchor.BL).Select(p => p.Widget));
    }
}
