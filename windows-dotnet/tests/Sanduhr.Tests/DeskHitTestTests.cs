using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Which Desk element is under a point, what the Desk draws as click areas, and the clicks switch.</summary>
public class DeskHitTestTests
{
    private static readonly DeskElement Playing = new(DeskElementKind.NowPlaying, null, new DeskRect(50, 800, 360, 40));
    private static readonly DeskElement Credit = new(DeskElementKind.WeatherCredit, null, new DeskRect(50, 860, 120, 16));
    private static readonly DeskElement Clock = new(DeskElementKind.Clock, null, new DeskRect(52, 560, 300, 150));
    private static readonly DeskElement Line = new(DeskElementKind.ClaudeLine, null, new DeskRect(52, 720, 340, 24));
    private static readonly DeskElement Message = new(DeskElementKind.Message, null, new DeskRect(52, 60, 700, 100));

    private static readonly IReadOnlyList<DeskElement> All = [Playing, Credit, Clock, Line, Message];

    [Fact]
    public void PriorityIsLauncherNowPlayingCreditClockMessageLine()
    {
        Assert.Equal(
            [DeskElementKind.LauncherButton, DeskElementKind.NowPlaying, DeskElementKind.WeatherCredit, DeskElementKind.Clock, DeskElementKind.Message, DeskElementKind.ClaudeLine],
            DeskHitTest.Priority);
    }

    [Fact]
    public void AnEarlierKindWinsWhereTwoAreaOverlap()
    {
        var over = new DeskElement(DeskElementKind.NowPlaying, null, new DeskRect(60, 570, 100, 20));
        Assert.Equal(over, DeskHitTest.Element(80, 580, [Clock, over]));
        // The line sits below the message in priority: the message takes a point both reach.
        var lineOverMessage = new DeskElement(DeskElementKind.ClaudeLine, null, new DeskRect(60, 70, 100, 20));
        Assert.Equal(Message, DeskHitTest.Element(80, 80, [lineOverMessage, Message]));
    }

    [Fact]
    public void SlackCountsAroundEachKind()
    {
        Assert.Equal((8d, 4d), DeskHitTest.Slack(DeskElementKind.NowPlaying));
        Assert.Equal((8d, 4d), DeskHitTest.Slack(DeskElementKind.Clock));
        Assert.Equal((8d, 4d), DeskHitTest.Slack(DeskElementKind.Message));
        Assert.Equal((8d, 2d), DeskHitTest.Slack(DeskElementKind.ClaudeLine));
        Assert.Equal((4d, 2d), DeskHitTest.Slack(DeskElementKind.WeatherCredit));
        // The clock's frame top is 560: 4 above still counts, 5 does not.
        Assert.Equal(Clock, DeskHitTest.Element(100, 556.5, [Clock]));
        Assert.Null(DeskHitTest.Element(100, 555, [Clock]));
        // The line: 8 to the right of 392 counts, 9 does not.
        Assert.Equal(Line, DeskHitTest.Element(399, 730, [Line]));
        Assert.Null(DeskHitTest.Element(401, 730, [Line]));
        // The credit: 4 to the left of 50.
        Assert.Equal(Credit, DeskHitTest.Element(46.5, 865, [Credit]));
        Assert.Null(DeskHitTest.Element(45, 865, [Credit]));
    }

    [Fact]
    public void EachPieceIsHitInsideItsFrame()
    {
        Assert.Equal(Clock, DeskHitTest.Element(100, 600, All));
        Assert.Equal(Message, DeskHitTest.Element(100, 100, All));
        Assert.Equal(Line, DeskHitTest.Element(100, 730, All));
        Assert.Equal(Playing, DeskHitTest.Element(100, 820, All));
        Assert.Equal(Credit, DeskHitTest.Element(100, 868, All));
    }

    [Fact]
    public void EmptyFramesAndUnclickableElementsNeverTakeAClick()
    {
        var zero = Clock with { Frame = new DeskRect(0, 0, 0, 0) };
        Assert.Equal(0, DeskHitTest.Area(zero).Width);
        Assert.Null(DeskHitTest.Element(0, 0, [zero]));
        Assert.Null(DeskHitTest.Element(4, 3, [zero]));
        var flat = Clock with { Frame = new DeskRect(10, 10, 100, 0) };
        Assert.Null(DeskHitTest.Element(50, 10, [flat]));
        var quiet = Clock with { Clickable = false };
        Assert.Null(DeskHitTest.Element(100, 600, [quiet]));
    }

    [Fact]
    public void NothingUnderThePointerLetsTheMouseThrough()
    {
        Assert.Null(DeskHitTest.Element(900, 100, All));
        Assert.Null(DeskHitTest.Element(100, 600, []));
    }

    [Fact]
    public void AreaGrowsTheFrameBySlack()
    {
        Assert.Equal(new DeskRect(44, 556, 316, 158), DeskHitTest.Area(Clock));
    }

    [Fact]
    public void TheClockTheMessageAndTheClaudeLineOpenAMenuTheRestDoNot()
    {
        Assert.True(DeskHitTest.HasMenu(Clock));
        Assert.True(DeskHitTest.HasMenu(Message));
        Assert.True(DeskHitTest.HasMenu(Line));
        Assert.False(DeskHitTest.HasMenu(Playing));
        Assert.False(DeskHitTest.HasMenu(Credit));
        Assert.False(DeskHitTest.HasMenu(null));
    }

    // Build

    private static DeskElementsInput Input(bool on, string layout = "message:tl clock:bl claude:bl") =>
        new(new HashSet<string>(new DeskArrangement(layout).Pieces.Select(p => p.Widget)),
            PiecesTakeClicks: on,
            ClockFrame: Clock.Frame,
            MessageDrawn: true, MessageFrame: Message.Frame,
            ClaudeLineDrawn: true, ClaudeLineFrame: Line.Frame);

    [Fact]
    public void OnTheThreePiecesAreClickAreas()
    {
        var out_ = DeskElements.Build(Input(true));
        Assert.Equal([DeskElementKind.Clock, DeskElementKind.Message, DeskElementKind.ClaudeLine], out_.Select(e => e.Kind));
        Assert.Equal(Clock.Frame, out_.First(e => e.Kind == DeskElementKind.Clock).Frame);
        Assert.All(out_, e => Assert.True(e.Clickable));
        Assert.Equal(DeskElementKind.Clock, DeskHitTest.Element(100, 600, out_)?.Kind);
        Assert.Equal(DeskElementKind.Message, DeskHitTest.Element(100, 100, out_)?.Kind);
        Assert.Equal(DeskElementKind.ClaudeLine, DeskHitTest.Element(100, 730, out_)?.Kind);
    }

    [Fact]
    public void OffTheyLetEveryClickThrough()
    {
        var out_ = DeskElements.Build(Input(false));
        Assert.Empty(out_);
        Assert.Null(DeskHitTest.Element(100, 600, out_));
        Assert.Null(DeskHitTest.Element(100, 100, out_));
        Assert.Null(DeskHitTest.Element(100, 730, out_));
    }

    [Fact]
    public void NowPlayingAndTheCreditTakeClicksWhateverTheSwitchSays()
    {
        var input = Input(false) with
        {
            NowPlayingDrawn = true, NowPlayingFrame = Playing.Frame,
            WeatherCreditDrawn = true, WeatherCreditFrame = Credit.Frame,
        };
        var out_ = DeskElements.Build(input);
        Assert.Equal([DeskElementKind.NowPlaying, DeskElementKind.WeatherCredit], out_.Select(e => e.Kind));
        Assert.Equal(Playing.Frame, out_[0].Frame);
        Assert.Equal(Credit.Frame, out_[1].Frame);
        var on = DeskElements.Build(input with { PiecesTakeClicks = true });
        Assert.Equal(5, on.Count);
    }

    [Fact]
    public void NowPlayingAndTheCreditAreListedOnlyWhenDrawn()
    {
        Assert.Empty(DeskElements.Build(Input(false) with { NowPlayingFrame = Playing.Frame, WeatherCreditFrame = Credit.Frame }));
    }

    [Fact]
    public void OnlyWhatIsPlacedAndDrawn()
    {
        Assert.Empty(DeskElements.Build(Input(true, "meters:bl")));
        var nothing = Input(true) with { MessageDrawn = false, ClaudeLineDrawn = false };
        Assert.Equal([DeskElementKind.Clock], DeskElements.Build(nothing).Select(e => e.Kind));
        // The message placed but the line's widget not: the line has nowhere to be.
        var noClaude = Input(true, "message:tl clock:bl");
        Assert.Equal([DeskElementKind.Clock, DeskElementKind.Message], DeskElements.Build(noClaude).Select(e => e.Kind));
    }

    [Fact]
    public void ADrawnPieceWhoseFrameNeverArrivedIsListedButNeverHit()
    {
        var input = Input(true) with { ClockFrame = default };
        var out_ = DeskElements.Build(input);
        Assert.Contains(out_, e => e.Kind == DeskElementKind.Clock);
        Assert.Null(DeskHitTest.Element(0, 0, out_.Where(e => e.Kind == DeskElementKind.Clock).ToList()));
    }

    // The switch

    [Fact]
    public void TheSwitchIsOnByDefaultWithItsWords()
    {
        Assert.Equal("desk_pieces_take_clicks", DeskPieceClicks.Key);
        Assert.True(DeskPieceClicks.DefaultOn);
        Assert.Equal("Clock and message take clicks", DeskPieceClicks.Title);
        Assert.Equal("On: right-click them for Sanduhr's menu. Desktop icons right beneath them can't be clicked there while it's on.", DeskPieceClicks.Caption);
    }

    [Fact]
    public void MenuItems()
    {
        Assert.Equal("Edit messages…", DeskPieceClicks.MenuItem(DeskElementKind.Message)?.Title);
        Assert.Equal(DeskElementKind.Message, DeskPieceClicks.MenuItem(DeskElementKind.Message)?.Page);
        Assert.Equal("Desk settings…", DeskPieceClicks.MenuItem(DeskElementKind.Clock)?.Title);
        Assert.Null(DeskPieceClicks.MenuItem(DeskElementKind.ClaudeLine));
        Assert.Null(DeskPieceClicks.MenuItem(DeskElementKind.NowPlaying));
        Assert.Null(DeskPieceClicks.MenuItem(DeskElementKind.WeatherCredit));
    }
}
