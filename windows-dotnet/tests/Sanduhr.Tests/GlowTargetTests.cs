using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The glow target setting (spec 6.8) and the Edge placement.</summary>
public class GlowTargetTests
{
    private static readonly PxRect Mon = new(0, 0, 1920, 1080);

    // ---- Settings ----

    [Fact]
    public void DefaultsAreWindowTopPrimary()
    {
        var d = GlowTargetSettings.Default;
        Assert.Equal(GlowTargetKind.Window, d.Kind);
        Assert.Equal(GlowEdgeSide.Top, d.Side);
        Assert.Null(d.Monitor);
        Assert.Equal(d, GlowTargetSettings.Decode(null));
        Assert.Equal(d, GlowTargetSettings.Decode(new JsonObject()));
    }

    [Fact]
    public void ExistingInstallWithOnlyTheSwitchesReadsAsWindow()
    {
        var glow = (JsonObject)JsonNode.Parse("""{"waiting":true,"done":false,"skip_terminal":true}""")!;
        Assert.Equal(GlowTargetSettings.Default, GlowTargetSettings.Decode(glow));
    }

    [Theory]
    [InlineData("window", GlowTargetKind.Window)]
    [InlineData("edge", GlowTargetKind.Edge)]
    [InlineData("widget", GlowTargetKind.Widget)]
    [InlineData("desk", GlowTargetKind.Desk)]
    [InlineData("Edge", GlowTargetKind.Edge)]
    [InlineData("hologram", GlowTargetKind.Window)]
    [InlineData("", GlowTargetKind.Window)]
    public void TargetWordsDecode(string word, GlowTargetKind expected)
    {
        var glow = (JsonObject)JsonNode.Parse($$"""{"target":"{{word}}"}""")!;
        Assert.Equal(expected, GlowTargetSettings.Decode(glow).Kind);
    }

    [Theory]
    [InlineData("top", GlowEdgeSide.Top)]
    [InlineData("bottom", GlowEdgeSide.Bottom)]
    [InlineData("left", GlowEdgeSide.Left)]
    [InlineData("right", GlowEdgeSide.Right)]
    [InlineData("middle", GlowEdgeSide.Top)]
    public void SideWordsDecode(string word, GlowEdgeSide expected)
    {
        var glow = (JsonObject)JsonNode.Parse($$"""{"edge_side":"{{word}}"}""")!;
        Assert.Equal(expected, GlowTargetSettings.Decode(glow).Side);
    }

    [Fact]
    public void WrongTypedMembersAreDefaults()
    {
        var glow = (JsonObject)JsonNode.Parse("""{"target":4,"edge_side":true,"edge_monitor":9}""")!;
        Assert.Equal(GlowTargetSettings.Default, GlowTargetSettings.Decode(glow));
    }

    [Fact]
    public void DecodeReadsTheMonitorName()
    {
        var glow = (JsonObject)JsonNode.Parse("""{"target":"edge","edge_side":"left","edge_monitor":"\\\\.\\DISPLAY2"}""")!;
        var s = GlowTargetSettings.Decode(glow);
        Assert.Equal(new GlowTargetSettings(GlowTargetKind.Edge, GlowEdgeSide.Left, @"\\.\DISPLAY2"), s);
        var blank = (JsonObject)JsonNode.Parse("""{"edge_monitor":"  "}""")!;
        Assert.Null(GlowTargetSettings.Decode(blank).Monitor);
    }

    [Fact]
    public void DeskReadsAsWindowAndIsNeverOffered()
    {
        Assert.Equal(GlowTargetKind.Window, new GlowTargetSettings(GlowTargetKind.Desk, GlowEdgeSide.Top, null).Effective);
        Assert.Equal(GlowTargetKind.Edge, new GlowTargetSettings(GlowTargetKind.Edge, GlowEdgeSide.Top, null).Effective);
        Assert.Equal(GlowTargetKind.Widget, new GlowTargetSettings(GlowTargetKind.Widget, GlowEdgeSide.Top, null).Effective);
        Assert.Equal(new[] { GlowTargetKind.Window, GlowTargetKind.Edge, GlowTargetKind.Widget }, GlowTargetSettings.Offered);
    }

    [Fact]
    public void WriteIntoSetsTheThreeMembersAndKeepsTheRest()
    {
        var glow = (JsonObject)JsonNode.Parse("""{"waiting":true,"done":true,"skip_terminal":false,"extra":1}""")!;
        new GlowTargetSettings(GlowTargetKind.Edge, GlowEdgeSide.Right, "DISPLAY2").WriteInto(glow);
        Assert.Equal("edge", (string?)glow["target"]);
        Assert.Equal("right", (string?)glow["edge_side"]);
        Assert.Equal("DISPLAY2", (string?)glow["edge_monitor"]);
        Assert.True((bool?)glow["waiting"]);
        Assert.True((bool?)glow["done"]);
        Assert.False((bool?)glow["skip_terminal"]);
        Assert.Equal(1, (int?)glow["extra"]);
    }

    [Fact]
    public void WriteIntoRemovesTheMonitorWhenNull()
    {
        var glow = (JsonObject)JsonNode.Parse("""{"waiting":true,"edge_monitor":"DISPLAY2"}""")!;
        GlowTargetSettings.Default.WriteInto(glow);
        Assert.False(glow.ContainsKey("edge_monitor"));
        Assert.Equal("window", (string?)glow["target"]);
        Assert.Equal("top", (string?)glow["edge_side"]);
        Assert.True((bool?)glow["waiting"]);
    }

    [Fact]
    public void WriteThenDecodeRoundTrips()
    {
        foreach (var kind in Enum.GetValues<GlowTargetKind>())
            foreach (var side in Enum.GetValues<GlowEdgeSide>())
            {
                var s = new GlowTargetSettings(kind, side, side == GlowEdgeSide.Left ? "DISPLAY3" : null);
                var glow = new JsonObject();
                s.WriteInto(glow);
                Assert.Equal(s, GlowTargetSettings.Decode(JsonNode.Parse(glow.ToJsonString()) as JsonObject));
            }
    }

    // ---- GlowPlacement.Edge ----

    [Fact]
    public void TopIsTheSameLightCompute_PutsOnTheFallbackMonitor()
    {
        Assert.Equal(new PxRect(480, 0, 960, 24), GlowPlacement.Edge(Mon, GlowEdgeSide.Top, 1.0));
        Assert.Equal(GlowPlacement.Compute(null, Mon, 1.0), GlowPlacement.Edge(Mon, GlowEdgeSide.Top, 1.0));
        Assert.Equal(GlowPlacement.Compute(null, Mon, 1.5), GlowPlacement.Edge(Mon, GlowEdgeSide.Top, 1.5));
    }

    [Fact]
    public void BottomSitsOnTheBottomEdge()
    {
        Assert.Equal(new PxRect(480, 1056, 960, 24), GlowPlacement.Edge(Mon, GlowEdgeSide.Bottom, 1.0));
    }

    [Fact]
    public void LeftIsTallAndThinOnTheLeftEdge()
    {
        Assert.Equal(new PxRect(0, 270, 24, 540), GlowPlacement.Edge(Mon, GlowEdgeSide.Left, 1.0));
    }

    [Fact]
    public void RightIsTallAndThinOnTheRightEdge()
    {
        Assert.Equal(new PxRect(1896, 270, 24, 540), GlowPlacement.Edge(Mon, GlowEdgeSide.Right, 1.0));
    }

    [Theory]
    [InlineData(GlowEdgeSide.Top)]
    [InlineData(GlowEdgeSide.Bottom)]
    [InlineData(GlowEdgeSide.Left)]
    [InlineData(GlowEdgeSide.Right)]
    public void SecondMonitorKeepsItsOffsetAndStaysInside(GlowEdgeSide side)
    {
        var m = new PxRect(1920, -200, 2560, 1440);
        var r = GlowPlacement.Edge(m, side, 1.0);
        Assert.True(r.X >= m.X && r.Y >= m.Y && r.Right <= m.Right && r.Bottom <= m.Bottom);
    }

    [Fact]
    public void OffsetMonitorBottomAndRightLandOnTheirEdges()
    {
        var m = new PxRect(1920, -200, 2560, 1440);
        Assert.Equal(new PxRect(2560, 1216, 1280, 24), GlowPlacement.Edge(m, GlowEdgeSide.Bottom, 1.0));
        Assert.Equal(new PxRect(4456, 160, 24, 720), GlowPlacement.Edge(m, GlowEdgeSide.Right, 1.0));
        Assert.Equal(new PxRect(1920, 160, 24, 720), GlowPlacement.Edge(m, GlowEdgeSide.Left, 1.0));
        Assert.Equal(new PxRect(2560, -200, 1280, 24), GlowPlacement.Edge(m, GlowEdgeSide.Top, 1.0));
    }

    [Fact]
    public void At150PercentTheFadeIs36PxAndTheLengthIsStillHalfTheEdge()
    {
        Assert.Equal(new PxRect(480, 0, 960, 36), GlowPlacement.Edge(Mon, GlowEdgeSide.Top, 1.5));
        Assert.Equal(new PxRect(480, 1044, 960, 36), GlowPlacement.Edge(Mon, GlowEdgeSide.Bottom, 1.5));
        Assert.Equal(new PxRect(0, 270, 36, 540), GlowPlacement.Edge(Mon, GlowEdgeSide.Left, 1.5));
        Assert.Equal(new PxRect(1884, 270, 36, 540), GlowPlacement.Edge(Mon, GlowEdgeSide.Right, 1.5));
    }

    [Fact]
    public void BadScaleFallsBackToOne()
    {
        Assert.Equal(GlowPlacement.Edge(Mon, GlowEdgeSide.Left, 1.0), GlowPlacement.Edge(Mon, GlowEdgeSide.Left, 0));
        Assert.Equal(GlowPlacement.Edge(Mon, GlowEdgeSide.Left, 1.0), GlowPlacement.Edge(Mon, GlowEdgeSide.Left, double.NaN));
    }

    [Fact]
    public void ATinyMonitorIsNotOverrun()
    {
        var tiny = new PxRect(0, 0, 10, 10);
        var r = GlowPlacement.Edge(tiny, GlowEdgeSide.Bottom, 2.0);
        Assert.True(r.Width >= 1 && r.Height >= 1 && r.Right <= 10 && r.Bottom <= 10);
        Assert.True(GlowPlacement.Edge(new PxRect(5, 5, 0, 0), GlowEdgeSide.Top, 1.0).IsEmpty);
    }
}
