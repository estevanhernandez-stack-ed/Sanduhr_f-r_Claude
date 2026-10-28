# Settings v2 for Windows, Slice 6 ("Privacy, network and agents") Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A new Privacy page answers "what leaves this PC" from one Core list, `NetworkDisclosure.All`, that a source scan holds to every outbound call in `windows-dotnet/src`, and "what Claude can change" from one Core list, `AgentConsents.All`, that a scan holds to every tool the MCP server offers. About gains Updates, where "Check for updates once a day" (new key `update_check`, default on) gates every update probe, beside "Check now". About gets its jump links, and the What's New window's own box stops going stale.

**Architecture:** Core comes first. `UpdateCheck` holds the daily rule, `UsageSchedule` holds the 5-minute refresh the copy quotes, and `StoreUpdate.ShouldCheck` learns the switch. Then the slice's copy goes into `SettingsNames`. Then `NetworkDisclosure` arrives, with `// network: <row>` markers at every call site and `NetworkDisclosureTests` pinning the inventory file by file. Then `AgentConsents`, with a scan of `ToolCatalog.cs`. In App, `UpdateChecker` is rebuilt around one `IUpdateProbe` (the only code that asks GitHub) and rides the widget's tick. `StoreUpdateService` honours the same switch. `PrivacyViewModel` reads each consent from the store its own page writes, and the MCP install from the folders. `PrivacyPage.xaml` is a new own-file page after Alerts. About's Updates section is inline in `SettingsWindow.xaml`. Last, the Done line runs as tests, the harness lands the new anchors, and the Tier 2 checks go to the owner.

**Tech Stack:** .NET 10, WPF, xUnit 2.9 (Xunit.SkippableFact for `[DesktopFact]`), CommunityToolkit.Mvvm 8.4, Velopack 0.0.1298, WinRT `Windows.Services.Store`, the render harness (`tools/Sanduhr.RenderHarness`).

**Spec:** `docs/settings-v2-windows/spec.md`, "Shippable slices, Slice 6: Privacy, network and agents", is the authority on scope. It works with these page sections:

- "Privacy (`privacy`, new)", items 2 to 4. Item 1, Streamer mode, is slice 7's.
- "About (`about`)", Updates.
- "Sidebar: 17 pages in 5 groups", the Privacy row.
- "Search", the privacy, network and update synonyms.
- "Keyboard and screen reader", jump links.
- "Mapping", the `update_check` row and the "What leaves this PC, What Claude can change" row.
- "Settings keys and migrations", `update_check`.
- The disposition rows for WF6 and WF10.

The other inputs:

- Findings: `docs/settings-v2-windows/findings.md` WF6, WF10 and Tier 2 item 6.
- Inventory: W9, W18 and W25.
- Carried in: `.superpowers/sdd/carry-into-slice-6/slice-5/carry.md`.
- Lessons: `.superpowers/sdd/2026-10-25-settings-v2-slice-5/progress.md`, `preflight.md` and `final-review.md`.
- The executed slice 5 plan, `docs/superpowers/plans/2026-10-25-settings-v2-slice-5.md`, is the model for conventions and helpers.

## Spec questions decided in this plan

1. **A call site is a line that opens a connection or hands an address to something that will.** The scan looks for these shapes:
   - `new HttpClient(`, and an HttpClient send (`.GetAsync(`, `.GetStringAsync(`, `.SendAsync(` and the rest).
   - A WebView2 navigation or in-page script: `.Navigate(`, `.NavigateToString(`, `ExecuteScriptAsync(`.
   - Velopack: `new GithubSource(`, `new UpdateManager(`, `.CheckForUpdatesAsync(`, `.DownloadUpdatesAsync(`.
   - The Store: `GetAppAndOptionalStorePackageUpdatesAsync(`, `RequestDownloadAndInstallStorePackageUpdatesAsync(`.
   - DNS and sockets: `Dns.`, `new TcpClient(`, `new Socket(`, `new ClientWebSocket(`, `WebClient`, `WebRequest.Create`, `HttpWebRequest`.
   - `Process.Start(` and `LaunchUriAsync(`.

   Each matching line carries `// network: <id>`, on the line or within the three lines above it. `<id>` is one of:
   - a row of `NetworkDisclosure.All`;
   - `your-browser`, an address handed to the person's browser;
   - `local`: a named pipe, Explorer, Windows Settings or a relaunch.

   The inventory below is what the scan finds at the base commit: 39 lines. 24 are outbound calls Sanduhr makes, 6 are browser hand-offs and 9 are local.
2. **The table has nine rows, not the spec's seven.** Being exactly true needs three more.
   - The Store build asks the Microsoft Store every 6 hours (`StoreUpdateService`). The spec's "GitHub, update check" row doesn't cover that.
   - claude.ai's usage row also names the routine budget call.
   - What Sanduhr's MCP server answers reaches Claude's service through Claude Code. Sanduhr makes no call, but the data leaves the PC. So it gets a row whose caller is Claude Code (`NetworkCaller.ClaudeCode`) and which has no call site.
3. **"Nothing else leaves this PC. Sanduhr has no analytics." becomes "Nothing else leaves this PC from Sanduhr. Sanduhr has no analytics."** The spec's sentence is not true without that qualifier. Two notes go under it:
   - The claude.ai windows run in Microsoft Edge WebView2. The pages are claude.ai's own and run claude.ai's code, and WebView2 can contact Microsoft as Edge does. Microsoft Defender SmartScreen is on by default, and Sanduhr doesn't turn it off.
   - A link the person clicks opens in their own browser. That is the browser visiting the page, not Sanduhr sending anything.
4. **`ClaudeApiClient` (Core) is in the inventory**, marked `claude-usage`, though the app never builds it. `WidgetViewModel.cs:1050` and `InactiveUsageReader.cs:45` build `WebView2ApiClient`; only tests build the HttpClient transport. The scan holds every shipped line, built or not.
5. **The Docker pipe is HTTP over `\\.\pipe\docker_engine`**, with no TCP (`DockerPipe.cs:26`). It is marked `local` and listed in the inventory. It gets no row.
6. **`update_check` gates both update probes.** With it off:
   - `UpdateChecker` (GitHub, the .exe build) asks nothing, at launch or on the tick.
   - `StoreUpdateService` asks the Store nothing on its 6-hour tick.

   The Store build shows "The Microsoft Store keeps Sanduhr up to date." in place of the switch (spec). On that build the key stays at its default, and the row says so ("No switch on the Store build"). Owner taste 6 offers the switch there too.
7. **"Check now" asks with the switch off.** Pressing it is the person asking, so it runs whatever `update_check` says. The caption under it says so: "Check now asks once, even with the switch off."
8. **The GitHub check also runs while Sanduhr runs, not only at launch.** Today `UpdateChecker` runs once at launch, debounced to 24 hours, so a widget left running for a week never checks, and "once a day" is false. It now also rides the widget's 30-second tick and asks once 24 hours have passed. The interval is `UpdateCheck.Interval`, which the copy quotes.
9. **"What Claude can change" lists every MCP tool, in nine rows.** The spec names four consents. Being exactly true needs the rest of what the server offers:
   - `publish_usage` follows "Publish daily at".
   - `propose_theme` has no switch and applies a theme when asked.
   - `propose_desk_messages` and `propose_widget` always wait for the person.
   - `get_usage`, `get_desk`, `get_desk_messages` and `ping` read with no switch.

   `AgentConsentsTests` reads `src/Sanduhr.Mcp/ToolCatalog.cs`, and fails when a tool is in no row or in two.
10. **Each consent's state is read from the store its own page writes.** In the app these are one file. In tests they are not: the widget's keys live in `ScratchPaths()`, and the Settings pages' keys live in the fixture's dev profile.
    - Watchers, Desk and Now playing write through `new SettingsStore(new Paths())`, so Privacy reads those three there.
    - The Claude Code cards write `mcp_roots` through the widget, so Privacy reads `widget.LoadMcpRoots()`.
    - Publish usage writes `publish.enabled` through the widget's publish service, so Privacy reads `widget.Publish?.Load().Enabled`.
11. **The MCP install status is what the Claude Code cards show.** For each discovered Claude Code folder, Privacy calls `McpIntegrationInstaller.IsRegistered(name)`, the same call the cards make (`ClaudeCodeViewModel.cs:575`). It is true when a file Claude Code reads for that folder holds Sanduhr's `mcpServers.sanduhr` entry.
    - Hand-added folders can't hold the MCP server (`ClaudeCodeViewModel.cs:582-584`), so they aren't counted.
    - The line reads "Sanduhr's MCP server is installed in 1 of 2 Claude Code folders."
    - The spec's "None of these work until the MCP server is installed in a folder." drops "in a folder". A server set up by hand outside Claude Code folders would still work.
12. **Privacy's "Where settings live" is a status line, not an indexed section.** Its home is About (spec: "The rows stay on About"). The page ends with one line and "About settings…" to `about#where`, with no anchor and no search row. So Privacy has two sections in this slice, `network` and `agents`, and no jump links: the spec gives them to pages with four or more.
13. **About gets jump links** (carry, P5). They are Version, Updates, Trademark, Where settings live, Reset Sanduhr… and Third-party notices, in page order, labelled with their index titles (owner taste 9).
14. **The What's New window's box reads the key again on Activated** (carry, the reverse of P14). `WhatsNewViewModel.Reload()` re-reads the key and raises the change, writing nothing; the window calls it from `Activated`. That is the same rule About follows, and smaller than a shared change event.
15. **The Privacy page is its own file**, `Views/PrivacyPage.xaml`, as Watchers and Launcher are. No test or code finds its controls through `window.FindName`. About stays inline, because existing tests find About's controls by name.
16. **"privacy" leaves About's page synonyms.** Two sets of boosts already exist in `SettingsIndex.Boosts` and go live with this slice:
    - `privacy`, `offline`, `network` and `send` point at `privacy#network`.
    - `update` and `updates` point at `about#updates`.

    So the new page's own name lands on it first, which is what slice 5's live check asked of "history".
17. **No link on the page opens the Catalog.** The widget catalog row's link is "Mods & Widgets settings…" to `mods#mods`. Landing on `mods#catalog` opens the section, and opening it fetches.
18. **The privacy policy link goes through `SettingsViewModel.OpenLinkCommand`**, the existing `your-browser` site, so the page adds no `Process.Start`. The URL is GitHub's rendering of `docs/PRIVACY.md` (owner taste 4).
19. **Every number in the copy comes from the code that acts on it.** The constants are:
    - `UsageSchedule.Refresh` (new; `WidgetViewModel.RefreshInterval` becomes it)
    - `AccountFollow.CheckInterval`
    - `WeatherSchedule.Refresh`
    - `UpdateCheck.Interval` (new)
    - `StoreUpdate.CheckInterval`

    The copy is built from them with `=>` properties, so a changed interval changes the words.
20. **The rows' copy lives in `SettingsNames`**, under the rule that new user-facing text is a `SettingsNames` member. `NetworkDisclosure` and `AgentConsents` hold ids, links, hosts and tool names, and take every sentence from the names table.
21. **The list rows' records override `ToString`.** `NetworkRow` reads as its destination, and `AgentRow` as "name, state". A plain `ItemsControl`'s item peer can fall back to the item's text. A record's default text ("NetworkRow { Id = … }") is exactly what `NoPageReadsAsATypeNameOrARecord` forbids.
22. **`docs/PRIVACY.md` changes with the code that makes it true.** Task 2 adds the update switch. Task 4 adds the routine budget endpoint, WebView2 and "through Claude Code". The hosted policy at 626labs.dev needs the same edits, made through a hub issue: that goes to the owner (Task 11, Tier 2).

## Owner's taste (decided with a default; each is one line to change)

1. **The Privacy page's first line** reads "What Sanduhr sends off this PC, and what Claude can change on it." (`SettingsNames.PrivacyLine`).
2. **What leaves this PC is a list of cards**, not a grid. Each card has:
   - the destination as a small heading;
   - its hosts, in a monospace line;
   - When, Carries and Off;
   - the link.

   Cards wrap at 680 DIP, where a four-column grid would clip.
3. **The rows keep the spec's order**: claude.ai usage, sign-in window, Open-Meteo, GitHub catalog, GitHub updates, Publish, Publish by Claude. The Microsoft Store goes after GitHub updates, and "Claude's service, through Claude Code" goes last.
4. **The privacy policy link opens GitHub's rendering of `docs/PRIVACY.md`** (`https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/docs/PRIVACY.md`), not the hosted page at 626labs.dev.
5. **What Claude can change puts the spec's four first**, in the spec's order, then publish, theme, messages, widget packs and reads.
6. **The Store build shows its line only** (spec), with no switch. The alternative shows "Check for updates once a day" there too, gating the Store's 6-hour question. The code already honours the switch on that build (decision 6).
7. **Check now's words.** It shows "Checking GitHub for a newer Sanduhr…", then one of:
   - "Sanduhr is up to date."
   - "Sanduhr 4.0.1 is ready. Restart to update is in the tray menu and on the widget."
8. **The consent states read "On", "Off", "Asks you first" and "No switch".** Publish reads "On, with Publish daily at", and the folder reads read "On for ~\.claude. Off for ~\.claude-work.".
9. **About's jump links use the sections' index titles**, as every other page's do: "Version · Updates · Trademark · Where settings live · Reset Sanduhr… · Third-party notices".
10. **The updates caption** reads "Sanduhr asks GitHub for a newer release at most once every 24 hours, when it starts or while it runs. A new version downloads in the background and installs only when you choose Restart to update."

## Global Constraints

- **CRLF working tree, LF index.** Edit tracked files with the Edit tool only.
  - **Never `sed -i`, ever.**
  - Never rewrite a tracked file wholesale with Write. New files may be created with Write.
  - Before an Edit, confirm its anchor is unique: `git grep -c -F "<anchor>" -- <file>` prints 1. Slice 5's P6 and P7 were anchors that weren't.
- **Keys.** One `settings.json` key is added: `update_check` (bool, on when absent). It goes into `SettingsStore` and into `SettingsKeysContractTests.Frozen` (Task 1). No key is renamed or removed. `streamer_mode` is slice 7's.
- **Trust boundary.** No file is added to `Sanduhr.Mcp.csproj`'s `Compile Include` list, and no file on that list is edited.
  - The new Core files (`UpdateCheck.cs`, `UsageSchedule.cs`, `NetworkDisclosure.cs`, `AgentConsents.cs`) are off the list.
  - So are the changed ones: `StoreUpdate.cs`, `SettingsNames.cs`, `SettingsIndex.cs`, `WhatsNew.cs`, `ClaudeApiClient.cs`, `UsagePublisher.cs`, `Startup.cs`.
  - No `src/Sanduhr.Mcp` file changes, and `TrustBoundaryTests` stays green untouched.
- **The MCP is network-free by contract.** `TrustBoundaryTests` pins no `PackageReference` or `ProjectReference`. It also scans the linked Core files for `HttpClient`, `System.Net`, `Socket`, `WebView` and `Process.Start`. But nothing scanned the server's own files.
  - `NetworkDisclosureTests` now does. It fails on any call shape under `src/Sanduhr.Mcp`, marker or not.
  - At the base commit, the server's own files have none.
- **`~\.sanduhr` is shared ground.** This slice adds no file there and deletes nothing there.
- **Hands off the live app.** Never start, stop, kill or attach to `Sanduhr.exe`. If a build fails because a `Sanduhr.exe` holds a file, stop and report BLOCKED.
- **No test reads the real AppData**, LocalAppData, `~\.sanduhr`, a real Claude Code folder or Credential Manager. The update stamp's real path (`UpdateChecker.DefaultStampPath`, in LocalAppData) is never used by a test: every test passes a scratch stamp path.
- **No test touches the network.** The update probe is a counting fake, the Store query is a counting fake, and the catalog is the fixture's `OfflineHandler`.
- **Test hygiene.**
  - An App test class carries `[Collection("SettingsWindow")]` when it derives from `AppWindowFixture`, builds a `SettingsWindow` or `SettingsViewModel`, uses `ScratchWidget`, or touches a process-wide static.
  - Windows are shown through `NoActivate.Show(window)`, and `guard.Check()` follows anything that moves focus. Keyboard focus is routed with `guard.RouteKeyboard()`.
  - `[DesktopFact]` is skippable and runs locally.
  - **A widget a test builds is disposed in a `finally`** (`widget.Dispose()`). Slice 5's P21 leaked one into later tests.
  - A Core test that reads `TierModel`'s registry carries `[Collection(TierRegistryCollection.Name)]` (P8). This slice's Core tests read none.
- **Event lifetime.** Nothing in this slice subscribes a Settings view model to the long-lived `WidgetViewModel`. `PrivacyViewModel` and `AboutUpdatesViewModel` read on each visit (`Tabs_SelectionChanged`) and hold no handler, so `SettingsWindow`'s `Closed` gains no `Detach`. A task that finds it needs a subscription adds the matching `Detach` in `Closed` in the same commit, and says so in its report.
- **Implementers keep full test logs.** Redirect each run to a file (`dotnet test … > %TEMP%\slice6-<task>-<n>.log 2>&1`) and read failures from that file.
- **Run tests from `windows-dotnet/`.**
  - Core: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~<Class>`.
  - App: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~<Class>`.
  - The server: `dotnet test tests/Sanduhr.Mcp.Tests`.
- **Names.** Every new visible or spoken string is a `SettingsNames` member or built from one. It passes `SettingsNames.PathProblems`, `RetiredNames` and `UnknownLinks`. A control's spoken name starts with its visible text, and per-row context follows after a comma ("Weather settings…, Open-Meteo, weather").
- **Index with the XAML.** Three things move together:
  - An anchor joins `SettingsIndex.Anchors` in the commit that marks it in XAML.
  - A sidebar change lands with its TabItem.
  - A `SettingsNames.Table` row lands with its `NameAnchors` entry.
- **Own-file pages.** `PrivacyPage.xaml` uses the window's styles through `DynamicResource` (`SettingsLinkButton`, `FlatButton`).
  - Its `ScrollViewer` is `Focusable="False"`.
  - Its two lists are `ItemsControl`s with `Focusable="False"` and a name.
  - Neither list can be empty: both are built from fixed Core lists.
- **Live regions.** A line that must be announced raises `LiveRegionChanged` through `views:LiveRegion.Shown`, which raises only when the bound value turns true. `LiveSetting` alone is silent (slice 5's P4). So a status line whose text changes from one message to another pulses its flag: false, then the new text, then true. About's Check now status does this (Task 8).
- **No control that can't act.** "Check now" with no checker wired (tests, the harness) is disabled, not dead.
- **Privacy copy must be exactly true.** Every row of What leaves this PC and What Claude can change says what the code does: no more, no less. That is not taste, and the owner's taste list never touches it.
- **Branch.** Tasks run in numeric order on `feat/4.0-slice-6`, cut from `release/4.0` at 8dae01fc.

## Review Focus

1. **Check now pressed while the launch probe is still running.** One probe runs, both callers get its answer, and one Restart to update offer appears. Pinned in Task 2 (`Check_now_shares_a_probe_already_running`).
2. **The switch turned off after a probe ran.** The next tick, a day later, asks nothing, and the stamp is not touched. Pinned in Task 2 (`Turning_it_off_stops_the_next_tick`).
3. **A stamp from the future, or one that can't be read.** The check still runs; neither parks it forever. Pinned in Task 1 (`A_stamp_from_the_future_or_unreadable_is_due`) and Task 2 (`An_unreadable_stamp_still_lets_it_ask`).
4. **A consent changed on its own page, then Privacy visited in the same window.** Privacy shows the new state, because it reads on each visit. Pinned in Task 7 (`A_consent_changed_on_its_page_shows_on_the_next_visit`).
5. **The catalog row's link.** It lands on the Installed list and fetches nothing: the Catalog stays closed. Pinned in Task 7 (`The_catalog_row_lands_on_the_installed_list_and_fetches_nothing`).

---

## Where this plan steps outside the spec's file list

The spec's list is the core: `NetworkDisclosure.cs`, `SettingsNames.cs`, `SettingsIndex.cs`, `WhatsNew.cs`, `UpdateChecker.cs`, `SettingsStore.cs`, `SettingsWindow.xaml` and `SettingsKeysContractTests.cs`. These extra files deliver its own bullets and the carry.

Core:

- New `UpdateCheck.cs`: the daily rule, pure.
- New `UsageSchedule.cs`: the refresh interval the copy quotes.
- New `AgentConsents.cs`: What Claude can change, pure.
- Changed `StoreUpdate.cs` (decision 6).
- Comment-only markers in `ClaudeApiClient.cs`, `UsagePublisher.cs` and `Startup.cs`.

App:

- `Services/StoreUpdateService.cs`: the switch, and a query seam.
- `ViewModels/WidgetViewModel.cs`: `AttachUpdateChecker`, the tick, `RefreshInterval`.
- New `ViewModels/PrivacyViewModel.cs` and `ViewModels/AboutUpdatesViewModel.cs`.
- `ViewModels/SettingsViewModel.cs`.
- `ViewModels/WhatsNewViewModel.cs` and `Views/WhatsNewWindow.xaml.cs`: the carry.
- New `Views/PrivacyPage.xaml(.cs)`.
- `Views/SettingsWindow.xaml.cs` and `App.xaml.cs`.
- Comment-only markers in:
  - `Desk/DockerPipe.cs`, `Desk/WeatherService.cs`, `Desk/DeskController.cs`, `Desk/DeskReviewBar.xaml.cs`
  - `Interop/LauncherApps.cs`, `Modals/WebView2NotInstalledWindow.xaml.cs`
  - `Services/VaultService.cs`, `Services/WebView2ApiClient.cs`, `Services/WidgetCatalogService.cs`
  - `ViewModels/ThemesViewModel.cs`, `Views/SignInWindow.xaml.cs`

Docs: `docs/PRIVACY.md` (decision 22).

Harness: `tools/Sanduhr.RenderHarness/SettingsPassScenario.cs`, for two focus shots.

Tests:

- New Core tests in `tests/Sanduhr.Tests/`: `UpdateCheckTests.cs`, `SettingsSlice6NamesTests.cs`, `NetworkDisclosureTests.cs`, `AgentConsentsTests.cs`, `SettingsSlice6IndexTests.cs`.
- New App tests in `tests/Sanduhr.App.Tests/`: `UpdateCheckKeyTests.cs`, `UpdateCheckSwitchTests.cs`, `PrivacyViewModelTests.cs`, `PrivacyPageTests.cs`, `AboutUpdatesTests.cs`, `WhatsNewBoxTests.cs`, `Slice6PagesTests.cs`.
- Changed: `StoreUpdateTests.cs`, `SettingsKeysContractTests.cs`, `SettingsNamesTests.cs`, `SettingsModsIndexTests.cs`, `DeskSplitPagesTests.cs`.

Not in this slice, though the spec puts these on the same page:

- Streamer mode and its allowlist (slice 7).
- The 4.0 What's New cards (slice 7; they land on `privacy#streamer`).
- Reset's new files (slice 7).

## Merge surface with slice 7

Slice 7 (streamer mode) follows this slice and builds on the Privacy page. This slice leaves it a clean seat:

| File | Slice 6 leaves | Slice 7 does there | Risk |
| --- | --- | --- | --- |
| `Views/PrivacyPage.xaml` | the comment `<!-- Slice 7: Streamer mode (streamer, streamer-allowlist) goes here, first on the page, with its divider below it. -->` right under the page's first line | replaces the comment with the Streamer mode section, using it verbatim as the Edit anchor | None: one marked seat. |
| `ViewModels/PrivacyViewModel.cs` | no streamer members; `Reload()` reads the consents and the MCP status | adds a streamer sub-view-model property. It must also mask the folder names in the folder-reads row's state, which shows `~\.claude-work`. The spec's reach table doesn't list Privacy, so slice 7 must add it there. | Low: additive. **The folder names on Privacy need masking**; this goes into slice 7's carry (Task 11 Step 6). |
| `Core/SettingsIndex.cs` | Privacy's `network` and `agents` entries after Alerts'; no Privacy `JumpLinks` arm (two sections) | inserts `streamer` and `streamer-allowlist` entries before `network`, and adds a `SettingsPage.Privacy` `JumpLinks` arm when the page reaches four sections | Low: one line per entry. The boosts for `stream`, `demo` and `codename` already name slice 7's anchors. |
| `Core/SettingsNames.cs` | the "slice 6" block; `Sidebar`'s first group ends with Privacy; `TitlesToCome` is empty | adds its own block | None. |
| `tests/Sanduhr.Tests/SettingsKeysContractTests.cs` | `update_check` between `tray_mode` and `vault_prompted` | adds `streamer_mode` between `statusline_enabled` and `theme` | None: different lines. |
| `Core/NetworkDisclosure.cs` | nine rows, none naming a person, a folder or a host of the person's (the endpoint row says "the endpoint URL you set") | nothing to mask | None. |

---

## The outbound-call inventory (base 8dae01fc)

This lists every line in `windows-dotnet/src` that matches a call shape (decision 1).

What the scan does not find:

- `src/Sanduhr.Mcp`: **none**.
- DNS, sockets, `WebClient`, `HttpWebRequest`, `WebRequest.Create`, `ClientWebSocket`, `LaunchUriAsync`: **none**.
- XAML has no WebView2 `Source`. The two `Hyperlink NavigateUri` (`SettingsWindow.xaml:2657-2658`, Weather's credit and licence) both go through `Hyperlink_RequestNavigate` (row 39).
- `tools/` and `tests/` ship nothing and are not scanned.
- `App.xaml.cs:231` (`new UpdateChecker().CheckForUpdatesAsync(`) matches today only because the wrapper shares Velopack's method name. Task 2 renames the wrapper's methods (`RunIfDueAsync`, `CheckNowAsync`), and the line stops matching.

**Sanduhr calls out (24):**

| # | File:line | Call | Goes to | Carries | Row |
| --- | --- | --- | --- | --- | --- |
| 1 | `Sanduhr.App/Services/WebView2ApiClient.cs:391` | `core.Navigate(StartPage)` | `https://claude.ai` in a hidden WebView2 window | the `sessionKey` (and `cf_clearance`) cookies injected at `:495-540` | `claude-usage` |
| 2 | `Sanduhr.App/Services/WebView2ApiClient.cs:575` | `ExecuteScriptAsync(BuildFetchScript(…))` | in-page fetches: `claude.ai/api/organizations` (`:254`), `…/organizations/{id}/usage` (`:198`), `claude.ai/v1/code/routines/run-budget` (`:227`) | the same cookies | `claude-usage` |
| 3 | `Sanduhr.Core/ClaudeApiClient.cs:114` | `base.SendAsync(…)` (the Cloudflare-aware handler) | as row 5 | as row 5 | `claude-usage` |
| 4 | `Sanduhr.Core/ClaudeApiClient.cs:181` | `new HttpClient(new CloudflareAwareHandler(inner), …)` | | | `claude-usage` |
| 5 | `Sanduhr.Core/ClaudeApiClient.cs:211` | `_http.SendAsync(req, ct)` | `claude.ai/api/organizations`, `/usage`, routines (`:241`, `:260`, `:280`) | the `sessionKey` and `cf_clearance` cookies; **not built by the app** (decision 4) | `claude-usage` |
| 6 | `Sanduhr.App/Views/SignInWindow.xaml.cs:108` | `CoreWebView2.Navigate(ClaudeSignIn.LoginUrl)` | `https://claude.ai/login` | what the person types | `claude-sign-in` |
| 7 | `Sanduhr.App/Views/SignInWindow.xaml.cs:163` | `Navigate(uri)` (a popup folded into the main view) | the popup's URL, Google's sign-in | what the person types | `claude-sign-in` |
| 8 | `Sanduhr.App/Views/SignInWindow.xaml.cs:310` | `Navigate(LoginUrl)` (Back) | `claude.ai/login` | | `claude-sign-in` |
| 9 | `Sanduhr.App/Views/SignInWindow.xaml.cs:367` | `Navigate(LoginUrl)` (back from Google) | `claude.ai/login` | | `claude-sign-in` |
| 10 | `Sanduhr.App/Views/SignInWindow.xaml.cs:381` | `Navigate(LoginUrl)` (Try again) | `claude.ai/login` | | `claude-sign-in` |
| 11 | `Sanduhr.App/Desk/WeatherService.cs:20` | `new HttpClient(handler)`, user agent "Sanduhr", no cookies | | | `open-meteo` |
| 12 | `Sanduhr.App/Desk/WeatherService.cs:72` | `GetStringAsync(url)` | `api.open-meteo.com/v1/forecast` (`Weather.cs:70`) | latitude and longitude at 2 decimals, and the unit | `open-meteo` |
| 13 | `Sanduhr.App/Desk/WeatherService.cs:98` | `GetStringAsync(url, ct)` (Look up) | `geocoding-api.open-meteo.com/v1/search` (`Weather.cs:61`) | the typed city, at most 100 characters | `open-meteo` |
| 14 | `Sanduhr.App/Services/WidgetCatalogService.cs:57` | `new HttpClient(handler, …)`, user agent "Sanduhr", no cookies | | | `widget-catalog` |
| 15 | `Sanduhr.App/Services/WidgetCatalogService.cs:76` | `_http.SendAsync(request, …)` | `github.com/626Labs-LLC/sanduhr-widgets/releases/…` (`WidgetCatalog.cs:46`, `:49`); redirects only to `WidgetCatalog.AllowedHosts` (`:70`) | nothing of the person's | `widget-catalog` |
| 16 | `Sanduhr.App/Updates/UpdateChecker.cs:44` | `new GithubSource(GitHubRepoUrl, …)` | | | `github-update` |
| 17 | `Sanduhr.App/Updates/UpdateChecker.cs:45` | `new UpdateManager(source)` | | | `github-update` |
| 18 | `Sanduhr.App/Updates/UpdateChecker.cs:55` | `manager.CheckForUpdatesAsync()` | `api.github.com` (`/releases?per_page=`) | a request for the release list | `github-update` |
| 19 | `Sanduhr.App/Updates/UpdateChecker.cs:59` | `manager.DownloadUpdatesAsync(…)` | GitHub's release files | a request for the files | `github-update` |
| 20 | `Sanduhr.App/Services/StoreUpdateService.cs:62` | `GetAppAndOptionalStorePackageUpdatesAsync()` (every 6 hours) | the Microsoft Store, through Windows | the package Windows names | `store-update` |
| 21 | `Sanduhr.App/Services/StoreUpdateService.cs:133` | the same, in the installer | | | `store-update` |
| 22 | `Sanduhr.App/Services/StoreUpdateService.cs:136` | `RequestDownloadAndInstallStorePackageUpdatesAsync(…)` (after the click) | | | `store-update` |
| 23 | `Sanduhr.Core/UsagePublisher.cs:208` | `new HttpClient(handler ?? new HttpClientHandler(), …)` | | | `publish-usage`, `publish-agent` |
| 24 | `Sanduhr.Core/UsagePublisher.cs:235` | `http.SendAsync(req, ct)` (POST) | the endpoint the person set (`IsUsableEndpoint`: https, or http on loopback) | the record (`BuildPayload`), and the token in the scheme's header | `publish-usage`, `publish-agent` |

Rows 16 to 19 move into `VelopackUpdateProbe`, and row 20 into `StoreUpdateService.AskTheStoreAsync` (both Task 2). The scan pins files, not line numbers.

**Local only (9):**

- `Sanduhr.App/Desk/DockerPipe.cs:60`, `:76` and `:95`: HTTP over the named pipe `docker_engine`.
- `Sanduhr.App/App.xaml.cs:1138`: the relaunch after an update.
- `Sanduhr.App/Interop/LauncherApps.cs:215`: `explorer.exe shell:AppsFolder\…`.
- Explorer on a folder: `Sanduhr.App/Services/VaultService.cs:185`, `Sanduhr.App/ViewModels/SettingsViewModel.cs:274`, `Sanduhr.App/ViewModels/ThemesViewModel.cs:266`.
- `Sanduhr.Core/Startup.cs:155`: `ms-settings:startupapps`.

**Handed to the person's browser (6):**

- `Sanduhr.App/Desk/DeskController.cs:271`: Open-Meteo's credit on the Desk.
- `Sanduhr.App/Desk/DeskReviewBar.xaml.cs:139`: a font's download page.
- `Sanduhr.App/Interop/LauncherApps.cs:219`: a Launcher link.
- `Sanduhr.App/Modals/WebView2NotInstalledWindow.xaml.cs:68`: Microsoft's WebView2 pages.
- `Sanduhr.App/ViewModels/SettingsViewModel.cs:292`: About's project pages, and from Task 7 the privacy policy.
- `Sanduhr.App/Views/SettingsWindow.xaml.cs:55`: Weather's credit and licence links.

**How the browser differs from Sanduhr calling out.** A `your-browser` line hands an `https` address to the shell, which opens it in the person's default browser.

- The browser makes the request, with its own cookies and settings.
- Sanduhr adds nothing to it: no account, no usage, no file.
- Sanduhr has no say in what that browser sends.

That is why these lines get a sentence under the table rather than a row. Every row is a request Sanduhr builds and sends itself, or (the last row) data Sanduhr hands Claude Code to send.

**Through Claude Code (no call site).** The MCP server (`src/Sanduhr.Mcp`) answers Claude Code over stdio and makes no network call. What it answers becomes part of the conversation, which Claude Code sends to its model service. That is row `claude-code`.

## The agent-consent inventory (base 8dae01fc)

`src/Sanduhr.Mcp/ToolCatalog.cs` offers 16 tools. Each is in exactly one row:

| Row (`AgentConsents` id) | Tools | Switch, key | Home | What off or no-switch means (source) |
| --- | --- | --- | --- | --- |
| `watchers`: "Let Claude show watchers" | `watch_start`, `watch_update`, `watch_end` | `watchers_agents`, off by default (`SettingsStore.cs:848`) | `watchers#watchers` | Off: nothing shows (`App.xaml.cs:576`, `WatcherStore`). |
| `desk-direct`: "Let Claude change the Desk directly" | `propose_desk` | `desk_direct`, off (`SettingsStore.cs:872`) | `desk#desk-designs` | Off: every design waits for Apply or Dismiss. On: it applies at once after the current Desk is saved, unless it needs a missing font (`DeskDesignService.cs:340`). |
| `songs-direct`: "Let Claude style songs directly" | `propose_now_playing_looks` | `now_playing.looks_direct`, off (`DeskContentSettings.cs:66`) | `nowplaying#np-looks` | Off: each look waits as a card (`NowPlayingLookService.cs:42`). |
| `folder-reads`: "Let Claude read this folder's project totals" | `get_local_burn_by_project`, `get_model_usage`, `get_usage_history` | `mcp_roots`, per discovered folder, off (`SettingsStore.cs:592`; the server: `McpConfig.cs:208`) | `claudecode#folders` | An unconsented folder is never read (`ToolLogic.cs:440`, `:509`, `:599`). `get_usage`'s local burn is also limited to consented folders (`:288`). |
| `publish`: "Publish your usage when Claude asks" | `publish_usage` | follows `publish.enabled` (`UsagePublishService.cs:165-170`) | `publish#schedule` | Off: refused as `publishing_off`. |
| `theme`: "Give the widget a theme" | `propose_theme` | **no switch**: saved, and applied when asked; `apply` defaults to true (`ThemeHandoff.cs:61`, `ThemeHandoffService.cs:100`) | `themes#gallery` | |
| `messages`: "Suggest Desk messages" | `propose_desk_messages` | **no switch**: always a suggestion (`DeskHandoffSeams.cs:32`, `direct: () => false`) | `deskmessage#suggestion` | |
| `widget-packs`: "Make a widget pack" | `propose_widget` | **no switch**: every pack waits for Apply (`WidgetReviewService.cs:42`) | `mods#mods` | |
| `reads`: "Read your meters and your Desk" | `get_usage`, `get_desk`, `get_desk_messages`, `ping` | **no switch**: readable once the server is installed | `claudecode#folders` | `get_desk` includes monitors, installed fonts, Start-menu app names, and the wallpaper's path, summary and preview (`DeskState.cs:88-142`). |

**MCP install status.** `McpIntegrationInstaller.IsRegistered(ccHomeName)` (`McpIntegrationInstaller.cs:413`) is true when any file Claude Code reads for that home carries a `mcpServers.sanduhr` entry whose command names `sanduhr-mcp` (`IsOurs`, `:466`). The cards call it once per discovered folder (`ClaudeCodeViewModel.cs:575`). Hand-added folders can't hold the server.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Sanduhr.Core/UpdateCheck.cs` (create) | `UpdateCheck.Interval`, `Due`, `ParseStamp`, `FormatStamp`. |
| `src/Sanduhr.Core/UsageSchedule.cs` (create) | `UsageSchedule.Refresh` (5 minutes). |
| `src/Sanduhr.Core/StoreUpdate.cs` (modify) | `ShouldCheck(…, bool enabled = true)`. |
| `src/Sanduhr.Core/SettingsNames.cs` (modify) | Slice 6 copy; Privacy in the sidebar; `TitlesToCome` empty; `PageTitle(Privacy)`; Table rows for Updates. |
| `src/Sanduhr.Core/NetworkDisclosure.cs` (create) | `NetworkCaller`, `NetworkRow`, `NetworkDisclosure.All`, `YourBrowser`, `LocalOnly`, `PrivacyPolicyUrl`. |
| `src/Sanduhr.Core/AgentConsents.cs` (create) | `AgentConsentKind`, `AgentConsent`, `AgentConsents.All`, `State`. |
| `src/Sanduhr.Core/SettingsIndex.cs` (modify) | Privacy and Updates anchors, synonyms, `NameAnchors`, About's `JumpLinks`. |
| `src/Sanduhr.Core/WhatsNew.cs` (modify) | `SettingsPage.Privacy`. |
| `src/Sanduhr.App/Services/SettingsStore.cs` (modify) | `LoadUpdateCheck`, `SaveUpdateCheck`. |
| `src/Sanduhr.App/Updates/UpdateChecker.cs` (rewrite) | `UpdateProbeStatus`, `UpdateProbeResult`, `IUpdateProbe`, `VelopackUpdateProbe`, `UpdateChecker`, `VelopackUpdateInstaller`. |
| `src/Sanduhr.App/Services/StoreUpdateService.cs` (modify) | The switch; the `Query` seam. |
| `src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (modify) | `AttachUpdateChecker`, the tick, `RefreshInterval = UsageSchedule.Refresh`. |
| `src/Sanduhr.App/ViewModels/PrivacyViewModel.cs` (create) | `AgentRow`, `PrivacyViewModel`. |
| `src/Sanduhr.App/ViewModels/AboutUpdatesViewModel.cs` (create) | About, Updates. |
| `src/Sanduhr.App/Views/PrivacyPage.xaml(.cs)` (create) | Settings, Privacy. |
| `src/Sanduhr.App/ViewModels/SettingsViewModel.cs`, `Views/SettingsWindow.xaml(.cs)`, `App.xaml.cs` (modify) | Wiring, the TabItem, About's Updates and jump links. |
| `src/Sanduhr.App/ViewModels/WhatsNewViewModel.cs`, `Views/WhatsNewWindow.xaml.cs` (modify) | The box reads the key again on Activated. |

---

### Task 1: The `update_check` key, and the rules the two update probes and the copy share

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/UpdateCheck.cs`
- Create: `windows-dotnet/src/Sanduhr.Core/UsageSchedule.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/StoreUpdate.cs` (`ShouldCheck` takes `enabled`)
- Modify: `windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs` (`LoadUpdateCheck`, `SaveUpdateCheck`)
- Modify: `windows-dotnet/tests/Sanduhr.Tests/SettingsKeysContractTests.cs` (`update_check` in `Frozen`)
- Modify: `windows-dotnet/tests/Sanduhr.Tests/StoreUpdateTests.cs`
- Test: create `windows-dotnet/tests/Sanduhr.Tests/UpdateCheckTests.cs`, `windows-dotnet/tests/Sanduhr.App.Tests/UpdateCheckKeyTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `public static class UpdateCheck`, with `TimeSpan Interval` (24 h), `bool Due(bool enabled, DateTimeOffset? lastChecked, DateTimeOffset now)`, `DateTimeOffset? ParseStamp(string? text)` and `string FormatStamp(DateTimeOffset at)`.
  - `public static class UsageSchedule`, with `TimeSpan Refresh` (5 min).
  - `StoreUpdate.ShouldCheck(bool isPackaged, DateTimeOffset? lastCheck, DateTimeOffset now, TimeSpan? interval = null, bool enabled = true)`.
  - `SettingsStore.LoadUpdateCheck()`, which is true when absent, and `SettingsStore.SaveUpdateCheck(bool on)`.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/UpdateCheckTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 6: when the GitHub build asks for a newer release ("update_check"), and the intervals Privacy's copy quotes.</summary>
public class UpdateCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Off_is_never_due()
    {
        Assert.False(UpdateCheck.Due(enabled: false, lastChecked: null, now: Now));
        Assert.False(UpdateCheck.Due(enabled: false, lastChecked: Now.AddDays(-30), now: Now));
    }

    [Fact]
    public void On_it_is_due_first_then_once_a_day()
    {
        Assert.Equal(TimeSpan.FromHours(24), UpdateCheck.Interval);
        Assert.True(UpdateCheck.Due(true, null, Now));
        Assert.False(UpdateCheck.Due(true, Now.AddHours(-23), Now));
        Assert.True(UpdateCheck.Due(true, Now - UpdateCheck.Interval, Now));
    }

    [Fact]
    public void A_stamp_from_the_future_or_unreadable_is_due()
    {
        // A clock that moved backwards must not park the check forever (Review Focus 3).
        Assert.True(UpdateCheck.Due(true, Now.AddDays(2), Now));
        Assert.Null(UpdateCheck.ParseStamp("not a time"));
        Assert.Null(UpdateCheck.ParseStamp(null));
        Assert.True(UpdateCheck.Due(true, UpdateCheck.ParseStamp("not a time"), Now));
    }

    [Fact]
    public void A_stamp_round_trips()
    {
        var text = UpdateCheck.FormatStamp(Now);
        Assert.Equal(Now, UpdateCheck.ParseStamp(text));
        // The stamp 3.x builds wrote (DateTimeOffset.UtcNow.ToString("O")) still reads.
        Assert.Equal(Now, UpdateCheck.ParseStamp(Now.ToString("O") + "\r\n"));
    }

    [Fact]
    public void The_usage_refresh_is_five_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), UsageSchedule.Refresh);
    }
}
```

In `windows-dotnet/tests/Sanduhr.Tests/StoreUpdateTests.cs`, add after `Unpackaged_builds_never_ask_the_store`:

```csharp
    [Fact]
    public void With_update_check_off_the_store_is_never_asked()
    {
        // Settings, About, Updates' switch gates both probes (slice 6, decision 6).
        Assert.False(StoreUpdate.ShouldCheck(true, null, Now, enabled: false));
        Assert.False(StoreUpdate.ShouldCheck(true, Now.AddDays(-30), Now, enabled: false));
        Assert.True(StoreUpdate.ShouldCheck(true, null, Now, enabled: true));
    }
```

In `windows-dotnet/tests/Sanduhr.Tests/SettingsKeysContractTests.cs`, in `Frozen`, replace

```csharp
        "tray_mode",
        "vault_prompted",
```

with

```csharp
        "tray_mode",
        "update_check",
        "vault_prompted",
```

Create `windows-dotnet/tests/Sanduhr.App.Tests/UpdateCheckKeyTests.cs`:

```csharp
using System.IO;
using System.Text.Json.Nodes;
using Sanduhr.App.Services;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 6: settings.json "update_check" is on when absent, and both values round-trip.</summary>
[Collection("SettingsWindow")]
public sealed class UpdateCheckKeyTests : AppWindowFixture
{
    [Fact]
    public void Absent_is_on_and_off_round_trips()
    {
        var paths = ScratchPaths();
        var store = new SettingsStore(paths);
        Assert.True(store.LoadUpdateCheck());

        store.SaveUpdateCheck(false);
        Assert.False(new SettingsStore(paths).LoadUpdateCheck());
        Assert.False((bool?)JsonNode.Parse(File.ReadAllText(paths.SettingsFile))!["update_check"]);

        store.SaveUpdateCheck(true);
        Assert.True(new SettingsStore(paths).LoadUpdateCheck());
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~UpdateCheckTests|FullyQualifiedName~StoreUpdateTests|FullyQualifiedName~SettingsKeysContractTests" > %TEMP%\slice6-t1-red.log 2>&1`
Expected: the build FAILS with `The name 'UpdateCheck' does not exist in the current context`.

- [ ] **Step 3: The two Core rules, the switch on the Store's rule, and the key**

Create `windows-dotnet/src/Sanduhr.Core/UpdateCheck.cs`:

```csharp
using System.Globalization;

namespace Sanduhr.Core;

/// <summary>
/// When the GitHub build asks for a newer release (Settings v2, slice 6; WF6): settings.json "update_check", on when absent. Off, nothing asks
/// on its own, neither at launch nor on the widget's tick. "Check now" on About is the person asking, so it asks whatever the switch says: it is
/// not this rule's. <see cref="Interval"/> is what the switch's words promise ("once a day"), and Privacy's "What leaves this PC" quotes it.
/// </summary>
public static class UpdateCheck
{
    /// <summary>How long after the last ask the next one is due.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>Ask now? Never with the switch off; else when nothing was asked yet or <see cref="Interval"/> has passed. A stamp in the
    /// future (a clock moved backwards) counts as due, so it can't park the check forever, as <see cref="StoreUpdate.ShouldCheck"/> does.</summary>
    public static bool Due(bool enabled, DateTimeOffset? lastChecked, DateTimeOffset now)
    {
        if (!enabled)
            return false;
        if (lastChecked is not { } last)
            return true;
        if (last > now)
            return true;
        return now - last >= Interval;
    }

    /// <summary>The stamp file's text as a time, null when it is missing or unreadable (which reads as never asked).</summary>
    public static DateTimeOffset? ParseStamp(string? text) =>
        DateTimeOffset.TryParse(text?.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null;

    /// <summary>The stamp file's text: round-trip, UTC.</summary>
    public static string FormatStamp(DateTimeOffset at) => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
```

Create `windows-dotnet/src/Sanduhr.Core/UsageSchedule.cs`:

```csharp
namespace Sanduhr.Core;

/// <summary>
/// How often the widget asks claude.ai for the account in use (its refresh timer, and the spacing of the per-seat rounds). Settings, Privacy,
/// What leaves this PC quotes it, so the words and the timer can't drift (Settings v2, slice 6).
/// </summary>
public static class UsageSchedule
{
    public static readonly TimeSpan Refresh = TimeSpan.FromMinutes(5);
}
```

In `windows-dotnet/src/Sanduhr.Core/StoreUpdate.cs`, replace

```csharp
    public static bool ShouldCheck(
        bool isPackaged, DateTimeOffset? lastCheck, DateTimeOffset now, TimeSpan? interval = null)
    {
        if (!isPackaged)
            return false;
```

with

```csharp
    public static bool ShouldCheck(
        bool isPackaged, DateTimeOffset? lastCheck, DateTimeOffset now, TimeSpan? interval = null, bool enabled = true)
    {
        // enabled: settings.json "update_check" (Settings, About, Updates). Off, no update probe runs on its own (Settings v2, slice 6).
        if (!isPackaged || !enabled)
            return false;
```

In `windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs`, after the method

```csharp
    public void SaveWhatsNewHideAfterUpdates(bool hide)
    {
        var root = Read();
        root["whats_new_hide_after_updates"] = hide;
        Write(root);
    }
```

add:

```csharp

    /// <summary>Settings, About, Updates' "Check for updates once a day" (settings.json "update_check", Settings v2 slice 6): on when absent.
    /// Off, no update probe runs on its own (<c>UpdateChecker</c>, <c>StoreUpdateService</c>); "Check now" still asks once.</summary>
    public bool LoadUpdateCheck() => ReadBool(Read(), "update_check", true);

    public void SaveUpdateCheck(bool on)
    {
        var root = Read();
        root["update_check"] = on;
        Write(root);
    }
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~UpdateCheckTests|FullyQualifiedName~StoreUpdateTests|FullyQualifiedName~SettingsKeysContractTests" > %TEMP%\slice6-t1-core.log 2>&1`
Then: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~UpdateCheckKeyTests > %TEMP%\slice6-t1-app.log 2>&1`
Expected: PASS. `Frozen_list_is_sorted_and_unique` passes, because `update_check` sorts between `tray_mode` and `vault_prompted`. `Every_frozen_key_is_still_read_or_written` finds `root["update_check"]`.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/UpdateCheck.cs windows-dotnet/src/Sanduhr.Core/UsageSchedule.cs windows-dotnet/src/Sanduhr.Core/StoreUpdate.cs windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs windows-dotnet/tests/Sanduhr.Tests/UpdateCheckTests.cs windows-dotnet/tests/Sanduhr.Tests/StoreUpdateTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsKeysContractTests.cs windows-dotnet/tests/Sanduhr.App.Tests/UpdateCheckKeyTests.cs
git commit -m "feat(settings-v2): update_check, on by default, and the daily rule both update probes honour"
```

(End every commit message in this plan with the trailer line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`, as the standing rules say.)

---

### Task 2: `UpdateChecker` honours the switch everywhere, and offers Check now; the Store probe honours it too

**Files:**
- Rewrite: `windows-dotnet/src/Sanduhr.App/Updates/UpdateChecker.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Services/StoreUpdateService.cs` (`enabled`, `Query`, markers)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (`RefreshInterval`, `AttachUpdateChecker`, the tick)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (the checker is built once, with the switch, and runs at launch and on the tick)
- Modify: `docs/PRIVACY.md` (the update switch)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/UpdateCheckSwitchTests.cs`

**Interfaces:**
- Consumes (from Task 1): `UpdateCheck.Due`, `ParseStamp`, `FormatStamp`; `StoreUpdate.ShouldCheck(…, enabled:)`; `SettingsStore.LoadUpdateCheck`; `UsageSchedule.Refresh`.
- Produces:
  - `public enum UpdateProbeStatus { UpToDate, Ready, NotInstalled, Failed }`.
  - `public sealed record UpdateProbeResult(UpdateProbeStatus Status, string? Version = null, IUpdateInstaller? Installer = null)`.
  - `public interface IUpdateProbe { Task<UpdateProbeResult> FindAsync(); }`.
  - `public sealed class VelopackUpdateProbe : IUpdateProbe`.
  - `public sealed class UpdateChecker`. Its constructor is `(Func<bool> enabled, IUpdateProbe probe, string stampPath, Action<string, IUpdateInstaller> onReady, Func<DateTimeOffset>? now = null)`. Its members are `Task RunIfDueAsync()`, `void Tick()`, `Task<UpdateProbeResult> CheckNowAsync()`, `internal Task<UpdateProbeResult>? Running` and `static string DefaultStampPath`.
  - `StoreUpdateService`'s constructor gains `Func<bool>? enabled = null`, and the service gains `internal Func<Task<string?>> Query { get; init; }`.
  - `WidgetViewModel.AttachUpdateChecker(UpdateChecker)`.
  - App's `_updateChecker` field. Task 8 reads it for Check now.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/UpdateCheckSwitchTests.cs`:

```csharp
using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.Updates;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 6: "Check for updates once a day" (settings.json "update_check") gates every update probe. Off, nothing asks: not at
/// launch, not on the widget's tick, not on the Store build's timer. Check now asks once whatever the switch says. The probe is the only code
/// that asks GitHub (NetworkDisclosureTests pins Velopack's four calls to its file, and the last test here pins who builds it), so a counting
/// probe that counts zero is zero HTTP.
/// </summary>
[Collection("SettingsWindow")]
public sealed class UpdateCheckSwitchTests : AppWindowFixture
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private sealed class Clock
    {
        public DateTimeOffset Now = T0;
    }

    private sealed class CountingProbe(UpdateProbeResult answer) : IUpdateProbe
    {
        public int Asked;
        public TaskCompletionSource? Gate;

        public async Task<UpdateProbeResult> FindAsync()
        {
            Interlocked.Increment(ref Asked);
            if (Gate is { } gate)
                await gate.Task.ConfigureAwait(false);
            return answer;
        }
    }

    private sealed class NoInstall : IUpdateInstaller
    {
        public string Channel => "test";
        public Task<UpdateInstallResult> InstallAsync() => Task.FromResult(UpdateInstallResult.Installing);
    }

    private string StampPath => Path.Combine(Root, "local", "last-update-check.txt");

    private (UpdateChecker Checker, CountingProbe Probe, Clock Clock, SettingsStore Store, List<string> Offered) Make(bool on,
        UpdateProbeResult? answer = null)
    {
        var store = new SettingsStore(ScratchPaths());
        store.SaveUpdateCheck(on);
        var probe = new CountingProbe(answer ?? new UpdateProbeResult(UpdateProbeStatus.UpToDate));
        var clock = new Clock();
        var offered = new List<string>();
        var checker = new UpdateChecker(store.LoadUpdateCheck, probe, StampPath, (v, _) => { lock (offered) offered.Add(v); }, () => clock.Now);
        return (checker, probe, clock, store, offered);
    }

    private static Task Settled(UpdateChecker checker) => checker.Running ?? Task.CompletedTask;

    [Fact]
    public async Task With_the_switch_off_nothing_asks_at_launch_or_on_the_tick()
    {
        var (checker, probe, clock, _, _) = Make(on: false);

        await checker.RunIfDueAsync();
        clock.Now = T0.AddDays(3);
        checker.Tick();
        await Settled(checker);

        Assert.Equal(0, probe.Asked);
        Assert.False(File.Exists(StampPath));
    }

    [Fact]
    public async Task With_the_switch_on_it_asks_at_launch_then_once_a_day_on_the_tick()
    {
        var (checker, probe, clock, _, _) = Make(on: true);

        await checker.RunIfDueAsync();
        Assert.Equal(1, probe.Asked);
        Assert.Equal(T0, UpdateCheck.ParseStamp(File.ReadAllText(StampPath)));

        clock.Now = T0.AddHours(23);
        checker.Tick();
        await Settled(checker);
        Assert.Equal(1, probe.Asked);

        clock.Now = T0.AddHours(25);
        checker.Tick();
        await Settled(checker);
        Assert.Equal(2, probe.Asked);
    }

    [Fact]
    public async Task Turning_it_off_stops_the_next_tick()
    {
        var (checker, probe, clock, store, _) = Make(on: true);
        await checker.RunIfDueAsync();
        var stamped = File.GetLastWriteTimeUtc(StampPath);

        store.SaveUpdateCheck(false);
        clock.Now = T0.AddDays(2);
        checker.Tick();
        await Settled(checker);

        Assert.Equal(1, probe.Asked);
        Assert.Equal(stamped, File.GetLastWriteTimeUtc(StampPath));
    }

    [Fact]
    public async Task Check_now_asks_once_with_the_switch_off_and_offers_what_it_found()
    {
        var (checker, probe, _, _, offered) = Make(on: false, new UpdateProbeResult(UpdateProbeStatus.Ready, "4.0.1", new NoInstall()));

        var result = await checker.CheckNowAsync();

        Assert.Equal(1, probe.Asked);
        Assert.Equal(UpdateProbeStatus.Ready, result.Status);
        Assert.Equal(["4.0.1"], offered);
    }

    [Fact]
    public async Task Check_now_shares_a_probe_already_running()
    {
        var (checker, probe, _, _, offered) = Make(on: true, new UpdateProbeResult(UpdateProbeStatus.Ready, "4.0.1", new NoInstall()));
        probe.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var launch = checker.RunIfDueAsync();
        var now = checker.CheckNowAsync();
        probe.Gate.SetResult();
        await launch;
        var result = await now;

        Assert.Equal(1, probe.Asked);
        Assert.Equal(UpdateProbeStatus.Ready, result.Status);
        Assert.Equal(["4.0.1"], offered);
    }

    [Fact]
    public async Task An_unreadable_stamp_still_lets_it_ask()
    {
        var (checker, probe, _, _, _) = Make(on: true);
        Directory.CreateDirectory(Path.GetDirectoryName(StampPath)!);
        File.WriteAllText(StampPath, "garbage");

        await checker.RunIfDueAsync();

        Assert.Equal(1, probe.Asked);
    }

    [Fact]
    public void The_Store_build_asks_the_Store_nothing_with_the_switch_off()
    {
        int asked = 0;
        // The offers are never reached with the switch off; nothing here may touch the real Store.
        var service = new StoreUpdateService(null!, () => 0, isPackaged: () => true, enabled: () => false)
        {
            Query = () => { asked++; return Task.FromResult<string?>(null); },
        };

        service.Tick(T0);
        service.Tick(T0.AddDays(1));

        Assert.Equal(0, asked);
    }

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Sanduhr.App");
    }

    /// <summary>"Not anywhere else": one Velopack probe is built, in App, straight into the one checker; only the checker asks it; App wires the
    /// switch into the Store service too.</summary>
    [Fact]
    public void Nothing_but_the_checker_asks_the_probe()
    {
        var files = Directory.EnumerateFiles(AppDir(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToDictionary(f => Path.GetFileName(f), File.ReadAllText);

        var builders = files.Where(f => f.Value.Contains("new VelopackUpdateProbe(", StringComparison.Ordinal)).Select(f => f.Key).ToList();
        Assert.Equal(["App.xaml.cs"], builders);
        var app = files["App.xaml.cs"];
        var line = app.Split('\n').Single(l => l.Contains("new VelopackUpdateProbe(", StringComparison.Ordinal));
        Assert.Contains("new UpdateChecker(", line);

        var askers = files.Where(f => f.Value.Contains(".FindAsync(", StringComparison.Ordinal)).Select(f => f.Key).ToList();
        Assert.Equal(["UpdateChecker.cs"], askers);
        Assert.Contains("enabled: updateSwitch.LoadUpdateCheck", app);
        Assert.DoesNotContain("CheckForUpdatesAsync(", app);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~UpdateCheckSwitchTests > %TEMP%\slice6-t2-red.log 2>&1`
Expected: the build FAILS with `The type or namespace name 'IUpdateProbe' could not be found`.

- [ ] **Step 3: The checker, the probe, the Store service and the wiring**

In `windows-dotnet/src/Sanduhr.App/Updates/UpdateChecker.cs`, the five `using` lines, the namespace and `VelopackUpdateInstaller` at the bottom stay as they are. Read the file. Then make one Edit: `old_string` is lines 9 to 106 copied exactly from the Read, running from `/// <summary>` / `/// Velopack-backed update probe for the GitHub release channel` through the closing `}` of `public sealed class UpdateChecker`, just before the `/// <summary>` that opens `/// The Velopack channel's install`. `new_string` is this block:

```csharp
/// <summary>What one ask of the release feed found.</summary>
public enum UpdateProbeStatus
{
    /// <summary>No newer release.</summary>
    UpToDate,
    /// <summary>A newer release is downloaded and waits for Restart to update.</summary>
    Ready,
    /// <summary>This copy wasn't installed by Setup (a dev build, or the Store's MSIX), so it can't update itself; nothing was asked.</summary>
    NotInstalled,
    /// <summary>The ask failed (offline, GitHub refused, a download broke).</summary>
    Failed,
}

/// <summary>One ask's answer. <see cref="Version"/> and <see cref="Installer"/> are set when <see cref="Status"/> is Ready.</summary>
public sealed record UpdateProbeResult(UpdateProbeStatus Status, string? Version = null, IUpdateInstaller? Installer = null);

/// <summary>
/// The one thing that asks the network whether Sanduhr has a newer release (Settings v2, slice 6). <see cref="UpdateChecker"/> is its only
/// caller, so the switch that gates the checker gates every probe. Tests put a counting probe here.
/// </summary>
public interface IUpdateProbe
{
    Task<UpdateProbeResult> FindAsync();
}

/// <summary>
/// The GitHub release feed through Velopack, for the Setup.exe / delta install (not the Store MSIX, which the Store updates). A newer release
/// is downloaded in the background and handed back for Restart to update (<see cref="Services.RestartToUpdateService"/>): nothing installs until
/// the person clicks. Never throws.
/// </summary>
public sealed class VelopackUpdateProbe : IUpdateProbe
{
    private const string GitHubRepoUrl = "https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude";

    public async Task<UpdateProbeResult> FindAsync()
    {
        try
        {
            // network: github-update
            var source = new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: false);
            // network: github-update
            var manager = new UpdateManager(source);
            // IsInstalled is false during dev (bin\Debug) and for the Store/MSIX install: no network call in either.
            if (!manager.IsInstalled)
                return new UpdateProbeResult(UpdateProbeStatus.NotInstalled);
            // network: github-update
            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
                return new UpdateProbeResult(UpdateProbeStatus.UpToDate);
            Debug.WriteLine($"[Sanduhr] Update available: {update.TargetFullRelease.Version}");
            // network: github-update
            await manager.DownloadUpdatesAsync(update, null, CancellationToken.None).ConfigureAwait(false);
            var v = update.TargetFullRelease.Version;
            return new UpdateProbeResult(UpdateProbeStatus.Ready, RestartToUpdate.DisplayVersion(v.Major, v.Minor, v.Patch),
                new VelopackUpdateInstaller(manager, update.TargetFullRelease));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Sanduhr] Update check threw (treating as failed): {ex.Message}");
            return new UpdateProbeResult(UpdateProbeStatus.Failed);
        }
    }
}

/// <summary>
/// When the GitHub build asks for a newer release (Settings v2, slice 6; WF6). Three ways in, one probe:
/// <list type="bullet">
/// <item><description><see cref="RunIfDueAsync"/>: launch. It asks only when "Check for updates once a day" (settings.json "update_check") is
/// on and <see cref="UpdateCheck.Interval"/> has passed since the stamp.</description></item>
/// <item><description><see cref="Tick"/>: the widget's 30-second tick, the same rule, so a widget left running still checks once a day.</description></item>
/// <item><description><see cref="CheckNowAsync"/>: About's "Check now". The person asked, so it asks whatever the switch says.</description></item>
/// </list>
/// With the switch off, the first two ask nothing and write nothing. A probe already running is shared, never doubled. A ready release goes to
/// <c>onReady</c> (on a pool thread: marshal before touching UI). Never throws; never blocks the UI.
/// </summary>
public sealed class UpdateChecker
{
    /// <summary>The stamp the 3.x builds wrote, kept so an upgrade doesn't ask again at once.</summary>
    public static readonly string DefaultStampPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sanduhr", "last-update-check.txt");

    private readonly Func<bool> _enabled;
    private readonly IUpdateProbe _probe;
    private readonly string _stampPath;
    private readonly Action<string, IUpdateInstaller> _onReady;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private Task<UpdateProbeResult>? _inFlight;
    private DateTimeOffset? _last;
    private bool _lastRead;

    public UpdateChecker(Func<bool> enabled, IUpdateProbe probe, string stampPath, Action<string, IUpdateInstaller> onReady,
        Func<DateTimeOffset>? now = null)
    {
        _enabled = enabled;
        _probe = probe;
        _stampPath = stampPath;
        _onReady = onReady;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The probe running now, or the last one; null before the first. Tests await it.</summary>
    internal Task<UpdateProbeResult>? Running => _inFlight;

    /// <summary>Launch: one probe when the switch is on and a day has passed since the last; nothing at all when it is off.</summary>
    public async Task RunIfDueAsync()
    {
        if (!UpdateCheck.Due(_enabled(), LastChecked(), _now()))
            return;
        await Probe().ConfigureAwait(false);
    }

    /// <summary>The widget's tick (UI thread): free until a day has passed (the stamp is held in memory), then the launch rule, switch included.</summary>
    public void Tick()
    {
        if (_inFlight is { IsCompleted: false } || !UpdateCheck.Due(true, LastChecked(), _now()))
            return;
        _ = RunIfDueAsync();
    }

    /// <summary>About, Updates, "Check now": the person asked, so it asks even with the switch off (decision 7). Shares a probe already running.</summary>
    public Task<UpdateProbeResult> CheckNowAsync() => Probe();

    private Task<UpdateProbeResult> Probe()
    {
        lock (_gate)
        {
            if (_inFlight is { IsCompleted: false } running)
                return running;
            return _inFlight = ProbeCore();
        }
    }

    private async Task<UpdateProbeResult> ProbeCore()
    {
        UpdateProbeResult result;
        try
        {
            result = await _probe.FindAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            result = new UpdateProbeResult(UpdateProbeStatus.Failed);
        }
        Stamp(_now());
        if (result is { Status: UpdateProbeStatus.Ready, Version: { } version, Installer: { } installer })
            _onReady(version, installer);
        return result;
    }

    private DateTimeOffset? LastChecked()
    {
        if (!_lastRead)
        {
            try { _last = File.Exists(_stampPath) ? UpdateCheck.ParseStamp(File.ReadAllText(_stampPath)) : null; }
            catch (Exception) { _last = null; }
            _lastRead = true;
        }
        return _last;
    }

    private void Stamp(DateTimeOffset at)
    {
        _last = at;
        _lastRead = true;
        try
        {
            var dir = Path.GetDirectoryName(_stampPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_stampPath, UpdateCheck.FormatStamp(at));
        }
        catch (Exception)
        {
            // Best-effort; a failed stamp means the next launch asks again (the in-memory stamp still holds the tick off).
        }
    }
}

```

In `windows-dotnet/src/Sanduhr.App/Services/StoreUpdateService.cs`:
- Replace `    private readonly Func<bool> _isPackaged;` with the lines below. That line is unique: `StoreUpdateInstaller` also declares `_ownerWindow` and `_log`, so anchor on `_isPackaged`, never on `_log` (it occurs twice).

```csharp
    private readonly Func<bool> _isPackaged;
    private readonly Func<bool> _enabled;
```

- Replace the constructor's signature and its body:

```csharp
    public StoreUpdateService(
        RestartToUpdateService offers, Func<nint> ownerWindow, Func<bool>? isPackaged = null, Action<string>? log = null)
    {
        _offers = offers;
        _ownerWindow = ownerWindow;
        _isPackaged = isPackaged ?? (() => Startup.IsPackaged());
        _log = log;
    }
```

with:

```csharp
    /// <param name="enabled">Settings, About, Updates' switch (settings.json "update_check"). Off, the Store is never asked (Settings v2, slice 6).</param>
    public StoreUpdateService(
        RestartToUpdateService offers, Func<nint> ownerWindow, Func<bool>? isPackaged = null, Action<string>? log = null, Func<bool>? enabled = null)
    {
        _offers = offers;
        _ownerWindow = ownerWindow;
        _isPackaged = isPackaged ?? (() => Startup.IsPackaged());
        _log = log;
        _enabled = enabled ?? (() => true);
    }

    /// <summary>The Store's answer: the waiting update's version, or null. A seam for tests, which never ask the real Store.</summary>
    internal Func<Task<string?>> Query { get; init; } = AskTheStoreAsync;

    private static async Task<string?> AskTheStoreAsync()
    {
        var context = StoreContext.GetDefault();
        // network: store-update
        var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
        return updates.Count > 0 ? VersionOf(updates[0]) : null;
    }
```

- In `Tick`, replace `        if (_checking || !StoreUpdate.ShouldCheck(_isPackaged(), _lastCheck, now))` with `        if (_checking || !StoreUpdate.ShouldCheck(_isPackaged(), _lastCheck, now, enabled: _enabled()))`.
- In `CheckAsync`, replace

```csharp
            var context = StoreContext.GetDefault();
            var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
            if (updates.Count > 0)
                version = VersionOf(updates[0]);
```

with

```csharp
            version = await Query();
```

- In `StoreUpdateInstaller.InstallAsync`, replace

```csharp
        var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
        if (updates.Count == 0)
            return UpdateInstallResult.NotAvailable;
        var result = await context.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
```

with

```csharp
        // network: store-update
        var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
        if (updates.Count == 0)
            return UpdateInstallResult.NotAvailable;
        // network: store-update
        var result = await context.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs`:
- Replace `    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);` with `    private static readonly TimeSpan RefreshInterval = UsageSchedule.Refresh;   // Privacy, What leaves this PC quotes it`.
- After `    public void AttachStoreUpdateService(StoreUpdateService service) => _storeUpdate = service;`, add:

```csharp

    private Sanduhr.App.Updates.UpdateChecker? _updateChecker;

    /// <summary>The GitHub update check (Settings, About, Updates). Attached by App; rides the 30-second tick, asks at most once a day and
    /// never while "update_check" is off. Null in unit contexts.</summary>
    public void AttachUpdateChecker(Sanduhr.App.Updates.UpdateChecker checker) => _updateChecker = checker;
```

- Replace `        _storeUpdate?.Tick(DateTimeOffset.Now);` with:

```csharp
        _storeUpdate?.Tick(DateTimeOffset.Now);
        // The GitHub build's daily check, so a widget left running for days still asks once a day (slice 6, decision 8).
        _updateChecker?.Tick();
```

In `windows-dotnet/src/Sanduhr.App/App.xaml.cs`:
- Replace `    private RestartToUpdateService? _restartToUpdate;` with:

```csharp
    private RestartToUpdateService? _restartToUpdate;
    /// <summary>The GitHub update check (Settings v2, slice 6): launch, the widget's tick and About's Check now share it.</summary>
    private UpdateChecker? _updateChecker;
```

- Replace `        _vm.AttachStoreUpdateService(new StoreUpdateService(_restartToUpdate, OwnerWindowHandle, log: LogUpdate));` with:

```csharp
        // Settings, About, Updates' "Check for updates once a day" (settings.json "update_check") gates both update probes: off, neither the
        // Store's question nor GitHub's runs on its own. About's Check now asks GitHub through the same checker.
        var updateSwitch = new SettingsStore(new Sanduhr.Core.Paths());
        _vm.AttachStoreUpdateService(new StoreUpdateService(_restartToUpdate, OwnerWindowHandle, log: LogUpdate, enabled: updateSwitch.LoadUpdateCheck));
        _updateChecker = new UpdateChecker(updateSwitch.LoadUpdateCheck, new VelopackUpdateProbe(), UpdateChecker.DefaultStampPath,
            (version, installer) => Dispatcher.BeginInvoke(() => _restartToUpdate?.Offer(version, installer)));
        _vm.AttachUpdateChecker(_updateChecker);
```

- Replace

```csharp
        // Best-effort GitHub/Velopack update probe — fire-and-forget so a slow or offline
        // network never delays the first paint. No-ops on the Store/MSIX install and in dev.
        // A downloaded release joins the same Restart to update offer as the Store's.
        _ = new UpdateChecker().CheckForUpdatesAsync((version, installer) =>
            Dispatcher.BeginInvoke(() => _restartToUpdate?.Offer(version, installer)));
```

with

```csharp
        // The GitHub update probe at launch, fire-and-forget so a slow or offline network never delays the first paint. It asks only while
        // "Check for updates once a day" is on and a day has passed; it no-ops on the Store/MSIX install and in dev. A downloaded release
        // joins the same Restart to update offer as the Store's.
        _ = _updateChecker?.RunIfDueAsync();
```

In `docs/PRIVACY.md`, replace

```
**Updates on the GitHub (.exe) build** (Windows; not the Store build, which the Microsoft Store updates): at most once a day Sanduhr asks this project's GitHub releases whether a newer version is out and, from 3.8, downloads it in the background. Nothing installs until you click **Restart to update**.
```

with

```
**Updates on the GitHub (.exe) build** (Windows; not the Store build, which the Microsoft Store updates): at most once a day, when it starts or while it runs, Sanduhr asks this project's GitHub releases whether a newer version is out and, from 3.8, downloads it in the background. Nothing installs until you click **Restart to update**. From 4.0, Settings ▸ About ▸ Updates has the switch: with "Check for updates once a day" off, Sanduhr asks only when you press **Check now**.
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~UpdateCheckSwitchTests|FullyQualifiedName~StoreUpdateInstallerTests" > %TEMP%\slice6-t2-app.log 2>&1`
Then: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~StoreUpdateTests > %TEMP%\slice6-t2-core.log 2>&1`
Expected: PASS. `StoreUpdateInstallerTests` still passes: `Map` is unchanged.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Updates/UpdateChecker.cs windows-dotnet/src/Sanduhr.App/Services/StoreUpdateService.cs windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs windows-dotnet/src/Sanduhr.App/App.xaml.cs docs/PRIVACY.md windows-dotnet/tests/Sanduhr.App.Tests/UpdateCheckSwitchTests.cs
git commit -m "feat(settings-v2): with Check for updates once a day off, no update probe runs; Check now asks once"
```

---

### Task 3: The slice's names

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (the slice 6 block)
- Test: create `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice6NamesTests.cs`

**Interfaces:**
- Consumes (from Task 1): `UsageSchedule.Refresh`, `UpdateCheck.Interval`, `StoreUpdate.CheckInterval`. Also the existing `WeatherSchedule.Refresh`, `AccountFollow.CheckInterval`, `FollowAccount`, `LinkTitle`.
- Produces every string the later tasks bind:
  - Privacy: `PrivacyLine`, `WhatLeavesThisPc`, `WhatClaudeCanChange`, `NetworkWhenLabel`, `NetworkCarriesLabel`, `NetworkOffLabel`.
  - The rows: `Net*` (nine rows × Destination, When, Carries, Off), plus `NothingElseLeaves`, `NetWebView2Note`, `NetYourBrowserNote`, `ReadPrivacyPolicy`.
  - The agents: `AgentsIntro`, `McpInstalledStatus(int, int)`, `McpStatusLinkName`, `AgentOn`, `AgentOff`, `AgentAsksFirst`, `AgentNoSwitch`, `AgentFollowsPublish(bool)`, `AgentFolders(IReadOnlyList<(string Folder, bool On)>)`, and the `Agent*` names and captions.
  - The page foot: `PrivacyWhereLine`, `PrivacyWhereLinkName`.
  - About, Updates: `UpdatesSection`, `CheckForUpdatesDaily`, `UpdatesCaption`, `CheckNow`, `CheckNowCaption`, `UpdateChecking`, `UpdateUpToDate`, `UpdateReady(string)`, `UpdateNotInstalled`, `UpdateFailed`, `StoreKeepsUpToDate`.

- [ ] **Step 1: Write the failing test**

Create `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice6NamesTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 6: Privacy's and About, Updates' copy. The sentences privacy rests on are pinned word for word, and every
/// number comes from the code that acts on it.</summary>
public class SettingsSlice6NamesTests
{
    [Fact]
    public void The_spec_words_are_the_specs()
    {
        Assert.Equal("Privacy", SettingsNames.PrivacyTitle);
        Assert.Equal("What leaves this PC", SettingsNames.WhatLeavesThisPc);
        Assert.Equal("What Claude can change", SettingsNames.WhatClaudeCanChange);
        Assert.Equal("Check for updates once a day", SettingsNames.CheckForUpdatesDaily);
        Assert.Equal("Check now", SettingsNames.CheckNow);
        Assert.Equal("The Microsoft Store keeps Sanduhr up to date.", SettingsNames.StoreKeepsUpToDate);
        Assert.StartsWith("Claude means any agent that uses Sanduhr's MCP server.", SettingsNames.AgentsIntro);
    }

    [Fact]
    public void Nothing_else_is_qualified_by_from_Sanduhr()
    {
        // Decision 3: WebView2 and the person's browser can reach the network on their own, so the sentence says whose calls it counts.
        Assert.Equal("Nothing else leaves this PC from Sanduhr. Sanduhr has no analytics.", SettingsNames.NothingElseLeaves);
        Assert.Contains("SmartScreen", SettingsNames.NetWebView2Note);
        Assert.Contains("your own browser", SettingsNames.NetYourBrowserNote);
    }

    [Fact]
    public void The_publish_row_says_project_names_leave()
    {
        Assert.Contains("so the project names in the folders you tick do leave this PC", SettingsNames.NetPublishCarries);
        Assert.Contains("this PC's name", SettingsNames.NetPublishCarries);
    }

    [Fact]
    public void Every_number_comes_from_the_code_that_acts_on_it()
    {
        Assert.Contains($"Every {(int)UsageSchedule.Refresh.TotalMinutes} minutes", SettingsNames.NetClaudeUsageWhen);
        Assert.Contains($"every {(int)AccountFollow.CheckInterval.TotalMinutes} minutes", SettingsNames.NetClaudeUsageWhen);
        Assert.Contains($"every {(int)WeatherSchedule.Refresh.TotalMinutes} minutes", SettingsNames.NetWeatherWhen);
        Assert.Contains($"every {(int)UpdateCheck.Interval.TotalHours} hours", SettingsNames.NetUpdatesWhen);
        Assert.Contains($"every {(int)UpdateCheck.Interval.TotalHours} hours", SettingsNames.UpdatesCaption);
        Assert.Contains($"every {(int)StoreUpdate.CheckInterval.TotalHours} hours", SettingsNames.NetStoreWhen);
    }

    [Fact]
    public void The_MCP_status_counts_folders_in_words()
    {
        Assert.Equal("Sanduhr found no Claude Code folder, so its MCP server isn't installed in one.", SettingsNames.McpInstalledStatus(0, 0));
        Assert.Equal("Sanduhr's MCP server isn't installed in any Claude Code folder.", SettingsNames.McpInstalledStatus(0, 2));
        Assert.Equal("Sanduhr's MCP server is installed in 1 of 2 Claude Code folders.", SettingsNames.McpInstalledStatus(1, 2));
        Assert.Equal("Sanduhr's MCP server is installed in your Claude Code folder.", SettingsNames.McpInstalledStatus(1, 1));
        Assert.Equal("Sanduhr's MCP server is installed in all 3 Claude Code folders.", SettingsNames.McpInstalledStatus(3, 3));
    }

    [Fact]
    public void Consent_states_read_in_words()
    {
        Assert.Equal(("On", "Off", "Asks you first", "No switch"),
            (SettingsNames.AgentOn, SettingsNames.AgentOff, SettingsNames.AgentAsksFirst, SettingsNames.AgentNoSwitch));
        Assert.Equal("On, with Publish daily at", SettingsNames.AgentFollowsPublish(true));
        Assert.Equal("Off, with Publish daily at", SettingsNames.AgentFollowsPublish(false));
        Assert.Equal("No Claude Code folder found.", SettingsNames.AgentFolders([]));
        Assert.Equal(@"On for ~\.claude. Off for ~\.claude-work and ~\.claude-x.",
            SettingsNames.AgentFolders([(@"~\.claude", true), (@"~\.claude-work", false), (@"~\.claude-x", false)]));
        Assert.Equal(@"Off for ~\.claude.", SettingsNames.AgentFolders([(@"~\.claude", false)]));
    }

    [Fact]
    public void Check_now_says_what_it_found()
    {
        Assert.Equal("Sanduhr 4.0.1 is ready. Restart to update is in the tray menu and on the widget.", SettingsNames.UpdateReady("4.0.1"));
        Assert.Equal("Check now asks once, even with the switch off.", SettingsNames.CheckNowCaption);
    }

    [Fact]
    public void Slice_6_copy_uses_no_retired_name_and_no_bad_path()
    {
        foreach (var text in new[]
        {
            SettingsNames.PrivacyLine, SettingsNames.NetClaudeUsageWhen, SettingsNames.NetClaudeUsageCarries, SettingsNames.NetClaudeUsageOff,
            SettingsNames.NetSignInWhen, SettingsNames.NetSignInCarries, SettingsNames.NetSignInOff, SettingsNames.NetWeatherWhen,
            SettingsNames.NetWeatherCarries, SettingsNames.NetWeatherOff, SettingsNames.NetCatalogWhen, SettingsNames.NetCatalogCarries,
            SettingsNames.NetCatalogOff, SettingsNames.NetUpdatesWhen, SettingsNames.NetUpdatesCarries, SettingsNames.NetUpdatesOff,
            SettingsNames.NetStoreWhen, SettingsNames.NetStoreCarries, SettingsNames.NetStoreOff, SettingsNames.NetPublishWhen,
            SettingsNames.NetPublishCarries, SettingsNames.NetPublishOff, SettingsNames.NetPublishAgentWhen, SettingsNames.NetPublishAgentCarries,
            SettingsNames.NetPublishAgentOff, SettingsNames.NetThroughClaudeWhen, SettingsNames.NetThroughClaudeCarries,
            SettingsNames.NetThroughClaudeOff, SettingsNames.NothingElseLeaves, SettingsNames.NetWebView2Note, SettingsNames.NetYourBrowserNote,
            SettingsNames.AgentsIntro, SettingsNames.AgentWatchersCaption, SettingsNames.AgentDeskDirectCaption, SettingsNames.AgentSongsDirectCaption,
            SettingsNames.AgentFolderReadsCaption, SettingsNames.AgentPublishCaption, SettingsNames.AgentThemeCaption, SettingsNames.AgentMessagesCaption,
            SettingsNames.AgentWidgetPacksCaption, SettingsNames.AgentReadsCaption, SettingsNames.PrivacyWhereLine, SettingsNames.UpdatesCaption,
            SettingsNames.CheckNowCaption, SettingsNames.UpdateChecking, SettingsNames.UpdateUpToDate, SettingsNames.UpdateNotInstalled,
            SettingsNames.UpdateFailed, SettingsNames.StoreKeepsUpToDate, SettingsNames.McpInstalledStatus(1, 2),
        })
        {
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.Empty(SettingsNames.RetiredNames(text));
            Assert.Empty(SettingsNames.PathProblems(text));
            Assert.Empty(SettingsNames.UnknownLinks(text));
        }
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsSlice6NamesTests > %TEMP%\slice6-t3-red.log 2>&1`
Expected: the build FAILS with `'SettingsNames' does not contain a definition for 'WhatLeavesThisPc'`.

- [ ] **Step 3: The copy**

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, after `    public const string SampleFailed = "The sample couldn't be built.";`, add:

```csharp

    // ---- Settings v2, slice 6: Privacy (WF6, WF10) and About, Updates. Every row says what the code does (decisions 2, 3, 9). ----

    /// <summary>Privacy's first line.</summary>
    public const string PrivacyLine = "What Sanduhr sends off this PC, and what Claude can change on it.";
    /// <summary>Privacy's two sections (spec, Privacy: network and agents).</summary>
    public const string WhatLeavesThisPc = "What leaves this PC";
    public const string WhatClaudeCanChange = "What Claude can change";
    /// <summary>What leaves this PC: each row's three lines start with these.</summary>
    public const string NetworkWhenLabel = "When:";
    public const string NetworkCarriesLabel = "Carries:";
    public const string NetworkOffLabel = "Off:";

    // What leaves this PC, one row each (NetworkDisclosure.All, in its order).
    public const string NetClaudeUsage = "claude.ai, your usage";
    public static string NetClaudeUsageWhen =>
        $"Every {(int)UsageSchedule.Refresh.TotalMinutes} minutes for the account in use, and when you refresh or switch accounts. "
        + $"With {FollowAccount} on, every {(int)AccountFollow.CheckInterval.TotalMinutes} minutes for each other signed-in account. "
        + $"Another signed-in account linked to a Claude Code folder you are using is read up to every {(int)UsageSchedule.Refresh.TotalMinutes} minutes too.";
    public const string NetClaudeUsageCarries =
        "The account's claude.ai sign-in cookie (sessionKey, and cf_clearance if you pasted one). In a hidden window, Sanduhr opens claude.ai "
        + "and asks it for the account's organizations, usage and routine budget.";
    public static string NetClaudeUsageOff => $"Remove the account. For another account, also turn off {FollowAccount} and unlink its Claude Code folder.";

    public const string NetSignIn = "claude.ai, sign-in window";
    public const string NetSignInWhen = "Only while the sign-in window is open, which happens when you choose Add account… or Update sign-in…";
    public const string NetSignInCarries =
        "What you type into claude.ai's sign-in page. The window is a browser you drive: claude.ai's pages and, if you choose Continue with Google, Google's.";
    public const string NetSignInOff = "It opens only when you ask for it.";

    public const string NetWeather = "Open-Meteo, weather";
    public static string NetWeatherWhen =>
        "Look up asks once for the city you typed. The forecast runs only while the Desk is on, a place is set and Weather is on the Desk, "
        + $"at most every {(int)WeatherSchedule.Refresh.TotalMinutes} minutes.";
    public const string NetWeatherCarries =
        "The city name you typed, for Look up. For the forecast, the place's latitude and longitude rounded to 2 decimals, and your temperature unit. "
        + "No account, cookie or identifier; the user agent is just \"Sanduhr\".";
    public const string NetWeatherOff = "Clear the place, or hide the Weather piece.";

    public const string NetCatalog = "GitHub, widget catalog";
    public const string NetCatalogWhen =
        "The first time the Catalog section on Mods & Widgets opens after Settings does (you open it, or a link such as the Companion row's "
        + "Get one… opens it), when you press Check the catalog again, and when you install or update a pack.";
    public const string NetCatalogCarries =
        "Requests for the catalog, its signature and the packs you choose. No account, cookie or identifier; the user agent is just \"Sanduhr\".";
    public const string NetCatalogOff = "Leave the Catalog section closed.";

    public const string NetUpdates = "GitHub, updates";
    public static string NetUpdatesWhen =>
        $"Installs from GitHub only: at most once every {(int)UpdateCheck.Interval.TotalHours} hours, when Sanduhr starts or while it runs, "
        + "and whenever you press Check now. A newer release downloads in the background and installs only when you choose Restart to update.";
    public const string NetUpdatesCarries = "A request for Sanduhr's list of releases and, when one is newer, its files. No account, usage or file of yours.";
    public static string NetUpdatesOff => $"Turn off {CheckForUpdatesDaily}. Check now still asks once when you press it.";

    public const string NetStore = "Microsoft Store, updates";
    public static string NetStoreWhen =>
        $"Store installs only: every {(int)StoreUpdate.CheckInterval.TotalHours} hours while Sanduhr runs, it asks the Store whether an update is "
        + "waiting. When you choose Restart to update, the Store installs it.";
    public const string NetStoreCarries = "Windows asks the Store about Sanduhr's package. Sanduhr adds nothing of yours.";
    public const string NetStoreOff = "No switch on the Store build: the Microsoft Store keeps Sanduhr up to date.";

    public const string NetPublish = "Your endpoint, Publish usage";
    public const string NetPublishWhen =
        "Each day at the time you set while Publish daily at is on, and when you press Publish now. Never without an endpoint, "
        + "or without a token when your endpoint asks for one.";
    public const string NetPublishCarries =
        "The day, this PC's name, the folders you ticked, token counts in total, per limit and per project name, and your meters' percentages, "
        + "reset times and when they were read. The counts are per project name, so the project names in the folders you tick do leave this PC. "
        + "Your token goes in the header your endpoint asks for.";
    public const string NetPublishOff = "Turn off Publish daily at, and don't press Publish now.";

    public const string NetPublishAgent = "Your endpoint, when Claude asks";
    public const string NetPublishAgentWhen = "When Claude calls Sanduhr's publish_usage tool, and only while Publish daily at is on.";
    public const string NetPublishAgentCarries =
        "The same record as Publish usage, to the same endpoint. The widget sends it; the MCP server only passes the request on, on this PC.";
    public const string NetPublishAgentOff = "Turn off Publish daily at.";

    public const string NetThroughClaude = "Claude's service, through Claude Code";
    public const string NetThroughClaudeWhen =
        "When Claude calls one of Sanduhr's MCP tools. The MCP server makes no network call itself: it answers Claude Code on this PC, "
        + "and Claude Code sends the answer on with your conversation.";
    public const string NetThroughClaudeCarries =
        "What the tool answers: your meters and limits; your Desk's design, monitors, fonts, Start menu app names and wallpaper preview; "
        + "your Desk messages; and, for each folder where you let Claude read project totals, token counts per project and model with "
        + "project names, or full project paths when Claude asks.";
    public const string NetThroughClaudeOff =
        "Don't install Sanduhr's MCP server, or remove it from a folder. Each folder's project totals have their own switch.";

    /// <summary>Under the table (decision 3: the spec's sentence, qualified to Sanduhr's own calls).</summary>
    public const string NothingElseLeaves = "Nothing else leaves this PC from Sanduhr. Sanduhr has no analytics.";
    public const string NetWebView2Note =
        "The claude.ai rows run in Microsoft Edge WebView2, part of Windows. The pages are claude.ai's own, so claude.ai's code runs in them as in "
        + "your browser, and like Edge, WebView2 can contact Microsoft, for example to check a page with Microsoft Defender SmartScreen.";
    public const string NetYourBrowserNote =
        "A link you click, such as Open-Meteo's credit, a Launcher link or a font's download page, opens in your own browser. "
        + "That is your browser visiting the page; Sanduhr sends nothing with it.";
    public const string ReadPrivacyPolicy = "Read the privacy policy";

    // What Claude can change (AgentConsents.All).
    /// <summary>The spec's line, with "in a folder" dropped: a server set up by hand outside Claude Code folders works too (decision 11).</summary>
    public const string AgentsIntro = "Claude means any agent that uses Sanduhr's MCP server. None of these work until the MCP server is installed.";
    /// <summary>The install status, counted the way the Claude Code cards count it (decision 11).</summary>
    public static string McpInstalledStatus(int installed, int folders) =>
        folders == 0 ? "Sanduhr found no Claude Code folder, so its MCP server isn't installed in one."
        : installed == 0 ? "Sanduhr's MCP server isn't installed in any Claude Code folder."
        : installed == folders ? (folders == 1 ? "Sanduhr's MCP server is installed in your Claude Code folder."
            : $"Sanduhr's MCP server is installed in all {folders} Claude Code folders.")
        : $"Sanduhr's MCP server is installed in {installed} of {folders} Claude Code folders.";
    public static string McpStatusLinkName => $"{LinkTitle(SettingsPage.ClaudeCode)}, MCP server";
    public const string AgentOn = "On";
    public const string AgentOff = "Off";
    public const string AgentAsksFirst = "Asks you first";
    public const string AgentNoSwitch = "No switch";
    public static string AgentFollowsPublish(bool on) => (on ? AgentOn : AgentOff) + ", with Publish daily at";
    /// <summary>The folder reads' state: "On for ~\.claude. Off for ~\.claude-work." (per discovered folder).</summary>
    public static string AgentFolders(IReadOnlyList<(string Folder, bool On)> folders)
    {
        if (folders.Count == 0)
            return "No Claude Code folder found.";
        static string Join(List<string> names) =>
            names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
        var on = folders.Where(f => f.On).Select(f => f.Folder).ToList();
        var off = folders.Where(f => !f.On).Select(f => f.Folder).ToList();
        var parts = new List<string>();
        if (on.Count > 0) parts.Add($"On for {Join(on)}.");
        if (off.Count > 0) parts.Add($"Off for {Join(off)}.");
        return string.Join(" ", parts);
    }
    public const string AgentWatchersCaption = "Live cards on the Desk for work Claude is watching. Off: none show.";
    public const string AgentDeskDirectCaption =
        "On: Claude's Desk designs apply at once, after Sanduhr saves your Desk; one that needs a font you don't have still waits. "
        + "Off: each waits on the Desk for Apply or Dismiss.";
    public const string AgentSongsDirectCaption = "On: a look Claude suggests for a song is saved at once. Off: each waits as a card on Now playing.";
    public const string AgentFolderReadsCaption =
        "Per folder: token counts per project and per model, the folder's session archive, and its recent use beside your meters. "
        + "Project names go with them.";
    public const string AgentPublish = "Publish your usage when Claude asks";
    public const string AgentPublishCaption = "Claude can ask Sanduhr to send the day's record to your endpoint. It follows Publish daily at.";
    public const string AgentTheme = "Give the widget a theme";
    public const string AgentThemeCaption = "Claude can save a theme beside yours and switch the widget to it. Choose another one in Themes any time.";
    public const string AgentMessages = "Suggest Desk messages";
    public const string AgentMessagesCaption = "Claude's lines wait as Claude's suggestion on Message until you add or dismiss them.";
    public const string AgentWidgetPacks = "Make a widget pack";
    public const string AgentWidgetPacksCaption = "A companion, palette or effects pack Claude makes waits for your Apply or Dismiss.";
    public const string AgentReads = "Read your meters and your Desk";
    public const string AgentReadsCaption =
        "Your meters and limits, your Desk's design, monitors, fonts, Start menu app names and wallpaper preview, and your Desk messages.";
    /// <summary>Privacy's foot: one line and "About settings…" to about#where (decision 12).</summary>
    public const string PrivacyWhereLine = "Sanduhr keeps its settings, history, session archive and sign-in keys on this PC. About lists each place.";
    public static string PrivacyWhereLinkName => $"{LinkTitle(SettingsPage.About)}, {WhereSettingsLive}";

    // About, Updates (spec, About; WF6).
    public const string UpdatesSection = "Updates";
    public const string CheckForUpdatesDaily = "Check for updates once a day";
    public static string UpdatesCaption =>
        $"Sanduhr asks GitHub for a newer release at most once every {(int)UpdateCheck.Interval.TotalHours} hours, when it starts or while it runs. "
        + "A new version downloads in the background and installs only when you choose Restart to update.";
    public const string CheckNow = "Check now";
    public const string CheckNowCaption = "Check now asks once, even with the switch off.";
    public const string UpdateChecking = "Checking GitHub for a newer Sanduhr…";
    public const string UpdateUpToDate = "Sanduhr is up to date.";
    public static string UpdateReady(string version) => $"Sanduhr {version} is ready. Restart to update is in the tray menu and on the widget.";
    public const string UpdateNotInstalled = "This copy wasn't installed by Sanduhr's installer, so it can't update itself.";
    public const string UpdateFailed = "Couldn't reach GitHub. Check your connection and try again.";
    public const string StoreKeepsUpToDate = "The Microsoft Store keeps Sanduhr up to date.";
```

- [ ] **Step 4: Run it to see it pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSlice6NamesTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests" > %TEMP%\slice6-t3.log 2>&1`
Expected: PASS. No Table row or anchor is added yet, so the index tests are untouched. If `SettingsSourceScanTests` flags a constant, read its message: `SettingsNames.cs` itself is not scanned, but a literal that duplicates one of these strings elsewhere would be.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.Tests/SettingsSlice6NamesTests.cs
git commit -m "feat(settings-v2): Privacy's and About, Updates' copy, every number taken from the code that acts on it"
```

---

### Task 4: `NetworkDisclosure.All`, and a scan that holds it to every outbound call

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/NetworkDisclosure.cs`
- Modify (comment-only markers):
  - Core: `windows-dotnet/src/Sanduhr.Core/ClaudeApiClient.cs`, `windows-dotnet/src/Sanduhr.Core/UsagePublisher.cs`, `windows-dotnet/src/Sanduhr.Core/Startup.cs`.
  - App: `windows-dotnet/src/Sanduhr.App/` `Desk/DockerPipe.cs`, `Desk/WeatherService.cs`, `Desk/DeskController.cs`, `Desk/DeskReviewBar.xaml.cs`, `Interop/LauncherApps.cs`, `Modals/WebView2NotInstalledWindow.xaml.cs`, `Services/VaultService.cs`, `Services/WebView2ApiClient.cs`, `Services/WidgetCatalogService.cs`, `ViewModels/SettingsViewModel.cs`, `ViewModels/ThemesViewModel.cs`, `Views/SettingsWindow.xaml.cs`, `Views/SignInWindow.xaml.cs`, `App.xaml.cs`.
- Modify: `docs/PRIVACY.md` (the routine budget endpoint, WebView2, through Claude Code)
- Test: create `windows-dotnet/tests/Sanduhr.Tests/NetworkDisclosureTests.cs`

**Interfaces:**
- Consumes (from Task 3): the `Net*` names. Existing: `SettingsIndex` anchor constants, `SettingsIndex.Check`, `FetchingSections`, `WidgetCatalog.AllowedHosts`, `ClaudeSignIn.CookieOrigin`.
- Produces:
  - `public enum NetworkCaller { Sanduhr, ClaudeCode }`.
  - `public sealed record NetworkRow(string Id, string Destination, string Hosts, string When, string Carries, string Off, SettingsPage OffPage, string OffAnchor, NetworkCaller Caller = NetworkCaller.Sanduhr)`, with `OffLinkTitle`, `OffLink`, `OffLinkName` and `ToString() => Destination`.
  - `NetworkDisclosure.All`, `Find(string)`.
  - The constants `YourBrowser = "your-browser"`, `LocalOnly = "local"` and `PrivacyPolicyUrl`.
  - The row ids: `claude-usage`, `claude-sign-in`, `open-meteo`, `widget-catalog`, `github-update`, `store-update`, `publish-usage`, `publish-agent`, `claude-code`.

- [ ] **Step 1: Write the failing test**

Create `windows-dotnet/tests/Sanduhr.Tests/NetworkDisclosureTests.cs`:

```csharp
using System.Text.RegularExpressions;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Settings v2, slice 6 (spec, Privacy, "What leaves this PC"; WF6): every outbound call in src is disclosed. The scan finds every call shape
/// in Sanduhr.App, Sanduhr.Core and Sanduhr.Mcp (decision 1). Each line must carry "// network: id", on the line or within the three above,
/// naming a row of NetworkDisclosure.All, "your-browser" or "local". The inventory is pinned file by file and in total, so:
/// <list type="bullet">
/// <item><description>a new call fails here until it is marked and counted;</description></item>
/// <item><description>a call that stops matching (a rename, a refactor) fails too;</description></item>
/// <item><description>a scan that finds nothing can't pass.</description></item>
/// </list>
/// The MCP server may have none at all.
/// </summary>
public class NetworkDisclosureTests
{
    private static string Src()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src");
    }

    /// <summary>The call shapes (decision 1).</summary>
    internal static readonly Regex Call = new(
        @"new\s+HttpClient\s*\("
        + @"|\.(?:GetAsync|GetStringAsync|GetByteArrayAsync|GetStreamAsync|PostAsync|PutAsync|PatchAsync|DeleteAsync|SendAsync)\s*\("
        + @"|\.Navigate\s*\(|\.NavigateToString\s*\(|ExecuteScriptAsync\s*\("
        + @"|new\s+GithubSource\s*\(|new\s+UpdateManager\s*\(|\.CheckForUpdatesAsync\s*\(|\.DownloadUpdatesAsync\s*\("
        + @"|GetAppAndOptionalStorePackageUpdatesAsync\s*\(|RequestDownloadAndInstallStorePackageUpdatesAsync\s*\("
        + @"|\bDns\.|new\s+(?:TcpClient|UdpClient|Socket|ClientWebSocket|WebClient|Ping)\s*\(|WebRequest\.Create|\bHttpWebRequest\b"
        + @"|Process\.Start\s*\(|LaunchUriAsync\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex Marker = new(@"//\s*network:\s*([a-z0-9-]+(?:\s*,\s*[a-z0-9-]+)*)", RegexOptions.Compiled);

    internal sealed record Site(string File, int Line, IReadOnlyList<string> Ids, string Text);

    /// <summary>Every line in src (bin and obj aside) that matches a call shape, outside comments, with its marker's ids.</summary>
    internal static List<Site> Scan()
    {
        var src = Src();
        var sites = new List<Site>();
        foreach (var project in new[] { "Sanduhr.App", "Sanduhr.Core", "Sanduhr.Mcp" })
        {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(src, project), "*.cs", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(src, path).Replace('\\', '/');
                if (rel.Contains("/bin/", StringComparison.Ordinal) || rel.Contains("/obj/", StringComparison.Ordinal))
                    continue;
                var lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    var code = lines[i].TrimStart();
                    if (code.StartsWith("//", StringComparison.Ordinal) || code.StartsWith('*'))
                        continue;
                    if (Call.IsMatch(lines[i]))
                        sites.Add(new Site(rel, i + 1, MarkerFor(lines, i), lines[i].Trim()));
                }
            }
        }
        return sites;
    }

    /// <summary>The marker on the line, else the nearest within the three lines above.</summary>
    private static IReadOnlyList<string> MarkerFor(string[] lines, int i)
    {
        for (int j = i; j >= Math.Max(0, i - 3); j--)
        {
            if (Marker.Match(lines[j]) is { Success: true } m)
                return [.. m.Groups[1].Value.Split(',').Select(s => s.Trim())];
        }
        return [];
    }

    /// <summary>The inventory at the base commit, file by file (this plan's "The outbound-call inventory"). A site with two ids counts once
    /// under each.</summary>
    private static readonly (string File, string Id, int Count)[] Known =
    [
        ("Sanduhr.App/Services/WebView2ApiClient.cs", "claude-usage", 2),
        ("Sanduhr.Core/ClaudeApiClient.cs", "claude-usage", 3),
        ("Sanduhr.App/Views/SignInWindow.xaml.cs", "claude-sign-in", 5),
        ("Sanduhr.App/Desk/WeatherService.cs", "open-meteo", 3),
        ("Sanduhr.App/Services/WidgetCatalogService.cs", "widget-catalog", 2),
        ("Sanduhr.App/Updates/UpdateChecker.cs", "github-update", 4),
        ("Sanduhr.App/Services/StoreUpdateService.cs", "store-update", 3),
        ("Sanduhr.Core/UsagePublisher.cs", "publish-usage", 2),
        ("Sanduhr.Core/UsagePublisher.cs", "publish-agent", 2),
        ("Sanduhr.App/Desk/DockerPipe.cs", "local", 3),
        ("Sanduhr.App/App.xaml.cs", "local", 1),
        ("Sanduhr.App/Interop/LauncherApps.cs", "local", 1),
        ("Sanduhr.App/Services/VaultService.cs", "local", 1),
        ("Sanduhr.App/ViewModels/SettingsViewModel.cs", "local", 1),
        ("Sanduhr.App/ViewModels/ThemesViewModel.cs", "local", 1),
        ("Sanduhr.Core/Startup.cs", "local", 1),
        ("Sanduhr.App/Desk/DeskController.cs", "your-browser", 1),
        ("Sanduhr.App/Desk/DeskReviewBar.xaml.cs", "your-browser", 1),
        ("Sanduhr.App/Interop/LauncherApps.cs", "your-browser", 1),
        ("Sanduhr.App/Modals/WebView2NotInstalledWindow.xaml.cs", "your-browser", 1),
        ("Sanduhr.App/ViewModels/SettingsViewModel.cs", "your-browser", 1),
        ("Sanduhr.App/Views/SettingsWindow.xaml.cs", "your-browser", 1),
    ];

    /// <summary>Lines the scan must find: 24 calls Sanduhr makes, 6 browser hand-offs, 9 local.</summary>
    private const int KnownSites = 39;
    private const int KnownOutboundSites = 24;

    private static HashSet<string> Allowed() =>
        [.. NetworkDisclosure.All.Where(r => r.Caller == NetworkCaller.Sanduhr).Select(r => r.Id), NetworkDisclosure.YourBrowser, NetworkDisclosure.LocalOnly];

    [Fact]
    public void Every_call_site_carries_a_marker()
    {
        var bare = Scan().Where(s => s.Ids.Count == 0).Select(s => $"{s.File}:{s.Line}  {s.Text}").ToList();
        Assert.True(bare.Count == 0, "Undisclosed outbound calls (add a row to NetworkDisclosure.All and \"// network: <id>\"):\n" + string.Join("\n", bare));
    }

    [Fact]
    public void Every_marker_names_a_row_or_says_your_browser_or_local()
    {
        var allowed = Allowed();
        var bad = Scan().SelectMany(s => s.Ids.Where(id => !allowed.Contains(id)).Select(id => $"{s.File}:{s.Line} names \"{id}\"")).ToList();
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void The_inventory_is_what_this_slice_found()
    {
        var sites = Scan();
        Assert.Equal(KnownSites, sites.Count);
        Assert.Equal(KnownOutboundSites,
            sites.Count(s => s.Ids.Any(id => id != NetworkDisclosure.YourBrowser && id != NetworkDisclosure.LocalOnly)));
        var found = sites.SelectMany(s => s.Ids.Select(id => (s.File, Id: id)))
            .GroupBy(x => x).Select(g => (g.Key.File, g.Key.Id, Count: g.Count()))
            .OrderBy(x => x.File, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        var expected = Known.OrderBy(x => x.File, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        Assert.Equal(expected, found);
    }

    [Fact]
    public void The_MCP_server_makes_no_call()
    {
        // Network-free by contract (repo CLAUDE.md, Windows gotchas). TrustBoundaryTests scans its linked Core files; this scans its own.
        Assert.DoesNotContain(Scan(), s => s.File.StartsWith("Sanduhr.Mcp/", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_row_Sanduhr_makes_has_a_site_and_the_Claude_Code_row_has_none()
    {
        var named = Scan().SelectMany(s => s.Ids).ToHashSet();
        foreach (var row in NetworkDisclosure.All)
        {
            if (row.Caller == NetworkCaller.Sanduhr)
                Assert.True(named.Contains(row.Id), $"{row.Id} has no call site");
            else
                Assert.DoesNotContain(row.Id, named);
        }
        Assert.Contains(NetworkDisclosure.All, r => r.Caller == NetworkCaller.ClaudeCode);
    }

    [Theory]
    [InlineData("var c = new HttpClient(handler) { Timeout = x };", true)]
    [InlineData("resp = await http.SendAsync(req, ct).ConfigureAwait(false);", true)]
    [InlineData("var json = await Http.Value.GetStringAsync(url);", true)]
    [InlineData("WebView.CoreWebView2?.Navigate(ClaudeSignIn.LoginUrl);", true)]
    [InlineData("await core.ExecuteScriptAsync(BuildFetchScript(id, url, headers));", true)]
    [InlineData("var manager = new UpdateManager(source);", true)]
    [InlineData("var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();", true)]
    [InlineData("using var q = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });", true)]
    [InlineData("var ip = Dns.GetHostAddresses(host);", true)]
    [InlineData("using var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);", true)]
    [InlineData("protected override Task<HttpResponseMessage> SendAsync(", false)]
    [InlineData("private readonly HttpClient _http;", false)]
    [InlineData("_ = _updateChecker?.RunIfDueAsync();", false)]
    [InlineData("var handler = new HttpClientHandler { UseCookies = false };", false)]
    public void The_call_shapes_match_calls_and_only_calls(string line, bool isCall) =>
        Assert.Equal(isCall, Call.IsMatch(line));

    [Fact]
    public void Every_row_says_where_when_what_and_how_to_stop_it()
    {
        Assert.Equal(9, NetworkDisclosure.All.Count);
        Assert.Equal(NetworkDisclosure.All.Count, NetworkDisclosure.All.Select(r => r.Id).Distinct().Count());
        Assert.Equal(NetworkDisclosure.All.Count, NetworkDisclosure.All.Select(r => r.Destination).Distinct().Count());
        foreach (var row in NetworkDisclosure.All)
        {
            foreach (var text in new[] { row.Destination, row.Hosts, row.When, row.Carries, row.Off })
            {
                Assert.False(string.IsNullOrWhiteSpace(text), row.Id);
                Assert.Empty(SettingsNames.RetiredNames(text));
                Assert.Empty(SettingsNames.PathProblems(text));
            }
            // The link lands on a live section, never on one that fetches when opened (decision 17).
            Assert.Equal(new SettingsLink(row.OffPage, row.OffAnchor), SettingsIndex.Check(row.OffPage, row.OffAnchor));
            Assert.False(SettingsIndex.FetchingSections.ContainsKey((row.OffPage, row.OffAnchor)), row.Id);
            Assert.Equal(SettingsNames.LinkTitle(row.OffPage), row.OffLinkTitle);
            Assert.StartsWith(row.OffLinkTitle + ", ", row.OffLinkName);
            Assert.Equal(row.Destination, row.ToString());
        }
    }

    [Fact]
    public void The_rows_name_the_hosts_the_code_asks()
    {
        Assert.Contains("claude.ai", NetworkDisclosure.Find("claude-usage")!.Hosts);
        foreach (var host in WidgetCatalog.AllowedHosts)
            Assert.Contains(host, NetworkDisclosure.Find("widget-catalog")!.Hosts);
        Assert.Contains("geocoding-api.open-meteo.com", NetworkDisclosure.Find("open-meteo")!.Hosts);
        Assert.Contains("api.open-meteo.com", NetworkDisclosure.Find("open-meteo")!.Hosts);
        Assert.Contains("api.github.com", NetworkDisclosure.Find("github-update")!.Hosts);
        Assert.StartsWith("https://", NetworkDisclosure.PrivacyPolicyUrl);
        Assert.EndsWith("/docs/PRIVACY.md", NetworkDisclosure.PrivacyPolicyUrl);
    }

    [Fact]
    public void Xaml_opens_no_page_of_its_own()
    {
        var app = Path.Combine(Src(), "Sanduhr.App");
        foreach (var path in Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            var text = File.ReadAllText(path);
            Assert.DoesNotMatch(@"Source=""https?://", text);
            // A Hyperlink opens only through the window's https-only handler (the marked SettingsWindow.xaml.cs site).
            foreach (Match m in Regex.Matches(text, @"<Hyperlink\b[^>]*NavigateUri=[^>]*>", RegexOptions.Singleline))
                Assert.Contains("RequestNavigate=\"Hyperlink_RequestNavigate\"", m.Value);
        }
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~NetworkDisclosureTests > %TEMP%\slice6-t4-red.log 2>&1`
Expected: the build FAILS with `The name 'NetworkDisclosure' does not exist in the current context`.

- [ ] **Step 3: The list, and a marker at every site**

Create `windows-dotnet/src/Sanduhr.Core/NetworkDisclosure.cs`:

```csharp
namespace Sanduhr.Core;

/// <summary>Who makes the call a <see cref="NetworkRow"/> discloses.</summary>
public enum NetworkCaller
{
    /// <summary>Sanduhr itself: every such call site in src carries a "// network: id" marker (NetworkDisclosureTests).</summary>
    Sanduhr,
    /// <summary>Claude Code, sending what Sanduhr's MCP server answered as part of the conversation. Sanduhr makes no call.</summary>
    ClaudeCode,
}

/// <summary>One row of Settings, Privacy, What leaves this PC (spec, Privacy; WF6): where, when, what, and how to stop it.</summary>
public sealed record NetworkRow(
    string Id, string Destination, string Hosts, string When, string Carries, string Off,
    SettingsPage OffPage, string OffAnchor, NetworkCaller Caller = NetworkCaller.Sanduhr)
{
    public string OffLinkTitle => SettingsNames.LinkTitle(OffPage);
    public string OffLink => SettingsIndex.PageId(OffPage) + "#" + OffAnchor;
    /// <summary>The link spoken with its row, so two "Accounts settings…" read apart.</summary>
    public string OffLinkName => $"{OffLinkTitle}, {Destination}";
    /// <summary>A list item peer can read an item as its text: the destination, never the record (decision 21).</summary>
    public override string ToString() => Destination;
}

/// <summary>
/// Everything that leaves this PC (Settings v2, slice 6). One row per destination and purpose, in the page's order. NetworkDisclosureTests
/// holds this list to every outbound call in src; a new call fails CI until it is disclosed here and marked at its site. Lines that hand an
/// address to the person's browser are marked <see cref="YourBrowser"/>, and lines that stay on this PC <see cref="LocalOnly"/>; neither is
/// a row. Pure; not linked into the MCP server.
/// </summary>
public static class NetworkDisclosure
{
    /// <summary>A site that opens an https address in the person's default browser: the browser's visit, with nothing of Sanduhr's.</summary>
    public const string YourBrowser = "your-browser";
    /// <summary>A site that never leaves the PC: Docker's named pipe, Explorer, Windows Settings, a relaunch.</summary>
    public const string LocalOnly = "local";
    /// <summary>The privacy policy (owner taste 4: GitHub's rendering of docs/PRIVACY.md).</summary>
    public const string PrivacyPolicyUrl = "https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/docs/PRIVACY.md";

    public static IReadOnlyList<NetworkRow> All { get; } =
    [
        new("claude-usage", SettingsNames.NetClaudeUsage, "claude.ai",
            SettingsNames.NetClaudeUsageWhen, SettingsNames.NetClaudeUsageCarries, SettingsNames.NetClaudeUsageOff,
            SettingsPage.Accounts, SettingsIndex.AccountList),
        new("claude-sign-in", SettingsNames.NetSignIn, "claude.ai, and Google's sign-in if you choose it",
            SettingsNames.NetSignInWhen, SettingsNames.NetSignInCarries, SettingsNames.NetSignInOff,
            SettingsPage.Accounts, SettingsIndex.SignInSection),
        new("open-meteo", SettingsNames.NetWeather, "geocoding-api.open-meteo.com, api.open-meteo.com",
            SettingsNames.NetWeatherWhen, SettingsNames.NetWeatherCarries, SettingsNames.NetWeatherOff,
            SettingsPage.Weather, SettingsIndex.WeatherPlace),
        new("widget-catalog", SettingsNames.NetCatalog, string.Join(", ", WidgetCatalog.AllowedHosts),
            SettingsNames.NetCatalogWhen, SettingsNames.NetCatalogCarries, SettingsNames.NetCatalogOff,
            SettingsPage.Mods, SettingsIndex.ModList),
        new("github-update", SettingsNames.NetUpdates, "api.github.com, github.com and GitHub's download hosts",
            SettingsNames.NetUpdatesWhen, SettingsNames.NetUpdatesCarries, SettingsNames.NetUpdatesOff,
            SettingsPage.About, SettingsIndex.Updates),
        new("store-update", SettingsNames.NetStore, "the Microsoft Store, through Windows",
            SettingsNames.NetStoreWhen, SettingsNames.NetStoreCarries, SettingsNames.NetStoreOff,
            SettingsPage.About, SettingsIndex.Updates),
        new("publish-usage", SettingsNames.NetPublish, "the endpoint URL you set",
            SettingsNames.NetPublishWhen, SettingsNames.NetPublishCarries, SettingsNames.NetPublishOff,
            SettingsPage.Publish, SettingsIndex.Schedule),
        new("publish-agent", SettingsNames.NetPublishAgent, "the endpoint URL you set",
            SettingsNames.NetPublishAgentWhen, SettingsNames.NetPublishAgentCarries, SettingsNames.NetPublishAgentOff,
            SettingsPage.Publish, SettingsIndex.Schedule),
        new("claude-code", SettingsNames.NetThroughClaude, "the service Claude Code sends your conversation to",
            SettingsNames.NetThroughClaudeWhen, SettingsNames.NetThroughClaudeCarries, SettingsNames.NetThroughClaudeOff,
            SettingsPage.ClaudeCode, SettingsIndex.Folders, NetworkCaller.ClaudeCode),
    ];

    /// <summary>The row with this id, null when there is none.</summary>
    public static NetworkRow? Find(string id) => All.FirstOrDefault(r => r.Id == id);
}
```

`SettingsIndex.Updates` doesn't exist until Task 8. Add it now, so this file compiles. In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`, replace `    public const string Version = "version";` with:

```csharp
    public const string Version = "version";
    /// <summary>About, Updates (slice 6). The constant comes first, for NetworkDisclosure's links; Task 8 marks it and indexes it.</summary>
    public const string Updates = "updates";
```

Until Task 8 lands, `Check(About, "updates")` drops the unknown anchor. So, for this task only, `Every_row_says_where_when_what_and_how_to_stop_it` must skip the two About rows' `Check` assertion. Write that assertion as:

```csharp
            if (SettingsIndex.Find(row.OffPage, row.OffAnchor) is not null || row.OffAnchor != SettingsIndex.Updates)
                Assert.Equal(new SettingsLink(row.OffPage, row.OffAnchor), SettingsIndex.Check(row.OffPage, row.OffAnchor));
```

Task 8 replaces this guard with the plain assertion (its Step 3 says so).

Add a marker to every site. Each marker is one comment line, inserted directly above the line named, at that line's indentation. These are the only edits to these files:

| File | Above the line containing | Marker |
| --- | --- | --- |
| `Sanduhr.App/Services/WebView2ApiClient.cs` | `core.Navigate(StartPage);` | `// network: claude-usage` |
| `Sanduhr.App/Services/WebView2ApiClient.cs` | `await core.ExecuteScriptAsync(BuildFetchScript(id, url, headers))` | `// network: claude-usage` |
| `Sanduhr.Core/ClaudeApiClient.cs` | `return base.SendAsync(request, cancellationToken);` | `// network: claude-usage (the HttpClient transport; the app builds WebView2ApiClient, tests build this)` |
| `Sanduhr.Core/ClaudeApiClient.cs` | `_http = new HttpClient(new CloudflareAwareHandler(inner), disposeHandler: true)` | `// network: claude-usage` |
| `Sanduhr.Core/ClaudeApiClient.cs` | `return await _http.SendAsync(req, ct).ConfigureAwait(false);` | `// network: claude-usage` |
| `Sanduhr.App/Views/SignInWindow.xaml.cs` | each of the five `Navigate(` lines (108, 163, 310, 367, 381) | `// network: claude-sign-in` |
| `Sanduhr.App/Desk/WeatherService.cs` | `var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };` | `// network: open-meteo` |
| `Sanduhr.App/Desk/WeatherService.cs` | `var json = await Http.Value.GetStringAsync(url);` | `// network: open-meteo (the forecast)` |
| `Sanduhr.App/Desk/WeatherService.cs` | `var json = await Http.Value.GetStringAsync(url, ct);` | `// network: open-meteo (Look up)` |
| `Sanduhr.App/Services/WidgetCatalogService.cs` | `var c = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromMinutes(2) };` | `// network: widget-catalog` |
| `Sanduhr.App/Services/WidgetCatalogService.cs` | `using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)` | `// network: widget-catalog` |
| `Sanduhr.Core/UsagePublisher.cs` | `using var http = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: handler is null)` | `// network: publish-usage, publish-agent` |
| `Sanduhr.Core/UsagePublisher.cs` | `resp = await http.SendAsync(req, ct).ConfigureAwait(false);` | `// network: publish-usage, publish-agent` |
| `Sanduhr.App/Desk/DockerPipe.cs` | `_http = new HttpClient(handler) { BaseAddress = new Uri("http://docker/")` | `// network: local (Docker's named pipe; no TCP)` (no `/json`, no `containers/`: `DockerPipeBoundaryTests` counts both in this file) |
| `Sanduhr.App/Desk/DockerPipe.cs` | `using var response = await _http.GetAsync(PathFor(Call.ListContainers)` | `// network: local` |
| `Sanduhr.App/Desk/DockerPipe.cs` | `var response = await _http.GetAsync(PathFor(Call.FollowLogs, id)` | `// network: local` |
| `Sanduhr.App/App.xaml.cs` | `System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target.FileName, target.Arguments)` | `// network: local (the relaunch)` |
| `Sanduhr.App/Interop/LauncherApps.cs` | `using var p = Process.Start(start);` | `// network: local (an app, through explorer.exe)` |
| `Sanduhr.App/Interop/LauncherApps.cs` | `using var q = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });` | `// network: your-browser` |
| `Sanduhr.App/Services/VaultService.cs` | `Process.Start(new ProcessStartInfo("explorer.exe", _paths.VaultDir)` | `// network: local` |
| `Sanduhr.App/ViewModels/SettingsViewModel.cs` | `System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"")` | `// network: local` |
| `Sanduhr.App/ViewModels/SettingsViewModel.cs` | `System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });` | `// network: your-browser` |
| `Sanduhr.App/ViewModels/ThemesViewModel.cs` | `Process.Start(new ProcessStartInfo("explorer.exe", dir)` | `// network: local` |
| `Sanduhr.Core/Startup.cs` | `=> Process.Start(new ProcessStartInfo(Startup.SettingsUri) { UseShellExecute = true });` | `// network: local (Windows Settings)` |
| `Sanduhr.App/Desk/DeskController.cs` | `System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DeskClicks.WeatherCreditUrl)` | `// network: your-browser` |
| `Sanduhr.App/Desk/DeskReviewBar.xaml.cs` | `System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(safe) { UseShellExecute = true });` | `// network: your-browser` |
| `Sanduhr.App/Modals/WebView2NotInstalledWindow.xaml.cs` | `Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });` | `// network: your-browser` |
| `Sanduhr.App/Views/SettingsWindow.xaml.cs` | `System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();` | `// network: your-browser` |

Two of those lines need care:

- **`Startup.cs:155` is an expression-bodied member** (`=> Process.Start(…)` on its own line under the signature). The marker goes between the signature line and the `=>` line. A comment line there is legal C#.
- **In `SignInWindow.xaml.cs`, line 381 is also an expression body** (`=> WebView.CoreWebView2?.Navigate(…)`). Its marker goes between its signature and the `=>` line in the same way.

In `SettingsViewModel.cs`, change `OpenLink`'s comment `// Only the fixed https links from ThirdPartyNotices reach here.` to `// Only fixed https links reach here: ThirdPartyNotices' project pages and the privacy policy (Settings, Privacy).`.

In `docs/PRIVACY.md`:

- Replace the sentence `The only network destination it contacts is `claude.ai` — and only with the credentials you yourself paste into it — to read your own Claude subscription usage so it can show it to you.` (the Short version's second paragraph, all of it) with:

```
By default it contacts `claude.ai`, only with the credentials you yourself paste into it, to read your own Claude subscription usage, and it checks for updates: this project's GitHub releases on the .exe build (Settings ▸ About ▸ Updates turns that off), the Microsoft Store on the Store build. On Windows, Settings ▸ Privacy ▸ What leaves this PC lists every destination.
```

- Replace `By default, exactly one destination:` with `For your usage, one destination:`.
- After the list item `- `https://claude.ai/api/organizations/{your-org-id}/usage` — to read your subscription usage`, add:

```
- `https://claude.ai/v1/code/routines/run-budget` — to read your Claude Code routines budget, shown as a meter when your plan has one
```

- After the paragraph that starts `Anthropic's privacy policy governs`, add:

```
On Windows these requests run in Microsoft Edge WebView2, part of Windows, in a hidden window that opens claude.ai first. The page is claude.ai's own, so claude.ai's code runs in it as in your browser, and like Edge, WebView2 can contact Microsoft, for example to check a page with Microsoft Defender SmartScreen.

**Through Claude Code, not Sanduhr.** Sanduhr's MCP server makes no network request: it answers Claude Code on this PC. What it answers becomes part of your conversation, which Claude Code sends to the service it uses. That is your meters and limits, your Desk's design, monitors, fonts, Start menu app names and wallpaper preview, your Desk messages, and, for each Claude Code folder where you turned on "Let Claude read this folder's project totals", token counts per project and model with project names (or full project paths when Claude asks).
```

- [ ] **Step 4: Run it to see it pass**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~NetworkDisclosureTests > %TEMP%\slice6-t4.log 2>&1`
Expected: PASS. When `The_inventory_is_what_this_slice_found` fails, its message names the file and id that differ. The fix is a missing or misplaced marker, never a changed count. If the scan finds a line this plan's inventory missed, stop and report it with the line: that is a privacy finding, not a test to adjust.

Then: `dotnet build Sanduhr.slnx > %TEMP%\slice6-t4-build.log 2>&1` (from `windows-dotnet/`). Expected: 0 errors. The markers are comments; the build proves none landed inside an expression.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/NetworkDisclosure.cs windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.Core/ClaudeApiClient.cs windows-dotnet/src/Sanduhr.Core/UsagePublisher.cs windows-dotnet/src/Sanduhr.Core/Startup.cs windows-dotnet/src/Sanduhr.App docs/PRIVACY.md windows-dotnet/tests/Sanduhr.Tests/NetworkDisclosureTests.cs
git commit -m "feat(settings-v2): What leaves this PC from one list, held to every outbound call by a source scan"
```

(`git add windows-dotnet/src/Sanduhr.App` adds only the marker edits listed above. Check `git diff --cached --stat` shows those 14 App files and nothing else.)

---

### Task 5: `AgentConsents.All`, held to every tool the MCP server offers

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/AgentConsents.cs`
- Test: create `windows-dotnet/tests/Sanduhr.Tests/AgentConsentsTests.cs`

**Interfaces:**
- Consumes (from Task 3): the `Agent*` names, `AgentFolders`, `AgentFollowsPublish`. Existing: `SettingsNames.WatchersSwitch`, `DeskDirectSwitch`, `NowPlayingLooksDirect`, `LetClaudeReadTotals`; the `SettingsIndex` anchors.
- Produces:
  - `public enum AgentConsentKind { Switch, PerFolder, FollowsPublish, AsksFirst, NoSwitch }`.
  - `public sealed record AgentConsent(string Id, string Name, string Caption, AgentConsentKind Kind, string? Key, SettingsPage Home, string HomeAnchor, IReadOnlyList<string> Tools)`, with `LinkTitle`, `Link` and `LinkName`.
  - `AgentConsents.All`.
  - The id constants `Watchers`, `DeskDirect`, `SongsDirect`, `FolderReads`, `Publish`, `Theme`, `Messages`, `WidgetPacks`, `Reads`.
  - `AgentConsents.State(AgentConsent c, bool on, IReadOnlyList<(string Folder, bool On)> folders)`.

- [ ] **Step 1: Write the failing test**

Create `windows-dotnet/tests/Sanduhr.Tests/AgentConsentsTests.cs`:

```csharp
using System.Text.RegularExpressions;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Settings v2, slice 6 (spec, Privacy, "What Claude can change"; WF10): every tool Sanduhr's MCP server offers is in exactly one row, so a
/// new tool fails here until the page says what it lets Claude do. The tools are read from src/Sanduhr.Mcp/ToolCatalog.cs, the list the
/// server answers tools/list with.
/// </summary>
public class AgentConsentsTests
{
    private static IReadOnlyList<string> CatalogTools()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var text = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Sanduhr.Mcp", "ToolCatalog.cs"));
        return [.. Regex.Matches(text, @"\bTool\(\s*""([a-z_]+)""").Select(m => m.Groups[1].Value)];
    }

    [Fact]
    public void Every_MCP_tool_is_in_exactly_one_row()
    {
        var tools = CatalogTools();
        Assert.Equal(16, tools.Count);   // the scan must find the catalog, not nothing
        foreach (var tool in tools)
            Assert.True(AgentConsents.All.Count(c => c.Tools.Contains(tool)) == 1, $"{tool} is in {AgentConsents.All.Count(c => c.Tools.Contains(tool))} rows");
        Assert.Equal(tools.Order(StringComparer.Ordinal), AgentConsents.All.SelectMany(c => c.Tools).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_spec_four_come_first_in_its_order_and_wear_their_switches_names()
    {
        Assert.Equal([AgentConsents.Watchers, AgentConsents.DeskDirect, AgentConsents.SongsDirect, AgentConsents.FolderReads],
            AgentConsents.All.Take(4).Select(c => c.Id));
        Assert.Equal([SettingsNames.WatchersSwitch, SettingsNames.DeskDirectSwitch, SettingsNames.NowPlayingLooksDirect, SettingsNames.LetClaudeReadTotals],
            AgentConsents.All.Take(4).Select(c => c.Name));
    }

    [Fact]
    public void A_row_with_a_switch_names_its_key_and_a_row_without_names_none()
    {
        foreach (var c in AgentConsents.All)
        {
            if (c.Kind is AgentConsentKind.AsksFirst or AgentConsentKind.NoSwitch)
                Assert.Null(c.Key);
            else
                Assert.False(string.IsNullOrEmpty(c.Key), c.Id);
        }
        Assert.Equal(["watchers_agents", "desk_direct", "now_playing.looks_direct", "mcp_roots", "publish.enabled"],
            AgentConsents.All.Where(c => c.Key is not null).Select(c => c.Key!));
    }

    [Fact]
    public void Every_row_links_to_its_home_section()
    {
        foreach (var c in AgentConsents.All)
        {
            Assert.Equal(new SettingsLink(c.Home, c.HomeAnchor), SettingsIndex.Check(c.Home, c.HomeAnchor));
            Assert.False(SettingsIndex.FetchingSections.ContainsKey((c.Home, c.HomeAnchor)), c.Id);
            Assert.Equal(SettingsNames.LinkTitle(c.Home), c.LinkTitle);
            Assert.Equal($"{SettingsIndex.PageId(c.Home)}#{c.HomeAnchor}", c.Link);
            Assert.StartsWith(c.LinkTitle + ", ", c.LinkName);
            Assert.Empty(SettingsNames.RetiredNames(c.Name));
            Assert.Empty(SettingsNames.PathProblems(c.Caption));
        }
    }

    [Fact]
    public void States_read_as_the_kind_says()
    {
        AgentConsent Of(string id) => AgentConsents.All.Single(c => c.Id == id);
        IReadOnlyList<(string, bool)> none = [];
        Assert.Equal("On", AgentConsents.State(Of(AgentConsents.Watchers), true, none));
        Assert.Equal("Off", AgentConsents.State(Of(AgentConsents.DeskDirect), false, none));
        Assert.Equal("On, with Publish daily at", AgentConsents.State(Of(AgentConsents.Publish), true, none));
        Assert.Equal("Asks you first", AgentConsents.State(Of(AgentConsents.Messages), true, none));
        Assert.Equal("Asks you first", AgentConsents.State(Of(AgentConsents.WidgetPacks), false, none));
        Assert.Equal("No switch", AgentConsents.State(Of(AgentConsents.Theme), false, none));
        Assert.Equal("No switch", AgentConsents.State(Of(AgentConsents.Reads), false, none));
        Assert.Equal(@"On for ~\.claude.", AgentConsents.State(Of(AgentConsents.FolderReads), false, [(@"~\.claude", true)]));
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~AgentConsentsTests > %TEMP%\slice6-t5-red.log 2>&1`
Expected: the build FAILS with `The name 'AgentConsents' does not exist in the current context`.

- [ ] **Step 3: The list**

Create `windows-dotnet/src/Sanduhr.Core/AgentConsents.cs`:

```csharp
namespace Sanduhr.Core;

/// <summary>How a person decides what Claude may do through one row of tools.</summary>
public enum AgentConsentKind
{
    /// <summary>One switch in settings.json, off by default.</summary>
    Switch,
    /// <summary>One switch per Claude Code folder ("mcp_roots").</summary>
    PerFolder,
    /// <summary>Follows "Publish daily at" ("publish.enabled").</summary>
    FollowsPublish,
    /// <summary>No switch: everything Claude proposes waits for the person.</summary>
    AsksFirst,
    /// <summary>No switch: Claude can do it whenever the MCP server is installed.</summary>
    NoSwitch,
}

/// <summary>One row of Settings, Privacy, What Claude can change: the tools it covers, how it is decided, and where its home is.</summary>
public sealed record AgentConsent(string Id, string Name, string Caption, AgentConsentKind Kind, string? Key,
    SettingsPage Home, string HomeAnchor, IReadOnlyList<string> Tools)
{
    public string LinkTitle => SettingsNames.LinkTitle(Home);
    public string Link => SettingsIndex.PageId(Home) + "#" + HomeAnchor;
    /// <summary>The link spoken with its row, so the two "Claude Code settings…" read apart.</summary>
    public string LinkName => $"{LinkTitle}, {Name}";
}

/// <summary>
/// What Claude can change (Settings v2, slice 6; WF10): every tool of Sanduhr's MCP server, in nine rows (decision 9). The spec's four consents
/// come first in its order; then the rows the spec didn't name, so the list is the whole truth. AgentConsentsTests holds it to ToolCatalog.cs.
/// Pure; not linked into the MCP server.
/// </summary>
public static class AgentConsents
{
    public const string Watchers = "watchers";
    public const string DeskDirect = "desk-direct";
    public const string SongsDirect = "songs-direct";
    public const string FolderReads = "folder-reads";
    public const string Publish = "publish";
    public const string Theme = "theme";
    public const string Messages = "messages";
    public const string WidgetPacks = "widget-packs";
    public const string Reads = "reads";

    public static IReadOnlyList<AgentConsent> All { get; } =
    [
        new(Watchers, SettingsNames.WatchersSwitch, SettingsNames.AgentWatchersCaption, AgentConsentKind.Switch, "watchers_agents",
            SettingsPage.Watchers, SettingsIndex.Watchers, ["watch_start", "watch_update", "watch_end"]),
        new(DeskDirect, SettingsNames.DeskDirectSwitch, SettingsNames.AgentDeskDirectCaption, AgentConsentKind.Switch, "desk_direct",
            SettingsPage.Desk, SettingsIndex.DeskDesigns, ["propose_desk"]),
        new(SongsDirect, SettingsNames.NowPlayingLooksDirect, SettingsNames.AgentSongsDirectCaption, AgentConsentKind.Switch, "now_playing.looks_direct",
            SettingsPage.NowPlaying, SettingsIndex.NowPlayingLooks, ["propose_now_playing_looks"]),
        new(FolderReads, SettingsNames.LetClaudeReadTotals, SettingsNames.AgentFolderReadsCaption, AgentConsentKind.PerFolder, "mcp_roots",
            SettingsPage.ClaudeCode, SettingsIndex.Folders, ["get_local_burn_by_project", "get_model_usage", "get_usage_history"]),
        new(Publish, SettingsNames.AgentPublish, SettingsNames.AgentPublishCaption, AgentConsentKind.FollowsPublish, "publish.enabled",
            SettingsPage.Publish, SettingsIndex.Schedule, ["publish_usage"]),
        new(Theme, SettingsNames.AgentTheme, SettingsNames.AgentThemeCaption, AgentConsentKind.NoSwitch, null,
            SettingsPage.Themes, SettingsIndex.Gallery, ["propose_theme"]),
        new(Messages, SettingsNames.AgentMessages, SettingsNames.AgentMessagesCaption, AgentConsentKind.AsksFirst, null,
            SettingsPage.DeskMessage, SettingsIndex.MessageSuggestion, ["propose_desk_messages"]),
        new(WidgetPacks, SettingsNames.AgentWidgetPacks, SettingsNames.AgentWidgetPacksCaption, AgentConsentKind.AsksFirst, null,
            SettingsPage.Mods, SettingsIndex.ModList, ["propose_widget"]),
        new(Reads, SettingsNames.AgentReads, SettingsNames.AgentReadsCaption, AgentConsentKind.NoSwitch, null,
            SettingsPage.ClaudeCode, SettingsIndex.Folders, ["get_usage", "get_desk", "get_desk_messages", "ping"]),
    ];

    /// <summary>A row's state as the list shows it. <paramref name="on"/> is the switch (Switch, FollowsPublish); <paramref name="folders"/>
    /// is each discovered folder, shown as on screen, and its read switch (PerFolder).</summary>
    public static string State(AgentConsent c, bool on, IReadOnlyList<(string Folder, bool On)> folders) => c.Kind switch
    {
        AgentConsentKind.Switch => on ? SettingsNames.AgentOn : SettingsNames.AgentOff,
        AgentConsentKind.FollowsPublish => SettingsNames.AgentFollowsPublish(on),
        AgentConsentKind.PerFolder => SettingsNames.AgentFolders(folders),
        AgentConsentKind.AsksFirst => SettingsNames.AgentAsksFirst,
        _ => SettingsNames.AgentNoSwitch,
    };
}
```

- [ ] **Step 4: Run it to see it pass**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~AgentConsentsTests > %TEMP%\slice6-t5.log 2>&1`
Expected: PASS. If `Every_MCP_tool_is_in_exactly_one_row` names a tool this plan didn't list, the MCP server changed since 8dae01fc. Stop and report it: the row's words depend on what that tool does.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/AgentConsents.cs windows-dotnet/tests/Sanduhr.Tests/AgentConsentsTests.cs
git commit -m "feat(settings-v2): What Claude can change from one list, held to every MCP tool"
```

---

### Task 6: `PrivacyViewModel`: each consent read where its page writes it, and the MCP install read from the folders

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/PrivacyViewModel.cs`
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/PrivacyViewModelTests.cs`

**Interfaces:**
- Consumes:
  - From Task 4: `NetworkDisclosure.All`.
  - From Task 5: `AgentConsents.All`, `State`.
  - From Task 3: `SettingsNames.McpInstalledStatus`, `McpStatusLinkName`, `PrivacyWhereLinkName`.
  - Existing: `ClaudeCodeHost` (internal record, `Folders()`, `Display`, `Profile`, `SanduhrAppDataDir`, `SanduhrExternalDir`), `McpIntegrationInstaller.IsRegistered`, `SettingsStore.LoadWatchersAgents`, `LoadDeskDirect`, `LoadNowPlaying().LooksDirect`.
- Produces:
  - `public sealed record AgentRow(string Name, string State, string Caption, string LinkTitle, string Link, string LinkName)`, with `ToString() => $"{Name}, {State}"`.
  - `public sealed partial class PrivacyViewModel : ObservableObject`. It has an `internal` constructor `(SettingsStore store, ClaudeCodeHost host, Func<IReadOnlyDictionary<string, bool>> mcpRoots, Func<bool> publishOn)`.
  - Its members are `Network`, `Agents`, `McpStatus`, `ClaudeCodeLinkTitle`, `ClaudeCodeLink`, `McpStatusLinkName`, `AboutLinkTitle`, `AboutLink`, `AboutLinkName` and `Reload()`.

- [ ] **Step 1: Write the failing test**

Create `windows-dotnet/tests/Sanduhr.App.Tests/PrivacyViewModelTests.cs`:

```csharp
using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 6: Privacy reads each consent from the store its own page writes (decision 10) and the MCP server's install
/// the way the Claude Code cards do (decision 11).</summary>
[Collection("SettingsWindow")]
public sealed class PrivacyViewModelTests : AppWindowFixture
{
    private (PrivacyViewModel Vm, SettingsStore Pages, Dictionary<string, bool> Roots, WidgetViewModel Widget) Make(bool publishOn = false)
    {
        var (widget, _) = ScratchWidget("Personal");
        var pages = new SettingsStore(new Paths());   // where Watchers, Desk and Now playing write (the fixture's dev profile)
        var roots = new Dictionary<string, bool>(StringComparer.Ordinal);
        var vm = new PrivacyViewModel(pages, ClaudeCodeHost.For(widget), () => roots, () => publishOn);
        return (vm, pages, roots, widget);
    }

    private static string StateOf(PrivacyViewModel vm, string id) =>
        vm.Agents[AgentConsents.All.ToList().FindIndex(c => c.Id == id)].State;

    [Fact]
    public void Every_switch_is_off_by_default_and_the_others_say_what_they_are()
    {
        var (vm, _, _, widget) = Make();
        try
        {
            Assert.Equal(AgentConsents.All.Count, vm.Agents.Count);
            Assert.Equal(SettingsNames.AgentOff, StateOf(vm, AgentConsents.Watchers));
            Assert.Equal(SettingsNames.AgentOff, StateOf(vm, AgentConsents.DeskDirect));
            Assert.Equal(SettingsNames.AgentOff, StateOf(vm, AgentConsents.SongsDirect));
            Assert.Equal(SettingsNames.AgentFollowsPublish(false), StateOf(vm, AgentConsents.Publish));
            Assert.Equal(SettingsNames.AgentNoSwitch, StateOf(vm, AgentConsents.Theme));
            Assert.Equal(SettingsNames.AgentAsksFirst, StateOf(vm, AgentConsents.Messages));
            Assert.Same(NetworkDisclosure.All, vm.Network);
        }
        finally { widget.Dispose(); }
    }

    [Fact]
    public void Each_switch_row_reads_the_key_its_page_writes()
    {
        var (vm, pages, _, widget) = Make(publishOn: true);
        try
        {
            pages.SaveWatchersAgents(true);
            pages.SaveDeskDirect(true);
            pages.SaveNowPlaying(pages.LoadNowPlaying() with { LooksDirect = true });
            vm.Reload();

            Assert.Equal(SettingsNames.AgentOn, StateOf(vm, AgentConsents.Watchers));
            Assert.Equal(SettingsNames.AgentOn, StateOf(vm, AgentConsents.DeskDirect));
            Assert.Equal(SettingsNames.AgentOn, StateOf(vm, AgentConsents.SongsDirect));
            Assert.Equal(SettingsNames.AgentFollowsPublish(true), StateOf(vm, AgentConsents.Publish));
            Assert.Equal($"{SettingsNames.WatchersSwitch}, {SettingsNames.AgentOn}", vm.Agents[0].ToString());
        }
        finally { widget.Dispose(); }
    }

    [Fact]
    public void The_folder_row_lists_each_found_folder_with_its_read_switch()
    {
        var work = Home(".claude-work");
        var (vm, _, roots, widget) = Make();
        try
        {
            var host = ClaudeCodeHost.For(widget);
            roots[".claude"] = true;
            vm.Reload();
            Assert.Equal(SettingsNames.AgentFolders([(host.Display(Path.Combine(Profile, ".claude")), true), (host.Display(work), false)]),
                StateOf(vm, AgentConsents.FolderReads));
        }
        finally { widget.Dispose(); }
    }

    [Fact]
    public void The_MCP_status_counts_the_folders_holding_Sanduhrs_entry()
    {
        Home(".claude-work");
        var (vm, _, _, widget) = Make();
        try
        {
            Assert.Equal(SettingsNames.McpInstalledStatus(0, 2), vm.McpStatus);

            var host = ClaudeCodeHost.For(widget);
            Assert.True(new McpIntegrationInstaller(host.Profile, host.SanduhrAppDataDir, externalDir: host.SanduhrExternalDir).Register(".claude").Ok);
            vm.Reload();
            Assert.Equal(SettingsNames.McpInstalledStatus(1, 2), vm.McpStatus);
        }
        finally { widget.Dispose(); }
    }

    [Fact]
    public void With_no_Claude_Code_folder_both_lines_say_so()
    {
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            var empty = Path.Combine(Root, "empty-profile");
            Directory.CreateDirectory(empty);
            var host = ClaudeCodeHost.For(widget) with { Profile = empty, AddedFolders = () => [] };
            var vm = new PrivacyViewModel(new SettingsStore(new Paths()), host, () => new Dictionary<string, bool>(), () => false);

            Assert.Equal(SettingsNames.McpInstalledStatus(0, 0), vm.McpStatus);
            Assert.Equal(SettingsNames.AgentFolders([]), StateOf(vm, AgentConsents.FolderReads));
        }
        finally { widget.Dispose(); }
    }

    [Fact]
    public void The_links_land_where_the_page_says()
    {
        var (vm, _, _, widget) = Make();
        try
        {
            Assert.Equal("claudecode#folders", vm.ClaudeCodeLink);
            Assert.Equal("about#where", vm.AboutLink);
            Assert.StartsWith(vm.ClaudeCodeLinkTitle + ", ", vm.McpStatusLinkName);
            Assert.StartsWith(vm.AboutLinkTitle + ", ", vm.AboutLinkName);
            Assert.All(vm.Agents, a => Assert.StartsWith(a.LinkTitle + ", ", a.LinkName));
        }
        finally { widget.Dispose(); }
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~PrivacyViewModelTests > %TEMP%\slice6-t6-red.log 2>&1`
Expected: the build FAILS with `The type or namespace name 'PrivacyViewModel' could not be found`.

- [ ] **Step 3: The view model**

Create `windows-dotnet/src/Sanduhr.App/ViewModels/PrivacyViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One row of What Claude can change, as shown. Reads as "name, state" (decision 21), never as the record.</summary>
public sealed record AgentRow(string Name, string State, string Caption, string LinkTitle, string Link, string LinkName)
{
    public override string ToString() => $"{Name}, {State}";
}

/// <summary>
/// Settings, Privacy (Settings v2, slice 6; WF6, WF10). The page has three parts:
/// <list type="bullet">
/// <item><description>What leaves this PC is <see cref="NetworkDisclosure.All"/> as it is.</description></item>
/// <item><description>What Claude can change is <see cref="AgentConsents.All"/>. Each switch is read from the store its own page writes
/// (decision 10): Watchers, Desk and Now playing write <c>new SettingsStore(new Paths())</c>; the Claude Code cards write <c>mcp_roots</c>
/// through the widget; Publish usage writes <c>publish.enabled</c> through the widget's service.</description></item>
/// <item><description>The MCP server's install is read from the folders, as the cards read it (decision 11).</description></item>
/// </list>
/// It subscribes to nothing. Settings calls <see cref="Reload"/> on each visit, and nothing needs detaching when the window closes.
/// </summary>
public sealed partial class PrivacyViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly ClaudeCodeHost _host;
    private readonly Func<IReadOnlyDictionary<string, bool>> _mcpRoots;
    private readonly Func<bool> _publishOn;

    /// <summary>What leaves this PC, in the page's order.</summary>
    public IReadOnlyList<NetworkRow> Network => NetworkDisclosure.All;

    /// <summary>What Claude can change, in the page's order, with each row's state as read on the last visit.</summary>
    public ObservableCollection<AgentRow> Agents { get; } = new();

    /// <summary>Where the MCP server is installed, as the Claude Code cards count it.</summary>
    [ObservableProperty] private string _mcpStatus = "";

    public string ClaudeCodeLinkTitle => SettingsNames.LinkTitle(SettingsPage.ClaudeCode);
    public string ClaudeCodeLink => SettingsIndex.PageId(SettingsPage.ClaudeCode) + "#" + SettingsIndex.Folders;
    public string McpStatusLinkName => SettingsNames.McpStatusLinkName;
    public string AboutLinkTitle => SettingsNames.LinkTitle(SettingsPage.About);
    public string AboutLink => SettingsIndex.PageId(SettingsPage.About) + "#" + SettingsIndex.Where;
    public string AboutLinkName => SettingsNames.PrivacyWhereLinkName;

    internal PrivacyViewModel(SettingsStore store, ClaudeCodeHost host, Func<IReadOnlyDictionary<string, bool>> mcpRoots, Func<bool> publishOn)
    {
        _store = store;
        _host = host;
        _mcpRoots = mcpRoots;
        _publishOn = publishOn;
        Reload();
    }

    /// <summary>Read every state again (the page was opened; a consent's own page may have changed it).</summary>
    public void Reload()
    {
        var discovered = _host.Folders().Where(f => f.Discovered).ToList();
        var installer = new McpIntegrationInstaller(_host.Profile, _host.SanduhrAppDataDir, externalDir: _host.SanduhrExternalDir);
        McpStatus = SettingsNames.McpInstalledStatus(discovered.Count(f => Registered(installer, f.Name)), discovered.Count);

        var roots = _mcpRoots();
        IReadOnlyList<(string Folder, bool On)> folders = [.. discovered.Select(f => (_host.Display(f.Path), roots.GetValueOrDefault(f.Name)))];
        Agents.Clear();
        foreach (var c in AgentConsents.All)
            Agents.Add(new AgentRow(c.Name, AgentConsents.State(c, SwitchOn(c), folders), c.Caption, c.LinkTitle, c.Link, c.LinkName));
    }

    private bool SwitchOn(AgentConsent c) => c.Id switch
    {
        AgentConsents.Watchers => _store.LoadWatchersAgents(),
        AgentConsents.DeskDirect => _store.LoadDeskDirect(),
        AgentConsents.SongsDirect => _store.LoadNowPlaying().LooksDirect,
        AgentConsents.Publish => _publishOn(),
        _ => false,
    };

    private static bool Registered(McpIntegrationInstaller installer, string name)
    {
        try { return installer.IsRegistered(name); }
        catch (Exception) { return false; }   // a folder that can't be read doesn't count, as on its card
    }
}
```

- [ ] **Step 4: Run it to see it pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~PrivacyViewModelTests > %TEMP%\slice6-t6.log 2>&1`
Expected: PASS. If `The_folder_row_lists_each_found_folder_with_its_read_switch` lists the folders in another order, check `ClaudeCodeFolders.Build`'s order (`.claude` first, then `.claude-*` in natural order). Assert that order. Don't sort in the view model: the cards use the same order.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/PrivacyViewModel.cs windows-dotnet/tests/Sanduhr.App.Tests/PrivacyViewModelTests.cs
git commit -m "feat(settings-v2): Privacy reads each consent where its page writes it and the MCP install where the cards do"
```

---

### Task 7: The Privacy page

**Files:**
- Modify Core:
  - `windows-dotnet/src/Sanduhr.Core/WhatsNew.cs` (`SettingsPage.Privacy`).
  - `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (`Sidebar`, `TitlesToCome`, `PageTitle`).
  - `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (anchors, synonyms).
- Create App: `windows-dotnet/src/Sanduhr.App/Views/PrivacyPage.xaml`, `windows-dotnet/src/Sanduhr.App/Views/PrivacyPage.xaml.cs`.
- Modify App:
  - `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (the TabItem after Alerts).
  - `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs` (Reload on visit).
  - `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (`Privacy`).
- Modify tests: `windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs`, `windows-dotnet/tests/Sanduhr.Tests/SettingsModsIndexTests.cs`.
- Test: create `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice6IndexTests.cs`, `windows-dotnet/tests/Sanduhr.App.Tests/PrivacyPageTests.cs`.

**Interfaces:**
- Consumes:
  - From Task 6: `PrivacyViewModel`.
  - From Task 3: `WhatLeavesThisPc`, `WhatClaudeCanChange`, `PrivacyLine`, the labels and notes, `ReadPrivacyPolicy`, `PrivacyWhereLine`.
  - From Task 4: `NetworkDisclosure.PrivacyPolicyUrl`.
  - Existing: `SettingsViewModel.OpenLinkCommand`.
- Produces:
  - `SettingsPage.Privacy`.
  - `SettingsIndex.Network = "network"`, `SettingsIndex.Agents = "agents"`.
  - `SettingsViewModel.Privacy`.
  - `Views/PrivacyPage` with the slice 7 seat comment.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice6IndexTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 6: the Privacy page in the sidebar and the index, and the words that land on it.</summary>
public class SettingsSlice6IndexTests
{
    private static SettingsIndex.Hit Top(string q)
    {
        var hits = SettingsIndex.Search(q);
        Assert.NotEmpty(hits);
        return hits[0];
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("offline")]
    [InlineData("network")]
    [InlineData("send")]
    public void Privacy_words_land_on_what_leaves_this_PC_first(string query)
    {
        var top = Top(query);
        Assert.Equal((SettingsPage.Privacy, SettingsIndex.Network, -1), (top.Entry.Page, top.Entry.Anchor, top.Rank));
    }

    [Fact]
    public void Privacy_groups_first_for_its_own_name_and_send_still_finds_the_test_alert()
    {
        Assert.Equal(SettingsPage.Privacy, SettingsIndex.Grouped(SettingsIndex.Search("privacy"))[0].Page);
        Assert.Contains(SettingsIndex.Search("send"), h => h.Entry.Page == SettingsPage.Alerts && h.Entry.Anchor == SettingsIndex.TestAlert);
        Assert.DoesNotContain("privacy", SettingsIndex.PageSynonyms[SettingsPage.About]);
    }

    [Fact]
    public void Privacy_is_the_first_groups_fourth_page()
    {
        Assert.Equal([SettingsPage.General, SettingsPage.Accounts, SettingsPage.Alerts, SettingsPage.Privacy], SettingsNames.Sidebar[0].Pages);
        Assert.Equal("Privacy", SettingsNames.PageTitle(SettingsPage.Privacy));
        Assert.Equal(SettingsPage.Privacy, SettingsNames.Linked("Privacy settings…"));
        Assert.Empty(SettingsNames.TitlesToCome);
        Assert.Equal(SettingsPage.Privacy, SettingsIndex.PageFromId("privacy"));
    }

    [Fact]
    public void Privacy_holds_two_sections_and_no_jump_links()
    {
        Assert.Equal([SettingsIndex.Network, SettingsIndex.Agents], SettingsIndex.EntriesOn(SettingsPage.Privacy).Select(e => e.Anchor));
        Assert.Equal([SettingsNames.WhatLeavesThisPc, SettingsNames.WhatClaudeCanChange],
            SettingsIndex.EntriesOn(SettingsPage.Privacy).Select(e => e.Title));
        Assert.Empty(SettingsIndex.JumpLinks(SettingsPage.Privacy));   // two sections (decision 12); slice 7 adds the third
    }

    [Theory]
    [InlineData("agents")]
    [InlineData("what Claude can change")]
    [InlineData("consent")]
    public void Agent_words_find_the_Privacy_page_first(string query) =>
        Assert.Equal(SettingsPage.Privacy, Top(query).Entry.Page);

    [Fact]
    public void MCP_still_finds_the_Claude_Code_folders_first() =>
        Assert.Equal((SettingsPage.ClaudeCode, SettingsIndex.Folders), (Top("MCP").Entry.Page, Top("MCP").Entry.Anchor));
}
```

In `windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs`:
- In `PageTitle_is_pinned`'s data, after `    [InlineData(SettingsPage.Alerts, "Alerts")]`, add `    [InlineData(SettingsPage.Privacy, "Privacy")]`.
- Replace

```csharp
        Assert.Equal(["Privacy"], SettingsNames.TitlesToCome);   // Desk Look, Watchers, Launcher landed in slice 3; Mods & Widgets in slice 4
```

with

```csharp
        Assert.Empty(SettingsNames.TitlesToCome);   // Desk Look, Watchers, Launcher landed in slice 3; Mods & Widgets in slice 4; Privacy in slice 6
```

- Replace `    [InlineData("Open Settings, Privacy.", "no page \"Privacy\" after \"Settings, \"")]` with `    [InlineData("Open Settings, Updates.", "no page \"Updates\" after \"Settings, \"")]`. Privacy is a page now; Updates is a section, not a page.
- In `PathProblems_passes_real_places`' data, after `    [InlineData("Settings, Claude Usage, Overview")]`, add `    [InlineData("Settings, Privacy, What leaves this PC")]`.

In `windows-dotnet/tests/Sanduhr.Tests/SettingsModsIndexTests.cs`, replace `        Assert.Equal([SettingsNames.PrivacyTitle], SettingsNames.TitlesToCome);` with `        Assert.Empty(SettingsNames.TitlesToCome);   // Privacy landed in slice 6`.

Create `windows-dotnet/tests/Sanduhr.App.Tests/PrivacyPageTests.cs`:

```csharp
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 6: the Privacy page sits after Alerts, shows every row of What leaves this PC and What Claude can change, and
/// its links land where they say, the catalog's never opening the catalog.</summary>
[Collection("SettingsWindow")]
public sealed class PrivacyPageTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Svm, WidgetViewModel Widget, NoActivate Guard) Open()
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
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

    private static DependencyObject PageOf(SettingsWindow window, SettingsPage page) =>
        (DependencyObject)((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == page.ToString()).Content;

    private static Button Link(DependencyObject root, string spoken) => Below<Button>(root).Single(b => b.IsVisible && Spoken(b) == spoken);

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    [Fact]
    public void Privacy_sits_after_Alerts_and_lands_on_its_sections() => AppHost.Run(() =>
    {
        var (window, _, widget, guard) = Open();
        try
        {
            var tags = ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Select(t => (string)t.Tag).ToList();
            Assert.Equal(tags.IndexOf(nameof(SettingsPage.Alerts)) + 1, tags.IndexOf(nameof(SettingsPage.Privacy)));
            foreach (var anchor in new[] { SettingsIndex.Network, SettingsIndex.Agents })
            {
                window.SelectPage(SettingsPage.Privacy, anchor);
                Settle(window, guard);
                Assert.Equal(new SettingsLink(SettingsPage.Privacy, anchor), window.Place);
            }
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void Every_destination_is_on_the_page_with_its_link() => AppHost.Run(() =>
    {
        var (window, _, widget, guard) = Open();
        try
        {
            window.SelectPage(SettingsPage.Privacy, SettingsIndex.Network);
            Settle(window, guard);
            var page = PageOf(window, SettingsPage.Privacy);
            var texts = Below<TextBlock>(page).Where(t => t.IsVisible).Select(t => t.Text).ToList();
            foreach (var row in NetworkDisclosure.All)
            {
                Assert.Contains(row.Destination, texts);
                Assert.NotNull(Link(page, row.OffLinkName));
            }
            Assert.Contains(SettingsNames.NothingElseLeaves, texts);
            Assert.Contains(SettingsNames.NetWebView2Note, texts);
            Assert.Contains(SettingsNames.NetYourBrowserNote, texts);

            Click(Link(page, NetworkDisclosure.Find("open-meteo")!.OffLinkName));
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Weather, SettingsIndex.WeatherPlace), window.Place);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void The_catalog_row_lands_on_the_installed_list_and_fetches_nothing() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            window.SelectPage(SettingsPage.Privacy, SettingsIndex.Network);
            Settle(window, guard);
            Click(Link(PageOf(window, SettingsPage.Privacy), NetworkDisclosure.Find("widget-catalog")!.OffLinkName));
            Settle(window, guard);

            Assert.Equal(new SettingsLink(SettingsPage.Mods, SettingsIndex.ModList), window.Place);
            Assert.False(svm.ModsPage.CatalogOpen);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void A_consent_changed_on_its_page_shows_on_the_next_visit() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            window.SelectPage(SettingsPage.Privacy, SettingsIndex.Agents);
            Settle(window, guard);
            Assert.Equal(SettingsNames.AgentOff, svm.Privacy.Agents[0].State);

            window.SelectPage(SettingsPage.Watchers, SettingsIndex.Watchers);
            Settle(window, guard);
            svm.Desk.WatchersAgents = true;   // the Watchers page's switch
            window.SelectPage(SettingsPage.Privacy, SettingsIndex.Agents);
            Settle(window, guard);

            Assert.Equal(SettingsNames.AgentOn, svm.Privacy.Agents[0].State);
            Assert.Contains(Below<TextBlock>(PageOf(window, SettingsPage.Privacy)), t => t.IsVisible && t.Text == SettingsNames.AgentOn);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void Every_consent_is_on_the_page_and_the_foot_links_to_About() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            window.SelectPage(SettingsPage.Privacy, SettingsIndex.Agents);
            Settle(window, guard);
            var page = PageOf(window, SettingsPage.Privacy);
            var texts = Below<TextBlock>(page).Where(t => t.IsVisible).Select(t => t.Text).ToList();
            foreach (var row in svm.Privacy.Agents)
            {
                Assert.Contains(row.Name, texts);
                Assert.NotNull(Link(page, row.LinkName));
            }
            Assert.Contains(svm.Privacy.McpStatus, texts);
            Assert.NotNull(Link(page, svm.Privacy.McpStatusLinkName));

            Click(Link(page, svm.Privacy.AboutLinkName));
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.About, SettingsIndex.Where), window.Place);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void The_privacy_policy_opens_through_the_windows_one_link_command() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            window.SelectPage(SettingsPage.Privacy, SettingsIndex.Network);
            Settle(window, guard);
            var policy = Link(PageOf(window, SettingsPage.Privacy), SettingsNames.ReadPrivacyPolicy);
            // Never executed here: it would open a browser.
            Assert.Same(svm.OpenLinkCommand, policy.Command);
            Assert.Equal(NetworkDisclosure.PrivacyPolicyUrl, policy.CommandParameter);
        }
        finally { window.Close(); widget.Dispose(); }
    });
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsSlice6IndexTests > %TEMP%\slice6-t7-red.log 2>&1`
Expected: the build FAILS with `'SettingsPage' does not contain a definition for 'Privacy'`.

- [ ] **Step 3: The page, its place in the sidebar and the index**

In `windows-dotnet/src/Sanduhr.Core/WhatsNew.cs`, replace `Watchers, Launcher }` (the end of `public enum SettingsPage`) with `Watchers, Launcher, Privacy }`. It goes at the end, so no value the app or a card stored moves.

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`:
- Replace `        new(null, [SettingsPage.General, SettingsPage.Accounts, SettingsPage.Alerts]),` with `        new(null, [SettingsPage.General, SettingsPage.Accounts, SettingsPage.Alerts, SettingsPage.Privacy]),`.
- Replace

```csharp
    // ---- Page titles added by Settings v2. Desk Look, Watchers and Launcher are pages since slice 3, Mods & Widgets since slice 4;
    // Privacy (slice 6) is still to come.
```

with

```csharp
    // ---- Page titles added by Settings v2. Desk Look, Watchers and Launcher are pages since slice 3, Mods & Widgets since slice 4,
    // Privacy since slice 6.
```

- Replace `    public static IReadOnlyList<string> TitlesToCome { get; } = [PrivacyTitle];` with `    public static IReadOnlyList<string> TitlesToCome { get; } = [];`.
- In `PageTitle`, after `        SettingsPage.Alerts => "Alerts",`, add `        SettingsPage.Privacy => PrivacyTitle,`.

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`:
- Replace `    public const string SignInReminders = "signin-reminders";` with:

```csharp
    public const string SignInReminders = "signin-reminders";
    // Privacy (slice 6): Streamer mode's anchors come first on the page in slice 7
    public const string Network = "network";
    public const string Agents = "agents";
```

- Replace `        E(SettingsNames.SendTestAlert, SettingsPage.Alerts, TestAlert, ["test", "try"]),` with:

```csharp
        E(SettingsNames.SendTestAlert, SettingsPage.Alerts, TestAlert, ["test", "try"]),

        E(SettingsNames.WhatLeavesThisPc, SettingsPage.Privacy, Network,
            ["network", "internet", "online", "offline", "send", "sends", "upload", "analytics", "telemetry", "tracking", "claude.ai",
             "Open-Meteo", "GitHub", "Microsoft Store", "endpoint", "what leaves", "WebView2", "SmartScreen", "privacy policy"]),
        E(SettingsNames.WhatClaudeCanChange, SettingsPage.Privacy, Agents,
            ["agents", "agent", "consent", "consents", "permission", "permissions", "allow", "propose", "what can Claude do"]),
```

- In `PageSynonyms`, replace `        [SettingsPage.Alerts] = ["notification", "warning"],` with:

```csharp
        [SettingsPage.Alerts] = ["notification", "warning"],
        [SettingsPage.Privacy] = ["privacy", "what leaves this PC", "what Claude can change", "agents", "network"],
```

and replace `        [SettingsPage.About] = ["help", "version", "trademark", "privacy"],` with `        [SettingsPage.About] = ["help", "version", "trademark"],`.

Create `windows-dotnet/src/Sanduhr.App/Views/PrivacyPage.xaml`:

```xml
<UserControl x:Class="Sanduhr.App.Views.PrivacyPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:views="clr-namespace:Sanduhr.App.Views"
             xmlns:core="clr-namespace:Sanduhr.Core;assembly=Sanduhr.Core"
             Focusable="False">
    <!-- Settings, Privacy (spec, Privacy; WF6, WF10): what leaves this PC and what Claude can change. Hosted in SettingsWindow.
         DataContext: the PrivacyViewModel. Both lists are built from fixed Core lists, so neither is ever empty. -->
    <Border Background="{DynamicResource Sanduhr.Brush.Glass}" CornerRadius="8">
        <ScrollViewer VerticalScrollBarVisibility="Auto" Focusable="False">
            <ScrollViewer.Resources>
                <Style TargetType="ScrollBar" BasedOn="{StaticResource SlimThemeScrollBar}" />
            </ScrollViewer.Resources>
            <StackPanel x:Name="PageBody" Margin="20">
                <TextBlock Text="{x:Static core:SettingsNames.PrivacyTitle}" FontSize="14" FontWeight="SemiBold"
                           AutomationProperties.HeadingLevel="Level1"
                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,12" />
                <TextBlock Text="{x:Static core:SettingsNames.PrivacyLine}" FontSize="11" TextWrapping="Wrap"
                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,-8,0,12" />

                <!-- Slice 7: Streamer mode (streamer, streamer-allowlist) goes here, first on the page, with its divider below it. -->

                <!-- What leaves this PC (network): NetworkDisclosure.All, one card per row. -->
                <StackPanel views:SettingsAnchor.Id="network">
                    <TextBlock Text="{x:Static core:SettingsNames.WhatLeavesThisPc}" FontSize="13" FontWeight="SemiBold"
                               AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                    <ItemsControl ItemsSource="{Binding Network}" Focusable="False"
                                  AutomationProperties.Name="{x:Static core:SettingsNames.WhatLeavesThisPc}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border BorderBrush="{DynamicResource Sanduhr.Brush.Border}" BorderThickness="0,0,0,1"
                                        Padding="0,0,0,10" Margin="0,0,0,10">
                                    <StackPanel>
                                        <TextBlock Text="{Binding Destination, Mode=OneWay}" FontSize="12" FontWeight="SemiBold"
                                                   AutomationProperties.HeadingLevel="Level3" TextWrapping="Wrap"
                                                   Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                        <TextBlock Text="{Binding Hosts, Mode=OneWay}" FontSize="11" FontFamily="Cascadia Code, Consolas"
                                                   TextWrapping="Wrap" Margin="0,2,0,4" Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                                        <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,0,0,2"
                                                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}">
                                            <Run Text="{x:Static core:SettingsNames.NetworkWhenLabel}" FontWeight="SemiBold" /><Run Text=" " /><Run Text="{Binding When, Mode=OneWay}" />
                                        </TextBlock>
                                        <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,0,0,2"
                                                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}">
                                            <Run Text="{x:Static core:SettingsNames.NetworkCarriesLabel}" FontWeight="SemiBold" /><Run Text=" " /><Run Text="{Binding Carries, Mode=OneWay}" />
                                        </TextBlock>
                                        <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,0,0,2"
                                                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}">
                                            <Run Text="{x:Static core:SettingsNames.NetworkOffLabel}" FontWeight="SemiBold" /><Run Text=" " /><Run Text="{Binding Off, Mode=OneWay}" />
                                        </TextBlock>
                                        <Button Style="{DynamicResource SettingsLinkButton}" HorizontalAlignment="Left" Margin="0,4,0,0"
                                                Content="{Binding OffLinkTitle, Mode=OneWay}" views:SettingsLinkTo.Link="{Binding OffLink, Mode=OneWay}"
                                                AutomationProperties.Name="{Binding OffLinkName, Mode=OneWay}" />
                                    </StackPanel>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <TextBlock Text="{x:Static core:SettingsNames.NothingElseLeaves}" FontSize="12" FontWeight="SemiBold" TextWrapping="Wrap"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,2,0,6" />
                    <TextBlock Text="{x:Static core:SettingsNames.NetWebView2Note}" FontSize="11" TextWrapping="Wrap"
                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,0,0,6" />
                    <TextBlock Text="{x:Static core:SettingsNames.NetYourBrowserNote}" FontSize="11" TextWrapping="Wrap"
                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,0,0,6" />
                    <!-- The policy opens through the window's one link command (decision 18): no new Process.Start. -->
                    <Button Style="{DynamicResource SettingsLinkButton}" HorizontalAlignment="Left"
                            Content="{x:Static core:SettingsNames.ReadPrivacyPolicy}"
                            AutomationProperties.Name="{x:Static core:SettingsNames.ReadPrivacyPolicy}"
                            ToolTip="{x:Static core:NetworkDisclosure.PrivacyPolicyUrl}"
                            Command="{Binding DataContext.OpenLinkCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                            CommandParameter="{x:Static core:NetworkDisclosure.PrivacyPolicyUrl}" />
                </StackPanel>

                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />

                <!-- What Claude can change (agents): AgentConsents.All, each with its state as read on this visit. -->
                <StackPanel views:SettingsAnchor.Id="agents">
                    <TextBlock Text="{x:Static core:SettingsNames.WhatClaudeCanChange}" FontSize="13" FontWeight="SemiBold"
                               AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                    <TextBlock Text="{x:Static core:SettingsNames.AgentsIntro}" FontSize="11" TextWrapping="Wrap"
                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,0,0,6" />
                    <TextBlock Text="{Binding McpStatus}" FontSize="11" TextWrapping="Wrap"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,2" />
                    <Button Style="{DynamicResource SettingsLinkButton}" HorizontalAlignment="Left" Margin="0,0,0,12"
                            Content="{Binding ClaudeCodeLinkTitle, Mode=OneWay}" views:SettingsLinkTo.Link="{Binding ClaudeCodeLink, Mode=OneWay}"
                            AutomationProperties.Name="{Binding McpStatusLinkName, Mode=OneWay}" />
                    <ItemsControl ItemsSource="{Binding Agents}" Focusable="False"
                                  AutomationProperties.Name="{x:Static core:SettingsNames.WhatClaudeCanChange}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <StackPanel Margin="0,0,0,12">
                                    <TextBlock Text="{Binding Name, Mode=OneWay}" FontSize="12" FontWeight="SemiBold" TextWrapping="Wrap"
                                               AutomationProperties.HeadingLevel="Level3"
                                               Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                    <TextBlock Text="{Binding State, Mode=OneWay}" FontSize="11" FontWeight="SemiBold" TextWrapping="Wrap"
                                               Foreground="{DynamicResource Sanduhr.Brush.Accent}" Margin="0,2,0,2" />
                                    <TextBlock Text="{Binding Caption, Mode=OneWay}" FontSize="11" TextWrapping="Wrap"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                    <Button Style="{DynamicResource SettingsLinkButton}" HorizontalAlignment="Left" Margin="0,4,0,0"
                                            Content="{Binding LinkTitle, Mode=OneWay}" views:SettingsLinkTo.Link="{Binding Link, Mode=OneWay}"
                                            AutomationProperties.Name="{Binding LinkName, Mode=OneWay}" />
                                </StackPanel>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>

                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />

                <!-- Where settings live: a status line; its home is About (decision 12). -->
                <TextBlock Text="{x:Static core:SettingsNames.PrivacyWhereLine}" FontSize="11" TextWrapping="Wrap"
                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,0,0,2" />
                <Button Style="{DynamicResource SettingsLinkButton}" HorizontalAlignment="Left"
                        Content="{Binding AboutLinkTitle, Mode=OneWay}" views:SettingsLinkTo.Link="{Binding AboutLink, Mode=OneWay}"
                        AutomationProperties.Name="{Binding AboutLinkName, Mode=OneWay}" />
            </StackPanel>
        </ScrollViewer>
    </Border>
</UserControl>
```

Create `windows-dotnet/src/Sanduhr.App/Views/PrivacyPage.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace Sanduhr.App.Views;

/// <summary>Settings, Privacy (Settings v2, slice 6).</summary>
public partial class PrivacyPage : UserControl
{
    public PrivacyPage() => InitializeComponent();
}
```

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`, replace the line

```xml
                <!-- Claude Usage (WS-C; Settings v2, slice 5): Meters / Overview / Trends / Sessions over the meter history,
```

with

```xml
                <!-- Settings, Privacy (slice 6): what leaves this PC and what Claude can change (WF6, WF10). Slice 7 adds Streamer mode at its top. -->
                <TabItem Header="Privacy" Tag="Privacy" AutomationProperties.Name="Privacy" DataContext="{Binding Privacy}">
                    <views:PrivacyPage />
                </TabItem>

                <!-- Claude Usage (WS-C; Settings v2, slice 5): Meters / Overview / Trends / Sessions over the meter history,
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`:
- Replace `    public WidgetPageViewModel WidgetPage { get; }` with:

```csharp
    public WidgetPageViewModel WidgetPage { get; }

    /// <summary>Settings, Privacy (Settings v2, slice 6): what leaves this PC and what Claude can change.</summary>
    public PrivacyViewModel Privacy { get; }
```

- Replace `        WidgetPage = new WidgetPageViewModel(widget);` with:

```csharp
        WidgetPage = new WidgetPageViewModel(widget);
        // Each consent is read where its own page writes it (slice 6, decision 10): the Desk pages' store, the widget's mcp_roots, the publish service.
        Privacy = new PrivacyViewModel(new SettingsStore(new Paths()), ClaudeCodeHost.For(widget), widget.LoadMcpRoots,
            () => widget.Publish?.Load().Enabled ?? false);
```

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs`, replace `        // Deep Work length is also written by the focus timer's own stepper: read it again on each visit.` with:

```csharp
        // Privacy reads every consent and the MCP install again on each visit; it holds no subscription, so Closed detaches nothing (slice 6).
        if (page == SettingsPage.Privacy)
            ViewModel.Privacy.Reload();

        // Deep Work length is also written by the focus timer's own stepper: read it again on each visit.
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSlice6IndexTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsModsIndexTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsReachIndexTests|FullyQualifiedName~NetworkDisclosureTests|FullyQualifiedName~AgentConsentsTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~WhatsNewTests|FullyQualifiedName~SettingsLinkTests" > %TEMP%\slice6-t7-core.log 2>&1`
Then: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~PrivacyPageTests|FullyQualifiedName~SettingsReachTests|FullyQualifiedName~SettingsWindowNamesTests" > %TEMP%\slice6-t7-app.log 2>&1`
Expected: PASS.
- `A_boost_ranks_first_only_once_its_section_is_live` now sees the privacy boosts live, and the streamer boosts still waiting.
- `The_sidebar_in_XAML_matches_the_names_table` finds the TabItem after Alerts.
- `Every_search_anchor_is_marked_in_its_pages_TabItem` reads `<views:PrivacyPage />`.

If `SettingsWindowNamesTests.NoPageReadsAsATypeNameOrARecord` (a `[DesktopFact]`) reads a row as its record text, check `NetworkRow.ToString` and `AgentRow.ToString` (decision 21).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/WhatsNew.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.App/Views/PrivacyPage.xaml windows-dotnet/src/Sanduhr.App/Views/PrivacyPage.xaml.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/tests/Sanduhr.Tests/SettingsSlice6IndexTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsModsIndexTests.cs windows-dotnet/tests/Sanduhr.App.Tests/PrivacyPageTests.cs
git commit -m "feat(settings-v2): the Privacy page, after Alerts: what leaves this PC and what Claude can change"
```

---

### Task 8: About, Updates; About's jump links

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/AboutUpdatesViewModel.cs`
- Modify App:
  - `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (`AboutUpdates`; the constructor takes `checkForUpdatesNow`, `isStoreBuild`).
  - `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (About's heading, jump links, the Updates section).
  - `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs` (About reloads Updates on visit).
  - `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (Check now through `_updateChecker`).
- Modify Core:
  - `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (the `updates` entry, `NameAnchors`, About's `JumpLinks`).
  - `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (`Table`).
- Modify tests: `windows-dotnet/tests/Sanduhr.Tests/NetworkDisclosureTests.cs` (drop Task 4's guard), `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice6IndexTests.cs`, `windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs`.
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/AboutUpdatesTests.cs`

**Interfaces:**
- Consumes:
  - From Task 1: `SettingsStore.LoadUpdateCheck`, `SaveUpdateCheck`.
  - From Task 2: `UpdateProbeResult`, `UpdateProbeStatus`, App's `_updateChecker`.
  - From Task 3: the Updates names.
  - From Task 4: `SettingsIndex.Updates`.
- Produces:
  - `public sealed partial class AboutUpdatesViewModel : ObservableObject`. Its constructor is `(SettingsStore store, bool isStoreBuild, Func<Task<UpdateProbeResult>>? checkNow)`. Its members are `CheckDaily`, `Status`, `StatusShown`, `IsStoreBuild`, `IsGitHubBuild`, `Caption`, `CheckNowCommand` and `Reload()`.
  - `SettingsViewModel.AboutUpdates`.
  - `SettingsViewModel`'s constructor gains `Func<Task<UpdateProbeResult>>? checkForUpdatesNow = null, bool? isStoreBuild = null`.
  - `SettingsIndex.JumpLinks(SettingsPage.About)`.

- [ ] **Step 1: Write the failing tests**

Append to `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice6IndexTests.cs`, inside the class:

```csharp
    [Theory]
    [InlineData("update")]
    [InlineData("updates")]
    public void Update_words_land_on_About_Updates_first(string query)
    {
        var top = Top(query);
        Assert.Equal((SettingsPage.About, SettingsIndex.Updates, -1), (top.Entry.Page, top.Entry.Anchor, top.Rank));
        Assert.Contains(SettingsIndex.Search(query), h => h.Entry.Page == SettingsPage.Accounts);   // "Update sign-in…" still found, below
    }

    [Fact]
    public void About_has_jump_links_to_its_sections_in_page_order()
    {
        var links = SettingsIndex.JumpLinks(SettingsPage.About);
        Assert.Equal([SettingsIndex.Version, SettingsIndex.Updates, SettingsIndex.Trademark, SettingsIndex.Where, SettingsIndex.Reset, SettingsIndex.Notices],
            links.Select(l => l.Anchor));
        Assert.Equal(SettingsNames.UpdatesSection, links[1].Title);
        Assert.Equal(SettingsIndex.Updates, SettingsIndex.NameAnchors[SettingsNames.CheckForUpdatesDaily]);
        Assert.Equal(SettingsIndex.Updates, SettingsIndex.NameAnchors[SettingsNames.CheckNow]);
    }
```

In `windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs`, after `    [InlineData(SettingsPage.Publish)]` (in `Every_jump_link_lands_on_its_section`'s data), add `    [InlineData(SettingsPage.About)]`.

Create `windows-dotnet/tests/Sanduhr.App.Tests/AboutUpdatesTests.cs`:

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

/// <summary>Settings v2, slice 6: About, Updates. The switch writes "update_check" where the checker reads it; Check now asks once with the
/// switch off and says what it found, aloud; the Store build shows its line instead.</summary>
[Collection("SettingsWindow")]
public sealed class AboutUpdatesTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Svm, WidgetViewModel Widget, NoActivate Guard) Open(
        Func<Task<UpdateProbeResult>>? checkNow = null, bool isStoreBuild = false)
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
            checkForUpdatesNow: checkNow, isStoreBuild: isStoreBuild);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        window.SelectPage(SettingsPage.About, SettingsIndex.Updates);
        Settle(window, guard);
        return (window, svm, widget, guard);
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

    private static T Named<T>(DependencyObject root, string name) where T : UIElement => Below<T>(root).Single(e => Spoken(e) == name);

    [Fact]
    public void The_switch_writes_update_check_where_the_checker_reads_it() => AppHost.Run(() =>
    {
        var (window, _, widget, guard) = Open();
        try
        {
            var box = Named<CheckBox>(window, SettingsNames.CheckForUpdatesDaily);
            Assert.True(box.IsVisible);
            Assert.True(box.IsChecked);
            box.IsChecked = false;
            // App builds the checker over new SettingsStore(new Paths()).LoadUpdateCheck: the same file.
            Assert.False(new SettingsStore(new Paths()).LoadUpdateCheck());
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void Check_now_asks_with_the_switch_off_and_says_what_it_found_aloud() => AppHost.Run(() =>
    {
        int asked = 0;
        var raised = new List<UIElement>();
        LiveRegion.Raised = raised.Add;
        var (window, svm, widget, guard) = Open(() =>
        {
            asked++;
            return Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate));
        });
        try
        {
            Named<CheckBox>(window, SettingsNames.CheckForUpdatesDaily).IsChecked = false;
            var button = Named<Button>(window, SettingsNames.CheckNow);
            Assert.True(button.IsEnabled);
            button.Command.Execute(null);
            Settle(window, guard);

            Assert.Equal(1, asked);
            Assert.Equal(SettingsNames.UpdateUpToDate, svm.AboutUpdates.Status);
            Assert.Contains(raised, e => e is TextBlock { Text: var text } && text == SettingsNames.UpdateUpToDate);
        }
        finally { LiveRegion.Raised = null; window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void A_found_update_names_its_version() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open(() => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.Ready, "4.0.1")));
        try
        {
            svm.AboutUpdates.CheckNowCommand.Execute(null);
            Settle(window, guard);
            Assert.Equal(SettingsNames.UpdateReady("4.0.1"), svm.AboutUpdates.Status);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void Without_a_checker_Check_now_is_dimmed_not_dead() => AppHost.Run(() =>
    {
        var (window, _, widget, _) = Open();
        try { Assert.False(Named<Button>(window, SettingsNames.CheckNow).IsEnabled); }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void The_Store_build_shows_its_line_instead_of_the_switch() => AppHost.Run(() =>
    {
        var (window, _, widget, _) = Open(isStoreBuild: true);
        try
        {
            Assert.DoesNotContain(Below<CheckBox>(window), c => c.IsVisible && Spoken(c) == SettingsNames.CheckForUpdatesDaily);
            Assert.DoesNotContain(Below<Button>(window), b => b.IsVisible && Spoken(b) == SettingsNames.CheckNow);
            Assert.Contains(Below<TextBlock>(window), t => t.IsVisible && t.Text == SettingsNames.StoreKeepsUpToDate);
        }
        finally { window.Close(); widget.Dispose(); }
    });
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~AboutUpdatesTests > %TEMP%\slice6-t8-red.log 2>&1`
Expected: the build FAILS with `'SettingsViewModel' does not contain a constructor that takes … 'checkForUpdatesNow'`, or with `'SettingsViewModel' does not contain a definition for 'AboutUpdates'`.

- [ ] **Step 3: The section, its view model, its index rows and its jump links**

Create `windows-dotnet/src/Sanduhr.App/ViewModels/AboutUpdatesViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.App.Updates;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// About, Updates (Settings v2, slice 6; WF6).
/// <list type="bullet">
/// <item><description>On a GitHub install: "Check for updates once a day" (settings.json "update_check", read by <c>UpdateChecker</c> and
/// <c>StoreUpdateService</c>), and "Check now", which asks once whatever the switch says (decision 7).</description></item>
/// <item><description>On the Store build: one line, "The Microsoft Store keeps Sanduhr up to date."</description></item>
/// </list>
/// Each new status line pulses <see cref="StatusShown"/>, so it is announced: <c>LiveRegion.Shown</c> raises only on the way to true. It
/// subscribes to nothing.
/// </summary>
public sealed partial class AboutUpdatesViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly Func<Task<UpdateProbeResult>>? _checkNow;
    private bool _loading;

    /// <summary>"Check for updates once a day" (settings.json "update_check").</summary>
    [ObservableProperty] private bool _checkDaily;
    /// <summary>What Check now found, in words; empty until it is pressed.</summary>
    [ObservableProperty] private string _status = "";
    /// <summary>The status line's live-region flag, pulsed for each new message.</summary>
    [ObservableProperty] private bool _statusShown;

    public bool IsStoreBuild { get; }
    public bool IsGitHubBuild => !IsStoreBuild;
    public string Caption => SettingsNames.UpdatesCaption;

    /// <param name="checkNow">App's <c>UpdateChecker.CheckNowAsync</c>; null in tests and the harness, which dims Check now.</param>
    public AboutUpdatesViewModel(SettingsStore store, bool isStoreBuild, Func<Task<UpdateProbeResult>>? checkNow)
    {
        _store = store;
        IsStoreBuild = isStoreBuild;
        _checkNow = checkNow;
        Reload();
    }

    /// <summary>Read the switch again (About was opened).</summary>
    public void Reload()
    {
        _loading = true;
        CheckDaily = _store.LoadUpdateCheck();
        _loading = false;
    }

    partial void OnCheckDailyChanged(bool value)
    {
        if (!_loading)
            _store.SaveUpdateCheck(value);
    }

    private bool CanCheckNow() => _checkNow is not null;

    [RelayCommand(CanExecute = nameof(CanCheckNow))]
    private async Task CheckNow()
    {
        Say(SettingsNames.UpdateChecking);
        var result = await _checkNow!();
        Say(result.Status switch
        {
            UpdateProbeStatus.UpToDate => SettingsNames.UpdateUpToDate,
            UpdateProbeStatus.Ready when result.Version is { } version => SettingsNames.UpdateReady(version),
            UpdateProbeStatus.NotInstalled => SettingsNames.UpdateNotInstalled,
            _ => SettingsNames.UpdateFailed,
        });
    }

    /// <summary>A new message, announced: the flag goes false first, so the live region raises again.</summary>
    private void Say(string text)
    {
        StatusShown = false;
        Status = text;
        StatusShown = true;
    }
}
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`:
- At the top, after `using Sanduhr.App.Services;`, add `using Sanduhr.App.Updates;`.
- Replace `        GlobalHotKeyService? hotKeys = null, Action? showWhatsNew = null, Action? showTour = null)` with:

```csharp
        GlobalHotKeyService? hotKeys = null, Action? showWhatsNew = null, Action? showTour = null,
        Func<Task<UpdateProbeResult>>? checkForUpdatesNow = null, bool? isStoreBuild = null)
```

- Replace `        AboutVersion = new AboutVersionViewModel(new SettingsStore(new Paths()));` with:

```csharp
        AboutVersion = new AboutVersionViewModel(new SettingsStore(new Paths()));
        // About, Updates (slice 6): App's checker reads "update_check" from this same file; the Store build shows its line instead.
        AboutUpdates = new AboutUpdatesViewModel(new SettingsStore(new Paths()), isStoreBuild ?? Sanduhr.Core.Startup.IsPackaged(), checkForUpdatesNow);
```

- Replace `    public AboutVersionViewModel AboutVersion { get; }` with:

```csharp
    public AboutVersionViewModel AboutVersion { get; }

    /// <summary>About, Updates (slice 6): Check for updates once a day, and Check now.</summary>
    public AboutUpdatesViewModel AboutUpdates { get; }
```

In `windows-dotnet/src/Sanduhr.App/App.xaml.cs`:
- Replace

```csharp
        var svm = new SettingsViewModel(_vm, () => RunSignInAsync(embedded: true), RunUpdateSignInAsync, _hotKeys,
            ShowWhatsNewFromMenu, ShowTour);
```

with

```csharp
        var svm = new SettingsViewModel(_vm, () => RunSignInAsync(embedded: true), RunUpdateSignInAsync, _hotKeys,
            ShowWhatsNewFromMenu, ShowTour, checkForUpdatesNow: CheckForUpdatesNowAsync);
```

- Replace `    private void ShowWhatsNewFromMenu()` with:

```csharp
    /// <summary>About, Updates' Check now: the launch checker, asked once whatever the switch says (slice 6, decision 7).</summary>
    private Task<UpdateProbeResult> CheckForUpdatesNowAsync() =>
        _updateChecker?.CheckNowAsync() ?? Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.Failed));

    private void ShowWhatsNewFromMenu()
```

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`:
- Replace

```xml
                            <TextBlock Text="About" FontSize="14" FontWeight="SemiBold"
                                       Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,12" />
```

with

```xml
                            <TextBlock Text="About" FontSize="14" FontWeight="SemiBold"
                                       AutomationProperties.HeadingLevel="Level1"
                                       Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,12" />
                            <!-- Six sections: jump links (spec, Keyboard and screen reader; slice 5 carry P5). -->
                            <views:SectionJumpLinks Page="About" />
```

- Replace

```xml
                                          AutomationProperties.Name="{x:Static core:SettingsNames.HideWhatsNewAfterUpdates}" />
                            </StackPanel>
```

with

```xml
                                          AutomationProperties.Name="{x:Static core:SettingsNames.HideWhatsNewAfterUpdates}" />
                            </StackPanel>

                            <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />

                            <!-- About, Updates (slice 6; WF6): the GitHub build's daily check and Check now; the Store build's one line. -->
                            <StackPanel views:SettingsAnchor.Id="updates" DataContext="{Binding AboutUpdates}">
                                <TextBlock Text="{x:Static core:SettingsNames.UpdatesSection}" FontSize="13" FontWeight="SemiBold"
                                           AutomationProperties.HeadingLevel="Level2"
                                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                                <StackPanel Visibility="{Binding IsGitHubBuild, Converter={StaticResource BoolVis}}">
                                    <CheckBox Style="{StaticResource ThemedCheckBox}"
                                              Content="{x:Static core:SettingsNames.CheckForUpdatesDaily}"
                                              IsChecked="{Binding CheckDaily, Mode=TwoWay}"
                                              AutomationProperties.Name="{x:Static core:SettingsNames.CheckForUpdatesDaily}" />
                                    <TextBlock Text="{Binding Caption, Mode=OneWay}" FontSize="11" TextWrapping="Wrap" Margin="0,6,0,8"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                                    <Button Style="{StaticResource FlatButton}" Padding="10,4" HorizontalAlignment="Left"
                                            Content="{x:Static core:SettingsNames.CheckNow}"
                                            AutomationProperties.Name="{x:Static core:SettingsNames.CheckNow}"
                                            AutomationProperties.HelpText="{x:Static core:SettingsNames.CheckNowCaption}"
                                            Command="{Binding CheckNowCommand}" />
                                    <TextBlock Text="{x:Static core:SettingsNames.CheckNowCaption}" FontSize="11" TextWrapping="Wrap" Margin="0,6,0,0"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                                    <TextBlock Text="{Binding Status}" FontSize="11" TextWrapping="Wrap" Margin="0,6,0,0"
                                               Foreground="{DynamicResource Sanduhr.Brush.Text}"
                                               Visibility="{Binding StatusShown, Converter={StaticResource BoolVis}}"
                                               views:LiveRegion.Shown="{Binding StatusShown}" />
                                </StackPanel>
                                <TextBlock Text="{x:Static core:SettingsNames.StoreKeepsUpToDate}" FontSize="11" TextWrapping="Wrap"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                                           Visibility="{Binding IsStoreBuild, Converter={StaticResource BoolVis}}" />
                            </StackPanel>
```

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs`, in `Tabs_SelectionChanged`, replace

```csharp
            ViewModel.AboutVersion.Reload();
            CommandManager.InvalidateRequerySuggested();
```

with

```csharp
            ViewModel.AboutVersion.Reload();
            ViewModel.AboutUpdates.Reload();
            CommandManager.InvalidateRequerySuggested();
```

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`:
- Replace `    /// <summary>About, Updates (slice 6). The constant comes first, for NetworkDisclosure's links; Task 8 marks it and indexes it.</summary>` with `    /// <summary>About, Updates (slice 6): Check for updates once a day and Check now.</summary>`.
- Replace

```csharp
        E("Version", SettingsPage.About, Version,
            ["version", "What's New", "Take the tour", "tour", "release", "update", "don't show", "after updates", SettingsNames.HideWhatsNewAfterUpdates]),
```

with

```csharp
        E("Version", SettingsPage.About, Version,
            ["version", "What's New", "Take the tour", "tour", "release", "update", "don't show", "after updates", SettingsNames.HideWhatsNewAfterUpdates]),
        E(SettingsNames.UpdatesSection, SettingsPage.About, Updates,
            ["update", "updates", "check for updates", SettingsNames.CheckForUpdatesDaily, SettingsNames.CheckNow, "new version", "upgrade",
             "auto-update", "Velopack", "GitHub", "Microsoft Store", "Restart to update"]),
```

- In `NameAnchors`, replace `        [SettingsNames.HideWhatsNewAfterUpdates] = Version,` with:

```csharp
        [SettingsNames.HideWhatsNewAfterUpdates] = Version,
        [SettingsNames.CheckForUpdatesDaily] = Updates,
        [SettingsNames.CheckNow] = Updates,
```

- In `JumpLinks`, replace `        SettingsPage.Publish => Links(page, Destination, Token, Homes, Schedule, Sample),` with:

```csharp
        SettingsPage.Publish => Links(page, Destination, Token, Homes, Schedule, Sample),
        // About (slice 6): six sections once Updates joins (slice 5 carry P5); labels are the index titles (owner taste 9).
        SettingsPage.About => Links(page, Version, Updates, Trademark, Where, Reset, Notices),
```

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, in `Table`, replace `        (HideWhatsNewAfterUpdates, SettingsPage.About),` with:

```csharp
        (HideWhatsNewAfterUpdates, SettingsPage.About),
        (CheckForUpdatesDaily, SettingsPage.About),
        (CheckNow, SettingsPage.About),
```

In `windows-dotnet/tests/Sanduhr.Tests/NetworkDisclosureTests.cs`, replace Task 4's guarded lines

```csharp
            if (SettingsIndex.Find(row.OffPage, row.OffAnchor) is not null || row.OffAnchor != SettingsIndex.Updates)
                Assert.Equal(new SettingsLink(row.OffPage, row.OffAnchor), SettingsIndex.Check(row.OffPage, row.OffAnchor));
```

with

```csharp
            Assert.Equal(new SettingsLink(row.OffPage, row.OffAnchor), SettingsIndex.Check(row.OffPage, row.OffAnchor));
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSlice6IndexTests|FullyQualifiedName~NetworkDisclosureTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsReachIndexTests|FullyQualifiedName~SettingsNamesTests" > %TEMP%\slice6-t8-core.log 2>&1`
Then: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~AboutUpdatesTests|FullyQualifiedName~DeskSplitPagesTests|FullyQualifiedName~AboutWhatsNewSwitchTests|FullyQualifiedName~Slice5PagesTests" > %TEMP%\slice6-t8-app.log 2>&1`
Expected: PASS.
- `A_boost_waits_for_its_section_then_ranks_first` still passes: it appends a second "updates" entry, and the boost takes the first.
- `Slice5PagesTests` still finds About's Version box.
- `DeskSplitPagesTests.Every_jump_link_lands_on_its_section(About)` lands on all six.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/AboutUpdatesViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs windows-dotnet/src/Sanduhr.App/App.xaml.cs windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.Tests/NetworkDisclosureTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsSlice6IndexTests.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs windows-dotnet/tests/Sanduhr.App.Tests/AboutUpdatesTests.cs
git commit -m "feat(settings-v2): About, Updates: Check for updates once a day and Check now; About gets jump links"
```

---

### Task 9: The What's New window's box reads the key again when it is brought back

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WhatsNewViewModel.cs` (`Reload`)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/WhatsNewWindow.xaml.cs` (`Activated`)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/WhatsNewBoxTests.cs`

**Interfaces:**
- Consumes: `SettingsStore.LoadWhatsNewHideAfterUpdates`; About's box (slice 5), which writes the same key.
- Produces: `WhatsNewViewModel.Reload()`.

- [ ] **Step 1: Write the failing test**

Create `windows-dotnet/tests/Sanduhr.App.Tests/WhatsNewBoxTests.cs`:

```csharp
using System.IO;
using System.Reflection;
using System.Windows;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 6 (slice 5 carry, the reverse of P14): About and the What's New window both write "whats_new_hide_after_updates". While
/// both are open, About's change must show in the window when the window is brought back, as the window's change shows on About. The window
/// is built without being shown, so nothing takes the foreground.
/// </summary>
[Collection("SettingsWindow")]
public sealed class WhatsNewBoxTests : AppWindowFixture
{
    private static WhatsNewWindow Unshown() =>
        (WhatsNewWindow)Activator.CreateInstance(typeof(WhatsNewWindow), BindingFlags.Instance | BindingFlags.NonPublic, null,
            [(Action<SettingsPage, string?>)((_, _) => { })], null)!;

    private static void Activate(Window window) =>
        typeof(Window).GetMethod("OnActivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [EventArgs.Empty]);

    [Fact]
    public void The_box_reads_the_key_again_when_the_window_is_brought_back() => AppHost.Run(() =>
    {
        var paths = ScratchPaths();
        var model = new WhatsNewViewModel([], "Version 4.0.0", new SettingsStore(paths));
        Assert.False(model.HideAfterUpdates);
        var window = Unshown();
        window.DataContext = model;
        try
        {
            new SettingsStore(paths).SaveWhatsNewHideAfterUpdates(true);   // About's box, through its own store
            Assert.False(model.HideAfterUpdates);                           // nothing re-read yet

            Activate(window);

            Assert.True(model.HideAfterUpdates);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Reading_again_writes_nothing()
    {
        var paths = ScratchPaths();
        var store = new SettingsStore(paths);
        store.SaveWhatsNewHideAfterUpdates(false);
        var model = new WhatsNewViewModel([], "Version 4.0.0", store);
        var before = File.ReadAllText(paths.SettingsFile);

        model.Reload();

        Assert.Equal(before, File.ReadAllText(paths.SettingsFile));
        Assert.False(model.HideAfterUpdates);
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~WhatsNewBoxTests > %TEMP%\slice6-t9-red.log 2>&1`
Expected: the build FAILS with `'WhatsNewViewModel' does not contain a definition for 'Reload'`.

- [ ] **Step 3: `Reload`, called on Activated**

In `windows-dotnet/src/Sanduhr.App/ViewModels/WhatsNewViewModel.cs`, replace

```csharp
    /// <summary>The tray glyph at 12% as a WPF image, for the Tray preview.</summary>
```

with

```csharp
    /// <summary>Read the key again (About may have changed it while this window was open; slice 6). Raises the change, writes nothing.</summary>
    public void Reload()
    {
        var hide = _store.LoadWhatsNewHideAfterUpdates();
        if (hide == _hideAfterUpdates)
            return;
        _hideAfterUpdates = hide;
        OnPropertyChanged(nameof(HideAfterUpdates));
    }

    /// <summary>The tray glyph at 12% as a WPF image, for the Tray preview.</summary>
```

In `windows-dotnet/src/Sanduhr.App/Views/WhatsNewWindow.xaml.cs`, in the private constructor, replace

```csharp
        _showMe = showMe;
        InitializeComponent();
```

with

```csharp
        _showMe = showMe;
        InitializeComponent();
        // About writes the same "Don't show What's New after updates" key: read it again when this window is brought back (slice 6),
        // as About reads it again when Settings is (slice 5, P14). The tour's model has no such box.
        Activated += (_, _) => (DataContext as WhatsNewViewModel)?.Reload();
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~WhatsNewBoxTests|FullyQualifiedName~AboutWhatsNewSwitchTests|FullyQualifiedName~AppWindowNamesTests" > %TEMP%\slice6-t9.log 2>&1`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/WhatsNewViewModel.cs windows-dotnet/src/Sanduhr.App/Views/WhatsNewWindow.xaml.cs windows-dotnet/tests/Sanduhr.App.Tests/WhatsNewBoxTests.cs
git commit -m "fix(settings-v2): the What's New window's box reads the key again when it is brought back"
```

---

### Task 10: The Done line, and the slice's pages by keyboard and screen reader

**Files:**
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/Slice6PagesTests.cs`
- Modify only if a test finds a gap: `Views/PrivacyPage.xaml`, About's Updates section in `Views/SettingsWindow.xaml`

**Interfaces:**
- Consumes: everything above.
- Produces the spec's Done line as tests:
  - "'privacy' in search opens the Privacy page", typed into the search box and opened with Enter.
  - "With the switch off, no update probe runs". The switch is unticked on About, and the checker App builds runs at launch and on the tick a day later.
  - The spoken-name, leak and landing checks for Privacy and About, Updates.

- [ ] **Step 1: Write the tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/Slice6PagesTests.cs`:

```csharp
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.Services;
using Sanduhr.App.Updates;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 6: the Done line, and Privacy and About, Updates read and land the way the spec says.</summary>
[Collection("SettingsWindow")]
public sealed class Slice6PagesTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Svm, WidgetViewModel Widget, NoActivate Guard) Open()
    {
        var (widget, _) = ScratchWidget("Personal");
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

    private static string Bare(string label) => label.Trim().TrimEnd('…').TrimEnd(':').Trim();

    private sealed class CountingProbe : IUpdateProbe
    {
        public int Asked;
        public Task<UpdateProbeResult> FindAsync()
        {
            Asked++;
            return Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate));
        }
    }

    /// <summary>Done (spec, slice 6): "privacy" in search opens the Privacy page, typed and opened with Enter, as a person does.</summary>
    [Fact]
    public void Privacy_in_search_opens_the_Privacy_page() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            Settle(window, guard);
            var tabs = (TabControl)window.FindName("Tabs");
            var box = (TextBox)tabs.Template.FindName("SearchBox", tabs);
            svm.Search.Query = "privacy";
            Settle(window, guard);
            Assert.Equal((SettingsPage.Privacy, SettingsIndex.Network), (svm.Search.First!.Page, svm.Search.First.Anchor));

            box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box)!, 0, Key.Enter)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Settle(window, guard);

            Assert.Equal(new SettingsLink(SettingsPage.Privacy, SettingsIndex.Network), window.Place);
            Assert.Equal(nameof(SettingsPage.Privacy), (string)((TabItem)tabs.SelectedItem).Tag);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    /// <summary>Done (spec, slice 6): with the switch off, no update probe runs. It is unticked on About, then the checker App builds (over
    /// the same store) runs at launch and on the tick two days later.</summary>
    [Fact]
    public void With_the_switch_off_no_update_probe_runs() => AppHost.Run(() =>
    {
        var (window, _, widget, guard) = Open();
        try
        {
            window.SelectPage(SettingsPage.About, SettingsIndex.Updates);
            Settle(window, guard);
            Below<CheckBox>(window).Single(c => Spoken(c) == SettingsNames.CheckForUpdatesDaily).IsChecked = false;

            var probe = new CountingProbe();
            var now = new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
            var stamp = Path.Combine(Root, "local", "last-update-check.txt");
            var checker = new UpdateChecker(new SettingsStore(new Paths()).LoadUpdateCheck, probe, stamp, (_, _) => { }, () => now);
            Assert.True(checker.RunIfDueAsync().IsCompleted);   // off: it returns before asking anything
            now = now.AddDays(2);
            checker.Tick();

            Assert.Equal(0, probe.Asked);
            Assert.Null(checker.Running);
            Assert.False(File.Exists(stamp));
        }
        finally { window.Close(); widget.Dispose(); }
    });

    /// <summary>Every button and box on Privacy and About, Updates is spoken with its visible label first (WF34), item templates included,
    /// and no name on either page leaks a type or a record (decision 21). Leaks are collected page by page (slice 5, P19).</summary>
    [Fact]
    public void Every_control_on_the_slice_6_pages_is_spoken_with_its_label_first() => AppHost.Run(() =>
    {
        var (window, _, widget, guard) = Open();
        try
        {
            var bad = new List<string>();
            var leaks = new List<string>();
            foreach (var (page, anchor, wholePage) in new (SettingsPage, string, bool)[]
            {
                (SettingsPage.Privacy, SettingsIndex.Network, true), (SettingsPage.About, SettingsIndex.Updates, false),
            })
            {
                window.SelectPage(page, anchor);
                Settle(window, guard);
                var tab = ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == page.ToString());
                DependencyObject root = wholePage ? (DependencyObject)tab.Content : SettingsAnchor.Find((DependencyObject)tab.Content, anchor)!;
                foreach (var b in Below<ButtonBase>(root).Where(b => b.IsVisible && b.Content is string))
                {
                    var name = Spoken(b);
                    if (name.Length == 0 || !name.StartsWith(Bare((string)b.Content), StringComparison.Ordinal))
                        bad.Add($"{page}: \"{b.Content}\" is spoken \"{name}\"");
                }
                leaks.AddRange(LeaksIn(NamesOf(window)).Select(l => $"{page}: {l}"));
            }
            Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
            Assert.True(leaks.Count == 0, string.Join(Environment.NewLine, leaks));
        }
        finally { window.Close(); widget.Dispose(); }
    });

    /// <summary>A landing on What leaves this PC puts the keyboard on the first row's link; on About, Updates, on the switch.</summary>
    [DesktopFact]
    public void A_landing_puts_the_keyboard_on_the_sections_first_control() => AppHost.Run(() =>
    {
        var (window, _, widget, guard) = Open();
        try
        {
            guard.RouteKeyboard();
            foreach (var (page, anchor, expected) in new[]
            {
                (SettingsPage.Privacy, SettingsIndex.Network, NetworkDisclosure.All[0].OffLinkName),
                (SettingsPage.About, SettingsIndex.Updates, SettingsNames.CheckForUpdatesDaily),
            })
            {
                window.SelectPage(page, anchor);
                Settle(window, guard);
                var focused = Keyboard.FocusedElement as UIElement;
                var name = focused is null ? "" : Spoken(focused);
                Assert.True(name == expected,
                    $"focus is on {focused?.GetType().Name ?? "nothing"} \"{name}\" on {window.Place}, not \"{expected}\"");
                guard.Check();
            }
        }
        finally { window.Close(); widget.Dispose(); }
    });
}
```

- [ ] **Step 2: Run them**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~Slice6PagesTests > %TEMP%\slice6-t10.log 2>&1`
Expected: PASS, the `[DesktopFact]` included. These pin behaviour built in Tasks 2 to 9, so a first run may pass at once. A failure names the control, its name or the place, and points at a XAML gap. Fix it in the page's XAML and run once more, never in a loop. If the focus test's message shows an outside process holding the foreground at the start, re-run its class once; if it passes, report it as environmental with both logs (slice 5's standing rule).

- [ ] **Step 3: Commit**

```bash
git add windows-dotnet/tests/Sanduhr.App.Tests/Slice6PagesTests.cs
git commit -m "test(settings-v2): slice 6's Done line, and Privacy and About, Updates spoken with their labels and landing the keyboard"
```

(Add the XAML file to the commit if Step 2 needed a fix.)

---

### Task 11: End to end: the harness lands the new anchors; the whole suite; the owner's checks

**Files:**
- Modify: `windows-dotnet/tools/Sanduhr.RenderHarness/SettingsPassScenario.cs` (two focus shots)
- Create (not committed with code): `.superpowers/sdd/2026-10-26-settings-v2-slice-6/tier2-checklist.md`, `.superpowers/sdd/carry-into-slice-7/slice-6/carry.md`

**Interfaces:**
- Consumes: everything above.
- Produces:
  - `anchors-landing.txt` lines for `privacy/network`, `privacy/agents` and `about/updates` at 800 and 600.
  - A green suite.
  - The Tier 2 hand-off, and slice 7's carry.

- [ ] **Step 1: The harness entries**

In `windows-dotnet/tools/Sanduhr.RenderHarness/SettingsPassScenario.cs`, replace

```csharp
        await Shot("about-flat-button", SettingsPage.About, () => ByName(c.Host, "What's New"));
```

with

```csharp
        await Shot("about-flat-button", SettingsPage.About, () => ByName(c.Host, "What's New"));
        await Shot("about-updates-switch", SettingsPage.About, () => ByName(c.Host, SettingsNames.CheckForUpdatesDaily));
        await Shot("privacy-link-button", SettingsPage.Privacy, () => ByName(c.Host, NetworkDisclosure.All[0].OffLinkName));
```

The pages loop builds one unit per sidebar page from `SettingsNames.Pages`, and the anchors table walks `SettingsIndex.Anchors`, so Privacy and About's `updates` join both with no other change.

- [ ] **Step 2: Build and run the harness's links part off screen, against a scratch profile**

Run: `dotnet build tools/Sanduhr.RenderHarness > %TEMP%\slice6-t11-build.log 2>&1` (from `windows-dotnet/`)
Expected: Build succeeded, 0 errors, no new warnings.

From the repo root, in PowerShell:

```powershell
$env:SANDUHR_DEV_PROFILE = Join-Path $env:TEMP "sanduhr-slice6-profile"
New-Item -ItemType Directory -Force $env:SANDUHR_DEV_PROFILE | Out-Null
dotnet run --project windows-dotnet/tools/Sanduhr.RenderHarness -- --scenario settings-pass --parts links --out (Join-Path $env:TEMP "sanduhr-slice6-out") *> (Join-Path $env:TEMP "slice6-t11-harness.log")
```

Expected: the run ends normally. Its `settings-pass: foreground window before X, after X` line shows the same handle twice.

- [ ] **Step 3: Check every new landing**

```powershell
$out = Join-Path $env:TEMP "sanduhr-slice6-out\settings-pass"
Select-String -Path (Join-Path $out "anchors-landing.txt") -Pattern "^(Privacy|About) / .* \| (800|600) \|" |
    Where-Object { $_.Line -notmatch "\| (fully in view|top in view)[^|]* \|[^|]*\| ok$" }
Select-String -Path (Join-Path $out "deeplinks.txt") -Pattern "(CARD|TOUR)-LANDING" |
    Where-Object { $_.Line -notmatch "at 800: (fully in view|top in view)[^;]*; ok" }
```

Expected: no output.
- `network`, `agents`, `version`, `updates`, `trademark`, `where`, `reset` and `notices` each land in view at 800 and 600.
- Every What's New and tour card still lands in view.

A printed line names the anchor or card and why. Fix the landing, then run Steps 2 and 3 once more. The evidence under `%TEMP%` is never committed.

- [ ] **Step 4: The whole suite and the contracts**

Run: `dotnet test Sanduhr.slnx > %TEMP%\slice6-t11-suite.log 2>&1` (from `windows-dotnet/`)
Expected: PASS across `Sanduhr.Tests`, `Sanduhr.App.Tests` and `Sanduhr.Mcp.Tests`, every `[DesktopFact]` included (this is a desktop session). Read failures from the log file. Then:

```bash
git diff release/4.0 --stat -- windows-dotnet/tests/Sanduhr.Mcp.Tests/TrustBoundaryTests.cs windows-dotnet/src/Sanduhr.Mcp
git diff release/4.0 -- windows-dotnet/tests/Sanduhr.Tests/SettingsKeysContractTests.cs
```

The first prints nothing: the MCP's link list, its pin and the MCP source are untouched. The second shows exactly one added line, `+        "update_check",`.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/tools/Sanduhr.RenderHarness/SettingsPassScenario.cs
git commit -m "test(settings-v2): the harness lands Privacy and About, Updates at 800 and 600 and shoots their focus rings"
```

- [ ] **Step 6: Hand the Tier 2 checks, the owner's calls and slice 7's carry over**

None of this is code, and none of it is for the agent: these checks need the real app on real screens and a real profile, which this plan never launches. Write `.superpowers/sdd/2026-10-26-settings-v2-slice-6/tier2-checklist.md` with these items, and post them to the owner:

1. **Tier 2 item 6 (WF6, network), on a GitHub install:**
   - With Weather placed but no city, and with a city but the piece hidden: no forecast call (a network monitor, or `%APPDATA%\Sanduhr` logs).
   - Open Mods & Widgets offline, then online: no catalog request until the Catalog section is opened.
   - The update stamp `%LOCALAPPDATA%\Sanduhr\last-update-check.txt` changes at most once a day.
   - Untick Check for updates once a day, relaunch after 24 hours: the stamp doesn't change. Check now still asks, and says what it found.
   - On the Store build: About shows "The Microsoft Store keeps Sanduhr up to date." and no switch.
2. **Privacy on a real profile.**
   - Read every row of What leaves this PC against what the app does on this PC.
   - The MCP line matches the Claude Code cards (installed in N of M).
   - The folder-reads row matches each card's "Let Claude read this folder's project totals".
   - Turn on Let Claude show watchers on Watchers, then return to Privacy: it reads On.
3. **Search and links.**
   - Type "privacy" and press Enter: Privacy opens at What leaves this PC.
   - "updates" opens About, Updates.
   - About's jump links reach all six sections.
   - Every "<Page> settings…" on Privacy lands on its section. The widget catalog's link never opens the Catalog.
4. **The What's New window and About together.** Open What's New from the tray and Settings, About. Tick the box on About, then click back into the What's New window: its box is ticked.
5. **Narrator on Privacy.** Each row's destination and each consent's name is a heading (level 3), and each link is spoken with its row ("Weather settings…, Open-Meteo, weather").
6. **The hosted policy.** 626labs.dev/privacy.html (`#privacy-sanduhr`) needs this slice's `docs/PRIVACY.md` additions: the update switch, the routine budget endpoint, WebView2, and "through Claude Code". Open a hub issue before the next Store submission, and curl-check the live page, as the 3.8 and 3.9 letters did.
7. **The owner's taste list** (above, "Owner's taste"), and one product question that is not taste. The hidden claude.ai window runs with WebView2's SmartScreen on, which the Privacy page now discloses. Turn it off there (`CoreWebView2Settings.IsReputationCheckingRequired = false`), or leave it as it is? A backlog task if yes.

Write `.superpowers/sdd/carry-into-slice-7/slice-6/carry.md`:

```markdown
# Carried from slice 6 into slice 7

## Privacy, What Claude can change, shows folder names
The folder-reads row's state names each Claude Code folder ("On for ~\.claude. Off for ~\.claude-work."), from
`PrivacyViewModel.Reload` through `SettingsNames.AgentFolders`. The spec's streamer reach table doesn't list Privacy.
With streamer mode on, mask those names as the Claude Code cards are masked, and add Privacy to the reach table's rows.

## The Privacy page's seat
`Views/PrivacyPage.xaml` holds `<!-- Slice 7: Streamer mode (streamer, streamer-allowlist) goes here, first on the page, with its divider below it. -->`.
Replace it verbatim. `SettingsIndex` gets `streamer` and `streamer-allowlist` before `network`.
Once the page has four sections, add `SettingsPage.Privacy => Links(page, …)` to `SettingsIndex.JumpLinks`, `<views:SectionJumpLinks Page="Privacy" />`
under the page's first line, and `[InlineData(SettingsPage.Privacy)]` to `DeskSplitPagesTests.Every_jump_link_lands_on_its_section`.
Also change `SettingsSlice6IndexTests.Privacy_holds_two_sections_and_no_jump_links`.

## NetworkDisclosure needs nothing from streamer mode
No row names a person, folder or host of theirs. Keep it that way: the scan test pins the rows' copy.
```

---

## Carried in, and where each went

Every row of `.superpowers/sdd/carry-into-slice-6/`, the slice 5 plan's "Carried, not in this slice" rows addressed to slice 6, and the lessons the slice 5 ledger, preflight and final review recorded:

| # | Carried item (source) | Where |
| --- | --- | --- |
| 1 | About: section jump links, now that About gains Updates (carry, preflight P5) | Task 8: the `JumpLinks` arm, `<views:SectionJumpLinks Page="About" />`, `[InlineData(SettingsPage.About)]` in `DeskSplitPagesTests`, and `About_has_jump_links_to_its_sections_in_page_order` |
| 2 | The What's New window's own box goes stale while About changes the key (carry, P14's reverse) | Task 9 (decision 14) |
| 3 | "About, Updates and the Privacy page … The Publish row there should quote this slice's `PublishRecordLine` facts" (slice 5 plan, Carried) | Task 3: `NetPublishCarries` names every field group `PublishRecordLine` names, in its order, and says the project names leave |
| 4 | Privacy copy true in only some states (slice 5 P2, P3, I2) | Decisions 2, 3, 6, 9 and 11. `McpInstalledStatus` and `AgentFolders` have a form for every count, pinned in Task 3. The Store line replaces the switch. |
| 5 | Empty-line live regions that never raised (P4) | Task 8: the Check now status collapses while empty and pulses `StatusShown` per message; `Check_now_asks_with_the_switch_off_and_says_what_it_found_aloud` counts the raise |
| 6 | Event subscriptions to the long-lived widget must detach on Settings Closed (slice 5 final review, checked list 2) | Global Constraints: this slice subscribes nothing. Privacy and About, Updates reload on visit, so `Closed` gains no `Detach`. |
| 7 | Edit anchors that weren't unique (P6, P7) | Global Constraints (`git grep -c -F`). Every anchor in this plan was checked against 8dae01fc. The one that occurs twice (`_log` in `StoreUpdateService.cs`) is avoided by name in Task 2. |
| 8 | App tests that leak WidgetViewModels (P21) | Global Constraints. Every new App test disposes its widget in `finally`. |
| 9 | Tests racing the tier registry (P8) | No test in this slice reads `TierModel`'s registry. Noted in Global Constraints. |
| 10 | Copy inline instead of in SettingsNames (P10) | Decision 20: every sentence of the rows, the agents, Updates and the page lives in `SettingsNames` (Task 3). `NetworkDisclosure` holds hosts and ids. |
| 11 | Search must land a new page's own name on it first: use `SettingsIndex.Boosts` (slice 5 live check 7) | Decision 16. The existing boosts go live; "privacy" leaves About's synonyms (Task 7). `Privacy_words_land_on_what_leaves_this_PC_first` and `Update_words_land_on_About_Updates_first` pin it. |
| 12 | Leak checks collected per page, not after the loop (P19) | Task 10, `Every_control_on_the_slice_6_pages_is_spoken_with_its_label_first` |
| 13 | A focus failure with an outside process in the foreground at the start is re-run once (slice 5 standing rule) | Task 10 Step 2 |

## Carried, not in this slice

- **Streamer mode, its allowlist and the 4.0 What's New cards:** slice 7. The Privacy page leaves a marked seat ("Merge surface with slice 7"), and Task 11 writes slice 7's carry: mask the folder names on Privacy, and give Privacy jump links once it has four sections.
- **The hosted policy at 626labs.dev and the next Store reviewer letter** need this slice's `docs/PRIVACY.md` additions: the owner, through a hub issue (Task 11, Tier 2 item 6).
- **SmartScreen in the hidden claude.ai window** is disclosed now; whether to turn it off is a product question for the owner (Task 11, Tier 2 item 7).
- **`docs/PRIVACY.md` beyond what this slice makes true:** the Mac sections and the wider document are left as they are. Only the sentences this slice's code changes are edited.
- **Slice 5's carries addressed to slice 8** (the LiveSetting-only status lines, the copy and comment sweeps, Reset's per-folder list) stay with slice 8.
- **The Tier 2 checks** (Task 11 Step 6): the owner, on real screens.

---

## Spec coverage (slice 6)

| Slice 6 bullet, Done item or Tier 2 item (spec) | Task |
| --- | --- |
| The Privacy page, a new sidebar page after Alerts (spec, Sidebar) | 7 |
| "What leaves this PC" from `NetworkDisclosure.All`: destination, when, what it carries, its off switch or link (spec, Privacy 2) | 3, 4, 6, 7 |
| The table's foot: "Nothing else leaves this PC…", and the link to `docs/PRIVACY.md`'s published page | 3 (amended copy, decision 3), 7 |
| "What Claude can change": every consent with its state and its home link; the MCP intro and the install's status (spec, Privacy 3) | 3, 5, 6, 7 |
| "Where settings live" on Privacy: a status line and "About settings…" (spec, Privacy 4) | 3, 7 (decision 12) |
| The `update_check` key, default on, in `SettingsStore` and `SettingsKeysContractTests` (spec, Settings keys) | 1 |
| "Check for updates once a day" and "Check now" on About, Updates; the Store build's line (spec, About) | 3, 8 |
| `UpdateChecker` honouring it | 1, 2 |
| Search: "privacy", "offline", "network", "send" → Privacy, network; "update", "updates" → About, Updates (spec, Search) | 7, 8 |
| `NetworkDisclosureTests`: each disclosed caller exists; a source scan maps every call site to a row, so a new outbound call fails CI until disclosed | 4 |
| An update-check off test | 2 (`With_the_switch_off_nothing_asks_at_launch_or_on_the_tick`, `The_Store_build_asks_the_Store_nothing_with_the_switch_off`), 10 |
| Done: "privacy" in search opens the Privacy page | 7 (index), 10 (typed and opened) |
| Done: with the switch off, no update probe runs | 2, 10 |
| Tier 2 item 6 | 11 Step 6 |
| `SettingsPage.Privacy` in `WhatsNew.cs` (spec's file list) | 7 |
| One key added; no MCP link list or MCP source change (spec, Settings keys; "No change to the MCP link list or `TrustBoundaryTests`") | Global Constraints, 11 Step 4 |
| WF6 and WF10 dispositions ("Privacy, What leaves this PC; update switch"; "What Claude can change") | 1 to 8 |

## Self-review (writing-plans checklist, run once)

1. **Spec coverage.** Every slice 6 bullet, the Done line and Tier 2 item 6 map to a task above. Of the spec's file list, every file is touched, and `SettingsStore.cs` gains `update_check` in Task 1. The extra files are listed with their reason.
2. **Placeholder scan.** There is no "TBD", no "add appropriate" and no "similar to Task N". The one forward reference, `SettingsIndex.Updates` used by Task 4 before Task 8 indexes it, is declared in Task 4 with a guard that Task 8 removes. Both steps spell out the exact text.
3. **Type consistency.**
   - `UpdateCheck.Due/ParseStamp/FormatStamp/Interval` and `UsageSchedule.Refresh` (Task 1) are what Tasks 2 and 3 use.
   - `IUpdateProbe.FindAsync`, `UpdateProbeResult`, `UpdateProbeStatus`, `UpdateChecker.RunIfDueAsync/Tick/CheckNowAsync/Running/DefaultStampPath` (Task 2) are what Tasks 8 and 10 use.
   - `StoreUpdateService(…, enabled:)` and `Query` (Task 2) are what its test uses.
   - The `Net*`, `Agent*` and Updates names (Task 3) are what Tasks 4, 5, 7 and 8 bind.
   - `NetworkRow` (`OffLinkTitle`, `OffLink`, `OffLinkName`) and `NetworkDisclosure.Find`, `YourBrowser`, `LocalOnly`, `PrivacyPolicyUrl` (Task 4) are what Tasks 6, 7, 10 and 11 use.
   - `AgentConsent` (`LinkTitle`, `Link`, `LinkName`) and `AgentConsents.State` and the id constants (Task 5) are what Task 6 uses.
   - `AgentRow`, `PrivacyViewModel.Reload/McpStatus/Agents/Network` and the link members (Task 6) are what Task 7 binds.
   - `AboutUpdatesViewModel.CheckDaily/Status/StatusShown/IsStoreBuild/IsGitHubBuild/Caption/CheckNowCommand/Reload` (Task 8) are what the XAML binds.
   - `WhatsNewViewModel.Reload` (Task 9) is what the window calls.
4. **Review Focus.** The five lines above each have their test in the owning task: Tasks 2, 2, 1 and 2, 7, 7.
