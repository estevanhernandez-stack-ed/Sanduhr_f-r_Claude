using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Toolkit.Uwp.Notifications;
using Sanduhr.App.Interop;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// The Arm build notice, on screen (5.0 spec 7). <see cref="ArmBuildNotice"/> decides; this reads the machine,
/// shows the one notification, remembers that it did, and opens the release list when its button is clicked.
/// The log line is written when the notification is due, before it is shown, so the Arm check workflow can see
/// the decision on a runner that cannot show a notification. Nothing here throws: a notice is never worth a
/// failed launch.
/// </summary>
internal sealed class ArmBuildNoticeService
{
    private readonly bool _applies;
    private readonly string _url;
    private readonly Func<bool> _alreadyShown;
    private readonly Action _markShown;
    private readonly Func<bool> _showToast;
    private readonly Action<string> _openLink;
    private readonly Action<string>? _log;

    public ArmBuildNoticeService(bool applies, string url, Func<bool> alreadyShown, Action markShown,
        Func<bool>? showToast = null, Action<string>? openLink = null, Action<string>? log = null)
    {
        _applies = applies;
        _url = url;
        _alreadyShown = alreadyShown;
        _markShown = markShown;
        _showToast = showToast ?? ShowToast;
        _openLink = openLink ?? OpenInBrowser;
        _log = log;
    }

    /// <summary>True when this copy is an x64 build on an Arm PC and is not the Store package.</summary>
    public static bool AppliesHere()
        => ArmBuildNotice.Applies(RuntimeInformation.ProcessArchitecture, MachineNative.Native(), Startup.IsPackaged());

    private static int Major() => System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.Major ?? 0;

    /// <summary>The release list to get the Arm build from, or null when the notice does not apply here.</summary>
    public static string? UrlIfApplies() => AppliesHere() ? ArmBuildNotice.ReleasesUrl(Major()) : null;

    public static ArmBuildNoticeService ForThisApp(SettingsStore store, Action<string>? log)
        => new(AppliesHere(), ArmBuildNotice.ReleasesUrl(Major()), store.LoadArmBuildNoticeShown, store.SaveArmBuildNoticeShown, log: log);

    /// <summary>Hooks the notification's button. Does nothing where the notice does not apply.</summary>
    public void Start()
    {
        if (!_applies)
            return;
        try { Toasts.OnActivated(OnToast); }
        catch (Exception e) { Log($"activation-hook ({e.GetType().Name})"); }
    }

    /// <summary>A clicked notification's argument: only this notice's button opens the link.</summary>
    internal void OnToast(string argument)
    {
        if (argument.Contains(ArmBuildNotice.GetAction, StringComparison.Ordinal))
            _openLink(_url);
    }

    /// <summary>Shows the notification when it is due. True when it was handed to Windows.</summary>
    public bool ShowIfDue()
    {
        bool shown;
        try { shown = _alreadyShown(); }
        catch (Exception e)
        {
            Log($"read ({e.GetType().Name})");
            return false;
        }
        if (!ArmBuildNotice.ToastDue(_applies, shown))
            return false;
        _log?.Invoke(ArmBuildNotice.LogDue);
        bool taken;
        try { taken = _showToast(); }
        catch (Exception e)
        {
            // Not marked: the next launch tries again.
            Log($"toast ({e.GetType().Name})");
            return false;
        }
        if (!taken)
        {
            // Windows dropped it without throwing (notifications off): not marked, the next launch tries again.
            Log("toast (refused)");
            return false;
        }
        try { _markShown(); }
        catch (Exception e) { Log($"flag ({e.GetType().Name})"); }
        return true;
    }

    /// <summary>True when Windows took the notification, false when the user's notification settings refuse it.</summary>
    private static bool ShowToast()
    {
        if (NotificationsRefused())
            return false;
        Toasts.Show(ArmBuildNotice.Title, ArmBuildNotice.Body, more: b => b
            .AddButton(new ToastButton().SetContent(ArmBuildNotice.GetText).AddArgument("action", ArmBuildNotice.GetAction))
            .AddButton(new ToastButtonDismiss(ArmBuildNotice.LaterText)));
        return true;
    }

    private static bool NotificationsRefused()
    {
        try { return ToastNotificationManagerCompat.CreateToastNotifier().Setting != Windows.UI.Notifications.NotificationSetting.Enabled; }
        catch (Exception) { return false; }   // an unreadable setting is unknown, not refused: still try to show
    }

    private static void OpenInBrowser(string url)
    {
        if (!url.StartsWith("https://", StringComparison.Ordinal))
            return;
        try
        {
            // network: your-browser
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception)
        {
            // The link is also in Settings, About, Updates.
        }
    }

    private void Log(string message) => _log?.Invoke("arm build notice: " + message);
}
