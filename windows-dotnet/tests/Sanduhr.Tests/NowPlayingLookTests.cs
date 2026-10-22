using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>A look per song, the pure half: the song key (with the vectors the now-playing mod shares), seeding, the checks on a
/// look, the contrast rule, the saved-looks book and the wire shapes.</summary>
public class NowPlayingLookTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static JsonNode J(string json) => JsonNode.Parse(json)!;

    // ---- the key ----

    [Theory]
    [InlineData("", "cbf29ce484222325")]
    [InlineData("a", "af63dc4c8601ec8c")]
    [InlineData("foobar", "85944171f73967e8")]
    public void Fnv1a64MatchesTheReferenceVectors(string text, string expected) =>
        Assert.Equal(expected, SongKey.Hash(text));

    /// <summary>Computed with an independent implementation (BigInt in Node) and published in SongKey's description.</summary>
    [Theory]
    [InlineData("Blinding Lights", "The Weeknd", "ad75f4c29077a9de")]
    [InlineData("  Blinding   Lights  ", "THE WEEKND", "ad75f4c29077a9de")]
    [InlineData("Blinding\tLights", "the\u00a0weeknd", "ad75f4c29077a9de")]
    [InlineData("Song", "Artist", "d2a7aa06c42e57bd")]
    [InlineData("Song - Remastered 2011", "Artist", "d2a7aa06c42e57bd")]
    [InlineData("Song (Remastered 2009)", "Artist", "d2a7aa06c42e57bd")]
    [InlineData("song - remaster", "artist", "d2a7aa06c42e57bd")]
    [InlineData("Café del Mar", "Energy 52", "245e2ded93cf9f31")]
    [InlineData("CAFÉ DEL MAR", "energy 52", "245e2ded93cf9f31")]
    [InlineData("Untitled", "", "5f31c64688d31616")]
    [InlineData("Hey Jude - Remastered 2015", "The Beatles", "b0ccc496e92995e4")]
    [InlineData("Remaster Me", "X", "a31dfc67c5b9c43a")]
    [InlineData("A - Remastered", "B", "e5beb1190415e670")]
    [InlineData("x", "y", "be74b5197ee0cbea")]
    public void TheSongKeyVectors(string title, string artist, string expected) =>
        Assert.Equal(expected, SongKey.Of(title, artist));

    [Fact]
    public void ANullArtistIsAnEmptyOneAndAnEmptyTitleHasNoKey()
    {
        Assert.Equal(SongKey.Of("Untitled", ""), SongKey.Of("Untitled", null));
        Assert.Null(SongKey.Of("", "Artist"));
        Assert.Null(SongKey.Of("   ", "Artist"));
        Assert.Null(SongKey.Of(null, "Artist"));
        Assert.NotNull(SongKey.Of(" - Remastered", "Artist"));   // the mark needs a title in front of it
    }

    [Theory]
    [InlineData("Song (Remastered 2009) - Live", "song (remastered 2009) - live")]     // only a trailing mark
    [InlineData("Song (Remastered 2009) (Live)", "song (remastered 2009) (live)")]
    [InlineData("Song (Live) (Remastered)", "song (live)")]
    [InlineData("Song - Remastered - Live", "song")]                                    // the first mark to the end
    [InlineData("Song   -   Remastered", "song")]                                       // spaces collapse first, so the mark matches
    [InlineData("Song-Remastered", "song-remastered")]                                  // the mark needs its spaces
    public void OnlyATrailingRemasterMarkIsDropped(string title, string normalized) =>
        Assert.Equal(normalized, SongKey.Normalize(title));

    [Fact]
    public void WhitespaceIsJavaScriptsSetAndSurrogatesPassThrough()
    {
        foreach (int c in new[] { 9, 10, 11, 12, 13, 32, 0xA0, 0x1680, 0x2000, 0x200A, 0x2028, 0x2029, 0x202F, 0x205F, 0x3000, 0xFEFF })
            Assert.True(SongKey.IsSpace((char)c), c.ToString("x4"));
        foreach (int c in new[] { 0x85, 0x180E, 0x200B, 0x2060 })   // NEL, Mongolian vowel separator, zero width space and word joiner are not
            Assert.False(SongKey.IsSpace((char)c), c.ToString("x4"));
        Assert.Equal("a b", SongKey.Normalize("\ufeff a\u2003\u3000b \u2028"));
        Assert.Equal("\U0001F3B5 song", SongKey.Normalize("\U0001F3B5  Song"));
        // U+0130 has no single-character lowercase: it stays as written.
        Assert.Equal("İ", SongKey.Normalize("İ"));
    }

    // ---- seeding ----

    [Fact]
    public void ASeededLookComesFromTheKeyAndIsTheSameEveryTime()
    {
        // 0xad75f4c29077a9de: the low byte 0xde is 222, 222 mod 8 = 6 (toxic); 0xde >> 3 = 27, 27 mod 4 = 3 (bold italic).
        var look = NowPlayingLooks.Seeded("ad75f4c29077a9de");
        Assert.Equal("toxic", look.Mood);
        Assert.Equal(["#b6ff00", "#00ff9c", "#00c3ff"], look.Ink);
        Assert.Equal(LookStyle.BoldItalic, look.Style);
        Assert.Equal(look, NowPlayingLooks.Seeded("ad75f4c29077a9de"));
    }

    [Fact]
    public void EveryPaletteAndEverySeedStyleIsReachableAndPlainNeverIs()
    {
        var palettes = new HashSet<string?>();
        var styles = new HashSet<LookStyle>();
        for (int i = 0; i < 64; i++)
        {
            var look = NowPlayingLooks.Seeded(i.ToString("x16", CultureInfo.InvariantCulture));
            palettes.Add(look.Mood);
            styles.Add(look.Style);
            Assert.Contains(look.Style, NowPlayingLooks.SeedStyles);
            Assert.Equal(3, look.Ink.Count);
        }
        Assert.Equal(NowPlayingLooks.Palettes.Select(p => p.Name).Order(), palettes.Order());
        Assert.Equal(NowPlayingLooks.SeedStyles.Order(), styles.Order());
        Assert.DoesNotContain(LookStyle.Plain, styles);
    }

    [Fact]
    public void TheEightPalettesAreTheModsAndSeededLooksPassTheirOwnChecks()
    {
        Assert.Equal(["synthwave", "sunset", "ocean", "aurora", "ember", "bubblegum", "toxic", "gold"], NowPlayingLooks.Palettes.Select(p => p.Name));
        Assert.Equal(["#ff2a6d", "#d16ba5", "#05d9e8"], NowPlayingLooks.Palettes[0].Stops);
        Assert.Equal(["#f7971e", "#ffd200", "#fff6b7"], NowPlayingLooks.Palettes[7].Stops);
        foreach (var p in NowPlayingLooks.Palettes)
            Assert.All(p.Stops, s => Assert.NotNull(InkHex.Normalize(s)));
    }

    [Fact]
    public void ResolveTakesTheSavedLookElseTheSeededOne()
    {
        var saved = new SongLook(SongKey.Of("Song", "Artist")!, "Song", "Artist", new NowPlayingLook(["#ff0000", "#00ff00"], LookStyle.Bold, "loud"));
        var book = NowPlayingLookBook.Empty.Merge([saved]);
        var hit = NowPlayingLooks.Resolve(book, " SONG - Remastered 2011", "artist")!.Value;
        Assert.Equal(LookSource.Saved, hit.Source);
        Assert.Equal(saved.Look, hit.Look);
        var miss = NowPlayingLooks.Resolve(book, "Another", "Artist")!.Value;
        Assert.Equal(LookSource.Seeded, miss.Source);
        Assert.Equal(NowPlayingLooks.Seeded(miss.Key), miss.Look);
        Assert.Null(NowPlayingLooks.Resolve(book, "  ", "Artist"));
    }

    [Fact]
    public void ColorAtWalksTheStopsLeftToRightAndDrawStopsCapsWhiteOnlyWhenAsked()
    {
        Assert.Equal("ff0000", NowPlayingLooks.ColorAt(["#ff0000", "#0000ff"], 0));
        Assert.Equal("0000ff", NowPlayingLooks.ColorAt(["#ff0000", "#0000ff"], 1));
        Assert.Equal("800080", NowPlayingLooks.ColorAt(["#ff0000", "#0000ff"], 0.5));
        Assert.Equal("00ff00", NowPlayingLooks.ColorAt(["#ff0000", "#00ff00", "#0000ff"], 0.5));
        Assert.Equal("123456", NowPlayingLooks.ColorAt(["#123456"], 0.7));
        Assert.Equal("0000ff", NowPlayingLooks.ColorAt(["#ff0000", "#0000ff"], 9));

        var white = new NowPlayingLook(["#ffffff", "#336699"], LookStyle.Plain, null);
        Assert.Equal(["ffffff", "336699"], NowPlayingLooks.DrawStops(white, capWhite: false));
        var capped = NowPlayingLooks.DrawStops(white, capWhite: true);
        Assert.NotEqual("ffffff", capped[0]);
        Assert.Equal("336699", capped[1]);
    }

    // ---- styles ----

    [Theory]
    [InlineData("plain", LookStyle.Plain)]
    [InlineData("bold", LookStyle.Bold)]
    [InlineData("Italic", LookStyle.Italic)]
    [InlineData("bolditalic", LookStyle.BoldItalic)]
    [InlineData("bold-italic", LookStyle.BoldItalic)]
    [InlineData("Bold Italic", LookStyle.BoldItalic)]
    [InlineData("smallcaps", LookStyle.SmallCaps)]
    [InlineData("small_caps", LookStyle.SmallCaps)]
    public void StyleWordsParseLoosely(string word, LookStyle expected) => Assert.Equal(expected, LookStyles.Parse(word));

    [Theory]
    [InlineData("sans")]
    [InlineData("fraktur")]
    [InlineData("")]
    [InlineData(null)]
    public void OtherStylesAreNotLookStyles(string? word) => Assert.Null(LookStyles.Parse(word));

    [Fact]
    public void SmallCapsMapsLettersAndTheOtherStylesLeaveTheTextToTheFont()
    {
        var small = LookStyles.Text("Song", LookStyle.SmallCaps);
        Assert.Equal(LetterMap.Map("Song", LetterStyle.SmallCaps), small);
        Assert.NotEqual("Song", small);
        Assert.StartsWith("S", small, StringComparison.Ordinal);   // a capital has no small capital here: it stays
        Assert.Equal("Song", LookStyles.Text("Song", LookStyle.Bold));
        Assert.Equal("Song", LookStyles.Text("Song", LookStyle.Plain));
        Assert.True(LookStyles.IsBold(LookStyle.BoldItalic) && LookStyles.IsItalic(LookStyle.BoldItalic));
        Assert.False(LookStyles.IsBold(LookStyle.Italic));
    }

    // ---- the checks ----

    private static string Entry(string extra = "", string title = "Song", string artist = "Artist", string ink = """["#ff2a6d","#05d9e8"]""") =>
        $$"""{"title":"{{title}}","artist":"{{artist}}","ink":{{ink}}{{extra}}}""";

    [Fact]
    public void AGoodLookIsReadAndNormalized()
    {
        var parsed = NowPlayingLookCheck.ParseEntries(J($$"""[{{Entry(""","style":"Bold-Italic","mood":"  neon night drive  " """, title: "  Song  ", ink: """["#FF2A6D","05d9e8","#fff"]""")}}]"""));
        Assert.Empty(parsed.Reasons);
        var one = Assert.Single(parsed.Looks);
        Assert.Equal("Song", one.Title);
        Assert.Equal(SongKey.Of("Song", "Artist"), one.Key);
        Assert.Equal(["#ff2a6d", "#05d9e8", "#ffffff"], one.Look.Ink);
        Assert.Equal(LookStyle.BoldItalic, one.Look.Style);
        Assert.Equal("neon night drive", one.Look.Mood);
    }

    [Fact]
    public void StyleAndMoodAreOptionalAndAnEmptyMoodIsNone()
    {
        var one = Assert.Single(NowPlayingLookCheck.ParseEntries(J($$"""[{{Entry(""","mood":" " """)}}]""")).Looks);
        Assert.Equal(LookStyle.Plain, one.Look.Style);
        Assert.Null(one.Look.Mood);
        Assert.Equal("", Assert.Single(NowPlayingLookCheck.ParseEntries(J("""[{"title":"Song","ink":["#ffffff"]}]""")).Looks).Artist);
    }

    [Theory]
    [InlineData("""[]""", "looks must be a list of 1 to 20 looks")]
    [InlineData("""{"title":"x"}""", "looks must be a list of 1 to 20 looks")]
    [InlineData("""["x"]""", "look 1 must be an object")]
    [InlineData("""[{"artist":"A","ink":["#ffffff"]}]""", "look 1 title must be one line")]
    [InlineData("""[{"title":"   ","ink":["#ffffff"]}]""", "look 1 title must be one line")]
    [InlineData("""[{"title":"a\nb","ink":["#ffffff"]}]""", "look 1 title must be one line")]
    [InlineData("""[{"title":"a\u0007b","ink":["#ffffff"]}]""", "look 1 title must be one line")]
    [InlineData("""[{"title":"a\u2028b","ink":["#ffffff"]}]""", "look 1 title must be one line")]
    [InlineData("""[{"title":"x","artist":5,"ink":["#ffffff"]}]""", "look 1 artist must be one line")]
    [InlineData("""[{"title":"x","artist":"a\nb","ink":["#ffffff"]}]""", "look 1 artist must be one line")]
    [InlineData("""[{"title":"x"}]""", "look 1 ink must be 1 to 4 hex colors")]
    [InlineData("""[{"title":"x","ink":[]}]""", "look 1 ink must be 1 to 4 hex colors")]
    [InlineData("""[{"title":"x","ink":"#ffffff"}]""", "look 1 ink must be 1 to 4 hex colors")]
    [InlineData("""[{"title":"x","ink":["#ffffff","#fffffe","#fffffd","#fffffc","#fffffb"]}]""", "look 1 ink must be 1 to 4 hex colors")]
    [InlineData("""[{"title":"x","ink":["red"]}]""", "look 1 ink must be 1 to 4 hex colors")]
    [InlineData("""[{"title":"x","ink":["#ffff"]}]""", "look 1 ink must be 1 to 4 hex colors")]
    [InlineData("""[{"title":"x","ink":["#gggggg"]}]""", "look 1 ink must be 1 to 4 hex colors")]
    [InlineData("""[{"title":"x","ink":[5]}]""", "look 1 ink must be 1 to 4 hex colors")]
    [InlineData("""[{"title":"x","ink":["#ffffff"],"style":"sans"}]""", "look 1 style must be one of: plain, bold, italic, bolditalic, smallcaps")]
    [InlineData("""[{"title":"x","ink":["#ffffff"],"style":3}]""", "look 1 style must be one of")]
    [InlineData("""[{"title":"x","ink":["#ffffff"],"mood":"a\nb"}]""", "look 1 mood must be one line of at most 40 characters")]
    [InlineData("""[{"title":"x","ink":["#ffffff"],"mood":7}]""", "look 1 mood must be one line")]
    [InlineData("""[{"title":"x","ink":["#ffffff"],"colour":"red"}]""", "look 1 has an unknown field: colour")]
    [InlineData("""[{"title":"...","ink":["#ffffff"]}]""", "")]
    public void HostileLooksAreRefusedWithAReason(string json, string reason)
    {
        var parsed = NowPlayingLookCheck.ParseEntries(J(json));
        if (reason.Length == 0)
        {
            Assert.Single(parsed.Looks);   // punctuation is a title; the check is the key, not the alphabet
            return;
        }
        Assert.Empty(parsed.Looks);
        Assert.Contains(parsed.Reasons, r => r.StartsWith(reason, StringComparison.Ordinal));
    }

    [Fact]
    public void TheLimitsAreExact()
    {
        string Title(int n) => new('t', n);
        Assert.Single(NowPlayingLookCheck.ParseEntries(J($$"""[{{Entry(title: Title(200))}}]""")).Looks);
        Assert.Contains(NowPlayingLookCheck.ParseEntries(J($$"""[{{Entry(title: Title(201))}}]""")).Reasons, r => r.Contains("title"));
        Assert.Single(NowPlayingLookCheck.ParseEntries(J($$"""[{{Entry(artist: Title(100))}}]""")).Looks);
        Assert.Contains(NowPlayingLookCheck.ParseEntries(J($$"""[{{Entry(artist: Title(101))}}]""")).Reasons, r => r.Contains("artist"));
        Assert.Single(NowPlayingLookCheck.ParseEntries(J($$"""[{{Entry($$""","mood":"{{Title(40)}}" """)}}]""")).Looks);
        Assert.Contains(NowPlayingLookCheck.ParseEntries(J($$"""[{{Entry($$""","mood":"{{Title(41)}}" """)}}]""")).Reasons, r => r.Contains("mood"));

        string Many(int n) => "[" + string.Join(",", Enumerable.Range(0, n).Select(i => Entry(title: "Song " + i))) + "]";
        Assert.Equal(20, NowPlayingLookCheck.ParseEntries(J(Many(20))).Looks.Count);
        Assert.Contains(NowPlayingLookCheck.ParseEntries(J(Many(21))).Reasons, r => r.Contains("1 to 20"));
    }

    [Fact]
    public void ASongListedTwiceKeepsItsLastLook()
    {
        var parsed = NowPlayingLookCheck.ParseEntries(J($$"""[{{Entry(title: "Song")}},{{Entry(title: " song ", ink: """["#00ff00"]""")}},{{Entry(title: "Other")}}]"""));
        Assert.Equal(2, parsed.Looks.Count);
        Assert.Equal(["#00ff00"], parsed.Looks.Single(l => l.Title == "song").Look.Ink);
    }

    [Fact]
    public void EveryReasonNamesItsLook()
    {
        var parsed = NowPlayingLookCheck.ParseEntries(J($$"""[{{Entry()}},{"title":"x","ink":["nope"]},{"ink":["nope"]}]"""));
        Assert.Empty(parsed.Looks);
        Assert.All(parsed.Reasons, r => Assert.StartsWith("look ", r));
        Assert.Contains(parsed.Reasons, r => r.StartsWith("look 2 ", StringComparison.Ordinal));
        Assert.Contains(parsed.Reasons, r => r.StartsWith("look 3 ", StringComparison.Ordinal));
        Assert.DoesNotContain(parsed.Reasons, r => r.StartsWith("look 1 ", StringComparison.Ordinal));
    }

    // ---- the contrast rule ----

    [Fact]
    public void AStopTooDarkForTheSpotIsRefusedAndSaysSo()
    {
        var ctx = new DeskDesignContext(Wallpaper: DeskDesignValidatorTests.Flat(0.02));
        var parsed = NowPlayingLookCheck.ParseEntries(J($$"""[{{Entry(ink: """["#ff2a6d","#202020"]""")}}]"""), ctx);
        Assert.Empty(parsed.Looks);
        var reason = Assert.Single(parsed.Reasons);
        Assert.StartsWith("look 1 color #202020 is too dark for this spot (", reason, StringComparison.Ordinal);
        Assert.Contains(":1, needs 3.0:1)", reason);
    }

    [Fact]
    public void AStopTooLightForABrightSpotSaysTooLight()
    {
        var ctx = new DeskDesignContext(Wallpaper: DeskDesignValidatorTests.Flat(0.95));
        var parsed = NowPlayingLookCheck.ParseEntries(J($$"""[{{Entry(ink: """["#ffffcc"]""")}}]"""), ctx);
        Assert.Contains("is too light for this spot", Assert.Single(parsed.Reasons));
    }

    [Fact]
    public void ABrightLookPassesADarkSpotAndTheUnknownIsNotJudged()
    {
        var dark = new DeskDesignContext(Wallpaper: DeskDesignValidatorTests.Flat(0.02));
        Assert.Single(NowPlayingLookCheck.ParseEntries(J($$"""[{{Entry()}}]"""), dark).Looks);
        // No wallpaper known, or no context: the rule waits for the widget.
        var dim = Entry(ink: """["#101010"]""");
        Assert.Single(NowPlayingLookCheck.ParseEntries(J($$"""[{{dim}}]"""), DeskDesignContext.Unknown).Looks);
        Assert.Single(NowPlayingLookCheck.ParseEntries(J($$"""[{{dim}}]""")).Looks);
    }

    [Fact]
    public void ThePlateIsWhatTheLooksAreJudgedAgainstWhenItIsOn()
    {
        // A dark ink puts a white plate behind the text: the spot is the wallpaper with that plate over it.
        var plated = DeskDesign.Parse(J("""{"ink":"dark","plate":true}""")).Design!;
        var wall = DeskDesignValidatorTests.Flat(0.0);
        var withPlate = new DeskDesignContext(Wallpaper: wall, Current: plated);
        Assert.Equal([DeskDesign.UnderPlate(0.0, darkInk: true)], NowPlayingLookCheck.Backgrounds(withPlate));
        Assert.Equal([0.0], NowPlayingLookCheck.Backgrounds(new DeskDesignContext(Wallpaper: wall)));
        // A mid grey reads on the bare dark wallpaper and not on the pale plate over it.
        string grey = Entry(ink: """["#707070"]""");
        Assert.Single(NowPlayingLookCheck.ParseEntries(J($$"""[{{grey}}]"""), new DeskDesignContext(Wallpaper: wall)).Looks);
        Assert.NotEmpty(NowPlayingLookCheck.ParseEntries(J($$"""[{{grey}}]"""), withPlate).Reasons);
        // Light ink puts a black plate behind it instead.
        var dark = new DeskDesignContext(Wallpaper: DeskDesignValidatorTests.Flat(0.9), Current: DeskDesign.Parse(J("""{"ink":"light","plate":true}""")).Design!);
        Assert.Equal([DeskDesign.UnderPlate(0.9, darkInk: false)], NowPlayingLookCheck.Backgrounds(dark));
    }

    [Fact]
    public void AHiddenPieceIsNotJudged()
    {
        var current = DeskDesign.Parse(J("""{"layout":{"nowPlaying":{"hidden":true}}}""")).Design!;
        var ctx = new DeskDesignContext(Wallpaper: DeskDesignValidatorTests.Flat(0.02), Current: current);
        Assert.Empty(NowPlayingLookCheck.Backgrounds(ctx));
        Assert.Single(NowPlayingLookCheck.ParseEntries(J($$"""[{{Entry(ink: """["#101010"]""")}}]"""), ctx).Looks);
    }

    [Fact]
    public void TheSpotIsTheAnchorOfTheNowPlayingPiece()
    {
        var wall = DeskDesignValidatorTests.Flat(0.9, new Dictionary<DeskAnchor, double> { [DeskAnchor.TR] = 0.02 });
        var atTopRight = DeskDesign.Parse(J("""{"layout":{"nowPlaying":{"anchor":"tr"}}}""")).Design!;
        var ctx = new DeskDesignContext(Wallpaper: wall, Current: atTopRight);
        Assert.Equal([0.02], NowPlayingLookCheck.Backgrounds(ctx));
        Assert.Single(NowPlayingLookCheck.ParseEntries(J($$"""[{{Entry()}}]"""), ctx).Looks);
    }

    // ---- the saved looks ----

    private static SongLook Song(string title, string artist = "A", string ink = "#ff2a6d") =>
        new(SongKey.Of(title, artist)!, title, artist, new NowPlayingLook([ink], LookStyle.Bold, "mood"));

    [Fact]
    public void TheBookRoundTripsAndKeepsOrder()
    {
        var book = NowPlayingLookBook.Empty.Merge([Song("One"), Song("Two", "B", "#05d9e8")]);
        var back = NowPlayingLookBook.Parse(book.ToJson());
        Assert.Equal(book.Looks, back.Looks);
        Assert.Equal(["One", "Two"], back.Looks.Select(l => l.Title));
        var text = Encoding.UTF8.GetString(book.ToJson());
        Assert.Contains("\"schema_version\": 1", text);
        Assert.Contains($"\"key\": \"{Song("One").Key}\"", text);
    }

    [Fact]
    public void ASongAlreadySavedTakesTheNewLookAndMovesToTheEnd()
    {
        var book = NowPlayingLookBook.Empty.Merge([Song("One"), Song("Two")]).Merge([Song("one", "a", "#00ff00")]);
        Assert.Equal(["Two", "one"], book.Looks.Select(l => l.Title));
        Assert.Equal(["#00ff00"], book.Find(Song("One").Key)!.Ink);
    }

    [Fact]
    public void PastFiveHundredTheOldestGo()
    {
        var songs = Enumerable.Range(0, 503).Select(i => Song("Song " + i)).ToList();
        var book = NowPlayingLookBook.Empty.Merge(songs);
        Assert.Equal(500, book.Count);
        Assert.Null(book.Find(songs[0].Key));
        Assert.Null(book.Find(songs[2].Key));
        Assert.NotNull(book.Find(songs[3].Key));
        Assert.NotNull(book.Find(songs[502].Key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"schema_version":2,"looks":[{"title":"a","ink":["#ffffff"]}]}""")]
    [InlineData("""{"schema_version":1}""")]
    [InlineData("""{"schema_version":1,"looks":"x"}""")]
    public void AFileThatIsNotALooksFileReadsAsNoLooks(string text) =>
        Assert.Equal(0, NowPlayingLookBook.Parse(Encoding.UTF8.GetBytes(text)).Count);

    [Fact]
    public void AnEntryThatFailsTheChecksIsSkippedAndTheKeyIsWorkedOutAgain()
    {
        var file = """
            {"schema_version":1,"looks":[
              {"key":"0000000000000000","title":"Song","artist":"A","ink":["#ff2a6d"],"style":"bold"},
              {"key":"x","title":"Bad","artist":"A","ink":["nope"]},
              "junk",
              {"title":"Other","ink":["#05d9e8"],"mood":"m"}
            ]}
            """;
        var book = NowPlayingLookBook.Parse(Encoding.UTF8.GetBytes(file));
        Assert.Equal(["Song", "Other"], book.Looks.Select(l => l.Title));
        Assert.Equal(SongKey.Of("Song", "A"), book.Looks[0].Key);
        Assert.NotNull(book.Find(SongKey.Of("song", "a")!));
    }

    // ---- the request, the result and the files for the mod ----

    [Fact]
    public void ARequestRoundTripsThroughTheWidgetsDecoder()
    {
        var looks = new[] { Song("One"), Song("Two", "B") };
        var json = NowPlayingLookProposal.RequestJson("id1", T0, looks, "  for you  ").ToJsonString();
        var decoded = NowPlayingLookProposal.Decode(Encoding.UTF8.GetBytes(json), T0);
        Assert.Equal(MessageDecodedKind.Proposal, decoded.Kind);
        Assert.Equal("id1", decoded.Id);
        Assert.Equal(looks, decoded.Proposal!.Looks);
        Assert.Equal("for you", decoded.Proposal.Note);
        Assert.Equal("Claude suggested looks for 2 songs", decoded.Proposal.Title);
    }

    [Fact]
    public void ARequestIsDroppedWhenUnreadableWithoutAnIdOrOlderThanTenMinutes()
    {
        var good = NowPlayingLookProposal.RequestJson("id1", T0, [Song("One")], null).ToJsonString();
        Assert.Equal(MessageDecodedKind.Discard, NowPlayingLookProposal.Decode(Encoding.UTF8.GetBytes("nope"), T0).Kind);
        Assert.Equal(MessageDecodedKind.Discard, NowPlayingLookProposal.Decode(Encoding.UTF8.GetBytes("[]"), T0).Kind);
        Assert.Equal(MessageDecodedKind.Discard, NowPlayingLookProposal.Decode(Encoding.UTF8.GetBytes(good.Replace("\"id1\"", "\"\"")), T0).Kind);
        Assert.Equal(MessageDecodedKind.Discard, NowPlayingLookProposal.Decode(Encoding.UTF8.GetBytes(good), T0.AddSeconds(601)).Kind);
        Assert.Equal(MessageDecodedKind.Proposal, NowPlayingLookProposal.Decode(Encoding.UTF8.GetBytes(good), T0.AddSeconds(600)).Kind);
    }

    [Fact]
    public void ABadRequestIsRefusedByIdWithItsReasons()
    {
        var bad = NowPlayingLookProposal.RequestJson("id9", T0, [Song("One")], null);
        bad["looks"]![0]!["ink"] = new JsonArray("nope");
        var decoded = NowPlayingLookProposal.Decode(Encoding.UTF8.GetBytes(bad.ToJsonString()), T0);
        Assert.Equal(MessageDecodedKind.Refused, decoded.Kind);
        Assert.Equal("id9", decoded.Id);
        Assert.Contains("look 1 ink must be", decoded.Reasons[0]);

        var v2 = NowPlayingLookProposal.RequestJson("id9", T0, [Song("One")], null);
        v2["schema_version"] = 2;
        Assert.Equal(MessageDecodedKind.Refused, NowPlayingLookProposal.Decode(Encoding.UTF8.GetBytes(v2.ToJsonString()), T0).Kind);

        var noDate = NowPlayingLookProposal.RequestJson("id9", T0, [Song("One")], null);
        noDate["requested_at"] = "yesterday";
        Assert.Equal(MessageDecodedKind.Refused, NowPlayingLookProposal.Decode(Encoding.UTF8.GetBytes(noDate.ToJsonString()), T0).Kind);

        var longNote = NowPlayingLookProposal.RequestJson("id9", T0, [Song("One")], new string('n', 201));
        Assert.Contains("note must be one line", NowPlayingLookProposal.Decode(Encoding.UTF8.GetBytes(longNote.ToJsonString()), T0).Reasons[0]);
    }

    [Fact]
    public void TheWidgetAppliesTheContrastRuleAgainAndRefusesWhatTheServerCouldNotJudge()
    {
        var json = NowPlayingLookProposal.RequestJson("id1", T0, [Song("One", ink: "#101010")], null).ToJsonString();
        var ctx = new DeskDesignContext(Wallpaper: DeskDesignValidatorTests.Flat(0.02));
        Assert.Equal(MessageDecodedKind.Proposal, NowPlayingLookProposal.Decode(Encoding.UTF8.GetBytes(json), T0).Kind);
        var refused = NowPlayingLookProposal.Decode(Encoding.UTF8.GetBytes(json), T0, ctx);
        Assert.Equal(MessageDecodedKind.Refused, refused.Kind);
        Assert.Contains("too dark for this spot", refused.Reasons[0]);
    }

    [Fact]
    public void TheResultCarriesStyleOnInEveryAnswerWithSortedKeys()
    {
        var applied = JsonNode.Parse(NowPlayingLookProposal.ResultJson("id1", MessageProposalStatus.Applied, styleOn: true, saved: 3, now: T0))!;
        Assert.Equal("id1", (string?)applied["id"]);
        Assert.Equal("applied", (string?)applied["result"]!["status"]);
        Assert.Equal(3, (int)applied["result"]!["looks_saved"]!);
        Assert.True((bool)applied["result"]!["style_on"]!);
        Assert.Equal("2026-10-21T12:00:00.000Z", (string?)applied["completed_at"]);
        Assert.Equal(["looks_saved", "status", "style_on"], ((JsonObject)applied["result"]!).Select(kv => kv.Key));

        var pending = JsonNode.Parse(NowPlayingLookProposal.ResultJson("id1", MessageProposalStatus.PendingApproval, styleOn: false, now: T0))!;
        Assert.Equal("pending_approval", (string?)pending["result"]!["status"]);
        Assert.False((bool)pending["result"]!["style_on"]!);
        Assert.Null(pending["result"]!["looks_saved"]);

        var rejected = JsonNode.Parse(NowPlayingLookProposal.ResultJson("id1", MessageProposalStatus.Rejected, true, ["dismissed by the user"], now: T0))!;
        Assert.Equal("dismissed by the user", (string?)rejected["result"]!["reasons"]![0]);
    }

    [Fact]
    public void TheCurrentLookFileHasTheKeyTheLookAndTheSourceAndNoTitleOrArtist()
    {
        var look = new NowPlayingLook(["#ff2a6d", "#05d9e8"], LookStyle.SmallCaps, "neon");
        var text = Encoding.UTF8.GetString(NowPlayingLookProposal.CurrentJson("ad75f4c29077a9de", look, LookSource.Seeded));
        Assert.Equal(
            """{"schema_version":1,"key":"ad75f4c29077a9de","look":{"ink":["#ff2a6d","#05d9e8"],"style":"smallcaps","mood":"neon"},"source":"seeded"}""",
            text);
        Assert.DoesNotContain("title", text);
        Assert.DoesNotContain("artist", text);
        Assert.Contains("\"source\":\"saved\"", Encoding.UTF8.GetString(NowPlayingLookProposal.CurrentJson("k", look, LookSource.Saved)));
        Assert.DoesNotContain("mood", Encoding.UTF8.GetString(NowPlayingLookProposal.CurrentJson("k", look with { Mood = null }, LookSource.Saved)));
    }

    [Fact]
    public void TheModsOfferIsReadWhenItsKeyIsTheKeyOfItsSong()
    {
        string key = SongKey.Of("Song", "Artist")!;
        string offer = $$$"""{"key":"{{{key}}}","title":"Song","artist":"Artist","look":{"ink":["#ff2a6d","#05d9e8"],"style":"italic","mood":"m"}}""";
        var (look, reasons) = NowPlayingLookProposal.DecodeOffer(Encoding.UTF8.GetBytes(offer));
        Assert.Empty(reasons);
        Assert.Equal(key, look!.Key);
        Assert.Equal(LookStyle.Italic, look.Look.Style);

        Assert.Null(NowPlayingLookProposal.DecodeOffer(Encoding.UTF8.GetBytes(offer.Replace(key, "0123456789abcdef"))).Look);   // a key from another rule
        Assert.Null(NowPlayingLookProposal.DecodeOffer(Encoding.UTF8.GetBytes(offer.Replace($"\"key\":\"{key}\",", ""))).Look);
        Assert.Null(NowPlayingLookProposal.DecodeOffer(Encoding.UTF8.GetBytes(offer.Replace("\"ink\":[\"#ff2a6d\",\"#05d9e8\"]", "\"ink\":[\"x\"]"))).Look);
        Assert.Null(NowPlayingLookProposal.DecodeOffer(Encoding.UTF8.GetBytes("nope")).Look);
        Assert.Null(NowPlayingLookProposal.DecodeOffer(Encoding.UTF8.GetBytes("""{"key":"k","title":"x"}""")).Look);
        Assert.Equal(key, NowPlayingLookProposal.DecodeOffer(Encoding.UTF8.GetBytes(offer.Replace("{\"key\"", "{\"schema_version\":1,\"key\""))).Look!.Key);
        Assert.Null(NowPlayingLookProposal.DecodeOffer(Encoding.UTF8.GetBytes(offer.Replace("{\"key\"", "{\"schema_version\":2,\"key\""))).Look);
    }

    [Fact]
    public void TheFileNamesAreTheContract()
    {
        Assert.Equal("now-playing-looks-request.json", NowPlayingLookProposal.RequestFile);
        Assert.Equal("now-playing-looks-result.json", NowPlayingLookProposal.ResultFile);
        Assert.Equal("now-playing-look.json", NowPlayingLookProposal.CurrentFile);
        Assert.Equal("now-playing-look-offer.json", NowPlayingLookProposal.OfferFile);
        Assert.Equal("now-playing-looks.json", NowPlayingLookProposal.StoreFile);
    }

    [Fact]
    public void ADesignCarriesLooksThroughParseJsonAndMerge()
    {
        var design = DeskDesign.Parse(J($$"""{"name":"Neon","looks":[{{Entry()}}]}""")).Design!;
        Assert.Single(design.Looks!);
        Assert.Equal("Song", design.ToJson()["looks"]![0]!["title"]!.GetValue<string>());
        var merged = DeskDesign.Merge(null, design);
        Assert.Equal(design.Looks, merged.Looks);
        Assert.Contains("Song looks: 1 song styled for now playing", DeskDesign.Diff(new DeskDesign(), merged));
        var again = DeskDesign.Parse(design.ToJson());
        Assert.Equal(design.Looks, again.Design!.Looks);
    }

    [Fact]
    public void ADesignsLooksAreRefusedByTheSameChecksAndNoLongerAsUnsupported()
    {
        var parsed = DeskDesign.Parse(J("""{"looks":[{"title":"x","ink":["nope"]}]}"""));
        Assert.Null(parsed.Design);
        var reason = Assert.Single(parsed.Reasons);
        Assert.Equal("looks", reason.Field);
        Assert.Contains("look 1 ink must be", reason.Message);
        Assert.DoesNotContain(DeskDesign.Parse(J("""{"looks":[]}""")).Reasons, r => r.Message.Contains("not supported"));
        Assert.Contains("looks must be a list", DeskDesign.Parse(J("""{"looks":[]}""")).Reasons[0].Message);
    }

    [Fact]
    public void ADesignsLooksMeetTheContrastRuleOnTheDeskItLeavesBehind()
    {
        var ctx = new DeskDesignContext(Wallpaper: DeskDesignValidatorTests.Flat(0.02));
        var verdict = DeskDesign.Validate(J($$"""{"looks":[{{Entry(ink: """["#101010"]""")}}]}"""), ctx);
        Assert.Equal(DeskDesignStatus.Rejected, verdict.Status);
        Assert.Equal("looks", verdict.Reasons[0].Field);
        Assert.Contains("too dark for this spot", verdict.Reasons[0].Message);
        Assert.True(DeskDesign.Validate(J($$"""{"looks":[{{Entry()}}]}"""), ctx).Ok);
        // The spot is where the design puts the piece: a pale look is fine at the dark default spot and fails once the piece moves onto a bright one.
        var brightTopLeft = new DeskDesignContext(Wallpaper: DeskDesignValidatorTests.Flat(0.02, new Dictionary<DeskAnchor, double> { [DeskAnchor.TL] = 0.95 }));
        string pale = Entry(ink: """["#ffffcc"]""");
        Assert.True(DeskDesign.Validate(J($$"""{"looks":[{{pale}}]}"""), brightTopLeft).Ok);
        var moved = DeskDesign.Validate(J($$$"""{"layout":{"nowPlaying":{"anchor":"tl"}},"looks":[{{{pale}}}]}"""), brightTopLeft);
        Assert.False(moved.Ok);
        Assert.Contains("too light for this spot", moved.Reasons[0].Message);
    }

    // ---- the settings ----

    [Fact]
    public void TheLooksSwitchesAreOffByDefaultAndWrittenOnlyWhileOn()
    {
        var off = NowPlayingSettings.Decode(null);
        Assert.False(off.Style);
        Assert.False(off.LooksDirect);
        Assert.DoesNotContain(NowPlayingSettings.Default.Encode(), kv => kv.Key is "style" or "looks_direct");

        var on = (off with { Style = true, LooksDirect = true }).Encode();
        Assert.True((bool)on["style"]!);
        Assert.True((bool)on["looks_direct"]!);
        var back = NowPlayingSettings.Decode(on);
        Assert.True(back.Style && back.LooksDirect);
        Assert.True(back.Bars);   // the other members ride along

        var odd = NowPlayingSettings.Decode(J("""{"style":"yes","looks_direct":1}"""));
        Assert.False(odd.Style || odd.LooksDirect);
    }
}
