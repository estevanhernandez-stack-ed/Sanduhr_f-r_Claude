using System.Diagnostics;
using System.IO;
using Sanduhr.Core;
using Velopack;
using Velopack.Sources;

namespace Sanduhr.App.Updates;

/// <summary>
/// Velopack-backed update probe for the GitHub release channel (the Setup.exe / delta install path, NOT
/// the Store MSIX, which updates through the Store). Best-effort and non-blocking: fired fire-and-forget
/// from <see cref="App.OnStartup"/>, debounced to once per 24h via a stamp file, and every failure is
/// swallowed (auto-update is comfort, not load-bearing). A Store/MSIX install and a dev build report
/// <c>UpdateManager.IsInstalled == false</c>, so the probe no-ops there.
/// <para>
/// When a newer release is out it is downloaded in the background and handed to Restart to update
/// (<see cref="Services.RestartToUpdateService"/>), the same offer the Store channel makes: nothing
/// installs until the person clicks, and then the WebView2 helpers close before
/// <c>ApplyUpdatesAndRestart</c>.
/// </para>
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
    /// Probe the GitHub release feed; when a newer version is out, download it and call
    /// <paramref name="onReady"/> with its version and installer (on a pool thread: marshal before
    /// touching UI). Never throws; never blocks the UI.
    /// </summary>
    public async Task CheckForUpdatesAsync(Action<string, IUpdateInstaller>? onReady = null)
    {
        if (!ShouldCheck())
            return;

        try
        {
            var source = new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: false);
            var manager = new UpdateManager(source);

            // IsInstalled is false during dev (running from bin/Debug) and for the Store/MSIX
            // install path: skip the network call in both cases.
            if (!manager.IsInstalled)
            {
                StampNow();
                return;
            }

            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
                return;
            Debug.WriteLine($"[Sanduhr] Update available: {update.TargetFullRelease.Version}");
            await manager.DownloadUpdatesAsync(update, null, CancellationToken.None).ConfigureAwait(false);
            var v = update.TargetFullRelease.Version;
            onReady?.Invoke(
                RestartToUpdate.DisplayVersion(v.Major, v.Minor, v.Patch),
                new VelopackUpdateInstaller(manager, update.TargetFullRelease));
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

/// <summary>
/// The Velopack channel's install: <c>ApplyUpdatesAndRestart</c> hands the downloaded release to
/// Update.exe, which waits for Sanduhr to exit, applies it and starts the new version. It exits this
/// process, so it does not return.
/// </summary>
public sealed class VelopackUpdateInstaller : IUpdateInstaller
{
    public const string ChannelName = "velopack";

    private readonly UpdateManager _manager;
    private readonly VelopackAsset _release;

    public VelopackUpdateInstaller(UpdateManager manager, VelopackAsset release)
    {
        _manager = manager;
        _release = release;
    }

    public string Channel => ChannelName;

    public Task<UpdateInstallResult> InstallAsync()
    {
        _manager.ApplyUpdatesAndRestart(_release);
        return Task.FromResult(UpdateInstallResult.Installing);
    }
}
