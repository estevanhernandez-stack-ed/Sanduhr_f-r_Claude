using System.Diagnostics;
using Microsoft.Toolkit.Uwp.Notifications;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;
using Windows.Services.Store;

namespace Sanduhr.App.Services;

/// <summary>
/// Tells the person when a Store update is waiting, and gets out of its way.
///
/// An MSIX cannot replace files the running process holds, so the Store's
/// install fails while the widget runs (0x80073D02) and silently leaves the old
/// build in place — 3.4.1, 2026-09-14. The Velopack channel restarts itself
/// after an update; this is the Store channel's equivalent: a toast with a
/// "Quit and update" button, plus a line in the widget's status slot.
///
/// Everything here is best-effort and guarded. <see cref="StoreContext"/> only
/// answers for a package with Store identity, so the whole service is inert on
/// the Velopack and dev builds, and any WinRT failure is logged and treated as
/// "no update waiting" — a missed notice is a nuisance, a crashed tick is a
/// broken widget. The cadence itself lives in <see cref="StoreUpdate"/>, away
/// from WinRT, so it can be tested.
/// </summary>
public sealed class StoreUpdateService
{
    private readonly WidgetViewModel _widget;
    private readonly Func<bool> _isPackaged;
    private readonly Action<string>? _log;
    private readonly Action _quit;

    private DateTimeOffset? _lastCheck;
    private DateTimeOffset? _lastNotified;
    private bool _checking;
    private bool _updatePending;

    /// <summary>True once the Store has told us an update is waiting. Read by
    /// the tray menu so "Quit and update" can surface there too.</summary>
    public bool UpdatePending => _updatePending;

    public StoreUpdateService(
        WidgetViewModel widget, Action quit, Func<bool>? isPackaged = null, Action<string>? log = null)
    {
        _widget = widget;
        _quit = quit;
        _isPackaged = isPackaged ?? (() => Startup.IsPackaged());
        _log = log;
        try
        {
            // The toast's button carries QuitAction; the body click does not, and
            // must not quit the app out from under someone who just tapped it.
            ToastNotificationManagerCompat.OnActivated += args =>
            {
                if (args.Argument?.Contains(StoreUpdate.QuitAction, StringComparison.Ordinal) == true)
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(QuitForUpdate);
            };
        }
        catch (Exception e)
        {
            Log("activation-hook", e);
        }
    }

    /// <summary>Called from the widget's 30-second tick on the UI thread. The
    /// Store query itself is async and deliberately not awaited: the tick must
    /// not stall on a network call, and the answer is acted on when it lands.</summary>
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
        bool pending = false;
        try
        {
            var context = StoreContext.GetDefault();
            var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
            pending = updates.Count > 0;
        }
        catch (Exception e)
        {
            // No Store identity, no network, a Store service hiccup: all the same
            // answer here. Never notify on a guess.
            Log("update-check", e);
        }
        finally
        {
            _checking = false;
        }

        _updatePending = pending;
        if (!pending)
            return;

        var now = DateTimeOffset.Now;
        if (!StoreUpdate.ShouldNotify(true, _lastNotified, now))
            return;
        _lastNotified = now;

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _widget.ShowTransientStatus(StoreUpdate.StatusNotice, TimeSpan.FromSeconds(30));
            ShowToast();
        });
    }

    private void ShowToast()
    {
        try
        {
            new ToastContentBuilder()
                .AddText(StoreUpdate.ToastTitle)
                .AddText(StoreUpdate.ToastBody)
                .AddButton(new ToastButton().SetContent("Quit and update").AddArgument("action", StoreUpdate.QuitAction))
                .AddButton(new ToastButtonDismiss("Later"))
                .Show();
        }
        catch (Exception e)
        {
            Log("toast", e);
        }
    }

    /// <summary>Open the Store's downloads page so the retry is one click away,
    /// then quit. Quitting is the part that matters: the Store cannot replace a
    /// file this process holds, and it retries on its own once nothing does.
    /// The app exits even if the deep link fails.</summary>
    public void QuitForUpdate()
    {
        try
        {
            Process.Start(new ProcessStartInfo(StoreUpdate.StoreUpdatesUri) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log("store-deep-link", e);
        }
        _quit();
    }

    private void Log(string stage, Exception e) => _log?.Invoke($"store update {stage} ({e.GetType().Name}: {e.Message})");
}
