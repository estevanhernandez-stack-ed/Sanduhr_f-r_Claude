using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// What Reset's "Claude Code entries" part does to <c>settings.json</c> when the Settings part is left unchecked: the
/// entries are gone, so the switches that make the widget re-create their files at the next launch
/// (<c>mcp_enabled</c> brings back <c>mcp-settings.json</c> and the launcher, <c>statusline_enabled</c> the script and the
/// snapshot) go false, and the folder choices that pointed at them go too. Everything else in the file is untouched.
/// </summary>
public static class ResetSettings
{
    /// <summary>Sets the two switches false and removes the two home names. Returns whether anything changed.</summary>
    public static bool ClearClaudeCodeSwitches(JsonObject root) => ClearMcpSwitches(root) | ClearStatuslineSwitches(root);

    /// <summary>The MCP pair: <c>mcp_enabled</c> false and <c>mcp_cc_home</c> removed. Returns whether anything changed.</summary>
    public static bool ClearMcpSwitches(JsonObject root) => Clear(root, "mcp_enabled", "mcp_cc_home");

    /// <summary>The statusline pair: <c>statusline_enabled</c> false and <c>statusline_cc_home</c> removed. Returns whether anything changed.</summary>
    public static bool ClearStatuslineSwitches(JsonObject root) => Clear(root, "statusline_enabled", "statusline_cc_home");

    private static bool Clear(JsonObject root, string switchKey, string homeKey)
    {
        bool changed = false;
        if (root.TryGetPropertyValue(switchKey, out var node) && !IsFalse(node))
        {
            root[switchKey] = false;
            changed = true;
        }
        changed |= root.Remove(homeKey);
        return changed;
    }

    private static bool IsFalse(JsonNode? node)
    {
        try { return node is not null && !node.GetValue<bool>(); }
        catch (Exception) { return false; }
    }
}
