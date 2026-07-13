using System.Text.Json.Serialization;

namespace Sanduhr.Core;

/// <summary>
/// Wire schema for the usage vault (spec 2026-07-12-usage-vault-design.md).
/// Session shards are the irreplaceable primary record: raw model strings
/// (never tiers), unconditional totals, per-local-day buckets. Meaning changes
/// take a NEW field name; readers accept every schema_version &lt;= CurrentSchemaVersion
/// forever — the source to re-derive old shards is gone.
/// </summary>
public static class VaultSchema
{
    public const int CurrentSchemaVersion = 1;
}

/// <summary>One local calendar day inside a session row. Total is unconditional
/// (every timestamped token-bearing event); by_model keys are raw CC model strings.</summary>
public sealed class VaultDayBucket
{
    [JsonPropertyName("total")] public long Total { get; set; }

    /// <summary>Sent-side tokens (input). Always written on new buckets;
    /// absent on WS-C-era rows — 0 means "unsplit", and readers must treat a
    /// day whose input+output is 0 while total &gt; 0 as legacy, not as zero
    /// traffic (the Overview's "(partial)" rule).</summary>
    [JsonPropertyName("input")] public long Input { get; set; }

    /// <summary>Received-side tokens (output).</summary>
    [JsonPropertyName("output")] public long Output { get; set; }

    [JsonPropertyName("by_model")]
    public Dictionary<string, long> ByModel { get; set; } = new();

    /// <summary>Per-day skill split — needed so the rollup fold (per-day by_skill)
    /// reads exactly one shard. Omitted when the day had no attributed events.</summary>
    [JsonPropertyName("by_skill")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, long>? BySkill { get; set; }
}

public sealed class VaultCacheTokens
{
    [JsonPropertyName("read")] public long Read { get; set; }
    [JsonPropertyName("creation")] public long Creation { get; set; }
}

/// <summary>
/// One session's aggregates (or one month-slice of a session when
/// <see cref="Continuation"/> is true). Row invariant:
/// <c>Total == sum(ByDay[*].Total)</c> — every row is self-consistent, so the
/// rollup fold for a day reads exactly one month's shard. EventCount,
/// SkippedLines and CacheTokens live on the PRIMARY row only (slices carry 0 /
/// null — they cannot be split by month from day buckets).
/// </summary>
public sealed class VaultSessionRow
{
    [JsonPropertyName("project_key")] public string ProjectKey { get; set; } = "";
    [JsonPropertyName("project_name")] public string ProjectName { get; set; } = "";

    /// <summary>Populated ONLY when the store_full_paths setting (off by default) is on.</summary>
    [JsonPropertyName("cwd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cwd { get; set; }

    /// <summary>The session this transcript belongs to when it is a NESTED
    /// subagent/workflow transcript ({projectDir}\{parent-uuid}\...\x.jsonl).
    /// Null for main transcripts. Readers fold rows by parent_session ?? uuid.</summary>
    [JsonPropertyName("parent_session")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentSession { get; set; }

    [JsonPropertyName("first_ts")] public string FirstTs { get; set; } = "";
    [JsonPropertyName("last_ts")] public string LastTs { get; set; } = "";
    [JsonPropertyName("utc_offset_min")] public int UtcOffsetMin { get; set; }
    [JsonPropertyName("event_count")] public long EventCount { get; set; }
    [JsonPropertyName("skipped_lines")] public long SkippedLines { get; set; }
    [JsonPropertyName("continuation")] public bool Continuation { get; set; }
    [JsonPropertyName("total")] public long Total { get; set; }

    [JsonPropertyName("by_model")]
    public Dictionary<string, long> ByModel { get; set; } = new();

    [JsonPropertyName("cache_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VaultCacheTokens? CacheTokens { get; set; }

    [JsonPropertyName("by_skill")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, long>? BySkill { get; set; }

    [JsonPropertyName("by_day")]
    public Dictionary<string, VaultDayBucket> ByDay { get; set; } = new();
}

public sealed class VaultSessionShard
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = VaultSchema.CurrentSchemaVersion;
    [JsonPropertyName("writer_version")] public string WriterVersion { get; set; } = "";
    [JsonPropertyName("sessions")] public Dictionary<string, VaultSessionRow> Sessions { get; set; } = new();
}

/// <summary>Rollups are a DECLARED DERIVED CACHE — deletable at any time,
/// rebuilt by folding the month's session shard. Never a second truth.</summary>
public sealed class VaultRollupDay
{
    [JsonPropertyName("total")] public long Total { get; set; }

    /// <summary>Sent/received split, rebuilt by the fold from bucket
    /// input/output. 0/0 while total &gt; 0 means the month's buckets are
    /// WS-C-era legacy (unsplit), not zero traffic.</summary>
    [JsonPropertyName("input")] public long Input { get; set; }
    [JsonPropertyName("output")] public long Output { get; set; }

    [JsonPropertyName("by_model")] public Dictionary<string, long> ByModel { get; set; } = new();
    [JsonPropertyName("by_project")] public Dictionary<string, long> ByProject { get; set; } = new();
    [JsonPropertyName("by_skill")] public Dictionary<string, long> BySkill { get; set; } = new();
    [JsonPropertyName("sessions")] public int Sessions { get; set; }
}

public sealed class VaultRollupShard
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = VaultSchema.CurrentSchemaVersion;
    [JsonPropertyName("days")] public Dictionary<string, VaultRollupDay> Days { get; set; } = new();
}

/// <summary>Bookkeeping only — the "vault outlives its sources" property belongs
/// to shards, not checkpoints. Keyed by SHA-256 of the lowercased absolute path
/// so this file is never a readable path ledger.</summary>
public sealed class VaultCheckpointEntry
{
    [JsonPropertyName("mtime_ticks")] public long MtimeTicks { get; set; }
    [JsonPropertyName("length")] public long Length { get; set; }
    [JsonPropertyName("offset")] public long Offset { get; set; }
    [JsonPropertyName("tail_guard")] public string TailGuard { get; set; } = "";

    /// <summary>Fingerprint of the stored rows this checkpoint corresponds to
    /// (sum of the session's row totals / event counts). A tail parse is only
    /// trusted when the stored rows MATCH this fingerprint — after a crash
    /// between the shard write and the checkpoint write, the rows are newer
    /// than the checkpoint and a seeded tail parse would double-count; the
    /// mismatch forces the idempotent full reparse instead.</summary>
    [JsonPropertyName("row_total")] public long RowTotal { get; set; }
    [JsonPropertyName("row_events")] public long RowEvents { get; set; }
    [JsonPropertyName("row_cache_read")] public long RowCacheRead { get; set; }
    [JsonPropertyName("row_cache_creation")] public long RowCacheCreation { get; set; }

    [JsonPropertyName("months")] public List<string> Months { get; set; } = new();
    [JsonPropertyName("sealed")] public bool Sealed { get; set; }
    [JsonPropertyName("last_seen")] public string LastSeenTs { get; set; } = "";
}

public sealed class VaultCheckpointFile
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = VaultSchema.CurrentSchemaVersion;
    [JsonPropertyName("entries")] public Dictionary<string, VaultCheckpointEntry> Entries { get; set; } = new();
}

public sealed class VaultDateRange
{
    [JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonPropertyName("to")] public string To { get; set; } = "";
}

/// <summary>Per-root vault metadata: birth date (Trends footer), ingest-coverage
/// ranges (the "no record" texture), and the last successful ingest stamp
/// (degraded-mode gate). Lives inside the root's folder so purging the folder
/// purges the bookkeeping with it.</summary>
public sealed class VaultRootMeta
{
    [JsonPropertyName("since")] public string Since { get; set; } = "";
    [JsonPropertyName("covered")] public List<VaultDateRange> Covered { get; set; } = new();
    [JsonPropertyName("last_ingest_ts")] public string LastIngestTs { get; set; } = "";

    /// <summary>Discovery-walk generation. Absent (0) or 1 = the one-level
    /// pre-WS-C.1 walk; the ingester invalidates checkpoints ONCE when this is
    /// below its CurrentWalkVersion so the full re-ingest picks up nested
    /// subagent transcripts still inside CC retention.</summary>
    [JsonPropertyName("walk_version")]
    public int WalkVersion { get; set; }
}

public enum ShardLoadResult
{
    Ok,
    Missing,
    Corrupt,
}

/// <summary>
/// Row algebra shared by the ingester (split a parsed session into per-month
/// rows) and every reader (merge a session's primary + continuation slices
/// back into one logical session). Merge(SplitByMonth(x)) == x.
/// </summary>
public static class VaultRowMath
{
    /// <summary>Split a merged session row into per-month rows keyed yyyy-MM.
    /// The primary month is the month of the earliest by_day key (== the local
    /// date of first_ts, since the first counted event defines both). Each row's
    /// Total/ByModel/BySkill are recomputed from ITS OWN day buckets (row
    /// invariant); EventCount/SkippedLines/CacheTokens ride the primary only.</summary>
    public static Dictionary<string, VaultSessionRow> SplitByMonth(VaultSessionRow merged)
    {
        var result = new Dictionary<string, VaultSessionRow>();
        if (merged.ByDay.Count == 0)
            return result;
        var primaryMonth = merged.ByDay.Keys.Min(StringComparer.Ordinal)![..7];

        foreach (var group in merged.ByDay.GroupBy(kv => kv.Key[..7]))
        {
            bool primary = group.Key == primaryMonth;
            var row = new VaultSessionRow
            {
                ProjectKey = merged.ProjectKey,
                ProjectName = merged.ProjectName,
                Cwd = merged.Cwd,
                ParentSession = merged.ParentSession,
                FirstTs = merged.FirstTs,
                LastTs = merged.LastTs,
                UtcOffsetMin = merged.UtcOffsetMin,
                EventCount = primary ? merged.EventCount : 0,
                SkippedLines = primary ? merged.SkippedLines : 0,
                Continuation = !primary,
                CacheTokens = primary ? merged.CacheTokens : null,
                ByDay = group.ToDictionary(kv => kv.Key, kv => kv.Value),
            };
            RecomputeRowAggregates(row);
            result[group.Key] = row;
        }
        return result;
    }

    /// <summary>Merge one session's rows (primary + slices, any order) into a
    /// single logical row. Continuation is false on the result.</summary>
    public static VaultSessionRow Merge(IReadOnlyList<VaultSessionRow> rows)
    {
        var primary = rows.FirstOrDefault(r => !r.Continuation) ?? rows[0];
        var merged = new VaultSessionRow
        {
            ProjectKey = primary.ProjectKey,
            ProjectName = primary.ProjectName,
            Cwd = primary.Cwd,
            ParentSession = primary.ParentSession,
            FirstTs = primary.FirstTs,
            LastTs = primary.LastTs,
            UtcOffsetMin = primary.UtcOffsetMin,
            EventCount = rows.Sum(r => r.EventCount),
            SkippedLines = rows.Sum(r => r.SkippedLines),
            Continuation = false,
            CacheTokens = rows.Select(r => r.CacheTokens).FirstOrDefault(c => c is not null),
            ByDay = new Dictionary<string, VaultDayBucket>(),
        };
        foreach (var row in rows)
            foreach (var (day, bucket) in row.ByDay)
                merged.ByDay[day] = bucket;   // day-in-own-month invariant: no key collides
        RecomputeRowAggregates(merged);
        return merged;
    }

    /// <summary>Total/ByModel/BySkill := sums of the row's own day buckets.</summary>
    public static void RecomputeRowAggregates(VaultSessionRow row)
    {
        long total = 0;
        var byModel = new Dictionary<string, long>();
        var bySkill = new Dictionary<string, long>();
        foreach (var bucket in row.ByDay.Values)
        {
            total += bucket.Total;
            foreach (var (m, v) in bucket.ByModel)
                byModel[m] = byModel.GetValueOrDefault(m) + v;
            if (bucket.BySkill is not null)
                foreach (var (s, v) in bucket.BySkill)
                    bySkill[s] = bySkill.GetValueOrDefault(s) + v;
        }
        row.Total = total;
        row.ByModel = byModel;
        row.BySkill = bySkill.Count > 0 ? bySkill : null;
    }
}
