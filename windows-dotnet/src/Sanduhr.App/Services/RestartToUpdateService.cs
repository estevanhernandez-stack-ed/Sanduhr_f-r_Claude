using Microsoft.Toolkit.Uwp.Notifications;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// Restart to update, on screen (3.8 spec 8b). One offer for both channels: the Store
/// (<see cref="StoreUpdateService"/>) and Velopack (<see cref="Updates.UpdateChecker"/>) hand in the waiting
/// version and their installer; this shows "Sanduhr 3.x is ready. Restart to update?" as a toast, in the
/// tray and in the widget, and runs <see cref="RestartToUpdate"/> when the person clicks. Nothing happens
/// without a click.
/// </summary>
public sealed class RestartToUpdateService
{
    private readonly WidgetViewModel _widget;
    private readonly Action<string>? _log;
    private DateTimeOffset? _lastNotified;

    public RestartToUpdateService(WidgetViewModel widget, IRestartHost host, Action<string>? log = null)
    {
        _widget = widget;
        _log = log;
        Flow = new RestartToUpdate(host);
        try
        {
            // The toast's button carries the action; the body click does not, and only shows the widget.
            // A 3.4 to 3.7 "Quit and update" toast still in the notification centre lands here too.
            ToastNotificationManagerCompat.OnActivated += args =>
            {
                var argument = args.Argument ?? "";
                if (argument.Contains(RestartToUpdate.RestartAction, StringComparison.Ordinal)
                    || argument.Contains(StoreUpdate.LegacyQuitAction, StringComparison.Ordinal))
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(Accept);
            };
        }
        catch (Exception e)
        {
            Log($"activation-hook ({e.GetType().Name})");
        }
    }

    /// <summary>The decision logic.</summary>
    public RestartToUpdate Flow { get; }

    /// <summary>True while an update is on offer (the tray item shows).</summary>
    public bool Offered => Flow.Offered;

    /// <summary>The offer changed: the tray refreshes its item.</summary>
    public event Action? OfferChanged;

    /// <summary>An update is waiting. UI thread. Toasts once per version, then at most every 12 hours.</summary>
    public void Offer(string version, IUpdateInstaller installer)
    {
        bool isNew = Flow.Offer(version, installer);
        _widget.SetUpdateOffer(Flow.Notice);
        OfferChanged?.Invoke();
        var now = DateTimeOffset.Now;
        if (!isNew && !StoreUpdate.ShouldNotify(true, _lastNotified, now))
            return;
        _lastNotified = now;
        Log($"offer {installer.Channel} {version}");
        ShowToast(version);
    }

    /// <summary>The channel no longer reports its update.</summary>
    public void Withdraw(string channel)
    {
        if (Flow.Channel != channel)
            return;
        Flow.Withdraw();
        _widget.SetUpdateOffer(Flow.Notice);
        OfferChanged?.Invoke();
    }

    /// <summary>The person clicked Restart to update (toast, tray or widget). UI thread.</summary>
    public async void Accept()
    {
        if (!Flow.Offered || Flow.Busy)
            return;
        RestartOutcome outcome;
        try { outcome = await Flow.AcceptAsync().ConfigureAwait(true); }
        catch (Exception e)
        {
            Log($"accept ({e.GetType().Name})");
            outcome = RestartOutcome.StillRunning;
        }
        if (outcome != RestartOutcome.StillRunning)
            return;
        _widget.SetUpdateOffer(Flow.Notice);
        OfferChanged?.Invoke();
        if (Flow.Offered)
            _widget.ShowTransientStatus(RestartToUpdate.NotInstalledStatus, TimeSpan.FromSeconds(30));
    }

    private void ShowToast(string version)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(RestartToUpdate.ReadyText(version))
                .AddText(RestartToUpdate.ToastBody)
                .AddButton(new ToastButton().SetContent(RestartToUpdate.ButtonText).AddArgument("action", RestartToUpdate.RestartAction))
                .AddButton(new ToastButtonDismiss(RestartToUpdate.LaterText))
                .Show();
        }
        catch (Exception e)
        {
            Log($"toast ({e.GetType().Name})");
        }
    }

    private void Log(string message) => _log?.Invoke("restart to update: " + message);
}
