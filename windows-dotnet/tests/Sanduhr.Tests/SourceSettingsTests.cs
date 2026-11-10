using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class SourceSettingsTests
{
    private static SetupSettings Setup(bool claude, bool codex) =>
        new(true, new Dictionary<string, bool> { ["claude"] = claude, ["codex"] = codex });

    [Fact]
    public void Defaults_are_claude_primary_brand_colors_and_alerts_on()
    {
        var s = SourceSettings.Default;
        Assert.Equal("claude", s.Primary);
        Assert.Equal(AgentSources.DefaultColor("codex"), s.ColorOf("codex"));
        Assert.True(s.AlertsOn("codex"));
    }

    [Fact]
    public void Round_trips()
    {
        var s = SourceSettings.Default with
        {
            Primary = "codex",
            Colors = new Dictionary<string, string> { ["codex"] = "#123456" },
            Alerts = new Dictionary<string, bool> { ["claude"] = false },
            Collapsed = new HashSet<string> { "codex" },
        };
        var back = SourceSettings.Decode(JsonNode.Parse(s.Encode().ToJsonString()));
        Assert.Equal("codex", back.Primary);
        Assert.Equal("#123456", back.ColorOf("codex"));
        Assert.False(back.AlertsOn("claude"));
        Assert.Contains("codex", back.Collapsed);
    }

    [Theory]
    [InlineData("""{"colors":{"codex":"teal"}}""")]
    [InlineData("""{"colors":{"codex":"#12345"}}""")]
    [InlineData("""{"colors":7}""")]
    [InlineData("""[]""")]
    public void Bad_values_fall_back(string json) =>
        Assert.Equal(AgentSources.DefaultColor("codex"), SourceSettings.Decode(JsonNode.Parse(json)).ColorOf("codex"));

    [Fact]
    public void An_unknown_primary_decodes_to_claude() =>
        Assert.Equal("claude", SourceSettings.Decode(JsonNode.Parse("""{"primary":"gemini"}""")).Primary);

    [Fact]
    public void Primary_follows_what_is_ticked()
    {
        var s = SourceSettings.Default with { Primary = "claude" };
        Assert.Equal("codex", s.PrimaryFor(Setup(claude: false, codex: true)));
        Assert.Equal("claude", s.PrimaryFor(Setup(claude: true, codex: true)));
        Assert.Equal("claude", s.PrimaryFor(Setup(claude: false, codex: false)));
    }
}
