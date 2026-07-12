# WS-B — Threshold alerts (design)

Approved forks 2026-07-12: toasts + soft chime (A); v1 = threshold crossings + burn-projection, reset notification off-by-default (B); global thresholds 80/95 with a Settings ▸ Alerts tab, per-tier overrides deferred (C); 100% earns the Snake "!" as an opt-in easter egg (D). Second workstream from `docs/roadmap-2026-07-11.md`. Windows build only.

## Problem

The product promise is "know when you'll hit your Claude weekly cap before you do" — but the .NET build contains zero notification code (recon 2026-07-11: no toast/alert path anywhere in `windows-dotnet/src`). The Python build's "session 100% reminder" died in the port. Today the promise holds only while the user is looking at the widget.

## Goals

- Threshold-crossing alerts per tier (global warn/urgent levels), burn-projection alerts ("on pace to hit the weekly cap before reset"), delivered as Windows toasts plus a soft procedural chime.
- Alert logic pure and truth-table-tested in Core; delivery thin in App.
- Respects Focus Assist / do-not-disturb; never nags (hysteresis + once-per-window semantics).

## Non-goals

- Per-tier threshold overrides (deferred until asked for; the engine keys off a config object so adding them later is additive).
- Email/phone/push — everything is local, per the privacy posture.
- Multi-account alerts (active account only, consistent with the fetch; WS-D revisits).
- Shipping the actual MGS sample — see Sounds.

## Design

### 1. Core: `AlertEngine` (new, pure)

`Sanduhr.Core/AlertEngine.cs` — no timers, no I/O, no WPF. One entry point:

```csharp
public sealed record AlertConfig(
    bool Enabled, int WarnPct, int UrgentPct,
    bool ProjectionEnabled, bool ResetEnabled);

public enum AlertKind { Warn, Urgent, Full, Projection, Reset }

public sealed record AlertEvent(AlertKind Kind, string TierKey, string TierLabel, int UtilizationPct, string? ResetsAt);

public IReadOnlyList<AlertEvent> Evaluate(TierSnapshot[] previous, TierSnapshot[] current, AlertConfig config, DateTimeOffset now);
```

Semantics (the truth table the tests pin):

- **Crossing, not level:** an alert fires when a tier moves from below a threshold to at-or-above it between two evaluations. Warn (default 80), Urgent (default 95), Full (100 — distinct kind so the Snake sting can bind to it).
- **Hysteresis re-arm:** once fired, a threshold does not re-fire until the tier drops below `threshold − 5` or its reset window rolls over (`resets_at` changed). No 5-minute nag loops.
- **Window rollover resets all armed state** for that tier — a new week gets fresh alerts.
- **Projection:** fires once per window when the tier's projection flips to `expires_before_reset` (reusing `Pacing`'s existing burn/velocity projection — the same math the cards render). Re-arms on window rollover or when the projection flips back false for 2+ consecutive evaluations (flap suppression).
- **Reset (opt-in, default off):** fires when a window rolls over AND the previous window's peak was ≥ WarnPct — "your weekly reset landed, tank's full" is only news if the tank was ever low.
- **Ordering:** at most one toast per tier per evaluation — highest severity wins (Full > Urgent > Warn > Projection); Reset is independent.
- Armed-state lives in an `AlertEngineState` object owned by the caller; in-memory only. App restart may re-fire at most one alert per already-crossed tier — acceptable, documented, not persisted (a settings-persisted dedupe is complexity the failure doesn't justify).

### 2. App: delivery

- **`AlertService`** (new, `Sanduhr.App/Services/`): consumes `AlertEvent`s, renders toasts + plays the chime. Wired in `WidgetViewModel.RefreshAsync` after a successful fetch: evaluate old-vs-new tier snapshots, dispatch.
- **Toasts** via `Microsoft.Toolkit.Uwp.Notifications` (`ToastContentBuilder`): MSIX channel gets toast identity free; the Velopack/unpackaged channel uses the library's unpackaged support (AUMID + shortcut registration — Velopack already creates the Start Menu shortcut). Toast content: tier label, percentage, reset countdown, e.g. *"Weekly (Opus) at 95% — resets in 2d 4h."* Projection: *"On pace to hit the weekly cap ~6h before reset."* Clicking a toast focuses the widget.
- **Focus Assist / DND:** toasts defer natively via Windows. The **chime** additionally checks `SHQueryUserNotificationState` and stays silent during busy/fullscreen/quiet states (the OS won't gate app-played audio for us). One interop helper, reusable later by Ghost Mode if that ships.
- **Quiet failure:** if toast registration/dispatch throws (notification platform quirks), log to `sanduhr.log` (no labels — WS-A convention) and continue; alerts must never break the fetch loop.

### 3. Sounds

- Two new **procedural** chime kinds in `ChimeSynth`: `AlertWarn` (soft two-note), `AlertUrgent` (slightly firmer, still soft — house philosophy: never Windows system sounds).
- **Snake "!" (opt-in, default off):** the `Full` (100%) event, when the user enables "Snake mode" in the Alerts tab, plays a **procedurally synthesized two-tone "!" homage sting** — evoking the MGS alert without shipping the copyrighted sample (a ripped sample is a Store-cert and copyright risk; the synth homage is ours). Label in Settings: *"100% plays the ! (you know the one)"*. When off, Full uses `AlertUrgent`.

### 4. Settings ▸ Alerts tab (themed)

New tab in `SettingsWindow` between Themes and History, all `Sanduhr.Brush.*` tokens, no literals:

- Master toggle **Alerts** (default ON — alerting is the product's job; first-run behavior is sane at 80/95).
- Warn threshold + Urgent threshold (numeric steppers, 50–99, Warn < Urgent enforced on save; invalid input reverts with themed inline hint).
- Toggles: Projection alerts (default on) · Reset notifications (default off) · Sound (default on) · Snake at 100% (default off).
- **"Send test alert"** button — fires a fake Warn toast + chime through the real pipeline (the support answer to "is it working?").
- `SettingsStore` keys (key-per-preference pattern): `alerts_enabled`, `alert_warn_pct`, `alert_urgent_pct`, `alert_projection`, `alert_reset`, `alert_sound`, `alert_snake_full`.

### 5. Theming

In-app surfaces (Alerts tab, inline validation hints) follow the WS-A rule: `DynamicResource Sanduhr.Brush.*` only, themed dialogs only, live theme-flip clean. Toasts render in the OS shell style by design — outside theming scope, stated here so the acceptance check doesn't chase them.

## Error handling

- Engine evaluation wrapped so an exception never aborts `RefreshAsync` (log, skip cycle).
- Missing/null `resets_at` (routines tier) ⇒ tier participates in threshold alerts (`used/limit`-derived percentage where utilization is null) but not projection/reset alerts.
- Recovery states (Expired/Blocked) suspend evaluation — no alerts off stale `_lastData`.

## Testing

- **Core (xUnit), the bulk:** `AlertEngineTests` truth table — first crossing fires; sustained level doesn't re-fire; hysteresis re-arm at threshold−5; window rollover re-arms; Warn+Urgent crossed in one jump ⇒ single highest-severity event; Full distinct from Urgent; projection fires once, flap-suppressed, re-arms on rollover; reset fires only when previous peak ≥ Warn; disabled config ⇒ empty; Warn<Urgent validation.
- **Settings round-trip** for the seven keys.
- **Manual smoke additions:** toast on both channels (MSIX + Velopack/AUMID path); Focus Assist suppression (chime silent, toast deferred); test-alert button; Snake sting audible only when opted in; theme flip across the Alerts tab.

## Blast radius

`WidgetViewModel.RefreshAsync` (dispatch hook only), `SettingsStore` (+7 keys), `SettingsWindow.xaml` (+tab), `SettingsViewModel` (+child VM), `ChimeSynth` (+2 tones +sting), new `AlertEngine`/`AlertService`/`AlertsViewModel`, one new NuGet (`Microsoft.Toolkit.Uwp.Notifications`). No transport, fetcher, account, or tier-model changes.

## Effort

M. Suggested order: AlertEngine + tests (Core) → SettingsStore keys + Alerts tab → AlertService/toasts (MSIX first, then AUMID) → chimes + Snake sting → smoke.
