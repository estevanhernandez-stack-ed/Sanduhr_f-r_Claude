# Sanduhr für Claude — Windows v2.0 "Diamond" Design

**Status:** Approved (brainstorming → implementation planning)
**Date:** 2026-04-16
**Branch target:** `windows-native` (cut from `main`)
**Authors:** Este + Stitch

## Goal

Take the current 615-LOC `sanduhr.py` Python/tkinter widget on `main` and rewrite it as a polished, installable Windows application that achieves visual and feature parity with the Mac SwiftUI version on the `mac-native` branch. Ship it as a signed-ready `.exe` installer downloadable from GitHub Releases.

The Mac version was a native SwiftUI rewrite with NSVisualEffectView vibrancy, Keychain-backed credentials, and a drag-to-install DMG. Windows must match that quality bar without requiring a separate full-native rewrite (no C# / WinUI / MSIX). The chosen path is **PySide6 + Inno Setup**: Qt 6 gives real Win11 Mica, Credential Manager via `keyring`, proper DPI scaling, and Segoe UI Variable typography, while keeping the implementation in Python so feature ports from Swift remain fast and cheap.

## Non-goals

- **Not a native C# / WPF / WinUI rewrite.** Evaluated and explicitly rejected — the extra week of work buys a marginal polish delta no user will notice.
- **Not Tauri / shared-codebase.** Mac's SwiftUI work stays; we don't throw it away for uniformity.
- **Not MSIX / Microsoft Store.** Inno Setup `.exe` is the target. MSIX can be revisited if/when the project goes public.
- **Not code-signed at v2.0.** SmartScreen warning is acceptable for a side-project release. Signing infrastructure can be added in a two-line build step later.
- **Not a visual companion mockup tool.** The Mac version is the live visual reference.
- **Not a tray-only app.** Widget stays visible in the taskbar (user preference — matches how they position the Mac version on the menu bar).

## Architecture

### Repo & branch strategy

Non-disruptive. `main` is not touched until both platforms are ready to unify.

- Cut `windows-native` from `main`.
- Add a new `/windows/` folder to mirror the existing `/mac/` folder structure.
- Leave the existing root `sanduhr.py` (v1.x) untouched — anyone who clones `main` today still gets a working widget.
- Add `/docs/specs/` for cross-platform feature specs going forward (each new feature = one spec, two ports).

### Target layout on `windows-native`

```text
/mac/                            (existing, untouched)
/windows/
  src/
    sanduhr/                     # Python package — import sanduhr
      __init__.py
      app.py                     # QApplication bootstrap, app ID, entry point
      widget.py                  # SanduhrWidget(QWidget) — frameless main window
      tiers.py                   # TierCard(QFrame) — per-tier card rendering
      sparkline.py               # Sparkline(QWidget) — QPainter sparkline
      api.py                     # ClaudeAPI — cloudscraper wiring
      credentials.py             # keyring-backed credential store + v1 migration
      pacing.py                  # pure functions ported verbatim from v1
      history.py                 # sparkline history in %APPDATA%
      themes.py                  # THEMES dict + glass tuning + apply()
      paths.py                   # %APPDATA%\Sanduhr helpers
      fetcher.py                 # UsageFetcher(QObject) — threaded refresh
  icon/
    Sanduhr.ico                  # multi-res (16/20/24/32/40/48/64/128/256)
    source.png                   # committed source artwork (same as Mac)
    make-icon.ps1                # regenerate .ico from source.png
  installer/
    Sanduhr.iss                  # Inno Setup script
    banner.bmp                   # setup wizard banner artwork
  build.ps1                      # PowerShell build orchestrator
  requirements.txt               # PySide6, cloudscraper, keyring
  sanduhr.spec                   # PyInstaller spec
  version_info.txt               # Windows .exe resource metadata
  README.md                      # Windows build/run docs
  TEST_PLAN.md                   # manual test checklist
/sanduhr.py                      (existing v1.x, untouched)
/docs/
  specs/                         (new — shared feature specs)
  screenshots/
    windows/                     (reference renders for regression checks)
  superpowers/specs/             (brainstorm + implementation artifacts)
```

### Module responsibilities

Each module is small, single-purpose, and independently testable. Rough target: no file over 300 LOC.

- **`app.py`** — creates `QApplication`, sets Windows AppUserModelID to `com.626labs.sanduhr` (so the taskbar groups correctly), loads the icon, instantiates `SanduhrWidget`, runs the event loop. ~30 LOC.
- **`widget.py`** — `SanduhrWidget(QWidget)`. Frameless always-on-top window with custom title bar. Holds the title bar, theme strip, tier card container, footer. Manages drag, double-click-to-compact, right-click context menu, button wiring, window position persistence. Subscribes to `UsageFetcher` signals.
- **`tiers.py`** — `TierCard(QFrame)`. One per active tier. `update(util, resets_at, history)` mutates in place — no rebuild, no flicker.
- **`sparkline.py`** — `Sparkline(QWidget)`. QPainter-based, anti-aliased, DPI-aware. `setValues(list[int])` + `paintEvent`.
- **`api.py`** — `ClaudeAPI` wrapping cloudscraper. `get_usage()` returns raw dict. Accepts optional `cf_clearance`. Raises `SessionExpired` / `CloudflareBlocked` / `NetworkError` custom exceptions.
- **`credentials.py`** — keyring-backed store. `load()`, `save(session_key, cf_clearance)`, `clear()`. Service `com.626labs.sanduhr`, accounts `sessionKey` and `cf_clearance`. Includes v1 → v2 migration (reads `~/.claude-usage-widget/config.json`, writes to keyring, moves theme preference to `%APPDATA%\Sanduhr\settings.json`, deletes old file).
- **`pacing.py`** — pure functions: `pace_frac`, `pace_info`, `burn_projection`, `time_until`, `reset_datetime_str`. Ported verbatim from v1. The math is correct; we don't rewrite it.
- **`history.py`** — `append(tier_key, util)`, `load(tier_key)`. 24-point rolling window, JSON at `%APPDATA%\Sanduhr\history.json`.
- **`themes.py`** — `THEMES` dict with per-theme colors + glass tuning dials (see Visual Design below). `apply(widget, theme_key)` helper sets the Qt palette and window backdrop.
- **`paths.py`** — `app_data_dir()` returns `%APPDATA%\Sanduhr`. Creates on demand.
- **`fetcher.py`** — `UsageFetcher(QObject)` lives on a `QThread`. `fetch()` slot runs on the worker; emits `dataReady(dict)` / `fetchFailed(type, message)` via queued connections.

### Threading model

Network calls never run on the GUI thread.

```text
QTimer (main thread, 5-min cadence)
   ↓ (queued connection)
UsageFetcher.fetch()  [worker thread]
   → api.get_usage()
   → history.append() for each tier
   ↓ (emits signal)
SanduhrWidget slot  [main thread]
   → TierCard.update() per tier
   → footer timestamp update
```

Countdown tick (30s) runs entirely on the main thread — no network, just updates reset-in labels and pace marker positions.

### Data flow on refresh

1. `QTimer` fires every `REFRESH_MS` (5 min) — or user clicks Refresh, or initial bootstrap.
2. `UsageFetcher.fetch()` runs on worker thread.
3. Calls `ClaudeAPI.get_usage()` → returns dict.
4. Appends each tier's utilization to `history`.
5. Emits `dataReady(dict)` via queued connection.
6. Main thread slot receives, iterates tiers, calls `TierCard.update()` on existing cards, creates new cards for newly-active tiers, destroys cards for gone tiers.
7. Footer label updates to "Updated HH:MM AM/PM | Pinned".

## Feature parity

### Kept from v1 (verbatim behavior)

- Real-time bars for all active Claude.ai usage tiers.
- 5 themes: Obsidian, Aurora, Ember, Mint, Matrix.
- Compact mode (double-click title, shows only highest-usage tier).
- Pin/unpin always-on-top.
- 5-minute auto-refresh.
- 30-second countdown tick.
- In-place updates (no flicker).
- "Use Sonnet" footer link (opens `claude.ai/new?model=claude-sonnet-4-6`).
- Burn rate projection ("At current pace, expires in Xh Ym").
- Pace markers on bars.
- Sparklines (24-point rolling window, last 2 hours).
- Extra usage / overage credit display.

### Ported from Mac (new on Windows)

- `cf_clearance` fallback field in credentials dialog.
- Cloudflare block detection in API layer — error message surfaces "add cf_clearance" hint with second-field focus.
- Right-click context menu on the widget → Refresh / Compact / Credentials… / Quit.
- Window position persistence to `%APPDATA%\Sanduhr\settings.json`.
- Drag anywhere on the widget to move (not just title bar).
- Credential Manager storage via `keyring` — no more plaintext JSON holding a live session token. Service name `com.626labs.sanduhr`, matches Mac Keychain exactly.

### New (enabled by PySide6 rewrite)

- Windows 11 Mica backdrop — real translucent material, runtime-detected with Win10 fallback.
- Segoe UI Variable throughout (Segoe UI fallback on older Windows).
- DPI awareness — crisp on 4K, correct multi-monitor scaling.
- Cascadia Code monospaced font for Matrix theme numbers and timestamps.
- QPainter anti-aliased sparklines (v1 tkinter `smooth=True` was a janky approximation).
- Multi-resolution `.ico` (16/32/48/64/128/256) from the Mac source artwork.
- Explicit error states: "Session expired" vs "Cloudflare blocked" vs "Network error" — each with tailored remediation copy.

### Removed from v1 behavior

- `--break-system-packages` pip install shim at script head. v2 ships with pinned dependencies in the installer; the shim was only for the "just run the script" flow, which no longer applies. The v1 `sanduhr.py` at repo root keeps it.
- Hardcoded initial window position (bottom-right). Replaced by last-known-position persistence, or centered-on-primary-display on first run.

## Visual design

### The diamond pass — glass stacking

**Window-level material: Win11 Mica backdrop.** Applied via direct DWM API call from `ctypes`: `DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, byref(DWMSBT_MAINWINDOW), sizeof(DWORD))`. Runtime-detected via Windows build number (22000+ for Mica support). Falls back on Win10 / unsupported to a solid theme `bg` color with a manually-painted rounded rect (`WA_TranslucentBackground` + `paintEvent`). Single helper `apply_mica(widget)` in `themes.py`; one implementation, two triggers, three code paths (Mica supported / Mica unsupported on Win11 / Win10).

**Rounded window corners: 8px.** DWM handles on Win11 via `DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND`. Win10 fallback uses the same custom `paintEvent` as the Mica fallback.

**Card layer — translucent glass stacking:**

- Background: theme `glass_on_mica` color at the theme's tuned alpha (see per-theme table below).
- Border: 1px, theme-tinted, tuned alpha.
- Corner radius: 10px (slightly more than the window, so cards feel inset).
- Drop shadow via `QGraphicsDropShadowEffect`: offset `(0, 2)`, blur 12, color black α 0.25.
- Interior padding: 14px × 12px.
- Optional 1px inner highlight at the top edge, theme-tinted at low alpha — simulates a refracted light catch.

**Progress bar polish:**

- Bar fill: subtle vertical gradient (top 8% brighter, bottom 8% darker than base usage color).
- Pace marker: 3px-wide tick with a tight glow effect (inner shadow blur 4, same color as marker).
- Bar container: 10px rounded, slightly translucent so the card glass shows through the empty portion.

**Accent line at window top:** 2px horizontal gradient from accent 100% left to accent 20% right.

**Typography:**

- Segoe UI Variable throughout. Fallback: Segoe UI.
- Tier label: weight 600, size 10, tracking -0.2.
- Percentage number: weight 700, size 14, tabular-nums (digits don't jitter on update).
- Secondary/muted text: weight 400, sizes 8–9.
- Matrix theme swaps Cascadia Code (fallback Consolas) for percentages and timestamps — see 4b.

**Window chrome continuity:** No separate title bar background. Title bar, theme strip, and content flow as one continuous glass surface over the Mica, matching the Mac's unified-pane-of-glass look.

**Hover / interaction:**

- Title-bar buttons on hover: subtle rounded highlight at α 0.08 of theme `text`. Qt stylesheet `:hover`.
- Theme strip active button: accent text color + 1px accent underline (α 0.6) instead of v1's bold+color combo.
- No scroll bounce, no fade-ins, no "impressive" animations. Glass should feel solid and crisp.

### Per-theme glass tuning

Each non-Matrix theme has three glass dials defined in `themes.py` next to its color palette.

| Theme | Card α | Border glow | Accent bloom | Inner highlight | Notes |
| --- | --- | --- | --- | --- | --- |
| **Obsidian** | 0.68 | α 0.30 neutral | blur 4, α 0.35 | off | Heaviest glass. Purple-tinted Mica showing through dense panes. The restrained default. |
| **Aurora** | 0.55 | α 0.50 cyan | blur 6, α 0.55 | 1px cyan α 0.20 | Ice/sky energy. Cards read as frozen panes. Cyan highlight catches light like refraction. |
| **Ember** | 0.62 | α 0.40 orange | blur 6, α 0.55 | 1px orange α 0.18 | Cozy. Orange bloom on percentages simulates embers behind amber glass. |
| **Mint** | 0.50 | α 0.45 teal | blur 4, α 0.35 | 1px teal α 0.22 | Airiest. Most translucency — Mica bleeds through most, cards feel like glass leaves. |
| **Matrix** | 1.00 (opaque) | phosphor α 0.50 green | n/a (glow instead) | off | Opts out of the glass pass — see 4b. |

**Dial definitions:**

- **Card α** — alpha on `glass_on_mica` when painting each card. Lower = airier, higher = denser.
- **Border glow** — 1px border color + α, theme-tinted. Simulates refractive edge of real glass.
- **Accent bloom** — `QGraphicsDropShadowEffect` on accent-colored elements (percentage numbers, pace labels, accent line).
- **Inner highlight** — optional 1px theme-tinted line at card top edge, low alpha. Simulates light catch on wet glass top edge.

**Implementation pattern:** `TierCard` reads these from the active theme dict. No per-theme branching in widget code — the card paints whatever the theme says.

**Performance tradeoff:** drop shadows apply only to static elements (card outer frame, percentage text). The progress bar fill, which updates on every refresh, stays flat. Win10 fallback path can dial back further if any frame hiccup appears.

**v1 solid `glass` colors preserved** for the Win10 fallback (non-Mica) path. New `glass_on_mica` values are tuned for α blending over live blurred backdrop — each theme gets a touch lighter and slightly more saturated:

- Obsidian: `#1c1c1c` → `#2a2a2e`
- Aurora: `#161e30` → `#1f2a42`
- Ember: `#261414` → `#331b1b`
- Mint: `#122a1e` → `#18392a`
- Matrix: unchanged (opts out of Mica)

### 4b — Matrix theme special handling

**Design brief (committed to spec):** *A terminal on a Macintosh in 2030, but in the Matrix.* Future-retro. Restrained. Phosphor-green on deep black. Apple minimalism filtering raw CRT energy — no bevels, no skeuomorphism, no gimmicks.

**Overrides when `theme_key == "matrix"`:**

- **No Mica.** Force `DWMSBT_NONE`. Window backdrop is solid `#020a02`. No translucency, no desktop bleeding through. Matrix is opaque.
- **No card translucency.** Cards render at α 1.0. Stacking effect switches from translucent-glass to thin bright borders — 1px `#00ff41` at α 0.5 on each card edge.
- **Phosphor glow on numbers.** Percentage labels get `QGraphicsDropShadowEffect` color `#00ff41`, offset `(0, 0)`, blur 4, α 0.6. Subtle bloom; reads as alive, not flat.
- **Cascadia Code** for percentages, countdowns, reset timestamps. Fallback: Consolas. Tier labels stay in Segoe UI — restraint, not pastiche.
- **Tabular-nums enforced** on all Cascadia Code numerics.
- **Progress bar:** fill solid `#00ff41`, bar bg `#0a1a0a`. Pace marker: 2px bright red `#ff0040` with its own glow.
- **Sparkline:** bright phosphor green, 2px stroke, tight glow. Reads like a scope trace.
- **Accent line:** solid `#00ff41`, 2px, with a 6px vertical glow below fading to transparent. "Horizon of a terminal display."
- **Hover:** title-bar buttons get thin `#00ff41` underline at α 0.4, no filled rect.
- **Card corner radius:** 2px (barely perceptible). Glass themes get rounded; Matrix gets geometry.
- **Window corners:** stay rounded at 8px — the Macintosh part of the brief.

**Deliberately NOT added to Matrix:**

- No digital rain, no falling code animation.
- No scanlines.
- No typewriter text reveals. "Update in place, no flicker" invariant holds everywhere, including Matrix.

**Storage:** overrides live in `themes.py` as a `MATRIX_OVERRIDES` dict the theme apply function checks. Explicit and editable in one place.

## Packaging & distribution

### PyInstaller build

- Mode: **one-folder** (not one-file). Faster startup, easier crash diagnosis; installer unpacks to `Program Files` anyway so one-file buys nothing.
- Spec file `sanduhr.spec` committed to `windows/`.
- Entry point: `src/sanduhr/app.py`.
- Name: `Sanduhr`.
- Windowed (no console attached).
- Icon: `icon/Sanduhr.ico`.
- Hidden imports declared: `cloudscraper`, `keyring.backends.Windows`, `PySide6.QtSvg`.
- Excluded Qt modules: `QtWebEngine`, `QtMultimedia`, `QtQuick3D` (unused — cuts ~120MB from bundle).
- Output: `dist/Sanduhr/` — ~80–100MB uncompressed, ~35–45MB compressed in the installer.

### Windows .exe resource metadata

Embedded via PyInstaller from `version_info.txt`:

- `FileVersion` / `ProductVersion`: e.g. `2.0.0.0`.
- `CompanyName`: 626Labs.
- `FileDescription`: Sanduhr für Claude — Usage Tracker.
- `LegalCopyright`: MIT.
- `ProductName`: Sanduhr für Claude.

Right-click `Sanduhr.exe` → Properties → Details will show these. The difference between "looks like a real app" and "some random Python script."

### Icon generation

- `icon/make-icon.ps1` reads `icon/source.png` (same artwork as Mac `.icns`), produces `Sanduhr.ico` with resolutions 16, 20, 24, 32, 40, 48, 64, 128, 256.
- Uses ImageMagick if present, falls back to Pillow bundled with the Python build env.
- Pre-generated `Sanduhr.ico` committed to repo — build does not require ImageMagick.

### Inno Setup installer (`installer/Sanduhr.iss`)

- **App ID GUID** pinned (stable across versions; upgrades replace rather than stack).
- **Install location:** `{autopf}\Sanduhr` → `C:\Program Files\Sanduhr` on 64-bit. User-level fallback `{userpf}\Sanduhr` if no admin.
- **Components:**
  - Core app (required)
  - Start Menu shortcut (default on)
  - Desktop shortcut (default off)
  - Launch at login (default off — opt-in; writes to `HKCU\...\Run\Sanduhr`)
- **Uninstaller:** removes install dir. Preserves `%APPDATA%\Sanduhr\` by default; offers "Also remove my settings and history" checkbox. Always clears Credential Manager entries.
- **Setup wizard:** standard Inno style, themed banner image with Sanduhr artwork. No custom pages, no marketing.
- **Output:** `build/Sanduhr-Setup-v{version}.exe`.

### Code signing — deliberately skipped for v2.0

- SmartScreen "Windows protected your PC" warning on first run is expected for unsigned apps.
- Docs explicitly tell users: *"Click More info → Run anyway. This is expected for unsigned apps."* Matches Mac `.app` ad-hoc sign guidance.
- OV code signing cert ~$80–200/yr. Not worth it for side-project scope.
- When signing is added later: `signtool.exe` step inserted into `build.ps1` between PyInstaller and Inno Setup. Two-line change.

### Release flow

- Tag `v2.0.0-windows` on `windows-native` branch.
- GitHub Actions workflow `.github/workflows/windows-release.yml` — on tag push, spins up a Windows runner, runs `build.ps1`, uploads the resulting `.exe` as a GitHub Release artifact. Release notes auto-drafted from `CHANGELOG.md`.
- Users download `Sanduhr-Setup-v2.0.0.exe` from GitHub Releases (mirrors Mac `.dmg` distribution).

### Local build commands

```powershell
cd windows
./build.ps1                   # full pipeline: PyInstaller → Inno Setup → build/Sanduhr-Setup-vX.Y.Z.exe
./build.ps1 -SkipInstaller    # PyInstaller only, for iteration
./build.ps1 -Debug            # unpacked dist/Sanduhr/ with console window for tracebacks
```

Mirrors `mac/build.sh` / `mac/make-dmg.sh` in spirit — three commands, no surprises.

### Install footprint

```text
C:\Program Files\Sanduhr\                   ~95MB (app + Qt runtime)
C:\Users\<you>\AppData\Roaming\Sanduhr\     (created at first run)
  history.json
  settings.json                             theme, window position
  sanduhr.log                               rotating 1MB × 3
  last_error.json                           written only on unhandled exception
Credential Manager                          service com.626labs.sanduhr
  account sessionKey
  account cf_clearance
Start Menu                                  Sanduhr für Claude.lnk
Registry (optional, opt-in)                 HKCU\...\Run\Sanduhr
```

## Migration from v1

v1 user upgrading to v2 must not lose their session key or preferences.

**Detected on first v2 run:**

1. Check for `~/.claude-usage-widget/config.json`.
2. If present:
   - Read `session_key` → write to keyring (service `com.626labs.sanduhr`, account `sessionKey`).
   - Read `theme` → write to `%APPDATA%\Sanduhr\settings.json`.
   - Check for `~/.claude-usage-widget/history.json` → copy to `%APPDATA%\Sanduhr\history.json`.
   - Delete `~/.claude-usage-widget/config.json` (old plaintext credential file).
   - Optionally preserve `history.json` in old location for a release or two in case users downgrade, then delete in v2.1.
3. Log migration to `sanduhr.log` at INFO level.

User sees no prompt. Widget launches connected.

## Testing

### Unit tests (pytest, CI on every push)

- **`pacing.py`** — `pace_frac`, `pace_info`, `burn_projection`, `time_until`, `reset_datetime_str`. Edge cases: period boundaries, util=0, util=100, resets_at=None, DST crossing, negative deltas. Target: 100% branch coverage.
- **`history.py`** — append, load, rolling-window trim, corruption recovery (malformed JSON returns empty dict, doesn't crash).
- **`credentials.py`** — v1 → v2 migration (mocked `~/.claude-usage-widget/config.json`, assert keyring written, old file deleted, theme preserved). Mock `keyring` in tests.
- **`api.py`** — `requests-mock` or `responses` for stubbed HTTP. Correct cookie header, correct URL sequence (orgs → usage), graceful 401/403/5xx handling, Cloudflare HTML page detection path.
- **`themes.py`, `paths.py`** — trivial smoke tests.

### Integration tests (pytest-qt, headless)

- `UsageFetcher` thread: given mocked `api.get_usage()`, assert `dataReady` signal fires on main thread with expected payload. Assert `fetchFailed` fires on exception.
- `TierCard.update()`: given new `util` / `resets_at`, assert label text and pace marker `x` position match expected values. No pixel-level assertions.
- One full bootstrap test: `QApplication` + `SanduhrWidget` starts without exceptions; no credentials present shows setup flow.

### Not automated

- Visual rendering (Mica, translucency, hover). Cross-build pixel assertions are a maintenance trap. Covered by manual test plan.
- Live claude.ai API calls. No CI machine hits real API on push with a real key.

### Manual test plan

Checked into `windows/TEST_PLAN.md`. Run before every release. Rough checklist:

1. Fresh install on machine with no prior v1 state. First-run dialog → paste key → widget shows data → close → reopen → still connected.
2. Upgrade install over v1: config migrates, no re-auth, history preserved.
3. Each theme renders correctly — compare to reference `docs/screenshots/windows/`.
4. Win11 Mica with colorful desktop: confirm translucency. Win10 fallback: solid bg, no crash.
5. Exercise: Pin toggle, Compact mode (double-click), Key dialog, Refresh, right-click context menu (all 4 items), drag widget, window position persisted across close/reopen.
6. Session expiry: paste invalid key → "Session expired — click Key." Paste valid key → recovers.
7. Cloudflare block simulated: block claude.ai via hosts file → "Cloudflare — add cf_clearance" message, second-field focus in dialog.
8. Uninstall: removes install dir, preserves `%APPDATA%\Sanduhr\` by default, removes it if checkbox ticked, clears Credential Manager regardless.

Reference screenshots in `docs/screenshots/windows/` give the reviewer a concrete visual baseline.

## Error handling

| Condition | Detection | UI response |
| --- | --- | --- |
| No credentials on first run | `credentials.load()` returns empty | Setup dialog walks user through F12 → cookies flow |
| Session expired | 401/403 from API | Red status "Session expired — click Key." Key button pulses subtly. |
| Cloudflare blocked | 403 + Cloudflare HTML signature, or captcha page detected | Status "Cloudflare — add cf_clearance." Key dialog opens with cf_clearance field pre-focused. |
| Network error | `requests.ConnectionError` / `Timeout` | Status "No connection — retrying." Exponential backoff 30s → 5min cap, then resume normal cadence. |
| Unexpected API shape | KeyError / TypeError in parse | Status "Unexpected response — check logs." Dump full response to `%APPDATA%\Sanduhr\last_error.json`. Don't crash. |
| Zero active tiers | API returns all nulls | Status "No active tiers" (v1 behavior preserved). |
| History file corrupted | `JSONDecodeError` on load | Wipe, start fresh, log warning. Don't block startup. |
| Keyring unavailable | `keyring.errors.KeyringError` | Fallback: ask user to re-paste each session. Log warning. Rare — Windows always has CredMan. |

### Logging

- `%APPDATA%\Sanduhr\sanduhr.log` — rotating file handler, 1MB × 3 files.
- INFO by default; DEBUG when launched with `--debug` flag.
- Never log credential values. Log presence/absence only.
- Unhandled exceptions: stack trace to log + full context dump to `last_error.json`.

## Open questions — all resolved during brainstorming

- ~~Native rewrite in C#?~~ → No. Upgraded Python (PySide6) is better ROI.
- ~~Tray-only vs taskbar?~~ → Taskbar visible (user preference).
- ~~Repo reorg disruptive?~~ → Non-disruptive. New branch, new folder, root `sanduhr.py` untouched.
- ~~Python package name `sanduhr_fur_claude`?~~ → Short `sanduhr`, internal only. Full name "Sanduhr für Claude" user-facing only.
- ~~v1 migration?~~ → Yes. Silent migration on first v2 run.
- ~~Auto-start default on?~~ → Off. Opt-in checkbox during install.
- ~~GitHub Actions builds from day one?~~ → Yes.
- ~~Code signing?~~ → Skipped for v2.0. Revisit if this goes public.

## Success criteria

The v2.0 release is successful when:

1. A user on a fresh Windows 11 machine downloads `Sanduhr-Setup-v2.0.0.exe` from GitHub Releases, clicks through the installer, launches the app, pastes a session key, and sees their Claude usage rendered with Mica backdrop and glass-stacked cards — in under 60 seconds from download.
2. A v1 user running `sanduhr.py` from source installs v2.0 and their existing session key + theme preference carry over silently.
3. All five themes render correctly; Matrix theme reads as distinct from the four glass themes; each glass theme reads as distinct from the others.
4. Running `./build.ps1` on a clean Windows dev machine produces a byte-reproducible installer.
5. pytest + pytest-qt suite passes in CI on every push.
6. The manual test plan passes end-to-end before the release tag.
