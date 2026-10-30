namespace Sanduhr.Core;

/// <summary>How an install request ended.</summary>
public enum UpdateInstallResult
{
    /// <summary>The installer took over and is closing Sanduhr (Velopack's restart, or the Store mid-install).</summary>
    Installing,
    /// <summary>The Store says the update is installed and Sanduhr is somehow still running.</summary>
    Completed,
    /// <summary>The person said no in Windows' own dialog.</summary>
    Canceled,
    /// <summary>The installer refused or threw.</summary>
    Failed,
    /// <summary>Nothing to install any more.</summary>
    NotAvailable,
}

/// <summary>One update channel's install step: the Store or Velopack. Implementations live in the app (WinRT, Velopack).</summary>
public interface IUpdateInstaller
{
    /// <summary>For the log: <c>store</c> or <c>velopack</c>.</summary>
    string Channel { get; }

    /// <summary>Install the waiting update. May never return when the installer closes the process.</summary>
    Task<UpdateInstallResult> InstallAsync();
}

/// <summary>What the restart needs from the running app. The app implements it; tests fake it.</summary>
public interface IRestartHost
{
    /// <summary>The 3.7.2 teardown: close every WebView2 controller and wait, bounded, for the browsers to exit.
    /// No new browser starts until <see cref="ResumeBrowsers"/>.</summary>
    Task ReleaseBrowsersAsync();

    /// <summary>The update did not happen: browsers may start again, and the widget fetches.</summary>
    void ResumeBrowsers();

    /// <summary>Stop answering second launches, so the relaunch waits for this process and takes over.</summary>
    void HoldInstance();

    /// <summary>Answer second launches again.</summary>
    void ResumeInstance();

    /// <summary>RegisterApplicationRestart: Windows relaunches Sanduhr after the update closes it. False when refused.</summary>
    bool RegisterRestart();

    /// <summary>UnregisterApplicationRestart.</summary>
    void UnregisterRestart();

    /// <summary>Start a new Sanduhr and quit this one (the update is in, but nothing closed this process).</summary>
    void RelaunchAndQuit();

    /// <summary>Operation names and results only.</summary>
    void Log(string message);
}

/// <summary>What <see cref="RestartToUpdate.AcceptAsync"/> did.</summary>
public enum RestartOutcome
{
    /// <summary>No offer, or one already running: nothing happened.</summary>
    Ignored,
    /// <summary>The installer has it; Sanduhr is closing.</summary>
    Restarting,
    /// <summary>The update did not install; Sanduhr keeps running and keeps the offer.</summary>
    StillRunning,
}

/// <summary>
/// Restart to update (3.8 spec 8b, item J). An update is waiting: say "Sanduhr 3.x is ready. Restart to
/// update?" and do nothing until the person clicks. On yes, close the WebView2 helpers first (an orphaned
/// one keeps the package in use and the install fails with 0x80073D02), register for restart, stop
/// answering second launches so the relaunch can take over, then hand off to the channel's installer.
/// When the install fails or is cancelled, undo all of that and keep the offer.
/// <para>
/// The order and the undo live here, away from WinRT and Velopack, so they can be tested.
/// </para>
/// </summary>
public sealed class RestartToUpdate
{
    private readonly IRestartHost _host;
    private IUpdateInstaller? _installer;

    public RestartToUpdate(IRestartHost host) => _host = host;

    /// <summary>How long a process the installer said it would close may linger before the restart counts
    /// as failed. Two minutes covers a Store deploy; tests pass their own wait.</summary>
    public Func<Task> WaitForInstallerAsync { get; init; } = () => Task.Delay(TimeSpan.FromMinutes(2));

    /// <summary>The waiting version, as the notice shows it, or null with no offer.</summary>
    public string? Version { get; private set; }

    /// <summary>True while an update is on offer.</summary>
    public bool Offered => _installer is not null;

    /// <summary>The offering channel, or null with no offer.</summary>
    public string? Channel => _installer?.Channel;

    /// <summary>True while a restart runs: a second click does nothing.</summary>
    public bool Busy { get; private set; }

    /// <summary>The notice, or null with no offer.</summary>
    public string? Notice => Version is null ? null : ReadyText(Version);

    /// <summary>Puts an update on offer. Returns true when it is new (another version, or none before).</summary>
    public bool Offer(string version, IUpdateInstaller installer)
    {
        bool isNew = Version != version || _installer is null;
        Version = version;
        _installer = installer;
        return isNew;
    }

    /// <summary>The update went away (installed elsewhere, or the channel no longer reports it).</summary>
    public void Withdraw()
    {
        if (Busy) return;
        Version = null;
        _installer = null;
    }

    /// <summary>The person said yes. Never throws.</summary>
    public async Task<RestartOutcome> AcceptAsync()
    {
        if (_installer is not { } installer || Busy)
            return RestartOutcome.Ignored;
        Busy = true;
        bool registered = false;
        bool held = false;
        UpdateInstallResult result;
        try
        {
            _host.Log($"restart to update: {installer.Channel} {Version}, closing browsers");
            await _host.ReleaseBrowsersAsync().ConfigureAwait(true);
            _host.HoldInstance();
            held = true;
            registered = _host.RegisterRestart();
            if (!registered)
                _host.Log("restart to update: restart registration refused (relaunch by hand)");
            result = await installer.InstallAsync().ConfigureAwait(true);
        }
        catch (Exception e)
        {
            _host.Log($"restart to update: {e.GetType().Name}");
            result = UpdateInstallResult.Failed;
        }

        _host.Log($"restart to update: {installer.Channel} result {result}");
        switch (result)
        {
            case UpdateInstallResult.Installing:
                // The installer is closing this process; Windows (Store) or Update.exe (Velopack) relaunches
                // it. Should this process still be here after the grace, the handoff failed: undo, so the
                // widget does not sit with its browsers held closed.
                try { await WaitForInstallerAsync().ConfigureAwait(true); }
                catch (Exception e) { _host.Log($"restart to update: wait {e.GetType().Name}"); }
                _host.Log("restart to update: still running after the installer took over");
                Undo(registered, held);
                Busy = false;
                return RestartOutcome.StillRunning;
            case UpdateInstallResult.Completed:
                // Installed, and Windows did not close us: start the new version ourselves.
                try { _host.RelaunchAndQuit(); }
                catch (Exception e) { _host.Log($"restart to update: relaunch {e.GetType().Name}"); }
                return RestartOutcome.Restarting;
            case UpdateInstallResult.NotAvailable:
                Undo(registered, held);
                Version = null;
                _installer = null;
                Busy = false;
                return RestartOutcome.StillRunning;
            default:
                Undo(registered, held);
                Busy = false;
                return RestartOutcome.StillRunning;
        }
    }

    private void Undo(bool registered, bool held)
    {
        if (registered)
            Try(_host.UnregisterRestart);
        if (held)
            Try(_host.ResumeInstance);
        Try(_host.ResumeBrowsers);
    }

    private void Try(Action step)
    {
        try { step(); }
        catch (Exception e) { _host.Log($"restart to update: undo {e.GetType().Name}"); }
    }

    // -- copy -------------------------------------------------------------------

    /// <summary>The toast, tray and status text for a waiting version.</summary>
    public static string ReadyText(string version) =>
        version == AnyUpdate ? "A Sanduhr update is ready. Restart to update?" : $"Sanduhr {version} is ready. Restart to update?";

    /// <summary>The offer's version when the channel cannot say which version is coming (the Store).</summary>
    public const string AnyUpdate = "update";

    /// <summary>The button, everywhere (toast, tray, widget), on both channels.</summary>
    public const string ButtonText = "Restart to update";

    /// <summary>The toast's second line: what the click does.</summary>
    public const string ToastBody = "Sanduhr closes, the update installs, and Sanduhr opens again. Nothing happens until you click.";

    /// <summary>The toast's second button.</summary>
    public const string LaterText = "Later";

    /// <summary>The status line after a failed or cancelled install.</summary>
    public const string NotInstalledStatus = "The update didn't install. Sanduhr keeps running; Restart to update is in the tray.";

    /// <summary>Toast button argument; the activation handler matches on it.</summary>
    public const string RestartAction = "sanduhr-restart-to-update";

    /// <summary>
    /// A package or release version as the notice shows it: Major.Minor.Build, with the Store's fourth
    /// component dropped when it is zero (3.8.1.0 reads 3.8.1).
    /// </summary>
    public static string DisplayVersion(int major, int minor, int build, int revision = 0)
        => revision == 0 ? $"{major}.{minor}.{build}" : $"{major}.{minor}.{build}.{revision}";
}
