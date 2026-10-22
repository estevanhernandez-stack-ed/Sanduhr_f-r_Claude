namespace Sanduhr.Core;

/// <summary>Where the looks files live: the external folder (<c>%USERPROFILE%\.sanduhr</c>) for the request, result, current-look and
/// offer files, and the app's own folder for the saved looks.</summary>
/// <param name="Support">The external folder.</param>
/// <param name="Store">now-playing-looks.json.</param>
public sealed record NowPlayingLookPaths(string Support, string Store)
{
    public string Request => Path.Combine(Support, NowPlayingLookProposal.RequestFile);
    public string Result => Path.Combine(Support, NowPlayingLookProposal.ResultFile);
    public string Current => Path.Combine(Support, NowPlayingLookProposal.CurrentFile);
    public string Offer => Path.Combine(Support, NowPlayingLookProposal.OfferFile);
}

/// <summary>The saved looks file: read as a <see cref="NowPlayingLookBook"/>, written through a temp sibling and a replace.
/// Private to the widget (the server never reads it).</summary>
public static class NowPlayingLookStore
{
    /// <summary>The book the file holds; an empty one when it is missing, unreadable or not a looks file.</summary>
    public static NowPlayingLookBook Load(string path)
    {
        try
        {
            return File.Exists(path) ? NowPlayingLookBook.Parse(File.ReadAllBytes(path)) : NowPlayingLookBook.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return NowPlayingLookBook.Empty;
        }
    }

    /// <summary>Writes the book. False when it could not be written.</summary>
    public static bool Save(string path, NowPlayingLookBook book)
    {
        try
        {
            MessageFileIo.WriteAtomic(path, book.ToJson());
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Removes the file (Clear Looks, Reset). Never throws.</summary>
    public static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the next change tries again */ }
    }
}

/// <summary>
/// Claude's suggested song looks, the file half (spec, a look per song). The MCP server's <c>propose_now_playing_looks</c> drops
/// <c>now-playing-looks-request.json</c> into Sanduhr's folder, and the now-playing mod may drop <c>now-playing-look-offer.json</c>
/// (a look its model chose for a song Sanduhr has none saved for); the App watches the folder and calls <see cref="Check"/>, which
/// checks each again (the widget is the authority: it knows the wallpaper) and either saves it, when "Let Claude style songs
/// directly" is on, or holds it as the suggestion Settings, Now playing shows with Save and Dismiss. A tool request is answered in
/// <c>now-playing-looks-result.json</c>: <c>pending_approval</c> at once, then <c>applied</c> (with <c>looks_saved</c>) or
/// <c>rejected</c>; every answer carries <c>style_on</c>. An offer has no id and no answer.
///
/// A suggestion waits in memory: quitting Sanduhr drops it. A newer request replaces an older one still waiting; an offer joins the
/// card that waits (up to 20 songs). Nothing here logs a title. Also keeps the saved looks (<see cref="Book"/>) and writes the
/// current song's look for the mod (<see cref="WriteCurrent"/>). Not thread-safe: call it from one thread (the UI thread).
/// </summary>
public sealed class NowPlayingLookHandoff
{
    private readonly Func<bool> _styleOn;
    private readonly Func<bool> _direct;
    private readonly Func<DeskDesignContext?> _context;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<bool> _canWrite;
    private byte[]? _lastCurrent;

    /// <param name="paths">The folders.</param>
    /// <param name="styleOn">Settings, Now playing, "Style what's playing": reported in every answer.</param>
    /// <param name="direct">"Let Claude style songs directly", off by default.</param>
    /// <param name="context">What the machine adds to the rules (the wallpaper, for the contrast rule); null or unknown skips that rule.</param>
    /// <param name="now">The clock (a request older than ten minutes is dropped).</param>
    /// <param name="canWrite">False once Reset has run: nothing is written again.</param>
    public NowPlayingLookHandoff(NowPlayingLookPaths paths, Func<bool>? styleOn = null, Func<bool>? direct = null,
        Func<DeskDesignContext?>? context = null, Func<DateTimeOffset>? now = null, Func<bool>? canWrite = null)
    {
        Paths = paths;
        _styleOn = styleOn ?? (() => false);
        _direct = direct ?? (() => false);
        _context = context ?? (() => null);
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _canWrite = canWrite ?? (() => true);
        Book = NowPlayingLookStore.Load(paths.Store);
    }

    public NowPlayingLookPaths Paths { get; }

    /// <summary>The saved looks.</summary>
    public NowPlayingLookBook Book { get; private set; }

    /// <summary>The suggestion waiting for the user, if any.</summary>
    public NowPlayingLookProposal? Pending { get; private set; }

    /// <summary>The saved looks changed (saved, cleared or read again): the Desk draws the song again.</summary>
    public event Action? BookChanged;

    /// <summary>A suggestion arrived, grew, or was decided: Settings shows or drops its card.</summary>
    public event Action? PendingChanged;

    /// <summary>A suggestion arrived (the banner decision is the caller's).</summary>
    public event Action<NowPlayingLookProposal>? SuggestionReceived;

    /// <summary>Reads again what is on disk (a settings change elsewhere, a test).</summary>
    public void Reload()
    {
        Book = NowPlayingLookStore.Load(Paths.Store);
        BookChanged?.Invoke();
    }

    /// <summary>Takes a waiting request and a waiting offer, if there are any.</summary>
    public void Check()
    {
        if (MessageFileIo.Take(Paths.Request) is { } data) Receive(data);
        DateTime written = File.Exists(Paths.Offer) ? File.GetLastWriteTimeUtc(Paths.Offer) : DateTime.MinValue;
        if (MessageFileIo.Take(Paths.Offer) is { } offer) ReceiveOffer(offer, new DateTimeOffset(written, TimeSpan.Zero));
    }

    /// <summary>One request's bytes, as read from the file.</summary>
    public void Receive(byte[] data)
    {
        var decoded = NowPlayingLookProposal.Decode(data, _now(), _context());
        switch (decoded.Kind)
        {
            case MessageDecodedKind.Discard:
                return;
            case MessageDecodedKind.Refused:
                WriteResult(decoded.Id!, MessageProposalStatus.Rejected, decoded.Reasons);
                return;
            default:
            {
                var p = decoded.Proposal!;
                if (_direct())
                {
                    Apply(p);
                    return;
                }
                Pending = p;
                WriteResult(p.Id, MessageProposalStatus.PendingApproval);
                PendingChanged?.Invoke();
                SuggestionReceived?.Invoke(p);
                return;
            }
        }
    }

    /// <summary>One offer's bytes and the time the mod wrote it. Anything wrong with it drops it without an answer.</summary>
    public void ReceiveOffer(byte[] data, DateTimeOffset writtenAt)
    {
        if (_now() - writtenAt > NowPlayingLookProposal.MaxAge) return;
        if (NowPlayingLookProposal.DecodeOffer(data, _context()).Look is not { } look) return;
        if (_direct())
        {
            Apply(new NowPlayingLookProposal("", _now(), [look], null, IsOffer: true));
            return;
        }
        var joined = Pending is { } waiting
            ? waiting with { Looks = [.. waiting.Looks.Where(l => l.Key != look.Key), look] }
            : new NowPlayingLookProposal("", _now(), [look], null, IsOffer: true);
        if (joined.Looks.Count > NowPlayingLookCheck.MaxLooks) joined = joined with { Looks = [.. joined.Looks.Skip(joined.Looks.Count - NowPlayingLookCheck.MaxLooks)] };
        Pending = joined;
        PendingChanged?.Invoke();
        SuggestionReceived?.Invoke(joined);
    }

    /// <summary>Save: the waiting suggestion goes into the saved looks.</summary>
    public void Approve()
    {
        if (Pending is not { } p) return;
        Pending = null;
        PendingChanged?.Invoke();
        Apply(p);
    }

    /// <summary>Dismiss: the suggestion is dropped and the server's caller hears so.</summary>
    public void Dismiss()
    {
        if (Pending is not { } p) return;
        Pending = null;
        PendingChanged?.Invoke();
        if (!p.IsOffer) WriteResult(p.Id, MessageProposalStatus.Rejected, ["dismissed by the user"]);
    }

    private void Apply(NowPlayingLookProposal p)
    {
        // Reset Sanduhr... ran: the saved looks are not written again.
        if (!_canWrite()) return;
        if (!Save(p.Looks))
        {
            if (!p.IsOffer) WriteResult(p.Id, MessageProposalStatus.Rejected, ["the saved looks could not be written"]);
            return;
        }
        if (!p.IsOffer) WriteResult(p.Id, MessageProposalStatus.Applied, null, p.Looks.Count);
    }

    /// <summary>Saves looks into the book (a song already there takes the new look). False when the file could not be written.</summary>
    public bool Save(IEnumerable<SongLook> looks)
    {
        if (!_canWrite()) return false;
        var next = Book.Merge(looks);
        if (!NowPlayingLookStore.Save(Paths.Store, next)) return false;
        Book = next;
        BookChanged?.Invoke();
        return true;
    }

    /// <summary>Clear Looks: the file goes and the book is empty. A waiting suggestion stays.</summary>
    public void Clear()
    {
        NowPlayingLookStore.Delete(Paths.Store);
        Book = NowPlayingLookBook.Empty;
        BookChanged?.Invoke();
    }

    /// <summary>The result the server polls for. The file sits in the user's own profile folder and is written through a temp sibling and a replace.</summary>
    private void WriteResult(string id, MessageProposalStatus status, IReadOnlyList<string>? reasons = null, int? saved = null)
    {
        if (!_canWrite()) return;
        var data = NowPlayingLookProposal.ResultJson(id, status, _styleOn(), reasons, saved, _now());
        TryWrite(Paths.Result, data);
    }

    /// <summary>
    /// <c>now-playing-look.json</c>: the current song's look for the mod (key, look and where it came from; never the title or the
    /// artist). Rewritten only when its bytes changed. False when it could not be written.
    /// </summary>
    public bool WriteCurrent(string key, NowPlayingLook look, LookSource source)
    {
        if (!_canWrite()) return false;
        var data = NowPlayingLookProposal.CurrentJson(key, look, source);
        if (_lastCurrent is not null && data.AsSpan().SequenceEqual(_lastCurrent)) return true;
        if (!TryWrite(Paths.Current, data)) return false;
        _lastCurrent = data;
        return true;
    }

    /// <summary>Removes <c>now-playing-look.json</c> (nothing plays, Style is off, the integration is off, Reset). Never throws.</summary>
    public void DeleteCurrent()
    {
        _lastCurrent = null;
        try { if (File.Exists(Paths.Current)) File.Delete(Paths.Current); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the next change tries again */ }
    }

    private static bool TryWrite(string path, byte[] data)
    {
        try
        {
            MessageFileIo.WriteAtomic(path, data);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
