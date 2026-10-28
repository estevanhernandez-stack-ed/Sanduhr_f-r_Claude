namespace Sanduhr.Core;

/// <summary>One Claude Code folder the Claude Code and Mods &amp; Widgets pages know: its full path, its
/// folder name, and whether Sanduhr found it (<see cref="ClaudeCodeHomes.Discover"/>) or the user added it.</summary>
public sealed record CcFolder(string Path, string Name, bool Discovered);

/// <summary>
/// The folder list behind the Claude Code page: every discovered home, then the folders the user added by
/// hand (<c>cc_folders_added</c>). Added folders are installer targets only. No path built here ever reaches
/// the MCP server, which resolves its own roots from <see cref="ClaudeCodeHomes.Discover"/> alone (spec 4.0).
/// </summary>
public static class ClaudeCodeFolders
{
    /// <summary>Discovered homes first (in <c>Discover</c>'s order), then added folders in the order given,
    /// each once. An added folder that is already a discovered home, or repeats another, is dropped.</summary>
    public static IReadOnlyList<CcFolder> Build(string profileDir, string? claudeConfigDir, IEnumerable<string> added)
    {
        var list = new List<CcFolder>();
        foreach (var name in ClaudeCodeHomes.Discover(profileDir, claudeConfigDir))
            list.Add(new CcFolder(Norm(System.IO.Path.Combine(profileDir, name)), name, true));
        foreach (var raw in added)
        {
            var path = Norm(raw);
            if (path.Length == 0 || list.Any(f => Same(f.Path, path)))
                continue;
            list.Add(new CcFolder(path, System.IO.Path.GetFileName(path) is { Length: > 0 } n ? n : path, false));
        }
        return list;
    }

    /// <summary>The full path without a trailing separator; empty for a blank or unusable value.</summary>
    public static string Norm(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try
        {
            var full = System.IO.Path.GetFullPath(path.Trim());
            var root = System.IO.Path.GetPathRoot(full) ?? "";
            return full.Length > root.Length ? full.TrimEnd('\\', '/') : full;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or System.IO.PathTooLongException)
        {
            return "";
        }
    }

    public static bool Same(string a, string b) =>
        string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>The path with the profile folder written as <c>~</c> (<c>~\.claude</c>), so a user name never shows.</summary>
    public static string Display(string path, string profileDir)
    {
        var full = Norm(path);
        var profile = Norm(profileDir);
        if (full.Length == 0 || profile.Length == 0) return path;
        if (string.Equals(full, profile, StringComparison.OrdinalIgnoreCase)) return "~";
        var prefix = profile.EndsWith('\\') ? profile : profile + "\\";
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? "~\\" + full[prefix.Length..] : full;
    }

    /// <summary>
    /// Whether a Claude Code piece that reads <c>snapshot.json</c> (the statusline, the meters mod) is
    /// installed in any of <paramref name="folders"/>. This is what the <c>statusline_enabled</c> key means:
    /// the widget writes the snapshot while it is true and deletes it when none remains.
    /// </summary>
    public static bool AnySnapshotConsumer(IEnumerable<CcFolder> folders,
        Func<string, bool> statuslineInstalled, Func<string, bool> metersInstalled) =>
        folders.Any(f => statuslineInstalled(f.Path) || metersInstalled(f.Path));
}
