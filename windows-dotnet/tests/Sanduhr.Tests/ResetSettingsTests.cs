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
    public void A_switch_of_the_wrong_type_is_replaced_by_false()
    {
        var root = Parse("""{"mcp_enabled":"yes"}""");
        Assert.True(ResetSettings.ClearClaudeCodeSwitches(root));
        Assert.False((bool)root["mcp_enabled"]!);
    }
}
