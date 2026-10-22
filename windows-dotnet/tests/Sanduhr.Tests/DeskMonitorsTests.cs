using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Desk pieces per monitor (spec, Desk pieces per monitor): the layout string's optional @monitor suffix, its escaping,
/// older layouts reading and writing unchanged, the stacks of one screen, and a missing monitor falling back to the main one
/// without touching what is stored.</summary>
public sealed class DeskMonitorsTests
{
    private const string Device = @"\\?\DISPLAY#GSM5B09#5&2c9d3a1&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string Other = @"\\?\DISPLAY#DEL4167#5&2c9d3a1&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    private static readonly DeskScreenRef MainScreen = new(Device, true);
    private static readonly DeskScreenRef Second = new(Other, false);
    private static readonly string[] Both = [Device, Other];

    // ---- older layouts are untouched ----

    [Theory]
    [InlineData("clock:bl claude:bl")]
    [InlineData("message:tl:1.2 clock:tr claude:bl:0.8 launcher:br:1.6")]
    [InlineData("clock:bc")]
    public void AnOlderLayoutReadsAsEveryMonitorAndWritesBackExactlyAsItWas(string layout)
    {
        var a = new DeskArrangement(layout);
        Assert.All(a.Pieces, p => Assert.Null(p.Monitor));
        Assert.Equal(layout, a.Layout);
        Assert.DoesNotContain("@", a.Layout);
    }

    [Fact]
    public void ThePlacementConstructorStillTakesThreeArguments()
    {
        var p = new DeskPlacement("clock", DeskAnchor.TL, 1.2);
        Assert.Null(p.Monitor);
    }

    // ---- the suffix ----

    [Fact]
    public void TheMainMonitorIsAWordAfterTheAtSign()
    {
        var a = new DeskArrangement("clock:tl@main claude:bl");
        Assert.Equal("main", a.Placement("clock")!.Monitor);
        Assert.Null(a.Placement("claude")!.Monitor);
        Assert.Equal("clock:tl@main claude:bl", a.Layout);
    }

    [Fact]
    public void TheSuffixFollowsTheSize()
    {
        var a = new DeskArrangement("launcher:br:1.4@main");
        var p = a.Placement("launcher")!;
        Assert.Equal((DeskAnchor.BR, 1.4, "main"), (p.Anchor, p.Scale, p.Monitor));
        Assert.Equal("launcher:br:1.4@main", a.Layout);
    }

    [Fact]
    public void ADevicePathIsEscapedSoNothingInItCanBreakTheList()
    {
        var a = new DeskArrangement([new DeskPlacement("launcher", DeskAnchor.BR, 1, Device), new DeskPlacement("clock", DeskAnchor.TL)]);
        string layout = a.Layout;
        Assert.DoesNotContain(" claude", layout);
        // The id holds a backslash, a hash, braces and ampersands: all literal; and no colon, comma, space or at sign leaks out of it.
        string idPart = layout.Split(' ')[0].Split('@')[1];
        Assert.DoesNotContain(":", idPart);
        Assert.DoesNotContain(",", idPart);
        Assert.DoesNotContain(" ", idPart);
        var back = new DeskArrangement(layout);
        Assert.Equal(Device, back.Placement("launcher")!.Monitor);
        Assert.Null(back.Placement("clock")!.Monitor);
        Assert.Equal(layout, back.Layout);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("has space")]
    [InlineData("a,b:c@d%e")]
    [InlineData("100%")]
    [InlineData("%41")]
    [InlineData("tab\there")]
    [InlineData("\u00e9cran \u4e2d\u6587 \U0001F600")]
    [InlineData(@"\\?\DISPLAY#X#{guid}")]
    public void EscapeAndUnescapeRoundTripWhateverTheId(string id)
    {
        string escaped = DeskMonitors.Escape(id);
        Assert.DoesNotContain(" ", escaped);
        Assert.DoesNotContain(",", escaped);
        Assert.DoesNotContain(":", escaped);
        Assert.DoesNotContain("@", escaped);
        Assert.Equal(id, DeskMonitors.Unescape(escaped));
        var a = new DeskArrangement([new DeskPlacement("clock", DeskAnchor.TL, 1, id)]);
        Assert.Equal(id, new DeskArrangement(a.Layout).Placement("clock")!.Monitor);
    }

    [Theory]
    [InlineData("clock:tl@")]
    [InlineData("clock:tl@all")]
    [InlineData("clock:tl@ALL")]
    public void AnEmptyOrAllSuffixIsEveryMonitorAndIsWrittenWithoutIt(string word)
    {
        var a = new DeskArrangement(word);
        Assert.Null(a.Placement("clock")!.Monitor);
        Assert.Equal("clock:tl", a.Layout);
    }

    [Fact]
    public void ABrokenEscapeIsKeptAsItIs()
    {
        Assert.Equal("100%", DeskMonitors.Unescape("100%"));
        Assert.Equal("a%zzb", DeskMonitors.Unescape("a%zzb"));
        Assert.Equal("a%4", DeskMonitors.Unescape("a%4"));
    }

    [Fact]
    public void AnUnknownPieceOrOddWordWithASuffixIsStillDropped()
    {
        Assert.Equal("clock:bl", new DeskArrangement("nope:tl@main clock:bl").Layout);
        Assert.Equal("", new DeskArrangement("@main").Layout);
    }

    // ---- editing keeps the monitor ----

    [Fact]
    public void SetMonitorPutsAPlacedPieceOnAMonitorAndKeepsItsAnchorSizeAndPlace()
    {
        var a = new DeskArrangement("clock:tl:1.4 claude:tl launcher:br");
        a.SetMonitor("claude", "main");
        a.SetMonitor("launcher", Other);
        Assert.Equal("clock:tl:1.4 claude:tl@main launcher:br@" + DeskMonitors.Escape(Other), a.Layout);
        a.SetMonitor("launcher", null);
        a.SetMonitor("nope", "main");
        a.SetMonitor("meters", "main");   // not placed: nothing happens
        Assert.Equal("clock:tl:1.4 claude:tl@main launcher:br", a.Layout);
        a.SetMonitor("claude", "all");
        Assert.Equal("clock:tl:1.4 claude:tl launcher:br", a.Layout);
    }

    [Fact]
    public void MovingASizingAndReorderingKeepTheMonitor()
    {
        var a = new DeskArrangement("clock:tl@main claude:tl launcher:br@main");
        a.Place("clock", DeskAnchor.TR);
        a.SetScale("clock", 1.3);
        a.Put("launcher", DeskAnchor.TR, "clock");
        a.Move("claude", "clock");
        Assert.Equal("main", a.Placement("clock")!.Monitor);
        Assert.Equal("main", a.Placement("launcher")!.Monitor);
        Assert.Null(a.Placement("claude")!.Monitor);
        Assert.Equal(1.3, a.Placement("clock")!.Scale);
    }

    [Fact]
    public void HidingAndShowingForgetsTheMonitor()
    {
        var a = new DeskArrangement("clock:tl@main");
        a.Hide("clock");
        a.Show("clock");
        Assert.Null(a.Placement("clock")!.Monitor);
    }

    // ---- which screen draws what ----

    [Fact]
    public void EachScreenDrawsItsOwnPiecesAndThoseOnEveryMonitor()
    {
        var a = new DeskArrangement([
            new DeskPlacement("clock", DeskAnchor.TL),
            new DeskPlacement("claude", DeskAnchor.BL, 1, DeskMonitors.Main),
            new DeskPlacement("launcher", DeskAnchor.BL, 1, Other),
            new DeskPlacement("message", DeskAnchor.TR, 1, Device),
        ]);
        Assert.Equal("clock:tl claude:bl@main message:tr@" + DeskMonitors.Escape(Device), a.ForScreen(MainScreen, Both).Layout);
        Assert.Equal("clock:tl launcher:bl@" + DeskMonitors.Escape(Other), a.ForScreen(Second, Both).Layout);
    }

    [Fact]
    public void TheStacksAreKeptPerMonitorAndAnchor()
    {
        var a = new DeskArrangement("clock:bl claude:bl@main launcher:bl@" + DeskMonitors.Escape(Other));
        Assert.Equal(["clock", "claude"], a.ForScreen(MainScreen, Both).Stack(DeskAnchor.BL).Select(p => p.Widget));
        Assert.Equal(["clock", "launcher"], a.ForScreen(Second, Both).Stack(DeskAnchor.BL).Select(p => p.Widget));
        Assert.Equal(["clock", "claude"], a.ForScreen(MainScreen, Both).Stacks(["clock", "claude", "launcher"])[DeskAnchor.BL].Select(p => p.Widget));
    }

    [Fact]
    public void AMissingMonitorsPiecesDrawOnTheMainOneAndComeBackWhenItDoes()
    {
        var stored = "clock:tl claude:bl@" + DeskMonitors.Escape(Other);
        var a = new DeskArrangement(stored);
        // Only the main monitor is connected: the second one's piece falls back to it.
        string[] alone = [Device];
        Assert.Equal(["clock", "claude"], a.ForScreen(MainScreen, alone).Pieces.Select(p => p.Widget));
        // Both connected: it is on its own monitor again, and gone from main.
        Assert.Equal(["clock"], a.ForScreen(MainScreen, Both).Pieces.Select(p => p.Widget));
        Assert.Equal(["clock", "claude"], a.ForScreen(Second, Both).Pieces.Select(p => p.Widget));
        // Resolution never rewrites what is stored.
        Assert.Equal(stored, a.Layout);
        Assert.Equal(stored, new DeskArrangement(stored).Layout);
    }

    [Fact]
    public void WhenWindowsGivesNoIdsEveryPieceOnAnIdDrawsOnTheMainOne()
    {
        var a = new DeskArrangement("claude:bl@" + DeskMonitors.Escape(Other));
        Assert.Single(a.ForScreen(new DeskScreenRef(null, true), []).Pieces);
        Assert.Empty(a.ForScreen(new DeskScreenRef(null, false), []).Pieces);
    }

    [Fact]
    public void IdsAreComparedWithoutRegardToCase()
    {
        var a = new DeskArrangement([new DeskPlacement("clock", DeskAnchor.TL, 1, Device.ToUpperInvariant())]);
        Assert.Single(a.ForScreen(new DeskScreenRef(Device.ToLowerInvariant(), false), [Device.ToLowerInvariant()]).Pieces);
        Assert.True(DeskMonitors.Same(Device, Device.ToUpperInvariant()));
        Assert.True(DeskMonitors.Same(null, "all"));
        Assert.True(DeskMonitors.Same("MAIN", "main"));
        Assert.False(DeskMonitors.Same("main", null));
    }

    // ---- words, Show on ----

    [Fact]
    public void WordsAndIdsAreTold()
    {
        Assert.Null(DeskMonitors.FromWord(null));
        Assert.Null(DeskMonitors.FromWord("  "));
        Assert.Null(DeskMonitors.FromWord("All"));
        Assert.Equal("main", DeskMonitors.FromWord(" MAIN "));
        Assert.Equal(Device, DeskMonitors.FromWord(Device));
        Assert.Equal("all", DeskMonitors.Word(null));
        Assert.Equal("main", DeskMonitors.Word("main"));
        Assert.False(DeskMonitors.IsId(null));
        Assert.False(DeskMonitors.IsId("main"));
        Assert.True(DeskMonitors.IsId(Device));
        Assert.True(DeskMonitors.IsConnected(Device, Both));
        Assert.False(DeskMonitors.IsConnected("main", Both));
        Assert.False(DeskMonitors.IsConnected("gone", Both));
    }

    [Fact]
    public void ShowOnTogglesBetweenEveryMonitorAndThisOne()
    {
        // On the main screen "this monitor" is the main monitor (it follows the primary); on another it is that screen's own id.
        Assert.Equal("main", DeskMonitors.ToggleShowOn(null, MainScreen));
        Assert.Equal(Other, DeskMonitors.ToggleShowOn(null, Second));
        Assert.Null(DeskMonitors.ToggleShowOn("main", MainScreen));
        Assert.Null(DeskMonitors.ToggleShowOn(Other, Second));
        Assert.Equal("main", DeskMonitors.ThisScreen(new DeskScreenRef(null, false)));   // no id: the main monitor, the only one it can name
    }

    // ---- converting points between monitors ----

    [Fact]
    public void APointLeavesOneMonitorAndLandsOnTheNextAtTheRightScale()
    {
        var left = new PxRect(0, 0, 3440, 1440);       // 100%
        var right = new PxRect(3440, 0, 1920, 1080);   // 150%
        var (px, py) = DeskScreenMath.ToPixels(left, 1.0, 3500, 200);
        Assert.Equal((3500.0, 200.0), (px, py));
        Assert.False(DeskScreenMath.Contains(left, px, py));
        Assert.True(DeskScreenMath.Contains(right, px, py));
        var (dx, dy) = DeskScreenMath.ToDips(right, 1.5, px, py);
        Assert.Equal(40.0, dx, 6);
        Assert.Equal(200 / 1.5, dy, 6);
        Assert.True(DeskScreenMath.Contains(left, 0, 0));
        Assert.False(DeskScreenMath.Contains(left, 3440, 0));      // the right edge is outside
        Assert.Equal((10.0, 10.0), DeskScreenMath.ToPixels(left, 0, 10, 10));   // a nonsense scale reads as 1
    }
}
