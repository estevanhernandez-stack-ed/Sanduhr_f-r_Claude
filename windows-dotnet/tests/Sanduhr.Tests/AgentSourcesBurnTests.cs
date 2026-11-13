using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class AgentSourcesBurnTests
{
    [Fact]
    public void Antigravity_is_a_burn_source_but_never_a_limits_source()
    {
        Assert.Equal(new[] { "claude", "codex", "antigravity" }, AgentSources.Burn);
        Assert.DoesNotContain(AgentSources.Antigravity, AgentSources.All);
        Assert.Equal(new[] { "antigravity" }, AgentSources.ActivityOnly);
    }

    [Theory]
    [InlineData("claude", "CC", "Claude Code")]
    [InlineData("codex", "Codex", "Codex")]
    [InlineData("antigravity", "Agy", "Antigravity")]
    public void Names(string id, string shortName, string burnName)
    {
        Assert.Equal(shortName, AgentSources.ShortName(id));
        Assert.Equal(burnName, AgentSources.BurnName(id));
    }

    [Fact]
    public void Antigravity_has_its_default_color_and_a_display_name()
    {
        Assert.Equal("#7B8CF0", AgentSources.DefaultColor(AgentSources.Antigravity));
        Assert.Equal("Antigravity", AgentSources.Name(AgentSources.Antigravity));
        Assert.Equal("#7B8CF0", SourceSettings.Default.ColorOf(AgentSources.Antigravity));
    }
}
