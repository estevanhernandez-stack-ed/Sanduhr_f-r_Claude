using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Saved designs and the ten-kept rule, the desk-state.json document, and the widget half of the request and
/// result handoff (temp folders only).</summary>
public sealed class DeskDesignHandoffTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-design-" + Guid.NewGuid().ToString("N"));
    private readonly DeskDesignPaths _paths;
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    public DeskDesignHandoffTests()
    {
        _paths = new DeskDesignPaths(Path.Combine(_root, ".sanduhr"));
        Directory.CreateDirectory(_paths.Support);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static JsonObject Obj(string json) => (JsonObject)JsonNode.Parse(json)!;

    // ---- saved designs ----

    private static SavedDesign Saved(string id, int minutesAgo, bool pinned = false) =>
        new(id, "Design " + id, pinned, T0.AddMinutes(-minutesAgo), DeskDesign.Parse(Obj("""{"plate":true}""")).Design!);

    [Fact]
    public void TenDesignsAreKeptAndNothingIsPrunedAtTen()
    {
        var ten = Enumerable.Range(0, 10).Select(i => Saved("d" + i, i)).ToList();
        var result = DeskDesignLibrary.Prune(ten);
        Assert.Equal(10, result.Kept.Count);
        Assert.Empty(result.Removed);
    }

    [Fact]
    public void TheOldestUnpinnedDesignIsPrunedFirst()
    {
        var eleven = Enumerable.Range(0, 11).Select(i => Saved("d" + i, i)).ToList();
        var result = DeskDesignLibrary.Prune(eleven);
        Assert.Equal(["d10"], result.Removed.Select(d => d.Id));
        Assert.Equal(10, result.Kept.Count);
    }

    [Fact]
    public void APinnedDesignIsNeverPrunedEvenWhenItIsTheOldest()
    {
        var all = Enumerable.Range(0, 12).Select(i => Saved("d" + i, i, pinned: i == 11)).ToList();
        var result = DeskDesignLibrary.Prune(all);
        Assert.Equal(["d10", "d9"], result.Removed.Select(d => d.Id));
        Assert.Contains(result.Kept, d => d.Id == "d11");
    }

    [Fact]
    public void ALibraryOfOnlyPinsMayHoldMoreThanTen()
    {
        var pins = Enumerable.Range(0, 12).Select(i => Saved("d" + i, i, pinned: true)).ToList();
        var result = DeskDesignLibrary.Prune(pins);
        Assert.Equal(12, result.Kept.Count);
        Assert.Empty(result.Removed);
    }

    [Fact]
    public void KeptDesignsComeBackNewestFirst_AndTiesBreakByIdSoPruningIsDeterministic()
    {
        var same = new[] { Saved("b", 5), Saved("a", 5), Saved("c", 1) };
        var kept = DeskDesignLibrary.Prune(same).Kept;
        Assert.Equal(["c", "a", "b"], kept.Select(d => d.Id));
        var pruned = DeskDesignLibrary.Prune(same, keep: 2).Removed;
        Assert.Equal(["b"], pruned.Select(d => d.Id));
    }

    [Fact]
    public void AddingReplacesTheSameIdAndPrunes()
    {
        var existing = Enumerable.Range(0, 10).Select(i => Saved("d" + i, i + 1)).ToList();
        var added = DeskDesignLibrary.Add(existing, Saved("new", 0));
        Assert.Equal("new", added.Kept[0].Id);
        Assert.Equal(["d9"], added.Removed.Select(d => d.Id));

        var renamed = DeskDesignLibrary.Add(existing, Saved("d3", 0));
        Assert.Equal(10, renamed.Kept.Count);
        Assert.Empty(renamed.Removed);
    }

    [Fact]
    public void ASavedDesignRoundTripsThroughItsDocument()
    {
        var saved = new SavedDesign(SavedDesign.NewId(), "Dusk", true, T0, DeskDesign.Parse(Obj("""{"ink":"light","layout":{"clock":{"anchor":"tr"}}}""")).Design!);
        var back = SavedDesign.Parse(saved.ToJson())!;
        Assert.Equal(saved.Id, back.Id);
        Assert.Equal("Dusk", back.Name);
        Assert.True(back.Pinned);
        Assert.Equal(T0, back.AppliedAt);
        Assert.Equal(saved.Design.ToJson().ToJsonString(), back.Design.ToJson().ToJsonString());
        Assert.Equal(["id", "name", "pinned", "applied_at"], saved.SummaryJson().Select(kv => kv.Key));
    }

    [Theory]
    [InlineData("""{"schema_version":2,"id":"a","name":"x","applied_at":"2026-10-21T12:00:00Z","design":{}}""")]
    [InlineData("""{"schema_version":1,"id":"..\\evil","name":"x","applied_at":"2026-10-21T12:00:00Z","design":{}}""")]
    [InlineData("""{"schema_version":1,"id":"A","name":"x","applied_at":"2026-10-21T12:00:00Z","design":{}}""")]
    [InlineData("""{"schema_version":1,"id":"a","name":"","applied_at":"2026-10-21T12:00:00Z","design":{}}""")]
    [InlineData("""{"schema_version":1,"id":"a","name":"x","applied_at":"yesterday","design":{}}""")]
    [InlineData("""{"schema_version":1,"id":"a","name":"x","applied_at":"2026-10-21T12:00:00Z","design":{"launcher":{"buttons":[{"kind":"app","target":"cmd.exe /c x"}]}}}""")]
    [InlineData("[]")]
    public void ADocumentThatIsNotASavedDesignIsNull(string json) => Assert.Null(SavedDesign.Parse(JsonNode.Parse(json)));

    [Theory]
    [InlineData("a", true)]
    [InlineData("0123456789abcdef0123456789abcdef", true)]
    [InlineData("a-b", true)]
    [InlineData("", false)]
    [InlineData("-a", false)]
    [InlineData("A", false)]
    [InlineData("a/b", false)]
    [InlineData("a.json", false)]
    [InlineData("a b", false)]
    public void ADesignIdIsAShortFileSafeKey(string id, bool ok) => Assert.Equal(ok, SavedDesign.IsValidId(id));

    // ---- desk-state.json ----

    [Fact]
    public void TheCurrentDeskIsBuiltFromTheSettingsAndTheTheme()
    {
        var desk = new DeskSettings(true, "clock:bl:1.2 claude:tr", DeskMarginSettings.Default, null, 112, Plate: true);
        var ink = new DeskInkSettings(DeskInkMode.Custom, "ffd08a,ff7e5f", Shadow: false);
        var launcher = LauncherModel.Parse(Obj("""{"orientation":"column","buttons":[{"kind":"link","target":"https://example.com"}]}"""));
        var theme = DeskDesignTests.ObsidianTheme();

        var d = DeskState.Current(desk, ink, launcher, theme);

        Assert.Equal("EsteFont Pro", d.Font!.Family);
        Assert.Equal(["#ffd08a", "#ff7e5f"], d.Ink!.Colors);
        Assert.True(d.Plate);
        Assert.False(d.Shadow);
        Assert.Equal(DeskAnchor.BL, d.Layout!.Single(p => p.Piece == "clock").Anchor);
        Assert.Equal(1.2, d.Layout!.Single(p => p.Piece == "clock").Size);
        Assert.Equal(DeskAnchor.TR, d.Layout!.Single(p => p.Piece == "claude").Anchor);
        Assert.True(d.Layout!.Single(p => p.Piece == "meters").IsHidden);
        Assert.Equal(LauncherOrientation.Column, d.Launcher!.Orientation);
        // The current design is itself a valid design: the contract reads back what it writes.
        Assert.Empty(DeskDesign.Parse(d.ToJson()).Reasons);
        Assert.Null(d.Messages);
    }

    [Theory]
    [InlineData(DeskInkMode.Auto, DeskInkKind.Auto)]
    [InlineData(DeskInkMode.Light, DeskInkKind.Light)]
    [InlineData(DeskInkMode.Dark, DeskInkKind.Dark)]
    [InlineData(DeskInkMode.Theme, DeskInkKind.Theme)]
    public void EveryInkModeMapsToADesignInk(DeskInkMode mode, DeskInkKind kind)
    {
        var d = DeskState.Current(DeskSettings.Default, new DeskInkSettings(mode, "ffffff", true), LauncherModel.Empty, null);
        Assert.Equal(kind, d.Ink!.Kind);
    }

    [Fact]
    public void ASystemFontAndAnUnreadableCustomInkStayReadable()
    {
        var d = DeskState.Current(DeskSettings.Default with { Font = "" }, new DeskInkSettings(DeskInkMode.Custom, "not a color", true), LauncherModel.Empty, null);
        Assert.Equal("", d.Font!.Family);
        Assert.Equal(["#ffffff"], d.Ink!.Colors);
        Assert.Empty(DeskDesign.Parse(d.ToJson()).Reasons);
    }

    [Fact]
    public void TheStateDocumentHasTheShapeGetDeskPromises()
    {
        var current = DeskState.Current(DeskSettings.Default, DeskInkSettings.Default, LauncherModel.Empty, null);
        var saved = Saved("one", 3);
        var state = DeskState.Build(current, ["Segoe UI", "Caveat", "caveat", "bad,name", ""],
            WallpaperOverview.Combine([]), @"C:\Users\me\Pictures\wall.jpg",
            [new StartMenuApp("Spotify", "Spotify"), new StartMenuApp("Calc!App", "Calculator")], [saved], direct: false, writerVersion: "3.7.0");

        Assert.Equal(1, (int)state["schema_version"]!);
        Assert.Equal("3.7.0", (string?)state["writer_version"]);
        Assert.Equal(DeskArrangement.Known, state["pieces"]!.AsArray().Select(n => (string)n!));
        Assert.Equal(["tl", "tc", "tr", "ml", "mr", "bl", "bc", "br"], state["anchors"]!.AsArray().Select(n => (string)n!));
        Assert.Equal(["Caveat", "Segoe UI"], state["fonts"]!["installed"]!.AsArray().Select(n => (string)n!));
        Assert.Equal(["EsteFont Pro", "EsteFont 26"], state["fonts"]!["bundled"]!.AsArray().Select(n => (string)n!));
        Assert.Equal(@"C:\Users\me\Pictures\wall.jpg", (string?)state["wallpaper"]!["path"]);
        Assert.NotNull(state["wallpaper"]!["summary"]);
        Assert.Equal("image", (string?)state["wallpaper"]!["kind"]);
        var row = state["designs"]![0]!;
        Assert.Equal("one", (string?)row["id"]);
        Assert.Equal("Design one", (string?)row["name"]);
        Assert.False((bool)state["direct"]!);
        Assert.Equal(["Calc!App", "Spotify"], state["start_menu_apps"]!.AsArray().Select(n => (string)n!["id"]!));
        Assert.Equal(3.0, (double)state["limits"]!["contrast_min"]!);
        Assert.NotNull(state["design"]!["layout"]);
        Assert.Equal(8, (int)state["limits"]!["launcher_buttons"]!);
    }

    [Fact]
    public void ADesktopWithNoWallpaperFileIsReportedAsColor()
    {
        var current = DeskState.Current(DeskSettings.Default, DeskInkSettings.Default, LauncherModel.Empty, null);
        var state = DeskState.Build(current, [], WallpaperOverview.Combine([]), null, null, [], direct: false, writerVersion: "3.7.0");
        Assert.Equal("color", (string?)state["wallpaper"]!["kind"]);
    }

    [Fact]
    public void TheStateLeavesOutWhatItDoesNotKnow()
    {
        var current = DeskState.Current(DeskSettings.Default, DeskInkSettings.Default, LauncherModel.Empty, null);
        var state = DeskState.Build(current, [], null, null, null, [], direct: false, writerVersion: "3.7.0");
        Assert.Null(state["start_menu_apps"]);
        Assert.Null(state["wallpaper"]!["summary"]);
        Assert.Null(state["wallpaper"]!["kind"]);
        Assert.Null(state["wallpaper"]!["path"]);
        Assert.Empty(state["designs"]!.AsArray());
    }

    [Fact]
    public void WriteStateWritesOnceAndAgainOnlyWhenItChanged()
    {
        var handoff = new DeskDesignHandoff(_paths, () => DeskDesignContext.Unknown);
        var a = new JsonObject { ["schema_version"] = 1, ["n"] = 1 };
        Assert.True(handoff.WriteState(a));
        var first = File.GetLastWriteTimeUtc(_paths.State);
        Assert.Equal("{\"schema_version\":1,\"n\":1}", File.ReadAllText(_paths.State));

        Thread.Sleep(30);
        Assert.True(handoff.WriteState(new JsonObject { ["schema_version"] = 1, ["n"] = 1 }));
        Assert.Equal(first, File.GetLastWriteTimeUtc(_paths.State));

        Assert.True(handoff.WriteState(new JsonObject { ["schema_version"] = 1, ["n"] = 2 }));
        Assert.Contains("\"n\":2", File.ReadAllText(_paths.State));
        Assert.Empty(Directory.GetFiles(_paths.Support, "*.tmp"));
    }

    [Fact]
    public void DeleteStateRemovesTheFile_AndTheNextWriteBringsItBack()
    {
        var handoff = new DeskDesignHandoff(_paths, () => DeskDesignContext.Unknown);
        var state = new JsonObject { ["schema_version"] = 1 };
        handoff.WriteState(state);
        Assert.True(File.Exists(_paths.State));
        handoff.DeleteState();
        Assert.False(File.Exists(_paths.State));
        handoff.DeleteState();   // twice is fine
        handoff.WriteState(state);
        Assert.True(File.Exists(_paths.State));
    }

    // ---- the request and the result ----

    private static byte[] Request(string id, string designJson, DateTimeOffset? at = null, int schema = 1) =>
        Encoding.UTF8.GetBytes(new JsonObject
        {
            ["schema_version"] = schema,
            ["id"] = id,
            ["requested_at"] = (at ?? T0).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
            ["design"] = JsonNode.Parse(designJson),
        }.ToJsonString());

    private DeskDesignHandoff Handoff(Func<DeskDesignContext>? context = null) =>
        new(_paths, context ?? (() => DeskDesignContext.Unknown), () => T0);

    private JsonObject Result() => Obj(File.ReadAllText(_paths.Result));

    [Fact]
    public void AGoodDesignIsAnsweredNotReadyInPhaseOne()
    {
        var h = Handoff();
        File.WriteAllBytes(_paths.Request, Request("req1", """{"plate":true}"""));
        h.Check();

        Assert.False(File.Exists(_paths.Request));   // taken
        var result = Result();
        Assert.Equal("req1", (string?)result["id"]);
        Assert.Equal("rejected", (string?)result["result"]!["status"]);
        Assert.Equal("not_ready", (string?)result["result"]!["reason"]);
        Assert.Equal("design", (string?)result["result"]!["reasons"]![0]!["field"]);
        Assert.NotNull(result["completed_at"]);
    }

    [Fact]
    public void ABadDesignIsAnsweredWithItsReasons()
    {
        var h = Handoff();
        h.Receive(Request("req2", """{"plate":"yes","ink":"red"}"""));
        var result = Result()["result"]!;
        Assert.Equal("rejected", (string?)result["status"]);
        Assert.Null(result["reason"]);
        Assert.Equal(["ink", "plate"], result["reasons"]!.AsArray().Select(n => (string)n!["field"]!));
    }

    [Fact]
    public void TheWidgetChecksTheMachineRulesToo()
    {
        var h = Handoff(() => new DeskDesignContext(StartMenuApps: ["Spotify"]));
        h.Receive(Request("req3", """{"launcher":{"buttons":[{"kind":"app","target":"Some.App!x"}]}}"""));
        Assert.Equal("launcher.buttons[0].target", (string?)Result()["result"]!["reasons"]![0]!["field"]);
    }

    [Fact]
    public void ARequestOlderThanTenMinutesIsDroppedWithoutAnAnswer()
    {
        var h = Handoff();
        h.Receive(Request("old", "{}", T0.AddMinutes(-11)));
        Assert.False(File.Exists(_paths.Result));
    }

    [Fact]
    public void ARequestJustInsideTenMinutesIsAnswered()
    {
        Handoff().Receive(Request("fresh", "{}", T0.AddMinutes(-9)));
        Assert.True(File.Exists(_paths.Result));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"requested_at\":\"2026-10-21T12:00:00.000Z\"}")]
    [InlineData("{\"id\":\"\",\"requested_at\":\"2026-10-21T12:00:00.000Z\"}")]
    public void ThingsThatAreNotRequestsAreDroppedInSilence(string text)
    {
        Handoff().Receive(Encoding.UTF8.GetBytes(text));
        Assert.False(File.Exists(_paths.Result));
    }

    [Fact]
    public void AnIdTooLongToEchoIsDropped()
    {
        Handoff().Receive(Request(new string('x', 65), "{}"));
        Assert.False(File.Exists(_paths.Result));
    }

    [Fact]
    public void AnUnknownSchemaIsRefused()
    {
        Handoff().Receive(Request("s2", "{}", schema: 2));
        Assert.Equal("schema_version", (string?)Result()["result"]!["reasons"]![0]!["field"]);
    }

    [Fact]
    public void AMissingDateIsRefusedWithTheId()
    {
        Handoff().Receive(Encoding.UTF8.GetBytes("""{"schema_version":1,"id":"nodate","design":{}}"""));
        Assert.Equal("nodate", (string?)Result()["id"]);
        Assert.Equal("requested_at", (string?)Result()["result"]!["reasons"]![0]!["field"]);
    }

    [Fact]
    public void ANeedForAFontReachesTheHandler_WhichCanAnswerWithTheFonts()
    {
        var h = Handoff(() => new DeskDesignContext(InstalledFonts: ["Segoe UI"]));
        h.Handler = (id, verdict) => new DeskAnswer(DeskAnswer.NeedsFont, Fonts: verdict.Fonts, Changes: ["Font: x to Caveat"]);
        h.Receive(Request("font", """{"font":{"family":"Caveat","link":"https://example.com/caveat"}}"""));

        var result = Result()["result"]!;
        Assert.Equal("needs_font", (string?)result["status"]);
        Assert.Equal("Caveat", (string?)result["fonts"]![0]!["family"]);
        Assert.Equal("https://example.com/caveat", (string?)result["fonts"]![0]!["link"]);
        Assert.Equal("Font: x to Caveat", (string?)result["changes"]![0]);
    }

    [Fact]
    public void ResultsLeaveNoTempFilesBehind()
    {
        var h = Handoff();
        for (int i = 0; i < 5; i++) h.Receive(Request("r" + i, "{}"));
        Assert.Empty(Directory.GetFiles(_paths.Support, "*.tmp"));
        Assert.Equal("r4", (string?)Result()["id"]);
    }

    [Fact]
    public void ANewRequestIsTakenOnceEvenIfCheckedTwice()
    {
        var h = Handoff();
        File.WriteAllBytes(_paths.Request, Request("once", "{}"));
        h.Check();
        File.Delete(_paths.Result);
        h.Check();   // nothing waiting now
        Assert.False(File.Exists(_paths.Result));
    }
}
