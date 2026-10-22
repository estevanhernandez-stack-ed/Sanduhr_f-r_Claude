using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>The Desk design contract (spec, the agent-designed Desk, A): reading, normalizing, merging, comparing.
/// Hostile inputs are here too: the design arrives from an agent.</summary>
public class DeskDesignTests
{
    internal static JsonObject Obj(string json) => (JsonObject)JsonNode.Parse(json)!;

    /// <summary>A theme that lints clean: the built-in Obsidian, as the Theme Studio would write it.</summary>
    internal static JsonObject ObsidianTheme() => ThemeDraft.FromDefinition("obsidian", ThemeCatalog.BuiltIns["obsidian"]).ToJson();

    private const string AppId = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";

    private static DeskParse Parse(string json) => DeskDesign.Parse(JsonNode.Parse(json));

    private static void AssertRefused(string json, string field)
    {
        var p = Parse(json);
        Assert.Null(p.Design);
        Assert.Contains(p.Reasons, r => r.Field == field);
    }

    // ---- reading ----

    [Fact]
    public void AnEmptyDesignIsValidAndChangesNothing()
    {
        var p = Parse("{}");
        Assert.NotNull(p.Design);
        Assert.Empty(p.Reasons);
        Assert.Equal("{}", p.Design!.ToJson().ToJsonString());
    }

    [Fact]
    public void AFullDesignRoundTripsThroughJson()
    {
        var json = new JsonObject
        {
            ["name"] = "Dusk",
            ["note"] = "Warm ink to match the sunset.",
            ["layout"] = Obj("""{"clock":{"anchor":"tr","size":1.4},"meters":{"hidden":true},"launcher":{"anchor":"bl"}}"""),
            ["font"] = Obj("""{"family":"Caveat","link":"https://fonts.google.com/specimen/Caveat"}"""),
            ["ink"] = JsonNode.Parse("""["#ffd08a","#ff7e5f"]"""),
            ["plate"] = true,
            ["shadow"] = false,
            ["launcher"] = Obj($$$"""{"orientation":"column","buttons":[{"kind":"app","target":"{{{AppId}}}","label":"Calc","glow":"#abc"},{"kind":"link","target":"https://example.com/a"}]}"""),
            ["messages"] = Obj("""{"lines":["{ink:#ffd08a} hello."],"mode":"replace"}"""),
            ["theme"] = ObsidianTheme(),
        };
        var p = DeskDesign.Parse(json);
        Assert.Empty(p.Reasons);
        var again = DeskDesign.Parse(p.Design!.ToJson());
        Assert.Empty(again.Reasons);
        Assert.Equal(p.Design.ToJson().ToJsonString(), again.Design!.ToJson().ToJsonString());

        var d = p.Design;
        Assert.Equal("Dusk", d.Name);
        Assert.Equal(DeskAnchor.TR, d.Layout![0].Anchor);
        Assert.Equal(1.4, d.Layout[0].Size);
        Assert.True(d.Layout[1].IsHidden);
        Assert.Equal("Caveat", d.Font!.Family);
        Assert.Equal(["#ffd08a", "#ff7e5f"], d.Ink!.Colors);
        Assert.Equal("#aabbcc", d.Launcher!.Buttons[0].Glow);
        Assert.Equal(LauncherOrientation.Column, d.Launcher.Orientation);
        Assert.Equal(MessageProposalMode.Replace, d.Messages!.Mode);
    }

    [Theory]
    [InlineData("\"auto\"", DeskInkKind.Auto)]
    [InlineData("\"LIGHT\"", DeskInkKind.Light)]
    [InlineData("\"dark\"", DeskInkKind.Dark)]
    [InlineData("\"theme\"", DeskInkKind.Theme)]
    [InlineData("\"#FFD08A\"", DeskInkKind.Custom)]
    [InlineData("\"fa0\"", DeskInkKind.Custom)]
    [InlineData("[\"#ffffff\"]", DeskInkKind.Custom)]
    [InlineData("[\"#ffffff\",\"#000\",\"#123456\",\"#abcdef\"]", DeskInkKind.Custom)]
    public void InkAcceptsTheWordsOneColorAndGradients(string ink, DeskInkKind kind)
    {
        var p = Parse($$$"""{"ink":{{{ink}}}}""");
        Assert.Empty(p.Reasons);
        Assert.Equal(kind, p.Design!.Ink!.Kind);
    }

    [Fact]
    public void InkColorsAreNormalizedToSixDigitLowercaseWithAHash()
    {
        Assert.Equal(["#ffaa00"], Parse("""{"ink":"FA0"}""").Design!.Ink!.Colors);
        Assert.Equal("#ffaa00", Parse("""{"ink":"FA0"}""").Design!.ToJson()["ink"]!.GetValue<string>());
    }

    [Fact]
    public void ASizeSnapsToTheDesksStep()
    {
        Assert.Equal(1.2, Parse("""{"layout":{"clock":{"anchor":"bl","size":1.23}}}""").Design!.Layout![0].Size);
    }

    [Fact]
    public void AHiddenPieceDropsItsAnchorAndSize()
    {
        var piece = Parse("""{"layout":{"clock":{"anchor":"bl","size":1.2,"hidden":true}}}""").Design!.Layout![0];
        Assert.Null(piece.Anchor);
        Assert.Null(piece.Size);
        Assert.True(piece.IsHidden);
    }

    [Fact]
    public void NameAndNoteAreTrimmed_AndBlankIsAbsent()
    {
        var d = Parse("""{"name":"  Dusk  ","note":"   "}""").Design!;
        Assert.Equal("Dusk", d.Name);
        Assert.Null(d.Note);
    }

    [Fact]
    public void TheEmptyFontIsTheSystemFont()
    {
        Assert.Equal("", Parse("""{"font":""}""").Design!.Font!.Family);
    }

    [Fact]
    public void ALauncherWithNoButtonsIsAllowed()
    {
        Assert.True(Parse("""{"launcher":{"orientation":"row","buttons":[]}}""").Design!.Launcher!.IsEmpty);
    }

    [Fact]
    public void ADesignAcceptsTheStoredImageNameFromGetDesk()
    {
        var name = new string('a', 16) + ".png";
        var p = Parse($$$"""{"launcher":{"buttons":[{"kind":"link","target":"https://example.com","image":"{{{name}}}"}]}}""");
        Assert.Empty(p.Reasons);
        Assert.Equal(name, p.Design!.Launcher!.Buttons[0].Image);
    }

    // ---- refusing: shape and range ----

    [Theory]
    [InlineData("[]")]
    [InlineData("\"design\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void ADesignThatIsNotAnObjectIsRefused(string json)
    {
        var p = DeskDesign.Parse(json == "null" ? null : JsonNode.Parse(json));
        Assert.Null(p.Design);
        Assert.Contains(p.Reasons, r => r.Field == "design");
    }

    [Fact]
    public void AnUnknownFieldIsRefusedByName_SoATypoNeverDoesNothing()
    {
        var p = Parse("""{"colour":"#fff","command":"calc.exe"}""");
        Assert.Null(p.Design);
        Assert.Contains("colour", p.Reasons[0].Message);
        Assert.Contains("command", p.Reasons[0].Message);
    }

    [Fact]
    public void PerSongLooksAreRefusedUntilTheyShip()
    {
        AssertRefused("""{"looks":{"a":1}}""", "looks");
    }

    [Theory]
    [InlineData("""{"layout":{"toaster":{"anchor":"bl"}}}""", "layout.toaster")]
    [InlineData("""{"layout":{"clock":{"anchor":"center"}}}""", "layout.clock.anchor")]
    [InlineData("""{"layout":{"clock":{"anchor":7}}}""", "layout.clock.anchor")]
    [InlineData("""{"layout":{"clock":{"size":0.59}}}""", "layout.clock.size")]
    [InlineData("""{"layout":{"clock":{"size":1.61}}}""", "layout.clock.size")]
    [InlineData("""{"layout":{"clock":{"size":"big"}}}""", "layout.clock.size")]
    [InlineData("""{"layout":{"clock":{"hidden":"yes"}}}""", "layout.clock.hidden")]
    [InlineData("""{"layout":{"clock":{"color":"red"}}}""", "layout.clock")]
    [InlineData("""{"layout":{"clock":"bl"}}""", "layout.clock")]
    [InlineData("""{"layout":[]}""", "layout")]
    [InlineData("""{"layout":{"clock":null}}""", "layout.clock")]
    public void ALayoutThatBreaksARuleIsRefusedWithTheFieldPath(string json, string field) => AssertRefused(json, field);

    [Fact]
    public void ALayoutNamingTooManyPiecesIsRefusedWithoutReadingThemAll()
    {
        var layout = new JsonObject();
        for (int i = 0; i < 5000; i++) layout["p" + i] = new JsonObject();
        var p = DeskDesign.Parse(new JsonObject { ["layout"] = layout });
        Assert.Null(p.Design);
        Assert.Single(p.Reasons);
    }

    [Theory]
    [InlineData("""{"ink":"red"}""")]
    [InlineData("""{"ink":"#12345"}""")]
    [InlineData("""{"ink":"#ffffff,#000000"}""")]
    [InlineData("""{"ink":["#ffffff","#000000","#111111","#222222","#333333"]}""")]
    [InlineData("""{"ink":[]}""")]
    [InlineData("""{"ink":["#ffffff",5]}""")]
    [InlineData("""{"ink":true}""")]
    [InlineData("""{"ink":{"color":"#fff"}}""")]
    [InlineData("""{"ink":"url(javascript:alert(1))"}""")]
    public void InkThatIsNotAWordOrHexColorsIsRefused(string json) => AssertRefused(json, "ink");

    [Theory]
    [InlineData("""{"plate":"on"}""", "plate")]
    [InlineData("""{"plate":1}""", "plate")]
    [InlineData("""{"shadow":null,"plate":[]}""", "plate")]
    [InlineData("""{"shadow":"false"}""", "shadow")]
    public void SwitchesMustBeBooleans(string json, string field) => AssertRefused(json, field);

    [Theory]
    [InlineData("""{"font":5}""", "font")]
    [InlineData("""{"font":[]}""", "font")]
    [InlineData("""{"font":"C:\\Windows\\Fonts\\evil.ttf"}""", "font")]
    [InlineData("""{"font":"/usr/share/fonts/x"}""", "font")]
    [InlineData("""{"font":"Arial, Segoe UI"}""", "font")]
    [InlineData("""{"font":"pack://application:,,,/x#Evil"}""", "font")]
    [InlineData("""{"font":"https://evil.example/f.ttf"}""", "font")]
    [InlineData("""{"font":"<b>x</b>"}""", "font")]
    [InlineData("""{"font":"Bad\u0000Name"}""", "font")]
    [InlineData("""{"font":" Padded "}""", "font")]
    [InlineData("""{"font":{"family":"Caveat","link":"http://example.com/c"}}""", "font.link")]
    [InlineData("""{"font":{"family":"Caveat","link":"javascript:alert(1)"}}""", "font.link")]
    [InlineData("""{"font":{"family":"Caveat","link":"file:///c:/x.ttf"}}""", "font.link")]
    [InlineData("""{"font":{"family":"Caveat","link":"https://user:pw@example.com/"}}""", "font.link")]
    [InlineData("""{"font":{"family":"Caveat","link":5}}""", "font.link")]
    [InlineData("""{"font":{"link":"https://example.com"}}""", "font.family")]
    [InlineData("""{"font":{"family":"Caveat","exec":"x"}}""", "font")]
    public void FontsThatCouldBePathsLinksOrListsAreRefused(string json, string field) => AssertRefused(json, field);

    [Fact]
    public void AFontFamilyIsAtMostSixtyFourCharacters()
    {
        AssertRefused($$$"""{"font":"{{{new string('a', 65)}}}"}""", "font");
        Assert.NotNull(Parse($$$"""{"font":"{{{new string('a', 64)}}}"}""").Design);
    }

    [Theory]
    [InlineData("Segoe UI Variable")]
    [InlineData("Cascadia Code")]
    [InlineData("MS Gothic")]
    [InlineData("Noto Sans JP")]
    [InlineData("Source Sans 3")]
    [InlineData("\u30e1\u30a4\u30ea\u30aa")]
    public void RealFamilyNamesPass(string family) => Assert.Null(DeskDesign.FontFamilyProblem(family));

    [Theory]
    [InlineData("""{"name":7}""", "name")]
    [InlineData("""{"name":"line one\nline two"}""", "name")]
    [InlineData("""{"name":"tab\there"}""", "name")]
    [InlineData("""{"name":"bell\u0007"}""", "name")]
    [InlineData("""{"name":"sep\u2028arator"}""", "name")]
    [InlineData("""{"note":"x\u0000y"}""", "note")]
    [InlineData("""{"note":{"a":1}}""", "note")]
    public void NamesAndNotesAreOneLineOfPlainText(string json, string field) => AssertRefused(json, field);

    [Fact]
    public void NameAndNoteLengthsAreCountedInCharacters()
    {
        Assert.NotNull(Parse($$$"""{"name":"{{{new string('n', 40)}}}"}""").Design);
        AssertRefused($$$"""{"name":"{{{new string('n', 41)}}}"}""", "name");
        Assert.NotNull(Parse($$$"""{"note":"{{{new string('n', 200)}}}"}""").Design);
        AssertRefused($$$"""{"note":"{{{new string('n', 201)}}}"}""", "note");
        // 40 emoji are 40 characters (80 UTF-16 units): within the limit.
        Assert.NotNull(Parse($$$"""{"name":"{{{string.Concat(Enumerable.Repeat("\U0001F600", 40))}}}"}""").Design);
        AssertRefused($$$"""{"name":"{{{string.Concat(Enumerable.Repeat("\U0001F600", 41))}}}"}""", "name");
    }

    [Fact]
    public void ARunawayStringIsRefusedWithoutBeingCounted()
    {
        AssertRefused(new JsonObject { ["note"] = new string('x', 5_000_000) }.ToJsonString(), "note");
    }

    // ---- refusing: the Launcher ----

    [Theory]
    [InlineData("""{"launcher":[]}""", "launcher")]
    [InlineData("""{"launcher":{"buttons":{}}}""", "launcher.buttons")]
    [InlineData("""{"launcher":{}}""", "launcher.buttons")]
    [InlineData("""{"launcher":{"orientation":"diagonal","buttons":[]}}""", "launcher.orientation")]
    [InlineData("""{"launcher":{"buttons":[],"exec":"x"}}""", "launcher")]
    [InlineData("""{"launcher":{"buttons":["calc"]}}""", "launcher.buttons[0]")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"exe","target":"calc.exe"}]}}""", "launcher.buttons[0].kind")]
    [InlineData("""{"launcher":{"buttons":[{"target":"calc.exe"}]}}""", "launcher.buttons[0].kind")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"app"}]}}""", "launcher.buttons[0].target")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"app","target":"cmd.exe /c calc"}]}}""", "launcher.buttons[0].target")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"app","target":"C:\\Windows\\System32\\calc.exe"}]}}""", "launcher.buttons[0].target")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"app","target":"\\\\server\\share\\x.exe"}]}}""", "launcher.buttons[0].target")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"app","target":"shell:AppsFolder\\x"}]}}""", "launcher.buttons[0].target")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"app","target":"calc & del *"}]}}""", "launcher.buttons[0].target")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"app","target":"a\"b"}]}}""", "launcher.buttons[0].target")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"app","target":"{6D809377-6AF0-444B-8957-A3773F02200E}\\..\\x.exe"}]}}""", "launcher.buttons[0].target")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"app","target":"{6D809377-6AF0-444B-8957-A3773F02200E}\\x.bat"}]}}""", "launcher.buttons[0].target")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"link","target":"http://example.com"}]}}""", "launcher.buttons[0].target")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"link","target":"file:///C:/Windows/System32/calc.exe"}]}}""", "launcher.buttons[0].target")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"link","target":"javascript:alert(1)"}]}}""", "launcher.buttons[0].target")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"link","target":"ms-settings:network"}]}}""", "launcher.buttons[0].target")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"link","target":"https://example.com/a b"}]}}""", "launcher.buttons[0].target")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"link","target":"https://u:p@example.com/"}]}}""", "launcher.buttons[0].target")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"link","target":"https://example.com","label":"0123456789012345678901234"}]}}""", "launcher.buttons[0].label")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"link","target":"https://example.com","label":"a\nb"}]}}""", "launcher.buttons[0].label")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"link","target":"https://example.com","label":5}]}}""", "launcher.buttons[0].label")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"link","target":"https://example.com","glow":"red"}]}}""", "launcher.buttons[0].glow")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"link","target":"https://example.com","image":"C:\\x.png"}]}}""", "launcher.buttons[0].image")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"link","target":"https://example.com","image":"..\\..\\x.png"}]}}""", "launcher.buttons[0].image")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"link","target":"https://example.com","image":"logo.png"}]}}""", "launcher.buttons[0].image")]
    [InlineData("""{"launcher":{"buttons":[{"kind":"link","target":"https://example.com","args":"-x"}]}}""", "launcher.buttons[0]")]
    public void ALauncherThatCouldRunAnythingButAnInstalledAppOrAnHttpsLinkIsRefused(string json, string field) => AssertRefused(json, field);

    [Fact]
    public void ALauncherIsAtMostEightButtons()
    {
        string Buttons(int n) => string.Join(",", Enumerable.Repeat("""{"kind":"link","target":"https://example.com"}""", n));
        Assert.NotNull(Parse($$$"""{"launcher":{"buttons":[{{{Buttons(8)}}}]}}""").Design);
        AssertRefused($$$"""{"launcher":{"buttons":[{{{Buttons(9)}}}]}}""", "launcher.buttons");
        AssertRefused($$$"""{"launcher":{"buttons":[{{{Buttons(20000)}}}]}}""", "launcher.buttons");
    }

    // ---- refusing: messages and theme ----

    [Theory]
    [InlineData("""{"messages":[]}""", "messages")]
    [InlineData("""{"messages":{"lines":[]}}""", "messages.lines")]
    [InlineData("""{"messages":{"lines":"hello"}}""", "messages.lines")]
    [InlineData("""{"messages":{"lines":[5]}}""", "messages.lines")]
    [InlineData("""{"messages":{"lines":["a\nb"]}}""", "messages.lines")]
    [InlineData("""{"messages":{"lines":["Monday: hello"]}}""", "messages.lines")]
    [InlineData("""{"messages":{"lines":["{ink:red} hello"]}}""", "messages.lines")]
    [InlineData("""{"messages":{"lines":["# only a comment"]}}""", "messages.lines")]
    [InlineData("""{"messages":{"lines":["hello"],"mode":"append"}}""", "messages.mode")]
    [InlineData("""{"messages":{"lines":["hello"],"shell":"x"}}""", "messages")]
    public void MessagesThatBreakTheLineRulesAreRefused(string json, string field) => AssertRefused(json, field);

    [Fact]
    public void MessageLinesAreAtMostSixtyOfOneHundredTwentyCharacters()
    {
        string Lines(int n, int chars) => string.Join(",", Enumerable.Repeat($"\"{new string('x', chars)}\"", n));
        Assert.NotNull(Parse($$$"""{"messages":{"lines":[{{{Lines(60, 120)}}}]}}""").Design);
        AssertRefused($$$"""{"messages":{"lines":[{{{Lines(61, 10)}}}]}}""", "messages.lines");
        AssertRefused($$$"""{"messages":{"lines":[{{{Lines(1, 121)}}}]}}""", "messages.lines");
        AssertRefused($$$"""{"messages":{"lines":[{{{Lines(50000, 3)}}}]}}""", "messages.lines");
    }

    [Fact]
    public void MessageLinesAreTrimmedAndModeDefaultsToAdd()
    {
        var m = Parse("""{"messages":{"lines":["  hello.  "]}}""").Design!.Messages!;
        Assert.Equal(["hello."], m.Lines);
        Assert.Equal(MessageProposalMode.Add, m.Mode);
    }

    [Theory]
    [InlineData("""{"theme":[]}""")]
    [InlineData("""{"theme":"obsidian"}""")]
    [InlineData("""{"theme":{}}""")]
    [InlineData("""{"theme":{"name":"x"}}""")]
    [InlineData("""{"theme":{"name":5,"bg":[],"glass":{}}}""")]
    public void AThemeThatDoesNotLintIsRefused(string json)
    {
        var p = Parse(json);
        Assert.Null(p.Design);
        Assert.All(p.Reasons, r => Assert.StartsWith("theme", r.Field));
    }

    [Fact]
    public void ABrokenColorInAThemeNamesTheThemeField()
    {
        var theme = ObsidianTheme();
        theme["accent"] = "orange";
        var p = DeskDesign.Parse(new JsonObject { ["theme"] = theme });
        Assert.Null(p.Design);
        Assert.Contains(p.Reasons, r => r.Field == "theme.accent");
    }

    [Fact]
    public void ThemeWarningsRideAlongWithoutRefusing()
    {
        var theme = ObsidianTheme();
        theme["text_secondary"] = "#0b0b0e";   // fails the secondary contrast: a warning, not an error
        var p = DeskDesign.Parse(new JsonObject { ["theme"] = theme });
        Assert.NotNull(p.Design);
        Assert.NotEmpty(p.Warnings);
    }

    // ---- merging ----

    private static DeskDesign Current() => Parse("""
        {"layout":{"clock":{"anchor":"bl","size":1},"claude":{"anchor":"bl","size":1},"meters":{"hidden":true},"launcher":{"hidden":true}},
         "font":"EsteFont Pro","ink":"auto","plate":false,"shadow":true,
         "launcher":{"orientation":"row","buttons":[]}}
        """).Design!;

    private static DeskDesign Proposal(string json) => Parse(json).Design!;

    [Fact]
    public void MergeLeavesAbsentFieldsAlone()
    {
        var merged = DeskDesign.Merge(Current(), Proposal("""{"plate":true}"""));
        Assert.True(merged.Plate);
        Assert.Equal("EsteFont Pro", merged.Font!.Family);
        Assert.Equal(DeskInkKind.Auto, merged.Ink!.Kind);
        Assert.True(merged.Shadow);
    }

    [Fact]
    public void MergeMovesAPieceAndKeepsItsSizeUnlessTheProposalSetsOne()
    {
        var current = DeskDesign.Merge(Current(), Proposal("""{"layout":{"clock":{"size":1.4}}}"""));
        var moved = DeskDesign.Merge(current, Proposal("""{"layout":{"clock":{"anchor":"tr"}}}"""));
        var clock = moved.Layout!.Single(p => p.Piece == "clock");
        Assert.Equal(DeskAnchor.TR, clock.Anchor);
        Assert.Equal(1.4, clock.Size);
        Assert.Equal(DeskAnchor.BL, moved.Layout!.Single(p => p.Piece == "claude").Anchor);
    }

    [Fact]
    public void MergeHidesAndShowsPieces()
    {
        var hidden = DeskDesign.Merge(Current(), Proposal("""{"layout":{"claude":{"hidden":true}}}"""));
        Assert.True(hidden.Layout!.Single(p => p.Piece == "claude").IsHidden);

        var shown = DeskDesign.Merge(Current(), Proposal("""{"layout":{"meters":{"hidden":false}}}"""));
        var meters = shown.Layout!.Single(p => p.Piece == "meters");
        Assert.False(meters.IsHidden);
        Assert.Equal(DeskArrangement.DefaultAnchor("meters"), meters.Anchor);

        var placed = DeskDesign.Merge(Current(), Proposal("""{"layout":{"meters":{"anchor":"ml","size":0.8}}}"""));
        Assert.Equal(DeskAnchor.ML, placed.Layout!.Single(p => p.Piece == "meters").Anchor);
        Assert.Equal(0.8, placed.Layout!.Single(p => p.Piece == "meters").Size);
    }

    [Fact]
    public void MergeListsEveryKnownPiece_PlacedFirstThenHidden()
    {
        var merged = DeskDesign.Merge(Current(), Proposal("""{"layout":{"clock":{"anchor":"tl"}}}"""));
        Assert.Equal(DeskArrangement.Known.Order(StringComparer.Ordinal), merged.Layout!.Select(p => p.Piece).Order(StringComparer.Ordinal));
        Assert.Equal(["clock", "claude"], merged.Layout!.Where(p => !p.IsHidden).Select(p => p.Piece));
    }

    [Fact]
    public void MergeWithoutAKnownDeskBuildsFromTheProposalAlone()
    {
        var merged = DeskDesign.Merge(null, Proposal("""{"layout":{"clock":{"anchor":"br","size":1.2}},"plate":true}"""));
        Assert.Equal(DeskAnchor.BR, merged.Layout!.Single(p => p.Piece == "clock").Anchor);
        Assert.True(merged.Plate);
        Assert.Null(merged.Font);
    }

    [Fact]
    public void MergeReplacesTheLauncherAndThemeAsWholes()
    {
        var merged = DeskDesign.Merge(Current(), Proposal($$$"""
            {"launcher":{"orientation":"column","buttons":[{"kind":"app","target":"{{{AppId}}}"}]},"theme":{{{ObsidianTheme().ToJsonString()}}}}
            """));
        Assert.Equal(LauncherOrientation.Column, merged.Launcher!.Orientation);
        Assert.Single(merged.Launcher.Buttons);
        Assert.NotNull(merged.Theme);
    }

    [Fact]
    public void AFirstLauncherButtonPlacesTheLauncherAtTheFirstFreeCorner()
    {
        // clock and claude sit at bottom left, so the first free corner is bottom right.
        var merged = DeskDesign.Merge(Current(), Proposal($$$"""{"launcher":{"buttons":[{"kind":"app","target":"{{{AppId}}}"}]}}"""));
        var launcher = merged.Layout!.Single(p => p.Piece == "launcher");
        Assert.False(launcher.IsHidden);
        Assert.Equal(DeskAnchor.BR, launcher.Anchor);
    }

    [Fact]
    public void ALauncherThePlayerPlacedItselfIsNotMoved()
    {
        var merged = DeskDesign.Merge(Current(), Proposal($$$"""
            {"layout":{"launcher":{"anchor":"tc"}},"launcher":{"buttons":[{"kind":"app","target":"{{{AppId}}}"}]}}
            """));
        Assert.Equal(DeskAnchor.TC, merged.Layout!.Single(p => p.Piece == "launcher").Anchor);
    }

    [Fact]
    public void MergeKeepsTheNameOfTheProposal_AndMessagesPassThrough()
    {
        var merged = DeskDesign.Merge(Current(), Proposal("""{"name":"Dusk","messages":{"lines":["hi."],"mode":"add"}}"""));
        Assert.Equal("Dusk", merged.Name);
        Assert.Equal(["hi."], merged.Messages!.Lines);
    }

    // ---- diff ----

    [Fact]
    public void DiffNamesWhatChanged_AndOnlyWhatChanged()
    {
        var current = Current();
        var proposal = Proposal($$$"""
            {"layout":{"clock":{"anchor":"tr","size":1.4},"meters":{"anchor":"ml"},"claude":{"hidden":true}},
             "font":"Caveat","ink":["#ffd08a","#ff7e5f"],"plate":true,"shadow":true,
             "launcher":{"orientation":"column","buttons":[{"kind":"app","target":"{{{AppId}}}"},{"kind":"link","target":"https://example.com"}]},
             "messages":{"lines":["one.","two.","# note"],"mode":"replace"},
             "theme":{{{ObsidianTheme().ToJsonString()}}}}
            """);
        var lines = DeskDesign.Diff(current, DeskDesign.Merge(current, proposal), k => char.ToUpperInvariant(k[0]) + k[1..]);

        Assert.Contains("Clock: moved from bottom left to top right", lines);
        Assert.Contains("Clock: size 100% to 140%", lines);
        Assert.Contains("Meters: shown at middle left", lines);
        Assert.Contains("Claude: hidden", lines);
        Assert.Contains("Font: EsteFont Pro to Caveat", lines);
        Assert.Contains("Ink: automatic to #ffd08a, #ff7e5f", lines);
        Assert.Contains("Plate behind the text: on", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("Text shadow"));   // unchanged
        Assert.Contains("Launcher: 2 buttons in a column", lines);
        Assert.Contains("Messages: replaces the list with 2 lines", lines);
        Assert.Contains(lines, l => l.StartsWith("Widget theme: "));
    }

    [Fact]
    public void DiffOfADesignAgainstItselfIsEmpty()
    {
        var current = Current();
        Assert.Empty(DeskDesign.Diff(current, DeskDesign.Merge(current, Proposal("{}"))));
    }

    [Fact]
    public void DiffMessagesAddsOneLineSingular()
    {
        var lines = DeskDesign.Diff(Current(), Proposal("""{"messages":{"lines":["hi."]}}"""));
        Assert.Equal(["Messages: adds 1 line"], lines);
    }
}
