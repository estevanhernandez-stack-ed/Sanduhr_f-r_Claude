using System.Globalization;

namespace Sanduhr.Core;

/// <summary>Merged closed-day window across consented roots (Overview's
/// vault side). ByProjectName merges by display name — parity with the live
/// tab's basename merge. ByDayInput/ByDayOutput carry the sent/received split
/// from rollup days; a day at 0/0 while ByDay shows tokens is WS-C-era legacy
/// (unsplit), not zero traffic.</summary>
public sealed record VaultWindow(
    Dictionary<DateOnly, long> ByDay,
    Dictionary<string, long> ByProjectName,
    Dictionary<string, long> BySkill,
    Dictionary<DateOnly, long> ByDayInput,
    Dictionary<DateOnly, long> ByDayOutput);

/// <summary>One Trends bar. HasNoRecordGap = some day of the week (clamped to
/// today) is not covered by EVERY consented root — rendered as the "no record"
/// texture, never a zero-height bar (auto-start is off by default; a
/// widget-off fortnight must not read as a vacation).</summary>
public sealed record VaultWeek(DateOnly WeekStart, long Total, bool IsCurrent, bool HasNoRecordGap);

/// <summary>One logical session — the main transcript plus its nested
/// subagent transcripts (parent_session ?? uuid), each member's primary +
/// continuation slices merged first. AgentCount/AgentTokens summarize the
/// members whose file-uuid differs from the logical id.</summary>
public sealed record VaultSessionInfo(
    string Uuid,
    string Root,
    string ProjectKey,
    string ProjectName,
    string? Cwd,
    DateTimeOffset FirstTs,
    DateTimeOffset LastTs,
    long Total,
    Dictionary<string, long> ByModel,
    Dictionary<string, long>? BySkill,
    Dictionary<string, VaultDayBucket> ByDay,
    VaultCacheTokens? Cache,
    int AgentCount,
    long AgentTokens);

/// <summary>
/// Read side of the vault. NEVER mutates: corrupt or missing shards degrade to
/// empty and are left for the next ingest cycle to quarantine. All aggregation
/// here is Core-tested so the App layer stays a projection.
/// </summary>
public sealed class VaultReader
{
    private readonly VaultStore _store;

    public VaultReader(VaultStore store) => _store = store;

    public static string ProjectNameOf(string projectKey)
    {
        int idx = projectKey.LastIndexOf('~');
        return idx > 0 ? projectKey[..idx] : projectKey;
    }

    public VaultWindow ReadWindow(IReadOnlyList<string> roots, DateOnly fromInclusive, DateOnly toExclusive)
    {
        var byDay = new Dictionary<DateOnly, long>();
        var byProject = new Dictionary<string, long>();
        var bySkill = new Dictionary<string, long>();
        var byDayInput = new Dictionary<DateOnly, long>();
        var byDayOutput = new Dictionary<DateOnly, long>();
        foreach (var root in roots)
        {
            foreach (var month in MonthsBetween(fromInclusive, toExclusive))
            {
                if (_store.TryLoadRollupShard(root, month, out var shard) != ShardLoadResult.Ok)
                    continue;
                foreach (var (dayKey, day) in shard.Days)
                {
                    if (!TryDay(dayKey, out var date) || date < fromInclusive || date >= toExclusive)
                        continue;
                    byDay[date] = byDay.GetValueOrDefault(date) + day.Total;
                    byDayInput[date] = byDayInput.GetValueOrDefault(date) + day.Input;
                    byDayOutput[date] = byDayOutput.GetValueOrDefault(date) + day.Output;
                    foreach (var (projectKey, v) in day.ByProject)
                    {
                        var name = ProjectNameOf(projectKey);
                        byProject[name] = byProject.GetValueOrDefault(name) + v;
                    }
                    foreach (var (skill, v) in day.BySkill)
                        bySkill[skill] = bySkill.GetValueOrDefault(skill) + v;
                }
            }
        }
        return new VaultWindow(byDay, byProject, bySkill, byDayInput, byDayOutput);
    }

    public IReadOnlyList<VaultWeek> ReadWeeks(IReadOnlyList<string> roots, int weeks, DateOnly today)
    {
        var currentWeekStart = WeekStart(today);
        var firstWeekStart = currentWeekStart.AddDays(-7 * (weeks - 1));
        var window = ReadWindow(roots, firstWeekStart, today.AddDays(1));
        // One meta read per root per render — the per-day public IsDayCovered
        // would be weeks x 7 x roots file reads on a UI path.
        var metas = roots.Select(r => _store.LoadMeta(r)).ToList();

        var result = new List<VaultWeek>(weeks);
        for (int i = 0; i < weeks; i++)
        {
            var start = firstWeekStart.AddDays(7 * i);
            bool isCurrent = start == currentWeekStart;
            long total = 0;
            bool gap = false;
            for (int d = 0; d < 7; d++)
            {
                var day = start.AddDays(d);
                if (day > today)
                    break;
                total += window.ByDay.GetValueOrDefault(day);
                if (!IsDayCovered(metas, day))
                    gap = true;
            }
            result.Add(new VaultWeek(start, total, isCurrent, gap));
        }
        return result;
    }

    public IReadOnlyList<(string Name, long Total)> TopProjects(
        IReadOnlyList<string> roots, DateOnly fromInclusive, DateOnly toExclusive, int top)
        => ReadWindow(roots, fromInclusive, toExclusive).ByProjectName
            .OrderByDescending(kv => kv.Value)
            .Take(top)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

    public IReadOnlyList<VaultSessionInfo> ReadSessions(IReadOnlyList<string> roots)
    {
        var result = new List<VaultSessionInfo>();
        foreach (var root in roots)
        {
            var byUuid = new Dictionary<string, List<VaultSessionRow>>();
            foreach (var month in _store.ListSessionShardMonths(root))
            {
                if (_store.TryLoadSessionShard(root, month, out var shard) != ShardLoadResult.Ok)
                    continue;   // read path never mutates — next ingest quarantines
                foreach (var (uuid, row) in shard.Sessions)
                {
                    if (!byUuid.TryGetValue(uuid, out var list))
                        byUuid[uuid] = list = new List<VaultSessionRow>();
                    list.Add(row);
                }
            }
            // Logical fold: a session = its main transcript + nested subagent
            // transcripts (parent_session ?? uuid). Identity comes from the
            // main member when present; an aged-out main leaves the ordinal-
            // first member to speak for the group.
            var byLogical = new Dictionary<string, List<(string FileUuid, VaultSessionRow Row)>>(StringComparer.Ordinal);
            foreach (var (uuid, rows) in byUuid)
            {
                var merged = VaultRowMath.Merge(rows);
                var logicalId = merged.ParentSession ?? uuid;
                if (!byLogical.TryGetValue(logicalId, out var list))
                    byLogical[logicalId] = list = new List<(string, VaultSessionRow)>();
                list.Add((uuid, merged));
            }
            foreach (var (logicalId, members) in byLogical)
            {
                members.Sort((a, b) => string.CompareOrdinal(a.FileUuid, b.FileUuid));
                var identity = members.Where(m => m.FileUuid == logicalId).Select(m => m.Row).FirstOrDefault()
                               ?? members[0].Row;
                var fold = VaultRowMath.Merge(members.Select(m => m.Row).ToList());
                // Merge() takes first_ts/last_ts/cache from the primary; a
                // logical fold needs min/max/sums across members instead.
                var firstTs = members.Min(m => ParseTsOrMax(m.Row.FirstTs));
                var lastTs = members.Max(m => ParseTsOrMin(m.Row.LastTs));
                long cacheRead = members.Sum(m => m.Row.CacheTokens?.Read ?? 0);
                long cacheCreation = members.Sum(m => m.Row.CacheTokens?.Creation ?? 0);
                int agentCount = members.Count(m => m.FileUuid != logicalId);
                long agentTokens = members.Where(m => m.FileUuid != logicalId).Sum(m => m.Row.Total);
                if (firstTs == DateTimeOffset.MaxValue || lastTs == DateTimeOffset.MinValue)
                    continue;
                result.Add(new VaultSessionInfo(
                    logicalId, root, identity.ProjectKey, identity.ProjectName, identity.Cwd,
                    firstTs, lastTs, fold.Total, fold.ByModel, fold.BySkill, fold.ByDay,
                    (cacheRead + cacheCreation) > 0
                        ? new VaultCacheTokens { Read = cacheRead, Creation = cacheCreation }
                        : null,
                    agentCount, agentTokens));
            }
        }
        return result;
    }

    public static long TokensInScope(VaultSessionInfo s, DateOnly fromInclusive, DateOnly toInclusive)
    {
        long total = 0;
        foreach (var (dayKey, bucket) in s.ByDay)
        {
            if (TryDay(dayKey, out var day) && day >= fromInclusive && day <= toInclusive)
                total += bucket.Total;
        }
        return total;
    }

    public DateOnly? BirthDate(IReadOnlyList<string> roots)
    {
        DateOnly? min = null;
        foreach (var root in roots)
        {
            if (_store.LoadMeta(root) is { } meta && TryDay(meta.Since, out var since))
            {
                if (min is null || since < min)
                    min = since;
            }
        }
        return min;
    }

    /// <summary>MIN of the consented roots' last successful ingest — any stale
    /// (or never-started) root makes the whole tab report degraded; frozen
    /// numbers with no signal would be the design lying.</summary>
    public DateTimeOffset? LastSuccessfulIngestUtc(IReadOnlyList<string> roots)
    {
        DateTimeOffset? min = null;
        foreach (var root in roots)
        {
            if (_store.LoadMeta(root) is not { } meta || !TryTs(meta.LastIngestTs, out var ts))
                return null;
            if (min is null || ts < min)
                min = ts;
        }
        return min;
    }

    /// <summary>Batch coverage: every day in [from, to] covered by EVERY
    /// consented root. Metas load once — the per-day IsDayCovered would be
    /// two file reads per cell on a UI path (the ReadWeeks lesson).</summary>
    public HashSet<DateOnly> CoveredSet(IReadOnlyList<string> roots, DateOnly fromInclusive, DateOnly toInclusive)
    {
        var result = new HashSet<DateOnly>();
        if (roots.Count == 0)
            return result;
        var metas = roots.Select(r => _store.LoadMeta(r)).ToList();
        for (var d = fromInclusive; d <= toInclusive; d = d.AddDays(1))
        {
            if (IsDayCovered(metas, d))
                result.Add(d);
        }
        return result;
    }

    /// <summary>Covered only when EVERY consented root's ranges contain the day.</summary>
    public bool IsDayCovered(IReadOnlyList<string> roots, DateOnly day)
        => IsDayCovered(roots.Count == 0
            ? new List<VaultRootMeta?>()
            : roots.Select(r => _store.LoadMeta(r)).ToList(), day);

    private static bool IsDayCovered(IReadOnlyList<VaultRootMeta?> metas, DateOnly day)
    {
        if (metas.Count == 0)
            return false;
        foreach (var meta in metas)
        {
            if (meta is null)
                return false;
            bool covered = false;
            foreach (var range in meta.Covered)
            {
                if (TryDay(range.From, out var from) && TryDay(range.To, out var to)
                    && day >= from && day <= to)
                {
                    covered = true;
                    break;
                }
            }
            if (!covered)
                return false;
        }
        return true;
    }

    internal static DateOnly WeekStart(DateOnly day)
    {
        int diff = ((int)day.DayOfWeek + 6) % 7;   // Monday = 0
        return day.AddDays(-diff);
    }

    private static IEnumerable<string> MonthsBetween(DateOnly fromInclusive, DateOnly toExclusive)
    {
        var cursor = new DateOnly(fromInclusive.Year, fromInclusive.Month, 1);
        var end = toExclusive.AddDays(-1);
        while (cursor <= end)
        {
            yield return cursor.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            cursor = cursor.AddMonths(1);
        }
    }

    private static bool TryDay(string s, out DateOnly day)
        => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    private static bool TryTs(string s, out DateTimeOffset ts)
        => DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out ts);

    // Fold-friendly wrappers: unparseable stamps become the identity for
    // min/max, so one bad member can't sink the whole logical session.
    private static DateTimeOffset ParseTsOrMax(string s)
        => TryTs(s, out var ts) ? ts : DateTimeOffset.MaxValue;

    private static DateTimeOffset ParseTsOrMin(string s)
        => TryTs(s, out var ts) ? ts : DateTimeOffset.MinValue;
}
