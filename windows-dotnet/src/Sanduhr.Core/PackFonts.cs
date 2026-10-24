namespace Sanduhr.Core;

/// <summary>A folder of an installed font pack's font files that is safe to load from.</summary>
public sealed record PackFontFolder(string PackId, string PackName, string Dir);

/// <summary>
/// Where the Desk loads installed font packs from (3.9.1). Never the pack's own folder: WPF keeps a font file open once it has drawn a
/// glyph from it, and a folder with an open file can't be renamed, which is the first step of Uninstall. So each enabled font pack's
/// <c>.ttf</c> and <c>.otf</c> files are copied once into <c>widgets\.fonts\&lt;id&gt;-&lt;installed at&gt;\</c> and loaded from there.
/// A removed or replaced pack's copy is swept the next time this runs (a copy still open waits for the next start).
/// </summary>
public static class PackFonts
{
    public const string CacheName = ".fonts";

    /// <summary>The enabled font packs, each as its copy. A pack whose files can't be copied is left out.</summary>
    public static IReadOnlyList<PackFontFolder> Folders(WidgetStore widgets) => Folders(widgets, null);

    /// <param name="beforeMove">Test seam: runs with the target folder just before a finished copy is renamed into place.</param>
    internal static IReadOnlyList<PackFontFolder> Folders(WidgetStore widgets, Action<string>? beforeMove)
    {
        string cache = Path.Combine(widgets.Root, CacheName);
        var folders = new List<PackFontFolder>();
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in widgets.InstalledOfKind("font"))
        {
            string name = $"{pack.Row.Id}-{pack.Row.InstalledAt.ToUnixTimeSeconds()}";
            keep.Add(name);
            string dir = Path.Combine(cache, name);
            try
            {
                if (!Directory.Exists(dir) && !Copy(pack.Dir, cache, dir, beforeMove)) continue;
                folders.Add(new PackFontFolder(pack.Row.Id, pack.Info.Name, dir));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // this pack's font stays out of the lists; the rest still load
            }
        }
        Sweep(cache, keep);
        return folders;
    }

    /// <summary>Copies the font files into a scratch folder and renames it into place, so a half copy is never read. False when the
    /// pack has no font file.</summary>
    private static bool Copy(string packDir, string cache, string dir, Action<string>? beforeMove)
    {
        var fonts = Directory.EnumerateFiles(packDir, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".ttf" or ".otf")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        if (fonts.Count == 0) return false;
        Directory.CreateDirectory(cache);
        string scratch = dir + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            Directory.CreateDirectory(scratch);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var font in fonts)
            {
                string file = Path.GetFileName(font);
                if (!used.Add(file)) file = $"{used.Count}-{file}";
                File.Copy(font, Path.Combine(scratch, file));
            }
            beforeMove?.Invoke(dir);
            try
            {
                Directory.Move(scratch, dir);
            }
            catch (IOException) when (Directory.Exists(dir))
            {
                // Another caller finished the same copy first (the Desk and Settings can ask at once): use theirs.
                TryDelete(scratch);
            }
            return true;
        }
        catch
        {
            TryDelete(scratch);
            throw;
        }
    }

    private static void Sweep(string cache, HashSet<string> keep)
    {
        if (!Directory.Exists(cache)) return;
        try
        {
            foreach (var old in Directory.EnumerateDirectories(cache).Where(d => !keep.Contains(Path.GetFileName(d))))
            {
                // a scratch folder a copy is still filling is left alone; one a crash left behind goes after a minute
                if (old.Contains(".tmp-") && DateTime.UtcNow - Directory.GetLastWriteTimeUtc(old) < TimeSpan.FromMinutes(1)) continue;
                TryDelete(old);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a sweep that can't list the folder tries again next time
        }
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
