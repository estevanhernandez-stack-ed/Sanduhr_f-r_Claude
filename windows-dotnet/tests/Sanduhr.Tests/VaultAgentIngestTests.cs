using System.Globalization;
using System.IO.Compression;
using System.Text;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class VaultAgentIngestTests
{
    private static readonly TimeZoneInfo Cst = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-12T18:00:00+00:00");

    /// <summary>Lines "2026-11-10T10:00:00Z,100,50"; state = how many events this file has produced so far.</summary>
    private sealed class CsvParser(string? state) : IBurnLineParser
    {
        public int Seen = state is null ? 0 : int.Parse(state, CultureInfo.InvariantCulture);
        public BurnEvent? Parse(string line, out bool bad)
        {
            bad = false;
            var p = line.Split(',');
            if (p.Length != 3) { bad = true; return null; }
            Seen++;
            return new BurnEvent(DateTimeOffset.Parse(p[0], CultureInfo.InvariantCulture), long.Parse(p[1], CultureInfo.InvariantCulture),
                long.Parse(p[2], CultureInfo.InvariantCulture), 0, 0, "fake-model", null, null);
        }
        public string? SaveState() => Seen.ToString(CultureInfo.InvariantCulture);
    }

    private sealed class FakeSource(string agent, Func<IReadOnlyList<AgentBurnFile>> files) : IAgentBurnSource
    {
        public readonly List<string?> StatesHandedOut = [];
        public string Agent => agent;
        public IReadOnlyList<AgentBurnFile> Discover() => files();
        public Stream Open(AgentBurnFile f)
        {
            var fs = new FileStream(f.File.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return f.Compressed ? new GZipStream(fs, CompressionMode.Decompress) : fs;
        }
        public IBurnLineParser NewParser(string? state) { StatesHandedOut.Add(state); return new CsvParser(state); }
    }

    private static string Write(string dir, string name, string body, bool gzip = false)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        if (gzip)
        {
            using var f = File.Create(path);
            using var gz = new GZipStream(f, CompressionLevel.Fastest);
            gz.Write(Encoding.UTF8.GetBytes(body));
        }
        else File.WriteAllText(path, body);
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-10).UtcDateTime);
        return path;
    }

    private static (VaultIngester, VaultStore) Make(string home, string vault)
    {
        var store = new VaultStore(vault);
        return (new VaultIngester(home, store, "test", null, Cst, "SanduhrTest.Vault." + Guid.NewGuid().ToString("N")), store);
    }

    [Fact]
    public void An_agent_source_writes_its_own_root_tagged_with_the_agent()
    {
        using var t = new TempDir();
        var file = Write(Path.Combine(t.Path, "in"), "a.jsonl", "2026-11-10T16:00:00Z,100,50\n2026-11-11T16:00:00Z,10,5\n");
        var src = new FakeSource("codex", () => [new AgentBurnFile(new FileInfo(file), "sess-a", @"C:\Users\x\Projects\api", false)]);
        var (ing, store) = Make(t.Path, Path.Combine(t.Path, "vault"));

        var r = ing.IngestOnce([], false, Now, agents: [src]);

        Assert.True(r.Acquired);
        Assert.Equal("codex", store.LoadMeta("codex")!.Agent);
        Assert.Equal(ShardLoadResult.Ok, store.TryLoadSessionShard("codex", "2026-11", out var shard));
        var row = shard.Sessions["sess-a"];
        Assert.Equal(165, row.Total);
        Assert.Equal("api", row.ProjectName);
        Assert.Equal(new[] { "2026-11-10", "2026-11-11" }, row.ByDay.Keys.OrderBy(k => k));
    }

    [Fact]
    public void A_grown_plain_file_resumes_with_the_saved_parser_state()
    {
        using var t = new TempDir();
        var dir = Path.Combine(t.Path, "in");
        var file = Write(dir, "a.jsonl", "2026-11-10T16:00:00Z,100,50\n");
        var src = new FakeSource("codex", () => [new AgentBurnFile(new FileInfo(file), "sess-a", null, false)]);
        var (ing, store) = Make(t.Path, Path.Combine(t.Path, "vault"));
        ing.IngestOnce([], false, Now, agents: [src]);

        File.AppendAllText(file, "2026-11-10T17:00:00Z,1,1\n");
        File.SetLastWriteTimeUtc(file, Now.AddMinutes(-5).UtcDateTime);
        ing.IngestOnce([], false, Now, agents: [src]);

        Assert.Equal(new string?[] { null, "1" }, src.StatesHandedOut);   // the tail pass got the first pass's state
        store.TryLoadSessionShard("codex", "2026-11", out var shard);
        Assert.Equal(152, shard.Sessions["sess-a"].Total);
    }

    [Fact]
    public void A_torn_last_line_is_left_for_the_next_pass()
    {
        using var t = new TempDir();
        var file = Write(Path.Combine(t.Path, "in"), "a.jsonl", "2026-11-10T16:00:00Z,100,50\n2026-11-10T16:30:00Z,9");
        var src = new FakeSource("codex", () => [new AgentBurnFile(new FileInfo(file), "sess-a", null, false)]);
        var (ing, store) = Make(t.Path, Path.Combine(t.Path, "vault"));
        ing.IngestOnce([], false, Now, agents: [src]);
        store.TryLoadSessionShard("codex", "2026-11", out var first);
        Assert.Equal(150, first.Sessions["sess-a"].Total);

        File.AppendAllText(file, ",1\n");
        File.SetLastWriteTimeUtc(file, Now.AddMinutes(-5).UtcDateTime);
        ing.IngestOnce([], false, Now, agents: [src]);
        store.TryLoadSessionShard("codex", "2026-11", out var second);
        Assert.Equal(160, second.Sessions["sess-a"].Total);
    }

    [Fact]
    public void A_compressed_file_is_parsed_whole_and_replaces_its_plain_twin_row()
    {
        using var t = new TempDir();
        var dir = Path.Combine(t.Path, "in");
        const string body = "2026-11-10T16:00:00Z,100,50\n";
        var plain = Write(dir, "a.jsonl", body);
        var src = new FakeSource("codex", () => [new AgentBurnFile(new FileInfo(plain), "sess-a", null, false)]);
        var (ing, store) = Make(t.Path, Path.Combine(t.Path, "vault"));
        ing.IngestOnce([], false, Now, agents: [src]);

        // Distinct body: 152 = the .zst was parsed whole and replaced the row; 150 = skipped; 302 = counts added.
        var zst = Write(dir, "a.jsonl.zst", body + "2026-11-10T17:00:00Z,1,1\n", gzip: true);
        var both = new FakeSource("codex", () =>
            [new AgentBurnFile(new FileInfo(plain), "sess-a", null, false), new AgentBurnFile(new FileInfo(zst), "sess-a", null, true)]);
        var r = ing.IngestOnce([], false, Now, agents: [both]);

        Assert.Equal(0, r.FilesFailed);
        Assert.Equal(0, r.RootsAborted);
        Assert.Equal(1, r.FilesFullParsed);
        store.TryLoadSessionShard("codex", "2026-11", out var shard);
        Assert.Equal(152, shard.Sessions["sess-a"].Total);
    }

    [Fact]
    public void CappedReadStream_allows_exactly_the_cap_and_throws_one_byte_past_it()
    {
        using (var ok = new CappedReadStream(new MemoryStream(new byte[10]), 10))
        {
            var buf = new byte[32];
            int total = 0, n;
            while ((n = ok.Read(buf, 0, buf.Length)) > 0) total += n;
            Assert.Equal(10, total);
        }
        using var over = new CappedReadStream(new MemoryStream(new byte[11]), 10);
        Assert.Throws<InvalidDataException>(() =>
        {
            var buf = new byte[32];
            while (over.Read(buf, 0, buf.Length) > 0) { }
        });
    }

    [Fact]
    public void A_bad_compressed_file_fails_alone_and_the_other_files_still_land()
    {
        using var t = new TempDir();
        var dir = Path.Combine(t.Path, "in");
        var bad = Write(dir, "bad.jsonl.zst", "not gzip at all");
        var good = Write(dir, "good.jsonl", "2026-11-10T16:00:00Z,1,1\n");
        var src = new FakeSource("codex", () =>
            [new AgentBurnFile(new FileInfo(bad), "sess-bad", null, true), new AgentBurnFile(new FileInfo(good), "sess-good", null, false)]);
        var (ing, store) = Make(t.Path, Path.Combine(t.Path, "vault"));

        var r = ing.IngestOnce([], false, Now, agents: [src]);

        Assert.Equal(1, r.FilesFailed);
        store.TryLoadSessionShard("codex", "2026-11", out var shard);
        Assert.True(shard.Sessions.ContainsKey("sess-good"));
        Assert.False(shard.Sessions.ContainsKey("sess-bad"));
    }

    [Fact]
    public void A_source_that_throws_on_discovery_skips_only_its_own_root()
    {
        using var t = new TempDir();
        var file = Write(Path.Combine(t.Path, "in"), "a.jsonl", "2026-11-10T16:00:00Z,1,1\n");
        var broken = new FakeSource("antigravity", () => throw new IOException("locked"));
        var ok = new FakeSource("codex", () => [new AgentBurnFile(new FileInfo(file), "s", null, false)]);
        var (ing, store) = Make(t.Path, Path.Combine(t.Path, "vault"));

        var r = ing.IngestOnce([], false, Now, agents: [broken, ok]);

        Assert.Equal(1, r.RootsAborted);
        Assert.Equal(ShardLoadResult.Ok, store.TryLoadSessionShard("codex", "2026-11", out _));
    }

    [Fact]
    public void An_agent_root_revoked_mid_cycle_writes_nothing()
    {
        using var t = new TempDir();
        var file = Write(Path.Combine(t.Path, "in"), "a.jsonl", "2026-11-10T16:00:00Z,1,1\n");
        var src = new FakeSource("codex", () => [new AgentBurnFile(new FileInfo(file), "s", null, false)]);
        var (ing, store) = Make(t.Path, Path.Combine(t.Path, "vault"));

        ing.IngestOnce([], false, Now, stillConsented: root => root != "codex", agents: [src]);

        Assert.Equal(ShardLoadResult.Missing, store.TryLoadSessionShard("codex", "2026-11", out _));
    }

    [Fact]
    public void A_project_that_appears_later_moves_the_whole_session()
    {
        using var t = new TempDir();
        var file = Write(Path.Combine(t.Path, "in"), "a.jsonl", "2026-11-10T16:00:00Z,100,50\n");
        string? cwd = null;
        var src = new FakeSource("antigravity", () => [new AgentBurnFile(new FileInfo(file), "conv", cwd, false)]);
        var (ing, store) = Make(t.Path, Path.Combine(t.Path, "vault"));
        ing.IngestOnce([], false, Now, agents: [src]);
        store.TryLoadSessionShard("antigravity", "2026-11", out var before);
        Assert.Equal("(none)", before.Sessions["conv"].ProjectName);

        cwd = @"C:\Users\x\Projects\web";
        File.AppendAllText(file, "2026-11-10T17:00:00Z,1,1\n");
        File.SetLastWriteTimeUtc(file, Now.AddMinutes(-5).UtcDateTime);
        ing.IngestOnce([], false, Now, agents: [src]);
        store.TryLoadSessionShard("antigravity", "2026-11", out var after);
        Assert.Equal("web", after.Sessions["conv"].ProjectName);
        Assert.Equal(152, after.Sessions["conv"].Total);
    }
}
