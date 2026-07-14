using System.Text;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// WS-C.1 logical-session fold: a session = its main transcript + nested
/// subagent transcripts (parent_session ?? uuid). Read side folds members into
/// one VaultSessionInfo with agent stats; rollups count DISTINCT logical
/// sessions; VaultWindow carries the per-day sent/received split; CoveredSet
/// batches coverage (metas load once). Reader fixtures write shards through
/// VaultStore directly (VaultReaderTests convention, Row extended with
/// parentSession); the rollup test uses the ingester fixture helpers from
/// VaultSubagentCoverageTests — duplicated so this file reads standalone
/// (fixed CST zone, pinned mtimes, unique mutex names; VaultIngesterTests
/// documents WHY).
/// </summary>
public class VaultLogicalSessionTests
{
    // -- reader-side fixture helpers (VaultReaderTests convention) ------------------

    private static VaultSessionRow Row(
        string projectKey, string firstTs, string lastTs, bool continuation,
        string? parentSession, params (string Day, long Total)[] days)
    {
        var row = new VaultSessionRow
        {
            ProjectKey = projectKey,
            ProjectName = VaultReader.ProjectNameOf(projectKey),
            ParentSession = parentSession,
            FirstTs = firstTs,
            LastTs = lastTs,
            UtcOffsetMin = -300,
            EventCount = continuation ? 0 : days.Length,
            Continuation = continuation,
            CacheTokens = continuation ? null : new VaultCacheTokens { Read = 1, Creation = 1 },
            ByDay = days.ToDictionary(
                d => d.Day,
                d => new VaultDayBucket
                {
                    Total = d.Total,
                    ByModel = new Dictionary<string, long> { ["claude-fable-5"] = d.Total },
                }),
        };
        VaultRowMath.RecomputeRowAggregates(row);
        return row;
    }

    private static void SaveShard(VaultStore store, string root, string month,
        params (string Uuid, VaultSessionRow Row)[] rows)
    {
        var shard = new VaultSessionShard { SchemaVersion = 1, WriterVersion = "test" };
        foreach (var (uuid, row) in rows)
            shard.Sessions[uuid] = row;
        store.SaveSessionShard(root, month, shard);
    }

    // -- ingester-side fixture helpers (VaultSubagentCoverageTests convention) ------

    private static readonly TimeZoneInfo Cst =
        TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-12T18:00:00+00:00");

    private const string ParentUuid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

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
    /// fixed test clock (Now-10min) — real-clock mtimes would make the
    /// quiesce/seal branch engage nondeterministically.</summary>
    private static string WriteSession(string home, string root, string uuid, params string[] lines)
    {
        var dir = Path.Combine(home, root, "projects", "c--Users-x-Projects-api");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, uuid + ".jsonl");
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-10).UtcDateTime);
        return path;
    }

    /// <summary>Writes a NESTED transcript: {projectDir}\{parentUuid}\subagents\{name}.jsonl,
    /// mtime pinned like WriteSession.</summary>
    private static string WriteNested(
        string home, string root, string parentUuid, string name,
        DateTimeOffset? mtimeUtc = null, params string[] lines)
    {
        var dir = Path.Combine(home, root, "projects", "c--Users-x-Projects-api",
            parentUuid, "subagents");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + ".jsonl");
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        File.SetLastWriteTimeUtc(path, (mtimeUtc ?? Now.AddMinutes(-10)).UtcDateTime);
        return path;
    }

    private static (VaultIngester Ingester, VaultStore Store) Make(
        string home, string vaultDir, TimeZoneInfo? tz = null, string? logFile = null, string? mutexName = null)
    {
        var store = new VaultStore(vaultDir, logFile);
        var ing = new VaultIngester(home, store, "test", logFile, tz ?? Cst, mutexName ?? MutexName());
        return (ing, store);
    }

    // -- tests -----------------------------------------------------------------------

    [Fact]
    public void Members_fold_into_one_logical_session_with_agent_stats()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        SaveShard(store, ".claude", "2026-07",
            ("parent-1", Row("api~aaaaaaaa", "2026-07-10T10:00:00+00:00", "2026-07-10T18:00:00+00:00",
                continuation: false, parentSession: null, ("2026-07-10", 100))),
            ("agent-x", Row("api~aaaaaaaa", "2026-07-10T11:00:00+00:00", "2026-07-10T12:00:00+00:00",
                continuation: false, parentSession: "parent-1", ("2026-07-10", 200))),
            ("agent-y", Row("api~aaaaaaaa", "2026-07-10T13:00:00+00:00", "2026-07-10T19:00:00+00:00",
                continuation: false, parentSession: "parent-1", ("2026-07-10", 50))));

        var reader = new VaultReader(store);
        var sessions = reader.ReadSessions(new[] { ".claude" });

        var s = Assert.Single(sessions);
        Assert.Equal("parent-1", s.Uuid);
        Assert.Equal(350, s.Total);
        Assert.Equal(350, s.ByDay["2026-07-10"].Total);           // same-day buckets SUMMED
        Assert.Equal(2, s.AgentCount);
        Assert.Equal(250, s.AgentTokens);
        Assert.Equal(new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero), s.FirstTs);
        Assert.Equal(new DateTimeOffset(2026, 7, 10, 19, 0, 0, TimeSpan.Zero), s.LastTs);  // agent outlived main
    }

    [Fact]
    public void Agent_only_session_survives_when_main_transcript_aged_out()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        SaveShard(store, ".claude", "2026-07",
            ("agent-x", Row("api~aaaaaaaa", "2026-07-10T11:00:00+00:00", "2026-07-10T12:00:00+00:00",
                continuation: false, parentSession: "parent-gone", ("2026-07-10", 200))));

        var reader = new VaultReader(store);
        var s = Assert.Single(reader.ReadSessions(new[] { ".claude" }));
        Assert.Equal("parent-gone", s.Uuid);
        Assert.Equal("api", s.ProjectName);                        // identity from first member
        Assert.Equal(1, s.AgentCount);
        Assert.Equal(200, s.AgentTokens);
    }

    [Fact]
    public void Sessions_without_parents_behave_exactly_as_before()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        SaveShard(store, ".claude", "2026-07",
            ("u1", Row("api~aaaaaaaa", "2026-07-10T10:00:00+00:00", "2026-07-10T11:00:00+00:00",
                continuation: false, parentSession: null, ("2026-07-10", 100))));

        var s = Assert.Single(new VaultReader(store).ReadSessions(new[] { ".claude" }));
        Assert.Equal("u1", s.Uuid);
        Assert.Equal(0, s.AgentCount);
        Assert.Equal(0, s.AgentTokens);
    }

    [Fact]
    public void Rollup_sessions_count_is_distinct_logical_sessions()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", ParentUuid, EventLine("2026-07-10T15:00:00Z"));
        WriteNested(home.Path, ".claude", ParentUuid, "agent-x", null,
            EventLine("2026-07-10T16:00:00Z", input: 200, output: 0));
        WriteNested(home.Path, ".claude", ParentUuid, "agent-y", null,
            EventLine("2026-07-10T17:00:00Z", input: 50, output: 0));

        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        store.TryLoadRollupShard(".claude", "2026-07", out var roll);
        Assert.Equal(1, roll.Days["2026-07-10"].Sessions);         // 3 files, ONE session
        Assert.Equal(400, roll.Days["2026-07-10"].Total);
    }

    [Fact]
    public void VaultWindow_carries_split_and_CoveredSet_batches_coverage()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        var roll = new VaultRollupShard { SchemaVersion = 1 };
        roll.Days["2026-07-10"] = new VaultRollupDay
        {
            Total = 130, Input = 100, Output = 30, Sessions = 1,
            ByModel = new() { ["claude-fable-5"] = 130 },
            ByProject = new() { ["api~aaaaaaaa"] = 130 },
        };
        store.SaveRollupShard(".claude", "2026-07", roll);
        store.SaveMeta(".claude", new VaultRootMeta
        {
            Since = "2026-07-01",
            Covered = new List<VaultDateRange> { new() { From = "2026-07-05", To = "2026-07-12" } },
            LastIngestTs = "2026-07-12T18:00:00.000000+00:00",
            WalkVersion = 2,
        });

        var reader = new VaultReader(store);
        var w = reader.ReadWindow(new[] { ".claude" }, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 12));
        Assert.Equal(100, w.ByDayInput[new DateOnly(2026, 7, 10)]);
        Assert.Equal(30, w.ByDayOutput[new DateOnly(2026, 7, 10)]);

        var covered = reader.CoveredSet(new[] { ".claude" }, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 12));
        Assert.Contains(new DateOnly(2026, 7, 5), covered);
        Assert.DoesNotContain(new DateOnly(2026, 7, 4), covered);
    }
}
