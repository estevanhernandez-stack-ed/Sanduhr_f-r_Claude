# WS-B Threshold Alerts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Threshold-crossing and burn-projection alerts (Windows toasts + soft procedural chime, Focus-Assist-aware) per the approved spec `docs/superpowers/specs/2026-07-12-threshold-alerts-design.md`.

**Architecture:** A pure, stateful `AlertEngine` in Sanduhr.Core carries the entire truth table (crossings not levels, hysteresis re-arm at threshold−5, once-per-reset-window, flap-suppressed projection via `Pacing.BurnProjection`, highest-severity-wins). App delivery is thin: `AlertService` renders toasts via `Microsoft.Toolkit.Uwp.Notifications` and plays new `ChimeSynth` tones through the existing `Sounds` cache, gated on `SHQueryUserNotificationState`. A themed Settings ▸ Alerts tab owns configuration.

**Tech Stack:** .NET 10 WPF (`windows-dotnet/`), CommunityToolkit.Mvvm, xUnit, Microsoft.Toolkit.Uwp.Notifications 7.1.3, CsWin32 (already referenced) for the notification-state interop.

## Global Constraints

- Test command: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`. App build: `dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj`.
- **Theming:** in-app surfaces use `{DynamicResource Sanduhr.Brush.*}` only; dialogs via `ThemedDialog`; toasts render OS-styled by design (out of theming scope, per spec §5).
- **Audio:** procedural tones only, ~25%-of-full-scale amplitude convention (`ChimeSynth.Amplitude` = 8000); NEVER Windows system sounds; the Snake sting is a **synthesized homage**, never a sampled MGS sound.
- **Logging:** `sanduhr.log` best-effort appends, no account labels, no alert payload text — operation + exception type only (WS-A convention).
- **Defaults (spec §4, exact):** `alerts_enabled`=true, `alert_warn_pct`=80, `alert_urgent_pct`=95, `alert_projection`=true, `alert_reset`=false, `alert_sound`=true, `alert_snake_full`=false. Thresholds clamp to 50–99 and enforce Warn < Urgent on save.
- Alerts must never break the fetch loop: every delivery path is try/caught.
- Branch: `feat/ws-b-threshold-alerts` (Task 1 creates). Main is PR-only — the final task opens a PR, it does not merge.
- Conventional commits; commit at the end of every task.
- GitNexus rules apply where tooling exists; each task's Interfaces block lists known callers (recon 2026-07-12) — verify with Grep before editing.

---

### Task 1: `AlertEngine` (Core, TDD — the truth table)

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/AlertEngine.cs`
- Test: create `windows-dotnet/tests/Sanduhr.Tests/AlertEngineTests.cs`

**Interfaces:**
- Consumes: `Pacing.BurnProjection(double? util, string? resetsAt, string tierKey, DateTimeOffset? now)` (existing, returns null when the pace won't hit 100 before reset).
- Produces (Tasks 3, 5, 6 rely on these exact names):
  - `sealed record TierAlertSnapshot(string TierKey, int? UtilizationPct, int? Used, int? Limit, string? ResetsAt)` with computed `int? EffectivePct`
  - `sealed record AlertConfig(bool Enabled, int WarnPct, int UrgentPct, bool ProjectionEnabled, bool ResetEnabled)`
  - `enum AlertKind { Warn, Urgent, Full, Projection, Reset }`
  - `sealed record AlertEvent(AlertKind Kind, string TierKey, int UtilizationPct, string? ResetsAt)`
  - `sealed class AlertEngine` with `IReadOnlyList<AlertEvent> Evaluate(IReadOnlyList<TierAlertSnapshot> current, AlertConfig config, DateTimeOffset now)` and `void Reset()` (clears all armed state — recovery/account-switch hook)

- [ ] **Step 1: Create the branch**

```bash
git checkout -b feat/ws-b-threshold-alerts
```

- [ ] **Step 2: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/AlertEngineTests.cs`:

```csharp
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
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "FullyQualifiedName~AlertEngineTests"`
Expected: compile error — `AlertEngine` not defined.

- [ ] **Step 4: Implement**

Create `windows-dotnet/src/Sanduhr.Core/AlertEngine.cs`:

```csharp
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
/// (the engine stays UI-free).</summary>
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
            else if (!string.Equals(s.WindowResetsAt, snap.ResetsAt, StringComparison.Ordinal))
            {
                if (config.Enabled && config.ResetEnabled && s.WindowPeak >= config.WarnPct)
                    events.Add(new AlertEvent(AlertKind.Reset, snap.TierKey, pct, snap.ResetsAt));
                s.WindowResetsAt = snap.ResetsAt;
                s.WarnFired = s.UrgentFired = s.FullFired = s.ProjectionFired = false;
                s.ProjectionFalseStreak = 0;
                s.WindowPeak = -1;
                s.LastPct = -1;    // new window: an immediately-high value is a fresh crossing
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
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "FullyQualifiedName~AlertEngineTests"`
Expected: PASS (18 tests). Then run the full suite once: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj` — expected 351 passing.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/AlertEngine.cs windows-dotnet/tests/Sanduhr.Tests/AlertEngineTests.cs
git commit -m "feat(core): AlertEngine — threshold/projection/reset truth table"
```

---

### Task 2: Alert chimes — `ChimeSynth` sequences + `Sounds` playback

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/ChimeSynth.cs` (add three sequences after `Toggle`, ~line 50)
- Modify: `windows-dotnet/src/Sanduhr.App/Services/Sounds.cs` (add three play methods after `PlayToggle`, ~line 40)
- Test: `windows-dotnet/tests/Sanduhr.Tests/ChimeSynthTests.cs`

**Interfaces:**
- Consumes: `ChimeSynth.Note`, `ChimeSynth.BuildWav`, `Sounds.Play(notes, cacheKey)` (existing private — the new methods mirror `PlayInfo`).
- Produces (Task 4 relies on): `ChimeSynth.AlertWarn`, `ChimeSynth.AlertUrgent`, `ChimeSynth.AlertSnake` (each `IReadOnlyList<ChimeSynth.Note>`); `Sounds.PlayAlertWarn()`, `Sounds.PlayAlertUrgent()`, `Sounds.PlayAlertSnake()`.

- [ ] **Step 1: Read the existing test style**

Read `windows-dotnet/tests/Sanduhr.Tests/ChimeSynthTests.cs` first and mirror its assertion style for the new tests (it pins note sequences and WAV byte-shape).

- [ ] **Step 2: Write the failing tests**

Append inside `ChimeSynthTests` (adapt assertion helpers to the file's existing style if it has them — the substance below is binding):

```csharp
    // -- WS-B alert tones (spec §3) --------------------------------------------

    [Fact]
    public void AlertWarn_is_a_soft_ascending_two_note()
    {
        Assert.Equal(2, ChimeSynth.AlertWarn.Count);
        Assert.True(ChimeSynth.AlertWarn[1].Frequency > ChimeSynth.AlertWarn[0].Frequency);
    }

    [Fact]
    public void AlertUrgent_is_a_firmer_three_note()
    {
        Assert.Equal(3, ChimeSynth.AlertUrgent.Count);
        // Lands and holds on the top note.
        Assert.Equal(ChimeSynth.AlertUrgent[1].Frequency, ChimeSynth.AlertUrgent[2].Frequency);
        Assert.True(ChimeSynth.AlertUrgent[2].DurationSeconds > ChimeSynth.AlertUrgent[1].DurationSeconds);
    }

    [Fact]
    public void AlertSnake_is_a_sharp_descending_two_tone()
    {
        Assert.Equal(2, ChimeSynth.AlertSnake.Count);
        Assert.True(ChimeSynth.AlertSnake[0].Frequency > ChimeSynth.AlertSnake[1].Frequency);
        Assert.True(ChimeSynth.AlertSnake[0].Frequency > 1000);   // the "!" bite lives up high
    }

    [Fact]
    public void Alert_sequences_build_valid_wavs()
    {
        foreach (var seq in new[] { ChimeSynth.AlertWarn, ChimeSynth.AlertUrgent, ChimeSynth.AlertSnake })
        {
            var wav = ChimeSynth.BuildWav(seq);
            Assert.True(wav.Length > 44);
            Assert.Equal((byte)'R', wav[0]);   // RIFF header intact
        }
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "FullyQualifiedName~ChimeSynthTests"`
Expected: compile error — `AlertWarn` not defined.

- [ ] **Step 4: Implement the sequences**

In `ChimeSynth.cs`, after the `Toggle` sequence (~line 50):

```csharp
    /// <summary>Soft ascending two-note — a tier crossed the warn threshold.
    /// Background-of-attention, same amplitude discipline as the UI cues.</summary>
    public static readonly IReadOnlyList<Note> AlertWarn = new[]
    {
        new Note(329.63, 0.09), // E4
        new Note(392.00, 0.16), // G4
    };

    /// <summary>Firmer three-note landing-and-holding on C5 — urgent threshold.</summary>
    public static readonly IReadOnlyList<Note> AlertUrgent = new[]
    {
        new Note(392.00, 0.09), // G4
        new Note(523.25, 0.09), // C5
        new Note(523.25, 0.18), // C5 held
    };

    /// <summary>The 100% sting — a synthesized homage to a certain codec-era
    /// alert ("!"), NOT a sample: sharp high attack falling to a held tone.
    /// Opt-in via the Alerts tab; when off, Full uses <see cref="AlertUrgent"/>.</summary>
    public static readonly IReadOnlyList<Note> AlertSnake = new[]
    {
        new Note(1244.51, 0.07), // D#6 — the bite
        new Note(830.61, 0.22),  // G#5 — the fall
    };
```

- [ ] **Step 5: Add the playback methods**

In `Sounds.cs`, after `PlayToggle()` (~line 40):

```csharp
    /// <summary>Warn-threshold alert chime (WS-B).</summary>
    public static void PlayAlertWarn() => Play(ChimeSynth.AlertWarn, "alert-warn");

    /// <summary>Urgent-threshold alert chime (WS-B).</summary>
    public static void PlayAlertUrgent() => Play(ChimeSynth.AlertUrgent, "alert-urgent");

    /// <summary>The opt-in 100% sting (synthesized homage — see ChimeSynth.AlertSnake).</summary>
    public static void PlayAlertSnake() => Play(ChimeSynth.AlertSnake, "alert-snake");
```

- [ ] **Step 6: Verify**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj` and `dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj`
Expected: all tests pass, build clean.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/ChimeSynth.cs windows-dotnet/src/Sanduhr.App/Services/Sounds.cs windows-dotnet/tests/Sanduhr.Tests/ChimeSynthTests.cs
git commit -m "feat(audio): alert chimes — warn, urgent, and the opt-in snake sting"
```

---

### Task 3: Settings keys + `AlertsViewModel`

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs` (append after the sparkline-style pair at the end of the class)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (settings passthroughs next to `LoadLocalCcShowBreakdowns`, ~line 200)
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/AlertsViewModel.cs`

**Interfaces:**
- Consumes: `SettingsStore.Read()/Write()` private pattern (mirror `LoadPinned`/`SavePinned` exactly); `AlertConfig` (Task 1).
- Produces (Tasks 5, 6 rely on):
  - `SettingsStore.LoadAlertConfig() : AlertConfig` and `SettingsStore.SaveAlertConfig(AlertConfig)` plus `LoadAlertSound()/SaveAlertSound(bool)` and `LoadAlertSnakeFull()/SaveAlertSnakeFull(bool)`
  - `WidgetViewModel.LoadAlertConfig()/SaveAlertConfig(AlertConfig)/LoadAlertSound()/SaveAlertSound(bool)/LoadAlertSnakeFull()/SaveAlertSnakeFull(bool)` passthroughs, plus `event Action? AlertSettingsChanged;` raised by the save passthroughs
  - `AlertsViewModel` (CommunityToolkit): observable properties `AlertsEnabled`, `WarnPct`, `UrgentPct`, `ProjectionEnabled`, `ResetEnabled`, `SoundEnabled`, `SnakeAtFull`, `ValidationHint`; `TestAlertCommand`; ctor `AlertsViewModel(WidgetViewModel widget, Func<Task> sendTestAlertAsync)`
- Known callers of modified symbols: `SettingsStore` is constructed only in `WidgetViewModel` ctor; no other construction sites.

- [ ] **Step 1: Settings pairs**

Append to `SettingsStore.cs` (inside the class, following the key-per-preference pattern — each Load reads with a safe default, each Save does read-modify-write):

```csharp
    // -- WS-B alerts (spec 2026-07-12-threshold-alerts-design.md §4) ------------

    /// <summary>Alert engine config. Defaults: enabled, warn 80, urgent 95,
    /// projection on, reset notification off.</summary>
    public AlertConfig LoadAlertConfig()
    {
        var root = Read();
        bool enabled = true; int warn = 80; int urgent = 95; bool projection = true; bool reset = false;
        try { enabled = root["alerts_enabled"]?.GetValue<bool>() ?? true; } catch { }
        try { warn = root["alert_warn_pct"]?.GetValue<int>() ?? 80; } catch { }
        try { urgent = root["alert_urgent_pct"]?.GetValue<int>() ?? 95; } catch { }
        try { projection = root["alert_projection"]?.GetValue<bool>() ?? true; } catch { }
        try { reset = root["alert_reset"]?.GetValue<bool>() ?? false; } catch { }
        warn = Math.Clamp(warn, 50, 99);
        urgent = Math.Clamp(urgent, 50, 99);
        if (warn >= urgent) { warn = 80; urgent = 95; }   // corrupt pair -> defaults
        return new AlertConfig(enabled, warn, urgent, projection, reset);
    }

    public void SaveAlertConfig(AlertConfig config)
    {
        var root = Read();
        root["alerts_enabled"] = config.Enabled;
        root["alert_warn_pct"] = config.WarnPct;
        root["alert_urgent_pct"] = config.UrgentPct;
        root["alert_projection"] = config.ProjectionEnabled;
        root["alert_reset"] = config.ResetEnabled;
        Write(root);
    }

    public bool LoadAlertSound()
    {
        var root = Read();
        try { return root["alert_sound"]?.GetValue<bool>() ?? true; } catch { return true; }
    }

    public void SaveAlertSound(bool on)
    {
        var root = Read();
        root["alert_sound"] = on;
        Write(root);
    }

    public bool LoadAlertSnakeFull()
    {
        var root = Read();
        try { return root["alert_snake_full"]?.GetValue<bool>() ?? false; } catch { return false; }
    }

    public void SaveAlertSnakeFull(bool on)
    {
        var root = Read();
        root["alert_snake_full"] = on;
        Write(root);
    }
```

Add `using Sanduhr.Core;` to `SettingsStore.cs` if absent (for `AlertConfig`).

- [ ] **Step 2: WidgetViewModel passthroughs**

Next to the existing settings passthroughs (~line 200):

```csharp
    /// <summary>Alert preferences (settings.json, WS-B). Saves raise
    /// <see cref="AlertSettingsChanged"/> so the live engine re-reads config.</summary>
    public AlertConfig LoadAlertConfig() => _settings.LoadAlertConfig();

    public void SaveAlertConfig(AlertConfig config)
    {
        _settings.SaveAlertConfig(config);
        AlertSettingsChanged?.Invoke();
    }

    public bool LoadAlertSound() => _settings.LoadAlertSound();

    public void SaveAlertSound(bool on)
    {
        _settings.SaveAlertSound(on);
        AlertSettingsChanged?.Invoke();
    }

    public bool LoadAlertSnakeFull() => _settings.LoadAlertSnakeFull();

    public void SaveAlertSnakeFull(bool on)
    {
        _settings.SaveAlertSnakeFull(on);
        AlertSettingsChanged?.Invoke();
    }
```

And with the other events (~line 150):

```csharp
    /// <summary>Raised when any alert preference is saved — the alert pipeline
    /// re-reads its config on the next evaluation.</summary>
    public event Action? AlertSettingsChanged;
```

- [ ] **Step 3: AlertsViewModel**

Create `windows-dotnet/src/Sanduhr.App/ViewModels/AlertsViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Drives the Settings ▸ Alerts tab (WS-B). Edits persist immediately through
/// the WidgetViewModel passthroughs (the tab is non-modal, matching the other
/// tabs' apply-on-change behavior). Threshold edits are validated on change:
/// values clamp to 50-99 and Warn must stay below Urgent — invalid combinations
/// revert and surface a themed inline hint instead of persisting.
/// </summary>
public sealed partial class AlertsViewModel : ObservableObject
{
    private readonly WidgetViewModel _widget;
    private readonly Func<Task> _sendTestAlertAsync;
    private bool _loading;

    [ObservableProperty] private bool _alertsEnabled;
    [ObservableProperty] private int _warnPct;
    [ObservableProperty] private int _urgentPct;
    [ObservableProperty] private bool _projectionEnabled;
    [ObservableProperty] private bool _resetEnabled;
    [ObservableProperty] private bool _soundEnabled;
    [ObservableProperty] private bool _snakeAtFull;
    [ObservableProperty] private string _validationHint = "";

    public AlertsViewModel(WidgetViewModel widget, Func<Task> sendTestAlertAsync)
    {
        _widget = widget;
        _sendTestAlertAsync = sendTestAlertAsync;
        _loading = true;
        var cfg = widget.LoadAlertConfig();
        AlertsEnabled = cfg.Enabled;
        WarnPct = cfg.WarnPct;
        UrgentPct = cfg.UrgentPct;
        ProjectionEnabled = cfg.ProjectionEnabled;
        ResetEnabled = cfg.ResetEnabled;
        SoundEnabled = widget.LoadAlertSound();
        SnakeAtFull = widget.LoadAlertSnakeFull();
        _loading = false;
    }

    partial void OnAlertsEnabledChanged(bool value) => PersistConfig();
    partial void OnWarnPctChanged(int value) => PersistConfig();
    partial void OnUrgentPctChanged(int value) => PersistConfig();
    partial void OnProjectionEnabledChanged(bool value) => PersistConfig();
    partial void OnResetEnabledChanged(bool value) => PersistConfig();

    partial void OnSoundEnabledChanged(bool value)
    {
        if (!_loading) _widget.SaveAlertSound(value);
    }

    partial void OnSnakeAtFullChanged(bool value)
    {
        if (!_loading) _widget.SaveAlertSnakeFull(value);
    }

    private void PersistConfig()
    {
        if (_loading)
            return;
        int warn = Math.Clamp(WarnPct, 50, 99);
        int urgent = Math.Clamp(UrgentPct, 50, 99);
        if (warn >= urgent)
        {
            ValidationHint = "Warn must be below Urgent — not saved.";
            return;
        }
        ValidationHint = "";
        _widget.SaveAlertConfig(new AlertConfig(
            AlertsEnabled, warn, urgent, ProjectionEnabled, ResetEnabled));
    }

    /// <summary>"Send test alert" — a fake Warn event through the real delivery
    /// pipeline (toast + chime), the support answer to "is it working?".</summary>
    [RelayCommand]
    private async Task TestAlert() => await _sendTestAlertAsync();
}
```

- [ ] **Step 4: Verify**

Run: `dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj && dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`
Expected: build clean, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/AlertsViewModel.cs
git commit -m "feat(app): alert settings keys + Alerts tab viewmodel"
```

---

### Task 4: `AlertService` — toasts, chime gate, notification-state interop

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj` (TFM + package)
- Modify: the project's CsWin32 `NativeMethods.txt` (Glob for it under `windows-dotnet/src/Sanduhr.App/`; if none exists, create it at the project root with the single line below)
- Create: `windows-dotnet/src/Sanduhr.App/Services/AlertService.cs`

**Interfaces:**
- Consumes: `AlertEvent`/`AlertKind` (Task 1), `Sounds.PlayAlertWarn/PlayAlertUrgent/PlayAlertSnake` (Task 2), `Pacing.TimeUntil` (existing) for the toast's reset countdown.
- Produces (Tasks 5-6 rely on): `sealed class AlertService` with ctor `AlertService(Paths paths, Action activateWidget)`, methods `void Deliver(AlertEvent e, string tierLabel, bool soundEnabled, bool snakeAtFull)` and `void DeliverTest()`; static `void HandleToastActivationOnLaunch(Action activate)` no-op-safe wiring.

- [ ] **Step 1: TFM bump, alone, verified**

The toast package needs a Windows-10 TFM. In `Sanduhr.App.csproj` change:

```xml
    <TargetFramework>net10.0-windows</TargetFramework>
```

to:

```xml
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>
```

Run BOTH gates before anything else in this task: `dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj && dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`
Expected: clean. (WebView2/CsWin32/WPF-UI all tolerate a Win10 TFM. If the build breaks here, STOP and report — do not stack the package on a broken base.)

- [ ] **Step 2: Add the toast package**

```bash
dotnet add windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj package Microsoft.Toolkit.Uwp.Notifications --version 7.1.3
```

- [ ] **Step 3: Notification-state interop**

Glob `windows-dotnet/src/Sanduhr.App/**/NativeMethods.txt`. Append (or create the file containing):

```
SHQueryUserNotificationState
```

- [ ] **Step 4: Implement `AlertService`**

Create `windows-dotnet/src/Sanduhr.App/Services/AlertService.cs`:

```csharp
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
```

(If CsWin32 generates the enum/namespace under slightly different names, follow the generated code — the semantic is exact: only `QUNS_ACCEPTS_NOTIFICATIONS` chimes.)

- [ ] **Step 5: Verify**

Run: `dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj && dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`
Expected: build clean, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj windows-dotnet/src/Sanduhr.App/Services/AlertService.cs
git add windows-dotnet/src/Sanduhr.App/NativeMethods.txt 2>/dev/null || git add -A windows-dotnet/src/Sanduhr.App/
git commit -m "feat(app): AlertService — toasts with AUMID compat, DND-gated chimes"
```

---

### Task 5: Wire the pipeline into `WidgetViewModel`

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (ctor ~line 217, `RefreshAsync` success path ~line 626, `EnterRecoveryState`, `SwitchAccount`)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (hand the activation callback in)

**Interfaces:**
- Consumes: `AlertEngine.Evaluate/Reset` (Task 1), `AlertService.Deliver` (Task 4), `WidgetViewModel.LoadAlertConfig/LoadAlertSound/LoadAlertSnakeFull` + `AlertSettingsChanged` (Task 3), existing private helpers `Util(JsonObject, string)` and `GetInt(JsonObject?, string)` and `TierModel.CanonicalOrder`.
- Produces (Task 6 relies on): `WidgetViewModel.AlertService` property (for the Settings test button) — created lazily in the VM ctor via `AttachAlertService(AlertService)` called from App after window creation.
- Known callers: `RefreshAsync` is the 5-min timer + RefreshCommand; `EnterRecoveryState` from the catch blocks; `SwitchAccount` from menus/Settings.

- [ ] **Step 1: Fields, config cache, attach point**

In `WidgetViewModel`, add fields next to `_ccReader`:

```csharp
    private readonly AlertEngine _alertEngine = new();
    private AlertConfig _alertConfig = new(true, 80, 95, true, false);
    private AlertService? _alertService;
```

In the ctor (after `_ccReader = new CcLogReader();`):

```csharp
        _alertConfig = _settings.LoadAlertConfig();
        AlertSettingsChanged += () => _alertConfig = _settings.LoadAlertConfig();
```

Add near the other public members:

```csharp
    /// <summary>Alert delivery, attached by App once the main window exists
    /// (the service needs an activation callback). Null in unit contexts.</summary>
    public AlertService? AlertService => _alertService;

    public void AttachAlertService(AlertService service) => _alertService = service;
```

- [ ] **Step 2: Snapshot mapping + dispatch**

Add these private methods near `RenderCards`:

```csharp
    /// <summary>Map the fetch payload to alert snapshots — every canonical tier
    /// with data participates, hidden or not (a cap is real whether or not its
    /// card is shown).</summary>
    private List<TierAlertSnapshot> BuildAlertSnapshots(JsonObject data)
    {
        var snaps = new List<TierAlertSnapshot>();
        foreach (var key in TierModel.CanonicalOrder)
        {
            var tier = data[key] as JsonObject;
            if (tier is null)
                continue;
            double? util = Util(data, key);
            snaps.Add(new TierAlertSnapshot(
                key,
                util is null ? null : (int)util.Value,
                GetInt(tier, "used"),
                GetInt(tier, "limit"),
                tier["resets_at"]?.GetValue<string>()));
        }
        return snaps;
    }

    /// <summary>Evaluate + deliver alerts for a fresh fetch. Never throws —
    /// alerting must not break the fetch loop.</summary>
    private void EvaluateAlerts(JsonObject data, DateTimeOffset now)
    {
        try
        {
            if (_alertService is null)
                return;
            var events = _alertEngine.Evaluate(BuildAlertSnapshots(data), _alertConfig, now);
            if (events.Count == 0)
                return;
            bool sound = _settings.LoadAlertSound();
            bool snake = _settings.LoadAlertSnakeFull();
            foreach (var e in events)
                _alertService.Deliver(e, TierModel.DisplayLabel(e.TierKey), sound, snake);
        }
        catch (Exception e)
        {
            try
            {
                File.AppendAllText(_paths.LogFile,
                    $"{DateTime.UtcNow:o} alert evaluate failed ({e.GetType().Name}){Environment.NewLine}");
            }
            catch { }
        }
    }
```

**Label resolution note:** check `TierModel` for the existing display-name source (the tier cards get labels from somewhere — grep `TierModel` for a label map or see how `TierCardViewModel` resolves its header). If the existing member has a different name than `DisplayLabel(key)`, use the existing one; add a `DisplayLabel` static only if none exists (single dictionary keyed by `CanonicalOrder`, labels matching the card headers).

- [ ] **Step 3: Hook the three sites**

In `RefreshAsync`'s success path, immediately after `RenderCards(data, DateTimeOffset.UtcNow);` (line ~621), add:

```csharp
            EvaluateAlerts(data, DateTimeOffset.UtcNow);
```

In `EnterRecoveryState`, after `Reason = reason;`:

```csharp
        _alertEngine.Reset();   // never alert off another context's baseline
```

In `SwitchAccount`, next to `Tiers.Clear();`:

```csharp
        _alertEngine.Reset();
```

- [ ] **Step 4: App wiring**

In `App.OnStartup`, after `_window = new MainWindow { DataContext = _vm }; _window.Show();`:

```csharp
        _vm.AttachAlertService(new AlertService(new Sanduhr.Core.Paths(), ShowWindow));
```

- [ ] **Step 5: Verify**

Run: `dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj && dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`
Expected: clean.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs windows-dotnet/src/Sanduhr.App/App.xaml.cs windows-dotnet/src/Sanduhr.Core/TierModel.cs
git commit -m "feat(app): alert pipeline wired into the fetch loop with recovery/switch resets"
```

(Drop `TierModel.cs` from the add if no `DisplayLabel` was needed.)

---

### Task 6: Settings ▸ Alerts tab

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (ctor, line ~34)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (new TabItem between Themes and History)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (`ShowSettingsAsync`, ~line 140)

**Interfaces:**
- Consumes: `AlertsViewModel` (Task 3), `WidgetViewModel.AlertService.DeliverTest` (Tasks 4-5).
- Produces: `SettingsViewModel.Alerts` property. Ctor becomes `SettingsViewModel(WidgetViewModel widget, Func<Task> addAccountAsync, Func<string, Task> updateSignInAsync)` — UNCHANGED signature; the Alerts child is constructed internally from `widget`.
- Known callers: `new SettingsViewModel(...)` ← `App.ShowSettingsAsync` only.

- [ ] **Step 1: SettingsViewModel child**

Add the property and construction (ctor body, after `LocalCc = ...`):

```csharp
    /// <summary>Backs the Alerts tab (WS-B): thresholds, projection/reset
    /// toggles, sound + snake sting, and the test-alert button.</summary>
    public AlertsViewModel Alerts { get; }
```

```csharp
        Alerts = new AlertsViewModel(widget, () =>
        {
            widget.AlertService?.DeliverTest();
            return Task.CompletedTask;
        });
```

- [ ] **Step 2: The tab XAML**

In `SettingsWindow.xaml`, insert a new `TabItem` immediately after the Themes tab's closing `</TabItem>` (before the History tab). Match the sibling tabs' structure exactly (`Border` with `Sanduhr.Brush.Glass`, `Padding="16"`); reuse the window's existing `FlatButton`/`AccentButton` styles and converters:

```xml
                <!-- Alerts tab (WS-B) — thresholds, projection, sounds. -->
                <TabItem Header="Alerts">
                    <Border Background="{DynamicResource Sanduhr.Brush.Glass}" CornerRadius="0,8,8,8" Padding="16">
                        <StackPanel DataContext="{Binding Alerts}">
                            <TextBlock TextWrapping="Wrap" FontSize="11"
                                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,0,0,12"
                                       Text="Get a toast and a soft chime when a tier crosses your thresholds, or when your pace will hit the cap before it resets. Toasts respect Focus Assist." />

                            <CheckBox Content="Enable alerts" IsChecked="{Binding AlertsEnabled}"
                                      Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,10" />

                            <Grid Margin="0,0,0,10">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="70" />
                                    <ColumnDefinition Width="24" />
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="70" />
                                </Grid.ColumnDefinitions>
                                <TextBlock Grid.Column="0" Text="Warn at" VerticalAlignment="Center" FontSize="12"
                                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,8,0" />
                                <TextBox Grid.Column="1" Text="{Binding WarnPct, UpdateSourceTrigger=LostFocus}"
                                         FontSize="12" Padding="6,4"
                                         Background="{DynamicResource Sanduhr.Brush.Bg}"
                                         Foreground="{DynamicResource Sanduhr.Brush.Text}"
                                         BorderBrush="{DynamicResource Sanduhr.Brush.Border}"
                                         CaretBrush="{DynamicResource Sanduhr.Brush.Text}" />
                                <TextBlock Grid.Column="3" Text="Urgent at" VerticalAlignment="Center" FontSize="12"
                                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,8,0" />
                                <TextBox Grid.Column="4" Text="{Binding UrgentPct, UpdateSourceTrigger=LostFocus}"
                                         FontSize="12" Padding="6,4"
                                         Background="{DynamicResource Sanduhr.Brush.Bg}"
                                         Foreground="{DynamicResource Sanduhr.Brush.Text}"
                                         BorderBrush="{DynamicResource Sanduhr.Brush.Border}"
                                         CaretBrush="{DynamicResource Sanduhr.Brush.Text}" />
                            </Grid>

                            <TextBlock Text="{Binding ValidationHint}" FontSize="11"
                                       Foreground="{DynamicResource Sanduhr.Brush.PaceMarker}"
                                       Visibility="{Binding ValidationHint, Converter={StaticResource StrVis}}"
                                       Margin="0,0,0,10" />

                            <CheckBox Content="Projection alerts (on pace to hit the cap before reset)"
                                      IsChecked="{Binding ProjectionEnabled}"
                                      Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,6" />
                            <CheckBox Content="Reset notifications (fresh window after a heavy one)"
                                      IsChecked="{Binding ResetEnabled}"
                                      Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,6" />
                            <CheckBox Content="Sound" IsChecked="{Binding SoundEnabled}"
                                      Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,6" />
                            <CheckBox Content="100% plays the ! (you know the one)"
                                      IsChecked="{Binding SnakeAtFull}"
                                      Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,14" />

                            <Button Style="{StaticResource FlatButton}" Content="Send test alert"
                                    Command="{Binding TestAlertCommand}" HorizontalAlignment="Left" />
                        </StackPanel>
                    </Border>
                </TabItem>
```

(If the window's string-visibility converter key differs from `StrVis`, use the key the file already declares — `MainWindow.xaml` uses `StrVis`; check `SettingsWindow.xaml`'s resources and reuse its equivalent.)

- [ ] **Step 3: Verify**

Run: `dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj && dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`
Expected: clean.

- [ ] **Step 4: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/App.xaml.cs
git commit -m "feat(app): Settings Alerts tab with thresholds, toggles, and test alert"
```

---

### Task 7: Smoke plan, final verification, PR

**Files:**
- Modify: `docs/smoke-test-plan.md` (append)

- [ ] **Step 1: Append the WS-B smoke section**

Append to `docs/smoke-test-plan.md` (match existing heading levels):

```markdown
## WS-B — threshold alerts (2026-07-12)

1. **Warn crossing.** Set Warn to a value just below a live tier's current %, wait for the next fetch (or click Refresh). Expect one toast naming the tier and % plus the soft two-note chime; no repeat on subsequent fetches.
2. **Urgent supersedes.** Set both thresholds below the live %, refresh. Expect a single Urgent toast (not two), replacing any prior toast for that tier in Action Center.
3. **Test button.** Settings ▸ Alerts ▸ Send test alert. Expect a Warn-style toast + chime regardless of thresholds.
4. **Focus Assist.** Enable Do Not Disturb, send a test alert. Expect: no audible chime; the toast lands in Action Center silently (deferred by Windows).
5. **Snake.** Enable "100% plays the !", send... nothing — the sting only binds to a real Full event. Verify by temporarily setting Warn/Urgent near a tier at 100% (or accept this as covered by the ChimeSynth unit tests plus scenario 3's pipeline coverage; the sting WAV can be auditioned by toggling the setting and hitting 100% naturally).
6. **Validation.** Set Warn 95 / Urgent 80. Expect the themed inline hint and no persistence (reopen Settings to confirm the old values).
7. **Recovery suspension.** Break the session (bogus key), confirm recovery card, then reauth. Expect no alert storm from the recovery/re-auth cycle.
8. **Velopack channel toast.** On an unpackaged (GitHub) install, send a test alert — the AUMID compat path must show a toast with the Sanduhr name/icon.
9. **Theming.** Flip default/dark/light/Matrix with the Alerts tab open — zero unstyled elements.
```

- [ ] **Step 2: Full verification**

Run: `dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj && dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`
Expected: build clean; 355 tests passing (333 + 18 engine + 4 chime).

- [ ] **Step 3: Commit, push, open the PR (do NOT merge — main is PR-only and the human merges)**

```bash
git add docs/smoke-test-plan.md
git commit -m "docs(test): WS-B smoke scenarios — crossings, DND, snake, both channels"
git push -u origin feat/ws-b-threshold-alerts
gh pr create --base main --head feat/ws-b-threshold-alerts --title "feat: threshold alerts — toasts, chimes, and the opt-in snake sting (WS-B)" --body "Implements docs/superpowers/specs/2026-07-12-threshold-alerts-design.md: pure AlertEngine truth table in Core (18 tests), toast delivery with AUMID compat for the Velopack channel, DND-gated procedural chimes, Settings Alerts tab, and the synthesized snake-sting homage at 100% (opt-in). Manual smoke pending per docs/smoke-test-plan.md WS-B section.

🤖 Generated with [Claude Code](https://claude.com/claude-code)"
```

---

## Plan self-review (done at authoring time)

- **Spec coverage:** §1 engine semantics → Task 1 (all truth-table rows tested); §2 delivery/toasts/AUMID/DND/quiet-failure → Task 4 + Task 5 hooks; §3 sounds → Task 2; §4 settings/tab/test-button/keys → Tasks 3+6; §5 theming → Task 6 + Global Constraints; error handling (never break fetch, recovery suspension, routines used/limit) → Tasks 1 (`EffectivePct`), 5 (try/catch + `Reset()` hooks); testing → Tasks 1-2 TDD + Task 7 smoke. **Deliberate deviation from spec:** the spec's "settings round-trip" unit tests are not implementable — `SettingsStore` lives in the App project, which the test project cannot reference (WPF TFM); coverage moves to `LoadAlertConfig`'s corrupt-pair defaults being exercised at App startup + smoke scenario 6.
- **Placeholder scan:** clean — every code step carries complete code; the two "check the existing style/key first" notes (ChimeSynthTests style, `StrVis` converter key, `TierModel` label source) are grounded verification instructions with concrete fallbacks, not deferred design.
- **Type consistency:** `TierAlertSnapshot/AlertConfig/AlertKind/AlertEvent/AlertEngine.Evaluate/Reset` (T1) ← T3 (`AlertConfig` in store), T5 (engine + snapshots); `PlayAlertWarn/Urgent/Snake` (T2) ← T4; `LoadAlertConfig/SaveAlertConfig/LoadAlertSound/LoadAlertSnakeFull/AlertSettingsChanged` (T3) ← T5; `AlertService.Deliver/DeliverTest` + `AttachAlertService`/`AlertService` property (T4/T5) ← T6. Verified consistent.
- **Known risk, flagged for the executor:** Task 4's TFM bump is the step most likely to surprise (CsWin32/WPF-UI interactions); it is deliberately isolated with its own build+test gate before the package lands.
