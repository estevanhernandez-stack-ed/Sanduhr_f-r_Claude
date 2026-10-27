using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// The mask every surface reads (Settings v2, slice 7). <see cref="StreamerService"/> is the only writer; it raises its own
/// <c>Changed</c>, so nothing subscribes to a static event. Off until the widget's service loads. Tests reset it (AppWindowFixture).
/// </summary>
public static class StreamerMode
{
    public static StreamerMask Mask { get; private set; } = StreamerMask.Off;

    public static bool On => Mask.On;

    internal static void Use(StreamerMask mask) => Mask = mask;

    internal static void Reset() => Mask = StreamerMask.Off;

    /// <summary>A Claude Code folder as every page shows it (CP 7): its codename while streamer mode hides it, else <c>~\…</c> under
    /// <paramref name="profileDir"/> (the page's own profile folder, which tests point at a scratch one).</summary>
    public static string ShowFolder(string path, string profileDir) =>
        Mask.HidesFolder(path) ? Mask.Folder(path) : ClaudeCodeFolders.Display(path, profileDir);

    /// <summary>Status and error text that may name folders by full path (CP 8): each of <paramref name="folderPaths"/> shows as it does on
    /// its card, and any other path under the user folder shows as <c>%USERPROFILE%</c>. Off, the text is unchanged.</summary>
    public static string FoldersIn(string text, IEnumerable<string> folderPaths)
    {
        if (!Mask.On || string.IsNullOrEmpty(text))
            return text;
        foreach (var path in folderPaths.Select(ClaudeCodeFolders.Norm).Where(p => p.Length > 0).OrderByDescending(p => p.Length))
            if (Mask.HidesFolder(path))
                text = text.Replace(path, Mask.Folder(path), StringComparison.OrdinalIgnoreCase);
        return Mask.Paths(text);
    }
}
