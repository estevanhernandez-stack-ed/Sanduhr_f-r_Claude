using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class AntigravityBurnSourceTests
{
    private static readonly TimeZoneInfo Cst = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-12T18:00:00+00:00");

    private static string Turn(string ts, long input, long output, long cacheRead) =>
        $$"""{"type":"PLANNER_RESPONSE","created_at":"{{ts}}","step_index":1,"input_tokens":{{input}},"output_tokens":{{output}},"cache_read_tokens":{{cacheRead}},"content":"never read"}""";
    private const string UserLine = """{"type":"USER_INPUT","created_at":"2026-11-10T16:00:00Z","step_index":0,"content":"never read"}""";

    private static string Transcript(string home, string conversation, string body, string name = "transcript.jsonl")
    {
        var dir = Path.Combine(home, "brain", conversation, ".system_generated", "logs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, body);
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-10).UtcDateTime);
        return path;
    }

    private static (VaultIngester, VaultStore) Make(string home)
    {
        var store = new VaultStore(Path.Combine(home, "vault"));
        return (new VaultIngester(home, store, "test", null, Cst, "SanduhrTest.Vault." + Guid.NewGuid().ToString("N")), store);
    }

    private const string Api = """["file:///c%3A/Users/x/Projects/api"]""";

    [Theory]
    [InlineData("""["file:///c%3A/Users/x/Projects/api"]""", @"c:\Users\x\Projects\api")]
    [InlineData("""["file:///C:/Users/x/My%20App","file:///C:/other"]""", @"C:\Users\x\My App")]
    [InlineData("[]", null)]
    [InlineData(null, null)]
    [InlineData("not json", null)]
    [InlineData("""["https://example.com/x"]""", null)]
    public void Project_path_is_the_first_file_uri(string? json, string? expected) =>
        Assert.Equal(expected, AntigravityBurnSource.ProjectPathOf(json));

    [Fact]
    public void Turns_are_dated_one_by_one_and_land_in_their_project()
    {
        using var t = new TempDir();
        Transcript(t.Path, "conv-1", string.Join("\n", UserLine,
            Turn("2026-11-10T16:01:00Z", 1000, 100, 5000),
            Turn("2026-11-11T16:01:00Z", 500, 50, 2000)) + "\n");
        var src = new AntigravityBurnSource(t.Path, () => new Dictionary<string, string?> { ["conv-1"] = Api });
        var (ing, store) = Make(t.Path);

        ing.IngestOnce([], false, Now, agents: [src]);

        store.TryLoadSessionShard("antigravity", "2026-11", out var shard);
        var row = shard.Sessions["conv-1"];
        Assert.Equal(1650, row.Total);
        Assert.Equal(7000, row.CacheTokens!.Read);
        Assert.Equal(1100, row.ByDay["2026-11-10"].Total);
        Assert.Equal(550, row.ByDay["2026-11-11"].Total);
        Assert.Equal("api", row.ProjectName);
        Assert.Equal(1650, row.ByModel["antigravity"]);
        Assert.Equal(0, row.SkippedLines);   // the user line is not bad
    }

    [Fact]
    public void Transcript_full_is_never_read()
    {
        using var t = new TempDir();
        Transcript(t.Path, "conv-1", Turn("2026-11-10T16:01:00Z", 10, 1, 0) + "\n");
        Transcript(t.Path, "conv-1", Turn("2026-11-10T16:01:00Z", 10, 1, 0) + "\n", name: "transcript_full.jsonl");
        var files = new AntigravityBurnSource(t.Path, () => new Dictionary<string, string?>()).Discover();
        Assert.Single(files);
        Assert.Equal("transcript.jsonl", files[0].File.Name);
    }

    [Fact]
    public void A_conversation_without_a_summaries_row_is_unattributed()
    {
        using var t = new TempDir();
        Transcript(t.Path, "conv-1", Turn("2026-11-10T16:01:00Z", 10, 1, 0) + "\n");
        var files = new AntigravityBurnSource(t.Path, () => new Dictionary<string, string?>()).Discover();
        Assert.Null(files.Single().Cwd);
        Assert.Equal("conv-1", files[0].SessionId);
    }

    [Fact]
    public void An_unreadable_database_lists_nothing_so_no_checkpoint_moves()
    {
        using var t = new TempDir();
        Transcript(t.Path, "conv-1", Turn("2026-11-10T16:01:00Z", 10, 1, 0) + "\n");
        Assert.Empty(new AntigravityBurnSource(t.Path, () => null).Discover());
    }

    [Fact]
    public void A_missing_cli_home_lists_nothing() =>
        Assert.Empty(new AntigravityBurnSource(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N")),
            () => new Dictionary<string, string?>()).Discover());

    [Fact]
    public void Bad_json_counts_as_skipped()
    {
        Assert.Null(new AntigravityBurnSource.Parser().Parse("""{"input_tokens": broken""", out var bad));
        Assert.True(bad);
    }

    [Fact]
    public void Duplicate_keys_count_as_skipped()
    {
        Assert.Null(new AntigravityBurnSource.Parser().Parse(
            """{"created_at":"2026-11-10T16:01:00Z","input_tokens":1,"input_tokens":2}""", out var bad));
        Assert.True(bad);
    }

    [Fact]
    public void Default_home_is_the_cli_folder() =>
        Assert.Equal(Path.Combine(@"C:\Users\x", ".gemini", "antigravity-cli"), AntigravityBurnSource.DefaultCliHome(@"C:\Users\x"));
}
