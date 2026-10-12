using System.Diagnostics;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>The covered-island rules (S7, 7.1): rectangle subtraction, which windows count, and the pause rule.</summary>
public class DeskCoverageTests
{
    private static readonly PxRect Island = new(100, 100, 200, 100);

    private static WindowFacts Facts(
        bool visible = true, bool minimized = false, bool cloaked = false, bool exTransparent = false,
        bool exLayered = false, bool isDesk = false, PxRect? frame = null) =>
        new(visible, minimized, cloaked, exTransparent, exLayered, isDesk, frame ?? new PxRect(0, 0, 500, 500));

    [Fact]
    public void OneWindowCoveringExactly() =>
        Assert.True(DeskCoverage.FullyCovered(Island, [Island]));

    [Fact]
    public void OneWindowCoveringMoreThanTheIsland() =>
        Assert.True(DeskCoverage.FullyCovered(Island, [new PxRect(0, 0, 1000, 1000)]));

    [Fact]
    public void TwoHalvesCoverTogether() =>
        Assert.True(DeskCoverage.FullyCovered(Island, [new PxRect(100, 100, 100, 100), new PxRect(200, 100, 100, 100)]));

    [Fact]
    public void AGapOfOnePixelLeavesItUncovered()
    {
        Assert.False(DeskCoverage.FullyCovered(Island, [new PxRect(100, 100, 100, 100), new PxRect(201, 100, 99, 100)]));
        Assert.False(DeskCoverage.FullyCovered(Island, [new PxRect(100, 100, 200, 99)]));
    }

    [Fact]
    public void PartialOverlapIsNotCovered() =>
        Assert.False(DeskCoverage.FullyCovered(Island, [new PxRect(0, 0, 200, 150)]));

    [Fact]
    public void TouchingEdgesCoverNothing()
    {
        Assert.False(DeskCoverage.FullyCovered(Island, [new PxRect(0, 100, 100, 100)]));
        Assert.False(DeskCoverage.FullyCovered(Island, [new PxRect(300, 100, 100, 100)]));
        Assert.False(DeskCoverage.FullyCovered(Island, [new PxRect(100, 200, 200, 50)]));
        Assert.False(DeskCoverage.FullyCovered(Island, [new PxRect(100, 0, 200, 100)]));
    }

    [Fact]
    public void AnEmptyTargetIsNeverCovered()
    {
        Assert.False(DeskCoverage.FullyCovered(new PxRect(0, 0, 0, 0), [new PxRect(0, 0, 1000, 1000)]));
        Assert.False(DeskCoverage.FullyCovered(new PxRect(5, 5, 10, 0), []));
    }

    [Fact]
    public void NoOccludersNeverCovers() =>
        Assert.False(DeskCoverage.FullyCovered(Island, []));

    [Fact]
    public void EmptyOccludersAreIgnored() =>
        Assert.False(DeskCoverage.FullyCovered(Island, [new PxRect(100, 100, 0, 100), new PxRect(100, 100, 200, 0)]));

    [Fact]
    public void AHoleInTheMiddleLeavesItUncovered()
    {
        // Strips around a 10x10 hole.
        var strips = new[]
        {
            new PxRect(100, 100, 200, 45),
            new PxRect(100, 155, 200, 45),
            new PxRect(100, 145, 95, 10),
            new PxRect(205, 145, 95, 10),
        };
        Assert.False(DeskCoverage.FullyCovered(Island, strips));
        Assert.True(DeskCoverage.FullyCovered(Island, [.. strips, new PxRect(195, 145, 10, 10)]));
    }

    [Fact]
    public void ATenByTenGridOfSmallRectsCovers()
    {
        var grid = new List<PxRect>();
        for (int r = 0; r < 10; r++)
            for (int c = 0; c < 10; c++)
                grid.Add(new PxRect(100 + c * 20, 100 + r * 10, 20, 10));
        Assert.True(DeskCoverage.FullyCovered(Island, grid));
        grid.RemoveAt(37);
        Assert.False(DeskCoverage.FullyCovered(Island, grid));
    }

    [Fact]
    public void FiftyOccludersRunFast()
    {
        var many = Enumerable.Range(0, 50).Select(i => new PxRect(100 + i * 3, 100 + (i % 7) * 10, 40, 30)).ToList();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 200; i++) DeskCoverage.FullyCovered(Island, many);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void AVisibleOrdinaryWindowOccludes() => Assert.True(DeskCoverage.Occluder(Facts()));

    [Theory]
    [InlineData("hidden")]
    [InlineData("minimized")]
    [InlineData("cloaked")]
    [InlineData("transparent")]
    [InlineData("layered")]
    [InlineData("desk")]
    [InlineData("emptyframe")]
    public void EachDisqualifierStopsAWindowOccluding(string why)
    {
        var f = why switch
        {
            "hidden" => Facts(visible: false),
            "minimized" => Facts(minimized: true),
            "cloaked" => Facts(cloaked: true),
            "transparent" => Facts(exTransparent: true),
            "layered" => Facts(exLayered: true),
            "desk" => Facts(isDesk: true),
            _ => Facts(frame: new PxRect(10, 10, 0, 50)),
        };
        Assert.False(DeskCoverage.Occluder(f));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void ArrangingNeverPauses(bool covered, bool arranging, bool expected) =>
        Assert.Equal(expected, DeskPauseRule.Paused(covered, arranging));
}
