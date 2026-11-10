using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>What the widget, the snapshot and the alerts know about Codex right now. <c>Fresh</c> means any tier is fresh;
/// <c>FreshTiers</c> says which (per tier: recent event, or that tier's own reset passed).</summary>
public sealed record CodexState(CodexReadStatus Status, JsonObject Tiers, DateTimeOffset? AsOf, bool Fresh, string? PlanType, IReadOnlySet<string> FreshTiers)
{
    public static CodexState Off { get; } = new(CodexReadStatus.NotInstalled, new JsonObject(), null, false, null, new HashSet<string>());
}

/// <summary>
/// Codex limits, kept current: reads the newest rollout event every 30 s and when a rollout file changes,
/// appends each new event to the history under the reserved account <see cref="CodexHistoryAccount"/>,
/// writes <c>snapshot.codex.json</c> (stamped with the event's time, so it means "Codex idle", not "app
/// stopped"), and runs its own <see cref="AlertEngine"/> so Claude's per-tier alert state is untouched.
/// <see cref="Changed"/> is raised on the timer or watcher thread; the consumer dispatches to the UI.
/// The reader is not thread-safe and is only ever called under the poll lock. Changed can arrive out of order across
/// threads, so a consumer reads <see cref="Current"/> in its dispatched handler instead of trusting the argument.
/// After <see cref="Stop"/> or <see cref="Dispose"/> no timer or watcher callback does any IO: Stop waits for a poll in
/// flight, and queued callbacks from an earlier run see a newer generation and return.
/// </summary>
public sealed class CodexUsageService : IDisposable
{
    /// <summary>The history account the Codex points live under. History readers skip "@" names and Reset removes
    /// the file by its pattern, so it is never taken for a Claude account.</summary>
    public const string CodexHistoryAccount = "@codex";

    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

    private readonly CodexLimitsReader _reader;
    private readonly UsageHistory _history;
    private readonly SnapshotWriter? _snapshot;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string>? _log;
    private readonly AlertEngine _alerts = new();
    private readonly object _gate = new();          // the poll lock: reader, history, snapshot, alert state
    private readonly object _startStop = new();     // serializes Start and Stop
    private readonly object _debounceGate = new();  // only re-arms or retires the debounce timer, never waits on a read
    private int _generation;
    private readonly HashSet<int> _loggedWindows = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private DateTimeOffset? _lastAppended;
    private bool _seeded;
    private string? _lastSnapshotKey;
    private Timer? _timer;
    private Timer? _debounce;
    private bool _disposed;
    private volatile CodexState _current = CodexState.Off;

    public CodexUsageService(CodexLimitsReader reader, UsageHistory history, SnapshotWriter? snapshot, Func<DateTimeOffset> clock, Action<string>? log)
    {
        _reader = reader;
        _history = history;
        _snapshot = snapshot;
        _clock = clock;
        _log = log;
    }

    public CodexState Current => _current;

    public event Action<CodexState>? Changed;

    public CodexState Poll() => PollCore(null);

    private CodexState PollCore(int? generation)
    {
        CodexState next;
        bool changed;
        lock (_gate)
        {
            if (_disposed || (generation is { } g && g != _generation))
                return _current;
            var now = _clock();
            var result = _reader.Read();
            if (result.Status == CodexReadStatus.Ok && result.Reading is { } reading)
            {
                var tiers = CodexLimits.ToTiers(reading, now, minutes =>
                {
                    if (_loggedWindows.Add(minutes))
                        _log?.Invoke($"codex: unknown window of {minutes} minutes ignored");
                });
                var fresh = CodexLimits.FreshTiers(reading, now);
                next = new CodexState(CodexReadStatus.Ok, tiers, reading.AsOf, fresh.Count > 0, reading.PlanType, fresh);
                AppendHistory(reading.AsOf, tiers);
                WriteSnapshot(reading, tiers);
            }
            else
            {
                next = new CodexState(result.Status == CodexReadStatus.Ok ? CodexReadStatus.NoLimits : result.Status, new JsonObject(), null, false, null, new HashSet<string>());
                _lastSnapshotKey = null;
                _snapshot?.Delete();
            }
            var prev = _current;
            changed = prev.Status != next.Status || prev.AsOf != next.AsOf || prev.Fresh != next.Fresh
                || !prev.FreshTiers.SetEquals(next.FreshTiers) || prev.Tiers.ToJsonString() != next.Tiers.ToJsonString();
            _current = next;
        }
        if (changed)
        {
            try { Changed?.Invoke(next); }
            catch (Exception e) when (e is not OutOfMemoryException) { _log?.Invoke($"codex: change handler failed ({e.GetType().Name})"); }
        }
        return next;
    }

    private void AppendHistory(DateTimeOffset asOf, JsonObject tiers)
    {
        if (!_seeded)
        {
            _seeded = true;
            _lastAppended = NewestHistoryPoint();
        }
        if (_lastAppended is { } last && asOf <= last)
            return;
        foreach (var (key, node) in tiers)
        {
            if (node is not JsonObject t || t["utilization"] is not JsonValue u || !u.TryGetValue<double>(out var pct)) continue;
            try { _history.AppendForAccount(key, (int)Math.Round(pct), CodexHistoryAccount, (string?)t["resets_at"]); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _log?.Invoke($"codex: history write failed ({e.GetType().Name})");
                return;
            }
        }
        _lastAppended = asOf;
    }

    private DateTimeOffset? NewestHistoryPoint()
    {
        DateTimeOffset? newest = null;
        try
        {
            foreach (var points in _history.LoadHistory(CodexHistoryAccount).Values)
                foreach (var p in points)
                    if (DateTimeOffset.TryParse(p.T, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) && (newest is null || t > newest))
                        newest = t;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log?.Invoke($"codex: history read failed ({e.GetType().Name})"); }
        return newest;
    }

    private void WriteSnapshot(CodexLimitsReading reading, JsonObject tiers)
    {
        if (_snapshot is null) return;
        string key = reading.AsOf.UtcTicks.ToString(CultureInfo.InvariantCulture) + tiers.ToJsonString() + reading.PlanType;
        if (key == _lastSnapshotKey) return;
        _snapshot.WriteOk(tiers, reading.PlanType, accountLabel: null, now: reading.AsOf);
        _lastSnapshotKey = key;
    }

    public IReadOnlyList<AlertEvent> EvaluateAlerts(AlertConfig config, bool alertsOn)
    {
        lock (_gate)
        {
            var state = _current;
            if (_disposed || !alertsOn || !state.Fresh || state.Status != CodexReadStatus.Ok)
                return Array.Empty<AlertEvent>();
            var snaps = new List<TierAlertSnapshot>();
            foreach (var (key, node) in state.Tiers)
            {
                if (!state.FreshTiers.Contains(key)) continue;
                if (node is not JsonObject t || t["utilization"] is not JsonValue u || !u.TryGetValue<double>(out var pct)) continue;
                snaps.Add(new TierAlertSnapshot(key, (int)Math.Round(pct), null, null, (string?)t["resets_at"]));
            }
            return _alerts.Evaluate(snaps, config, _clock());
        }
    }

    /// <summary>Poll every 30 s and when a rollout under any home's <c>sessions</c> folder changes. A missing folder is
    /// skipped: the timer is the fallback for it and for a watcher that errors.</summary>
    public void Start(IReadOnlyList<string> homes)
    {
        lock (_startStop)
        {
            StopCore();
            int generation;
            lock (_gate)
            {
                if (_disposed) return;
                generation = _generation;
            }
            var debounce = new Timer(_ => SafePoll(generation), null, Timeout.Infinite, Timeout.Infinite);
            lock (_debounceGate) _debounce = debounce;
            foreach (var home in homes)
            {
                try
                {
                    var sessions = Path.Combine(home, "sessions");   // streamer: notname - a folder Codex writes, joined for the watcher, never drawn
                    if (!Directory.Exists(sessions)) continue;
                    var w = new FileSystemWatcher(sessions, "rollout-*") { IncludeSubdirectories = true };
                    w.Changed += OnFileEvent;
                    w.Created += OnFileEvent;
                    w.Renamed += OnFileEvent;
                    w.Error += (_, e) => _log?.Invoke($"codex: watcher error ({e.GetException().GetType().Name}), the timer covers it");
                    w.EnableRaisingEvents = true;
                    _watchers.Add(w);
                }
                catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
                {
                    _log?.Invoke($"codex: watcher not started ({e.GetType().Name})");
                }
            }
            _timer = new Timer(_ => SafePoll(generation), null, TimeSpan.Zero, PollEvery);
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        lock (_debounceGate) _debounce?.Change(Debounce, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Test hook: the generation the live timers were issued with.</summary>
    internal int LiveGeneration { get { lock (_gate) return _generation; } }

    /// <summary>Test hook: the timer/debounce callback path, run synchronously for a given generation.</summary>
    internal void RunCallback(int generation) => SafePoll(generation);

    private void SafePoll(int generation)
    {
        try { PollCore(generation); }
        catch (Exception e) when (e is not OutOfMemoryException) { _log?.Invoke($"codex: poll failed ({e.GetType().Name})"); }
    }

    /// <summary>Stops the timer and watchers and waits for a poll already in flight; a callback queued before the stop
    /// returns without IO.</summary>
    public void Stop()
    {
        lock (_startStop) StopCore();
    }

    private void StopCore()
    {
        Timer? debounce;
        lock (_debounceGate) { debounce = _debounce; _debounce = null; }
        // Taking the poll lock waits out a read in flight; the bumped generation retires queued callbacks.
        lock (_gate) _generation++;
        foreach (var w in _watchers)
        {
            try { w.EnableRaisingEvents = false; w.Dispose(); }
            catch (Exception e) when (e is not OutOfMemoryException) { /* best-effort teardown */ }
        }
        _watchers.Clear();
        _timer?.Dispose();
        _timer = null;
        debounce?.Dispose();
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        Stop();
    }
}
