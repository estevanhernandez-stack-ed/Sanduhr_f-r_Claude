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

    public CcLogReader(string? homeDir = null)
    {
        _homeDir = homeDir ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>Candidate CC config roots in the user's home that actually exist.</summary>
    public IReadOnlyList<string> SearchRoots()
    {
        var outp = new List<string>();
        foreach (var name in LogRootNames)
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
                var line = rawLine.Trim();
                if (line.Length == 0)
                    continue;
                JsonNode? node;
                try
                {
                    node = JsonNode.Parse(line);
                }
                catch (JsonException)
                {
                    continue;
                }
                if (node is not JsonObject d)
                    continue;
                if (Str(d, "type") != "assistant")
                    continue;
                if (d["message"] is not JsonObject msg)
                    continue;
                if (msg["usage"] is not JsonObject usage)
                    continue;
                yield return new UsageEvent(
                    ParseIso(Str(d, "timestamp")),
                    Str(msg, "model"),
                    new TokenUsage(Num(usage, "input_tokens"), Num(usage, "output_tokens")),
                    Str(d, "cwd"),
                    Str(d, "attributionSkill"));
            }
        }
    }

    /// <summary><c>{model_name: total_tokens}</c> across ALL session files,
    /// considering only events with timestamp &gt;= <paramref name="cutoff"/>.
    /// Models with zero new tokens are omitted.</summary>
    public Dictionary<string, long> TokensSince(DateTimeOffset cutoff)
    {
        var totals = new Dictionary<string, long>();
        foreach (var path in DiscoverLogFiles())
        {
            if (!FileMtimeAfter(path, cutoff))
                continue;
            foreach (var ev in IterUsageEvents(path))
            {
                if (ev.Timestamp is null || ev.Model is null)
                    continue;
                if (ev.Timestamp.Value < cutoff)
                    continue;
                var tokens = Tokens(ev);
                if (tokens <= 0)
                    continue;
                totals[ev.Model] = totals.GetValueOrDefault(ev.Model) + tokens;
            }
        }
        return totals;
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

    /// <summary>Friendly basename out of a cwd path, e.g.
    /// <c>C:\Users\estev\Projects\Sanduhr</c> → <c>Sanduhr</c>. Handles both
    /// separators.</summary>
    public static string ProjectDisplayName(string cwd)
    {
        if (string.IsNullOrEmpty(cwd))
            return "";
        var parts = cwd.Replace("\\", "/").TrimEnd('/').Split('/');
        var last = parts.Length > 0 ? parts[^1] : cwd;
        return string.IsNullOrEmpty(last) ? cwd : last;
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
