using System.Diagnostics;
using System.IO;
using Sanduhr.App.Services;
using Sanduhr.Core;
using Velopack;
using Velopack.Sources;

namespace Sanduhr.App.Updates;

/// <summary>What one ask of the release feed found.</summary>
public enum UpdateProbeStatus
{
    /// <summary>No newer release.</summary>
    UpToDate,
    /// <summary>A newer release is downloaded and waits for Restart to update.</summary>
    Ready,
    /// <summary>This copy wasn't installed by Setup (a dev build, or the Store's MSIX), so it can't update itself; nothing was asked.</summary>
    NotInstalled,
    /// <summary>The ask failed (offline, GitHub refused, a download broke).</summary>
    Failed,
}

/// <summary>One ask's answer. <see cref="Version"/> and <see cref="Installer"/> are set when <see cref="Status"/> is Ready.</summary>
public sealed record UpdateProbeResult(UpdateProbeStatus Status, string? Version = null, IUpdateInstaller? Installer = null);

/// <summary>
/// The one thing that asks the network whether Sanduhr has a newer release (Settings v2, slice 6). <see cref="UpdateChecker"/> is its only
/// caller, so the switch that gates the checker gates every probe. Tests put a counting probe here.
/// </summary>
public interface IUpdateProbe
{
    Task<UpdateProbeResult> FindAsync();
}

/// <summary>
/// The GitHub release feed through Velopack, for the Setup.exe / delta install (not the Store MSIX, which the Store updates). A newer release
/// is downloaded in the background and handed back for Restart to update (<see cref="Services.RestartToUpdateService"/>): nothing installs until
/// the person clicks. Never throws.
/// </summary>
public sealed class VelopackUpdateProbe : IUpdateProbe
{
    private const string GitHubRepoUrl = "https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude";

    public async Task<UpdateProbeResult> FindAsync()
    {
        try
        {
            // network: github-update
            var source = new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: false);
            // network: github-update
            var manager = new UpdateManager(source);
            // IsInstalled is false during dev (bin\Debug) and for the Store/MSIX install: no network call in either.
            if (!manager.IsInstalled)
                return new UpdateProbeResult(UpdateProbeStatus.NotInstalled);
            // network: github-update
            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
                return new UpdateProbeResult(UpdateProbeStatus.UpToDate);
            Debug.WriteLine($"[Sanduhr] Update available: {update.TargetFullRelease.Version}");
            // network: github-update
            await manager.DownloadUpdatesAsync(update, null, CancellationToken.None).ConfigureAwait(false);
            var v = update.TargetFullRelease.Version;
            return new UpdateProbeResult(UpdateProbeStatus.Ready, RestartToUpdate.DisplayVersion(v.Major, v.Minor, v.Patch),
                new VelopackUpdateInstaller(manager, update.TargetFullRelease));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Sanduhr] Update check threw (treating as failed): {ex.Message}");   // streamer: notname - written to the log file, never drawn
            return new UpdateProbeResult(UpdateProbeStatus.Failed);
        }
    }
}

/// <summary>
/// When the GitHub build asks for a newer release (Settings v2, slice 6; WF6). Three ways in, one probe:
/// <list type="bullet">
/// <item><description><see cref="RunIfDueAsync"/>: launch. It asks only when "Check for updates once a day" (settings.json "update_check") is
/// on and <see cref="UpdateCheck.Interval"/> has passed since the stamp.</description></item>
/// <item><description><see cref="Tick"/>: the widget's 30-second tick, the same rule, so a widget left running still checks once a day.</description></item>
/// <item><description><see cref="CheckNowAsync"/>: About's "Check now". The person asked, so it asks whatever the switch says.</description></item>
/// </list>
/// With the switch off, the first two ask nothing and write nothing. A probe already running is shared, never doubled. A ready release goes to
/// <c>onReady</c> (on a pool thread: marshal before touching UI). Never throws; never blocks the UI.
/// </summary>
public sealed class UpdateChecker
{
    /// <summary>The stamp the 3.x builds wrote, kept so an upgrade doesn't ask again at once.</summary>
    public static readonly string DefaultStampPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sanduhr", "last-update-check.txt");   // streamer: notname - a file location used for IO, never drawn

    private readonly Func<bool> _enabled;
    private readonly IUpdateProbe _probe;
    private readonly string _stampPath;
    private readonly Action<string, IUpdateInstaller> _onReady;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private Task<UpdateProbeResult>? _inFlight;
    private DateTimeOffset? _last;
    private bool _lastRead;
    private DateTimeOffset? _enabledAt;
    private bool _enabledHeld;

    public UpdateChecker(Func<bool> enabled, IUpdateProbe probe, string stampPath, Action<string, IUpdateInstaller> onReady,
        Func<DateTimeOffset>? now = null)
    {
        _enabled = enabled;
        _probe = probe;
        _stampPath = stampPath;
        _onReady = onReady;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The probe running now, or the last one; null before the first. Tests await it.</summary>
    internal Task<UpdateProbeResult>? Running => _inFlight;

    /// <summary>Launch: one probe when the switch is on and a day has passed since the last; nothing at all when it is off.</summary>
    public async Task RunIfDueAsync()
    {
        if (!UpdateCheck.Due(_enabled(), LastChecked(), _now()))
            return;
        await Probe().ConfigureAwait(false);
    }

    /// <summary>How long the tick holds its read of the switch. A switch left off must not cost a settings.json read every 30 seconds.</summary>
    public static readonly TimeSpan EnabledRefresh = TimeSpan.FromMinutes(5);

    /// <summary>The widget's tick (UI thread): free until a day has passed (the stamp is held in memory), then the launch rule, switch included.
    /// The switch is read at most once every <see cref="EnabledRefresh"/>; a flip is seen by the next refresh.</summary>
    public void Tick()
    {
        if (ResetGuard.Done || _inFlight is { IsCompleted: false } || !UpdateCheck.Due(true, LastChecked(), _now()))
            return;
        // The held read keeps a switch left off from costing a read per tick; a held "on" is read again before asking, so
        // turning it off is honoured at once (turning it on may wait for the refresh).
        if (!HeldEnabled() || !_enabled())
            return;
        _ = Probe();
    }

    private bool HeldEnabled()
    {
        var now = _now();
        if (_enabledAt is not { } at || now < at || now - at >= EnabledRefresh)
        {
            _enabledHeld = _enabled();
            _enabledAt = now;
        }
        return _enabledHeld;
    }

    /// <summary>About, Updates, "Check now": the person asked, so it asks even with the switch off (decision 7). Shares a probe already running.</summary>
    public Task<UpdateProbeResult> CheckNowAsync() => Probe();

    private Task<UpdateProbeResult> Probe()
    {
        lock (_gate)
        {
            if (_inFlight is { IsCompleted: false } running)
                return running;
            return _inFlight = ProbeCore();
        }
    }

    private async Task<UpdateProbeResult> ProbeCore()
    {
        UpdateProbeResult result;
        try
        {
            result = await _probe.FindAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            result = new UpdateProbeResult(UpdateProbeStatus.Failed);
        }
        Stamp(_now());
        if (result is { Status: UpdateProbeStatus.Ready, Version: { } version, Installer: { } installer })
            _onReady(version, installer);
        return result;
    }

    private DateTimeOffset? LastChecked()
    {
        if (!_lastRead)
        {
            try { _last = File.Exists(_stampPath) ? UpdateCheck.ParseStamp(File.ReadAllText(_stampPath)) : null; }
            catch (Exception) { _last = null; }
            _lastRead = true;
        }
        return _last;
    }

    private void Stamp(DateTimeOffset at)
    {
        if (ResetGuard.Done)
            return;   // Reset deleted the stamp; a late probe must not bring it back
        _last = at;
        _lastRead = true;
        try
        {
            var dir = Path.GetDirectoryName(_stampPath);   // streamer: notname - a file location used for IO, never drawn
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_stampPath, UpdateCheck.FormatStamp(at));
        }
        catch (Exception)
        {
            // Best-effort; a failed stamp means the next launch asks again (the in-memory stamp still holds the tick off).
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
