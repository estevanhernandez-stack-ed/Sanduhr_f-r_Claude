using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class AgentBurnDeltasTests
{
    private static readonly DateOnly D1 = new(2026, 11, 12), D2 = new(2026, 11, 13);

    [Fact]
    public void The_first_observation_has_no_before_so_shows_nothing()
    {
        var d = new AgentBurnDeltas();
        d.Observe("codex", D1, 5000);
        Assert.Equal("", d.FooterPieces());
    }

    [Fact]
    public void Each_agent_shows_the_change_since_the_ingest_before_in_burn_order()
    {
        var d = new AgentBurnDeltas();
        d.Observe("antigravity", D1, 100);
        d.Observe("codex", D1, 1000);
        d.Observe("antigravity", D1, 1100);
        d.Observe("codex", D1, 4000);
        // TokenFormat.Compact renders 3000 as "3.0k" and 1000 as "1.0k"
        Assert.Equal("  ·  Codex +3.0k  ·  Agy +1.0k", d.FooterPieces());
    }

    [Fact]
    public void Nothing_new_is_left_out()
    {
        var d = new AgentBurnDeltas();
        d.Observe("codex", D1, 1000);
        d.Observe("codex", D1, 1000);
        Assert.Equal("", d.FooterPieces());
    }

    [Fact]
    public void At_a_day_change_the_earlier_total_counts_as_zero()
    {
        var d = new AgentBurnDeltas();
        d.Observe("codex", D1, 90_000);
        d.Observe("codex", D2, 2000);
        Assert.Equal("  ·  Codex +2.0k", d.FooterPieces());
    }

    [Fact]
    public void A_forgotten_agent_disappears()
    {
        var d = new AgentBurnDeltas();
        d.Observe("codex", D1, 1000);
        d.Observe("codex", D1, 3000);
        d.Forget("codex");
        Assert.Equal("", d.FooterPieces());
    }

    [Fact]
    public void An_observation_after_forget_is_a_first_observation_and_shows_nothing()
    {
        var d = new AgentBurnDeltas();
        d.Observe("codex", D1, 1000);
        d.Observe("codex", D1, 3000);
        d.Forget("codex");
        d.Observe("codex", D1, 9000);
        Assert.Equal("", d.FooterPieces());
        d.Observe("codex", D1, 10_000);
        Assert.Equal("  ·  Codex +1.0k", d.FooterPieces());
    }
}
