using System.Globalization;

namespace Sanduhr.Core;

/// <summary>Merged closed-day window across consented roots (Overview's
/// vault side). ByProjectName merges by display name — parity with the live
/// tab's basename merge.</summary>
public sealed record VaultWindow(
    Dictionary<DateOnly, long> ByDay,
    Dictionary<string, long> ByProjectName,
    Dictionary<string, long> BySkill);

/// <summary>One Trends bar. HasNoRecordGap = some day of the week (clamped to
/// today) is not covered by EVERY consented root — rendered as the "no record"
/// texture, never a zero-height bar (auto-start is off by default; a
/// widget-off fortnight must not read as a vacation).</summary>
public sealed record VaultWeek(DateOnly WeekStart, long Total, bool IsCurrent, bool HasNoRecordGap);

/// <summary>One logical session — primary + continuation slices merged.</summary>
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
    VaultCacheTokens? Cache);

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
        return new VaultWindow(byDay, byProject, bySkill);
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
            foreach (var (uuid, rows) in byUuid)
            {
                var merged = VaultRowMath.Merge(rows);
                if (!TryTs(merged.FirstTs, out var first) || !TryTs(merged.LastTs, out var last))
                    continue;
                result.Add(new VaultSessionInfo(
                    uuid, root, merged.ProjectKey, merged.ProjectName, merged.Cwd,
                    first, last, merged.Total, merged.ByModel, merged.BySkill,
                    merged.ByDay, merged.CacheTokens));
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
}
