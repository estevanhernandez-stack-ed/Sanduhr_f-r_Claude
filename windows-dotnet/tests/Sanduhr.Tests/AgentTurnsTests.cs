using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class AgentTurnsTests
{
    private static AgentTurnEvent E(string agent, AgentTurnKind kind, TimeSpan? d = null) =>
        new(agent, kind, "s1", @"C:\proj\alpha", DateTimeOffset.UnixEpoch, d);

    [Theory]
    [InlineData("codex", AgentTurnKind.Finished, "Codex finished")]
    [InlineData("claude", AgentTurnKind.Finished, "Claude Code finished")]
    [InlineData("antigravity", AgentTurnKind.Stopped, "Antigravity stopped")]
    [InlineData("claude", AgentTurnKind.Waiting, "Claude Code is waiting on you")]
    public void Title_names_the_agent_and_what_happened(string agent, AgentTurnKind kind, string expected) =>
        Assert.Equal(expected, NotificationText.Title(E(agent, kind)));

    [Theory]
    [InlineData(45, "45s")]
    [InlineData(124, "2m 04s")]
    [InlineData(252, "4m 12s")]
    [InlineData(3900, "1h 05m")]
    [InlineData(-5, "0s")]
    public void Duration_reads_as_a_person_says_it(int seconds, string expected) =>
        Assert.Equal(expected, NotificationText.Duration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Body_is_the_project_then_the_duration() =>
        Assert.Equal("alpha, 4m 12s", NotificationText.Body(E("codex", AgentTurnKind.Finished, TimeSpan.FromSeconds(252)), "alpha"));

    [Fact]
    public void Body_for_waiting_is_the_project_alone() =>
        Assert.Equal("alpha", NotificationText.Body(E("claude", AgentTurnKind.Waiting, TimeSpan.FromSeconds(9)), "alpha"));

    [Fact]
    public void Body_without_a_project_is_the_duration_and_without_either_is_empty()
    {
        Assert.Equal("45s", NotificationText.Body(E("codex", AgentTurnKind.Finished, TimeSpan.FromSeconds(45)), null));
        Assert.Equal("", NotificationText.Body(E("codex", AgentTurnKind.Finished), " "));
    }

    [Fact]
    public void Card_title_is_the_agent_and_the_project()
    {
        Assert.Equal("Codex · alpha", NotificationText.CardTitle("codex", "alpha"));
        Assert.Equal("Claude Code", NotificationText.CardTitle("claude", null));
    }
}
