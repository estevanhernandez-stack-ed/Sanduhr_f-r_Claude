using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class AgentSharesTests
{
    [Fact]
    public void One_agent_reads_exactly_as_today() =>
        Assert.Equal(TokenFormat.Compact(1_200_000), AgentShares.Describe(1_200_000, new Dictionary<string, long> { ["claude"] = 1_200_000 }));

    [Fact]
    public void Several_agents_list_their_shares_in_burn_order() =>
        Assert.Equal($"{TokenFormat.Compact(1_200_000)} (Claude Code {TokenFormat.Compact(900_000)}, Codex {TokenFormat.Compact(300_000)})",
            AgentShares.Describe(1_200_000, new Dictionary<string, long> { ["codex"] = 300_000, ["claude"] = 900_000, ["antigravity"] = 0 }));

    [Fact]
    public void The_strip_names_each_agent_with_a_figure() =>
        Assert.Equal($"30 days, Claude Code {TokenFormat.Compact(41_000_000)}, Codex {TokenFormat.Compact(6_200_000)}",
            AgentShares.StripName(new Dictionary<string, long> { ["claude"] = 41_000_000, ["codex"] = 6_200_000 }));
}
