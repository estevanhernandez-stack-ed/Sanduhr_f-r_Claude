namespace Sanduhr.Core;

/// <summary>
/// When to look for a waiting Store update, and when to say so.
///
/// An MSIX cannot replace files the running process holds: the Store reports a
/// failed install (0x80073D02) and the app keeps running the old build. Seen on
/// 3.4.1, 2026-09-14 — the install went straight through once the widget was
/// quit from the tray. The Velopack channel restarts itself; the Store channel
/// had nothing. 3.8 offers Restart to update when one waits (<see cref="RestartToUpdate"/>).
///
/// The policy lives here, away from WinRT, so the cadence is testable without a
/// Store, a package identity, or a clock.
/// </summary>
public static class StoreUpdate
{
    /// <summary>How often to ask the Store. The answer changes at most daily in
    /// practice, and the query is a network call on someone's metered laptop, so
    /// it rides the widget's 30-second tick but only acts this often.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    /// <summary>How long before the same waiting update is mentioned again. Long
    /// enough not to nag someone mid-session, short enough that a person who
    /// dismissed it yesterday hears about it today.</summary>
    public static readonly TimeSpan RenotifyInterval = TimeSpan.FromHours(12);

    /// <summary>The toast button argument 3.4 to 3.7 used ("Quit and update"). A toast an older build
    /// left in the notification centre still carries it; it now opens the same Restart to update as
    /// <see cref="RestartToUpdate.RestartAction"/>, which still waits for the click.</summary>
    public const string LegacyQuitAction = "sanduhr-quit-for-update";

    /// <summary>Time to ask the Store again? Only for the packaged build: the
    /// Velopack and dev builds have no Store identity, and asking would throw.
    /// A null <paramref name="lastCheck"/> is "never asked", which checks once
    /// shortly after start.</summary>
    public static bool ShouldCheck(
        bool isPackaged, DateTimeOffset? lastCheck, DateTimeOffset now, TimeSpan? interval = null, bool enabled = true)
    {
        // enabled: settings.json "update_check" (Settings, About, Updates). Off, no update probe runs on its own (Settings v2, slice 6).
        if (!isPackaged || !enabled)
            return false;
        if (lastCheck is not { } last)
            return true;
        // A clock that moved backwards (a timezone change, a resumed laptop)
        // must not park the check forever, so treat the future as due.
        if (last > now)
            return true;
        return now - last >= (interval ?? CheckInterval);
    }

    /// <summary>Say something about this waiting update? Once when it appears,
    /// then at most once per <see cref="RenotifyInterval"/> while it still
    /// waits. Nothing is said when no update is pending.</summary>
    public static bool ShouldNotify(
        bool updatePending, DateTimeOffset? lastNotified, DateTimeOffset now, TimeSpan? interval = null)
    {
        if (!updatePending)
            return false;
        if (lastNotified is not { } last)
            return true;
        if (last > now)
            return true;
        return now - last >= (interval ?? RenotifyInterval);
    }
}
