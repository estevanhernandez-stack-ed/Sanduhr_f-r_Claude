using System.Text;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// WS-C.1 sent/received split battery: new buckets carry input/output with the
/// conservation invariant (input + output == total), legacy WS-C-era buckets
/// deserialize 0/0 ("unsplit", not zero traffic), and tail parses preserve the
/// split accumulation. Uses the same fixture helpers as VaultIngesterTests —
/// duplicated so this file reads standalone (fixed CST zone, pinned mtimes,
/// unique mutex names; that file documents WHY).
/// </summary>
public class VaultSplitTests
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

    [Fact]
    public void New_buckets_conserve_input_plus_output_equals_total()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1",
            EventLine("2026-07-10T15:00:00Z", input: 700, output: 70),
            EventLine("2026-07-10T16:00:00Z", input: 20, output: 2));

        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        var bucket = shard.Sessions["u1"].ByDay["2026-07-10"];
        Assert.Equal(720, bucket.Input);
        Assert.Equal(72, bucket.Output);
        Assert.Equal(bucket.Total, bucket.Input + bucket.Output);   // conservation

        store.TryLoadRollupShard(".claude", "2026-07", out var roll);
        Assert.Equal(720, roll.Days["2026-07-10"].Input);
        Assert.Equal(72, roll.Days["2026-07-10"].Output);
    }

    [Fact]
    public void Legacy_buckets_without_split_fields_read_zero()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        var dir = Path.Combine(tmp.Path, ".claude");
        Directory.CreateDirectory(dir);
        // Hand-written WS-C-era shard: no input/output on the bucket.
        File.WriteAllText(Path.Combine(dir, "sessions-2026-07.json"),
            "{\"schema_version\":1,\"writer_version\":\"3.2.0\",\"sessions\":{\"u1\":{" +
            "\"project_key\":\"api~00000000\",\"project_name\":\"api\"," +
            "\"first_ts\":\"2026-07-01T00:00:00+00:00\",\"last_ts\":\"2026-07-01T01:00:00+00:00\"," +
            "\"utc_offset_min\":-300,\"event_count\":1,\"skipped_lines\":0,\"continuation\":false," +
            "\"total\":150,\"by_model\":{\"claude-fable-5\":150}," +
            "\"by_day\":{\"2026-07-01\":{\"total\":150,\"by_model\":{\"claude-fable-5\":150}}}}}}");

        Assert.Equal(ShardLoadResult.Ok, store.TryLoadSessionShard(".claude", "2026-07", out var shard));
        var bucket = shard.Sessions["u1"].ByDay["2026-07-01"];
        Assert.Equal(150, bucket.Total);
        Assert.Equal(0, bucket.Input);
        Assert.Equal(0, bucket.Output);                               // unsplit, not wrong
    }

    [Fact]
    public void Tail_parse_preserves_split_accumulation()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        var path = WriteSession(home.Path, ".claude", "u1",
            EventLine("2026-07-10T15:00:00Z", input: 100, output: 50));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        File.AppendAllText(path, EventLine("2026-07-10T16:00:00Z", input: 30, output: 3) + "\n");
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-5).UtcDateTime);
        var r2 = ing.IngestOnce(new[] { ".claude" }, false, Now);

        Assert.Equal(1, r2.FilesTailParsed);
        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        var bucket = shard.Sessions["u1"].ByDay["2026-07-10"];
        Assert.Equal(130, bucket.Input);
        Assert.Equal(53, bucket.Output);
        Assert.Equal(bucket.Total, bucket.Input + bucket.Output);
    }
}
