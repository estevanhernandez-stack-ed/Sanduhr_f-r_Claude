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

- [x] **9. History charts + CSV export + Local CC reader**
  Spec ref: `spec.md > Module map` (`history_chart.py → App/Views/HistoryChart`, `cc_logs.py` UI surface)
  What to build: `HistoryChart.xaml(.cs)` WPF drawing — 30-day per-tier charts, per-account / all-accounts overlay; CSV export; Local CC reader surface showing live token-burn delta vs the lagging `/usage` endpoint.
  Acceptance (prd): 30-day charts + overlay + CSV export; Local CC delta updates live.
  Verify: `dotnet run` → open history, toggle the all-accounts overlay, export a CSV, confirm the CC delta moves.

- [x] **10. Focus timer (hourglass) + cooldown game + auto-start (Milestone D gate — parity complete)**
  Spec ref: `spec.md > Focus hourglass — view rebuild (item 10)` + `spec.md > Module map` (`game.py → App/Views/CooldownGame`, `startup.py → App/Startup.cs`) + `scope.md > Constraints` (cert 10.1.4.4)
  What to build: `FocusTimer.xaml(.cs)` — the deep-work hourglass. **Port the falling-sand CA model 1:1** (31×31 grid, ~30fps off a ms elapsed clock, wall-clock throttle with `expected_passed` as float-not-truncated, diagonals throttled too — keep the `test_focus_physics` intents green). **Rebuild the view in WPF** per `spec.md > Focus hourglass`: thin-line vector glass vessel + visible neck/stream, per-theme grains (square on retro, round on glass), sand = active theme accent inside a 626 cyan→magenta-tinted glass vessel, **no alpha-60 haze**. `CooldownGame.xaml(.cs)` — snake; `Startup.cs` — auto-start (HKCU `Run` value for unpackaged + MSIX `windows.startupTask` for the Store build, off by default; port PR #26). These three carry the MS Store **10.1.4.4 "unique lasting value"** weight — do not regress to "just a usage display."
  Acceptance (prd): the hourglass drains in proportion to wall-clock time AND reads clearly as a branded-glass hourglass (vessel + falling stream visible, crisp themed sand, no haze); ported physics keeps `test_focus_physics` green; cooldown game functional; auto-start opt-in, off by default; the cert unique-value story holds.
  Verify: `dotnet run` → run a focus session — confirm the sand drains on schedule, the vessel + neck stream are legible, grains match the active theme, and the glass carries the brand tint; play the game; toggle auto-start on/off. **Milestone D checkpoint — feature-for-feature parity reached.**

- [x] **11. MSIX + Velopack + release prep (Milestone E gate — pre-ship)**
  Spec ref: `spec.md > Release` + `spec.md > Stack / packages` (Velopack, MSIX manifest)
  What to build: `Sanduhr.App/Package.appxmanifest` (reuse identity `626LabsLLC.SanduhrfrClaude` / Publisher `CN=177BCE59-...`, add `windows.startupTask` extension); custom `Program` so `VelopackApp.Build().Run()` precedes WPF; Velopack GitHub `Setup.exe` + delta; MSIX build via `scripts/makeappx`; version **v3.0.0** (4th component MUST be `.0` per the playbook); GitNexus indexes the new tree. Follow RORORO's `release-playbook.md` merged with Sanduhr's `docs/ms-store-submission-playbook.md` (Partner Center, reviewer letter, draft-release discipline, listing "What's new").
  Acceptance: single-version dual build (Store MSIX + GitHub/Velopack); manifest valid; version 4th-component `.0`; playbook steps enumerated for the actual submission.
  Verify: build MSIX + Velopack from one version; `makeappx` validates the package; confirm the version format. **Milestone E checkpoint — pre-ship review.**

- [x] **12. Documentation & security verification**
  Spec ref: `prd.md > Acceptance` + `spec.md` (all sections)
  What to build: `windows-dotnet/README.md` — what the app does, build/run (`dotnet build` / `dotnet run`, .NET 10 SDK), where credentials live (Credential Manager — never in repo or config), tech stack, a screenshot or two. Confirm `docs/` artifacts (scope/prd/spec/checklist) reflect what was actually built (back-merge the hourglass deepening outcome). Secrets scan — verify no `sessionKey`/`cf_clearance`/tokens hardcoded or logged; `.gitignore` covers `bin/`, `obj/`, and the `webview2/` profile path. Dependency audit: `dotnet list package --vulnerable` — address criticals or document with mitigation. Input-validation spot-check proportional to scope — focus on the cookie/credential handling surface and the WebView2 navigation (only persist cookies from the real `claude.ai` origin). Push the branch.
  Acceptance: README clear enough for a fresh clone to build; no secrets in committed code or logs; vulnerable-package audit clean or documented; security spot-check (esp. credential + cookie handling) written down; code pushed.
  Verify: fresh clone → follow README → `dotnet build` succeeds. Run `git log --all -p | Select-String -Pattern "sessionKey|cf_clearance|secret|password"` and confirm nothing sensitive appears.

---

### Embedded feedback

✓ **Sequencing** — dependencies flow correctly: pure Core (1–4) is the testable parity bar before any UI; the widget (5) needs Core; the WebView2 login (6) needs the credential store + a fetch to verify end-to-end; ship (11–12) last. △ **Granularity** — items 5, 8, 10 each bundle several ported modules; acceptable for an experienced builder porting against an exact Python reference, but 10 is the chunkiest *and* cert-load-bearing, so its milestone gate matters most. ✓ **Completeness** — every `spec.md > Module map` row maps to an item; data-compat and cert constraints are threaded into acceptance. △ **Open thread** — item 10's hourglass is provisional pending the deepening round; back-merge before /build reaches it.

---

## Iteration 1 (Mac): Desk becomes home

From /iterate, 2026-10-02, after 2.1.0 shipped. Direction: Desk is the default surface and carries
the meters; the widget stays as the toolbox for the deep tools (pacing calculators, Deep Work,
Cooldown Snake, themes, history once M7 lands). Mac sources only; build and test with
`cd mac && ./build.sh --debug && ./test.sh`; every user-visible change gets a line under
`## Unreleased (mac)` in `CHANGELOG.md`.

- [x] **13. Meters on Desk**
  Spec ref: New — not in original spec (`docs/mac-merge-plan.md` > Phase 1, item 2: the desk reads `UsageViewModel` directly)
  What to build: a `meters` Desk piece, placeable in any corner like the others (`DeskLayout.widgets`, `DeskView`). One row per limit the widget shows (session, weekly, and any other weekly tiers): label, a bar drawn in the Desk ink (single color or gradient) and font with the drop shadow, the pace marker, the percent and the reset time. Desk gets its numbers from the in-process `UsageViewModel` instead of polling `snapshot.json` (the widget keeps writing the snapshot for the statusline and MCP). The notch wings are unchanged.
  Acceptance: `meters:br` in the layout shows a bar per limit that updates within one refresh of the widget, with the pace marker where the widget puts it; hiding the widget does not stop updates; the existing `claude` line still works for anyone who keeps it.
  Verify: swift-testing for a pure row model (label, fill fraction, pace fraction, reset text) built from fixture usage, including a missing reset time and a tier above 100%; then on screen.

- [x] **14. Desk is the default for new installs**
  Spec ref: New — reverses `docs/mac-merge-plan.md` > Decisions ("desktop layer and notch are off by default") for new installs only
  What to build: on first launch with a fresh defaults domain (no widget settings, no legacy Desk domains), turn Desk on with meters in the layout, meetings off (no Calendar prompt until the user turns meetings on), the notch off, and the widget panel hidden. Anyone with existing widget settings, or migrating from Sanduhr Desk, keeps exactly what they have.
  Acceptance: a fresh domain shows the meters on the desktop at first launch with no window and no permission prompts; a 2.1.0 user who updates sees no change; a Sanduhr Desk migrant gets their imported layout.
  Verify: swift-testing for a pure first-run decision with in-memory stores (fresh, existing widget user, Sanduhr Desk migrant); `docs/mac-smoke-test.md` section 1 rewritten for the new default.

- [x] **15. The toolbox is one click away**
  Spec ref: New — not in original spec
  What to build: the Desk meter rows take clicks the way meeting rows do (the click-through stays everywhere else); a click shows the widget beside the meters. A **Tools** section in the menus: Show Sanduhr, Deep Work, Pacing Calculators, Cooldown Snake (History when M7 lands). A one-time hint under the meters on first run ("Click the meters for history and tools. Option+S for settings."), gone after the first meter click or three days.
  Acceptance: with the widget hidden, clicking the meters opens it next to them; every deep tool is reachable from the menu without the widget showing first; the hint appears once and never again after it is dismissed.
  Verify: swift-testing for the hint's shown/dismissed state; on screen with Ice hiding the menu bar icon.

## Iteration 2 (Mac): one control surface

- [x] **16. One Settings window, one menu**
  Spec ref: `docs/mac-merge-plan.md` > Phase 1, item 4 (one Settings window with a Surfaces list)
  What to build: a standalone Settings window (sidebar) replacing both the widget's settings sheet and Desk Settings: General (surfaces Desk, Notch and Widget; shortcuts; open at login), Desk Layout, Desk Look, Message, Notch, Widget Look (themes, font, subtle mode), Alerts, Credentials. One menu model builds the menu bar item's menu, the widget's two-finger menu and Desk's clock menu: Show/Hide Sanduhr, Tools, Refresh, Settings…, Check for Updates…, Quit. Option+S, `sanduhr://settings`, the notch island and every Settings item open the same window. Defaults keys stay as they are.
  Acceptance: every setting is reachable with the widget hidden and the menu bar icon hidden by Ice; the three menus list the same items in the same order; no saved setting is lost across the update.
  Verify: swift-testing for the menu model's items; smoke test updated.

- [x] **17. Alerts v2: pace, delivery, quiet and sound**
  Spec ref: New — extends the 2.1.0 alerts (`Notifier.swift`)
  What to build: move the alert decisions into a pure rules type and add: a **pace warning** when `burnProjection` says a limit will hit 100% before it resets (once per window, with the projected time and the reset time in the text); **where alerts show**: banner, a pulse on the Desk meters and notch island, or both; **quiet hours** (start and end, may cross midnight; banners held back, Desk pulses still shown); a **sound** picker (system sounds or silent); and a **weekly reset** alert beside the session one. Existing thresholds keep working.
  Acceptance: fixture usage on pace to run out fires one pace warning naming both times; during quiet hours no banner is posted; Desk-only delivery pulses the meters and posts nothing; each alert still fires once per reset window.
  Verify: swift-testing for the rules (pace, quiet hours across midnight, once-per-window, session and weekly reset detection); one real banner and one Desk pulse checked by hand.

## Iteration 3 (Mac): the notch does more, themes you can see

From /iterate, 2026-10-02, after 2.2.1. Same build rules as Iterations 1 and 2. Ships as 2.3.0.
New options keep today's behavior by default; the camera light and the glow start off.

- [x] **22. Calendar access that explains itself**
  Spec ref: New — follows a 2.2.1 report (Calendar allowed in System Settings, Desk still showed "Allow Sanduhr…")
  What to build: the Desk calendar note becomes clickable and opens System Settings at Privacy & Security, Calendars. Distinguish the states: not asked yet (ask), denied (the existing line), "Add Events Only" (its own line saying Full Access is needed). Recheck authorization when Sanduhr becomes active, when Settings opens, and on each Desk minute tick while not fully authorized, so a grant made in System Settings shows up within a minute without a relaunch (use a fresh `EKEventStore` if the old one keeps the stale answer).
  Acceptance: granting Full Access in System Settings clears the note within a minute and loads meetings with no relaunch; Add Only shows the Full Access line; clicking the note opens the right System Settings page.
  Verify: swift-testing for the status-to-message mapping; on screen by toggling the permission.

- [x] **18. Themes you can see**
  Spec ref: New — Settings, Widget, Themes (`Views/WidgetSettings.swift`, `Models/Theme.swift`, `Services/UserThemes.swift`)
  What to build: the Themes page opens on a gallery of every theme, built-in and user, each a small preview (background, card, bar colors, accent) with its name; the current one marked; one click applies it live. The existing custom-theme tools (paste JSON, agent prompt, folder, reload, delete) stay below the gallery.
  Acceptance: every built-in and installed user theme appears with a recognizable preview; clicking applies it to the widget at once; the widget's own Theme menu and the gallery agree.
  Verify: swift-testing for the gallery's theme list (built-ins plus user themes, current flag); smoke scenario opening Settings at Themes and finding theme names.

- [x] **19. You choose what the notch shows**
  Spec ref: New — `Desk/NotchView.swift` (`NotchWingsView.layout`, `leftText`, `NotchView.line`)
  What to build: Settings, Desk, Notch gets a picker for the left wing, the right wing and the strip under the camera, each one of: next meeting (or the time when none is due), time, Claude meters, message, nothing. Defaults reproduce today exactly (left: meeting or time; right: meters; strip: meeting or meters). Wing width still grows to fit its text.
  Acceptance: each choice shows on the right wing within a refresh; "nothing" leaves a plain black wing; an update changes nothing for anyone until they pick.
  Verify: swift-testing for the pure text-per-slot function across choices, times and stale meters; smoke scenario for `state.yaml` notch slots.

- [x] **20. The notch as a camera light**
  Spec ref: New
  What to build: while any app uses the camera (CoreMediaIO `kCMIODevicePropertyDeviceIsRunningSomewhere` on video devices, observed with a property listener; no permission), the notch area glows as a soft white light to light the user's face: a click-through window above every app around the notch (wings plus a band below, rounded, feathered edge), with brightness and size sliders in Settings, Desk, Notch, off by default. Ends when the camera stops. Works with the notch island on or off; on a screen without a notch, the light sits at the top center of the main screen. A Tools menu item turns it on by hand for a test.
  Acceptance: turning on a FaceTime or Teams camera lights it within a second, turning it off ends it; it never takes clicks; it sits above full-screen apps.
  Verify: swift-testing for the camera-state reducer (several devices, flapping); by hand with Photo Booth.

- [x] **21. The notch glows for Sanduhr's events**
  Spec ref: New — extends the item 17 Desk pulse
  What to build: an outer glow around the notch island (ink colored, a few seconds, gentle) for Sanduhr's own events: an alert (any delivery), a meeting starting in the next minute (once per meeting), the camera light coming on. Each event type has a switch in Settings, Desk, Notch, all off by default. No sound detection (no public API for other apps' sounds; an audio tap needs a recording permission and is out of scope).
  Acceptance: each enabled event glows once; disabled ones do not; the glow never blocks clicks or covers app content beyond the island's edge.
  Verify: swift-testing for the event-to-glow rules (once per meeting, switches); `smoke do pulse` style action to trigger the glow on demand.

- [x] **23. When the widget shows**
  Spec ref: New — Settings, General, Surfaces (`Views/SettingsWindow.swift` General section, `AppDelegate` show/hide, `DeskFirstRun`)
  What to build: a Widget setting with three choices: **Always** (today's behavior; the default for existing users), **While Desk is off** (hidden whenever Desk is on, shown when Desk is off; the default for new installs, replacing the tuck-after-first-fetch flag's one-time hide once sign-in is done), and **Never on its own** (only appears when asked: meter click, Tools, Show Sanduhr). Showing or hiding from a menu still works as a one-off; the choice takes over again when Desk turns on or off and at launch. The existing `panelHidden` key keeps meaning "hidden right now".
  Acceptance: with "While Desk is off", switching Desk off shows the widget and on hides it; "Never on its own" keeps it hidden across launches until asked; an updated install sees no change.
  Verify: swift-testing for a pure visibility rule (setting, Desk on/off, manual override, launch); smoke scenario toggling Desk with each setting.

- [x] **24. A "Match Desk" theme for the widget**
  Spec ref: New — builds on item 18's gallery (`Models/ThemeGallery.swift`, `Views/ThemeGalleryView.swift`, `Models/Theme.swift`)
  What to build: a built-in "Match Desk" theme in the gallery that draws the widget in Desk's look: the Desk font, the Desk ink color or gradient for text and bars, the Desk drop shadow, no glass background (like subtle mode). It follows the Desk settings live (font, ink, shadow). Also re-resolve the current theme by id when user themes reload, so an edited theme restyles the widget without being picked again.
  Acceptance: picking Match Desk makes the widget read like part of the desktop; changing Desk's ink or font restyles it at once; other themes are unaffected.
  Verify: swift-testing for the Desk-to-palette mapping (single color, gradient, empty fallback); smoke scenario picking it and checking `state.yaml` theme.

- [x] **25. Meter warnings on Desk, set per meter**
  Spec ref: New — follows review of the 2.3.0 build (Desk meters: `Desk/DeskMeters.swift` `DeskMeterRow`, `MeterRow` in `Desk/DeskView.swift`; glow technique from `Desk/NotchGlow.swift`)
  What to build: a Desk meter row is "warning" when its fill is at or above a threshold and its reset is more than a set time away. A warning row draws its bar red (the standard over-limit red) with a steady glow in the Desk ink gradient around the bar. Each meter is independent: session, weekly, and every other weekly limit get their own switch, threshold and minimum time to reset, on a new Desk, Meters settings page. Defaults: weekly limits on at 90% with more than 1 day to reset; session off (when turned on: 90%, more than 1 hour). A missing reset time counts as "far away".
  Acceptance: a weekly meter at 92% with 3 days left shows red with the ink glow; the same meter with 6 hours left stays normal; the session meter never warns until switched on; changing a setting restyles the row at once.
  Verify: swift-testing for the pure warning rule (per meter settings, threshold edge, time edge, missing reset); `state.yaml` meters gain `warning`; a smoke scenario that sets a low threshold to force a warning and checks the state.

- [x] **27. One notch glow, the plain notch too**
  Spec ref: New — review of the 2.3.0 build (`Desk/NotchView.swift` wings pulse stroke from item 17; `Desk/NotchGlow.swift`)
  What to build: the item-17 Desk pulse stroked the island's whole outline (top edge included) with a white shadow, so during a meter pulse light showed in the band between the wings and the screen edge. Remove that stroke; a Desk pulse fires the item-21 outer glow instead (forced, like the debug action), so there is one glow with one look: up the sides to the top of the wings, along the bottom, nothing at the screen edge. When the island is not extended (notch switch off) on a notched screen, the glow hugs the plain hardware notch outline (its width and height, rounded bottom corners) with the same treatment, so users who don't extend the notch can still have it glow.
  Acceptance: a Desk pulse shows no light above or beside the top of the wings; with the island off, a glow outlines the hardware notch; with the island on, unchanged from item 21 with its fixes.
  Verify: swift-testing for the plain-notch glow layout; smoke: pulse and glow scenarios with the island on and off; an on-screen capture of the notch area during a pulse.

- [x] **26. Warning bars on the widget too**
  Spec ref: New — extends item 25 (`Desk/MeterWarning.swift`, widget bars in `Views/ProgressBarView.swift` / `Views/TierCardView.swift`)
  What to build: the widget's tier bars use the same per-meter warning rule and settings as the Desk meters (item 25): a warning tier's bar turns red with a steady glow in the current theme's accent (in Match Desk, the Desk ink gradient) around the bar. Non-warning bars unchanged. The Meters settings page says it applies to both the Desk and the widget.
  Acceptance: a weekly tier at 92% with days left shows red with a theme-color glow on the widget, and the same on Desk; turning its warning off clears both.
  Verify: swift-testing that the widget's warning state per tier matches `MeterWarning` for the same inputs; smoke: state for the widget tiers gains `warning` (or reuse meters state) and a scenario forcing a warning checks it; an on-screen capture of the widget.

- [x] **28. Quit says what it quits**
  Spec ref: New — review of the 2.3.0 build (the menu model `Models/SanduhrMenu.swift`)
  What to build: the menus' first item reads Show Widget / Hide Widget (it only ever touched the floating widget), and Quit reads "Quit Sanduhr für Claude" so it is clear it closes everything (widget, Desk, notch). Same in all three menus and on Settings, General's Quit button, with a caption there saying it closes the widget, Desk and the notch.
  Acceptance: the three menus and Settings use the new names; nothing else changes.
  Verify: menu model tests; smoke scenarios that match menu titles updated.

- [x] **29. Match Desk draws the line sparkline**
  Spec ref: New — on-screen review of item 24 (`Views/SparklineView.swift`, `Views/TierCardView.swift`)
  What was built: under Match Desk there is no card behind the sparkline, so the horizon chart of a meter that sat high read as a solid block of the ink. Match Desk now draws the line sparkline; every other theme keeps the horizon chart. `SparklineView.mode(themeID:)` decides, tested.
  Acceptance: on the widget under Match Desk the sparkline is a thin ink line; other themes unchanged. Verified on screen (window capture) on the dev build.

## Iteration 4 (Mac): settings you'd expect

- [ ] **30. Updates and About in Settings**
  Spec ref: New — after 2.3.0 (`Views/SettingsWindow.swift` sidebar; Sparkle `SPUStandardUpdaterController` in `AppDelegate`)
  What to build: a "Sanduhr" group at the bottom of the Settings sidebar with two sections. **Updates**: installed version and build, last check time, a Check Now button, switches for checking automatically and for downloading and installing automatically (Sparkle's own settings, so the menus' Check for Updates… and these agree), and a link to the release notes. **About**: icon, "Sanduhr für Claude", version and build, a one-line description, links (website, GitHub, release notes, privacy policy, license), credits (Sparkle), and the independence line ("Independent third-party tool. Not affiliated with Anthropic. Requires an active Claude Pro / Team / Enterprise subscription.").
  Acceptance: both sections open from the sidebar and from `sanduhr://settings` style section links; Check Now starts Sparkle's check; the switches change Sparkle's behavior and survive a relaunch; every link opens the right page.
  Verify: swift-testing for the section list and the About link set; smoke `settings-sections` covers both new sections and finds "Check Now" and the independence line by text.

- [x] **31. Credentials in the Keychain for signed builds**
  Spec ref: `docs/mac-merge-plan.md` > Phase 2 ("Real Keychain once signed with a Developer ID"); `mac/Sources/Sanduhr/Services/KeychainStore.swift` (today a 0600 JSON file, with the reasons in its header)
  What to build: when the running app is signed with the team's Developer ID (team 82BSR56X5J, read from its own code signature), store the session key and cf_clearance as generic-password Keychain items (service `com.626labs.sanduhr`, accessible after first unlock, no biometric ACL; the default ACL ties them to the app's signed identity, so updates never prompt). Move existing credentials once: read the file, write the Keychain, read back and compare, then delete the file; on any failure keep the file and carry on with it. Ad-hoc and unsigned builds keep the file. Sign-out and credential changes go to whichever store is active. Never log a value.
  Acceptance: a 2.3.1 user updating to 2.3.2 keeps working with no sign-in and no Keychain prompt, the file is gone and the Keychain holds the key; a dev build still uses the file; a failed Keychain write leaves the file and the app working.
  Verify: swift-testing for the store decision and the migration steps with fake stores (signed or not, file only, Keychain only, both, write failure, read-back mismatch); smoke `state.yaml` gains `credentials_store` (keychain or file, never the value); on screen with the signed 2.3.2 build.

- [x] **32. Sign out**
  Spec ref: `docs/mac-merge-plan.md` > Phase 2 / feature inventory ("Sign out / clear credentials", Windows 2.0.4); found missing while building item 31 (mac/README described a sign-out that never worked)
  What to build: a Sign Out button in Settings, Credentials, behind a confirmation ("Sign out of Sanduhr? Your session key is removed from this Mac. Your usage history and settings stay."). Signing out deletes the session key and cf_clearance from both the Keychain and the credentials file, stops the refresh, clears the shown usage, puts the widget, Desk and notch in a signed-out state with a way back to sign-in (onboarding / Credentials), and writes `snapshot.json` so readers stop showing stale meters (status error with the auth error kind the shared set already uses, no tiers). History and settings are kept. No debug or smoke action signs out (a test run must never wipe real credentials).
  Acceptance: after Sign Out, no credential remains in either store, nothing is fetched, the Desk and notch show the sign-in line instead of meters, and signing in again works without a relaunch.
  Verify: swift-testing on fake backends (both stores cleared, either store empty, delete failure reported, snapshot written signed out); smoke checks the button exists on the Credentials page; by hand later.

- [x] **33. Issue #105 fixes**
  Spec ref: GitHub issue #105 (should-fixes from the PR #93 review, re-checked at `v2.3.2-mac`); `AppDelegate.swift` launch gate, `Models/WidgetVisibility.swift`, `Views/TierCardView.swift`, `Desk/DeskView.swift`, `Desk/MeterWarning.swift`, root `README.md`
  What was built: (1) a saved session key counts as signed in only after it has fetched once: `SignInGate` keeps `signedInFetchDone` in the standard defaults, set by a successful fetch, cleared by Sign Out or an auth error, and the launch shows the widget for sign-in until it is set (an update from 2.3.2 shows it once, until the first good fetch). (2) Choosing "Always shown" shows a widget an earlier choice hid; at every other event Always shown still leaves it as it is. (3) A warning meter carries `exclamationmark.triangle.fill` before its percent on the widget (theme text color) and Desk (Desk ink), and VoiceOver reads "92%, nearly full" on a warning row, the plain percent otherwise. (4) The root README says the Mac keeps credentials in the Keychain (release) or the `0600` file (dev), and points at Sign Out before trashing the app.
  Acceptance: a relaunch with a bad saved key and "Hidden while Desk is on" shows the widget; picking Always shown brings a tucked widget back; a warning bar is told apart from a plain 90%+ bar without color and by VoiceOver.
  Verify: swift-testing for `SignInGate` (never-fetched key, marker cleared by sign-out or a refused key, the 2.3.2 upgrader path), Always shown on `.choiceChanged`, and `MeterWarning.spokenValue`; on screen: the triangle on a forced warning on the widget and Desk (smoke-test "Warning bars" step), and VoiceOver on one warning and one plain row.

- [x] **34. Issue #105 follow-ups**
  Spec ref: issue #105, "Follow-ups from the same review" (closed with 2.3.3)
  What to build: (a) the notch glow traces only what is visible: when an app window covers the strip under the camera (the strip lives in the Desk window, below app windows), the glow follows the wings/notch outline, not the hidden strip; (b) `CameraMonitor` removes its CoreMediaIO property listeners with the same block it added (store the block; the Core Audio block-bridging trap) and logs a failed add/remove `OSStatus` once, without spamming; (c) when the current user theme is deleted outside the app, Sanduhr falls back to the default theme right away (and says so in the log), instead of keeping it until the next launch.
  Acceptance: no glow traced around an invisible strip; listener add and remove statuses are checked and logged on failure; deleting the active theme's file switches to the default theme without a relaunch.
  Verify: swift-testing for the pure parts (glow outline choice from strip visibility, theme fallback decision); a smoke check or state key where useful; the CMIO statuses by hand (Console) on a Mac with a camera.

- [x] **35. Pick the organization that has the usage**
  Spec ref: Windows `ClaudeApiParsing.ParseOrganizations` (2026-07-19: a login can carry a claude_max subscription org and an API individual org; `orgs[0]` is ordering luck)
  What to build: the Mac tracks the first organization `/api/organizations` lists. Port the Windows rule: the first org whose `capabilities` include `claude_max`, else the first with `chat`, else the first.
  Acceptance: a login whose first org is an API org fetches the subscription org's usage.
  Verify: swift-testing on the choice and on decoding real-shaped org JSON (capabilities missing, empty, mixed).

- [x] **36. Accounts (Windows 2.2 parity)**
  Spec: `docs/mac-accounts-spec.md`.
  Spec ref: `docs/mac-merge-plan.md` feature inventory (multi-account registry, Accounts tab, active-account label, account-scoped sign-out; per-account history); Windows `AccountStore`, `UsageHistory`, `SnapshotContract.AccountRef`
  What to build: named accounts (registry and active label in defaults; each account's key in the Keychain under `sessionKey:{label}` / `cf_clearance:{label}`, the file on dev builds); the existing key promotes to "Personal" on first launch; Settings, Accounts to add, rename, remove and switch; the active account's name on the widget and in the menus; Sign Out removes the active account; history per account (`history.{label}.json`, the current file moving to Personal); `account_ref` in `snapshot.json` hashed as on Windows. Only the active account is shown and fetched on the normal cadence (decided 2026-10-03); "Follow the account I'm using" (off by default) slow-checks the others and switches automatically on a clear signal.
  Acceptance: two accounts switch without a relaunch, each with its own history; signing out one leaves the other; an upgrade lands on Personal with nothing lost.
  Verify: swift-testing on fakes (registry, migration, switch, scoped sign-out, history paths, account_ref matching Windows); smoke state key for the active account (hashed, never the label); by hand with two keys.

- [ ] **37. All-accounts chart and CSV export** (later; decided 2026-10-03)
  Spec ref: `docs/mac-merge-plan.md` (Per-account history, All-accounts chart toggle, CSV export)

- [x] **38. Menu bar limit choice, hide special limits, inline duplicate-name error**
  Spec ref: on-screen check of item 36 (2026-10-03): a used-up promo limit ("Weekly — Special", `iguana_necktie`) pinned the menu bar at 100% because it shows the highest of every limit; Add Account's duplicate-name error showed only after Add, while the label-rule error shows under the field as you type.
  What to build: (a) Settings, Menu bar choice: Session, Weekly, Whichever is higher (default), Rotate (alternates the two every 8 s, "S 12%" / "W 96%"); limits other than Session and Weekly never drive the menu bar. (b) A Show switch per limit other than Session and Weekly in Settings, Desk, Meters (on by default, so a new limit appears once and the user decides); off hides it on the widget, Desk and from alerts. (c) Add Account (and Rename) show the duplicate-name error inline under the field as you type, like the label-rule error.
  Acceptance: a 100% special limit no longer shows in the menu bar; hiding it removes it everywhere; Rotate alternates; a duplicate name is flagged before Add.
  Verify: swift-testing for the menu bar choice (each mode, missing tiers, rotation step) and the visible-tier filter; state.yaml key for the menu bar mode; by hand.

- [x] **39. Switch, hide and silence from the meters**
  Spec ref: on-screen check of items 36 and 38 (2026-10-03): the user expected to switch accounts and hide or silence temporary limits right on the Desk, not only in Settings.
  What to build: (a) the account name at the start of the Desk's claude line is clickable with 2+ accounts and cycles to the next account (same path as the widget chip); a plain click on the bars still opens the widget. (b) A context menu (two-finger click) on Desk meter rows and widget tier cards: Accounts submenu (2+ accounts, active checked); "Hide <limit>" for limits beyond Session and Weekly (the item-38 Show switch); "Stop warnings for this limit" / "Warn again for this limit" (the existing "Warn when nearly full" setting for that limit); "Meter Settings…" opening Settings, Desk, Meters. Settings and the menus stay in sync.
  Acceptance: switching, hiding and silencing all work from the Desk and the widget without opening Settings.
  Verify: swift-testing for the menu model (which items appear for which tier and account count); smoke state reflects hide/silence; by hand.

- [x] **40. Desk meter menu, menu bar submenu, graceful account switch**
  Spec ref: on-screen check of items 38 and 39 (2026-10-04).
  What to build: (a) the two-finger (right-click / Control-click) menu on the Desk meter rows does not open; make it open over the Desk bars as it does on the widget cards, without a second menu from the Finder. (b) A "Menu Bar Shows" submenu in the menu bar's own menu (Session, Weekly, Whichever is higher, Rotate; the current one checked), in sync with Settings, General, Menu bar. (c) Switching accounts is graceful: the old account's meters fade out and the new account's fade in when they arrive (widget cards, Desk meters, the Desk line), with a faint "Switching account…" in between when the fetch is slow; never a hard blank. Old numbers are never shown as the new account's (they fade out before the new ones arrive, and the snapshot is still deleted at once).
  Acceptance: the Desk bar menu opens; the menu bar mode can be changed from the menu bar; a switch reads as a crossfade.
  Verify: swift-testing for the menu model; by hand for the menu and the animation.

- [x] **41. Desk geometry you can trust, passive meters, a gentle departure**
  Spec ref: on-screen check 2026-10-04: Desk clicks were dead because the meters' frame never reached the click code (fixed in `9901381`); the user asked that every Desk item's coordinates be known so they interact properly; a plain click on the meters repeated the same meters in the widget; the leaving account still vanished abruptly.
  What to build: (a) every interactive Desk element (meters block, each meter row, the account name, the calendar note, each meeting row, the meetings block) publishes its frame in `state.yaml` (`desk_frames`, keyed by element kind and tier or row index, never labels or meeting titles), plus a smoke scenario that fails when a visible element's frame is empty, off the Desk window, or when two click areas overlap; a pure, tested hit-test that picks the element under a point in priority order, used by DeskController for both left and two-finger clicks. (b) A plain click on the meters does nothing (passes through to the desktop); the two-finger menu keeps everything and gains "Show Widget"; the "Click the meters…" hint goes. (c) Account switch departure: slower, gentler fade out (about 0.6 s ease-in-out), and the account name on the chip and the Desk line changes only when the old numbers are gone.
  Acceptance: smoke catches a dead click area; a plain meter click does nothing; the switch reads as a calm handoff.
  Verify: swift-testing (hit-test, frame checks); smoke scenario; by hand.

- [x] **42. Hide only temporary limits, and bring them back when they refill**
  Spec ref: on-screen check 2026-10-04: hidden meters should come back if they refill; Hide should be offered only for limits believed temporary.
  What to build: (a) a pure, tested `LimitLifetime` rule: Session and Weekly — All Models are never temporary; another limit is temporary when its reset is more than 8 days away, or it is a known promo slot (`iguana_necktie`), or it first appeared within the last 14 days and is not a known model limit (Sonnet, Opus, Cowork, Design, OAuth Apps). First-seen dates per tier are kept in the desk defaults. Hide (menus and Settings' Show switch) is offered only for temporary limits; a hidden limit that is no longer temporary shows again. (b) Hiding records the limit's reset time and utilization at that moment; it shows again on its own when that reset passes (a new window) or its utilization drops by 10 points or more (a refill). (c) The Desk and widget two-finger menus gain a "Hidden Limits" submenu (only when something is hidden) to show one again by hand. (d) The Desk takes the mouse as soon as the pointer is over a click area, including right after launch and after the layout moves, so a two-finger click without prior movement still reaches Desk instead of the Finder.
  Acceptance: no Hide on permanent limits; a refilled or reset hidden limit reappears; hidden limits can be restored from the menu.
  Verify: swift-testing for every rule (temporary detection, refill and reset return, permanent limits never hideable); state.yaml `hidden_limits`, plus `temporary_limits`; by hand.

- [x] **43. Thirty days of meter history**
  Spec ref: `docs/mac-usage-data-spec.md` > Build order 1 (Windows keeps 30 days per account; the Mac kept 24 points, about 2 hours)
  What to build: `HistoryStore` keeps 30 days per account (time-based trim plus the Windows point cap of 8640), reads old files unchanged; the sparklines keep drawing their recent window from the longer series; a per-account "Meter history: Off · 30 days" choice (default 30 days) in Settings, Accounts, where Off stops recording for that account and offers to erase its file.
  Acceptance: history survives 30 days, sparklines unchanged, Off stops and erases on request.
  Verify: swift-testing on temp folders (trim by age and count, old 24-point files, Off); by hand.

- [x] **44. The Data section and Claude Code folder linking**
  Spec ref: `docs/mac-usage-data-spec.md` > What an account can have; Suggesting the folder; Decisions
  What to build: per-account data choices stored and shown in Settings, Accounts, Data (Meter history from item 43, Claude Code folder, Claude Code activity: Not tracked · Live only · Keep a record, Project names: Names · Hidden · Full paths, Share with Claude: Off · Meters · Meters and activity; defaults as the spec); Claude Code folder discovery (`~/.claude`, `~/.claude-*` folders that look like Claude Code homes, plus Choose…); one folder per account and one account per folder; the suggestion by organization match (read only `oauthAccount.organizationUuid` from the folder's `.claude.json`, in memory, never stored or logged; compare with the account's organization from claude.ai); choices follow rename and removal. Activity, names and sharing are stored for items 45 to 47 to act on.
  Acceptance: each account's choices persist and follow rename; a folder can't be linked twice; the matching folder is suggested, never auto-linked.
  Verify: swift-testing on temp homes (discovery, the `.claude.json` placement rule, the org match with no other field read, link rules, rename/remove); state.yaml keys without labels or paths; by hand.

- [ ] **45. Live Claude Code activity**
  Spec ref: `docs/mac-usage-data-spec.md` > What an account can have (Claude Code activity: Live only); Windows `CcLogReader`, `LocalCcViewModel`, `TierCardViewModel` local delta
  What to build: a Swift port of the Windows log reader (session JSONL under the linked folder's `projects/`, usage events, per-model and per-project sums, worktree/subfolder folding, the double-lag rule), run for the active account when its activity is Live only or Keep a record; the "+Nk" local burn badge on the widget's cards (local Claude Code tokens since the last meter refresh, as Windows shows); nothing stored. Activity Not tracked or no linked folder reads nothing.
  Acceptance: with a linked folder and Live, cards show local burn that matches the Windows reader on the same fixtures; with Not tracked, no file under the folder is opened.
  Verify: swift-testing on the Windows fixtures (ported) and temp folders; never real Claude Code folders in tests; by hand with a test folder.

- [ ] **46. The vault: keep a record per account**
  Spec ref: `docs/mac-usage-data-spec.md` (Keep a record, Project names, Erase); `docs/superpowers/specs/2026-07-12-usage-vault-design.md`; Windows `VaultIngester`, `VaultStore`, `VaultReader`, `VaultModels`, `VaultService`
  What to build: a Swift port of the vault for a linked folder whose account keeps a record: monthly session shards, rollups (a rebuildable cache), path-hash checkpoints written last, quarantine of unreadable shards as `.bad`, a single writer (a lock file in place of the Windows mutex), ingest at launch and every refresh off the main thread, the Windows file formats under `~/Library/Application Support/Sanduhr/vault/<folder id>/`. Project names per the account's choice: Names, Hidden (a stable short hash in place of the name, still grouping by project), Full paths (cwd kept). Erase (the account's vault, with the record choice turned off first as the tombstone, and a recheck so an in-flight ingest can't recreate it). Turning Keep a record off asks whether to erase.
  Acceptance: the vault matches the Windows vault's outputs on the same inputs; Hidden never stores a project name; erase is complete and stays erased; Live only and Not tracked never write.
  Verify: swift-testing ported from the Windows vault tests (torn files, month boundaries, worktree folding, checkpoints, quarantine, erase race); temp folders only; by hand with a test folder.

- [ ] **47. Sharing with Claude: the MCP tools and the access file**
  Spec ref: `docs/mac-usage-data-spec.md` > Share with Claude; How the MCP server learns the choices; Windows `Sanduhr.Mcp` (`ToolCatalog`, `ToolLogic`, `HistoryReader`, `SnapshotReader`)
  What to build: the app writes `~/Library/Application Support/Sanduhr/mcp-access.json` (per account: `account_ref`, sharing level off|meters|activity, the vault folder id and history file it may read; no labels or paths beyond what the server must open); `mac/integrations/sanduhr_mcp.py` gains `get_local_burn_by_project`, `get_model_usage`, `get_usage_history` with the Windows tool names and result shapes, reading the vault and history only as the access file allows (Hidden names stay hidden), `get_usage` honoring sharing (off = nothing for that account), `ping` updated; no network, no free-form paths. `publish_usage` stays dropped; `propose_theme` decided separately.
  Acceptance: each sharing level returns exactly what the spec allows; with no access file nothing is shared; results match the Windows tools on the same vault.
  Verify: Python tests over a temp app-support folder with synthetic vault files (the item 46 fixtures); the Swift side tested for the access file; by hand with Claude Code calling the tools on a test folder.
