namespace Sanduhr.Core;

/// <summary>
/// The "may this background answer be written" rule shared by the refresh path, the seat meters and
/// account follow. After Reset Sanduhr… (or a stop, or a cancelled token) an answer that arrives from an
/// await must write nothing: no seat file, no history, no snapshot. Pure, so the rule is tested without
/// the WPF timers that call it.
/// </summary>
public static class WritePolicy
{
    /// <summary>True only when the owner is still running, Reset has not run and the work was not cancelled.</summary>
    public static bool MayWrite(bool stopped, bool resetDone, bool cancelled) => !(stopped || resetDone || cancelled);
}
