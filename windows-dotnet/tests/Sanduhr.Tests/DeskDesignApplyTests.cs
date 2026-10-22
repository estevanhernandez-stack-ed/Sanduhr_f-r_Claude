using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>How a full design becomes the Desk's settings, which theme file a design's theme is, the review bar's model, and the
/// rules around saved designs (names, sameness, Launcher images).</summary>
public sealed class DeskDesignApplyTests
{
    private static DeskDesign D(string json) => DeskDesign.Parse(JsonNode.Parse(json)).Design!;

    private static DeskDesignSettings Settings(DeskDesign design, DeskSettings? desk = null, DeskInkSettings? ink = null, LauncherModel? launcher = null) =>
        DeskDesignApply.Settings(desk ?? DeskSettings.Default with { Enabled = true }, ink ?? DeskInkSettings.Default, launcher ?? LauncherModel.Empty, design);

    // ---- settings from a design ----

    [Fact]
    public void ADesignSetsTheLayoutFontPlateAndInk()
    {
        var s = Settings(D("""
            {"layout":{"clock":{"anchor":"tr","size":1.4},"claude":{"hidden":true},"message":{"anchor":"bl"}},
             "font":"Segoe UI","plate":true,"shadow":false,"ink":["#ffd08a","#ff7e5f"]}
            """));
        var layout = new DeskArrangement(s.Desk.Layout);
        Assert.Equal(new DeskPlacement("clock", DeskAnchor.TR, 1.4), layout.Placement("clock"));
        Assert.Equal(DeskAnchor.BL, layout.Placement("message")!.Anchor);
        Assert.Null(layout.Placement("claude"));
        Assert.Equal("Segoe UI", s.Desk.Font);
        Assert.True(s.Desk.Plate);
        Assert.Equal(new DeskInkSettings(DeskInkMode.Custom, "ffd08a,ff7e5f", false), s.Ink);
        Assert.True(s.Desk.Enabled);   // a design never turns the Desk on or off
    }

    [Fact]
    public void AFieldTheDesignLeavesAloneKeepsItsCurrentValue()
    {
        var desk = DeskSettings.Default with { Layout = "clock:tl", Font = "Georgia", Plate = true, TimeSize = 150 };
        var ink = new DeskInkSettings(DeskInkMode.Dark, "123456", true);
        var s = Settings(D("""{"shadow":false}"""), desk, ink);
        Assert.Equal("clock:tl", s.Desk.Layout);
        Assert.Equal("Georgia", s.Desk.Font);
        Assert.True(s.Desk.Plate);
        Assert.Equal(150, s.Desk.TimeSize);
        Assert.Equal(new DeskInkSettings(DeskInkMode.Dark, "123456", false), s.Ink);
    }

    [Theory]
    [InlineData("EsteFont Pro", null)]     // the default family is saved as absent, so a later default follows it
    [InlineData("", "")]                   // the system font
    [InlineData("EsteFont 26", "EsteFont 26")]
    [InlineData("Segoe UI", "Segoe UI")]
    public void TheFontIsSavedTheWayTheFontPickerSavesIt(string family, string? saved) =>
        Assert.Equal(saved, DeskDesignApply.FontSetting(new DeskFontChoice(family)));

    [Theory]
    [InlineData("\"auto\"", DeskInkMode.Auto)]
    [InlineData("\"light\"", DeskInkMode.Light)]
    [InlineData("\"dark\"", DeskInkMode.Dark)]
    [InlineData("\"theme\"", DeskInkMode.Theme)]
    public void TheNamedInksMapToTheirModesAndKeepTheCustomColors(string ink, DeskInkMode mode)
    {
        var s = Settings(D("{\"ink\":" + ink + "}"), ink: new DeskInkSettings(DeskInkMode.Custom, "abcdef", true));
        Assert.Equal(mode, s.Ink.Mode);
        Assert.Equal("abcdef", s.Ink.Color);
    }

    [Fact]
    public void OneCustomColorIsOneHexWithoutTheHash()
    {
        var s = Settings(D("""{"ink":"#9AD7FF"}"""));
        Assert.Equal(new DeskInkSettings(DeskInkMode.Custom, "9ad7ff".ToLowerInvariant(), true), s.Ink);
    }

    [Fact]
    public void TheLauncherComesFromTheDesignOrStays()
    {
        var keep = LauncherModel.Parse(JsonNode.Parse("""{"orientation":"column","buttons":[{"kind":"link","target":"https://example.com","label":"Home"}]}"""));
        Assert.Same(keep, Settings(D("""{"plate":true}"""), launcher: keep).Launcher);
        var replaced = Settings(D("""{"launcher":{"orientation":"row","buttons":[]}}"""), launcher: keep).Launcher;
        Assert.True(replaced.IsEmpty);
    }

    [Fact]
    public void AMergedDesignAppliedTwiceChangesNothingTheSecondTime()
    {
        var desk = DeskSettings.Default with { Enabled = true };
        var current = DeskState.Current(desk, DeskInkSettings.Default, LauncherModel.Empty, null);
        var merged = DeskDesign.Merge(current, D("""{"layout":{"clock":{"anchor":"tr"}},"plate":true}"""));
        var first = DeskDesignApply.Settings(desk, DeskInkSettings.Default, LauncherModel.Empty, merged);
        var again = DeskDesignApply.Settings(first.Desk, first.Ink, first.Launcher, merged);
        Assert.Equal(first.Desk, again.Desk);
        Assert.Equal(first.Ink, again.Ink);
    }

    // ---- the preview font ----

    [Theory]
    [InlineData("Fira Code Retina", "Cascadia Mono")]
    [InlineData("Playfair Display", "Georgia")]
    [InlineData("Some Brush Script", "EsteFont Pro")]
    [InlineData("Inter Tight", "Segoe UI")]
    public void AMissingFontPreviewsInTheNearestInstalledLook(string asked, string stands)
    {
        var installed = new HashSet<string>(["Cascadia Mono", "Georgia", "Segoe UI"], StringComparer.OrdinalIgnoreCase);
        Assert.Equal(stands, DeskDesignApply.PreviewFont(asked, installed.Contains));
    }

    [Fact]
    public void AnInstalledOrBundledFontPreviewsAsItself()
    {
        Assert.Equal("Calibri", DeskDesignApply.PreviewFont("Calibri", f => f == "Calibri"));
        Assert.Equal("EsteFont 26", DeskDesignApply.PreviewFont("EsteFont 26", _ => false));
        Assert.Equal("", DeskDesignApply.PreviewFont("", _ => false));
        Assert.Equal("Segoe UI", DeskDesignApply.PreviewFont("Gone Font", _ => false));
    }

    // ---- the theme a design brings ----

    private static JsonObject Theme(string name, string accent = "#ff7e5f")
    {
        var o = ThemeDraft.FromDefinition("obsidian", ThemeCatalog.BuiltIns["obsidian"]).ToJson();
        o["name"] = name;
        o["accent"] = accent;
        return o;
    }

    private static Func<string, ThemeDefinition?> Catalog(Dictionary<string, ThemeDefinition>? extra = null) => key =>
        extra is not null && extra.TryGetValue(key, out var d) ? d : ThemeCatalog.BuiltIns.GetValueOrDefault(key);

    [Fact]
    public void ABuiltInThemeIsRecognizedAndNotWrittenAgain()
    {
        var obsidian = ThemeDraft.FromDefinition("obsidian", ThemeCatalog.BuiltIns["obsidian"]).ToJson();
        var plan = DesignTheme.Plan(obsidian, Catalog());
        Assert.NotNull(plan);
        Assert.Equal("obsidian", plan!.Key);
        Assert.False(plan.Write);
    }

    [Fact]
    public void ANewThemeIsWrittenUnderItsSlug()
    {
        var plan = DesignTheme.Plan(Theme("Dusk Rose"), Catalog());
        Assert.Equal(new[] { "dusk-rose", "True" }, new[] { plan!.Key, plan.Write.ToString() });
    }

    [Fact]
    public void ATakenNameWithOtherColorsGetsASuffixAndNeverOverwrites()
    {
        // "Obsidian" is a built-in: a proposed theme of that name with other colors must not take its key.
        var plan = DesignTheme.Plan(Theme("Obsidian"), Catalog());
        Assert.Equal("obsidian-2", plan!.Key);
        Assert.True(plan.Write);

        var dusk = ThemeLint.Lint(Theme("Dusk")).Theme!;
        var user = new Dictionary<string, ThemeDefinition> { ["dusk"] = dusk };
        var same = DesignTheme.Plan(Theme("Dusk"), Catalog(user));
        Assert.Equal(("dusk", false), (same!.Key, same.Write));          // the user's own copy of it already
        var other = DesignTheme.Plan(Theme("Dusk", "#00ff88"), Catalog(user));
        Assert.Equal(("dusk-2", true), (other!.Key, other.Write));
    }

    [Fact]
    public void ABrokenThemeHasNoPlan()
    {
        var bad = Theme("Bad");
        bad["text"] = "not a color";
        Assert.Null(DesignTheme.Plan(bad, Catalog()));
    }

    [Fact]
    public void ANameAtTheLimitStaysAValidKeyAndAThemeNameOverTheLimitHasNoPlan()
    {
        var plan = DesignTheme.Plan(Theme("Abcdefghijklmnopqrstuvwx"), Catalog());
        Assert.True(ThemeHandoff.IsValidKey(plan!.Key));
        Assert.Null(DesignTheme.Plan(Theme("A theme with a rather long name 1234"), Catalog()));
    }

    // ---- the review bar's model ----

    private static DeskDesign Now() => DeskState.Current(DeskSettings.Default with { Enabled = true }, DeskInkSettings.Default, LauncherModel.Empty, null);

    private static DeskReviewModel Review(string json, IEnumerable<MissingFont>? fonts = null, Func<string, bool>? installed = null, bool deskOn = true)
    {
        var proposal = D(json);
        var current = Now();
        return DeskReviewModel.Build(proposal, current, DeskDesign.Merge(current, proposal), fonts ?? [], installed ?? (_ => false), deskOn, SettingsNames.DeskPieceName);
    }

    [Fact]
    public void TheBarShowsTheNameTheNoteAndWhatChanged()
    {
        var m = Review("""{"name":"Dusk","note":"Warm ink to match the sunset.","plate":true,"layout":{"clock":{"anchor":"tr"}}}""");
        Assert.Equal("Dusk", m.Title);
        Assert.Equal("Warm ink to match the sunset.", m.Note);
        Assert.Contains("Plate behind the text: on", m.Changes);
        Assert.Contains(m.Changes, c => c.StartsWith("Clock and date: moved from bottom left to top right"));
        Assert.True(m.CanApply);
        Assert.Equal(DeskReviewCopy.KeysLine, m.Keys);
    }

    [Fact]
    public void ADesignWithNoNameOrNoChangeStillReads()
    {
        var m = Review("""{"plate":false}""");
        Assert.Equal(DeskReviewCopy.Untitled, m.Title);
        Assert.Null(m.Note);
        Assert.Equal([DeskReviewCopy.NothingChanges], m.Changes);
    }

    [Fact]
    public void AMissingFontKeepsApplyWaitingUntilItIsInstalled()
    {
        var fonts = new[] { new MissingFont("Zed Sans", "https://fonts.example/zed") };
        var waiting = Review("""{"font":{"family":"Zed Sans","link":"https://fonts.example/zed"}}""", fonts);
        Assert.False(waiting.CanApply);
        Assert.Equal(DeskReviewCopy.KeysLineWaiting, waiting.Keys);
        var f = Assert.Single(waiting.MissingFonts);
        Assert.Equal("Zed Sans", f.Family);
        Assert.Equal("https://fonts.example/zed", f.Link);
        Assert.Contains("https://fonts.example/zed", f.Line);

        var arrived = Review("""{"font":"Zed Sans"}""", fonts, installed: name => name == "Zed Sans");
        Assert.True(arrived.CanApply);
        Assert.Empty(arrived.MissingFonts);
    }

    [Theory]
    [InlineData("https://fonts.example/zed", "https://fonts.example/zed")]
    [InlineData("http://fonts.example/zed", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("file:///C:/x.ttf", null)]
    [InlineData("https://user:pw@fonts.example/zed", null)]
    [InlineData("fonts.example/zed", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void OnlyAnHttpsLinkIsEverShownOrOpened(string? link, string? safe) =>
        Assert.Equal(safe, DeskReviewModel.SafeLink(link));

    [Fact]
    public void AnUnsafeLinkIsDroppedButTheFontStillWaits()
    {
        var m = Review("""{"font":"Zed Sans"}""", [new MissingFont("Zed Sans", "http://insecure.example/")]);
        Assert.False(m.CanApply);
        Assert.Null(Assert.Single(m.MissingFonts).Link);
    }

    [Fact]
    public void TheBarSaysWhenTheDeskIsOff()
    {
        Assert.True(Review("""{"plate":true}""", deskOn: false).DeskOff);
        Assert.False(Review("""{"plate":true}""").DeskOff);
    }

    // ---- saved designs ----

    [Theory]
    [InlineData("  Dusk\u0007 ", "Dusk")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void ANameIsTrimmedAndCleaned(string? raw, string? clean) => Assert.Equal(clean, DeskDesigns.NormalizeName(raw));

    [Fact]
    public void ANameIsCutToFortyCharactersWithoutSplittingAPair()
    {
        Assert.Equal(40, DeskDesigns.NormalizeName(new string('x', 100))!.Length);
        string pair = new string('x', 39) + "\U0001F600";
        var cut = DeskDesigns.NormalizeName(pair)!;
        Assert.Equal(39, cut.Length);
        Assert.False(char.IsHighSurrogate(cut[^1]));
    }

    [Fact]
    public void TheSavedDeskIsNamedBeforeTheNewOne()
    {
        Assert.Equal("Before Dusk", DeskDesigns.BeforeName("Dusk"));
        Assert.Equal("Before a change", DeskDesigns.BeforeName(null));
        Assert.True(DeskDesigns.BeforeName(new string('n', 80)).Length <= DeskDesigns.NameMax);
    }

    [Fact]
    public void TwoDesignsAreTheSameLookWhateverTheirNamesNotesAndPieceOrder()
    {
        var a = D("""{"name":"A","note":"x","layout":{"clock":{"anchor":"tr","size":1},"claude":{"anchor":"bl"}},"plate":true}""");
        var b = D("""{"name":"B","layout":{"claude":{"anchor":"bl","size":1,"hidden":false},"clock":{"anchor":"tr"}},"plate":true}""");
        Assert.Equal(DeskDesigns.Canonical(a), DeskDesigns.Canonical(b));
        Assert.NotEqual(DeskDesigns.Canonical(a), DeskDesigns.Canonical(D("""{"layout":{"clock":{"anchor":"tl"},"claude":{"anchor":"bl"}},"plate":true}""")));
        Assert.NotEqual(DeskDesigns.Canonical(a), DeskDesigns.Canonical(D("""{"layout":{"clock":{"anchor":"tr"},"claude":{"anchor":"bl"}},"plate":false}""")));
    }

    [Fact]
    public void MessagesAreNotPartOfASavedLook()
    {
        var plain = D("""{"plate":true}""");
        var withMessages = D("""{"plate":true,"messages":{"lines":["Hello there"],"mode":"add"}}""");
        Assert.Equal(DeskDesigns.Canonical(plain), DeskDesigns.Canonical(withMessages));
    }

    [Fact]
    public void AThemeWrittenInAnotherKeyOrderIsStillTheSameLook()
    {
        var one = D("{\"theme\":" + Theme("Dusk").ToJsonString() + "}");
        var reordered = new JsonObject();
        foreach (var kv in Theme("Dusk").Reverse()) reordered[kv.Key] = kv.Value!.DeepClone();
        var two = D("{\"theme\":" + reordered.ToJsonString() + "}");
        Assert.Equal(DeskDesigns.Canonical(one), DeskDesigns.Canonical(two));
    }

    private static SavedDesign Saved(string id, int minutesAgo, string json) =>
        new(id, "Design " + id, false, new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero).AddMinutes(-minutesAgo), D(json));

    [Fact]
    public void ASavedDesignThatIsTheSameLookIsFoundNewestFirst()
    {
        var older = Saved("older", 30, """{"plate":true}""");
        var newer = Saved("newer", 5, """{"plate":true,"shadow":true}""");
        var other = Saved("other", 1, """{"plate":false}""");
        Assert.Equal("older", DeskDesigns.FindSame([older, other], D("""{"plate":true}"""))!.Id);
        Assert.Null(DeskDesigns.FindSame([other], D("""{"plate":true}""")));
        var dup = Saved("dup", 2, """{"plate":true}""");
        Assert.Equal("dup", DeskDesigns.FindSame([older, dup], D("""{"plate":true}"""))!.Id);
        _ = newer;
    }

    private static string Img(char c) => new string(c, 64) + ".png";

    private static string Launcher(params string[] images) =>
        "{\"launcher\":{\"orientation\":\"row\",\"buttons\":[" + string.Join(",", images.Select((img, i) =>
            $"{{\"kind\":\"link\",\"target\":\"https://example.com/{i}\",\"image\":\"{img}\"}}")) + "]}}";

    [Fact]
    public void LauncherImagesStayWhileAnyDesignOrTheLauncherUsesThem()
    {
        var gone = Saved("gone", 50, Launcher(Img('a'), Img('b')));
        var kept = Saved("kept", 40, Launcher(Img('b')));
        var current = LauncherModel.Parse(JsonNode.Parse(Launcher(Img('c')))!["launcher"]);
        Assert.Equal([Img('a')], DeskDesigns.ImagesToDelete([gone], [kept], LauncherModel.Empty));
        Assert.Equal([Img('a'), Img('b')], DeskDesigns.ImagesToDelete([gone], [], LauncherModel.Empty));
        Assert.Equal([Img('a')], DeskDesigns.ImagesToDelete([gone], [], LauncherModel.Parse(JsonNode.Parse(Launcher(Img('b')))!["launcher"])));
        Assert.Empty(DeskDesigns.ImagesToDelete([Saved("c", 1, Launcher(Img('c')))], [], current));
        Assert.Equal(new[] { Img('b') }.ToHashSet(), DeskDesigns.UsedImages([kept]));
    }

    [Fact]
    public void AnImageThatWentIsDroppedFromItsButtonAndNamedByTheCheck()
    {
        var design = D(Launcher(Img('a'), Img('b')));
        Func<string, bool> exists = n => n == Img('a');
        Assert.Equal([Img('b')], DeskDesigns.MissingImages(design, exists));
        var fixedUp = DeskDesigns.WithoutMissingImages(design, exists);
        Assert.Equal(Img('a'), fixedUp.Launcher!.Buttons[0].Image);
        Assert.Null(fixedUp.Launcher.Buttons[1].Image);
        Assert.Equal(design.Launcher!.Buttons[1].Target, fixedUp.Launcher.Buttons[1].Target);
        Assert.Empty(DeskDesigns.MissingImages(D("""{"plate":true}"""), _ => false));
    }
}
