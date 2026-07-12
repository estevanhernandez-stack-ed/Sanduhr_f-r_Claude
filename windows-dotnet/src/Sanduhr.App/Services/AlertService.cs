using System.IO;
using System.Linq;
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
            "Weekly (test)",
            // Test alerts always chime (support tool) — the DND gate still applies.
            soundEnabled: true, snakeAtFull: false);

    private static void ShowToast(AlertEvent e, string tierLabel)
    {
        var (headline, body) = e.Kind switch
        {
            AlertKind.Full => ($"{tierLabel} at 100%",
                JoinBody("Limit reached.", ResetLine(e))),
            AlertKind.Urgent => ($"{tierLabel} at {e.UtilizationPct}%",
                JoinBody("Nearly out of headroom.", ResetLine(e))),
            AlertKind.Warn => ($"{tierLabel} at {e.UtilizationPct}%",
                JoinBody(ResetLine(e))),
            AlertKind.Projection => ($"{tierLabel} on pace to hit the cap",
                "Current burn rate exhausts this tier before it resets."),
            // Reset events carry the PREVIOUS window's peak in UtilizationPct
            // (the number that made the reset newsworthy) — render it.
            _ => ($"{tierLabel} reset",
                $"Fresh window after peaking at {e.UtilizationPct}%."),
        };

        var builder = new ToastContentBuilder().AddText(headline);
        if (body.Length > 0)
            builder.AddText(body);

        builder.Show(t =>
        {
            // Threshold alerts supersede each other per tier; tag so a newer
            // alert replaces a stale one instead of stacking.
            t.Tag = e.TierKey;
            t.Group = "sanduhr-alerts";
        });
    }

    /// <summary>Joins non-empty body fragments with a space — keeps a blank
    /// ResetLine from leaving an empty second toast line or a trailing space.</summary>
    private static string JoinBody(params string[] parts) =>
        string.Join(" ", parts.Where(p => p.Length > 0));

    private static string ResetLine(AlertEvent e)
    {
        var until = Pacing.TimeUntil(e.ResetsAt);
        return until is "--" or "now" ? "" : $"Resets in {until}.";
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
