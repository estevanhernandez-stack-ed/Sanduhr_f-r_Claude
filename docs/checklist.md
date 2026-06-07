<!-- Every item uses the five-field format. /build reads each item and relies on all
     five fields being present. The header encodes methodology so /build doesn't re-ask. -->

# Build Checklist — Sanduhr .NET 10 / WPF Rebuild

Pairs with `docs/scope.md`, `docs/prd.md`, `docs/spec.md`. Parity rewrite of a shipped product — the **Python `windows/` app + its 286 tests are the binding acceptance criteria**. When this checklist and a Python test disagree, the test wins.

**Milestone gates (verification checkpoints):** the build pauses after items **4, 5, 6, 10, 11** — the five real seams (Core green → widget renders → sign-in works → parity complete → pre-ship). Standard build order: Core unblocks the widget; the widget + credential store unblock the login.

> **Hourglass deepening round — RESOLVED (2026-06-06):** keep the falling-sand CA model 1:1, rebuild the view as a thin-line branded-glass vessel. Full design in `spec.md > Focus hourglass — view rebuild`. Item 10 is no longer provisional.

## Build Preferences

- **Build mode:** Autonomous (`autonomy_level: fully-autonomous`)
- **Comprehension checks:** N/A (autonomous mode)
- **Git:** Commit after each item — `feat(rebuild): complete step N — <title>` (matches the repo's `feat(rebuild)` prefix). Commits are the revert points.
- **Verification:** Yes — milestone-gate checkpoints after items 4, 5, 6, 10, 11. Agent pauses, summarizes, builder eyeballs before continuing.
- **Check-in cadence:** N/A (autonomous mode)

## Checklist

- [x] **1. Scaffold solution + pacing parity port (DONE)**
  Spec ref: `spec.md > Solution layout (windows-dotnet/)` + `spec.md > Test strategy`
  What to build: `Sanduhr.Core` / `Sanduhr.App` / `Sanduhr.Tests` projects, `Sanduhr.slnx`, package refs; port the pure pacing math (`pace_frac`, `pace_info`, cooldown, surplus, `burn_projection`, velocity) from `pacing.py` to `Core/Pacing.cs` with xUnit — the first vertical slice that proves the port + harness.
  Acceptance: solution compiles; `PacingTests` green; pacing values match the Python implementation.
  Verify: `dotnet test` → `PacingTests` pass. **(Complete — commit `5bc8798`.)**

- [x] **2. Port plan + tier models (pure Core)**
  Spec ref: `spec.md > Module map` (`plan.py → Core/PlanLabel.cs`, `tiers.py → Core/TierModel.cs`)
  What to build: `PlanLabel.cs` — `rate_limit_tier` → Pro/Team/Max/Max ×20 mapping with the defensive stripe-subscription-gated parse (port PR #25's logic; API/prepaid orgs render no badge). `TierModel.cs` — represent `five_hour`, `seven_day` + sub-tiers (`sonnet`/`opus`/`cowork`/`omelette`/`oauth_apps`), `extra_usage`, **Routines** daily-quota count, speculative-tier "future use" tags; utilization + reset-countdown calc. Pure Core, zero WPF. Port the matching Python test intents.
  Acceptance (prd): tier model covers every tier type incl. Routines + `extra_usage`; plan badge maps correctly incl. Max ×20 from `default_claude_max_20x`; tests green.
  Verify: `dotnet test` → plan + tier tests pass; assert Max ×20 maps and that a non-stripe org yields no badge.

- [x] **3. Port API client + fetcher (CF-aware, typed errors)**
  Spec ref: `spec.md > Module map` (`api.py → Core/ClaudeApiClient.cs`, `fetcher.py → Core/UsageFetcher.cs`) + `spec.md > Stack / packages` (Cloudflare-aware handler)
  What to build: `ClaudeApiClient.cs` — `HttpClient` + Cloudflare-aware handler replacing cloudscraper (Chrome UA + `Sec-Fetch-*` headers, CF-challenge HTML detection → distinct typed error); `/organizations` (capture `rate_limit_tier`/`billing_type`/`capabilities`, cache `orgID`) + `/organizations/{id}/usage`. `UsageFetcher.cs` — async fetch + Routines synth + history append + `_account` injection; typed errors (session-expired / CF-blocked / network). Port api-parsing tests with the JSON fixtures.
  Acceptance (prd): fetch returns typed usage; the three error classes are distinguishable; api-parsing tests green against fixtures.
  Verify: `dotnet test` → api/fetcher parsing tests pass; optional live run against a real `sessionKey` parses the usage JSON.

- [x] **4. Port persistence layer — history, accounts, credentials, CC logs (Milestone A gate)**
  Spec ref: `spec.md > Module map` (`history.py`, `accounts.py`, `credentials.py`, `cc_logs.py`) + `prd.md > Data-compat requirement (load-bearing)`
  What to build: `UsageHistory.cs` (`%APPDATA%\Sanduhr\history.{account}.json`, **same schema**, 30-day retention); `AccountStore.cs` (Windows Credential Manager slots `sessionKey:{label}` / `cf_clearance:{label}`, active-account switch, per-account history); `CredentialStore.cs` (DPAPI / Credential Manager); `CcLogReader.cs` (local Claude Code JSONL token-burn delta). Same files + same slots as Python = zero migration. Port history/accounts/cc-logs test intents.
  Acceptance (prd): reads/writes the **same** `%APPDATA%\Sanduhr\` files + Credential Manager slots as the Python build; an existing user's data + accounts carry over untouched; the ported Core xUnit suite is green — **the parity bar for pure logic is met.**
  Verify: `dotnet test` → full Core suite green; manually confirm a Python-written `history.json` + a Python-created Credential Manager slot are read correctly by the .NET Core. **Milestone A checkpoint.**

- [x] **5. Glass/Mica widget shell + tier cards + tier badge (Milestone B gate)**
  Spec ref: `spec.md > Glass / Mica` + `spec.md > Module map` (`widget.py → App/MainWindow + ViewModels/WidgetViewModel`, `tiers.py` render → `App/Views/TierCard.xaml`, footer badge from `PlanLabel`)
  What to build: `MainWindow.xaml` borderless (`WindowStyle=None`, `AllowsTransparency`, top-most); Mica via WPF-UI `SystemBackdrop` (CsWin32 `DwmExtendFrameIntoClientArea` + `DWMWA_SYSTEMBACKDROP_TYPE`; solid-color fallback < Win11 22H2); pin/float toggle (flip top-most); **frame persistence on MOVE only** (port the Python gotcha — never persist on resize); taskbar-icon binding. `WidgetViewModel` binds to `UsageFetcher`. `TierCard.xaml` renders each tier (utilization bar + reset countdown + sparkline), drag-reorder + hide. Footer tier badge (`PlanLabel` + rotating easter-egg tooltip).
  Acceptance (prd): borderless top-most Mica panel; tier cards show live usage; pin/float works; frame persists across launch; badge shows the plan. US-1 at-a-glance usage holds.
  Verify: `dotnet run` → widget floats top-most with Mica; with a credential, cards show live usage; drag a card, hide one, move the window, relaunch — layout persists. **Milestone B checkpoint.**

- [x] **6. Embedded WebView2 "Sign in to Claude" login + manual fallback (Milestone C gate — the headline)**
  Spec ref: `spec.md > The new piece — embedded WebView2 login`
  What to build: `App/Views/SignInWindow.xaml(.cs)` hosting a `WebView2` (`CoreWebView2`, app-owned user-data folder `%APPDATA%\Sanduhr\webview2\`, isolated from the user's Chrome/Edge); navigate `https://claude.ai/login`; user signs in normally (Google/email/passkey — all real Anthropic login); on navigation to a signed-in URL call `CoreWebView2.CookieManager.GetCookiesAsync("https://claude.ai")`, pull `sessionKey` (+ `cf_clearance` if present); persist via `CredentialStore`, close, kick a fetch. Manual sessionKey paste retained in the Add-Account modal as the power-user fallback. (RORORO `CookieCaptureWindow` pattern lifted 1:1.) No browser-store prying — we own the cookie jar.
  Acceptance (prd): a non-technical user goes "Sign in to Claude" → logs in → tracking, **zero DevTools**; manual paste still works.
  Verify: `dotnet run` → click Sign in → real Anthropic login → on success the widget begins tracking with no manual key entry. **Milestone C checkpoint — critical path closed.**

- [x] **7. Settings + multi-account UI**
  Spec ref: `spec.md > Module map` (`settings_dialog.py → App/Views/SettingsWindow + VM`, `accounts.py` UI surface)
  What to build: `SettingsWindow.xaml` + VM (tabbed); multi-account add / switch-active / account-scoped sign-out UI wired to `AccountStore` + `CredentialStore` + `SignInWindow`.
  Acceptance (prd): multi-account switch + per-account history + account-scoped sign-out; settings persist across launch.
  Verify: `dotnet run` → add a 2nd account via Sign in, switch active, sign out one — per-account data + active switch behave correctly.

- [x] **8. Themes + sound chimes**
  Spec ref: `spec.md > Module map` (`themes.py → Core/ThemeModel + App/Theming`, `sounds.py → App/Sounds.cs`)
  What to build: `ThemeModel.cs` (5 palettes ported pixel-exact) + `App/Theming/` + user JSON drop-ins (`%APPDATA%\Sanduhr\themes\`); `Sounds.cs` chimes (`SoundPlayer`/NAudio; honor `SANDUHR_SILENT_SOUNDS`).
  Acceptance (prd): 5 themes + user JSON drop-ins live; sound chimes play; `SANDUHR_SILENT_SOUNDS` silences them.
  Verify: `dotnet run` → cycle all 5 themes, drop a user theme JSON and see it load, confirm a chime fires and the silent env var mutes it.

- [ ] **9. History charts + CSV export + Local CC reader**
  Spec ref: `spec.md > Module map` (`history_chart.py → App/Views/HistoryChart`, `cc_logs.py` UI surface)
  What to build: `HistoryChart.xaml(.cs)` WPF drawing — 30-day per-tier charts, per-account / all-accounts overlay; CSV export; Local CC reader surface showing live token-burn delta vs the lagging `/usage` endpoint.
  Acceptance (prd): 30-day charts + overlay + CSV export; Local CC delta updates live.
  Verify: `dotnet run` → open history, toggle the all-accounts overlay, export a CSV, confirm the CC delta moves.

- [ ] **10. Focus timer (hourglass) + cooldown game + auto-start (Milestone D gate — parity complete)**
  Spec ref: `spec.md > Focus hourglass — view rebuild (item 10)` + `spec.md > Module map` (`game.py → App/Views/CooldownGame`, `startup.py → App/Startup.cs`) + `scope.md > Constraints` (cert 10.1.4.4)
  What to build: `FocusTimer.xaml(.cs)` — the deep-work hourglass. **Port the falling-sand CA model 1:1** (31×31 grid, ~30fps off a ms elapsed clock, wall-clock throttle with `expected_passed` as float-not-truncated, diagonals throttled too — keep the `test_focus_physics` intents green). **Rebuild the view in WPF** per `spec.md > Focus hourglass`: thin-line vector glass vessel + visible neck/stream, per-theme grains (square on retro, round on glass), sand = active theme accent inside a 626 cyan→magenta-tinted glass vessel, **no alpha-60 haze**. `CooldownGame.xaml(.cs)` — snake; `Startup.cs` — auto-start (HKCU `Run` value for unpackaged + MSIX `windows.startupTask` for the Store build, off by default; port PR #26). These three carry the MS Store **10.1.4.4 "unique lasting value"** weight — do not regress to "just a usage display."
  Acceptance (prd): the hourglass drains in proportion to wall-clock time AND reads clearly as a branded-glass hourglass (vessel + falling stream visible, crisp themed sand, no haze); ported physics keeps `test_focus_physics` green; cooldown game functional; auto-start opt-in, off by default; the cert unique-value story holds.
  Verify: `dotnet run` → run a focus session — confirm the sand drains on schedule, the vessel + neck stream are legible, grains match the active theme, and the glass carries the brand tint; play the game; toggle auto-start on/off. **Milestone D checkpoint — feature-for-feature parity reached.**

- [ ] **11. MSIX + Velopack + release prep (Milestone E gate — pre-ship)**
  Spec ref: `spec.md > Release` + `spec.md > Stack / packages` (Velopack, MSIX manifest)
  What to build: `Sanduhr.App/Package.appxmanifest` (reuse identity `626LabsLLC.SanduhrfrClaude` / Publisher `CN=177BCE59-...`, add `windows.startupTask` extension); custom `Program` so `VelopackApp.Build().Run()` precedes WPF; Velopack GitHub `Setup.exe` + delta; MSIX build via `scripts/makeappx`; version **v3.0.0** (4th component MUST be `.0` per the playbook); GitNexus indexes the new tree. Follow RORORO's `release-playbook.md` merged with Sanduhr's `docs/ms-store-submission-playbook.md` (Partner Center, reviewer letter, draft-release discipline, listing "What's new").
  Acceptance: single-version dual build (Store MSIX + GitHub/Velopack); manifest valid; version 4th-component `.0`; playbook steps enumerated for the actual submission.
  Verify: build MSIX + Velopack from one version; `makeappx` validates the package; confirm the version format. **Milestone E checkpoint — pre-ship review.**

- [ ] **12. Documentation & security verification**
  Spec ref: `prd.md > Acceptance` + `spec.md` (all sections)
  What to build: `windows-dotnet/README.md` — what the app does, build/run (`dotnet build` / `dotnet run`, .NET 10 SDK), where credentials live (Credential Manager — never in repo or config), tech stack, a screenshot or two. Confirm `docs/` artifacts (scope/prd/spec/checklist) reflect what was actually built (back-merge the hourglass deepening outcome). Secrets scan — verify no `sessionKey`/`cf_clearance`/tokens hardcoded or logged; `.gitignore` covers `bin/`, `obj/`, and the `webview2/` profile path. Dependency audit: `dotnet list package --vulnerable` — address criticals or document with mitigation. Input-validation spot-check proportional to scope — focus on the cookie/credential handling surface and the WebView2 navigation (only persist cookies from the real `claude.ai` origin). Push the branch.
  Acceptance: README clear enough for a fresh clone to build; no secrets in committed code or logs; vulnerable-package audit clean or documented; security spot-check (esp. credential + cookie handling) written down; code pushed.
  Verify: fresh clone → follow README → `dotnet build` succeeds. Run `git log --all -p | Select-String -Pattern "sessionKey|cf_clearance|secret|password"` and confirm nothing sensitive appears.

---

### Embedded feedback

✓ **Sequencing** — dependencies flow correctly: pure Core (1–4) is the testable parity bar before any UI; the widget (5) needs Core; the WebView2 login (6) needs the credential store + a fetch to verify end-to-end; ship (11–12) last. △ **Granularity** — items 5, 8, 10 each bundle several ported modules; acceptable for an experienced builder porting against an exact Python reference, but 10 is the chunkiest *and* cert-load-bearing, so its milestone gate matters most. ✓ **Completeness** — every `spec.md > Module map` row maps to an item; data-compat and cert constraints are threaded into acceptance. △ **Open thread** — item 10's hourglass is provisional pending the deepening round; back-merge before /build reaches it.
