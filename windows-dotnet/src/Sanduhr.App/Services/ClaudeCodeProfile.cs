namespace Sanduhr.App.Services;

/// <summary>
/// Where the Claude Code and Mods &amp; Config pages look for Claude Code folders: the user profile and
/// <c>CLAUDE_CONFIG_DIR</c>. Production reads the real ones. The render harness sets
/// <see cref="ProfileOverride"/> to a fake profile so no real path, project or account label reaches a
/// picture. Nothing else in the app should set it.
/// </summary>
public static class ClaudeCodeProfile
{
    /// <summary>When set, the profile folder the pages use in place of the real one.</summary>
    public static string? ProfileOverride { get; set; }

    /// <summary>When <see cref="ProfileOverride"/> is set, <c>CLAUDE_CONFIG_DIR</c> reads as this (empty: unset).</summary>
    public static string? ConfigDirOverride { get; set; }

    public static string Dir =>
        ProfileOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string? ConfigDir =>
        ProfileOverride is not null ? ConfigDirOverride : Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
}
