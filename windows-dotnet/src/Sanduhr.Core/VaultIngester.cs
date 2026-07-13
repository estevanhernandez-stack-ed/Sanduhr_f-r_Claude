using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>Counts for one ingest cycle. Acquired=false means another Sanduhr
/// process held the writer mutex and this cycle skipped entirely.</summary>
public sealed record VaultIngestResult(
    bool Acquired,
    int FilesSeen,
    int FilesFullParsed,
    int FilesTailParsed,
    int FilesSkipped,
    int FilesFailed,
    int RootsAborted);

/// <summary>
/// The vault's only writer (spec: Ingestion). Walks each CONSENTED CC root's
/// projects tree, parses session JSONLs into per-month session shards, folds
/// rollups, and advances checkpoints — in that order, checkpoints LAST, so a
/// crash at any point leaves a stale checkpoint and the next cycle converges.
///
/// Single-threaded and synchronous by design (callers Task.Run it); cross-
/// process exclusion via the named mutex; per-file stat captured from the
/// walk's FileInfo BEFORE any open. This class never uses
/// CcLogReader.IterUsageEvents — its silent empty-on-failure would advance a
/// checkpoint past unread data.
/// </summary>
public sealed class VaultIngester
{
    private const int CoverageMarginDays = 25;      // CC retention (~30d) minus safety
    private const int CheckpointPruneDays = 7;

    /// <summary>Bump when the discovery walk widens; a root whose meta carries
    /// an older value gets its checkpoints invalidated once so the re-ingest
    /// covers the newly visible files.</summary>
    public const int CurrentWalkVersion = 2;

    internal const int TailGuardBytes = 64;
    internal static readonly TimeSpan QuiesceAfter = TimeSpan.FromHours(1);

    private readonly string _homeDir;
    private readonly VaultStore _store;
    private readonly string _writerVersion;
    private readonly string? _logFile;
    private readonly TimeZoneInfo _tz;
    private readonly string _mutexName;

    public VaultIngester(
        string homeDir,
        VaultStore store,
        string writerVersion,
        string? logFile = null,
        TimeZoneInfo? timeZone = null,
        string? mutexName = null)
    {
        _homeDir = homeDir;
        _store = store;
        _writerVersion = writerVersion;
        _logFile = logFile;
        _tz = timeZone ?? TimeZoneInfo.Local;
        _mutexName = mutexName ?? "Global\\Sanduhr.VaultWriter";
    }

    /// <param name="stillConsented">Erase-vs-inflight-cycle guard. This cycle's
    /// roots were snapshotted BEFORE any consent flip a purge/erase makes, and
    /// the initial backfill can outlive the eraser's mutex-wait timeout — so
    /// each root consults this predicate once more immediately before its write
    /// phase. False means parse counters stand but nothing lands on disk: the
    /// just-purged folder stays purged instead of silently resurrecting.</param>
    public VaultIngestResult IngestOnce(
        IReadOnlyList<string> consentedRootNames, bool storeFullPaths, DateTimeOffset nowUtc,
        Func<string, bool>? stillConsented = null)
    {
        using var mutex = new Mutex(initiallyOwned: false, _mutexName);
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;   // previous holder died; state converges by design
        }
        if (!acquired)
        {
            Log("ingest skipped (writer mutex held)");
            return new VaultIngestResult(false, 0, 0, 0, 0, 0, 0);
        }
        try
        {
            int seen = 0, full = 0, tail = 0, skipped = 0, failed = 0, aborted = 0;
            foreach (var rootName in consentedRootNames)
            {
                var r = IngestRoot(rootName, storeFullPaths, nowUtc, stillConsented);
                seen += r.FilesSeen; full += r.FilesFullParsed; tail += r.FilesTailParsed;
                skipped += r.FilesSkipped; failed += r.FilesFailed; aborted += r.RootsAborted;
            }
            return new VaultIngestResult(true, seen, full, tail, skipped, failed, aborted);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private VaultIngestResult IngestRoot(
        string rootName, bool storeFullPaths, DateTimeOffset nowUtc, Func<string, bool>? stillConsented)
    {
        int seen = 0, full = 0, tailParsed = 0, skipped = 0, failed = 0;

        // walk_version gate: a vault written by an older (narrower) walk
        // re-ingests everything once. Aborted cycles leave the old meta, so
        // this re-fires until a cycle completes — idempotent convergence.
        var existingMeta = _store.LoadMeta(rootName);
        if ((existingMeta?.WalkVersion ?? 0) < CurrentWalkVersion)
        {
            _store.DeleteCheckpoints(rootName);
            Log("walk upgraded — checkpoints invalidated for re-ingest");
        }

        var checkpoints = _store.LoadCheckpoints(rootName);
        var shardCache = new Dictionary<string, VaultSessionShard>(StringComparer.Ordinal);
        var dirtyMonths = new HashSet<string>(StringComparer.Ordinal);
        var nowIso = IsoUtc(nowUtc);

        // Oldest-first, single-threaded; Length/LastWriteTimeUtc come off the
        // FileInfo the walk already produced (no per-file stat pass) and are
        // the PRE-OPEN stat the checkpoint will record. Recursive since walk
        // v2: nested subagent transcripts count too, tagged with the project
        // dir so their parent session can be derived from the path.
        var files = new List<(FileInfo File, string ProjectDir)>();
        var projects = new DirectoryInfo(Path.Combine(_homeDir, rootName, "projects"));
        if (projects.Exists)
        {
            foreach (var projectDir in projects.EnumerateDirectories())
                foreach (var fi in projectDir.EnumerateFiles("*.jsonl", SearchOption.AllDirectories))
                    files.Add((fi, projectDir.FullName));
        }
        files.Sort((a, b) => a.File.LastWriteTimeUtc.CompareTo(b.File.LastWriteTimeUtc));

        foreach (var (fi, projectDir) in files)
        {
            seen++;
            var key = VaultStore.PathKey(fi.FullName);
            long statLen = fi.Length;
            long statMtime = fi.LastWriteTimeUtc.Ticks;
            checkpoints.Entries.TryGetValue(key, out var cp);
            var uuid = Path.GetFileNameWithoutExtension(fi.Name);
            var parentSession = ParentSessionOf(fi.FullName, projectDir);

            bool unchanged = cp is not null && cp.MtimeTicks == statMtime && cp.Length == statLen;
            bool quiesced = nowUtc - fi.LastWriteTimeUtc >= QuiesceAfter;

            if (unchanged && (cp!.Sealed || !quiesced))
            {
                cp.LastSeenTs = nowIso;
                skipped++;
                continue;
            }

            // Live paths: (A) unchanged+quiesced+unsealed -> whole-file verify
            // parse, then seal; (B) grown + verified guard -> tail parse seeded
            // from the stored rows; (C) everything else -> full reparse.
            bool sealAfter = unchanged;
            bool wantTail = !unchanged && cp is not null && !cp.Sealed
                && statLen > cp.Length && cp.Offset > 0 && cp.TailGuard.Length > 0;

            SessionAgg? seed = null;
            if (wantTail)
            {
                bool corruptSeed = false;
                seed = SeedFromStored(rootName, uuid, cp!.Months, shardCache, nowUtc, ref corruptSeed);
                if (corruptSeed)
                    return new VaultIngestResult(true, seen, full, tailParsed, skipped, failed, 1);
                // Crash-vs-tail guard: a crash that landed the shard but not the
                // checkpoint leaves stored rows NEWER than cp — seeding a tail
                // from them would double-count. Fingerprint mismatch -> full
                // reparse (idempotent row replace), which converges. Cache
                // counters are part of the fingerprint because a cache-only
                // tail changes no token totals. (Residual: a tail that changed
                // ONLY skipped_lines slips through and briefly double-counts
                // that counter — cosmetic, heals at the quiesce-seal verify.)
                if (seed is not null
                    && (seed.ByDay.Values.Sum(d => d.Total) != cp.RowTotal
                        || seed.EventCount != cp.RowEvents
                        || seed.CacheRead != cp.RowCacheRead
                        || seed.CacheCreation != cp.RowCacheCreation))
                {
                    seed = null;
                }
            }

            SessionAgg agg;
            long endOffset;
            string tailGuard;
            bool usedTail = false;
            try
            {
                using var fs = new FileStream(
                    fi.FullName, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                if (seed is not null && fs.Length >= cp!.Offset
                    && GuardAt(fs, cp.Offset) == cp.TailGuard)
                {
                    agg = seed;
                    endOffset = ParseLines(fs, cp.Offset, agg);
                    usedTail = true;
                }
                else
                {
                    agg = new SessionAgg();
                    endOffset = ParseLines(fs, 0, agg);
                }
                tailGuard = GuardAt(fs, endOffset);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (cp is not null)
                    cp.LastSeenTs = nowIso;
                Log2("file-open", e);
                failed++;
                continue;
            }

            var newRows = BuildRows(uuid, agg, storeFullPaths, parentSession);
            var oldMonths = cp?.Months ?? new List<string>();
            var touched = new HashSet<string>(oldMonths, StringComparer.Ordinal);
            touched.UnionWith(newRows.Keys);

            bool corrupt = false;
            foreach (var month in touched)
            {
                var shard = GetShard(shardCache, rootName, month, nowUtc, ref corrupt);
                if (corrupt)
                    break;
                shard.Sessions.Remove(uuid);
                if (newRows.TryGetValue(month, out var row))
                    shard.Sessions[uuid] = row;
                dirtyMonths.Add(month);
            }
            if (corrupt)
                return new VaultIngestResult(true, seen, full, tailParsed, skipped, failed, 1);

            checkpoints.Entries[key] = new VaultCheckpointEntry
            {
                MtimeTicks = statMtime,
                Length = statLen,
                Offset = endOffset,
                TailGuard = tailGuard,
                RowTotal = newRows.Values.Sum(r => r.Total),
                RowEvents = newRows.Values.Sum(r => r.EventCount),
                RowCacheRead = newRows.Values.Sum(r => r.CacheTokens?.Read ?? 0),
                RowCacheCreation = newRows.Values.Sum(r => r.CacheTokens?.Creation ?? 0),
                Months = newRows.Keys.OrderBy(m => m, StringComparer.Ordinal).ToList(),
                Sealed = sealAfter,
                LastSeenTs = nowIso,
            };
            if (usedTail) tailParsed++; else full++;
        }

        // Prune bookkeeping for files missing from the walk for > 7 days —
        // bounds checkpoints.json at ~files-on-disk; a resurrected file simply
        // re-ingests (idempotent).
        var pruneBefore = nowUtc.AddDays(-CheckpointPruneDays);
        foreach (var (k, entry) in checkpoints.Entries.ToList())
        {
            // An unparseable LastSeenTs counts as stale — otherwise a malformed
            // stamp becomes immortal bookkeeping and defeats the size bound.
            if (ParseIso(entry.LastSeenTs) is not { } lastSeen || lastSeen < pruneBefore)
                checkpoints.Entries.Remove(k);
        }

        // Write-phase consent recheck (see IngestOnce's stillConsented doc):
        // consent revoked after this cycle snapshotted its roots means the
        // parse work above is discarded — a clean skip, not an abort.
        if (stillConsented is not null && !stillConsented(rootName))
        {
            Log("root writes skipped (consent revoked)");
            return new VaultIngestResult(true, seen, full, tailParsed, skipped, failed, 0);
        }

        // WRITE ORDER (binding): session shards -> rollups -> checkpoints -> meta.
        try
        {
            foreach (var month in dirtyMonths.OrderBy(m => m, StringComparer.Ordinal))
                _store.SaveSessionShard(rootName, month, shardCache[month]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Shard never landed: abort before rollups/checkpoints. Stale
            // checkpoints re-ingest next cycle; checkpoint-first would make
            // this crash permanent.
            Log2("shard-write", e);
            return new VaultIngestResult(true, seen, full, tailParsed, skipped, failed, 1);
        }

        var rollupMonths = new HashSet<string>(dirtyMonths, StringComparer.Ordinal);
        var currentMonth = LocalDate(nowUtc).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        if (_store.ListSessionShardMonths(rootName).Contains(currentMonth))
            rollupMonths.Add(currentMonth);   // always-rebuild subsumes the startup self-check
        foreach (var month in rollupMonths)
            RebuildRollups(rootName, month, shardCache, nowUtc);

        _store.SaveCheckpoints(rootName, checkpoints);

        UpdateMeta(rootName, nowUtc, nowIso);
        return new VaultIngestResult(true, seen, full, tailParsed, skipped, failed, 0);
    }

    private VaultSessionShard GetShard(
        Dictionary<string, VaultSessionShard> cache, string rootName, string month,
        DateTimeOffset nowUtc, ref bool corrupt)
    {
        if (cache.TryGetValue(month, out var cached))
            return cached;
        var result = _store.TryLoadSessionShard(rootName, month, out var shard);
        if (result == ShardLoadResult.Corrupt)
        {
            _store.QuarantineSessionShard(rootName, month, nowUtc);
            Log("session shard quarantined; checkpoints invalidated");
            corrupt = true;
            return shard;
        }
        shard.SchemaVersion = VaultSchema.CurrentSchemaVersion;
        shard.WriterVersion = _writerVersion;
        cache[month] = shard;
        return shard;
    }

    /// <summary>Reconstruct a parse aggregate from a session's stored rows
    /// (primary + slices) so a tail parse folds into a COPY of the stored row.
    /// Null when no rows exist (e.g. a previously zero-event session) — the
    /// caller falls back to a full reparse.</summary>
    private SessionAgg? SeedFromStored(
        string rootName, string uuid, List<string> months,
        Dictionary<string, VaultSessionShard> cache, DateTimeOffset nowUtc, ref bool corrupt)
    {
        var rows = new List<VaultSessionRow>();
        foreach (var month in months)
        {
            var shard = GetShard(cache, rootName, month, nowUtc, ref corrupt);
            if (corrupt)
                return null;
            if (shard.Sessions.TryGetValue(uuid, out var row))
                rows.Add(row);
        }
        if (rows.Count == 0)
            return null;

        var merged = VaultRowMath.Merge(rows);
        var agg = new SessionAgg
        {
            FirstTs = ParseIso(merged.FirstTs),
            LastTs = ParseIso(merged.LastTs),
            EventCount = merged.EventCount,
            SkippedLines = merged.SkippedLines,
            Cwd = merged.Cwd,
            CacheRead = merged.CacheTokens?.Read ?? 0,
            CacheCreation = merged.CacheTokens?.Creation ?? 0,
            SeedProjectKey = merged.ProjectKey,
            SeedProjectName = merged.ProjectName,
        };
        foreach (var (dayKey, bucket) in merged.ByDay)
        {
            var day = DateOnly.Parse(dayKey, CultureInfo.InvariantCulture);
            var d = new DayAgg { Total = bucket.Total };
            foreach (var (m, v) in bucket.ByModel)
                d.ByModel[m] = v;
            if (bucket.BySkill is not null)
                foreach (var (s, v) in bucket.BySkill)
                    d.BySkill[s] = v;
            agg.ByDay[day] = d;
        }
        return agg;
    }

    private void RebuildRollups(
        string rootName, string month, Dictionary<string, VaultSessionShard> shardCache, DateTimeOffset nowUtc)
    {
        VaultSessionShard shard;
        if (shardCache.TryGetValue(month, out var cached))
        {
            shard = cached;
        }
        else if (_store.TryLoadSessionShard(rootName, month, out var loaded) != ShardLoadResult.Ok)
        {
            _store.DeleteRollupShard(rootName, month);
            return;
        }
        else
        {
            shard = loaded;
        }

        var days = new Dictionary<string, VaultRollupDay>(StringComparer.Ordinal);
        foreach (var row in shard.Sessions.Values)
        {
            foreach (var (day, bucket) in row.ByDay)
            {
                if (!days.TryGetValue(day, out var d))
                    days[day] = d = new VaultRollupDay();
                d.Total += bucket.Total;
                d.Sessions++;
                foreach (var (m, v) in bucket.ByModel)
                    d.ByModel[m] = d.ByModel.GetValueOrDefault(m) + v;
                d.ByProject[row.ProjectKey] = d.ByProject.GetValueOrDefault(row.ProjectKey) + bucket.Total;
                if (bucket.BySkill is not null)
                    foreach (var (s, v) in bucket.BySkill)
                        d.BySkill[s] = d.BySkill.GetValueOrDefault(s) + v;
            }
        }
        _store.SaveRollupShard(rootName, month, new VaultRollupShard
        {
            SchemaVersion = VaultSchema.CurrentSchemaVersion,
            Days = days,
        });
    }

    private void UpdateMeta(string rootName, DateTimeOffset nowUtc, string nowIso)
    {
        var meta = _store.LoadMeta(rootName) ?? new VaultRootMeta();
        var today = LocalDate(nowUtc);
        if (string.IsNullOrEmpty(meta.Since))
            meta.Since = DayKey(today);
        MergeCoverage(meta.Covered, today.AddDays(-CoverageMarginDays), today);
        meta.LastIngestTs = nowIso;
        meta.WalkVersion = CurrentWalkVersion;
        _store.SaveMeta(rootName, meta);
    }

    /// <summary>Merge [from, to] into the sorted disjoint range list.</summary>
    internal static void MergeCoverage(List<VaultDateRange> ranges, DateOnly from, DateOnly to)
    {
        var parsed = ranges
            .Select(r => (From: DateOnly.Parse(r.From, CultureInfo.InvariantCulture),
                          To: DateOnly.Parse(r.To, CultureInfo.InvariantCulture)))
            .Append((From: from, To: to))
            .OrderBy(r => r.From)
            .ToList();
        var merged = new List<(DateOnly From, DateOnly To)>();
        foreach (var r in parsed)
        {
            if (merged.Count > 0 && r.From <= merged[^1].To.AddDays(1))
                merged[^1] = (merged[^1].From, r.To > merged[^1].To ? r.To : merged[^1].To);
            else
                merged.Add(r);
        }
        ranges.Clear();
        ranges.AddRange(merged.Select(r => new VaultDateRange { From = DayKey(r.From), To = DayKey(r.To) }));
    }

    // -- parsing ------------------------------------------------------------------

    private sealed class DayAgg
    {
        public long Total;
        public readonly Dictionary<string, long> ByModel = new();
        public readonly Dictionary<string, long> BySkill = new();
    }

    private sealed class SessionAgg
    {
        public DateTimeOffset? FirstTs;
        public DateTimeOffset? LastTs;
        public long EventCount;
        public long SkippedLines;
        public string? Cwd;
        public long CacheRead;
        public long CacheCreation;
        public string? SeedProjectKey;
        public string? SeedProjectName;
        public readonly Dictionary<DateOnly, DayAgg> ByDay = new();
    }

    /// <summary>Parse complete lines from <paramref name="start"/>; returns the
    /// byte offset AFTER the last consumed '\n'. A torn trailing line (no
    /// terminator) is NOT consumed — the next cycle re-reads it, so a line that
    /// completes after this checkpoint converges instead of being lost.</summary>
    private long ParseLines(FileStream fs, long start, SessionAgg agg)
    {
        fs.Seek(start, SeekOrigin.Begin);
        var buf = new byte[64 * 1024];
        using var pending = new MemoryStream();
        long pos = start;       // absolute offset of buf[0]
        long consumed = start;  // absolute offset after the last '\n' processed
        int read;
        while ((read = fs.Read(buf, 0, buf.Length)) > 0)
        {
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
                ProcessLine(line, agg);
                consumed = pos + i + 1;
                lineStart = i + 1;
            }
            if (lineStart < read)
                pending.Write(buf, lineStart, read - lineStart);
            pos += read;
        }
        return consumed;
    }

    private void ProcessLine(string rawLine, SessionAgg agg)
    {
        var line = rawLine.Trim();
        if (line.Length == 0)
            return;
        // Perf prefilter only — looser than "type":"assistant" on purpose so
        // formatting drift can never silently drop events. False positives are
        // parsed and rejected below.
        if (!line.Contains("\"assistant\"", StringComparison.Ordinal))
            return;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            agg.SkippedLines++;   // counts assistant-looking lines that fail to parse
            return;
        }
        if (node is not JsonObject d)
        {
            agg.SkippedLines++;
            return;
        }
        if (Str(d, "type") != "assistant")
            return;
        if (d["message"] is not JsonObject msg || msg["usage"] is not JsonObject usage)
            return;
        var ts = ParseIso(Str(d, "timestamp"));
        if (ts is null)
            return;   // matches the live reader: untimestamped events don't count

        agg.CacheRead += Num(usage, "cache_read_input_tokens");
        agg.CacheCreation += Num(usage, "cache_creation_input_tokens");

        long tokens = Num(usage, "input_tokens") + Num(usage, "output_tokens");
        if (tokens <= 0)
            return;   // matches AggregateForLocalCcTab's tokens<=0 skip

        agg.Cwd ??= Str(d, "cwd");
        var model = Str(msg, "model");
        model = string.IsNullOrEmpty(model) ? "<none>" : model;   // conservation: sum(by_model) == total
        var skill = Str(d, "attributionSkill");

        if (agg.FirstTs is null || ts < agg.FirstTs) agg.FirstTs = ts;
        if (agg.LastTs is null || ts > agg.LastTs) agg.LastTs = ts;
        agg.EventCount++;

        var day = LocalDate(ts.Value);
        if (!agg.ByDay.TryGetValue(day, out var bucket))
            agg.ByDay[day] = bucket = new DayAgg();
        bucket.Total += tokens;
        bucket.ByModel[model] = bucket.ByModel.GetValueOrDefault(model) + tokens;
        if (!string.IsNullOrEmpty(skill))
            bucket.BySkill[skill] = bucket.BySkill.GetValueOrDefault(skill) + tokens;
    }

    /// <summary>parent_session per the WS-C.1 rule: for a NESTED file
    /// ({projectDir}\{seg}\...\x.jsonl) whose first segment parses as a Guid,
    /// that segment; otherwise null. Main transcripts sit directly in the
    /// project dir and never have a parent.</summary>
    internal static string? ParentSessionOf(string fullPath, string projectDir)
    {
        var rel = Path.GetRelativePath(projectDir, fullPath);
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length > 1 && Guid.TryParse(parts[0], out _) ? parts[0] : null;
    }

    /// <summary>Per-month rows for a parsed session; empty when it had no
    /// counted events (zero-assistant-event sessions get no row).</summary>
    private Dictionary<string, VaultSessionRow> BuildRows(
        string uuid, SessionAgg agg, bool storeFullPaths, string? parentSession)
    {
        _ = uuid;
        if (agg.EventCount == 0 || agg.FirstTs is null || agg.LastTs is null)
            return new Dictionary<string, VaultSessionRow>();

        string projectKey, projectName;
        if (agg.Cwd is not null)
        {
            projectName = CcLogReader.ProjectDisplayName(agg.Cwd);
            projectKey = $"{projectName}~{VaultStore.PathKey(agg.Cwd)[..8]}";
        }
        else if (agg.SeedProjectKey is not null)
        {
            projectKey = agg.SeedProjectKey;
            projectName = agg.SeedProjectName ?? "(none)";
        }
        else
        {
            projectKey = "(none)";
            projectName = "(none)";
        }
        var merged = new VaultSessionRow
        {
            ProjectKey = projectKey,
            ProjectName = projectName,
            Cwd = storeFullPaths ? agg.Cwd : null,
            ParentSession = parentSession,
            FirstTs = IsoUtc(agg.FirstTs.Value),
            LastTs = IsoUtc(agg.LastTs.Value),
            UtcOffsetMin = (int)_tz.GetUtcOffset(agg.FirstTs.Value).TotalMinutes,
            EventCount = agg.EventCount,
            SkippedLines = agg.SkippedLines,
            CacheTokens = new VaultCacheTokens { Read = agg.CacheRead, Creation = agg.CacheCreation },
            ByDay = agg.ByDay.ToDictionary(
                kv => DayKey(kv.Key),
                kv => new VaultDayBucket
                {
                    Total = kv.Value.Total,
                    ByModel = new Dictionary<string, long>(kv.Value.ByModel),
                    BySkill = kv.Value.BySkill.Count > 0
                        ? new Dictionary<string, long>(kv.Value.BySkill)
                        : null,
                }),
        };
        return VaultRowMath.SplitByMonth(merged);
    }

    /// <summary>SHA-256 hex of the up-to-64 bytes ENDING at <paramref name="offset"/> —
    /// the tail guard Task 3's grown-file branch verifies before trusting a
    /// stored offset.</summary>
    internal static string GuardAt(FileStream fs, long offset)
    {
        int len = (int)Math.Min(TailGuardBytes, offset);
        if (len <= 0)
            return "";
        var buf = new byte[len];
        fs.Seek(offset - len, SeekOrigin.Begin);
        fs.ReadExactly(buf, 0, len);
        return Convert.ToHexString(SHA256.HashData(buf)).ToLowerInvariant();
    }

    // -- helpers ------------------------------------------------------------------

    private DateOnly LocalDate(DateTimeOffset ts)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ts, _tz).DateTime);

    private static string DayKey(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string IsoUtc(DateTimeOffset ts)
        => ts.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseIso(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return null;
        return DateTimeOffset.TryParse(
            s.Replace("Z", "+00:00"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var dt) ? dt : null;
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

    // PRIVACY.md contract: fixed phrases + exception type names only.
    private void Log(string message)
    {
        if (_logFile is null)
            return;
        try
        {
            File.AppendAllText(_logFile, $"{DateTime.UtcNow:o} vault {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never break ingestion.
        }
    }

    private void Log2(string operation, Exception e)
        => Log($"{operation} failed ({e.GetType().Name})");
}
