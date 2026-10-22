namespace Sanduhr.Core;

/// <summary>Which file the wallpaper is read from, and where it came from.</summary>
/// <param name="Path">The file to decode; null when the desktop is a plain color. A path with no <paramref name="Source"/> names
/// a file that is gone and has no cached copy: it cannot be read.</param>
/// <param name="Source"><see cref="WallpaperFallback.SourceFile"/> (the file Windows names), <see cref="WallpaperFallback.SourceCache"/>
/// (Windows' own cached copy) or null (nothing readable).</param>
public sealed record WallpaperChoice(string? Path, string? Source)
{
    /// <summary>True when there is a readable file.</summary>
    public bool Readable => Path is not null && Source is not null;
}

/// <summary>
/// Which wallpaper file to read when the one Windows names is gone (spec, the agent-designed Desk, B). <c>IDesktopWallpaper</c> and
/// the registry keep the path of the picture the user chose, long after the file was deleted, while Windows still draws the picture
/// from its cache in <c>%APPDATA%\Microsoft\Windows\Themes</c>: <c>TranscodedWallpaper</c> (the first monitor, or all of them) and
/// <c>Transcoded_000</c>, <c>Transcoded_001</c>... (one per monitor, in the same folder or in <c>CachedFiles</c>). These files have
/// no extension and are decoded by content. Pure: the App passes what exists.
/// </summary>
public static class WallpaperFallback
{
    public const string SourceFile = "file";
    public const string SourceCache = "cache";

    /// <summary>The cache file Windows keeps for the desktop as a whole.</summary>
    public const string CacheFile = "TranscodedWallpaper";

    /// <summary>The per-monitor cache file for a monitor index: <c>Transcoded_000</c>, <c>Transcoded_001</c>...</summary>
    public static string MonitorCacheFile(int monitorIndex) => "Transcoded_" + Math.Max(0, monitorIndex).ToString("D3", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Windows' themes folder under the roaming AppData folder.</summary>
    public static string ThemesDir(string appData) => System.IO.Path.Combine(appData, "Microsoft", "Windows", "Themes");

    /// <summary>The cache files to try for a monitor, best first: its own file, then the one for the whole desktop.</summary>
    public static IReadOnlyList<string> Candidates(string themesDir, int monitorIndex) =>
    [
        System.IO.Path.Combine(themesDir, MonitorCacheFile(monitorIndex)),
        System.IO.Path.Combine(themesDir, "CachedFiles", MonitorCacheFile(monitorIndex)),
        System.IO.Path.Combine(themesDir, CacheFile),
    ];

    /// <summary>
    /// The file to read for one monitor. The path Windows reported wins when the file is there. Otherwise (no path, or a file that
    /// was deleted or moved) the first cache file that exists is used. <paramref name="plainColor"/> says the shell reports no
    /// picture at all (a solid color desktop): then no cache is used, because a cache left from an earlier picture would be
    /// read as the desktop. With nothing readable the reported path is returned without a source.
    /// </summary>
    /// <param name="reported">The path <c>IDesktopWallpaper</c> reported, null or blank for none.</param>
    /// <param name="monitorIndex">The monitor's index in the shell's list (Transcoded_00N).</param>
    /// <param name="themesDir">Windows' themes folder (<see cref="ThemesDir"/>).</param>
    /// <param name="exists">Whether a file exists and has content.</param>
    /// <param name="plainColor">The desktop is a solid color (no picture chosen).</param>
    public static WallpaperChoice Choose(string? reported, int monitorIndex, string themesDir, Func<string, bool> exists, bool plainColor = false)
    {
        string? path = string.IsNullOrWhiteSpace(reported) ? null : reported;
        if (path is not null && exists(path)) return new WallpaperChoice(path, SourceFile);
        if (path is null && plainColor) return new WallpaperChoice(null, null);
        foreach (var candidate in Candidates(themesDir, monitorIndex))
            if (exists(candidate)) return new WallpaperChoice(candidate, SourceCache);
        return new WallpaperChoice(path, null);
    }
}

/// <summary>
/// The wallpaper preview file (<c>~\.sanduhr\wallpaper-preview.png</c>): a copy of the wallpaper scaled so its longest side is at
/// most 1600 pixels, written by the widget for Claude Code to look at while the MCP integration is installed. Pure sizing and
/// change detection; the App decodes and writes.
/// </summary>
public static class WallpaperPreview
{
    public const string FileName = "wallpaper-preview.png";

    /// <summary>The longest side of the preview, in pixels.</summary>
    public const int MaxSide = 1600;

    /// <summary>The preview's size for a picture of this size: the same shape, longest side at most <paramref name="maxSide"/>, never enlarged.</summary>
    public static (int Width, int Height) FitSize(int width, int height, int maxSide = MaxSide)
    {
        if (width <= 0 || height <= 0) return (0, 0);
        if (Math.Max(width, height) <= maxSide) return (width, height);
        double scale = (double)maxSide / Math.Max(width, height);
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>What makes the preview stale: the file it was made from, when that file last changed, and its size.</summary>
    public static string Key(string path, long lastWriteTicks, long length) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{path}|{lastWriteTicks}|{length}");
}
