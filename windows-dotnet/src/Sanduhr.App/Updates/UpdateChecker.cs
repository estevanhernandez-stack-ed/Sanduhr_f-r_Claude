using System.Diagnostics;
using System.IO;
using Velopack;
using Velopack.Sources;

namespace Sanduhr.App.Updates;

/// <summary>
/// Velopack-backed auto-update probe for the GitHub release channel (the Setup.exe / delta
/// install path — NOT the Store MSIX, which updates through the Store). Best-effort and
/// non-blocking: fired fire-and-forget from <see cref="App.OnStartup"/>, debounced to once
/// per 24h via a stamp file, and every failure is swallowed (auto-update is comfort, not
/// load-bearing). A Store/MSIX install reports <c>UpdateManager.IsInstalled == false</c>
/// (it wasn't installed by Velopack) so the probe no-ops there too.
/// </summary>
public sealed class UpdateChecker
{
    private const string GitHubRepoUrl = "https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude";
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromHours(24);

    private static readonly string DebounceStampPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Sanduhr",
        "last-update-check.txt");

    /// <summary>
    /// Probe the GitHub release feed for a newer version. Never throws; never blocks the UI.
    /// Today this only logs availability — there is no in-app "Update available" surface yet
    /// (Velopack applies updates on next launch via the Setup.exe channel).
    /// </summary>
    public async Task CheckForUpdatesAsync()
    {
        if (!ShouldCheck())
            return;

        try
        {
            var source = new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: false);
            var manager = new UpdateManager(source);

            // IsInstalled is false during dev (running from bin/Debug) and for the Store/MSIX
            // install path — skip the network call in both cases.
            if (!manager.IsInstalled)
            {
                StampNow();
                return;
            }

            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is not null)
                Debug.WriteLine($"[Sanduhr] Update available: {update.TargetFullRelease.Version}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Sanduhr] Update check threw (treating as no-update): {ex.Message}");
        }
        finally
        {
            StampNow();
        }
    }

    private static bool ShouldCheck()
    {
        try
        {
            if (!File.Exists(DebounceStampPath))
                return true;
            var contents = File.ReadAllText(DebounceStampPath).Trim();
            if (!DateTimeOffset.TryParse(contents, out var lastChecked))
                return true;
            return DateTimeOffset.UtcNow - lastChecked >= DebounceWindow;
        }
        catch
        {
            return true;
        }
    }

    private static void StampNow()
    {
        try
        {
            var dir = Path.GetDirectoryName(DebounceStampPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(DebounceStampPath, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch
        {
            // Best-effort; a failed stamp just means we check again sooner. Not load-bearing.
        }
    }
}
