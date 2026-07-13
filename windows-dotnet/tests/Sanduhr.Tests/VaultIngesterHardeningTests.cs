using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// The adversarial ingestion battery (spec: Testing). Uses the same fixture
/// helpers as VaultIngesterTests — keep the two files' helpers identical; they
/// are duplicated so each file reads standalone.
/// </summary>
public class VaultIngesterHardeningTests
{
    private static readonly TimeZoneInfo Cst =
        TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-12T18:00:00+00:00");

    private static string MutexName() => "SanduhrTest.Vault." + Guid.NewGuid().ToString("N");

    private static string EventLine(string ts, string model = "claude-fable-5",
        long input = 100, long output = 50, string? cwd = @"C:\Users\x\Projects\api")
    {
        var cwdJson = cwd is null ? "" : ",\"cwd\":" + System.Text.Json.JsonSerializer.Serialize(cwd);
        return "{\"type\":\"assistant\",\"timestamp\":\"" + ts + "\"" + cwdJson +
               ",\"message\":{\"model\":\"" + model + "\",\"usage\":{\"input_tokens\":" + input +
               ",\"output_tokens\":" + output + "}}}";
    }

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

    private static (VaultIngester Ingester, VaultStore Store) Make(
        string home, string vaultDir, string? logFile = null)
    {
        var store = new VaultStore(vaultDir, logFile);
        var ing = new VaultIngester(home, store, "test", logFile, Cst, MutexName());
        return (ing, store);
    }

    private static void Append(string path, DateTimeOffset mtimeUtc, params string[] lines)
    {
        File.AppendAllText(path, string.Join("\n", lines) + "\n");
        File.SetLastWriteTimeUtc(path, mtimeUtc.UtcDateTime);
    }

    private static long RowTotal(VaultStore store, string month = "2026-07", string uuid = "u1")
    {
        store.TryLoadSessionShard(".claude", month, out var shard);
        return shard.Sessions[uuid].Total;
    }

    /// <summary>Spec invariant: fold(session shard) == stored rollup shard,
    /// after EVERY ingest — including tail parses, month-moves, and
    /// crash/quarantine recovery. Called at the end of the recovery tests.</summary>
    private static void AssertFoldMatchesRollups(VaultStore store, string root, string month)
    {
        Assert.Equal(ShardLoadResult.Ok, store.TryLoadSessionShard(root, month, out var sessions));
        Assert.Equal(ShardLoadResult.Ok, store.TryLoadRollupShard(root, month, out var rollups));
        var expected = new Dictionary<string, long>();
        foreach (var row in sessions.Sessions.Values)
            foreach (var (day, bucket) in row.ByDay)
                expected[day] = expected.GetValueOrDefault(day) + bucket.Total;
        Assert.Equal(expected.Count, rollups.Days.Count);
        foreach (var (day, total) in expected)
            Assert.Equal(total, rollups.Days[day].Total);
    }

    [Fact]
    public void Growing_file_tail_parses_and_converges_with_full_parse()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        var path = WriteSession(home.Path, ".claude", "u1", null,
            EventLine("2026-07-10T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        Append(path, Now.AddMinutes(-5), EventLine("2026-07-10T16:00:00Z", input: 200, output: 0));
        var r2 = ing.IngestOnce(new[] { ".claude" }, false, Now);

        Assert.Equal(1, r2.FilesTailParsed);
        Assert.Equal(0, r2.FilesFullParsed);
        Assert.Equal(350, RowTotal(store));                  // 150 + 200, no double count
        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        Assert.Equal(2, shard.Sessions["u1"].EventCount);

        // Convergence: a from-scratch vault over the same file is identical.
        using var vault2 = new TempDir();
        var (ing2, store2) = Make(home.Path, vault2.Path);
        ing2.IngestOnce(new[] { ".claude" }, false, Now);
        store2.TryLoadSessionShard(".claude", "2026-07", out var fresh);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(fresh.Sessions["u1"]),
            System.Text.Json.JsonSerializer.Serialize(shard.Sessions["u1"]));
        AssertFoldMatchesRollups(store, ".claude", "2026-07");
    }

    [Fact]
    public void Tail_parse_preserves_project_identity_without_stored_cwd()
    {
        // store_full_paths is OFF: the stored row carries project_key but a null
        // cwd. A tail whose events carry no cwd must NOT degrade the project.
        using var home = new TempDir();
        using var vault = new TempDir();
        var path = WriteSession(home.Path, ".claude", "u1", null,
            EventLine("2026-07-10T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);
        store.TryLoadSessionShard(".claude", "2026-07", out var before);
        var keyBefore = before.Sessions["u1"].ProjectKey;

        Append(path, Now.AddMinutes(-5), EventLine("2026-07-10T16:00:00Z", cwd: null));
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        store.TryLoadSessionShard(".claude", "2026-07", out var after);
        Assert.Equal(keyBefore, after.Sessions["u1"].ProjectKey);
        Assert.Equal("api", after.Sessions["u1"].ProjectName);
    }

    [Fact]
    public void Torn_last_line_completing_after_checkpoint_converges()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        var full = EventLine("2026-07-10T16:00:00Z", input: 200, output: 0);
        var dir = Path.Combine(home.Path, ".claude", "projects", "c--Users-x-Projects-api");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "u1.jsonl");
        // Complete first line, then HALF of the second with no terminator.
        File.WriteAllText(path, EventLine("2026-07-10T15:00:00Z") + "\n" + full[..40]);
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-10).UtcDateTime);

        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);
        Assert.Equal(150, RowTotal(store));                  // torn line not consumed

        // The writer finishes the line.
        File.AppendAllText(path, full[40..] + "\n");
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-5).UtcDateTime);
        var r2 = ing.IngestOnce(new[] { ".claude" }, false, Now);

        Assert.Equal(1, r2.FilesTailParsed);                 // guard bytes precede the tear — still valid
        Assert.Equal(350, RowTotal(store));
        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        Assert.Equal(0, shard.Sessions["u1"].SkippedLines);  // the half-line was never "malformed"
    }

    [Fact]
    public void Guard_mismatch_forces_full_reparse()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        var path = WriteSession(home.Path, ".claude", "u1", null,
            EventLine("2026-07-10T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        // Same uuid, longer file, DIFFERENT bytes before the old offset —
        // (a restore-from-backup shape). Grown-gate passes, guard must not.
        File.WriteAllText(path,
            EventLine("2026-07-10T14:00:00Z", input: 999, output: 0) + "\n" +
            EventLine("2026-07-10T15:30:00Z", input: 1, output: 0) + "\n" +
            EventLine("2026-07-10T16:00:00Z", input: 2, output: 0) + "\n");
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-4).UtcDateTime);
        var r2 = ing.IngestOnce(new[] { ".claude" }, false, Now);

        Assert.Equal(1, r2.FilesFullParsed);
        Assert.Equal(0, r2.FilesTailParsed);
        Assert.Equal(1002, RowTotal(store));                 // truth, not stale + tail
    }

    [Fact]
    public void Shrunk_file_forces_full_reparse()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        var path = WriteSession(home.Path, ".claude", "u1", null,
            EventLine("2026-07-10T15:00:00Z"), EventLine("2026-07-10T16:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);
        Assert.Equal(300, RowTotal(store));

        WriteSession(home.Path, ".claude", "u1", Now.AddMinutes(-3),
            EventLine("2026-07-10T15:00:00Z", input: 10, output: 0));
        ing.IngestOnce(new[] { ".claude" }, false, Now);
        Assert.Equal(10, RowTotal(store));
    }

    [Fact]
    public void Quiesced_file_verify_parses_then_seals_then_skips()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        var path = WriteSession(home.Path, ".claude", "u1", Now.AddHours(-2),
            EventLine("2026-07-10T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);

        var r1 = ing.IngestOnce(new[] { ".claude" }, false, Now);   // backfill: full parse
        Assert.Equal(1, r1.FilesFullParsed);
        var cp1 = store.LoadCheckpoints(".claude").Entries.Values.Single();
        Assert.False(cp1.Sealed);

        var r2 = ing.IngestOnce(new[] { ".claude" }, false, Now);   // unchanged + quiesced: verify + seal
        Assert.Equal(1, r2.FilesFullParsed);
        Assert.True(store.LoadCheckpoints(".claude").Entries.Values.Single().Sealed);
        Assert.Equal(150, RowTotal(store));

        var r3 = ing.IngestOnce(new[] { ".claude" }, false, Now);   // sealed: skipped entirely
        Assert.Equal(1, r3.FilesSkipped);
        Assert.Equal(0, r3.FilesFullParsed + r3.FilesTailParsed);

        // A sealed file that changes unseals via full reparse.
        Append(path, Now.AddHours(-1).AddMinutes(30), EventLine("2026-07-10T16:00:00Z", input: 7, output: 0));
        var r4 = ing.IngestOnce(new[] { ".claude" }, false, Now);
        Assert.Equal(1, r4.FilesFullParsed);
        Assert.Equal(157, RowTotal(store));
        Assert.False(store.LoadCheckpoints(".claude").Entries.Values.Single().Sealed);
    }

    [Fact]
    public void Locked_file_advances_nothing_and_recovers_next_cycle()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        using var log = new TempDir();
        var logFile = Path.Combine(log.Path, "sanduhr.log");
        var path = WriteSession(home.Path, ".claude", "u1", null,
            EventLine("2026-07-10T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path, logFile);

        VaultIngestResult r1;
        using (var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            r1 = ing.IngestOnce(new[] { ".claude" }, false, Now);
        }
        Assert.Equal(1, r1.FilesFailed);
        Assert.Equal(ShardLoadResult.Missing, store.TryLoadSessionShard(".claude", "2026-07", out _));
        Assert.Empty(store.LoadCheckpoints(".claude").Entries);   // no entry ever existed — nothing advanced

        var r2 = ing.IngestOnce(new[] { ".claude" }, false, Now);
        Assert.Equal(1, r2.FilesFullParsed);
        Assert.Equal(150, RowTotal(store));
    }

    [Fact]
    public void Failed_ingest_log_contains_no_path_separators()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        using var log = new TempDir();
        var logFile = Path.Combine(log.Path, "sanduhr.log");
        var path = WriteSession(home.Path, ".claude", "u1", null,
            EventLine("2026-07-10T15:00:00Z"));
        var (ing, _) = Make(home.Path, vault.Path, logFile);

        using (var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            ing.IngestOnce(new[] { ".claude" }, false, Now);
        }

        // PRIVACY.md: operation + exception TYPE only — a path (or e.Message,
        // which embeds one) would carry a separator.
        var content = File.ReadAllText(logFile);
        Assert.NotEqual("", content);
        Assert.DoesNotContain("\\", content);
        Assert.DoesNotContain("/", content);
    }

    [Fact]
    public void Quarantined_shard_invalidates_checkpoints_and_full_reingest_converges()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1", null, EventLine("2026-07-10T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        // Corrupt the shard on disk behind the ingester's back.
        var shardPath = Path.Combine(vault.Path, ".claude", "sessions-2026-07.json");
        File.WriteAllText(shardPath, "{corrupt");
        // Touch the source file so the next cycle re-reads it (stat changed).
        WriteSession(home.Path, ".claude", "u1", Now.AddMinutes(-5),
            EventLine("2026-07-10T15:00:00Z"), EventLine("2026-07-10T16:00:00Z"));

        var r2 = ing.IngestOnce(new[] { ".claude" }, false, Now);   // hits corrupt -> quarantine + abort
        Assert.Equal(1, r2.RootsAborted);
        Assert.Single(Directory.GetFiles(Path.Combine(vault.Path, ".claude"), "*.bad"));
        Assert.False(File.Exists(Path.Combine(vault.Path, ".claude", "checkpoints.json")));

        var r3 = ing.IngestOnce(new[] { ".claude" }, false, Now);   // full re-ingest rebuilds
        Assert.Equal(0, r3.RootsAborted);
        Assert.Equal(300, RowTotal(store));
        Assert.Single(Directory.GetFiles(Path.Combine(vault.Path, ".claude"), "*.bad")); // .bad untouched
        AssertFoldMatchesRollups(store, ".claude", "2026-07");
    }

    [Fact]
    public void Checkpoint_prune_then_file_resurrection_is_harmless()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        var path = WriteSession(home.Path, ".claude", "u1", null,
            EventLine("2026-07-10T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        File.Delete(path);
        var later = Now.AddDays(8);                          // > 7-day prune horizon
        ing.IngestOnce(new[] { ".claude" }, false, later);

        Assert.Empty(store.LoadCheckpoints(".claude").Entries);       // bookkeeping pruned
        Assert.Equal(150, RowTotal(store));                           // the RECORD outlives its source

        // Resurrection (restored from backup): re-ingest is idempotent.
        WriteSession(home.Path, ".claude", "u1", later.AddMinutes(-10),
            EventLine("2026-07-10T15:00:00Z"));
        ing.IngestOnce(new[] { ".claude" }, false, later.AddMinutes(-5));
        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        Assert.Single(shard.Sessions);
        Assert.Equal(150, shard.Sessions["u1"].Total);
    }

    [Fact]
    public void Consent_revoked_mid_cycle_leaves_vault_byte_identical()
    {
        // The erase-vs-inflight-cycle guard: a cycle that snapshotted its roots
        // before a purge flipped consent must not write a single byte — the
        // just-purged folder stays purged instead of resurrecting.
        using var home = new TempDir();
        using var vault = new TempDir();
        var path = WriteSession(home.Path, ".claude", "u1", null,
            EventLine("2026-07-10T15:00:00Z"));
        var (ing, _) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        var snapshot = Directory.GetFiles(vault.Path, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToDictionary(p => p, File.ReadAllBytes);

        // Source grows, then consent is revoked mid-cycle.
        Append(path, Now.AddMinutes(-5), EventLine("2026-07-10T16:00:00Z", input: 200, output: 0));
        var r = ing.IngestOnce(new[] { ".claude" }, false, Now, stillConsented: _ => false);

        Assert.True(r.Acquired);
        Assert.Equal(1, r.FilesSeen);
        Assert.Equal(1, r.FilesTailParsed);                  // parsed as it would have been...
        Assert.Equal(0, r.RootsAborted);                     // ...but a clean skip, not an abort

        var after = Directory.GetFiles(vault.Path, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToDictionary(p => p, File.ReadAllBytes);
        Assert.Equal(snapshot.Keys, after.Keys);
        foreach (var (p, bytes) in snapshot)
            Assert.Equal(bytes, after[p]);
    }

    [Fact]
    public void Consent_revoked_from_the_start_keeps_fresh_vault_empty()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1", null, EventLine("2026-07-10T15:00:00Z"));
        var (ing, _) = Make(home.Path, vault.Path);

        var r = ing.IngestOnce(new[] { ".claude" }, false, Now, stillConsented: _ => false);

        Assert.True(r.Acquired);
        Assert.Equal(1, r.FilesFullParsed);
        Assert.Equal(0, r.RootsAborted);
        Assert.Empty(Directory.GetFiles(vault.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void First_ts_month_move_on_reingest_leaves_one_logical_session()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        // Initially the session's first counted event is in August...
        var path = WriteSession(home.Path, ".claude", "u1", null,
            EventLine("2026-08-05T20:00:00Z", input: 200, output: 0));
        var (ing, store) = Make(home.Path, vault.Path);
        var now = DateTimeOffset.Parse("2026-08-06T00:00:00+00:00");
        ing.IngestOnce(new[] { ".claude" }, false, now);
        store.TryLoadSessionShard(".claude", "2026-08", out var aug1);
        Assert.False(aug1.Sessions["u1"].Continuation);

        // ...then a parser-fix-shaped change makes a JULY event parse too:
        // first_ts moves across the month boundary. Exactly one logical
        // session must survive, across the checkpoint's month union.
        WriteSession(home.Path, ".claude", "u1", now.AddMinutes(-5),
            EventLine("2026-07-31T20:00:00Z", input: 100, output: 0),
            EventLine("2026-08-05T20:00:00Z", input: 200, output: 0));
        ing.IngestOnce(new[] { ".claude" }, false, now);

        store.TryLoadSessionShard(".claude", "2026-07", out var jul);
        store.TryLoadSessionShard(".claude", "2026-08", out var aug2);
        Assert.False(jul.Sessions["u1"].Continuation);       // primary moved to July
        Assert.True(aug2.Sessions["u1"].Continuation);       // August is now a slice
        Assert.Equal(100, jul.Sessions["u1"].Total);
        Assert.Equal(200, aug2.Sessions["u1"].Total);

        // And the reverse: the July event stops parsing again — the July row
        // must be REMOVED, not stranded.
        WriteSession(home.Path, ".claude", "u1", now.AddMinutes(-3),
            EventLine("2026-08-05T20:00:00Z", input: 200, output: 0));
        ing.IngestOnce(new[] { ".claude" }, false, now);
        store.TryLoadSessionShard(".claude", "2026-07", out var julAfter);
        Assert.False(julAfter.Sessions.ContainsKey("u1"));
        store.TryLoadSessionShard(".claude", "2026-08", out var aug3);
        Assert.False(aug3.Sessions["u1"].Continuation);
        AssertFoldMatchesRollups(store, ".claude", "2026-07");
        AssertFoldMatchesRollups(store, ".claude", "2026-08");
    }
}
