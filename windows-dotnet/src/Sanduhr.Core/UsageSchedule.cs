namespace Sanduhr.Core;

/// <summary>
/// How often the widget asks claude.ai for the account in use (its refresh timer, and the spacing of the per-seat rounds), and how soon it
/// asks again after a transient failure. Settings, Privacy, What leaves this PC quotes both, and the widget reads these same values, so the
/// words and the timers can't drift (Settings v2, slice 6).
/// </summary>
public static class UsageSchedule
{
    public static readonly TimeSpan Refresh = TimeSpan.FromMinutes(5);

    /// <summary>The wait before the one retry of a transient failed ask (<c>RetryOnceIfTransientAsync</c>).</summary>
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(5);
}
