using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>The two token fields that count as live human-burn against the
/// subscription. Cache-creation / cache-read are excluded (billed differently),
/// matching <c>cc_logs._human_tokens</c>.</summary>
public readonly record struct TokenUsage(long InputTokens, long OutputTokens);

/// <summary>One assistant-message event extracted from a CC session log.</summary>
public sealed record UsageEvent(
    DateTimeOffset? Timestamp,
    string? Model,
    TokenUsage Usage,
    string? Cwd,
    string? AttributionSkill);

/// <summary>One Claude Code home's burn for ONE local calendar day: total,
/// by project basename, by Sanduhr tier. Produced by
/// <see cref="CcLogReader.BurnForLocalDay"/>; consumed by the 626 Labs
/// publisher (<c>UsagePublisher.BuildPayload</c>). Lives here, not beside the
/// publisher, because this file is source-linked into sanduhr-mcp and the
/// publisher is deliberately not.</summary>
public sealed record RootDayBurn(
    string Root,
    long Total,
    IReadOnlyDictionary<string, long> ByProject,
    IReadOnlyDictionary<string, long> ByTier);

/// <summary>Single-pass aggregation for the Local CC tab —
/// <c>{by_day, by_project, by_skill}</c> plus the per-day sent/received split
/// (<c>ByDayInput</c>/<c>ByDayOutput</c>, accumulated under the same
/// counted-event gate, so <c>ByDay[d] == ByDayInput[d] + ByDayOutput[d]</c>).</summary>
public sealed record LocalCcAggregate(
    Dictionary<DateOnly, long> ByDay,
    Dictionary<string, long> ByProject,
    Dictionary<string, long> BySkill,
    Dictionary<DateOnly, long> ByDayInput,
    Dictionary<DateOnly, long> ByDayOutput);

/// <summary>
/// Local Claude Code session-log reader, ported 1:1 from <c>cc_logs.py</c>.
///
/// Anthropic's <c>/usage</c> endpoint lags actual consumption by minutes; the
/// local CC session JSONL files update as events happen, so reading them gives a
/// sharper "what just burned" delta. Discovery walks <c>~/.claude/projects/</c>
/// and <c>~/.claude-personal/projects/</c>; only <c>type=assistant</c> events
/// carry token counts (payload at <c>message.usage</c>, model at
/// <c>message.model</c>); the burn count is <c>input_tokens + output_tokens</c>.
///
/// The user home is injected so tests can sandbox against a temp tree (Python
/// monkeypatches <c>Path.home()</c>).
/// </summary>
public sealed class CcLogReader
{
    private static readonly string[] LogRootNames = { ".claude", ".claude-personal" };

    // Order matters: more specific prefixes first. Mirrors cc_logs._MODEL_TIER_PREFIXES.
    private static readonly (string Prefix, string Tier)[] ModelTierPrefixes =
    {
        ("claude-opus", "seven_day_opus"),
        ("claude-sonnet", "seven_day_sonnet"),
        ("claude-fable", TierModel.SevenDayFable),
        ("claude-haiku", "seven_day"), // No haiku-specific tier — fold to weekly.
    };

    private const int AggCacheTtlSec = 30;

    private readonly string _homeDir;

    private readonly object _cacheLock = new();
    private DateTimeOffset _cacheComputedAt = DateTimeOffset.MinValue;
    private int? _cacheDaysBack;
    private LocalCcAggregate? _cacheResult;

    private DateTimeOffset _todayCacheComputedAt = DateTimeOffset.MinValue;
    private LocalCcAggregate? _todayCacheResult;

    private readonly Func<IEnumerable<string>>? _rootNames;

    public CcLogReader(string? homeDir = null) : this(homeDir, null)
    {
    }

    /// <summary>Reader whose log roots come from <paramref name="rootNames"/> (folder
    /// names directly under the home), re-evaluated on every <see cref="SearchRoots"/>
    /// call so a home added later is picked up. A null provider uses the fixed
    /// <c>.claude</c> and <c>.claude-personal</c> pair.</summary>
    public CcLogReader(string? homeDir, Func<IEnumerable<string>>? rootNames)
    {
        _homeDir = homeDir ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _rootNames = rootNames;
    }

    /// <summary>Candidate CC config roots in the user's home that actually exist.</summary>
    public IReadOnlyList<string> SearchRoots()
    {
        var outp = new List<string>();
        foreach (var name in _rootNames?.Invoke() ?? LogRootNames)
        {
            var p = Path.Combine(_homeDir, name);
            if (Directory.Exists(p))
                outp.Add(p);
        }
        return outp;
    }

    /// <summary>All session JSONL files across known CC roots, including NESTED
    /// subagent/workflow transcripts ({projectDir}\{uuid}\...\x.jsonl). Missing
    /// <c>projects/</c> subdir is fine (fresh install).</summary>
    public IReadOnlyList<string> DiscoverLogFiles()
    {
        var files = new List<string>();
        foreach (var root in SearchRoots())
        {
            var projects = Path.Combine(root, "projects");
            if (!Directory.Exists(projects))
                continue;
            foreach (var projectDir in Directory.GetDirectories(projects))
                files.AddRange(Directory.GetFiles(projectDir, "*.jsonl", SearchOption.AllDirectories));
        }
        return files;
    }

    /// <summary><see cref="DiscoverLogFiles"/> with each file's last write (UTC),
    /// taken from the directory walk itself: no stat per file, which on thousands
    /// of logs cost more than the reading.</summary>
    private IEnumerable<(string Path, DateTime WrittenUtc)> DiscoverLogFilesWithTimes()
    {
        foreach (var root in SearchRoots())
        {
            var projects = new DirectoryInfo(Path.Combine(root, "projects"));
            if (!projects.Exists)
                continue;
            foreach (var projectDir in projects.EnumerateDirectories())
            {
                IEnumerable<FileInfo> files;
                try
                {
                    files = projectDir.EnumerateFiles("*.jsonl", SearchOption.AllDirectories).ToList();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                foreach (var f in files)
                    yield return (f.FullName, f.LastWriteTimeUtc);
            }
        }
    }

    /// <summary>Stream <see cref="UsageEvent"/> records for every
    /// <c>type=assistant</c> event in a single session JSONL. Malformed lines
    /// are skipped silently; an unreadable file yields nothing.</summary>
    public IEnumerable<UsageEvent> IterUsageEvents(string path)
    {
        StreamReader reader;
        try
        {
            reader = new StreamReader(path, Encoding.UTF8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Enumerable.Empty<UsageEvent>();
        }
        return Iterate(reader);
    }

    private static IEnumerable<UsageEvent> Iterate(StreamReader reader)
    {
        using (reader)
        {
            string? rawLine;
            while ((rawLine = reader.ReadLine()) is not null)
            {
                if (ParseLine(rawLine) is { } ev)
                    yield return ev;
            }
        }
    }

    /// <summary>One session-log line as a usage event, or null for anything that
    /// is not a well-formed assistant event carrying usage.</summary>
    private static UsageEvent? ParseLine(string rawLine)
    {
        var line = rawLine.Trim();
        if (line.Length == 0)
            return null;
        // Perf prefilter only (VaultIngester uses the same one): most lines are
        // user turns and tool results, and parsing them is the cost. Looser than
        // "type":"assistant" on purpose so formatting drift never drops events.
        if (!line.Contains("\"assistant\"", StringComparison.Ordinal))
            return null;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }
        if (node is not JsonObject d)
            return null;
        if (Str(d, "type") != "assistant")
            return null;
        if (d["message"] is not JsonObject msg)
            return null;
        if (msg["usage"] is not JsonObject usage)
            return null;
        return new UsageEvent(
            ParseIso(Str(d, "timestamp")),
            Str(msg, "model"),
            new TokenUsage(Num(usage, "input_tokens"), Num(usage, "output_tokens")),
            Str(d, "cwd"),
            Str(d, "attributionSkill"));
    }

    // TokensSince's incremental state. The widget asks every 30 s and an active
    // session log runs to hundreds of MB, so each file is read once and after
    // that only from where the last call stopped.
    private readonly object _sinceLock = new();
    private readonly Dictionary<string, SinceTail> _sinceTails = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _sinceFloor = DateTimeOffset.MaxValue;

    /// <summary>Bytes <see cref="TokensSince"/> has read from session logs, for tests.</summary>
    internal long SinceBytesRead { get; private set; }

    private sealed class SinceTail
    {
        /// <summary>Byte offset after the last complete line read.</summary>
        public long Offset;
        /// <summary>Counted events at or after the floor the file was last read against.</summary>
        public readonly List<(DateTimeOffset At, string Model, long Tokens)> Events = new();
    }

    /// <summary><c>{model_name: total_tokens}</c> across ALL session files,
    /// considering only events with timestamp &gt;= <paramref name="cutoff"/>.
    /// Models with zero new tokens are omitted. Incremental: a file already read
    /// is read again only from where the last call stopped; a cutoff earlier
    /// than the last one, or a file rewritten shorter, reads from the start.</summary>
    public Dictionary<string, long> TokensSince(DateTimeOffset cutoff)
    {
        lock (_sinceLock)
        {
            // Events below the last cutoff were dropped; an earlier cutoff needs them back.
            if (cutoff < _sinceFloor)
                _sinceTails.Clear();
            _sinceFloor = cutoff;
            var totals = new Dictionary<string, long>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (path, writtenUtc) in DiscoverLogFilesWithTimes())
            {
                if (writtenUtc < cutoff.UtcDateTime)
                    continue;
                seen.Add(path);
                if (!_sinceTails.TryGetValue(path, out var tail))
                    _sinceTails[path] = tail = new SinceTail();
                ReadTail(path, tail, cutoff);
                tail.Events.RemoveAll(e => e.At < cutoff);
                foreach (var (_, model, tokens) in tail.Events)
                    totals[model] = totals.GetValueOrDefault(model) + tokens;
            }
            foreach (var gone in _sinceTails.Keys.Where(k => !seen.Contains(k)).ToList())
                _sinceTails.Remove(gone);
            return totals;
        }
    }

    private void ReadTail(string path, SinceTail tail, DateTimeOffset cutoff)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // Shorter than the offset, or no line end just before it: the file was rewritten.
            if (fs.Length < tail.Offset || (tail.Offset > 0 && ByteBefore(fs, tail.Offset) != '\n'))
            {
                tail.Offset = 0;
                tail.Events.Clear();
            }
            if (fs.Length == tail.Offset)
                return;
            tail.Offset = ReadLines(fs, tail.Offset, line =>
            {
                if (ParseLine(line) is not { Timestamp: { } at, Model: { } model } ev || at < cutoff)
                    return;
                var tokens = Tokens(ev);
                if (tokens > 0)
                    tail.Events.Add((at, model, tokens));
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static int ByteBefore(FileStream fs, long offset)
    {
        fs.Seek(offset - 1, SeekOrigin.Begin);
        return fs.ReadByte();
    }

    /// <summary>Hands each complete line from <paramref name="start"/> to
    /// <paramref name="onLine"/>; returns the offset after the last '\n'. A torn
    /// trailing line is left for the next call (VaultIngester.ParseLines' rule).</summary>
    private long ReadLines(FileStream fs, long start, Action<string> onLine)
    {
        fs.Seek(start, SeekOrigin.Begin);
        var buf = new byte[64 * 1024];
        using var pending = new MemoryStream();
        long pos = start;
        long consumed = start;
        int read;
        while ((read = fs.Read(buf, 0, buf.Length)) > 0)
        {
            SinceBytesRead += read;
            int lineStart = 0;
            for (int i = 0; i < read; i++)
            {
                if (buf[i] != (byte)'\n')
                    continue;
                string line;
                if (pending.Length > 0)
                {
                    pending.Write(buf, lineStart, i - lineStart);
                    line = Encoding.UTF8.GetString(pending.GetBuffer(), 0, (int)pending.Length);
                    pending.SetLength(0);
                }
                else
                {
                    line = Encoding.UTF8.GetString(buf, lineStart, i - lineStart);
                }
                onLine(line);
                consumed = pos + i + 1;
                lineStart = i + 1;
            }
            if (lineStart < read)
                pending.Write(buf, lineStart, read - lineStart);
            pos += read;
        }
        return consumed;
    }

    /// <summary>Tokens grouped by LOCAL calendar date for the trailing
    /// <paramref name="daysBack"/> days.</summary>
    public Dictionary<DateOnly, long> TokensByDay(int daysBack = 30)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-daysBack);
        var byDay = new Dictionary<DateOnly, long>();
        foreach (var path in DiscoverLogFiles())
        {
            if (!FileMtimeAfter(path, cutoff))
                continue;
            foreach (var ev in IterUsageEvents(path))
            {
                if (ev.Timestamp is null)
                    continue;
                if (ev.Timestamp.Value < cutoff)
                    continue;
                var tokens = Tokens(ev);
                if (tokens <= 0)
                    continue;
                var localDate = LocalDate(ev.Timestamp.Value);
                byDay[localDate] = byDay.GetValueOrDefault(localDate) + tokens;
            }
        }
        return byDay;
    }

    /// <summary>Tokens grouped by working directory (<c>cwd</c>) since the
    /// cutoff. Events without a cwd are skipped.</summary>
    public Dictionary<string, long> TokensByProject(DateTimeOffset since)
    {
        var byProject = new Dictionary<string, long>();
        foreach (var path in DiscoverLogFiles())
        {
            if (!FileMtimeAfter(path, since))
                continue;
            foreach (var ev in IterUsageEvents(path))
            {
                if (ev.Timestamp is null || ev.Cwd is null)
                    continue;
                if (ev.Timestamp.Value < since)
                    continue;
                var tokens = Tokens(ev);
                if (tokens <= 0)
                    continue;
                byProject[ev.Cwd] = byProject.GetValueOrDefault(ev.Cwd) + tokens;
            }
        }
        return byProject;
    }

    /// <summary>Tokens grouped by <c>attributionSkill</c> since the cutoff.
    /// Events without one are skipped.</summary>
    public Dictionary<string, long> TokensBySkill(DateTimeOffset since)
    {
        var bySkill = new Dictionary<string, long>();
        foreach (var path in DiscoverLogFiles())
        {
            if (!FileMtimeAfter(path, since))
                continue;
            foreach (var ev in IterUsageEvents(path))
            {
                if (ev.Timestamp is null || string.IsNullOrEmpty(ev.AttributionSkill))
                    continue;
                if (ev.Timestamp.Value < since)
                    continue;
                var tokens = Tokens(ev);
                if (tokens <= 0)
                    continue;
                bySkill[ev.AttributionSkill] = bySkill.GetValueOrDefault(ev.AttributionSkill) + tokens;
            }
        }
        return bySkill;
    }

    /// <summary>Static tier projection over a raw CC model string — the vault's
    /// READ-TIME tier mapping (raw strings are stored; a mapping fix here
    /// retroactively heals all history). Null for unrecognized models —
    /// callers must keep unmapped tokens visible, never drop them.</summary>
    public static string? TierForModel(string? model)
    {
        if (string.IsNullOrEmpty(model))
            return null;
        foreach (var (prefix, tier) in ModelTierPrefixes)
        {
            if (model.StartsWith(prefix, StringComparison.Ordinal))
                return tier;
        }
        return null;
    }

    /// <summary>Map a CC <c>message.model</c> string to a Sanduhr tier key, or
    /// null for unrecognized models.</summary>
    public string? ModelToTierKey(string? model) => TierForModel(model);

    /// <summary>Same as <see cref="TokensSince"/> but keyed by Sanduhr tier.
    /// Models that don't map to a known tier are dropped.</summary>
    public Dictionary<string, long> TokensSinceByTier(DateTimeOffset cutoff)
    {
        var byModel = TokensSince(cutoff);
        var byTier = new Dictionary<string, long>();
        foreach (var (model, tokens) in byModel)
        {
            var tier = ModelToTierKey(model);
            if (tier is null)
                continue;
            byTier[tier] = byTier.GetValueOrDefault(tier) + tokens;
        }
        return byTier;
    }

    /// <summary>Folder names Claude Code and its tooling create git worktrees
    /// under, inside a repo. A session whose cwd sits below one of these is the
    /// parent repo's work (a subagent in its own checkout), so burn attribution
    /// rolls it up to the repo instead of listing one project per worktree.</summary>
    private static readonly string[][] WorktreeMarkers =
    {
        new[] { ".claude", "worktrees" },
        new[] { ".worktrees" },
    };

    /// <summary>Friendly project name out of a cwd path, e.g.
    /// <c>C:\Users\estev\Projects\Sanduhr</c> → <c>Sanduhr</c>. Handles both
    /// separators. Three rules, in order:
    /// <list type="number">
    ///   <item>A cwd under a worktree folder (<c>&lt;repo&gt;/.claude/worktrees/agent-x</c>,
    ///   <c>&lt;repo&gt;/.worktrees/x</c>, or deeper) names the repo, not the worktree.</item>
    ///   <item>Otherwise the nearest ancestor holding a <c>.git</c> entry names the
    ///   project, so a session opened in a SUBFOLDER of a repo
    ///   (<c>&lt;repo&gt;/functions</c>) is the repo's burn, not its own project.
    ///   The 2026-09-14 bulletin carried a "functions: 109k tokens" line for exactly
    ///   that reason.</item>
    ///   <item>Otherwise the basename, which is every cwd outside a repo.</item>
    /// </list>
    /// Callers that want the literal folder pass the full path through instead;
    /// the vault still keys rows by cwd hash, so this only merges DISPLAY names.</summary>
    public static string ProjectDisplayName(string cwd)
    {
        if (string.IsNullOrEmpty(cwd))
            return "";
        // Memoized: the ingester calls this once per session and the overview once
        // per project row, so without a cache one walk per event would hit disk
        // repeatedly for the same handful of directories.
        return DisplayNameCache.GetOrAdd(cwd, static c => ProjectDisplayName(c, DirectoryHoldsGit));
    }

    private static readonly ConcurrentDictionary<string, string> DisplayNameCache = new();

    /// <summary>Does this directory hold a <c>.git</c> entry? A directory in a normal
    /// clone, a FILE in a linked worktree or a submodule — both count, and both are
    /// probed because <see cref="Directory.Exists"/> is false for the file form.
    /// Any IO failure (a path on a disconnected drive, a permission wall) answers
    /// no, so attribution degrades to the basename instead of throwing.</summary>
    private static bool DirectoryHoldsGit(string dir)
    {
        try
        {
            var dot = dir + "/.git";
            return Directory.Exists(dot) || File.Exists(dot);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The name rule with the repo probe injected, so tests state which
    /// directories are repos instead of touching a real disk.</summary>
    public static string ProjectDisplayName(string cwd, Func<string, bool> dirHoldsGit)
    {
        if (string.IsNullOrEmpty(cwd))
            return "";
        var parts = cwd.Replace("\\", "/").TrimEnd('/').Split('/');
        if (WorktreeParentIndex(parts) is { } repoIndex && !string.IsNullOrEmpty(parts[repoIndex]))
            return parts[repoIndex];

        // Nearest enclosing repo wins, so a submodule keeps its own name rather
        // than folding into the superproject. Stops before the root segment
        // ("C:" or ""), which can never be a meaningful project name.
        for (int depth = parts.Length; depth > 1; depth--)
        {
            var name = parts[depth - 1];
            if (string.IsNullOrEmpty(name))
                continue;
            if (dirHoldsGit(string.Join('/', parts, 0, depth)))
                return name;
        }

        var last = parts.Length > 0 ? parts[^1] : cwd;
        return string.IsNullOrEmpty(last) ? cwd : last;
    }

    /// <summary>Index of the repo segment when <paramref name="parts"/> contains a
    /// worktree marker followed by at least one segment (the worktree itself);
    /// null otherwise. The first marker from the root wins, so a worktree inside a
    /// worktree still rolls up to the outermost repo.</summary>
    private static int? WorktreeParentIndex(string[] parts)
    {
        for (int i = 1; i < parts.Length; i++)
        {
            foreach (var marker in WorktreeMarkers)
            {
                if (i + marker.Length >= parts.Length)
                    continue;   // the marker needs a worktree name after it
                bool match = true;
                for (int k = 0; k < marker.Length; k++)
                {
                    if (!string.Equals(parts[i + k], marker[k], StringComparison.OrdinalIgnoreCase))
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return i - 1;
            }
        }
        return null;
    }

    /// <summary>Single-pass aggregation for the Local CC tab — one walk instead
    /// of three. Cached for <see cref="AggCacheTtlSec"/> seconds.</summary>
    public LocalCcAggregate AggregateForLocalCcTab(int daysBack = 30)
    {
        lock (_cacheLock)
        {
            if (_cacheResult is not null
                && _cacheDaysBack == daysBack
                && (DateTimeOffset.UtcNow - _cacheComputedAt).TotalSeconds < AggCacheTtlSec)
            {
                return _cacheResult;
            }
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-daysBack);
        var byDay = new Dictionary<DateOnly, long>();
        var byProject = new Dictionary<string, long>();
        var bySkill = new Dictionary<string, long>();
        var byDayInput = new Dictionary<DateOnly, long>();
        var byDayOutput = new Dictionary<DateOnly, long>();

        foreach (var path in DiscoverLogFiles())
        {
            if (!FileMtimeAfter(path, cutoff))
                continue;
            foreach (var ev in IterUsageEvents(path))
            {
                if (ev.Timestamp is null)
                    continue;
                if (ev.Timestamp.Value < cutoff)
                    continue;
                var tokens = Tokens(ev);
                if (tokens <= 0)
                    continue;

                var localDate = LocalDate(ev.Timestamp.Value);
                byDay[localDate] = byDay.GetValueOrDefault(localDate) + tokens;
                byDayInput[localDate] = byDayInput.GetValueOrDefault(localDate) + ev.Usage.InputTokens;
                byDayOutput[localDate] = byDayOutput.GetValueOrDefault(localDate) + ev.Usage.OutputTokens;
                if (!string.IsNullOrEmpty(ev.Cwd))
                    byProject[ev.Cwd] = byProject.GetValueOrDefault(ev.Cwd) + tokens;
                if (!string.IsNullOrEmpty(ev.AttributionSkill))
                    bySkill[ev.AttributionSkill] = bySkill.GetValueOrDefault(ev.AttributionSkill) + tokens;
            }
        }

        var result = new LocalCcAggregate(byDay, byProject, bySkill, byDayInput, byDayOutput);
        lock (_cacheLock)
        {
            _cacheComputedAt = DateTimeOffset.UtcNow;
            _cacheDaysBack = daysBack;
            _cacheResult = result;
        }
        return result;
    }

    /// <summary>Single-pass aggregate restricted to events whose LOCAL calendar
    /// date is today — the Overview's live-today source. The vault serves days
    /// strictly before today; this serves today; never both (the exclusion rule
    /// that prevents double-counting the vault's partial today row). Cached for
    /// <see cref="AggCacheTtlSec"/> seconds, independently of the 30-day cache.</summary>
    public LocalCcAggregate AggregateTodayOnly()
    {
        lock (_cacheLock)
        {
            if (_todayCacheResult is not null
                && (DateTimeOffset.UtcNow - _todayCacheComputedAt).TotalSeconds < AggCacheTtlSec)
            {
                return _todayCacheResult;
            }
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        // Offset AT MIDNIGHT, not now — on the DST fall-back day the current
        // offset would place the cutoff an hour late and the mtime prefilter
        // could skip files last written between 00:00 and 01:00 local.
        var midnightLocal = today.ToDateTime(TimeOnly.MinValue);
        var midnightUtc = new DateTimeOffset(
            midnightLocal, TimeZoneInfo.Local.GetUtcOffset(midnightLocal)).ToUniversalTime();
        var byDay = new Dictionary<DateOnly, long>();
        var byProject = new Dictionary<string, long>();
        var bySkill = new Dictionary<string, long>();
        var byDayInput = new Dictionary<DateOnly, long>();
        var byDayOutput = new Dictionary<DateOnly, long>();

        foreach (var path in DiscoverLogFiles())
        {
            if (!FileMtimeAfter(path, midnightUtc))
                continue;
            foreach (var ev in IterUsageEvents(path))
            {
                if (ev.Timestamp is null)
                    continue;
                var tokens = Tokens(ev);
                if (tokens <= 0)
                    continue;
                if (LocalDate(ev.Timestamp.Value) != today)
                    continue;
                byDay[today] = byDay.GetValueOrDefault(today) + tokens;
                byDayInput[today] = byDayInput.GetValueOrDefault(today) + ev.Usage.InputTokens;
                byDayOutput[today] = byDayOutput.GetValueOrDefault(today) + ev.Usage.OutputTokens;
                if (!string.IsNullOrEmpty(ev.Cwd))
                    byProject[ev.Cwd] = byProject.GetValueOrDefault(ev.Cwd) + tokens;
                if (!string.IsNullOrEmpty(ev.AttributionSkill))
                    bySkill[ev.AttributionSkill] = bySkill.GetValueOrDefault(ev.AttributionSkill) + tokens;
            }
        }

        var result = new LocalCcAggregate(byDay, byProject, bySkill, byDayInput, byDayOutput);
        lock (_cacheLock)
        {
            _todayCacheComputedAt = DateTimeOffset.UtcNow;
            _todayCacheResult = result;
        }
        return result;
    }

    /// <summary>Date-bounded, per-root burn for ONE local calendar day — the
    /// publisher's source (the relative 1/7/30-day windows serve the MCP burn
    /// tool; a daily upload needs a closed day). Scans only <paramref name="rootPath"/>
    /// (per-root keying is the tenant wall — the merged <see cref="DiscoverLogFiles"/>
    /// walk must not be used here). Projects are keyed by basename; events without
    /// a cwd stay visible as <c>(unknown)</c>; models with no tier mapping count
    /// toward the total but not <c>ByTier</c>. Uncached — one call per day.</summary>
    public RootDayBurn BurnForLocalDay(string rootName, string rootPath, DateOnly day)
    {
        // mtime prefilter anchored at the DAY's local midnight (offset at
        // midnight, not now — the AggregateTodayOnly DST lesson).
        var midnightLocal = day.ToDateTime(TimeOnly.MinValue);
        var midnightUtc = new DateTimeOffset(
            midnightLocal, TimeZoneInfo.Local.GetUtcOffset(midnightLocal)).ToUniversalTime();

        long total = 0;
        var byProject = new Dictionary<string, long>(StringComparer.Ordinal);
        var byTier = new Dictionary<string, long>(StringComparer.Ordinal);

        string projects = Path.Combine(rootPath, "projects");
        if (!Directory.Exists(projects))
            return new RootDayBurn(rootName, 0, byProject, byTier);

        foreach (var projectDir in Directory.GetDirectories(projects))
        {
            foreach (var path in Directory.GetFiles(projectDir, "*.jsonl", SearchOption.AllDirectories))
            {
                if (!FileMtimeAfter(path, midnightUtc))
                    continue;
                foreach (var ev in IterUsageEvents(path))
                {
                    if (ev.Timestamp is null || LocalDate(ev.Timestamp.Value) != day)
                        continue;
                    var tokens = Tokens(ev);
                    if (tokens <= 0)
                        continue;
                    total += tokens;
                    string project = ev.Cwd is { Length: > 0 } cwd ? ProjectDisplayName(cwd) : "(unknown)";
                    byProject[project] = byProject.GetValueOrDefault(project) + tokens;
                    if (TierForModel(ev.Model) is { } tier)
                        byTier[tier] = byTier.GetValueOrDefault(tier) + tokens;
                }
            }
        }
        return new RootDayBurn(rootName, total, byProject, byTier);
    }

    /// <summary>Force the next <see cref="AggregateForLocalCcTab"/> to recompute.</summary>
    public void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cacheComputedAt = DateTimeOffset.MinValue;
            _cacheResult = null;
            _todayCacheComputedAt = DateTimeOffset.MinValue;
            _todayCacheResult = null;
        }
    }

    // -- helpers --------------------------------------------------------------

    private static long Tokens(UsageEvent ev) => ev.Usage.InputTokens + ev.Usage.OutputTokens;

    private static DateOnly LocalDate(DateTimeOffset ts) => DateOnly.FromDateTime(ts.ToLocalTime().DateTime);

    private static bool FileMtimeAfter(string path, DateTimeOffset cutoff)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path) >= cutoff.UtcDateTime;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static DateTimeOffset? ParseIso(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return null;
        return DateTimeOffset.TryParse(
            s.Replace("Z", "+00:00"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var dt)
            ? dt
            : null;
    }

    private static string? Str(JsonObject o, string key)
        => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static long Num(JsonObject o, string key)
    {
        if (o[key] is not JsonValue v)
            return 0;
        if (v.TryGetValue<long>(out var l))
            return l;
        if (v.TryGetValue<double>(out var d))
            return (long)d;
        return 0;
    }
}
