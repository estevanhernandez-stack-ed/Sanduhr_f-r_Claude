using Sanduhr.App.Services;
using Xunit;

namespace Sanduhr.App.Tests;

public class VaultServiceAgentTests
{
    private static readonly IReadOnlyDictionary<string, bool> None = new Dictionary<string, bool>();

    [Fact]
    public void A_claude_home_needs_an_explicit_yes() =>
        Assert.False(VaultService.Consented(".claude", None, activeAgents: []));

    [Fact]
    public void An_active_agent_is_on_until_purged()
    {
        Assert.True(VaultService.Consented("codex", None, activeAgents: ["codex"]));
        Assert.False(VaultService.Consented("codex", new Dictionary<string, bool> { ["codex"] = false }, activeAgents: ["codex"]));
    }

    [Fact]
    public void An_agent_whose_switch_is_off_is_never_consented() =>
        Assert.False(VaultService.Consented("antigravity", new Dictionary<string, bool> { ["antigravity"] = true }, activeAgents: []));
}
