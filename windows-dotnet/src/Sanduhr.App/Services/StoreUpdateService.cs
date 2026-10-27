using Sanduhr.Core;
using Windows.Services.Store;

namespace Sanduhr.App.Services;

/// <summary>
/// Notices a waiting Store update and offers Restart to update (3.8 spec 8b).
///
/// An MSIX cannot replace files the running process holds, so the Store's install fails while the
/// widget runs (0x80073D02) and silently leaves the old build in place: 3.4.1, 2026-09-14. 3.4 to 3.7
/// offered "Quit and update", which left the install to the Store whenever it got round to it and
/// could orphan a WebView2 helper that kept the package in use. 3.8 asks first, then closes its
/// WebView2 helpers, registers for restart and asks the Store to install now
/// (<see cref="StoreUpdateInstaller"/>), through <see cref="RestartToUpdateService"/>.
///
/// Everything here is best-effort and guarded. <see cref="StoreContext"/> only answers for a package
/// with Store identity, so the whole service is inert on the Velopack and dev builds, and any WinRT
/// failure is logged and treated as "no update waiting": a missed notice is a nuisance, a crashed tick
/// is a broken widget. The cadence lives in <see cref="StoreUpdate"/> and the restart's order in
/// <see cref="RestartToUpdate"/>, away from WinRT, so both can be tested.
/// </summary>
public sealed class StoreUpdateService
{
    private readonly RestartToUpdateService _offers;
    private readonly Func<bool> _isPackaged;
    private readonly Func<nint> _ownerWindow;
    private readonly Action<string>? _log;

    private DateTimeOffset? _lastCheck;
    private bool _checking;

    /// <summary>True once the Store has told us an update is waiting.</summary>
    public bool UpdatePending { get; private set; }

    public StoreUpdateService(
        RestartToUpdateService offers, Func<nint> ownerWindow, Func<bool>? isPackaged = null, Action<string>? log = null)
    {
        _offers = offers;
        _ownerWindow = ownerWindow;
        _isPackaged = isPackaged ?? (() => Startup.IsPackaged());
        _log = log;
    }

    /// <summary>The Store's answer: the waiting update's version, or null. A seam for tests, which never ask the real Store.</summary>
    internal Func<Task<string?>> Query { get; init; } = AskTheStoreAsync;

    private static async Task<string?> AskTheStoreAsync()
    {
        var context = StoreContext.GetDefault();
        // network: store-update
        var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
        return updates.Count > 0 ? VersionOf(updates[0]) : null;
    }

    /// <summary>Called from the widget's 30-second tick on the UI thread. The Store query itself is
    /// async and deliberately not awaited: the tick must not stall on a network call, and the answer
    /// is acted on when it lands (on the UI thread, which the await resumes on).</summary>
    public void Tick(DateTimeOffset now)
    {
        if (_checking || !StoreUpdate.ShouldCheck(_isPackaged(), _lastCheck, now))
            return;
        _lastCheck = now;
        _checking = true;
        _ = CheckAsync();
    }

    private async Task CheckAsync()
    {
        string? version = null;
        try
        {
            version = await Query();
        }
        catch (Exception e)
        {
            // No Store identity, no network, a Store service hiccup: all the same answer here.
            // Never offer on a guess.
            Log("update-check", e);
        }
        finally
        {
            _checking = false;
        }

        UpdatePending = version is not null;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (version is null)
                _offers.Withdraw(StoreUpdateInstaller.ChannelName);
            else
                _offers.Offer(version, new StoreUpdateInstaller(_ownerWindow, _log));
        });
    }

    private static string VersionOf(StorePackageUpdate update)
    {
        try
        {
            var v = update.Package.Id.Version;
            return RestartToUpdate.DisplayVersion(v.Major, v.Minor, v.Build, v.Revision);
        }
        catch
        {
            return "update"; // reads "Sanduhr update is ready", still true
        }
    }

    private void Log(string stage, Exception e) => _log?.Invoke($"store update {stage} ({e.GetType().Name}: {e.Message})");   // streamer: notname - written to the log file, never drawn
}

/// <summary>
/// The Store channel's install: <c>RequestDownloadAndInstallStorePackageUpdatesAsync</c>, which shows
/// Windows' own consent dialog, installs, and closes Sanduhr; Windows relaunches it from the restart
/// registration. Only the person's click in <see cref="RestartToUpdateService"/> gets here.
/// </summary>
public sealed class StoreUpdateInstaller : IUpdateInstaller
{
    public const string ChannelName = "store";

    private readonly Func<nint> _ownerWindow;
    private readonly Action<string>? _log;

    public StoreUpdateInstaller(Func<nint> ownerWindow, Action<string>? log = null)
    {
        _ownerWindow = ownerWindow;
        _log = log;
    }

    public string Channel => ChannelName;

    public async Task<UpdateInstallResult> InstallAsync()
    {
        var context = StoreContext.GetDefault();
        // A desktop app's StoreContext needs an owner window for the consent dialog.
        var hwnd = _ownerWindow();
        if (hwnd != 0)
        {
            try { WinRT.Interop.InitializeWithWindow.Initialize(context, hwnd); }
            catch (Exception e) { _log?.Invoke($"store update owner ({e.GetType().Name})"); }
        }
        // network: store-update
        var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
        if (updates.Count == 0)
            return UpdateInstallResult.NotAvailable;
        // network: store-update
        var result = await context.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
        return Map(result.OverallState);
    }

    /// <summary>The Store's answer as the flow reads it.</summary>
    public static UpdateInstallResult Map(StorePackageUpdateState state) => state switch
    {
        StorePackageUpdateState.Completed => UpdateInstallResult.Completed,
        StorePackageUpdateState.Canceled => UpdateInstallResult.Canceled,
        // Pending, Downloading, Deploying: the install is under way and Windows closes Sanduhr for it.
        StorePackageUpdateState.Pending or StorePackageUpdateState.Downloading or StorePackageUpdateState.Deploying
            => UpdateInstallResult.Installing,
        _ => UpdateInstallResult.Failed, // ErrorLowBattery, ErrorWiFiRecommended, ErrorWiFiRequired, OtherError
    };
}
