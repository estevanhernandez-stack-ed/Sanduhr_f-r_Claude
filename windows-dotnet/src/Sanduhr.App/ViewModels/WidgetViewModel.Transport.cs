using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// The usage transport coming back after its browser process exits (issue #331), and the one action
/// notice in the status slot: "the connection stopped, Restart" or "Sanduhr 3.x is ready. Restart to
/// update?". The schedule is <see cref="TransportRecovery"/>; this is the wiring.
/// </summary>
public sealed partial class WidgetViewModel
{
    private readonly TransportRecovery _transport = new();
    private DispatcherTimer? _transportTimer;
    private WebView2ApiClient? _watchedClient;
    private string? _updateOfferText;

    /// <summary>The notice with a button under the status line, or empty for none.</summary>
    [ObservableProperty] private string _actionNotice = "";

    /// <summary>The notice's button text.</summary>
    [ObservableProperty] private string _actionNoticeButton = "";

    /// <summary>The usage connection would not come back: the person clicked Restart. App restarts Sanduhr.</summary>
    public event Action? RestartRequested;

    /// <summary>The person clicked Restart to update in the widget. App runs the update flow.</summary>
    public event Action? RestartToUpdateRequested;

    /// <summary>True once the transport would not come back after two rebuilds.</summary>
    public bool TransportStopped => _transport.OffersRestart;

    /// <summary>Shows or clears the update offer in the widget ("Sanduhr 3.8.1 is ready. Restart to update?").</summary>
    public void SetUpdateOffer(string? notice)
    {
        _updateOfferText = string.IsNullOrEmpty(notice) ? null : notice;
        UpdateActionNotice();
    }

    [RelayCommand]
    private void ActionNoticeClicked()
    {
        if (_transport.OffersRestart)
            RestartRequested?.Invoke();
        else if (_updateOfferText is not null)
            RestartToUpdateRequested?.Invoke();
    }

    /// <summary>The connection notice outranks the update offer: both end in a restart, and the
    /// connection one is why the numbers stopped.</summary>
    private void UpdateActionNotice()
    {
        if (_transport.OffersRestart)
        {
            ActionNotice = TransportRecovery.StoppedNotice;
            ActionNoticeButton = TransportRecovery.RestartButton;
        }
        else if (_updateOfferText is not null)
        {
            ActionNotice = _updateOfferText;
            ActionNoticeButton = RestartToUpdate.ButtonText;
        }
        else
        {
            ActionNotice = "";
            ActionNoticeButton = "";
        }
    }

    /// <summary>Follow the loss reports of the client <see cref="RebuildFetcher"/> just built; a new client starts clean.</summary>
    private void WatchTransport(IClaudeApiClient? client)
    {
        if (_watchedClient is not null)
            _watchedClient.TransportLost -= OnTransportLost;
        _watchedClient = client as WebView2ApiClient;
        if (_watchedClient is not null)
            _watchedClient.TransportLost += OnTransportLost;
        _transportTimer?.Stop();
        if (_transport.Lost)
        {
            _transport.Recovered();
            UpdateActionNotice();
        }
    }

    private void OnTransportLost(string kind)
    {
        if (ResetGuard.Done || IsDemo)
            return;
        var now = DateTimeOffset.Now;
        if (!_transport.BrowserLost(kind, now))
            return;
        LogTransport($"transport: lost ({kind}); rebuild in {TransportRecovery.DelayAfter(0).TotalSeconds:0}s");
        if (!_refreshing)
            StatusText = TransportRecovery.ReconnectingStatus;
        ScheduleTransportRebuild(now);
    }

    private void ScheduleTransportRebuild(DateTimeOffset now)
    {
        if (_transport.NextAttemptAt is not { } at)
            return;
        var wait = at - now;
        if (wait < TimeSpan.FromMilliseconds(250)) wait = TimeSpan.FromMilliseconds(250);
        _transportTimer?.Stop();
        _transportTimer = new DispatcherTimer { Interval = wait };
        _transportTimer.Tick += OnTransportTimer;
        _transportTimer.Start();
    }

    private async void OnTransportTimer(object? sender, EventArgs e)
    {
        (sender as DispatcherTimer)?.Stop();
        if (!ReferenceEquals(sender, _transportTimer) || !_transport.Lost || ResetGuard.Done || IsDemo)
            return;
        var client = _watchedClient;
        if (client is null || !ReferenceEquals(client, _client))
            return;
        if (_refreshing)
        {
            // A fetch is out (it may be the rebuild itself): look again shortly, without counting a failure.
            _transportTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _transportTimer.Tick += OnTransportTimer;
            _transportTimer.Start();
            return;
        }

        int attempt = _transport.FailedRebuilds + 1;
        LogTransport($"transport: rebuild attempt {attempt}");
        await RefreshCoreAsync().ConfigureAwait(true);
        if (!ReferenceEquals(client, _client))
            return; // the fetcher was rebuilt meanwhile: that client starts clean

        var now = DateTimeOffset.Now;
        if (client.BrowserAlive)
        {
            NoteTransportBack();
            return;
        }
        if (!_transport.Lost)
            return;
        _transport.RebuildFailed(now);
        LogTransport($"transport: rebuild attempt {attempt} failed; next in {TransportRecovery.DelayAfter(_transport.FailedRebuilds).TotalSeconds:0}s");
        // Once the restart notice shows, it carries the message: no "Reconnecting…" above it.
        if (_transport.OffersRestart && StatusText == TransportRecovery.ReconnectingStatus)
            StatusText = "";
        UpdateActionNotice();
        ScheduleTransportRebuild(now);
    }

    /// <summary>A fetch worked, so the browser is back: clear the loss and its notice.</summary>
    private void NoteTransportBack()
    {
        if (!_transport.Lost)
            return;
        _transportTimer?.Stop();
        _transport.Recovered();
        LogTransport("transport: rebuilt");
        UpdateActionNotice();
    }

    /// <summary>The status line while the transport is lost: the restart notice carries the message once offered.</summary>
    private string TransportLostStatus => _transport.OffersRestart ? "" : TransportRecovery.ReconnectingStatus;

    private static void LogTransport(string message)
    {
        try
        {
            var paths = new Paths();
            File.AppendAllText(Path.Combine(paths.AppDataDir, "fetch-debug.log"), $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // diagnostics never break the widget
        }
    }

    private void StopTransportRecovery()
    {
        _transportTimer?.Stop();
        if (_watchedClient is not null)
            _watchedClient.TransportLost -= OnTransportLost;
        _watchedClient = null;
    }

    /// <summary>
    /// Restart to update: close the active transport's browser now (the 3.7.2 teardown, before the
    /// installer runs). The client survives; its next fetch, once browsers may start again, rebuilds it.
    /// </summary>
    public void ReleaseTransport() => _watchedClient?.Release();
}
