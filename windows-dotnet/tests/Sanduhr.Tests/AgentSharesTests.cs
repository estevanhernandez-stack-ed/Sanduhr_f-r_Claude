using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class AgentSharesTests
{
    [Fact]
    public void One_agent_reads_exactly_as_today() =>
        Assert.Equal(TokenFormat.Compact(1_200_000), AgentShares.Describe(1_200_000, new Dictionary<string, long> { ["claude"] = 1_200_000 }));

    [Theory]
    [InlineData("codex", "Codex")]
    [InlineData("antigravity", "Antigravity")]
    public void Another_agent_alone_is_named(string agent, string name) =>
        // Live smoke 2026-11-17: a Codex-only project read "12.5M", indistinguishable from Claude Code burn.
        Assert.Equal($"{TokenFormat.Compact(12_500_000)} ({name})",
            AgentShares.Describe(12_500_000, new Dictionary<string, long> { [agent] = 12_500_000, ["claude"] = 0 }));

    [Fact]
    public void No_agent_figures_read_as_the_bare_total() =>
        Assert.Equal(TokenFormat.Compact(5_000), AgentShares.Describe(5_000, new Dictionary<string, long>()));

    [Fact]
    public void Several_agents_list_their_shares_in_burn_order() =>
        Assert.Equal($"{TokenFormat.Compact(1_200_000)} (Claude Code {TokenFormat.Compact(900_000)}, Codex {TokenFormat.Compact(300_000)})",
            AgentShares.Describe(1_200_000, new Dictionary<string, long> { ["codex"] = 300_000, ["claude"] = 900_000, ["antigravity"] = 0 }));

    [Fact]
    public void The_strip_names_each_agent_with_a_figure() =>
        Assert.Equal($"30 days, Claude Code {TokenFormat.Compact(41_000_000)}, Codex {TokenFormat.Compact(6_200_000)}",
            AgentShares.StripName(new Dictionary<string, long> { ["claude"] = 41_000_000, ["codex"] = 6_200_000 }));
}
