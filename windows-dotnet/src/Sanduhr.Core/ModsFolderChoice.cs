namespace Sanduhr.Core;

/// <summary>
/// Picks the Claude Code folder card that opens "What this folder holds": the folder you used last, else the one Claude Code
/// runs from (<c>CLAUDE_CONFIG_DIR</c>), then the active account's, then the most plugins on, then the first.
/// </summary>
public static class ModsFolderChoice
{
    /// <summary>
    /// The path in <paramref name="folders"/> to select, or null when the list is empty. Every candidate must be
    /// one of the listed folders (same path, any case or trailing slash); one that is not listed is passed over.
    /// </summary>
    /// <param name="remembered">The folder picked earlier in this session.</param>
    /// <param name="configDir">The <c>CLAUDE_CONFIG_DIR</c> environment variable as Sanduhr sees it.</param>
    /// <param name="activeAccountFolder">The folder linked to the active account.</param>
    /// <param name="enabledPlugins">How many plugins a folder's settings.json switches on.</param>
    public static string? Pick(IReadOnlyList<string> folders, string? remembered, string? configDir,
        string? activeAccountFolder, Func<string, int> enabledPlugins)
    {
        if (folders.Count == 0) return null;
        foreach (var candidate in new[] { remembered, configDir, activeAccountFolder })
            if (!string.IsNullOrWhiteSpace(candidate)
                && folders.FirstOrDefault(f => ClaudeCodeFolders.Same(f, candidate)) is { } listed)
                return listed;

        string best = folders[0];
        int most = Count(best);
        for (int i = 1; i < folders.Count; i++)
        {
            int n = Count(folders[i]);
            if (n > most) { best = folders[i]; most = n; }
        }
        return best;

        int Count(string folder)
        {
            try { return enabledPlugins(folder); }
            catch (Exception) { return 0; }
        }
    }
}
