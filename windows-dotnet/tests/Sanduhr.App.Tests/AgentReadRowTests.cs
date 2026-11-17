using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Tests;

/// <summary>Which agents get a "Let Claude read project totals" switch (spec 3).</summary>
public sealed class AgentReadRowTests
{
    [Fact]
    public void Active_agents_show_and_claude_never_does()
    {
        var shown = AgentReadRowViewModel.Shown(new HashSet<string> { "claude", "codex" }, new Dictionary<string, bool>());
        Assert.Equal(new[] { "codex" }, shown);
    }

    [Fact]
    public void A_consented_agent_that_went_inactive_still_shows_so_it_can_be_revoked()
    {
        var shown = AgentReadRowViewModel.Shown(new HashSet<string>(), new Dictionary<string, bool> { ["antigravity"] = true, ["codex"] = false });
        Assert.Equal(new[] { "antigravity" }, shown);
    }
}
