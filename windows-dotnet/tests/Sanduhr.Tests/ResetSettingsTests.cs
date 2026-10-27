using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

public class ResetSettingsTests
{
    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void Both_switches_go_false_and_both_homes_go_and_everything_else_stays()
    {
        var root = Parse("""{"mcp_enabled":true,"statusline_enabled":true,"mcp_cc_home":".claude","statusline_cc_home":".claude-work","theme":"dark","mcp_roots":{".claude":true}}""");

        Assert.True(ResetSettings.ClearClaudeCodeSwitches(root));

        Assert.False((bool)root["mcp_enabled"]!);
        Assert.False((bool)root["statusline_enabled"]!);
        Assert.Null(root["mcp_cc_home"]);
        Assert.Null(root["statusline_cc_home"]);
        Assert.Equal("dark", (string)root["theme"]!);
        Assert.True((bool)root["mcp_roots"]![".claude"]!);
    }

    [Fact]
    public void A_missing_key_is_not_created()
    {
        var root = Parse("""{"mcp_enabled":true}""");
        Assert.True(ResetSettings.ClearClaudeCodeSwitches(root));
        Assert.False((bool)root["mcp_enabled"]!);
        Assert.False(root.ContainsKey("statusline_enabled"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"mcp_enabled":false,"statusline_enabled":false,"theme":"x"}""")]
    public void Already_clear_changes_nothing(string json)
    {
        var root = Parse(json);
        var before = root.ToJsonString();
        Assert.False(ResetSettings.ClearClaudeCodeSwitches(root));
        Assert.Equal(before, root.ToJsonString());
    }

    [Fact]
    public void The_two_pairs_clear_on_their_own()
    {
        const string json = """{"mcp_enabled":true,"statusline_enabled":true,"mcp_cc_home":".claude","statusline_cc_home":".claude-work"}""";

        var mcp = Parse(json);
        Assert.True(ResetSettings.ClearMcpSwitches(mcp));
        Assert.False((bool)mcp["mcp_enabled"]!);
        Assert.Null(mcp["mcp_cc_home"]);
        Assert.True((bool)mcp["statusline_enabled"]!);
        Assert.Equal(".claude-work", (string)mcp["statusline_cc_home"]!);

        var statusline = Parse(json);
        Assert.True(ResetSettings.ClearStatuslineSwitches(statusline));
        Assert.False((bool)statusline["statusline_enabled"]!);
        Assert.Null(statusline["statusline_cc_home"]);
        Assert.True((bool)statusline["mcp_enabled"]!);
        Assert.Equal(".claude", (string)statusline["mcp_cc_home"]!);
    }

    [Fact]
    public void A_switch_of_the_wrong_type_is_replaced_by_false()
    {
        var root = Parse("""{"mcp_enabled":"yes"}""");
        Assert.True(ResetSettings.ClearClaudeCodeSwitches(root));
        Assert.False((bool)root["mcp_enabled"]!);
    }
}
