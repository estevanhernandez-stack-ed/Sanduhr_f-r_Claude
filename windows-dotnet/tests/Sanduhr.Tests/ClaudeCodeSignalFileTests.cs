using System.Text;
using System.Text.Json;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class ClaudeCodeSignalFileTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-17T20:00:00Z");

    private static byte[] File(string ev = "done", string? cwd = @"C:\proj\alpha", string? session = "sess-1", long? at = null, int v = 1) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["v"] = v, ["event"] = ev, ["cwd"] = cwd, ["session"] = session, ["at"] = at ?? Now.ToUnixTimeMilliseconds(),
        }));

    [Theory]
    [InlineData("prompt")]
    [InlineData("waiting")]
    [InlineData("done")]
    public void A_signal_decodes(string ev)
    {
        var s = ClaudeCodeSignalFile.Decode(File(ev), Now);
        Assert.Equal(new ClaudeCodeSignalFile.Signal(ev, "sess-1", @"C:\proj\alpha", Now), s);
    }

    [Fact]
    public void An_empty_folder_or_session_is_kept_as_unknown()
    {
        var s = ClaudeCodeSignalFile.Decode(File(cwd: "", session: ""), Now);
        Assert.Null(s!.Folder);
        Assert.Equal(ClaudeCodeSignalFile.NoSession, s.Session);
    }

    [Fact]
    public void A_signal_older_than_ten_minutes_or_from_the_future_is_dropped()
    {
        Assert.Null(ClaudeCodeSignalFile.Decode(File(at: Now.AddMinutes(-11).ToUnixTimeMilliseconds()), Now));
        Assert.Null(ClaudeCodeSignalFile.Decode(File(at: Now.AddMinutes(2).ToUnixTimeMilliseconds()), Now));
        Assert.NotNull(ClaudeCodeSignalFile.Decode(File(at: Now.AddMinutes(-9).ToUnixTimeMilliseconds()), Now));
    }

    [Fact]
    public void An_oversized_file_is_dropped() =>
        Assert.Null(ClaudeCodeSignalFile.Decode(File(cwd: new string('a', 5000)), Now));

    [Fact]
    public void A_folder_over_1024_characters_is_dropped_but_the_signal_stands()
    {
        var s = ClaudeCodeSignalFile.Decode(File(cwd: new string('a', 1100)), Now);
        Assert.NotNull(s);
        Assert.Null(s!.Folder);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1]")]
    [InlineData("{\"v\":2,\"event\":\"done\",\"at\":1}")]
    [InlineData("{\"v\":1,\"event\":\"other\",\"at\":1}")]
    [InlineData("{\"v\":1,\"event\":\"done\"}")]
    [InlineData("{\"v\":1,\"v\":1,\"event\":\"done\",\"at\":1}")]
    [InlineData("{\"v\":1,\"event\":\"done\",\"at\":99999999999999999}")]
    public void Anything_else_is_dropped(string text) =>
        Assert.Null(ClaudeCodeSignalFile.Decode(Encoding.UTF8.GetBytes(text), Now));

    [Fact]
    public void A_prompt_then_a_done_is_a_turn_with_its_length()
    {
        var turns = new ClaudeCodeTurns();
        var started = turns.Apply(new("prompt", "sess-1", @"C:\proj\alpha", Now));
        Assert.Equal(new AgentTurnEvent("claude", AgentTurnKind.Started, "sess-1", @"C:\proj\alpha", Now), started);

        var done = turns.Apply(new("done", "sess-1", @"C:\proj\alpha", Now.AddSeconds(252)));
        Assert.Equal(AgentTurnKind.Finished, done.Kind);
        Assert.Equal(TimeSpan.FromSeconds(252), done.Duration);
    }

    [Fact]
    public void A_done_with_no_prompt_seen_has_no_duration() =>
        Assert.Null(new ClaudeCodeTurns().Apply(new("done", "sess-1", null, Now)).Duration);

    [Fact]
    public void A_done_stamped_before_its_prompt_has_no_duration()
    {
        var turns = new ClaudeCodeTurns();
        turns.Apply(new("prompt", "sess-1", null, Now));
        Assert.Null(turns.Apply(new("done", "sess-1", null, Now.AddSeconds(-5))).Duration);
    }

    [Fact]
    public void Waiting_does_not_end_the_turn()
    {
        var turns = new ClaudeCodeTurns();
        turns.Apply(new("prompt", "sess-1", null, Now));
        Assert.Equal(AgentTurnKind.Waiting, turns.Apply(new("waiting", "sess-1", null, Now.AddSeconds(30))).Kind);
        Assert.Equal(TimeSpan.FromSeconds(90), turns.Apply(new("done", "sess-1", null, Now.AddSeconds(90))).Duration);
    }

    [Fact]
    public void Two_sessions_keep_their_own_start_times()
    {
        var turns = new ClaudeCodeTurns();
        turns.Apply(new("prompt", "a", null, Now));
        turns.Apply(new("prompt", "b", null, Now.AddSeconds(60)));
        Assert.Equal(TimeSpan.FromSeconds(100), turns.Apply(new("done", "a", null, Now.AddSeconds(100))).Duration);
        Assert.Equal(TimeSpan.FromSeconds(40), turns.Apply(new("done", "b", null, Now.AddSeconds(100))).Duration);
    }

    [Fact]
    public void Past_the_cap_the_oldest_start_is_forgotten()
    {
        var turns = new ClaudeCodeTurns();
        for (int i = 0; i <= ClaudeCodeTurns.MaxSessions; i++)
            turns.Apply(new("prompt", "s" + i, null, Now.AddSeconds(i)));
        Assert.Null(turns.Apply(new("done", "s0", null, Now.AddSeconds(500))).Duration);
        Assert.NotNull(turns.Apply(new("done", "s1", null, Now.AddSeconds(500))).Duration);
    }
}
