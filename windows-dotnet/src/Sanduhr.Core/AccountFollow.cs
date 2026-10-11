namespace Sanduhr.Core;

/// <summary>One five-hour session reading of an account: its utilization when it was fetched.</summary>
public readonly record struct SessionReading(DateTimeOffset At, double Utilization);

/// <summary>
/// Following the account in use (port of the Mac's AccountFollow.swift): with the setting on,
/// switch to the account whose session meter is climbing. The decision is pure: readings, the
/// last automatic switch, the manual pause and the clock go in, a decision comes out. The App's
/// AccountFollower schedules the usage checks (spec 4.6).
/// </summary>
public static class AccountFollow
{
    /// <summary>Inactive accounts are checked this often; the active one's rise is read over the same window.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);
    /// <summary>At most one automatic switch in this long.</summary>
    public static readonly TimeSpan SwitchSpacing = TimeSpan.FromMinutes(30);
    /// <summary>A manual switch pauses following for this long at most...</summary>
    public static readonly TimeSpan PauseLength = TimeSpan.FromHours(3);
    /// <summary>...or until the account picked has been idle this long.</summary>
    public static readonly TimeSpan PauseIdle = TimeSpan.FromMinutes(30);
    /// <summary>"Work (in use)" shows this long after an automatic switch.</summary>
    public static readonly TimeSpan NoteLength = TimeSpan.FromSeconds(60);
    /// <summary>A latest check counts only while this fresh (one check and some slack).</summary>
    public static readonly TimeSpan Freshness = CheckInterval + TimeSpan.FromMinutes(5);
    /// <summary>Readings older than this are dropped.</summary>
    public static readonly TimeSpan Keep = PauseLength + CheckInterval;

    /// <summary>A manual switch to <see cref="Account"/> at <see cref="Since"/>.</summary>
    public sealed record Pause(string Account, DateTimeOffset Since);

    /// <summary>Everything the decision looks at.</summary>
    public sealed class State
    {
        public bool Enabled { get; set; }
        public string? Active { get; set; }
        /// <summary>The accounts with a session key, the active one included when it has one.</summary>
        public HashSet<string> SignedIn { get; set; } = new();
        public bool ActiveSignedOut { get; set; }
        /// <summary>A switch is already running.</summary>
        public bool Switching { get; set; }
        /// <summary>Each account's session readings, oldest first.</summary>
        public Dictionary<string, List<SessionReading>> Readings { get; set; } = new();
        public DateTimeOffset? LastAutoSwitch { get; set; }
        public Pause? ManualPause { get; set; }

        /// <summary>Adds a reading and drops the ones nothing looks at any more.</summary>
        public void Record(string label, double utilization, DateTimeOffset now)
        {
            var list = Readings.TryGetValue(label, out var cur)
                ? cur.Where(r => now - r.At <= Keep).ToList()
                : new List<SessionReading>();
            list.Add(new SessionReading(now, utilization));
            Readings[label] = list;
        }

        /// <summary>A renamed account keeps its readings and its pause.</summary>
        public void Rename(string old, string newLabel)
        {
            if (Readings.Remove(old, out var list)) Readings[newLabel] = list;
            if (ManualPause?.Account == old) ManualPause = ManualPause with { Account = newLabel };
        }

        public void Forget(string label)
        {
            Readings.Remove(label);
            if (ManualPause?.Account == label) ManualPause = null;
        }
    }

    public enum Reason
    {
        /// <summary>The setting is off.</summary>
        Off,
        /// <summary>Signed out, a switch running, or fewer than two signed-in accounts.</summary>
        NotReady,
        /// <summary>A manual switch paused following.</summary>
        Paused,
        /// <summary>An automatic switch happened less than 30 minutes ago.</summary>
        TooSoon,
        /// <summary>No other account was in use in its latest check.</summary>
        NoSignal,
        /// <summary>The active account was in use too.</summary>
        BothInUse,
        /// <summary>More than one other account was in use: not a clear signal.</summary>
        Unclear,
    }

    /// <summary>Either stay (with a reason) or switch to an account.</summary>
    public readonly record struct Decision(Reason? StayReason, string? SwitchTo)
    {
        public static Decision Stay(Reason reason) => new(reason, null);
        public static Decision Switch(string account) => new(null, account);
        public bool IsSwitch => SwitchTo is not null;
    }

    // Use

    /// <summary>Whether the session rose between any two consecutive readings from
    /// <paramref name="since"/> on (the last reading before it is the baseline). A drop is not use.</summary>
    public static bool Rose(IReadOnlyList<SessionReading> readings, DateTimeOffset since)
    {
        if (readings.Count == 0) return false;
        int baseline = 0;
        for (int i = readings.Count - 1; i >= 0; i--)
            if (readings[i].At < since) { baseline = i; break; }
        for (int i = baseline + 1; i < readings.Count; i++)
            if (readings[i].Utilization > readings[i - 1].Utilization) return true;
        return false;
    }

    /// <summary>When the session last rose: the later reading of the last rising pair.</summary>
    public static DateTimeOffset? LastRise(IReadOnlyList<SessionReading> readings)
    {
        for (int i = readings.Count - 1; i >= 1; i--)
            if (readings[i].Utilization > readings[i - 1].Utilization) return readings[i].At;
        return null;
    }

    /// <summary>An inactive account was in use in its latest check: its session rose since the
    /// check before, and the latest check is fresh.</summary>
    public static bool InUseInLatestCheck(IReadOnlyList<SessionReading> readings, DateTimeOffset now)
    {
        if (readings.Count < 2) return false;
        var last = readings[^1];
        if (now - last.At > Freshness) return false;
        return last.Utilization > readings[^2].Utilization;
    }

    /// <summary>The active account was in use over the last check window.</summary>
    public static bool ActiveInUse(State state, DateTimeOffset now)
    {
        if (state.Active is null) return false;
        return Rose(ReadingsOf(state, state.Active), now - CheckInterval);
    }

    /// <summary>The other signed-in accounts seen in use in their latest check. Empty with the setting off.</summary>
    public static HashSet<string> OthersInUse(State state, DateTimeOffset now)
    {
        if (!state.Enabled) return new HashSet<string>();
        return state.SignedIn
            .Where(l => l != state.Active && InUseInLatestCheck(ReadingsOf(state, l), now))
            .ToHashSet();
    }

    /// <summary>A manual switch pauses following for 3 hours, or until the account picked has
    /// been idle for 30 minutes, whichever comes first.</summary>
    public static bool IsPaused(State state, DateTimeOffset now)
    {
        var pause = state.ManualPause;
        if (pause is null) return false;
        if (now - pause.Since >= PauseLength) return false;
        var rise = LastRise(ReadingsOf(state, pause.Account)) ?? pause.Since;
        var lastActivity = rise > pause.Since ? rise : pause.Since;
        return now - lastActivity < PauseIdle;
    }

    // Decision

    /// <summary>Switch only on a clear signal: exactly one other signed-in account in use in its
    /// latest check, the active one not in use over that window, following on, not paused, and no
    /// automatic switch in the last 30 minutes.</summary>
    public static Decision Decide(State state, DateTimeOffset now)
    {
        if (!state.Enabled) return Decision.Stay(Reason.Off);
        if (state.Active is not { } active || state.ActiveSignedOut || state.Switching || state.SignedIn.Count <= 1)
            return Decision.Stay(Reason.NotReady);
        if (IsPaused(state, now)) return Decision.Stay(Reason.Paused);
        if (state.LastAutoSwitch is { } last && now - last < SwitchSpacing)
            return Decision.Stay(Reason.TooSoon);
        var candidates = OthersInUse(state, now);
        candidates.Remove(active);
        if (candidates.Count == 0) return Decision.Stay(Reason.NoSignal);
        if (ActiveInUse(state, now)) return Decision.Stay(Reason.BothInUse);
        if (candidates.Count != 1) return Decision.Stay(Reason.Unclear);
        return Decision.Switch(candidates.First());
    }

    private static IReadOnlyList<SessionReading> ReadingsOf(State s, string label)
        => s.Readings.TryGetValue(label, out var l) ? l : Array.Empty<SessionReading>();
}
