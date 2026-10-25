using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Arrange mode: which anchor a drop lands on, reordering within a stack, the handle's size steps, the edit.</summary>
public class DeskArrangeTests
{
    // A 1512 x 982 screen less 52-point sides, 64 on top and 60 at the bottom.
    private static readonly DeskRect Content = new(52, 64, 1408, 858);

    private const string Start = "message:tl clock:bl meters:bl meetings:bl";

    private static readonly Dictionary<string, DeskRect> Tall = new()
    {
        ["message"] = new DeskRect(52, 64, 400, 60),
        ["clock"] = new DeskRect(52, 560, 300, 140),
        ["meters"] = new DeskRect(52, 720, 300, 80),
        ["meetings"] = new DeskRect(52, 820, 300, 80),
    };

    private static string[] Widgets(IEnumerable<DeskPlacement> ps) => ps.Select(p => p.Widget).ToArray();

    // Drag to an anchor

    [Fact]
    public void EachAnchorsOwnPointResolvesToIt()
    {
        foreach (var anchor in DeskAnchors.All)
        {
            var (x, y) = DeskGeometry.Point(anchor, Content);
            Assert.Equal(anchor, DeskArrange.Nearest(x, y, Content));
        }
    }

    [Theory]
    [InlineData(140, 120, DeskAnchor.TL)]
    [InlineData(1400, 900, DeskAnchor.BR)]
    [InlineData(760, 90, DeskAnchor.TC)]
    [InlineData(700, 880, DeskAnchor.BC)]
    [InlineData(90, 500, DeskAnchor.ML)]
    [InlineData(1450, 470, DeskAnchor.MR)]
    [InlineData(1300, 140, DeskAnchor.TR)]
    [InlineData(200, 850, DeskAnchor.BL)]
    public void ADropNearACornerAnEdgeMiddleOrACenterLandsThere(double x, double y, DeskAnchor expected)
    {
        Assert.Equal(expected, DeskArrange.Nearest(x, y, Content));
    }

    [Fact]
    public void ZonesAreSharesOfTheScreen()
    {
        // A third of the way across at mid height is nearer, as shares, the left middle than the top center.
        Assert.Equal(DeskAnchor.ML, DeskArrange.Nearest(Content.X + Content.Width * 0.3, Content.MidY, Content));
    }

    [Fact]
    public void AnEmptyContentRectangleNeverDividesByZero()
    {
        var a = DeskArrange.Nearest(0, 0, new DeskRect(0, 0, 0, 0));
        Assert.Equal(DeskAnchor.TL, a);
    }

    // Reorder and move

    [Fact]
    public void PieceAfterIsTheFirstDrawnPieceWhoseMiddleIsBelowThePointer()
    {
        var stack = new List<(string Widget, DeskRect Frame)>
        {
            ("clock", new DeskRect(0, 600, 300, 140)),
            ("meters", new DeskRect(0, 760, 300, 80)),
            ("watchers", new DeskRect(0, 0, 0, 0)),
            ("meetings", new DeskRect(0, 860, 300, 40)),
        };
        Assert.Equal("clock", DeskArrange.PieceAfter(500, stack));
        Assert.Equal("meters", DeskArrange.PieceAfter(700, stack));
        Assert.Equal("meetings", DeskArrange.PieceAfter(860, stack));
        Assert.Null(DeskArrange.PieceAfter(950, stack));
        Assert.Null(DeskArrange.PieceAfter(0, []));
    }

    [Fact]
    public void StackBoundsAreTheUnionOfDrawnPiecesGrownByTheReach()
    {
        var a = new DeskArrangement("message:tl clock:bl meters:bl watchers:bl meetings:bl");
        var bounds = DeskArrange.StackBounds(a, Tall, 10);
        Assert.Equal(new DeskRect(42, 550, 320, 360), bounds[DeskAnchor.BL]);
        Assert.Equal(new DeskRect(42, 54, 420, 80), bounds[DeskAnchor.TL]);
        Assert.False(bounds.ContainsKey(DeskAnchor.MR));
    }

    [Fact]
    public void StackBoundsUseTheDefaultReach()
    {
        var a = new DeskArrangement("message:tl");
        var b = DeskArrange.StackBounds(a, Tall)[DeskAnchor.TL];
        Assert.Equal(new DeskRect(52 - 26, 64 - 26, 400 + 52, 60 + 52), b);
    }

    [Fact]
    public void ADropOnTheTopOfATallStackStaysInItAndReorders()
    {
        var a = new DeskArrangement(Start);
        var stacks = DeskArrange.StackBounds(a, Tall);
        Assert.Equal(DeskAnchor.ML, DeskArrange.Nearest(150, 570, Content));
        var anchor = DeskArrange.Target(150, 570, Content, stacks, DeskAnchor.BL);
        Assert.Equal(DeskAnchor.BL, anchor);
        var stack = a.Stack(anchor).Where(p => p.Widget != "meetings")
            .Select(p => (p.Widget, Tall.GetValueOrDefault(p.Widget))).ToList();
        a.Put("meetings", anchor, DeskArrange.PieceAfter(570, stack));
        Assert.Equal("message:tl meetings:bl clock:bl meters:bl", a.Layout);
        // Just above the clock's outline and name label still counts as the stack.
        Assert.Equal(DeskAnchor.BL, DeskArrange.Target(150, 540, Content, stacks, DeskAnchor.BL));
    }

    [Fact]
    public void OffEveryStackTheNearestAnchorWinsOverAnotherStackThatStack()
    {
        var stacks = DeskArrange.StackBounds(new DeskArrangement(Start), Tall);
        Assert.Equal(DeskAnchor.ML, DeskArrange.Target(150, 480, Content, stacks, DeskAnchor.BL));
        Assert.Equal(DeskAnchor.BR, DeskArrange.Target(1400, 900, Content, stacks, DeskAnchor.BL));
        Assert.Equal(DeskAnchor.TL, DeskArrange.Target(300, 90, Content, stacks, DeskAnchor.BL));
        Assert.Equal(DeskAnchor.ML, DeskArrange.Target(150, 570, Content, new Dictionary<DeskAnchor, DeskRect>(), DeskAnchor.BL));
    }

    [Fact]
    public void WhereTwoStacksOverlapTheDraggedPiecesOwnStackWins()
    {
        var stacks = new Dictionary<DeskAnchor, DeskRect>
        {
            [DeskAnchor.ML] = new DeskRect(40, 400, 300, 300),
            [DeskAnchor.BL] = new DeskRect(40, 600, 300, 330),
        };
        Assert.Equal(DeskAnchor.BL, DeskArrange.Target(100, 650, Content, stacks, DeskAnchor.BL));
        Assert.Equal(DeskAnchor.ML, DeskArrange.Target(100, 650, Content, stacks, DeskAnchor.ML));
        Assert.Equal(DeskAnchor.ML, DeskArrange.Target(100, 650, Content, stacks, DeskAnchor.TR));
        Assert.Equal(DeskAnchor.ML, DeskArrange.Target(100, 650, Content, stacks, null));
    }

    [Fact]
    public void PutWithinAStackToTheTopToTheBottomBetween()
    {
        var a = new DeskArrangement(Start);
        a.Put("meetings", DeskAnchor.BL, "clock");
        Assert.Equal("message:tl meetings:bl clock:bl meters:bl", a.Layout);
        a.Put("meetings", DeskAnchor.BL, null);
        Assert.Equal("message:tl clock:bl meters:bl meetings:bl", a.Layout);
        a.Put("clock", DeskAnchor.BL, "meetings");
        Assert.Equal("message:tl meters:bl clock:bl meetings:bl", a.Layout);
        Assert.Equal(["meters", "clock", "meetings"], Widgets(a.Stack(DeskAnchor.BL)));
    }

    [Fact]
    public void PutToAnotherAnchorIntoAStackAtAPlaceIntoAnEmptyOneKeepingItsPlace()
    {
        var a = new DeskArrangement(Start);
        a.Put("meters", DeskAnchor.TL, "message");
        Assert.Equal("meters:tl message:tl clock:bl meetings:bl", a.Layout);
        var b = new DeskArrangement(Start);
        b.Put("clock", DeskAnchor.TR, null);
        Assert.Equal("message:tl clock:tr meters:bl meetings:bl", b.Layout);
        var c = new DeskArrangement(Start);
        c.Put("meetings", DeskAnchor.TL, null);
        Assert.Equal(["message", "meetings"], Widgets(c.Stack(DeskAnchor.TL)));
    }

    [Fact]
    public void PutKeepsTheSizeAndIgnoresAnUnplacedPieceOrItselfAsTheTarget()
    {
        var a = new DeskArrangement("message:tl clock:bl:1.3 meters:bl");
        a.Put("clock", DeskAnchor.MR, null);
        Assert.Equal(new DeskPlacement("clock", DeskAnchor.MR, 1.3), a.Placement("clock"));
        var before = a.Layout;
        a.Put("watchers", DeskAnchor.TL, null);
        a.Put("meters", DeskAnchor.BL, "meters");
        Assert.Equal(before, a.Layout);
    }

    [Fact]
    public void PutBeforeAPieceAtAnotherAnchorFallsToTheBottomOfTheStack()
    {
        var a = new DeskArrangement("message:tl clock:bl meters:bl");
        a.Put("message", DeskAnchor.BL, "nowhere");
        Assert.Equal("clock:bl meters:bl message:bl", a.Layout);
    }

    // The glow's Desk spot

    [Fact]
    public void TheGlowWordRoundTripsAndDefaultsToTopCenter()
    {
        Assert.Contains("glow", DeskArrangement.Known);
        Assert.Equal("glow", DeskArrangement.Known[^2]);
        Assert.Equal(DeskAnchor.TC, DeskArrangement.DefaultAnchor("glow"));
        Assert.Equal("clock:bl glow:tc", new DeskArrangement("clock:bl glow:tc").Layout);
        Assert.Equal("glow:br:1.2", new DeskArrangement("glow:br:1.2").Layout);
        Assert.Equal(DeskAnchor.TC, new DeskArrangement("glow:zz").Placement("glow")?.Anchor);
    }

    [Fact]
    public void TheCompanionWordIsLastRoundTripsAndDefaultsToBottomRight()
    {
        Assert.Equal("companion", DeskArrangement.Known[^1]);
        Assert.Equal(DeskAnchor.BR, DeskArrangement.DefaultAnchor("companion"));
        Assert.Equal("clock:bl companion:bc:1.4", new DeskArrangement("clock:bl companion:bc:1.4").Layout);
        Assert.Equal(DeskAnchor.BR, new DeskArrangement("companion:zz").Placement("companion")?.Anchor);
        Assert.Null(new DeskArrangement(DeskArrangement.Standard).Placement("companion"));
        Assert.Equal("Companion", SettingsNames.DeskPieceName("companion"));
    }

    [Fact]
    public void StandardIsUnchangedByTheGlow()
    {
        Assert.Equal("clock:bl claude:bl", DeskArrangement.Standard);
        Assert.Null(new DeskArrangement(DeskArrangement.Standard).Placement("glow"));
    }

    [Fact]
    public void AnOlderBuildStringWithoutTheGlowIsWrittenBackUnchanged()
    {
        const string layout = "message:tl clock:bl meters:bl meetings:bl";
        Assert.Equal(layout, new DeskArrangement(layout).Layout);
    }

    // The resize handle

    [Fact]
    public void TheHandleSitsOnTheCornerFacingTheMiddleOfTheScreen()
    {
        Assert.Equal((true, true), DeskArrange.Handle(DeskAnchor.TL));
        Assert.Equal((false, true), DeskArrange.Handle(DeskAnchor.TR));
        Assert.Equal((true, false), DeskArrange.Handle(DeskAnchor.BL));
        Assert.Equal((false, false), DeskArrange.Handle(DeskAnchor.BR));
        Assert.Equal((true, true), DeskArrange.Handle(DeskAnchor.ML));
        Assert.Equal((true, false), DeskArrange.Handle(DeskAnchor.BC));
    }

    [Fact]
    public void DraggingOutwardGrowsInwardShrinksSnappedToTheTenPercentSteps()
    {
        // 300 + 100 = 400 of span: 40 outward is 10%.
        Assert.Equal(1.1, DeskArrange.Scale(1, 300, 100, 40, 0, DeskAnchor.TL));
        Assert.Equal(1.1, DeskArrange.Scale(1, 300, 100, 20, 20, DeskAnchor.TL));
        Assert.Equal(0.8, DeskArrange.Scale(1, 300, 100, -80, 0, DeskAnchor.TL));
        // A small drag stays on the step it started on.
        Assert.Equal(1, DeskArrange.Scale(1, 300, 100, 10, 5, DeskAnchor.TL));
        // Bottom right grows up and to the left.
        Assert.Equal(1.2, DeskArrange.Scale(1, 300, 100, -40, -40, DeskAnchor.BR));
        Assert.Equal(0.8, DeskArrange.Scale(1, 300, 100, 40, 40, DeskAnchor.BR));
        // A center piece grows both ways across: its sideways drag counts twice.
        Assert.Equal(1.1, DeskArrange.Scale(1, 300, 100, 20, 0, DeskAnchor.TC));
    }

    [Fact]
    public void EveryScaleResultIsOneOfTheSizeStepsWithin60To160Percent()
    {
        Assert.Equal(1.6, DeskArrange.Scale(1, 300, 100, 5000, 5000, DeskAnchor.TL));
        Assert.Equal(0.6, DeskArrange.Scale(1, 300, 100, -5000, 0, DeskAnchor.TL));
        Assert.Equal(1.4, DeskArrange.Scale(1.4, 0, 0, 50, 0, DeskAnchor.TL));
        for (var dx = -400.0; dx <= 400.0; dx += 7.0)
            Assert.Contains(DeskArrange.Scale(1.2, 300, 100, dx, dx / 3, DeskAnchor.ML), DeskArrangement.ScaleSteps);
    }

    // Clicks, keys, copy, bar

    [Fact]
    public void ArrangingTakesTheWholeFrameAfterOnlyTheDrawnClickAreas()
    {
        Assert.True(DeskArrange.TakesMouse(true, false));
        Assert.True(DeskArrange.TakesMouse(true, true));
        Assert.False(DeskArrange.TakesMouse(false, false));
        Assert.True(DeskArrange.TakesMouse(false, true));
    }

    [Fact]
    public void ReturnAndEnterAreDoneEscapeIsCancelOtherKeysDoNothing()
    {
        Assert.Equal(DeskArrangeEnd.Done, DeskArrange.EndKey("Return"));
        Assert.Equal(DeskArrangeEnd.Done, DeskArrange.EndKey("Enter"));
        Assert.Equal(DeskArrangeEnd.Cancel, DeskArrange.EndKey("Escape"));
        Assert.Null(DeskArrange.EndKey("Space"));
        Assert.Null(DeskArrange.EndKey("A"));
        Assert.Null(DeskArrange.EndKey(null));
    }

    [Fact]
    public void TheBarIsNeverSmallerThanItsMinimum()
    {
        Assert.Equal((340d, 170d), DeskArrange.BarSize(0, 0));
        Assert.Equal((340d, 170d), DeskArrange.BarSize(double.NaN, double.PositiveInfinity));
        Assert.Equal((420d, 220d), DeskArrange.BarSize(420, 220));
        Assert.Equal(340, DeskArrange.BarMinimumWidth);
        Assert.Equal(170, DeskArrange.BarMinimumHeight);
    }

    [Fact]
    public void TheCopySaysReturnIsDoneAndEscapeIsCancel()
    {
        Assert.Equal("Arrange Desk…", DeskArrangeCopy.MenuItem);
        Assert.Equal("Arrange Desk…", DeskArrangeCopy.SettingsButton);
        Assert.Equal("Tab picks a piece; the arrow keys move it to the next place, and Shift+Up or Shift+Down moves it in its stack. "
            + "Return or Done keeps the new layout. Escape or Cancel puts it back.", DeskArrangeCopy.BarKeys);
        Assert.Contains("Return or Done keeps the new layout; Escape or Cancel puts it back", DeskArrangeCopy.SettingsNote);
        Assert.EndsWith("Or use the Order buttons below.", DeskArrangeCopy.SettingsNote);
        Assert.DoesNotContain("Escape or Done", DeskArrangeCopy.BarKeys);
        Assert.DoesNotContain("handle", DeskArrangeCopy.BarHint);
        Assert.Equal("Drag a piece to any of the eight places, or up and down its stack, or onto another monitor.", DeskArrangeCopy.BarHint);
        Assert.Equal("Turn on the Desk to arrange it on the desktop.", DeskArrangeCopy.SettingsDeskOff);
        Assert.Equal("Finish arranging", DeskArrangeCopy.TrayFinish);
        Assert.Equal("Arrange Desk", DeskArrangeCopy.BarTitle);
    }

    // The edit

    [Fact]
    public void DoneWritesOnceAndOnlyTheFinalString()
    {
        var s = new DeskArrangeSession(Start);
        s.Working.Put("meetings", DeskAnchor.BL, "clock");
        s.Working.SetScale("clock", 1.2);
        s.Working.Put("meters", DeskAnchor.MR, null);
        Assert.Equal(Start, s.Saved);
        Assert.True(s.Changed);
        Assert.Equal("message:tl meetings:bl clock:bl:1.2 meters:mr", s.ToWrite);
    }

    [Fact]
    public void CancelLeavesTheSavedStringExactlyAsItWasAnOddOneIncluded()
    {
        const string saved = "message:tl clock:zz claude:bl meetings:bl";
        var s = new DeskArrangeSession(saved);
        s.Working.Put("clock", DeskAnchor.TR, null);
        s.Working.SetScale("message", 1.5);
        Assert.NotEqual(saved, s.Working.Layout);
        Assert.Equal(saved, s.Saved);
    }

    [Fact]
    public void NothingChangedWritesNothingSoAnOlderStringStaysAsItWas()
    {
        const string saved = "message:tl clock:zz claude:bl meetings:bl";
        var s = new DeskArrangeSession(saved);
        // Moved away and back: the same layout.
        s.Working.Put("meetings", DeskAnchor.TL, null);
        s.Working.Put("meetings", DeskAnchor.BL, null);
        Assert.False(s.Changed);
        Assert.Null(s.ToWrite);
        Assert.Equal(saved, s.Saved);
    }

    [Fact]
    public void AnUntouchedOddStringIsNotChanged()
    {
        var s = new DeskArrangeSession("clock:zz  claude:bl");
        Assert.False(s.Changed);
        Assert.Null(s.ToWrite);
    }

    // Hide and restore while arranging

    [Fact]
    public void HideTakesAPieceOffAndTheHiddenListNamesItInSettingsOrder()
    {
        var a = new DeskArrangement("message:tl clock:bl:1.2 claude:bl");
        a.Hide("clock");
        Assert.Null(a.Placement("clock"));
        Assert.Equal("message:tl claude:bl", a.Layout);
        Assert.Equal(["clock", "meters", "nowPlaying", "telemetry", "weather", "watchers", "launcher", "meetings", "glow", "companion"], a.Hidden());
        // Limited to what the Desk draws: no meetings.
        Assert.Equal(["clock", "meters"], a.Hidden(["clock", "claude", "meters"]));
    }

    [Fact]
    public void ShowSeatsAHiddenPieceAtItsStandardAnchorAtSizeOne()
    {
        var a = new DeskArrangement("message:tl clock:bl:1.2 claude:bl");
        a.Hide("clock");
        a.Show("clock");
        Assert.Equal(DeskAnchor.BL, a.Placement("clock")!.Anchor);
        Assert.Equal(1, a.Placement("clock")!.Scale);
        a.Hide("message");
        a.Show("message");
        Assert.Equal(DeskAnchor.BL, a.Placement("message")!.Anchor);   // DefaultPlaceFor (slice 3)
        a.Show("weather");
        Assert.Equal(DeskAnchor.TR, a.Placement("weather")!.Anchor);
    }

    [Fact]
    public void ShowLeavesAPlacedPieceAndAnUnknownWordAlone()
    {
        var a = new DeskArrangement("clock:tr:1.3 claude:bl");
        a.Show("clock");
        a.Show("nope");
        Assert.Equal("clock:tr:1.3 claude:bl", a.Layout);
        a.Hide("nope");
        Assert.Equal("clock:tr:1.3 claude:bl", a.Layout);
    }

    [Fact]
    public void DoneKeepsAHiddenPieceHiddenAndWritesOnce()
    {
        var s = new DeskArrangeSession(Start);
        var piece = s.Working.Pieces[0].Widget;
        s.Working.Hide(piece);
        Assert.True(s.Changed);
        Assert.Equal(s.Working.Layout, s.ToWrite);
        Assert.DoesNotContain(piece + ":", s.ToWrite);
        Assert.Equal(Start, s.Saved);
        Assert.Contains(piece, new DeskArrangement(s.ToWrite).Hidden());
    }

    [Fact]
    public void CancelBringsHiddenPiecesBackBecauseTheSavedStringIsUntouched()
    {
        var s = new DeskArrangeSession(Start);
        foreach (var p in s.Working.Pieces.Select(p => p.Widget).ToList()) s.Working.Hide(p);
        Assert.Equal("", s.Working.Layout);
        Assert.Equal(Start, s.Saved);
        // The next edit starts from what was saved: everything is back.
        Assert.Equal(new DeskArrangement(Start).Layout, new DeskArrangeSession(s.Saved).Working.Layout);
    }

    [Fact]
    public void DoneWaitsWhileEveryDrawnPieceIsHiddenAndWritesOnceOneIsShownBack()
    {
        var s = new DeskArrangeSession(Start);
        Assert.True(s.CanFinish(DeskView_Drawn));
        foreach (var p in s.Working.Pieces.Select(p => p.Widget).ToList()) s.Working.Hide(p);
        Assert.False(s.CanFinish(DeskView_Drawn));
        Assert.Equal(Start, s.Saved); // nothing was written
        s.Working.Show("clock");
        Assert.True(s.CanFinish(DeskView_Drawn));
        Assert.Equal("clock:bl", s.ToWrite);
        Assert.Equal("Show a piece first, or Cancel.", DeskArrangeCopy.NothingPlaced);
    }

    private static readonly string[] DeskView_Drawn = ["message", "clock", "claude", "meters", "nowPlaying", "telemetry", "weather", "watchers", "launcher", "glow", "companion"];

    [Fact]
    public void HidingThenShowingAPieceIsNotAChangeWhenItWasAtItsStandardAnchor()
    {
        var s = new DeskArrangeSession("clock:bl claude:bl");
        s.Working.Hide("clock");
        s.Working.Show("clock");
        Assert.False(s.Changed);
        Assert.Null(s.ToWrite);
    }

    [Fact]
    public void ArrangeCopyNamesTheControlsForAScreenReader()
    {
        Assert.Equal("Hidden:", DeskArrangeCopy.HiddenStrip);
        Assert.Equal("Hide while arranging: Clock", DeskArrangeCopy.HideLabel("Clock"));
        Assert.Equal("Show Clock", DeskArrangeCopy.ShowLabel("Clock"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoSavedLayoutEditsTheStandardOne(string? saved)
    {
        var s = new DeskArrangeSession(saved);
        Assert.Equal(DeskArrangement.Standard, s.Saved);
        Assert.Equal(DeskArrangement.Standard, s.Working.Layout);
        Assert.False(s.Changed);
        Assert.Null(s.ToWrite);
        s.Working.Put("clock", DeskAnchor.TR, null);
        Assert.Equal("clock:tr claude:bl", s.ToWrite);
    }

    // ---- Settings v2, slice 3: the arrow keys (spec, Keyboard and screen reader) ----

    [Theory]
    [InlineData("Left", false, DeskArrangeArrowKind.Place, -1, 0)]
    [InlineData("Right", false, DeskArrangeArrowKind.Place, 1, 0)]
    [InlineData("Up", false, DeskArrangeArrowKind.Place, 0, -1)]
    [InlineData("Down", false, DeskArrangeArrowKind.Place, 0, 1)]
    [InlineData("Up", true, DeskArrangeArrowKind.Order, 0, -1)]
    [InlineData("Down", true, DeskArrangeArrowKind.Order, 0, 1)]
    public void Arrows_move_a_piece_and_Shift_arrows_reorder_it(string key, bool shift, DeskArrangeArrowKind kind, int dx, int dy) =>
        Assert.Equal(new DeskArrangeArrow(kind, dx, dy), DeskArrange.ArrowKey(key, shift));

    [Theory]
    [InlineData("Left", true)]
    [InlineData("Right", true)]
    [InlineData("Tab", false)]
    [InlineData("Return", false)]
    [InlineData(null, false)]
    public void Other_keys_are_not_arrows(string? key, bool shift) => Assert.Null(DeskArrange.ArrowKey(key, shift));

    [Theory]
    [InlineData(DeskAnchor.TL, 1, 0, DeskAnchor.TC)]
    [InlineData(DeskAnchor.TC, 1, 0, DeskAnchor.TR)]
    [InlineData(DeskAnchor.TC, 0, 1, DeskAnchor.BC)]   // the open middle of the screen is stepped over
    [InlineData(DeskAnchor.ML, 1, 0, DeskAnchor.MR)]
    [InlineData(DeskAnchor.TL, 0, 1, DeskAnchor.ML)]
    [InlineData(DeskAnchor.BR, -1, 0, DeskAnchor.BC)]
    public void Step_goes_to_the_next_place_that_way(DeskAnchor from, int dx, int dy, DeskAnchor to) =>
        Assert.Equal(to, DeskArrange.Step(from, dx, dy));

    [Theory]
    [InlineData(DeskAnchor.TL, -1, 0)]
    [InlineData(DeskAnchor.TL, 0, -1)]
    [InlineData(DeskAnchor.BR, 1, 0)]
    [InlineData(DeskAnchor.BR, 0, 1)]
    public void Step_stops_at_the_edge(DeskAnchor from, int dx, int dy) => Assert.Null(DeskArrange.Step(from, dx, dy));
}
