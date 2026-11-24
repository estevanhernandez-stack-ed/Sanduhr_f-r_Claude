using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Fitting a ready-made look to a PC (5.0 spec 5, sections 4 and 5): the font steps, the ink ladder, that a fitted look
/// always passes the validator, and when the Desk still counts as an untouched look.</summary>
public class DeskLookFitTests
{
    private static readonly string[] Fonts = ["Bahnschrift", "Consolas", "Georgia", "Segoe UI"];

    private static WallpaperOverview Flat(double b, Dictionary<DeskAnchor, double>? overrides = null) => DeskDesignValidatorTests.Flat(b, overrides);

    private static JsonObject Design(JsonObject o) => (JsonObject)o["design"]!;

    /// <summary>A Desk as get_desk reports it: the default layout with the user's own labelled Launcher button.</summary>
    private static readonly LauncherModel UsersLauncher =
        new(LauncherOrientation.Row, [LauncherButton.Create(LauncherKind.Link, "https://example.com", label: "Home")!]);

    private static DeskDesign Desk(DeskSettings? desk = null, DeskInkSettings? ink = null, bool follows = false) =>
        DeskState.Current(desk ?? DeskSettings.Default, ink ?? DeskInkSettings.Default, UsersLauncher, null, followsTheme: follows);

    private static DeskDesignContext Ctx(WallpaperOverview? wall, DeskDesign? current = null, IReadOnlyCollection<string>? fonts = null) =>
        new(fonts ?? Fonts, wall, null, current ?? Desk());

    /// <summary>The Desk after the look is applied over <paramref name="desk"/>, the way the App writes it: the fitted design merged,
    /// the layout in the file's order, through the same settings a design writes, read back as get_desk would.</summary>
    private static DeskDesign Applied(DeskLook look, DeskLookFitResult fit, DeskSettings? desk = null, DeskInkSettings? ink = null)
    {
        desk ??= DeskSettings.Default;
        ink ??= DeskInkSettings.Default;
        var before = DeskState.Current(desk, ink, UsersLauncher, null);
        var merged = DeskDesign.Merge(before, fit.Design) with { Layout = [.. DeskLookFit.LayoutOver(look, before)] };
        var next = DeskDesignApply.Settings(desk, ink, UsersLauncher, merged);
        return DeskState.Current(next.Desk, next.Ink, next.Launcher, null, followsTheme: next.FollowsTheme ?? false);
    }

    // ---- the ink ladder ----

    [Fact]
    public void OnAWallpaperItReadsOverTheLookIsUsedAsDesigned()
    {
        var look = DeskLookTests.Look();
        var fit = DeskLookFit.Fit(look, Ctx(Flat(0.03)));
        Assert.Empty(fit.Notes);
        Assert.Equal(look.Design.ToJson().ToJsonString(), fit.Design.ToJson().ToJsonString());
    }

    [Fact]
    public void TheThreeStepsEachHappenSomewhereAndEveryFitPassesTheValidator()
    {
        var look = DeskLookTests.Look();
        var seen = new HashSet<string>();
        for (int i = 1; i < 50; i++)
        {
            double b = i / 50.0;
            var ctx = Ctx(Flat(b));
            var fit = DeskLookFit.Fit(look, ctx);
            var verdict = DeskDesign.Validate(fit.Design, ctx);
            Assert.True(verdict.Ok, $"brightness {b}: {string.Join("; ", verdict.Reasons)}");

            if (fit.Notes.Count == 0)
            {
                seen.Add("as designed");
                Assert.Equal(false, fit.Design.Plate);
                Assert.Equal(DeskInkKind.Custom, fit.Design.Ink!.Kind);
            }
            else if (fit.Notes.SequenceEqual([DeskLookFit.PlateNote]))
            {
                seen.Add("plate");
                Assert.Equal(true, fit.Design.Plate);
                Assert.Equal(DeskInkKind.Custom, fit.Design.Ink!.Kind);
            }
            else
            {
                Assert.Equal([DeskLookFit.AutoInkNote], fit.Notes);
                seen.Add("auto");
                Assert.Equal(DeskInkKind.Auto, fit.Design.Ink!.Kind);
                Assert.Equal(false, fit.Design.Plate);   // the plate stays as the look designed it
            }
        }
        Assert.Equal(["as designed", "auto", "plate"], seen.Order());
    }

    [Fact]
    public void OnePieceOverABrightPatchIsEnoughToFit()
    {
        var look = DeskLookTests.Look();   // the clock is at tc
        var ctx = Ctx(Flat(0.03, new() { [DeskAnchor.TC] = 0.95 }));
        var fit = DeskLookFit.Fit(look, ctx);
        Assert.NotEmpty(fit.Notes);
        Assert.True(DeskDesign.Validate(fit.Design, ctx).Ok);
    }

    [Fact]
    public void TwoMonitorsOfOppositeBrightnessAreJudgedOnTheWorse()
    {
        var look = DeskLookTests.Look();
        var dark = Flat(0.03).Monitors[0];
        var bright = Flat(0.95).Monitors[0] with { Index = 1, Primary = false };
        var ctx = Ctx(new WallpaperOverview([], 0.5, [dark, bright]));
        var fit = DeskLookFit.Fit(look, ctx);
        Assert.NotEmpty(fit.Notes);
        Assert.True(DeskDesign.Validate(fit.Design, ctx).Ok);
    }

    [Fact]
    public void ALookWhoseInkIsAutomaticIsNeverAdjusted()
    {
        var look = DeskLookTests.Look(o => { Design(o)["ink"] = "auto"; });
        foreach (double b in new[] { 0.02, 0.3, 0.5, 0.7, 0.98 })
        {
            var fit = DeskLookFit.Fit(look, Ctx(Flat(b)));
            Assert.Empty(fit.Notes);
            Assert.Equal(DeskInkKind.Auto, fit.Design.Ink!.Kind);
            Assert.Equal(false, fit.Design.Plate);
        }
    }

    [Fact]
    public void ALookThatAlreadyHasItsPlateGoesStraightToAutomaticInk()
    {
        var look = DeskLookTests.Look(o => Design(o)["plate"] = true);
        var ctx = Ctx(Flat(0.98));
        var fit = DeskLookFit.Fit(look, ctx);
        Assert.Equal([DeskLookFit.AutoInkNote], fit.Notes);
        Assert.Equal(true, fit.Design.Plate);
        Assert.True(DeskDesign.Validate(fit.Design, ctx).Ok);
    }

    [Fact]
    public void WithNoWallpaperSummaryTheLookIsUsedAsDesigned()
    {
        var look = DeskLookTests.Look();
        Assert.Empty(DeskLookFit.Fit(look, Ctx(null)).Notes);
        Assert.Empty(DeskLookFit.Fit(look, Ctx(new WallpaperOverview([], 0.9, []))).Notes);
    }

    // ---- the font ----

    [Fact]
    public void AnInstalledFontIsKept() => Assert.Equal("Bahnschrift", DeskLookFit.Fit(DeskLookTests.Look(), Ctx(null)).Design.Font!.Family);

    [Fact]
    public void AMissingFontTakesTheFallbackAndSaysSo()
    {
        var fit = DeskLookFit.Fit(DeskLookTests.Look(), Ctx(null, fonts: ["Segoe UI", "Georgia"]));
        Assert.Equal("Segoe UI", fit.Design.Font!.Family);
        Assert.Equal(["Uses Segoe UI on this PC."], fit.Notes);
    }

    [Fact]
    public void WithNeitherFontTheLookTakesTheSystemFontAndNeverWaitsOnOne()
    {
        var ctx = Ctx(Flat(0.03), fonts: ["Wingdings"]);
        var fit = DeskLookFit.Fit(DeskLookTests.Look(), ctx);
        Assert.Equal("", fit.Design.Font!.Family);
        Assert.Equal(["Uses the system font on this PC."], fit.Notes);
        Assert.Equal(DeskDesignStatus.Ok, DeskDesign.Validate(fit.Design, ctx).Status);
    }

    [Fact]
    public void ABundledFontIsNeverReplaced()
    {
        var look = DeskLookTests.Look(o => Design(o)["font"] = "EsteFont Pro");
        var fit = DeskLookFit.Fit(look, Ctx(null, fonts: ["Wingdings"]));
        Assert.Equal("EsteFont Pro", fit.Design.Font!.Family);
        Assert.Empty(fit.Notes);
    }

    [Fact]
    public void WhenTheFontListIsUnknownTheFontIsKept()
    {
        var fit = DeskLookFit.Fit(DeskLookTests.Look(), new DeskDesignContext(null, null, null, Desk()));
        Assert.Equal("Bahnschrift", fit.Design.Font!.Family);
        Assert.Empty(fit.Notes);
    }

    [Fact]
    public void TheFontNoteComesBeforeTheInkNote()
    {
        var fit = DeskLookFit.Fit(DeskLookTests.Look(), Ctx(Flat(0.98), fonts: ["Segoe UI"]));
        Assert.Equal(["Uses Segoe UI on this PC.", DeskLookFit.AutoInkNote], fit.Notes);
    }

    // ---- the monitor a piece is on ----

    [Fact]
    public void APieceOnAnUnpluggedMonitorKeepsItAndTheLookStillValidates()
    {
        var look = DeskLookTests.Look();
        var desk = DeskSettings.Default with { Layout = new DeskArrangement([new DeskPlacement("clock", DeskAnchor.BL, 1, @"\\?\DISPLAY#GONE#1")]).Layout };
        var current = DeskState.Current(desk, DeskInkSettings.Default, UsersLauncher, null);
        var connected = new[] { new DeskMonitorInfo(@"\\?\DISPLAY#HERE#1", "1920×1080, main", 1920, 1080, true, 0, 0) };
        var ctx = new DeskDesignContext(Fonts, Flat(0.03), null, current, connected);

        var fit = DeskLookFit.Fit(look, ctx);
        Assert.True(DeskDesign.Validate(fit.Design, ctx).Ok);   // the fitted design names no monitor, so nothing is refused

        var layout = DeskLookFit.LayoutOver(look, current);
        Assert.True(DeskMonitors.Same(@"\\?\DISPLAY#GONE#1", layout.Single(p => p.Piece == "clock").Monitor));
        Assert.Null(layout.Single(p => p.Piece == "message").Monitor);   // hidden before: it comes up on every monitor
    }

    [Fact]
    public void TheLayoutToWriteIsTheFilesOrderThenWhateverTheLookDoesNotName()
    {
        var look = DeskLookTests.Look();
        var current = Desk() with { Layout = [.. Desk().Layout!.Where(p => p.Piece != "meetings"), new DeskPieceLayout("meetings", DeskAnchor.TR, 1, false, "all")] };
        var layout = DeskLookFit.LayoutOver(look, current);
        Assert.Equal(look.Design.Layout!.Select(p => p.Piece), layout.Take(look.Design.Layout!.Count).Select(p => p.Piece));
        Assert.Equal("meetings", layout[^1].Piece);
        Assert.Equal(DeskAnchor.TR, layout[^1].Anchor);
        Assert.Equal(look.Design.Layout!.Count, DeskLookFit.LayoutOver(look, null).Count);
    }

    // ---- is the Desk this look ----

    [Theory]
    [InlineData(0.03)]   // as designed
    [InlineData(0.25)]   // one of the fitted steps
    [InlineData(0.98)]   // automatic ink
    public void AnAppliedLookIsRecognisedWhateverTheFitWas(double brightness)
    {
        var look = DeskLookTests.Look();
        var fit = DeskLookFit.Fit(look, Ctx(Flat(brightness)));
        var now = Applied(look, fit);
        Assert.True(DeskLookFit.IsLook(look, now, Fonts));
        Assert.True(DeskLookFit.IsExactly(look, fit, now));
        Assert.Same(look, DeskLookFit.Untouched([DeskLookTests.Look(o => { o["id"] = "other"; Design(o)["glow"] = false; Design(o)["shadow"] = true; }), look], now, Fonts));
    }

    [Fact]
    public void AfterTheWallpaperChangesTheDeskIsStillTheLookButNoLongerExactlyItsNewFit()
    {
        var look = DeskLookTests.Look();
        var now = Applied(look, DeskLookFit.Fit(look, Ctx(Flat(0.03))));
        var refit = DeskLookFit.Fit(look, Ctx(Flat(0.98), now));
        Assert.True(DeskLookFit.IsLook(look, now, Fonts));
        Assert.False(DeskLookFit.IsExactly(look, refit, now));
    }

    [Fact]
    public void TheFallbackFontStillCountsAndSoDoesTheSystemFontWhenBothAreMissing()
    {
        var look = DeskLookTests.Look();
        string[] some = ["Segoe UI"], none = ["Wingdings"];
        Assert.True(DeskLookFit.IsLook(look, Applied(look, DeskLookFit.Fit(look, Ctx(null, fonts: some))), some));
        Assert.True(DeskLookFit.IsLook(look, Applied(look, DeskLookFit.Fit(look, Ctx(null, fonts: none))), none));
        // The system font is the user's own choice on a PC that has the look's font.
        Assert.False(DeskLookFit.IsLook(look, Applied(look, DeskLookFit.Fit(look, Ctx(null, fonts: none))), Fonts));
    }

    [Fact]
    public void TheUsersLauncherMessagesAndSourcesNeverCount()
    {
        var look = DeskLookTests.Look();
        var now = Applied(look, DeskLookFit.Fit(look, Ctx(Flat(0.03))));
        var others = now with
        {
            Launcher = LauncherModel.Empty,
            Messages = new DeskMessagesChange(["hello"], MessageProposalMode.Add),
            Weather = new DeskWeatherChange("Home"),
            Layout = [.. now.Layout!.Select(p => p.IsHidden ? p : p with { Monitor = "main" })],
        };
        Assert.True(DeskLookFit.IsLook(look, others, Fonts));
    }

    [Fact]
    public void AnyChangeToWhatALookCarriesMakesItTheUsersOwnDesk()
    {
        var look = DeskLookTests.Look();
        var now = Applied(look, DeskLookFit.Fit(look, Ctx(Flat(0.03))));
        DeskDesign With(Func<DeskPieceLayout, DeskPieceLayout> edit, string piece) =>
            now with { Layout = [.. now.Layout!.Select(p => p.Piece == piece ? edit(p) : p)] };

        Assert.False(DeskLookFit.IsLook(look, With(p => p with { Anchor = DeskAnchor.TL }, "clock"), Fonts));       // moved
        Assert.False(DeskLookFit.IsLook(look, With(p => p with { Size = 1.5 }, "clock"), Fonts));                    // resized
        Assert.False(DeskLookFit.IsLook(look, With(p => p with { Tilt = 10 }, "claude"), Fonts));                    // slanted
        Assert.False(DeskLookFit.IsLook(look, With(p => new DeskPieceLayout(p.Piece, null, null, true), "claude"), Fonts));   // hidden
        Assert.False(DeskLookFit.IsLook(look, With(p => new DeskPieceLayout(p.Piece, DeskAnchor.TR, 1, false), "weather"), Fonts));   // shown
        Assert.False(DeskLookFit.IsLook(look, now with { Ink = new DeskInkChoice(DeskInkKind.Custom, ["#ff0000"]) }, Fonts));
        Assert.False(DeskLookFit.IsLook(look, now with { Ink = DeskInkChoice.Of(DeskInkKind.Light) }, Fonts));
        Assert.False(DeskLookFit.IsLook(look, now with { Font = new DeskFontChoice("Georgia") }, Fonts));
        Assert.False(DeskLookFit.IsLook(look, now with { Glow = new DeskGlowChoice(true, 0.9) }, Fonts));
        Assert.False(DeskLookFit.IsLook(look, now with { Glow = DeskGlowChoice.Off }, Fonts));
        Assert.False(DeskLookFit.IsLook(look, now with { Glow = DeskGlowChoice.Off, Shadow = true }, Fonts));
        Assert.False(DeskLookFit.IsLook(look, now with { FollowsTheme = true }, Fonts));
        Assert.True(DeskLookFit.IsLook(look, now with { Plate = true }, Fonts));    // the plate is the fit's to change
        Assert.True(DeskLookFit.IsLook(look, now with { Ink = DeskInkChoice.Of(DeskInkKind.Auto) }, Fonts));
    }

    [Fact]
    public void AStackTheUserReorderedIsTheirOwnDesk()
    {
        var look = DeskLookTests.Look();   // clock above message at tc
        var now = Applied(look, DeskLookFit.Fit(look, Ctx(Flat(0.03))));
        var clock = now.Layout!.Single(p => p.Piece == "clock");
        var message = now.Layout!.Single(p => p.Piece == "message");
        var swapped = now with { Layout = [.. now.Layout!.Select(p => p.Piece == "clock" ? message : p.Piece == "message" ? clock : p)] };
        Assert.False(DeskLookFit.IsLook(look, swapped, Fonts));
    }

    [Fact]
    public void ADeskThatIsNoLookIsNone()
    {
        Assert.Null(DeskLookFit.Untouched([DeskLookTests.Look()], Desk(), Fonts));
        Assert.False(DeskLookFit.IsLook(DeskLookTests.Look(), new DeskDesign(), Fonts));
    }

    // ---- what a shown piece needs ----

    [Fact]
    public void AShownPieceWithNothingToDrawSaysWhatItNeeds()
    {
        var look = DeskLookTests.Look(o =>
        {
            var layout = (JsonObject)Design(o)["layout"]!;
            layout["weather"] = JsonNode.Parse("""{"anchor":"tr","size":1}""");
            layout["watchers"] = JsonNode.Parse("""{"anchor":"mr","size":1}""");
        });
        var needs = DeskLookFit.Needs(look, weatherHasPlace: false, launcherHasButtons: false, hasWatcher: false);
        Assert.Equal([new DeskLookNeed("weather", "Weather needs a place."), new DeskLookNeed("launcher", "The Launcher needs buttons."), new DeskLookNeed("watchers", "Watchers needs a watcher.")], needs);
        Assert.Empty(DeskLookFit.Needs(look, true, true, true));
        // A piece the look hides needs nothing.
        Assert.Equal(["launcher"], DeskLookFit.Needs(DeskLookTests.Look(), false, false, false).Select(n => n.Piece));
    }
}
