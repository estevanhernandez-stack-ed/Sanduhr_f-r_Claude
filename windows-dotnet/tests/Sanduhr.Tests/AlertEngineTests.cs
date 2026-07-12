using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Truth table for the WS-B alert engine (spec 2026-07-12-threshold-alerts-design.md §1):
/// crossings not levels, hysteresis re-arm at threshold−5, once per reset window,
/// flap-suppressed projection, highest severity wins, reset alert only when the
/// previous window peaked at or above Warn.
/// </summary>
public class AlertEngineTests
{
    private static readonly AlertConfig On = new(
        Enabled: true, WarnPct: 80, UrgentPct: 95, ProjectionEnabled: false, ResetEnabled: false);

    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-07-12T00:00:00+00:00");

    private static TierAlertSnapshot Tier(int pct, string? resetsAt = "2026-07-17T00:00:00+00:00", string key = "seven_day")
        => new(key, pct, null, null, resetsAt);

    private static IReadOnlyList<AlertEvent> Eval(AlertEngine e, int pct, AlertConfig? cfg = null, DateTimeOffset? now = null, string? resetsAt = "2026-07-17T00:00:00+00:00")
        => e.Evaluate(new[] { Tier(pct, resetsAt) }, cfg ?? On, now ?? T0);

    [Fact]
    public void First_evaluation_fires_for_already_crossed_tier()
    {
        // Restart-refire semantics (spec: at most one per already-crossed tier).
        var e = new AlertEngine();
        var events = Eval(e, 85);
        var ev = Assert.Single(events);
        Assert.Equal(AlertKind.Warn, ev.Kind);
        Assert.Equal("seven_day", ev.TierKey);
        Assert.Equal(85, ev.UtilizationPct);
    }

    [Fact]
    public void Below_threshold_fires_nothing()
    {
        var e = new AlertEngine();
        Assert.Empty(Eval(e, 79));
    }

    [Fact]
    public void Sustained_level_does_not_refire()
    {
        var e = new AlertEngine();
        Eval(e, 85);
        Assert.Empty(Eval(e, 86));
        Assert.Empty(Eval(e, 84));
    }

    [Fact]
    public void Hysteresis_rearm_at_threshold_minus_five()
    {
        var e = new AlertEngine();
        Eval(e, 85);                     // Warn fires
        Assert.Empty(Eval(e, 76));       // 76 >= 75: still armed-off
        Assert.Empty(Eval(e, 74));       // drops below 75: re-arms, no event yet
        var ev = Assert.Single(Eval(e, 81)); // re-crossing fires again
        Assert.Equal(AlertKind.Warn, ev.Kind);
    }

    [Fact]
    public void Jump_across_both_thresholds_fires_single_highest()
    {
        var e = new AlertEngine();
        var ev = Assert.Single(Eval(e, 96));
        Assert.Equal(AlertKind.Urgent, ev.Kind);
        // Warn was silently marked fired too: dipping to 90 then rising must not late-fire Warn.
        Assert.Empty(Eval(e, 90));
        Assert.Empty(Eval(e, 92));
    }

    [Fact]
    public void Full_is_distinct_and_wins_over_urgent()
    {
        var e = new AlertEngine();
        var ev = Assert.Single(Eval(e, 100));
        Assert.Equal(AlertKind.Full, ev.Kind);
    }

    [Fact]
    public void Window_rollover_rearms_all_thresholds()
    {
        var e = new AlertEngine();
        Eval(e, 96);                                     // Urgent fired
        Assert.Empty(Eval(e, 97));                       // armed off
        // resets_at changes => new window; low value, nothing fires...
        Assert.Empty(Eval(e, 10, resetsAt: "2026-07-24T00:00:00+00:00"));
        // ...but the thresholds are re-armed for the new window.
        var ev = Assert.Single(Eval(e, 85, resetsAt: "2026-07-24T00:00:00+00:00"));
        Assert.Equal(AlertKind.Warn, ev.Kind);
    }

    [Fact]
    public void Disabled_config_returns_empty_and_preserves_state()
    {
        var e = new AlertEngine();
        var off = On with { Enabled = false };
        Assert.Empty(Eval(e, 96, off));
        // Re-enabled: the crossing already happened while disabled — treat prior
        // observation as the baseline, so 96 -> 97 is NOT a fresh crossing.
        Assert.Empty(Eval(e, 97));
    }

    [Fact]
    public void Null_percentage_tier_is_skipped()
    {
        var e = new AlertEngine();
        var snap = new TierAlertSnapshot("seven_day_opus", null, null, null, "2026-07-17T00:00:00+00:00");
        Assert.Empty(e.Evaluate(new[] { snap }, On, T0));
    }

    [Fact]
    public void Used_limit_tier_derives_percentage()
    {
        // Routines-style tier: utilization null, used/limit present, resets_at null.
        var e = new AlertEngine();
        var snap = new TierAlertSnapshot("routines", null, 24, 25, null);
        var ev = Assert.Single(e.Evaluate(new[] { snap }, On, T0));
        Assert.Equal(AlertKind.Urgent, ev.Kind);   // 96%
        Assert.Equal(96, ev.UtilizationPct);
    }

    [Fact]
    public void Reset_clears_all_armed_state()
    {
        var e = new AlertEngine();
        Eval(e, 96);
        e.Reset();
        var ev = Assert.Single(Eval(e, 96));       // fires again after Reset()
        Assert.Equal(AlertKind.Urgent, ev.Kind);
    }

    // -- projection ------------------------------------------------------------

    // seven_day window: total 604800s. resets_at = T0 + 1 day => frac = 6/7 ≈ 0.857.
    // util 95 => rate 95/0.857 ≈ 110.8 > 100 and time-to-100 lands before reset
    // => BurnProjection non-null. util 30 => rate ≈ 35 => null.
    private static readonly AlertConfig Proj = new(
        Enabled: true, WarnPct: 99, UrgentPct: 99, ProjectionEnabled: true, ResetEnabled: false);
    private const string ResetsTomorrow = "2026-07-13T00:00:00+00:00";

    [Fact]
    public void Projection_fires_once_when_pace_exceeds_window()
    {
        var e = new AlertEngine();
        var ev = Assert.Single(Eval(e, 95, Proj, T0, ResetsTomorrow));
        Assert.Equal(AlertKind.Projection, ev.Kind);
        Assert.Empty(Eval(e, 96, Proj, T0, ResetsTomorrow));   // once per window
    }

    [Fact]
    public void Projection_rearms_after_two_consecutive_false_evaluations()
    {
        var e = new AlertEngine();
        Eval(e, 95, Proj, T0, ResetsTomorrow);                 // fired
        Assert.Empty(Eval(e, 30, Proj, T0, ResetsTomorrow));   // false #1
        Assert.Empty(Eval(e, 30, Proj, T0, ResetsTomorrow));   // false #2 -> re-armed
        var ev = Assert.Single(Eval(e, 95, Proj, T0, ResetsTomorrow));
        Assert.Equal(AlertKind.Projection, ev.Kind);
    }

    [Fact]
    public void Projection_single_false_does_not_rearm()
    {
        var e = new AlertEngine();
        Eval(e, 95, Proj, T0, ResetsTomorrow);                 // fired
        Assert.Empty(Eval(e, 30, Proj, T0, ResetsTomorrow));   // false #1 only
        Assert.Empty(Eval(e, 95, Proj, T0, ResetsTomorrow));   // flap suppressed
    }

    [Fact]
    public void Threshold_beats_projection_in_same_evaluation()
    {
        var cfg = new AlertConfig(true, 80, 95, ProjectionEnabled: true, ResetEnabled: false);
        var e = new AlertEngine();
        var ev = Assert.Single(Eval(e, 96, cfg, T0, ResetsTomorrow));
        Assert.Equal(AlertKind.Urgent, ev.Kind);               // projection suppressed this eval
    }

    // -- reset notification ------------------------------------------------------

    [Fact]
    public void Reset_event_fires_on_rollover_when_previous_peak_reached_warn()
    {
        var cfg = On with { ResetEnabled = true };
        var e = new AlertEngine();
        Eval(e, 85, cfg);                                       // peak 85 >= 80
        var events = Eval(e, 5, cfg, resetsAt: "2026-07-24T00:00:00+00:00");
        var ev = Assert.Single(events);
        Assert.Equal(AlertKind.Reset, ev.Kind);
    }

    [Fact]
    public void Reset_event_skipped_when_previous_window_stayed_low()
    {
        var cfg = On with { ResetEnabled = true };
        var e = new AlertEngine();
        Eval(e, 40, cfg);                                       // peak 40 < 80
        Assert.Empty(Eval(e, 5, cfg, resetsAt: "2026-07-24T00:00:00+00:00"));
    }

    [Fact]
    public void Reset_event_disabled_by_default_config()
    {
        var e = new AlertEngine();
        Eval(e, 85, On);
        Assert.Empty(Eval(e, 5, On, resetsAt: "2026-07-24T00:00:00+00:00"));
    }

    [Fact]
    public void Null_resets_at_transition_is_not_a_rollover()
    {
        var e = new AlertEngine();
        var cfg = On with { ResetEnabled = true };
        Assert.Single(Eval(e, 96, cfg));                              // Urgent fires
        Assert.Empty(Eval(e, 96, cfg, resetsAt: null));               // hiccup: no Reset, no refire
        Assert.Empty(Eval(e, 96, cfg));                               // back to normal: still armed-off
    }

    [Fact]
    public void Reset_event_carries_previous_window_peak()
    {
        var cfg = On with { ResetEnabled = true };
        var e = new AlertEngine();
        Eval(e, 85, cfg);
        var ev = Assert.Single(Eval(e, 5, cfg, resetsAt: "2026-07-24T00:00:00+00:00"));
        Assert.Equal(AlertKind.Reset, ev.Kind);
        Assert.Equal(85, ev.UtilizationPct);
    }

    [Fact]
    public void Exact_threshold_and_hysteresis_boundaries()
    {
        var e = new AlertEngine();
        var ev = Assert.Single(Eval(e, 80));       // crossing is inclusive at the threshold
        Assert.Equal(AlertKind.Warn, ev.Kind);
        Assert.Empty(Eval(e, 75));                 // exactly threshold-5: still armed-off
        Assert.Empty(Eval(e, 74));                 // strictly below: re-arms silently
        Assert.Single(Eval(e, 80));                // re-fires
    }

    [Fact]
    public void Disabled_config_suppresses_projection_too()
    {
        var cfg = new AlertConfig(Enabled: false, WarnPct: 99, UrgentPct: 99,
            ProjectionEnabled: true, ResetEnabled: false);
        var e = new AlertEngine();
        Assert.Empty(Eval(e, 95, cfg, T0, "2026-07-13T00:00:00+00:00"));
    }
}
