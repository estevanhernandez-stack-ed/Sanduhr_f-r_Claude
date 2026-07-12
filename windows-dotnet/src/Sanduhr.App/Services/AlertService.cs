using System.IO;
using Microsoft.Toolkit.Uwp.Notifications;
using Sanduhr.Core;
using Windows.UI.Notifications;

namespace Sanduhr.App.Services;

/// <summary>
/// WS-B alert delivery: Windows toast + optional procedural chime. Toasts use
/// ToastNotificationManagerCompat, which resolves identity automatically for
/// the MSIX channel and self-registers an AUMID for the unpackaged/Velopack
/// channel. The chime is additionally gated on SHQueryUserNotificationState
/// (Windows defers the toast during Focus Assist, but nothing gates app-played
/// audio for us). Every path is best-effort: alert delivery must never break
/// the fetch loop, and failures log without labels or payload text (WS-A
/// logging convention).
/// </summary>
public sealed class AlertService
{
    private readonly Paths _paths;

    public AlertService(Paths paths, Action activateWidget)
    {
        _paths = paths;
        try
        {
            // Toast body clicks re-activate the app; bring the widget forward.
            ToastNotificationManagerCompat.OnActivated += _ =>
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(activateWidget);
        }
        catch (Exception e)
        {
            LogQuiet("activation-hook", e);
        }
    }

    public void Deliver(AlertEvent e, string tierLabel, bool soundEnabled, bool snakeAtFull)
    {
        try
        {
            ShowToast(e, tierLabel);
        }
        catch (Exception ex)
        {
            LogQuiet("toast", ex);
        }

        if (!soundEnabled || !UserAcceptsSound())
            return;
        try
        {
            switch (e.Kind)
            {
                case AlertKind.Full when snakeAtFull:
                    Sounds.PlayAlertSnake();
                    break;
                case AlertKind.Full:
                case AlertKind.Urgent:
                    Sounds.PlayAlertUrgent();
                    break;
                default:
                    Sounds.PlayAlertWarn();
                    break;
            }
        }
        catch (Exception ex)
        {
            LogQuiet("chime", ex);
        }
    }

    /// <summary>The Alerts tab's "Send test alert": a fake Warn through the real pipeline.</summary>
    public void DeliverTest()
        => Deliver(
            new AlertEvent(AlertKind.Warn, "seven_day", 80, null),
            "Weekly (test)", soundEnabled: true, snakeAtFull: false);

    private static void ShowToast(AlertEvent e, string tierLabel)
    {
        var (headline, body) = e.Kind switch
        {
            AlertKind.Full => ($"{tierLabel} at 100%",
                "Limit reached. " + ResetLine(e)),
            AlertKind.Urgent => ($"{tierLabel} at {e.UtilizationPct}%",
                "Nearly out of headroom. " + ResetLine(e)),
            AlertKind.Warn => ($"{tierLabel} at {e.UtilizationPct}%",
                ResetLine(e)),
            AlertKind.Projection => ($"{tierLabel} on pace to hit the cap",
                "Current burn rate exhausts this tier before it resets."),
            _ => ($"{tierLabel} reset",
                "Fresh window — the tank is full."),
        };

        new ToastContentBuilder()
            .AddText(headline)
            .AddText(body)
            .Show(t =>
            {
                // Threshold alerts supersede each other per tier; tag so a newer
                // alert replaces a stale one instead of stacking.
                t.Tag = e.TierKey;
                t.Group = "sanduhr-alerts";
            });
    }

    private static string ResetLine(AlertEvent e)
    {
        var until = Pacing.TimeUntil(e.ResetsAt);
        return until is "--" ? "" : $"Resets in {until}.";
    }

    /// <summary>Chime only when Windows says the user accepts notifications —
    /// busy/fullscreen/quiet-hours states stay silent. Unknown/failed reads
    /// default to allowing the chime (the toast is already deferred by the OS).</summary>
    private static bool UserAcceptsSound()
    {
        try
        {
            var hr = Windows.Win32.PInvoke.SHQueryUserNotificationState(out var state);
            if (hr.Failed)
                return true;
            return state == Windows.Win32.UI.Shell.QUERY_USER_NOTIFICATION_STATE.QUNS_ACCEPTS_NOTIFICATIONS;
        }
        catch
        {
            return true;
        }
    }

    private void LogQuiet(string operation, Exception e)
    {
        try
        {
            File.AppendAllText(_paths.LogFile,
                $"{DateTime.UtcNow:o} alert {operation} failed ({e.GetType().Name}){Environment.NewLine}");
        }
        catch
        {
            // Logging must never break alert delivery.
        }
    }
}
