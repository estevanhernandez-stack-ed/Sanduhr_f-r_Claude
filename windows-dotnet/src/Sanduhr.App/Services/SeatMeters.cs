using System.IO;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// Per-seat meters, the stateful half (port of the Mac's SeatMeters.swift; the rules are
/// <see cref="SeatRules"/>, the files <see cref="SeatFiles"/>). Keeps seats.json and the
/// per-account files in line with the accounts, and fetches the inactive linked accounts by the
/// fetch rule: after each active fetch that did not fail on the network, every inactive signed-in
/// linked account in use is fetched in turn, <see cref="SeatRules.Stagger"/> apart, one round at a
/// time, at most once per refresh interval. In use: following saw it in use, or Claude Code wrote a
/// session log in its folder in the last 10 minutes (file times only). An account that becomes a
/// seat is also fetched once soon when its file is missing or older than the interval. A refused key
/// skips the account until a different key is saved. An answer is written only while the account is
/// still linked, signed in and not active. Nothing here logs a label. UI thread only.
/// </summary>
public sealed class SeatMeters
{
    /// <summary>The view model's state, read at each step.</summary>
    public sealed class SeatContext
    {
        /// <summary>Label to linked folder, for the listed accounts with a folder.</summary>
        public Dictionary<string, string> Linked { get; init; } = new();
        public HashSet<string> SignedIn { get; init; } = new();
        public string? Active { get; init; }
        /// <summary>Accounts following saw in use in their latest check.</summary>
        public HashSet<string> InUse { get; init; } = new();
    }

    private readonly string _dir;
    private readonly TimeSpan _interval;
    private readonly InactiveUsageReader _reader;
    private readonly string? _writerVersion;
    private readonly Action<string>? _log;
    private readonly Func<DateTimeOffset> _now;

    public Func<SeatContext> Context { get; set; } = () => new SeatContext();
    public Func<string, Credentials> CredentialsOf { get; set; } = _ => new Credentials(null, null);
    /// <summary>Whether Claude Code wrote to a seat folder lately. Runs off the UI thread.</summary>
    public Func<string, bool> RecentActivity { get; set; } = folder => SeatRules.RecentActivity(folder, DateTime.UtcNow);
    /// <summary>The wait between two fetches of a round; tests could shorten it.</summary>
    public Func<TimeSpan, Task> Sleep { get; set; } = d => Task.Delay(d);

    /// <summary>Demo mode: no round starts and no fetch goes out until it ends.</summary>
    public bool Paused { get; set; }

    private readonly Dictionary<string, DateTimeOffset> _lastFetch = new();
    private readonly Dictionary<string, string> _authSkipped = new();
    private readonly Dictionary<string, int> _epoch = new();
    private string? _synced;
    private HashSet<string> _knownFetchable = new();
    private readonly HashSet<string> _pending = new();
    private Task? _round;
    private bool _stopped;

    public SeatMeters(string appDataDir, TimeSpan interval, InactiveUsageReader reader,
        string? writerVersion = null, Action<string>? log = null, Func<DateTimeOffset>? now = null)
    {
        _dir = appDataDir;
        _interval = interval;
        _reader = reader;
        _writerVersion = writerVersion;
        _log = log;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Reset Sanduhr: no round, no file from here on.</summary>
    public void Stop()
    {
        _stopped = true;
        _pending.Clear();
        _knownFetchable = new();
    }

    // Files

    private bool IsLinked(string? label, out SeatContext c)
    {
        c = Context();
        return label is not null && c.Linked.ContainsKey(label);
    }

    /// <summary>seats.json and the per-account files follow every change to the accounts, their
    /// sign-in and their links. Cheap when nothing changed. A new seat with no reading, or an old
    /// one, is fetched once soon.</summary>
    public void Sync()
    {
        if (_stopped || Paused) return;
        var c = Context();
        var seats = SeatFiles.Seats(c.Linked);
        var signedOut = c.Linked.Keys.Where(l => !c.SignedIn.Contains(l)).OrderBy(l => l, StringComparer.Ordinal);
        var signature = string.Join("|", seats.Select(s => s.Folder + ">" + s.AccountRef)) + "#" + string.Join("|", signedOut.Select(l => SnapshotContract.AccountRef(l)));
        if (_synced != signature)
        {
            _synced = signature;
            foreach (var gone in _authSkipped.Keys.Where(k => !c.Linked.ContainsKey(k)).ToList()) _authSkipped.Remove(gone);
            foreach (var gone in _lastFetch.Keys.Where(k => !c.Linked.ContainsKey(k) || !c.SignedIn.Contains(k)).ToList()) _lastFetch.Remove(gone);
            try { SeatFiles.Sync(_dir, c.Linked, c.SignedIn, _now(), _writerVersion, _log); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log?.Invoke($"seat sync failed ({e.GetType().Name})"); }
        }
        var fetchable = c.Linked.Keys.Where(l => SeatRules.Fetchable(l, c.Linked, c.SignedIn, c.Active)).ToHashSet();
        foreach (var label in fetchable.Except(_knownFetchable))
            if (NeedsReading(label)) _pending.Add(label);
        _knownFetchable = fetchable;
        _pending.IntersectWith(fetchable);
        if (_pending.Count > 0) StartRound();
    }

    private bool NeedsReading(string label)
        => SeatRules.NeedsReading(SeatFiles.CapturedAt(_dir, label), _now(), _interval);

    /// <summary>The active account's seat starts from the numbers on screen, stamped with their fetch
    /// time, rather than waiting for the next fetch.</summary>
    public void Seed(JsonObject? usage, DateTimeOffset? at, string? label, string? plan)
    {
        if (usage is null || at is null || label is null || _stopped || Paused) return;
        if (!IsLinked(label, out _) || SeatFiles.CapturedAt(_dir, label) is not null) return;
        Write(() => SeatFiles.WriteAccount(_dir, label, usage, plan, at.Value, _writerVersion, _log));
    }

    /// <summary>A renamed account: its seat file moves to the new ref, its state follows.</summary>
    public void Rename(string old, string newLabel)
    {
        try { SeatFiles.Rename(_dir, old, newLabel); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log?.Invoke($"seat rename failed ({e.GetType().Name})"); }
        if (_authSkipped.Remove(old, out var k)) _authSkipped[newLabel] = k;
        if (_lastFetch.Remove(old, out var t)) _lastFetch[newLabel] = t;
        _pending.Remove(old);
        _epoch[old] = _epoch.GetValueOrDefault(old) + 1;
        _synced = null;
    }

    /// <summary>Removed or signed out: its file, its skip and its times go.</summary>
    public void Forget(string label)
    {
        SeatFiles.Delete(_dir, label);
        _authSkipped.Remove(label);
        _lastFetch.Remove(label);
        _pending.Remove(label);
        _epoch[label] = _epoch.GetValueOrDefault(label) + 1;
        _synced = null;
    }

    // Answers

    /// <summary>The active account's good fetch: its seat file gets the same, then a round runs.</summary>
    public void ActiveAnswered(string? label, JsonObject usage, string? plan)
    {
        if (_stopped || Paused) return;
        if (IsLinked(label, out _))
            Write(() => SeatFiles.WriteAccount(_dir, label!, usage, plan, _now(), _writerVersion, _log));
        StartRound();
    }

    /// <summary>The active account's fetch failed: its seat file says so (last good tiers kept), and
    /// a round runs unless the failure was the network (claude.ai unreachable or limiting: nothing
    /// more is asked).</summary>
    public void ActiveFailed(string? label, string errorKind, string? plan)
    {
        if (_stopped || Paused) return;
        if (IsLinked(label, out _))
            Write(() => SeatFiles.WriteAccountError(_dir, label!, errorKind, plan, _now(), _writerVersion, _log));
        if (errorKind != SnapshotContract.ErrorNetwork) StartRound();
    }

    /// <summary>Following's own check of an inactive account answered (no request made for this).</summary>
    public void FollowerAnswered(string label, InactiveReading reading)
    {
        if (_stopped || Paused) return;
        var c = Context();
        if (!SeatRules.Fetchable(label, c.Linked, c.SignedIn, c.Active)) return;
        _lastFetch[label] = _now();
        WriteReading(label, reading);
    }

    private void WriteReading(string label, InactiveReading reading)
    {
        if (reading.Data is { } data)
            Write(() => SeatFiles.WriteAccount(_dir, label, data, reading.Plan, _now(), _writerVersion, _log));
        else if (reading.Error is { } e)
            Write(() => SeatFiles.WriteAccountError(_dir, label, SeatRules.ErrorKind(e), reading.Plan, _now(), _writerVersion, _log));
    }

    private void Write(Action write)
    {
        try { write(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log?.Invoke($"seat write failed ({e.GetType().Name})"); }
    }

    // Rounds

    /// <summary>One round: the due accounts in turn, 5 s apart. Does nothing while a round runs or no
    /// seat could be due. Due: waiting for its first fetch, in use for following, or (checked once
    /// here, off the UI thread) with Claude Code activity in its folder.</summary>
    public void StartRound()
    {
        if (_round is not null || Paused || _stopped) return;
        var c = Context();
        var candidates = c.Linked.Keys
            .Where(l => SeatRules.Fetchable(l, c.Linked, c.SignedIn, c.Active)
                        && !SeatRules.Throttled(_lastFetch.TryGetValue(l, out var t) ? t : null, _now(), _interval))
            .OrderBy(l => l, StringComparer.Ordinal).ToList();
        if (candidates.Count == 0) return;
        _round = RunRoundAsync(c, candidates);
    }

    private async Task RunRoundAsync(SeatContext c, List<string> candidates)
    {
        try
        {
            var due = candidates.Where(l => c.InUse.Contains(l) || _pending.Contains(l)).ToList();
            var rest = candidates.Except(due).Select(l => (Label: l, Folder: c.Linked[l])).ToList();
            if (rest.Count > 0)
            {
                var check = RecentActivity;
                var active = await Task.Run(() => rest.Where(r => check(r.Folder)).Select(r => r.Label).ToList()).ConfigureAwait(true);
                due.AddRange(active);
            }
            _pending.ExceptWith(due);
            foreach (var label in due.OrderBy(l => l, StringComparer.Ordinal))
            {
                if (_stopped) return;
                await Sleep(SeatRules.Stagger).ConfigureAwait(true);
                await FetchAsync(label).ConfigureAwait(true);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _log?.Invoke($"seat round failed ({e.GetType().Name})");
        }
        finally
        {
            _round = null;
        }
        // A seat that became new during the round gets its turn now.
        if (!_stopped && _pending.Count > 0) StartRound();
    }

    private async Task FetchAsync(string label)
    {
        if (Paused || _stopped) return;
        var c = Context();
        if (!SeatRules.Fetchable(label, c.Linked, c.SignedIn, c.Active)
            || SeatRules.Throttled(_lastFetch.TryGetValue(label, out var last) ? last : null, _now(), _interval))
            return;
        var creds = CredentialsOf(label);
        var key = creds.SessionKey;
        if (string.IsNullOrEmpty(key)) return;
        if (_authSkipped.TryGetValue(label, out var failed))
        {
            if (failed == key) return;
            _authSkipped.Remove(label);
        }
        _lastFetch[label] = _now();
        int epoch = _epoch.GetValueOrDefault(label);
        var reading = await _reader.ReadAsync(label, creds).ConfigureAwait(true);
        // Switched to, signed out, removed, unlinked or renamed while the request ran: no one's.
        var after = Context();
        if (_epoch.GetValueOrDefault(label) != epoch
            || !SeatRules.Fetchable(label, after.Linked, after.SignedIn, after.Active))
            return;
        if (reading.Error is { } e && SeatRules.IsAuthKind(SeatRules.ErrorKind(e))) _authSkipped[label] = key;
        WriteReading(label, reading);
    }
}
