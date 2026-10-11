# Build Checklist

## Build Preferences

- **Build mode:** Autonomous. The orchestrator dispatches bulk items to workers (`sonnet`) and keeps verification, review and merging itself (judgment tier).
- **Comprehension checks:** N/A (autonomous mode).
- **Git:** One branch and one PR per stage against main, rebased as main moves. Conventional commits per item (`feat(win): …`, `test(core): …`, `docs(win): …`), each ending with the Co-Authored-By line. Merge with `gh pr merge --merge` once the gates pass.
- **Verification:** Yes. After each item: `dotnet build windows-dotnet` (0 warnings) and `dotnet test windows-dotnet` (green); the orchestrator re-reads every worker's diff against the spec before checking an item off. Stage gate: CI green and an independent review agent with no unresolved correctness findings.
- **Check-in cadence:** N/A for autonomous. Escalations (manifest, data location, privacy claims, 10.1.4.4 surfaces, the MCP trust boundary) stop that item and go to Este.
- **Workers in parallel only on disjoint files.** Core files and their tests (items 1 to 6, Core half) can run together; App wiring (the second half of each) touches shared files (`WidgetViewModel`, `SettingsStore`, `SettingsWindow.xaml`, `App.xaml.cs`) and runs one worker at a time.

## Checklist

### Stage 1: What's New and quick wins (this run)

- [ ] **1. Every Claude Code home counts (W7)**
  Spec ref: `spec.md > Stage 1 > 1.3 Claude Code home discovery`
  What to build: `Sanduhr.Core/ClaudeCodeHomes.cs` (`IsCandidateName`, `IsBackup`, `LooksLikeHome`, `ConfigFile` with the `.claude` placement rule, `Discover` with natural sort and the in-profile `CLAUDE_CONFIG_DIR` rule); `CcLogReader(string? homeDir, Func<IEnumerable<string>>? rootNames)` with `SearchRoots()` using the provider; `WidgetViewModel.cs:475` constructs the shared reader with the discovery provider. `ClaudeCodeHomesTests.cs` with temp-directory fixtures (the Mac's cases: projects-only, config-only, decoys `.claude-`, `.claudette`, a file named `.claude-file`, `.claude-notdir/projects` as a file, backups, natural order "a2" before "a10", the placement rule cases, `CLAUDE_CONFIG_DIR` in and outside the profile); a `CcLogReader` test that the provider is honored and the one-argument constructor still returns the two-name set.
  Acceptance: a third home such as `.claude-work` with a `projects` folder feeds local burn and model usage and appears as an unconsented toggle in the vault and publish lists; the MCP, `McpConfig`, `StatuslineInstaller` and `McpIntegrationInstaller` are unchanged; `TrustBoundaryTests` pass untouched.
  Verify: tests green; `git diff` shows no change under `src/Sanduhr.Mcp/` or to `Sanduhr.Mcp.csproj`.

- [ ] **2. Steady sparklines draw a level line (W5)**
  Spec ref: `spec.md > Stage 1 > 1.6 Steady sparklines`
  What to build: `Sanduhr.Core/SparklineSteady.cs` (`Drawn`, `LevelY`); `SparklineControl.OnRender` paints Level (12% wash below, 85% 1.5 px line). `SparklineSteadyTests.cs` with the Mac's cases ([96, 96, 96.4, 96, 97.2] Level; [40, 52, 61, 70] Horizon; spread exactly 2.0 and 2.5 Horizon; Classic never changes; empty unchanged) plus `LevelY` at 0, 50, 100 and clamping.
  Acceptance: Horizon with a spread under 2 draws a level line at the mean on a 0 to 100 scale; everything else draws as before.
  Verify: tests green; describe the drawing code path (the screen can't be seen from here).

- [ ] **3. What the tray shows (W3)**
  Spec ref: `spec.md > Stage 1 > 1.5 What the tray shows`
  What to build: `Sanduhr.Core/TrayReadout.cs`; `WidgetViewModel` raises `TrayReadingChanged(TrayReading?)` everywhere `TrayPercentChanged` fired (null for the -1 resets) from the fetch data and the visible cards; `App` renders the icon and tooltip, runs the 8 s Rotate timer only in Rotate; `tray_mode` in `SettingsStore`; a "Tray Shows" submenu in the tray menu (checkable, current checked) and a "Tray shows" combo on Settings, General, both writing the same key and taking effect at once. `TrayReadoutTests.cs` with the Mac's cases ((12, 96): session 12, weekly 96, higher 96; (40, 20) higher 40; tie picks session; rotate steps 0, 1, 2, 7; only-weekly rotate step 0 "Weekly 63"; only-session step 1; truncation 96.7 to 96; nothing gives null) plus Highest and Parse.
  Acceptance: the five modes behave as specified; an existing install still shows the highest visible limit.
  Verify: tests green; tray menu wiring reviewed in the diff.

- [ ] **4. Limits that come and go (W2)**
  Spec ref: `spec.md > Stage 1 > 1.4 Limits that come and go`
  What to build: `Sanduhr.Core/LimitLifetime.cs`; keys `limit_first_seen`, `limits_hidden_until_reset`, `alert_muted_tiers`; reconcile on each successful fetch before rendering; `ActiveTiers` gets manual ∪ until-reset hidden; alert snapshots drop until-reset hidden and muted tiers (manual hidden still alert); the card context menu built on open (Hide until it resets, Hide this tier, Stop/Warn again, Hidden limits submenu, Alerts Settings…). `LimitLifetimeTests.cs` with the Mac's cases (permanent never; iguana always; reset 8 days false, 8 days + 60 s true, missing false; unknown first seen 13 days true, 14 days false, none false; known model limit false; new window 3600 s exactly no, 3601 s yes, earlier reset no; refill 80 to 70.5 no, 80 to 70 yes, 80 to 0 yes, 80 to 100 no; missing data decides nothing; reconcile records first sightings once, returns not-temporary tiers, leaves absent tiers hidden).
  Acceptance: per the PRD's W2 stories, including the un-hide path for every hidden limit.
  Verify: tests green; menu and alert filtering reviewed in the diff.

- [ ] **5. Global shortcut with a recorder**
  Spec ref: `spec.md > Stage 1 > 1.2 Global shortcut with a recorder`
  What to build: `Sanduhr.Core/HotKeyCombo.cs` (`HotKeyCombo`, `HotKeyModifiers`, `HotKeyAction`, `HotKeys`, `HotKeyRefusal`); `hotkey_settings` in `SettingsStore`; `Interop/HotKeyNative.cs`, `Services/GlobalHotKeyService.cs` (message-only window, `MOD_NOREPEAT`, `WM_HOTKEY`, suspend/resume, failure 1409 reported); `ViewModels/ShortcutsViewModel.cs` and the Shortcuts section on Settings, General (keys button recorder, switch, Reset link, note line, caption, accessible names). `HotKeyComboTests.cs` (display order and key names, parse round-trip, the modifier refusal including Shift alone, taken-by-another-action, defaults, settings decoding of half-written or invalid entries reading as the default, storing the default stores nothing).
  Acceptance: off by default; switched on, Ctrl+Alt+S from any app opens Settings; a held combination shows "Another app already uses …"; recording never fires the shortcut.
  Verify: tests green; the Win32 path reviewed in the diff (registration can't be exercised from here; say so).

- [ ] **6. What's New: the rules and the card table (W1, Core)**
  Spec ref: `spec.md > Stage 1 > 1.1 What's New` (Core)
  What to build: `Sanduhr.Core/WhatsNew.cs` and `WhatsNewCards.cs` (six 3.5.0 cards). `WhatsNewTests.cs` with the Mac's cases retargeted (compare cases; the table-of-a1..d1 selection cases; cap at 8 from 12; range labels with the en dash; `ForMenu` highlights vs all; `AtLaunch` fresh, onboarding, tour, hidden, same version, downgrade) and `WhatsNewCardsTests.cs` (unique ids, at most two sentences and 200 characters, destinations valid, every version parses).
  Acceptance: the rules match the Mac's exactly.
  Verify: tests green.

- [ ] **7. What's New: the window, the router and the launch**
  Spec ref: `spec.md > Stage 1 > 1.1 What's New` (App)
  What to build: the three `whats_new_*` keys; `Views/WhatsNewWindow.xaml(.cs)` per the spec (themed, cards in two columns, previews, disclaimer footer, Don't show after updates, Done as default and cancel, accessible names); `SettingsWindow.SelectPage(SettingsPage)` and `ShowSettingsAsync(SettingsPage?)`; the launch decision after `_vm.Start()` (fresh, onboarding, record before show, open after two seconds); "What's New…" in the tray menu and on Settings, General.
  Acceptance: per the PRD's W1 stories.
  Verify: build and tests green; describe what was checked in XAML (bindings, names, tab order) and what needs Este's eyes.

- [ ] **8. Stage 1 docs, security pass and PR**
  Spec ref: `prd.md > What We're Building`, `spec.md > Data Model`
  What to build: `windows-dotnet/README.md` gains a short "Since 3.4.3" section naming S1's features and settings keys; `process-notes.md` updated; a secrets scan of the diff (no tokens, no paths with user names in tests); confirm no change to `Package.appxmanifest`, the MCP project, the vault location or Credential Manager code. Open the PR with the escalations listed, run CI, run an independent review agent, fix findings, merge.
  Acceptance: 0 warnings, tests green, CI green, review clean, merged with `gh pr merge --merge`.
  Verify: `gh pr checks`, the merge SHA on main.

### Stages 2 to 7 (later runs; each starts from the latest main with its own branch and PR)

- [ ] **9. Stage 2: welcome tour, signed-out first run, demo mode**
  Spec ref: `spec.md > Stage 2`; `docs/welcome-tour-spec.md`
  What to build: `WelcomeTour.cs` on the card type, the window's step mode, `tour_state`, the What's New hand-off, the signed-out widget, demo mode.
  Acceptance: PRD S2 stories. Verify: tests for step visibility, shown-once, choices written; describe the windows.

- [ ] **10. Stage 3: Settings v2**
  Spec ref: `spec.md > Stage 3`; `docs/settings-v2-spec.md`
  What to build: sidebar, names table, search, migrations, previews, About with the disclaimer, Reset (after Este rules on its wording).
  Acceptance: PRD S3 stories. Verify: names-table test, migrations idempotent, keyboard-only walk of every page.

- [ ] **11. Stage 4: the Claude Code side**
  Spec ref: `spec.md > Stage 4`; `docs/mods-and-config-spec.md`
  What to build: folder list, JSON splice and receipts, Mods & Config read-only then edits, meters mod, statusline combine, glow hooks, account follow, per-seat meters (after Este rules on the MCP root set).
  Acceptance: PRD S4 stories. Verify: byte-for-byte Remove tests on scratch folders only.

- [ ] **12. Stage 5: Desk foundation** (deepening pass first)
  Spec ref: `spec.md > Stage 5`
  What to build: the WorkerW spike with a written verdict, then the Desk window, layout, taskbar clearance, EsteFont.
  Acceptance: PRD S5 stories. Verify: spike notes; layout tests.

- [ ] **13. Stage 6: Desk content** (deepening pass first)
  Spec ref: `spec.md > Stage 6`
  What to build: messages and effects, editor, special days, now playing (widget then Desk), meetings, watchers, MCP tools (after Este rules).
  Acceptance: PRD S6 stories. Verify: parser tests ported from the Mac.

- [ ] **14. Stage 7: Desk polish** (deepening pass first)
  Spec ref: `spec.md > Stage 7`
  What to build: arrange by dragging, Desk clicks, sign-in reminders, Just the Desk, camera and mic spike.
  Acceptance: PRD S7 stories. Verify: arrange always endable.
