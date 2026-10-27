# Sanduhr für Claude — Windows (.NET 10 / WPF)

The Windows build of Sanduhr, rebuilt from the original Python/PySide6 app onto
**.NET 10 / WPF** at full behavioral parity. A floating, glass desktop widget that
shows a signed-in Claude.ai user their own subscription usage: five-hour and
seven-day limits, burn-rate projection, pacing markers, 30-day history, a
deep-work focus hourglass, a cooldown game, themes, and Win11 Mica.

This tree (`windows-dotnet/`) is the **next Microsoft Store submission** (v3.0.0.0).
It lives beside the legacy `windows/` (Python) and `mac/` (Swift) builds in the
same repo and reuses the same Store identity, so an existing user updates in place.

> The repo-root `README.md` documents the cross-platform/Python project. This file
> is scoped to the .NET Windows rebuild only.

---

## Architecture

Two assemblies, split on a hard testability boundary:

- **`src/Sanduhr.Core/`** (`net10.0`) — pure logic, zero WPF/Win32-UI dependencies.
  Pacing math, tier model, plan-badge mapping, usage history, account/credential
  storage, CC-log reader, API parsing, the sign-in decision logic. This is the
  parity bar: it ports the shipped Python app's behavior 1:1 and is covered by the
  full xUnit suite.
- **`src/Sanduhr.App/`** (`net10.0-windows10.0.19041.0`) — the WPF shell. Views + view models
  binding to Core, the tray icon, Mica interop, the WebView2 surfaces, packaging
  manifest. Custom `Program.Main` runs `VelopackApp.Build().Run()` before WPF so
  auto-update hooks fire first.

### The WebView2 fetch transport (the Cloudflare-beating pivot)

The original plan was a `HttpClient` + Cloudflare-aware handler (`Core/ClaudeApiClient.cs`,
Chrome UA + `Sec-Fetch-*` headers). That class is retained as the parity-tested
reference seam, but it is **not** the live transport: a raw `HttpClient` cannot
clear claude.ai's Cloudflare even with a valid `cf_clearance` and a matched Chrome
UA, because Cloudflare binds its challenge to the browser's TLS/JA3 fingerprint and
the short-lived `__cf_bm` cookie.

The production transport is **`App/Services/WebView2ApiClient.cs`**: a hidden,
off-screen, taskbar-less WebView2 host that injects the `sessionKey` (+ `cf_clearance`)
into its own cookie jar, navigates `https://claude.ai` (the real browser solves
Cloudflare natively), and runs the API requests from inside the page context via
in-page `fetch()`, correlating replies back over `WebMessageReceived`. It is a
drop-in for `ClaudeApiClient` — same `(sessionKey, cfClearance)` constructor, same
`IClaudeApiClient` surface, same shared `ClaudeApiParsing` and typed errors — so
`UsageFetcher` is untouched. `WidgetViewModel.RebuildFetcher()` constructs it.

### The embedded "Sign in to Claude" capture

`App/Views/SignInWindow.xaml(.cs)` (the RORORO `CookieCaptureWindow` pattern) hosts
a WebView2 against an app-owned, isolated user-data folder under
`%APPDATA%\Sanduhr\webview2\`. It navigates `https://claude.ai/login`; the user
signs in on Anthropic's real page; once the `sessionKey` cookie appears on the
claude.ai origin it is read straight off `CoreWebView2.CookieManager` and persisted
to the Windows Credential Manager. No browser-store prying — the app owns its own
cookie jar. Manual sessionKey paste (`ManualKeyWindow`) is retained as the
power-user fallback.

---

## Build / run / test

Requires the **.NET 10 SDK** (the repo was built against 10.0.202). All commands
run from the repo root.

```powershell
# Build everything (Core + App + Tests)
dotnet build windows-dotnet/Sanduhr.slnx -c Debug

# Run the widget
dotnet run --project windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj

# Run the test suite (297 tests, the ported Python behavioral spec)
dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj -c Debug
```

The App targets `net10.0-windows10.0.19041.0` with `UseWPF`, so it builds and runs on Windows
only. Core and Tests are `net10.0`. The embedded sign-in and the usage transport
need the **WebView2 runtime** installed (present on current Windows 11; the app
degrades to the manual-paste fallback when it is missing).

---

## Where credentials live

The user's claude.ai session credential **never** touches the repo, a config file,
or a log. It is stored only in the **Windows Credential Manager** (Generic
credential, DPAPI-protected, per-user/per-machine).

- Service: `com.626labs.sanduhr`
- Per-account slots: `sessionKey:{label}` and `cf_clearance:{label}`
- Registry slots: `accounts:list`, `accounts:active`
- On-disk target name (the keyring/WinVault format the Python build also wrote):
  **`{slot}@com.626labs.sanduhr`** — e.g. `sessionKey:Personal@com.626labs.sanduhr`

The diagnostic logs (`signin-debug.log`, `fetch-debug.log` under `%APPDATA%\Sanduhr\`)
record presence booleans, host names, HTTP status, and cookie counts only — never
a cookie value.

---

## Sign-in notes

- The login runs on Anthropic's **real** `claude.ai/login` page inside WebView2.
  Sanduhr never sees the password; it only reads back the resulting session cookie
  from its own jar.
- **Continue with Google works in the sign-in window (since 4.0).** Google's
  sign-in runs in a popup and hands its result back through `window.opener`, so
  `SignInWindow` gives each `window.open` a real owned window on the same WebView2
  environment, with the same desktop-Chrome UA (Google refuses the embedded one,
  `disallowed_useragent`). Google may add its own device check (g.co/sc). Folding
  the popup into the main view, as before 4.0, left a blank Google page. If a popup
  can't start, the URL falls back into the main view, and a one-shot timer sends it
  back to `claude.ai/login` 6 s after Google's `accounts.youtube.com` step, where
  the live Google session completes the sign-in. Email + login code still works.
- Cookies are read **only from the `claude.ai` origin** (`https://claude.ai`); the
  capture never fires on OAuth-provider, analytics, or captcha hosts.

---

## Data compatibility (zero migration)

The .NET build reads and writes the **same** `%APPDATA%\Sanduhr\` files
(`settings.json`, `history.{account}.json`, `themes/`, `sounds/`) and the **same**
Windows Credential Manager slots as the shipped Python build. An existing user who
updates keeps their accounts, history, and settings untouched — schemas match the
Python app byte-for-byte (verified by the data-compat tests in the suite).

### Since 3.4.3: Mac parity, Stage 1

The Windows app is catching up to the Mac in stages (`docs/scope.md`, `docs/spec.md`). Stage 1
adds What's New after an update, a global shortcut to open Settings (off until turned on,
Ctrl+Alt+S, with a recorder), every `~/.claude*` Claude Code home on the read paths (burn, model
usage; new homes appear unconsented in the vault and publish lists), limits that hide until they
reset, five tray readouts, and a level line for steady horizon sparklines. New `settings.json`
keys, each defaulting when absent: `whats_new_last_seen`, `whats_new_installed_version`,
`whats_new_hide_after_updates`, `hotkey_settings`, `tray_mode`, `limit_first_seen`,
`limits_hidden_until_reset`, `alert_muted_tiers`. No new files, Credential Manager slots or
manifest capabilities.

### Since 3.4.3: Mac parity, Stage 2

Stage 2 adds the welcome tour (`docs/welcome-tour-spec.md`): four steps in the What's New
window's step mode, opened once after a fresh install's first successful fetch, reopenable from
the tray and Settings, General ("Take the Tour…"). Its tray step writes `tray_mode` in place. The
first-run sign-in card gains **Not now**, which leaves a "Not signed in · Sign in" line for that
launch. Demo mode (`Sanduhr.exe --demo`, or `SANDUHR_DEMO=1`) shows two sample accounts with
made-up numbers for screenshots, labelled on the widget, tray tooltip and tour. It writes nothing:
no snapshot, history, alert or limit state. New `settings.json` key: `tour_state` (`pending`,
`finished`, `skipped`, `not_offered`).

### Since 3.4.3: Mac parity, Stage 3

Settings v2 (`docs/settings-v2-spec.md`): Settings is a sidebar in four groups (General,
Accounts, Alerts; Usage: Claude Usage (its first view, Meters, is the 30-day meter history), Publish usage; Widget: Themes; Help: About) with
a search box that finds pages, sections and controls by name and synonyms and opens them in view.
General shows a live tray preview. About holds the version, What's New… and Take the tour…, the
trademark notice, where settings live (with Show in Explorer) and the third-party notices.
Every control has one name across Settings, the menus, the tour and What's New
(`Sanduhr.Core/SettingsNames.cs`, checked by tests); commands are sentence case. Demo mode now
shows the sample accounts in Settings and the account menus too. No `settings.json` key changed,
so nothing migrates; `SettingsKeysContractTests` pins the keys so a future rename needs a
migration. Windows can be rendered off screen for checking
with `tools/Sanduhr.RenderHarness` (needs `SANDUHR_DEV_PROFILE`).

### Since 3.4.3: Mac parity, Stage 4

The Claude Code side (`docs/spec.md` Stage 4, `../docs/mods-and-config-spec.md`):

- **Settings, Claude Code**: every folder Sanduhr finds (`~/.claude`, `~/.claude-*` with
  `projects/` or a `.claude.json`, the `CLAUDE_CONFIG_DIR` folder in the profile) plus folders
  added by hand (`cc_folders_added`); a linked account per folder; the MCP consent per folder;
  Install and Remove for the statusline, the MCP server, the meters mod and the glow hooks, each
  behind a consent dialog. The statusline and MCP moved here from Claude Usage.
- **Edits with exact undo**: `JsonEdit` splices one member and keeps every other byte (LF and
  CRLF); `ConfigCommit` re-reads and compares before replacing, with backups in
  `%APPDATA%\Sanduhr\backups`; receipts in `%APPDATA%\Sanduhr\integrations\installs.json`, read
  record by record. Remove returns a file byte for byte when nobody else changed it.
- **Statusline combine**: keeps your own statusline and adds Sanduhr's segments (session, weekly,
  weekly reset) on their own line or the same one; the script looks the folder up in
  `seats.json` for per-folder meters.
- **Meters mod**: the Mac's `sanduhr-meters` mod ships in the app output (`mods\`), installs to
  `%USERPROFILE%\.sanduhr\mods\<stamp>` and joins `env.CLAUDE_CODE_PLUGIN_DIRS`; On, Off, Update and
  Remove live on **Settings, Mods & Widgets**, which otherwise reads a folder without changing it (settings,
  statusline, hooks, plugins, mods, MCP names, `CLAUDE.md`, retention). Secrets are masked.
- **Glow when Claude needs you**: `Notification` and `Stop` hooks touch a signal file; the widget
  lights the top edge of the window you are working in, in the theme's accent (waiting) or
  success color (done), and flashes the taskbar button. Off by default (`claude_code_glow`).
- **Accounts**: Follow the account I'm using (`follow_account`, off by default) checks the other
  signed-in accounts every 15 minutes through their own WebView2 profiles
  (`%APPDATA%\Sanduhr\webview2-<account_ref>`); linked folders get
  `%USERPROFILE%\.sanduhr\snapshot.<account_ref>.json` and `seats.json` (`account_folders`).
- **Files outside AppData (3.5.0).** Everything a process outside the app reads or writes lives in
  `%USERPROFILE%\.sanduhr` (`Paths.ExternalDir`): the snapshots, `seats.json`, `mcp-settings.json`
  (a projection of the MCP consent and publish readiness), the publish and theme handoff files, the
  glow signals, `bin\`, `mcp\` and `mods\`. Private data stays in `%APPDATA%\Sanduhr` and the vault;
  on the Store build Windows keeps those in package storage. The manifest declares `runFullTrust`
  only. `ExternalDirMigration` re-points registrations that still name the old paths on first
  launch. See "Plan B" in `docs/spec.md`.
- **Reset Sanduhr…** on About: five parts, all checked, then quit.
- **The MCP** reads discovered folders behind the `mcp_roots` consent (it now source-links
  `ClaudeCodeHomes.cs`); `publish_usage` hands its request over atomically. Since 3.6.0 it also reads
  the Desk messages (`get_desk_messages`) and hands the widget suggested messages
  (`propose_desk_messages`) and watcher cards (`watch_start`, `watch_update`, `watch_end`), each a
  request file the widget checks again; the link list grew by six pure Core files (spec 6.9).
  The agent-designed Desk (3.7.1) adds `get_desk` (reads the widget's `desk-state.json`:
  the current design, fonts, the wallpaper's local summary, the Start-menu apps) and `propose_desk`
  (`DeskDesign.Validate` first, then `desk-design-request.json` / `desk-design-result.json`); the link
  list grew by nine pure Core files (`docs/superpowers/specs/2026-10-21-desk-design-design.md`).
  A look per song (3.7.1) adds `propose_now_playing_looks` (`NowPlayingLookCheck` first, contrast against the wallpaper
  included, then `now-playing-looks-request.json` / `now-playing-looks-result.json`); the link list grew by one pure Core
  file, `NowPlayingLook.cs`. The widget keeps the saved looks (`now-playing-looks.json`, private) and writes
  `~\.sanduhr\now-playing-look.json` for the Claude Code now-playing mod (`docs/superpowers/specs/2026-10-21-now-playing-looks-design.md`).

`statusline_enabled` now means "the statusline or the meters mod is installed in some folder", so
`snapshot.json` is written while either is, or while the MCP server is installed (`mcp_enabled`:
`get_usage` reads the snapshot, and an MCP-only install has nothing else on the Store build).

### Since 3.4.3: Mac parity, Stages 5 and 6

The Desk (`docs/spec.md` Stages 5 and 6), off until **Settings, Desk** turns it on:

- **The window**: one bottom-most island window per stack of pieces, owned by the shell window
  (Rainmeter's "On desktop"), per monitor and per-monitor DPI; it keeps clear of the taskbar.
- **Pieces**: the clock and date, the Claude line and meters, the **message** (`messages.txt` in
  `%APPDATA%\Sanduhr`, the Mac's grammar and effects, special days that stack, take turns or
  scroll, edited on **Settings, Message**), **now playing** (shown only while something plays) with bars from the playing app's peak meter,
  **system** readings (CPU, RAM, disk) and **weather** from Open-Meteo for a city you pick
  (**Settings, Weather**), and **watchers**: live cards for work an agent is watching, shown when you turn on "Let agents show watchers" (**Settings, Desk**) and place the piece.
- **Ink**: Automatic (from the wallpaper image behind each piece, never the screen), Light, Dark,
  the theme's color or Custom (`desk_ink`).
- **Now playing in the widget**: a line under the cards (`now_playing.widget`, off by default).
- **Where the glow shows**: the window in use, a screen edge, or the taskbar flash only
  (`claude_code_glow.target`).
- `hotkey_settings` is a per-action map now; the flat value migrates once at launch
  (`SettingsMigrations`).
- New keys: `desk_ink`, `desk_message`, `desk_weather`, `now_playing`, and the glow group's
  `target`, `edge_side`, `edge_monitor`. Reset removes `messages.txt` with the settings.

### 3.8 (in progress): the widget catalog

`docs/widgets-3.8-spec.md` sections 2 and 3. **Settings, Mods & Widgets** (Extend group) lists the signed
`sanduhr-widgets` catalog and the installed packs, and installs, updates and uninstalls them.

- **Fetch** (`Services/WidgetCatalogService.cs`, the app only, never `Sanduhr.Mcp`): `catalog.json`,
  `.sig` and `.sig2` from the latest release, only when the page opens or on Check the catalog
  again; a pack zip only on Install or Update. Redirects are followed by hand, https only, to
  `github.com` and GitHub's asset hosts; every body has a size cap and every request a timeout.
- **Checks** (Core, pure, FORMAT 7's order): `WidgetCatalog` verifies an ECDSA P-256 / SHA-256
  P1363 signature against the pinned keys before parsing, parses strictly, refuses a rollback by
  `generated_at`, checks `sanduhr_min`, size and SHA-256; `WidgetZip` reads the stored-only zip
  without inflating (limits checked from the directory first, then per entry); `PackValidator`
  re-validates strict. `WidgetCatalogClient` runs the steps.
- **Disk** (`WidgetStore`): `%LOCALAPPDATA%\Sanduhr\widgets\<id>\` plus `widgets.json` (id, version,
  source, installed_at, enabled, and the newest catalog seen). Install stages under `.staging\`,
  rasterizes pixel sheets to `.raster\<sheet>.png` (WPF's PNG encoder), then renames into place.
  An id keeps its source. Reset's themes part removes the folder.

**The companion** (spec section 4, FORMAT 5.5 and 6): a Desk piece, `companion`, placed like any
other and listed in `get_desk` pieces (last in `DeskArrangement.Known`, default anchor bottom right).
It draws the sprite chosen in Settings, Desk (Pieces, the Companion row) (`desk_companion` {pack, roam}; no pick is the first
installed sprite). Showing it there places or hides the piece.

- **Core, pure**: `CompanionSprite` reads sprite.json (stages with their own cell sizes, the
  animation table, moods, triggers, roam); `CompanionSheet` cuts cell `i` row-major from a PNG or a
  `.raster` PNG. `CompanionGrowth` is the curve, piecewise linear in coding days through (0, 0),
  (3, 0.10), (10, 0.35), (30, 0.75), (60, 1): the hourglass's thresholds land on days 3, 10 and 30;
  any pack's `min_growth` maps through the same curve (`DaysFor`). Days come from Sanduhr Time's day
  files (`~\.sanduhr\time\days`, v1 or v2, read-only) when present, else the days with Claude Code
  activity (dates only, never token counts). `CompanionGrowthState` (`widgets\companion.json`)
  remembers every day seen and the highest growth, so it never goes backwards. `CompanionMoods` and
  `CompanionBrain` give the mood from the widget's pacing and the triggers (a watcher waiting or
  passed, an alert threshold crossed upward, 20 minutes without input); `CompanionAnimator` picks the
  frame (a trigger once, then the mood from its first frame; 1 fps with reduce motion);
  `CompanionPath` and `CompanionRoamer` are the route along the pack's edges and the walk/rest clock.
- **App**: `Desk/DeskCompanion.cs`. `CompanionService` lives in the controller across redraws and runs
  its frame timer only while a piece is in sight; growth is read off the UI thread once an hour.
  The piece is nearest-neighbour at cell x pack scale x piece size, bottom-centre, never hit-testable.
  Roaming, it is its own island with a `DeskIslandMotion` the host follows by moving the window
  (OLED care's drift and dim still apply); with Windows' animations off it sits in its stack.
- **Harness**: `--scenario companion` installs the hourglass into the dev profile and draws each stage,
  a trigger, a mood, a roaming frame and Settings, Desk, Pieces.

**The ride-alongs** (spec section 7, E to G):

- **Weather nickname** (#315): Settings, Weather, "Show it as" (`desk_weather.nickname`, up to 24
  characters, written only when set). The Desk's weather piece shows it instead of the place's name;
  the place search and the forecast never see it. `propose_desk` takes `weather {nickname}` under the
  same review and Apply (`""` goes back to the place's name); `get_desk` reports it.
- **The fold** (#314, affine only): `layout.<piece>.tilt`, whole degrees from -20 to 20, is the slant of
  the piece's **stack**: the pieces at one anchor share one slant (`DeskArrangement.SetTilt`; a piece
  that joins a stack takes its slant, one alone keeps its own; a design that gives two pieces of one
  stack different tilts is refused). The layout word grows a fourth field, `widget:anchor:scale:tilt`
  (`clock:ml:1:-18`), written only when not 0, so every older layout reads and writes unchanged and an
  older build reads the scale and ignores the tilt. `DeskView` puts a `SkewTransform` (`AngleY` = tilt;
  negative rises to the right) on the stack, its plate with it, as a `LayoutTransform`, so the island's
  window is sized for the slanted face and the host's DPI scale composes over it. Arrange Desk hit-tests,
  outlines and drags the slanted quad (`DeskFold`, `DeskView.PieceQuads`). Perspective stays out.
- **Livelier bars**: `AudioBarMotion` eases each bar up (attack) and down (decay, never faster than the
  falloff) and rides a peak cap over it that holds 9 frames and then drops with growing speed.
  "Gradient bars" (`now_playing.bars_gradient`, off) spreads the ink's colors across the row and fades
  each bar to its base. With Windows' animations off the bars ease gently, draw no caps, and the source
  ticks five times a second. Still a level only: no audio is captured.
- **Meter styles** (#336): `layout.<piece>.style` on `claude`, `meters` and `telemetry` only
  (`DeskMeterStyles`): `bar` (the default, drawn exactly as before), `slim` (label and value on one line
  over a thin bar, about half the height; the Claude line becomes two short rows, `DeskClaudeText.Compact`),
  `ring` (small 270-degree arc gauges with the value inside and a short name in the open bottom, one piece's
  readings side by side in a row) and `ticks` (ten cells, unlit ones a faint outline: the OLED-friendly
  one). A Claude limit keeps its pace mark in every style; slim, ring and ticks leave out the reset line.
  The layout word grows a fifth field after the tilt, `widget:anchor:scale:tilt:style`
  (`meters:tr:1:0:ring`), written only when not `bar` with the scale and tilt holding their places, so an
  older build reads the size and tilt and draws bars. A style is the piece's own (the tilt stays the
  stack's). `propose_desk` refuses a style on any other piece or an unknown word; `get_desk` reports each
  styled piece's style and `limits.piece_style`; the review says "Claude meters (bars): ring"; a saved design with
  bars matches one saved before styles. Settings, Desk has a Style picker under the place for the three
  pieces. Nothing animates, and OLED care dims the island as before. `DeskView.Styles.cs` draws them.
- **Harness**: `--scenario meterstyles` draws each style upright and in a ±18 degree fold, the dense
  right face (Claude line, telemetry, limits) as bars and again with slim and rings, close-ups with a limit
  over the warning line, Arrange Desk over a restyled fold, and the Style pickers.
- **Harness**: `--scenario ridealongs` draws a two-face fold (left -18, right +18) with the weather as a
  nickname and gradient bars, the bars close up (lively, gradient, calm), Arrange Desk over the fold with a
  drag, and Settings, Weather and Now playing.

**Widgets made with Claude** (spec sections 5 and 6): `propose_widget` on the MCP server takes a whole
pack as data (kind `sprite`, `palette` or `effects`; a theme goes through `propose_theme`, and no PNG or
font is ever accepted) and installs nothing.

- **Core, pure**: `WidgetProposal` (linked into the server) builds the pack from the tool's arguments:
  `pack.json` with the id namespaced `my-<id>`, a LICENSE text for the chosen license (CC0-1.0 by
  default), the kind's JSON files; it runs `PackValidator` strict (the trademark rule included) and holds
  the `widget-request.json` / `widget-result.json` shapes. `Decode` is the widget's own check of a request.
  `WidgetReview` is the bar's "What this widget is" and the palette's preview ink. `WidgetOwnArt` is Use
  my own art…: the pack image rules on a picked PNG, the pack rebuilt around it, validated whole.
- **App**: `Desk/WidgetReviewService.cs`. The service watches `%USERPROFILE%\.sanduhr` for the request
  (`WidgetHandoff`), stages a sprite in `widgets\.preview\` (rasterized there) and draws it through the
  companion's own loader (`CompanionService.PreviewPack`); a palette previews as Custom ink. The review
  bar wears the widget's words and one choice, Make it the companion. Apply installs as source `claude`;
  Dismiss removes the staged copy and writes nothing; the direct switch is never read. Settings, Mods & Widgets
  offers Use my own art… on a sprite made here, which reinstalls it as source `local`.
- **Harness**: `--scenario propose-widget` builds a 16 px pixel blob as the server would, previews it on a
  composited Desk with the bar, applies it, and shows Settings, Mods & Widgets with the row made here.

### Since 3.8.0: CI watchers from local runners

`docs/ci-watchers-3.9-spec.md`. Settings ▸ Desk ▸ **Show my CI runners' jobs** (`watchers_ci_runners`, off by default) puts a watcher card up for each job a GitHub Actions runner container on this PC runs. Core: `RunnerLog` (a log line to a start, end or restart), `RunnerFollow` (the replay gate), `DockerLogFrames` (Docker's multiplexed stream), `DockerContainers` (the list call, runner images, the status line), and `WatcherSource.Runner` on the board (no lost touch, hidden in demo mode). The board's runner operations in `Watchers.cs` take plain values, because `Watchers.cs` is source-linked into the MCP server; `RunnerBoard.cs` holds the `ApplyRunner` extension that feeds them. App: `DockerPipe` (an `HttpClient` over `\\.\pipe\docker_engine`, two calls only) and `RunnerWatchService`. `DockerPipeBoundaryTests` pins the boundary by structure: the only `HttpClient` call is `GetAsync(PathFor(...))`, and a scan of `src/Sanduhr.App`, `Sanduhr.Core` and `Sanduhr.Mcp` (each directory must exist) forbids the pipe name and Docker port tokens outside `src/Sanduhr.App/Desk/DockerPipe.cs`, so the pipe name appears only in `DockerPipe.cs`. The switch is searchable in Settings, under the Watchers entry. Fixtures in `tests/Sanduhr.Tests/fixtures/runner-logs/` are real lines from Neb's runners (a succeeded run, a cancelled one) plus derived ones, marked.

---

## Tech stack

| Concern | Choice |
|---|---|
| Runtime / language | .NET 10, C#, WPF (`net10.0-windows10.0.19041.0`) |
| MVVM | `CommunityToolkit.Mvvm` |
| Fluent + Mica | `WPF-UI` (+ CsWin32 `DwmExtendFrameIntoClientArea` / `DWMWA_SYSTEMBACKDROP_TYPE`) |
| Embedded login + usage transport | `Microsoft.Web.WebView2` |
| Tray icon | `Hardcodet.NotifyIcon.Wpf` |
| Win32 / DWM source-gen | `Microsoft.Windows.CsWin32` |
| Auto-update (GitHub channel) | `Velopack` |
| HTTP (reference transport) | `HttpClient` + a Cloudflare-aware `DelegatingHandler` |
| Credentials | Windows Credential Manager via `advapi32` P/Invoke (DPAPI at rest) |
| Tests | `xUnit` + `Microsoft.NET.Test.Sdk` (297 tests) |

Core carries no heavy dependencies (`System.Text.Json` only). Diagnostics are
lightweight file-append logs, not a logging framework.

---

## Packaging

Dual-channel from one version (Store MSIX + GitHub/Velopack), version `3.0.0.0`
(4th component must be `.0` for the Store). The full per-release loop —
version bump, reviewer letter, MSIX build, Velopack build, tag, GitHub release,
Partner Center submission, and the 10.1.4.4 acceptance gates — lives in
[`docs/release-runbook.md`](../docs/release-runbook.md). Build scripts are under
`windows-dotnet/scripts/` and emit to `windows-dotnet/dist/` (git-ignored).

---

## Screenshots

_TODO._ Add widget / focus-hourglass / sign-in screenshots here. Store tiles are
generated from the committed brand art by `scripts/generate-store-assets.ps1`
(see `src/Sanduhr.App/Package/Logos/README.md`); marketing graphics live under
`docs/store-assets/`.

---

## Trademark

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively to
describe integration. Sanduhr für Claude is an independent third-party tool, not
affiliated with, endorsed by, or sponsored by Anthropic PBC.
