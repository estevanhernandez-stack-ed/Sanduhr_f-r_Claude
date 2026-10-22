using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Claude's suggested song looks, the file half: the request and the mod's offer in, the card, the saved looks, the
/// result and the current-look file out. Temp folders only.</summary>
public sealed class NowPlayingLookHandoffTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-looks-" + Guid.NewGuid().ToString("N"));
    private readonly NowPlayingLookPaths _paths;

    public NowPlayingLookHandoffTests()
    {
        _paths = new NowPlayingLookPaths(Path.Combine(_root, "ext"), Path.Combine(_root, "app", NowPlayingLookProposal.StoreFile));
        Directory.CreateDirectory(_paths.Support);
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.Store)!);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private NowPlayingLookHandoff Handoff(bool style = true, bool direct = false, Func<bool>? canWrite = null, DeskDesignContext? context = null) =>
        new(_paths, () => style, () => direct, () => context, () => T0, canWrite);

    private static SongLook Song(string title, string artist = "Artist", string ink = "#ff2a6d") =>
        new(SongKey.Of(title, artist)!, title, artist, new NowPlayingLook([ink, "#05d9e8"], LookStyle.Italic, "m"));

    private static byte[] Request(string id = "req1", DateTimeOffset? at = null, string? note = null, params SongLook[] looks) =>
        Encoding.UTF8.GetBytes(NowPlayingLookProposal.RequestJson(id, at ?? T0, looks.Length == 0 ? [Song("One")] : looks, note).ToJsonString());

    private static byte[] Offer(SongLook s) =>
        Encoding.UTF8.GetBytes(new JsonObject { ["key"] = s.Key, ["title"] = s.Title, ["artist"] = s.Artist, ["look"] = s.Look.ToJson() }.ToJsonString());

    private JsonObject? Result() => File.Exists(_paths.Result) ? (JsonObject)JsonNode.Parse(File.ReadAllBytes(_paths.Result))!["result"]! : null;

    private string? Status() => (string?)Result()?["status"];

    // ---- a request from the tool ----

    [Fact]
    public void ARequestWaitsForSaveThenIsSavedAndAnswered()
    {
        var h = Handoff();
        var arrived = new List<string>();
        int changed = 0, bookChanged = 0;
        h.SuggestionReceived += p => arrived.Add(p.Id);
        h.PendingChanged += () => changed++;
        h.BookChanged += () => bookChanged++;
        File.WriteAllBytes(_paths.Request, Request(note: "for you", looks: [Song("One"), Song("Two")]));

        h.Check();

        Assert.False(File.Exists(_paths.Request));   // read once, removed
        Assert.Equal("req1", h.Pending?.Id);
        Assert.Equal("for you", h.Pending?.Note);
        Assert.Equal(["req1"], arrived);
        Assert.Equal("pending_approval", Status());
        Assert.True((bool)Result()!["style_on"]!);
        Assert.False(File.Exists(_paths.Store));
        Assert.Equal(0, h.Book.Count);

        h.Approve();

        Assert.Null(h.Pending);
        Assert.Equal(2, h.Book.Count);
        Assert.Equal("applied", Status());
        Assert.Equal(2, (int)Result()!["looks_saved"]!);
        Assert.Equal(2, NowPlayingLookStore.Load(_paths.Store).Count);
        Assert.Equal(2, changed);   // arrived, decided
        Assert.Equal(1, bookChanged);
    }

    [Fact]
    public void DismissAnswersRejectedAndSavesNothing()
    {
        var h = Handoff();
        h.Receive(Request());
        h.Dismiss();
        Assert.Null(h.Pending);
        Assert.Equal("rejected", Status());
        Assert.Equal("dismissed by the user", (string?)Result()!["reasons"]![0]);
        Assert.False(File.Exists(_paths.Store));
        h.Dismiss();   // nothing waits: nothing happens
        h.Approve();
        Assert.Equal(0, h.Book.Count);
    }

    [Fact]
    public void LetClaudeStyleSongsDirectlySavesAtOnceWithNoCard()
    {
        var h = Handoff(direct: true);
        h.Receive(Request(looks: [Song("One")]));
        Assert.Null(h.Pending);
        Assert.Equal("applied", Status());
        Assert.Equal(1, (int)Result()!["looks_saved"]!);
        Assert.Equal(1, h.Book.Count);
    }

    [Fact]
    public void EveryAnswerSaysWhetherStyleIsOn()
    {
        foreach (bool style in new[] { true, false })
        {
            var h = Handoff(style: style, direct: true);
            h.Receive(Request());
            Assert.Equal(style, (bool)Result()!["style_on"]!);
            File.Delete(_paths.Result);
            var refused = Handoff(style: style);
            refused.Receive(Encoding.UTF8.GetBytes(NowPlayingLookProposal.RequestJson("bad", T0, [Song("One")], null).ToJsonString().Replace("#ff2a6d", "nope")));
            Assert.Equal("rejected", Status());
            Assert.Equal(style, (bool)Result()!["style_on"]!);
            File.Delete(_paths.Result);
        }
    }

    [Fact]
    public void ARequestThatBreaksARuleIsRefusedByIdAndNeverShown()
    {
        var h = Handoff();
        var bad = NowPlayingLookProposal.RequestJson("bad1", T0, [Song("One")], null);
        bad["looks"]![0]!["style"] = "fraktur";
        h.Receive(Encoding.UTF8.GetBytes(bad.ToJsonString()));
        Assert.Null(h.Pending);
        Assert.Equal("rejected", Status());
        Assert.Contains("style must be one of", (string?)Result()!["reasons"]![0]);
        Assert.Equal("bad1", (string?)JsonNode.Parse(File.ReadAllBytes(_paths.Result))!["id"]);
    }

    [Fact]
    public void TheWidgetChecksTheContrastItsOwnContextKnows()
    {
        var h = Handoff(context: new DeskDesignContext(Wallpaper: DeskDesignValidatorTests.Flat(0.02)));
        h.Receive(Request(looks: [Song("Dim", ink: "#101010")]));
        Assert.Null(h.Pending);
        Assert.Equal("rejected", Status());
        Assert.Contains("too dark for this spot", (string?)Result()!["reasons"]![0]);
    }

    [Fact]
    public void AStaleOrUnreadableRequestGetsNoAnswerAtAll()
    {
        var h = Handoff();
        h.Receive(Request(at: T0.AddMinutes(-11)));
        h.Receive(Encoding.UTF8.GetBytes("nope"));
        Assert.Null(h.Pending);
        Assert.False(File.Exists(_paths.Result));
    }

    [Fact]
    public void ANewerRequestReplacesAnOlderOneStillWaiting()
    {
        var h = Handoff();
        h.Receive(Request("first", looks: [Song("One")]));
        h.Receive(Request("second", looks: [Song("Two")]));
        Assert.Equal("second", h.Pending?.Id);
        Assert.Equal(["Two"], h.Pending!.Looks.Select(l => l.Title));
        h.Approve();
        Assert.Equal(["Two"], h.Book.Looks.Select(l => l.Title));
    }

    [Fact]
    public void AfterResetNothingIsSaved()
    {
        bool allowed = true;
        var h = Handoff(canWrite: () => allowed);
        h.Receive(Request());
        allowed = false;
        File.Delete(_paths.Result);
        h.Approve();
        Assert.False(File.Exists(_paths.Store));
        Assert.False(File.Exists(_paths.Result));
        Assert.Equal(0, h.Book.Count);
        Assert.False(h.WriteCurrent("k", Song("One").Look, LookSource.Saved));
        Assert.False(File.Exists(_paths.Current));
    }

    [Fact]
    public void ASaveThatCannotBeWrittenIsRejectedNotLost()
    {
        var h = Handoff(direct: true);
        Directory.CreateDirectory(_paths.Store);   // a folder where the file goes
        h.Receive(Request());
        Assert.Equal("rejected", Status());
        Assert.Equal("the saved looks could not be written", (string?)Result()!["reasons"]![0]);
        Assert.Equal(0, h.Book.Count);
    }

    // ---- the mod's offer ----

    [Fact]
    public void AnOfferIsAProposalWithNoIdAndNoAnswer()
    {
        var h = Handoff();
        File.WriteAllBytes(_paths.Offer, Offer(Song("Offered")));
        h.Check();
        Assert.False(File.Exists(_paths.Offer));
        Assert.True(h.Pending!.IsOffer);
        Assert.Equal(["Offered"], h.Pending.Looks.Select(l => l.Title));
        Assert.False(File.Exists(_paths.Result));
        h.Approve();
        Assert.Equal(["Offered"], h.Book.Looks.Select(l => l.Title));
        Assert.False(File.Exists(_paths.Result));
    }

    [Fact]
    public void OffersJoinTheCardThatWaitsAndASongKeepsItsLatestOffer()
    {
        var h = Handoff();
        h.ReceiveOffer(Offer(Song("One")), T0);
        h.ReceiveOffer(Offer(Song("Two")), T0);
        h.ReceiveOffer(Offer(Song("one", "artist", "#00ff00")), T0);
        Assert.Equal(["Two", "one"], h.Pending!.Looks.Select(l => l.Title));
        h.Dismiss();
        Assert.False(File.Exists(_paths.Result));   // an offer has nobody waiting for an answer
    }

    [Fact]
    public void AnOfferJoinsAClaudeRequestsCard()
    {
        var h = Handoff();
        h.Receive(Request("req", looks: [Song("One")]));
        h.ReceiveOffer(Offer(Song("Two")), T0);
        Assert.Equal("req", h.Pending!.Id);
        Assert.False(h.Pending.IsOffer);
        Assert.Equal(["One", "Two"], h.Pending.Looks.Select(l => l.Title));
    }

    [Fact]
    public void TheCardTakesNoMoreThanTwentySongs()
    {
        var h = Handoff();
        for (int i = 0; i < 25; i++) h.ReceiveOffer(Offer(Song("Song " + i)), T0);
        Assert.Equal(20, h.Pending!.Looks.Count);
        Assert.Equal("Song 24", h.Pending.Looks[^1].Title);
        Assert.Equal("Song 5", h.Pending.Looks[0].Title);
    }

    [Fact]
    public void WithClaudeDirectAnOfferIsSavedAtOnce()
    {
        var h = Handoff(direct: true);
        h.ReceiveOffer(Offer(Song("Offered")), T0);
        Assert.Null(h.Pending);
        Assert.Equal(["Offered"], h.Book.Looks.Select(l => l.Title));
        Assert.False(File.Exists(_paths.Result));
    }

    [Fact]
    public void AnOfferFromTheOlderThanTenMinutesOrWithTheWrongKeyIsDropped()
    {
        var h = Handoff();
        h.ReceiveOffer(Offer(Song("Old")), T0.AddMinutes(-11));
        var wrongKey = new JsonObject { ["key"] = "0123456789abcdef", ["title"] = "Song", ["artist"] = "Artist", ["look"] = Song("x").Look.ToJson() };
        h.ReceiveOffer(Encoding.UTF8.GetBytes(wrongKey.ToJsonString()), T0);
        h.ReceiveOffer(Encoding.UTF8.GetBytes("nope"), T0);
        Assert.Null(h.Pending);
    }

    // ---- an offer written in place (the mod's engine cannot rename) ----

    /// <summary>A handoff whose clock the test moves, so the age of the offer on disk is whatever the test says.</summary>
    private NowPlayingLookHandoff Clocked(Func<DateTimeOffset> now, bool direct = false) => new(_paths, () => true, () => direct, () => null, now);

    private DateTimeOffset Written() => new(File.GetLastWriteTimeUtc(_paths.Offer), TimeSpan.Zero);

    [Fact]
    public void ATruncatedOfferSurvivesTheWatcherAndIsConsumedOnceCompleted()
    {
        var whole = Offer(Song("Offered"));
        var now = DateTimeOffset.UtcNow;
        var h = Clocked(() => now);
        var retries = new List<TimeSpan>();
        h.OfferRetryWanted += retries.Add;

        // The writer is half way: a watcher event fires and finds half of the JSON.
        File.WriteAllBytes(_paths.Offer, whole[..(whole.Length / 2)]);
        now = Written().AddMilliseconds(300);
        h.Check();
        Assert.True(File.Exists(_paths.Offer));   // not taken
        Assert.Null(h.Pending);
        Assert.Equal([NowPlayingLookHandoff.OfferRetryAfter], retries);

        // Another event while it is still half there: still kept.
        h.Check();
        Assert.True(File.Exists(_paths.Offer));
        Assert.Equal(2, retries.Count);

        // The writer finishes; the next look consumes it, once.
        File.WriteAllBytes(_paths.Offer, whole);
        now = Written().AddMilliseconds(100);
        h.Check();
        Assert.False(File.Exists(_paths.Offer));
        Assert.Equal(["Offered"], h.Pending!.Looks.Select(l => l.Title));
        Assert.True(h.Pending.IsOffer);
        Assert.Equal(2, retries.Count);   // no retry for a good offer
        h.Check();
        Assert.Single(h.Pending.Looks);
    }

    [Fact]
    public void AnEmptyJustCreatedOfferIsKeptToo()
    {
        var now = DateTimeOffset.UtcNow;
        var h = Clocked(() => now);
        File.WriteAllBytes(_paths.Offer, []);
        now = Written();
        h.Check();
        Assert.True(File.Exists(_paths.Offer));
        Assert.Null(h.Pending);
    }

    [Fact]
    public void AnOfferHeldOpenByTheWriterIsKeptNotDropped()
    {
        var now = DateTimeOffset.UtcNow;
        var h = Clocked(() => now);
        var retries = new List<TimeSpan>();
        h.OfferRetryWanted += retries.Add;
        var whole = Offer(Song("Offered"));
        using (var writer = new FileStream(_paths.Offer, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            writer.Write(whole, 0, whole.Length / 2);
            writer.Flush();
            now = Written().AddMilliseconds(100);
            h.Check();   // cannot even be opened for reading
        }
        Assert.True(File.Exists(_paths.Offer));
        Assert.Null(h.Pending);
        Assert.Single(retries);
    }

    [Fact]
    public void AnOfferThatNeverParsesIsRemovedOnceItIsTwoSecondsOld()
    {
        var now = DateTimeOffset.UtcNow;
        var h = Clocked(() => now);
        File.WriteAllText(_paths.Offer, "{\"key\":\"abc");
        now = Written().AddMilliseconds(1_999);
        h.Check();
        Assert.True(File.Exists(_paths.Offer));
        now = Written().AddSeconds(2);
        h.Check();
        Assert.False(File.Exists(_paths.Offer));
        Assert.Null(h.Pending);
    }

    [Fact]
    public void AnUnreadableOfferIsRemovedAfterAFewTriesWhateverTheClocksSay()
    {
        var now = DateTimeOffset.UtcNow;
        var h = Clocked(() => now);
        var retries = 0;
        h.OfferRetryWanted += _ => retries++;
        File.WriteAllText(_paths.Offer, "{\"key\":");
        now = Written().AddHours(-1);   // the clock says it is from the future, so it always looks new
        for (int i = 0; i < NowPlayingLookHandoff.MaxOfferTries; i++) h.Check();
        Assert.True(File.Exists(_paths.Offer));
        Assert.Equal(NowPlayingLookHandoff.MaxOfferTries, retries);
        h.Check();
        Assert.False(File.Exists(_paths.Offer));
    }

    [Fact]
    public void AnOfferTooLargeIsRemovedUnread()
    {
        var now = DateTimeOffset.UtcNow;
        var h = Clocked(() => now);
        var big = new JsonObject { ["key"] = "k", ["title"] = new string('t', NowPlayingLookProposal.MaxOfferBytes) };
        File.WriteAllText(_paths.Offer, big.ToJsonString());
        now = Written();
        h.Check();
        Assert.False(File.Exists(_paths.Offer));
        Assert.Null(h.Pending);
    }

    [Fact]
    public void AWholeOfferTheChecksRefuseIsStillRemoved()
    {
        var now = DateTimeOffset.UtcNow;
        var h = Clocked(() => now);
        var wrongKey = new JsonObject { ["key"] = "0123456789abcdef", ["title"] = "Song", ["artist"] = "Artist", ["look"] = Song("x").Look.ToJson() };
        File.WriteAllText(_paths.Offer, wrongKey.ToJsonString());
        now = Written();
        h.Check();
        Assert.False(File.Exists(_paths.Offer));
        Assert.Null(h.Pending);
    }

    [Fact]
    public void RequestsAreStillTakenAtOnceWhetherOrNotTheyParse()
    {
        var h = Handoff();
        File.WriteAllText(_paths.Request, "{\"id\":");
        h.Check();
        Assert.False(File.Exists(_paths.Request));
    }

    // ---- Clear Looks and the file for the mod ----

    [Fact]
    public void ClearLooksRemovesTheFileAndEmptiesTheBook()
    {
        var h = Handoff(direct: true);
        h.Receive(Request(looks: [Song("One"), Song("Two")]));
        Assert.True(File.Exists(_paths.Store));
        int changed = 0;
        h.BookChanged += () => changed++;
        h.Clear();
        Assert.False(File.Exists(_paths.Store));
        Assert.Equal(0, h.Book.Count);
        Assert.Equal(1, changed);
        Assert.Equal(0, Handoff().Book.Count);
    }

    [Fact]
    public void ABookSavedBeforeIsReadWhenTheHandoffStarts()
    {
        var first = Handoff(direct: true);
        first.Receive(Request(looks: [Song("One")]));
        Assert.Equal(["One"], Handoff().Book.Looks.Select(l => l.Title));
    }

    [Fact]
    public void SavingFromADesignMergesIntoTheBook()
    {
        var h = Handoff();
        Assert.True(h.Save([Song("One")]));
        Assert.True(h.Save([Song("Two"), Song("one", "artist", "#00ff00")]));
        Assert.Equal(["Two", "one"], h.Book.Looks.Select(l => l.Title));
    }

    [Fact]
    public void TheCurrentLookFileIsWrittenOnlyWhenItChangesAndRemovedOnRequest()
    {
        var h = Handoff();
        var look = Song("One").Look;
        Assert.True(h.WriteCurrent("ad75f4c29077a9de", look, LookSource.Seeded));
        var first = File.GetLastWriteTimeUtc(_paths.Current);
        var text = File.ReadAllText(_paths.Current);
        Assert.Contains("\"key\":\"ad75f4c29077a9de\"", text);
        Assert.Contains("\"source\":\"seeded\"", text);
        Assert.DoesNotContain("One", text);
        Assert.DoesNotContain("Artist", text);

        File.SetLastWriteTimeUtc(_paths.Current, first.AddMinutes(-5));
        var stamped = File.GetLastWriteTimeUtc(_paths.Current);
        Assert.True(h.WriteCurrent("ad75f4c29077a9de", look, LookSource.Seeded));
        Assert.Equal(stamped, File.GetLastWriteTimeUtc(_paths.Current));   // same bytes: not rewritten

        Assert.True(h.WriteCurrent("ad75f4c29077a9de", look, LookSource.Saved));
        Assert.Contains("\"source\":\"saved\"", File.ReadAllText(_paths.Current));
        Assert.Empty(Directory.GetFiles(_paths.Support, "*.tmp"));

        h.DeleteCurrent();
        Assert.False(File.Exists(_paths.Current));
        h.DeleteCurrent();   // already gone: fine
        Assert.True(h.WriteCurrent("ad75f4c29077a9de", look, LookSource.Saved));   // and it comes back
        Assert.True(File.Exists(_paths.Current));
    }

    [Fact]
    public void TheStoreFileNeverHoldsAnythingButKeptLooks()
    {
        var h = Handoff();
        h.Receive(Request(looks: [Song("Kept")]));
        Assert.False(File.Exists(_paths.Store));   // a suggestion nobody saved leaves no title on disk
        h.Approve();
        var text = File.ReadAllText(_paths.Store);
        Assert.Contains("Kept", text);
        Assert.Equal(_paths.Store, Path.Combine(_root, "app", "now-playing-looks.json"));
    }
}
