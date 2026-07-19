using System.Globalization;
using System.Text;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// VaultIngester core battery (spec: Ingestion + Testing). A fake CC home is
/// built in a temp dir: {home}\{root}\projects\{projDir}\{uuid}.jsonl. All
/// tests inject a unique mutex name and a FIXED time zone (Central Standard
/// Time) except the reader-parity test, which must share the machine-local
/// zone with CcLogReader.
/// </summary>
public class VaultIngesterTests
{
    private static readonly TimeZoneInfo Cst =
        TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-12T18:00:00+00:00");

    private static string MutexName() => "SanduhrTest.Vault." + Guid.NewGuid().ToString("N");

    private static string EventLine(
        string ts, string model = "claude-fable-5", long input = 100, long output = 50,
        string? cwd = @"C:\Users\x\Projects\api", string? skill = null,
        long cacheRead = 0, long cacheCreation = 0)
    {
        var sb = new StringBuilder();
        sb.Append("{\"type\":\"assistant\",\"timestamp\":\"").Append(ts).Append('"');
        if (cwd is not null)
            sb.Append(",\"cwd\":").Append(System.Text.Json.JsonSerializer.Serialize(cwd));
        if (skill is not null)
            sb.Append(",\"attributionSkill\":\"").Append(skill).Append('"');
        sb.Append(",\"message\":{\"model\":\"").Append(model)
          .Append("\",\"usage\":{\"input_tokens\":").Append(input)
          .Append(",\"output_tokens\":").Append(output);
        if (cacheRead > 0) sb.Append(",\"cache_read_input_tokens\":").Append(cacheRead);
        if (cacheCreation > 0) sb.Append(",\"cache_creation_input_tokens\":").Append(cacheCreation);
        sb.Append("}}}");
        return sb.ToString();
    }

    /// <summary>Writes a fixture session and PINS its mtime relative to the
    /// fixed test clock (default Now-10min) — real-clock mtimes would make the
    /// Task 3 quiesce/seal branch engage nondeterministically.</summary>
    private static string WriteSession(
        string home, string root, string uuid, DateTimeOffset? mtimeUtc = null, params string[] lines)
    {
        var dir = Path.Combine(home, root, "projects", "c--Users-x-Projects-api");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, uuid + ".jsonl");
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        File.SetLastWriteTimeUtc(path, (mtimeUtc ?? Now.AddMinutes(-10)).UtcDateTime);
        return path;
    }

    private static string WriteSession(string home, string root, string uuid, params string[] lines)
        => WriteSession(home, root, uuid, mtimeUtc: null, lines);

    private static (VaultIngester Ingester, VaultStore Store) Make(
        string home, string vaultDir, TimeZoneInfo? tz = null, string? logFile = null, string? mutexName = null)
    {
        var store = new VaultStore(vaultDir, logFile);
        var ing = new VaultIngester(home, store, "test", logFile, tz ?? Cst, mutexName ?? MutexName());
        return (ing, store);
    }

    [Fact]
    public void Happy_path_writes_raw_models_cache_and_unconditional_totals()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1",
            EventLine("2026-07-10T15:00:00Z", model: "claude-fable-5", input: 1000, output: 100,
                skill: "code-review", cacheRead: 5000, cacheCreation: 200),
            EventLine("2026-07-10T16:00:00Z", model: "<synthetic>", input: 0, output: 4),
            EventLine("2026-07-10T17:00:00Z", model: "claude-sonnet-5", input: 300, output: 30));

        var (ing, store) = Make(home.Path, vault.Path);
        var result = ing.IngestOnce(new[] { ".claude" }, storeFullPaths: false, Now);

        Assert.True(result.Acquired);
        Assert.Equal(1, result.FilesFullParsed);
        Assert.Equal(ShardLoadResult.Ok, store.TryLoadSessionShard(".claude", "2026-07", out var shard));
        var row = shard.Sessions["u1"];
        Assert.Equal(1434, row.Total);                       // 1100 + 4 + 330 — every timestamped event
        Assert.Equal(1100, row.ByModel["claude-fable-5"]);   // raw strings, never tiers
        Assert.Equal(4, row.ByModel["<synthetic>"]);
        Assert.Equal(330, row.ByModel["claude-sonnet-5"]);
        Assert.Equal(row.Total, row.ByModel.Values.Sum());
        Assert.Equal(5000, row.CacheTokens!.Read);
        Assert.Equal(200, row.CacheTokens.Creation);
        Assert.Equal(1100, row.BySkill!["code-review"]);
        Assert.Equal(3, row.EventCount);
        Assert.False(row.Continuation);
        Assert.Equal("api", row.ProjectName);
        Assert.Matches("^api~[0-9a-f]{8}$", row.ProjectKey);
        Assert.Null(row.Cwd);                                // store_full_paths off
        Assert.Equal(row.Total, row.ByDay.Values.Sum(d => d.Total));  // row invariant
        Assert.Equal("2026-07-10", row.ByDay.Keys.Single()); // 15:00Z = 10:00 CDT
        Assert.Equal("test", shard.WriterVersion);
    }

    [Fact]
    public void Store_full_paths_on_persists_cwd()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1", EventLine("2026-07-10T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, storeFullPaths: true, Now);
        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        Assert.Equal(@"C:\Users\x\Projects\api", shard.Sessions["u1"].Cwd);
    }

    [Fact]
    public void Reingest_with_same_now_is_byte_identical()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1",
            EventLine("2026-07-10T15:00:00Z"), EventLine("2026-07-10T16:00:00Z"));
        var (ing, _) = Make(home.Path, vault.Path);

        ing.IngestOnce(new[] { ".claude" }, false, Now);
        var snapshot = Directory.GetFiles(vault.Path, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToDictionary(p => p, File.ReadAllBytes);

        ing.IngestOnce(new[] { ".claude" }, false, Now);
        var after = Directory.GetFiles(vault.Path, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToDictionary(p => p, File.ReadAllBytes);

        Assert.Equal(snapshot.Keys, after.Keys);
        foreach (var (path, bytes) in snapshot)
            Assert.Equal(bytes, after[path]);
    }

    [Fact]
    public void Vault_day_total_matches_live_reader_even_for_unmapped_models()
    {
        // THE fable-5 invariant. Shares the machine-local zone with CcLogReader.
        using var home = new TempDir();
        using var vault = new TempDir();
        // Yesterday, REAL clock — this test intentionally shares the machine
        // clock and zone with CcLogReader, so the file mtime must track the
        // real clock too (the reader prefilters on mtime >= UtcNow-30d; the
        // fixed test-clock default would rot into a time-bomb failure ~30
        // days after authoring).
        var ts = DateTimeOffset.UtcNow.AddDays(-1);
        var iso = ts.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        WriteSession(home.Path, ".claude", "u1",
            mtimeUtc: DateTimeOffset.UtcNow.AddMinutes(-10),
            EventLine(iso, model: "claude-fable-5", input: 700, output: 70),
            EventLine(iso, model: "claude-mystery-99", input: 20, output: 2));

        var (ing, store) = Make(home.Path, vault.Path, tz: TimeZoneInfo.Local);
        ing.IngestOnce(new[] { ".claude" }, false, DateTimeOffset.UtcNow);

        var reader = new CcLogReader(home.Path);
        var live = reader.AggregateForLocalCcTab(30);
        var day = DateOnly.FromDateTime(ts.ToLocalTime().DateTime);
        var dayKey = day.ToString("yyyy-MM-dd");

        store.TryLoadSessionShard(".claude", dayKey[..7], out var shard);
        long vaultDayTotal = shard.Sessions.Values
            .Where(r => r.ByDay.ContainsKey(dayKey))
            .Sum(r => r.ByDay[dayKey].Total);
        Assert.Equal(live.ByDay[day], vaultDayTotal);
        Assert.Equal(792, vaultDayTotal);
    }

    [Fact]
    public void Month_spanning_session_slices_with_continuation_rows()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1",
            EventLine("2026-07-31T20:00:00Z", input: 100, output: 0),   // Jul 31 15:00 CDT
            EventLine("2026-08-01T20:00:00Z", input: 200, output: 0));  // Aug 1 15:00 CDT
        var (ing, store) = Make(home.Path, vault.Path, tz: Cst);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        store.TryLoadSessionShard(".claude", "2026-07", out var jul);
        store.TryLoadSessionShard(".claude", "2026-08", out var aug);
        var primary = jul.Sessions["u1"];
        var slice = aug.Sessions["u1"];

        Assert.False(primary.Continuation);
        Assert.True(slice.Continuation);
        Assert.Equal(100, primary.Total);                    // row total == own buckets only
        Assert.Equal(200, slice.Total);
        Assert.Equal(new[] { "2026-07-31" }, primary.ByDay.Keys.ToArray());
        Assert.Equal(new[] { "2026-08-01" }, slice.ByDay.Keys.ToArray());
        Assert.NotNull(primary.CacheTokens);                 // primary carries cache + counters
        Assert.Null(slice.CacheTokens);                      // slices don't (not splittable by month)
        Assert.Equal(0, slice.EventCount);
        Assert.Equal(primary.FirstTs, slice.FirstTs);        // session identity duplicated for standalone reads
        Assert.Equal(primary.ProjectKey, slice.ProjectKey);
    }

    [Fact]
    public void Utc_month_boundary_places_by_local_time()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        // 2026-08-01T02:00Z = 2026-07-31 21:00 CDT — belongs to July.
        WriteSession(home.Path, ".claude", "u1", EventLine("2026-08-01T02:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path, tz: Cst);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        Assert.Equal(ShardLoadResult.Ok, store.TryLoadSessionShard(".claude", "2026-07", out var jul));
        Assert.True(jul.Sessions.ContainsKey("u1"));
        Assert.Equal(ShardLoadResult.Missing, store.TryLoadSessionShard(".claude", "2026-08", out _));
    }

    [Fact]
    public void Dst_fall_back_25_hour_day_buckets_by_local_date()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        // US DST ends 2026-11-01. 06:30Z = 01:30 CDT; 23:30 CST is 2026-11-02T05:30Z.
        WriteSession(home.Path, ".claude", "u1",
            EventLine("2026-11-01T06:30:00Z", input: 10, output: 0),
            EventLine("2026-11-02T05:30:00Z", input: 20, output: 0));
        var (ing, store) = Make(home.Path, vault.Path, tz: Cst);
        ing.IngestOnce(new[] { ".claude" }, false, DateTimeOffset.Parse("2026-11-03T00:00:00+00:00"));

        store.TryLoadSessionShard(".claude", "2026-11", out var shard);
        var row = shard.Sessions["u1"];
        Assert.Equal(30, row.ByDay["2026-11-01"].Total);     // both land on the 25-hour local day
        Assert.False(row.ByDay.ContainsKey("2026-11-02"));
    }

    [Fact]
    public void Second_writer_holding_the_mutex_skips_cleanly()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1", EventLine("2026-07-10T15:00:00Z"));
        var name = MutexName();

        // Named mutexes are REENTRANT per thread — holding it on the test
        // thread would let the ingester's WaitOne(0) succeed recursively and
        // the test would fail against a correct implementation. Hold it on a
        // dedicated thread instead.
        using var acquired = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var holderThread = new Thread(() =>
        {
            using var holder = new Mutex(initiallyOwned: true, name);
            acquired.Set();
            release.Wait();
            holder.ReleaseMutex();
        });
        holderThread.Start();
        acquired.Wait();
        try
        {
            var (ing, store) = Make(home.Path, vault.Path, mutexName: name);
            var result = ing.IngestOnce(new[] { ".claude" }, false, Now);
            Assert.False(result.Acquired);
            Assert.False(Directory.Exists(Path.Combine(vault.Path, ".claude")));
        }
        finally
        {
            release.Set();
            holderThread.Join();
        }
    }

    [Fact]
    public void Consent_filter_never_touches_unconsented_roots()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1", EventLine("2026-07-10T15:00:00Z"));
        WriteSession(home.Path, ".claude-personal", "u2", EventLine("2026-07-10T15:00:00Z"));
        var (ing, _) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude-personal" }, false, Now);

        Assert.False(Directory.Exists(Path.Combine(vault.Path, ".claude")));
        Assert.True(File.Exists(Path.Combine(vault.Path, ".claude-personal", "sessions-2026-07.json")));
    }

    [Fact]
    public void Zero_assistant_event_session_writes_no_row_but_checkpoints()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1", "{\"type\":\"user\",\"text\":\"hi\"}");
        var (ing, store) = Make(home.Path, vault.Path);
        var result = ing.IngestOnce(new[] { ".claude" }, false, Now);

        Assert.Equal(ShardLoadResult.Missing, store.TryLoadSessionShard(".claude", "2026-07", out _));
        Assert.Single(store.LoadCheckpoints(".claude").Entries);
        Assert.Equal(1, result.FilesFullParsed);
    }

    [Fact]
    public void Malformed_assistant_lines_counted_never_fatal()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1",
            EventLine("2026-07-10T15:00:00Z"),
            "{\"type\":\"assistant\", TRUNCATED GARBAGE",
            EventLine("2026-07-10T16:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        var row = shard.Sessions["u1"];
        Assert.Equal(2, row.EventCount);
        Assert.Equal(1, row.SkippedLines);
        Assert.Equal(300, row.Total);
    }

    [Fact]
    public void Rollups_fold_per_day_project_skill_and_sessions_count()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1",
            EventLine("2026-07-10T15:00:00Z", input: 100, output: 0, skill: "code-review"),
            EventLine("2026-07-11T15:00:00Z", input: 200, output: 0));
        WriteSession(home.Path, ".claude", "u2",
            EventLine("2026-07-10T16:00:00Z", input: 40, output: 0));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        Assert.Equal(ShardLoadResult.Ok, store.TryLoadRollupShard(".claude", "2026-07", out var roll));
        var d10 = roll.Days["2026-07-10"];
        Assert.Equal(140, d10.Total);
        Assert.Equal(2, d10.Sessions);
        Assert.Equal(100, d10.BySkill["code-review"]);
        Assert.Equal(140, d10.ByProject.Values.Sum());
        Assert.Single(d10.ByProject);                        // both sessions share one project_key
        var d11 = roll.Days["2026-07-11"];
        Assert.Equal(200, d11.Total);
        Assert.Equal(1, d11.Sessions);
    }

    [Fact]
    public void Crash_between_shard_and_checkpoint_converges_without_double_count()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        var path = WriteSession(home.Path, ".claude", "u1", EventLine("2026-07-10T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        // Simulate: file grows, next cycle writes the shard but CRASHES before
        // the checkpoint lands — restore the stale checkpoint file afterwards.
        var cpPath = Path.Combine(vault.Path, ".claude", "checkpoints.json");
        var staleCheckpoint = File.ReadAllBytes(cpPath);
        File.AppendAllText(path, EventLine("2026-07-10T16:00:00Z") + "\n");
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-5).UtcDateTime);   // pinned, not quiesced
        ing.IngestOnce(new[] { ".claude" }, false, Now);
        File.WriteAllBytes(cpPath, staleCheckpoint);         // the "crash"

        ing.IngestOnce(new[] { ".claude" }, false, Now);     // recovery cycle

        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        Assert.Equal(300, shard.Sessions["u1"].Total);       // 2 events, no double count
        Assert.Equal(2, shard.Sessions["u1"].EventCount);
        store.TryLoadRollupShard(".claude", "2026-07", out var roll);
        Assert.Equal(300, roll.Days["2026-07-10"].Total);    // fold==rollups after recovery
    }

    [Fact]
    public void Meta_records_since_coverage_and_last_ingest()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1", EventLine("2026-07-10T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        var meta = store.LoadMeta(".claude");
        Assert.NotNull(meta);
        Assert.Equal("2026-07-12", meta!.Since);             // local date of Now (13:00 CDT)
        var range = meta.Covered.Single();
        Assert.Equal("2026-06-17", range.From);              // today-25
        Assert.Equal("2026-07-12", range.To);
        Assert.Equal(
            Now.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture),
            meta.LastIngestTs);

        // A later cycle extends the range instead of stacking a duplicate.
        var later = Now.AddDays(3);
        ing.IngestOnce(new[] { ".claude" }, false, later);
        meta = store.LoadMeta(".claude");
        Assert.Equal("2026-07-12", meta!.Since);             // birth date never moves
        Assert.Equal("2026-07-15", meta.Covered.Single().To);
        Assert.Equal("2026-06-17", meta.Covered.Single().From);
    }

    [Fact]
    public void RowMath_split_then_merge_round_trips()
    {
        var merged = new VaultSessionRow
        {
            ProjectKey = "api~12345678",
            ProjectName = "api",
            FirstTs = "2026-07-31T20:00:00+00:00",
            LastTs = "2026-08-02T10:00:00+00:00",
            UtcOffsetMin = -300,
            EventCount = 3,
            SkippedLines = 1,
            Total = 600,
            ByModel = new() { ["claude-fable-5"] = 600 },
            CacheTokens = new VaultCacheTokens { Read = 42, Creation = 7 },
            BySkill = new() { ["code-review"] = 100 },
            ByDay = new()
            {
                ["2026-07-31"] = new VaultDayBucket
                {
                    Total = 100,
                    ByModel = new() { ["claude-fable-5"] = 100 },
                    BySkill = new() { ["code-review"] = 100 },
                },
                ["2026-08-01"] = new VaultDayBucket { Total = 200, ByModel = new() { ["claude-fable-5"] = 200 } },
                ["2026-08-02"] = new VaultDayBucket { Total = 300, ByModel = new() { ["claude-fable-5"] = 300 } },
            },
        };

        var byMonth = VaultRowMath.SplitByMonth(merged);
        Assert.Equal(2, byMonth.Count);
        Assert.False(byMonth["2026-07"].Continuation);
        Assert.True(byMonth["2026-08"].Continuation);
        Assert.Equal(100, byMonth["2026-07"].Total);
        Assert.Equal(500, byMonth["2026-08"].Total);
        Assert.Null(byMonth["2026-08"].CacheTokens);

        var roundTrip = VaultRowMath.Merge(byMonth.Values.ToList());
        Assert.Equal(600, roundTrip.Total);
        Assert.Equal(3, roundTrip.EventCount);
        Assert.Equal(42, roundTrip.CacheTokens!.Read);
        Assert.Equal(600, roundTrip.ByModel["claude-fable-5"]);
        Assert.Equal(100, roundTrip.BySkill!["code-review"]);
        Assert.Equal(3, roundTrip.ByDay.Count);
        Assert.Equal("2026-07-31T20:00:00+00:00", roundTrip.FirstTs);
    }

    [Fact]
    public void TierForModel_is_static_and_matches_instance_mapping()
    {
        Assert.Equal("seven_day_opus", CcLogReader.TierForModel("claude-opus-4-8"));
        Assert.Equal("seven_day_sonnet", CcLogReader.TierForModel("claude-sonnet-5"));
        Assert.Equal("seven_day", CcLogReader.TierForModel("claude-haiku-4-5-20251001"));
        Assert.Equal(TierModel.SevenDayFable, CcLogReader.TierForModel("claude-fable-5"));
        Assert.Null(CcLogReader.TierForModel("<synthetic>"));
        Assert.Null(CcLogReader.TierForModel(null));
        Assert.Equal(CcLogReader.TierForModel("claude-sonnet-5"), new CcLogReader().ModelToTierKey("claude-sonnet-5"));
    }
}
