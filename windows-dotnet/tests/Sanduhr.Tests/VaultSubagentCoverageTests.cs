using System.Text;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// WS-C.1 recursive-walk battery: nested subagent transcripts get their own
/// rows with parent_session, and a vault written by the old one-level walk
/// re-ingests exactly once via the walk_version gate. Uses the same fixture
/// helpers as VaultIngesterTests — duplicated so this file reads standalone
/// (fixed CST zone, pinned mtimes, unique mutex names; that file documents WHY).
/// </summary>
public class VaultSubagentCoverageTests
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
    /// quiesce/seal branch engage nondeterministically.</summary>
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

    private const string ParentUuid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    [Fact]
    public void Nested_file_gets_parent_session_and_its_own_row()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", ParentUuid, EventLine("2026-07-10T15:00:00Z"));
        WriteNested(home.Path, ".claude", ParentUuid, "agent-x", null,
            EventLine("2026-07-10T16:00:00Z", input: 200, output: 0));

        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        Assert.Equal(2, shard.Sessions.Count);
        Assert.Null(shard.Sessions[ParentUuid].ParentSession);      // main transcript: no parent
        Assert.Equal(ParentUuid, shard.Sessions["agent-x"].ParentSession);
        Assert.Equal(200, shard.Sessions["agent-x"].Total);
    }

    [Fact]
    public void Non_uuid_subdirectory_yields_no_parent()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        var dir = Path.Combine(home.Path, ".claude", "projects", "c--Users-x-Projects-api", "scratch");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "stray.jsonl"), EventLine("2026-07-10T15:00:00Z") + "\n");
        File.SetLastWriteTimeUtc(Path.Combine(dir, "stray.jsonl"), Now.AddMinutes(-10).UtcDateTime);

        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        Assert.Null(shard.Sessions["stray"].ParentSession);          // "scratch" is not a Guid
    }

    [Fact]
    public void Walk_version_upgrade_reingests_once_and_matches_a_fresh_vault()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", ParentUuid, EventLine("2026-07-10T15:00:00Z"));
        WriteNested(home.Path, ".claude", ParentUuid, "agent-x", null,
            EventLine("2026-07-10T16:00:00Z", input: 200, output: 0));

        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        // Regress the vault to v1 shape: strip walk_version from meta (a WS-C
        // vault never wrote it) and delete the nested file's row — a v1 walk
        // never saw it. The stale checkpoints still cover the flat file, so
        // ONLY an invalidation makes the re-ingest resurrect the nested row.
        var metaPath = Path.Combine(vault.Path, ".claude", "meta.json");
        File.WriteAllText(metaPath, File.ReadAllText(metaPath)
            .Replace(",\"walk_version\":2", "").Replace("\"walk_version\":2,", ""));
        store.TryLoadSessionShard(".claude", "2026-07", out var s1);
        s1.Sessions.Remove("agent-x");
        store.SaveSessionShard(".claude", "2026-07", s1);

        var r2 = ing.IngestOnce(new[] { ".claude" }, false, Now);     // upgrade cycle
        Assert.True(r2.FilesFullParsed >= 2);                          // checkpoints were invalidated

        store.TryLoadSessionShard(".claude", "2026-07", out var upgraded);

        using var freshVault = new TempDir();
        var (ing2, store2) = Make(home.Path, freshVault.Path, mutexName: MutexName());
        ing2.IngestOnce(new[] { ".claude" }, false, Now);
        store2.TryLoadSessionShard(".claude", "2026-07", out var fresh);

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(fresh.Sessions),
            System.Text.Json.JsonSerializer.Serialize(upgraded.Sessions));

        var r3 = ing.IngestOnce(new[] { ".claude" }, false, Now);     // NO second invalidation
        Assert.Equal(0, r3.FilesFullParsed + r3.FilesTailParsed);
        Assert.Equal(2, r3.FilesSkipped);
    }

    [Fact]
    public void Upgrade_preserves_rows_for_aged_out_files()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        var path = WriteSession(home.Path, ".claude", "u-old", EventLine("2026-07-01T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);
        File.Delete(path);                                             // source ages out

        var metaPath = Path.Combine(vault.Path, ".claude", "meta.json");
        File.WriteAllText(metaPath, File.ReadAllText(metaPath)
            .Replace(",\"walk_version\":2", "").Replace("\"walk_version\":2,", ""));

        ing.IngestOnce(new[] { ".claude" }, false, Now);               // upgrade with source gone

        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        Assert.Equal(150, shard.Sessions["u-old"].Total);              // the record outlives its source
    }

    [Fact]
    public void Vault_day_total_matches_live_reader_with_nested_files()
    {
        // The WS-C parity invariant, recursive edition — real clock + local zone,
        // real-clock mtimes (see VaultIngesterTests' parity test for why).
        using var home = new TempDir();
        using var vault = new TempDir();
        var ts = DateTimeOffset.UtcNow.AddDays(-1);
        var iso = ts.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        WriteSession(home.Path, ".claude", ParentUuid,
            mtimeUtc: DateTimeOffset.UtcNow.AddMinutes(-10),
            EventLine(iso, input: 700, output: 70));
        WriteNested(home.Path, ".claude", ParentUuid, "agent-x",
            DateTimeOffset.UtcNow.AddMinutes(-10),
            EventLine(iso, input: 20, output: 2));

        var store = new VaultStore(vault.Path);
        var ing = new VaultIngester(home.Path, store, "test", null, TimeZoneInfo.Local, MutexName());
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
        Assert.Equal(792, vaultDayTotal);                              // 770 main + 22 agent
    }
}
