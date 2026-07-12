namespace Sanduhr.Core;

/// <summary>One tier's alert-relevant numbers at one evaluation. UtilizationPct
/// is the API percentage; Used/Limit derive one for count-based tiers (routines).</summary>
public sealed record TierAlertSnapshot(
    string TierKey, int? UtilizationPct, int? Used, int? Limit, string? ResetsAt)
{
    /// <summary>The percentage alerts key off: utilization, else used/limit, else null (skip).</summary>
    public int? EffectivePct =>
        UtilizationPct ?? (Used is int u && Limit is int l && l > 0
            ? (int)Math.Round(100.0 * u / l)
            : null);
}

/// <summary>User alert preferences, mapped from settings.json by the App layer.</summary>
public sealed record AlertConfig(
    bool Enabled, int WarnPct, int UrgentPct, bool ProjectionEnabled, bool ResetEnabled);

public enum AlertKind { Warn, Urgent, Full, Projection, Reset }

/// <summary>An alert to deliver. TierKey resolves to a display label in the App
/// (the engine stays UI-free). For Reset events, UtilizationPct is the PREVIOUS window's peak
/// (the number that made the reset newsworthy).</summary>
public sealed record AlertEvent(AlertKind Kind, string TierKey, int UtilizationPct, string? ResetsAt);

/// <summary>
/// The WS-B alert truth table — pure state machine, no timers, no I/O, no UI
/// (same Core posture as <see cref="Pacing"/>). Semantics per the spec:
/// crossings not levels; hysteresis re-arm at threshold − 5; a reset-window
/// rollover (resets_at change) re-arms everything; projection fires once per
/// window via <see cref="Pacing.BurnProjection"/> with 2-evaluation flap
/// suppression; at most one threshold/projection event per tier per evaluation,
/// highest severity wins (Full > Urgent > Warn > Projection); Reset events are
/// independent and only fire when the previous window peaked at or above Warn.
/// State is in-memory only — an app restart may re-fire at most one alert per
/// already-crossed tier, which the spec accepts.
/// </summary>
public sealed class AlertEngine
{
    private const int Hysteresis = 5;

    private sealed class TierState
    {
        public int LastPct = -1;               // -1 = never observed: first sight of a crossed tier fires
        public string? WindowResetsAt;
        public bool WindowInitialized;
        public bool WarnFired;
        public bool UrgentFired;
        public bool FullFired;
        public bool ProjectionFired;
        public int ProjectionFalseStreak;
        public int WindowPeak = -1;
    }

    private readonly Dictionary<string, TierState> _tiers = new();

    /// <summary>Forget everything — recovery states and account switches call this
    /// so alerts never fire off another context's baseline.</summary>
    public void Reset() => _tiers.Clear();

    public IReadOnlyList<AlertEvent> Evaluate(
        IReadOnlyList<TierAlertSnapshot> current, AlertConfig config, DateTimeOffset now)
    {
        var events = new List<AlertEvent>();

        foreach (var snap in current)
        {
            if (snap.EffectivePct is not int pct)
                continue;

            if (!_tiers.TryGetValue(snap.TierKey, out var s))
            {
                s = new TierState();
                _tiers[snap.TierKey] = s;
            }

            // Window rollover: the API's resets_at moved (null-keyed tiers like
            // routines never roll here; their hysteresis re-arm covers the daily drop).
            if (!s.WindowInitialized)
            {
                s.WindowResetsAt = snap.ResetsAt;
                s.WindowInitialized = true;
            }
            else if (s.WindowResetsAt is not null && snap.ResetsAt is not null
                     && !string.Equals(s.WindowResetsAt, snap.ResetsAt, StringComparison.Ordinal))
            {
                if (config.Enabled && config.ResetEnabled && s.WindowPeak >= config.WarnPct)
                    events.Add(new AlertEvent(AlertKind.Reset, snap.TierKey, s.WindowPeak, snap.ResetsAt));
                s.WindowResetsAt = snap.ResetsAt;
                s.WarnFired = s.UrgentFired = s.FullFired = s.ProjectionFired = false;
                s.ProjectionFalseStreak = 0;
                s.WindowPeak = -1;
                s.LastPct = -1;    // new window: an immediately-high value is a fresh crossing
            }
            else if (!string.Equals(s.WindowResetsAt, snap.ResetsAt, StringComparison.Ordinal))
            {
                // A null-involved transition is a data hiccup, not a new window:
                // track the value but keep the baseline and armed state.
                // Known accepted edge: a hiccup that masks a real rollover defers the
                // Reset one window and can carry a stale peak into it — bounded, opt-in path only.
                s.WindowResetsAt = snap.ResetsAt;
            }

            AlertKind? candidate = null;

            if (config.Enabled)
            {
                // Threshold crossings, most severe first; crossing marks ALL
                // levels at-or-below the new value as fired (no late lower-tier fires).
                bool crossedFull = pct >= 100 && s.LastPct < 100;
                bool crossedUrgent = pct >= config.UrgentPct && s.LastPct < config.UrgentPct;
                bool crossedWarn = pct >= config.WarnPct && s.LastPct < config.WarnPct;

                if (crossedFull && !s.FullFired) candidate = AlertKind.Full;
                else if (crossedUrgent && !s.UrgentFired) candidate = AlertKind.Urgent;
                else if (crossedWarn && !s.WarnFired) candidate = AlertKind.Warn;

                if (pct >= 100) s.FullFired = true;
                if (pct >= config.UrgentPct) s.UrgentFired = true;
                if (pct >= config.WarnPct) s.WarnFired = true;

                // Hysteresis re-arm.
                if (pct < 100 - Hysteresis) s.FullFired = false;
                if (pct < config.UrgentPct - Hysteresis) s.UrgentFired = false;
                if (pct < config.WarnPct - Hysteresis) s.WarnFired = false;

                // Projection: only when no threshold event claimed this evaluation.
                if (config.ProjectionEnabled)
                {
                    bool projected = pct > 0 && pct < 100
                        && Pacing.BurnProjection(pct, snap.ResetsAt, snap.TierKey, now) is not null;
                    if (projected)
                    {
                        if (!s.ProjectionFired && candidate is null)
                        {
                            candidate = AlertKind.Projection;
                            s.ProjectionFired = true;
                        }
                        else if (!s.ProjectionFired)
                        {
                            s.ProjectionFired = true;   // claimed by a threshold event this eval
                        }
                        s.ProjectionFalseStreak = 0;
                    }
                    else
                    {
                        if (s.ProjectionFired && ++s.ProjectionFalseStreak >= 2)
                        {
                            s.ProjectionFired = false;
                            s.ProjectionFalseStreak = 0;
                        }
                    }
                }
            }

            if (candidate is AlertKind kind)
                events.Add(new AlertEvent(kind, snap.TierKey, pct, snap.ResetsAt));

            s.LastPct = pct;                    // baseline advances even when disabled
            if (pct > s.WindowPeak) s.WindowPeak = pct;
        }

        return events;
    }
}
