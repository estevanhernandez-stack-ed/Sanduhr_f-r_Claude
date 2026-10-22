namespace Sanduhr.Core;

/// <summary>
/// The words in the MCP install dialog that say what the install leaves alone: the other Claude Code
/// folders that already carry Sanduhr's server, and the account links. Pure text, so the wording is tested
/// without a window.
/// </summary>
public static class McpConsentCopy
{
    /// <summary>
    /// "Installing here changes only ~\.claude. ~\.claude-personal already has Sanduhr's MCP server; installing
    /// here doesn't change it. Account links (which folder belongs to which account) don't change."
    /// <paramref name="othersWithServer"/> are the display paths (<c>~\.claude-personal</c>) of the other folders that
    /// already have the server; blank entries and the target itself are ignored.
    /// </summary>
    public static string WontChange(string targetDisplay, IEnumerable<string> othersWithServer)
    {
        var others = othersWithServer
            .Where(o => !string.IsNullOrWhiteSpace(o) && !string.Equals(o, targetDisplay, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var parts = new List<string> { $"Installing here changes only {targetDisplay}." };
        if (others.Count == 1)
            parts.Add($"{others[0]} already has Sanduhr's MCP server; installing here doesn't change it.");
        else if (others.Count > 1)
            parts.Add($"{JoinNames(others)} already have Sanduhr's MCP server; installing here doesn't change them.");
        parts.Add("Account links (which folder belongs to which account) don't change.");
        return string.Join(" ", parts);
    }

    private static string JoinNames(IReadOnlyList<string> names) =>
        names.Count == 1 ? names[0]
        : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
}
