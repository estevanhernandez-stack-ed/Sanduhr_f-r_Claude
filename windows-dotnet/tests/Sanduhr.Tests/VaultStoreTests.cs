using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// VaultStore IO contract (spec 2026-07-12-usage-vault-design.md, Storage):
/// atomic writes, per-root dirs, quarantine coupled to checkpoint invalidation,
/// corrupt-tolerant loads, and the hashed checkpoint path key.
/// </summary>
public class VaultStoreTests
{
    private static VaultSessionShard Shard(params (string Uuid, long Total)[] rows)
    {
        var shard = new VaultSessionShard
        {
            SchemaVersion = 1,
            WriterVersion = "test",
            Sessions = new Dictionary<string, VaultSessionRow>(),
        };
        foreach (var (uuid, total) in rows)
        {
            shard.Sessions[uuid] = new VaultSessionRow
            {
                ProjectKey = "api~00000000",
                ProjectName = "api",
                FirstTs = "2026-07-01T00:00:00+00:00",
                LastTs = "2026-07-01T01:00:00+00:00",
                UtcOffsetMin = -300,
                EventCount = 1,
                Total = total,
                ByModel = new Dictionary<string, long> { ["claude-fable-5"] = total },
                ByDay = new Dictionary<string, VaultDayBucket>
                {
                    ["2026-07-01"] = new VaultDayBucket
                    {
                        Total = total,
                        ByModel = new Dictionary<string, long> { ["claude-fable-5"] = total },
                    },
                },
            };
        }
        return shard;
    }

    [Fact]
    public void Session_shard_round_trips_and_lands_in_per_root_dir()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        store.SaveSessionShard(".claude", "2026-07", Shard(("u1", 100)));

        Assert.True(File.Exists(Path.Combine(tmp.Path, ".claude", "sessions-2026-07.json")));
        Assert.Equal(ShardLoadResult.Ok, store.TryLoadSessionShard(".claude", "2026-07", out var loaded));
        Assert.Equal(100, loaded.Sessions["u1"].Total);
        Assert.Equal(1, loaded.SchemaVersion);
    }

    [Fact]
    public void Save_replaces_atomically_leaving_no_tmp_file()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        store.SaveSessionShard(".claude", "2026-07", Shard(("u1", 100)));
        store.SaveSessionShard(".claude", "2026-07", Shard(("u1", 200)));

        var files = Directory.GetFiles(Path.Combine(tmp.Path, ".claude"));
        Assert.Single(files); // no .tmp residue
        store.TryLoadSessionShard(".claude", "2026-07", out var loaded);
        Assert.Equal(200, loaded.Sessions["u1"].Total);
    }

    [Fact]
    public void Missing_shard_reports_missing_not_corrupt()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        Assert.Equal(ShardLoadResult.Missing, store.TryLoadSessionShard(".claude", "2026-07", out _));
    }

    [Fact]
    public void Corrupt_session_shard_reports_corrupt_and_does_not_mutate()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        var dir = Path.Combine(tmp.Path, ".claude");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "sessions-2026-07.json"), "{not json");

        Assert.Equal(ShardLoadResult.Corrupt, store.TryLoadSessionShard(".claude", "2026-07", out _));
        // Read path never quarantines — the file is untouched.
        Assert.True(File.Exists(Path.Combine(dir, "sessions-2026-07.json")));
    }

    [Fact]
    public void Quarantine_renames_timestamped_and_deletes_checkpoints()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        var dir = Path.Combine(tmp.Path, ".claude");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "sessions-2026-07.json"), "{not json");
        store.SaveCheckpoints(".claude", new VaultCheckpointFile
        {
            SchemaVersion = 1,
            Entries = new Dictionary<string, VaultCheckpointEntry>(),
        });
        Assert.True(File.Exists(Path.Combine(dir, "checkpoints.json")));

        var now = DateTimeOffset.Parse("2026-07-15T03:15:00+00:00");
        store.QuarantineSessionShard(".claude", "2026-07", now);

        Assert.False(File.Exists(Path.Combine(dir, "sessions-2026-07.json")));
        Assert.True(File.Exists(Path.Combine(dir, "sessions-2026-07.json.20260715T031500.bad")));
        Assert.False(File.Exists(Path.Combine(dir, "checkpoints.json")));
    }

    [Fact]
    public void Quarantine_never_overwrites_an_existing_bad_file()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        var dir = Path.Combine(tmp.Path, ".claude");
        Directory.CreateDirectory(dir);
        var now = DateTimeOffset.Parse("2026-07-15T03:15:00+00:00");
        File.WriteAllText(Path.Combine(dir, "sessions-2026-07.json.20260715T031500.bad"), "earlier");
        File.WriteAllText(Path.Combine(dir, "sessions-2026-07.json"), "{not json");

        store.QuarantineSessionShard(".claude", "2026-07", now);

        // Existing .bad kept; new one gets a uniquified name.
        Assert.Equal("earlier", File.ReadAllText(Path.Combine(dir, "sessions-2026-07.json.20260715T031500.bad")));
        Assert.False(File.Exists(Path.Combine(dir, "sessions-2026-07.json")));
        Assert.Equal(2, Directory.GetFiles(dir, "*.bad").Length);
    }

    [Fact]
    public void Corrupt_checkpoints_load_empty_and_delete_the_file()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        var dir = Path.Combine(tmp.Path, ".claude");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "checkpoints.json"), "][");

        var cp = store.LoadCheckpoints(".claude");
        Assert.Empty(cp.Entries);
        Assert.False(File.Exists(Path.Combine(dir, "checkpoints.json")));
    }

    [Fact]
    public void ListSessionShardMonths_sorted_and_ignores_bad_and_rollups()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        store.SaveSessionShard(".claude", "2026-07", Shard(("u1", 1)));
        store.SaveSessionShard(".claude", "2026-06", Shard(("u2", 1)));
        store.SaveRollupShard(".claude", "2026-06", new VaultRollupShard
        {
            SchemaVersion = 1,
            Days = new Dictionary<string, VaultRollupDay>(),
        });
        File.WriteAllText(Path.Combine(tmp.Path, ".claude", "sessions-2026-05.json.20260715T031500.bad"), "x");

        Assert.Equal(new[] { "2026-06", "2026-07" }, store.ListSessionShardMonths(".claude"));
    }

    [Fact]
    public void Meta_round_trips_and_missing_is_null()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        Assert.Null(store.LoadMeta(".claude"));
        store.SaveMeta(".claude", new VaultRootMeta
        {
            Since = "2026-07-12",
            Covered = new List<VaultDateRange> { new() { From = "2026-06-17", To = "2026-07-12" } },
            LastIngestTs = "2026-07-12T09:00:00+00:00",
        });
        var meta = store.LoadMeta(".claude");
        Assert.NotNull(meta);
        Assert.Equal("2026-07-12", meta!.Since);
        Assert.Single(meta.Covered);
    }

    [Fact]
    public void PurgeRoot_removes_only_that_root()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        store.SaveSessionShard(".claude", "2026-07", Shard(("u1", 1)));
        store.SaveSessionShard(".claude-personal", "2026-07", Shard(("u2", 1)));

        store.PurgeRoot(".claude");

        Assert.False(Directory.Exists(Path.Combine(tmp.Path, ".claude")));
        Assert.True(Directory.Exists(Path.Combine(tmp.Path, ".claude-personal")));
    }

    [Fact]
    public void PathKey_is_case_insensitive_and_hex()
    {
        var a = VaultStore.PathKey(@"C:\Users\X\.claude\projects\p\u.jsonl");
        var b = VaultStore.PathKey(@"c:\users\x\.claude\projects\p\u.jsonl");
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
        Assert.Matches("^[0-9a-f]{64}$", a);
    }

    [Fact]
    public void Serialized_shard_uses_snake_case_wire_names()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        store.SaveSessionShard(".claude", "2026-07", Shard(("u1", 100)));
        var raw = File.ReadAllText(Path.Combine(tmp.Path, ".claude", "sessions-2026-07.json"));
        Assert.Contains("\"schema_version\"", raw);
        Assert.Contains("\"by_model\"", raw);
        Assert.Contains("\"by_day\"", raw);
        Assert.Contains("\"project_key\"", raw);
        Assert.DoesNotContain("\"ProjectKey\"", raw);
        // cwd omitted when null (store_full_paths off) — no path leaks by default.
        Assert.DoesNotContain("\"cwd\"", raw);
    }
}
