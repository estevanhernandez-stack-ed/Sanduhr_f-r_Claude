using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class UnderIconsClickTests
{
    // One island at (100,100) 10x10: the left half drawn (alpha 200), the right half bare except one faint plate pixel row.
    private static UnderIconsIsland Island(params UnderIconsRegion[] regions)
    {
        var alpha = new byte[100];
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 10; x++)
                alpha[y * 10 + x] = x < 5 ? (byte)200 : y == 9 ? (byte)1 : (byte)0;
        return new UnderIconsIsland(new PxRect(100, 100, 10, 10), alpha, regions);
    }

    private static readonly PxPoint Drawn = new(102, 102);
    private static readonly PxPoint Bare = new(107, 102);
    private static readonly PxPoint Plate = new(107, 109);
    private static readonly PxPoint Outside = new(50, 50);

    private static UnderIconsDecision Decide(DeskButton b, PxPoint p, UnderIconsIsland island, bool overShell = true,
        IReadOnlyList<PxRect>? icons = null, bool menuOpen = false) =>
        UnderIconsClick.Decide(b, p, overShell, icons ?? [], [island], menuOpen);

    [Fact]
    public void ARightPressOnDrawnInkOpensTheMenu()
    {
        var d = Decide(DeskButton.Right, Drawn, Island());
        Assert.Equal(new UnderIconsDecision(UnderIconsAct.Menu, 0, null), d);
    }

    [Fact]
    public void ARightPressOnBareOrFaintPixelsPasses()
    {
        Assert.Equal(UnderIconsDecision.Pass, Decide(DeskButton.Right, Bare, Island()));
        Assert.Equal(UnderIconsDecision.Pass, Decide(DeskButton.Right, Plate, Island()));
        Assert.Equal(UnderIconsDecision.Pass, Decide(DeskButton.Right, Outside, Island()));
    }

    [Fact]
    public void AnIconOrAnotherWindowOverTheInkPasses()
    {
        Assert.Equal(UnderIconsDecision.Pass, Decide(DeskButton.Right, Drawn, Island(), icons: [new PxRect(100, 100, 4, 4)]));
        Assert.Equal(UnderIconsDecision.Pass, Decide(DeskButton.Right, Drawn, Island(), overShell: false));
    }

    [Fact]
    public void AnOpenMenuPassesLeftPressesAndReopensOnARightPressOverInk()
    {
        var island = Island(new UnderIconsRegion(DeskElementKind.NowPlaying, new PxRect(100, 100, 10, 10)));
        Assert.Equal(UnderIconsDecision.Pass, Decide(DeskButton.Left, Drawn, island, menuOpen: true));
        Assert.Equal(UnderIconsAct.Menu, Decide(DeskButton.Right, Drawn, island, menuOpen: true).Act);
        Assert.Equal(UnderIconsDecision.Pass, Decide(DeskButton.Right, Outside, island, menuOpen: true));
    }

    [Fact]
    public void ALeftPressOnAClickablePieceClicksIt()
    {
        var island = Island(new UnderIconsRegion(DeskElementKind.NowPlaying, new PxRect(105, 100, 5, 5)));
        Assert.Equal(new UnderIconsDecision(UnderIconsAct.Click, 0, DeskElementKind.NowPlaying), Decide(DeskButton.Left, Bare, island));
    }

    [Fact]
    public void ALeftPressAnywhereElsePassesForTheRubberBand()
    {
        Assert.Equal(UnderIconsDecision.Pass, Decide(DeskButton.Left, Drawn, Island()));
        var launcher = Island(new UnderIconsRegion(DeskElementKind.LauncherButton, new PxRect(100, 100, 10, 10)));
        Assert.Equal(UnderIconsDecision.Pass, Decide(DeskButton.Left, Drawn, launcher));
    }

    [Fact]
    public void ARightPressOnAPieceWithItsOwnMenuCarriesTheKind()
    {
        var message = Island(new UnderIconsRegion(DeskElementKind.Message, new PxRect(105, 100, 5, 5)));
        Assert.Equal(new UnderIconsDecision(UnderIconsAct.Menu, 0, DeskElementKind.Message), Decide(DeskButton.Right, Bare, message));
        var weather = Island(new UnderIconsRegion(DeskElementKind.WeatherCredit, new PxRect(105, 100, 5, 5)));
        Assert.Equal(new UnderIconsDecision(UnderIconsAct.Menu, 0, null), Decide(DeskButton.Right, Bare, weather));
    }

    [Fact]
    public void TheLaterIslandWinsWhereTwoOverlap()
    {
        var under = Island();
        var over = new UnderIconsIsland(new PxRect(100, 100, 10, 10), Enumerable.Repeat((byte)255, 100).ToArray(), []);
        var d = UnderIconsClick.Decide(DeskButton.Right, Bare, true, [], [under, over], false);
        Assert.Equal(1, d.Island);
    }

    [Fact]
    public void AnIslandBeforeItsFirstFrameIsBare()
    {
        var empty = new UnderIconsIsland(new PxRect(100, 100, 10, 10), ReadOnlyMemory<byte>.Empty, []);
        Assert.Equal(0, empty.AlphaAt(Drawn));
        Assert.Equal(UnderIconsDecision.Pass, Decide(DeskButton.Right, Drawn, empty));
    }

    [Fact]
    public void TheSettingIsOnWhenAbsentAndOnlyOffIsWritten()
    {
        Assert.Equal("desk_under_icons", DeskUnderIcons.Key);
        Assert.True(DeskUnderIcons.Read(null));
        Assert.True(DeskUnderIcons.Read(new JsonObject { ["desk_enabled"] = true }));
        Assert.True(DeskUnderIcons.Read(new JsonObject { [DeskUnderIcons.Key] = "odd" }));
        var root = new JsonObject();
        DeskUnderIcons.Write(root, false);
        Assert.False(DeskUnderIcons.Read(root));
        DeskUnderIcons.Write(root, true);
        Assert.False(root.ContainsKey(DeskUnderIcons.Key));
    }
}
