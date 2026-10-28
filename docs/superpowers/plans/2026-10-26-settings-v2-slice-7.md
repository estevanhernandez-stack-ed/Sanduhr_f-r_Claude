# Settings v2 for Windows, Slice 7 ("Streamer mode") Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One switch, "Streamer mode: hide accounts, folders and projects" (new key `streamer_mode`, off by default), turns every account label, Claude Code folder, project, skill, CI runner, publish host, city and add-on name that Sanduhr draws into a stable two-word codename, on every surface Sanduhr draws (Settings, the widget, the tray, the Desk and the statusline), while live numbers stay. A per-item allowlist shows a real name on purpose, and a source scan plus a sentinel sweep hold the reach complete.

**Architecture:** Core owns the rules and stays pure where it is linked. `Codenames` holds the pool of 196, the deny list, the salted HMAC first pick, the shared used set and the overflow numbering. `CodenameBook` is the salt, the maps and the allowlist, and it reads and writes `codenames.json`. `StreamerMask` is the one formatter every surface calls (`Name`, `Folder`, `AddOn`, `Paths`, `Endpoint`, `Watchers`, `WeatherName`, `PublishSample`). `StreamerFile` writes `~\.sanduhr\streamer.json`, which holds folder refs and codenames and never a real name. In App, `StreamerService` (owned by the widget) loads the key and the book, coalesces writes, writes `streamer.json` and raises `Changed`. A process-wide `StreamerMode.Mask` is the one read path for the 30-odd choke points. Settings gets the Privacy section, the allowlist, a banner per masking page and a reload of every masking page when the switch moves. The statusline script reads `streamer.json` itself. Last, the reach scan, the Done-line sweep, the harness and the Tier 2 hand-off.

**Tech Stack:** .NET 10, WPF, xUnit 2.9 (Xunit.SkippableFact for `[DesktopFact]`), CommunityToolkit.Mvvm 8.4, Windows PowerShell 5.1 (the statusline script), the render harness (`tools/Sanduhr.RenderHarness`).

**Spec:** `docs/settings-v2-windows/spec.md`, "Shippable slices, Slice 7: streamer mode", is the authority on scope. It works with these sections:

- "## Streamer mode" (about line 272): the switch and its copy, the tray and widget shortcuts, the indicators and banner, Codenames, Shuffle, the allowlist, the reach inside and outside Settings, and what it can't reach.
- "Search" (about line 332): the `stream`, `streamer`, `screen share`, `hide names`, `obs`, `demo` and `codename` synonyms.
- "Reset (WF20)": `codenames.json` with All settings, `streamer.json` by name.
- "Settings keys and migrations": `streamer_mode`, and the two new files.
- "Outside Settings, same release": the 4.0 What's New cards.
- "Not in 4.0": MCP answers and the OBS source.

The other inputs:

- Findings: `docs/settings-v2-windows/findings.md`, WF7 and "Feeds for streamer mode".
- Inventory: `docs/settings-v2-windows/inventory.md`.
- Carried in: `.superpowers/sdd/carry-into-slice-7/slice-6/carry.md`, plus the slice 6 final review's `propose_theme` finding (P6).
- Lessons: `.superpowers/sdd/2026-10-26-settings-v2-slice-6/progress.md`, `preflight.md` and `final-review.md`.
- The executed slice 6 plan, `docs/superpowers/plans/2026-10-26-settings-v2-slice-6.md`, is the model for conventions and helpers.

## Spec questions decided in this plan

1. **The reach is the code's, not the table's.** Two independent sweeps re-derived it from `windows-dotnet/src` at 3cc3699d ("The reach inventory", below). They found 103 rendering sites. 93 of them are masked by this plan, behind 33 choke points. The spec's tables name 66 of the 93. The 27 they don't name are masked anyway, because the Done line ("no account label, email, folder path, project, repo or runner name appears…") is the rule and the table is a guide. Privacy joins the reach table, as the carry asks, and so do the Desk (its companion picker), Accounts' dialogs, the tour's account chip, the re-sign-in window, the first-run archive question, the Claude Code page's folder report and status lines, and the exports' on-screen file names.
2. **The tray tooltip and toasts name no account at HEAD.** The tooltip prints the tier label and a percent (`App.xaml.cs:972-976`, `TrayReadout.cs`). Alert toasts carry the tier label (`AlertService.cs:84-101`). The launcher toast carries an app name, and the update toast carries a version. So the tooltip gets its "Streamer mode · " prefix and nothing else, and toasts need no mask. The spec's line about toasts is true by construction; Task 15 pins it.
3. **The Desk's Claude piece names the account, and the spec's Desk row misses it.** With two or more accounts, `WidgetViewModel.CurrentDeskUsage` (`:1813-1823`) hands the active label to the Claude line in every style (`DeskMeters.cs:143-180`, `DeskView.Styles.cs:71-83`), and to the "sign in again" reminder. It is masked at that one producer.
4. **The statusline's label is the folder's, not the account's.** `Get-FolderLabel` (`StatuslineScript.cs:114-124`) prints `SANDUHR_LABEL` or the `CLAUDE_CONFIG_DIR` leaf (`.claude-work` reads `work`). Under streamer mode the script replaces that label with the folder's codename from `streamer.json`. It **never adds** a label where none would print: plain `.claude` stays bare. The ref is `SnapshotContract.AccountRef` over the folder's canonical key: the full path from `Get-FolderKey`, lower-cased invariant. Both sides compute the same 8 hex digits, and a run test proves it. Fail closed: if `streamer.json` is there but can't be read, or the folder isn't in it, the line prints no label. If the file is missing, streamer mode was never on, and the line prints as today.
5. **The meters mod draws only the person's own `label` option, so it is not changed.** `register.tsx:384` reads the plugin's `userConfig` label, and `draw.ts:192` prints it. Sanduhr never writes that option. The mod never prints an account or folder name from Sanduhr's files: the snapshot carries none (`SnapshotWriter.cs:103-109`), and its watcher rows read `band.json`, which nothing on Windows writes. That text is Claude Code's own screen, which the caption already excludes. Touching the mod would mean a Mac-shared change and a band-fixture regenerate (`mac-ci.yml:61`) for no gain. The "streamer sweep" checks it live (Task 16).
6. **Categories.** The spec's nine (account, email, Claude Code folder, project, skill, repo, CI runner, publish endpoint host, city) plus **add-on**: plugin, marketplace and MCP server names, and packs made here. The spec's Mods & Widgets row masks those, but it names no category, and one shared category keeps "sanduhr-meters" one entry. Two kinds of text are not codenamed:
   - **Paths:** the profile folder becomes `%USERPROFILE%` (About; the spec's own rule), via `StreamerMask.Paths`.
   - **This PC's name**, in Publish's sample record, becomes "this PC".

   Email has no site. Account labels can't hold `@` or `.` (`AccountStore.cs:52-54`), and no email is read anywhere. The category stays, so a future site has a home.
7. **Keys, so a codename follows the thing, not its spelling.**
   - Account: the label.
   - Folder: `StreamerMask.FolderKey(path)`, the canonical full path lower-cased. "~\.claude-work" on a card, ".claude-work" in the session archive and the folder in `streamer.json` are one entry.
   - Project and skill: the display name. When two Sessions rows share a name, the row uses its disambiguated "name ~xxxxxxxx" as the key and shows only the codename.
   - Runner: the runner's label. City: the place's full label. Host: the URI host. Add-on: each side of `name@marketplace` separately.
8. **Codenames are assigned only while streamer mode is on.** Off, `StreamerMask.Off` returns every name as it is and writes nothing. On, a name is assigned the first time a surface asks, which is exactly "first-seen and kept". Turning streamer mode on also assigns every known Claude Code folder at once, so `streamer.json` maps them before Claude Code renders a line.
9. **The spec's second default allowlist entry, "the 'Sanduhr' folder label on the widget", has no surface at HEAD.** No widget, Desk or Settings text shows a folder labelled "Sanduhr": both sweeps grepped for it. The defaults that identify no one are Sanduhr's own add-on names: `sanduhr-meters`, `sanduhr` (the MCP server entry) and `inline` (the meters mod's marketplace in `sanduhr-meters@inline`). Owner taste 6 can change this.
10. **`codenames.json` writes are coalesced.** A first visit to Sessions can assign hundreds of project names in one pass. `StreamerMask.Assigned` marks the book dirty, and `StreamerService` flushes once per dispatcher turn (Background priority), on Shuffle, on an allowlist change, when the widget is disposed and at quit. After Reset (`ResetGuard.Done`), nothing is written: slice 6's P14 rule.
11. **Free text is not codenamed, and the caption says so.** These are words Sanduhr can't map to a real name:
    - The titles and notes Claude writes on its watcher cards (`watch_start`).
    - The pages in the claude.ai sign-in window, which may show an email.
    - Launcher link targets, Desk design names and session ids (the spec's Launcher row: "Nothing on the page").

    The can't-reach caption names the first two and Claude Code's own screen. The spec's sentence stays first and word for word.
12. **Two-way fields.**
    - Publish's Endpoint URL box can't show a codename and keep its binding. While streamer mode is on, it is replaced by a read-only line showing the masked endpoint, with one sentence: "Turn streamer mode off to edit it."
    - The City box is the person's own typing, so it is not masked. Places found and the saved place are.
    - "Show it as" is kept as it is (spec).
    - The Rename prompt opens empty while streamer mode is on, so a codename can never be saved as an account's name.
    - A linked-account combo shows codenames and keeps real labels as its values (`AccountChoice`, `SelectedValuePath`).
13. **Exports keep real names** (spec). What reaches the screen during an export is masked: the Save dialog's default file name (`Sanduhr-usage-dune-fox-…`) and the "Wrote N rows to" path. "Exports keep real names." shows beside both export buttons while streamer mode is on.
14. **The banner is one control, `StreamerBanner`, placed under the first line of every masking page.** It binds to `Privacy.Streamer.On` on the window's view model. The pages are Accounts, Privacy, Claude Usage, Publish usage, Desk, Weather, Watchers, Claude Code, Mods & Widgets and About: `SettingsIndex.StreamerPages`, pinned against the XAML. On Privacy the order is the carry's: banner, jump links, Streamer mode, then What leaves this PC.
15. **Moving the switch while Settings is open re-masks every open page.** `StreamerSettingsViewModel` hears `StreamerService.Changed` and calls `SettingsViewModel.ReloadNames()`. That is the slice's one new subscription to something that outlives the window, so `Closed` detaches it (`ViewModel.Privacy.Streamer.Detach()`). A test counts the handler after Close.
16. **Quick routes.** "Show the real name on stream" is a context menu item that shows only while streamer mode is on:
    - on a row of Overview's top projects and top skills, Trends' top projects and the Sessions list;
    - on a Claude Code folder card;
    - on a Mods & Widgets row.

    Each writes the same allowlist as the Privacy list, and **Privacy's list is the keyboard route for every name** (Principle 7 holds through it). Where the row itself takes focus (Sessions rows, Mods & Widgets rows) or holds a focusable control (a folder card's buttons), Shift+F10 or the Menu key opens the menu too. Overview's and Trends' rows take no focus today, so there the menu is a mouse shortcut to what the list already does.
17. **Search.** `SettingsIndex.Boosts` already sends `stream`, `streamer`, `screen share`, `hide names`, `obs` and `demo` to `privacy#streamer`, and `codename` and `codenames` to `privacy#streamer-allowlist`. They go live when the anchors are indexed. The two new entries take narrow synonyms only, and a test pins that "record", "hide", "account", "folder", "share" and "names" still land elsewhere first. That is slice 6's P2 lesson: "recording" would steal "record" from Shortcuts.
18. **What's New gets the spec's two 4.0.0 cards**: "Settings, rearranged" (landing on `privacy#streamer`, as the spec says) and "Streamer mode". Both ride `WhatsNewCardsTests`' id pin.
19. **Error text that can carry a path is passed through `StreamerMask.Paths`.** That is the widget's "Error: …" status line (`WidgetViewModel.cs:1522`) and the Claude Code page's and Mods & Widgets' path shorteners (`ClaudeCodeViewModel.cs:539`, `ModsPageViewModel.cs:404`).
20. **`codenames.json` lives in AppData; `streamer.json` in `~\.sanduhr`.** Only the app reads the first, and the statusline reads the second (spec, "Two locations, on purpose"). `streamer.json` is written only once streamer mode has been on, and then on every change, so a person who never turns it on gets no new file in the shared folder. Reset removes both by name, in the Settings part.
21. **The MCP server is untouched** (spec, "Not in 4.0"; P6 below). `Codenames.cs`, `StreamerMask.cs` and `StreamerFile.cs` are App-side Core files off the MCP link list. `StreamerMask` calls into `Watchers.cs` and `SnapshotContract.cs`, which are linked, but neither file changes, so the pinned-pure files stay as they are.

## Owner's taste (decided with a default; each is one line to change)

1. **The codename pool** is the spec's: 14 sand, time and sky words × 14 small birds and desert animals ("Dune Fox", "Quartz Heron").
2. **A folder's codename stands alone**: "Ember Lark", not "~\Ember Lark", so the spoken name is the spec's "Forget folder Ember Lark".
3. **The banner** reads "Streamer mode is on: names are codenames." and then the link "Privacy settings…" (the spec's words). It has a 1 px accent border on the page background.
4. **The widget's indicator** is the Segoe Fluent Icons "Hide" glyph (U+ED1A), drawn before the account chip's text in the chip's own color. It is spoken as "Streamer mode on" after the label.
5. **The tray tooltip prefix** is "Streamer mode · " (spec), and the tray and widget items are checkable "Streamer mode" items placed after "Tray shows" and "Compact mode".
6. **The default allowlist** is `sanduhr-meters`, `sanduhr` and `inline` (decision 9).
7. **The allowlist row while streamer mode is off** reads "Dune Fox · ~\.claude-work". While on, it reads "Dune Fox" alone. Rows are grouped under category headings ("Accounts", "Claude Code folders", "Projects", …).
8. **Shuffle's words** are the spec's: "Shuffle codenames…", then "Every codename changes. Viewers following a name will lose it." with "Shuffle" and "Cancel" and no default button.
9. **Quick routes show only while streamer mode is on** (decision 16). Off, nothing is masked, so the item has nothing to unmask.
10. **The statusline replaces a label and never adds one** (decision 4).
11. **Places found while on** shows each place as its codename, with one note under the list: "Streamer mode shows places as codenames. Turn it off to see their names."
12. **The 4.0.0 cards' words** (Task 14).
13. **Publish's sample shows "this PC"** for the machine name while streamer mode is on.

## The P6 decision: MCP answers (owner's call, apart from taste)

The spec puts MCP answers out of reach in 4.0 ("Not in 4.0": masking needs `StreamerMask` source-linked into `Sanduhr.Mcp`). The slice 6 final review found that three answers carry paths with the Windows user folder:

- `ping.snapshot_path` (`ToolLogic.cs:93`).
- `get_desk.wallpaper.path` (`DeskState.cs:88`).
- `propose_theme.saved_path` (`ThemeProposals.cs:57`, returned as-is by `ToolLogic.cs:867`), plus `previous_key`, the theme the person had.

`get_local_burn_by_project` with `full_paths` returns full project paths by design, and `ping.cc_roots_found` lists folder names. The copy already discloses all of it: `NetThroughClaudeCarries` and `AgentReadsCaption` through `McpAnswerPaths` (`SettingsNames.cs:735-745`, `:815-819`), PRIVACY.md:132, and the hosted page via hub #275.

**Options:**

| | What changes | Trust boundary | Cross-repo cost | Leaves the user folder in answers? |
| --- | --- | --- | --- | --- |
| **A. Leave and disclose** (today) | Nothing in the server. The streamer can't-reach caption names "what Sanduhr's MCP server answers Claude". | None. | None. | Yes, and the copy says so. |
| **B. Relativise** `snapshot_path` and `saved_path` to `%USERPROFILE%\…` / `%APPDATA%\…` | Two fields in `ToolLogic.cs` and `ThemeProposals.cs`, plus `Sanduhr.Mcp.Tests` and Core tests. | None: both files are off the link list (`ToolLogic.cs` is the server's own; `ThemeProposals.cs` is App-side Core), and `McpConfig.cs` already reads the profile folder. | `McpAnswerPaths`, PRIVACY.md:132 and the hosted policy (another hub issue). The Mac MCP (`mac/integrations/sanduhr_mcp.py:1540`) keeps absolute paths, so the two differ. | **Yes.** `wallpaper.path` must stay absolute, because Claude Code's Read tool can't open `%USERPROFILE%\…`, and the copy exists to be opened (PRIVACY.md:63). `full_paths` stays absolute by design. The copy still has to say "include your Windows user folder". |
| **C. Mask under streamer mode** | The server reads `streamer.json`. Folder names, roots and project names in five tools come back as codenames, and paths are relativised. | Either `StreamerMask` joins the link list (spec: "a design decision that must break CI on purpose"), or the server re-implements the ref and map reading in its own files. Project codenames would need a ref → codename map in `streamer.json` for every project the app ever showed, and unmapped projects would have no codename in the server. | Mac parity for the same tools, `AGENT_PROMPT.md`, the hosted policy, and the agent-facing tool descriptions. | No, for folder and project names while on. Paths still need B. |

**Recommendation: A for 4.0.** B changes two diagnostic fields that agents rarely open, but it can't remove the user folder from MCP answers (the wallpaper path and `full_paths` keep it). So it buys copy churn and a Mac-Windows difference, and the privacy statement doesn't change. C is the real fix and belongs with the spec's "OBS-friendly capture source" sub-project, where the server, the Mac MCP and the agent prompt can move together. Under A, this plan does two things: the can't-reach caption names MCP answers plainly (decision 11), and the Tier 2 sweep checks that a Claude Code transcript on screen is the person's call. If the owner picks B, it is a separate one-task plan after this slice: the two fields, their tests, `McpAnswerPaths` and PRIVACY.md in one commit, then the hub issue. No task below depends on the choice.

## Global Constraints

- **CRLF working tree, LF index.** Edit tracked files with the Edit tool only.
  - **Never `sed -i`, ever.**
  - Never rewrite a tracked file wholesale with Write. New files may be created with Write.
  - Before an Edit, confirm its anchor is unique: `git grep -c -F "<first line of the anchor>" -- <file>` prints 1. For a multi-line anchor, Read the file and check the whole block. This plan names the anchors that repeat:
    - `WeatherNickname = _weatherSettings.Nickname,` occurs twice in `DeskController.cs`. Task 8 changes both, the same way, with `replace_all: true`.
    - `Content="{Binding Name}"` occurs three times in `SettingsWindow.xaml`, and `<Grid Margin="4,3">` twice. Every edit to them below carries its neighbouring lines.
- **Keys.** One `settings.json` key is added: `streamer_mode` (bool, off when absent). It goes into `SettingsStore` and into `SettingsKeysContractTests.Frozen` between `statusline_enabled` and `theme` (Task 4). No key is renamed or removed. `McpSettingsExport` copies a fixed list of keys, so the new key never reaches `mcp-settings.json`.
- **Trust boundary.** No file is added to `Sanduhr.Mcp.csproj`'s `Compile Include` list, and no file on that list is edited.
  - The new Core files (`Codenames.cs`, `StreamerMask.cs`, `StreamerFile.cs`) are off the list.
  - So are the changed ones: `Paths.cs`, `ResetPlan.cs`, `StatuslineScript.cs`, `SettingsNames.cs`, `SettingsIndex.cs`, `WhatsNewCards.cs`.
  - `Watchers.cs` and `SnapshotContract.cs` are on the list. They are **called**, never edited.
  - No `src/Sanduhr.Mcp` file changes, and `TrustBoundaryTests` stays green untouched.
- **`~\.sanduhr` is shared ground.** Sanduhr adds exactly one name there, `streamer.json`, created only once streamer mode has been on, and written atomically (`.tmp`, then move). Reset deletes it by name. No code lists, clears or deletes the folder.
- **No real name in `streamer.json`.** It holds `schema_version`, `on`, `folders` (8-hex ref → codename) and `shown` (refs). `StreamerFileTests` pins that a real folder name never appears in the bytes.
- **Hands off the live app.** Never start, stop, kill or attach to `Sanduhr.exe`. If a build fails because a `Sanduhr.exe` holds a file, stop and report BLOCKED.
- **No test reads the real AppData**, LocalAppData, `~\.sanduhr`, a real Claude Code folder or Credential Manager. Core tests build `Paths` over a `TempDir`. App tests use `AppWindowFixture` and `ScratchWidget`, whose `Paths` are scratch. `StreamerMode` is reset to off in the fixture's constructor and `Dispose` (Task 4).
- **No test touches the network.** Nothing in this slice calls out, and `NetworkDisclosureTests` must stay at 40 lines and 25 outbound sites.
- **Test hygiene.**
  - An App test class carries `[Collection("SettingsWindow")]` when it derives from `AppWindowFixture`, builds a `SettingsWindow` or `SettingsViewModel`, uses `ScratchWidget`, or touches a process-wide static (`StreamerMode` is one).
  - Windows are shown through `NoActivate.Show(window)`, and `guard.Check()` follows anything that moves focus.
  - **Every widget a test builds is disposed in a `finally`** (`widget.Dispose()`).
  - The statusline run tests use `[PowerShellFact]` and the sandbox in `StatuslineScriptRunTests`.
- **Event lifetime.** `StreamerService.Changed` is the one new event that outlives a window.
  - The widget subscribes to its own service, and detaches in `Dispose`.
  - App subscribes once for the app's life.
  - `StreamerSettingsViewModel` subscribes per Settings window and detaches in `Detach()`, which `SettingsWindow.Closed` calls (Task 6, with a test that counts subscribers after Close).
  - `StreamerBanner` binds through the window's DataContext and subscribes to nothing.
- **RED first, and RED must be behavioural for a mask.** A masking test's RED is the sentinel visible on the surface (for example "Expected no 'Qxalpha' in the chip, found '⇆ Qxalpha'"), not a compile error. Where a new member is needed for the test to compile, add it first as a pass-through stub that returns the real name, run the test, and record the sentinel failure. Each masking task's Step 2 names the stub.
- **Implementers keep full test logs.** Redirect each run to a file (`dotnet test … > %TEMP%\slice7-<task>-<n>.log 2>&1`) and read failures from that file.
- **Run tests from `windows-dotnet/`.**
  - Core: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~<Class>`.
  - App: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~<Class>`.
- **Names.** Every new visible or spoken string is a `SettingsNames` member or built from one. It passes `SettingsNames.PathProblems`, `RetiredNames` and `UnknownLinks`. A control's spoken name starts with its visible text, and per-row context follows after a comma.
- **Index with the XAML.** An anchor joins `SettingsIndex.Anchors` in the commit that marks it in XAML. A `SettingsNames.Table` row lands with its `NameAnchors` entry.
- **Live regions.** No line in this slice changes text while shown, except the Watchers runner status, which already raises (`LiveRegion`). The banner appears with a page, so it is not a live region.
- **No control that can't act.** The allowlist checkboxes act at once, and Shuffle is enabled whenever a book exists or can be made. While streamer mode is on, the Endpoint box is replaced, not disabled.
- **Privacy copy must be exactly true.** The caption says what streamer mode doesn't cover, and the banner says what it does. Neither is taste.
- **Timings in copy come from constants.** This slice's copy quotes no interval. If an implementer adds one, it is built from the constant that acts on it.
- **Branch.** Tasks run in numeric order on `feat/4.0-slice-7`, cut from `release/4.0` at 3cc3699d.

## Review Focus

1. **A label that contains a codename's words, or two accounts that differ only in case.** "Dune" and "dune" are two labels and get two codenames, because keys are ordinal. A label that *is* a pool pair ("Dune Fox") gets a different codename, so on stream nobody can tell whether it was masked. Pinned in Task 1 (`A_real_name_that_looks_like_a_codename_still_gets_another`) and Task 2 (`Case_differs_codename_differs_for_labels_but_not_for_folders`).
2. **A folder written two ways** ("C:\Users\x\.claude-work\" and "c:\users\X\.CLAUDE-WORK"). One codename on every surface, and the statusline finds it. Pinned in Task 2 (`A_folder_has_one_codename_however_its_path_is_written`) and Task 5 (`A_config_dir_written_another_way_still_finds_its_codename`).
3. **Shuffle while Settings is open on a masked page.** Every page shows the new codenames without a reopen, the allowlist survives, and `streamer.json` changes. Pinned in Task 9 (`Shuffle_renames_every_open_page_and_keeps_the_allowlist`).
4. **A corrupt or hand-edited `codenames.json`** (bad salt, a codename that isn't a pool name, two names sharing a codename). Load drops what it can't trust, and never shows a real name in place of a codename. Pinned in Task 1 (`A_tampered_book_is_cleaned_or_replaced_never_trusted`).
5. **Streamer mode on, then Reset with "All settings".** Both files are gone, and nothing writes them back before quit. Pinned in Task 4 (`After_reset_the_service_writes_nothing`) and Task 2 (`Reset_lists_both_files_by_name_only`).

---

## Where the plan steps outside the spec's file list

The spec's list is the core: `Codenames.cs` and `StreamerMask.cs` (new), `StatuslineScript.cs`, `ResetPlan.cs`, `SettingsNames.cs`, `SettingsIndex.cs`, `WhatsNewCards.cs`, `SettingsStore.cs`, `App.xaml.cs`, `AccountMenuBuilder.cs`, `MainWindow.xaml(.cs)`, toast code, the Desk's watcher and weather pieces, the reach table's pages, and `SettingsKeysContractTests.cs`. These extra files deliver its bullets and the carry.

Core:

- New `StreamerFile.cs`: `streamer.json`'s shape and its folder ref (spec, "Two locations"). It is apart from `StreamerMask` so the statusline contract has one small file.
- `Paths.cs`: `CodenamesFile` and `StreamerStateFile`.
- Toast code needs nothing (decision 2).

App:

- New `Services/StreamerMode.cs` (the process-wide read path) and `Services/StreamerService.cs` (load, write, coalesce, `Changed`).
- New `ViewModels/StreamerSettingsViewModel.cs` (the switch, Shuffle, the allowlist) and `Views/StreamerBanner.cs`.
- `ViewModels/WidgetViewModel.cs`: owns the service; the chip, its indicator, the Desk usage and the error line.
- `ViewModels/PrivacyViewModel.cs`, `Views/PrivacyPage.xaml`: the section, the jump links and the banner.
- `ViewModels/SettingsViewModel.cs`, `Views/SettingsWindow.xaml(.cs)`: `ReloadNames`, the banners, the About paths and the Detach.
- Masking choke points beyond the spec's list, from the inventory:
  - `ViewModels/WelcomeTourViewModel.cs`, `Views/ManualKeyWindow.xaml.cs`, `Views/VaultConsentDialog.xaml.cs`
  - `ViewModels/ClaudeCodeHost.cs`, `ViewModels/FolderHoldsViewModel.cs`, `Views/McpConsentDialog.xaml.cs`
  - `ViewModels/DeskCompanionViewModel.cs`, `ViewModels/DeskContentPagesViewModels.cs`, `Desk/DeskController.cs`
- `Views/ClaudeCodePage.xaml`, `Views/ModsPage.xaml`, `Views/WatchersPage.xaml`: banners and the quick routes.

Harness: `tools/Sanduhr.RenderHarness/SettingsPassScenario.cs`, for the switch's focus shot.

Tests:

- New Core tests in `tests/Sanduhr.Tests/`: `CodenamesTests.cs`, `StreamerMaskTests.cs`, `StreamerFileTests.cs`, `SettingsSlice7NamesTests.cs`, `SettingsSlice7IndexTests.cs`, `StreamerReachTests.cs`.
- New App tests in `tests/Sanduhr.App.Tests/`: `StreamerServiceTests.cs`, `StreamerPrivacyTests.cs`, `StreamerWidgetTests.cs`, `StreamerDeskTests.cs`, `StreamerAccountsAndFoldersTests.cs`, `StreamerModsTests.cs`, `StreamerUsageTests.cs`, `StreamerPagesTests.cs`, `StreamerSweepTests.cs`.
- Changed:
  - Core: `SettingsKeysContractTests.cs`, `ResetPlanTests.cs`, `StatuslineScriptRunTests.cs`, `WhatsNewCardsTests.cs`, `SettingsSlice6IndexTests.cs`.
  - App: `AppWindowFixture.cs`, `DeskSplitPagesTests.cs`.

Not in this slice, though the spec puts these near it:

- The MCP answers (P6; spec "Not in 4.0").
- The OBS capture source ("Not in 4.0").
- A demo-mode starter (spec "Not in 4.0"). The demo line under the switch only explains `--demo`.
- Reset's per-folder list (slice 8).

## Merge surface with slice 8

Slice 8 (Reset, themes and contrast) follows this slice.

| File | Slice 7 leaves | Slice 8 does there | Risk |
| --- | --- | --- | --- |
| `Core/ResetPlan.cs` | Two lines in the Settings part, after the wallpaper preview line: `AddFile(settings, paths.CodenamesFile)` and `AddFile(settings, Path.Combine(ext, StreamerFile.FileName))` | per-folder Claude Code undo | Low: different block. |
| `tests/Sanduhr.Tests/ResetPlanTests.cs` | one new `[Fact]` at the end of the class | its per-folder tests | None. |
| `Views/SettingsWindow.xaml` | `<views:StreamerBanner />` under the first line of Accounts, Claude Usage, Publish, Desk, Weather and About; the Endpoint swap; the export captions; the Places note; About's `*Shown` bindings | the Themes block (uncapped lists), Obsidian's selected label | Low: no shared lines. About's Reset row is not touched here. |
| `Core/SettingsNames.cs` | a "slice 7" block after `StoreKeepsUpToDate`; two `Table` rows | its own block | None. |
| `Core/SettingsIndex.cs` | `Streamer`, `StreamerAllowlist`, two entries before `network`, `StreamerPages`, Privacy's `JumpLinks` arm | none expected | None. |
| `ViewModels/SettingsViewModel.cs` | `ReloadNames()`, `SettingsFolderShown`, `LocalFolderShown`, `ClaudeCodeFilesFolderShown` | none expected | None. |
| Theme tokens | the banner uses `Sanduhr.Brush.Bg`, `.Accent` and `.Text` | `ThemeContrastTests` raises TextDim and TextMuted | None: the banner uses neither. |

Slice 8's carry file (Task 16) records this table.

---

## The reach inventory (base 3cc3699d)

Two read-only sweeps re-derived every site from the code, one inside Settings and one outside. A **site** is a binding or line that puts an identifying value on screen or into a spoken name. A **choke point** (CP) is the one property or formatter that covers it. "Spec" says whether the spec's reach tables name the site. Line numbers are at 3cc3699d; tasks pin files, not lines.

**Inside Settings: 82 sites, 25 choke points.** 81 are masked. One site, "Show it as", stays visible (spec).

| CP | Choke point (file:line) | Sites (what shows) | Category | Spec | Task |
| --- | --- | --- | --- | --- | --- |
| 1 | `AccountItemViewModel` (`AccountsViewModel.cs:14-33`); `RefreshData` `:213-217`; `ClearHistory`, `ConfirmClear`, `ConfirmRemove` `:229-262` | the list row and its spoken text (`SettingsWindow.xaml:1099`); Clear and Remove names (`:1181`, `:1186`); Remove caption and HelpText (`:1187`, `:1190`); the clear, remove and "cleared" dialogs; the Rename prompt's prefill (`:143`) | account | list yes; dialogs, prompt **no** | 9 |
| 25 | `AccountsViewModel.Rename`'s error (`:157`; the message carries the label, `AccountStore.cs:53`, `:120`, `:122`) | the rename error dialog | account | **no** | 9 |
| 2 | `ManualKeyWindow.ForReauth` (`ManualKeyWindow.xaml.cs:40-47`) | headline "Update sessionKey for '…'", the disabled Label box | account | "Label field" | 7 |
| 3 | `HistoryTabViewModel.RefreshAccountOptions` `:89-97`, `RefreshLegend` `:110-126` | Meters "Account:" picker (`SettingsWindow.xaml:1414-1415`); legend (`:1432`). `AccountOption` has no `ToString`, so a masked `Display` could still be spoken from the record's text. | account | yes | 11 |
| 4 | `HistoryTabViewModel.ExportCsv` `:204-235` | the Save dialog's default file name; "Wrote N rows to:" path | account, path | **no** | 11 |
| 5 | `WelcomeTourViewModel.ChipText` `:166-169` | the tour's account chip (`WhatsNewWindow.xaml:234`) | account | **no** | 7 |
| 6 | `ClaudeCodeViewModel.SyncAccountOptions` `:409-423`, `Reload` `:437-441` | a card's linked-account combo, items and selection (`ClaudeCodePage.xaml:98-99`) | account | yes | 9 |
| 7 | `ClaudeCodeHost.Display` (`ClaudeCodeHost.cs:35`) and its two direct bypasses, `AccountsViewModel.cs:85` and `ClaudeCodeViewModel.cs:715` | card title (`ClaudeCodePage.xaml:86`), card name, combo name, Forget, MCP switch, meters line and link, holds expander, Refresh (`:73` to `:223`); glow status; consent folder lines (statusline, hooks, meters); the MCP consent scope note; Accounts' "Linked to …"; Mods & Widgets' meters rows and their switch, Update and Remove names (`ModsPage.xaml:119` to `:265`); **Privacy's folder-reads state** (`PrivacyPage.xaml:112`) | folder | Claude Code yes; Mods partly; Accounts yes; **Privacy no** | 9 |
| 8 | `ClaudeCodeViewModel.Shorten` `:539-540`, `ModsPageViewModel.Short` `:404-406` | status lines after a failure, whose messages carry file paths (`ClaudeCodeIntegrations.cs:544`, `:943`, `:1043`, `:1080`) | path, folder | **no** | 9, 10 |
| 9 | `FolderHoldsViewModel` `:90` and `ClaudeCodeViewModel.cs:659-660` | the folder report's statusline command; the statusline consent's "already has a statusline: …" | path | **no** | 9 |
| 10 | `FolderHoldsViewModel.Refresh` `:103-108` | plugin lines (`name@marketplace`), MCP server names (`ClaudeCodePage.xaml:186`, `:197`) | add-on | **no** | 9 |
| 11 | folder basenames: `ClaudeCodeViewModel.cs:717`, `LocalCcViewModel.cs:124`, `:144`, `PublishViewModel.cs:136`, `CcLedgerViewModel.cs:132`, `:165` | the MCP consent's radio buttons and checkboxes; session archive checkboxes (`SettingsWindow.xaml:1630`); "Stop archiving …?"; Folders to share (`:1959`); a session's "Folder:" | folder | archive, share yes; dialogs **no** | 9, 11, 12 |
| 12 | `McpConsentDialog.UpdateDetails` (`McpConsentDialog.xaml.cs:72-73`) | the launcher's and `.claude.json`'s full paths | path | **no** | 9 |
| 13 | `InstalledRowViewModel.Name`, `Detail` (`ModsPageViewModel.cs:112`, `:123`) | an Installed row's name, source, spoken name, preview name, pack switch, Uninstall, own art, Where link; the uninstall question; status lines; the own-art dialog title (`ModsPage.xaml:67` to `:308`) | add-on | yes | 10 |
| 14 | `InstalledRowViewModel.Description` (`:113`) | a masked row's description (the author's text, which names the plugin) | add-on | **no** | 10 |
| 15 | `LocalCcViewModel.Apply` `:434-440` | Overview's top projects and top skills (`SettingsWindow.xaml:1561`, `:1595`) | project, skill | yes | 11 |
| 16 | `CcTrendsViewModel` `:86-87` | Trends' top projects (`:1692`) | project | yes | 11 |
| 17 | `LedgerRowViewModel` (`CcLedgerViewModel.cs:33-40`, `:67-69`, `:126-132`, `:162-165`) | Sessions' project column, Stack by project, the day view, the full-path tooltip, the spoken row, the detail's skills and folders (`:1818` to `:1832`) | project, skill, folder, path | yes | 11 |
| 18 | `PublishViewModel.EndpointUrl` `:153`, `:206` | the Endpoint URL box (`SettingsWindow.xaml:1890`) | host | yes | 12 |
| 19 | `PublishViewModel.RefreshStatus` `:458` | the last result, which can name a redirect host (`UsagePublisher.cs:259`) | host | **no** | 12 |
| 20 | `PublishViewModel.ShowSample` `:400-418` | the sample's host line; the record's homes, project names and `machine` | host, folder, project, PC | sample yes; **PC name no** | 12 |
| 21 | `WeatherPageViewModel` `:300`, `:309`, `:325` | the current place (`SettingsWindow.xaml:2612`), Places found (`:2626`) | city | yes | 12 |
| 22 | `App.xaml.cs:781-782` (`RunnerStatus.Text`) | Watchers' CI runners status (`WatchersPage.xaml:51`) | runner | yes | 12 |
| 23 | `SettingsViewModel` `:263-265` | About's three folders, text and tooltip (`SettingsWindow.xaml:2772` to `:2813`) | path | yes | 12 |
| 24 | `DeskCompanionViewModel.Reload` `:105` | the Desk's companion picker (made-here sprite names) | add-on | **no** (Desk: "nothing") | 10 |

**Outside Settings: 23 rows, 21 once the tour chip and the re-sign-in window (inside rows 5 and 2) are not counted twice.**

| CP | Choke point (file:line) | Sites | Category | Spec | Task |
| --- | --- | --- | --- | --- | --- |
| M1 | `WidgetViewModel.RefreshAccountLabel` `:1924-1941` | the widget's account chip (`MainWindow.xaml:360`) | account | yes | 7 |
| M2 | `AccountMenuBuilder.PopulateSwitchList` `:46` | the widget's and the tray's Accounts ▸ items (`MainWindow.xaml.cs:100`, `App.xaml.cs:454`) | account | yes | 7 |
| M3 | `WidgetViewModel.CurrentDeskUsage` `:1813-1823` | the Desk's Claude line in the bar, two-row and gauge styles, and its sign-in reminder line (`DeskView.cs:708`, `DeskView.Styles.cs:71-83`) | account | **no** | 8 |
| M4 | `DeskController.WatchersNow` `:172` | runner watcher card titles, "runner · job" (`DeskView.cs:1094`, from `Watchers.cs:395`) | runner | yes | 8 |
| M5 | the two frame builds, `DeskController.cs:524` and `:775` | the weather piece's heading (`DeskView.cs:827-837`) | city | yes | 8 |
| M6 | `ManualKeyWindow`'s inline error (`:79-115`, from `AccountStore.cs:53`, `:86`) | "Account '…' already exists" | account | **no** | 7 |
| M7 | `VaultConsentDialog`'s constructor `:26` (first run, `App.xaml.cs:138`) | the archive question's folder checkboxes | folder | **no** | 7 |
| M8 | `WidgetViewModel` `:1522` | the "Error: …" status line, whose text can carry a path | path | **no** | 7 |
| M9 | `StatuslineScript`, `Get-OurLine` `:206-208` | the statusline's folder label in every Claude Code folder | folder | yes ("account segment") | 5 |
| none | the tray tooltip (`App.xaml.cs:972-976`) | a tier label and a percent | none | listed; nothing to mask | 7 (prefix) |
| none | alert toasts (`AlertService.cs:84-101`), the launcher toast (`DeskController.cs:252`), the update toast | a tier label, an app name, a version | none | listed; nothing to mask | 15 pins it |
| none | the meters mod's label (`register.tsx:384`, `draw.ts:192`) | the person's own `label` option | typed by the person | "checks whether it draws one" | decision 5 |
| none | the meters mod's watcher rows (`register.tsx:491-505`) | `band.json`, never written on Windows | dormant | **no** | decision 5 |
| can't | agent watcher titles and notes (`Watchers.cs:293`, `:560-563`) | Claude's words | free text | **no** | the caption (decision 11) |
| can't | the sign-in window (`SignInWindow.xaml.cs`) | claude.ai's and Google's pages | email | **no** | the caption |
| can't | "Name this account" (`SignInCoordinator.cs:254`) | the person typing a new name | input | **no** | none |

**Totals.**

- **103 sites**: 82 inside Settings and 21 outside.
- **93 are masked**, behind **33 choke points**: 25 inside and 8 outside (M1 to M5, M7, M8, M9; M6 shares CP 2's window).
- **66 of the 93 are in the spec's tables. 27 are not.**
  - 20 inside Settings: CP 1's dialogs and prompt, CPs 4, 5, 8, 9, 10, 12, 14, 19, 24 and 25, CP 11's two dialogs, CP 20's PC name, and Privacy in CP 7.
  - 7 outside: M3's three styles and its reminder line, M6, M7 and M8.
- Two spec rows have nothing to mask: the tray tooltip and toasts.
- Four sites can't be masked: agent text, the sign-in pages, the name prompt and the dormant band rows. The caption names the first two.

**The source scan's base (Task 13).** The producer shapes in Task 13 match 108 lines in 21 files under `src/Sanduhr.App` at 3cc3699d:

| File | Lines |
| --- | --- |
| `App.xaml.cs` | 8 |
| `Desk/DeskHandoffSeams.cs` | 1 |
| `Desk/RunnerWatchService.cs` | 1 |
| `Services/AccountMenuBuilder.cs` | 2 |
| `Services/ResetService.cs` | 1 |
| `Services/SignInCoordinator.cs` | 7 |
| `Services/UsagePublishService.cs` | 3 |
| `Services/VaultService.cs` | 4 |
| `ViewModels/AccountsViewModel.cs` | 3 |
| `ViewModels/CcLedgerViewModel.cs` | 21 |
| `ViewModels/CcTrendsViewModel.cs` | 1 |
| `ViewModels/ClaudeCodeHost.cs` | 2 |
| `ViewModels/ClaudeCodeViewModel.cs` | 4 |
| `ViewModels/DeskContentPagesViewModels.cs` | 1 |
| `ViewModels/DeskSettingsViewModel.cs` | 2 |
| `ViewModels/FolderHoldsViewModel.cs` | 5 |
| `ViewModels/HistoryTabViewModel.cs` | 4 |
| `ViewModels/LocalCcViewModel.cs` | 7 |
| `ViewModels/PublishViewModel.cs` | 6 |
| `ViewModels/WelcomeTourViewModel.cs` | 1 |
| `ViewModels/WidgetViewModel.cs` | 24 |

Tasks 7 to 12 add lines that match (each new mask call sits beside its producer), so Task 13 pins the counts the scan prints at that point. The controller reads every line marked `key` and confirms it puts nothing on screen.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Sanduhr.Core/Codenames.cs` (create) | `CodenameCategory`, `CodenameEntry`, `CodenameBook` (salt, maps, used set, allowlist, Shuffle, JSON), `Codenames` (pool, deny list, pick, load, save). |
| `src/Sanduhr.Core/StreamerMask.cs` (create) | `StreamerMask`, the one formatter. |
| `src/Sanduhr.Core/StreamerFile.cs` (create) | `StreamerFolder`, `StreamerFile.FolderRef`, `Build`, `Write`. |
| `src/Sanduhr.Core/Paths.cs` (modify) | `CodenamesFile`, `StreamerStateFile`. |
| `src/Sanduhr.Core/ResetPlan.cs` (modify) | Both files in the Settings part, by name. |
| `src/Sanduhr.Core/StatuslineScript.cs` (modify) | `Get-StreamerLabel`; `Version = 6`. |
| `src/Sanduhr.Core/SettingsNames.cs` (modify) | The slice 7 block; two `Table` rows. |
| `src/Sanduhr.Core/SettingsIndex.cs` (modify) | Two anchors, two entries, `StreamerPages`, Privacy's jump links, `NameAnchors`. |
| `src/Sanduhr.Core/WhatsNewCards.cs` (modify) | Two 4.0.0 cards. |
| `src/Sanduhr.App/Services/SettingsStore.cs` (modify) | `LoadStreamerMode`, `SaveStreamerMode`. |
| `src/Sanduhr.App/Services/StreamerMode.cs` (create) | The process-wide mask. |
| `src/Sanduhr.App/Services/StreamerService.cs` (create) | Load, switch, Shuffle, allowlist, coalesced writes, `streamer.json`, `Changed`. |
| `src/Sanduhr.App/ViewModels/StreamerSettingsViewModel.cs` (create) | `CodenameRow`, `StreamerSettingsViewModel`. |
| `src/Sanduhr.App/Views/StreamerBanner.cs` (create) | The banner. |
| `src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (modify) | Owns the service; the chip, its indicator, the Desk usage, the error line. |
| `src/Sanduhr.App/App.xaml.cs`, `MainWindow.xaml(.cs)` (modify) | Tray and widget items, the tooltip, the Desk reload, the runner status. |
| The inventory's choke points (modify) | One mask call each. |
| `src/Sanduhr.App/Views/PrivacyPage.xaml`, `SettingsWindow.xaml(.cs)`, `ClaudeCodePage.xaml`, `ModsPage.xaml`, `WatchersPage.xaml` (modify) | The section, banners, quick routes and swaps. |

---

### Task 1: `Codenames` and `CodenameBook`: the pool, the deny list, first-seen-and-kept, the shared used set, overflow, Shuffle, the allowlist and `codenames.json`

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/Codenames.cs`
- Test: create `windows-dotnet/tests/Sanduhr.Tests/CodenamesTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `public enum CodenameCategory { Account, Email, Folder, Project, Skill, Repo, Runner, Host, City, AddOn }`
  - `public sealed record CodenameEntry(CodenameCategory Category, string Real, string? Codename, bool Shown)`
  - `public sealed class CodenameBook`, with:
    - `CodenameBook(byte[] salt, bool withDefaults = true)`, `static CodenameBook New()`
    - `byte[] Salt`, `int Count`
    - `bool TryGet(CodenameCategory, string real, out string codename)`, `string Assign(CodenameCategory, string real)`
    - `bool IsShown(CodenameCategory, string real)`, `void SetShown(CodenameCategory, string real, bool shown)`
    - `void Shuffle(byte[] salt)`, `IReadOnlyList<CodenameEntry> Entries()`
    - `JsonObject ToJson()`, `static CodenameBook? FromJson(JsonNode?)`
  - `public static class Codenames`, with:
    - `FirstWords`, `SecondWords`, `Pool` (196), `DenyPairs`, `DefaultShown`
    - `SaltBytes` (32), `MinSaltBytes` (16), `SchemaVersion` (1), `FileName` ("codenames.json"), `MaxFileBytes` (4 MiB)
    - `string Id(CodenameCategory)`, `bool TryParseId(string, out CodenameCategory)`
    - `int StartIndex(byte[] salt, CodenameCategory, string real)`, `bool IsCodename(string)`
    - `CodenameBook? Load(string path)`, `bool Save(CodenameBook, string path)`

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/CodenamesTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 7: the codename pool and how names are assigned, kept, shuffled and allowlisted (spec, Streamer mode, Codenames).</summary>
public class CodenamesTests
{
    private static byte[] Salt(byte fill) => Enumerable.Repeat(fill, Codenames.SaltBytes).ToArray();

    [Fact]
    public void The_pool_is_fourteen_by_fourteen_and_none_is_a_known_product()
    {
        Assert.Equal(14, Codenames.FirstWords.Count);
        Assert.Equal(14, Codenames.SecondWords.Count);
        Assert.Equal(196, Codenames.Pool.Count);
        Assert.Equal(196, Codenames.Pool.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("Dune Fox", Codenames.Pool);
        Assert.Contains("Quartz Heron", Codenames.Pool);
        Assert.Contains("Saffron Moth", Codenames.Pool);
        Assert.NotEmpty(Codenames.DenyPairs);
        static string Squash(string s) => new string(s.Where(char.IsLetter).ToArray()).ToLowerInvariant();
        var deny = Codenames.DenyPairs.Select(Squash).ToHashSet();
        Assert.Empty(Codenames.Pool.Where(p => deny.Contains(Squash(p))));
        // "Desert Eagle" can't occur: neither word is in the pool (spec).
        Assert.DoesNotContain("Desert", Codenames.FirstWords);
        Assert.DoesNotContain("Eagle", Codenames.SecondWords);
    }

    [Fact]
    public void A_name_keeps_its_codename_and_a_second_book_with_the_same_salt_agrees()
    {
        var a = new CodenameBook(Salt(7));
        var first = a.Assign(CodenameCategory.Account, "Work");
        Assert.Equal(first, a.Assign(CodenameCategory.Account, "Work"));
        Assert.True(Codenames.IsCodename(first));
        Assert.Equal(first, new CodenameBook(Salt(7)).Assign(CodenameCategory.Account, "Work"));
    }

    [Fact]
    public void Another_salt_gives_another_codename_for_most_names()
    {
        var names = Enumerable.Range(0, 20).Select(i => "project-" + i).ToList();
        var a = new CodenameBook(Salt(1));
        var b = new CodenameBook(Salt(2));
        int same = names.Count(n => a.Assign(CodenameCategory.Project, n) == b.Assign(CodenameCategory.Project, n));
        Assert.True(same < 5, $"{same} of 20 names kept their codename across salts");
    }

    [Fact]
    public void No_codename_is_shared_across_categories()
    {
        var book = new CodenameBook(Salt(3));
        var all = new List<string>();
        foreach (var c in Enum.GetValues<CodenameCategory>())
            for (int i = 0; i < 10; i++)
                all.Add(book.Assign(c, "same-name-" + i));
        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_real_name_that_looks_like_a_codename_still_gets_another()
    {
        var book = new CodenameBook(Salt(4));
        Assert.NotEqual("Dune Fox", book.Assign(CodenameCategory.Account, "Dune Fox"));
    }

    [Fact]
    public void Past_196_names_gain_a_numeral()
    {
        var book = new CodenameBook(Salt(5));
        var names = Enumerable.Range(0, 200).Select(i => book.Assign(CodenameCategory.Project, "p" + i)).ToList();
        Assert.Equal(200, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(196, names.Take(196).Count(n => Codenames.Pool.Contains(n)));
        Assert.All(names.Skip(196), n => Assert.Matches(@"^[A-Z][a-z]+ [A-Z][a-z]+ 2$", n));
        Assert.All(names, n => Assert.True(Codenames.IsCodename(n), n));
    }

    [Fact]
    public void Shuffle_changes_every_codename_and_keeps_the_allowlist()
    {
        var book = new CodenameBook(Salt(6));
        var before = Enumerable.Range(0, 12).Select(i => book.Assign(CodenameCategory.Folder, "f" + i)).ToList();
        book.SetShown(CodenameCategory.Account, "Personal", true);
        book.Shuffle(Salt(9));
        Assert.Equal(0, book.Count);
        Assert.False(book.TryGet(CodenameCategory.Folder, "f0", out _));
        var after = Enumerable.Range(0, 12).Select(i => book.Assign(CodenameCategory.Folder, "f" + i)).ToList();
        Assert.True(before.Zip(after).Count(p => p.First == p.Second) < 4);
        Assert.True(book.IsShown(CodenameCategory.Account, "Personal"));
        Assert.Equal(Salt(9), book.Salt);
    }

    [Fact]
    public void Sanduhrs_own_add_ons_are_shown_by_default_and_nothing_else_is()
    {
        var book = new CodenameBook(Salt(8));
        Assert.True(book.IsShown(CodenameCategory.AddOn, "sanduhr-meters"));
        Assert.True(book.IsShown(CodenameCategory.AddOn, "sanduhr"));
        Assert.True(book.IsShown(CodenameCategory.AddOn, "inline"));
        Assert.False(book.IsShown(CodenameCategory.AddOn, "someone-elses-plugin"));
        Assert.False(book.IsShown(CodenameCategory.Account, "Personal"));
        Assert.False(new CodenameBook(Salt(8), withDefaults: false).IsShown(CodenameCategory.AddOn, "sanduhr-meters"));
    }

    [Fact]
    public void Entries_list_mapped_and_shown_names_by_category()
    {
        var book = new CodenameBook(Salt(10), withDefaults: false);
        var code = book.Assign(CodenameCategory.Project, "alpha");
        book.SetShown(CodenameCategory.Account, "Personal", true);
        Assert.Equal(
            [new CodenameEntry(CodenameCategory.Account, "Personal", null, true), new CodenameEntry(CodenameCategory.Project, "alpha", code, false)],
            book.Entries());
    }

    [Fact]
    public void A_book_round_trips_through_its_file()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "Sanduhr", Codenames.FileName);
        var book = new CodenameBook(Salt(11));
        var work = book.Assign(CodenameCategory.Account, "Work");
        var folder = book.Assign(CodenameCategory.Folder, @"c:\users\x\.claude-work");
        book.SetShown(CodenameCategory.AddOn, "sanduhr", false);
        Assert.True(Codenames.Save(book, path));
        Assert.False(File.Exists(path + ".tmp"));

        var back = Codenames.Load(path)!;
        Assert.Equal(book.Salt, back.Salt);
        Assert.True(back.TryGet(CodenameCategory.Account, "Work", out var w) && w == work);
        Assert.True(back.TryGet(CodenameCategory.Folder, @"c:\users\x\.claude-work", out var f) && f == folder);
        Assert.False(back.IsShown(CodenameCategory.AddOn, "sanduhr"));     // the file's allowlist wins over the defaults
        Assert.True(back.IsShown(CodenameCategory.AddOn, "sanduhr-meters"));
        // A new name never collides with a loaded one.
        Assert.DoesNotContain(back.Assign(CodenameCategory.Project, "new"), new[] { work, folder });
    }

    [Fact]
    public void A_missing_file_loads_as_nothing()
    {
        using var tmp = new TempDir();
        Assert.Null(Codenames.Load(Path.Combine(tmp.Path, Codenames.FileName)));
    }

    /// <summary>Review Focus 4: a hand-edited or broken file is cleaned or replaced, never trusted.</summary>
    [Fact]
    public void A_tampered_book_is_cleaned_or_replaced_never_trusted()
    {
        Assert.Null(CodenameBook.FromJson(JsonNode.Parse("""{"schema_version":1,"salt":"c2hvcnQ=","maps":{}}""")));    // salt too short
        Assert.Null(CodenameBook.FromJson(JsonNode.Parse("""{"schema_version":2,"salt":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=","maps":{}}""")));
        Assert.Null(CodenameBook.FromJson(JsonNode.Parse("[]")));
        var salt = Convert.ToBase64String(Salt(12));
        var book = CodenameBook.FromJson(JsonNode.Parse($$"""
            {"schema_version":1,"salt":"{{salt}}",
             "maps":{"account":{"Work":"Work","Home":"Dune Fox","Side":"Dune Fox","Odd":"Mesa Owl 3"},"nonsense":{"x":"Amber Wren"}},
             "shown":{"project":["kept"],"nonsense":["y"]}}
            """))!;
        Assert.False(book.TryGet(CodenameCategory.Account, "Work", out _));     // a real name as its own codename is dropped
        Assert.True(book.TryGet(CodenameCategory.Account, "Home", out var home) && home == "Dune Fox");
        Assert.False(book.TryGet(CodenameCategory.Account, "Side", out _));     // a second holder of one codename is dropped
        Assert.True(book.TryGet(CodenameCategory.Account, "Odd", out var odd) && odd == "Mesa Owl 3");
        Assert.True(book.IsShown(CodenameCategory.Project, "kept"));
        Assert.Equal(2, book.Count);
    }

    [Fact]
    public void A_file_too_large_or_not_json_loads_as_nothing()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, Codenames.FileName);
        File.WriteAllText(path, "{ not json");
        Assert.Null(Codenames.Load(path));
        File.WriteAllText(path, new string(' ', Codenames.MaxFileBytes + 1));
        Assert.Null(Codenames.Load(path));
    }

    [Theory]
    [InlineData("Dune Fox", true)]
    [InlineData("Zenith Plover 12", true)]
    [InlineData("Dune Fox 1", false)]
    [InlineData("dune fox", false)]
    [InlineData("Desert Eagle", false)]
    [InlineData("", false)]
    public void Only_pool_names_and_their_numbered_forms_are_codenames(string text, bool expected) =>
        Assert.Equal(expected, Codenames.IsCodename(text));

    [Fact]
    public void Category_ids_round_trip()
    {
        foreach (var c in Enum.GetValues<CodenameCategory>())
            Assert.True(Codenames.TryParseId(Codenames.Id(c), out var back) && back == c);
        Assert.Equal("addon", Codenames.Id(CodenameCategory.AddOn));
        Assert.False(Codenames.TryParseId("nonsense", out _));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~CodenamesTests > %TEMP%\slice7-t1-red.log 2>&1`
Expected: the build FAILS with `The type or namespace name 'CodenameBook' could not be found`. This task has no mask to show a behavioural RED: it creates the rules.

- [ ] **Step 3: Write `Codenames.cs`**

Create `windows-dotnet/src/Sanduhr.Core/Codenames.cs`:

```csharp
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>
/// What a codename stands in for (spec, Streamer mode, Codenames: "Categories, one pool"). The spec's nine, plus <see cref="AddOn"/>: plugin,
/// marketplace and MCP server names and packs made here, which the spec's Mods &amp; Widgets row masks without naming a category (slice 7,
/// decision 6). Not on the MCP link list.
/// </summary>
public enum CodenameCategory { Account, Email, Folder, Project, Skill, Repo, Runner, Host, City, AddOn }

/// <summary>One name in the book, for the allowlist list: its codename (null for a shown name never masked) and whether it is shown.</summary>
public sealed record CodenameEntry(CodenameCategory Category, string Real, string? Codename, bool Shown);

/// <summary>
/// The salt, the map per category (real name → codename) and the allowlist: <c>%APPDATA%\Sanduhr\codenames.json</c> (spec). First-seen and
/// kept: <see cref="Assign"/> picks once and the map keeps it. One used set across every category, so an account and a repo never share a
/// codename. Not thread-safe: <see cref="StreamerMask"/> locks around it.
/// </summary>
public sealed class CodenameBook
{
    private readonly Dictionary<CodenameCategory, Dictionary<string, string>> _maps = [];
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);
    private readonly HashSet<(CodenameCategory, string)> _shown = [];

    /// <param name="withDefaults">Starts the allowlist with <see cref="Codenames.DefaultShown"/>. A loaded book takes the file's list instead.</param>
    public CodenameBook(byte[] salt, bool withDefaults = true)
    {
        if (salt is null || salt.Length < Codenames.MinSaltBytes)
            throw new ArgumentException("The salt is too short.", nameof(salt));
        Salt = (byte[])salt.Clone();
        if (withDefaults)
            foreach (var shown in Codenames.DefaultShown)
                _shown.Add(shown);
    }

    /// <summary>A book with a fresh random salt: two PCs give different codenames, so none can be reversed from a public list (spec).</summary>
    public static CodenameBook New() => new(RandomNumberGenerator.GetBytes(Codenames.SaltBytes));

    public byte[] Salt { get; private set; }

    /// <summary>How many codenames are in use, across every category.</summary>
    public int Count => _used.Count;

    public bool TryGet(CodenameCategory category, string real, out string codename)
    {
        codename = "";
        return _maps.TryGetValue(category, out var map) && map.TryGetValue(real, out codename!);
    }

    /// <summary>The name's codename: the one it has, else a new one, picked by <see cref="Codenames.StartIndex"/> and stepped past every
    /// codename in use; past the pool's 196, the same pairs with " 2", then " 3" (spec, overflow).</summary>
    public string Assign(CodenameCategory category, string real)
    {
        if (TryGet(category, real, out var known))
            return known;
        int start = Codenames.StartIndex(Salt, category, real);
        for (int round = 1; ; round++)
        {
            for (int k = 0; k < Codenames.Pool.Count; k++)
            {
                var name = Codenames.Pool[(start + k) % Codenames.Pool.Count]
                    + (round == 1 ? "" : " " + round.ToString(CultureInfo.InvariantCulture));
                if (_used.Contains(name))
                    continue;
                Map(category)[real] = name;
                _used.Add(name);
                return name;
            }
        }
    }

    public bool IsShown(CodenameCategory category, string real) => _shown.Contains((category, real));

    public void SetShown(CodenameCategory category, string real, bool shown)
    {
        if (shown) _shown.Add((category, real));
        else _shown.Remove((category, real));
    }

    /// <summary>Shuffle codenames… (spec): a new salt and an empty map. The allowlist is kept.</summary>
    public void Shuffle(byte[] salt)
    {
        if (salt is null || salt.Length < Codenames.MinSaltBytes)
            throw new ArgumentException("The salt is too short.", nameof(salt));
        Salt = (byte[])salt.Clone();
        _maps.Clear();
        _used.Clear();
    }

    /// <summary>Every mapped or shown name, by category in enum order, then by real name (ordinal, ignoring case).</summary>
    public IReadOnlyList<CodenameEntry> Entries()
    {
        var keys = new HashSet<(CodenameCategory, string)>(_shown);
        foreach (var (category, map) in _maps)
            foreach (var real in map.Keys)
                keys.Add((category, real));
        return [.. keys
            .OrderBy(k => k.Item1).ThenBy(k => k.Item2, StringComparer.OrdinalIgnoreCase).ThenBy(k => k.Item2, StringComparer.Ordinal)
            .Select(k => new CodenameEntry(k.Item1, k.Item2, TryGet(k.Item1, k.Item2, out var c) ? c : null, _shown.Contains(k)))];
    }

    public JsonObject ToJson()
    {
        var maps = new JsonObject();
        foreach (var (category, map) in _maps.OrderBy(m => m.Key))
        {
            var o = new JsonObject();
            foreach (var (real, code) in map.OrderBy(p => p.Key, StringComparer.Ordinal))
                o[real] = code;
            maps[Codenames.Id(category)] = o;
        }
        var shown = new JsonObject();
        foreach (var group in _shown.GroupBy(s => s.Item1).OrderBy(g => g.Key))
            shown[Codenames.Id(group.Key)] = new JsonArray([.. group.Select(s => (JsonNode?)s.Item2).OrderBy(n => (string)n!, StringComparer.Ordinal)]);
        return new JsonObject
        {
            ["schema_version"] = Codenames.SchemaVersion,
            ["salt"] = Convert.ToBase64String(Salt),
            ["maps"] = maps,
            ["shown"] = shown,
        };
    }

    /// <summary>
    /// A book from <c>codenames.json</c>, or null when the file can't be trusted at all (another schema, no salt, a salt under 16 bytes).
    /// Entries it can't trust are dropped one by one: an unknown category, a codename that is not a pool name (or its numbered form), a
    /// codename equal to its real name, and every holder of a codename after the first.
    /// </summary>
    public static CodenameBook? FromJson(JsonNode? node)
    {
        if (node is not JsonObject root)
            return null;
        if (root["schema_version"] is not JsonValue v || !v.TryGetValue(out int version) || version != Codenames.SchemaVersion)
            return null;
        byte[] salt;
        try { salt = Convert.FromBase64String((string?)root["salt"] ?? ""); }
        catch (Exception e) when (e is FormatException or InvalidOperationException) { return null; }
        if (salt.Length < Codenames.MinSaltBytes)
            return null;

        var book = new CodenameBook(salt, withDefaults: root["shown"] is not JsonObject);
        if (root["maps"] is JsonObject maps)
        {
            foreach (var (id, value) in maps)
            {
                if (!Codenames.TryParseId(id, out var category) || value is not JsonObject map)
                    continue;
                foreach (var (real, codeNode) in map)
                {
                    if (codeNode is not JsonValue cv || !cv.TryGetValue(out string? code) || code is null)
                        continue;
                    if (real.Length == 0 || !Codenames.IsCodename(code) || code == real || book._used.Contains(code))
                        continue;
                    book.Map(category)[real] = code;
                    book._used.Add(code);
                }
            }
        }
        if (root["shown"] is JsonObject shown)
        {
            foreach (var (id, value) in shown)
            {
                if (!Codenames.TryParseId(id, out var category) || value is not JsonArray list)
                    continue;
                foreach (var item in list)
                    if (item is JsonValue iv && iv.TryGetValue(out string? real) && !string.IsNullOrEmpty(real))
                        book._shown.Add((category, real));
            }
        }
        return book;
    }

    private Dictionary<string, string> Map(CodenameCategory category)
    {
        if (!_maps.TryGetValue(category, out var map))
            _maps[category] = map = new Dictionary<string, string>(StringComparer.Ordinal);
        return map;
    }
}

/// <summary>
/// The codename pool and the file (Settings v2, slice 7; spec, Streamer mode, Codenames). The words come from sand, time, desert, sky and
/// glass, with no people, brands or trademarks. App-side Core: not on the MCP link list.
/// </summary>
public static partial class Codenames
{
    public const int SaltBytes = 32;
    public const int MinSaltBytes = 16;
    public const int SchemaVersion = 1;
    public const string FileName = "codenames.json";
    public const int MaxFileBytes = 4 * 1024 * 1024;

    public static IReadOnlyList<string> FirstWords { get; } =
        ["Dune", "Amber", "Mesa", "Dusk", "Ember", "Quartz", "Ochre", "Mirage", "Saffron", "Drift", "Tawny", "Opal", "Cinder", "Zenith"];

    public static IReadOnlyList<string> SecondWords { get; } =
        ["Fox", "Wren", "Heron", "Lark", "Moth", "Ibis", "Gecko", "Hare", "Kestrel", "Owl", "Oryx", "Swift", "Finch", "Plover"];

    /// <summary>The 196 pairs, first word major.</summary>
    public static IReadOnlyList<string> Pool { get; } = [.. FirstWords.SelectMany(f => SecondWords.Select(s => f + " " + s))];

    /// <summary>Pairs no codename may form: known product, team and character names. A pool change that forms one fails <c>CodenamesTests</c>.
    /// Compared by letters only, ignoring case ("Fire fox" is "Firefox").</summary>
    public static IReadOnlyList<string> DenyPairs { get; } =
    [
        "Desert Eagle", "Firefox", "Fire Fox", "Red Fox", "Night Owl", "Snow Leopard", "Golden Eagle", "Black Widow", "Silver Surfer",
        "Dark Horse", "Sand Cat", "Amber Alert", "Grey Owl", "Blue Heron", "Night Hawk", "Thunderbird", "Firebird", "Mockingbird",
        "Opal Fox", "Ember Fox",
    ];

    /// <summary>Allowlisted by default because they identify no one (spec; slice 7 decision 9): Sanduhr's own mod, its MCP server entry and
    /// the mod's marketplace in <c>sanduhr-meters@inline</c>.</summary>
    public static IReadOnlyList<(CodenameCategory Category, string Real)> DefaultShown { get; } =
        [(CodenameCategory.AddOn, "sanduhr-meters"), (CodenameCategory.AddOn, "sanduhr"), (CodenameCategory.AddOn, "inline")];

    public static string Id(CodenameCategory category) => category switch
    {
        CodenameCategory.Account => "account",
        CodenameCategory.Email => "email",
        CodenameCategory.Folder => "folder",
        CodenameCategory.Project => "project",
        CodenameCategory.Skill => "skill",
        CodenameCategory.Repo => "repo",
        CodenameCategory.Runner => "runner",
        CodenameCategory.Host => "host",
        CodenameCategory.City => "city",
        CodenameCategory.AddOn => "addon",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    public static bool TryParseId(string id, out CodenameCategory category)
    {
        foreach (var c in Enum.GetValues<CodenameCategory>())
        {
            if (Id(c) == id)
            {
                category = c;
                return true;
            }
        }
        category = default;
        return false;
    }

    /// <summary>The first pick: HMAC-SHA256 of (category, real name) keyed with the per-install salt, as an index into <see cref="Pool"/>.</summary>
    public static int StartIndex(byte[] salt, CodenameCategory category, string real)
    {
        var mac = HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(Id(category) + "\u001f" + real));
        return (int)(BinaryPrimitives.ReadUInt32BigEndian(mac) % (uint)Pool.Count);
    }

    [GeneratedRegex(@"^(?<pair>[A-Z][a-z]+ [A-Z][a-z]+)(?: (?<n>[2-9]|[1-9][0-9]+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex CodenameShape();

    private static readonly HashSet<string> PoolSet = new(Pool, StringComparer.Ordinal);

    /// <summary>A pool pair, or a pool pair with a numeral from 2.</summary>
    public static bool IsCodename(string text) =>
        CodenameShape().Match(text ?? "") is { Success: true } m && PoolSet.Contains(m.Groups["pair"].Value);

    /// <summary>The book in <paramref name="path"/>; null when the file is missing, too large, not JSON or not a book.</summary>
    public static CodenameBook? Load(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes)
                return null;
            return CodenameBook.FromJson(JsonNode.Parse(File.ReadAllText(path)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes the book atomically (a <c>.tmp</c> beside it, then a move). It is never sent anywhere (spec). False when it can't.</summary>
    public static bool Save(CodenameBook book, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, book.ToJson().ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~CodenamesTests > %TEMP%\slice7-t1.log 2>&1`
Expected: PASS. If `Another_salt_gives_another_codename_for_most_names` reports 5 or more equal, the HMAC isn't keyed with the salt: fix `StartIndex`, never the threshold.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/Codenames.cs windows-dotnet/tests/Sanduhr.Tests/CodenamesTests.cs
git commit -m "feat(settings-v2): codenames, a salted pool of 196 that keeps each name's codename and never shares one"
```

(End every commit message in this plan with the trailer line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`, as the standing rules say.)

---

### Task 2: `StreamerMask`, `StreamerFile`, the two paths and Reset's entries

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/StreamerMask.cs`, `windows-dotnet/src/Sanduhr.Core/StreamerFile.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/Paths.cs` (`CodenamesFile`, `StreamerStateFile`)
- Modify: `windows-dotnet/src/Sanduhr.Core/ResetPlan.cs` (the Settings part)
- Test: create `windows-dotnet/tests/Sanduhr.Tests/StreamerMaskTests.cs`, `windows-dotnet/tests/Sanduhr.Tests/StreamerFileTests.cs`; modify `windows-dotnet/tests/Sanduhr.Tests/ResetPlanTests.cs`

**Interfaces:**
- Consumes (Task 1): `CodenameBook`, `CodenameCategory`, `Codenames`.
- Produces:
  - `public sealed class StreamerMask`, with:
    - `StreamerMask(bool on, CodenameBook? book, string profileDir, IReadOnlyList<string>? userFolders = null)`, `static StreamerMask Off`
    - `bool On`, `string ProfileDir`, `event Action<CodenameCategory>? Assigned`
    - `string Name(CodenameCategory, string? real)`, `bool Hides(CodenameCategory, string? real)`
    - `static string FolderKey(string path)`, `string Folder(string path)`, `string FolderName(string name)`, `bool HidesFolder(string path)`
    - `string AddOn(string? id)`, `string Paths(string? text)`, `string Endpoint(string? url)`, `string HostsIn(string? text, string? url)`
    - `IReadOnlyList<Watcher> Watchers(IReadOnlyList<Watcher>)`, `string WeatherName(WeatherPlace? place, string? nickname)`
    - `JsonObject PublishSample(JsonObject payload, string thisPc)`
  - `public sealed record StreamerFolder(string Ref, string? Codename, bool Shown)`
  - `public static class StreamerFile`: `FileName` ("streamer.json"), `SchemaVersion` (1), `MaxBytes` (65536), `string FolderRef(string path)`, `JsonObject Build(bool on, IEnumerable<StreamerFolder>)`, `bool Write(string path, JsonObject)`
  - `Paths.CodenamesFile` (AppData), `Paths.StreamerStateFile` (`~\.sanduhr`)

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/StreamerMaskTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 7: the one formatter every surface calls (spec, Streamer mode). Off, it changes nothing and writes nothing.</summary>
public class StreamerMaskTests
{
    private const string Profile = @"C:\Users\qxuser";
    private static CodenameBook Book() => new(Enumerable.Repeat((byte)21, Codenames.SaltBytes).ToArray());
    private static StreamerMask OnMask(CodenameBook? book = null) => new(true, book ?? Book(), Profile);

    [Fact]
    public void Off_returns_every_name_as_it_is_and_assigns_nothing()
    {
        var book = Book();
        var off = new StreamerMask(false, book, Profile);
        Assert.Equal("Work", off.Name(CodenameCategory.Account, "Work"));
        Assert.Equal(@"~\.claude-work", off.Folder(Profile + @"\.claude-work"));
        Assert.Equal(".claude-work", off.FolderName(".claude-work"));
        Assert.Equal("https://qxhost.example/ingest", off.Endpoint("https://qxhost.example/ingest"));
        Assert.Equal(Profile + @"\AppData", off.Paths(Profile + @"\AppData"));
        Assert.Equal(0, book.Count);
        Assert.False(StreamerMask.Off.On);
        Assert.Equal("x", StreamerMask.Off.Name(CodenameCategory.Project, "x"));
    }

    [Fact]
    public void On_a_name_becomes_its_codename_and_keeps_it()
    {
        var mask = OnMask();
        var code = mask.Name(CodenameCategory.Account, "Qxalpha");
        Assert.True(Codenames.IsCodename(code));
        Assert.Equal(code, mask.Name(CodenameCategory.Account, "Qxalpha"));
        Assert.Equal(code, mask.Name(CodenameCategory.Account, "  Qxalpha "));   // trimmed before the lookup
        Assert.True(mask.Hides(CodenameCategory.Account, "Qxalpha"));
        Assert.Equal("", mask.Name(CodenameCategory.Account, ""));
        Assert.Equal("", mask.Name(CodenameCategory.Account, null));
    }

    [Fact]
    public void A_new_name_is_announced_once()
    {
        var mask = OnMask();
        var heard = new List<CodenameCategory>();
        mask.Assigned += heard.Add;
        mask.Name(CodenameCategory.Project, "a");
        mask.Name(CodenameCategory.Project, "a");
        mask.Folder(Profile + @"\.claude");
        Assert.Equal([CodenameCategory.Project, CodenameCategory.Folder], heard);
    }

    [Fact]
    public void A_shown_name_stays_real()
    {
        var book = Book();
        book.SetShown(CodenameCategory.Account, "Personal", true);
        book.SetShown(CodenameCategory.Folder, StreamerMask.FolderKey(Profile + @"\.claude"), true);
        var mask = OnMask(book);
        Assert.Equal("Personal", mask.Name(CodenameCategory.Account, "Personal"));
        Assert.False(mask.Hides(CodenameCategory.Account, "Personal"));
        Assert.Equal(@"~\.claude", mask.Folder(Profile + @"\.claude"));
        Assert.Equal(".claude", mask.FolderName(".claude"));
        Assert.Equal("sanduhr-meters@inline", mask.AddOn("sanduhr-meters@inline"));
        Assert.Equal(0, book.Count);
    }

    /// <summary>Review Focus 2.</summary>
    [Fact]
    public void A_folder_has_one_codename_however_its_path_is_written()
    {
        var mask = OnMask();
        var code = mask.Folder(Profile + @"\.claude-work");
        Assert.True(Codenames.IsCodename(code));
        Assert.Equal(code, mask.Folder(@"c:\users\QXUSER\.CLAUDE-WORK\"));
        Assert.Equal(code, mask.FolderName(".claude-work"));
        Assert.True(mask.HidesFolder(Profile + @"\.claude-work"));
    }

    /// <summary>Review Focus 1: labels are ordinal keys, folders are case-folded paths.</summary>
    [Fact]
    public void Case_differs_codename_differs_for_labels_but_not_for_folders()
    {
        var mask = OnMask();
        Assert.NotEqual(mask.Name(CodenameCategory.Account, "Dune"), mask.Name(CodenameCategory.Account, "dune"));
        Assert.Equal(mask.Folder(Profile + @"\.claude-A"), mask.Folder(Profile + @"\.claude-a"));
    }

    [Fact]
    public void An_add_on_id_masks_each_side_of_the_at()
    {
        var mask = OnMask();
        var shown = mask.AddOn("qxplugin@qxmarket");
        Assert.DoesNotContain("qx", shown);
        Assert.Matches(@"^[A-Z][a-z]+ [A-Z][a-z]+ ?\d*@[A-Z][a-z]+ [A-Z][a-z]+ ?\d*$", shown);
        Assert.Equal(mask.Name(CodenameCategory.AddOn, "qxplugin") + "@inline", mask.AddOn("qxplugin@inline"));
    }

    [Fact]
    public void Paths_show_the_profile_folder_as_USERPROFILE()
    {
        var mask = new StreamerMask(true, Book(), Profile, [Profile, @"D:\Home\qxother"]);
        Assert.Equal(@"%USERPROFILE%\AppData\Roaming\Sanduhr", mask.Paths(@"c:\users\qxuser\AppData\Roaming\Sanduhr"));
        Assert.Equal(@"copy failed: %USERPROFILE%\x.json", mask.Paths(@"copy failed: D:\Home\qxother\x.json"));
        Assert.Equal("no path here", mask.Paths("no path here"));
    }

    [Fact]
    public void An_endpoint_shows_its_host_as_a_codename_and_hides_its_path()
    {
        var mask = OnMask();
        var host = mask.Name(CodenameCategory.Host, "qxhost.example");
        Assert.Equal($"https://{host}/…", mask.Endpoint("https://qxhost.example/ingest?k=1"));
        Assert.Equal($"https://{host}", mask.Endpoint("https://qxhost.example"));
        Assert.Equal($"Redirected to {host}.", mask.HostsIn("Redirected to qxhost.example.", "https://qxhost.example/x"));
        Assert.Equal("", mask.Endpoint(""));
    }

    [Fact]
    public void A_runner_cards_title_names_the_runner_by_codename_and_keeps_the_job()
    {
        var mask = OnMask();
        var runner = new Watcher
        {
            Id = "r:k:build", Source = WatcherSource.Runner, Title = "qxrunner · build", Short = "build", Runner = "qxrunner", RunnerKey = "k", Job = "build",
        };
        var agent = new Watcher { Id = "a:1", Source = WatcherSource.Agent, Title = "Deploying qxproject" };
        var shown = mask.Watchers([runner, agent]);
        Assert.Equal($"{mask.Name(CodenameCategory.Runner, "qxrunner")} · build", shown[0].Title);
        Assert.Equal("Deploying qxproject", shown[1].Title);   // Claude's own words: not a name Sanduhr can map (decision 11)
        Assert.Same(runner, new StreamerMask(false, Book(), Profile).Watchers([runner])[0]);
    }

    [Fact]
    public void The_weather_piece_shows_the_nickname_else_the_citys_codename()
    {
        var place = WeatherPlace.Create("Qxcity", "Texas", "United States", 32.75, -97.33);
        var mask = OnMask();
        Assert.Equal("Home", mask.WeatherName(place, "Home"));
        Assert.Equal(mask.Name(CodenameCategory.City, place.Label), mask.WeatherName(place, ""));
        Assert.Equal("", new StreamerMask(false, Book(), Profile).WeatherName(place, ""));
        Assert.Equal("", mask.WeatherName(null, ""));
    }

    [Fact]
    public void The_publish_sample_masks_folders_projects_and_the_pc_and_leaves_the_numbers()
    {
        var payload = new JsonObject
        {
            ["date"] = "2026-10-26",
            ["machine"] = "QXPC",
            ["homes"] = new JsonArray(".claude-qxwork"),
            ["totals"] = new JsonObject { ["tokens"] = 1200 },
            ["byProject"] = new JsonArray(new JsonObject { ["name"] = "qxproj", ["tokens"] = 1200 }),
        };
        var mask = OnMask();
        var shown = mask.PublishSample(payload, "this PC");
        var text = shown.ToJsonString();
        Assert.DoesNotContain("qx", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("this PC", (string?)shown["machine"]);
        Assert.Equal(mask.FolderName(".claude-qxwork"), (string?)shown["homes"]![0]);
        Assert.Equal(mask.Name(CodenameCategory.Project, "qxproj"), (string?)shown["byProject"]![0]!["name"]);
        Assert.Equal(1200, (int)shown["totals"]!["tokens"]!);
        Assert.Equal("QXPC", (string?)payload["machine"]);   // the record itself is never changed
    }
}
```

Create `windows-dotnet/tests/Sanduhr.Tests/StreamerFileTests.cs`:

```csharp
using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 7: ~\.sanduhr\streamer.json, the statusline's half (spec, "Two locations, on purpose"). No real name goes in.</summary>
public class StreamerFileTests
{
    [Fact]
    public void A_folders_ref_is_the_account_ref_of_its_lower_case_full_path()
    {
        var a = StreamerFile.FolderRef(@"C:\Users\qxuser\.claude-work");
        Assert.Matches("^[0-9a-f]{8}$", a);
        Assert.Equal(a, StreamerFile.FolderRef(@"c:\users\QXUSER\.CLAUDE-WORK\"));
        Assert.Equal(SnapshotContract.AccountRef(@"c:\users\qxuser\.claude-work"), a);
        Assert.Equal("", StreamerFile.FolderRef(""));
    }

    [Fact]
    public void On_it_holds_codenames_and_shown_refs_and_no_real_name()
    {
        var work = StreamerFile.FolderRef(@"C:\Users\qxuser\.claude-qxwork");
        var home = StreamerFile.FolderRef(@"C:\Users\qxuser\.claude");
        var json = StreamerFile.Build(true, [new StreamerFolder(work, "Dune Fox", false), new StreamerFolder(home, null, true)]);
        Assert.Equal(1, (int)json["schema_version"]!);
        Assert.True((bool)json["on"]!);
        Assert.Equal("Dune Fox", (string?)json["folders"]![work]);
        Assert.Equal([home], json["shown"]!.AsArray().Select(n => (string)n!));
        Assert.DoesNotContain("qx", json.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Off_it_says_off_and_lists_nothing()
    {
        var json = StreamerFile.Build(false, [new StreamerFolder("abcd1234", "Dune Fox", false)]);
        Assert.False((bool)json["on"]!);
        Assert.Empty(json["folders"]!.AsObject());
        Assert.Empty(json["shown"]!.AsArray());
    }

    [Fact]
    public void A_ref_that_is_not_eight_hex_digits_is_left_out()
    {
        var json = StreamerFile.Build(true, [new StreamerFolder("", "Dune Fox", false), new StreamerFolder("ABCD1234", "Mesa Owl", false)]);
        Assert.Empty(json["folders"]!.AsObject());
    }

    [Fact]
    public void It_is_written_atomically_as_utf8_without_a_bom()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, ".sanduhr", StreamerFile.FileName);
        Assert.True(StreamerFile.Write(path, StreamerFile.Build(true, [])));
        Assert.False(File.Exists(path + ".tmp"));
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.True((bool)JsonNode.Parse(Encoding.UTF8.GetString(bytes))!["on"]!);
    }

    [Fact]
    public void The_two_files_live_where_the_spec_puts_them()
    {
        using var tmp = new TempDir();
        var paths = new Paths(Path.Combine(tmp.Path, "roaming"), Path.Combine(tmp.Path, "home"), Path.Combine(tmp.Path, "local"));
        Assert.Equal(Path.Combine(paths.AppDataDir, "codenames.json"), paths.CodenamesFile);
        Assert.Equal(Path.Combine(paths.ExternalDirPath, "streamer.json"), paths.StreamerStateFile);
    }
}
```

In `windows-dotnet/tests/Sanduhr.Tests/ResetPlanTests.cs`, add before the class's last closing brace (after `Inventory_on_an_empty_tree_is_empty_and_does_not_create_the_external_folder`; Read the end of the file to place it):

```csharp

    /// <summary>Settings v2, slice 7 (spec, Reset): codenames.json goes with All settings, streamer.json by name from ~\.sanduhr. Review Focus 5.</summary>
    [Fact]
    public void Reset_lists_both_files_by_name_only()
    {
        var paths = Make();
        Touch(paths.CodenamesFile);
        Touch(Path.Combine(paths.ExternalDirPath, StreamerFile.FileName));
        Touch(Path.Combine(paths.ExternalDirPath, "streamer.json.bak"));   // someone else's name in the shared folder
        var settings = ResetPlan.Inventory(paths)[ResetPart.Settings].Select(i => i.Path).ToList();
        Assert.Contains(paths.CodenamesFile, settings);
        Assert.Contains(Path.Combine(paths.ExternalDirPath, StreamerFile.FileName), settings);
        Assert.DoesNotContain(Path.Combine(paths.ExternalDirPath, "streamer.json.bak"), settings);
        Assert.DoesNotContain(paths.ExternalDirPath, settings);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~StreamerMaskTests|FullyQualifiedName~StreamerFileTests|FullyQualifiedName~ResetPlanTests" > %TEMP%\slice7-t2-red.log 2>&1`
Expected: the build FAILS with `The type or namespace name 'StreamerMask' could not be found`.

- [ ] **Step 3: The paths**

In `windows-dotnet/src/Sanduhr.Core/Paths.cs`, replace

```csharp
    public string LastErrorFile => Path.Combine(AppDataDir, "last_error.json");
```

with

```csharp
    public string LastErrorFile => Path.Combine(AppDataDir, "last_error.json");

    /// <summary>Streamer mode's salt, codename maps and allowlist (Settings v2, slice 7). Only the app reads it, so it stays in AppData; it
    /// holds real names and is never sent anywhere.</summary>
    public string CodenamesFile => Path.Combine(AppDataDir, Codenames.FileName);
```

and replace

```csharp
    public string SnapshotFile => Path.Combine(ExternalDir, "snapshot.json");
```

with

```csharp
    public string SnapshotFile => Path.Combine(ExternalDir, "snapshot.json");

    /// <summary>Streamer mode's half for the statusline (Settings v2, slice 7): on, and folder ref → codename, never a real name. In
    /// <see cref="ExternalDir"/> because the statusline runs outside the package. Shared ground: only this name is Sanduhr's.</summary>
    public string StreamerStateFile => Path.Combine(_externalDir, StreamerFile.FileName);
```

(`StreamerStateFile` uses `_externalDir`, not `ExternalDir`, so asking for the path never creates the shared folder.)

- [ ] **Step 4: `StreamerFile.cs`**

Create `windows-dotnet/src/Sanduhr.Core/StreamerFile.cs`:

```csharp
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>One Claude Code folder as streamer.json lists it: its ref, its codename (null when it has none) and whether its real label shows.</summary>
public sealed record StreamerFolder(string Ref, string? Codename, bool Shown);

/// <summary>
/// <c>%USERPROFILE%\.sanduhr\streamer.json</c> (Settings v2, slice 7; spec, "Two locations, on purpose"). The statusline reads it: streamer
/// mode's state and each folder's codename, keyed by <see cref="FolderRef"/>. No real name goes in. The script's <c>Get-StreamerLabel</c>
/// computes the same ref (a run test pins the two together). App-side Core: not on the MCP link list.
/// </summary>
public static class StreamerFile
{
    public const string FileName = "streamer.json";
    public const int SchemaVersion = 1;
    /// <summary>The script reads no more than this; the widget never writes more.</summary>
    public const int MaxBytes = 65536;

    /// <summary><see cref="SnapshotContract.AccountRef"/> over the folder's canonical key (<see cref="StreamerMask.FolderKey"/>): 8 lowercase
    /// hex digits; empty for a path that can't be read.</summary>
    public static string FolderRef(string path)
    {
        var key = StreamerMask.FolderKey(path);
        return key.Length == 0 ? "" : SnapshotContract.AccountRef(key) ?? "";
    }

    /// <summary>The file's content. Off lists nothing. A shown folder goes under <c>shown</c> with no codename.</summary>
    public static JsonObject Build(bool on, IEnumerable<StreamerFolder> folders)
    {
        var map = new JsonObject();
        var shown = new JsonArray();
        if (on)
        {
            foreach (var f in folders)
            {
                if (!IsRef(f.Ref))
                    continue;
                if (f.Shown) shown.Add(f.Ref);
                else if (f.Codename is { Length: > 0 } code) map[f.Ref] = code;
            }
        }
        return new JsonObject { ["schema_version"] = SchemaVersion, ["on"] = on, ["folders"] = map, ["shown"] = shown };
    }

    /// <summary>Writes atomically, UTF-8 without a BOM. False when it can't.</summary>
    public static bool Write(string path, JsonObject content)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsRef(string s) => s.Length == 8 && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
```

- [ ] **Step 5: `StreamerMask.cs`**

Create `windows-dotnet/src/Sanduhr.Core/StreamerMask.cs`:

```csharp
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// The one formatter every surface calls while streamer mode may be on (Settings v2, slice 7; spec, Streamer mode). Off (<see cref="Off"/>,
/// or built with <c>on: false</c>), every method returns its input as the page showed it before, and nothing is assigned. On, a name that is
/// not allowlisted becomes its codename, assigned the first time a surface asks and kept (<see cref="CodenameBook"/>); <see cref="Assigned"/>
/// tells the owner to save. Thread-safe. App-side Core: not on the MCP link list. It calls <see cref="SnapshotContract"/> and
/// <see cref="WatcherText"/>, which are linked, and changes neither.
/// </summary>
public sealed class StreamerMask
{
    private readonly object _gate = new();
    private readonly CodenameBook? _book;
    private readonly IReadOnlyList<string> _userFolders;

    /// <param name="profileDir">The profile folder the Claude Code pages use (folders show as <c>~\…</c> under it).</param>
    /// <param name="userFolders">Folders <see cref="Paths(string?)"/> writes as <c>%USERPROFILE%</c>; the profile folder when null.</param>
    public StreamerMask(bool on, CodenameBook? book, string profileDir, IReadOnlyList<string>? userFolders = null)
    {
        On = on && book is not null;
        _book = book;
        ProfileDir = profileDir;
        _userFolders = [.. (userFolders ?? [profileDir])
            .Select(ClaudeCodeFolders.Norm).Where(f => f.Length > 3).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(f => f.Length)];
    }

    /// <summary>Streamer mode off: nothing is masked and nothing is written.</summary>
    public static StreamerMask Off { get; } = new(false, null, "");

    public bool On { get; }
    public string ProfileDir { get; }

    /// <summary>A name got its codename just now (the book changed and needs saving).</summary>
    public event Action<CodenameCategory>? Assigned;

    /// <summary>The name, or its codename while on and not allowlisted. Blank stays blank.</summary>
    public string Name(CodenameCategory category, string? real)
    {
        if (string.IsNullOrWhiteSpace(real) || !On)
            return real ?? "";
        return Code(category, real.Trim()) ?? real;
    }

    /// <summary>Whether <see cref="Name"/> hides this name now (on, not blank, not allowlisted). Assigns nothing.</summary>
    public bool Hides(CodenameCategory category, string? real)
    {
        if (!On || string.IsNullOrWhiteSpace(real))
            return false;
        lock (_gate) return !_book!.IsShown(category, real.Trim());
    }

    /// <summary>A folder's key: its full path, without a trailing separator, lower-cased (invariant). Empty when the path can't be read.</summary>
    public static string FolderKey(string path) => ClaudeCodeFolders.Norm(path).ToLowerInvariant();

    /// <summary>A Claude Code folder as a card shows it: <c>~\.claude-work</c> off or allowlisted, else its codename alone.</summary>
    public string Folder(string path)
    {
        var shown = ClaudeCodeFolders.Display(path, ProfileDir);
        if (!On)
            return shown;
        var key = FolderKey(path);
        return key.Length == 0 ? Name(CodenameCategory.Folder, path) : Code(CodenameCategory.Folder, key) ?? shown;
    }

    /// <summary>A folder named by its leaf in the profile folder (the session archive, Folders to share, the MCP consent): the leaf off or
    /// allowlisted, else the same codename <see cref="Folder"/> gives.</summary>
    public string FolderName(string name)
    {
        if (!On || string.IsNullOrWhiteSpace(name))
            return name;
        return Code(CodenameCategory.Folder, FolderKey(Path.Combine(ProfileDir, name))) ?? name;
    }

    public bool HidesFolder(string path)
    {
        var key = FolderKey(path);
        return key.Length > 0 && Hides(CodenameCategory.Folder, key);
    }

    /// <summary>A plugin, marketplace, MCP server or pack id. <c>name@marketplace</c> masks each side, so "inline" (allowlisted) stays.</summary>
    public string AddOn(string? id)
    {
        if (!On || string.IsNullOrWhiteSpace(id))
            return id ?? "";
        int at = id.LastIndexOf('@');
        return at > 0 && at < id.Length - 1
            ? Name(CodenameCategory.AddOn, id[..at]) + "@" + Name(CodenameCategory.AddOn, id[(at + 1)..])
            : Name(CodenameCategory.AddOn, id);
    }

    /// <summary>Text that may hold a path: each user folder becomes <c>%USERPROFILE%</c> (spec, About). Nothing else changes.</summary>
    public string Paths(string? text)
    {
        if (!On || string.IsNullOrEmpty(text))
            return text ?? "";
        foreach (var folder in _userFolders)
            text = text.Replace(folder, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    /// <summary>A publish endpoint: the host as a codename, the path hidden as "/…" (spec, Publish usage).</summary>
    public string Endpoint(string? url)
    {
        if (!On || string.IsNullOrWhiteSpace(url))
            return url ?? "";
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return Name(CodenameCategory.Host, url);
        var rest = uri.PathAndQuery is "" or "/" ? "" : "/…";
        return $"{uri.Scheme}://{Name(CodenameCategory.Host, uri.Host)}{rest}";
    }

    /// <summary>A line that may name the endpoint's host (a publish result): the host becomes its codename, and paths are masked.</summary>
    public string HostsIn(string? text, string? url)
    {
        if (!On || string.IsNullOrEmpty(text))
            return text ?? "";
        if (Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            text = text.Replace(uri.Host, Name(CodenameCategory.Host, uri.Host), StringComparison.OrdinalIgnoreCase);
        return Paths(text);
    }

    /// <summary>The Desk's watcher cards: a CI runner's card names its runner by codename and keeps the job (spec, the Desk). Claude's own
    /// cards are its words, which no codename can map (slice 7 decision 11).</summary>
    public IReadOnlyList<Watcher> Watchers(IReadOnlyList<Watcher> watchers)
    {
        if (!On)
            return watchers;
        return [.. watchers.Select(w => w.Source == WatcherSource.Runner && !string.IsNullOrWhiteSpace(w.Runner)
            ? w with { Title = WatcherText.Clip($"{Name(CodenameCategory.Runner, w.Runner)} · {w.Job ?? w.Short}", WatcherLimits.Title) }
            : w)];
    }

    /// <summary>The name the weather piece draws in place of the place's own: the "Show it as" nickname when set (spec), else, while on,
    /// the city's codename; else empty (the piece draws the place's name).</summary>
    public string WeatherName(WeatherPlace? place, string? nickname)
    {
        if (!string.IsNullOrWhiteSpace(nickname))
            return nickname;
        return On && place is not null ? Name(CodenameCategory.City, place.Label) : "";
    }

    /// <summary>A copy of a publish record for Show a sample: folder and project names as codenames, this PC's name as
    /// <paramref name="thisPc"/>. The record itself is never changed (it is what would be sent).</summary>
    public JsonObject PublishSample(JsonObject payload, string thisPc)
    {
        var copy = (JsonObject)JsonNode.Parse(payload.ToJsonString())!;
        if (!On)
            return copy;
        if (copy["machine"] is not null)
            copy["machine"] = thisPc;
        if (copy["homes"] is JsonArray homes)
            for (int i = 0; i < homes.Count; i++)
                if (homes[i] is JsonValue v && v.TryGetValue(out string? home))
                    homes[i] = FolderName(home);
        if (copy["byProject"] is JsonArray projects)
            foreach (var p in projects.OfType<JsonObject>())
                if (p["name"] is JsonValue v && v.TryGetValue(out string? name))
                    p["name"] = Name(CodenameCategory.Project, name);
        return copy;
    }

    /// <summary>The codename, or null when the name is allowlisted. Assigns under the lock and announces outside it.</summary>
    private string? Code(CodenameCategory category, string key)
    {
        string code;
        bool fresh;
        lock (_gate)
        {
            if (_book!.IsShown(category, key))
                return null;
            fresh = !_book.TryGet(category, key, out code);
            if (fresh)
                code = _book.Assign(category, key);
        }
        if (fresh)
            Assigned?.Invoke(category);
        return code;
    }
}
```

- [ ] **Step 6: Reset's entries**

In `windows-dotnet/src/Sanduhr.Core/ResetPlan.cs`, replace

```csharp
        AddFile(settings, Path.Combine(ext, WallpaperPreview.FileName));
        map[ResetPart.Settings] = Finish(settings, backups);
```

with

```csharp
        AddFile(settings, Path.Combine(ext, WallpaperPreview.FileName));
        // Streamer mode (Settings v2, slice 7; spec, Reset): the codename book goes with the settings; streamer.json is taken from the
        // shared folder by its own name only.
        AddFile(settings, paths.CodenamesFile);
        AddFile(settings, Path.Combine(ext, StreamerFile.FileName));
        map[ResetPart.Settings] = Finish(settings, backups);
```

- [ ] **Step 7: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~StreamerMaskTests|FullyQualifiedName~StreamerFileTests|FullyQualifiedName~ResetPlanTests|FullyQualifiedName~PathsTests" > %TEMP%\slice7-t2.log 2>&1`
Expected: PASS. Then run `dotnet test tests/Sanduhr.Mcp.Tests > %TEMP%\slice7-t2-mcp.log 2>&1`: `TrustBoundaryTests` passes untouched, because no linked file changed.

- [ ] **Step 8: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/StreamerMask.cs windows-dotnet/src/Sanduhr.Core/StreamerFile.cs windows-dotnet/src/Sanduhr.Core/Paths.cs windows-dotnet/src/Sanduhr.Core/ResetPlan.cs windows-dotnet/tests/Sanduhr.Tests/StreamerMaskTests.cs windows-dotnet/tests/Sanduhr.Tests/StreamerFileTests.cs windows-dotnet/tests/Sanduhr.Tests/ResetPlanTests.cs
git commit -m "feat(settings-v2): StreamerMask, the one formatter, and streamer.json, which holds codenames and never a real name"
```

---

### Task 3: The slice's names

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (the slice 7 block)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (the two anchor constants)
- Test: create `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice7NamesTests.cs`

**Interfaces:**
- Consumes (Task 1): `CodenameCategory`.
- Produces (every name later tasks bind):
  - `StreamerSection` ("Streamer mode"), `StreamerSwitch`, `StreamerCaption`, `StreamerCantReach`, `StreamerCantReachMore`, `StreamerDemoLine`
  - `ShuffleCodenames`, `ShuffleCaption`, `ShuffleQuestion`, `ShuffleDetail`, `ShuffleConfirm`, `ShuffleCancel`
  - `ShowRealNameSection`, `ShowRealNameOnStream`, `AllowlistCaptionOn`, `AllowlistCaptionOff`, `AllowlistEmpty`, `AllowlistRowName(string)`, `AllowlistRowOff(string codename, string real)`, `CodenameCategoryTitle(CodenameCategory)`
  - `StreamerBanner`, `StreamerBannerLinkName`, `StreamerIndicatorName`, `StreamerTooltipPrefix`, `TrayTooltip(bool streamer, string text)`
  - `ExportsKeepRealNames`, `StreamerThisPc`, `PlacesHiddenNote`, `EndpointHiddenNote`, `StreamerChipName(string shown, bool streamer)`
  - `SettingsIndex.Streamer` ("streamer"), `SettingsIndex.StreamerAllowlist` ("streamer-allowlist")

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice7NamesTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 7: streamer mode's words. The spec's sentences are pinned word for word; the rest are checked for the copy rules.</summary>
public class SettingsSlice7NamesTests
{
    [Fact]
    public void The_switch_and_its_caption_are_the_specs_words()
    {
        Assert.Equal("Streamer mode", SettingsNames.StreamerSection);
        Assert.Equal("Streamer mode: hide accounts, folders and projects", SettingsNames.StreamerSwitch);
        Assert.Equal("Live numbers stay. Names become codenames until you choose to show one.", SettingsNames.StreamerCaption);
        Assert.Equal("Claude Code's own screen isn't covered. Check what it shows before you share it.", SettingsNames.StreamerCantReach);
        Assert.Equal("Shuffle codenames…", SettingsNames.ShuffleCodenames);
        Assert.Equal("Every codename changes. Viewers following a name will lose it.", SettingsNames.ShuffleDetail);
        Assert.Equal("Show the real name", SettingsNames.ShowRealNameSection);
        Assert.Equal("Show the real name on stream", SettingsNames.ShowRealNameOnStream);
        Assert.Equal("Exports keep real names.", SettingsNames.ExportsKeepRealNames);
    }

    /// <summary>Exactly true (decision 11): what streamer mode can't reach is named, and MCP answers are among it (P6, option A).</summary>
    [Fact]
    public void What_it_cant_reach_names_the_sign_in_window_the_MCP_answers_and_Claudes_cards()
    {
        Assert.Contains("sign-in window", SettingsNames.StreamerCantReachMore);
        Assert.Contains("MCP server", SettingsNames.StreamerCantReachMore);
        Assert.Contains("watcher cards", SettingsNames.StreamerCantReachMore);
    }

    /// <summary>The demo line: the search word "demo" lands on it (spec), and it says how demo mode starts, as DemoMode does.</summary>
    [Fact]
    public void The_demo_line_says_how_demo_mode_starts()
    {
        Assert.Contains("--demo", SettingsNames.StreamerDemoLine);
        Assert.Contains("SANDUHR_DEMO=1", SettingsNames.StreamerDemoLine);
    }

    [Fact]
    public void The_banner_and_its_link_are_the_specs()
    {
        Assert.Equal("Streamer mode is on: names are codenames.", SettingsNames.StreamerBanner);
        Assert.Equal("Privacy settings…, Streamer mode", SettingsNames.StreamerBannerLinkName);
    }

    [Fact]
    public void The_tray_tooltip_starts_with_streamer_mode_only_while_it_is_on()
    {
        Assert.Equal("Streamer mode · Sanduhr für Claude — 5h 42%", SettingsNames.TrayTooltip(true, "Sanduhr für Claude — 5h 42%"));
        Assert.Equal("Sanduhr für Claude — 5h 42%", SettingsNames.TrayTooltip(false, "Sanduhr für Claude — 5h 42%"));
    }

    [Fact]
    public void The_chip_is_spoken_with_the_indicator_after_the_name()
    {
        Assert.Equal("Dune Fox, Streamer mode on", SettingsNames.StreamerChipName("Dune Fox", streamer: true));
        Assert.Equal("Work", SettingsNames.StreamerChipName("Work", streamer: false));
    }

    [Fact]
    public void Allowlist_rows_name_the_codename_and_show_the_real_name_only_while_off()
    {
        Assert.Equal("Show the real name on stream, Dune Fox", SettingsNames.AllowlistRowName("Dune Fox"));
        Assert.Equal(@"Dune Fox · ~\.claude-work", SettingsNames.AllowlistRowOff("Dune Fox", @"~\.claude-work"));
        foreach (var c in Enum.GetValues<CodenameCategory>())
            Assert.False(string.IsNullOrWhiteSpace(SettingsNames.CodenameCategoryTitle(c)));
        Assert.Equal(Enum.GetValues<CodenameCategory>().Length,
            Enum.GetValues<CodenameCategory>().Select(SettingsNames.CodenameCategoryTitle).Distinct().Count());
    }

    [Fact]
    public void Every_slice_7_sentence_passes_the_copy_rules()
    {
        string[] all =
        [
            SettingsNames.StreamerSwitch, SettingsNames.StreamerCaption, SettingsNames.StreamerCantReach, SettingsNames.StreamerCantReachMore,
            SettingsNames.StreamerDemoLine, SettingsNames.ShuffleCaption, SettingsNames.ShuffleQuestion, SettingsNames.ShuffleDetail,
            SettingsNames.AllowlistCaptionOn, SettingsNames.AllowlistCaptionOff, SettingsNames.AllowlistEmpty, SettingsNames.StreamerBanner,
            SettingsNames.ExportsKeepRealNames, SettingsNames.PlacesHiddenNote, SettingsNames.EndpointHiddenNote,
        ];
        foreach (var s in all)
        {
            Assert.Empty(SettingsNames.PathProblems(s));
            Assert.Empty(SettingsNames.RetiredNames(s));
            Assert.DoesNotContain("—", s);
        }
    }

    [Fact]
    public void The_two_anchors_are_the_ids_the_boosts_already_name()
    {
        Assert.Equal("streamer", SettingsIndex.Streamer);
        Assert.Equal("streamer-allowlist", SettingsIndex.StreamerAllowlist);
        Assert.Contains(SettingsIndex.Boosts, b => b.PageId == "privacy" && b.Anchor == SettingsIndex.Streamer);
        Assert.Contains(SettingsIndex.Boosts, b => b.PageId == "privacy" && b.Anchor == SettingsIndex.StreamerAllowlist);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsSlice7NamesTests > %TEMP%\slice7-t3-red.log 2>&1`
Expected: the build FAILS with `'SettingsNames' does not contain a definition for 'StreamerSection'`.

- [ ] **Step 3: The names**

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, replace

```csharp
    public const string StoreKeepsUpToDate = "The Microsoft Store keeps Sanduhr up to date.";
```

with

```csharp
    public const string StoreKeepsUpToDate = "The Microsoft Store keeps Sanduhr up to date.";

    // Streamer mode (Settings v2, slice 7; spec, Streamer mode; WF7). The switch, its caption, Shuffle's words and the banner are the spec's.
    public const string StreamerSection = "Streamer mode";
    public const string StreamerSwitch = "Streamer mode: hide accounts, folders and projects";
    public const string StreamerCaption = "Live numbers stay. Names become codenames until you choose to show one.";
    public const string StreamerCantReach = "Claude Code's own screen isn't covered. Check what it shows before you share it.";
    /// <summary>The rest of what it can't reach, exactly (slice 7 decision 11; P6, option A).</summary>
    public const string StreamerCantReachMore =
        "Also not covered: claude.ai's own pages in the sign-in window, what Sanduhr's MCP server answers Claude (it becomes part of your "
        + "conversation), and the words Claude writes on its watcher cards.";
    /// <summary>Under the switch: the search word "demo" lands here (spec, Search). True to DemoMode: it starts only from the command line.</summary>
    public const string StreamerDemoLine =
        "Demo mode is something else: it swaps in sample numbers and accounts. It starts only when Sanduhr is started with --demo or SANDUHR_DEMO=1.";
    public const string ShuffleCodenames = "Shuffle codenames…";
    public const string ShuffleCaption = "Draws new codenames for everything. Names you chose to show stay shown.";
    public const string ShuffleQuestion = "Shuffle codenames?";
    public const string ShuffleDetail = "Every codename changes. Viewers following a name will lose it.";
    public const string ShuffleConfirm = "Shuffle";
    public const string ShuffleCancel = "Cancel";
    public const string ShowRealNameSection = "Show the real name";
    public const string ShowRealNameOnStream = "Show the real name on stream";
    public const string AllowlistCaptionOn = "While streamer mode is on, this list shows codenames only.";
    public const string AllowlistCaptionOff = "Each name with its codename. A ticked name shows as it is while streamer mode is on.";
    public const string AllowlistEmpty = "Nothing has a codename yet. A name gets one the first time streamer mode hides it.";
    public static string AllowlistRowName(string shown) => $"{ShowRealNameOnStream}, {shown}";
    public static string AllowlistRowOff(string codename, string real) => $"{codename} · {real}";
    public static string CodenameCategoryTitle(CodenameCategory category) => category switch
    {
        CodenameCategory.Account => "Accounts",
        CodenameCategory.Email => "Emails",
        CodenameCategory.Folder => "Claude Code folders",
        CodenameCategory.Project => "Projects",
        CodenameCategory.Skill => "Skills",
        CodenameCategory.Repo => "Repos",
        CodenameCategory.Runner => "CI runners",
        CodenameCategory.Host => "Publish endpoints",
        CodenameCategory.City => "Places",
        CodenameCategory.AddOn => "Plugins, marketplaces and packs",
        _ => category.ToString(),
    };
    public const string StreamerBanner = "Streamer mode is on: names are codenames.";
    public static string StreamerBannerLinkName => $"{LinkTitle(SettingsPage.Privacy)}, {StreamerSection}";
    public const string StreamerIndicatorName = "Streamer mode on";
    public const string StreamerTooltipPrefix = "Streamer mode · ";
    /// <summary>The tray tooltip (spec: it starts with "Streamer mode · " while on). It names no account at HEAD (slice 7 decision 2).</summary>
    public static string TrayTooltip(bool streamer, string text) => streamer ? StreamerTooltipPrefix + text : text;
    /// <summary>The widget's account chip as spoken: the shown name, then the indicator while on (owner taste 4).</summary>
    public static string StreamerChipName(string shown, bool streamer) => streamer ? $"{shown}, {StreamerIndicatorName}" : shown;
    public const string ExportsKeepRealNames = "Exports keep real names.";
    /// <summary>Show a sample's machine name while streamer mode is on (owner taste 13).</summary>
    public const string StreamerThisPc = "this PC";
    public const string PlacesHiddenNote = "Streamer mode shows places as codenames. Turn it off to see their names.";
    public const string EndpointHiddenNote = "Streamer mode hides the address. Turn it off to edit it.";
```

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`, replace

```csharp
    // Privacy (slice 6): Streamer mode's anchors come first on the page in slice 7
    public const string Network = "network";
```

with

```csharp
    // Privacy: Streamer mode first (slice 7), then What leaves this PC and What Claude can change (slice 6)
    public const string Streamer = "streamer";
    public const string StreamerAllowlist = "streamer-allowlist";
    public const string Network = "network";
```

The constants alone change no index: nothing names them until Task 6 marks the anchors in XAML, indexes them, and adds the `Table` rows and `NameAnchors` for the switch and Shuffle in the same commit (the Global Constraint "Index with the XAML").

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSlice7NamesTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsSlice6" > %TEMP%\slice7-t3.log 2>&1`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/tests/Sanduhr.Tests/SettingsSlice7NamesTests.cs
git commit -m "feat(settings-v2): streamer mode's names, the spec's words for the switch, Shuffle and the banner"
```

---

### Task 4: `streamer_mode`, `StreamerService` and `StreamerMode`: one switch, one book, one writer

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs` (`LoadStreamerMode`, `SaveStreamerMode`)
- Modify: `windows-dotnet/tests/Sanduhr.Tests/SettingsKeysContractTests.cs` (`streamer_mode` in `Frozen`)
- Create: `windows-dotnet/src/Sanduhr.App/Services/StreamerMode.cs`, `windows-dotnet/src/Sanduhr.App/Services/StreamerService.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (the widget owns the service)
- Modify: `windows-dotnet/tests/Sanduhr.App.Tests/AppWindowFixture.cs` (reset `StreamerMode`)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/StreamerServiceTests.cs`

**Interfaces:**
- Consumes (Tasks 1, 2): `CodenameBook`, `Codenames.Load/Save`, `StreamerMask`, `StreamerFile`, `Paths.CodenamesFile`, `Paths.StreamerStateFile`.
- Produces:
  - `SettingsStore.LoadStreamerMode()` (false when absent), `SettingsStore.SaveStreamerMode(bool)`
  - `public static class StreamerMode`: `StreamerMask Mask`, `bool On`, `internal static void Use(StreamerMask)`, `internal static void Reset()`
  - `public sealed class StreamerService : IDisposable`:
    - `StreamerService(Paths paths, SettingsStore store, Func<IReadOnlyList<CcFolder>> folders, Func<string> profileDir, Action<Action>? post = null)`
    - `StreamerMask Mask`, `bool On`, `event Action? Changed`
    - `void Load()`, `void SetOn(bool on)`, `void Shuffle()`, `void SetShown(CodenameCategory, string real, bool shown)`
    - `IReadOnlyList<CodenameEntry> Entries()`, `void Flush()`, `void Dispose()`
  - `WidgetViewModel.Streamer` (a `StreamerService`), loaded in the constructor

- [ ] **Step 1: Write the failing tests**

In `windows-dotnet/tests/Sanduhr.Tests/SettingsKeysContractTests.cs`, in `Frozen`, replace

```csharp
        "statusline_enabled",
        "theme",
```

with

```csharp
        "statusline_enabled",
        "streamer_mode",
        "theme",
```

Create `windows-dotnet/tests/Sanduhr.App.Tests/StreamerServiceTests.cs`:

```csharp
using System.IO;
using System.Text.Json.Nodes;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 7: the switch, the book, the coalesced writes and streamer.json (spec, Streamer mode; Reset).</summary>
[Collection("SettingsWindow")]
public sealed class StreamerServiceTests : AppWindowFixture
{
    private (StreamerService Service, Paths Paths, List<Action> Posted) Make()
    {
        var paths = ScratchPaths();
        var posted = new List<Action>();
        Home(".claude-qxwork");
        var service = new StreamerService(paths, new SettingsStore(paths),
            () => ClaudeCodeFolders.Build(Profile, null, []), () => Profile, post: posted.Add);
        return (service, paths, posted);
    }

    private static void RunPosted(List<Action> posted)
    {
        var now = posted.ToList();
        posted.Clear();
        foreach (var a in now) a();
    }

    [Fact]
    public void Absent_is_off_and_off_writes_no_file()
    {
        var (service, paths, _) = Make();
        service.Load();
        Assert.False(service.On);
        Assert.False(StreamerMode.On);
        Assert.False(new SettingsStore(paths).LoadStreamerMode());
        Assert.False(File.Exists(paths.CodenamesFile));
        Assert.False(File.Exists(paths.StreamerStateFile));
        Assert.Equal("Qxalpha", StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"));
    }

    [Fact]
    public void Turning_it_on_saves_the_key_masks_and_maps_every_known_folder_for_the_statusline()
    {
        var (service, paths, _) = Make();
        service.Load();
        int changed = 0;
        service.Changed += () => changed++;
        service.SetOn(true);

        Assert.Equal(1, changed);
        Assert.True(new SettingsStore(paths).LoadStreamerMode());
        Assert.True(StreamerMode.On);
        Assert.Same(service.Mask, StreamerMode.Mask);
        var json = JsonNode.Parse(File.ReadAllText(paths.StreamerStateFile))!;
        Assert.True((bool)json["on"]!);
        var work = StreamerFile.FolderRef(Path.Combine(Profile, ".claude-qxwork"));
        var plain = StreamerFile.FolderRef(Path.Combine(Profile, ".claude"));
        Assert.Equal(service.Mask.Folder(Path.Combine(Profile, ".claude-qxwork")), (string?)json["folders"]![work]);
        Assert.NotNull(json["folders"]![plain]);
        Assert.DoesNotContain("qx", File.ReadAllText(paths.StreamerStateFile), StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(paths.CodenamesFile));
    }

    [Fact]
    public void A_codename_survives_a_restart()
    {
        var (service, paths, posted) = Make();
        service.Load();
        service.SetOn(true);
        var code = StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
        RunPosted(posted);
        service.Dispose();

        var again = new StreamerService(paths, new SettingsStore(paths), () => ClaudeCodeFolders.Build(Profile, null, []), () => Profile, post: _ => { });
        again.Load();
        Assert.True(again.On);
        Assert.Equal(code, again.Mask.Name(CodenameCategory.Account, "Qxalpha"));
    }

    /// <summary>Decision 10: a burst of new names is one write, on the next dispatcher turn.</summary>
    [Fact]
    public void A_burst_of_new_names_is_written_once()
    {
        var (service, paths, posted) = Make();
        service.Load();
        service.SetOn(true);
        var stamp = File.GetLastWriteTimeUtc(paths.CodenamesFile);
        for (int i = 0; i < 50; i++)
            StreamerMode.Mask.Name(CodenameCategory.Project, "qxproject-" + i);
        Assert.Single(posted);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(paths.CodenamesFile));
        RunPosted(posted);
        var book = Codenames.Load(paths.CodenamesFile)!;
        Assert.True(book.TryGet(CodenameCategory.Project, "qxproject-49", out _));
    }

    [Fact]
    public void Turning_it_off_keeps_the_book_and_tells_the_statusline()
    {
        var (service, paths, _) = Make();
        service.Load();
        service.SetOn(true);
        var code = StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
        service.SetOn(false);
        Assert.False(StreamerMode.On);
        Assert.Equal("Qxalpha", StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"));
        Assert.False((bool)JsonNode.Parse(File.ReadAllText(paths.StreamerStateFile))!["on"]!);
        service.SetOn(true);
        Assert.Equal(code, StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"));
    }

    [Fact]
    public void Shuffle_renames_everything_keeps_the_allowlist_and_rewrites_streamer_json()
    {
        var (service, paths, _) = Make();
        service.Load();
        service.SetOn(true);
        var folder = Path.Combine(Profile, ".claude-qxwork");
        var before = StreamerMode.Mask.Folder(folder);
        service.SetShown(CodenameCategory.Account, "Personal", true);
        int changed = 0;
        service.Changed += () => changed++;
        service.Shuffle();
        Assert.Equal(1, changed);
        Assert.NotEqual(before, StreamerMode.Mask.Folder(folder));
        Assert.Equal("Personal", StreamerMode.Mask.Name(CodenameCategory.Account, "Personal"));
        var json = JsonNode.Parse(File.ReadAllText(paths.StreamerStateFile))!;
        Assert.Equal(StreamerMode.Mask.Folder(folder), (string?)json["folders"]![StreamerFile.FolderRef(folder)]);
    }

    [Fact]
    public void Showing_a_folders_real_name_moves_it_to_shown_in_streamer_json()
    {
        var (service, paths, _) = Make();
        service.Load();
        service.SetOn(true);
        var folder = Path.Combine(Profile, ".claude-qxwork");
        service.SetShown(CodenameCategory.Folder, StreamerMask.FolderKey(folder), true);
        var json = JsonNode.Parse(File.ReadAllText(paths.StreamerStateFile))!;
        var reference = StreamerFile.FolderRef(folder);
        Assert.Contains(reference, json["shown"]!.AsArray().Select(n => (string)n!));
        Assert.Null(json["folders"]![reference]);
        Assert.Equal(@"~\.claude-qxwork", StreamerMode.Mask.Folder(folder));
    }

    [Fact]
    public void The_allowlist_can_be_set_while_off_and_entries_list_it()
    {
        var (service, _, _) = Make();
        service.Load();
        service.SetShown(CodenameCategory.Account, "Personal", true);
        Assert.Contains(service.Entries(), e => e.Category == CodenameCategory.Account && e.Real == "Personal" && e.Shown);
        Assert.Contains(service.Entries(), e => e.Real == "sanduhr-meters" && e.Shown);
    }

    /// <summary>Review Focus 5, slice 6's P14 rule: after Reset, nothing writes either file back.</summary>
    [Fact]
    public void After_reset_the_service_writes_nothing()
    {
        var (service, paths, posted) = Make();
        service.Load();
        service.SetOn(true);
        File.Delete(paths.CodenamesFile);
        File.Delete(paths.StreamerStateFile);
        ResetGuard.MarkDone();
        try
        {
            StreamerMode.Mask.Name(CodenameCategory.Project, "late");
            RunPosted(posted);
            service.SetShown(CodenameCategory.Account, "x", true);
            service.Dispose();
            Assert.False(File.Exists(paths.CodenamesFile));
            Assert.False(File.Exists(paths.StreamerStateFile));
        }
        finally { ResetGuard.ClearForTests(); }
    }

    [Fact]
    public void The_widget_owns_one_and_its_dispose_flushes()
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        try
        {
            Assert.NotNull(widget.Streamer);
            Assert.False(widget.Streamer.On);
            widget.Streamer.SetOn(true);
            Assert.True(StreamerMode.On);
        }
        finally { widget.Dispose(); }
    }
}
```

`ResetGuard` (`Services/ResetGuard.cs`) has `MarkDone()` and no way back, by design: the process is exiting. Step 3 adds one test seam, `internal static void ClearForTests() => _done = false;`. `Sanduhr.App.csproj` already has `InternalsVisibleTo` for `Sanduhr.App.Tests`. The test class is in the `SettingsWindow` collection, so no other test runs while the flag is set.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~StreamerServiceTests > %TEMP%\slice7-t4-red.log 2>&1`
Expected: the build FAILS with `The type or namespace name 'StreamerService' could not be found`.

- [ ] **Step 3: The key, the static and the service**

In `windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs`, after the method

```csharp
    public void SaveUpdateCheck(bool on)
    {
        var root = Read();
        root["update_check"] = on;
        Write(root);
    }
```

add:

```csharp

    /// <summary>Settings, Privacy, Streamer mode (settings.json "streamer_mode", Settings v2 slice 7): off when absent. The tray's and the
    /// widget's "Streamer mode" items write the same key.</summary>
    public bool LoadStreamerMode() => ReadBool(Read(), "streamer_mode", false);

    public void SaveStreamerMode(bool on)
    {
        var root = Read();
        root["streamer_mode"] = on;
        Write(root);
    }
```

Create `windows-dotnet/src/Sanduhr.App/Services/StreamerMode.cs`:

```csharp
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// The mask every surface reads (Settings v2, slice 7). <see cref="StreamerService"/> is the only writer; it raises its own
/// <c>Changed</c>, so nothing subscribes to a static event. Off until the widget's service loads. Tests reset it (AppWindowFixture).
/// </summary>
public static class StreamerMode
{
    public static StreamerMask Mask { get; private set; } = StreamerMask.Off;

    public static bool On => Mask.On;

    internal static void Use(StreamerMask mask) => Mask = mask;

    internal static void Reset() => Mask = StreamerMask.Off;
}
```

Create `windows-dotnet/src/Sanduhr.App/Services/StreamerService.cs`:

```csharp
using System.Security.Cryptography;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// Streamer mode's state (Settings v2, slice 7; spec, Streamer mode). It owns:
/// <list type="bullet">
/// <item><description>the <c>streamer_mode</c> key;</description></item>
/// <item><description>the codename book in <c>%APPDATA%\Sanduhr\codenames.json</c>, saved once per burst (decision 10);</description></item>
/// <item><description><c>~\.sanduhr\streamer.json</c>, written once streamer mode has been on, and on every change after that (decision 20);</description></item>
/// <item><description>the mask the app reads (<see cref="StreamerMode"/>).</description></item>
/// </list>
/// After Reset it writes nothing (<see cref="ResetGuard"/>). The widget owns one; Settings and App listen to <see cref="Changed"/>.
/// </summary>
public sealed class StreamerService : IDisposable
{
    private readonly Paths _paths;
    private readonly SettingsStore _store;
    private readonly Func<IReadOnlyList<CcFolder>> _folders;
    private readonly Func<string> _profileDir;
    private readonly Action<Action> _post;
    private CodenameBook? _book;
    private bool _dirty;
    private bool _foldersDirty;
    private bool _flushQueued;

    /// <param name="post">Runs the coalesced save later; the widget passes its dispatcher at Background priority. Tests collect them.</param>
    public StreamerService(Paths paths, SettingsStore store, Func<IReadOnlyList<CcFolder>> folders, Func<string> profileDir,
        Action<Action>? post = null)
    {
        _paths = paths;
        _store = store;
        _folders = folders;
        _profileDir = profileDir;
        _post = post ?? (a => a());
    }

    public StreamerMask Mask { get; private set; } = StreamerMask.Off;

    public bool On => Mask.On;

    /// <summary>The switch moved, the codenames were shuffled, or a name was shown or hidden again.</summary>
    public event Action? Changed;

    /// <summary>Reads the key (and the book, when on) and puts the mask in place. Raises nothing.</summary>
    public void Load() => Apply(_store.LoadStreamerMode(), announce: false);

    public void SetOn(bool on)
    {
        if (on == On)
            return;
        _store.SaveStreamerMode(on);
        Apply(on, announce: true);
    }

    /// <summary>Shuffle codenames… (spec): a new salt, an empty map; the allowlist is kept.</summary>
    public void Shuffle()
    {
        var book = EnsureBook();
        book.Shuffle(RandomNumberGenerator.GetBytes(Codenames.SaltBytes));
        _dirty = true;
        Apply(On, announce: true);
    }

    /// <summary>"Show the real name on stream" (spec, the allowlist C): ticked, the name shows as it is while streamer mode is on.</summary>
    public void SetShown(CodenameCategory category, string real, bool shown)
    {
        var book = EnsureBook();
        lock (book) book.SetShown(category, real, shown);
        _dirty = true;
        _foldersDirty |= category == CodenameCategory.Folder;
        Flush();
        Changed?.Invoke();
    }

    /// <summary>Every mapped or shown name, for the allowlist list.</summary>
    public IReadOnlyList<CodenameEntry> Entries()
    {
        var book = EnsureBook();
        lock (book) return book.Entries();
    }

    /// <summary>Saves the book (when it changed) and streamer.json (when a folder's line changed). Nothing after Reset.</summary>
    public void Flush()
    {
        if (ResetGuard.Done)
            return;
        if (_dirty && _book is not null)
        {
            lock (_book)
                if (Codenames.Save(_book, _paths.CodenamesFile))
                    _dirty = false;
        }
        if (_foldersDirty)
        {
            WriteStreamerFile();
            _foldersDirty = false;
        }
    }

    public void Dispose()
    {
        Flush();
        Mask.Assigned -= OnAssigned;
    }

    private void Apply(bool on, bool announce)
    {
        var book = on ? EnsureBook() : _book;
        var mask = new StreamerMask(on, book, _profileDir(),
            [_profileDir(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)]);
        Mask.Assigned -= OnAssigned;
        mask.Assigned += OnAssigned;
        Mask = mask;
        StreamerMode.Use(mask);
        if (on)
        {
            // Every known folder gets its codename now, so streamer.json maps it before Claude Code renders a line (decision 8).
            foreach (var f in SafeFolders())
                mask.Folder(f.Path);
        }
        _foldersDirty = on || File.Exists(_paths.StreamerStateFile);
        Flush();
        if (announce)
            Changed?.Invoke();
    }

    private CodenameBook EnsureBook()
    {
        if (_book is null)
        {
            _book = Codenames.Load(_paths.CodenamesFile) ?? CodenameBook.New();
            _dirty |= !File.Exists(_paths.CodenamesFile);
        }
        return _book;
    }

    private void OnAssigned(CodenameCategory category)
    {
        _dirty = true;
        _foldersDirty |= category == CodenameCategory.Folder;
        if (_flushQueued)
            return;
        _flushQueued = true;
        _post(() =>
        {
            _flushQueued = false;
            Flush();
        });
    }

    private void WriteStreamerFile()
    {
        var folders = new List<StreamerFolder>();
        if (On && _book is not null)
        {
            lock (_book)
            {
                foreach (var f in SafeFolders())
                {
                    var key = StreamerMask.FolderKey(f.Path);
                    if (key.Length == 0)
                        continue;
                    folders.Add(new StreamerFolder(StreamerFile.FolderRef(f.Path),
                        _book.TryGet(CodenameCategory.Folder, key, out var code) ? code : null,
                        _book.IsShown(CodenameCategory.Folder, key)));
                }
            }
        }
        StreamerFile.Write(_paths.StreamerStateFile, StreamerFile.Build(On, folders));
    }

    private IReadOnlyList<CcFolder> SafeFolders()
    {
        try { return _folders(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }
}
```

`StreamerMask` locks its own gate around the book. `StreamerService` locks the book object itself for its own reads and writes. The two never nest, so they can't deadlock. Every call here runs on the UI thread in the app.

In `windows-dotnet/src/Sanduhr.App/Services/ResetGuard.cs`, replace

```csharp
    public static void MarkDone() => _done = true;
```

with

```csharp
    public static void MarkDone() => _done = true;

    /// <summary>Tests only: a test that marks Reset done puts the flag back in its finally (slice 7, StreamerServiceTests).</summary>
    internal static void ClearForTests() => _done = false;
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs`, replace

```csharp
        _settings = new SettingsStore(_paths);
        _ccReader = new CcLogReader(null, () => ClaudeCodeHomes.Discover(
```

with

```csharp
        _settings = new SettingsStore(_paths);
        // Streamer mode (Settings v2, slice 7): the widget owns the switch, the codename book and streamer.json; StreamerMode.Mask is set here.
        Streamer = new StreamerService(_paths, _settings,
            () => ClaudeCodeFolders.Build(ClaudeCodeProfile.Dir, ClaudeCodeProfile.ConfigDir, _settings.LoadCcFoldersAdded()),
            () => ClaudeCodeProfile.Dir,
            post: a => System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(a, System.Windows.Threading.DispatcherPriority.Background));
        Streamer.Load();
        _ccReader = new CcLogReader(null, () => ClaudeCodeHomes.Discover(
```

Add the property beside `SanduhrExternalDir` (replace

```csharp
    public string SanduhrExternalDir => _paths.ExternalDir;
```

with

```csharp
    public string SanduhrExternalDir => _paths.ExternalDir;

    /// <summary>Streamer mode (Settings v2, slice 7): the switch, the codename book and streamer.json.</summary>
    public StreamerService Streamer { get; }
```

) and in `Dispose()`, replace

```csharp
        _themeHandoff?.Dispose();
        _follower.Stop();
```

with

```csharp
        _themeHandoff?.Dispose();
        Streamer.Dispose();
        _follower.Stop();
```

In `windows-dotnet/tests/Sanduhr.App.Tests/AppWindowFixture.cs`, replace

```csharp
        WidgetCatalogService.HandlerOverride = () => new OfflineHandler();
        NoLiveMedia();
    }
```

with

```csharp
        WidgetCatalogService.HandlerOverride = () => new OfflineHandler();
        NoLiveMedia();
        // Streamer mode is process-wide (slice 7): every test starts with it off, whatever the last one left.
        StreamerMode.Reset();
    }
```

and replace

```csharp
        ClaudeCodeViewModel.ForgetSessionChoice();
        try { Directory.Delete(Root, true); } catch (IOException) { }
```

with

```csharp
        ClaudeCodeViewModel.ForgetSessionChoice();
        StreamerMode.Reset();
        try { Directory.Delete(Root, true); } catch (IOException) { }
```

(`AppWindowFixture.cs` already has `using Sanduhr.App.Services;`, since it names `ClaudeCodeProfile`; check it and add the `using` if not.)

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~StreamerServiceTests" > %TEMP%\slice7-t4-app.log 2>&1`
Then: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsKeysContractTests > %TEMP%\slice7-t4-core.log 2>&1`
Expected: PASS. `Frozen_list_is_sorted_and_unique` passes, because `streamer_mode` sorts between `statusline_enabled` and `theme`. `Every_frozen_key_is_still_read_or_written` finds `root["streamer_mode"]`.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs windows-dotnet/src/Sanduhr.App/Services/StreamerMode.cs windows-dotnet/src/Sanduhr.App/Services/StreamerService.cs windows-dotnet/src/Sanduhr.App/Services/ResetGuard.cs windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs windows-dotnet/tests/Sanduhr.Tests/SettingsKeysContractTests.cs windows-dotnet/tests/Sanduhr.App.Tests/AppWindowFixture.cs windows-dotnet/tests/Sanduhr.App.Tests/StreamerServiceTests.cs
git commit -m "feat(settings-v2): streamer_mode and the service that keeps the codename book and streamer.json"
```


---

### Task 5: The statusline prints the folder's codename while streamer mode is on

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/StatuslineScript.cs` (`Get-StreamerLabel`, the account segment, `Version = 6`, the header comment)
- Test: modify `windows-dotnet/tests/Sanduhr.Tests/StatuslineScriptRunTests.cs`

**Interfaces:**
- Consumes (Task 2): `StreamerFile.FolderRef`, `StreamerFile.Build`, `StreamerFolder`.
- Produces: the script reads `%USERPROFILE%\.sanduhr\streamer.json`. With `on: true`, the account segment's label is the folder's codename, the real label for a shown ref, or nothing. With `on: false` or no file, it is as before.

- [ ] **Step 1: Write the failing tests**

In `windows-dotnet/tests/Sanduhr.Tests/StatuslineScriptRunTests.cs`, add before the `// -- seats` comment line (`    // -- seats ----------------------------------------------------------------`):

```csharp
    // -- streamer mode (Settings v2, slice 7) ----------------------------------

    private void Streamer(bool on, params StreamerFolder[] folders) =>
        WriteSanduhr(StreamerFile.FileName, StreamerFile.Build(on, folders).ToJsonString());

    [PowerShellFact]
    public void Streamer_mode_on_prints_the_folders_codename_in_place_of_its_label()
    {
        WriteSanduhr("snapshot.json", Snapshot(42, 17));
        var folder = Path.Combine(_profile, ".claude-qxwork");
        Streamer(true, new StreamerFolder(StreamerFile.FolderRef(folder), "Dune Fox", false));
        Assert.StartsWith("Dune Fox | 5h 42% | wk 17% |", RunScript([], env: Config(folder)).Text);
        // SANDUHR_LABEL is the person's own label, and it is masked too.
        Assert.StartsWith("Dune Fox | 5h 42% |", RunScript([], env: new() { ["CLAUDE_CONFIG_DIR"] = folder, ["SANDUHR_LABEL"] = "Qxlabel" }).Text);
    }

    /// <summary>Review Focus 2: the script's ref and Core's agree however the folder is written.</summary>
    [PowerShellFact]
    public void A_config_dir_written_another_way_still_finds_its_codename()
    {
        WriteSanduhr("snapshot.json", Snapshot(42, 17));
        var folder = Path.Combine(_profile, ".claude-qxwork");
        Streamer(true, new StreamerFolder(StreamerFile.FolderRef(folder), "Dune Fox", false));
        Assert.StartsWith("Dune Fox | 5h 42% |", RunScript([], env: Config(folder.ToUpperInvariant() + "\\")).Text);
    }

    [PowerShellFact]
    public void Streamer_mode_on_with_the_folder_unmapped_prints_no_label()
    {
        WriteSanduhr("snapshot.json", Snapshot(42, 17));
        Streamer(true);
        var run = RunScript([], env: Config(Path.Combine(_profile, ".claude-qxwork")));
        Assert.StartsWith("5h 42% | wk 17% |", run.Text);
        Assert.DoesNotContain("qxwork", run.Text);
    }

    [PowerShellFact]
    public void A_shown_folder_keeps_its_real_label()
    {
        WriteSanduhr("snapshot.json", Snapshot(42, 17));
        var folder = Path.Combine(_profile, ".claude-qxwork");
        Streamer(true, new StreamerFolder(StreamerFile.FolderRef(folder), null, true));
        Assert.StartsWith("qxwork | 5h 42% |", RunScript([], env: Config(folder)).Text);
    }

    /// <summary>Decision 4: streamer mode replaces a label, it never adds one. Plain .claude stays bare.</summary>
    [PowerShellFact]
    public void Streamer_mode_never_adds_a_label_where_none_would_print()
    {
        WriteSanduhr("snapshot.json", Snapshot(42, 17));
        var plain = Path.Combine(_profile, ".claude");
        Streamer(true, new StreamerFolder(StreamerFile.FolderRef(plain), "Mesa Owl", false));
        Assert.StartsWith("5h 42% | wk 17% |", RunScript([], env: Config(plain)).Text);
        Assert.StartsWith("5h 42% | wk 17% |", RunScript([]).Text);
    }

    [PowerShellFact]
    public void Streamer_mode_off_or_no_file_prints_the_label_as_before()
    {
        WriteSanduhr("snapshot.json", Snapshot(42, 17));
        var folder = Path.Combine(_profile, ".claude-qxwork");
        Assert.StartsWith("qxwork | 5h 42% |", RunScript([], env: Config(folder)).Text);
        Streamer(false, new StreamerFolder(StreamerFile.FolderRef(folder), "Dune Fox", false));
        Assert.StartsWith("qxwork | 5h 42% |", RunScript([], env: Config(folder)).Text);
    }

    /// <summary>Fail closed (decision 4): a file that is there but can't be trusted prints no label, never the real one.</summary>
    [PowerShellFact]
    public void An_unreadable_streamer_file_prints_no_label()
    {
        WriteSanduhr("snapshot.json", Snapshot(42, 17));
        var folder = Path.Combine(_profile, ".claude-qxwork");
        foreach (var bad in new[] { "{ not json", """{"schema_version":2,"on":true}""", """{"schema_version":1,"on":"true"}""", new string(' ', StreamerFile.MaxBytes + 1) })
        {
            WriteSanduhr(StreamerFile.FileName, bad);
            var run = RunScript([], env: Config(folder));
            Assert.StartsWith("5h 42% | wk 17% |", run.Text);
        }
    }

    [PowerShellFact]
    public void Reading_streamer_mode_writes_nothing_to_the_sanduhr_folder()
    {
        WriteSanduhr("snapshot.json", Snapshot(42, 17));
        Streamer(true);
        RunScript([], env: Config(Path.Combine(_profile, ".claude-qxwork")));
        Assert.Equal(["snapshot.json", StreamerFile.FileName],
            Directory.GetFileSystemEntries(_sanduhr).Select(p => Path.GetFileName(p)).Order(StringComparer.Ordinal));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~StatuslineScriptRunTests > %TEMP%\slice7-t5-red.log 2>&1`
Expected: a behavioural RED. It builds, and these fail with the real label printed. For example, `Streamer_mode_on_prints_the_folders_codename_in_place_of_its_label` reports `Assert.StartsWith() Failure … Expected: Dune Fox | 5h 42% | wk 17% | … Actual: qxwork | 5h 42% …`. `Streamer_mode_off_or_no_file_prints_the_label_as_before`, `Streamer_mode_never_adds_a_label_where_none_would_print` and `Reading_streamer_mode_writes_nothing_to_the_sanduhr_folder` pass already. Record which failed.

- [ ] **Step 3: The script**

In `windows-dotnet/src/Sanduhr.Core/StatuslineScript.cs`, replace

```csharp
    public const int Version = 5;
```

with

```csharp
    public const int Version = 6;
```

Replace

```text
# name (.claude-personal reads personal; plain .claude has none), else no label.
param(
```

with

```text
# name (.claude-personal reads personal; plain .claude has none), else no label.
# Streamer mode (Settings, Privacy) puts the folder's codename in its place, from
# %USERPROFILE%\.sanduhr\streamer.json (folder ref -> codename, never a real name). It never adds
# a label where none would print, and a file it can't trust prints no label at all.
param(
```

Replace

```text
# ---- Sanduhr's own line (null = nothing of Sanduhr's to print) -----------------
function Get-OurLine {
```

with

```text
# ---- streamer mode: the folder's codename in place of its label -----------------
# streamer.json is written by the widget once streamer mode has been on. Missing: as before.
# On: the codename for this folder's ref, the label itself for a shown ref, else nothing.
# The ref is the first 4 bytes of SHA-256 over the folder's full path, lower-cased (StreamerFile.FolderRef).
function Get-StreamerLabel([string]$label) {
  $path = Join-Path $env:USERPROFILE '.sanduhr\streamer.json'
  $info = New-Object System.IO.FileInfo($path)
  if (-not $info.Exists) { return $label }
  if ($info.Length -gt 65536) { return $null }
  try { $s = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json -ErrorAction Stop } catch { return $null }
  if ($null -eq $s -or $null -eq $s.schema_version) { return $null }
  if (-not ($s.schema_version -is [int] -or $s.schema_version -is [long] -or $s.schema_version -is [double])) { return $null }
  if ([double]$s.schema_version -ne 1) { return $null }
  if (-not ($s.on -is [bool])) { return $null }
  if (-not $s.on) { return $label }
  $profileDir = $env:USERPROFILE
  $config = $env:CLAUDE_CONFIG_DIR
  if ([string]::IsNullOrEmpty($config)) { $config = Join-Path $profileDir '.claude' }
  $folder = Get-FolderKey $config $profileDir
  if ($null -eq $folder) { return $null }
  $sha = [System.Security.Cryptography.SHA256]::Create()
  try { $hash = $sha.ComputeHash($utf8.GetBytes($folder.ToLowerInvariant())) } finally { $sha.Dispose() }
  $ref = (($hash[0..3] | ForEach-Object { $_.ToString('x2') }) -join '')
  if (@($s.shown) -contains $ref) { return $label }
  if ($null -eq $s.folders) { return $null }
  $code = $s.folders.$ref
  if ($code -is [string]) { return (Clean-Label $code) }
  return $null
}

# ---- Sanduhr's own line (null = nothing of Sanduhr's to print) -----------------
function Get-OurLine {
```

Replace

```text
    $folderLabel = Get-FolderLabel
    if ($folderLabel) { $line = $folderLabel + ' | ' + $line }   # the label may be non-ASCII; Emit writes UTF-8
```

with

```text
    $folderLabel = Get-FolderLabel
    if ($folderLabel) { $folderLabel = Get-StreamerLabel $folderLabel }   # streamer mode: the codename, or nothing
    if ($folderLabel) { $line = $folderLabel + ' | ' + $line }   # the label may be non-ASCII; Emit writes UTF-8
```

These lines sit inside a C# raw string literal, so no escaping changes. Keep the script body ASCII (the header comment of `Content` says Sanduhr's own text stays ASCII); every line above is.

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~StatuslineScriptRunTests|FullyQualifiedName~StatuslineInstallerTests|FullyQualifiedName~SnapshotConsumersTests" > %TEMP%\slice7-t5.log 2>&1`
Expected: PASS, the earlier run tests included. If a pin on `StatuslineScript.Version` or on the script's contents fails (a test that compares the body, or the version, to a constant), update that pin to the new value and say so in the report. The installer re-writes the script on the next app start, which is how the widget acts as the script's update channel (`App.xaml.cs:200-207`).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/StatuslineScript.cs windows-dotnet/tests/Sanduhr.Tests/StatuslineScriptRunTests.cs
git commit -m "feat(settings-v2): the statusline prints the folder's codename in streamer mode, and nothing it can't trust"
```

(Add any pin file Step 4 needed.)

---

### Task 6: Privacy, Streamer mode: the switch, Shuffle, the allowlist, the banner on every masking page, the index and the jump links

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/StreamerSettingsViewModel.cs`, `windows-dotnet/src/Sanduhr.App/Views/StreamerBanner.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/PrivacyViewModel.cs` (`Streamer`)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (builds it; `ReloadNames`)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs` (`NamesChanged`, the owner, `Closed`)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/PrivacyPage.xaml` (the seat)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`, `ClaudeCodePage.xaml`, `ModsPage.xaml`, `WatchersPage.xaml` (banners)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (two entries, `StreamerPages`, Privacy's jump links, `NameAnchors`, the page synonyms)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (two `Table` rows)
- Modify: `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice6IndexTests.cs`, `windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs`
- Test: create `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice7IndexTests.cs`, `windows-dotnet/tests/Sanduhr.App.Tests/StreamerPrivacyTests.cs`

**Interfaces:**
- Consumes (Tasks 3, 4): the slice 7 names, `SettingsIndex.Streamer`, `StreamerAllowlist`, `StreamerService`, `WidgetViewModel.Streamer`.
- Produces:
  - `public sealed partial class CodenameRow : ObservableObject` (`Category`, `Real`, `Codename`, `Text`, `Name`, `CategoryTitle`, `StartsCategory`, `Shown`)
  - `public sealed partial class StreamerSettingsViewModel : ObservableObject`, with:
    - `StreamerSettingsViewModel(StreamerService service, Func<string, string>? showFolder = null)`
    - `bool On` (two-way), `ObservableCollection<CodenameRow> Rows`, `bool HasRows`, `bool NoRows`, `string AllowlistCaption`
    - `IRelayCommand ShuffleCommand`, `Action? NamesChanged`, `void AttachOwner(Window)`, `void Read()`, `void Detach()`
    - `internal Func<bool>? ConfirmShuffleHook`
  - `PrivacyViewModel.Streamer` (`StreamerSettingsViewModel?`, set by Settings)
  - `SettingsViewModel.ReloadNames()`, `internal int NamesReloaded`
  - `public sealed class StreamerBanner : Border`
  - `SettingsIndex.StreamerPages`, and Privacy's `JumpLinks` arm

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice7IndexTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 7: Streamer mode's anchors, its search words, Privacy's four sections and jump links.</summary>
public class SettingsSlice7IndexTests
{
    private static SettingsIndex.Hit Top(string q)
    {
        var hits = SettingsIndex.Search(q);
        Assert.NotEmpty(hits);
        return hits[0];
    }

    [Fact]
    public void Privacy_holds_four_sections_in_the_carrys_order_with_jump_links()
    {
        Assert.Equal([SettingsIndex.Streamer, SettingsIndex.StreamerAllowlist, SettingsIndex.Network, SettingsIndex.Agents],
            SettingsIndex.EntriesOn(SettingsPage.Privacy).Select(e => e.Anchor).Distinct());
        Assert.Equal([SettingsIndex.Streamer, SettingsIndex.StreamerAllowlist, SettingsIndex.Network, SettingsIndex.Agents],
            SettingsIndex.JumpLinks(SettingsPage.Privacy).Select(l => l.Anchor));
        Assert.Equal([SettingsNames.StreamerSection, SettingsNames.ShowRealNameSection, SettingsNames.WhatLeavesThisPc, SettingsNames.WhatClaudeCanChange],
            SettingsIndex.JumpLinks(SettingsPage.Privacy).Select(l => l.Title));
    }

    [Theory]
    [InlineData("stream")]
    [InlineData("streamer")]
    [InlineData("screen share")]
    [InlineData("hide names")]
    [InlineData("obs")]
    [InlineData("demo")]
    [InlineData("streamer mode")]
    public void Streaming_words_land_on_streamer_mode_first(string query) =>
        Assert.Equal((SettingsPage.Privacy, SettingsIndex.Streamer), (Top(query).Entry.Page, Top(query).Entry.Anchor));

    [Theory]
    [InlineData("codename")]
    [InlineData("codenames")]
    public void Codename_lands_on_the_allowlist_first(string query) =>
        Assert.Equal((SettingsPage.Privacy, SettingsIndex.StreamerAllowlist), (Top(query).Entry.Page, Top(query).Entry.Anchor));

    /// <summary>Slice 6's P2 lesson: the new entries take no word another page owns.</summary>
    [Theory]
    [InlineData("record")]
    [InlineData("hide")]
    [InlineData("account")]
    [InlineData("folder")]
    [InlineData("share")]
    [InlineData("names")]
    public void Words_other_pages_own_do_not_land_on_streamer_mode(string query)
    {
        var top = Top(query).Entry;
        Assert.False(top.Page == SettingsPage.Privacy && (top.Anchor == SettingsIndex.Streamer || top.Anchor == SettingsIndex.StreamerAllowlist),
            $"\"{query}\" lands on {top.Page}#{top.Anchor}");
    }

    [Fact]
    public void Shuffle_finds_the_switchs_section()
    {
        Assert.Equal((SettingsPage.Privacy, SettingsIndex.Streamer), (Top("shuffle").Entry.Page, Top("shuffle").Entry.Anchor));
    }

    [Fact]
    public void The_switch_and_shuffle_are_in_the_names_table_on_Privacy()
    {
        Assert.Contains((SettingsNames.StreamerSection, SettingsPage.Privacy), SettingsNames.Table);
        Assert.Contains((SettingsNames.ShuffleCodenames, SettingsPage.Privacy), SettingsNames.Table);
        Assert.Equal(SettingsIndex.Streamer, SettingsIndex.NameAnchors[SettingsNames.StreamerSection]);
        Assert.Equal(SettingsIndex.Streamer, SettingsIndex.NameAnchors[SettingsNames.ShuffleCodenames]);
    }

    [Fact]
    public void The_masking_pages_are_the_ten_the_reach_inventory_names()
    {
        Assert.Equal(
            [SettingsPage.Accounts, SettingsPage.Privacy, SettingsPage.ClaudeUsage, SettingsPage.Publish, SettingsPage.Desk, SettingsPage.Weather,
             SettingsPage.Watchers, SettingsPage.ClaudeCode, SettingsPage.Mods, SettingsPage.About],
            SettingsIndex.StreamerPages);
    }
}
```

In `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice6IndexTests.cs`, replace

```csharp
    [Fact]
    public void Privacy_holds_two_sections_and_no_jump_links()
    {
        Assert.Equal([SettingsIndex.Network, SettingsIndex.Agents], SettingsIndex.EntriesOn(SettingsPage.Privacy).Select(e => e.Anchor));
        Assert.Equal([SettingsNames.WhatLeavesThisPc, SettingsNames.WhatClaudeCanChange],
            SettingsIndex.EntriesOn(SettingsPage.Privacy).Select(e => e.Title));
        Assert.Empty(SettingsIndex.JumpLinks(SettingsPage.Privacy));   // two sections (decision 12); slice 7 adds the third
    }
```

with

```csharp
    [Fact]
    public void Privacy_keeps_its_two_slice_6_sections_after_streamer_mode()
    {
        // Slice 7 put Streamer mode and Show the real name first; slice 6's two follow, in order (SettingsSlice7IndexTests pins the four).
        Assert.Equal([SettingsIndex.Network, SettingsIndex.Agents],
            SettingsIndex.EntriesOn(SettingsPage.Privacy).Select(e => e.Anchor).Distinct().Skip(2));
    }
```

In `windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs`, replace

```csharp
    [InlineData(SettingsPage.About)]
    public void Every_jump_link_lands_on_its_section(SettingsPage page) => AppHost.Run(() =>
```

with

```csharp
    [InlineData(SettingsPage.About)]
    [InlineData(SettingsPage.Privacy)]
    public void Every_jump_link_lands_on_its_section(SettingsPage page) => AppHost.Run(() =>
```

Create `windows-dotnet/tests/Sanduhr.App.Tests/StreamerPrivacyTests.cs`:

```csharp
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Services;
using Sanduhr.App.Updates;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 7: Privacy, Streamer mode; the allowlist; the banner on every masking page; the window's one new subscription.</summary>
[Collection("SettingsWindow")]
public sealed class StreamerPrivacyTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Svm, WidgetViewModel Widget, NoActivate Guard) Open()
    {
        var (widget, _) = ScratchWidget("Qxalpha", "Qxbeta");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
            checkForUpdatesNow: () => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate)), isStoreBuild: false);
        var window = new SettingsWindow(svm);
        return (window, svm, widget, NoActivate.Show(window));
    }

    private static void Settle(SettingsWindow window, NoActivate guard)
    {
        window.UpdateLayout();
        Pump();
        window.UpdateLayout();
        Pump();
        guard.Check();
    }

    private static IEnumerable<T> Below<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Below<T>(child)) yield return deeper;
        }
    }

    private static string Spoken(UIElement e) => UIElementAutomationPeer.CreatePeerForElement(e)?.GetName() ?? "";

    private static TabItem Tab(SettingsWindow window, SettingsPage page) =>
        ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == page.ToString());

    [Fact]
    public void The_switch_on_Privacy_turns_streamer_mode_on_and_off() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            window.SelectPage(SettingsPage.Privacy, SettingsIndex.Streamer);
            Settle(window, guard);
            var box = Below<CheckBox>(window).Single(c => Spoken(c) == SettingsNames.StreamerSwitch);
            box.IsChecked = true;
            Assert.True(widget.Streamer.On);
            Assert.True(StreamerMode.On);
            box.IsChecked = false;
            Assert.False(widget.Streamer.On);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void Turning_it_on_from_the_tray_ticks_the_open_switch_and_reloads_the_names() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            window.SelectPage(SettingsPage.Privacy, SettingsIndex.Streamer);
            Settle(window, guard);
            int before = svm.NamesReloaded;
            widget.Streamer.SetOn(true);   // the tray's item writes the service, as the switch does
            Settle(window, guard);
            Assert.True(Below<CheckBox>(window).Single(c => Spoken(c) == SettingsNames.StreamerSwitch).IsChecked);
            Assert.Equal(before + 1, svm.NamesReloaded);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    /// <summary>Decision 15: the window's one subscription to the long-lived service is gone after Close.</summary>
    [Fact]
    public void Closing_settings_detaches_it_from_the_service() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            Settle(window, guard);
            int heard = 0;
            svm.Privacy.Streamer!.NamesChanged += () => heard++;
            window.Close();
            int reloaded = svm.NamesReloaded;
            widget.Streamer.SetOn(true);
            Assert.Equal(0, heard);
            Assert.Equal(reloaded, svm.NamesReloaded);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Shuffle_asks_first_and_cancel_changes_nothing() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            widget.Streamer.SetOn(true);
            var code = StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
            var streamer = svm.Privacy.Streamer!;
            streamer.ConfirmShuffleHook = () => false;
            streamer.ShuffleCommand.Execute(null);
            Assert.Equal(code, StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"));
            streamer.ConfirmShuffleHook = () => true;
            streamer.ShuffleCommand.Execute(null);
            Assert.NotEqual(code, StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"));
        }
        finally { window.Close(); widget.Dispose(); }
    });

    /// <summary>Spec, the allowlist: while on, the list shows codenames only; off, each codename beside its real name.</summary>
    [Fact]
    public void The_allowlist_shows_codenames_only_while_on() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            widget.Streamer.SetOn(true);
            var code = StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
            var streamer = svm.Privacy.Streamer!;
            streamer.Read();
            var row = streamer.Rows.Single(r => r.Category == CodenameCategory.Account && r.Real == "Qxalpha");
            Assert.Equal(code, row.Text);
            Assert.Equal(SettingsNames.AllowlistRowName(code), row.Name);
            Assert.All(streamer.Rows, r => Assert.DoesNotContain("qx", r.Text + r.Name, StringComparison.OrdinalIgnoreCase));

            widget.Streamer.SetOn(false);
            row = streamer.Rows.Single(r => r.Category == CodenameCategory.Account && r.Real == "Qxalpha");
            Assert.Equal(SettingsNames.AllowlistRowOff(code, "Qxalpha"), row.Text);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void Ticking_a_row_shows_that_name_and_keeps_the_row() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            widget.Streamer.SetOn(true);
            StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
            var streamer = svm.Privacy.Streamer!;
            streamer.Read();
            var row = streamer.Rows.Single(r => r.Real == "Qxalpha");
            row.Shown = true;
            Assert.Equal("Qxalpha", StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"));
            Assert.Same(row, streamer.Rows.Single(r => r.Real == "Qxalpha"));   // updated in place: the focus stays on it
            row.Shown = false;
            Assert.NotEqual("Qxalpha", StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"));
        }
        finally { window.Close(); widget.Dispose(); }
    });

    /// <summary>Decision 14: the banner shows on every masking page while on, and on none while off; the set is SettingsIndex.StreamerPages.</summary>
    [Fact]
    public void The_banner_shows_on_every_masking_page_and_only_while_on() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            var shown = new List<SettingsPage>();
            foreach (var page in SettingsNames.Pages)
            {
                window.SelectPage(page, null);
                Settle(window, guard);
                Assert.DoesNotContain(Below<StreamerBanner>((DependencyObject)Tab(window, page).Content), b => b.IsVisible);
            }
            widget.Streamer.SetOn(true);
            foreach (var page in SettingsNames.Pages)
            {
                window.SelectPage(page, null);
                Settle(window, guard);
                if (Below<StreamerBanner>((DependencyObject)Tab(window, page).Content).Any(b => b.IsVisible))
                    shown.Add(page);
            }
            Assert.Equal(SettingsIndex.StreamerPages.Order(), shown.Order());
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void The_banners_link_lands_on_streamer_mode_and_is_spoken_with_it() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            widget.Streamer.SetOn(true);
            window.SelectPage(SettingsPage.Accounts, null);
            Settle(window, guard);
            var banner = Below<StreamerBanner>((DependencyObject)Tab(window, SettingsPage.Accounts).Content).Single(b => b.IsVisible);
            var link = Below<Button>(banner).Single();
            Assert.Equal(SettingsNames.StreamerBannerLinkName, Spoken(link));
            link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Privacy, SettingsIndex.Streamer), window.Place);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    /// <summary>The carry's order on Privacy: banner, jump links, Streamer mode, then What leaves this PC.</summary>
    [Fact]
    public void Privacy_puts_the_banner_then_the_jump_links_then_streamer_mode_first() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            widget.Streamer.SetOn(true);
            window.SelectPage(SettingsPage.Privacy, null);
            Settle(window, guard);
            var body = (Panel)((FrameworkElement)Tab(window, SettingsPage.Privacy).Content).FindName("PageBody");
            var kids = body.Children.OfType<FrameworkElement>().ToList();
            int banner = kids.FindIndex(k => k is StreamerBanner);
            int jumps = kids.FindIndex(k => k is SectionJumpLinks);
            int streamer = kids.FindIndex(k => SettingsAnchor.GetId(k) == SettingsIndex.Streamer);
            int network = kids.FindIndex(k => SettingsAnchor.GetId(k) == SettingsIndex.Network);
            Assert.True(banner >= 0 && banner < jumps && jumps < streamer && streamer < network, $"{banner} {jumps} {streamer} {network}");
        }
        finally { window.Close(); widget.Dispose(); }
    });
}
```

If `SettingsAnchor` has no public `GetId`, Read `Views/SettingsAnchor` (it is the attached property `views:SettingsAnchor.Id`) and use its getter's real name. If `SettingsPage.Privacy`'s content is not a `FrameworkElement` holding `PageBody` (the PrivacyPage UserControl is), take the `PrivacyPage` from `Below<PrivacyPage>`.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSlice7IndexTests|FullyQualifiedName~SettingsSlice6IndexTests" > %TEMP%\slice7-t6-red-core.log 2>&1`
Expected: a behavioural RED for the index. It builds, and `Privacy_holds_four_sections…`, `Streaming_words…` and `Codename_lands…` fail: Privacy has two anchors, and the boosts find no `streamer` entry. `StreamerPages` doesn't exist yet, so add it first as `public static IReadOnlyList<SettingsPage> StreamerPages { get; } = [];` to see `The_masking_pages…` fail on content, not on compile.
Then: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~StreamerPrivacyTests > %TEMP%\slice7-t6-red-app.log 2>&1`
Expected: the build FAILS on `StreamerBanner` and `Privacy.Streamer`. These are new controls, so a compile error is their RED.

- [ ] **Step 3: The index**

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`, replace

```csharp
        // P2: "endpoint", "upload", "GitHub", "telemetry", "Microsoft Store" and "propose" stay with the pages that own those controls.
        E(SettingsNames.WhatLeavesThisPc, SettingsPage.Privacy, Network,
```

with

```csharp
        // Streamer mode (slice 7): the spec's words are Boosts (stream, streamer, screen share, hide names, obs, demo, codename). The entries
        // take only words no other page owns: "recording" would take "record" from Shortcuts, "hide" from Alerts (decision 17).
        E(SettingsNames.StreamerSection, SettingsPage.Privacy, Streamer,
            ["streaming", "share my screen", "presenting", "codenames", "shuffle", "demo mode"]),
        E(SettingsNames.ShowRealNameSection, SettingsPage.Privacy, StreamerAllowlist,
            ["real name", "real names", "allowlist", "show on stream", "unmask"]),

        // P2: "endpoint", "upload", "GitHub", "telemetry", "Microsoft Store" and "propose" stay with the pages that own those controls.
        E(SettingsNames.WhatLeavesThisPc, SettingsPage.Privacy, Network,
```

The page's own synonyms stay as they are: "streamer mode" is the section's title, so it lands on the section, not the page row.

In `NameAnchors`, replace

```csharp
        [SettingsNames.TrayShows] = Tray,
```

with

```csharp
        [SettingsNames.TrayShows] = Tray,
        [SettingsNames.StreamerSection] = Streamer,
        [SettingsNames.ShuffleCodenames] = Streamer,
```

In `JumpLinks`, replace

```csharp
        SettingsPage.Publish => Links(page, Destination, Token, Homes, Schedule, Sample),
```

with

```csharp
        SettingsPage.Publish => Links(page, Destination, Token, Homes, Schedule, Sample),
        // Privacy (slice 7): four sections once Streamer mode joins (slice 6 carry); the labels are the index titles.
        SettingsPage.Privacy => Links(page, Streamer, StreamerAllowlist, Network, Agents),
```

And replace

```csharp
    /// <summary>Where an opening Settings lands: the place asked for; else where it was left this session; else General.</summary>
```

with

```csharp
    /// <summary>The pages that mask something while streamer mode is on, each with a <c>StreamerBanner</c> under its first line (slice 7
    /// decision 14; the reach inventory). A test holds the XAML to this list.</summary>
    public static IReadOnlyList<SettingsPage> StreamerPages { get; } =
    [
        SettingsPage.Accounts, SettingsPage.Privacy, SettingsPage.ClaudeUsage, SettingsPage.Publish, SettingsPage.Desk, SettingsPage.Weather,
        SettingsPage.Watchers, SettingsPage.ClaudeCode, SettingsPage.Mods, SettingsPage.About,
    ];

    /// <summary>Where an opening Settings lands: the place asked for; else where it was left this session; else General.</summary>
```

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, in `Table`, replace

```csharp
        (TrayShows, SettingsPage.General),
        (ShortcutToOpenSettings, SettingsPage.General),
```

with

```csharp
        (TrayShows, SettingsPage.General),
        (ShortcutToOpenSettings, SettingsPage.General),
        (StreamerSection, SettingsPage.Privacy),
        (ShuffleCodenames, SettingsPage.Privacy),
```

- [ ] **Step 4: The view model**

Create `windows-dotnet/src/Sanduhr.App/ViewModels/StreamerSettingsViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One row of Show the real name: a mapped or shown name, as the list may show it now (spec, the allowlist C).</summary>
public sealed partial class CodenameRow : ObservableObject
{
    private readonly StreamerService _service;
    private bool _loading;

    public CodenameCategory Category { get; }
    /// <summary>The book's key: a label, a folder's lower-cased full path, a project's name.</summary>
    public string Real { get; }
    public string CategoryTitle => SettingsNames.CodenameCategoryTitle(Category);

    [ObservableProperty] private string _codename = "";
    /// <summary>On: the codename alone. Off: "codename · real name", or the real name alone when it never had a codename.</summary>
    [ObservableProperty] private string _text = "";
    /// <summary>The checkbox's spoken name: "Show the real name on stream, Dune Fox".</summary>
    [ObservableProperty] private string _name = "";
    /// <summary>The first row of its category: its heading shows above it.</summary>
    [ObservableProperty] private bool _startsCategory;
    [ObservableProperty] private bool _shown;

    internal CodenameRow(StreamerService service, CodenameEntry entry)
    {
        _service = service;
        Category = entry.Category;
        Real = entry.Real;
    }

    internal void Update(CodenameEntry entry, bool on, string realShown, bool startsCategory)
    {
        _loading = true;
        Codename = entry.Codename ?? "";
        Text = on ? Codename : entry.Codename is { } code ? SettingsNames.AllowlistRowOff(code, realShown) : realShown;
        Name = SettingsNames.AllowlistRowName(on ? Codename : Text);
        StartsCategory = startsCategory;
        Shown = entry.Shown;
        _loading = false;
    }

    partial void OnShownChanged(bool value)
    {
        if (!_loading)
            _service.SetShown(Category, Real, value);
    }

    public override string ToString() => Name;
}

/// <summary>
/// Settings, Privacy, Streamer mode and Show the real name (Settings v2, slice 7; spec, Streamer mode). The switch writes the widget's
/// service, as the tray's and the widget's items do. The window's one subscription to something that outlives it: <see cref="Detach"/>
/// on Closed (decision 15). <see cref="NamesChanged"/> tells Settings to build every masked page's text again.
/// </summary>
public sealed partial class StreamerSettingsViewModel : ObservableObject
{
    private readonly StreamerService _service;
    private readonly Func<string, string> _showFolder;
    private Window? _owner;

    /// <param name="showFolder">How a folder key shows while off (<c>~\.claude-work</c>); <see cref="ClaudeCodeFolders.Display"/> under the
    /// Claude Code pages' profile folder when null.</param>
    public StreamerSettingsViewModel(StreamerService service, Func<string, string>? showFolder = null)
    {
        _service = service;
        _showFolder = showFolder ?? (key => ClaudeCodeFolders.Display(key, ClaudeCodeProfile.Dir));
        _service.Changed += OnChanged;
        Read();
    }

    public bool On
    {
        get => _service.On;
        set
        {
            if (value != _service.On)
                _service.SetOn(value);
        }
    }

    public ObservableCollection<CodenameRow> Rows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoRows))]
    private bool _hasRows;

    public bool NoRows => !HasRows;

    public string AllowlistCaption => On ? SettingsNames.AllowlistCaptionOn : SettingsNames.AllowlistCaptionOff;

    /// <summary>Set by Settings: every masked page builds its text again.</summary>
    public Action? NamesChanged { get; set; }

    /// <summary>Tests answer Shuffle's question here.</summary>
    internal Func<bool>? ConfirmShuffleHook { get; set; }

    public void AttachOwner(Window owner) => _owner = owner;

    [RelayCommand]
    private void Shuffle()
    {
        bool yes = ConfirmShuffleHook is { } hook
            ? hook()
            : ThemedDialog.ShowConfirm(_owner, SettingsNames.ShuffleQuestion, SettingsNames.ShuffleDetail,
                SettingsNames.ShuffleConfirm, SettingsNames.ShuffleCancel);
        if (yes)
            _service.Shuffle();
    }

    /// <summary>Reads the book again. Rows are updated in place by (category, name), so a ticked row keeps its focus.</summary>
    public void Read()
    {
        bool on = _service.On;
        var entries = _service.Entries();
        var keep = new HashSet<(CodenameCategory, string)>(entries.Select(e => (e.Category, e.Real)));
        for (int i = Rows.Count - 1; i >= 0; i--)
            if (!keep.Contains((Rows[i].Category, Rows[i].Real)))
                Rows.RemoveAt(i);
        CodenameCategory? last = null;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            int at = IndexOf(e);
            CodenameRow row;
            if (at < 0)
            {
                row = new CodenameRow(_service, e);
                Rows.Insert(Math.Min(i, Rows.Count), row);
            }
            else
            {
                row = Rows[at];
                if (at != i && i < Rows.Count)
                    Rows.Move(at, i);
            }
            row.Update(e, on, e.Category == CodenameCategory.Folder ? _showFolder(e.Real) : e.Real, last != e.Category);
            last = e.Category;
        }
        HasRows = Rows.Count > 0;
    }

    public void Detach() => _service.Changed -= OnChanged;

    private int IndexOf(CodenameEntry e)
    {
        for (int i = 0; i < Rows.Count; i++)
            if (Rows[i].Category == e.Category && Rows[i].Real == e.Real)
                return i;
        return -1;
    }

    private void OnChanged()
    {
        OnPropertyChanged(nameof(On));
        OnPropertyChanged(nameof(AllowlistCaption));
        Read();
        NamesChanged?.Invoke();
    }
}
```

While on, the list must show codenames only, so every entry needs a codename then, including a shown name that was never masked. In `StreamerService.Entries()` (Task 4), replace

```csharp
        var book = EnsureBook();
        lock (book) return book.Entries();
```

with

```csharp
        var book = EnsureBook();
        lock (book)
        {
            // While on, the list shows codenames only (spec): a shown name that never needed one gets one now.
            if (On)
            {
                foreach (var e in book.Entries().Where(e => e.Codename is null))
                {
                    book.Assign(e.Category, e.Real);
                    _dirty = true;
                }
            }
            return book.Entries();
        }
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/PrivacyViewModel.cs`, replace

```csharp
    /// <summary>What leaves this PC, in the page's order.</summary>
    public IReadOnlyList<NetworkRow> Network => NetworkDisclosure.All;
```

with

```csharp
    /// <summary>Streamer mode and Show the real name (slice 7), set by Settings; null in tests that build Privacy alone.</summary>
    public StreamerSettingsViewModel? Streamer { get; internal set; }

    /// <summary>What leaves this PC, in the page's order.</summary>
    public IReadOnlyList<NetworkRow> Network => NetworkDisclosure.All;
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`, replace

```csharp
        Privacy = new PrivacyViewModel(privacyStore, ClaudeCodeHost.For(widget), widget.LoadMcpRoots,
            publishOn: () => privacyStore.LoadPublishSettings().Enabled);
```

with

```csharp
        Privacy = new PrivacyViewModel(privacyStore, ClaudeCodeHost.For(widget), widget.LoadMcpRoots,
            publishOn: () => privacyStore.LoadPublishSettings().Enabled);
        // Streamer mode (slice 7): the widget's service; the window detaches it on Closed (decision 15).
        Privacy.Streamer = new StreamerSettingsViewModel(widget.Streamer) { NamesChanged = ReloadNames };
```

and add after the constructor's closing brace (Read the file and place it after the constructor, before the next member):

```csharp

    /// <summary>
    /// Streamer mode moved, the codenames were shuffled, or a name was shown or hidden (slice 7 decision 15): every page that masks a name
    /// builds its text again, so nothing on an open page keeps a real name or an old codename. Tasks 9 to 12 add their pages here.
    /// </summary>
    public void ReloadNames()
    {
        Accounts.Reload();
        ClaudeCodePage.Reload();
        Privacy.Reload();
        NamesReloaded++;
    }

    /// <summary>Tests count the reloads.</summary>
    internal int NamesReloaded { get; private set; }
```

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs`, replace

```csharp
        Closed += (_, _) =>
        {
            ViewModel.Desk.Flush();
```

with

```csharp
        ViewModel.Privacy.Streamer?.AttachOwner(this);
        Closed += (_, _) =>
        {
            // The one subscription to the widget's long-lived streamer service (slice 7 decision 15).
            ViewModel.Privacy.Streamer?.Detach();
            ViewModel.Desk.Flush();
```

- [ ] **Step 5: The banner**

Create `windows-dotnet/src/Sanduhr.App/Views/StreamerBanner.cs`:

```csharp
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// "Streamer mode is on: names are codenames. Privacy settings…" (spec, Streamer mode, While on), under the first line of every page that
/// masks something (<see cref="SettingsIndex.StreamerPages"/>). It shows while the window's <c>Privacy.Streamer.On</c> is true, through a
/// binding to the window's DataContext, so it subscribes to nothing. Built in code, like <see cref="SectionJumpLinks"/>, so the own-file pages
/// need no converter of their own.
/// </summary>
public sealed class StreamerBanner : Border
{
    public StreamerBanner()
    {
        Margin = new Thickness(0, 0, 0, 12);
        Padding = new Thickness(10, 6, 10, 6);
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "Sanduhr.Brush.Bg");
        SetResourceReference(BorderBrushProperty, "Sanduhr.Brush.Accent");

        var row = new WrapPanel();
        var text = new TextBlock
        {
            Text = SettingsNames.StreamerBanner,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Sanduhr.Brush.Text");
        var link = new Button { Content = SettingsNames.LinkTitle(SettingsPage.Privacy) };
        link.SetResourceReference(StyleProperty, "SettingsLinkButton");
        SettingsLinkTo.SetLink(link, SettingsIndex.PageId(SettingsPage.Privacy) + "#" + SettingsIndex.Streamer);
        AutomationProperties.SetName(link, SettingsNames.StreamerBannerLinkName);
        row.Children.Add(text);
        row.Children.Add(link);
        Child = row;

        Visibility = Visibility.Collapsed;
        SetBinding(VisibilityProperty, new Binding("DataContext.Privacy.Streamer.On")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Window), 1),
            Converter = new BooleanToVisibilityConverter(),
            FallbackValue = Visibility.Collapsed,
        });
    }
}
```

- [ ] **Step 6: The Privacy section and the banners**

In `windows-dotnet/src/Sanduhr.App/Views/PrivacyPage.xaml`, replace

```xml
             Focusable="False">
    <!-- Settings, Privacy (spec, Privacy; WF6, WF10): what leaves this PC and what Claude can change. Hosted in SettingsWindow.
```

with

```xml
             Focusable="False">
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BoolVis" />
    </UserControl.Resources>
    <!-- Settings, Privacy (spec, Privacy; WF6, WF10): what leaves this PC and what Claude can change. Hosted in SettingsWindow.
```

and replace the seat, word for word (the carry's anchor):

```xml
                <!-- Slice 7: Streamer mode (streamer, streamer-allowlist) goes here, first on the page, with its divider below it. -->
```

with

```xml
                <!-- The carry's order (slice 6 carry): the banner, the jump links, Streamer mode, then What leaves this PC. -->
                <views:StreamerBanner />
                <views:SectionJumpLinks Page="Privacy" />

                <!-- Streamer mode (streamer; spec, Streamer mode): the switch, what it can't reach, demo mode, Shuffle. DataContext: Streamer. -->
                <StackPanel views:SettingsAnchor.Id="streamer" DataContext="{Binding Streamer}">
                    <TextBlock Text="{x:Static core:SettingsNames.StreamerSection}" FontSize="13" FontWeight="SemiBold"
                               AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                    <CheckBox Style="{DynamicResource ThemedCheckBox}" Content="{x:Static core:SettingsNames.StreamerSwitch}"
                              IsChecked="{Binding On, Mode=TwoWay}"
                              AutomationProperties.Name="{x:Static core:SettingsNames.StreamerSwitch}" />
                    <TextBlock Text="{x:Static core:SettingsNames.StreamerCaption}" FontSize="11" TextWrapping="Wrap" Margin="22,3,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                    <TextBlock Text="{x:Static core:SettingsNames.StreamerCantReach}" FontSize="11" TextWrapping="Wrap" Margin="22,6,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                    <TextBlock Text="{x:Static core:SettingsNames.StreamerCantReachMore}" FontSize="11" TextWrapping="Wrap" Margin="22,3,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                    <TextBlock Text="{x:Static core:SettingsNames.StreamerDemoLine}" FontSize="11" TextWrapping="Wrap" Margin="22,6,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                    <Button Style="{DynamicResource FlatButton}" HorizontalAlignment="Left" Margin="0,12,0,0" Padding="10,4"
                            Content="{x:Static core:SettingsNames.ShuffleCodenames}" Command="{Binding ShuffleCommand}"
                            AutomationProperties.Name="{x:Static core:SettingsNames.ShuffleCodenames}" />
                    <TextBlock Text="{x:Static core:SettingsNames.ShuffleCaption}" FontSize="11" TextWrapping="Wrap" Margin="0,3,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                </StackPanel>

                <!-- Show the real name (streamer-allowlist; spec, the allowlist C): codenames only while on. -->
                <StackPanel views:SettingsAnchor.Id="streamer-allowlist" DataContext="{Binding Streamer}" Margin="0,16,0,0">
                    <TextBlock Text="{x:Static core:SettingsNames.ShowRealNameSection}" FontSize="13" FontWeight="SemiBold"
                               AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,6" />
                    <TextBlock Text="{Binding AllowlistCaption}" FontSize="11" TextWrapping="Wrap" Margin="0,0,0,6"
                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                    <TextBlock Text="{x:Static core:SettingsNames.AllowlistEmpty}" FontSize="11" TextWrapping="Wrap"
                               Visibility="{Binding NoRows, Converter={StaticResource BoolVis}}"
                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                    <ItemsControl ItemsSource="{Binding Rows}" Focusable="False"
                                  Visibility="{Binding HasRows, Converter={StaticResource BoolVis}}"
                                  AutomationProperties.Name="{x:Static core:SettingsNames.ShowRealNameSection}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <StackPanel>
                                    <TextBlock Text="{Binding CategoryTitle, Mode=OneWay}" FontSize="11.5" FontWeight="SemiBold" Margin="0,8,0,4"
                                               AutomationProperties.HeadingLevel="Level3"
                                               Visibility="{Binding StartsCategory, Converter={StaticResource BoolVis}}"
                                               Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                    <CheckBox Style="{DynamicResource ThemedCheckBox}" Margin="0,0,0,4"
                                              IsChecked="{Binding Shown, Mode=TwoWay}"
                                              AutomationProperties.Name="{Binding Name, Mode=OneWay}">
                                        <TextBlock Text="{Binding Text, Mode=OneWay}" FontSize="11.5" TextWrapping="Wrap" />
                                    </CheckBox>
                                </StackPanel>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>

                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />
```

The checkbox's spoken name is "Show the real name on stream, Dune Fox", and its visible text is the row's text, so the spoken name doesn't start with the visible label. That is deliberate: the action is the checkbox's, and the row's name follows it, which is the per-row context rule. If `SettingsSourceScanTests.Visible_labels_start_their_spoken_names` flags it (it checks string `Content`; this content is a TextBlock, so it shouldn't), leave the XAML and add the template to that test's documented exceptions with this reason.

Place a banner under the first line of every other masking page:

- `SettingsWindow.xaml`, Accounts: replace

  ```xml
                        <StackPanel Margin="16" DataContext="{Binding Accounts}">
                            <StackPanel views:SettingsAnchor.Id="accounts">
  ```

  with

  ```xml
                        <StackPanel Margin="16" DataContext="{Binding Accounts}">
                            <views:StreamerBanner />
                            <StackPanel views:SettingsAnchor.Id="accounts">
  ```

- Claude Usage: replace

  ```xml
                            <!-- Sub-nav -->
                            <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,10">
  ```

  with

  ```xml
                            <!-- Sub-nav, under the streamer banner (slice 7) -->
                            <StackPanel Grid.Row="0">
                            <views:StreamerBanner />
                            <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
  ```

  and replace

  ```xml
                                <Button Content="Sessions" Style="{StaticResource CcNavSessions}"
                                        Command="{Binding SetSessionsCommand}" />
                            </StackPanel>
  ```

  with

  ```xml
                                <Button Content="Sessions" Style="{StaticResource CcNavSessions}"
                                        Command="{Binding SetSessionsCommand}" />
                            </StackPanel>
                            </StackPanel>
  ```

- Publish usage: replace

  ```xml
                            <TextBlock Text="Publish usage" FontSize="14" FontWeight="SemiBold"
                                       AutomationProperties.HeadingLevel="Level1"
                                       Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,6" />
  ```

  with the same three lines followed by `                            <views:StreamerBanner />`.

- Desk: replace

  ```xml
                                    <TextBlock Text="Desk" FontSize="14" FontWeight="SemiBold" AutomationProperties.HeadingLevel="Level1"
                                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,4" />
  ```

  with the same two lines followed by `                                    <views:StreamerBanner />`.

- Weather: replace

  ```xml
                            <TextBlock Text="Weather" FontSize="14" FontWeight="SemiBold" AutomationProperties.HeadingLevel="Level1"
                                       Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
  ```

  with the same two lines followed by `                            <views:StreamerBanner />`.

- About: replace

  ```xml
                            <TextBlock Text="About" FontSize="14" FontWeight="SemiBold"
                                       AutomationProperties.HeadingLevel="Level1"
                                       Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,12" />
  ```

  with the same three lines followed by `                            <views:StreamerBanner />`.

- `ClaudeCodePage.xaml`: replace

  ```xml
                <TextBlock Text="Claude Code" FontSize="14" FontWeight="SemiBold" AutomationProperties.HeadingLevel="Level1"
                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,4" />
  ```

  with the same two lines followed by `                <views:StreamerBanner />`.

- `ModsPage.xaml`: replace

  ```xml
                <TextBlock Text="{x:Static core:SettingsNames.ModsPageLine}" Style="{StaticResource Dim}" Margin="0,0,0,4" />
  ```

  with the same line followed by `                <views:StreamerBanner />`.

- `WatchersPage.xaml`: replace

  ```xml
                <TextBlock Text="{x:Static core:SettingsNames.WatchersTitle}" FontSize="14" FontWeight="SemiBold"
                           AutomationProperties.HeadingLevel="Level1"
                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,12" />
  ```

  with the same three lines followed by `                <views:StreamerBanner />`.

Check each anchor's first line with `git grep -c -F` before the Edit. Every one is unique at 3cc3699d. Where a page has jump links (Publish, Desk, About), the banner sits above them, as on Privacy.

- [ ] **Step 7: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSlice7IndexTests|FullyQualifiedName~SettingsSlice6IndexTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsNamesTests" > %TEMP%\slice7-t6-core.log 2>&1`
Then: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~StreamerPrivacyTests|FullyQualifiedName~DeskSplitPagesTests|FullyQualifiedName~PrivacyPageTests|FullyQualifiedName~SettingsWindowNamesTests|FullyQualifiedName~SettingsSourceScanTests" > %TEMP%\slice7-t6-app.log 2>&1`
Expected: PASS. Two existing pins may move:
- `SettingsIndexTests`' boost check, which said the streamer boosts were still dead (slice 6 preflight table, "Pass (streamer boosts still dead)"): it now sees them live. Update it to expect them live.
- `Every_anchor_marked_in_XAML_is_in_the_index`: it passes, because both anchors are marked and indexed in this commit.

Say which tests you updated and why. Don't weaken any.

- [ ] **Step 8: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.App/ViewModels/StreamerSettingsViewModel.cs windows-dotnet/src/Sanduhr.App/Services/StreamerService.cs windows-dotnet/src/Sanduhr.App/ViewModels/PrivacyViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.App/Views/StreamerBanner.cs windows-dotnet/src/Sanduhr.App/Views/PrivacyPage.xaml windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs windows-dotnet/src/Sanduhr.App/Views/ClaudeCodePage.xaml windows-dotnet/src/Sanduhr.App/Views/ModsPage.xaml windows-dotnet/src/Sanduhr.App/Views/WatchersPage.xaml windows-dotnet/tests/Sanduhr.Tests/SettingsSlice7IndexTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsSlice6IndexTests.cs windows-dotnet/tests/Sanduhr.App.Tests/StreamerPrivacyTests.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs
git commit -m "feat(settings-v2): Privacy, Streamer mode: the switch, Shuffle, Show the real name and a banner on every masking page"
```

(Add any existing test file Step 7 updated.)

---

### Task 7: The tray and the widget: the two shortcuts, the tooltip, the chip's indicator, the account menus, and the windows that name an account or a folder

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (`StreamerOn`, `AccountSwitcherName`, the chip, the error line, `OnStreamerChanged`)
- Modify: `windows-dotnet/src/Sanduhr.App/MainWindow.xaml` (the glyph, the chip's spoken name), `windows-dotnet/src/Sanduhr.App/MainWindow.xaml.cs` (the item)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (the tray item, the tooltip, `OnStreamerChanged`)
- Modify: `windows-dotnet/src/Sanduhr.App/Services/AccountMenuBuilder.cs` (M2)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WelcomeTourViewModel.cs` (CP 5)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/ManualKeyWindow.xaml.cs` (CP 2, M6)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/VaultConsentDialog.xaml.cs` (M7)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/StreamerWidgetTests.cs`

**Interfaces:**
- Consumes (Tasks 3, 4): `StreamerMode.Mask`, `WidgetViewModel.Streamer`, `SettingsNames.TrayTooltip`, `StreamerChipName`, `StreamerSection`.
- Produces:
  - `WidgetViewModel.StreamerOn` (observable), `WidgetViewModel.AccountSwitcherName` (observable)
  - `AccountMenuBuilder.Header(string label, bool isActive, bool inUse)` (internal static, pure)
  - `ManualKeyWindow.ForReauth` shows the codename and keeps the real label in its persist closure
  - `VaultConsentDialog` shows codenames and keeps real names as its keys

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/StreamerWidgetTests.cs`:

```csharp
using System.Windows.Controls;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 7: streamer mode outside Settings: the chip, its indicator, the account menus, the tour's chip, the
/// re-sign-in window, the first-run archive question and the widget's error line (spec, Reach outside Settings; the reach inventory).</summary>
[Collection("SettingsWindow")]
public sealed class StreamerWidgetTests : AppWindowFixture
{
    [Fact]
    public void The_chip_shows_the_codename_and_says_streamer_mode_is_on() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha", "Qxbeta");
        try
        {
            Assert.Contains("Qxalpha", widget.AccountSwitcherText);
            Assert.False(widget.StreamerOn);
            widget.Streamer.SetOn(true);
            var code = StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
            Assert.Equal($"⇆ {code}", widget.AccountSwitcherText);
            Assert.True(widget.StreamerOn);
            Assert.Equal(SettingsNames.StreamerChipName($"⇆ {code}", true), widget.AccountSwitcherName);
            widget.Streamer.SetOn(false);
            Assert.Equal("⇆ Qxalpha", widget.AccountSwitcherText);
            Assert.Equal("⇆ Qxalpha", widget.AccountSwitcherName);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_account_menus_show_codenames_and_switch_by_the_real_label() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha", "Qxbeta");
        try
        {
            widget.Streamer.SetOn(true);
            var menu = new MenuItem();
            AccountMenuBuilder.PopulateSwitchList(menu.Items, widget, includeAdd: true, includeManage: true);
            var rows = menu.Items.OfType<MenuItem>().Where(m => m.CommandParameter is string).ToList();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, m => Assert.DoesNotContain("Qx", (string)m.Header));
            Assert.Equal(["Qxalpha", "Qxbeta"], rows.Select(m => (string)m.CommandParameter));
            Assert.Contains(StreamerMode.Mask.Name(CodenameCategory.Account, "Qxbeta"), (string)rows[1].Header);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_tours_chip_shows_the_codename() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        try
        {
            widget.Streamer.SetOn(true);
            var tour = new WelcomeTourViewModel(WelcomeTour.StepsFor(false, new HashSet<TourFeature>()), widget);
            Assert.DoesNotContain("Qxalpha", tour.ChipPreview);
            Assert.Equal(StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"), tour.ChipPreview);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_re_sign_in_window_shows_the_codename_and_saves_to_the_real_label() => AppHost.Run(() =>
    {
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
        string? saved = null;
        var window = ManualKeyWindow.ForReauth("Qxalpha", (label, _) => saved = label, embeddedAvailable: false);
        OffScreen(window);
        try
        {
            var code = StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
            Assert.DoesNotContain("Qxalpha", window.HeadlineText.Text);
            Assert.Contains(code, window.HeadlineText.Text);
            Assert.Equal(code, window.LabelBox.Text);
            window.ShowErrorForTests("Account 'Qxalpha' already exists");
            Assert.Equal($"Account '{code}' already exists", window.ErrorText.Text);
        }
        finally { window.Close(); }
        Assert.Null(saved);   // nothing was saved; the closure still names the real label (SignInCoordinator.cs:101)
    });

    [Fact]
    public void The_first_run_archive_question_shows_codenames_and_keeps_the_real_folders() => AppHost.Run(() =>
    {
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
        var dialog = VaultConsentDialog.ForTests([".claude-qxwork"]);
        try
        {
            var box = dialog.RootBoxesForTests.Single();
            Assert.Equal(".claude-qxwork", box.Key);
            Assert.Equal(StreamerMode.Mask.FolderName(".claude-qxwork"), (string)box.Value.Content);
        }
        finally { dialog.Close(); }
    });

    [Fact]
    public void The_widgets_error_line_shows_no_user_folder() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        try
        {
            widget.Streamer.SetOn(true);
            widget.FailForTests($@"Could not read {Profile}\.claude-qxwork\settings.json");
            Assert.DoesNotContain(Profile, widget.StatusText);
            Assert.Contains("%USERPROFILE%", widget.StatusText);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_tray_tooltip_and_its_item_follow_the_switch()
    {
        Assert.StartsWith("Streamer mode · ", SettingsNames.TrayTooltip(true, "Sanduhr für Claude — wk 42%"));
        Assert.Equal(SettingsNames.StreamerSection, SettingsNames.StreamerMenuItemHeader);
    }
}
```

Three test seams are named here. Step 3 adds each as a small `internal` member, and Step 2 adds them first as stubs that **don't mask**, so the RED is the sentinel on screen:
- `WelcomeTourViewModel.ChipPreview` (internal, `=> ChipText()`)
- `ManualKeyWindow.ShowErrorForTests(string)`, `HeadlineText`, `LabelBox` and `ErrorText` (the named elements are already `internal` through `x:Name`)
- `VaultConsentDialog.ForTests(IReadOnlyList<string>)` and `RootBoxesForTests`
- `WidgetViewModel.FailForTests(string)`, which runs the same text through the `Fail` path that `:1522` uses

`SettingsNames.StreamerMenuItemHeader` is one more name: add `public const string StreamerMenuItemHeader = StreamerSection;` to the slice 7 block, so the tray and the widget name the item from one member (Principle 2).

- [ ] **Step 2: Run them to see them fail**

First add the stubs: `StreamerOn` and `AccountSwitcherName` as plain observable properties that nothing sets, `ChipPreview => ChipText()`, `ShowErrorForTests(m) => ShowError(m)`, `ForTests(names) => new VaultConsentDialog(names)`, `RootBoxesForTests => _checkboxes`, `FailForTests(m) => Fail($"Error: {Truncate(m, 200)}")`, and the `StreamerMenuItemHeader` name.

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~StreamerWidgetTests > %TEMP%\slice7-t7-red.log 2>&1`
Expected: a behavioural RED. Every test but the last fails with a sentinel on screen. For example: `Assert.Equal() Failure: Expected ⇆ Dune Fox, Actual ⇆ Qxalpha`, `DoesNotContain … found "Qx" in "●  Qxalpha"`, and `found "Qxalpha" in "Update sessionKey for 'Qxalpha'"`. Record each.

- [ ] **Step 3: Mask each choke point**

`WidgetViewModel.cs`. Beside `_accountSwitcherClickable` (replace

```csharp
    [ObservableProperty] private bool _accountSwitcherClickable;
```

with

```csharp
    [ObservableProperty] private bool _accountSwitcherClickable;

    /// <summary>Streamer mode is on (slice 7): the chip draws its eye-slash glyph (owner taste 4).</summary>
    [ObservableProperty] private bool _streamerOn;

    /// <summary>The chip as spoken: its text, then "Streamer mode on" while on (SettingsNames.StreamerChipName).</summary>
    [ObservableProperty] private string _accountSwitcherName = "";
```

). In the constructor, replace

```csharp
        Streamer.Load();
```

with

```csharp
        Streamer.Load();
        StreamerOn = Streamer.On;
        Streamer.Changed += OnStreamerChanged;
```

In `RefreshAccountLabel`, replace

```csharp
        bool multiple = _accounts.ListAccounts().Count > 1;
        AccountSwitcherText = multiple ? $"⇆ {active}" : active;
        AccountSwitcherClickable = multiple;
    }
```

with

```csharp
        bool multiple = _accounts.ListAccounts().Count > 1;
        // Streamer mode (slice 7, M1): the chip names the account by its codename; the indicator says why.
        var shown = StreamerMode.Mask.Name(CodenameCategory.Account, active);
        AccountSwitcherText = multiple ? $"⇆ {shown}" : shown;
        AccountSwitcherName = SettingsNames.StreamerChipName(AccountSwitcherText, StreamerMode.On);
        AccountSwitcherClickable = multiple;
    }

    /// <summary>The switch moved, the codenames were shuffled or a name was shown (slice 7): the chip and the Desk read their names again.
    /// The widget listens to its own service, and detaches in Dispose.</summary>
    private void OnStreamerChanged()
    {
        StreamerOn = Streamer.On;
        RefreshAccountLabel();
        DeskUsageChanged?.Invoke();
    }
```

The demo branch and the signed-out branch of `RefreshAccountLabel` set `AccountSwitcherText` and return early. Add `AccountSwitcherName = AccountSwitcherText;` before each of their `return;` lines (Read the method: the anchors are `AccountSwitcherClickable = true;\n            return;` in the demo branch and `AccountSwitcherClickable = false;\n            return;` in the signed-out branch, each unique as a two-line block).

In the error path, replace

```csharp
                Fail($"Error: {Truncate(ex.Message, 60)}");
```

with

```csharp
                Fail($"Error: {Truncate(StreamerMode.Mask.Paths(ex.Message), 60)}");   // M8: an exception's text can carry a path
```

and make `FailForTests` run `Fail($"Error: {Truncate(StreamerMode.Mask.Paths(message), 200)}")`, the same expression with the test's length.

In `Dispose()`, replace

```csharp
        Streamer.Dispose();
```

with

```csharp
        Streamer.Changed -= OnStreamerChanged;
        Streamer.Dispose();
```

`MainWindow.xaml`, the chip. Replace

```xml
                            ToolTip="Active Claude account. Click to switch when multiple are configured. Add or remove accounts in Settings, Accounts.">
```

with

```xml
                            ToolTip="Active Claude account. Click to switch when multiple are configured. Add or remove accounts in Settings, Accounts."
                            AutomationProperties.Name="{Binding AccountSwitcherName}">
```

and replace

```xml
                                                <TextBlock Text="{Binding AccountSwitcherText}"
                                                           Foreground="{TemplateBinding Foreground}" />
```

with

```xml
                                                <StackPanel Orientation="Horizontal">
                                                    <!-- Streamer mode (slice 7): the eye-slash glyph, spoken through the chip's name. -->
                                                    <TextBlock Text="&#xED1A;" FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets" FontSize="10"
                                                               VerticalAlignment="Center" Margin="0,0,4,0"
                                                               Visibility="{Binding StreamerOn, Converter={StaticResource BoolVis}}"
                                                               Foreground="{TemplateBinding Foreground}" />
                                                    <TextBlock Text="{Binding AccountSwitcherText}"
                                                               Foreground="{TemplateBinding Foreground}" />
                                                </StackPanel>
```

`MainWindow.xaml.cs`, the widget's right-click item. Replace

```csharp
        menu.Opened += (_, _) => compact.IsChecked = Vm?.IsCompact ?? false;
        menu.Items.Add(compact);
```

with

```csharp
        menu.Opened += (_, _) => compact.IsChecked = Vm?.IsCompact ?? false;
        menu.Items.Add(compact);

        // Streamer mode (spec: a widget right-click item of the same name, writing the same key). Checkable: one name, the check says which.
        var streamer = new MenuItem { Header = SettingsNames.StreamerMenuItemHeader, IsCheckable = true };
        streamer.Click += (_, _) => Vm?.Streamer.SetOn(streamer.IsChecked);
        menu.Opened += (_, _) => streamer.IsChecked = Vm?.Streamer.On ?? false;
        menu.Items.Add(streamer);
```

`AccountMenuBuilder.cs` (M2). Replace

```csharp
                var mark = !isActive && inUse.Contains(label) ? " · in use" : "";
                items.Add(new MenuItem
                {
                    Header = (isActive ? $"●  {label}" : $"     {label}") + mark,
```

with

```csharp
                items.Add(new MenuItem
                {
                    Header = Header(label, isActive, !isActive && inUse.Contains(label)),
```

and add this method to the class, after `PopulateSwitchList`:

```csharp

    /// <summary>A row's text: the bullet for the active account, the label (its codename while streamer mode is on, slice 7 M2), and
    /// " · in use" when Follow saw it in use. The row's command still takes the real label.</summary>
    internal static string Header(string label, bool isActive, bool inUse)
    {
        var shown = StreamerMode.Mask.Name(CodenameCategory.Account, label);
        return (isActive ? $"●  {shown}" : $"     {shown}") + (inUse ? " · in use" : "");
    }
```

`WelcomeTourViewModel.cs` (CP 5). Replace

```csharp
        : _widget.ActiveAccount is { } active ? active
```

with

```csharp
        : _widget.ActiveAccount is { } active ? StreamerMode.Mask.Name(CodenameCategory.Account, active)   // slice 7, CP 5
```

`ManualKeyWindow.xaml.cs` (CP 2, M6). Replace

```csharp
        var w = new ManualKeyWindow(label, persist, embeddedAvailable);
        w.Title = "Update sessionKey";
        w.HeadlineText.Text = $"Update sessionKey for '{label}'";
```

with

```csharp
        var w = new ManualKeyWindow(label, persist, embeddedAvailable);
        // Streamer mode (slice 7, CP 2): the window names the account by its codename. The persist closure (SignInCoordinator) still targets
        // the real label, and the box is disabled, so a codename is never saved.
        var shown = StreamerMode.Mask.Name(CodenameCategory.Account, label);
        w._hidden = shown == label ? null : (label, shown);
        w.LabelBox.Text = shown;
        w.Title = "Update sessionKey";
        w.HeadlineText.Text = $"Update sessionKey for '{shown}'";
```

and replace

```csharp
    private void ShowError(string message)
    {
        ErrorText.Text = message;
```

with

```csharp
    /// <summary>Re-sign-in under streamer mode: the real label and its codename, so an error never names the account (slice 7, M6).</summary>
    private (string Real, string Shown)? _hidden;

    private void ShowError(string message)
    {
        if (_hidden is { } h)
            message = message.Replace(h.Real, h.Shown, StringComparison.Ordinal);
        message = StreamerMode.Mask.Paths(message);
        ErrorText.Text = message;
```

Add `using Sanduhr.App.Services;` and `using Sanduhr.Core;` to the file if they're missing.

`VaultConsentDialog.xaml.cs` (M7). Replace

```csharp
            var cb = new CheckBox
            {
                Content = root,
```

with

```csharp
            var cb = new CheckBox
            {
                Content = StreamerMode.Mask.FolderName(root),   // slice 7, M7: the key below stays the real folder
```

`App.xaml.cs`. Add the tray item beside the others: replace

```csharp
        var refresh = new MenuItem { Header = SettingsNames.RefreshUsageNow };
        refresh.Click += (_, _) => _vm?.RefreshCommand.Execute(null);
```

with

```csharp
        var refresh = new MenuItem { Header = SettingsNames.RefreshUsageNow };
        refresh.Click += (_, _) => _vm?.RefreshCommand.Execute(null);

        // Streamer mode (spec: a tray item with a checked state, writing the same key as Privacy's switch).
        var streamer = new MenuItem { Header = SettingsNames.StreamerMenuItemHeader, IsCheckable = true };
        streamer.Click += (_, _) => _vm?.Streamer.SetOn(streamer.IsChecked);
        menu.Opened += (_, _) => streamer.IsChecked = _vm?.Streamer.On ?? false;
```

and replace

```csharp
        menu.Items.Add(BuildTrayShowsMenu());
        menu.Items.Add(new Separator());
```

with

```csharp
        menu.Items.Add(BuildTrayShowsMenu());
        menu.Items.Add(streamer);
        menu.Items.Add(new Separator());
```

The tooltip. Replace

```csharp
        _tray.ToolTipText = reading is null
            ? "Sanduhr für Claude — no data yet"
            : _vm?.IsDemo == true
                ? $"Sanduhr für Claude — {DemoMode.TrayLabel(reading.Label, reading.Percent)}"
                : $"Sanduhr für Claude — {reading.Label} {reading.Percent}%";
    }
```

with

```csharp
        _lastTrayReading = reading;
        _tray.ToolTipText = SettingsNames.TrayTooltip(_vm?.Streamer.On == true, reading is null
            ? "Sanduhr für Claude — no data yet"
            : _vm?.IsDemo == true
                ? $"Sanduhr für Claude — {DemoMode.TrayLabel(reading.Label, reading.Percent)}"
                : $"Sanduhr für Claude — {reading.Label} {reading.Percent}%");
    }

    /// <summary>The last reading, so the tooltip can be built again when streamer mode moves (slice 7).</summary>
    private TrayReading? _lastTrayReading;

    /// <summary>Streamer mode moved (slice 7): the tooltip's prefix, the Desk and the Watchers runner line follow. App lives as long as the
    /// widget, so this subscription is never detached.</summary>
    private void OnStreamerChanged()
    {
        if (_tray is not null)
            OnTrayReadingChanged(_lastTrayReading);
        _desk?.Reload();
        RunnerStatusShown?.Invoke();
    }
```

`OnTrayReadingChanged` also re-renders the icon from the reading; that is the same picture, so calling it again is harmless. Then subscribe once, in the startup path where the tray is set up (`_vm` is built by then: the line above it calls `_vm.AttachAlertService`). Replace

```csharp
        SetupTray();
        SyncTrayRotation();
```

with

```csharp
        SetupTray();
        SyncTrayRotation();
        // Streamer mode (slice 7): App lives as long as the widget, so this is never detached.
        _vm.Streamer.Changed += OnStreamerChanged;
```

`RunnerStatusShown` is App's private `event Action?` (`App.xaml.cs:47`), which the runner service raises through the dispatcher (`:602`) and Settings subscribes to (`:783`), so invoking it re-pushes the Watchers line to an open Settings.

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~StreamerWidgetTests|FullyQualifiedName~MainWindow|FullyQualifiedName~AccountMenu|FullyQualifiedName~WelcomeTour|FullyQualifiedName~ManualKey" > %TEMP%\slice7-t7.log 2>&1`
Expected: PASS. Existing chip and menu tests pass with streamer mode off: off, `StreamerMask.Off` returns each label unchanged.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs windows-dotnet/src/Sanduhr.App/MainWindow.xaml windows-dotnet/src/Sanduhr.App/MainWindow.xaml.cs windows-dotnet/src/Sanduhr.App/App.xaml.cs windows-dotnet/src/Sanduhr.App/Services/AccountMenuBuilder.cs windows-dotnet/src/Sanduhr.App/ViewModels/WelcomeTourViewModel.cs windows-dotnet/src/Sanduhr.App/Views/ManualKeyWindow.xaml.cs windows-dotnet/src/Sanduhr.App/Views/VaultConsentDialog.xaml.cs windows-dotnet/tests/Sanduhr.App.Tests/StreamerWidgetTests.cs
git commit -m "feat(settings-v2): streamer mode in the tray and on the widget: the items, the tooltip, the chip's glyph and every account name"
```

---

### Task 8: The Desk: the Claude line's account, runner cards and the weather name

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (`CurrentDeskUsage`, M3)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskController.cs` (`WatchersNow`, M4; the two frame builds, M5)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/StreamerDeskTests.cs`

**Interfaces:**
- Consumes (Tasks 2, 4, 7): `StreamerMask.Watchers`, `WeatherName`, `StreamerMode.Mask`; `OnStreamerChanged` already reloads the Desk (Task 7).
- Produces: `CurrentDeskUsage.Account` is the codename while on; the Desk frame's watchers and weather name are masked.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/StreamerDeskTests.cs`:

```csharp
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 7: the Desk's names (spec, Reach outside Settings: watcher cards, the weather name; decision 3: the Claude line).</summary>
[Collection("SettingsWindow")]
public sealed class StreamerDeskTests : AppWindowFixture
{
    /// <summary>Decision 3: with two accounts the Claude line names the active one, in every style; while on, by its codename.</summary>
    [Fact]
    public void The_claude_line_names_the_account_by_codename() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha", "Qxbeta");
        try
        {
            Assert.Equal("Qxalpha", widget.CurrentDeskUsage.Account);
            widget.Streamer.SetOn(true);
            Assert.Equal(StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"), widget.CurrentDeskUsage.Account);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Moving_the_switch_tells_the_desk_to_read_its_usage_again() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha", "Qxbeta");
        try
        {
            int heard = 0;
            widget.DeskUsageChanged += () => heard++;
            widget.Streamer.SetOn(true);
            Assert.True(heard >= 1);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_desk_frame_masks_runner_cards_and_the_weather_name()
    {
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
        var runner = new Watcher
        {
            Id = "r:k:build", Source = WatcherSource.Runner, Title = "qxrunner · build", Short = "build", Runner = "qxrunner", RunnerKey = "k", Job = "build",
        };
        var place = WeatherPlace.Create("Qxcity", "Texas", "United States", 32.75, -97.33);
        var (watchers, nickname) = DeskController.StreamerFrameParts([runner], place, "");
        Assert.DoesNotContain("qx", watchers[0].Title, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(" · build", watchers[0].Title);
        Assert.Equal(StreamerMode.Mask.Name(CodenameCategory.City, place.Label), nickname);
        Assert.Equal("Home", DeskController.StreamerFrameParts([runner], place, "Home").Nickname);
    }
}
```

`DeskController.StreamerFrameParts` is the one internal static seam both frame builds call (Step 3). Add it first as a pass-through stub, `=> (watchers, nickname)`, for the RED.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~StreamerDeskTests > %TEMP%\slice7-t8-red.log 2>&1`
Expected: a behavioural RED. `The_claude_line…` fails with `Expected: Dune Fox… Actual: Qxalpha`, and `The_desk_frame…` fails with `found "qx" in "qxrunner · build"`. `Moving_the_switch…` passes already (Task 7 raises `DeskUsageChanged`).

- [ ] **Step 3: Mask the three Desk choke points**

`WidgetViewModel.cs` (M3). Replace

```csharp
            string? account = IsDemo ? DemoMode.ChipText(_demoAccount)
                : _accounts.ListAccounts().Count > 1 ? _accounts.GetActive() : null;
```

with

```csharp
            // Streamer mode (slice 7, decision 3): the Claude line names the account by its codename, in every style and in its reminder.
            string? account = IsDemo ? DemoMode.ChipText(_demoAccount)
                : _accounts.ListAccounts().Count > 1 && _accounts.GetActive() is { } active
                    ? StreamerMode.Mask.Name(CodenameCategory.Account, active) : null;
```

`DeskController.cs` (M4, M5). Replace

```csharp
    private IReadOnlyList<Watcher> WatchersNow() => _watchers is not null && Placed("watchers") ? _watchers.Shown : [];
```

with

```csharp
    private IReadOnlyList<Watcher> WatchersNow() =>
        _watchers is not null && Placed("watchers") ? StreamerFrameParts(_watchers.Shown, null, "").Watchers : [];

    /// <summary>
    /// Streamer mode's part of a Desk frame (slice 7, M4 and M5): a CI runner's card names its runner by codename, and the weather piece
    /// shows the "Show it as" nickname, else the city's codename (spec). Off, both pass through. One seam, so both frame builds agree.
    /// </summary>
    internal static (IReadOnlyList<Watcher> Watchers, string Nickname) StreamerFrameParts(
        IReadOnlyList<Watcher> watchers, WeatherPlace? place, string? nickname) =>
        (StreamerMode.Mask.Watchers(watchers), StreamerMode.Mask.WeatherName(place, nickname));
```

and replace both occurrences (they are identical, in the full and the light frame builds; use `replace_all: true`, as the Global Constraints say)

```csharp
            WeatherNickname = _weatherSettings.Nickname,
```

with

```csharp
            WeatherNickname = StreamerFrameParts([], _weatherSettings.Place, _weatherSettings.Nickname).Nickname,
```

Off, `WeatherName` returns the nickname, or "" when there is none, which is what the frame held before. `DeskView` draws `place.Name` for an empty nickname (`DeskView.cs:827-837`), so nothing changes off.

Add `using Sanduhr.App.Services;` to `DeskController.cs` if it isn't there.

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~StreamerDeskTests|FullyQualifiedName~Desk" > %TEMP%\slice7-t8.log 2>&1`
Expected: PASS, with the existing Desk tests unchanged.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs windows-dotnet/src/Sanduhr.App/Desk/DeskController.cs windows-dotnet/tests/Sanduhr.App.Tests/StreamerDeskTests.cs
git commit -m "feat(settings-v2): the Desk names the account, runners and the city by codename in streamer mode"
```

---

### Task 9: Accounts, the Claude Code cards, their dialogs, and Privacy's folder names

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Services/StreamerMode.cs` (`ShowFolder`, `FoldersIn`)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/AccountsViewModel.cs` (CP 1, CP 25, the CP 7 bypass)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeHost.cs` (CP 7)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs` (CP 6 `AccountChoice`, the CP 7 bypass, CP 8, CP 9, CP 11, the card's quick route)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/FolderHoldsViewModel.cs` (CP 9, CP 10)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/McpConsentDialog.xaml.cs` (CP 11, CP 12)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (the Accounts row's text), `windows-dotnet/src/Sanduhr.App/Views/ClaudeCodePage.xaml` (the combo, the card's menu)
- Modify: `windows-dotnet/tests/Sanduhr.App.Tests/ClaudeCodeLinkTests.cs` (the combo's items are `AccountChoice`s)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/StreamerAccountsAndFoldersTests.cs`

**Interfaces:**
- Consumes: `StreamerMode`, `StreamerMask.Name/Folder/FolderName/HidesFolder/AddOn/Paths/FolderKey`, `WidgetViewModel.Streamer.SetShown`, `SettingsViewModel.ReloadNames`.
- Produces:
  - `StreamerMode.ShowFolder(string path, string profileDir)`: the masked folder when hidden, else `~\…`
  - `StreamerMode.FoldersIn(string text, IEnumerable<string> folderPaths)`: each folder's full path as it shows, then `Paths`
  - `AccountItemViewModel.Shown`, `AccountsViewModel.LastQuestion` (internal), the Rename prompt's empty prefill while hidden
  - `public sealed partial class AccountChoice : ObservableObject` (`Label`, `Shown`), `ClaudeCodeViewModel.AccountOptions` as `ObservableCollection<AccountChoice>`
  - `CcFolderRowViewModel.CanShowRealName`, `ShowRealNameCommand`

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/StreamerAccountsAndFoldersTests.cs`:

```csharp
using System.IO;
using System.Text.Json;
using Sanduhr.App.Services;
using Sanduhr.App.Updates;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 7: Accounts, the Claude Code cards and their report and dialogs, and Privacy's folder names (spec, Reach
/// inside Settings; the slice 6 carry; CPs 1, 6 to 12 and 25).</summary>
[Collection("SettingsWindow")]
public sealed class StreamerAccountsAndFoldersTests : AppWindowFixture
{
    private (SettingsViewModel Svm, WidgetViewModel Widget) Make()
    {
        var statusline = JsonSerializer.Serialize(new
        {
            statusLine = new { type = "command", command = $"powershell -File \"{Path.Combine(Profile, ".claude-qxwork", "qx.ps1")}\"" },
            enabledPlugins = new Dictionary<string, bool> { ["qxplugin@qxmarket"] = true, ["sanduhr-meters@inline"] = true },
        });
        Home(".claude-qxwork", statusline);
        Home(".claude-qxhome");
        var (widget, _) = ScratchWidget("Qxalpha", "Qxbeta");
        widget.SetAccountFolder("Qxalpha", Path.Combine(Profile, ".claude-qxwork"));
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
            checkForUpdatesNow: () => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate)), isStoreBuild: false);
        return (svm, widget);
    }

    private static void NoSentinel(string where, string? text) =>
        Assert.False((text ?? "").Contains("qx", StringComparison.OrdinalIgnoreCase), $"{where}: \"{text}\"");

    [Fact]
    public void Accounts_rows_names_and_messages_use_codenames() => AppHost.Run(() =>
    {
        var (svm, widget) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            var accounts = svm.Accounts;
            accounts.Reload();
            foreach (var a in accounts.Accounts)
            {
                NoSentinel("row", a.Shown);
                NoSentinel("row spoken", a.ToString());
                Assert.StartsWith("Qx", a.Label);   // the key stays real
            }
            accounts.SelectedAccount = accounts.Accounts.First(a => a.Label == "Qxalpha");
            NoSentinel("clear name", accounts.ClearHistoryName);
            NoSentinel("remove name", accounts.RemoveName);
            NoSentinel("remove caption", accounts.RemoveCaption);
            NoSentinel("linked folder", accounts.LinkedFolderText);

            string? info = null;
            accounts.ConfirmClearHook = _ => true;
            accounts.InfoHook = m => info = m;
            accounts.ClearHistoryCommand.Execute(null);
            NoSentinel("clear question", accounts.LastQuestion);
            NoSentinel("cleared notice", info);
            accounts.ConfirmRemoveHook = _ => false;
            accounts.SignOutCommand.Execute(null);
            NoSentinel("remove question", accounts.LastQuestion);
        }
        finally { widget.Dispose(); }
    });

    /// <summary>Decision 12: a codename is never saved as an account's name.</summary>
    [Fact]
    public void The_rename_prompt_opens_empty_while_the_name_is_hidden() => AppHost.Run(() =>
    {
        var (svm, widget) = Make();
        try
        {
            Assert.Equal("Qxalpha", AccountsViewModel.RenamePrefill("Qxalpha"));
            widget.Streamer.SetOn(true);
            Assert.Equal("", AccountsViewModel.RenamePrefill("Qxalpha"));
            widget.Streamer.SetShown(CodenameCategory.Account, "Qxalpha", true);
            Assert.Equal("Qxalpha", AccountsViewModel.RenamePrefill("Qxalpha"));
            NoSentinel("rename error", AccountsViewModel.RenameError("Qxbeta", new InvalidOperationException("Account 'Qxbeta' already exists")));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Claude_code_cards_their_names_and_the_linked_account_use_codenames() => AppHost.Run(() =>
    {
        var (svm, widget) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            var page = svm.ClaudeCodePage;
            page.Reload();
            foreach (var row in page.Folders)
            {
                foreach (var (what, text) in new[]
                {
                    ("title", row.Display), ("card", row.AutomationName), ("combo name", row.LinkedAccountName), ("forget", row.ForgetName),
                    ("mcp switch", row.McpReadableName), ("holds", row.HoldsName), ("meters", row.MetersSpoken), ("meters link", row.MetersLinkName),
                })
                    NoSentinel(what, text);
                Assert.True(row.CanShowRealName);
            }
            Assert.All(page.AccountOptions, o => NoSentinel("combo item", o.Shown + o.ToString()));
            var work = page.Folders.Single(f => f.Path.EndsWith(".claude-qxwork", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("Qxalpha", work.LinkedAccount);   // the value stays the real label

            work.HoldsOpen = true;
            NoSentinel("statusline command", work.Holds.StatuslineCommand);
            Assert.All(work.Holds.PluginLines, l => NoSentinel("plugin", l.Name + l.AutomationName));
            Assert.Contains(work.Holds.PluginLines, l => l.Name == "sanduhr-meters@inline");   // allowlisted by default
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_cards_quick_route_shows_its_real_name() => AppHost.Run(() =>
    {
        var (svm, widget) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            var page = svm.ClaudeCodePage;
            page.Reload();
            var work = page.Folders.Single(f => f.Path.EndsWith(".claude-qxwork", StringComparison.OrdinalIgnoreCase));
            work.ShowRealNameCommand.Execute(null);
            page.Reload();
            Assert.Equal(@"~\.claude-qxwork", page.Folders.Single(f => f.Path == work.Path).Display);
            Assert.False(page.Folders.Single(f => f.Path == work.Path).CanShowRealName);
        }
        finally { widget.Dispose(); }
    });

    /// <summary>The slice 6 carry: Privacy's folder-reads row names folders, masked as the cards are.</summary>
    [Fact]
    public void Privacys_folder_names_are_codenames() => AppHost.Run(() =>
    {
        var (svm, widget) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            svm.Privacy.Reload();
            Assert.All(svm.Privacy.Agents, a => NoSentinel(a.Name, a.State + a.ToString()));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Status_lines_and_dialog_text_show_no_folder_or_user_path() => AppHost.Run(() =>
    {
        var (_, widget) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            var work = Path.Combine(Profile, ".claude-qxwork");
            var text = StreamerMode.FoldersIn($@"Couldn't write {work}\settings.json; see {Profile}\AppData\x.log", [work]);
            NoSentinel("status", text);
            Assert.Contains(StreamerMode.Mask.Folder(work), text);
            Assert.Contains(@"%USERPROFILE%\AppData\x.log", text);
            NoSentinel("mcp consent details", McpConsentDialog.DetailsFor(Path.Combine(Profile, ".sanduhr", "bin", "sanduhr-mcp.cmd"),
                Path.Combine(work, ".claude.json"), [work]));
        }
        finally { widget.Dispose(); }
    });

    /// <summary>Review Focus 3: Shuffle with Settings open renames every open page at once, and the allowlist survives.</summary>
    [Fact]
    public void Shuffle_renames_every_open_page_and_keeps_the_allowlist() => AppHost.Run(() =>
    {
        var (svm, widget) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            svm.ReloadNames();
            widget.Streamer.SetShown(CodenameCategory.Account, "Qxbeta", true);
            var account = svm.Accounts.Accounts.Single(a => a.Label == "Qxalpha").Shown;
            var card = svm.ClaudeCodePage.Folders.Single(f => f.Path.EndsWith(".claude-qxwork", StringComparison.OrdinalIgnoreCase)).Display;
            svm.Privacy.Streamer!.ConfirmShuffleHook = () => true;
            svm.Privacy.Streamer.ShuffleCommand.Execute(null);
            Assert.NotEqual(account, svm.Accounts.Accounts.Single(a => a.Label == "Qxalpha").Shown);
            Assert.NotEqual(card, svm.ClaudeCodePage.Folders.Single(f => f.Path.EndsWith(".claude-qxwork", StringComparison.OrdinalIgnoreCase)).Display);
            Assert.Equal("Qxbeta", svm.Accounts.Accounts.Single(a => a.Label == "Qxbeta").Shown);
        }
        finally { widget.Dispose(); }
    });
}
```

Three seams are named here:
- `AccountsViewModel.RenamePrefill(string)` and `RenameError(string label, Exception)` (internal static, pure)
- `AccountsViewModel.LastQuestion`, set by `ConfirmClear` and `ConfirmRemove` before the hook or the dialog
- `McpConsentDialog.DetailsFor(string launcherPath, string configPath, IEnumerable<string> folders)` (internal static: the text `UpdateDetails` sets)

Step 2 adds each first as a stub that returns the real text.

In `windows-dotnet/tests/Sanduhr.App.Tests/ClaudeCodeLinkTests.cs`, replace

```csharp
            Assert.Equal(["None", "Work", "Personal", "Spare"], page.AccountOptions);
```

with

```csharp
            Assert.Equal(["None", "Work", "Personal", "Spare"], page.AccountOptions.Select(o => o.Label));
```

- [ ] **Step 2: Run them to see them fail**

Add the stubs, plus `AccountItemViewModel.Shown => Label`, `AccountChoice` holding the label as its `Shown`, `CanShowRealName => false`, an empty `ShowRealName` command, and `StreamerMode.ShowFolder`/`FoldersIn` returning `ClaudeCodeFolders.Display` and the text unchanged.

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~StreamerAccountsAndFoldersTests > %TEMP%\slice7-t9-red.log 2>&1`
Expected: a behavioural RED. Every test fails with a sentinel on screen, for example `row: "Qxalpha"`, `title: "~\.claude-qxwork"`, `plugin: "qxplugin@qxmarket…"` and `status: "Couldn't write C:\…\.claude-qxwork…"`. Record them.

- [ ] **Step 3: Mask each choke point**

`StreamerMode.cs`: add after `Reset()`:

```csharp

    /// <summary>A Claude Code folder as every page shows it (CP 7): its codename while streamer mode hides it, else <c>~\…</c> under
    /// <paramref name="profileDir"/> (the page's own profile folder, which tests point at a scratch one).</summary>
    public static string ShowFolder(string path, string profileDir) =>
        Mask.HidesFolder(path) ? Mask.Folder(path) : ClaudeCodeFolders.Display(path, profileDir);

    /// <summary>Status and error text that may name folders by full path (CP 8): each of <paramref name="folderPaths"/> shows as it does on
    /// its card, and any other path under the user folder shows as <c>%USERPROFILE%</c>. Off, the text is unchanged.</summary>
    public static string FoldersIn(string text, IEnumerable<string> folderPaths)
    {
        if (!Mask.On || string.IsNullOrEmpty(text))
            return text;
        foreach (var path in folderPaths.Select(ClaudeCodeFolders.Norm).Where(p => p.Length > 0).OrderByDescending(p => p.Length))
            if (Mask.HidesFolder(path))
                text = text.Replace(path, Mask.Folder(path), StringComparison.OrdinalIgnoreCase);
        return Mask.Paths(text);
    }
```

`ClaudeCodeHost.cs` (CP 7, the one lever for 25 sites). Replace

```csharp
    public string Display(string path) => ClaudeCodeFolders.Display(path, Profile);
```

with

```csharp
    /// <remarks>Streamer mode (slice 7, CP 7): the folder's codename while hidden. Cards, consents, Mods &amp; Widgets' meters rows and
    /// Privacy's folder-reads row all read their folder names here.</remarks>
    public string Display(string path) => StreamerMode.ShowFolder(path, Profile);
```

`AccountsViewModel.cs` (CP 1, CP 25, the bypass). Replace

```csharp
    public string Label { get; }

    [ObservableProperty] private bool _isActive;

    public AccountItemViewModel(string label, bool isActive)
    {
        Label = label;
        _isActive = isActive;
    }
```

with

```csharp
    public string Label { get; }

    /// <summary>What the row shows: the label, or its codename while streamer mode hides it (slice 7, CP 1). <see cref="Label"/> stays the key.</summary>
    public string Shown { get; }

    [ObservableProperty] private bool _isActive;

    public AccountItemViewModel(string label, bool isActive)
    {
        Label = label;
        Shown = StreamerMode.Mask.Name(CodenameCategory.Account, label);
        _isActive = isActive;
    }
```

and replace

```csharp
    public override string ToString() => IsActive ? Label + ", active" : Label;
```

with

```csharp
    public override string ToString() => IsActive ? Shown + ", active" : Shown;
```

Replace the constructor's default

```csharp
        _displayFolder = displayFolder ?? (path => ClaudeCodeFolders.Display(path, ClaudeCodeProfile.Dir));
```

with

```csharp
        _displayFolder = displayFolder ?? (path => StreamerMode.ShowFolder(path, ClaudeCodeProfile.Dir));   // slice 7: CP 7's bypass
```

In `Rename`, replace

```csharp
        var prompt = new TextPromptWindow("Rename account", "New name", item.Label);
```

with

```csharp
        var prompt = new TextPromptWindow("Rename account", "New name", RenamePrefill(item.Label));
```

and replace

```csharp
            ShowError($"Couldn't rename: {ex.Message}");
```

with

```csharp
            ShowError(RenameError(item.Label, ex));
```

and add after `Rename`:

```csharp

    /// <summary>The Rename prompt's text: empty while streamer mode hides the name, so a codename can never be saved as the name (decision 12).</summary>
    internal static string RenamePrefill(string label) => StreamerMode.Mask.Hides(CodenameCategory.Account, label) ? "" : label;

    /// <summary>The rename error, naming the account as the page does (CP 25). The new name is the person's own typing and stays.</summary>
    internal static string RenameError(string label, Exception ex) =>
        $"Couldn't rename: {StreamerMode.Mask.Paths(ex.Message.Replace(label, StreamerMode.Mask.Name(CodenameCategory.Account, label), StringComparison.Ordinal))}";
```

Replace

```csharp
    public string ClearHistoryName => SettingsNames.ClearAccountHistoryNamed(SelectedAccount?.Label ?? "");
    public string RemoveName => SettingsNames.RemoveAccountNamed(SelectedAccount?.Label ?? "");
```

with

```csharp
    public string ClearHistoryName => SettingsNames.ClearAccountHistoryNamed(SelectedAccount?.Shown ?? "");
    public string RemoveName => SettingsNames.RemoveAccountNamed(SelectedAccount?.Shown ?? "");

    /// <summary>The question last asked (clear or remove), as shown; tests read it.</summary>
    internal string? LastQuestion { get; private set; }
```

In `RefreshData`, replace

```csharp
        RemoveCaption = label is null ? "" : SettingsNames.RemoveAccountDetail(label);
```

with

```csharp
        RemoveCaption = label is null ? "" : SettingsNames.RemoveAccountDetail(SelectedAccount!.Shown);
```

In `ClearHistory`, replace

```csharp
        Info(SettingsNames.HistoryCleared(item.Label));
```

with

```csharp
        Info(SettingsNames.HistoryCleared(item.Shown));
```

and replace the two confirm helpers

```csharp
    private bool ConfirmClear(string label) => ConfirmClearHook is { } hook
        ? hook(label)
        : ThemedDialog.ShowConfirm(_owner, SettingsNames.ClearHistoryQuestion(label), SettingsNames.ClearHistoryDetail,
            SettingsNames.ClearHistoryConfirm, SettingsNames.ClearHistoryCancel);

    private bool ConfirmRemove(string label) => ConfirmRemoveHook is { } hook
        ? hook(label)
        : ThemedDialog.ShowConfirm(_owner, SettingsNames.RemoveAccountQuestion(label), SettingsNames.RemoveAccountDetail(label),
            SettingsNames.RemoveAccountConfirm, SettingsNames.RemoveAccountCancel);
```

with

```csharp
    // The questions name the account as the row does (slice 7, CP 1); the hooks and the action keep the real label.
    private bool ConfirmClear(string label)
    {
        var shown = StreamerMode.Mask.Name(CodenameCategory.Account, label);
        LastQuestion = SettingsNames.ClearHistoryQuestion(shown);
        return ConfirmClearHook is { } hook
            ? hook(label)
            : ThemedDialog.ShowConfirm(_owner, LastQuestion, SettingsNames.ClearHistoryDetail,
                SettingsNames.ClearHistoryConfirm, SettingsNames.ClearHistoryCancel);
    }

    private bool ConfirmRemove(string label)
    {
        var shown = StreamerMode.Mask.Name(CodenameCategory.Account, label);
        LastQuestion = SettingsNames.RemoveAccountQuestion(shown);
        return ConfirmRemoveHook is { } hook
            ? hook(label)
            : ThemedDialog.ShowConfirm(_owner, LastQuestion, SettingsNames.RemoveAccountDetail(shown),
                SettingsNames.RemoveAccountConfirm, SettingsNames.RemoveAccountCancel);
    }
```

`SettingsWindow.xaml`, the Accounts row. Replace

```xml
                                                        <TextBlock Grid.Column="1" Text="{Binding Label}"
                                                                   Foreground="{DynamicResource Sanduhr.Brush.Text}" FontSize="13"
```

with

```xml
                                                        <TextBlock Grid.Column="1" Text="{Binding Shown}"
                                                                   Foreground="{DynamicResource Sanduhr.Brush.Text}" FontSize="13"
```

`ClaudeCodeViewModel.cs` (CP 6). Add before the `/// <summary>The three pieces a folder card installs` comment (Read the file: it is the line `internal enum CcPieceKind { Statusline, Mcp, Hooks }`'s summary):

```csharp
/// <summary>A linked-account choice (slice 7, CP 6): the label is the value the combo writes; <see cref="Shown"/> is what it shows and
/// says, the codename while streamer mode hides it. Updated in place, so a reload never clears a live selection (WF2).</summary>
public sealed partial class AccountChoice : ObservableObject
{
    public AccountChoice(string label, string shown)
    {
        Label = label;
        _shown = shown;
    }

    public string Label { get; }

    [ObservableProperty] private string _shown;

    public override string ToString() => Shown;
}

```

In `CcFolderRowViewModel`, replace

```csharp
    public ObservableCollection<string> AccountOptions => _owner.AccountOptions;
```

with

```csharp
    public ObservableCollection<AccountChoice> AccountOptions => _owner.AccountOptions;

    /// <summary>Streamer mode hides this folder's name: the card's menu offers "Show the real name on stream" (decision 16).</summary>
    public bool CanShowRealName => StreamerMode.Mask.HidesFolder(Path);

    [RelayCommand]
    private void ShowRealName() => _owner.ShowRealName(this);
```

In `ClaudeCodeViewModel`, replace

```csharp
    public ObservableCollection<string> AccountOptions { get; } = new();
```

with

```csharp
    public ObservableCollection<AccountChoice> AccountOptions { get; } = new();

    /// <summary>A card's quick route (decision 16): the folder's real name shows on stream; every masked page reloads through the service.</summary>
    internal void ShowRealName(CcFolderRowViewModel row) =>
        _widget.Streamer.SetShown(CodenameCategory.Folder, StreamerMask.FolderKey(row.Path), true);
```

and replace `SyncAccountOptions`'s body

```csharp
        var wanted = new List<string> { NoAccount };
        wanted.AddRange(_widget.ShownAccounts);
        for (int i = AccountOptions.Count - 1; i >= 0; i--)
            if (!wanted.Contains(AccountOptions[i])) AccountOptions.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            if (i < AccountOptions.Count && AccountOptions[i] == wanted[i]) continue;
            var at = AccountOptions.IndexOf(wanted[i]);
            if (at >= 0) AccountOptions.Move(at, i);
            else AccountOptions.Insert(i, wanted[i]);
        }
```

with

```csharp
        var wanted = new List<string> { NoAccount };
        wanted.AddRange(_widget.ShownAccounts);
        for (int i = AccountOptions.Count - 1; i >= 0; i--)
            if (!wanted.Contains(AccountOptions[i].Label)) AccountOptions.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            // Streamer mode (slice 7, CP 6): the choice shows the codename; its value stays the label.
            var shown = wanted[i] == NoAccount ? NoAccount : StreamerMode.Mask.Name(CodenameCategory.Account, wanted[i]);
            int at = -1;
            for (int j = 0; j < AccountOptions.Count; j++)
                if (AccountOptions[j].Label == wanted[i]) { at = j; break; }
            if (at < 0) AccountOptions.Insert(i, new AccountChoice(wanted[i], shown));
            else
            {
                if (at != i) AccountOptions.Move(at, i);
                AccountOptions[i].Shown = shown;
            }
        }
```

Replace the CP 8 shortener

```csharp
    private string Shorten(string text) =>
        string.IsNullOrEmpty(text) ? text : text.Replace(Profile, "~", StringComparison.OrdinalIgnoreCase);
```

with

```csharp
    private string Shorten(string text) =>
        string.IsNullOrEmpty(text) ? text
            : StreamerMode.On ? StreamerMode.FoldersIn(text, KnownFolders().Select(f => f.Path))   // slice 7, CP 8
            : text.Replace(Profile, "~", StringComparison.OrdinalIgnoreCase);
```

In the MCP install (CP 7's bypass, CP 11), replace

```csharp
                .Select(f => ClaudeCodeFolders.Display(f.Path, Profile));
```

with

```csharp
                .Select(f => StreamerMode.ShowFolder(f.Path, Profile));
```

In the statusline consent (CP 9), replace

```csharp
                ? ConfigMask.MaskString(cmd) : null;
```

with

```csharp
                ? StreamerMode.Mask.Paths(ConfigMask.MaskString(cmd)) : null;
```

`McpConsentDialog.xaml.cs` (CP 11, CP 12). In the constructor's loop, replace

```csharp
            var rb = new RadioButton
            {
                Content = home,
```

with

```csharp
            var rb = new RadioButton
            {
                Content = StreamerMode.Mask.FolderName(home),   // slice 7, CP 11: Tag keeps the real folder
```

and replace

```csharp
            var cb = new CheckBox
            {
                Content = home,
                IsChecked = false,   // burn-attribution consent is NEVER pre-answered
```

with

```csharp
            var cb = new CheckBox
            {
                Content = StreamerMode.Mask.FolderName(home),   // slice 7, CP 11: _rootChecks keeps the real folder
                IsChecked = false,   // burn-attribution consent is NEVER pre-answered
```

In `UpdateDetails`, replace

```csharp
        DetailsText.Text =
            $"What gets written: the server files under %USERPROFILE%\\.sanduhr\\mcp\\, a launcher at {launcherPath}, " +
```

with

```csharp
        DetailsText.Text = DetailsFor(launcherPath, configPathFor(home), [Path.Combine(ClaudeCodeProfile.Dir, home)]);
    }

    /// <summary>The consent's details (slice 7, CP 12): the launcher's and the config's full paths, with the chosen folder as its codename
    /// and the user folder as %USERPROFILE% while streamer mode is on.</summary>
    internal static string DetailsFor(string launcherPath, string configPath, IEnumerable<string> folders)
    {
        launcherPath = StreamerMode.Mask.Paths(launcherPath);
        configPath = StreamerMode.FoldersIn(configPath, folders);
        return
            $"What gets written: the server files under %USERPROFILE%\\.sanduhr\\mcp\\, a launcher at {launcherPath}, " +
```

then, in the same expression, replace

```csharp
            $"and a 'sanduhr' entry in {configPathFor(home)} (a timestamped backup is saved beside it first). " +
```

with

```csharp
            $"and a 'sanduhr' entry in {configPath} (a timestamped backup is saved beside it first). " +
```

The expression's last line, `"Remove any time with the Remove button in this tab; removal reverts everything.";`, now ends `DetailsFor`, and the method's closing brace follows it. `UpdateDetails` keeps its own braces. Read the method after the Edit and check that it compiles as two methods.

`FolderHoldsViewModel.cs` (CP 9, CP 10). Replace

```csharp
            StatuslineCommand = sl.Command;
```

with

```csharp
            StatuslineCommand = StreamerMode.Mask.Paths(sl.Command);   // slice 7, CP 9
```

replace

```csharp
            PluginLines.Add(new ModsLine(name, on ? SettingsNames.FolderOn : SettingsNames.FolderOff));
```

with

```csharp
            PluginLines.Add(new ModsLine(StreamerMode.Mask.AddOn(name), on ? SettingsNames.FolderOn : SettingsNames.FolderOff));   // CP 10
```

and replace

```csharp
        foreach (var n in report.Mcp.UserServers) McpNames.Add(n);
```

with

```csharp
        foreach (var n in report.Mcp.UserServers) McpNames.Add(StreamerMode.Mask.AddOn(n));   // slice 7, CP 10
```

`ClaudeCodePage.xaml`, the combo (CP 6). Replace

```xml
                                        <ComboBox Style="{DynamicResource ThemedComboBox}" Width="170" ItemsSource="{Binding AccountOptions}"
                                                  SelectedItem="{Binding LinkedAccount, Mode=TwoWay}" IsEnabled="{Binding LinkEnabled}"
```

with

```xml
                                        <ComboBox Style="{DynamicResource ThemedComboBox}" Width="170" ItemsSource="{Binding AccountOptions}"
                                                  DisplayMemberPath="Shown" SelectedValuePath="Label"
                                                  SelectedValue="{Binding LinkedAccount, Mode=TwoWay}" IsEnabled="{Binding LinkEnabled}"
```

The card's quick route (decision 16). Replace

```xml
                            <Border Background="{DynamicResource Sanduhr.Brush.Bg}" CornerRadius="6" Padding="12,10" Margin="0,0,0,10"
                                    BorderBrush="{DynamicResource Sanduhr.Brush.Border}" BorderThickness="1">
                                <StackPanel>
```

with

```xml
                            <Border Background="{DynamicResource Sanduhr.Brush.Bg}" CornerRadius="6" Padding="12,10" Margin="0,0,0,10"
                                    BorderBrush="{DynamicResource Sanduhr.Brush.Border}" BorderThickness="1"
                                    ContextMenuService.IsEnabled="{Binding CanShowRealName}">
                                <!-- Streamer mode's quick route (decision 16): Shift+F10 or the Menu key on any control in the card opens it. -->
                                <Border.ContextMenu>
                                    <ContextMenu>
                                        <MenuItem Header="{x:Static core:SettingsNames.ShowRealNameOnStream}" Command="{Binding ShowRealNameCommand}" />
                                    </ContextMenu>
                                </Border.ContextMenu>
                                <StackPanel>
```

Check the Border anchor with `git grep -c -F` on its first line, and Read the block: `ClaudeCodePage.xaml` has one folder-card template.

Add `using Sanduhr.App.Services;` to each edited file that lacks it, and `using Sanduhr.Core;` where `CodenameCategory` is new.

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~StreamerAccountsAndFoldersTests|FullyQualifiedName~ClaudeCode|FullyQualifiedName~Accounts|FullyQualifiedName~Privacy|FullyQualifiedName~McpConsent|FullyQualifiedName~Mods" > %TEMP%\slice7-t9.log 2>&1`
Expected: PASS. `ClaudeCodeLinkTests` (WF2) still passes, with the combo's value bound by `SelectedValue`: a reload never writes `SetAccountFolder(null, …)`. If `ReloadingTwiceKeepsTheLink` or `NoNullWrite…` fails, the cause is the combo writing null while its items move; the guard in `OnLinkedAccountChanged` already ignores null, so look for a missing `_loading` around the in-place `Shown` update. Fix it there, not in the test.

- [ ] **Step 5: Add the pages to `ReloadNames`**

Nothing to add: `ReloadNames` already reloads Accounts, the Claude Code page and Privacy (Task 6). `Shuffle_renames_every_open_page_and_keeps_the_allowlist` is the proof.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Services/StreamerMode.cs windows-dotnet/src/Sanduhr.App/ViewModels/AccountsViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeHost.cs windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/FolderHoldsViewModel.cs windows-dotnet/src/Sanduhr.App/Views/McpConsentDialog.xaml.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/Views/ClaudeCodePage.xaml windows-dotnet/tests/Sanduhr.App.Tests/ClaudeCodeLinkTests.cs windows-dotnet/tests/Sanduhr.App.Tests/StreamerAccountsAndFoldersTests.cs
git commit -m "feat(settings-v2): streamer mode on Accounts, the Claude Code cards and their dialogs, and Privacy's folder names"
```

---

### Task 10: Mods & Widgets and the Desk's companion picker

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/ModsPageViewModel.cs` (CP 13, CP 14, CP 8's `Short`, the row's quick route)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/DeskCompanionViewModel.cs` (CP 24)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (`ModsPage`'s setter wires the route; `ReloadNames`)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/ModsPage.xaml` (`Source` in place of `Item.Source` where bound; the row's menu)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/StreamerModsTests.cs`

**Interfaces:**
- Consumes: `StreamerMode.Mask.Name/AddOn`, `StreamerMode.FoldersIn`, `WidgetViewModel.Streamer.SetShown`.
- Produces:
  - `InstalledRowViewModel.Name` (masked), `Source` (masked marketplace), `Description` (empty when the name is hidden), `CanShowRealName`, `ShowRealNameCommand`
  - `ModsPageViewModel.ShowRealName` (`Action<IReadOnlyList<string>>?`, wired by Settings)

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/StreamerModsTests.cs`:

```csharp
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 7: Mods &amp; Widgets' rows and the Desk's companion picker (spec, Reach inside Settings, Mods &amp; Widgets;
/// CPs 13, 14, 24). Catalog packs are public and keep their names; plugins, marketplaces and packs made here are masked.</summary>
[Collection("SettingsWindow")]
public sealed class StreamerModsTests : AppWindowFixture
{
    private static InstalledItem Plugin(string name, string marketplace) =>
        new(name + "@" + marketplace, name, InstalledKind.ClaudeCodePlugin, null, marketplace, "1.0.0", null, [], true, SwitchRoute.None,
            SettingsNames.ReadOnlyClaudeCode, false, false, $"{name} does things for {marketplace}.");

    private static InstalledItem Pack(string name, bool madeHere) =>
        new("pack." + name, name, InstalledKind.WidgetPack, "sprite", madeHere ? SettingsNames.WidgetMadeHere : SettingsNames.SourceFromCatalog,
            "1.0.0", null, null, true, SwitchRoute.PackEnabled, null, madeHere, false, "A sprite.");

    [Fact]
    public void A_plugin_row_masks_its_name_marketplace_and_description_and_says_so_everywhere() => AppHost.Run(() =>
    {
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
        var row = InstalledRowViewModel.ForTests(Plugin("qxplugin", "qxmarket"));
        foreach (var (what, text) in new[]
        {
            ("name", row.Name), ("source", row.Source), ("detail", row.Detail), ("description", row.Description),
            ("spoken", row.RowName), ("where", row.WhereLinkName), ("uninstall", row.UninstallName),
        })
            Assert.False(text.Contains("qx", StringComparison.OrdinalIgnoreCase), $"{what}: \"{text}\"");
        Assert.False(row.HasDescription);
        Assert.True(row.CanShowRealName);
    });

    [Fact]
    public void A_pack_made_here_is_masked_and_a_catalog_pack_keeps_its_name() => AppHost.Run(() =>
    {
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
        var mine = InstalledRowViewModel.ForTests(Pack("Qxsprite", madeHere: true));
        Assert.DoesNotContain("Qxsprite", mine.Name + mine.SwitchName + mine.UseOwnArtName + mine.RowName);
        var catalog = InstalledRowViewModel.ForTests(Pack("Hourglass", madeHere: false));
        Assert.Equal("Hourglass", catalog.Name);
        Assert.False(catalog.CanShowRealName);
    });

    [Fact]
    public void Sanduhrs_own_mod_keeps_its_name() => AppHost.Run(() =>
    {
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
        Assert.Equal("sanduhr-meters@inline", StreamerMode.Mask.AddOn("sanduhr-meters@inline"));
    });

    [Fact]
    public void A_rows_quick_route_shows_its_names() => AppHost.Run(() =>
    {
        var book = CodenameBook.New();
        StreamerMode.Use(new StreamerMask(true, book, Profile));
        IReadOnlyList<string>? shown = null;
        var row = InstalledRowViewModel.ForTests(Plugin("qxplugin", "qxmarket"), names => shown = names);
        row.ShowRealNameCommand.Execute(null);
        Assert.Equal(["qxplugin", "qxmarket"], shown);
    });

    [Fact]
    public void The_companion_picker_masks_sprites_made_here() => AppHost.Run(() =>
    {
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
        Assert.Equal(StreamerMode.Mask.Name(CodenameCategory.AddOn, "Qxsprite"), DeskCompanionViewModel.ChoiceName("Qxsprite", madeHere: true));
        Assert.Equal("Hourglass", DeskCompanionViewModel.ChoiceName("Hourglass", madeHere: false));
    });

    [Fact]
    public void A_meters_status_line_shows_the_folder_by_codename() => AppHost.Run(() =>
    {
        Home(".claude-qxwork");
        var (widget, _) = ScratchWidget("Qxalpha");
        try
        {
            StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
            var page = new ModsPageViewModel(host: ClaudeCodeHost.For(widget));
            var line = page.ShortForTests($@"Couldn't update {Profile}\.claude-qxwork\settings.json", "fallback");
            Assert.DoesNotContain("qx", line, StringComparison.OrdinalIgnoreCase);
        }
        finally { widget.Dispose(); }
    });
}
```

The seams, added first as non-masking stubs for the RED:
- `InstalledRowViewModel.ForTests(InstalledItem item, Action<IReadOnlyList<string>>? showRealName = null)` (internal static): a row with a test owner.
- `DeskCompanionViewModel.ChoiceName(string name, bool madeHere)` (internal static).
- `ModsPageViewModel.ShortForTests(string message, string fallback) => Short(message, fallback)`.

Read the `InstalledItem` record's constructor (`Sanduhr.Core/InstalledItems.cs`, around line 30) before running: the helper above passes its fields in the order the record declares them at 3cc3699d (`Id, Name, Kind, PackKind, Source, Version, Update, Scope, Enabled, Route, ReadOnlyNote, MadeHere, IsMod, Description`, then the optional touches). If the order differs, fix the helpers, not the record.

`InstalledRowViewModel` takes its owner (`ModsPageViewModel`) in its constructor. For `ForTests`, build the owner with the test's host: `new ModsPageViewModel(host: null)` when the constructor allows a null host (Read it), and set its `ShowRealName` to the given action.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~StreamerModsTests > %TEMP%\slice7-t10-red.log 2>&1`
Expected: a behavioural RED: `name: "qxplugin"`, `found Qxsprite`, `ChoiceName … Actual: Qxsprite`, and the status line naming `.claude-qxwork`. `Sanduhrs_own_mod_keeps_its_name` passes already (Task 2's defaults).

- [ ] **Step 3: Mask each choke point**

`ModsPageViewModel.cs`, `InstalledRowViewModel` (CP 13, CP 14). Replace

```csharp
    public InstalledItem Item { get; }
    public string Id => Item.Id;
    public string Source => Item.Source;
    public string Name => Item.Name;
    public string Description => Item.Description;
```

with

```csharp
    public InstalledItem Item { get; }
    public string Id => Item.Id;

    /// <summary>The row's name as shown (slice 7, CP 13): a Claude Code plugin's or mod's name and a pack made here are codenames while
    /// streamer mode hides them; catalog packs are public and Sanduhr's own mod is allowlisted. Every name below is built from this.</summary>
    public string Name { get; }

    /// <summary>Where it came from: a plugin's marketplace is masked like its name; the source words ("from the catalog") stay.</summary>
    public string Source { get; }

    /// <summary>The author's text, which names the plugin: hidden while its name is (slice 7, CP 14).</summary>
    public string Description => HidesName ? "" : Item.Description;

    private bool HidesName => Name != Item.Name;
    private bool IsClaudeCodeItem => Item.Kind is InstalledKind.ClaudeCodeMod or InstalledKind.ClaudeCodePlugin;

    /// <summary>Streamer mode hides this row's name: its menu offers "Show the real name on stream" (decision 16).</summary>
    public bool CanShowRealName => HidesName || Source != Item.Source;

    [RelayCommand]
    private void ShowRealName() =>
        _owner.ShowRealName?.Invoke([.. new[] { Item.Name, IsClaudeCodeItem && Source != Item.Source ? Item.Source : null }.OfType<string>()]);
```

In the constructor, replace

```csharp
        _owner = owner;
        Item = item;
```

with

```csharp
        _owner = owner;
        Item = item;
        Name = item.Kind switch
        {
            InstalledKind.WidgetPack when item.MadeHere => StreamerMode.Mask.Name(CodenameCategory.AddOn, item.Name),
            InstalledKind.ClaudeCodeMod or InstalledKind.ClaudeCodePlugin => StreamerMode.Mask.AddOn(item.Name),
            _ => item.Name,
        };
        Source = item.Kind is InstalledKind.ClaudeCodeMod or InstalledKind.ClaudeCodePlugin
                 && item.Source != SettingsNames.SourceSkillsFolder && item.Source != SettingsNames.SourcePluginFolder
            ? StreamerMode.Mask.AddOn(item.Source)
            : item.Source;
```

Replace

```csharp
    public string Detail => string.Join(" · ", new[] { KindWord, Item.Version ?? "", Item.Source }.Where(s => s.Length > 0));
```

with

```csharp
    public string Detail => string.Join(" · ", new[] { KindWord, Item.Version ?? "", Source }.Where(s => s.Length > 0));
```

and in `RowName`, replace

```csharp
        ? string.Join(", ", new[] { Name, Item.PackKind ?? "", "version " + Item.Version, Item.Source, Enabled ? SettingsNames.RowOn : SettingsNames.RowOff }
```

with

```csharp
        ? string.Join(", ", new[] { Name, Item.PackKind ?? "", "version " + Item.Version, Source, Enabled ? SettingsNames.RowOn : SettingsNames.RowOff }
```

In `ModsPageViewModel`, add beside the other wiring hooks (Read the class for `SettingsChanged`'s declaration and add after it):

```csharp

    /// <summary>Set by Settings (slice 7): a row's "Show the real name on stream" shows these add-on names through the widget's service.</summary>
    public Action<IReadOnlyList<string>>? ShowRealName { get; set; }
```

and replace CP 8's shortener

```csharp
    private string Short(string message, string fallback) =>
        string.IsNullOrWhiteSpace(message) ? fallback
            : _host is null ? message : message.Replace(_host.Profile, "~", StringComparison.OrdinalIgnoreCase);
```

with

```csharp
    private string Short(string message, string fallback) =>
        string.IsNullOrWhiteSpace(message) ? fallback
            : _host is null ? StreamerMode.Mask.Paths(message)
            : StreamerMode.On ? StreamerMode.FoldersIn(message, _host.Folders().Select(f => f.Path))   // slice 7, CP 8
            : message.Replace(_host.Profile, "~", StringComparison.OrdinalIgnoreCase);
```

`ModsPage.xaml`, the row's quick route (the row is the focusable `ListBoxItem`, so Shift+F10 opens it). Replace

```xml
            <Setter Property="AutomationProperties.Name" Value="{Binding RowName}" />
```

with

```xml
            <Setter Property="AutomationProperties.Name" Value="{Binding RowName}" />
            <!-- Streamer mode's quick route (decision 16): only while the row's name is hidden. -->
            <Setter Property="ContextMenuService.IsEnabled" Value="{Binding CanShowRealName}" />
            <Setter Property="ContextMenu">
                <Setter.Value>
                    <ContextMenu>
                        <MenuItem Header="{x:Static core:SettingsNames.ShowRealNameOnStream}" Command="{Binding ShowRealNameCommand}" />
                    </ContextMenu>
                </Setter.Value>
            </Setter>
```

Check that `AutomationProperties.Name" Value="{Binding RowName}"` occurs once in `ModsPage.xaml`. If the file binds `Item.Source` anywhere (`git grep -n "Item.Source" -- windows-dotnet/src/Sanduhr.App/Views/ModsPage.xaml`), change that binding to `Source`.

`DeskCompanionViewModel.cs` (CP 24). Replace

```csharp
                    if (row.Enabled) Companions.Add(new CompanionChoice(row.Id, info.Name));
```

with

```csharp
                    if (row.Enabled) Companions.Add(new CompanionChoice(row.Id, ChoiceName(info.Name, row.MadeHere)));
```

and add to the class:

```csharp

    /// <summary>A sprite's name in the picker (slice 7, CP 24): a sprite made here is a codename while streamer mode hides it; catalog
    /// sprites are public.</summary>
    internal static string ChoiceName(string name, bool madeHere) =>
        madeHere ? StreamerMode.Mask.Name(CodenameCategory.AddOn, name) : name;
```

`SettingsViewModel.cs`. In the `ModsPage` setter, replace

```csharp
            value.MetersChanged = installed => ClaudeCodePage.AfterMetersChange(installed);
```

with

```csharp
            value.MetersChanged = installed => ClaudeCodePage.AfterMetersChange(installed);
            value.ShowRealName = names =>
            {
                foreach (var name in names)
                    _widget.Streamer.SetShown(CodenameCategory.AddOn, name, true);
            };
```

The setter needs the widget. If `SettingsViewModel` keeps no `_widget` field, add `private readonly WidgetViewModel _widget;` and set it first in the constructor (`_widget = widget;`), before `ModsPage = …` runs. Read the constructor to place it.

In `ReloadNames`, replace

```csharp
        Privacy.Reload();
        NamesReloaded++;
```

with

```csharp
        Privacy.Reload();
        ModsPage.ReloadLocal();
        Desk.Companion.Reload();
        NamesReloaded++;
```

Add the `using`s each edited file lacks (`Sanduhr.App.Services`, `Sanduhr.Core`).

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~StreamerModsTests|FullyQualifiedName~Mods|FullyQualifiedName~DeskCompanion|FullyQualifiedName~InstalledRow" > %TEMP%\slice7-t10.log 2>&1`
Expected: PASS. With streamer mode off, every row reads as before: `Name == Item.Name` and `Source == Item.Source`.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/ModsPageViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/DeskCompanionViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.App/Views/ModsPage.xaml windows-dotnet/tests/Sanduhr.App.Tests/StreamerModsTests.cs
git commit -m "feat(settings-v2): streamer mode on Mods & Widgets and the companion picker; catalog packs keep their names"
```

---

### Task 11: Claude Usage: Meters, the exports, Overview, Trends, Sessions and the session archive

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/HistoryTabViewModel.cs` (CP 3, CP 4, `RefreshNames`)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/LocalCcViewModel.cs` (`BreakdownRow`, CP 15, CP 11's archive rows and question)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/CcTrendsViewModel.cs` (CP 16)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/CcLedgerViewModel.cs` (CP 17)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (the quick route; `ReloadNames`)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (the archive rows, the export captions, the tables' menus)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/StreamerUsageTests.cs`

**Interfaces:**
- Consumes: `StreamerMode.Mask.Name/FolderName/Paths`, `WidgetViewModel.Streamer.SetShown`.
- Produces:
  - `public sealed record AccountOption(string Display, string? Account)` gains `ToString() => Display`
  - `BreakdownRow(string Name, string Tokens, string Real = "", CodenameCategory Category = CodenameCategory.Project)`, with `CanShowRealName`
  - `HistoryTabViewModel.ExportFileName(string? account, DateTime day)`, `ExportedMessage(int rows, string path)` (internal static)
  - `LocalCcViewModel.Rows(IEnumerable<(string, long)>, CodenameCategory)` (internal static), `VaultRootToggleViewModel.Shown`
  - `LedgerRowViewModel.ProjectText` masked, `CwdTooltip` null while on, the detail's folders and skills masked
  - `ClaudeCodeTabViewModel.ShowRealName` (`Action<CodenameCategory, string>?`), `StreamerOn` (for the export captions)

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/StreamerUsageTests.cs`:

```csharp
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 7: Claude Usage (spec, Reach inside Settings, Claude Usage; CPs 3, 4, 11, 15, 16, 17). Exports keep real
/// names; what shows on screen does not.</summary>
[Collection("SettingsWindow")]
public sealed class StreamerUsageTests : AppWindowFixture
{
    private static void On(string profile) => StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), profile));

    private static void NoSentinel(string where, string? text) =>
        Assert.False((text ?? "").Contains("qx", StringComparison.OrdinalIgnoreCase), $"{where}: \"{text}\"");

    [Fact]
    public void The_meters_picker_and_legend_name_accounts_by_codename_and_keep_the_real_label_as_the_value() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha", "Qxbeta");
        try
        {
            widget.Streamer.SetOn(true);
            var history = new HistoryTabViewModel(widget);
            history.RefreshNames();
            foreach (var o in history.AccountOptions)
            {
                NoSentinel("option", o.Display);
                NoSentinel("option spoken", o.ToString());
            }
            Assert.Contains(history.AccountOptions, o => o.Account == "Qxalpha");
            history.SelectedOption = history.AccountOptions[0];   // All accounts: the legend lists every account
            Assert.All(history.Legend, l => NoSentinel("legend", l.Label + l.ToString()));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void An_exports_file_name_and_its_result_show_no_real_name() => AppHost.Run(() =>
    {
        On(Profile);
        NoSentinel("file name", HistoryTabViewModel.ExportFileName("Qxalpha", new DateTime(2026, 10, 26)));
        Assert.StartsWith("Sanduhr-usage-", HistoryTabViewModel.ExportFileName("Qxalpha", new DateTime(2026, 10, 26)));
        Assert.Equal("Sanduhr-usage-all-accounts-2026-10-26.csv", HistoryTabViewModel.ExportFileName(null, new DateTime(2026, 10, 26)));
        var message = HistoryTabViewModel.ExportedMessage(12, $@"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\Documents\x.csv");
        Assert.DoesNotContain(Environment.UserName, message, StringComparison.OrdinalIgnoreCase);
    });

    [Fact]
    public void Exports_say_they_keep_real_names_only_while_streamer_mode_is_on() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        try
        {
            var history = new HistoryTabViewModel(widget);
            Assert.False(history.ExportsKeepRealNamesShown);
            widget.Streamer.SetOn(true);
            history.RefreshNames();
            Assert.True(history.ExportsKeepRealNamesShown);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Overview_and_trends_rows_are_codenames_and_remember_their_real_names() => AppHost.Run(() =>
    {
        On(Profile);
        var rows = LocalCcViewModel.Rows([("qxproject", 1200L), ("qxother", 900L)], CodenameCategory.Project);
        Assert.All(rows, r => NoSentinel("row", r.Name + r.ToString()));
        Assert.Equal(["qxproject", "qxother"], rows.Select(r => r.Real));
        Assert.All(rows, r => Assert.True(r.CanShowRealName));
        var skills = LocalCcViewModel.Rows([("qxskill", 10L)], CodenameCategory.Skill);
        Assert.Equal(StreamerMode.Mask.Name(CodenameCategory.Skill, "qxskill"), skills[0].Name);
        // Overview's and Trends' top projects are one name in two places: one codename.
        Assert.Equal(rows[0].Name, CcTrendsViewModel.Rows([("qxproject", 5L)])[0].Name);
    });

    [Fact]
    public void A_session_row_names_its_project_skills_and_folder_by_codename_and_drops_the_full_path() => AppHost.Run(() =>
    {
        On(Profile);
        var info = LedgerFixtures.Session(project: "qxproject", projectKey: "C--Users-qxuser-qxproject", root: ".claude-qxwork",
            cwd: $@"{Profile}\qxproject", skills: ["qxskill"]);
        var row = new LedgerRowViewModel(info.ProjectKey, info, DateOnly.MinValue, DateOnly.MaxValue, disambiguate: false);
        row.IsExpanded = true;
        NoSentinel("project", row.ProjectText);
        NoSentinel("spoken", row.ToString());
        NoSentinel("detail", row.DetailText);
        Assert.Null(row.CwdTooltip);
        var twin = new LedgerRowViewModel(info.ProjectKey, info, DateOnly.MinValue, DateOnly.MaxValue, disambiguate: true);
        NoSentinel("disambiguated", twin.ProjectText);
        Assert.NotEqual(row.ProjectText, twin.ProjectText);   // two projects that share a name keep two codenames
    });

    [Fact]
    public void The_session_archives_folders_are_codenames_and_keep_their_keys() => AppHost.Run(() =>
    {
        On(Profile);
        var toggle = new VaultRootToggleViewModel(null!, ".claude-qxwork", true);
        NoSentinel("archive", toggle.Shown + toggle.ToString());
        Assert.Equal(".claude-qxwork", toggle.Name);
        NoSentinel("stop question", LocalCcViewModel.StopArchivingQuestion(".claude-qxwork"));
    });
}
```

`LedgerFixtures.Session` builds a `VaultSessionInfo`. Read `Sanduhr.Core/VaultModels.cs` for its constructor and the existing ledger tests (`git grep -l "new LedgerRowViewModel" -- windows-dotnet/tests`) for a builder to reuse. If one exists, use it and drop `LedgerFixtures`. If none does, add `internal static class LedgerFixtures` at the end of this test file, building a `VaultSessionInfo` with those fields and empty totals.

The seams, added first as stubs that return real text for the RED: `AccountOption.ToString`, `HistoryTabViewModel.RefreshNames`, `ExportFileName`, `ExportedMessage`, `ExportsKeepRealNamesShown`, `LocalCcViewModel.Rows`, `CcTrendsViewModel.Rows`, `BreakdownRow.Real`/`CanShowRealName`, `VaultRootToggleViewModel.Shown` and `LocalCcViewModel.StopArchivingQuestion`.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~StreamerUsageTests > %TEMP%\slice7-t11-red.log 2>&1`
Expected: a behavioural RED: `option: "Qxalpha"`, `file name: "Sanduhr-usage-qxalpha-2026-10-26.csv"`, `row: "qxproject"`, `project: "qxproject"` and `archive: ".claude-qxwork"`. Record them.

- [ ] **Step 3: Mask each choke point**

`HistoryTabViewModel.cs` (CP 3, CP 4). Replace

```csharp
public sealed record AccountOption(string Display, string? Account);
```

with

```csharp
public sealed record AccountOption(string Display, string? Account)
{
    /// <summary>What a screen reader reads: the shown name, never the record's text, which would carry the real label (slice 7, CP 3).</summary>
    public override string ToString() => Display;
}
```

In `RefreshAccountOptions`, replace

```csharp
        foreach (var label in _widget.ShownAccounts)
            AccountOptions.Add(new AccountOption(label, label));
```

with

```csharp
        foreach (var label in _widget.ShownAccounts)
            AccountOptions.Add(new AccountOption(StreamerMode.Mask.Name(CodenameCategory.Account, label), label));   // slice 7, CP 3
```

In `RefreshLegend`, replace

```csharp
        foreach (var label in accounts)
            Legend.Add(new LegendItem(label, AccountColors.BrushFor(label, accounts)));
```

with

```csharp
        // The swatch keeps its colour by the real label; the text is the codename while streamer mode hides it (slice 7, CP 3).
        foreach (var label in accounts)
            Legend.Add(new LegendItem(StreamerMode.Mask.Name(CodenameCategory.Account, label), AccountColors.BrushFor(label, accounts)));
```

Add after `RefreshLegend`:

```csharp

    /// <summary>Streamer mode moved (slice 7): the picker, the legend and the export caption read their names again.</summary>
    public void RefreshNames()
    {
        RefreshAccountOptions();
        RefreshLegend();
        OnPropertyChanged(nameof(ExportsKeepRealNamesShown));
    }

    /// <summary>"Exports keep real names." beside the export button, while streamer mode is on (spec).</summary>
    public bool ExportsKeepRealNamesShown => StreamerMode.On;

    /// <summary>The Save dialog's default name (slice 7, CP 4): the account's codename while hidden, never its label. The file itself keeps
    /// real names (spec).</summary>
    internal static string ExportFileName(string? account, DateTime day)
    {
        string scopeTag = account is null
            ? "all-accounts"
            : StreamerMode.Mask.Name(CodenameCategory.Account, account).ToLowerInvariant().Replace(" ", "-");
        return $"Sanduhr-usage-{scopeTag}-{day:yyyy-MM-dd}.csv";
    }

    /// <summary>"Wrote N rows to:" with the path's user folder as %USERPROFILE% while streamer mode is on (slice 7, CP 4).</summary>
    internal static string ExportedMessage(int rows, string path) => $"Wrote {rows} rows to:\n{StreamerMode.Mask.Paths(path)}";
```

In `ExportCsv`, replace

```csharp
        string? account = SelectedOption?.Account;
        string scopeTag = account is null
            ? "all-accounts"
            : account.ToLowerInvariant().Replace(" ", "-");
        string defaultName = $"Sanduhr-usage-{scopeTag}-{DateTime.Today:yyyy-MM-dd}.csv";
```

with

```csharp
        string? account = SelectedOption?.Account;
        string defaultName = ExportFileName(account, DateTime.Today);
```

replace

```csharp
            Info($"Wrote {result.RowCount} rows to:\n{dlg.FileName}");
```

with

```csharp
            Info(ExportedMessage(result.RowCount, dlg.FileName));
```

and replace

```csharp
            Info($"Could not write {dlg.FileName}:\n{e.Message}");
```

with

```csharp
            Info(StreamerMode.Mask.Paths($"Could not write {dlg.FileName}:\n{e.Message}"));
```

Off, `ExportFileName` returns exactly the old name (`Mask.Name` returns the label), so the existing export test holds.

`LocalCcViewModel.cs` (CP 15, CP 11). Replace

```csharp
public sealed record BreakdownRow(string Name, string Tokens)
{
    /// <summary>What a screen reader reads for the row: "my-project, 1.2M".</summary>
    public override string ToString() => $"{Name}, {Tokens}";
}
```

with

```csharp
public sealed record BreakdownRow(string Name, string Tokens, string Real = "", CodenameCategory Category = CodenameCategory.Project)
{
    /// <summary>What a screen reader reads for the row: "my-project, 1.2M" (the codename while streamer mode hides it).</summary>
    public override string ToString() => $"{Name}, {Tokens}";

    /// <summary>The row's menu offers "Show the real name on stream" (decision 16).</summary>
    public bool CanShowRealName => Real.Length > 0 && Name != Real;
}
```

In `VaultRootToggleViewModel`, replace

```csharp
    public string Name { get; }

    [ObservableProperty] private bool _isEnabled;

    public VaultRootToggleViewModel(LocalCcViewModel owner, string name, bool enabled)
    {
        _owner = owner;
        Name = name;
```

with

```csharp
    public string Name { get; }

    /// <summary>The folder as shown: its codename while streamer mode hides it (slice 7, CP 11). <see cref="Name"/> stays the key.</summary>
    public string Shown { get; }

    [ObservableProperty] private bool _isEnabled;

    public VaultRootToggleViewModel(LocalCcViewModel owner, string name, bool enabled)
    {
        _owner = owner;
        Name = name;
        Shown = StreamerMode.Mask.FolderName(name);
```

and replace that class's

```csharp
    public override string ToString() => Name;
}

/// <summary>
/// Overview section of the Claude Usage tab.
```

with

```csharp
    public override string ToString() => Shown;
}

/// <summary>
/// Overview section of the Claude Usage tab.
```

(the two-line anchor plus the summary's first line is unique; `PublishRootToggleViewModel` has its own `ToString`, changed in Task 12).

In `OnRootToggled`, replace

```csharp
                var res = ThemedDialog.Show(_owner, $"Stop archiving {root}?",
```

with

```csharp
                var res = ThemedDialog.Show(_owner, StopArchivingQuestion(root),
```

and add to `LocalCcViewModel`:

```csharp

    /// <summary>"Stop archiving …?" naming the folder as its checkbox does (slice 7, CP 11).</summary>
    internal static string StopArchivingQuestion(string root) => $"Stop archiving {StreamerMode.Mask.FolderName(root)}?";

    /// <summary>A top-ten table's rows (slice 7, CP 15): each name as shown, with its real name kept for the quick route.</summary>
    internal static IReadOnlyList<BreakdownRow> Rows(IEnumerable<(string Name, long Tokens)> rows, CodenameCategory category) =>
        [.. rows.Select(r => new BreakdownRow(StreamerMode.Mask.Name(category, r.Name), TokenFormat.Compact(r.Tokens), r.Name, category))];
```

In `Apply`, replace

```csharp
        Projects.Clear();
        foreach (var (name, tokens) in data.Projects.OrderByDescending(kv => kv.Value).Take(10))
            Projects.Add(new BreakdownRow(name, TokenFormat.Compact(tokens)));

        Skills.Clear();
        foreach (var (name, tokens) in data.Skills.OrderByDescending(kv => kv.Value).Take(10))
            Skills.Add(new BreakdownRow(name, TokenFormat.Compact(tokens)));
```

with

```csharp
        Projects.Clear();
        foreach (var row in Rows(data.Projects.OrderByDescending(kv => kv.Value).Take(10).Select(kv => (kv.Key, kv.Value)), CodenameCategory.Project))
            Projects.Add(row);

        Skills.Clear();
        foreach (var row in Rows(data.Skills.OrderByDescending(kv => kv.Value).Take(10).Select(kv => (kv.Key, kv.Value)), CodenameCategory.Skill))
            Skills.Add(row);
```

`data.Projects` and `data.Skills` iterate as `(name, tokens)` today; if they are dictionaries, `kv.Key`/`kv.Value` is right; if they are tuple lists, use `p => (p.Item1, p.Item2)`. Read their declaration.

`CcTrendsViewModel.cs` (CP 16). Replace

```csharp
        TopProjects.Clear();
        foreach (var (name, total) in data.Top)
            TopProjects.Add(new BreakdownRow(name, TokenFormat.Compact(total)));
```

with

```csharp
        TopProjects.Clear();
        foreach (var row in Rows(data.Top))
            TopProjects.Add(row);
```

and add to the class:

```csharp

    /// <summary>Trends' top projects (slice 7, CP 16): the same codenames as Overview's (one key, the project's name).</summary>
    internal static IReadOnlyList<BreakdownRow> Rows(IEnumerable<(string Name, long Total)> top) =>
        LocalCcViewModel.Rows(top, CodenameCategory.Project);
```

`CcLedgerViewModel.cs` (CP 17). Replace

```csharp
    public string? CwdTooltip => Info.Cwd;
```

with

```csharp
    public string? CwdTooltip => StreamerMode.On ? null : Info.Cwd;   // slice 7, CP 17: a full path names the user folder and the project
```

replace

```csharp
        ProjectText = disambiguate
            ? $"{info.ProjectName} ~{info.ProjectKey[^Math.Min(8, info.ProjectKey.Length)..]}"
            : info.ProjectName;
```

with

```csharp
        var project = disambiguate
            ? $"{info.ProjectName} ~{info.ProjectKey[^Math.Min(8, info.ProjectKey.Length)..]}"
            : info.ProjectName;
        // Streamer mode (slice 7, CP 17): one codename per name; two projects that share a name are told apart by their disambiguated key,
        // and only the codename shows.
        ProjectText = StreamerMode.Mask.Name(CodenameCategory.Project, project);
```

In the session detail, replace

```csharp
                Info.BySkill.OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key} {TokenFormat.Compact(kv.Value)}")));
        }
        sb.Append("\nFolder: ").Append(Info.Root).Append("  ·  Session: ").Append(Info.Uuid);
```

with

```csharp
                Info.BySkill.OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{StreamerMode.Mask.Name(CodenameCategory.Skill, kv.Key)} {TokenFormat.Compact(kv.Value)}")));
        }
        sb.Append("\nFolder: ").Append(StreamerMode.Mask.FolderName(Info.Root)).Append("  ·  Session: ").Append(Info.Uuid);
```

and in the group detail, replace

```csharp
        var roots = ordered.Select(m => m.Root).Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal);
```

with

```csharp
        var roots = ordered.Select(m => m.Root).Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal).Select(StreamerMode.Mask.FolderName);
```

The CSV export (`ExportCsv`, `BuildCsvRows`) keeps `Info.Root` and the real project text: exports keep real names (spec). It reads `Info`, not `ProjectText`. Read `BuildCsvRows` to confirm it doesn't read `ProjectText`. If it does, make it read `Info.ProjectName` (and the disambiguated form) directly, so the export stays real.

`SettingsWindow.xaml`. The session archive's checkbox: replace

```xml
                                                <CheckBox Style="{StaticResource ThemedCheckBox}" Margin="0,0,16,4"
                                                          Content="{Binding Name}"
                                                          IsChecked="{Binding IsEnabled, Mode=TwoWay}" />
```

with

```xml
                                                <CheckBox Style="{StaticResource ThemedCheckBox}" Margin="0,0,16,4"
                                                          Content="{Binding Shown}"
                                                          IsChecked="{Binding IsEnabled, Mode=TwoWay}" />
```

The export captions: beside `Export meter history…` and beside the Sessions export (`Export sessions…`), add a `TextBlock` bound to `ExportsKeepRealNamesShown`. Read the two buttons' blocks (`git grep -n "ExportMeterHistory\|ExportSessions" -- windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`) and add after each:

```xml
<TextBlock Text="{x:Static core:SettingsNames.ExportsKeepRealNames}" FontSize="10.5" VerticalAlignment="Center" Margin="8,0,0,0"
           Visibility="{Binding ExportsKeepRealNamesShown, Converter={StaticResource BoolVis}}"
           Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
```

The Sessions export's `DataContext` is the ledger view model. Give `CcLedgerViewModel` the same `public bool ExportsKeepRealNamesShown => StreamerMode.On;` and raise it from a `RefreshNames()` that also rebuilds the rows (`Rebuild` or the method that re-runs `Update` on each row; Read the class and call the one that does).

The quick route on the three tables (decision 16). Overview's and Trends' rows are `BreakdownRow`s in three `ItemsControl`s, named `OverviewProjectsList` (`:1553`), `OverviewSkillsList` (`:1587`) and `TrendsProjectsList` (`:1684`). Their item templates' roots are `<Grid Margin="4,3">`, `<Grid Margin="4,3">` and `<Grid Margin="0,1">`. The first two are identical, so anchor each Edit on its `ItemsControl x:Name="…"` line through the template's root `Grid` line (four lines, unique by the name). In each, give the root `Grid` these attributes and this menu, keeping its own `Margin`:

```xml
<Grid Margin="4,3" Background="Transparent" ContextMenuService.IsEnabled="{Binding CanShowRealName}"
      Tag="{Binding DataContext, RelativeSource={RelativeSource AncestorType=TabItem}}">
    <!-- Trends' root keeps Margin="0,1". -->
    <Grid.ContextMenu>
        <ContextMenu>
            <MenuItem Header="{x:Static core:SettingsNames.ShowRealNameOnStream}"
                      Command="{Binding PlacementTarget.Tag.ShowRealNameOfCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                      CommandParameter="{Binding}" />
        </ContextMenu>
    </Grid.ContextMenu>
```

`Tag` carries the Claude Usage tab's `DataContext` (`ClaudeCodeTabViewModel`, set on the `TabItem` at `SettingsWindow.xaml:1367`), because a context menu is outside the visual tree and can only reach the page through its placement target. Because the three anchors are identical, Edit each with its two lines of context above (the `<DataTemplate>` and `<Border …>` lines differ per table; Read and quote them). These rows are not focusable: their keyboard route is Privacy's list (decision 16).

On the Sessions list, add the same menu to its `ListBoxItem` style or item template, bound to the row's `ProjectText`/key. Read the Sessions `ListBox` (`:1767`): if it has an `ItemContainerStyle`, add the `ContextMenu` setter there (as on Mods & Widgets), with `CommandParameter` set to the row.

`ClaudeCodeTabViewModel` (the `DataContext` of Claude Usage) gets the command. Add to it:

```csharp

    /// <summary>Set by Settings (slice 7): "Show the real name on stream" from a table row (decision 16).</summary>
    public Action<CodenameCategory, string>? ShowRealName { get; set; }

    [RelayCommand]
    private void ShowRealNameOf(object? row)
    {
        switch (row)
        {
            case BreakdownRow b when b.CanShowRealName: ShowRealName?.Invoke(b.Category, b.Real); break;
            case LedgerRowViewModel l: ShowRealName?.Invoke(CodenameCategory.Project, l.ProjectKeyForStreamer); break;
        }
    }
```

The generated command is `ShowRealNameOfCommand`, which the menus above bind. Give `LedgerRowViewModel` `internal string ProjectKeyForStreamer { get; private set; } = "";`, set to `project` in `Update` beside `ProjectText`. The page's `DataContext` reaches `ClaudeCodeTabViewModel` through `Tag` as shown.

`SettingsViewModel.cs`. After `ClaudeCode = new ClaudeCodeTabViewModel(…);` (Read the constructor: the statement spans three lines and ends `widget.SaveLedgerGroupByProject));`), add:

```csharp
        ClaudeCode.ShowRealName = (category, real) => widget.Streamer.SetShown(category, real, true);
```

In `ReloadNames`, replace

```csharp
        Desk.Companion.Reload();
        NamesReloaded++;
```

with

```csharp
        Desk.Companion.Reload();
        History.RefreshNames();
        ClaudeCode.Overview.RefreshNames();
        ClaudeCode.Trends.RefreshNames();
        ClaudeCode.Ledger.RefreshNames();
        NamesReloaded++;
```

Give `LocalCcViewModel` and `CcTrendsViewModel` a `public void RefreshNames()` that rebuilds their rows from the data they last applied (`Apply` keeps `data`'s fields in members such as `_byDay`; keep the last `Projects`/`Skills`/`Top` source the same way and rebuild through `Rows`). If `ClaudeCode` names its children differently (`Overview`, `Trends`, `Ledger`), use the names the class has: `SettingsWindow.xaml.cs` already calls `ViewModel.ClaudeCode.Overview` and `.Trends`.

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~StreamerUsageTests|FullyQualifiedName~History|FullyQualifiedName~LocalCc|FullyQualifiedName~Ledger|FullyQualifiedName~Trends|FullyQualifiedName~Vault" > %TEMP%\slice7-t11.log 2>&1`
Expected: PASS. Every existing Claude Usage test runs with streamer mode off and reads as before.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/HistoryTabViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/LocalCcViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/CcTrendsViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/CcLedgerViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeTabViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/tests/Sanduhr.App.Tests/StreamerUsageTests.cs
git commit -m "feat(settings-v2): streamer mode on Claude Usage: accounts, projects, skills and folders by codename; exports keep real names"
```

---

### Task 12: Publish usage, Weather, Watchers and About

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/PublishViewModel.cs` (CP 18, 19, 20, CP 11's share rows, `RefreshNames`)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/DeskContentPagesViewModels.cs` (CP 21: `PlaceChoice`, `PlaceText`, `PlacesHidden`)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (CP 22: the runner line)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (CP 23: the `*Shown` folders; `ReloadNames`)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (the Endpoint swap, the share rows, Places found, About's paths)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/StreamerPagesTests.cs`

**Interfaces:**
- Consumes: `StreamerMode.Mask.Endpoint/HostsIn/PublishSample/FolderName/Name/WeatherName/Paths`, `SettingsNames.StreamerThisPc`, `PlacesHiddenNote`, `EndpointHiddenNote`.
- Produces:
  - `PublishViewModel.StreamerOn`, `EndpointShown`, `RefreshNames()`; `PublishRootToggleViewModel.Shown`
  - `public sealed record PlaceChoice(WeatherPlace Place, string Shown)`; `WeatherPageViewModel.Results` as `ObservableCollection<PlaceChoice>`, `PlacesHidden`, `RefreshNames()`
  - `App.RunnerStatusText(RunnerStatus?)` (internal static, pure)
  - `SettingsViewModel.SettingsFolderShown`, `LocalFolderShown`, `ClaudeCodeFilesFolderShown`

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/StreamerPagesTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Sanduhr.App.Services;
using Sanduhr.App.Updates;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 7: Publish usage, Weather, Watchers and About (spec, Reach inside Settings; CPs 11, 18 to 23).</summary>
[Collection("SettingsWindow")]
public sealed class StreamerPagesTests : AppWindowFixture
{
    private static void NoSentinel(string where, string? text) =>
        Assert.False((text ?? "").Contains("qx", StringComparison.OrdinalIgnoreCase), $"{where}: \"{text}\"");

    private (SettingsViewModel Svm, WidgetViewModel Widget) Make()
    {
        Home(".claude-qxwork");
        var (widget, _) = ScratchWidget("Qxalpha");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
            checkForUpdatesNow: () => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate)), isStoreBuild: false);
        return (svm, widget);
    }

    [Fact]
    public void Publish_shows_the_endpoint_host_by_codename_and_hides_the_box() => AppHost.Run(() =>
    {
        var (svm, widget) = Make();
        try
        {
            svm.Publish.EndpointUrl = "https://qxhost.example/ingest";
            widget.Streamer.SetOn(true);
            svm.Publish.RefreshNames();
            Assert.True(svm.Publish.StreamerOn);
            NoSentinel("endpoint", svm.Publish.EndpointShown);
            Assert.Equal("https://qxhost.example/ingest", svm.Publish.EndpointUrl);   // the value itself never changes
            Assert.All(svm.Publish.Roots, r => NoSentinel("share row", r.Shown + r.ToString()));
            NoSentinel("status", svm.Publish.StatusText);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_publish_sample_names_no_folder_project_host_or_pc() => AppHost.Run(() =>
    {
        var (svm, widget) = Make();
        try
        {
            svm.Publish.EndpointUrl = "https://qxhost.example/ingest";
            widget.Streamer.SetOn(true);
            string? header = null, json = null;
            svm.Publish.SampleHook = (h, _, j) => { header = h; json = j; };
            svm.Publish.ShowSampleCommand.Execute(null);
            NoSentinel("sample header", header);
            NoSentinel("sample json", json);
            Assert.DoesNotContain(Environment.MachineName, json!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(SettingsNames.StreamerThisPc, (string?)JsonNode.Parse(json!)!["machine"]);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Weather_shows_the_nickname_else_the_citys_codename_and_masks_places_found() => AppHost.Run(() =>
    {
        var (svm, widget) = Make();
        try
        {
            var weather = svm.WeatherPage;
            weather.Picked = WeatherPlace.Create("Qxcity", "Texas", "United States", 32.75, -97.33);
            Assert.Contains("Qxcity", weather.PlaceText);
            widget.Streamer.SetOn(true);
            weather.RefreshNames();
            NoSentinel("place", weather.PlaceText);
            weather.Nickname = "Home";
            weather.RefreshNames();
            Assert.Equal("Home", weather.PlaceText);
            weather.ShowResultsForTests([WeatherPlace.Create("Qxtown", null, "Germany", 50.1, 8.7)]);
            Assert.All(weather.Results, r => NoSentinel("place found", r.Shown + r.ToString()));
            Assert.True(weather.PlacesHidden);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_runner_line_names_runners_by_codename()
    {
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
        var text = App.RunnerStatusText(new RunnerStatus(RunnerStatusKind.Watching, ["qxrunner-1", "qxrunner-2"]));
        NoSentinel("runners", text);
        Assert.StartsWith("Watching 2 runners: ", text);
        Assert.Equal("Docker isn't running.", App.RunnerStatusText(new RunnerStatus(RunnerStatusKind.DockerDown, [])));
        Assert.Equal("", App.RunnerStatusText(null));
    }

    [Fact]
    public void About_shows_the_user_folder_as_USERPROFILE_and_opens_the_real_one() => AppHost.Run(() =>
    {
        var (svm, widget) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            svm.ReloadNames();
            foreach (var shown in new[] { svm.SettingsFolderShown, svm.LocalFolderShown, svm.ClaudeCodeFilesFolderShown })
                Assert.DoesNotContain(Environment.UserName, shown, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(svm.SettingsFolderShown, svm.SettingsFolder);   // the Show in Explorer command keeps the real path
        }
        finally { widget.Dispose(); }
    });
}
```

In tests, About's folders sit under the fixture's dev profile, which is under the real temp folder in the real user folder, so `Paths` has a user folder to replace there too.

The seams, added first as non-masking stubs for the RED: `PublishViewModel.StreamerOn`, `EndpointShown` (`=> EndpointUrl`), `RefreshNames`, `PublishRootToggleViewModel.Shown` (`=> Name`), `PlaceChoice` with `Shown = place.Label`, `WeatherPageViewModel.RefreshNames`, `PlacesHidden`, `ShowResultsForTests`, `App.RunnerStatusText` (`=> status?.Text ?? ""`), and the three `*Shown` properties (`=> SettingsFolder` and so on). If `SampleHook`'s signature differs from `(header, note, json)`, Read it (`PublishViewModel.cs:416`) and match it.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~StreamerPagesTests > %TEMP%\slice7-t12-red.log 2>&1`
Expected: a behavioural RED: `endpoint: "https://qxhost.example/ingest"`, `sample json: …qx…`, `place: "Qxcity, Texas, United States"`, `runners: "Watching 2 runners: qxrunner-1, qxrunner-2"`, and the user name in About's folders. Record them.

- [ ] **Step 3: Mask each choke point**

`PublishViewModel.cs`. In `PublishRootToggleViewModel`, replace

```csharp
    public string Name { get; }

    [ObservableProperty] private bool _isShared;

    public PublishRootToggleViewModel(PublishViewModel owner, string name, bool shared)
    {
        _owner = owner;
        Name = name;
```

with

```csharp
    public string Name { get; }

    /// <summary>The folder as shown: its codename while streamer mode hides it (slice 7, CP 11). <see cref="Name"/> stays the key.</summary>
    public string Shown { get; }

    [ObservableProperty] private bool _isShared;

    public PublishRootToggleViewModel(PublishViewModel owner, string name, bool shared)
    {
        _owner = owner;
        Name = name;
        Shown = StreamerMode.Mask.FolderName(name);
```

and replace that class's

```csharp
    public override string ToString() => Name;

    partial void OnIsSharedChanged(bool value)
```

with

```csharp
    public override string ToString() => Shown;

    partial void OnIsSharedChanged(bool value)
```

In `PublishViewModel`, add after `[ObservableProperty] private string _statusText = "Never published.";`:

```csharp

    /// <summary>Streamer mode is on (slice 7, CP 18): the Endpoint URL box gives way to a read-only line, so the address is never on screen
    /// and a codename is never written back.</summary>
    public bool StreamerOn => StreamerMode.On;

    /// <summary>The endpoint as the read-only line shows it: the host as a codename, the path hidden.</summary>
    public string EndpointShown => StreamerMode.Mask.Endpoint(EndpointUrl);

    /// <summary>Streamer mode moved (slice 7): the folder rows, the endpoint line and the status read their names again.</summary>
    public void RefreshNames()
    {
        Reload();
        OnPropertyChanged(nameof(StreamerOn));
        OnPropertyChanged(nameof(EndpointShown));
    }
```

`EndpointUrl` is a generated property: add `[NotifyPropertyChangedFor(nameof(EndpointShown))]` on its field (Read the field's declaration, `_endpointUrl`, and add the attribute above it).

In `RefreshStatus` (CP 19), replace

```csharp
        StatusText = string.Create(CultureInfo.InvariantCulture, $"{last}{ok}{next}");
```

with

```csharp
        // Streamer mode (slice 7, CP 19): a result that names a redirect host names it by codename.
        StatusText = StreamerMode.Mask.HostsIn(string.Create(CultureInfo.InvariantCulture, $"{last}{ok}{next}"), s.Target.EndpointUrl);
```

If `RefreshStatus`'s settings variable is not `s` with a `Target`, Read the method and use its endpoint (the method opens with `var s = _service.Load();`).

In `ShowSample` (CP 20), replace

```csharp
            var header = SettingsNames.SampleHeader(date, host, tokenMissing);
```

with

```csharp
            var header = SettingsNames.SampleHeader(date, host is null ? null : StreamerMode.Mask.Name(CodenameCategory.Host, host), tokenMissing);
```

and replace

```csharp
            var json = record.Payload.ToJsonString(SampleJson);
```

with

```csharp
            // Streamer mode (slice 7, CP 20): folders, projects and this PC by codename; the record that would be sent is unchanged.
            var json = StreamerMode.Mask.PublishSample(record.Payload, SettingsNames.StreamerThisPc).ToJsonString(SampleJson);
```

`DeskContentPagesViewModels.cs` (CP 21). Add before `public sealed partial class WeatherPageViewModel` (Read the file for the class's summary block and add above it):

```csharp
/// <summary>A place Look up found, as the list shows it: its full label, or its codename while streamer mode is on (slice 7, CP 21).</summary>
public sealed record PlaceChoice(WeatherPlace Place, string Shown)
{
    public override string ToString() => Shown;
}

```

Replace

```csharp
    public ObservableCollection<WeatherPlace> Results { get; } = [];

    /// <summary>"Fort Worth, Texas, United States", or "No city set".</summary>
    public string PlaceText => _saved.Place?.Label ?? "No city set";
```

with

```csharp
    public ObservableCollection<PlaceChoice> Results { get; } = [];

    /// <summary>"Fort Worth, Texas, United States", or "No city set". While streamer mode is on: the "Show it as" name, else the city's
    /// codename (spec, Weather; slice 7, CP 21).</summary>
    public string PlaceText => _saved.Place is not { } place ? "No city set"
        : StreamerMode.On ? StreamerMode.Mask.WeatherName(place, _saved.Nickname)
        : place.Label;

    /// <summary>Places found are codenames: the note under the list says so (owner taste 11).</summary>
    public bool PlacesHidden => StreamerMode.On && Results.Count > 0;

    /// <summary>Streamer mode moved (slice 7).</summary>
    public void RefreshNames()
    {
        var places = Results.Select(r => r.Place).ToList();
        Results.Clear();
        foreach (var p in places) Results.Add(Choice(p));
        OnPropertyChanged(nameof(PlaceText));
        OnPropertyChanged(nameof(PlacesHidden));
    }

    private static PlaceChoice Choice(WeatherPlace p) =>
        new(p, StreamerMode.On ? StreamerMode.Mask.Name(CodenameCategory.City, p.Label) : p.Label);

    /// <summary>Tests fill the list as Look up does.</summary>
    internal void ShowResultsForTests(IEnumerable<WeatherPlace> places)
    {
        Results.Clear();
        foreach (var p in places) Results.Add(Choice(p));
        OnPropertyChanged(nameof(PlacesHidden));
    }
```

In `LookUp`, replace

```csharp
            foreach (var p in places) Results.Add(p);
```

with

```csharp
            foreach (var p in places) Results.Add(Choice(p));
            OnPropertyChanged(nameof(PlacesHidden));
```

In `Save`, after `_saved` is written (Read the method's end), add `OnPropertyChanged(nameof(PlaceText));` if the method doesn't already raise it. The nickname changes `PlaceText` while on.

`App.xaml.cs` (CP 22). Replace

```csharp
        svm.Desk.CiRunnersStatus = _runners?.Status.Text ?? "";
        Action showRunnerStatus = () => svm.Desk.CiRunnersStatus = _runners?.Status.Text ?? "";
```

with

```csharp
        svm.Desk.CiRunnersStatus = RunnerStatusText(_runners?.Status);
        Action showRunnerStatus = () => svm.Desk.CiRunnersStatus = RunnerStatusText(_runners?.Status);
```

and add to `App`:

```csharp

    /// <summary>Watchers' CI runners line (slice 7, CP 22): each runner by codename while streamer mode is on. Raised again when the switch
    /// moves (<see cref="OnStreamerChanged"/>).</summary>
    internal static string RunnerStatusText(RunnerStatus? status) =>
        status is null ? "" : (status with { Runners = [.. status.Runners.Select(r => StreamerMode.Mask.Name(CodenameCategory.Runner, r))] }).Text;
```

`SettingsViewModel.cs` (CP 23). Replace

```csharp
    public string SettingsFolder { get; }
```

with

```csharp
    public string SettingsFolder { get; }

    /// <summary>About's folders as shown (slice 7, CP 23; spec, About): the profile folder as %USERPROFILE% while streamer mode is on. The
    /// Show in Explorer commands keep the real paths.</summary>
    public string SettingsFolderShown => StreamerMode.Mask.Paths(SettingsFolder);
    public string LocalFolderShown => StreamerMode.Mask.Paths(LocalFolder);
    public string ClaudeCodeFilesFolderShown => StreamerMode.Mask.Paths(ClaudeCodeFilesFolder);
```

In `ReloadNames`, replace

```csharp
        ClaudeCode.Ledger.RefreshNames();
        NamesReloaded++;
```

with

```csharp
        ClaudeCode.Ledger.RefreshNames();
        Publish.RefreshNames();
        WeatherPage.RefreshNames();
        OnPropertyChanged(nameof(SettingsFolderShown));
        OnPropertyChanged(nameof(LocalFolderShown));
        OnPropertyChanged(nameof(ClaudeCodeFilesFolderShown));
        NamesReloaded++;
```

(Watchers' line is App's: `OnStreamerChanged` raises `RunnerStatusShown`, Task 7.)

`SettingsWindow.xaml`. The Endpoint (CP 18). Replace

```xml
                                <TextBox Style="{StaticResource Input}" Width="360"
                                         Text="{Binding EndpointUrl, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                                         AutomationProperties.Name="Endpoint URL"
```

with

```xml
                                <!-- Streamer mode (slice 7, decision 12): a read-only line in place of the box, so the address never shows. -->
                                <StackPanel Visibility="{Binding StreamerOn, Converter={StaticResource BoolVis}}">
                                    <TextBlock Text="{Binding EndpointShown}" FontFamily="Cascadia Code, Consolas" FontSize="12"
                                               Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                    <TextBlock Text="{x:Static core:SettingsNames.EndpointHiddenNote}" FontSize="10.5"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                                </StackPanel>
                                <TextBox Style="{StaticResource Input}" Width="360"
                                         Visibility="{Binding StreamerOn, Converter={StaticResource InvBoolVis}}"
                                         Text="{Binding EndpointUrl, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                                         AutomationProperties.Name="Endpoint URL"
```

If `SettingsWindow.xaml` has no inverted converter (`git grep -n "InvBoolVis\|InverseBool" -- windows-dotnet/src/Sanduhr.App`), add `public bool StreamerOff => !StreamerOn;` to `PublishViewModel` (raised with `StreamerOn`) and bind the box's Visibility to `StreamerOff` through `BoolVis`.

The share rows (CP 11). Replace

```xml
                                        <CheckBox Style="{StaticResource ThemedCheckBox}" Margin="0,0,16,4"
                                                  Content="{Binding Name}"
                                                  IsChecked="{Binding IsShared, Mode=TwoWay}"
```

with

```xml
                                        <CheckBox Style="{StaticResource ThemedCheckBox}" Margin="0,0,16,4"
                                                  Content="{Binding Shown}"
                                                  IsChecked="{Binding IsShared, Mode=TwoWay}"
```

Places found (CP 21). Replace

```xml
                                <ListBox ItemsSource="{Binding Results}" SelectedItem="{Binding Picked, Mode=TwoWay}"
                                         DisplayMemberPath="Label" MaxHeight="140" Margin="0,4,0,0" Width="400" HorizontalAlignment="Left"
```

with

```xml
                                <ListBox ItemsSource="{Binding Results}" SelectedValuePath="Place" SelectedValue="{Binding Picked, Mode=TwoWay}"
                                         DisplayMemberPath="Shown" MaxHeight="140" Margin="0,4,0,0" Width="400" HorizontalAlignment="Left"
```

and after that `ListBox`'s closing line (`AutomationProperties.Name="Places found" />`) add:

```xml
                                <TextBlock Text="{x:Static core:SettingsNames.PlacesHiddenNote}" FontSize="10.5" Margin="0,2,0,0"
                                           Visibility="{Binding PlacesHidden, Converter={StaticResource BoolVis}}"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
```

About's paths (CP 23): in the three rows, change the display bindings, and only those. `Text="{Binding SettingsFolder}"` and `ToolTip="{Binding SettingsFolder}"` become `SettingsFolderShown`; `Text="{Binding LocalFolder}"` and `ToolTip="{Binding LocalFolder}"` become `LocalFolderShown`; `Text="{Binding ClaudeCodeFilesFolder}"` and `ToolTip="{Binding ClaudeCodeFilesFolder}"` become `ClaudeCodeFilesFolderShown`. Every `CommandParameter="{Binding …Folder}"` stays. Each `Text=` binding occurs once (`git grep -c -F 'Text="{Binding SettingsFolder}"'` prints 1; check all three and both tooltips).

Add `using Sanduhr.App.Services;` and `using Sanduhr.Core;` where an edited file lacks them.

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~StreamerPagesTests|FullyQualifiedName~Publish|FullyQualifiedName~Weather|FullyQualifiedName~DeskSplitPagesTests|FullyQualifiedName~Slice5PagesTests|FullyQualifiedName~Watchers" > %TEMP%\slice7-t12.log 2>&1`
Expected: PASS. `DeskSplitPagesTests` sets `svm.WeatherPage.Picked` directly, which still works: `Picked` stays a `WeatherPlace`. The slice 5 publish-sample test ("the sample equals the next real record's fields") runs with streamer mode off, where `PublishSample` returns an unchanged copy.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/PublishViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/DeskContentPagesViewModels.cs windows-dotnet/src/Sanduhr.App/App.xaml.cs windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/tests/Sanduhr.App.Tests/StreamerPagesTests.cs
git commit -m "feat(settings-v2): streamer mode on Publish usage, Weather, Watchers and About"
```

---

### Task 13: The reach scan: every producer of a name in the app carries a marker, and the counts are pinned

**Files:**
- Modify (comment-only markers): every file the scan lists (the base table in "The reach inventory", plus the lines Tasks 7 to 12 added)
- Test: create `windows-dotnet/tests/Sanduhr.Tests/StreamerReachTests.cs`

**Interfaces:**
- Consumes: Tasks 7 to 12's choke points.
- Produces: `StreamerReachTests` (Core test project, reads `src/Sanduhr.App`), the pinned inventory, and the marker rule. The marker is `// streamer: <id>` on the line or within the three lines above, where `<id>` is one of:
  - `masked`: the value reaches the screen through a `StreamerMode`/`StreamerMask` call on this line or the next three;
  - `key`: the value is a key, a count, a write or a comparison, and never shown;
  - `export`: written to a file the person asked for (exports keep real names, spec);
  - `demo`: demo mode's sample labels;
  - `cant`: on the can't-reach list (decision 11), with the caption naming it.

**Why a scan as well as the sweep (Task 15):** the sweep proves today's surfaces with today's sentinels; the scan makes tomorrow's new line that reads a name fail CI until someone decides what it is. Slice 6's `NetworkDisclosureTests` is the model: it can't pass vacuously (pinned counts per file and per id, a minimum, and synthetic sources).

- [ ] **Step 1: Write the test**

Create `windows-dotnet/tests/Sanduhr.Tests/StreamerReachTests.cs`:

```csharp
using System.Text.RegularExpressions;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Settings v2, slice 7: every line in the app that reads an identifying name carries a "// streamer: &lt;id&gt;" marker saying what
/// becomes of it, and the inventory is pinned file by file, so a new reader of a name fails CI until it is masked or marked (the reach
/// inventory; slice 6's NetworkDisclosureTests is the model). It can't pass vacuously: the counts, a floor and synthetic sources are pinned.
/// </summary>
public class StreamerReachTests
{
    private static string Src()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src");
    }

    /// <summary>The producer shapes: account labels, folder paths and names, projects, skills, runners, add-ons, the PC's name, places,
    /// endpoints. The plan's "The reach inventory" lists what each finds at 3cc3699d.</summary>
    internal static readonly Regex Producer = new(
        @"\bShownAccounts\b|\bShownActiveAccount\b|\.GetActive\(\)|\bActiveAccount\b|\.ListAccounts\(\)"
        + @"|ClaudeCodeFolders\.Display\s*\(|DetectedRootNames\s*\("
        + @"|\.ProjectName\b|\.BySkill\b|\bdata\.(?:Projects|Skills|Top)\b|\bInfo\.Root\b|\bm\.Root\b"
        + @"|\.Runners?\b|\.Marketplace\b|\bUserServers\b|\bEnabledPlugins\b|Environment\.MachineName"
        + @"|\bPlace\??\.(?:Label|Name)\b|\bEndpointUrl\b",
        RegexOptions.Compiled);

    private static readonly Regex Marker = new(@"//\s*streamer:\s*([a-z]+)", RegexOptions.Compiled);

    internal static readonly string[] Ids = ["masked", "key", "export", "demo", "cant"];

    internal sealed record Site(string File, int Line, string? Id, string Text);

    internal static List<Site> Scan()
    {
        var src = Src();
        var sites = new List<Site>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(src, "Sanduhr.App"), "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, path).Replace('\\', '/');
            if (rel.Contains("/bin/", StringComparison.Ordinal) || rel.Contains("/obj/", StringComparison.Ordinal))
                continue;
            sites.AddRange(ScanLines(rel, File.ReadAllLines(path)));
        }
        return sites;
    }

    internal static List<Site> ScanLines(string rel, string[] lines)
    {
        var sites = new List<Site>();
        for (int i = 0; i < lines.Length; i++)
        {
            var code = lines[i].TrimStart();
            if (code.StartsWith("//", StringComparison.Ordinal) || code.StartsWith('*'))
                continue;
            if (Producer.IsMatch(lines[i]))
                sites.Add(new Site(rel, i + 1, MarkerFor(lines, i), lines[i].Trim()));
        }
        return sites;
    }

    private static string? MarkerFor(string[] lines, int i)
    {
        for (int j = i; j >= Math.Max(0, i - 3); j--)
            if (Marker.Match(lines[j]) is { Success: true } m)
                return m.Groups[1].Value;
        return null;
    }

    /// <summary>The inventory this slice pinned, file by file and id by id (Task 13 Step 3 fills it from the scan).</summary>
    private static readonly (string File, string Id, int Count)[] Known =
    [
        // Filled in Step 3 from the scan's own output.
    ];

    /// <summary>The floor: at 3cc3699d the shapes matched 108 lines. Fewer means the shapes stopped seeing the code.</summary>
    private const int Floor = 108;

    [Fact]
    public void Every_reader_of_a_name_carries_a_marker()
    {
        var bare = Scan().Where(s => s.Id is null).Select(s => $"{s.File}:{s.Line}  {s.Text}").ToList();
        Assert.True(bare.Count == 0, "Unmarked readers of a name (mask it, or mark it \"// streamer: key|export|demo|cant\"):\n" + string.Join("\n", bare));
    }

    [Fact]
    public void Every_marker_is_one_of_the_five()
    {
        var bad = Scan().Where(s => s.Id is not null && !Ids.Contains(s.Id)).Select(s => $"{s.File}:{s.Line} says \"{s.Id}\"").ToList();
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    /// <summary>A line marked masked must call the mask: on the line or within the three below it.</summary>
    [Fact]
    public void A_masked_line_really_calls_the_mask()
    {
        var src = Src();
        var bad = new List<string>();
        foreach (var site in Scan().Where(s => s.Id == "masked"))
        {
            var lines = File.ReadAllLines(Path.Combine(src, site.File));
            var window = string.Join("\n", lines.Skip(site.Line - 1).Take(4));
            if (!Regex.IsMatch(window, @"StreamerMode\.|StreamerMask\b|\bMask\.(?:Name|Folder|FolderName|AddOn|Paths|Endpoint|HostsIn|Watchers|WeatherName|PublishSample)\b|ShowFolder\(|FoldersIn\("))
                bad.Add($"{site.File}:{site.Line}  {site.Text}");
        }
        Assert.True(bad.Count == 0, "Marked masked, but no mask call on the line or the three below:\n" + string.Join("\n", bad));
    }

    [Fact]
    public void The_inventory_is_what_this_slice_found()
    {
        var sites = Scan();
        Assert.True(sites.Count >= Floor, $"the scan found {sites.Count} lines, under the floor of {Floor}: the shapes stopped matching");
        var found = sites.Where(s => s.Id is not null).GroupBy(s => (s.File, s.Id!)).Select(g => (g.Key.File, g.Key.Item2, Count: g.Count()))
            .OrderBy(x => x.File, StringComparer.Ordinal).ThenBy(x => x.Item2, StringComparer.Ordinal).ToList();
        var expected = Known.OrderBy(x => x.File, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        Assert.True(expected.SequenceEqual(found),
            "Pin this inventory (or explain the change):\n" + string.Join("\n", found.Select(f => $"        (\"{f.File}\", \"{f.Item2}\", {f.Count}),")));
    }

    [Theory]
    [InlineData("var a = _widget.ShownAccounts;", true)]
    [InlineData("foreach (var root in vault.DetectedRootNames())", true)]
    [InlineData("Text = info.ProjectName;", true)]
    [InlineData("var who = Environment.MachineName;", true)]
    [InlineData("var s = place?.Label;", true)]
    [InlineData("var n = row.Marketplace;", true)]
    [InlineData("var x = 1 + 2;", false)]
    [InlineData("// _widget.ShownAccounts in a comment", false)]
    public void The_shapes_see_what_they_should(string line, bool expected) =>
        Assert.Equal(expected, ScanLines("x.cs", [line]).Count == 1);

    [Fact]
    public void A_marker_above_counts_and_one_too_far_does_not()
    {
        string[] near = ["// streamer: key", "", "", "var a = _widget.ShownAccounts;"];
        string[] far = ["// streamer: key", "", "", "", "var a = _widget.ShownAccounts;"];
        Assert.Equal("key", ScanLines("x.cs", near).Single().Id);
        Assert.Null(ScanLines("x.cs", far).Single().Id);
    }

    [Fact]
    public void The_MCP_server_is_not_scanned_and_the_mask_is_not_linked()
    {
        Assert.DoesNotContain(Scan(), s => s.File.StartsWith("Sanduhr.Mcp/", StringComparison.Ordinal));
        var csproj = File.ReadAllText(Path.Combine(Src(), "Sanduhr.Mcp", "Sanduhr.Mcp.csproj"));
        Assert.DoesNotContain("StreamerMask", csproj);
        Assert.DoesNotContain("Codenames", csproj);
        Assert.DoesNotContain("StreamerFile", csproj);
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~StreamerReachTests > %TEMP%\slice7-t13-red.log 2>&1`
Expected: a behavioural RED. `Every_reader_of_a_name_carries_a_marker` lists every unmarked line: about 108 plus the lines Tasks 7 to 12 added. `The_inventory_is_what_this_slice_found` fails and prints the found table (empty so far). The shape theory and the marker fact pass. Record the unmarked count.

- [ ] **Step 3: Mark every line, then pin the inventory**

For each line the first test lists, Read it with its neighbours and add one marker comment on the line above (or at the end of the line, as slice 6's markers did):

- `// streamer: masked`: the value goes on screen through a mask call within the next three lines. If there is no mask call, the line is a gap: mask it as Tasks 7 to 12 did, and say so in the report.
- `// streamer: key`: it is a key, a count, a comparison, a write or a lookup (`ShownAccounts.Count`, `GetActive()` passed to a fetcher, `DetectedRootNames()` to build consent keys, `EndpointUrl` saved).
- `// streamer: export`: `CsvExport` and the Sessions CSV.
- `// streamer: demo`: demo mode's sample labels (`DemoMode.ChipText` neighbours).
- `// streamer: cant`: only for what decision 11 lists.

A line that puts a real name on screen without a mask is a finding, not a marker. Fix it in the same commit, with a test line in the owning task's test class, and name it in the report. Expect `WidgetViewModel.cs`'s 24 lines to be mostly `key` (the fetcher, the follower, seats, history), with `RefreshAccountLabel` and `CurrentDeskUsage` `masked`. Expect `SignInCoordinator.cs`'s 7 to be `key` (the persist closures).

Then run the test again. `The_inventory_is_what_this_slice_found` prints the found table as `Known` lines; paste them into `Known`, replacing the comment. Run once more: PASS.

- [ ] **Step 4: Run the suites the markers touched**

Run: `dotnet test tests/Sanduhr.Tests > %TEMP%\slice7-t13-core.log 2>&1` and `dotnet test tests/Sanduhr.App.Tests > %TEMP%\slice7-t13-app.log 2>&1`
Expected: PASS. Markers are comments; nothing else changes unless Step 3 found a gap.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/tests/Sanduhr.Tests/StreamerReachTests.cs windows-dotnet/src/Sanduhr.App
git commit -m "test(settings-v2): the reach scan: every reader of a name is masked or marked, and the inventory is pinned"
```

The controller then reads every `key`, `demo` and `cant` line in the diff (`git show --stat` and `git show -U0 | grep "streamer: "`) and confirms none of them puts a name on screen. That is this slice's riskiest review.

---

### Task 14: The 4.0.0 What's New cards: "Settings, rearranged" and "Streamer mode"

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/WhatsNewCards.cs` (`Release400`, `Table`, `AnchorFor`)
- Test: modify `windows-dotnet/tests/Sanduhr.Tests/WhatsNewCardsTests.cs`

**Interfaces:**
- Consumes: `SettingsIndex.Streamer`, `SettingsNames.StreamerSection`, `PrivacyTitle`.
- Produces: two cards, ids `settings-rearranged` and `streamer-mode`, version 4.0.0, landing on `privacy#streamer` (spec, "Outside Settings, same release").

- [ ] **Step 1: Write the failing tests**

In `windows-dotnet/tests/Sanduhr.Tests/WhatsNewCardsTests.cs`, replace

```csharp
            ["ci-runners",
             "widgets", "companion",
```

with

```csharp
            ["settings-rearranged", "streamer-mode",
             "ci-runners",
             "widgets", "companion",
```

and add after `Table_HasEveryReleaseNewestFirst`:

```csharp

    /// <summary>4.0.0 (spec, Outside Settings, same release): "Settings, rearranged", landing on privacy#streamer, plus one for streamer mode.</summary>
    [Fact]
    public void Release400_ShowsItsTwoCardsAfterTheUpdate()
    {
        var shown = WhatsNew.Cards("3.9.0", "4.0.0");
        Assert.Equal(["settings-rearranged", "streamer-mode"], shown.Select(c => c.Id));
        Assert.All(shown, c => Assert.Equal("4.0.0", c.Version));
        Assert.All(shown, c => Assert.Equal(SettingsPage.Privacy, c.Destination));
        Assert.All(shown, c => Assert.Equal(SettingsIndex.Streamer, c.Anchor));
    }

    [Fact]
    public void Release400_CardsNameThePlaceInTheSettingsForm()
    {
        Assert.Equal("Settings, rearranged", Table.Single(c => c.Id == "settings-rearranged").Title);
        Assert.Equal(SettingsNames.StreamerSection, Table.Single(c => c.Id == "streamer-mode").Title);
        Assert.Contains("Settings, Privacy, Streamer mode", Table.Single(c => c.Id == "streamer-mode").Body);
        Assert.Contains("Settings, Privacy", Table.Single(c => c.Id == "settings-rearranged").Body);
    }

    [Fact]
    public void Release390_StillShowsOnlyItsOwnCardOnThe390Build()
    {
        Assert.Equal(["ci-runners"], WhatsNew.Cards("3.8.0", "3.9.0").Select(c => c.Id));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~WhatsNewCardsTests|FullyQualifiedName~WhatsNewTests" > %TEMP%\slice7-t14-red.log 2>&1`
Expected: a behavioural RED. `Table_HasEveryReleaseNewestFirst` reports the two missing ids at the head, and `Release400_ShowsItsTwoCardsAfterTheUpdate` finds no cards. `Release390_StillShowsOnlyItsOwnCardOnThe390Build` passes.

- [ ] **Step 3: The cards**

In `windows-dotnet/src/Sanduhr.Core/WhatsNewCards.cs`, replace

```csharp
    public static IReadOnlyList<WhatsNewCard> Table { get; } = Anchored([.. Release390(), .. Release380(), .. Release371(), .. Release360(), .. Release350()]);
```

with

```csharp
    public static IReadOnlyList<WhatsNewCard> Table { get; } =
        Anchored([.. Release400(), .. Release390(), .. Release380(), .. Release371(), .. Release360(), .. Release350()]);

    // 4.0.0 (Settings v2; spec, Outside Settings, same release): Settings rearranged, landing on Streamer mode, and Streamer mode itself.
    private static WhatsNewCard[] Release400() =>
    [
        WhatsNewCard.Of("4.0.0", "settings-rearranged", "Settings, rearranged",
            "Every feature has one home now, and search lands on the exact section. Settings, Privacy shows what leaves this PC and what Claude can change.",
            "", // Settings
            WhatsNewPreview.None, SettingsPage.Privacy),
        WhatsNewCard.Of("4.0.0", "streamer-mode", SettingsNames.StreamerSection,
            "Before you share your screen, turn on Settings, Privacy, Streamer mode. Accounts, folders and projects become codenames; live numbers stay.",
            "", // Hide
            WhatsNewPreview.None, SettingsPage.Privacy),
    ];
```

and in `AnchorFor`, replace

```csharp
        "ci-runners" => SettingsIndex.CiRunners,
```

with

```csharp
        "settings-rearranged" => SettingsIndex.Streamer,
        "streamer-mode" => SettingsIndex.Streamer,
        "ci-runners" => SettingsIndex.CiRunners,
```

Both bodies are two sentences and under 200 characters. Check `WhatsNewCard.Of`'s parameter order against a 3.9.0 card above it (version, id, title, body, glyph, preview, page) before the Edit.

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~WhatsNew|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsIndexTests" > %TEMP%\slice7-t14.log 2>&1`
Expected: PASS. The house-rule tests (unique ids, body length, sentence case, "Settings, <Page>, <Section>" prose, an anchor that exists on every Settings card) pass for both cards.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/WhatsNewCards.cs windows-dotnet/tests/Sanduhr.Tests/WhatsNewCardsTests.cs
git commit -m "feat(settings-v2): the 4.0.0 What's New cards, Settings rearranged and Streamer mode, landing on Privacy"
```

---

### Task 15: The Done line: a two-account, three-folder profile with streamer mode on shows no real name anywhere but where it is allowlisted

**Files:**
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/StreamerSweepTests.cs`
- Modify: `windows-dotnet/tests/Sanduhr.Tests/StreamerReachTests.cs` (the toasts pin)
- Modify only if a test finds a gap: the choke point it names (Tasks 7 to 12's files)

**Interfaces:**
- Consumes: everything above.
- Produces the spec's Done line as tests: "with streamer mode on and a two-account, three-folder profile, no account label, email, folder path, project, repo or runner name appears in Settings, the widget, the tray, toasts, the Desk or the statusline, except allowlisted ones."

**The sentinel rule.** Every real name in the profile starts with "qx" (any case), and nothing in Sanduhr's own copy does (Step 2 checks that). So "no real name appears" is "no 'qx' in any text or spoken name", which can't miss a site that renders a name, because the test doesn't need to know where the site is. It can't pass vacuously: it also asserts that each expected codename appears where its page shows it, that the one allowlisted name (`Qxbeta`) appears in place of its codename, and that every page was walked.

What it doesn't feed: projects and skills come from the session archive, which this test can't populate without a vault. Task 11's builder tests cover those surfaces, and the Tier 2 sweep (Task 16) checks them on a real profile. The statusline is Task 5's run tests (named below), because it is a separate process.

- [ ] **Step 1: Write the tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/StreamerSweepTests.cs`:

```csharp
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.Updates;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 7: the Done line, as a sweep of every Settings page and every surface outside it that a test can draw.</summary>
[Collection("SettingsWindow")]
public sealed class StreamerSweepTests : AppWindowFixture
{
    private const string Sentinel = "qx";
    private const string Shown = "Qxbeta";   // the one allowlisted name

    private (SettingsWindow Window, SettingsViewModel Svm, WidgetViewModel Widget, NoActivate Guard) Open()
    {
        var settings = JsonSerializer.Serialize(new
        {
            statusLine = new { type = "command", command = $"powershell -File \"{Path.Combine(Profile, ".claude-qxwork", "qx.ps1")}\"" },
            enabledPlugins = new Dictionary<string, bool> { ["qxplugin@qxmarket"] = true },
        });
        Home(".claude-qxwork", settings);
        Home(".claude-qxhome");
        // Three folders: ~\.claude (the fixture's), ~\.claude-qxwork and ~\.claude-qxhome. Two accounts.
        var (widget, _) = ScratchWidget("Qxalpha", "Qxbeta");
        widget.SetAccountFolder("Qxalpha", Path.Combine(Profile, ".claude-qxwork"));
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
            checkForUpdatesNow: () => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate)), isStoreBuild: false);
        svm.WeatherPage.Picked = WeatherPlace.Create("Qxcity", "Texas", "United States", 32.75, -97.33);
        svm.Publish.EndpointUrl = "https://qxhost.example/ingest";
        widget.Streamer.SetOn(true);
        widget.Streamer.SetShown(CodenameCategory.Account, Shown, true);
        svm.Desk.CiRunnersStatus = App.RunnerStatusText(new RunnerStatus(RunnerStatusKind.Watching, ["qxrunner-1"]));
        var window = new SettingsWindow(svm);
        return (window, svm, widget, NoActivate.Show(window));
    }

    private static void Settle(SettingsWindow window, NoActivate guard)
    {
        window.UpdateLayout();
        Pump();
        window.UpdateLayout();
        Pump();
        guard.Check();
    }

    /// <summary>Every piece of text under <paramref name="root"/>: TextBlocks (and their runs), text boxes, string content, tooltips and
    /// list items as their ToString.</summary>
    private static IEnumerable<string> TextsOf(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            switch (child)
            {
                case TextBlock t:
                    yield return t.Text;
                    foreach (var run in t.Inlines.OfType<Run>()) yield return run.Text;
                    break;
                case TextBox b:
                    yield return b.Text;
                    break;
                case ContentControl c when c.Content is string s:
                    yield return s;
                    break;
                case ItemsControl list:
                    foreach (var item in list.Items) yield return item?.ToString() ?? "";
                    break;
            }
            if (child is FrameworkElement { ToolTip: string tip }) yield return tip;
            foreach (var deeper in TextsOf(child)) yield return deeper;
        }
    }

    private static List<string> Leaks(IEnumerable<string> texts) =>
        [.. texts.Where(t => t is not null)
            .Select(t => t.Replace(Shown, "", StringComparison.Ordinal))
            .Where(t => t.Contains(Sentinel, StringComparison.OrdinalIgnoreCase)
                        || t.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase))
            .Distinct()];

    private static TabItem Tab(SettingsWindow window, SettingsPage page) =>
        ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == page.ToString());

    /// <summary>Done (spec, slice 7), Settings: every page, every visible text and every spoken name.</summary>
    [Fact]
    public void No_real_name_appears_on_any_settings_page_but_the_allowlisted_one() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            var leaks = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int walked = 0;
            foreach (var page in SettingsNames.Pages)
            {
                window.SelectPage(page, null);
                Settle(window, guard);
                if (page == SettingsPage.ClaudeCode)
                    foreach (var card in svm.ClaudeCodePage.Folders) card.HoldsOpen = true;   // the folder report too
                Settle(window, guard);
                var content = (DependencyObject)Tab(window, page).Content;
                var texts = TextsOf(content).Concat(NamesOf(window)).ToList();
                leaks.AddRange(Leaks(texts).Select(l => $"{page}: {l}"));
                foreach (var t in texts) seen.Add(t);
                walked++;
            }
            Assert.True(leaks.Count == 0, "Real names on screen:\n" + string.Join("\n", leaks));
            Assert.Equal(SettingsNames.Pages.Count, walked);

            // Not vacuous: each codename shows where its page shows the name, and the allowlisted name shows as it is.
            var mask = StreamerMode.Mask;
            string[] expected =
            [
                mask.Name(CodenameCategory.Account, "Qxalpha"),
                mask.Folder(Path.Combine(Profile, ".claude-qxwork")),
                mask.Folder(Path.Combine(Profile, ".claude-qxhome")),
                mask.Folder(Path.Combine(Profile, ".claude")),
                mask.Name(CodenameCategory.Runner, "qxrunner-1"),
                Shown,
            ];
            foreach (var code in expected)
                Assert.True(seen.Any(t => t.Contains(code, StringComparison.Ordinal)), $"\"{code}\" never showed: the sweep saw nothing it should have");
        }
        finally { window.Close(); widget.Dispose(); }
    });

    /// <summary>Done, outside Settings: the widget's chip and its spoken name, both account menus, the tray tooltip, the Desk's Claude line,
    /// runner cards and weather name.</summary>
    [Fact]
    public void No_real_name_appears_on_the_widget_the_tray_or_the_desk() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            var texts = new List<string> { widget.AccountSwitcherText, widget.AccountSwitcherName };
            var menu = new MenuItem();
            AccountMenuBuilder.PopulateSwitchList(menu.Items, widget, includeAdd: true, includeManage: true);
            texts.AddRange(menu.Items.OfType<MenuItem>().Select(m => m.Header?.ToString() ?? ""));
            texts.Add(SettingsNames.TrayTooltip(widget.Streamer.On, "Sanduhr für Claude — 5h 42%"));
            texts.Add(widget.CurrentDeskUsage.Account ?? "");
            var runner = new Watcher
            {
                Id = "r:k:build", Source = WatcherSource.Runner, Title = "qxrunner-1 · build", Short = "build", Runner = "qxrunner-1", RunnerKey = "k", Job = "build",
            };
            var (watchers, weather) = DeskController.StreamerFrameParts([runner], WeatherPlace.Create("Qxcity", null, "US", 1, 1), "");
            texts.AddRange(watchers.Select(w => w.Title));
            texts.Add(weather);
            Assert.Empty(Leaks(texts));
            Assert.StartsWith(SettingsNames.StreamerTooltipPrefix, texts.Single(t => t.Contains("5h 42%")));
            Assert.Contains(SettingsNames.StreamerIndicatorName, widget.AccountSwitcherName);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    /// <summary>Allowlisted names are the exception, and the switch off shows everything as it was.</summary>
    [Fact]
    public void Off_every_page_shows_the_real_names_again() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            widget.Streamer.SetOn(false);
            window.SelectPage(SettingsPage.Accounts, null);
            Settle(window, guard);
            Assert.Contains(TextsOf((DependencyObject)Tab(window, SettingsPage.Accounts).Content), t => t.Contains("Qxalpha", StringComparison.Ordinal));
        }
        finally { window.Close(); widget.Dispose(); }
    });
}
```

In `windows-dotnet/tests/Sanduhr.Tests/StreamerReachTests.cs`, add inside the class:

```csharp

    /// <summary>Decision 2: the toasts read no name (the alert toasts carry a tier label; the update toast a version). Pinned by the scan.</summary>
    [Fact]
    public void Toasts_read_no_name()
    {
        Assert.DoesNotContain(Scan(), s => s.File.EndsWith("AlertService.cs", StringComparison.Ordinal)
                                           || s.File.EndsWith("RestartToUpdateService.cs", StringComparison.Ordinal));
    }
```

The statusline's part of the Done line is Task 5's run tests: `Streamer_mode_on_prints_the_folders_codename_in_place_of_its_label`, `Streamer_mode_on_with_the_folder_unmapped_prints_no_label` and `An_unreadable_streamer_file_prints_no_label`.

- [ ] **Step 2: Check the sentinel can't be Sanduhr's own copy**

Run: `git grep -n -i "qx" -- windows-dotnet/src` (from the repo root)
Expected: no output. If Sanduhr's own code or copy contains "qx", pick another two-letter sentinel that `git grep` doesn't find, and use it in every slice 7 test.

- [ ] **Step 3: Run them**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~StreamerSweepTests > %TEMP%\slice7-t15.log 2>&1`
Then: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~StreamerReachTests > %TEMP%\slice7-t15-core.log 2>&1`
Expected: PASS. These pin behaviour built in Tasks 4 to 12, so they may pass at once. A failure names the page and the text. Each one is a missed site: find its producer (the reach inventory's tables, or a fresh grep), mask it in the owning file, mark it for Task 13's scan (its pinned counts change: update `Known` and say why), and run once more. Never widen `Leaks` to let a text through.

To show this test is not vacuous, prove it once: comment out the mask call in `AccountItemViewModel`'s constructor (`Shown = label;`), run the sweep, and confirm it fails naming `Accounts: Qxalpha`. Then restore the line. Report the RED line. This is the behavioural RED for a test that is written after the code it gates.

- [ ] **Step 4: Commit**

```bash
git add windows-dotnet/tests/Sanduhr.App.Tests/StreamerSweepTests.cs windows-dotnet/tests/Sanduhr.Tests/StreamerReachTests.cs
git commit -m "test(settings-v2): slice 7's Done line, a sentinel sweep of every page and surface with streamer mode on"
```

(Add any choke-point file Step 3 had to fix.)

---

### Task 16: End to end: the harness, the whole suite, the owner's "streamer sweep" and slice 8's carry

**Files:**
- Modify: `windows-dotnet/tools/Sanduhr.RenderHarness/SettingsPassScenario.cs` (the switch's focus shot)
- Create (not committed with code): `.superpowers/sdd/2026-10-26-settings-v2-slice-7/tier2-checklist.md`, `.superpowers/sdd/carry-into-slice-8/slice-7/carry.md`

**Interfaces:**
- Consumes: everything above.
- Produces: `anchors-landing.txt` lines for `privacy/streamer` and `privacy/streamer-allowlist` at 800 and 600; a green suite; the Tier 2 hand-off; slice 8's carry.

- [ ] **Step 1: The harness entry**

In `windows-dotnet/tools/Sanduhr.RenderHarness/SettingsPassScenario.cs`, replace

```csharp
        await Shot("privacy-link-button", SettingsPage.Privacy, () => ByName(c.Host, NetworkDisclosure.All[0].OffLinkName));
```

with

```csharp
        await Shot("privacy-link-button", SettingsPage.Privacy, () => ByName(c.Host, NetworkDisclosure.All[0].OffLinkName));
        await Shot("privacy-streamer-switch", SettingsPage.Privacy, () => ByName(c.Host, SettingsNames.StreamerSwitch));
```

The pages loop and the anchors table walk `SettingsNames.Pages` and `SettingsIndex.Anchors`, so the two new anchors join both with no other change. The harness never turns streamer mode on: its pictures use the fake profile, and the banner's shot is the owner's (Tier 2).

- [ ] **Step 2: Build and run the harness's links part off screen, against a scratch profile**

Run: `dotnet build tools/Sanduhr.RenderHarness > %TEMP%\slice7-t16-build.log 2>&1` (from `windows-dotnet/`)
Expected: Build succeeded, 0 errors, no new warnings.

From the repo root, in PowerShell:

```powershell
$env:SANDUHR_DEV_PROFILE = Join-Path $env:TEMP "sanduhr-slice7-profile"
New-Item -ItemType Directory -Force $env:SANDUHR_DEV_PROFILE | Out-Null
dotnet run --project windows-dotnet/tools/Sanduhr.RenderHarness -- --scenario settings-pass --parts links --out (Join-Path $env:TEMP "sanduhr-slice7-out") *> (Join-Path $env:TEMP "slice7-t16-harness.log")
```

Expected: the run ends normally. Its `settings-pass: foreground window before X, after X` line shows the same handle twice.

- [ ] **Step 3: Check every new landing**

```powershell
$out = Join-Path $env:TEMP "sanduhr-slice7-out\settings-pass"
Select-String -Path (Join-Path $out "anchors-landing.txt") -Pattern "^Privacy / .* \| (800|600) \|" |
    Where-Object { $_.Line -notmatch "\| (fully in view|top in view)[^|]* \|[^|]*\| ok$" }
Select-String -Path (Join-Path $out "deeplinks.txt") -Pattern "(CARD|TOUR)-LANDING" |
    Where-Object { $_.Line -notmatch "at 800: (fully in view|top in view)[^;]*; ok" }
```

Expected: no output.
- `streamer`, `streamer-allowlist`, `network` and `agents` each land in view at 800 and 600.
- Every What's New card, the two 4.0.0 cards included, lands in view.

A printed line names the anchor or card and why. Fix the landing, then run Steps 2 and 3 once more. The evidence under `%TEMP%` is never committed.

- [ ] **Step 4: The whole suite and the contracts**

Run: `dotnet test Sanduhr.slnx > %TEMP%\slice7-t16-suite.log 2>&1` (from `windows-dotnet/`)
Expected: PASS across `Sanduhr.Tests`, `Sanduhr.App.Tests` and `Sanduhr.Mcp.Tests`, with every `[DesktopFact]` and `[PowerShellFact]` included (this is a desktop session with Windows PowerShell). Read failures from the log file. Then:

```bash
git diff release/4.0 --stat -- windows-dotnet/tests/Sanduhr.Mcp.Tests/TrustBoundaryTests.cs windows-dotnet/src/Sanduhr.Mcp
git diff release/4.0 -- windows-dotnet/tests/Sanduhr.Tests/SettingsKeysContractTests.cs
git diff release/4.0 --stat -- mac/
```

The first prints nothing: the MCP's link list, its pin and the MCP source are untouched. The second shows exactly one added line, `+        "streamer_mode",`. The third prints nothing: the meters mod is not changed (decision 5).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/tools/Sanduhr.RenderHarness/SettingsPassScenario.cs
git commit -m "test(settings-v2): the harness lands Streamer mode and Show the real name at 800 and 600 and shoots the switch's focus"
```

- [ ] **Step 6: Hand the Tier 2 checks, the owner's calls and slice 8's carry over**

None of this is code, and none of it is for the agent: these checks need the real app, a real profile and real Claude Code folders, which this plan never launches. Write `.superpowers/sdd/2026-10-26-settings-v2-slice-7/tier2-checklist.md` with these items, and post them to the owner:

1. **The streamer sweep** (spec, slice 7, Tier 2: "walk every page and surface on a real profile with streamer mode on and record any real name seen"). Turn it on from the tray, then:
   - Every Settings page, top to bottom, with each collapsed section opened: Accounts, Privacy (the list, both captions), Claude Usage (Meters, Overview, Trends, Sessions with a row open, the calendar's day click, Session archive), Publish usage (and Show a sample), Claude Code (every card, What this folder holds open, each consent dialog up to its question), Mods & Widgets (Installed, the meters rows), Weather (Look up a city), Watchers, Desk (the companion picker), About.
   - The widget: the chip and its glyph, the right-click Accounts menu, the signed-out and sign-in-again cards.
   - The tray: the tooltip and the Accounts menu.
   - The Desk: the Claude line in each meter style with two accounts, a sign-in reminder, a CI runner's card, the weather piece with and without "Show it as".
   - The statusline in each Claude Code folder, including one with `SANDUHR_LABEL` set and plain `.claude`.
   - The meters mod's label above the prompt (decision 5: the person's own option, unchanged).
   - Record any real name seen, with where.
2. **What it says it can't cover, checked.** With streamer mode on, open the sign-in window (claude.ai shows its own page), ask Claude Code something that calls `ping` and `get_local_burn_by_project`, and let Claude start a watcher. Confirm the caption's three items are the only names left, and that they are worded truthfully.
3. **Shuffle on stream.** With Settings open on Claude Code and the Desk visible, Shuffle: every codename changes at once, on every surface (the statusline on its next render), and the ticked names stay real.
4. **The quick routes.** Show a real name from an Overview row, a Sessions row (Shift+F10), a folder card's menu and a Mods & Widgets row (Shift+F10); then untick each in Privacy's list.
5. **Restart.** Quit and relaunch with streamer mode on: the same codenames, the banner, the glyph and the tray prefix are back before the first fetch.
6. **Reset.** Reset with All settings: `%APPDATA%\Sanduhr\codenames.json` and `%USERPROFILE%\.sanduhr\streamer.json` are gone, other names in `~\.sanduhr` (such as `time\`) are untouched, and nothing writes either file back before quit.
7. **Narrator.** The switch, the Shuffle button, a list row ("Show the real name on stream, Dune Fox"), the banner's link ("Privacy settings…, Streamer mode") and the chip ("⇆ Dune Fox, Streamer mode on").
8. **The owner's calls.** The taste list above (13 lines, each with its default), and **P6** (MCP answers), with the recommendation: A for 4.0.

Write `.superpowers/sdd/carry-into-slice-8/slice-7/carry.md`:

```markdown
# Carried from slice 7 into slice 8

## Reset
- `ResetPlan`'s Settings part ends with codenames.json and streamer.json (slice 7). Slice 8's per-folder Claude Code list goes in its own
  part; don't move those two lines.
- Reset's About caption (spec, Reset) should name streamer mode's files only if it names files at all: it names kinds of things today.

## Themes and contrast
- `StreamerBanner` uses Sanduhr.Brush.Bg, .Accent and .Text. If ThemeContrastTests raises TextDim and TextMuted, the banner is unaffected;
  if it changes Accent, check the banner's border against Bg in all six themes.

## Merge surface
- SettingsWindow.xaml: banners under the first line of Accounts, Claude Usage, Publish, Desk, Weather and About; About's three rows bind
  *Shown for text and tooltip and the real path for the command. Slice 8's Themes block shares no line with these.
- SettingsNames.cs: the slice 7 block follows StoreKeepsUpToDate.

## Not done, and why
- MCP answers under streamer mode (P6): owner decision; the recommendation was A (leave and disclose) for 4.0.
- An OBS capture source: spec "Not in 4.0".
```

---

## Carried in, and where each went

Every row of `.superpowers/sdd/carry-into-slice-7/slice-6/carry.md`, the slice 6 final review's P6 addition, and the lessons the slice 6 ledger, preflight and final review recorded:

| # | Carried item (source) | Where |
| --- | --- | --- |
| 1 | Privacy's marked seat: replace it verbatim; `streamer` and `streamer-allowlist` before `network` (carry) | Task 6 Step 6 (the seat, word for word), Step 3 (the entries before `network`) |
| 2 | The order under Privacy's first line: banner, jump links, streamer section, What leaves this PC (carry) | Task 6 Step 6; pinned by `Privacy_puts_the_banner_then_the_jump_links_then_streamer_mode_first` |
| 3 | Privacy's jump links once it has four sections: the `JumpLinks` arm, `<views:SectionJumpLinks Page="Privacy" />`, the `DeskSplitPagesTests` row, and `Privacy_holds_two_sections_and_no_jump_links` changed (carry) | Task 6 Steps 1 and 3 |
| 4 | Mask the folder names Privacy shows, and add Privacy to the reach table (carry) | Task 9 (CP 7 at `ClaudeCodeHost.Display`, which `PrivacyViewModel.ReadAgents` already calls); `Privacys_folder_names_are_codenames`; the reach inventory's CP 7 row; `SettingsIndex.StreamerPages` |
| 5 | P6: MCP answers carry the user folder and folder names (`ping`, `get_desk`), plus `propose_theme`'s `saved_path` and `previous_key` (carry; slice 6 final review I2) | "The P6 decision", options A, B and C with costs; recommendation A; the caption names MCP answers (decision 11; Task 3) |
| 6 | NetworkDisclosure needs nothing from streamer mode (carry) | Global Constraints: no outbound call is added; `NetworkDisclosureTests` must stay at 40 and 25 |
| 7 | Copy that is true in only some states (slice 6 P4 to P6, P11, P12, P17, P19; final review M4, M5) | The can't-reach caption is exact and pinned (Task 3); `TrayTooltip` and `StreamerChipName` have both forms pinned; decision 2 corrects the spec's toast and tooltip claim |
| 8 | Inventories that miss a site (slice 6 P1, final review I2) | "The reach inventory": two independent sweeps, 103 sites, 27 beyond the spec; Task 13's scan (pinned counts, a floor, synthetic shapes); Task 15's sentinel sweep, which needs no list of sites |
| 9 | Live-region-only status lines (slice 5 P4, carried through slice 6) | Global Constraints, Live regions: nothing new changes while shown except Watchers' line, which already raises |
| 10 | Subscriptions to the long-lived widget must detach on Settings Closed | Decision 15; Task 6's `Closing_settings_detaches_it_from_the_service`; the banner subscribes to nothing |
| 11 | Non-unique Edit anchors (slice 6 P8) | Global Constraints names the two repeats (`WeatherNickname = …` twice, `Content="{Binding Name}"` three times) and how each is handled |
| 12 | Leaked WidgetViewModels in tests (slice 5 P21) | Global Constraints; every new App test disposes its widget in `finally`; `AppWindowFixture` resets `StreamerMode` |
| 13 | Search words that steal another page's queries; use Boosts (slice 6 P2) | Decision 17; the spec's words stay Boosts; `Words_other_pages_own_do_not_land_on_streamer_mode` |
| 14 | Timings in copy that don't come from constants (slice 6 final review M4, M5) | Global Constraints: this slice's copy quotes no interval |
| 15 | RED first, behavioural for gates (slice 6 ledger: the controller's mutation tests) | Global Constraints; every masking task adds non-masking stubs first so the RED is a sentinel on screen; Task 15 Step 3's mutation proof |
| 16 | Reset deletes only Sanduhr's names in `~\.sanduhr` (repo CLAUDE.md) | Task 2's `Reset_lists_both_files_by_name_only`; Task 4's `After_reset_the_service_writes_nothing` |

## Carried, not in this slice

- **MCP answers under streamer mode** (P6): the owner's call; the recommendation is A for 4.0, with B as a one-task follow-up if chosen, and C with the OBS source.
- **The OBS-friendly capture source:** spec "Not in 4.0".
- **A demo-mode starter:** spec "Not in 4.0"; the demo line explains `--demo` and `SANDUHR_DEMO=1` only.
- **The meters mod** reading `streamer.json`: not needed (decision 5); revisit if Sanduhr ever writes the mod's label or `band.json` on Windows.
- **Reset's per-folder list and its caption:** slice 8 (carry file above).
- **The Tier 2 checks** (Task 16 Step 6): the owner, on a real profile.

---

## Spec coverage (slice 7)

| Slice 7 bullet, Done item or Tier 2 item (spec) | Task |
| --- | --- |
| `Codenames`: pool, salt, first-seen-and-kept maps, shared used set, allowlist, Shuffle; `codenames.json` | 1, 4 |
| `StreamerMask` applied at every surface in the reach table | 2 (the formatter), 7 to 12 (the choke points), 13 (the scan), 15 (the sweep) |
| The tray and widget toggles and indicator | 7 |
| `streamer.json` and the statusline script reading it | 2, 4, 5 |
| Reset entries | 2 |
| The `streamer_mode` key | 4 |
| The allowlist list and its quick routes | 6 (list), 9, 10, 11 (routes) |
| The What's New card (and "Settings, rearranged", spec "Outside Settings, same release") | 14 |
| The switch's words, its caption, "what it can't reach", the demo line (spec, Streamer mode) | 3, 6 |
| The banner on every page that masks something | 6 |
| Search: stream, streamer, screen share, hide names, obs, demo, codename | 6 |
| `CodenamesTests`: pool size and deny list; kept across reloads; no codename shared across categories; Shuffle changes all and keeps the allowlist; unmapped names masked; overflow numbering | 1, 2 |
| `StreamerMaskTests`: each surface's formatter | 2, and each masking task's tests |
| A statusline script test with `streamer.json` on and off | 5 |
| `ResetPlanTests`: both files removed by name only | 2 |
| `WhatsNewTests`: the new card | 14 |
| `SettingsKeysContractTests`: `streamer_mode` | 4 |
| The meters mod: "Slice 7 checks whether it draws one" | Decision 5 (it draws only the person's own label; no change) |
| Done: two accounts, three folders, streamer mode on, no real name except allowlisted ones | 15 (with 5 for the statusline) |
| Tier 2: "streamer sweep" | 16 Step 6 |
| WF7 disposition ("streamer mode; demo mode unchanged") | 1 to 15 |

## Self-review (writing-plans checklist, run once)

1. **Spec coverage.** Every slice 7 bullet, the Done line and the Tier 2 item map to a task above. Of the spec's file list, every file is touched except "toast code", which decision 2 shows needs nothing (pinned in Task 15). The extra files are listed with their reasons.
2. **Placeholder scan.** No "TBD", no "add appropriate", no "similar to Task N". Two places ask the implementer to read before editing, and say exactly what to look for:
   - Task 11's three table templates, anchored by their `ItemsControl` names because two root `Grid`s are identical.
   - Task 13's `Known` table, which is filled from the scan's own output because Tasks 7 to 12 add matching lines. The base counts at 3cc3699d are in "The reach inventory".
3. **Type consistency.**
   - `CodenameCategory`, `CodenameBook` (`Assign`, `TryGet`, `IsShown`, `SetShown`, `Shuffle`, `Entries`, `ToJson`, `FromJson`) and `Codenames` (`Load`, `Save`, `IsCodename`, `DefaultShown`) are what Tasks 2, 4 and 6 use.
   - `StreamerMask` (`Name`, `Hides`, `Folder`, `FolderName`, `HidesFolder`, `FolderKey`, `AddOn`, `Paths`, `Endpoint`, `HostsIn`, `Watchers`, `WeatherName`, `PublishSample`, `Assigned`) is what Tasks 4 and 7 to 12 call.
   - `StreamerFile.FolderRef`, `Build`, `Write` and `StreamerFolder` are what Tasks 4 and 5 use.
   - `StreamerService` (`Load`, `SetOn`, `Shuffle`, `SetShown`, `Entries`, `Flush`, `Changed`, `On`, `Mask`) and `StreamerMode` (`Mask`, `On`, `Use`, `Reset`, `ShowFolder`, `FoldersIn`) are what Tasks 6 to 15 use.
   - `StreamerSettingsViewModel` (`On`, `Rows`, `ShuffleCommand`, `ConfirmShuffleHook`, `NamesChanged`, `Read`, `Detach`) and `CodenameRow` are what the XAML and Task 6's tests bind.
   - `SettingsViewModel.ReloadNames` grows in Tasks 6, 10, 11 and 12, each by the lines the task names.
4. **Review Focus.** The five lines above each have their test in the owning task: Tasks 1 and 2, 2 and 5, 9, 1, and 2 and 4.
