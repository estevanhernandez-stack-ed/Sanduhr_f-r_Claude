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
    public static bool ClearClaudeCodeSwitches(JsonObject root)
    {
        bool changed = false;
        foreach (var key in new[] { "mcp_enabled", "statusline_enabled" })
        {
            if (root.TryGetPropertyValue(key, out var node) && !IsFalse(node))
            {
                root[key] = false;
                changed = true;
            }
        }
        foreach (var key in new[] { "mcp_cc_home", "statusline_cc_home" })
            changed |= root.Remove(key);
        return changed;
    }

    private static bool IsFalse(JsonNode? node)
    {
        try { return node is not null && !node.GetValue<bool>(); }
        catch (Exception) { return false; }
    }
}
