using System.Globalization;

namespace Sanduhr.Core;

/// <summary>Outcome of one <see cref="PackagedDataMigration.Run"/>.</summary>
/// <param name="ShortCircuited">The marker said done (or the attempt cap was hit); nothing ran.</param>
/// <param name="Completed">Every file was handled without a failure; the marker now says done.</param>
/// <param name="Copied">Files copied to the real path.</param>
/// <param name="UpToDate">Files left alone because the real copy was the same age or newer.</param>
/// <param name="Failed">Files (or whole WebView2 profiles counted once) that could not be copied.</param>
public readonly record struct MigrationResult(bool ShortCircuited, bool Completed, int Copied, int UpToDate, int Failed);

/// <summary>
/// One-shot move of a Store install's data out of the package's LocalCache.
/// <para>
/// Before 3.4.3 the Store (MSIX) build ran with AppData write virtualization on, so files it
/// created under <c>%APPDATA%\Sanduhr</c> and <c>%LOCALAPPDATA%\Sanduhr</c> were redirected to
/// <c>%LOCALAPPDATA%\Packages\&lt;PFN&gt;\LocalCache\{Roaming,Local}\Sanduhr</c>. From 3.4.3 the
/// manifest turns virtualization off and the app reads the real paths, so this copies each
/// cached file across where the real one is missing or older. The originals stay put as a
/// rollback.
/// </para>
/// <list type="bullet">
/// <item><c>bin\</c> and <c>mcp\</c> are skipped: the MCP and statusline installers regenerate
/// them, and a stale launcher pointing at a redirected exe is the bug being fixed.</item>
/// <item>WebView2 profiles (<c>webview2\</c>, <c>webview2-fetch\</c>) are copied whole, into a
/// staging folder renamed into place, and only when no real profile exists. Merging two
/// Chromium profiles file by file could pair a cookie database with the wrong key or journal,
/// so an existing real profile wins, and a profile that fails to copy is skipped with a log
/// line (the cost is one fresh sign-in or Cloudflare check).</item>
/// <item>A marker in the real <c>%APPDATA%\Sanduhr</c> makes it one-shot. A run with failures
/// records the attempt and retries next launch, up to <see cref="DefaultMaxAttempts"/>.</item>
/// <item><see cref="Run"/> never throws. Log lines carry counts, folder kinds and exception
/// type names only, never paths or project names (PRIVACY.md log contract).</item>
/// </list>
/// Writes land only in the two <c>Sanduhr</c> folders, so the Velopack install tree
/// (<c>%LOCALAPPDATA%\626Labs.Sanduhr</c>) is never touched.
/// </summary>
public sealed class PackagedDataMigration
{
    public const string MarkerFileName = ".packaged-migration";
    public const string StagingSuffix = ".migrating";
    public const int DefaultMaxAttempts = 3;

    private const string Done = "done";
    private const string AttemptPrefix = "attempt ";

    private static readonly string[] SkippedDirs = { "bin", "mcp" };
    private static readonly string[] ProfileDirs = { "webview2", "webview2-fetch" };

    private readonly string _localCacheRoot;
    private readonly string _roamingBase;
    private readonly string _localBase;
    private readonly Action<string> _log;
    private readonly int _maxAttempts;

    private int _copied;
    private int _upToDate;
    private int _failed;

    /// <param name="localCacheRoot">The package's <c>LocalCache</c> folder.</param>
    /// <param name="roamingAppDataBase">The real <c>%APPDATA%</c>.</param>
    /// <param name="localAppDataBase">The real <c>%LOCALAPPDATA%</c>.</param>
    /// <param name="log">Best-effort log sink; called with path-free lines.</param>
    /// <param name="maxAttempts">Runs with failures allowed before giving up.</param>
    public PackagedDataMigration(string localCacheRoot, string roamingAppDataBase, string localAppDataBase,
        Action<string>? log = null, int maxAttempts = DefaultMaxAttempts)
    {
        _localCacheRoot = localCacheRoot;
        _roamingBase = roamingAppDataBase;
        _localBase = localAppDataBase;
        _log = log ?? (_ => { });
        _maxAttempts = maxAttempts;
    }

    private string MarkerPath => Path.Combine(_roamingBase, "Sanduhr", MarkerFileName);

    public MigrationResult Run()
    {
        try
        {
            int attempts = ReadAttempts(out bool done);
            if (done || attempts >= _maxAttempts)
                return new MigrationResult(true, done, 0, 0, 0);

            MigrateTree(Path.Combine(_localCacheRoot, "Roaming", "Sanduhr"), Path.Combine(_roamingBase, "Sanduhr"), "roaming");
            MigrateTree(Path.Combine(_localCacheRoot, "Local", "Sanduhr"), Path.Combine(_localBase, "Sanduhr"), "local");

            bool completed = _failed == 0;
            WriteMarker(completed ? Done : AttemptPrefix + (attempts + 1).ToString(CultureInfo.InvariantCulture));
            Log($"done: copied {_copied}, up to date {_upToDate}, failed {_failed}"
                + (completed ? "" : $", will retry (attempt {attempts + 1} of {_maxAttempts})"));
            return new MigrationResult(false, completed, _copied, _upToDate, _failed);
        }
        catch (Exception ex)
        {
            Log($"aborted: {ex.GetType().Name}");
            return new MigrationResult(false, false, _copied, _upToDate, _failed + 1);
        }
    }

    private int ReadAttempts(out bool done)
    {
        done = false;
        try
        {
            if (!File.Exists(MarkerPath))
                return 0;
            var text = File.ReadAllText(MarkerPath).Trim();
            if (text == Done)
            {
                done = true;
                return 0;
            }
            return text.StartsWith(AttemptPrefix, StringComparison.Ordinal)
                && int.TryParse(text.AsSpan(AttemptPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? n
                : 0;
        }
        catch (Exception ex)
        {
            Log($"marker unreadable: {ex.GetType().Name}");
            return 0;
        }
    }

    private void WriteMarker(string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            File.WriteAllText(MarkerPath, text);
        }
        catch (Exception ex)
        {
            Log($"marker not written: {ex.GetType().Name}");
        }
    }

    private void MigrateTree(string source, string dest, string kind)
    {
        if (!Directory.Exists(source))
            return;
        try
        {
            Directory.CreateDirectory(dest);
        }
        catch (Exception ex)
        {
            _failed++;
            Log($"{kind}: real folder unavailable: {ex.GetType().Name}");
            return;
        }

        foreach (var dir in SafeEnumerate(() => Directory.EnumerateDirectories(source), kind))
        {
            var name = Path.GetFileName(dir);
            if (SkippedDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            if (ProfileDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
                CopyProfile(dir, Path.Combine(dest, name), name.ToLowerInvariant());
            else
                CopyFilesRecursive(dir, Path.Combine(dest, name), kind);
        }
        foreach (var file in SafeEnumerate(() => Directory.EnumerateFiles(source), kind))
        {
            if (string.Equals(Path.GetFileName(file), MarkerFileName, StringComparison.OrdinalIgnoreCase))
                continue;
            CopyIfNewer(file, Path.Combine(dest, Path.GetFileName(file)), kind);
        }
    }

    private void CopyFilesRecursive(string source, string dest, string kind)
    {
        foreach (var file in SafeEnumerate(() => Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories), kind))
            CopyIfNewer(file, Path.Combine(dest, Path.GetRelativePath(source, file)), kind);
    }

    private void CopyIfNewer(string src, string dst, string kind)
    {
        try
        {
            var srcTime = File.GetLastWriteTimeUtc(src);
            if (File.Exists(dst) && File.GetLastWriteTimeUtc(dst) >= srcTime)
            {
                _upToDate++;
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
            File.SetLastWriteTimeUtc(dst, srcTime);
            _copied++;
        }
        catch (Exception ex)
        {
            _failed++;
            Log($"{kind}: a file was not copied: {ex.GetType().Name}");
        }
    }

    private void CopyProfile(string source, string dest, string name)
    {
        if (Directory.Exists(dest))
        {
            Log($"{name}: skipped, a real profile already exists");
            return;
        }
        var staging = dest + StagingSuffix;
        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            int files = 0;
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(staging, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
                File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(file));
                files++;
            }
            Directory.Move(staging, dest);
            _copied += files;
        }
        catch (Exception ex)
        {
            // Not counted as a failure: a profile is a cache, and retrying a live or locked
            // profile on every launch would not get it unstuck.
            Log($"{name}: skipped, profile copy failed: {ex.GetType().Name}");
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
            catch (Exception cleanup)
            {
                Log($"{name}: staging folder left behind: {cleanup.GetType().Name}");
            }
        }
    }

    private IEnumerable<string> SafeEnumerate(Func<IEnumerable<string>> list, string kind)
    {
        try
        {
            return list().ToList();
        }
        catch (Exception ex)
        {
            _failed++;
            Log($"{kind}: folder not readable: {ex.GetType().Name}");
            return Array.Empty<string>();
        }
    }

    private void Log(string line)
    {
        try { _log("packaged-migration " + line); }
        catch { /* logging is best-effort and must never break startup */ }
    }
}
