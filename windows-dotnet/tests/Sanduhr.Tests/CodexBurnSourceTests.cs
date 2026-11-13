using System.IO.Compression;
using System.Text;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class CodexBurnSourceTests
{
    private static readonly TimeZoneInfo Cst = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-12T18:00:00+00:00");

    private static Stream Gunzip(Stream s) => new GZipStream(s, CompressionMode.Decompress);   // stand-in for zstd: the source only sees Func<Stream, Stream>

    private static string Meta(string session, string cwd = @"C:\\Users\\x\\Projects\\api") =>
        $$$"""{"timestamp":"2026-11-10T16:00:00Z","type":"session_meta","payload":{"session_id":"{{{session}}}","cwd":"{{{cwd}}}","thread_source":"user"}}""";
    private static string Turn(string model, string? cwd = null) =>
        cwd is null
            ? $$$"""{"timestamp":"2026-11-10T16:00:01Z","type":"turn_context","payload":{"model":"{{{model}}}"}}"""
            : $$$"""{"timestamp":"2026-11-10T16:00:01Z","type":"turn_context","payload":{"model":"{{{model}}}","cwd":"{{{cwd}}}"}}""";
    private static string Tokens(string ts, long input, long cached, long cacheWrite, long output, long lastInput = 0, long lastOutput = 0) =>
        $$$"""{"timestamp":"{{{ts}}}","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":{{{input}}},"cached_input_tokens":{{{cached}}},"cache_write_input_tokens":{{{cacheWrite}}},"output_tokens":{{{output}}},"reasoning_output_tokens":0,"total_tokens":{{{input + output}}}},"last_token_usage":{"input_tokens":{{{lastInput}}},"cached_input_tokens":0,"cache_write_input_tokens":0,"output_tokens":{{{lastOutput}}},"reasoning_output_tokens":0,"total_tokens":{{{lastInput + lastOutput}}}}},"rate_limits":null}}""";
    private const string NullInfo = """{"timestamp":"2026-11-10T16:05:00Z","type":"event_msg","payload":{"type":"token_count","info":null,"rate_limits":null}}""";

    private static string Rollout(string home, string name, string body, bool compressed = false)
    {
        var dir = Path.Combine(home, "sessions", "2026", "11", "10");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + (compressed ? ".jsonl.zst" : ".jsonl"));
        if (compressed)
        {
            using var f = File.Create(path);
            using var gz = new GZipStream(f, CompressionLevel.Fastest);
            gz.Write(Encoding.UTF8.GetBytes(body));
        }
        else File.WriteAllText(path, body);
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-10).UtcDateTime);
        return path;
    }

    private static VaultSessionRow Ingest(string home, string vault, string session = "rollout-a")
    {
        var store = new VaultStore(vault);
        var ing = new VaultIngester(home, store, "test", null, Cst, "SanduhrTest.Vault." + Guid.NewGuid().ToString("N"));
        ing.IngestOnce([], false, Now, agents: [new CodexBurnSource(() => [home], Gunzip)]);
        Assert.Equal(ShardLoadResult.Ok, store.TryLoadSessionShard("codex", "2026-11", out var shard));
        return shard.Sessions[session];
    }

    [Theory]
    [InlineData("rollout-a.jsonl", "rollout-a")]
    [InlineData("rollout-a.jsonl.zst", "rollout-a")]
    public void Plain_and_compressed_names_share_a_session(string name, string id) =>
        Assert.Equal(id, CodexBurnSource.SessionIdOf(name));

    [Fact]
    public void Tokens_are_total_deltas_mapped_to_fresh_input_cache_and_output()
    {
        using var t = new TempDir();
        Rollout(t.Path, "rollout-a", string.Join("\n",
            Meta("s1"), Turn("gpt-x"),
            Tokens("2026-11-10T16:01:00Z", input: 1000, cached: 600, cacheWrite: 50, output: 100),
            Tokens("2026-11-10T16:02:00Z", input: 1000, cached: 600, cacheWrite: 50, output: 100),   // a repeated reading counts zero
            Tokens("2026-11-10T16:03:00Z", input: 1500, cached: 900, cacheWrite: 50, output: 160)) + "\n");

        var row = Ingest(t.Path, Path.Combine(t.Path, "vault"));

        // fresh input = (1500 - 900) = 600, output = 160 -> total 760
        Assert.Equal(760, row.Total);
        Assert.Equal(900, row.CacheTokens!.Read);
        Assert.Equal(50, row.CacheTokens.Creation);
        Assert.Equal(760, row.ByModel["gpt-x"]);
        Assert.Equal(2, row.EventCount);
        Assert.Equal("api", row.ProjectName);
    }

    [Fact]
    public void A_total_that_drops_falls_back_to_the_last_reading()
    {
        using var t = new TempDir();
        Rollout(t.Path, "rollout-a", string.Join("\n",
            Meta("s1"), Turn("gpt-x"),
            Tokens("2026-11-10T16:01:00Z", input: 1000, cached: 0, cacheWrite: 0, output: 100),
            Tokens("2026-11-10T16:02:00Z", input: 200, cached: 0, cacheWrite: 0, output: 20, lastInput: 200, lastOutput: 20)) + "\n");

        Assert.Equal(1100 + 220, Ingest(t.Path, Path.Combine(t.Path, "vault")).Total);
    }

    [Fact]
    public void A_new_segment_restarts_the_running_total_and_null_info_counts_nothing()
    {
        using var t = new TempDir();
        Rollout(t.Path, "rollout-a", string.Join("\n",
            Meta("s1"), Turn("gpt-x"),
            Tokens("2026-11-10T16:01:00Z", 1000, 0, 0, 100),
            NullInfo,
            Meta("s2"), Turn("gpt-y"),
            Tokens("2026-11-10T16:04:00Z", 300, 0, 0, 30)) + "\n");

        var row = Ingest(t.Path, Path.Combine(t.Path, "vault"));

        Assert.Equal(1100 + 330, row.Total);
        Assert.Equal(1100, row.ByModel["gpt-x"]);
        Assert.Equal(330, row.ByModel["gpt-y"]);
    }

    [Fact]
    public void The_cwd_in_force_at_the_first_counted_event_names_the_project()
    {
        using var t = new TempDir();
        Rollout(t.Path, "rollout-a", string.Join("\n",
            Meta("s1"), Turn("gpt-x", @"C:\\Users\\x\\Projects\\web"),
            Tokens("2026-11-10T16:01:00Z", 10, 0, 0, 1)) + "\n");

        Assert.Equal("web", Ingest(t.Path, Path.Combine(t.Path, "vault")).ProjectName);
    }

    [Fact]
    public void A_grown_rollout_resumes_mid_segment_without_recounting()
    {
        using var t = new TempDir();
        var path = Rollout(t.Path, "rollout-a", string.Join("\n", Meta("s1"), Turn("gpt-x"), Tokens("2026-11-10T16:01:00Z", 1000, 0, 0, 100)) + "\n");
        var vault = Path.Combine(t.Path, "vault");
        Ingest(t.Path, vault);

        File.AppendAllText(path, Tokens("2026-11-10T16:02:00Z", 1200, 0, 0, 120) + "\n");
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-5).UtcDateTime);

        Assert.Equal(1320, Ingest(t.Path, vault).Total);   // 1100 + delta 220, not 1100 + 1320
    }

    [Fact]
    public void A_compressed_rollout_reads_like_its_plain_original()
    {
        using var t = new TempDir();
        var body = string.Join("\n", Meta("s1"), Turn("gpt-x"), Tokens("2026-11-10T16:01:00Z", 1000, 400, 0, 100)) + "\n";
        Rollout(t.Path, "rollout-a", body, compressed: true);

        Assert.Equal(700, Ingest(t.Path, Path.Combine(t.Path, "vault")).Total);
    }

    [Fact]
    public void Without_a_decompressor_compressed_rollouts_are_not_listed()
    {
        using var t = new TempDir();
        Rollout(t.Path, "rollout-a", "x\n", compressed: true);
        Assert.Empty(new CodexBurnSource(() => [t.Path], null).Discover());
    }

    [Fact]
    public void A_rollout_over_the_cap_fails_alone()
    {
        using var t = new TempDir();
        var src = new CodexBurnSource(() => [t.Path], s => new CappedReadStream(Gunzip(s), 10));
        Rollout(t.Path, "rollout-big", string.Join("\n", Meta("s1"), Tokens("2026-11-10T16:01:00Z", 1, 0, 0, 1)) + "\n", compressed: true);
        Rollout(t.Path, "rollout-ok", string.Join("\n", Meta("s2"), Tokens("2026-11-10T16:01:00Z", 1, 0, 0, 1)) + "\n");
        var store = new VaultStore(Path.Combine(t.Path, "vault"));
        var ing = new VaultIngester(t.Path, store, "test", null, Cst, "SanduhrTest.Vault." + Guid.NewGuid().ToString("N"));

        var r = ing.IngestOnce([], false, Now, agents: [src]);

        Assert.Equal(1, r.FilesFailed);
        store.TryLoadSessionShard("codex", "2026-11", out var shard);
        Assert.True(shard.Sessions.ContainsKey("rollout-ok"));
    }

    [Fact]
    public void The_parser_never_reads_message_text()
    {
        var p = new CodexLineParser(null);
        Assert.Null(p.Parse("""{"timestamp":"2026-11-10T16:00:00Z","type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"secret"}]}}""", out var bad));
        Assert.False(bad);
        Assert.DoesNotContain("secret", p.SaveState() ?? "");
    }
}
