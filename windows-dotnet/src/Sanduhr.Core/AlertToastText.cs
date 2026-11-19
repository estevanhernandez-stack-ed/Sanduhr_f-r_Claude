namespace Sanduhr.Core;

/// <summary>The words a limit alert's toast shows. Moved here from the app's AlertService so they are pinned by a test.</summary>
public static class AlertToastText
{
    public static (string Headline, string Body) For(AlertEvent e, string tierLabel) => e.Kind switch
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

    /// <summary>Joins non-empty body fragments with a space — keeps a blank
    /// ResetLine from leaving an empty second toast line or a trailing space.</summary>
    private static string JoinBody(params string[] parts) =>
        string.Join(" ", parts.Where(p => p.Length > 0));

    private static string ResetLine(AlertEvent e)
    {
        var until = Pacing.TimeUntil(e.ResetsAt);
        return until is "--" or "now" ? "" : $"Resets in {until}.";
    }
}
