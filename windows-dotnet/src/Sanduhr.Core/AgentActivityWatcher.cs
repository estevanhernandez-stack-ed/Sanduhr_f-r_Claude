using System.Text;

namespace Sanduhr.Core;

/// <summary>
/// Watches the files that say what each agent's turn is doing (5.0 spec 4) and raises one <see cref="AgentTurnEvent"/> per
/// change: Codex's plain rollouts, the Antigravity CLI's transcripts and the signal files Claude Code's hook writes. It
/// tails each file from an offset kept in memory and stores nothing. The first pass over an agent reads for state and
/// raises nothing, so starting Sanduhr never replays old turns. An agent that is off is not read at all.
/// Scans run one at a time and raise their events before the next scan starts, so events arrive in file order. A
/// listener may call Start, Refresh or Dispose, but must not block on another thread that is waiting to scan.
/// </summary>
public sealed class AgentActivityWatcher : IDisposable
{
    public static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    /// <summary>At the first pass, a file changed this recently is read (its tail) to learn whether a turn is open.</summary>
    public static readonly TimeSpan PrimeWindow = TimeSpan.FromMinutes(10);

    public const long PrimeTailBytes = 4L * 1024 * 1024;

    /// <summary>A longer line is skipped whole: turn markers are small, and tool output can be huge.</summary>
    public const int MaxLineBytes = 1024 * 1024;

    private readonly Func<IReadOnlyList<string>> _codexHomes;
    private readonly Func<string?> _antigravityHome;
    private readonly Func<string, string?> _antigravityFolder;
    private readonly Func<bool> _claudeOn;
    private readonly string _signalDir;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string>? _log;

    private readonly object _gate = new();
    private readonly object _scanGate = new();   // held across a whole Scan, raising included, so scans deliver in order
    private readonly Dictionary<string, Tail> _tails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _firstPass = new(StringComparer.Ordinal);   // agent -> when its first pass ran
    private readonly ClaudeCodeTurns _claude = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private bool _claudeWasOn;
    private Timer? _poll;
    private Timer? _soon;
    private Timer? _due;
    private bool _disposed;

    public AgentActivityWatcher(Func<IReadOnlyList<string>> codexHomes, Func<string?> antigravityHome,
        Func<string, string?> antigravityFolder, Func<bool> claudeOn, string signalDir,
        Func<DateTimeOffset>? clock = null, Action<string>? log = null)
    {
        _codexHomes = codexHomes;
        _antigravityHome = antigravityHome;
        _antigravityFolder = antigravityFolder;
        _claudeOn = claudeOn;
        _signalDir = signalDir;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _log = log;
    }

    /// <summary>A turn started, finished, stopped or is waiting. Raised on a worker thread.</summary>
    public event Action<AgentTurnEvent>? Turn;

    /// <summary>An agent's source went off: whatever shows its turns should clear them. Raised on a worker thread.</summary>
    public event Action<string>? AgentOff;

    private sealed class Tail(string agent, string session)
    {
        public readonly string Agent = agent;
        public readonly string Session = session;
        public long Offset;
        public readonly MemoryStream Partial = new();
        public bool Oversized;
        public CodexTurnReader? Codex = agent == AgentSources.Codex ? new CodexTurnReader(session) : null;
        public AntigravityTurnReader? Antigravity = agent == AgentSources.Antigravity ? new AntigravityTurnReader(session) : null;

        public void Reset(long offset)
        {
            Offset = offset;
            Partial.SetLength(0);
            Oversized = false;
            if (Codex is not null) Codex = new CodexTurnReader(Session);
            if (Antigravity is not null) Antigravity = new AntigravityTurnReader(Session);
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _poll is not null) return;
            _soon = new Timer(_ => SafeScan(), null, Timeout.Infinite, Timeout.Infinite);
            _due = new Timer(_ => SafeScan(), null, Timeout.Infinite, Timeout.Infinite);
            Watch();
            _poll = new Timer(_ => SafeScan(), null, TimeSpan.Zero, PollEvery);
        }
    }

    /// <summary>The sources changed: watch the folders that are on now, and scan soon.</summary>
    public void Refresh()
    {
        lock (_gate)
        {
            if (_disposed || _poll is null) return;
            Unwatch();
            Watch();
            _soon?.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Unwatch();
            _poll?.Dispose();
            _soon?.Dispose();
            _due?.Dispose();
            _poll = _soon = _due = null;
        }
    }

    /// <summary>One pass over everything that is on. The timers call it; a test calls it directly.</summary>
    public void Scan()
    {
        lock (_scanGate)
        {
            var events = new List<AgentTurnEvent>();
            var off = new List<string>();
            lock (_gate)
            {
                if (_disposed) return;
                var now = _clock();
                ScanAgent(AgentSources.Codex, CodexFiles(), now, events, off);
                ScanAgent(AgentSources.Antigravity, AntigravityFiles(), now, events, off);
                ScanSignals(now, events, off);
                ArmDue(now);
            }
            foreach (var agent in off)
                foreach (var handler in AgentOff?.GetInvocationList() ?? [])
                    Raise(() => ((Action<string>)handler)(agent));
            foreach (var e in events)
                foreach (var handler in Turn?.GetInvocationList() ?? [])
                    Raise(() => ((Action<AgentTurnEvent>)handler)(e));
        }
    }

    private void SafeScan()
    {
        try { Scan(); }
        catch (Exception e) when (e is not OutOfMemoryException) { _log?.Invoke($"agent activity: scan failed ({e.GetType().Name})"); }
    }

    private void Raise(Action a)
    {
        try { a(); }
        catch (Exception e) when (e is not OutOfMemoryException) { _log?.Invoke($"agent activity: a listener failed ({e.GetType().Name})"); }
    }

    // ---- files ----

    /// <summary>Codex's plain rollouts, or null while Codex is off. Compressed rollouts are finished history.</summary>
    private List<(string Path, string Session)>? CodexFiles()
    {
        var homes = _codexHomes();
        if (homes.Count == 0) return null;
        var files = new List<(string, string)>();
        foreach (var home in homes)
        {
            try
            {
                var sessions = new DirectoryInfo(Path.Combine(home, "sessions"));
                if (!sessions.Exists) continue;
                foreach (var fi in sessions.EnumerateFiles("rollout-*.jsonl", SearchOption.AllDirectories))
                    files.Add((fi.FullName, CodexBurnSource.SessionIdOf(fi.Name)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _log?.Invoke($"agent activity: codex folder not read ({e.GetType().Name})");
            }
        }
        return files;
    }

    /// <summary>The Antigravity CLI's transcripts, or null while Antigravity is off.</summary>
    private List<(string Path, string Session)>? AntigravityFiles()
    {
        if (_antigravityHome() is not { } home) return null;
        var files = new List<(string, string)>();
        try
        {
            var brain = new DirectoryInfo(Path.Combine(home, "brain"));
            if (!brain.Exists) return files;
            foreach (var conversation in brain.EnumerateDirectories())
            {
                var path = Path.Combine(conversation.FullName, ".system_generated", "logs", "transcript.jsonl");
                if (File.Exists(path)) files.Add((path, conversation.Name));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"agent activity: antigravity folder not read ({e.GetType().Name})");
        }
        return files;
    }

    private void ScanAgent(string agent, List<(string Path, string Session)>? files, DateTimeOffset now,
        List<AgentTurnEvent> events, List<string> off)
    {
        if (files is null)
        {
            if (_firstPass.Remove(agent))
            {
                DropTails(t => t.Agent == agent);
                off.Add(agent);
            }
            return;
        }

        bool first = !_firstPass.TryGetValue(agent, out var firstPassAt);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, session) in files)
        {
            seen.Add(path);
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) continue;
                if (!_tails.TryGetValue(path, out var tail))
                {
                    tail = new Tail(agent, session);
                    // A file that was there before this agent's first pass is history: read it for state, raise nothing.
                    // The tail is kept only once its prime has succeeded, so a failed prime is retried as history.
                    if (first || fi.CreationTimeUtc < firstPassAt.UtcDateTime)
                    {
                        Prime(tail, fi, now);
                        _tails[path] = tail;
                        continue;
                    }
                    _tails[path] = tail;
                }
                if (fi.Length < tail.Offset) Rewritten(tail, fi);          // rewritten shorter: carry on from its new end
                else if (fi.Length > tail.Offset) ReadNew(tail, path, now, events);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _log?.Invoke($"agent activity: a file was not read ({e.GetType().Name})");
            }
        }
        // A file that is gone (compressed, deleted) takes its open turn with it: no ending is invented.
        DropTails(t => t.Agent == agent, keep: seen);
        if (first) _firstPass[agent] = now;

        if (agent != AgentSources.Antigravity) return;
        foreach (var tail in _tails.Values)
            if (tail.Antigravity is { DueAt: not null } reader && reader.Due(now, _antigravityFolder(tail.Session)) is { } e)
                events.Add(e);
    }

    /// <summary>A file shrank: start its reader afresh at the new end. A Codex rollout's first line (its sub-agent mark and
    /// folder) is read again, as the prime does. If that read fails, the offset is left past any length so the next scan
    /// resets and tries again.</summary>
    private void Rewritten(Tail tail, FileInfo fi)
    {
        tail.Reset(fi.Length);
        if (tail.Codex is null) return;
        try
        {
            using var fs = Open(fi.FullName);
            long consumed = 0;
            ReadLines(tail, fs, Math.Min(fs.Length, MaxLineBytes), false, default, null, null, ref consumed, firstLineOnly: true);
            tail.Partial.SetLength(0);
            tail.Oversized = false;
        }
        catch
        {
            tail.Offset = long.MaxValue;
            throw;
        }
    }

    private void DropTails(Func<Tail, bool> which, HashSet<string>? keep = null)
    {
        foreach (var key in _tails.Where(kv => which(kv.Value) && keep?.Contains(kv.Key) != true).Select(kv => kv.Key).ToList())
            _tails.Remove(key);
    }

    private static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>History: Codex's first line (the meta that marks a sub-agent), then the tail of a file that changed
    /// lately, with every event dropped. Afterwards the offset is the file's end.</summary>
    private void Prime(Tail tail, FileInfo fi, DateTimeOffset now)
    {
        using var fs = Open(fi.FullName);
        long length = fs.Length;
        long consumed = 0;   // the prime sets the offset itself
        if (tail.Codex is not null) ReadLines(tail, fs, Math.Min(length, MaxLineBytes), false, now, null, null, ref consumed, firstLineOnly: true);
        tail.Partial.SetLength(0);
        tail.Oversized = false;

        if (now.UtcDateTime - fi.LastWriteTimeUtc <= PrimeWindow)
        {
            long start = Math.Max(0, length - PrimeTailBytes);
            fs.Seek(start, SeekOrigin.Begin);
            ReadLines(tail, fs, length - start, skipFirstPartial: start > 0, now, null, null, ref consumed);
            tail.Antigravity?.Settle();
        }
        tail.Offset = length;
    }

    private void ReadNew(Tail tail, string path, DateTimeOffset now, List<AgentTurnEvent> events)
    {
        using var fs = Open(path);
        long length = fs.Length;
        if (length <= tail.Offset) return;
        fs.Seek(tail.Offset, SeekOrigin.Begin);
        var folder = tail.Antigravity is null ? null : _antigravityFolder(tail.Session);
        long consumed = 0;
        try { ReadLines(tail, fs, length - tail.Offset, false, now, events, folder, ref consumed); }
        finally { tail.Offset += consumed; }   // Offset and Partial always agree, even when a read faults part-way
    }

    /// <summary>Feeds every complete line in the next <paramref name="count"/> bytes to the tail's reader. A line with no
    /// newline yet stays in the tail for the next read. <paramref name="sink"/> null drops the events (history).</summary>
    private static void ReadLines(Tail tail, FileStream fs, long count, bool skipFirstPartial, DateTimeOffset now,
        List<AgentTurnEvent>? sink, string? folder, ref long consumed, bool firstLineOnly = false)
    {
        var buf = new byte[64 * 1024];
        long left = count;
        bool skipping = skipFirstPartial;
        while (left > 0)
        {
            int n = fs.Read(buf, 0, (int)Math.Min(buf.Length, left));
            if (n <= 0) break;
            left -= n;
            consumed += n;
            int lineStart = 0;
            for (int i = 0; i < n; i++)
            {
                if (buf[i] != (byte)'\n') continue;
                if (skipping) skipping = false;
                else
                {
                    Collect(tail, buf, lineStart, i - lineStart);
                    EndLine(tail, now, sink, folder);
                    if (firstLineOnly) return;
                }
                lineStart = i + 1;
            }
            if (!skipping) Collect(tail, buf, lineStart, n - lineStart);
        }
    }

    private static void Collect(Tail tail, byte[] buf, int start, int count)
    {
        if (count <= 0 || tail.Oversized) return;
        if (tail.Partial.Length + count > MaxLineBytes)
        {
            tail.Oversized = true;
            tail.Partial.SetLength(0);
            return;
        }
        tail.Partial.Write(buf, start, count);
    }

    private static void EndLine(Tail tail, DateTimeOffset now, List<AgentTurnEvent>? sink, string? folder)
    {
        if (tail.Oversized)
        {
            tail.Oversized = false;
            tail.Antigravity?.Activity();   // still a line: the turn is going on
            return;
        }
        var line = Encoding.UTF8.GetString(tail.Partial.GetBuffer(), 0, (int)tail.Partial.Length).Trim();
        tail.Partial.SetLength(0);
        if (line.Length == 0) return;
        var e = tail.Codex is { } codex ? codex.Read(line) : tail.Antigravity?.Read(line, folder, now);
        if (e is not null) sink?.Add(e);
    }

    // ---- Claude Code's signal files ----

    private void ScanSignals(DateTimeOffset now, List<AgentTurnEvent> events, List<string> off)
    {
        bool on = _claudeOn();
        if (_claudeWasOn && !on) off.Add(AgentSources.Claude);
        _claudeWasOn = on;

        List<string> files;
        try { files = [.. Directory.EnumerateFiles(_signalDir, ClaudeCodeSignalFile.Pattern).OrderBy(f => f, StringComparer.Ordinal)]; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }

        foreach (var file in files)
        {
            byte[]? data = null;
            try
            {
                // Read only while Claude Code's notifications are on and the file is small; delete it either way.
                if (on && new FileInfo(file).Length <= ClaudeCodeSignalFile.MaxBytes) data = File.ReadAllBytes(file);
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            if (data is not null && ClaudeCodeSignalFile.Decode(data, now) is { } signal) events.Add(_claude.Apply(signal));
        }
    }

    // ---- timers and folder watchers ----

    /// <summary>Wakes when the earliest pending Antigravity ending is due.</summary>
    private void ArmDue(DateTimeOffset now)
    {
        DateTimeOffset? next = null;
        foreach (var tail in _tails.Values)
            if (tail.Antigravity?.DueAt is { } at && (next is null || at < next)) next = at;
        if (next is not { } due) return;
        var wait = due - now + TimeSpan.FromMilliseconds(200);
        _due?.Change(wait < TimeSpan.Zero ? TimeSpan.Zero : wait, Timeout.InfiniteTimeSpan);
    }

    private void Watch()
    {
        foreach (var home in _codexHomes()) AddWatcher(Path.Combine(home, "sessions"), "rollout-*", subfolders: true);
        if (_antigravityHome() is { } agy) AddWatcher(Path.Combine(agy, "brain"), "transcript.jsonl", subfolders: true);
        AddWatcher(_signalDir, ClaudeCodeSignalFile.Pattern, subfolders: false);
    }

    private void AddWatcher(string dir, string filter, bool subfolders)
    {
        try
        {
            if (!Directory.Exists(dir)) return;   // the poll covers a folder that appears later
            var w = new FileSystemWatcher(dir, filter) { IncludeSubdirectories = subfolders };
            FileSystemEventHandler soon = (_, _) => Soon();
            w.Changed += soon;
            w.Created += soon;
            w.Renamed += (_, _) => Soon();
            w.Error += (_, e) => _log?.Invoke($"agent activity: watcher error ({e.GetException().GetType().Name}), the timer covers it");
            w.EnableRaisingEvents = true;
            _watchers.Add(w);
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _log?.Invoke($"agent activity: watcher not started ({e.GetType().Name})");
        }
    }

    private void Soon()
    {
        try { _soon?.Change(Debounce, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { }
    }

    private void Unwatch()
    {
        foreach (var w in _watchers)
        {
            try { w.EnableRaisingEvents = false; w.Dispose(); }
            catch (Exception e) when (e is not OutOfMemoryException) { /* best-effort teardown */ }
        }
        _watchers.Clear();
    }
}
