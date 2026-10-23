using System.Globalization;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// Meter styles (#336): the style as the fifth field of a layout word (written only when not bar, read back by every older build as
/// the size and the tilt it already knew), the design contract's <c>layout.&lt;piece&gt;.style</c> (only on the pieces that have one),
/// the review's lines, saved-design matching that ignores the default bar, get_desk's report and limits, and the small rules the
/// view draws by (lit ticks, a ring's short name, the Claude line's compact two rows).
/// </summary>
public sealed class DeskMeterStylesTests
{
    private static DeskParse Parse(string json) => DeskDesign.Parse(JsonNode.Parse(json));

    private static DeskDesign D(string json) => Parse(json).Design!;

    // ---- the layout word ----

    [Theory]
    [InlineData("meters:tr:1:0:ring", DeskMeterStyle.Ring)]
    [InlineData("telemetry:mr:1.2:18:slim@main", DeskMeterStyle.Slim)]
    [InlineData("claude:bl:1:-18:ticks", DeskMeterStyle.Ticks)]
    [InlineData("meters:tr", DeskMeterStyle.Bar)]
    [InlineData("meters:tr:1.4", DeskMeterStyle.Bar)]
    [InlineData("meters:tr:1:12", DeskMeterStyle.Bar)]
    public void AStyleIsTheFifthFieldOfAWordAndRoundTrips(string word, DeskMeterStyle style)
    {
        var a = new DeskArrangement(word);
        var p = Assert.Single(a.Pieces);
        Assert.Equal(style, p.Style);
        Assert.Equal(word, a.Layout);
    }

    [Fact]
    public void ABarIsNeverWrittenSoEveryOlderLayoutReadsAndWritesAsItDid()
    {
        foreach (var layout in new[] { DeskArrangement.Standard, "clock:bl:1:-18 claude:bl:1:-18 meters:tr:1.2", "clock:tl@main telemetry:tr:0.8" })
        {
            var a = new DeskArrangement(layout);
            Assert.All(a.Pieces, p => Assert.Equal(DeskMeterStyle.Bar, p.Style));
            Assert.Equal(layout, a.Layout);
        }
        var b = new DeskArrangement("meters:tr:1:0:bar");
        Assert.Equal("meters:tr", b.Layout);
    }

    [Fact]
    public void AStyleHoldsTheSizeAndTiltPlacesSoABuildBeforeItReadsBothAndDrawsBars()
    {
        var a = new DeskArrangement();
        a.Place("meters", DeskAnchor.TR);
        a.SetStyle("meters", DeskMeterStyle.Ring);
        a.SetScale("meters", 1.3);
        string word = a.Layout;
        Assert.Equal("meters:tr:1.3:0:ring", word);
        // What a 3.8 build before styles (and one before the fold) reads: the size from the third field, the tilt from the fourth.
        var bits = word.Split(':');
        Assert.Equal(1.3, double.Parse(bits[2], CultureInfo.InvariantCulture));
        Assert.Equal(0, double.Parse(bits[3], CultureInfo.InvariantCulture));
        // The monitor suffix still ends the word.
        a.SetMonitor("meters", DeskMonitors.Main);
        Assert.Equal("meters:tr:1.3:0:ring@main", a.Layout);
        Assert.Equal(DeskMeterStyle.Ring, new DeskArrangement(a.Layout).Placement("meters")!.Style);
    }

    [Theory]
    [InlineData("meters:tr:1:0:donut")]
    [InlineData("meters:tr:1:0:RING")]
    [InlineData("clock:bl:1:0:ring")]
    [InlineData("launcher:tr:1:0:slim")]
    public void AnUnknownStyleOrAStyleOnAPieceWithoutOneIsABar(string word)
    {
        var p = Assert.Single(new DeskArrangement(word).Pieces);
        Assert.Equal(DeskMeterStyle.Bar, p.Style);
    }

    [Fact]
    public void AStyleIsThePiecesOwnAndMovesWithIt()
    {
        var a = new DeskArrangement("clock:bl claude:bl:1:0:slim meters:tr:1:0:ring");
        a.SetStyle("clock", DeskMeterStyle.Ring);       // no style: stays a bar
        Assert.Equal(DeskMeterStyle.Bar, a.Placement("clock")!.Style);
        a.SetStyle("watchers", DeskMeterStyle.Ring);    // not placed: nothing
        Assert.Null(a.Placement("watchers"));
        a.Place("meters", DeskAnchor.BL);                // into a stack: keeps its style, takes the stack's slant
        Assert.Equal(DeskMeterStyle.Ring, a.Placement("meters")!.Style);
        a.Move("claude", "clock");
        Assert.Equal(DeskMeterStyle.Slim, a.Placement("claude")!.Style);
        a.Put("meters", DeskAnchor.TR, null);
        Assert.Equal(DeskMeterStyle.Ring, a.Placement("meters")!.Style);
        a.SetTilt("meters", 18);                          // one slant per stack, a style per piece
        Assert.Equal("meters:tr:1:18:ring", a.Layout.Split(' ').Single(w => w.StartsWith("meters", StringComparison.Ordinal)));
    }

    [Fact]
    public void AReviewCountsAChangedStyleAsAChangedPiece()
    {
        var pieces = DeskMonitors.ReviewPieces(new DeskArrangement("clock:bl meters:tr"), new DeskArrangement("clock:bl meters:tr:1:0:slim"));
        Assert.Equal(["meters"], pieces.Select(p => p.Widget));
    }

    // ---- the design contract ----

    [Fact]
    public void ADesignCarriesAStylePerPieceAndAppliesIt()
    {
        var p = D("""{"layout":{"meters":{"anchor":"tr","style":"ring"},"telemetry":{"style":" Ticks "},"claude":{"style":"slim"}}}""");
        Assert.Equal(DeskMeterStyle.Ring, p.Layout!.Single(x => x.Piece == "meters").Style);
        Assert.Equal(DeskMeterStyle.Ticks, p.Layout!.Single(x => x.Piece == "telemetry").Style);
        Assert.Equal("ring", (string?)p.ToJson()["layout"]!["meters"]!["style"]);

        var current = DeskState.Current(DeskSettings.Default with { Layout = "clock:bl claude:bl meters:tl telemetry:tr" },
            DeskInkSettings.Default, LauncherModel.Empty, null);
        var merged = DeskDesign.Merge(current, p);
        var layout = new DeskArrangement(DeskDesignApply.Settings(DeskSettings.Default, DeskInkSettings.Default, LauncherModel.Empty, merged).Desk.Layout);
        Assert.Equal(DeskMeterStyle.Ring, layout.Placement("meters")!.Style);
        Assert.Equal(DeskAnchor.TR, layout.Placement("meters")!.Anchor);
        Assert.Equal(DeskMeterStyle.Ticks, layout.Placement("telemetry")!.Style);
        Assert.Equal(DeskMeterStyle.Slim, layout.Placement("claude")!.Style);
        Assert.Equal(DeskMeterStyle.Bar, layout.Placement("clock")!.Style);
    }

    [Theory]
    [InlineData("""{"layout":{"clock":{"style":"ring"}}}""", "layout.clock.style")]
    [InlineData("""{"layout":{"launcher":{"anchor":"tr","style":"slim"}}}""", "layout.launcher.style")]
    [InlineData("""{"layout":{"meters":{"style":"donut"}}}""", "layout.meters.style")]
    [InlineData("""{"layout":{"meters":{"style":3}}}""", "layout.meters.style")]
    [InlineData("""{"layout":{"meters":{"look":"ring"}}}""", "layout.meters")]
    public void AStyleOnAPieceWithoutOneOrAnUnknownStyleIsRefused(string json, string field)
    {
        var parsed = Parse(json);
        Assert.Null(parsed.Design);
        Assert.Contains(parsed.Reasons, r => r.Field == field);
    }

    [Fact]
    public void TheRefusalNamesThePiecesAndTheWords()
    {
        Assert.Contains("claude, meters, telemetry", Parse("""{"layout":{"clock":{"style":"ring"}}}""").Reasons.Single().Message);
        Assert.Contains("bar, slim, ring, ticks", Parse("""{"layout":{"meters":{"style":"donut"}}}""").Reasons.Single().Message);
    }

    [Fact]
    public void AHiddenPieceHasNoStyle() =>
        Assert.Null(D("""{"layout":{"meters":{"hidden":true,"style":"ring"}}}""").Layout!.Single().Style);

    [Fact]
    public void TheReviewNamesAStyleChange()
    {
        var current = DeskState.Current(DeskSettings.Default with { Layout = "clock:bl meters:tr" }, DeskInkSettings.Default, LauncherModel.Empty, null);
        var merged = DeskDesign.Merge(current, D("""{"layout":{"meters":{"style":"ring"},"telemetry":{"anchor":"tr","style":"slim"}}}"""));
        var lines = DeskDesign.Diff(current, merged, SettingsNames.DeskPieceName);
        Assert.Contains($"{SettingsNames.DeskPieceName("meters")}: ring", lines);
        Assert.Contains($"{SettingsNames.DeskPieceName("telemetry")}: shown at top right, slim", lines);
        // Saying bar to a piece that is a bar changes nothing.
        Assert.Empty(DeskDesign.Diff(current, DeskDesign.Merge(current, D("""{"layout":{"meters":{"style":"bar"}}}"""))));
    }

    [Fact]
    public void ADesignWithBarsIsTheSameLookAsOneSavedBeforeStyles()
    {
        var before = D("""{"layout":{"meters":{"anchor":"tr","size":1,"monitor":"all"}},"font":"Segoe UI"}""");
        var bars = before with { Layout = [new DeskPieceLayout("meters", DeskAnchor.TR, 1, false, "all", 0, DeskMeterStyle.Bar)] };
        var rings = before with { Layout = [new DeskPieceLayout("meters", DeskAnchor.TR, 1, false, "all", 0, DeskMeterStyle.Ring)] };
        Assert.Equal(DeskDesigns.Canonical(before), DeskDesigns.Canonical(bars));
        Assert.NotEqual(DeskDesigns.Canonical(before), DeskDesigns.Canonical(rings));
        var saved = new SavedDesign("a1", "Bars", false, DateTimeOffset.UnixEpoch, before);
        Assert.Same(saved, DeskDesigns.FindSame([saved], bars));
        Assert.Null(DeskDesigns.FindSame([saved], rings));
    }

    [Fact]
    public void GetDeskReportsEveryStyleAndTheAllowedValues()
    {
        var current = DeskState.Current(DeskSettings.Default with { Layout = "clock:bl claude:bl meters:tr:1:0:ring" },
            DeskInkSettings.Default, LauncherModel.Empty, null);
        var state = DeskState.Build(current, [], null, null, null, [], false, "3.8.0");
        var layout = state["design"]!["layout"]!;
        Assert.Equal("ring", (string?)layout["meters"]!["style"]);
        Assert.Equal("bar", (string?)layout["claude"]!["style"]);
        Assert.Null(layout["clock"]!["style"]);
        Assert.Null(layout["telemetry"]!["style"]);   // hidden: no style
        var limits = state["limits"]!["piece_style"]!;
        Assert.Equal(["claude", "meters", "telemetry"], limits["pieces"]!.AsArray().Select(n => (string)n!));
        Assert.Equal(["bar", "slim", "ring", "ticks"], limits["values"]!.AsArray().Select(n => (string)n!));
        Assert.Equal("bar", (string?)limits["default"]);
        // What get_desk reports parses back as a design, so Claude can send it straight back.
        Assert.NotNull(DeskDesign.Parse(state["design"]!.DeepClone()).Design);
    }

    // ---- what the view draws by ----

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.004, 1)]
    [InlineData(0.34, 3)]
    [InlineData(0.35, 4)]
    [InlineData(0.93, 9)]
    [InlineData(1, 10)]
    [InlineData(1.4, 10)]
    [InlineData(double.NaN, 0)]
    public void TicksLightToTheNearestCellAndAnyReadingLightsOne(double fill, int lit) =>
        Assert.Equal(lit, DeskMeters.LitTicks(fill));

    [Theory]
    [InlineData(TierModel.FiveHour, "Session")]
    [InlineData(TierModel.SevenDay, "Week")]
    [InlineData(TierModel.SevenDaySonnet, "Sonnet")]
    [InlineData(TierModel.ExtraUsage, "Capped Extra Usage")]
    public void ARingCarriesAShortName(string key, string name) => Assert.Equal(name, DeskMeters.ShortLabel(key));

    [Fact]
    public void TheClaudeLinesCompactFormIsTwoShortRows()
    {
        var now = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        string iso = now.AddHours(3).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var both = new DeskUsage([new DeskTier(TierModel.FiveHour, 7.9, iso), new DeskTier(TierModel.SevenDay, 63, null)], now, false, false, null);
        var rows = DeskClaudeText.Compact(both, TimeZoneInfo.Utc)!;
        Assert.Equal(["claude   7% session", "resets 3:00   63% week"], rows);
        Assert.True(rows.Max(r => r.Length) < DeskClaudeText.Line(both, TimeZoneInfo.Utc)!.Length * 0.65);

        Assert.Equal(["claude", "63% week"], DeskClaudeText.Compact(both with { Tiers = [new DeskTier(TierModel.SevenDay, 63, null)] })!);
        Assert.Equal(["Work   7% session"], DeskClaudeText.Compact(both with { Tiers = [new DeskTier(TierModel.FiveHour, 7, null)], Account = "Work" })!);
        Assert.Equal(["claude", "sign in again in Sanduhr"], DeskClaudeText.Compact(both with { SignInNeeded = true })!);
        Assert.Null(DeskClaudeText.Compact(both with { SignInNeeded = true, SignInReminder = false }));
        Assert.Null(DeskClaudeText.Compact(DeskUsage.Empty));
    }

    [Fact]
    public void OnlyTheMeterLikePiecesHaveAStyle()
    {
        Assert.Equal(["claude", "meters", "telemetry"], DeskMeterStyles.Pieces);
        Assert.All(DeskArrangement.Known.Where(w => !DeskMeterStyles.Pieces.Contains(w)), w => Assert.False(DeskMeterStyles.Supports(w)));
        Assert.All(DeskMeterStyles.All, s => Assert.Equal(s, DeskMeterStyles.Parse(DeskMeterStyles.Word(s))));
        Assert.Equal(["Bars", "Slim", "Rings", "Ticks"], DeskMeterStyles.All.Select(DeskMeterStyles.Name));
    }
}
