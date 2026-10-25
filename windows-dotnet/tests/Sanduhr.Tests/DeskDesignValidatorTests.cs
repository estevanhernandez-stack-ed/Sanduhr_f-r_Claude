using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>DeskDesign.Validate: the rules that need the machine (fonts, the Start menu, the wallpaper's contrast) on top of
/// the shape rules DeskDesignTests covers.</summary>
public class DeskDesignValidatorTests
{
    private const string AppId = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";

    private static JsonObject Obj(string json) => (JsonObject)JsonNode.Parse(json)!;

    /// <summary>A one-monitor wallpaper whose every anchor has this brightness.</summary>
    internal static WallpaperOverview Flat(double brightness, Dictionary<DeskAnchor, double>? overrides = null)
    {
        var readings = DeskAnchors.All
            .Select(a => new AnchorReading(a, overrides is not null && overrides.TryGetValue(a, out var b) ? b : brightness, 0))
            .ToList();
        var summary = new WallpaperSummary([new DominantColor("#808080", 1)], brightness, readings);
        return new WallpaperOverview(summary.Dominant, brightness, [new WallpaperMonitorSummary(0, true, 2560, 1440, summary)]);
    }

    private static DeskDesign Current(string json) => DeskDesign.Parse(Obj(json)).Design!;

    private static string Fields(DeskValidation v) => string.Join("|", v.Reasons.Select(r => r.Field));

    // ---- the font rule ----

    [Fact]
    public void AnInstalledFontIsOk_ByAnyCase()
    {
        var ctx = new DeskDesignContext(InstalledFonts: ["Caveat", "Segoe UI"]);
        Assert.True(DeskDesign.Validate(Obj("""{"font":"caveat"}"""), ctx).Ok);
    }

    [Fact]
    public void ABundledFontIsOkEvenWhenNotInstalled()
    {
        var ctx = new DeskDesignContext(InstalledFonts: []);
        Assert.True(DeskDesign.Validate(Obj("""{"font":"EsteFont Pro"}"""), ctx).Ok);
        Assert.True(DeskDesign.Validate(Obj("""{"font":"EsteFont 26"}"""), ctx).Ok);
    }

    [Fact]
    public void TheSystemFontIsAlwaysOk()
    {
        Assert.True(DeskDesign.Validate(Obj("""{"font":""}"""), new DeskDesignContext(InstalledFonts: [])).Ok);
    }

    [Fact]
    public void AMissingFontNeedsTheFont_AndCarriesItsLink()
    {
        var ctx = new DeskDesignContext(InstalledFonts: ["Segoe UI"]);
        var v = DeskDesign.Validate(Obj("""{"font":{"family":"Caveat","link":"https://fonts.google.com/specimen/Caveat"},"plate":true}"""), ctx);
        Assert.Equal(DeskDesignStatus.NeedsFont, v.Status);
        Assert.Equal("needs_font", v.StatusWord);
        var font = Assert.Single(v.Fonts);
        Assert.Equal("Caveat", font.Family);
        Assert.Equal("https://fonts.google.com/specimen/Caveat", font.Link);
        Assert.NotNull(v.Design);   // valid: it just waits for the font
        Assert.Empty(v.Reasons);
    }

    [Fact]
    public void AMissingFontWithoutALinkHasANullLink()
    {
        var v = DeskDesign.Validate(Obj("""{"font":"Caveat"}"""), new DeskDesignContext(InstalledFonts: []));
        Assert.Equal(DeskDesignStatus.NeedsFont, v.Status);
        Assert.Null(v.Fonts[0].Link);
        Assert.Null(v.FontsJson()[0]!["link"]);
    }

    [Fact]
    public void WithoutTheFontListTheFontRuleIsSkipped()
    {
        Assert.True(DeskDesign.Validate(Obj("""{"font":"Caveat"}"""), DeskDesignContext.Unknown).Ok);
    }

    [Fact]
    public void ARejectionOutranksANeedForAFont()
    {
        var v = DeskDesign.Validate(Obj("""{"font":"Caveat","plate":"yes"}"""), new DeskDesignContext(InstalledFonts: []));
        Assert.Equal(DeskDesignStatus.Rejected, v.Status);
        Assert.Empty(v.Fonts);
    }

    // ---- the Start menu rule ----

    [Fact]
    public void AnAppMustBeInTheStartMenuList()
    {
        var ctx = new DeskDesignContext(StartMenuApps: [AppId]);
        Assert.True(DeskDesign.Validate(Obj($$$"""{"launcher":{"buttons":[{"kind":"app","target":"{{{AppId}}}"}]}}"""), ctx).Ok);

        var v = DeskDesign.Validate(Obj($$$"""{"launcher":{"buttons":[{"kind":"link","target":"https://example.com"},{"kind":"app","target":"Some.Other_app!App"}]}}"""), ctx);
        Assert.Equal(DeskDesignStatus.Rejected, v.Status);
        Assert.Equal("launcher.buttons[1].target", Fields(v));
    }

    [Fact]
    public void ALinkNeedsNoMembership()
    {
        Assert.True(DeskDesign.Validate(Obj("""{"launcher":{"buttons":[{"kind":"link","target":"https://example.com"}]}}"""),
            new DeskDesignContext(StartMenuApps: [])).Ok);
    }

    [Fact]
    public void AGoodShapedAppIsStillRefusedWhenTheStartMenuDoesNotListIt()
    {
        // The shape is only the first gate; membership is the safety property.
        var v = DeskDesign.Validate(Obj("""{"launcher":{"buttons":[{"kind":"app","target":"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\\cmd.exe"}]}}"""),
            new DeskDesignContext(StartMenuApps: [AppId]));
        Assert.Equal(DeskDesignStatus.Rejected, v.Status);
    }

    [Fact]
    public void WithoutTheStartMenuListTheMembershipRuleIsSkipped()
    {
        Assert.True(DeskDesign.Validate(Obj($$$"""{"launcher":{"buttons":[{"kind":"app","target":"{{{AppId}}}"}]}}"""), DeskDesignContext.Unknown).Ok);
    }

    // ---- the contrast rule ----

    [Fact]
    public void WhiteInkOnADarkWallpaperPasses()
    {
        var ctx = new DeskDesignContext(Wallpaper: Flat(0.05));
        Assert.True(DeskDesign.Validate(Obj("""{"ink":"light","layout":{"clock":{"anchor":"bl"}}}"""), ctx).Ok);
    }

    [Fact]
    public void WhiteInkOnABrightWallpaperIsRefused_NamingThePieceAndAnchor()
    {
        var ctx = new DeskDesignContext(Wallpaper: Flat(0.8));
        var v = DeskDesign.Validate(Obj("""{"ink":"#ffffff","layout":{"clock":{"anchor":"bl"}}}"""), ctx);
        Assert.Equal(DeskDesignStatus.Rejected, v.Status);
        var reason = Assert.Single(v.Reasons);
        Assert.Equal("ink", reason.Field);
        Assert.Contains("clock", reason.Message);
        Assert.Contains("bl", reason.Message);
        Assert.Contains("1.2:1", reason.Message);
        Assert.Contains("3:1", reason.Message);
        Assert.Contains("darker ink", reason.Message);
    }

    [Fact]
    public void DarkInkOnADarkWallpaperIsRefusedAndTheFixIsALighterInk()
    {
        var ctx = new DeskDesignContext(Wallpaper: Flat(0.03));
        var v = DeskDesign.Validate(Obj("""{"ink":"dark","layout":{"clock":{"anchor":"bl"}}}"""), ctx);
        Assert.Equal(DeskDesignStatus.Rejected, v.Status);
        Assert.Contains("lighter ink", v.Reasons[0].Message);
    }

    [Fact]
    public void ThreeToOneIsTheEdge()
    {
        // Black ink (luminance 0) against a wallpaper of luminance L reads at (L + 0.05) / 0.05: 3:1 is L = 0.1.
        Assert.True(DeskDesign.Validate(Obj("""{"ink":"#000000","layout":{"clock":{"anchor":"bl"}}}"""), new DeskDesignContext(Wallpaper: Flat(0.101))).Ok);
        Assert.Equal(DeskDesignStatus.Rejected,
            DeskDesign.Validate(Obj("""{"ink":"#000000","layout":{"clock":{"anchor":"bl"}}}"""), new DeskDesignContext(Wallpaper: Flat(0.098))).Status);
    }

    [Fact]
    public void EveryStopOfAGradientMustPass()
    {
        var ctx = new DeskDesignContext(Wallpaper: Flat(0.4));
        // White reads at 2.3:1 against 0.4 and black at 9:1, so a gradient holding white fails and black alone passes.
        Assert.Equal(DeskDesignStatus.Rejected,
            DeskDesign.Validate(Obj("""{"ink":["#000000","#ffffff"],"layout":{"clock":{"anchor":"bl"}}}"""), ctx).Status);
        Assert.True(DeskDesign.Validate(Obj("""{"ink":["#000000","#111111"],"layout":{"clock":{"anchor":"bl"}}}"""), ctx).Ok);
    }

    [Fact]
    public void ThePlateIsWhatTheInkIsJudgedAgainstWhenItIsOn()
    {
        var ctx = new DeskDesignContext(Wallpaper: Flat(0.5));
        // White on 0.5 reads 1.9:1; behind a black plate at 30% it reads about 3.8:1.
        Assert.Equal(DeskDesignStatus.Rejected,
            DeskDesign.Validate(Obj("""{"ink":"light","plate":false,"layout":{"clock":{"anchor":"bl"}}}"""), ctx).Status);
        Assert.True(DeskDesign.Validate(Obj("""{"ink":"light","plate":true,"layout":{"clock":{"anchor":"bl"}}}"""), ctx).Ok);
    }

    [Fact]
    public void ADarkInkGetsAWhitePlate()
    {
        var ctx = new DeskDesignContext(Wallpaper: Flat(0.2));
        // Dark ink on 0.2 reads 3.9:1 alone; the white plate lifts the ground and it only gets better.
        var v = DeskDesign.Validate(Obj("""{"ink":"dark","plate":true,"layout":{"clock":{"anchor":"bl"}}}"""), ctx);
        Assert.True(v.Ok);
        Assert.True(DeskDesign.UnderPlate(0.2, darkInk: true) > 0.2);
        Assert.True(DeskDesign.UnderPlate(0.2, darkInk: false) < 0.2);
    }

    [Fact]
    public void ThePieceIsJudgedAtItsOwnAnchor()
    {
        var wall = Flat(0.05, new() { [DeskAnchor.TR] = 0.9 });
        var ctx = new DeskDesignContext(Wallpaper: wall);
        Assert.True(DeskDesign.Validate(Obj("""{"ink":"light","layout":{"clock":{"anchor":"bl"}}}"""), ctx).Ok);
        var v = DeskDesign.Validate(Obj("""{"ink":"light","layout":{"clock":{"anchor":"tr"}}}"""), ctx);
        Assert.Equal(DeskDesignStatus.Rejected, v.Status);
        Assert.Contains("tr", v.Reasons[0].Message);
    }

    [Fact]
    public void TheWorstMonitorDecides()
    {
        var dark = Flat(0.05).Monitors[0];
        var bright = Flat(0.9).Monitors[0] with { Index = 1, Primary = false };
        var wall = new WallpaperOverview([], 0.5, [dark, bright]);
        Assert.Equal(DeskDesignStatus.Rejected,
            DeskDesign.Validate(Obj("""{"ink":"light","layout":{"clock":{"anchor":"bl"}}}"""), new DeskDesignContext(Wallpaper: wall)).Status);
    }

    [Fact]
    public void AutomaticInkIsNotJudged()
    {
        var ctx = new DeskDesignContext(Wallpaper: Flat(0.5));
        Assert.True(DeskDesign.Validate(Obj("""{"ink":"auto","layout":{"clock":{"anchor":"bl"}}}"""), ctx).Ok);
    }

    [Fact]
    public void WithoutTheWallpaperTheContrastRuleIsSkipped()
    {
        Assert.True(DeskDesign.Validate(Obj("""{"ink":"#ffffff","layout":{"clock":{"anchor":"bl"}}}"""), DeskDesignContext.Unknown).Ok);
    }

    [Fact]
    public void AnInkChangeJudgesEveryPlacedPieceOfTheCurrentDesk()
    {
        var current = Current("""{"layout":{"clock":{"anchor":"bl"},"claude":{"anchor":"tr"}},"ink":"auto","plate":false}""");
        var wall = Flat(0.05, new() { [DeskAnchor.TR] = 0.9 });
        var ctx = new DeskDesignContext(Wallpaper: wall, Current: current);
        var v = DeskDesign.Validate(Obj("""{"ink":"light"}"""), ctx);
        Assert.Equal(DeskDesignStatus.Rejected, v.Status);
        Assert.Contains("claude", v.Reasons[0].Message);
    }

    [Fact]
    public void ALayoutChangeOnlyJudgesTheMovedPieces()
    {
        // The current Desk already has white ink over a bright top right (the user's own doing); moving the clock
        // somewhere dark must not be blamed on the meters sitting there.
        var current = Current("""{"layout":{"clock":{"anchor":"bl"},"meters":{"anchor":"tr"}},"ink":"light","plate":false}""");
        var ctx = new DeskDesignContext(Wallpaper: Flat(0.05, new() { [DeskAnchor.TR] = 0.9 }), Current: current);
        Assert.True(DeskDesign.Validate(Obj("""{"layout":{"clock":{"anchor":"br"}}}"""), ctx).Ok);
        Assert.Equal(DeskDesignStatus.Rejected, DeskDesign.Validate(Obj("""{"layout":{"clock":{"anchor":"tr"}}}"""), ctx).Status);
    }

    [Fact]
    public void ATurningOffThePlateCanBreakTheRule()
    {
        var current = Current("""{"layout":{"clock":{"anchor":"bl"}},"ink":"light","plate":true}""");
        var ctx = new DeskDesignContext(Wallpaper: Flat(0.5), Current: current);
        var v = DeskDesign.Validate(Obj("""{"plate":false}"""), ctx);
        Assert.Equal(DeskDesignStatus.Rejected, v.Status);
        Assert.Equal("plate", v.Reasons[0].Field);
    }

    [Fact]
    public void AThemeOnlyChangeIsNotBlamedOnTheInk()
    {
        var current = Current("""{"layout":{"clock":{"anchor":"bl"}},"ink":"light","plate":false}""");
        var ctx = new DeskDesignContext(Wallpaper: Flat(0.9), Current: current);
        Assert.True(DeskDesign.Validate(DeskDesignTests.Obj($$$"""{"theme":{{{DeskDesignTests.ObsidianTheme().ToJsonString()}}}}"""), ctx).Ok);
    }

    [Fact]
    public void ThemeInkIsJudgedByTheThemesTextColor()
    {
        var theme = DeskDesignTests.ObsidianTheme();   // light text
        var ctx = new DeskDesignContext(Wallpaper: Flat(0.9));
        var v = DeskDesign.Validate(new JsonObject { ["ink"] = "theme", ["theme"] = theme, ["layout"] = Obj("""{"clock":{"anchor":"bl"}}""") }, ctx);
        Assert.Equal(DeskDesignStatus.Rejected, v.Status);
    }

    [Fact]
    public void HiddenPiecesTheMessageAndTheGlowAreNotJudged()
    {
        var ctx = new DeskDesignContext(Wallpaper: Flat(0.9));
        Assert.True(DeskDesign.Validate(Obj("""{"ink":"light","layout":{"clock":{"hidden":true},"message":{"anchor":"tl"},"glow":{"anchor":"tc"}}}"""), ctx).Ok);
    }

    [Fact]
    public void TheLauncherIsJudgedOnlyWhenItHasLabels()
    {
        var ctx = new DeskDesignContext(Wallpaper: Flat(0.9));
        var plain = """{"ink":"light","layout":{"launcher":{"anchor":"bl"}},"launcher":{"buttons":[{"kind":"link","target":"https://example.com"}]}}""";
        var labelled = """{"ink":"light","layout":{"launcher":{"anchor":"bl"}},"launcher":{"buttons":[{"kind":"link","target":"https://example.com","label":"Home"}]}}""";
        Assert.True(DeskDesign.Validate(Obj(plain), ctx).Ok);
        Assert.Equal(DeskDesignStatus.Rejected, DeskDesign.Validate(Obj(labelled), ctx).Status);
    }

    [Fact]
    public void EveryFailingPieceIsNamedOnce()
    {
        var pieces = string.Join(",", new[] { "clock", "claude", "meters", "nowPlaying", "telemetry", "weather", "watchers", "meetings" }
            .Select((p, i) => $"\"{p}\":{{\"anchor\":\"{DeskAnchors.Word(DeskAnchors.All[i])}\"}}"));
        var v = DeskDesign.Validate(Obj($$$"""{"ink":"#ffffff","layout":{ {{{pieces}}} }}"""), new DeskDesignContext(Wallpaper: Flat(0.9)));
        Assert.Equal(8, v.Reasons.Count);
    }

    // ---- the gate in general ----

    [Fact]
    public void ADesignInMemoryMeetsTheSameRulesAsOneFromAFile()
    {
        var bad = new DeskDesign { Layout = [new DeskPieceLayout("clock", DeskAnchor.BL, 9, null)] };
        var v = DeskDesign.Validate(bad, DeskDesignContext.Unknown);
        Assert.Equal(DeskDesignStatus.Rejected, v.Status);
        Assert.Equal("layout.clock.size", Fields(v));
    }

    [Fact]
    public void ARunawayDesignIsRefusedBeforeItIsRead()
    {
        var big = new JsonObject { ["note"] = new string('x', DeskDesign.MaxJsonBytes) };
        var v = DeskDesign.Validate(big, DeskDesignContext.Unknown);
        Assert.Equal(DeskDesignStatus.Rejected, v.Status);
        Assert.Equal("design", v.Reasons[0].Field);
    }

    [Fact]
    public void ThemeWarningsAreCarriedOnAnAcceptedDesign()
    {
        var theme = DeskDesignTests.ObsidianTheme();
        theme["text_secondary"] = "#0b0b0e";
        var v = DeskDesign.Validate(new JsonObject { ["theme"] = theme }, DeskDesignContext.Unknown);
        Assert.True(v.Ok);
        Assert.NotEmpty(v.Warnings);
        Assert.NotEmpty(v.WarningsJson());
    }

    [Fact]
    public void ReasonsSerializeAsFieldAndMessage()
    {
        var v = DeskDesign.Validate(Obj("""{"plate":"x"}"""), DeskDesignContext.Unknown);
        var r = (JsonObject)v.ReasonsJson()[0]!;
        Assert.Equal("plate", (string?)r["field"]);
        Assert.False(string.IsNullOrEmpty((string?)r["message"]));
    }

    [Fact]
    public void TheValidatedDesignIsTheNormalizedOne()
    {
        var v = DeskDesign.Validate(Obj("""{"name":"  Dusk ","ink":"FA0","layout":{"clock":{"anchor":"BL","size":1.23}}}"""), DeskDesignContext.Unknown);
        Assert.True(v.Ok);
        Assert.Equal("""{"name":"Dusk","layout":{"clock":{"anchor":"bl","size":1.2}},"ink":"#ffaa00"}""", v.Design!.ToJson().ToJsonString());
    }

    // ---- theme match: a design that follows the widget theme is judged in the theme's look ----

    private static JsonObject ObsidianTheme(bool? plate = null)
    {
        var t = ThemeDraft.FromDefinition("obsidian", ThemeCatalog.BuiltIns["obsidian"]).ToJson();
        if (plate is { } p) t["desk"] = new JsonObject { ["plate"] = p };
        return t;
    }

    private static JsonObject Follow(JsonObject theme) => new() { ["follows_theme"] = true, ["theme"] = theme };

    [Fact]
    public void A_followed_design_is_judged_by_the_themes_ink_not_the_desks_own()
    {
        var ctx = new DeskDesignContext(Wallpaper: Flat(0.05), Current: Current("""{"layout":{"clock":{"anchor":"bl"}},"ink":"dark","plate":false}"""));
        Assert.False(DeskDesign.Validate(Obj("""{"plate":false}"""), ctx).Ok);   // the Desk's own dark ink on a dark wallpaper
        Assert.True(DeskDesign.Validate(Follow(ObsidianTheme()), ctx).Ok);         // #e8e4dc reads at 8:1
    }

    [Fact]
    public void The_guard_turns_the_plate_on_before_a_followed_look_is_refused()
    {
        // At 0.3 #e8e4dc reads at 2.4:1 bare but 4.4:1 over the plate: the Desk turns the plate on, so the design is fine.
        var ctx = new DeskDesignContext(Wallpaper: Flat(0.3), Current: Current("""{"layout":{"clock":{"anchor":"bl"}}}"""));
        Assert.True(DeskDesign.Validate(Follow(ObsidianTheme(plate: false)), ctx).Ok);
    }

    [Fact]
    public void A_followed_look_that_cannot_read_even_on_its_plate_is_refused_naming_follows_theme()
    {
        var ctx = new DeskDesignContext(Wallpaper: Flat(0.9), Current: Current("""{"layout":{"clock":{"anchor":"bl"}},"ink":"dark"}"""));
        var v = DeskDesign.Validate(Follow(ObsidianTheme()), ctx);
        Assert.Equal("follows_theme", Fields(v));
        Assert.Contains("with the plate on", v.Reasons[0].Message);
    }
}
