# Changelog

## v2.3.0-windows — 2026-05-06

**Platform:** Windows
**Feature release — Routines daily-quota tier, Local CC tab reading session JSONLs for live token-burn deltas, drag-reorder tier cards, custom dialog sound chimes, theme apply across all open dialogs, and a Win11 taskbar-icon binding fix.**

### Added

- **Routines (Daily Routines) tier.** Wires the `/v1/code/routines/run-budget` endpoint discovered via Cowork DevTools — needs `x-organization-uuid` + `anthropic-version` headers. Renders as a count card (e.g. `3/15`) instead of a percentage since it's a daily run-quota, not subscription utilization. Was parked at v2.2.0 with "different shape, undiscovered endpoint" — both unblocked.
- **Local CC settings tab.** Read-only view of token consumption from local Claude Code session JSONLs in `~/.claude*/projects/`. Side-by-side prominent numbers for Today vs Last 30 Days, a 30-day daily-bar strip, and per-project / per-skill breakdown tables (top 10 each). Show/hide breakdown toggle persists. Anthropic's `/usage` endpoint lags actual consumption by minutes — this is the canonical local view of what's actually being burned.
- **Local CC token-burn delta on tier cards + footer.** Each tier card overlays a live "+1.2k since fetch" badge and the footer shows " · CC +X" — both source from the local session logs and reset to zero on every successful API fetch (~5 min). Hover tooltips explain the semantics.
- **Cards tab — drag-and-drop tier reorder.** Settings → Cards (renamed from Pacing) uses a checkable `QListWidget` with internal-move drag-drop. Reorder by dragging; uncheck to hide. Saves `tier_order` alongside `hidden_tiers`. Tiers added in future releases fall in at the end automatically.
- **Speculative-tier "future use" tag.** Codename / null-returning tiers (`seven_day_cowork`, `seven_day_omelette`, `seven_day_oauth_apps`, `iguana_necktie`) carry an inline `· future use` label and a tooltip explaining they'll auto-render when Anthropic surfaces data. Leaving them checked is a free discovery cue.
- **Custom sound chimes.** Replaced every Windows system beep on dialog confirmations with four short PCM-WAV tones — success: C4-E4-G4 ascending; error: D4-B3-G3 descending; info: E4 two-beat; toggle: A4 single tap. Files lazy-built to `%APPDATA%\Sanduhr\sounds\*.wav` on first use, played via `winsound.PlaySound` with `SND_FILENAME | SND_ASYNC`. Disabled via `SANDUHR_SILENT_SOUNDS=1` for tests.
- **Apply Selected button on Themes tab.** Pick any installed theme + click Apply Selected → live theme switch without restart. Double-click on the list item also applies.
- **History tab visual polish.** Gridlines at 25/50/75/100%, area fills under each line, time-axis ticks, right-edge stacked label showing "X% left" + "resets in Yd Zh". Per-tier line is the theme accent in single-account mode; one colored line per account in All-accounts mode.
- **Auto-save on Pacing / Cards toggles.** Save button removed — toggles persist instantly with a click chime for confirmation.

### Changed

- **Settings tab: Pacing → Cards.** New name better reflects the tab's contents (visible tier cards + drag-reorder + pacing-tools toggle). Old `TAB_PACING` constant kept as an alias for backwards compat.
- **`extra_usage` tier label: "API Credits" → "Capped Extra Usage".** Clearer labeling for what the tier actually tracks.
- **Tier-card label collision on narrow widgets.** Width-responsive: secondary rows hide when the card width drops below 360 px so labels don't overlap.

### Fixed

- **Themes apply across child dialogs.** Qt stylesheets don't cross window boundaries by default; `apply_theme` now iterates `findChildren(QDialog)` and pushes the stylesheet at the source. Previously, applying a theme via Settings → Themes → Apply Selected updated the widget but left the open settings dialog stale until reopened.
- **Win11 taskbar icon binds at startup on frameless + topmost windows.** The `FramelessWindowHint + WindowStaysOnTopHint` combo on Win11 dropped the small-icon variant for the taskbar button at first show — neither Qt's `setWindowIcon` nor a direct `WM_SETICON` was enough; only an actual HWND recreation via `setWindowFlag` bound it. Widget now starts unpinned (clean HWND), forces a one-time recreation 150 ms after init, and replays the user's saved pin preference. Cosmetic side effects batched at the end so launch doesn't visibly flicker. Pin choice now persists across launches via a new `pinned` settings key.
- **Pin / Float footer label updates immediately on pin toggle** instead of waiting up to 30 s for the next tick.
- **Local CC tab no longer freezes on first open.** Aggregation moved to a `QThread` worker; tab shows "Loading…" placeholder until the disk walk returns. Subsequent ticks hit the warm 30 s cache and stay instant.
- **Local CC table no longer shows duplicate project rows** when the same project is accessed via different `cwd` paths (e.g. via symlink + directly). Tokens are summed under the display basename before the table renders.
- **Local CC totals split: Today + Last 30 days side-by-side.** Earlier single "Today's local CC" headline got read as the 30-day total because the bar strip's "Last 30 days" caption sat right next to it.
- **Error chime now audible.** Earlier `SND_NODEFAULT` flag interfered with `SND_MEMORY` payloads; switched to file-based playback for reliable audio across Windows configs.
- **Settings dialog re-styles on Apply.** Apply-theme on a paste no longer leaves the dialog stale; theme propagates to all open dialog windows.
- **History chart axis labels no longer clipped** by parent layout — `axis_h=22` reservation.

### Performance

- **Local CC mtime filter + single-pass aggregation + 30 s in-process cache.** Heavy CC users with hundreds of session JSONL files: sub-100 ms refresh after the first fetch (was multi-second freeze).

### Tests

- **Total: 273 / 273 tests pass** (up from 251 in v2.2.0).
- **+20 `test_cc_logs.py`** — discovery, parsing, mtime filter, model→tier mapping, by-day / by-project / by-skill aggregations, cache TTL, project_display_name normalization.
- **+6 `test_sounds.py`** — file-based playback, env-var silence, lazy WAV build, tone palette stability, soft-fail on platform without `winsound`.

### Known limitations

- The `/usage` endpoint Sanduhr fetches still lags actual consumption by minutes — same upstream property as before. The Local CC tab + per-tier delta badges are the live-burn workaround. Future PR may cross-reference Anthropic's `ccusage` data shape for sharper attribution.
- Mac parity for the new features (Routines, Local CC, drag reorder, custom sounds, taskbar icon fix) is deferred to a separate Mac-side release.

## v2.2.0-windows — 2026-05-05

**Platform:** Windows
**Feature release — multi-account support, per-account history, aggregated overlay views, plus the API Credits (`extra_usage`) tier and a sign-out flow that keeps your other accounts intact.**

### Added

- **Multi-account registry.** Track multiple Claude accounts in one Sanduhr install (e.g. Personal + Work). Per-account credentials in Windows Credential Manager under named slots (`sessionKey:{label}`, `cf_clearance:{label}`); a registry slot tracks the active account. Subscription usage, history, and CSV exports follow whichever account is active.
- **Settings → Accounts tab** between Pacing and History. List view of registered accounts with the active one marked. Buttons for `Add…` (name + sessionKey + cf_clearance), `Rename…`, `Set Active`, and `Remove…` (with confirmation). Empty-state hint walks first-time users through their first add.
- **Active-account label in the widget**, just below the status text. Renders the bare account name when only one is configured; renders `⇆ Name` when multiple are configured to indicate that clicking cycles through them. Hidden entirely when signed-out.
- **History tab — per-account / All-accounts toggle.** New combo box selects either a single account (theme-accent line per tier, identical to the existing single-account view) or "All accounts" (one colored line per account per tier, overlaid). Legend strip shows colored swatches when in All-accounts mode. CSV export honors the selection — per-account exports keep the original 3-column shape (`timestamp, tier, util_pct`); All-accounts exports add a 4th `account` column.
- **`extra_usage` (API Credits) tier.** The claude.ai usage endpoint returns API credit-spend tracking with `is_enabled / monthly_limit / used_credits / utilization / currency` — Sanduhr now records and renders the `utilization` value alongside the other tiers, with the label "API Credits".
- **Account-scoped sign-out.** Saving a blank sessionKey now removes only the active account (not your entire registry) and wipes only its history file. If other accounts remain, the widget switches to the next one and refreshes ("Switched to {Account}"). If the signed-out account was the last one, the existing signed-out flow runs.

### Changed

- **`history.json` is per-account.** Files now live at `%APPDATA%\Sanduhr\history.{Account}.json` so per-account wipes are atomic and aggregation happens at chart-render time. **Migration is automatic on first v2.2.0 launch:** any existing single-slot keyring credentials become a "Personal" account, and the legacy `history.json` is renamed to `history.Personal.json`. No data loss, no user prompt.
- **Settings tab order.** Themes · Pacing · **Accounts (NEW)** · History · Help · Credentials. The Credentials tab still operates on the active account, so single-account users see no UX change.

### Tests

- **+18 `test_accounts.py`** — registry CRUD (add, remove, rename, set-active), validation (label rules, duplicates), legacy migration (promote single-slot creds to "Personal"), edge cases (empty registry, unknown account, last-account removal).
- **+9 `test_history_per_account.py`** — per-account routing, isolation, explicit-account override, legacy `history.json` migration with idempotency, aggregate window, no-active-account no-op.
- **+8 `test_accounts_dialog.py`** — UI tests via pytest-qt: empty state, add-first-account becomes active, add-second doesn't change active, invalid name surfaces error, set-active emits credentials, rename, remove of active advances pointer, remove of last emits empty creds.
- **+5 `test_aggregated_chart.py`** — color stability (same label → same color), unknown-label fallback, aggregate `_data` shape, single-account filter, account round-trip.
- **+2 `test_csv_export.py`** — all-accounts CSV includes `account` column.
- **+1 `test_clear_credentials.py`** — multi-account sign-out leaves other accounts intact.
- **Updated** `test_credentials.py` keyring fixture (now patches the `keyring` module globally instead of the `credentials` module attribute, since `credentials.py` delegates to `accounts.py`).

**Total: 251 / 251 tests pass.**

### Known limitations

- The `/usage` endpoint Sanduhr fetches lags actual consumption by minutes — known property of the upstream API. Community tool [`ccusage`](https://www.npmjs.com/package/ccusage) reads local Claude Code logs for a more real-time view; a hybrid local-logs data source is a candidate for a future PR.
- Routines (Claude Code's cloud-hosted scheduled / API / GitHub-triggered runs, shipped 2026-04-14) have a daily quota that surfaces on `claude.ai/settings/usage` as `0/15` (Max plan) but is **not** returned by the `/usage` endpoint. Routines tracking is parked for v2.3.0 — different shape (count-based) and lives at a different (undiscovered) endpoint.

## v2.0.4-windows — 2026-04-19

**Platform:** Windows + macOS
**Feature release — advanced pacing, deep-work focus timer, cooldown snake game, hourglass physics tightening, and sign-out flow restoration.**

### Added

- **Always-on pace ghost.** A faint vertical tick on every tier card showing where pace says usage should be right now. Actual fill sits to the left (under pace), at (on pace), or to the right (ahead). Replaces the previous Projection graph mode + the bright `_pace_tick` widget that used to double-render at the same x-position. Ghost alpha tunable per theme via `ghost_alpha` (default 0.35, Matrix overrides brighter for CRT feel).
- **Horizon sparkline.** The pulse-histogram mode is replaced by a 4-band Heer/Tufte horizon chart — all bands render from the widget bottom with ascending alpha, so peaks pile up four-way overlap at the base and step down one band at a time as they reach the top. Denser information than a line chart, visually distinct enough that the graph-cycle button does real work. Cycle is now Classic / Horizon only.
- **Breathing glass.** Usage bars pulse with a 2.8s sine-wave alpha shimmer toward the theme's `accent` color, so the widget feels alive at rest. Amplitude 0.08 — subliminal, not flickery. Period tunable per theme via `breath_period_ms` (Matrix drops to 1800ms for a more CRT-alive cadence). 15fps timer, effectively free on CPU.
- **Edge-drag resize.** Hover any edge or corner of the frameless window → cursor changes → click-drag to resize. Minimum size dynamically computed from `QFontMetrics` of the longest user-facing strings (status messages, burn projections, tier labels) so text never clips. Resized geometry persists across launches via the `geom` key in `settings.json` (legacy `window` key migrated transparently on first save). Resize is disabled while the focus timer or snake overlay owns the window.
- **Advanced pacing tools.** Cards now surface **Cooldown required** (how long at zero usage to get back on pace) and **Surplus** (burn-rate delta when under pace) as deep-math metrics. Hover-to-reveal, not click, so you can glance and move on without stacking extra clicks.
- **Deep-work focus timer** (`⏳` in the tool strip). Replaces the tier cards with a 31×31 digitised hourglass overlay that drains in proportion to the remaining block. Inline minute-picker inside the overlay — no digging through Settings. Mac port renders via SwiftUI `Canvas` + `TimelineView`; Windows via `QPainter` + `QTimer`. Zero external dependencies on either platform.
- **Cooldown snake game** (`🐍` in the tool strip). A pure-Qt / pure-SwiftUI snake game for the wait state when you've blown through your budget and need to kill a few minutes. High score persists to `%APPDATA%\Sanduhr\settings.json` / macOS `UserDefaults`.
- **Graph-mode cycling** (`📊`). Cycles the tier-card graph between Classic / Projection / Pulse. `pacing.velocity_projection()` does the linear extrapolation for the Projection mode.
- **Tool strip** between content and footer. Consolidates Themes, Settings, Graph-mode, Compact, Focus, and Snake into persistent tooltip-bearing buttons with explicit `setAccessibleName` so screen readers — and MS Store review tooling — can identify every control by name, not by emoji glyph.
- **Sign out / clear credentials from the Credentials tab.** (Originally intended for v2.0.3 — see errata below.) Saving the Credentials form with an empty sessionKey triggers a confirmation dialog; confirming calls `credentials.clear()`, wipes `sessionKey` and `cf_clearance` from Windows Credential Manager, stops the fetcher, tears down tier cards, and sets the status to *"Signed out — paste sessionKey in Settings to resume."* Hint label under the sessionKey field advertises the behaviour so users discover it.

### Fixed

- **Hourglass sand-fall was drifting out of sync with wall-clock.** Two bugs in `focus._physics_tick`: (1) `elapsed` was derived from the integer-second `_remaining` counter, so the 30Hz physics tick had no sub-second resolution → sand stalled between 1Hz timer ticks and then dropped in clumps; (2) the bottleneck throttle only guarded the centre cell `(cy-1, cx)`, so diagonal falls from `(cy-1, cx±1)` into the bottom half bypassed the rate limit and the bottom half would outpace the timer over long focus blocks. Now uses `QElapsedTimer` for millisecond-resolution elapsed and throttles the entire `y == cy-1` row. Sand visibly drips in proportion to the fraction of the timer that's elapsed at all times.
- **Compact mode now actually shrinks the window.** Previously it only hid child widgets; the frame stayed the original size, wasting desktop space. Windows: `setFixedHeight(sizeHint.height())` after `adjustSize()`. Mac: SwiftUI `fixedSize` bounding.
- **Heartbeat pace marker is no longer clipped by the progress-bar container.** Moved from an inside-the-bar overlay to an absolute draw that protrudes a pixel above and below the bar, so it's visible at a glance regardless of fill level.

### Changed

- **Settings dialog tab order.** Themes first (quick win, low-stakes onboarding), Pacing + Help in the middle, Credentials last (the "spooky" one). Also dropped the `focus_minutes` option — the Focus overlay has its own inline minute-picker now — and replaced the *Auto-trigger snake* checkbox with a *Session 100% reminder* preference.
- **Title bar chrome simplified.** Settings and Key buttons removed from the top bar, now accessible from the tool strip. Cleaner native title-bar look.

### Tests

- **+8 `test_focus_physics.py`** — construction, start/stop lifecycle, float-vs-int regression guard for `expected_passed`, zero-duration edge case, pre-start tick, snake construction, snake wall/self-collision.
- **+4 `test_clear_credentials.py`** (restored from stranded commit `0a3b3d6`) — happy path, cancellation, non-blank regression, hint-label visibility.
- **+4 `test_pace_ghost.py`** — ghost position tracks `pace_frac`, ghost absent without reset data, theme `ghost_alpha` override, sane default alpha.
- **+5 `test_horizon_sparkline.py`** — horizon mode recognized, renders without crash, short-history no-op, line mode regression guard, peaks-render-darker-than-lulls pixel test.
- **+5 `test_breathing_glass.py`** — timer runs on construct, phase advances on tick, theme `breath_period_ms` override, sane default period, paint-doesn't-crash smoke test.
- **+6 `test_edge_resize.py`** — dynamic minimum size, zone detection (left / bottom-right corner / interior), minimum clamp, geometry persistence.
- **+11 Swift `UsageMathTests`** — new `mac/Tests/SanduhrTests/` target in `Package.swift` with baseline coverage for `parseISO`, `timeUntil`, `paceFrac`, `paceInfo`. Was zero Mac test coverage before — this is the scaffold to build on.
- Full Windows suite: **193 passed**, up from 161 on the pre-v2.0.4 state.

### Errata for v2.0.3

The v2.0.3 CHANGELOG and Notes-to-Publisher referred to a **sign out / clear credentials** feature. That commit (`0a3b3d6`) was pushed to the `fix/dialog-light-mode-readability` branch **44 minutes after PR #14 squash-merged**, so the feature was never in the main branch and never in the `v2.0.3-windows` tag or the MSIX that was uploaded to Microsoft Partner Center. The v2.0.3 shipped artifact contains only the light-mode dialog fix. The sign-out feature lands properly in this release (v2.0.4).

---

## v2.0.3-windows — 2026-04-17

**Platform:** Windows
**Patch release — Microsoft Store resubmission (light-mode dialog legibility).**

### Fixed

- **Dialogs unreadable on light-mode Windows.** Microsoft Store review (device: HP 17-bs011dx) caught the first-run welcome dialog and Settings dialog rendering as dark theme text on Windows' default *light* background — because the root stylesheet scoped its background to `SanduhrWidget` only, while cascading a light text color through `QWidget { color: ... }` to every widget including dialogs. On a light-mode host, the system filled the dialog background in white, producing light-on-white invisible text. Fix: explicit `QDialog`, `QMessageBox`, `QTabWidget`, `QTabBar`, `QLineEdit`, `QTextEdit`, `QListWidget`, `QDialogButtonBox`, and `QScrollBar` rules in the main stylesheet, all bound to the active theme's `bg` / `glass` / `text` / `border` / `accent` tokens. `_open_settings_dialog` now explicitly applies the root stylesheet to the dialog before `exec_()` — QDialog is a separate top-level window so it doesn't inherit QSS from its parent automatically. The `cf_clearance` help label in the Credentials tab no longer hardcodes its grey color (was invisible on Matrix theme and on light-mode Windows).

This was the root cause of the **10.1.4.4 App Quality / "Navigation of the product is poor"** finding in the last Store rejection. First-run setup is now legible regardless of the host system theme.

> **Errata:** the original v2.0.3 CHANGELOG also claimed a *Sign out / clear credentials* feature in an `### Added` section. That feature's commit was stranded on the branch after squash-merge and never shipped in the v2.0.3 MSIX. It lands in v2.0.4. See the v2.0.4 errata note for the full story.

---

## v2.0.2-windows — 2026-04-17

**Platform:** Windows
**Patch release — Microsoft Store resubmission (navigation + content policy).**

### Added

- **Windows-native close button.** Replaced the plain lowercase `x` with the
  proper heavy-multiplication glyph (×), widened to 46px to match Explorer /
  Settings title bars, and styled with the standard Win11 red hover
  (`#c42b1c`) + darker pressed state. Button order rearranged so close sits
  rightmost, matching every other Windows title bar.
- **Keyboard shortcuts.** `Ctrl+R` refresh, `Ctrl+,` open Settings,
  `Ctrl+D` toggle compact mode, `Ctrl+H` jump straight to the Help tab,
  `Alt+F4` close (stock). Surfaced in tooltips and the Help tab.
- **Settings dialog → Help tab.** Full keybinding list, widget interaction
  reference (drag anywhere, double-click title, right-click menu, theme
  strip, tier-card elements), and quick links to the source repo + privacy
  policy. Scrollable, rich-text, with real `<a>` hyperlinks.
- **First-run tip banner.** Dismissible one-liner below the theme strip on
  first launch: *"💡 Drag anywhere to move · double-click title for compact
  · right-click for menu."* Dismissal persists to
  `%APPDATA%\Sanduhr\settings.json`.
- **Tooltips on chrome buttons.** Settings / Refresh / Pin / × / title bar
  each get a short hover tooltip including the relevant keyboard shortcut.
  Deliberately kept off internal elements — no tooltip spam.
- **Accessible names** on every chrome button so screen readers and Store
  review tooling can identify controls.

### Fixed

- N/A (all additions).

### Why

Microsoft Store review rejected v2.0.1 under policy **10.1.4.4** with three
specific concerns: (a) "Content" / trademark disclosure, (b) "unique lasting
value," (c) "navigation is poor." This release targets (c) directly with
real affordance work: explicit Help surface, keyboard shortcuts, native
close-button styling, and a discoverable first-run tip. (a) and (b) are
addressed in parallel via an updated Store listing description — no code
changes needed for those. Ships alongside the Store resubmission.

## v2.0.1-windows — 2026-04-16

**Platform:** Windows
**Patch release — cosmetic + CI quality-of-life + Microsoft Store scaffold.**

### Added

- Inno Setup installer wizard now uses real 626 Labs artwork on both the
  Welcome/Finish banner (164×314) and the interior-pages small banner (55×58),
  generated by `windows/installer/make-banners.py` from the same
  `icon/source.png` as the `.exe` icon.
- MSIX packaging scaffold under `windows/msix/`:
  `Package.appxmanifest.template` with 626Labs LLC identity (reserved in
  Microsoft Partner Center), `make-msix-images.py` generating the required
  tile/logo PNGs, and `make-msix.ps1` producing a Store-ready unsigned MSIX
  from the PyInstaller output. Release workflow now publishes both
  `Sanduhr-Setup-vX.Y.Z.exe` (direct download) and `Sanduhr-vX.Y.Z.0.msix`
  (upload to Partner Center — MS Store signs on ingestion).

### Fixed

- `.github/workflows/windows-release.yml` installed Inno Setup via chocolatey
  with a pinned `--version=6.2.2`, which failed on `windows-latest` runners
  because they ship a newer (6.7.1) pre-install. Replaced with an idempotent
  presence check that only installs when missing.
- `Sanduhr.iss` hardcoded `#define MyAppVersion "2.0.0"` would override
  the `/DMyAppVersion=X.Y.Z` flag that `build.ps1` passes from
  `pyproject.toml`, producing `Sanduhr-Setup-v2.0.0.exe` regardless of the
  real version. Wrapped in `#ifndef` so `/D` wins.

## v2.0.0-windows — 2026-04-16

**Platform:** Windows
**Breaking change:** Full rewrite. v1 `sanduhr.py` still works on `main` for users who prefer running from source.

### Added

- PySide6 rewrite -- native Qt 6 widget, proper DPI awareness, Segoe UI Variable throughout
- Win11 Mica backdrop -- real translucent system material, with Win10 solid-color fallback
- Glass-stacking visuals -- per-theme glass tuning (Obsidian densest, Mint airiest, Matrix opaque with phosphor glow)
- Windows Credential Manager storage via `keyring` (service `com.626labs.sanduhr`, matching Mac Keychain)
- `cf_clearance` fallback field in credentials dialog
- Right-click context menu: Refresh / Compact / Credentials / Quit
- Drag anywhere to move (not just the title bar)
- Window position persistence in `%APPDATA%\Sanduhr\settings.json`
- Inno Setup installer -- `Sanduhr-Setup-v2.0.0.exe` from GitHub Releases
- Matrix theme: Cascadia Code monospace, phosphor glow, Mica opt-out
- Silent v1 migration: session key, theme, history carry over on first v2 run
- GitHub Actions: pytest on every push, release workflow on `v*.*.*-windows` tags

### Removed

- `--break-system-packages` pip shim (replaced by pinned dependencies in the installer)
- Hardcoded initial window position (replaced by last-known-position / centered default)

## v1.1 — 2026-04-15

### Added
- **Matrix theme** — green-on-black terminal aesthetic
- **Burn rate projection** — warns when current pace will exhaust usage before the reset period ends
- **Sparklines** — inline trend charts showing usage velocity over the last 2 hours (24 data points at 5-min intervals)
- **Pace markers** — colored tick on each progress bar showing where "on pace" is right now
- `.gitignore` — excludes config, docs, and Python cache from repo

### Changed
- Theme selector moved from title bar button to a dedicated strip below the title bar with all themes visible
- Burn rate framing revised: only warns when exhaustion happens before reset, not raw "hits 100% in X hours"

## v1.0 — 2026-04-15

### Added
- Real-time usage bars for all active Claude.ai tiers (Session 5hr, Weekly All Models, Sonnet, Opus, Cowork, Routines)
- Linear pacing engine with "On pace" / "X% ahead" / "X% under" indicators
- Reset countdown ("3d 5h 44m") and reset date/time ("Sun 1:00 AM") display
- 4 themes: Obsidian, Aurora, Ember, Mint
- Compact mode (double-click title bar)
- Always-on-top with pin/unpin toggle
- Draggable frameless window with custom title bar
- Extra usage / overage credit tracking
- "Use Sonnet" quick link in footer
- Auto-refresh every 5 minutes, countdown tick every 30 seconds
- Graceful in-place UI updates (no flicker)
- Auto-install of cloudscraper dependency on first run
- Session key setup dialog on first launch
