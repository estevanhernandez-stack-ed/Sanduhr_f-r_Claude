using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>A design's layout pieces choose a monitor (spec, Desk pieces per monitor): the contract, the merge, the validator's
/// monitor check and per-monitor contrast, the review's "what changed" lines, the state document's monitors and the wallpaper
/// summary's ids.</summary>
public sealed class DeskDesignMonitorTests
{
    private const string MainId = @"\\?\DISPLAY#GSM5B09#5&2c9d3a1&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string SideId = @"\\?\DISPLAY#DEL4167#5&2c9d3a1&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    private static JsonObject Obj(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static DeskDesign D(string json) => DeskDesign.Parse(Obj(json)).Design!;

    private static string Esc(string id) => id.Replace("\\", "\\\\");

    private static readonly IReadOnlyList<DeskMonitorInfo> Monitors = DeskMonitorNames.Named(
    [
        new DeskMonitorInfo(MainId, "", 3440, 1440, true, 0, 0),
        new DeskMonitorInfo(SideId, "", 1920, 1080, false, 3440, 0),
    ]);

    // ---- the contract ----

    [Fact]
    public void AMonitorIsAllMainOrAnId()
    {
        var d = D("{\"layout\":{\"clock\":{\"anchor\":\"tl\",\"monitor\":\"all\"},\"claude\":{\"anchor\":\"bl\",\"monitor\":\"MAIN\"},"
            + "\"launcher\":{\"anchor\":\"br\",\"monitor\":\"" + Esc(SideId) + "\"}}}");
        Assert.Equal(["all", "main", SideId], d.Layout!.Select(p => p.Monitor));
        var json = d.ToJson();
        Assert.Equal("all", (string?)json["layout"]!["clock"]!["monitor"]);
        Assert.Equal("main", (string?)json["layout"]!["claude"]!["monitor"]);
        Assert.Equal(SideId, (string?)json["layout"]!["launcher"]!["monitor"]);
        Assert.Equal(d.ToJson().ToJsonString(), D(json.ToJsonString()).ToJson().ToJsonString());
    }

    [Fact]
    public void ALayoutPieceWithNoMonitorLeavesItAlone()
    {
        var d = D("""{"layout":{"clock":{"anchor":"tl"}}}""");
        Assert.Null(d.Layout![0].Monitor);
        Assert.Null(d.ToJson()["layout"]!["clock"]!["monitor"]);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("true")]
    public void ABadMonitorIsRefusedWithItsField(string monitor)
    {
        var p = DeskDesign.Parse(Obj("{\"layout\":{\"clock\":{\"anchor\":\"tl\",\"monitor\":" + monitor + "}}}"));
        Assert.Null(p.Design);
        Assert.Contains(p.Reasons, r => r.Field == "layout.clock.monitor");
    }

    [Fact]
    public void AnOverlongOrControlCharacterIdIsRefused()
    {
        string tooLong = new('x', DeskMonitors.MaxIdLength + 1);
        Assert.Null(DeskDesign.Parse(Obj("{\"layout\":{\"clock\":{\"anchor\":\"tl\",\"monitor\":\"" + tooLong + "\"}}}")).Design);
        Assert.Null(DeskDesign.Parse(Obj("{\"layout\":{\"clock\":{\"anchor\":\"tl\",\"monitor\":\"a\\u0007b\"}}}")).Design);
    }

    [Fact]
    public void AHiddenPieceHasNoMonitor()
    {
        var d = D("""{"layout":{"clock":{"hidden":true,"monitor":"main"}}}""");
        Assert.Null(d.Layout![0].Monitor);
    }

    [Fact]
    public void AnUnknownMemberStillNamesTheMonitorAmongTheOnesToUse()
    {
        var p = DeskDesign.Parse(Obj("""{"layout":{"clock":{"screen":"main"}}}"""));
        Assert.Contains("monitor", p.Reasons[0].Message);
    }

    // ---- merging and the current Desk ----

    [Fact]
    public void TheCurrentDeskSaysWhereEveryPlacedPieceIsDrawn()
    {
        var current = DeskState.Current(DeskSettings.Default with { Layout = "clock:tl claude:bl@main launcher:br@" + DeskMonitors.Escape(SideId) },
            DeskInkSettings.Default, LauncherModel.Empty, null);
        var json = current.ToJson();
        Assert.Equal("all", (string?)json["layout"]!["clock"]!["monitor"]);
        Assert.Equal("main", (string?)json["layout"]!["claude"]!["monitor"]);
        Assert.Equal(SideId, (string?)json["layout"]!["launcher"]!["monitor"]);
        Assert.Null(json["layout"]!["meters"]!["monitor"]);   // a hidden piece is nowhere
        Assert.Empty(DeskDesign.Parse(json).Reasons);
    }

    [Fact]
    public void AProposalMovesOnePieceToAMonitorAndLeavesTheRest()
    {
        var current = DeskState.Current(DeskSettings.Default with { Layout = "clock:tl claude:bl" }, DeskInkSettings.Default, LauncherModel.Empty, null);
        var merged = DeskDesign.Merge(current, D("{\"layout\":{\"clock\":{\"monitor\":\"" + Esc(SideId) + "\"}}}"));
        Assert.Equal(SideId, merged.Layout!.Single(p => p.Piece == "clock").Monitor);
        Assert.Null(merged.Layout!.Single(p => p.Piece == "claude").Monitor);   // untouched: every monitor
        Assert.Equal(DeskAnchor.TL, merged.Layout!.Single(p => p.Piece == "clock").Anchor);   // its anchor stayed

        var applied = DeskDesignApply.Layout(merged.Layout!);
        Assert.Equal("clock:tl@" + DeskMonitors.Escape(SideId) + " claude:bl", applied);

        var back = DeskDesign.Merge(merged, D("""{"layout":{"clock":{"monitor":"all"}}}"""));
        Assert.Equal("clock:tl claude:bl", DeskDesignApply.Layout(back.Layout!));
    }

    [Fact]
    public void ShowingAHiddenPieceOnAMonitorPlacesItThere()
    {
        var current = DeskState.Current(DeskSettings.Default, DeskInkSettings.Default, LauncherModel.Empty, null);
        var merged = DeskDesign.Merge(current, D("""{"layout":{"weather":{"anchor":"mr","monitor":"main"}}}"""));
        Assert.Equal("main", merged.Layout!.Single(p => p.Piece == "weather").Monitor);
    }

    [Fact]
    public void TwoDesignsOnDifferentMonitorsAreDifferentLooksAndNoMonitorIsEveryMonitor()
    {
        var all = D("""{"layout":{"clock":{"anchor":"tl","monitor":"all"}}}""");
        var none = D("""{"layout":{"clock":{"anchor":"tl"}}}""");
        var main = D("""{"layout":{"clock":{"anchor":"tl","monitor":"main"}}}""");
        Assert.Equal(DeskDesigns.Canonical(all), DeskDesigns.Canonical(none));
        Assert.NotEqual(DeskDesigns.Canonical(all), DeskDesigns.Canonical(main));
    }

    // ---- what changed ----

    private static string? NameOf(string id) => DeskMonitorNames.NameOf(Monitors, id);

    [Fact]
    public void TheReviewNamesTheMonitorAPieceMovesTo()
    {
        var current = DeskState.Current(DeskSettings.Default, DeskInkSettings.Default, LauncherModel.Empty, null);
        var merged = DeskDesign.Merge(current, D("{\"layout\":{\"clock\":{\"monitor\":\"" + Esc(SideId) + "\"},\"claude\":{\"monitor\":\"main\"}}}"));
        var lines = DeskDesign.Diff(current, merged, SettingsNames.DeskPieceName, NameOf);
        Assert.Contains("Clock and date: moved to 1920\u00D71080, right of main", lines);
        Assert.Contains("Claude meters (line): moved to the main monitor", lines);

        var again = DeskDesign.Merge(merged, D("""{"layout":{"clock":{"monitor":"all"}}}"""));
        Assert.Contains("Clock and date: now on all monitors", DeskDesign.Diff(merged, again, SettingsNames.DeskPieceName, NameOf));
        Assert.Empty(DeskDesign.Diff(merged, merged, SettingsNames.DeskPieceName, NameOf));
    }

    [Fact]
    public void AnUnnameableMonitorIsCalledAnotherMonitorAndAShownPieceSaysWhereItIsShown()
    {
        var current = DeskState.Current(DeskSettings.Default, DeskInkSettings.Default, LauncherModel.Empty, null);
        var merged = DeskDesign.Merge(current, D("""{"layout":{"weather":{"anchor":"mr","monitor":"somewhere"}}}"""));
        Assert.Contains("Weather: shown at middle right, on another monitor", DeskDesign.Diff(current, merged, SettingsNames.DeskPieceName, NameOf));
    }

    // ---- the validator ----

    [Fact]
    public void AnUnknownMonitorIdIsRefusedWhenTheMonitorsAreKnown()
    {
        var ctx = new DeskDesignContext(Monitors: Monitors);
        var v = DeskDesign.Validate(Obj("""{"layout":{"clock":{"anchor":"tl","monitor":"\\\\?\\DISPLAY#NOPE#1"}}}"""), ctx);
        Assert.Equal(DeskDesignStatus.Rejected, v.Status);
        Assert.Equal("layout.clock.monitor", v.Reasons[0].Field);
        Assert.Contains("get_desk", v.Reasons[0].Message);
    }

    [Fact]
    public void AConnectedIdAllAndMainAreAccepted()
    {
        var ctx = new DeskDesignContext(Monitors: Monitors);
        foreach (var monitor in new[] { "all", "main", Esc(SideId), Esc(SideId.ToLowerInvariant()) })
            Assert.True(DeskDesign.Validate(Obj("{\"layout\":{\"clock\":{\"anchor\":\"tl\",\"monitor\":\"" + monitor + "\"}}}"), ctx).Ok, monitor);
    }

    [Fact]
    public void WithTheMonitorsUnknownTheCheckIsSkippedAndTheWidgetChecksAgain()
    {
        Assert.True(DeskDesign.Validate(Obj("""{"layout":{"clock":{"anchor":"tl","monitor":"whatever"}}}"""), DeskDesignContext.Unknown).Ok);
        // Known and empty (an old state, or Windows said nothing): an id cannot be one of them.
        Assert.False(DeskDesign.Validate(Obj("""{"layout":{"clock":{"anchor":"tl","monitor":"whatever"}}}"""), new DeskDesignContext(Monitors: [])).Ok);
    }

    private static WallpaperOverview TwoMonitorWall(double mainBrightness, double sideBrightness)
    {
        WallpaperMonitorSummary Mon(int index, bool primary, string id, double b)
        {
            var readings = DeskAnchors.All.Select(a => new AnchorReading(a, b, 0)).ToList();
            return new WallpaperMonitorSummary(index, primary, 1920, 1080, new WallpaperSummary([new DominantColor("#808080", 1)], b, readings), id);
        }
        return new WallpaperOverview([], (mainBrightness + sideBrightness) / 2, [Mon(0, true, MainId, mainBrightness), Mon(1, false, SideId, sideBrightness)]);
    }

    [Fact]
    public void ContrastIsJudgedAgainstThePiecesOwnMonitor()
    {
        // Light ink: fine on the dark main monitor, unreadable on the bright side one.
        var ctx = new DeskDesignContext(Wallpaper: TwoMonitorWall(0.02, 0.9), Monitors: Monitors);
        string Design(string monitor) => "{\"ink\":\"light\",\"layout\":{\"clock\":{\"anchor\":\"bl\",\"monitor\":\"" + monitor + "\"}}}";
        Assert.True(DeskDesign.Validate(Obj(Design("main")), ctx).Ok);
        Assert.True(DeskDesign.Validate(Obj(Design(Esc(MainId))), ctx).Ok);
        Assert.Equal(DeskDesignStatus.Rejected, DeskDesign.Validate(Obj(Design(Esc(SideId))), ctx).Status);
        // On every monitor the worst one decides, as before.
        Assert.Equal(DeskDesignStatus.Rejected, DeskDesign.Validate(Obj(Design("all")), ctx).Status);
    }

    [Fact]
    public void AMonitorTheSummaryDoesNotKnowIsJudgedAgainstAllOfThem()
    {
        var wall = TwoMonitorWall(0.02, 0.9);
        wall = wall with { Monitors = [.. wall.Monitors.Select(m => m with { Id = null })] };
        var ctx = new DeskDesignContext(Wallpaper: wall, Monitors: Monitors);
        var v = DeskDesign.Validate(Obj("{\"ink\":\"light\",\"layout\":{\"clock\":{\"anchor\":\"bl\",\"monitor\":\"" + Esc(MainId) + "\"}}}"), ctx);
        Assert.Equal(DeskDesignStatus.Rejected, v.Status);
    }

    [Fact]
    public void ARePlacedPieceIsJudgedOnItsNewMonitorWhenOnlyTheLayoutChanges()
    {
        var current = D("{\"ink\":\"light\",\"layout\":{\"clock\":{\"anchor\":\"bl\",\"monitor\":\"main\"}}}");
        var ctx = new DeskDesignContext(Wallpaper: TwoMonitorWall(0.02, 0.9), Current: current, Monitors: Monitors);
        var v = DeskDesign.Validate(Obj("{\"layout\":{\"clock\":{\"monitor\":\"" + Esc(SideId) + "\"}}}"), ctx);
        Assert.Equal(DeskDesignStatus.Rejected, v.Status);
        Assert.Equal("layout.clock", v.Reasons[0].Field);
    }

    // ---- the wallpaper summary and the state document ----

    [Fact]
    public void TheWallpaperSummaryCarriesEachMonitorsId()
    {
        var wall = TwoMonitorWall(0.2, 0.4);
        var back = WallpaperOverview.FromJson(wall.ToJson())!;
        Assert.Equal([MainId, SideId], back.Monitors.Select(m => m.Id));
        Assert.Equal(SideId, (string?)wall.ToJson()["monitors"]![1]!["id"]);
        // A summary from an older widget has no ids and reads fine.
        var json = wall.ToJson();
        foreach (var m in json["monitors"]!.AsArray()) ((JsonObject)m!).Remove("id");
        Assert.All(WallpaperOverview.FromJson(json)!.Monitors, m => Assert.Null(m.Id));
    }

    [Fact]
    public void TheStateListsTheMonitors()
    {
        var current = DeskState.Current(DeskSettings.Default, DeskInkSettings.Default, LauncherModel.Empty, null);
        var state = DeskState.Build(current, [], null, null, null, [], false, "3.7.0", monitors: Monitors);
        var list = state["monitors"]!.AsArray();
        Assert.Equal(2, list.Count);
        Assert.Equal(MainId, (string?)list[0]!["id"]);
        Assert.Equal("3440\u00D71440, main", (string?)list[0]!["name"]);
        Assert.True((bool)list[0]!["primary"]!);
        Assert.Equal((3440, 1440, 0, 0), ((int)list[0]!["width"]!, (int)list[0]!["height"]!, (int)list[0]!["left"]!, (int)list[0]!["top"]!));
        Assert.Equal(3440, (int)list[1]!["left"]!);
        Assert.False((bool)list[1]!["primary"]!);
        Assert.Empty(DeskState.Build(current, [], null, null, null, [], false, "3.7.0")["monitors"]!.AsArray());
    }
}
