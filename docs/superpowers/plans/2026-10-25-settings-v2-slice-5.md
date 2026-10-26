# Settings v2 for Windows, Slice 5 ("Usage, Accounts, Alerts and Widget") Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every per-account, per-limit and widget-only choice gets one Settings home: the meter history becomes Claude Usage's first view (Meters) and the History page retires with its links still landing; Accounts gains "Data for this account" (the linked folder, "Clear this account's history…", "Remove account…" with what it removes said under it) and asks for a Label on the first sign-in; Alerts gains "Each limit", where a muted, hidden or hidden-until-reset limit can be seen and restored; the Widget sidebar group finally holds a Widget page (compact mode, graph style, keep on top, Deep Work length, limit order, and Just the Desk's "Show the widget"); Publish usage shows a sample of the next record built by the same function that builds the real one; About carries "Don't show What's New after updates".

**Architecture:** Core first. A new pure `LimitSettings` turns the widget's last answer and the three per-limit keys into `LimitRow`s and moves a limit in the order; a new pure `AccountLabels` names a new account ("Personal" first, then "Account N") and accepts or refuses a typed label; `JustTheDesk.End` is the one rule both an added account and "Show the widget" use. Then the names table gets the slice's copy. App: `SignInCoordinator` gets a test seam and asks for the first account's label; `WidgetViewModel` grows a small per-limit and widget-options API with a `LimitsChanged` event; the Alerts, Accounts, Claude Usage, Publish usage and About TabItems change in place in `SettingsWindow.xaml`; the Widget page is a new own-file `WidgetPage.xaml` with a `WidgetPageViewModel`. `SettingsIndex` and `SettingsNames` move with each page's XAML in the same commit. Last, the spec's Done line runs as tests, the harness reports where every new anchor lands, and the Tier 2 checks go to the owner.

**Tech Stack:** .NET 10, WPF, xUnit 2.9 (Xunit.SkippableFact for `[DesktopFact]`), CommunityToolkit.Mvvm 8.4 (two-argument `On…Changed` hooks are fine; this plan uses the one-argument form), the render harness (`tools/Sanduhr.RenderHarness`).

**Spec:** `docs/settings-v2-windows/spec.md`, "Shippable slices, Slice 5: Usage, Accounts, Alerts and Widget" (the authority on scope), with the page sections "Accounts (`accounts`)", "Alerts (`alerts`)", "Claude Usage (`claudeusage`)", "Publish usage (`publish`)", "Widget (`widget`, new; WF24, WF15)", "About (`about`)", and "Principles", "Sidebar", "Search", "Deep links and entry points" (Menus and Aliases), "Keyboard and screen reader", "Mapping", "Settings keys and migrations", "Outside Settings, same release" (core words) and the disposition rows for WF1, WF13, WF14, WF24, WF27 to WF30, WF32, WF35. Findings in `docs/settings-v2-windows/findings.md` (WF1, WF13, WF14, WF24, WF28, WF29, WF30, WF32, WF35; the widget-only list is inventory W18; Tier 2 items 7 and 8 under "Needs the live pass"). The executed slice 4 plan, `docs/superpowers/plans/2026-10-25-settings-v2-slice-4.md`, is the model for conventions and helpers. The records carried in are in `.superpowers/sdd/carry-into-slice-5/`.

## Spec questions decided in this plan

1. **The Widget page and "show the widget" share `SettingsPage.Widget`.** The enum value becomes a sidebar page (spec, Sidebar), but cards, the tour and links already use it to mean "show the widget itself". The rule, in Core as `SettingsIndex.ShowsTheWidget(link)`: `Widget` with **no anchor** shows the widget (the "paced" tour card, the "steady-sparklines" card, `--settings widget`, `widget#tray`, all unchanged); `Widget` **with an anchor** opens the Settings page (`widget#widget-options`, `widget#just-the-desk`, search, the sidebar). The five places that test `== SettingsPage.Widget` (App's `ShowMe` and `OpenSettingsLink`, `SettingsWindow.OpenLink`, `WhatsNewCardItem.ShowMeToolTip`, and `ParseLink`, which needs no change) use the rule.
2. **The What's New window keeps its own box.** The About section says the switch moved "from the What's New window, which keeps a link"; the Mapping row says "About, Version (window keeps it too, same key)". The Mapping row is the more specific and costs nothing: both boxes write `whats_new_hide_after_updates`, and About reads the key again each time the page is shown, so the two never disagree on screen.
3. **"Linked folders" is one line.** `account_folders` maps an account label to one folder path (`WidgetViewModel.SetAccountFolder`), so an account has at most one linked folder. The Data section reads "Linked to ~\.claude-work." or "No Claude Code folder is linked to this account.", with "Claude Code settings…" to `claudecode#folders`. Anchors are page-local and the cards sit inside an `ItemsControl`, so the link lands on the Folders section, not one card.
4. **Each limit's rows.** One row per limit in the widget's last answer, plus one per limit any of the three keys still names (`alert_muted_tiers`, `hidden_tiers`, `limits_hidden_until_reset`), so a muted or hidden limit that is absent today can still be restored. Rows follow the canonical tier order; keys the tier registry doesn't know get no row (a dynamic scoped limit appears after the first fetch registers it). "Show this limit" is unchecked while the limit is hidden either way; ticking it clears both (`WidgetViewModel.ShowTier`, as the card menu's Hidden limits item does). "Hidden until it resets" is a caption under the row, not a third control: the spec asks for it "shown and clearable", and Show this limit is the clear.
5. **Each limit sits between the two halves of the dimmed block.** "Warn for this limit" binds `IsEnabled` to "Enable alerts" (WF36); "Show this limit" hides a card and sends nothing, so it stays live with alerts off. `AlertsDependents` keeps the thresholds and the projection and reset switches; a second panel, `AlertsSoundDependents`, holds Sound to Send test alert. Each limit sits between them, in the spec's order.
6. **"Clear this account's history…" acts on the selected account only.** The All-accounts clear goes with the History page; Reset Sanduhr's "Meter history and the session archive" is the every-account erase. `UsageHistory.ClearEvery` stays in Core (Reset's path and its tests use the file layout it pins).
7. **Remove account says everything it removes.** `SignOutAccountAsync` deletes the sign-in, the history file, the account's folder link and its sign-in browser profile; today's confirmation names only the first two. The caption under the button and the confirmation share one sentence that names all four (privacy copy must be exactly true). The confirmation becomes `ThemedDialog.ShowConfirm` (no default button).
8. **The first sign-in's Label.** Every embedded sign-in asks through one prompt titled "Name this account" with the field "Label": "Personal" filled in for the first account, "Account N" after. Cancelling or an invalid label keeps the suggestion, as the second-account prompt does today: a captured sign-in is never thrown away. The manual key window's "Account name" becomes "Label" too (one name per control).
9. **"Show the widget" ends Just the Desk the way signing in does** (`JustTheDesk.End`, which `EndBySignIn` and the new `EndByShowWidget` both call): saved off, the reminder back only when Just the Desk silenced it, the Desk redrawn, the widget shown. With no account the widget then shows the first-run card, so it is never blank. One place runs it, `WidgetViewModel.EndJustTheDesk()`, and App's sign-in path calls the same method.
10. **The sample is the record Publish now would send now.** `UsagePublishService.BuildRecord(date)` is the one builder for the scheduled publish, Publish now, the MCP's `publish_usage` and "Show a sample…"; the sample's date is `UsagePublisher.DefaultDate(now)` (yesterday, local), the date Publish now uses. Building it sends nothing. The record also carries this PC's name and the meters' percentages and reset times, which the page's intro doesn't say; the new "What gets sent" line names every field group (privacy copy must be exactly true).
11. **"Publish usage's … empty states" are the sample's.** "Folders to share" got its empty-state line in slice 2 (`PublishHomesEmpty`). The sample says when no folder is ticked ("…so the record names no folder or project.") and when no endpoint is set.
12. **Meters is a Claude Usage view, inline.** The History TabItem's body moves into the Claude Usage TabItem as a fourth `CcSection` grid, keeping `x:Name="Chart"` and `HistoryEmptyText` (tests find them by name). Its anchors are `meters` (Subsection "Meters") and `meters-export`. `HistoryTabViewModel` keeps its name (the spec's file list names it) and becomes `ClaudeCodeTabViewModel.Meters` too. The constants `Chart` and `HistoryExport` stay for the aliases.
13. **Claude Usage opens on Meters.** The first pill is the default view (owner taste 1): every signed-in person has meters, and it no longer walks session logs the moment the page opens.
14. **The tier card's link and the 3.5.0 "limits-come-and-go" card land on `alerts#each-limit`.** `MenuAnchor(Alerts)` becomes `each-limit` (spec, Menus table); `TierCard.xaml.cs` already routes through it and needs no edit. The card ("Each limit can also stop warning you.") showed the widget because no Settings home existed; it now opens the home (owner taste 7).
15. **Limit order lists the limits the widget shows**, in its order (compact mode aside). Move up and Move down rewrite `tier_order` as the new shown order followed by every other known limit once, so a hidden limit keeps a place. A button that can't move is collapsed, not dimmed (no control that can't act).
16. **Compact mode on the Widget page drives the widget's in-memory state** (no key), through the widget's own `ToggleCompactCommand`. It is a Settings control for a session state, as the spec's mapping row says ("session").
17. **Graph style is a two-choice combo**, "Classic line" and "Horizon bands" (the strip's tooltips' words), not a cycle button.
18. **Deep Work length is a minutes box, 1 to 120** (the focus timer's own range), validated like Warn at and Urgent at: an out-of-range entry is not saved and a line says why.
19. **No jump links on this slice's pages.** Accounts has three sections and Widget two. Alerts and Publish usage have five each, but the spec gives jump links to slice 3 ("Heading stops and jump links"), slice 3 gave them to its own pages, and no slice lists Alerts, Publish usage or About; that gap goes to slice 8's accessibility sweep (listed in "Carried, not in this slice").
20. **Retired names arrive with the change that retires them**, so the scans stay green at every commit: "Clear history" (Task 7), "History", "History chart" and "History settings…" (Task 8), "Account name" (Task 4). All are whole-string retirements, because "meter history" and "Clear this account's history…" are live.
21. **New strings live in `SettingsNames`; moved strings may stay** (slice 4 lesson). The meter history's two empty lines and the Meters intro move into the table now (slice 2 carry T15), and the chart's dead fallback text is deleted.
22. **The Widget page is its own file; Accounts, Alerts, Claude Usage, Publish usage and About stay inline** in `SettingsWindow.xaml`. Existing tests find `AlertsOffLine`, `AlertsDependents`, `AccountsList`, `HistoryEmptyText`, `Chart` and `PublishHomesList` through `window.FindName`, which never reaches into a `UserControl`'s name scope.

## Owner's taste (decided with a default; each is one line to change)

1. **Claude Usage opens on Meters** the first time each Settings window shows it (decision 13).
2. **The label prompt's words:** title "Name this account", field "Label", "Personal" filled in (`SettingsNames.NameThisAccount`, `AccountLabelField`).
3. **Hidden until it resets is a caption** ("Hidden until it resets. Tick Show this limit to bring it back now."), not its own button (decision 4).
4. **Graph style's two names:** "Classic line" and "Horizon bands" (`GraphStyleClassic`, `GraphStyleHorizon`).
5. **Deep Work length is a minutes box**, not a list of presets (decision 18).
6. **The sample is the exact JSON** in a read-only window with a Close button, headed "Publish now would send this for 2026-10-24 to example.com." (`SampleHeader`).
7. **The "limits-come-and-go" What's New card opens Alerts, Each limit** instead of the widget (decision 14).
8. **The Widget page's options section is "Widget options" in search** (the page title is already "Widget"; a heading "Widget" under it would read twice).
9. **Remove account's caption** reads "Removes Work's sign-in from Windows Credential Manager, its meter history (history.Work.json), its Claude Code folder link and its sign-in browser profile. It can't be undone."
10. **The widget strip's tooltips stay as they are** ("Graph style: classic line (click to cycle)"; slice 1 final review T6 left them for this pass).
11. **The tour carries four of the seven core words** (slice 1 P14 asked for the owner's sign-off; the Widget page now opens with the fifth place one is defined: "The widget is the small floating window with your meters.").
12. **Linked folders' link lands on the Folders section**, not the one card (decision 3).

## Global Constraints

- **CRLF working tree, LF index.** Edit tracked files with the Edit tool only. **Never `sed -i`, ever** (slice 4 broke this twice). Never rewrite a tracked file wholesale with Write. New files may be created with Write. Renames are `git mv` followed by Edit.
- **Keys.** No `settings.json` key is renamed, removed or added in this slice (spec, "Settings keys and migrations": `update_check` and `streamer_mode` belong to slices 6 and 7). `SettingsKeysContractTests` is not touched. Every control this slice adds writes a key already in `Frozen`: `alert_muted_tiers`, `hidden_tiers`, `limits_hidden_until_reset`, `sparkline_style`, `pinned`, `focus_mode_duration`, `tier_order`, `whats_new_hide_after_updates`, `just_the_desk`, `just_the_desk_silenced_reminder`, `desk_sign_in_reminder`. Compact mode is session state, as today.
- **Trust boundary.** No file is added to `Sanduhr.Mcp.csproj`'s `Compile Include` list, and **no file on that list is edited** (`TierModel.cs` and `CcLogReader.cs` are on it: this plan reads them, never changes them). The new Core files `LimitSettings.cs` and `AccountLabels.cs`, and the changed `JustTheDesk.cs`, `SettingsNames.cs`, `SettingsIndex.cs` and `WhatsNewCards.cs`, are all off the list and stay off it. No Mcp source changes. `TrustBoundaryTests` stays green untouched.
- **`~\.sanduhr` is shared ground.** This slice adds no file there and deletes nothing there. The publish handoff files under it are read and written only by `UsagePublishService`'s existing code, unchanged.
- **Hands off the live app.** Never start, stop, kill or attach to `Sanduhr.exe`. If a build fails because a `Sanduhr.exe` holds a file, stop and ask the owner. **No test reads the real AppData**, LocalAppData, `~\.sanduhr`, a real Claude Code folder or Credential Manager: App tests derive from `AppWindowFixture` (scratch `SANDUHR_DEV_PROFILE`, `ClaudeCodeProfile` overrides, `MemoryCredentials`) and build widgets with `ScratchWidget(...)`. Never `DemoWidget()` in a new test; `ScratchWidget(...)` followed by `widget.StartDemo()` is the way to get sample limits over scratch folders. Never the real network: the sample test's endpoint is answered by a recording `HttpMessageHandler`.
- **Test hygiene.** Every App test class that derives from `AppWindowFixture`, builds a `SettingsWindow` or `SettingsViewModel`, uses `ScratchWidget`, or touches a process-wide static carries `[Collection("SettingsWindow")]`. A window is shown through `NoActivate.Show(window)`, and `guard.Check()` follows anything that moves focus; keyboard focus is routed with `guard.RouteKeyboard()` (WM_SETFOCUS only). `[DesktopFact]` is skippable and runs locally: it must pass before the slice is done, once per change, never re-run in a loop. Assert messages in focus tests name the focused element and the page.
- **Implementers keep full test logs.** Redirect each run to a file (`dotnet test … > %TEMP%\slice5-<task>-<n>.log 2>&1`) and read failures from the file; never grep a run's output down to a count and throw the rest away.
- **Run tests from `windows-dotnet/`.** `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~<Class>` for Core, `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~<Class>` for App, `dotnet test tests/Sanduhr.Mcp.Tests` for the server.
- **Names.** Every new visible or spoken string is a `SettingsNames` member or built from one, and passes `SettingsNames.PathProblems` and `RetiredNames`. Copy that moves unchanged may stay where it is. Buttons that open Settings read "<Page> settings…" (`SettingsNames.LinkTitle`); prose that names a place uses "Settings, <Page>, <Section>". A control's spoken name starts with its visible text (`Visible_labels_start_their_spoken_names`; per-row context follows after a comma: "Warn for this limit, Weekly - Opus").
- **Index with the XAML.** An anchor joins `SettingsIndex.Anchors` in the same commit that marks it in XAML (`Every_search_anchor_is_marked_in_its_pages_TabItem`, `Every_anchor_marked_in_XAML_is_in_the_index`), and a sidebar change lands with its TabItem (`The_sidebar_in_XAML_matches_the_names_table`).
- **Own-file pages and controls.** Pages are `UserControl`s named `<Name>Page` in `Views/` (the anchor scans read `<views:\w+Page>`). They use the window's styles through `DynamicResource` (`FlatButton`, `ThemedCheckBox`, `ThemedComboBox`, `Input`, `EmptyLine`), never a local style with `FocusVisualStyle="{x:Null}"` and no keyboard-focus trigger; focus rings come from the window's ring styles. **Every page `ScrollViewer` is `Focusable="False"`.** A `TextBlock` outside a template that only decorates (a separator dot, a glyph) is `controls:DecorativeText`. A `SectionExpander` toggles through its `HeaderSite` and carries a non-empty `AutomationProperties.HelpText` (this slice adds none).
- **Lists.** A `ListBox` gets an explicit `KeyboardNavigation.TabNavigation="Continue"` (its default, Once, hides every row's controls but the first). This slice's new lists are `ItemsControl`s with `Focusable="False"`, whose rows' controls are ordinary Tab stops, each with an empty-state line.
- **Live regions.** A line that must be announced raises `LiveRegionChanged` (`views:LiveRegion.Shown`); `LiveSetting` alone is silent. This slice's empty-state lines use the `EmptyLine` style, as every list since slice 2 does.
- **No control that can't act.** A control shown on a row acts, or it is collapsed (Limit order's end buttons) or dimmed with one line saying why (Warn for this limit with alerts off, under the existing "Alerts are off. Nothing below sends.").
- **Destructive confirms have no default button.** "Clear this account's history…" and "Remove account…" ask through `ThemedDialog.ShowConfirm` (Enter picks nothing, Esc cancels).
- **Privacy copy must be exactly true.** The Remove caption, the Clear question and the "What gets sent" line each name what the code does, no more and no less (decisions 7 and 10).
- **Branch.** Tasks run in numeric order on `feat/4.0-slice-5`, cut from `release/4.0` at 5117696. Slice 6 may run in parallel (see "Merge surface with slice 6"). The owner pre-approved executing without a plan-review stop.

## Review Focus

1. **Restoring a limit while the widget's own menu also changes it.** A person mutes "Weekly - Opus" from the card menu with Settings open on Alerts, then ticks "Warn for this limit" for it in Settings, then hides it until it resets from the card and ticks "Show this limit". Expected: each row follows the card menu at once, the box keeps keyboard focus after a toggle (the list syncs in place, never rebuilds under the person), and settings.json ends with the limit neither muted nor hidden. Pinned in Task 6 (`The_rows_follow_the_card_menu_and_keep_their_place`, `A_muted_limit_can_be_restored_from_Alerts`).
2. **Clearing or removing the right account, and only it.** With Work active and Personal selected in Accounts, "Clear this account's history…" empties Personal's file and leaves Work's; declining clears nothing; demo mode, whose sample accounts are also named Work and Personal, clears nothing even through `Execute`. Pinned in Task 7 (`Clearing_clears_the_selected_account_not_the_active_one`, `Declining_clears_nothing`, `Demo_mode_never_clears_a_real_history_file`).
3. **Show the widget with no account.** With Just the Desk on and no account, "Show the widget" brings the widget back showing the first-run card (never a blank window), restores the reminder only if Just the Desk silenced it, and the next launch starts with the widget shown. Pinned in Task 10 (`Show_the_widget_ends_Just_the_Desk_and_shows_the_first_run_card`, `A_reminder_the_person_had_turned_off_stays_off`, `The_next_launch_shows_the_widget`).
4. **The sample sends nothing and is the record.** "Show a sample…" with a ticked folder and an endpoint set posts nothing; the record Publish now then sends is the sample, field for field; with no folder ticked the sample says the record names no folder. Pinned in Task 11 (`The_sample_is_the_record_publish_now_sends`, `Building_the_sample_sends_nothing`, `With_no_folder_ticked_the_sample_says_so`).
5. **Old links land on the new homes.** `history`, `history#chart`, `history#export`, `--settings history`, the tier card's "Alerts settings…", `widget` (still the widget), `widget#widget-options` (the page), the 3.5.0 "limits-come-and-go" card, and search "export", "meter history", "csv", "limit", "compact" land where this plan says. Pinned in Task 8 (`Old_history_links_land_on_the_Meters_view`, the window-level `A_history_link_lands_on_the_meters_view_in_view`), Task 6 (`The_tier_card_and_the_limits_card_land_on_each_limit`) and Task 9 (`Widget_without_an_anchor_still_shows_the_widget`).

---

## Where this plan steps outside the spec's slice 5 file list

The spec's list is the core; these extra files deliver its own bullets:

- Core: new `LimitSettings.cs` (Each limit's rows and Limit order's moves, pure) and `AccountLabels.cs` (the label rule, pure); `WhatsNewCards.cs` (decision 14).
- App: `ViewModels/AlertsViewModel.cs` (Each limit), `ViewModels/ClaudeCodeTabViewModel.cs` (the Meters view), `ViewModels/PublishViewModel.cs` (the sample), `ViewModels/SettingsViewModel.cs`, `ViewModels/WhatsNewViewModel.cs` (the tooltip rule), `ViewModels/WidgetViewModel.cs` (the per-limit and widget-options API), new `ViewModels/WidgetPageViewModel.cs`, `ViewModels/AboutVersionViewModel.cs`, `ViewModels/LimitRowViewModel.cs`; `Views/SettingsWindow.xaml.cs`, `Views/ManualKeyWindow.xaml` (the Label field), new `Views/WidgetPage.xaml(.cs)` and `Views/PublishSampleWindow.xaml(.cs)`; `App.xaml.cs` (wiring and the link rule).
- Harness: `tools/Sanduhr.RenderHarness/SettingsPassScenario.cs` (the Meters view and the retired History page).
- Tests: new `tests/Sanduhr.Tests/LimitSettingsTests.cs`, `AccountLabelsTests.cs`, `SettingsSlice5IndexTests.cs`; new `tests/Sanduhr.App.Tests/SignInLabelTests.cs`, `WidgetLimitsTests.cs`, `EachLimitTests.cs`, `AccountsDataTests.cs` (replaces `HistoryClearTests.cs`), `ClaudeUsageMetersTests.cs`, `WidgetPageTests.cs`, `PublishSampleTests.cs`, `AboutWhatsNewSwitchTests.cs`, `Slice5PagesTests.cs`; changed `JustTheDeskTests.cs`, `SettingsNamesTests.cs`, `SettingsIndexTests.cs`, `SettingsLinkTests.cs`, `SettingsReachTests.cs`.

Not in this slice, though the spec mentions it nearby: WF32's "Add an account to link this folder." on the Claude Code card (shipped in slice 4, decision 21), streamer-mode masks on these pages and "Exports keep real names." (slice 7), the Privacy page and About's Updates section (slice 6), Reset's per-folder list (slice 8).

## Merge surface with slice 6

Slice 6 (Privacy, network and agents) may run beside this one. Slice 4 has merged, so it is no longer a neighbour.

| File | Slice 5 touches | Slice 6 likely touches | Risk |
| --- | --- | --- | --- |
| `Views/SettingsWindow.xaml` | Accounts, Alerts, Claude Usage, Publish usage TabItems; the History TabItem (deleted); a new Widget TabItem before Themes; About's Version section (one CheckBox) | a new Privacy TabItem after Alerts; About's new Updates section | Low: whole-block edits in different TabItems. In About, keep both: Version's box (5) above Updates (6). |
| `Core/SettingsNames.cs` | `Sidebar` lines for "Usage" and "Widget"; `Table`, `Retired`, new constants | `Sidebar`'s first group (Privacy), `TitlesToCome`, `Table`, new constants | Medium: different `Sidebar` lines; keep both sides' `Table` and `Retired` rows. |
| `Core/SettingsIndex.cs` | Accounts, Alerts, Claude Usage, Publish, Widget, About rows of `Anchors`, `NameAnchors`, `PageSynonyms`; `MenuAnchor`; `ShowsTheWidget` | Privacy rows, About `updates`, the boosts go live | Medium: line-per-entry; keep both. |
| `ViewModels/SettingsViewModel.cs`, `Views/SettingsWindow.xaml.cs`, `App.xaml.cs` | new properties, `Tabs_SelectionChanged` branches (Accounts, About), Closed detaches, wiring | Privacy page members, `UpdateChecker` | Low: additive. |
| `Core/WhatsNew.cs` | not touched | the `Privacy` enum value | None. |
| `tests/Sanduhr.Tests/SettingsKeysContractTests.cs` | not touched | `update_check` added | None. |

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Sanduhr.Core/LimitSettings.cs` (create) | `LimitRow`; `LimitSettings.Rows`, `Move`, `SavedOrder`. |
| `src/Sanduhr.Core/AccountLabels.cs` (create) | `AccountLabels.First`, `Suggested`, `IsValid`, `Accept`. |
| `src/Sanduhr.Core/JustTheDesk.cs` (modify) | `End`, and `EndByShowWidget` beside `EndBySignIn`. |
| `src/Sanduhr.Core/SettingsNames.cs` (modify) | Slice 5 copy; the sidebar's Usage and Widget groups; table rows; retired names. |
| `src/Sanduhr.Core/SettingsIndex.cs` (modify) | Anchors for Accounts, Alerts, Claude Usage, Publish usage, Widget and About; `MenuAnchor`; `ShowsTheWidget`. |
| `src/Sanduhr.Core/WhatsNewCards.cs` (modify) | "limits-come-and-go" lands on Alerts, Each limit. |
| `src/Sanduhr.App/Services/SignInCoordinator.cs` (modify) | The test seam; every embedded sign-in asks for a Label. |
| `src/Sanduhr.App/Services/UsagePublishService.cs` (modify) | `BuildRecord`, `SampleAsync`, the home and handler seams. |
| `src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (modify) | `LimitsChanged`, `LimitRows`, `ShownLimitOrder`, `SetShown`, `SetLimitOrder`, `SetSparklineStyle`, `SetPinned`, `EndJustTheDesk`. |
| `src/Sanduhr.App/ViewModels/LimitRowViewModel.cs` (create) | One Each limit row. |
| `src/Sanduhr.App/ViewModels/AlertsViewModel.cs` (modify) | Each limit's rows, synced in place. |
| `src/Sanduhr.App/ViewModels/AccountsViewModel.cs` (modify) | Data for this account; Remove's caption and confirm; the signed-out line. |
| `src/Sanduhr.App/ViewModels/HistoryTabViewModel.cs` (modify) | Clear leaves; names from the table; `Refresh`. |
| `src/Sanduhr.App/ViewModels/ClaudeCodeTabViewModel.cs` (modify) | The Meters view. |
| `src/Sanduhr.App/ViewModels/WidgetPageViewModel.cs` (create) | Settings, Widget. |
| `src/Sanduhr.App/ViewModels/PublishViewModel.cs` (modify) | Show a sample…. |
| `src/Sanduhr.App/ViewModels/AboutVersionViewModel.cs` (create) | About's What's New switch. |
| `src/Sanduhr.App/ViewModels/SettingsViewModel.cs`, `WhatsNewViewModel.cs` (modify) | Wiring; the tooltip rule. |
| `src/Sanduhr.App/Views/WidgetPage.xaml(.cs)`, `PublishSampleWindow.xaml(.cs)` (create) | The Widget page; the sample window. |
| `src/Sanduhr.App/Views/SettingsWindow.xaml(.cs)`, `ManualKeyWindow.xaml`, `HistoryChart.cs`, `App.xaml.cs` (modify) | TabItems, landing, wiring. |

---

### Task 1: `LimitSettings`: Each limit's rows and Limit order's moves, in Core

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/LimitSettings.cs`
- Test: create `windows-dotnet/tests/Sanduhr.Tests/LimitSettingsTests.cs`

**Interfaces:**
- Consumes: `TierModel.EffectiveOrder`, `TierModel.Label`, `TierModel.IsKnown`, `TierModel.ResolveOrder` (read only; `TierModel.cs` is MCP-linked and is not edited).
- Produces:
  - `public sealed record LimitRow(string Key, string Label, bool WarnOn, bool Shown, bool HiddenUntilReset)`
  - `public static IReadOnlyList<LimitRow> LimitSettings.Rows(IEnumerable<string> present, IEnumerable<string> hidden, IEnumerable<string> muted, IEnumerable<string> hiddenUntilReset)`
  - `public static IReadOnlyList<string>? LimitSettings.Move(IReadOnlyList<string> shown, string key, int delta)` (null when the key isn't shown, the step isn't one place, or the move leaves the list)
  - `public static IReadOnlyList<string> LimitSettings.SavedOrder(IReadOnlyList<string> shown, IEnumerable<string>? saved)`

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/LimitSettingsTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Settings v2, slice 5: Alerts, Each limit (WF13) and Widget, Limit order (WF24). The rows a person sees and the order a move
/// writes, from the widget's last answer and the three per-limit keys. Canonical tiers only: nothing here registers a dynamic tier.
/// </summary>
public class LimitSettingsTests
{
    private static readonly string[] None = [];

    [Fact]
    public void Rows_list_the_limits_in_the_last_answer_in_canonical_order_with_their_switches()
    {
        var rows = LimitSettings.Rows([TierModel.SevenDay, TierModel.FiveHour], None, None, None);

        Assert.Equal([TierModel.FiveHour, TierModel.SevenDay], rows.Select(r => r.Key));
        Assert.Equal("Session (5hr)", rows[0].Label);
        Assert.All(rows, r => Assert.True(r.WarnOn && r.Shown && !r.HiddenUntilReset));
    }

    [Fact]
    public void A_muted_or_hidden_limit_missing_from_the_last_answer_still_has_a_row_so_it_can_be_restored()
    {
        var rows = LimitSettings.Rows([TierModel.FiveHour], hidden: [TierModel.SevenDaySonnet], muted: [TierModel.SevenDayOpus], None);

        Assert.Equal([TierModel.FiveHour, TierModel.SevenDaySonnet, TierModel.SevenDayOpus], rows.Select(r => r.Key));
        var sonnet = rows.Single(r => r.Key == TierModel.SevenDaySonnet);
        Assert.False(sonnet.Shown);
        Assert.True(sonnet.WarnOn);
        var opus = rows.Single(r => r.Key == TierModel.SevenDayOpus);
        Assert.False(opus.WarnOn);
        Assert.True(opus.Shown);
    }

    [Fact]
    public void A_limit_hidden_until_it_resets_reads_not_shown_and_says_why()
    {
        var row = Assert.Single(LimitSettings.Rows(None, None, None, hiddenUntilReset: [TierModel.IguanaNecktie]));

        Assert.Equal(TierModel.IguanaNecktie, row.Key);
        Assert.False(row.Shown);
        Assert.True(row.HiddenUntilReset);
        Assert.True(row.WarnOn);
    }

    [Fact]
    public void A_key_the_registry_does_not_know_gets_no_row() =>
        Assert.Empty(LimitSettings.Rows(["no_such_tier"], ["also_unknown"], ["nor_this"], ["nope"]));

    [Fact]
    public void A_key_named_twice_is_one_row()
    {
        var row = Assert.Single(LimitSettings.Rows([TierModel.FiveHour, TierModel.FiveHour], [TierModel.FiveHour], [TierModel.FiveHour], None));

        Assert.False(row.Shown);
        Assert.False(row.WarnOn);
    }

    [Fact]
    public void Move_swaps_with_the_neighbour_and_refuses_to_leave_the_list()
    {
        IReadOnlyList<string> shown = [TierModel.FiveHour, TierModel.SevenDay, TierModel.SevenDaySonnet];

        Assert.Equal([TierModel.SevenDay, TierModel.FiveHour, TierModel.SevenDaySonnet], LimitSettings.Move(shown, TierModel.SevenDay, -1));
        Assert.Equal([TierModel.FiveHour, TierModel.SevenDaySonnet, TierModel.SevenDay], LimitSettings.Move(shown, TierModel.SevenDay, +1));
        Assert.Null(LimitSettings.Move(shown, TierModel.FiveHour, -1));
        Assert.Null(LimitSettings.Move(shown, TierModel.SevenDaySonnet, +1));
        Assert.Null(LimitSettings.Move(shown, TierModel.SevenDayOpus, +1));
        Assert.Null(LimitSettings.Move(shown, TierModel.SevenDay, 0));
        Assert.Null(LimitSettings.Move(shown, TierModel.FiveHour, 2));
    }

    [Fact]
    public void The_saved_order_is_the_shown_order_then_every_other_known_limit_once()
    {
        var order = LimitSettings.SavedOrder([TierModel.SevenDay, TierModel.FiveHour], saved: [TierModel.SevenDayOpus, "unknown"]);

        Assert.Equal([TierModel.SevenDay, TierModel.FiveHour, TierModel.SevenDayOpus], order.Take(3));
        Assert.Equal(order.Count, order.Distinct().Count());
        Assert.DoesNotContain("unknown", order);
        Assert.Equal(TierModel.ResolveOrder(null).Count, order.Count);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~LimitSettingsTests > %TEMP%\slice5-t1-red.log 2>&1`
Expected: build FAILS with `The name 'LimitSettings' does not exist in the current context`.

- [ ] **Step 3: Write `LimitSettings`**

Create `windows-dotnet/src/Sanduhr.Core/LimitSettings.cs`:

```csharp
namespace Sanduhr.Core;

/// <summary>One row of Settings, Alerts, Each limit (WF13): a limit the account has, or one a setting still names.</summary>
/// <param name="WarnOn">Not in "alert_muted_tiers".</param>
/// <param name="Shown">In neither "hidden_tiers" nor "limits_hidden_until_reset".</param>
/// <param name="HiddenUntilReset">In "limits_hidden_until_reset": it comes back on its own when it renews or refills.</param>
public sealed record LimitRow(string Key, string Label, bool WarnOn, bool Shown, bool HiddenUntilReset);

/// <summary>
/// Settings v2, slice 5: the per-limit settings in one place, and the widget's limit order. Pure: the widget's last answer and
/// the three settings.json keys come in as values, so the rules are testable without a widget. Not linked into the MCP.
/// </summary>
public static class LimitSettings
{
    /// <summary>
    /// One row per limit in <paramref name="present"/> (the widget's last answer) and per limit any of the three keys still
    /// names, so a muted or hidden limit that is absent today can be restored. Canonical order; a key the tier registry doesn't
    /// know gets no row (a dynamic scoped limit has one once a fetch has registered it).
    /// </summary>
    public static IReadOnlyList<LimitRow> Rows(IEnumerable<string> present, IEnumerable<string> hidden, IEnumerable<string> muted,
        IEnumerable<string> hiddenUntilReset)
    {
        var hiddenSet = new HashSet<string>(hidden, StringComparer.Ordinal);
        var mutedSet = new HashSet<string>(muted, StringComparer.Ordinal);
        var untilSet = new HashSet<string>(hiddenUntilReset, StringComparer.Ordinal);
        var named = new HashSet<string>(present, StringComparer.Ordinal);
        named.UnionWith(hiddenSet);
        named.UnionWith(mutedSet);
        named.UnionWith(untilSet);

        var rows = new List<LimitRow>();
        foreach (var key in TierModel.EffectiveOrder)
        {
            if (!named.Contains(key))
                continue;
            bool until = untilSet.Contains(key);
            rows.Add(new LimitRow(key, TierModel.Label(key), !mutedSet.Contains(key), !hiddenSet.Contains(key) && !until, until));
        }
        return rows;
    }

    /// <summary>
    /// <paramref name="shown"/> with <paramref name="key"/> moved one place (<paramref name="delta"/> -1 up, +1 down); null when the
    /// key isn't shown, the step is not one place, or the move would leave the list.
    /// </summary>
    public static IReadOnlyList<string>? Move(IReadOnlyList<string> shown, string key, int delta)
    {
        if (delta is not (-1 or 1))
            return null;
        int at = -1;
        for (int i = 0; i < shown.Count; i++)
        {
            if (shown[i] == key) { at = i; break; }
        }
        int to = at + delta;
        if (at < 0 || to < 0 || to >= shown.Count)
            return null;
        var moved = shown.ToList();
        (moved[at], moved[to]) = (moved[to], moved[at]);
        return moved;
    }

    /// <summary>What "tier_order" becomes after a move: the shown order, then every other known limit once in its resolved order,
    /// so a hidden limit keeps its place for when it shows again.</summary>
    public static IReadOnlyList<string> SavedOrder(IReadOnlyList<string> shown, IEnumerable<string>? saved)
    {
        var order = shown.Where(TierModel.IsKnown).Distinct(StringComparer.Ordinal).ToList();
        foreach (var key in TierModel.ResolveOrder(saved))
        {
            if (!order.Contains(key))
                order.Add(key);
        }
        return order;
    }
}
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~LimitSettingsTests|FullyQualifiedName~TierModelTests" > %TEMP%\slice5-t1-green.log 2>&1`
Expected: PASS. Then confirm the boundary: `git grep -n "LimitSettings" -- windows-dotnet/src/Sanduhr.Mcp` prints nothing.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/LimitSettings.cs windows-dotnet/tests/Sanduhr.Tests/LimitSettingsTests.cs
git commit -m "feat(settings-v2): each limit's row and the limit order's moves, in Core"
```

---

### Task 2: One rule for ending Just the Desk, and one for a new account's label

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/JustTheDesk.cs` (`EndBySignIn`)
- Create: `windows-dotnet/src/Sanduhr.Core/AccountLabels.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/JustTheDeskTests.cs`; create `windows-dotnet/tests/Sanduhr.Tests/AccountLabelsTests.cs`

**Interfaces:**
- Produces:
  - `public static (JustTheDeskState State, bool ReminderOn, bool Ended) JustTheDesk.End(JustTheDeskState state, bool reminderOn)`; `EndBySignIn` and the new `EndByShowWidget` (same signature) return `End(...)`.
  - `public static class AccountLabels { public const string First = "Personal"; public static string Suggested(IReadOnlyCollection<string> existing); public static bool IsValid(string? label); public static string Accept(string? typed, string suggested, IReadOnlyCollection<string> existing); }`

- [ ] **Step 1: Write the failing tests**

Append to the class in `windows-dotnet/tests/Sanduhr.Tests/JustTheDeskTests.cs`:

```csharp
    // ---- Settings v2, slice 5: Settings, Widget's "Show the widget" (WF32) ----

    [Fact]
    public void ShowingTheWidgetEndsItTheWaySigningInDoes()
    {
        foreach (bool reminderWasOn in new[] { true, false })
        {
            var chosen = JustTheDesk.Choose(JustTheDeskState.Off, reminderWasOn);
            var shown = JustTheDesk.EndByShowWidget(chosen.State, chosen.ReminderOn);
            Assert.Equal(JustTheDesk.EndBySignIn(chosen.State, chosen.ReminderOn), shown);
            Assert.True(shown.Ended);
            Assert.Equal(JustTheDeskState.Off, shown.State);
            Assert.Equal(reminderWasOn, shown.ReminderOn);
        }
    }

    [Fact]
    public void ShowingTheWidgetWhenItIsNotOnChangesNothing()
    {
        var r = JustTheDesk.EndByShowWidget(JustTheDeskState.Off, reminderOn: false);
        Assert.False(r.Ended);
        Assert.Equal(JustTheDeskState.Off, r.State);
        Assert.False(r.ReminderOn);
    }

    [Fact]
    public void AfterShowingTheWidgetTheNextLaunchShowsIt()
    {
        var chosen = JustTheDesk.Choose(JustTheDeskState.Off, reminderOn: true);
        var shown = JustTheDesk.EndByShowWidget(chosen.State, chosen.ReminderOn);
        Assert.False(JustTheDesk.WidgetStartsHidden(shown.State.On, hasAccount: false, deskOn: true));
        Assert.True(JustTheDesk.ShowsFirstRunCard(hasAccount: false, shown.State.On));
    }
```

Create `windows-dotnet/tests/Sanduhr.Tests/AccountLabelsTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 5 (WF30): the first sign-in asks for a Label with "Personal" filled in; later ones offer "Account N".</summary>
public class AccountLabelsTests
{
    [Fact]
    public void The_first_account_is_offered_Personal()
    {
        Assert.Equal("Personal", AccountLabels.Suggested([]));
        Assert.Equal(AccountLabels.First, AccountLabels.Suggested([]));
    }

    [Fact]
    public void A_later_account_is_offered_the_next_free_number()
    {
        Assert.Equal("Account 2", AccountLabels.Suggested(["Personal"]));
        Assert.Equal("Account 3", AccountLabels.Suggested(["Personal", "account 2"]));
    }

    [Theory]
    [InlineData("Work", true)]
    [InlineData("My_work-2", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("a/b", false)]
    [InlineData("ThirtyThreeCharactersIsTooLongXYZ", false)]
    [InlineData(null, false)]
    public void A_label_is_one_to_32_letters_digits_spaces_underscores_or_hyphens(string? label, bool valid) =>
        Assert.Equal(valid, AccountLabels.IsValid(label));

    [Fact]
    public void A_typed_label_is_trimmed_and_kept_when_valid_and_free() =>
        Assert.Equal("Work", AccountLabels.Accept("  Work ", "Personal", []));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad/name")]
    [InlineData("personal")]
    public void Cancelled_invalid_or_taken_keeps_the_suggestion(string? typed) =>
        Assert.Equal("Account 2", AccountLabels.Accept(typed, "Account 2", ["Personal"]));
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~JustTheDeskTests|FullyQualifiedName~AccountLabelsTests" > %TEMP%\slice5-t2-red.log 2>&1`
Expected: build FAILS with `'JustTheDesk' does not contain a definition for 'EndByShowWidget'` and `The name 'AccountLabels' does not exist`.

- [ ] **Step 3: Write the two rules**

In `windows-dotnet/src/Sanduhr.Core/JustTheDesk.cs`, replace `EndBySignIn` (its two-line summary and its body) with:

```csharp
    /// <summary>Ends Just the Desk. Not on: nothing changes. On: off, and the reminder returns only if Just the Desk silenced it.
    /// The one rule for both ways out: an added account and Settings, Widget's "Show the widget" (WF32).</summary>
    public static (JustTheDeskState State, bool ReminderOn, bool Ended) End(JustTheDeskState state, bool reminderOn)
    {
        if (!state.On) return (state, reminderOn, false);
        return (JustTheDeskState.Off, state.SilencedReminder || reminderOn, true);
    }

    /// <summary>Adding an account ends Just the Desk (<see cref="End"/>).</summary>
    public static (JustTheDeskState State, bool ReminderOn, bool Ended) EndBySignIn(JustTheDeskState state, bool reminderOn) =>
        End(state, reminderOn);

    /// <summary>Settings, Widget's "Show the widget" ends Just the Desk the same way signing in does (spec, Widget).</summary>
    public static (JustTheDeskState State, bool ReminderOn, bool Ended) EndByShowWidget(JustTheDeskState state, bool reminderOn) =>
        End(state, reminderOn);
```

Create `windows-dotnet/src/Sanduhr.Core/AccountLabels.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>
/// The name a new account is offered and the rule a typed one passes (Settings v2, slice 5; WF30). The first account is offered
/// "Personal", later ones the next free "Account N"; every sign-in asks, and a cancelled, invalid or taken label keeps the offer,
/// so a captured sign-in is never thrown away. The rule is <see cref="AccountStore"/>'s own label check.
/// </summary>
public static class AccountLabels
{
    /// <summary>The first account's offered label.</summary>
    public const string First = "Personal";

    private static readonly Regex Valid = new("^[A-Za-z0-9 _-]{1,32}$", RegexOptions.Compiled);

    /// <summary>"Personal" with no account yet, else the first "Account N" (from 2) no account uses, in any case.</summary>
    public static string Suggested(IReadOnlyCollection<string> existing)
    {
        if (existing.Count == 0)
            return First;
        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        for (int n = 2; ; n++)
        {
            var candidate = string.Format(CultureInfo.InvariantCulture, "Account {0}", n);
            if (!taken.Contains(candidate))
                return candidate;
        }
    }

    /// <summary>1 to 32 letters, digits, spaces, underscores or hyphens.</summary>
    public static bool IsValid(string? label) => label is not null && Valid.IsMatch(label);

    /// <summary>The label a sign-in saves: <paramref name="typed"/> trimmed when it is valid and no account uses it (in any
    /// case), else <paramref name="suggested"/>.</summary>
    public static string Accept(string? typed, string suggested, IReadOnlyCollection<string> existing)
    {
        var name = (typed ?? "").Trim();
        return IsValid(name) && !existing.Contains(name, StringComparer.OrdinalIgnoreCase) ? name : suggested;
    }
}
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~JustTheDeskTests|FullyQualifiedName~AccountLabelsTests|FullyQualifiedName~AccountStoreTests" > %TEMP%\slice5-t2-green.log 2>&1`
Expected: PASS (`SigningInEndsItAndBringsTheReminderBack` and its neighbours unchanged).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/JustTheDesk.cs windows-dotnet/src/Sanduhr.Core/AccountLabels.cs windows-dotnet/tests/Sanduhr.Tests/JustTheDeskTests.cs windows-dotnet/tests/Sanduhr.Tests/AccountLabelsTests.cs
git commit -m "feat(settings-v2): one rule ends Just the Desk for sign-in and Show the widget; one rule names a new account"
```

---

### Task 3: The slice's names

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (constants after `NoFolderInClaudeCode`, before the slice 3 block)
- Test: `windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs`

**Interfaces:**
- Produces (`public const string` unless shown):
  - Accounts: `NameThisAccount`, `AccountLabelField`, `AccountDataSection`, `LinkedFolders`, `ClearAccountHistory`, `ClearHistoryDetail`, `ClearHistoryConfirm`, `ClearHistoryCancel`, `HistoryNotCleared`, `RemoveAccount`, `RemoveAccountConfirm`, `RemoveAccountCancel`, `SignedOutAccountsLine`; `public static string LinkedFolderLine(string? folder)`, `ClearAccountHistoryNamed(string account)`, `ClearHistoryQuestion(string account)`, `HistoryCleared(string account)`, `RemoveAccountNamed(string account)`, `RemoveAccountQuestion(string account)`, `RemoveAccountDetail(string account)`.
  - Alerts: `EachLimitSection`, `EachLimitCaption`, `EachLimitListName`, `WarnForThisLimit`, `ShowThisLimit`, `HiddenUntilItResets`, `HiddenUntilResetCaption`, `EachLimitSignedOut`, `EachLimitNotYet`; `public static string WarnForLimit(string limit)`, `ShowLimit(string limit)`.
  - Claude Usage, Meters: `MeterHistorySection`, `MetersIntro`, `MeterHistorySignedOut`, `MeterHistoryEmpty`, `ExportMeterHistoryTitle`, `MeterHistoryDialogTitle`.
  - Widget: `WidgetLine`, `WidgetOptions`, `GraphStyleClassic`, `GraphStyleHorizon`, `DeepWorkLength`, `DeepWorkMinutes`, `DeepWorkRange`, `CompactModeCaption`, `LimitOrder`, `LimitOrderListName`, `JustTheDeskOn`, `ShowTheWidget`, `ShowTheWidgetCaption`; `public static string DeepWorkLengthSpoken { get; }`, `MoveLimitUp(string limit)`, `MoveLimitDown(string limit)`.
  - Publish usage: `PublishSampleSection`, `PublishRecordLine`, `ShowASample`, `SampleTitle`, `SampleNoFolders`, `SampleClose`, `SampleFailed`; `public static string SampleHeader(string date, string? host)`.
- Consumes: `DefineWidget`, `MoveUp`, `MoveDown`.
- No page title, sidebar group, table row or retired name changes here: they move with their pages (Tasks 4 and 6 to 12), so every scan stays green at every commit.

- [ ] **Step 1: Write the failing test**

Append to the class in `windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs`:

```csharp
    // ---- Settings v2, slice 5: Usage, Accounts, Alerts and Widget ----

    [Fact]
    public void Slice_5_copy_is_the_specs()
    {
        Assert.Equal("Claude Usage, Alerts and the meters fill in after you sign in.", SettingsNames.SignedOutAccountsLine);
        Assert.Equal("Sign in to start recording your meters. Sanduhr keeps 30 days on this PC.", SettingsNames.MeterHistorySignedOut);
        Assert.Equal("Sanduhr is in Just the Desk: the widget is hidden.", SettingsNames.JustTheDeskOn);
        Assert.Equal(("Show the widget", "Warn for this limit", "Show this limit", "Hidden until it resets"),
            (SettingsNames.ShowTheWidget, SettingsNames.WarnForThisLimit, SettingsNames.ShowThisLimit, SettingsNames.HiddenUntilItResets));
        Assert.Equal(("Clear this account's history…", "Remove account…", "Show a sample…", "Data for this account", "Linked folders"),
            (SettingsNames.ClearAccountHistory, SettingsNames.RemoveAccount, SettingsNames.ShowASample, SettingsNames.AccountDataSection,
             SettingsNames.LinkedFolders));
        Assert.Equal(("Each limit", "What gets sent", "Limit order", "Deep Work length"),
            (SettingsNames.EachLimitSection, SettingsNames.PublishSampleSection, SettingsNames.LimitOrder, SettingsNames.DeepWorkLength));
        Assert.Equal("The widget is " + SettingsNames.DefineWidget + ".", SettingsNames.WidgetLine);
        Assert.Equal(("Name this account", "Label"), (SettingsNames.NameThisAccount, SettingsNames.AccountLabelField));
    }

    [Fact]
    public void Row_names_lead_with_the_visible_label()
    {
        Assert.Equal("Warn for this limit, Weekly - Opus", SettingsNames.WarnForLimit("Weekly - Opus"));
        Assert.Equal("Show this limit, Weekly - Opus", SettingsNames.ShowLimit("Weekly - Opus"));
        Assert.Equal("Move up, Weekly - Opus", SettingsNames.MoveLimitUp("Weekly - Opus"));
        Assert.Equal("Move down, Weekly - Opus", SettingsNames.MoveLimitDown("Weekly - Opus"));
        Assert.Equal("Clear this account's history, Work", SettingsNames.ClearAccountHistoryNamed("Work"));
        Assert.Equal("Remove account, Work", SettingsNames.RemoveAccountNamed("Work"));
        Assert.Equal("Deep Work length, minutes", SettingsNames.DeepWorkLengthSpoken);
    }

    [Fact]
    public void What_Remove_and_Clear_say_is_everything_they_do()
    {
        // SignOutAccountAsync removes the sign-in, the history file, the folder link and the sign-in browser profile (decision 7).
        Assert.Equal("Removes Work's sign-in from Windows Credential Manager, its meter history (history.Work.json), its Claude Code folder link "
            + "and its sign-in browser profile. It can't be undone.", SettingsNames.RemoveAccountDetail("Work"));
        Assert.Equal("Remove Work from Sanduhr?", SettingsNames.RemoveAccountQuestion("Work"));
        Assert.Equal("Clear Work's meter history?", SettingsNames.ClearHistoryQuestion("Work"));
        Assert.Equal("Work's meter history is cleared.", SettingsNames.HistoryCleared("Work"));
        Assert.Equal("Linked to ~\\.claude-work.", SettingsNames.LinkedFolderLine("~\\.claude-work"));
        Assert.Equal("No Claude Code folder is linked to this account.", SettingsNames.LinkedFolderLine(null));
    }

    [Fact]
    public void The_sample_header_says_where_the_record_would_go()
    {
        Assert.Equal("Publish now would send this for 2026-10-24 to example.com.", SettingsNames.SampleHeader("2026-10-24", "example.com"));
        Assert.Equal("Publish now would send this for 2026-10-24, but no endpoint is set, so it would go nowhere yet.",
            SettingsNames.SampleHeader("2026-10-24", null));
    }

    [Fact]
    public void Slice_5_copy_uses_no_retired_name_and_no_bad_path()
    {
        foreach (var text in new[]
        {
            SettingsNames.SignedOutAccountsLine, SettingsNames.ClearHistoryDetail, SettingsNames.RemoveAccountDetail("Work"),
            SettingsNames.EachLimitCaption, SettingsNames.HiddenUntilResetCaption, SettingsNames.EachLimitSignedOut, SettingsNames.EachLimitNotYet,
            SettingsNames.MetersIntro, SettingsNames.MeterHistorySignedOut, SettingsNames.MeterHistoryEmpty, SettingsNames.WidgetLine,
            SettingsNames.CompactModeCaption, SettingsNames.DeepWorkRange, SettingsNames.JustTheDeskOn, SettingsNames.ShowTheWidgetCaption,
            SettingsNames.PublishRecordLine, SettingsNames.SampleNoFolders, SettingsNames.SampleFailed, SettingsNames.SampleHeader("2026-10-24", null),
        })
        {
            Assert.Empty(SettingsNames.RetiredNames(text));
            Assert.Empty(SettingsNames.PathProblems(text));
            Assert.Empty(SettingsNames.UnknownLinks(text));
        }
    }
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsNamesTests > %TEMP%\slice5-t3-red.log 2>&1`
Expected: build FAILS with `'SettingsNames' does not contain a definition for 'SignedOutAccountsLine'`.

- [ ] **Step 3: Add the names**

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, after the line `public const string NoFolderInClaudeCode = "No Claude Code folder found. Add one with Add folder…";`, add:

```csharp

    // ---- Settings v2, slice 5: Accounts (spec, Accounts; WF1, WF27, WF29, WF30, WF32) ----

    /// <summary>The label prompt every embedded sign-in shows, and the manual key window's field (one name: "Label").</summary>
    public const string NameThisAccount = "Name this account";
    public const string AccountLabelField = "Label";
    /// <summary>Accounts, Data for this account: what Sanduhr keeps for the selected account.</summary>
    public const string AccountDataSection = "Data for this account";
    public const string LinkedFolders = "Linked folders";
    /// <summary>An account links at most one Claude Code folder (settings.json "account_folders" maps a label to one path).</summary>
    public static string LinkedFolderLine(string? folder) =>
        folder is null ? "No Claude Code folder is linked to this account." : $"Linked to {folder}.";
    /// <summary>Accounts, Data: the one per-account erase (WF1), spoken with the account.</summary>
    public const string ClearAccountHistory = "Clear this account's history…";
    public static string ClearAccountHistoryNamed(string account) => $"Clear this account's history, {account}";
    public static string ClearHistoryQuestion(string account) => $"Clear {account}'s meter history?";
    public const string ClearHistoryDetail =
        "This permanently removes the 30 days of meter history Sanduhr keeps for this account on this PC. The sparklines and the meter history start again from the next check. Its sign-in is kept.";
    public const string ClearHistoryConfirm = "Clear";
    public const string ClearHistoryCancel = "Cancel";
    public static string HistoryCleared(string account) => $"{account}'s meter history is cleared.";
    public const string HistoryNotCleared = "The meter history couldn't be cleared.";
    /// <summary>Accounts, Data: Remove account…, with everything it removes said under it (WF29, decision 7).</summary>
    public const string RemoveAccount = "Remove account…";
    public static string RemoveAccountNamed(string account) => $"Remove account, {account}";
    public static string RemoveAccountQuestion(string account) => $"Remove {account} from Sanduhr?";
    public static string RemoveAccountDetail(string account) =>
        $"Removes {account}'s sign-in from Windows Credential Manager, its meter history (history.{account}.json), its Claude Code folder link "
        + "and its sign-in browser profile. It can't be undone.";
    public const string RemoveAccountConfirm = "Remove";
    public const string RemoveAccountCancel = "Cancel";
    /// <summary>Accounts while signed out (Principle 6).</summary>
    public const string SignedOutAccountsLine = "Claude Usage, Alerts and the meters fill in after you sign in.";

    // ---- Settings v2, slice 5: Alerts, Each limit (WF13) ----

    public const string EachLimitSection = "Each limit";
    public const string EachLimitCaption = "The limit card's menu on the widget changes the same switches.";
    public const string EachLimitListName = "Limits";
    public const string WarnForThisLimit = "Warn for this limit";
    public static string WarnForLimit(string limit) => $"{WarnForThisLimit}, {limit}";
    public const string ShowThisLimit = "Show this limit";
    public static string ShowLimit(string limit) => $"{ShowThisLimit}, {limit}";
    public const string HiddenUntilItResets = "Hidden until it resets";
    public const string HiddenUntilResetCaption = HiddenUntilItResets + ". Tick " + ShowThisLimit + " to bring it back now.";
    public const string EachLimitSignedOut = "Your limits show here after you sign in.";
    public const string EachLimitNotYet = "Your limits show here after Sanduhr checks your usage.";

    // ---- Settings v2, slice 5: Claude Usage, Meters (WF14, WF32) ----

    /// <summary>The Meters view's chart section ("meter history", WF14); was "History chart".</summary>
    public const string MeterHistorySection = "Meter history";
    public const string MetersIntro =
        "Rolling 30-day meter history, kept on this PC and never uploaded. Export it as CSV to read in a spreadsheet or with any agent.";
    public const string MeterHistorySignedOut = "Sign in to start recording your meters. Sanduhr keeps 30 days on this PC.";
    public const string MeterHistoryEmpty = "No meter history yet. It fills in each time Sanduhr checks your meters.";
    public const string ExportMeterHistoryTitle = "Export meter history";
    public const string MeterHistoryDialogTitle = "Meter history";

    // ---- Settings v2, slice 5: the Widget page (WF15, WF24, WF32) ----

    /// <summary>The Widget page's first line (WF21; slice 1 P14).</summary>
    public const string WidgetLine = "The widget is " + DefineWidget + ".";
    /// <summary>The page's options section in search; the page title is already "Widget".</summary>
    public const string WidgetOptions = "Widget options";
    public const string GraphStyleClassic = "Classic line";
    public const string GraphStyleHorizon = "Horizon bands";
    public const string DeepWorkLength = "Deep Work length";
    public const string DeepWorkMinutes = "minutes";
    public static string DeepWorkLengthSpoken => $"{DeepWorkLength}, {DeepWorkMinutes}";
    public const string DeepWorkRange = "Use a whole number of minutes from 1 to 120. Not saved.";
    public const string CompactModeCaption = "Ctrl+D on the widget does the same. The widget opens full size at each launch.";
    public const string LimitOrder = "Limit order";
    public const string LimitOrderListName = "Limits in the widget's order";
    public static string MoveLimitUp(string limit) => $"{MoveUp}, {limit}";
    public static string MoveLimitDown(string limit) => $"{MoveDown}, {limit}";
    public const string JustTheDeskOn = "Sanduhr is in Just the Desk: the widget is hidden.";
    public const string ShowTheWidget = "Show the widget";
    public const string ShowTheWidgetCaption = "Ends Just the Desk and shows the widget, which offers Sign in. The Desk stays on.";

    // ---- Settings v2, slice 5: Publish usage, What gets sent (WF28) ----

    public const string PublishSampleSection = "What gets sent";
    /// <summary>Every field group of the record (UsagePublisher.BuildPayload), and that the sample sends nothing (decision 10).</summary>
    public const string PublishRecordLine =
        "Each record carries the day, this PC's name, the folders you ticked, token counts in total, per model and per project name, "
        + "and your meters' percentages and reset times. Show a sample… builds the next one here and sends nothing.";
    public const string ShowASample = "Show a sample…";
    public const string SampleTitle = "The next record";
    public static string SampleHeader(string date, string? host) => host is null
        ? $"Publish now would send this for {date}, but no endpoint is set, so it would go nowhere yet."
        : $"Publish now would send this for {date} to {host}.";
    public const string SampleNoFolders = "No folder is ticked under Folders to share, so the record names no folder or project.";
    public const string SampleClose = "Close";
    public const string SampleFailed = "The sample couldn't be built.";
```

`MoveUp` and `MoveDown` are declared later in the file (the slice 3 block); `MoveLimitUp` is a method, so the order of declarations doesn't matter.

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests" > %TEMP%\slice5-t3-green.log 2>&1`
Expected: PASS. Nothing in App or XAML uses these yet.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs
git commit -m "feat(settings-v2): slice 5 copy for Accounts, Each limit, Meters, Widget and the publish sample, in the names table"
```

---

### Task 4: The first sign-in asks for a Label

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Services/SignInCoordinator.cs` (constructor, `PersistReauth`, `SignInManual`, `PersistEmbedded`, `PromptForAccountName`, `NextFreeLabel`)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/ManualKeyWindow.xaml:53-59` (the "Account name" label and its box)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (`Retired`)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/SignInLabelTests.cs`; `windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs`

**Interfaces:**
- Consumes: `AccountLabels.First`, `Suggested`, `Accept` (Task 2); `SettingsNames.NameThisAccount`, `AccountLabelField` (Task 3).
- Produces: `internal SignInCoordinator(AccountStore accounts, Paths paths, Func<string, string?> askLabel)` (the test seam: `askLabel` gets the suggestion and returns the typed text, or null when cancelled); `internal string PersistEmbedded(CapturedCookies cookies)` (was private). The public parameterless constructor is unchanged for App.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/SignInLabelTests.cs`:

```csharp
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 5, the Done line's third clause: "the first sign-in asks for a label" (WF30). The embedded sign-in's save,
/// over an in-memory registry and scratch folders: nothing reaches Credential Manager or a browser.
/// </summary>
[Collection("SettingsWindow")]
public sealed class SignInLabelTests : AppWindowFixture
{
    private (SignInCoordinator Coordinator, AccountStore Accounts, List<string> Offered) Coordinator(Func<string, string?> answer,
        params string[] existing)
    {
        var accounts = new AccountStore(new MemoryCredentials());
        foreach (var label in existing)
            accounts.AddAccount(label, "placeholder-" + label);
        var offered = new List<string>();
        var coordinator = new SignInCoordinator(accounts, ScratchPaths(), suggestion =>
        {
            offered.Add(suggestion);
            return answer(suggestion);
        });
        return (coordinator, accounts, offered);
    }

    private static CapturedCookies Cookies() => new("sk-ant-placeholder", null);

    [Fact]
    public void The_first_sign_in_asks_for_a_label_with_Personal_filled_in()
    {
        var (coordinator, accounts, offered) = Coordinator(_ => "Work");

        var saved = coordinator.PersistEmbedded(Cookies());

        Assert.Equal([AccountLabels.First], offered);
        Assert.Equal("Work", saved);
        Assert.Equal(["Work"], accounts.ListAccounts());
        Assert.Equal("Work", accounts.GetActive());
        Assert.Equal(AccountOrigin.Embedded, accounts.GetOrigin("Work"));
    }

    [Fact]
    public void Cancelling_the_first_label_keeps_Personal_and_the_sign_in()
    {
        var (coordinator, accounts, _) = Coordinator(_ => null);

        Assert.Equal("Personal", coordinator.PersistEmbedded(Cookies()));
        Assert.Equal(["Personal"], accounts.ListAccounts());
        Assert.Equal("sk-ant-placeholder", accounts.LoadCredentials("Personal").SessionKey);
    }

    [Fact]
    public void A_later_sign_in_offers_the_next_free_number_and_refuses_a_taken_label()
    {
        var (coordinator, accounts, offered) = Coordinator(_ => "personal", "Personal");

        Assert.Equal("Account 2", coordinator.PersistEmbedded(Cookies()));
        Assert.Equal(["Account 2"], offered);
        Assert.Equal("Account 2", accounts.GetActive());
    }
}
```

In `windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs`, append:

```csharp
    [Fact]
    public void The_key_windows_field_is_called_Label()
    {
        Assert.NotEmpty(SettingsNames.RetiredNames("Account name"));
        Assert.Empty(SettingsNames.RetiredNames("account name"));   // prose that says "the account name" stays
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SignInLabelTests > %TEMP%\slice5-t4-red.log 2>&1`
Expected: build FAILS with `'SignInCoordinator' does not contain a constructor that takes 3 arguments`.

- [ ] **Step 3: The seam and the prompt**

In `windows-dotnet/src/Sanduhr.App/Services/SignInCoordinator.cs`:

Replace the field block and the constructor:

```csharp
    private readonly AccountStore _accounts;
    private readonly CredentialStore _credentials;
    private readonly WebView2UserDataDirectory _userData;

    public SignInCoordinator()
    {
        var paths = new Paths();
        _accounts = new AccountStore(new WindowsCredentialManager(AccountStore.Service));
        _credentials = new CredentialStore(_accounts, paths);
        _userData = new WebView2UserDataDirectory(Path.Combine(paths.AppDataDir, "webview2"));
    }
```

with:

```csharp
    private readonly AccountStore _accounts;
    private readonly CredentialStore _credentials;
    private readonly WebView2UserDataDirectory _userData;
    private readonly Func<string, string?> _askLabel;

    public SignInCoordinator()
        : this(new AccountStore(new WindowsCredentialManager(AccountStore.Service)), new Paths(), AskLabel)
    {
    }

    /// <summary>Tests: an in-memory registry, scratch folders, and the answer to the label prompt (the typed text, or null when
    /// cancelled). The prompt is given the label it offers.</summary>
    internal SignInCoordinator(AccountStore accounts, Paths paths, Func<string, string?> askLabel)
    {
        _accounts = accounts;
        _credentials = new CredentialStore(_accounts, paths);
        _userData = new WebView2UserDataDirectory(Path.Combine(paths.AppDataDir, "webview2"));
        _askLabel = askLabel;
    }
```

In `PersistReauth`, change `var label = _accounts.GetActive() ?? "Personal";` to `var label = _accounts.GetActive() ?? AccountLabels.First;`.

In `SignInManual`, change `var suggested = _accounts.GetActive() is null ? "Personal" : NextFreeLabel();` to `var suggested = AccountLabels.Suggested(_accounts.ListAccounts());`.

Replace `PersistEmbedded`, `PromptForAccountName` and `NextFreeLabel` (from the `/// <summary>Embedded-flow save:` comment down to the closing brace of `NextFreeLabel`, keeping `PersistManual` between them) so the three become:

```csharp
    /// <summary>Embedded-flow save (Settings v2, WF30): every new account, the first included, is named through the label prompt,
    /// "Personal" offered first and "Account N" after; it becomes active. A cancelled, invalid or taken label keeps the offer, so a
    /// captured sign-in is never thrown away.</summary>
    internal string PersistEmbedded(CapturedCookies cookies)
    {
        var existing = _accounts.ListAccounts();
        var suggested = AccountLabels.Suggested(existing);
        var name = AccountLabels.Accept(_askLabel(suggested), suggested, existing);
        _accounts.AddAccount(name, cookies.SessionKey!, cookies.CfClearance);
        _accounts.SetActive(name);
        SetOriginSafe(name, AccountOrigin.Embedded);
        return name;
    }

    /// <summary>The label prompt: "Name this account", field "Label", with <paramref name="suggested"/> filled in. Null when cancelled.</summary>
    private static string? AskLabel(string suggested)
    {
        var prompt = new TextPromptWindow(SettingsNames.NameThisAccount, SettingsNames.AccountLabelField, suggested);
        if (Application.Current?.MainWindow is { IsLoaded: true } main)
            prompt.Owner = main;
        return prompt.ShowDialog() == true ? prompt.Value : null;
    }
```

(`PersistManual` stays where it is, unchanged. `NextFreeLabel` has no other caller: `git grep -n "NextFreeLabel" -- windows-dotnet` prints nothing after the edit.) Update the class summary's "the account-persistence semantics (first-run → "Personal"; subsequent → named slot + make active)" to "the account-persistence semantics (every new account is named through the Label prompt, "Personal" offered first; it becomes active)".

In `windows-dotnet/src/Sanduhr.App/Views/ManualKeyWindow.xaml`, change

```xml
        <TextBlock Grid.Row="2" Text="Account name" FontSize="11"
```

to

```xml
        <TextBlock Grid.Row="2" Text="{x:Static core:SettingsNames.AccountLabelField}" FontSize="11"
```

and add `AutomationProperties.Name="{x:Static core:SettingsNames.AccountLabelField}"` to the `<TextBox Grid.Row="3" x:Name="LabelBox" …` element (after `x:Name="LabelBox"`). The `core` prefix is already declared in that file (it binds `SettingsNames.ManualKeySteps`).

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, add to `Retired`, after the `("Widgets settings…", …)` row:

```csharp
        // Slice 5: the account's name is its Label, in the key window as in the sign-in prompt (decision 8).
        ("Account name", AccountLabelField, true),
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~SignInLabelTests|FullyQualifiedName~AppWindowNamesTests" > %TEMP%\slice5-t4-green.log 2>&1`
Then: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests" > %TEMP%\slice5-t4-core.log 2>&1`
Expected: PASS. `Accounts_and_the_key_window_read_their_copy_from_the_table` still finds its four names in `ManualKeyWindow.xaml`.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Services/SignInCoordinator.cs windows-dotnet/src/Sanduhr.App/Views/ManualKeyWindow.xaml windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.App.Tests/SignInLabelTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs
git commit -m "feat(settings-v2): the first sign-in asks for a Label, Personal filled in; the key window's field is Label too"
```

---

### Task 5: The widget's per-limit and widget-options API, and one way out of Just the Desk

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (`RenderCards`, `HideTier`, `HideUntilReset`, `ShowTier`, `SetWarnings`, `TogglePin`, `CycleSparklineStyle`; a new block after `SetWarnings`)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (`EndJustTheDeskIfAccount`)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/WidgetLimitsTests.cs`

**Interfaces:**
- Consumes: `LimitSettings.Rows`, `SavedOrder` (Task 1); `JustTheDesk.End` (Task 2).
- Produces (on `WidgetViewModel`):
  - `public event Action? LimitsChanged` (raised after a limit's warnings, hiding or order change from the card menu or Settings, and at the end of every card render)
  - `public IReadOnlyList<LimitRow> LimitRows()`
  - `public IReadOnlyList<string> ShownLimitOrder { get; }` (the limits the widget shows, in its order, compact mode aside)
  - `public void SetShown(string tierKey, bool shown)`
  - `public void SetLimitOrder(IReadOnlyList<string> shownOrder)`
  - `public void SetSparklineStyle(SparklineStyle style)`
  - `public void SetPinned(bool on)`
  - `public bool EndJustTheDesk()` (true when it ended; saves, restores the reminder by the rule, redraws the Desk; showing the widget is App's)

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/WidgetLimitsTests.cs`:

```csharp
using System.IO;
using System.Text.Json.Nodes;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 5: what Alerts, Each limit and the Widget page read and write through the widget. A scratch widget in demo
/// mode supplies sample limits (Session and Weekly at least) over scratch folders; every write lands in the scratch settings.json.
/// </summary>
[Collection("SettingsWindow")]
public sealed class WidgetLimitsTests : AppWindowFixture
{
    private JsonObject Settings() => (JsonObject)JsonNode.Parse(File.ReadAllText(ScratchPaths().SettingsFile))!;

    private static string[] Strings(JsonNode? node) => node is JsonArray a ? [.. a.Select(n => n!.GetValue<string>())] : [];

    [Fact]
    public void Muting_hiding_and_showing_a_limit_reach_the_rows_and_the_file_and_say_so() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        widget.StartDemo();
        int raised = 0;
        widget.LimitsChanged += () => raised++;

        widget.SetWarnings(TierModel.SevenDay, false);
        widget.SetShown(TierModel.FiveHour, false);

        var rows = widget.LimitRows();
        Assert.False(rows.Single(r => r.Key == TierModel.SevenDay).WarnOn);
        Assert.False(rows.Single(r => r.Key == TierModel.FiveHour).Shown);
        Assert.Equal([TierModel.SevenDay], Strings(Settings()["alert_muted_tiers"]));
        Assert.Equal([TierModel.FiveHour], Strings(Settings()["hidden_tiers"]));
        Assert.True(raised >= 2, $"LimitsChanged raised {raised} times");

        widget.SetShown(TierModel.FiveHour, true);
        widget.SetWarnings(TierModel.SevenDay, true);
        Assert.All(widget.LimitRows(), r => Assert.True(r.WarnOn && r.Shown));
        Assert.Empty(Strings(Settings()["hidden_tiers"]));
        Assert.Empty(Strings(Settings()["alert_muted_tiers"]));
    });

    [Fact]
    public void A_muted_limit_the_account_no_longer_has_still_has_a_row() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");   // signed in, nothing fetched
        widget.SetWarnings(TierModel.SevenDayOpus, false);

        var row = Assert.Single(widget.LimitRows());
        Assert.Equal(TierModel.SevenDayOpus, row.Key);
        Assert.False(row.WarnOn);
    });

    [Fact]
    public void Moving_a_limit_rewrites_the_order_and_the_cards_follow() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        widget.StartDemo();
        var shown = widget.ShownLimitOrder;
        Assert.True(shown.Count >= 2, "demo mode shows at least two limits");

        var moved = LimitSettings.Move(shown, shown[1], -1)!;
        widget.SetLimitOrder(moved);

        Assert.Equal(moved, widget.ShownLimitOrder);
        Assert.Equal(moved, widget.Tiers.Select(t => t.TierKey));
        Assert.Equal(moved, Strings(Settings()["tier_order"]).Take(moved.Count));
    });

    [Fact]
    public void Graph_style_and_keep_on_top_write_their_keys() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");

        widget.SetSparklineStyle(SparklineStyle.Horizon);
        widget.SetPinned(true);

        Assert.Equal(SparklineStyle.Horizon, widget.SparklineStyle);
        Assert.True(widget.Pinned);
        Assert.Equal("Horizon", (string?)Settings()["sparkline_style"]);
        Assert.True((bool?)Settings()["pinned"]);
    });

    [Fact]
    public void Ending_Just_the_Desk_saves_it_off_and_brings_back_a_reminder_it_had_silenced() => AppHost.Run(() =>
    {
        var store = new SettingsStore(ScratchPaths());
        store.SaveJustTheDesk(new JustTheDeskState(true, SilencedReminder: true));
        store.SaveDeskSignInReminder(false);
        var (widget, _) = ScratchWidget();
        Assert.True(widget.JustTheDesk);

        Assert.True(widget.EndJustTheDesk());

        Assert.False(widget.JustTheDesk);
        Assert.False(store.LoadJustTheDesk().On);
        Assert.True(store.LoadDeskSignInReminder());
        Assert.False(widget.EndJustTheDesk());   // not on: nothing to end
    });
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~WidgetLimitsTests > %TEMP%\slice5-t5-red.log 2>&1`
Expected: build FAILS with `'WidgetViewModel' does not contain a definition for 'LimitsChanged'`.

- [ ] **Step 3: The API**

In `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs`:

In `RenderCards`, right after `var active = TierModel.ActiveTiers(utilByKey, _savedOrder, hiddenKeys);`, add:

```csharp
        // Settings, Widget, Limit order lists these, in this order, whatever compact mode shows.
        _shownOrder = [.. active];
```

and at the end of `RenderCards`, after `RaiseDesk();`, add:

```csharp
        LimitsChanged?.Invoke();
```

At the end of `HideTier` (after `RecomputeHighestForTray();`), at the end of `HideUntilReset` (after its `RecomputeHighestForTray();`) and at the end of `ShowTier` (after the `if (ShownData is { } shown) … RenderCards(…)` line), add:

```csharp
        LimitsChanged?.Invoke();
```

Replace `SetWarnings` with:

```csharp
    public void SetWarnings(string tierKey, bool on)
    {
        bool changed = on ? _mutedTiers.Remove(tierKey) : _mutedTiers.Add(tierKey);
        if (!changed)
            return;
        _settings.SaveAlertMutedTiers(_mutedTiers);
        LimitsChanged?.Invoke();
    }

    // -- Settings, Alerts, Each limit and Settings, Widget (Settings v2, slice 5) --

    /// <summary>Raised after a limit's warnings, hiding or order change, from the card menu or Settings, and at the end of every card
    /// render (a fetch may bring a limit in or take one away). Settings' Each limit and Limit order follow it.</summary>
    public event Action? LimitsChanged;

    private IReadOnlyList<string> _shownOrder = [];

    /// <summary>Settings, Alerts, Each limit (WF13): a row per limit in the shown answer, and per limit a setting still names.</summary>
    public IReadOnlyList<LimitRow> LimitRows() =>
        LimitSettings.Rows(PresentLimitKeys(), _hidden, _mutedTiers, _limitState.HiddenUntilReset.Keys);

    private IEnumerable<string> PresentLimitKeys() =>
        ShownData is { } data ? TierModel.EffectiveOrder.Where(k => data[k] is JsonObject).ToList() : [];

    /// <summary>Settings, Widget, Limit order: the limits the widget shows, in its order (compact mode aside).</summary>
    public IReadOnlyList<string> ShownLimitOrder => _shownOrder;

    /// <summary>Settings, Alerts, Each limit's "Show this limit": shown clears both kinds of hiding (as the card menu's Hidden limits
    /// item does); not shown hides it until shown again.</summary>
    public void SetShown(string tierKey, bool shown)
    {
        if (shown) ShowTier(tierKey);
        else HideTier(tierKey);
    }

    /// <summary>Settings, Widget, Limit order: <paramref name="shownOrder"/> first, then every other known limit once, saved to
    /// "tier_order"; the cards follow at once.</summary>
    public void SetLimitOrder(IReadOnlyList<string> shownOrder)
    {
        var order = LimitSettings.SavedOrder(shownOrder, _savedOrder);
        _savedOrder.Clear();
        _savedOrder.AddRange(order);
        _settings.SaveTierOrder(order);
        if (ShownData is { } shown)
            RenderCards(shown, DateTimeOffset.UtcNow);
        else
            LimitsChanged?.Invoke();
    }

    /// <summary>Settings, Widget, Graph style: every card redraws, with no refetch; saved to "sparkline_style".</summary>
    public void SetSparklineStyle(SparklineStyle style)
    {
        if (SparklineStyle == style)
            return;
        SparklineStyle = style;
        foreach (var card in Tiers)
            card.ApplyStyle(style);
        _settings.SaveSparklineStyle(style);
    }

    /// <summary>Settings, Widget, Keep the widget on top: saved to "pinned".</summary>
    public void SetPinned(bool on)
    {
        if (Pinned == on)
            return;
        Pinned = on;
        _settings.SavePinned(on);
    }

    /// <summary>
    /// Ends Just the Desk (Settings, Widget's "Show the widget", and an added account; <see cref="Sanduhr.Core.JustTheDesk.End"/>): saves
    /// it off, brings the sign-in reminder back only when Just the Desk silenced it, and redraws the Desk. False when it was not on.
    /// Showing the widget is the App's.
    /// </summary>
    public bool EndJustTheDesk()
    {
        var (state, reminderOn, ended) = Sanduhr.Core.JustTheDesk.End(_settings.LoadJustTheDesk(), _settings.LoadDeskSignInReminder());
        if (!ended)
            return false;
        _settings.SaveJustTheDesk(state);
        _settings.SaveDeskSignInReminder(reminderOn);
        JustTheDesk = false;
        RefreshDesk();
        return true;
    }
```

Replace `TogglePin`'s body with `SetPinned(!Pinned);` and `CycleSparklineStyle`'s body with:

```csharp
        var styles = (SparklineStyle[])Enum.GetValues(typeof(SparklineStyle));
        int i = Array.IndexOf(styles, SparklineStyle);
        SetSparklineStyle(styles[(i + 1) % styles.Length]);
        Sounds.PlayToggle();
```

In `windows-dotnet/src/Sanduhr.App/App.xaml.cs`, replace `EndJustTheDeskIfAccount`'s body with:

```csharp
        try
        {
            if (vm.AccountStore.ListAccounts().Count == 0 || !vm.EndJustTheDesk())
                return;
            if (_settingsWindow?.ViewModel.Alerts is { } alerts) alerts.DeskSignInReminder = vm.LoadDeskSignInReminder();
            ShowWindow();
        }
        catch
        {
            // Settings are best-effort here; the account is added either way.
        }
```

(The rule, the two saves and the Desk redraw now live in `EndJustTheDesk`; App keeps the account check, the open Settings window's box and the window.)

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~WidgetLimitsTests|FullyQualifiedName~WidgetBarsTests|FullyQualifiedName~MenuItemCheckMarkTests" > %TEMP%\slice5-t5-green.log 2>&1`
Then: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~JustTheDeskTests|FullyQualifiedName~LimitLifetimeTests|FullyQualifiedName~TierModelTests" > %TEMP%\slice5-t5-core.log 2>&1`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs windows-dotnet/src/Sanduhr.App/App.xaml.cs windows-dotnet/tests/Sanduhr.App.Tests/WidgetLimitsTests.cs
git commit -m "feat(settings-v2): the widget's per-limit, graph, pin and order API for Settings, and one way out of Just the Desk"
```

---

### Task 6: Alerts, Each limit

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/LimitRowViewModel.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/AlertsViewModel.cs` (constructor; `Limits`, `LimitsEmptyText`, `ReloadLimits`, `Detach`)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (the Alerts TabItem: split `AlertsDependents`, add Each limit)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs` (`Closed`: `ViewModel.Alerts.Detach()`)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (`EachLimit`, its anchor row, `NameAnchors`, `MenuAnchor`)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (`Table`)
- Modify: `windows-dotnet/src/Sanduhr.Core/WhatsNewCards.cs` ("limits-come-and-go")
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/EachLimitTests.cs` and `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice5IndexTests.cs`; modify `windows-dotnet/tests/Sanduhr.Tests/SettingsLinkTests.cs` (`Menu_links_land_on_their_section`)

**Interfaces:**
- Consumes: `WidgetViewModel.LimitsChanged`, `LimitRows`, `SetWarnings`, `SetShown` (Task 5); Task 3's Alerts names.
- Produces: `public sealed partial class LimitRowViewModel : ObservableObject` (`Key`, `Label`, `WarnOn`, `Shown`, `HiddenUntilReset`, `WarnName`, `ShowName`, `internal void Sync(LimitRow row)`); `AlertsViewModel.Limits` (`ObservableCollection<LimitRowViewModel>`), `LimitsEmptyText`, `Detach()`; `SettingsIndex.EachLimit = "each-limit"`; `MenuAnchor(SettingsPage.Alerts) == EachLimit`. `TierCard.xaml.cs` needs no change: its "Alerts settings…" item calls `OpenSettingsAtAsync(SettingsPage.Alerts)`, which App routes through `MenuAnchor`.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice5IndexTests.cs` (later tasks append to it):

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 5: the index, the menus and the cards for Accounts, Alerts, Claude Usage, Publish usage, Widget and About.</summary>
public class SettingsSlice5IndexTests
{
    private static SettingsIndex.Hit Top(string q)
    {
        var hits = SettingsIndex.Search(q);
        Assert.NotEmpty(hits);
        return hits[0];
    }

    [Fact]
    public void The_tier_card_and_the_limits_card_land_on_each_limit()
    {
        Assert.Equal(SettingsIndex.EachLimit, SettingsIndex.MenuAnchor(SettingsPage.Alerts));
        Assert.NotNull(SettingsIndex.Find(SettingsPage.Alerts, SettingsIndex.EachLimit));
        var card = WhatsNewCards.Table.Single(c => c.Id == "limits-come-and-go");
        Assert.Equal((SettingsPage.Alerts, SettingsIndex.EachLimit), (card.Destination, card.Anchor));
    }

    [Theory]
    [InlineData("each limit")]
    [InlineData("limit")]
    [InlineData("warn for this limit")]
    [InlineData("hidden limits")]
    [InlineData("until it resets")]
    public void Limit_words_find_each_limit(string query) =>
        Assert.Contains(SettingsIndex.Search(query).Take(3), h => h.Entry.Page == SettingsPage.Alerts && h.Entry.Anchor == SettingsIndex.EachLimit);
}
```

In `windows-dotnet/tests/Sanduhr.Tests/SettingsLinkTests.cs`, change the row `[InlineData(SettingsPage.Alerts, SettingsIndex.Notifications)]` of `Menu_links_land_on_their_section` to `[InlineData(SettingsPage.Alerts, SettingsIndex.EachLimit)]   // slice 5: the tier card's link lands on its home (WF13)`.

Create `windows-dotnet/tests/Sanduhr.App.Tests/EachLimitTests.cs`:

```csharp
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 5: Alerts, Each limit (WF13). The Done line's second clause: "a muted limit can be restored from Alerts".</summary>
[Collection("SettingsWindow")]
public sealed class EachLimitTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Svm, WidgetViewModel Widget, NoActivate Guard) Open(bool demo)
    {
        var (widget, _) = ScratchWidget("Personal");
        if (demo) widget.StartDemo();
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

    private static CheckBox Box(SettingsWindow window, string name) =>
        Below<CheckBox>(window).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == name);

    private string[] Muted() => JsonNode.Parse(File.ReadAllText(ScratchPaths().SettingsFile))!["alert_muted_tiers"] is JsonArray a
        ? [.. a.Select(n => n!.GetValue<string>())] : [];

    /// <summary>Done (spec, slice 5): a muted limit can be restored from Alerts, even one the account doesn't have today.</summary>
    [Fact]
    public void A_muted_limit_can_be_restored_from_Alerts() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open(demo: false);
        try
        {
            widget.SetWarnings(TierModel.SevenDayOpus, false);
            window.SelectPage(SettingsPage.Alerts, SettingsIndex.EachLimit);
            Settle(window, guard);
            var warn = Box(window, SettingsNames.WarnForLimit("Weekly - Opus"));
            Assert.False(warn.IsChecked);
            Assert.True(warn.IsEnabled && warn.IsVisible);

            warn.IsChecked = true;   // what a click sets; the binding writes it through
            Settle(window, guard);

            Assert.Empty(Muted());
            Assert.True(widget.WarningsOn(TierModel.SevenDayOpus));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_rows_follow_the_card_menu_and_keep_their_place() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open(demo: true);
        try
        {
            window.SelectPage(SettingsPage.Alerts, SettingsIndex.EachLimit);
            Settle(window, guard);
            var session = svm.Alerts.Limits.Single(r => r.Key == TierModel.FiveHour);
            var box = Box(window, SettingsNames.ShowLimit(session.Label));

            widget.HideTier(TierModel.FiveHour);   // the card menu's "Hide this limit"
            Settle(window, guard);

            Assert.Same(session, svm.Alerts.Limits.Single(r => r.Key == TierModel.FiveHour));   // synced in place, not rebuilt
            Assert.False(box.IsChecked);
            Assert.Same(box, Box(window, SettingsNames.ShowLimit(session.Label)));

            box.IsChecked = true;
            Settle(window, guard);
            Assert.DoesNotContain(TierModel.FiveHour, widget.HiddenTierKeys);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void With_alerts_off_Warn_dims_and_Show_still_acts() => AppHost.Run(() =>
    {
        var (window, svm, _, guard) = Open(demo: true);
        try
        {
            window.SelectPage(SettingsPage.Alerts, SettingsIndex.EachLimit);
            svm.Alerts.AlertsEnabled = false;
            Settle(window, guard);
            var label = svm.Alerts.Limits[0].Label;
            Assert.False(Box(window, SettingsNames.WarnForLimit(label)).IsEnabled);
            Assert.True(Box(window, SettingsNames.ShowLimit(label)).IsEnabled);
            Assert.True(((FrameworkElement)window.FindName("AlertsOffLine")).IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Signed_out_the_list_says_when_limits_show() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget();
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        try
        {
            window.SelectPage(SettingsPage.Alerts, SettingsIndex.EachLimit);
            Settle(window, guard);
            var line = (TextBlock)window.FindName("EachLimitEmpty");
            Assert.True(line.IsVisible);
            Assert.Equal(SettingsNames.EachLimitSignedOut, line.Text);
            Assert.False(((Control)window.FindName("EachLimitList")).Focusable);
        }
        finally { window.Close(); }
    });
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSlice5IndexTests|FullyQualifiedName~SettingsLinkTests" > %TEMP%\slice5-t6-red.log 2>&1`
Expected: build FAILS with `'SettingsIndex' does not contain a definition for 'EachLimit'`.

- [ ] **Step 3: The row view model and the Alerts view model**

Create `windows-dotnet/src/Sanduhr.App/ViewModels/LimitRowViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// One row of Settings, Alerts, Each limit (WF13). Its two switches write through the widget, which saves the keys and raises
/// LimitsChanged; the page then syncs every row in place (<see cref="Sync"/>), so a switch never loses keyboard focus to a rebuilt list.
/// </summary>
public sealed partial class LimitRowViewModel : ObservableObject
{
    private readonly WidgetViewModel _widget;
    private bool _syncing;

    public string Key { get; }
    public string Label { get; }

    /// <summary>"Warn for this limit" (not in "alert_muted_tiers").</summary>
    [ObservableProperty] private bool _warnOn;

    /// <summary>"Show this limit" (in neither "hidden_tiers" nor "limits_hidden_until_reset").</summary>
    [ObservableProperty] private bool _shown;

    /// <summary>Hidden until its window renews or refills; the caption under the row says so.</summary>
    [ObservableProperty] private bool _hiddenUntilReset;

    public LimitRowViewModel(WidgetViewModel widget, LimitRow row)
    {
        _widget = widget;
        Key = row.Key;
        Label = row.Label;
        Sync(row);
    }

    public string WarnName => SettingsNames.WarnForLimit(Label);
    public string ShowName => SettingsNames.ShowLimit(Label);

    /// <summary>Takes the row's state without writing anything back.</summary>
    internal void Sync(LimitRow row)
    {
        _syncing = true;
        WarnOn = row.WarnOn;
        Shown = row.Shown;
        HiddenUntilReset = row.HiddenUntilReset;
        _syncing = false;
    }

    partial void OnWarnOnChanged(bool value)
    {
        if (!_syncing) _widget.SetWarnings(Key, value);
    }

    partial void OnShownChanged(bool value)
    {
        if (!_syncing) _widget.SetShown(Key, value);
    }

    /// <summary>A screen reader reads the row by its limit, never the type behind it.</summary>
    public override string ToString() => Label;
}
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/AlertsViewModel.cs`, add `using System.Collections.ObjectModel;` at the top. After `DeskSignInReminder = widget.LoadDeskSignInReminder();` and `_loading = false;` in the constructor, add:

```csharp
        _widget.LimitsChanged += ReloadLimits;
        ReloadLimits();
```

and add these members after the constructor:

```csharp
    /// <summary>Alerts, Each limit (WF13): one row per limit the account has or a setting still names.</summary>
    public ObservableCollection<LimitRowViewModel> Limits { get; } = new();

    /// <summary>What Each limit says while it has no row: signed out, or signed in with nothing fetched yet.</summary>
    [ObservableProperty] private string _limitsEmptyText = "";

    /// <summary>Syncs the rows in place: a row keeps its object (and its box keeps focus) while its state changes; new limits
    /// slot in at their place; gone ones leave.</summary>
    private void ReloadLimits()
    {
        var rows = _widget.LimitRows();
        for (int i = 0; i < rows.Count; i++)
        {
            int at = -1;
            for (int j = i; j < Limits.Count; j++)
            {
                if (Limits[j].Key == rows[i].Key) { at = j; break; }
            }
            if (at < 0)
            {
                Limits.Insert(i, new LimitRowViewModel(_widget, rows[i]));
                continue;
            }
            if (at != i)
                Limits.Move(at, i);
            Limits[i].Sync(rows[i]);
        }
        while (Limits.Count > rows.Count)
            Limits.RemoveAt(Limits.Count - 1);
        LimitsEmptyText = rows.Count > 0 ? ""
            : _widget.ActiveAccount is null && !_widget.IsDemo ? SettingsNames.EachLimitSignedOut : SettingsNames.EachLimitNotYet;
    }

    /// <summary>Stop following the widget (Settings closed): it outlives every Settings window.</summary>
    public void Detach() => _widget.LimitsChanged -= ReloadLimits;
```

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs`, in the `Closed` handler, after `ViewModel.Themes.Detach();`, add `ViewModel.Alerts.Detach();`.

- [ ] **Step 4: The XAML**

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`, in the Alerts TabItem, replace from

```xml
                            <CheckBox Content="Sound" IsChecked="{Binding SoundEnabled}" views:SettingsAnchor.Id="sound"
```

up to and including the `</StackPanel>` that closes `AlertsDependents` (the line after the Send test alert `Button`), with:

```xml
                            </StackPanel>

                            <!-- Each limit (WF13): warnings and hiding per limit; the card menu writes the same keys. "Show this limit" sends
                                 nothing, so it stays live with alerts off; "Warn for this limit" dims with them (decision 5). -->
                            <StackPanel Margin="0,4,0,14" views:SettingsAnchor.Id="each-limit">
                                <TextBlock Text="{x:Static core:SettingsNames.EachLimitSection}" FontSize="13" FontWeight="SemiBold"
                                           AutomationProperties.HeadingLevel="Level2"
                                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,4" />
                                <TextBlock Text="{x:Static core:SettingsNames.EachLimitCaption}" FontSize="10.5" TextWrapping="Wrap"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextDim}" Margin="0,0,0,8" />
                                <TextBlock x:Name="EachLimitEmpty" Style="{StaticResource EmptyLine}" Text="{Binding LimitsEmptyText}"
                                           Tag="{Binding HasItems, ElementName=EachLimitList}" />
                                <ItemsControl x:Name="EachLimitList" ItemsSource="{Binding Limits}" Focusable="False"
                                              AutomationProperties.Name="{x:Static core:SettingsNames.EachLimitListName}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <StackPanel Margin="0,0,0,8">
                                                <TextBlock Text="{Binding Label}" FontSize="12" Margin="0,0,0,2"
                                                           Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                                <WrapPanel>
                                                    <CheckBox Style="{StaticResource ThemedCheckBox}" Margin="0,0,16,0"
                                                              Content="{x:Static core:SettingsNames.WarnForThisLimit}"
                                                              IsChecked="{Binding WarnOn, Mode=TwoWay}"
                                                              IsEnabled="{Binding DataContext.AlertsEnabled, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                                              AutomationProperties.Name="{Binding WarnName}" />
                                                    <CheckBox Style="{StaticResource ThemedCheckBox}"
                                                              Content="{x:Static core:SettingsNames.ShowThisLimit}"
                                                              IsChecked="{Binding Shown, Mode=TwoWay}"
                                                              AutomationProperties.Name="{Binding ShowName}" />
                                                </WrapPanel>
                                                <TextBlock Text="{x:Static core:SettingsNames.HiddenUntilResetCaption}" FontSize="10.5" TextWrapping="Wrap"
                                                           Margin="22,2,0,0" Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                                           Visibility="{Binding HiddenUntilReset, Converter={StaticResource BoolVis}}" />
                                            </StackPanel>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>

                            <StackPanel x:Name="AlertsSoundDependents" IsEnabled="{Binding AlertsEnabled}">
                                <StackPanel.Style>
                                    <Style TargetType="StackPanel">
                                        <Style.Triggers>
                                            <Trigger Property="IsEnabled" Value="False">
                                                <Setter Property="Opacity" Value="0.45" />
                                            </Trigger>
                                        </Style.Triggers>
                                    </Style>
                                </StackPanel.Style>
                            <CheckBox Content="Sound" IsChecked="{Binding SoundEnabled}" views:SettingsAnchor.Id="sound"
                                      Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,6" />
                            <CheckBox Content="Chime for model-scoped weekly tiers"
                                      IsChecked="{Binding ScopedChimeEnabled}"
                                      Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,6" />
                            <CheckBox Content="{x:Static core:SettingsNames.SnakeSoundAtFull}"
                                      IsChecked="{Binding SnakeAtFull}"
                                      Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,14" />

                            <Button Style="{StaticResource FlatButton}" Content="{x:Static core:SettingsNames.SendTestAlert}"
                                    views:SettingsAnchor.Id="test"
                                    Command="{Binding TestAlertCommand}" HorizontalAlignment="Left" />
                            </StackPanel>
```

The first `</StackPanel>` closes `AlertsDependents` after the Reset notifications check box (so the thresholds, the hint, Projection alerts and Reset notifications stay in it), and the last closes `AlertsSoundDependents`; the outer `StackPanel` (anchor `notifications`) still closes after it. `AlertsOffLine`'s "Nothing below sends." stays true: Each limit's live box hides a card and sends nothing.

- [ ] **Step 5: The index, the table and the card**

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`: under `// Alerts`, add `public const string EachLimit = "each-limit";`. In `Anchors`, after the `"Warn at and Urgent at"` row, add:

```csharp
        E(SettingsNames.EachLimitSection, SettingsPage.Alerts, EachLimit,
            ["limit", "limits", "each limit", "per limit", "hide", "hidden", "hidden limits", "show", "mute", "stop warnings", "warn again",
             "until it resets", "promo", "special limit", SettingsNames.WarnForThisLimit, SettingsNames.ShowThisLimit, SettingsNames.HiddenUntilItResets]),
```

In `NameAnchors`, add `[SettingsNames.WarnForThisLimit] = EachLimit,` and `[SettingsNames.ShowThisLimit] = EachLimit,`. In `MenuAnchor`, change `SettingsPage.Alerts => Notifications,` to `SettingsPage.Alerts => EachLimit,` and its summary's second sentence to "Alerts opens at Each limit (slice 5), where the tier card's link lands; null opens the page top.".

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, add to `Table`, after `(SendTestAlert, SettingsPage.Alerts),`:

```csharp
        (WarnForThisLimit, SettingsPage.Alerts),
        (ShowThisLimit, SettingsPage.Alerts),
```

In `windows-dotnet/src/Sanduhr.Core/WhatsNewCards.cs`, change the "limits-come-and-go" card's last argument from `SettingsPage.Widget` to `SettingsPage.Alerts`, and add `"limits-come-and-go" => SettingsIndex.EachLimit,` to `AnchorFor`, after `"tray-shows" => SettingsIndex.Tray,`.

- [ ] **Step 6: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSlice5IndexTests|FullyQualifiedName~SettingsLinkTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsReachIndexTests|FullyQualifiedName~WhatsNewTests|FullyQualifiedName~WhatsNewCardsTests" > %TEMP%\slice5-t6-core.log 2>&1`
Then: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~EachLimitTests|FullyQualifiedName~SettingsReachTests|FullyQualifiedName~WhatsNewShowMeTests" > %TEMP%\slice5-t6-app.log 2>&1`
Expected: PASS. `Alerts_off_dims_everything_under_the_switch_and_says_why` still finds Thresholds, Sound and the test button disabled and `AlertsDependents` at 0.45.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/LimitRowViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/AlertsViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.Core/WhatsNewCards.cs windows-dotnet/tests/Sanduhr.App.Tests/EachLimitTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsSlice5IndexTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsLinkTests.cs
git commit -m "feat(settings-v2): Alerts, Each limit: warn, show and hidden-until-reset per limit, where the tier card's link lands"
```

---

### Task 7: Accounts, Sign in and Data for this account; Clear leaves the meter history

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/AccountsViewModel.cs` (constructor, `SelectedAccount` hook, `SignOut`; Data members)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/HistoryTabViewModel.cs` (Clear history goes; `Refresh`)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (`Accounts.HistoryCleared`)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (the Accounts TabItem; the History TabItem's Clear button)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs` (`Tabs_SelectionChanged`: Accounts reloads)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (`SignInSection`, `AccountData`, their rows, `NameAnchors`; Accounts and History synonyms)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (`Table`, `Retired`)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/AccountsDataTests.cs`; delete `windows-dotnet/tests/Sanduhr.App.Tests/HistoryClearTests.cs` (its four facts move here, onto Accounts); append to `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice5IndexTests.cs`

**Interfaces:**
- Consumes: Task 3's Accounts names; `WidgetViewModel.AccountFolder`, `History`, `SignOutAccountAsync`, `IsDemo`; `ClaudeCodeFolders.Display`, `ClaudeCodeProfile.Dir`.
- Produces: `AccountsViewModel(WidgetViewModel widget, Func<Task> addAccountAsync, Func<string, Task> updateSignInAsync, Func<string, string>? displayFolder = null)`; `HasData`, `LinkedFolderText`, `RemoveCaption`, `ClearHistoryName`, `RemoveName`, `ClearHistoryCommand`, `public Action? HistoryCleared { get; set; }`; test hooks `internal Func<string, bool>? ConfirmClearHook`, `ConfirmRemoveHook`, `internal Action<string>? InfoHook`. `HistoryTabViewModel.Refresh()`. `SettingsIndex.SignInSection = "sign-in"`, `SettingsIndex.AccountData = "data"`.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/AccountsDataTests.cs`:

```csharp
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 5: Accounts, Data for this account (WF1, WF27, WF29). "Clear this account's history…" acts on the account the list
/// selects (it moved here from History, slice 0's fix included); Remove account… says under it everything it removes.
/// </summary>
[Collection("SettingsWindow")]
public sealed class AccountsDataTests : AppWindowFixture
{
    /// <summary>A real (not demo) widget over scratch folders and an in-memory registry: Work active, Personal second, each with history.</summary>
    private (WidgetViewModel Widget, AccountsViewModel Accounts, List<string> Asked, List<string> Told) Open()
    {
        var (widget, _) = ScratchWidget("Work", "Personal");
        widget.History.AppendForAccount("five_hour", 41, "Work");
        widget.History.AppendForAccount("five_hour", 17, "Personal");
        var asked = new List<string>();
        var told = new List<string>();
        var accounts = new AccountsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask, path => "~\\" + Path.GetFileName(path))
        {
            ConfirmClearHook = label => { asked.Add(label); return true; },
            InfoHook = told.Add,
        };
        return (widget, accounts, asked, told);
    }

    private static void Pick(AccountsViewModel accounts, string label) =>
        accounts.SelectedAccount = accounts.Accounts.Single(a => a.Label == label);

    [Fact]
    public void Clearing_clears_the_selected_account_not_the_active_one() => AppHost.Run(() =>
    {
        var (widget, accounts, asked, told) = Open();
        Pick(accounts, "Personal");

        accounts.ClearHistoryCommand.Execute(null);

        Assert.Equal(["Personal"], asked);
        Assert.Empty(widget.History.Load("five_hour", "Personal"));
        Assert.Equal([41], widget.History.Load("five_hour", "Work"));
        Assert.Equal([SettingsNames.HistoryCleared("Personal")], told);
    });

    [Fact]
    public void Declining_clears_nothing() => AppHost.Run(() =>
    {
        var (widget, accounts, _, _) = Open();
        accounts.ConfirmClearHook = _ => false;
        Pick(accounts, "Personal");

        accounts.ClearHistoryCommand.Execute(null);

        Assert.Equal([17], widget.History.Load("five_hour", "Personal"));
        Assert.Equal([41], widget.History.Load("five_hour", "Work"));
    });

    [Fact]
    public void Demo_mode_never_clears_a_real_history_file() => AppHost.Run(() =>
    {
        var (widget, accounts, asked, _) = Open();
        Pick(accounts, "Personal");
        widget.StartDemo();   // the sample accounts are named Work and Personal too

        accounts.ClearHistoryCommand.Execute(null);   // CanExecute is not enforced on a programmatic Execute

        Assert.Empty(asked);
        Assert.Equal([17], widget.History.Load("five_hour", "Personal"));
    });

    [Fact]
    public void The_data_section_names_the_linked_folder_and_what_Remove_removes() => AppHost.Run(() =>
    {
        var (widget, accounts, _, _) = Open();
        widget.SetAccountFolder("Personal", Path.Combine(Profile, ".claude-personal"));

        Pick(accounts, "Personal");
        Assert.True(accounts.HasData);
        Assert.Equal(SettingsNames.LinkedFolderLine("~\\.claude-personal"), accounts.LinkedFolderText);
        Assert.Equal(SettingsNames.RemoveAccountDetail("Personal"), accounts.RemoveCaption);
        Assert.Equal(SettingsNames.ClearAccountHistoryNamed("Personal"), accounts.ClearHistoryName);
        Assert.Equal(SettingsNames.RemoveAccountNamed("Personal"), accounts.RemoveName);

        Pick(accounts, "Work");
        Assert.Equal(SettingsNames.LinkedFolderLine(null), accounts.LinkedFolderText);
    });

    [Fact]
    public void Declining_Remove_keeps_the_account() => AppHost.Run(() =>
    {
        var (widget, accounts, _, _) = Open();
        string? asked = null;
        accounts.ConfirmRemoveHook = label => { asked = label; return false; };
        Pick(accounts, "Personal");

        accounts.SignOutCommand.Execute(null);

        Assert.Equal("Personal", asked);
        Assert.Contains("Personal", widget.AccountStore.ListAccounts());
        Assert.Equal([17], widget.History.Load("five_hour", "Personal"));
    });

    [Fact]
    public void Signed_out_the_page_says_what_fills_in_and_shows_no_data_section() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget();
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        try
        {
            window.SelectPage(SettingsPage.Accounts, SettingsIndex.AccountList);
            window.UpdateLayout();
            Pump();
            guard.Check();
            var tab = ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == "Accounts");
            Assert.True(((FrameworkElement)window.FindName("SignedOutAccountsLine")).IsVisible);
            Assert.False(SettingsAnchor.Find((DependencyObject)tab.Content, SettingsIndex.AccountData)!.IsVisible);
            Assert.True(SettingsAnchor.Find((DependencyObject)tab.Content, SettingsIndex.SignInSection)!.IsVisible);
        }
        finally { window.Close(); }
    });
}
```

Append to the class in `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice5IndexTests.cs`:

```csharp
    [Theory]
    [InlineData("clear history", SettingsIndex.AccountData)]
    [InlineData("remove account", SettingsIndex.AccountData)]
    [InlineData("linked folder", SettingsIndex.AccountData)]
    [InlineData("add account", SettingsIndex.SignInSection)]
    [InlineData("sessionKey", SettingsIndex.SignInSection)]
    [InlineData("label", SettingsIndex.SignInSection)]
    public void Account_words_find_their_section(string query, string anchor)
    {
        var top = Top(query);
        Assert.Equal((SettingsPage.Accounts, anchor), (top.Entry.Page, top.Entry.Anchor));
    }

    [Fact]
    public void Clear_history_is_retired_as_a_label_but_the_new_one_is_live()
    {
        Assert.NotEmpty(SettingsNames.RetiredNames("Clear history"));
        Assert.Empty(SettingsNames.RetiredNames(SettingsNames.ClearAccountHistory));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~AccountsDataTests > %TEMP%\slice5-t7-red.log 2>&1`
Expected: build FAILS with `'AccountsViewModel' does not contain a definition for 'ConfirmClearHook'`.

- [ ] **Step 3: The view models**

In `windows-dotnet/src/Sanduhr.App/ViewModels/AccountsViewModel.cs`:

Add `using System.IO;`, `using Sanduhr.App.Services;` and `using Sanduhr.Core;` to the usings.

Add `[NotifyCanExecuteChangedFor(nameof(ClearHistoryCommand))]` to both `_isDemo` and `_selectedAccount`, beside their other `NotifyCanExecuteChangedFor` lines.

Replace the fields-and-constructor lines

```csharp
    private readonly Func<string, Task> _updateSignInAsync;
    private Window? _owner;
```

and the constructor with:

```csharp
    private readonly Func<string, Task> _updateSignInAsync;
    private readonly Func<string, string> _displayFolder;
    private Window? _owner;
```

```csharp
    public AccountsViewModel(WidgetViewModel widget, Func<Task> addAccountAsync, Func<string, Task> updateSignInAsync,
        Func<string, string>? displayFolder = null)
    {
        _widget = widget;
        _addAccountAsync = addAccountAsync;
        _updateSignInAsync = updateSignInAsync;
        _displayFolder = displayFolder ?? (path => ClaudeCodeFolders.Display(path, ClaudeCodeProfile.Dir));
        Reload();
    }
```

Replace `SignOut` (its attribute and body) with:

```csharp
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task SignOut()
    {
        var item = SelectedAccount;
        if (item is null || IsDemo || _widget.IsDemo)
            return;
        if (!ConfirmRemove(item.Label))
            return;
        await _widget.SignOutAccountAsync(item.Label);
        Reload();
    }
```

After `Add`, add:

```csharp
    // ---- Data for this account (Settings v2, slice 5; WF1, WF27, WF29) ----

    /// <summary>The Data section shows while an account is selected.</summary>
    public bool HasData => SelectedAccount is not null;

    /// <summary>"Linked to ~\.claude-work." or "No Claude Code folder is linked to this account." (an account links one folder).</summary>
    [ObservableProperty] private string _linkedFolderText = "";

    /// <summary>Under Remove account…: everything it removes, the same sentence its question shows.</summary>
    [ObservableProperty] private string _removeCaption = "";

    public string ClearHistoryName => SettingsNames.ClearAccountHistoryNamed(SelectedAccount?.Label ?? "");
    public string RemoveName => SettingsNames.RemoveAccountNamed(SelectedAccount?.Label ?? "");

    /// <summary>Connected by Settings: the meter history redraws after a clear.</summary>
    public Action? HistoryCleared { get; set; }

    partial void OnSelectedAccountChanged(AccountItemViewModel? value) => RefreshData();

    private void RefreshData()
    {
        var label = SelectedAccount?.Label;
        var folder = label is null || IsDemo ? null : _widget.AccountFolder(label);
        LinkedFolderText = label is null ? "" : SettingsNames.LinkedFolderLine(folder is null ? null : _displayFolder(folder));
        RemoveCaption = label is null ? "" : SettingsNames.RemoveAccountDetail(label);
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(ClearHistoryName));
        OnPropertyChanged(nameof(RemoveName));
    }

    /// <summary>"Clear this account's history…" (WF1): the selected account's 30-day meter history, after a question with no default
    /// button. Never in demo mode, whose sample labels can match a real account's.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ClearHistory()
    {
        if (IsDemo || _widget.IsDemo || SelectedAccount is not { } item)
            return;
        if (!ConfirmClear(item.Label))
            return;
        try
        {
            _widget.History.ClearAll(item.Label);
        }
        catch (IOException)
        {
            Sounds.PlayError();
            Info(SettingsNames.HistoryNotCleared);
            return;
        }
        HistoryCleared?.Invoke();
        Info(SettingsNames.HistoryCleared(item.Label));
    }

    /// <summary>Tests answer the questions and read the notices here instead of showing them.</summary>
    internal Func<string, bool>? ConfirmClearHook { get; set; }
    internal Func<string, bool>? ConfirmRemoveHook { get; set; }
    internal Action<string>? InfoHook { get; set; }

    private bool ConfirmClear(string label) => ConfirmClearHook is { } hook
        ? hook(label)
        : ThemedDialog.ShowConfirm(_owner, SettingsNames.ClearHistoryQuestion(label), SettingsNames.ClearHistoryDetail,
            SettingsNames.ClearHistoryConfirm, SettingsNames.ClearHistoryCancel);

    private bool ConfirmRemove(string label) => ConfirmRemoveHook is { } hook
        ? hook(label)
        : ThemedDialog.ShowConfirm(_owner, SettingsNames.RemoveAccountQuestion(label), SettingsNames.RemoveAccountDetail(label),
            SettingsNames.RemoveAccountConfirm, SettingsNames.RemoveAccountCancel);

    private void Info(string message)
    {
        if (InfoHook is { } hook) { hook(message); return; }
        ThemedDialog.Show(_owner, SettingsNames.MeterHistoryDialogTitle, message, MessageBoxButton.OK, ThemedDialogKind.Info);
    }
```

In `Reload`, after `SelectedAccount = …;` add `RefreshData();` (a reload with the same selection still re-reads the link: the Claude Code page may have changed it).

In `windows-dotnet/src/Sanduhr.App/ViewModels/HistoryTabViewModel.cs`: delete `ClearHistoryCommand.NotifyCanExecuteChanged();` from `RefreshAccountOptions`; delete the `ClearHistory` command, `ClearConfirmation`, `ConfirmHook` and `Confirm` (from `[RelayCommand(CanExecute = nameof(NotDemo))]` above `private void ClearHistory()` down to the end of `ClearConfirmation`, and the two members `ConfirmHook` and `Confirm`); keep `InfoHook` and `Info` (Export uses them). Change the class summary's "the CSV export + Clear-history actions" to "the CSV export (Clear moved to Accounts, Data for this account, in Settings v2)". Add after `Detach`:

```csharp
    /// <summary>Redraw the chart (Accounts cleared a history, or the Meters view came into view).</summary>
    public void Refresh() => Changed?.Invoke();
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`, after `History = new HistoryTabViewModel(widget);` add `Accounts.HistoryCleared = History.Refresh;`.

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs`, in `Tabs_SelectionChanged`, before the Claude Code reload, add:

```csharp
        // Accounts shows the selected account's folder link, which the Claude Code page changes: read it again on each visit.
        if (page == SettingsPage.Accounts)
            ViewModel.Accounts.Reload();
```

- [ ] **Step 4: The XAML**

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`, in the History TabItem's action row (anchor `export`), delete the `Clear history` button (the `<Button Style="{StaticResource FlatButton}" Content="Clear history" …/>` element, three lines). The Export button stays as it is; Task 8 moves the row into Claude Usage.

Replace the whole Accounts TabItem (from `<!-- Accounts tab — the feature. -->` through its `</TabItem>`) with:

```xml
                <!-- Accounts (Settings v2, slice 5): the list, Sign in, and Data for this account (WF1, WF27, WF29, WF30, WF32). -->
                <TabItem Header="Accounts" Tag="Accounts" AutomationProperties.Name="Accounts">
                    <Border Background="{DynamicResource Sanduhr.Brush.Glass}" CornerRadius="8">
                        <ScrollViewer VerticalScrollBarVisibility="Auto" Focusable="False">
                            <ScrollViewer.Resources>
                                <Style TargetType="ScrollBar" BasedOn="{StaticResource SlimThemeScrollBar}" />
                            </ScrollViewer.Resources>
                        <StackPanel Margin="16" DataContext="{Binding Accounts}">
                            <StackPanel views:SettingsAnchor.Id="accounts">
                                <TextBlock TextWrapping="Wrap" FontSize="11" Margin="0,0,0,8"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                                           Text="Track multiple Claude accounts in one install. Switch which one is active to send fetches and history to that account. Each account's history is stored in its own file." />
                                <TextBlock TextWrapping="Wrap" FontSize="11" Margin="0,0,0,8"
                                           Foreground="{DynamicResource Sanduhr.Brush.Accent}"
                                           Visibility="{Binding IsDemo, Converter={StaticResource BoolVis}}"
                                           Text="Demo mode: these are sample accounts. End demo mode from the tray menu to manage your own." />
                                <StackPanel Margin="0,0,0,12" views:SettingsAnchor.Id="follow">
                                    <CheckBox Style="{StaticResource ThemedCheckBox}"
                                              Content="Follow the account I'm using"
                                              IsChecked="{Binding FollowAccount, Mode=TwoWay}"
                                              AutomationProperties.Name="Follow the account I'm using" />
                                    <TextBlock TextWrapping="Wrap" FontSize="10.5" Margin="22,3,0,0"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                               Text="{x:Static core:SettingsNames.FollowAccountCaption}" />
                                </StackPanel>

                                <Border Background="{DynamicResource Sanduhr.Brush.Bg}" CornerRadius="6" MinHeight="120" MaxHeight="260"
                                        BorderBrush="{DynamicResource Sanduhr.Brush.Border}" BorderThickness="1">
                                    <Grid>
                                        <ListBox x:Name="AccountsList" AutomationProperties.Name="Accounts"
                                                 ItemsSource="{Binding Accounts}"
                                                 SelectedItem="{Binding SelectedAccount, Mode=TwoWay}"
                                                 Background="Transparent" BorderThickness="0"
                                                 ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                                                 HorizontalContentAlignment="Stretch">
                                            <ListBox.ItemContainerStyle>
                                                <Style TargetType="ListBoxItem">
                                                    <Setter Property="Padding" Value="10,8" />
                                                    <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.Text}" />
                                                    <Setter Property="Template">
                                                        <Setter.Value>
                                                            <ControlTemplate TargetType="ListBoxItem">
                                                                <Border x:Name="row" Background="Transparent"
                                                                        Padding="{TemplateBinding Padding}" CornerRadius="4">
                                                                    <ContentPresenter />
                                                                </Border>
                                                                <ControlTemplate.Triggers>
                                                                    <Trigger Property="IsMouseOver" Value="True">
                                                                        <Setter TargetName="row" Property="Background" Value="{DynamicResource Sanduhr.Brush.GlassHover}" />
                                                                    </Trigger>
                                                                    <Trigger Property="IsSelected" Value="True">
                                                                        <Setter TargetName="row" Property="Background" Value="{DynamicResource Sanduhr.Brush.GlassHover}" />
                                                                    </Trigger>
                                                                </ControlTemplate.Triggers>
                                                            </ControlTemplate>
                                                        </Setter.Value>
                                                    </Setter>
                                                </Style>
                                            </ListBox.ItemContainerStyle>
                                            <ListBox.ItemTemplate>
                                                <DataTemplate>
                                                    <Grid>
                                                        <Grid.ColumnDefinitions>
                                                            <ColumnDefinition Width="18" />
                                                            <ColumnDefinition Width="*" />
                                                            <ColumnDefinition Width="Auto" />
                                                        </Grid.ColumnDefinitions>
                                                        <TextBlock Grid.Column="0" Text="{Binding Marker}"
                                                                   Foreground="{DynamicResource Sanduhr.Brush.Accent}" FontSize="12"
                                                                   VerticalAlignment="Center" />
                                                        <TextBlock Grid.Column="1" Text="{Binding Label}"
                                                                   Foreground="{DynamicResource Sanduhr.Brush.Text}" FontSize="13"
                                                                   VerticalAlignment="Center" />
                                                        <TextBlock Grid.Column="2" Text="active" FontSize="10"
                                                                   Foreground="{DynamicResource Sanduhr.Brush.Accent}"
                                                                   VerticalAlignment="Center"
                                                                   Visibility="{Binding IsActive, Converter={StaticResource BoolVis}}" />
                                                    </Grid>
                                                </DataTemplate>
                                            </ListBox.ItemTemplate>
                                        </ListBox>

                                        <!-- Empty hint -->
                                        <TextBlock Text="No accounts yet. Click &quot;Add account…&quot; to sign in."
                                                   Foreground="{DynamicResource Sanduhr.Brush.TextDim}" FontSize="11"
                                                   HorizontalAlignment="Center" VerticalAlignment="Center"
                                                   Visibility="{Binding HasNoAccounts, Converter={StaticResource BoolVis}}" />
                                    </Grid>
                                </Border>
                                <!-- Signed out is a state (Principle 6, WF32): what fills in once there is an account. -->
                                <TextBlock x:Name="SignedOutAccountsLine" Text="{x:Static core:SettingsNames.SignedOutAccountsLine}"
                                           TextWrapping="Wrap" FontSize="11" Margin="0,8,0,0"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                                           Visibility="{Binding HasNoAccounts, Converter={StaticResource BoolVis}}" />

                                <!-- Per-selection actions -->
                                <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
                                    <Button Style="{StaticResource FlatButton}" Content="Switch to"
                                            Command="{Binding SwitchToCommand}" />
                                    <Button Style="{StaticResource FlatButton}" Content="Rename…"
                                            Command="{Binding RenameCommand}" />
                                    <Button Style="{StaticResource FlatButton}" Content="Update sign-in…"
                                            Command="{Binding UpdateSignInCommand}" />
                                </StackPanel>
                                <!-- Why the buttons above are dim until a row is picked (WF29). -->
                                <TextBlock Text="{x:Static core:SettingsNames.PickAnAccount}" TextWrapping="Wrap" FontSize="10.5" Margin="0,8,0,0"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextDim}">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock">
                                            <Setter Property="Visibility" Value="Collapsed" />
                                            <Style.Triggers>
                                                <MultiDataTrigger>
                                                    <MultiDataTrigger.Conditions>
                                                        <Condition Binding="{Binding SelectedAccount}" Value="{x:Null}" />
                                                        <Condition Binding="{Binding HasNoAccounts}" Value="False" />
                                                    </MultiDataTrigger.Conditions>
                                                    <Setter Property="Visibility" Value="Visible" />
                                                </MultiDataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBlock.Style>
                                </TextBlock>
                            </StackPanel>

                            <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />

                            <!-- Sign in (WF30: the first sign-in asks for a Label). -->
                            <StackPanel views:SettingsAnchor.Id="sign-in">
                                <TextBlock Text="{x:Static core:SettingsNames.SignIn}" FontSize="13" FontWeight="SemiBold"
                                           AutomationProperties.HeadingLevel="Level2"
                                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                                <Button Style="{StaticResource AccentButton}" Content="Add account…" HorizontalAlignment="Left"
                                        Command="{Binding AddCommand}" Margin="0" />
                            </StackPanel>

                            <!-- Data for this account: the linked folder, the one per-account erase, and Remove (WF1, WF27, WF29). -->
                            <StackPanel views:SettingsAnchor.Id="data" Margin="0,16,0,0"
                                        Visibility="{Binding HasData, Converter={StaticResource BoolVis}}">
                                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,0,0,16" />
                                <TextBlock Text="{x:Static core:SettingsNames.AccountDataSection}" FontSize="13" FontWeight="SemiBold"
                                           AutomationProperties.HeadingLevel="Level2"
                                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                                <TextBlock Text="{x:Static core:SettingsNames.LinkedFolders}" FontSize="9" Margin="0,0,0,4"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                <WrapPanel Margin="0,0,0,12">
                                    <TextBlock Text="{Binding LinkedFolderText}" FontSize="11" VerticalAlignment="Center" Margin="0,0,8,0"
                                               Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                    <Button Style="{StaticResource SettingsLinkButton}" views:SettingsLinkTo.Link="claudecode#folders"
                                            Content="{x:Static core:SettingsNames.ClaudeCodeLinkTitle}" />
                                </WrapPanel>
                                <Button Style="{StaticResource FlatButton}" HorizontalAlignment="Left" Margin="0,0,0,10"
                                        Content="{x:Static core:SettingsNames.ClearAccountHistory}"
                                        AutomationProperties.Name="{Binding ClearHistoryName}"
                                        Command="{Binding ClearHistoryCommand}"
                                        Foreground="{DynamicResource Sanduhr.Brush.PaceMarker}" />
                                <Button Style="{StaticResource FlatButton}" HorizontalAlignment="Left"
                                        Content="{x:Static core:SettingsNames.RemoveAccount}"
                                        AutomationProperties.Name="{Binding RemoveName}"
                                        AutomationProperties.HelpText="{Binding RemoveCaption}"
                                        Command="{Binding SignOutCommand}"
                                        Foreground="{DynamicResource Sanduhr.Brush.PaceMarker}" />
                                <TextBlock Text="{Binding RemoveCaption}" TextWrapping="Wrap" FontSize="10.5" Margin="0,4,0,0"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                            </StackPanel>
                        </StackPanel>
                        </ScrollViewer>
                    </Border>
                </TabItem>
```

(`Remove account…` moves into Data; `Add account…` keeps its name and gets its own section. The `AccountsList` element keeps its attributes in order, which `Lists_the_close_button_and_the_shortcut_switch_are_named` pins.)

- [ ] **Step 5: The index and the names**

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`: under `// Accounts`, add

```csharp
    public const string SignInSection = "sign-in";
    public const string AccountData = "data";
```

Replace the two Accounts rows of `Anchors` with:

```csharp
        E("Accounts", SettingsPage.Accounts, AccountList,
            ["switch", "rename", "update sign-in", "active account", "account list"]),
        E(SettingsNames.FollowAccount, SettingsPage.Accounts, Follow,
            ["follow", "auto switch", "in use", "switch automatically", "every 15 minutes"]),
        E(SettingsNames.SignIn, SettingsPage.Accounts, SignInSection,
            ["sign in", "add account", "label", "Personal", "sessionKey", "session key", "cookie", "cf_clearance", "Cloudflare",
             "paste a key", "manual key"]),
        E(SettingsNames.AccountDataSection, SettingsPage.Accounts, AccountData,
            ["data", "linked folder", "linked folders", "clear history", "clear this account's history", "meter history", "remove account",
             "sign out", "delete account", "history file", SettingsNames.LinkedFolders]),
```

In `NameAnchors`, change `[SettingsNames.AddAccount] = AccountList,` to `[SettingsNames.AddAccount] = SignInSection,` and add `[SettingsNames.ClearAccountHistory] = AccountData,` and `[SettingsNames.RemoveAccount] = AccountData,`. In the History chart row's synonyms, remove `"clear history"` from the `HistoryExport` row (Clear left that view).

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, add to `Table` after `(FollowAccount, SettingsPage.Accounts),`:

```csharp
        (ClearAccountHistory, SettingsPage.Accounts),
        (RemoveAccount, SettingsPage.Accounts),
```

and to `Retired`, after the `("Account name", …)` row:

```csharp
        // Slice 5: History's Clear became Accounts' per-account erase (WF1).
        ("Clear history", ClearAccountHistory, true),
```

- [ ] **Step 6: Run them to see them pass**

Run: `git rm windows-dotnet/tests/Sanduhr.App.Tests/HistoryClearTests.cs`
Then: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~AccountsDataTests|FullyQualifiedName~SettingsReachTests|FullyQualifiedName~SettingsWindowNamesTests|FullyQualifiedName~AppWindowNamesTests" > %TEMP%\slice5-t7-app.log 2>&1`
Then: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSlice5IndexTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsReachIndexTests|FullyQualifiedName~UsageHistoryTests" > %TEMP%\slice5-t7-core.log 2>&1`
Expected: PASS. `Accounts_and_the_key_window_read_their_copy_from_the_table` still finds `FollowAccountCaption` and `PickAnAccount` in the Accounts TabItem.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/AccountsViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/HistoryTabViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.App.Tests/AccountsDataTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsSlice5IndexTests.cs
git commit -m "feat(settings-v2): Accounts, Sign in and Data for this account: the linked folder, Clear this account's history, Remove says what it removes"
```

(`git rm` already staged the deleted `HistoryClearTests.cs`.)

---

### Task 8: Claude Usage, Meters; the History page retires and its links land

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeTabViewModel.cs` (`Meters`, the default view, `SetMeters`, `RefreshActiveAsync`)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (pass `History` as Meters)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/HistoryTabViewModel.cs` (empty lines and titles from the table)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/HistoryChart.cs:80-88` (the dead fallback text)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (`CcNavMeters`, `CcSectionMeters`, the Meters pill and view; the History TabItem deleted)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs` (`Tabs_SelectionChanged`, `SelectPage`'s view switch, the class summary)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (`MeterHistory`, `MeterHistoryExport`, `MetersView`; the rows; `PageSynonyms`; `NameAnchors`)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (`Sidebar`, `Table`, `Retired`)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/ClaudeUsageMetersTests.cs`; append to `SettingsSlice5IndexTests.cs`; modify `SettingsIndexTests.cs`, `SettingsLinkTests.cs`, `SettingsNamesTests.cs`, `SettingsReachTests.cs` (App)

**Interfaces:**
- Consumes: `HistoryTabViewModel.Refresh` (Task 7); Task 3's Meters names.
- Produces: `ClaudeCodeTabViewModel(WidgetViewModel widget, HistoryTabViewModel meters, LocalCcViewModel overview, CcTrendsViewModel trends, CcLedgerViewModel ledger)`, `.Meters`, `.SetMetersCommand`, default `Section == SettingsIndex.MetersView`; `SettingsIndex.MeterHistory = "meters"`, `MeterHistoryExport = "meters-export"`, `MetersView = "Meters"`. `SettingsPage.History` leaves `SettingsNames.Pages`; `PageTitle(History)` stays "History" for the aliases and logs.

- [ ] **Step 1: Write the failing tests**

Append to the class in `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice5IndexTests.cs`:

```csharp
    [Fact]
    public void Old_history_links_land_on_the_Meters_view()
    {
        Assert.DoesNotContain(SettingsPage.History, SettingsNames.Pages);
        Assert.Equal(new SettingsLink(SettingsPage.ClaudeUsage, SettingsIndex.MeterHistory), SettingsIndex.ParseLink("history"));
        Assert.Equal(new SettingsLink(SettingsPage.ClaudeUsage, SettingsIndex.MeterHistory), SettingsIndex.ParseLink("history#chart"));
        Assert.Equal(new SettingsLink(SettingsPage.ClaudeUsage, SettingsIndex.MeterHistoryExport), SettingsIndex.ParseLink("history#export"));
        Assert.Equal(new SettingsLink(SettingsPage.ClaudeUsage, SettingsIndex.MeterHistory), SettingsIndex.LinkFromArgs(["--settings", "history"]));
        Assert.Equal(SettingsIndex.MetersView, SettingsIndex.Find(SettingsPage.ClaudeUsage, SettingsIndex.MeterHistory)!.Subsection);
        Assert.Equal(SettingsIndex.MetersView, SettingsIndex.Find(SettingsPage.ClaudeUsage, SettingsIndex.MeterHistoryExport)!.Subsection);
    }

    [Fact]
    public void History_words_find_the_Meters_view()
    {
        Assert.Equal((SettingsPage.ClaudeUsage, SettingsIndex.MeterHistory), (Top("meter history").Entry.Page, Top("meter history").Entry.Anchor));
        // "history" alone also finds Accounts' "Clear this account's history…" first (Accounts comes before Claude Usage in the sidebar).
        Assert.Contains(SettingsIndex.Search("history").Take(3), h => (h.Entry.Page, h.Entry.Anchor) == (SettingsPage.ClaudeUsage, SettingsIndex.MeterHistory));
        Assert.Equal((SettingsPage.ClaudeUsage, SettingsIndex.MeterHistoryExport), (Top("csv").Entry.Page, Top("csv").Entry.Anchor));
    }

    [Fact]
    public void The_History_page_title_is_retired()
    {
        Assert.NotEmpty(SettingsNames.RetiredNames("History"));
        Assert.NotEmpty(SettingsNames.RetiredNames("History chart"));
        Assert.NotEmpty(SettingsNames.RetiredNames("History settings…"));
        Assert.Empty(SettingsNames.RetiredNames(SettingsNames.MeterHistorySection));
        Assert.NotEmpty(SettingsNames.PathProblems("Settings, History"));
        Assert.Equal("History", SettingsNames.PageTitle(SettingsPage.History));   // kept for the aliases and logs
    }
```

In `windows-dotnet/tests/Sanduhr.Tests/SettingsIndexTests.cs`:
- In `Subsection_is_set_only_on_Claude_Usage_and_always_there`, change the views array to `string?[] views = [SettingsIndex.MetersView, SettingsIndex.OverviewView, SettingsIndex.TrendsView, SettingsIndex.SessionsView];`.
- In `Csv_first_hit_is_the_meter_history_export`, change the comment's second line to "a synonym (rank 1); both sit on Claude Usage, and the tie goes to index order: Meters before Sessions." and the two page/anchor asserts to `Assert.Equal(SettingsPage.ClaudeUsage, top.Entry.Page);` and `Assert.Equal(SettingsIndex.MeterHistoryExport, top.Entry.Anchor);`.
- In `Old_words_still_find_the_renamed_controls`, change the two History rows to `[InlineData("export", SettingsPage.ClaudeUsage, SettingsIndex.MeterHistoryExport)]` and `[InlineData("meter history", SettingsPage.ClaudeUsage, SettingsIndex.MeterHistoryExport)]`.

In `windows-dotnet/tests/Sanduhr.Tests/SettingsLinkTests.cs`, change both `[InlineData("history#chart", SettingsPage.History, "chart")]` rows to `[InlineData("history#chart", SettingsPage.ClaudeUsage, "meters")]   // History retired in slice 5: its alias lands on Meters`.

In `windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs`, change `SidebarPages` to

```csharp
    private static readonly SettingsPage[] SidebarPages =
        [.. Enum.GetValues<SettingsPage>().Where(p => p is not SettingsPage.Widget and not SettingsPage.Widgets and not SettingsPage.History)];
```

(its comment already says "History in slice 5").

In `windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs`:
- In `The_meter_history_says_why_it_is_empty`, replace `HistoryTabViewModel.SignedOutLine` with `SettingsNames.MeterHistorySignedOut` and `HistoryTabViewModel.NoHistoryLine` with `SettingsNames.MeterHistoryEmpty`.
- Rename `Signed_out_the_History_page_reads_its_empty_line` to `Signed_out_the_Meters_view_reads_its_empty_line`, change its `Land(window, guard, SettingsPage.History, SettingsIndex.Chart);` to `Land(window, guard, SettingsPage.ClaudeUsage, SettingsIndex.MeterHistory);` and its `HistoryTabViewModel.SignedOutLine` to `SettingsNames.MeterHistorySignedOut`.

Create `windows-dotnet/tests/Sanduhr.App.Tests/ClaudeUsageMetersTests.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 5: the meter history is Claude Usage's first view (WF14), and the History page's old links land on it.</summary>
[Collection("SettingsWindow")]
public sealed class ClaudeUsageMetersTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Svm, NoActivate Guard) Open(params string[] accounts)
    {
        var (widget, _) = ScratchWidget(accounts);
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        return (window, svm, NoActivate.Show(window));
    }

    private static void Settle(SettingsWindow window, NoActivate guard)
    {
        window.UpdateLayout();
        Pump();
        window.UpdateLayout();
        Pump();
        guard.Check();
    }

    private static TabItem TabOf(SettingsWindow window, SettingsPage page) =>
        ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == page.ToString());

    [Fact]
    public void Claude_Usage_opens_on_Meters_with_the_empty_line_until_there_is_history() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.ClaudeUsage);
            Settle(window, guard);
            Assert.Equal(SettingsIndex.MetersView, svm.ClaudeCode.Section);
            var line = (TextBlock)window.FindName("HistoryEmptyText");
            Assert.True(line.IsVisible, $"the empty line is not shown on {window.Place}");
            Assert.Equal(SettingsNames.MeterHistoryEmpty, line.Text);
            Assert.Same(svm.History, svm.ClaudeCode.Meters);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_history_link_lands_on_the_meters_view_in_view() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            svm.ClaudeCode.Section = SettingsIndex.SessionsView;
            window.SelectPage(SettingsPage.History, SettingsIndex.HistoryExport);
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.ClaudeUsage, SettingsIndex.MeterHistoryExport), window.Place);
            Assert.Equal(SettingsIndex.MetersView, svm.ClaudeCode.Section);
            var export = SettingsAnchor.Find((DependencyObject)TabOf(window, SettingsPage.ClaudeUsage).Content, SettingsIndex.MeterHistoryExport)!;
            Assert.True(export.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_Meters_pill_shows_the_view_and_redraws_the_chart() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.ClaudeUsage, SettingsIndex.Sessions);
            Settle(window, guard);
            int redraws = 0;
            svm.History.Changed += () => redraws++;

            svm.ClaudeCode.SetMetersCommand.Execute(null);
            Settle(window, guard);

            Assert.Equal(SettingsIndex.MetersView, svm.ClaudeCode.Section);
            Assert.True(redraws > 0, "the chart was not redrawn");
            Assert.True(((FrameworkElement)window.FindName("Chart")).IsVisible || ((FrameworkElement)window.FindName("HistoryEmptyText")).IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_card_that_named_History_says_it_opens_Claude_Usage()
    {
        var card = WhatsNewCard.Of("3.0.0", "history-card", "Meter history", "Body.", "", WhatsNewPreview.None, SettingsPage.History);
        Assert.Equal("Opens Settings, Claude Usage", new WhatsNewCardItem(card, null).ShowMeToolTip);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSlice5IndexTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsLinkTests" > %TEMP%\slice5-t8-red.log 2>&1`
Expected: build FAILS with `'SettingsIndex' does not contain a definition for 'MeterHistory'`.

- [ ] **Step 3: The view models and the chart**

Replace the body of `windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeTabViewModel.cs`'s class header through `RefreshActiveAsync`, and add `SetMeters`, so the file reads (usings first):

```csharp
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Parent of Settings, Claude Usage: owns the Meters / Overview / Trends / Sessions views and fans refreshes to the one shown.
/// Meters is the 30-day meter history (Settings v2, slice 5: it was the History page) and the first view; the other three read the
/// session archive and the live session logs.
/// </summary>
public sealed partial class ClaudeCodeTabViewModel : ObservableObject
{
    private readonly WidgetViewModel _widget;
    private readonly Action _ingestHandler;

    /// <summary>The meter history (WF14). The same object as <see cref="SettingsViewModel.History"/>.</summary>
    public HistoryTabViewModel Meters { get; }

    public LocalCcViewModel Overview { get; }

    public CcTrendsViewModel Trends { get; }

    public CcLedgerViewModel Ledger { get; }

    /// <summary>Set by the window on tab selection — ingest-completed refreshes
    /// only run while the user can see the tab.</summary>
    public bool IsTabActive { get; set; }

    /// <summary>The view shown: Meters first (owner taste 1), then wherever the person or a link left it this window.</summary>
    [ObservableProperty] private string _section = SettingsIndex.MetersView;

    public ClaudeCodeTabViewModel(WidgetViewModel widget, HistoryTabViewModel meters, LocalCcViewModel overview, CcTrendsViewModel trends,
        CcLedgerViewModel ledger)
    {
        _widget = widget;
        Meters = meters;
        Overview = overview;
        Trends = trends;
        Ledger = ledger;
```

(the rest of the constructor, `Attach` and `Detach` stay as they are), and in `RefreshActiveAsync` add the first case:

```csharp
            case SettingsIndex.MetersView:
                Meters.Refresh();   // the chart is drawn by the window; a view switch must redraw it at its new size
                break;
```

and before `SetOverview` add:

```csharp
    [RelayCommand]
    private async Task SetMeters()
    {
        Section = SettingsIndex.MetersView;
        await RefreshActiveAsync();
    }
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`, change `ClaudeCode = new ClaudeCodeTabViewModel(widget, new LocalCcViewModel(` to `ClaudeCode = new ClaudeCodeTabViewModel(widget, History, new LocalCcViewModel(`, and the two summaries: `History`'s to "Backs Claude Usage, Meters (the 30-day meter history; the History page until Settings v2, slice 5). The same object as ClaudeCode.Meters." and `ClaudeCode`'s to "Backs Claude Usage: Meters, Overview, Trends and Sessions.".

In `windows-dotnet/src/Sanduhr.App/ViewModels/HistoryTabViewModel.cs`:
- Delete `internal const string SignedOutLine = …;` and `internal const string NoHistoryLine = …;`, and change `EmptyLine` to
  `internal static string EmptyLine(bool signedIn, bool hasRows) => hasRows ? "" : signedIn ? SettingsNames.MeterHistoryEmpty : SettingsNames.MeterHistorySignedOut;`
- In `ExportCsv`, change `Title = "Export usage history",` to `Title = SettingsNames.ExportMeterHistoryTitle,`.
- In `Info`, change `ThemedDialog.Show(_owner, "History", …` to `ThemedDialog.Show(_owner, SettingsNames.MeterHistoryDialogTitle, …`.
- Change the class summary's first sentence to "Backs Settings, Claude Usage, Meters — the 30-day meter history with the per-account vs all-accounts overlay, the Week/Month window selector, the legend, and the CSV export." and `AccountOption`'s summary "History tab's selector" to "Meters view's selector".

In `windows-dotnet/src/Sanduhr.App/Views/HistoryChart.cs`, replace the block

```csharp
        if (_rows.Count == 0)
        {
            var ft = MakeText("No history yet.\nData accumulates as you use Sanduhr.",
                typeface, FontSize, textDim, dpi);
            ft.TextAlignment = TextAlignment.Center;
            ft.MaxTextWidth = w;
            dc.DrawText(ft, new Point(0, (h - ft.Height) / 2));
            return;
        }
```

with

```csharp
        // Nothing to draw: the Meters view hides the chart and shows its empty-state line instead (WF32, WF35; slice 2 carry T15).
        if (_rows.Count == 0)
            return;
```

If the build then warns that `textDim` is unused, delete its declaration line too.

- [ ] **Step 4: The XAML and the window**

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`:

Before `<Style x:Key="CcNavOverview" …>`, add

```xml
        <Style x:Key="CcNavMeters" TargetType="Button" BasedOn="{StaticResource FlatButton}">
            <Style.Triggers>
                <DataTrigger Binding="{Binding Section}" Value="Meters">
                    <Setter Property="Background" Value="{DynamicResource Sanduhr.Brush.Accent}" />
                    <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.Bg}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
```

and before `<Style x:Key="CcSectionOverview" TargetType="Grid">`, add

```xml
        <Style x:Key="CcSectionMeters" TargetType="Grid">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding DataContext.Section, RelativeSource={RelativeSource AncestorType=TabItem}}" Value="Meters">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
```

In the Claude Usage TabItem's sub-nav, before the Overview button, add

```xml
                                <Button Content="Meters" Style="{StaticResource CcNavMeters}"
                                        Command="{Binding SetMetersCommand}" />
```

and right after the sub-nav's closing `</StackPanel>` (before `<!-- Overview -->`), add the view:

```xml
                            <!-- Meters (Settings v2, slice 5): the 30-day meter history, the History page until now (WF14). Its DataContext is
                                 the HistoryTabViewModel; the window pushes the chart's rows (SettingsWindow.RenderHistory). -->
                            <Grid Grid.Row="1" Style="{StaticResource CcSectionMeters}" DataContext="{Binding Meters}">
                                <Grid.RowDefinitions>
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="*" />
                                    <RowDefinition Height="Auto" />
                                </Grid.RowDefinitions>

                                <TextBlock Grid.Row="0" TextWrapping="Wrap" FontSize="11" Margin="0,0,0,12"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                                           Text="{x:Static core:SettingsNames.MetersIntro}" />

                                <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,8" views:SettingsAnchor.Id="meters">
                                    <TextBlock Text="Window:" VerticalAlignment="Center" FontSize="12"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,0,8,0" />
                                    <Button Content="Week" Style="{StaticResource RangePillWeek}"
                                            Command="{Binding SetWeekCommand}" />
                                    <Button Content="Month" Style="{StaticResource RangePillMonth}"
                                            Command="{Binding SetMonthCommand}" />
                                    <TextBlock Text="Account:" VerticalAlignment="Center" FontSize="12"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="12,0,8,0" />
                                    <ComboBox Style="{StaticResource ThemedComboBox}" Width="170"
                                              VerticalAlignment="Center"
                                              AutomationProperties.Name="Account, meter history"
                                              ItemsSource="{Binding AccountOptions}"
                                              DisplayMemberPath="Display"
                                              SelectedItem="{Binding SelectedOption, Mode=TwoWay}" />
                                </StackPanel>

                                <!-- Legend — only when 'All accounts' is selected. -->
                                <ItemsControl Grid.Row="2" ItemsSource="{Binding Legend}" Margin="0,0,0,4" Focusable="False"
                                              Visibility="{Binding ShowLegend, Converter={StaticResource BoolVis}}">
                                    <ItemsControl.ItemsPanel>
                                        <ItemsPanelTemplate>
                                            <WrapPanel Orientation="Horizontal" />
                                        </ItemsPanelTemplate>
                                    </ItemsControl.ItemsPanel>
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <StackPanel Orientation="Horizontal" Margin="0,0,12,2" VerticalAlignment="Center">
                                                <Ellipse Width="10" Height="10" VerticalAlignment="Center"
                                                         Fill="{Binding Swatch}" Margin="0,0,5,0" />
                                                <TextBlock Text="{Binding Label}" FontSize="11" VerticalAlignment="Center"
                                                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                            </StackPanel>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>

                                <!-- The chart; with nothing to draw it gives way to a line a screen reader can read (WF32, WF35). -->
                                <Border Grid.Row="3" Background="{DynamicResource Sanduhr.Brush.Bg}" CornerRadius="6" MinHeight="160"
                                        BorderBrush="{DynamicResource Sanduhr.Brush.Border}" BorderThickness="1"
                                        Padding="8" Margin="0,4,0,0">
                                    <Grid>
                                        <views:HistoryChart x:Name="Chart">
                                            <views:HistoryChart.Style>
                                                <Style TargetType="views:HistoryChart">
                                                    <Style.Triggers>
                                                        <DataTrigger Binding="{Binding HasEmptyText}" Value="True">
                                                            <Setter Property="Visibility" Value="Hidden" />
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </views:HistoryChart.Style>
                                        </views:HistoryChart>
                                        <TextBlock x:Name="HistoryEmptyText" Text="{Binding EmptyText}" TextWrapping="Wrap" FontSize="12"
                                                   HorizontalAlignment="Center" VerticalAlignment="Center" TextAlignment="Center" Margin="12"
                                                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                                                   views:LiveRegion.Shown="{Binding HasEmptyText}"
                                                   Visibility="{Binding HasEmptyText, Converter={StaticResource BoolVis}}" />
                                    </Grid>
                                </Border>

                                <StackPanel Grid.Row="4" Orientation="Horizontal" Margin="0,12,0,0" views:SettingsAnchor.Id="meters-export">
                                    <Button Style="{StaticResource AccentButton}" Content="{x:Static core:SettingsNames.ExportMeterHistory}"
                                            Command="{Binding ExportCsvCommand}" Margin="0" />
                                </StackPanel>
                            </Grid>
```

Delete the whole History TabItem (from `<!-- History tab (item 9) — 30-day usage chart + CSV export. -->` through its `</TabItem>`). Update the Claude Usage TabItem's leading comment to "Claude Usage (WS-C; Settings v2, slice 5): Meters / Overview / Trends / Sessions over the meter history, the session archive and the live session-log reader.".

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs`:
- In `Tabs_SelectionChanged`, replace

```csharp
        else
        {
            _localCcTimer.Stop();
            if (page == SettingsPage.History)
                RenderHistory();
        }
```

with

```csharp
        else
        {
            _localCcTimer.Stop();
        }
```

(`RefreshActiveAsync` redraws Meters on selection: `Meters.Refresh` raises `Changed`, which `RenderHistory` follows.)
- In `SelectPage`'s view switch, add `SettingsIndex.MetersView => ViewModel.ClaudeCode.SetMetersCommand,` above the Trends arm.
- In the class summary, change "The History chart and the Claude Usage Overview bar strip are custom" to "The meter history chart (Claude Usage, Meters) and the Overview bar strip are custom", and in `Tabs_SelectionChanged`'s summary change "The History chart re-renders on (re)selection too" to "Meters re-renders through its refresh".

- [ ] **Step 5: The index and the names**

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`:
- Change the `// History` comment above `Chart` to `// History (retired in slice 5): kept for the aliases to Claude Usage, Meters`.
- Under `// Claude Usage`, add before `Overview`:

```csharp
    /// <summary>Claude Usage, Meters (slice 5): the meter history and its export.</summary>
    public const string MeterHistory = "meters";
    public const string MeterHistoryExport = "meters-export";
```

- Beside `OverviewView`, add `public const string MetersView = "Meters";` first.
- Replace the two History rows of `Anchors` (`"History chart"` and the `ExportMeterHistory` row on `SettingsPage.History`) with, placed right before the Overview row:

```csharp
        E(SettingsNames.MeterHistorySection, SettingsPage.ClaudeUsage, MeterHistory,
            ["history", "30 days", "week", "month", "account", "all accounts", "chart", "graph", "meters over time"], MetersView),
        E(SettingsNames.ExportMeterHistory, SettingsPage.ClaudeUsage, MeterHistoryExport,
            ["csv", "export", "export csv", "spreadsheet"], MetersView),
```

- In `PageSynonyms`, delete the `[SettingsPage.History] = …` line.
- In `NameAnchors`, change `[SettingsNames.ExportMeterHistory] = HistoryExport,` to `[SettingsNames.ExportMeterHistory] = MeterHistoryExport,`.

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`:
- In `Sidebar`, change `new("Usage", [SettingsPage.History, SettingsPage.ClaudeUsage, SettingsPage.Publish]),` to `new("Usage", [SettingsPage.ClaudeUsage, SettingsPage.Publish]),`, and in its summary "Retired pages (Widgets since slice 4)" to "Retired pages (Widgets since slice 4, History since slice 5)".
- In `Table`, change `(ExportMeterHistory, SettingsPage.History),` to `(ExportMeterHistory, SettingsPage.ClaudeUsage),`.
- In `PageTitle`, change `SettingsPage.History => "History",` to `SettingsPage.History => "History",   // retired in slice 5: keeps its old title for aliases and logs`.
- In `Retired`, after the `("Clear history", …)` row, add:

```csharp
        // Slice 5: the History page is Claude Usage's Meters view (WF14). Whole only: "meter history" and the Meters view's names are live.
        ("History", PageTitle(SettingsPage.ClaudeUsage), true),
        ("History chart", MeterHistorySection, true),
        ("History settings…", LinkTitle(SettingsPage.ClaudeUsage), true),
```

- [ ] **Step 6: Run them to see them pass**

First: `git grep -n "\"History\"\|SettingsPage.History\b" -- windows-dotnet/src/Sanduhr.App` prints only lines that name the enum for an alias (none is expected; fix any visible "History" literal it finds by using `SettingsNames.MeterHistoryDialogTitle`).

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSlice5IndexTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsLinkTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsReachIndexTests|FullyQualifiedName~SettingsDeskSplitIndexTests|FullyQualifiedName~WhatsNewTests" > %TEMP%\slice5-t8-core.log 2>&1`
Then: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~ClaudeUsageMetersTests|FullyQualifiedName~SettingsReachTests|FullyQualifiedName~SettingsWindowNamesTests|FullyQualifiedName~CcCalendarControlTests|FullyQualifiedName~WhatsNewShowMeTests" > %TEMP%\slice5-t8-app.log 2>&1`
Expected: PASS. `The_sidebar_in_XAML_matches_the_names_table` passes because the History TabItem and `SettingsPage.History` left together; `Every_alias_whose_target_page_exists_lands_there_once_its_source_is_retired` and `Every_Settings_card_names_an_anchor_in_the_index` now exercise live History aliases.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeTabViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/HistoryTabViewModel.cs windows-dotnet/src/Sanduhr.App/Views/HistoryChart.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.App.Tests/ClaudeUsageMetersTests.cs windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsSlice5IndexTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsIndexTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsLinkTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs
git commit -m "feat(settings-v2): the meter history is Claude Usage's first view; the History page retires and every old link lands on Meters"
```

---

### Task 9: The Widget page: the widget's own options

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetPageViewModel.cs`
- Create: `windows-dotnet/src/Sanduhr.App/Views/WidgetPage.xaml`, `windows-dotnet/src/Sanduhr.App/Views/WidgetPage.xaml.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (a Widget TabItem before Themes)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs` (`OpenLink`, `SelectPage`'s summary, `Closed`)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (`WidgetPage`)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WhatsNewViewModel.cs` (`ShowMeToolTip`)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (`ShowMe`, `OpenSettingsLink`)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (`WidgetOptions`, its row, `PageSynonyms`, `NameAnchors`, `MenuAnchor`, `ShowsTheWidget`)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (`Sidebar`, its summary, `PageTitle`'s Widget comment, `Table`)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/WidgetPageTests.cs`; append to `SettingsSlice5IndexTests.cs`; modify `SettingsNamesTests.cs` (`Sidebar_has_every_page_but_Widget_exactly_once`)

**Interfaces:**
- Consumes: `WidgetViewModel.IsCompact`, `ToggleCompactCommand`, `SparklineStyle`, `SetSparklineStyle`, `Pinned`, `SetPinned`, `LoadFocusDuration`, `SaveFocusDuration`, `ShownLimitOrder`, `SetLimitOrder`, `LimitsChanged` (Task 5); `LimitSettings.Move` (Task 1); Task 3's Widget names.
- Produces: `public sealed record GraphStyleOption(SparklineStyle Value, string Display)`; `public sealed partial class LimitOrderRowViewModel` (`Key`, `Label`, `CanMoveUp`, `CanMoveDown`, `MoveUpName`, `MoveDownName`, `MoveUpCommand`, `MoveDownCommand`); `public sealed partial class WidgetPageViewModel` (`PageTitle`, `GraphStyles`, `Limits`, `IsCompact`, `ToggleCompactCommand`, `SelectedGraphStyle`, `KeepOnTop`, `DeepWorkMinutes`, `DeepWorkHint`, `HasDeepWorkHint`, `internal void Move(string key, int delta)`, `Detach()`); `SettingsViewModel.WidgetPage`; `SettingsIndex.WidgetOptions = "widget-options"`; `public static bool SettingsIndex.ShowsTheWidget(SettingsLink link)`. Task 10 adds Just the Desk to the same page and view model.

- [ ] **Step 1: Write the failing tests**

Append to the class in `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice5IndexTests.cs`:

```csharp
    [Fact]
    public void Widget_is_a_page_and_widget_without_an_anchor_still_shows_the_widget()
    {
        Assert.Contains(SettingsPage.Widget, SettingsNames.Pages);
        Assert.Equal("Widget", SettingsNames.GroupTitle(SettingsPage.Widget));
        Assert.Equal([SettingsPage.Widget, SettingsPage.Themes], SettingsNames.Sidebar.Single(g => g.Title == "Widget").Pages);
        Assert.True(SettingsIndex.ShowsTheWidget(new SettingsLink(SettingsPage.Widget, null)));
        Assert.True(SettingsIndex.ShowsTheWidget(SettingsIndex.ParseLink("widget")!));
        Assert.True(SettingsIndex.ShowsTheWidget(SettingsIndex.ParseLink("widget#tray")!));   // an anchor the page doesn't have drops
        Assert.False(SettingsIndex.ShowsTheWidget(SettingsIndex.ParseLink("widget#widget-options")!));
        Assert.Equal(new SettingsLink(SettingsPage.Widget, SettingsIndex.WidgetOptions), SettingsIndex.ParseLink("widget#widget-options"));
        Assert.Equal(SettingsIndex.WidgetOptions, SettingsIndex.MenuAnchor(SettingsPage.Widget));
        foreach (var card in WhatsNewCards.Table.Where(c => c.Destination == SettingsPage.Widget))
            Assert.True(SettingsIndex.ShowsTheWidget(new SettingsLink(card.Destination, card.Anchor)), card.Id);
    }

    [Theory]
    [InlineData("compact")]
    [InlineData("graph style")]
    [InlineData("on top")]
    [InlineData("deep work")]
    [InlineData("limit order")]
    [InlineData("tier order")]
    public void Widget_words_find_the_widget_options(string query) =>
        Assert.Contains(SettingsIndex.Search(query).Take(3), h => h.Entry.Page == SettingsPage.Widget && h.Entry.Anchor == SettingsIndex.WidgetOptions);
```

In `windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs`, change `SidebarPages` to

```csharp
    // Retired pages stay in the enum for their aliases (old cards, agent text, --settings links) but leave the sidebar: Widgets in
    // slice 4 (P4), History in slice 5. Widget is a page since slice 5 (the widget itself is Widget with no anchor).
    private static readonly SettingsPage[] SidebarPages =
        [.. Enum.GetValues<SettingsPage>().Where(p => p is not SettingsPage.Widgets and not SettingsPage.History)];
```

and replace `Sidebar_has_every_page_but_Widget_exactly_once` with:

```csharp
    [Fact]
    public void Sidebar_has_every_live_page_exactly_once()
    {
        foreach (var page in SidebarPages)
            Assert.Single(SettingsNames.Pages, p => p == page);
        Assert.Contains(SettingsPage.Widget, SettingsNames.Pages);
        Assert.Equal(SidebarPages.Length, SettingsNames.Pages.Count);
    }
```

Create `windows-dotnet/tests/Sanduhr.App.Tests/WidgetPageTests.cs`:

```csharp
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 5: the Widget page (WF15, WF24). Every widget-only choice gets a Settings control.</summary>
[Collection("SettingsWindow")]
public sealed class WidgetPageTests : AppWindowFixture
{
    private JsonObject Settings() => (JsonObject)JsonNode.Parse(File.ReadAllText(ScratchPaths().SettingsFile))!;

    [Fact]
    public void The_page_follows_the_widget_and_writes_through_it() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var page = new WidgetPageViewModel(widget);
        try
        {
            page.SelectedGraphStyle = WidgetPageViewModel.GraphStyles.Single(o => o.Value == SparklineStyle.Horizon);
            page.KeepOnTop = true;
            page.DeepWorkMinutes = "45";
            page.ToggleCompactCommand.Execute(null);

            Assert.Equal(SparklineStyle.Horizon, widget.SparklineStyle);
            Assert.True(widget.Pinned);
            Assert.Equal(45, widget.LoadFocusDuration());
            Assert.True(page.IsCompact);
            Assert.Equal(("Horizon", true, 45), ((string?)Settings()["sparkline_style"], (bool?)Settings()["pinned"], (int?)Settings()["focus_mode_duration"]));

            // The widget's strip changes it (its cycle calls SetSparklineStyle, then plays a sound a test doesn't want): the page follows.
            widget.SetSparklineStyle(SparklineStyle.Classic);
            Assert.Equal(SparklineStyle.Classic, page.SelectedGraphStyle.Value);
        }
        finally { page.Detach(); }
    });

    [Theory]
    [InlineData("0")]
    [InlineData("121")]
    [InlineData("ten")]
    [InlineData("")]
    public void A_Deep_Work_length_out_of_range_is_not_saved_and_says_why(string typed) => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var page = new WidgetPageViewModel(widget);
        try
        {
            page.DeepWorkMinutes = typed;
            Assert.Equal(25, widget.LoadFocusDuration());
            Assert.True(page.HasDeepWorkHint);
            Assert.Equal(SettingsNames.DeepWorkRange, page.DeepWorkHint);
        }
        finally { page.Detach(); }
    });

    [Fact]
    public void Limit_order_moves_a_limit_and_offers_no_move_off_the_ends() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        widget.StartDemo();
        var page = new WidgetPageViewModel(widget);
        try
        {
            Assert.Equal(widget.ShownLimitOrder, page.Limits.Select(r => r.Key));
            Assert.False(page.Limits[0].CanMoveUp);
            Assert.False(page.Limits[^1].CanMoveDown);
            var second = page.Limits[1];
            Assert.True(second.CanMoveUp);

            second.MoveUpCommand.Execute(null);

            Assert.Equal(second.Key, widget.ShownLimitOrder[0]);
            Assert.Same(second, page.Limits[0]);   // synced in place
            Assert.False(second.CanMoveUp);
            Assert.Equal(second.Key, (string?)((JsonArray)Settings()["tier_order"]!)[0]);
        }
        finally { page.Detach(); }
    });

    [Fact]
    public void The_page_sits_first_in_the_Widget_group_and_opens_on_its_options() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        try
        {
            window.SelectPage(SettingsPage.Widget, SettingsIndex.WidgetOptions);
            window.UpdateLayout();
            Pump();
            guard.Check();
            var tabs = ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().ToList();
            var tab = tabs.Single(t => (string)t.Tag == "Widget");
            Assert.Equal(tabs.IndexOf(tabs.Single(t => (string)t.Tag == "Themes")) - 1, tabs.IndexOf(tab));
            Assert.Equal("Widget", SettingsSidebar.GetGroupHeader(tab));
            Assert.True(SettingsAnchor.Find((DependencyObject)tab.Content, SettingsIndex.WidgetOptions)!.IsVisible);
            Assert.Equal(new SettingsLink(SettingsPage.Widget, SettingsIndex.WidgetOptions), window.Place);
        }
        finally { window.Close(); }
    });
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSlice5IndexTests|FullyQualifiedName~SettingsNamesTests" > %TEMP%\slice5-t9-red.log 2>&1`
Expected: build FAILS with `'SettingsIndex' does not contain a definition for 'ShowsTheWidget'`.

- [ ] **Step 3: The view model**

Create `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetPageViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>A Graph style choice: the style and the words the strip's tooltip uses for it.</summary>
public sealed record GraphStyleOption(SparklineStyle Value, string Display)
{
    public override string ToString() => Display;
}

/// <summary>One row of Settings, Widget, Limit order: a limit the widget shows, with Move up and Move down where they can act.</summary>
public sealed partial class LimitOrderRowViewModel : ObservableObject
{
    private readonly WidgetPageViewModel _page;

    public string Key { get; }
    public string Label { get; }

    /// <summary>Not the first row; the button is collapsed otherwise (no control that can't act).</summary>
    [ObservableProperty] private bool _canMoveUp;

    /// <summary>Not the last row.</summary>
    [ObservableProperty] private bool _canMoveDown;

    public LimitOrderRowViewModel(WidgetPageViewModel page, string key)
    {
        _page = page;
        Key = key;
        Label = TierModel.Label(key);
    }

    /// <summary>"Move up, Weekly - Opus": the visible label first (WCAG 2.5.3), then the limit.</summary>
    public string MoveUpName => SettingsNames.MoveLimitUp(Label);
    public string MoveDownName => SettingsNames.MoveLimitDown(Label);

    [RelayCommand]
    private void MoveUp() => _page.Move(Key, -1);

    [RelayCommand]
    private void MoveDown() => _page.Move(Key, +1);

    public override string ToString() => Label;
}

/// <summary>
/// Settings, Widget (Settings v2, slice 5; WF15, WF24): the widget's own options, which had no Settings home. Every control writes
/// through the widget, which saves its key and raises the change, so the strip, the widget menu and this page never disagree.
/// </summary>
public sealed partial class WidgetPageViewModel : ObservableObject
{
    private readonly WidgetViewModel _widget;

    public static IReadOnlyList<GraphStyleOption> GraphStyles { get; } =
    [
        new(SparklineStyle.Classic, SettingsNames.GraphStyleClassic),
        new(SparklineStyle.Horizon, SettingsNames.GraphStyleHorizon),
    ];

    /// <summary>Limit order's rows, synced in place so a moved row keeps its buttons and its keyboard focus.</summary>
    public ObservableCollection<LimitOrderRowViewModel> Limits { get; } = new();

    public WidgetPageViewModel(WidgetViewModel widget)
    {
        _widget = widget;
        _deepWorkMinutes = widget.LoadFocusDuration().ToString(CultureInfo.InvariantCulture);
        _widget.PropertyChanged += OnWidgetChanged;
        _widget.LimitsChanged += ReloadLimits;
        ReloadLimits();
    }

    /// <summary>The page's title, from the names table.</summary>
    public string PageTitle => SettingsNames.PageTitle(SettingsPage.Widget);

    /// <summary>Compact mode: the widget's in-memory state (Ctrl+D); no key (decision 16).</summary>
    public bool IsCompact => _widget.IsCompact;
    public ICommand ToggleCompactCommand => _widget.ToggleCompactCommand;

    /// <summary>Graph style ("sparkline_style").</summary>
    public GraphStyleOption SelectedGraphStyle
    {
        get => GraphStyles.FirstOrDefault(o => o.Value == _widget.SparklineStyle) ?? GraphStyles[0];
        set
        {
            if (value is not null)
                _widget.SetSparklineStyle(value.Value);
        }
    }

    /// <summary>Keep the widget on top ("pinned").</summary>
    public bool KeepOnTop
    {
        get => _widget.Pinned;
        set => _widget.SetPinned(value);
    }

    /// <summary>Deep Work length in minutes ("focus_mode_duration"), 1 to 120 like the focus timer's own stepper.</summary>
    [ObservableProperty] private string _deepWorkMinutes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDeepWorkHint))]
    private string _deepWorkHint = "";

    public bool HasDeepWorkHint => DeepWorkHint.Length > 0;

    partial void OnDeepWorkMinutesChanged(string value)
    {
        if (int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) && minutes is >= 1 and <= 120)
        {
            _widget.SaveFocusDuration(minutes);
            DeepWorkHint = "";
        }
        else
        {
            DeepWorkHint = SettingsNames.DeepWorkRange;
        }
    }

    private void OnWidgetChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WidgetViewModel.IsCompact): OnPropertyChanged(nameof(IsCompact)); break;
            case nameof(WidgetViewModel.SparklineStyle): OnPropertyChanged(nameof(SelectedGraphStyle)); break;
            case nameof(WidgetViewModel.Pinned): OnPropertyChanged(nameof(KeepOnTop)); break;
        }
    }

    private void ReloadLimits()
    {
        var keys = _widget.ShownLimitOrder;
        for (int i = 0; i < keys.Count; i++)
        {
            int at = -1;
            for (int j = i; j < Limits.Count; j++)
            {
                if (Limits[j].Key == keys[i]) { at = j; break; }
            }
            if (at < 0)
                Limits.Insert(i, new LimitOrderRowViewModel(this, keys[i]));
            else if (at != i)
                Limits.Move(at, i);
        }
        while (Limits.Count > keys.Count)
            Limits.RemoveAt(Limits.Count - 1);
        for (int i = 0; i < Limits.Count; i++)
        {
            Limits[i].CanMoveUp = i > 0;
            Limits[i].CanMoveDown = i < Limits.Count - 1;
        }
    }

    /// <summary>Moves a limit one place in the widget's order; nothing when it can't move.</summary>
    internal void Move(string key, int delta)
    {
        if (LimitSettings.Move(_widget.ShownLimitOrder, key, delta) is { } order)
            _widget.SetLimitOrder(order);
    }

    /// <summary>Stop following the widget (Settings closed): it outlives every Settings window.</summary>
    public void Detach()
    {
        _widget.PropertyChanged -= OnWidgetChanged;
        _widget.LimitsChanged -= ReloadLimits;
    }
}
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`, add the property after `Publish`:

```csharp
    /// <summary>Settings, Widget (Settings v2, slice 5): the widget's own options and Just the Desk's way back.</summary>
    public WidgetPageViewModel WidgetPage { get; }
```

and in the constructor, after `Publish = new PublishViewModel(widget.Publish);`, add `WidgetPage = new WidgetPageViewModel(widget);`.

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs`, in the `Closed` handler after `ViewModel.Alerts.Detach();`, add `ViewModel.WidgetPage.Detach();`.

- [ ] **Step 4: The page**

Create `windows-dotnet/src/Sanduhr.App/Views/WidgetPage.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace Sanduhr.App.Views;

/// <summary>Settings, Widget (Settings v2, slice 5).</summary>
public partial class WidgetPage : UserControl
{
    public WidgetPage() => InitializeComponent();
}
```

Create `windows-dotnet/src/Sanduhr.App/Views/WidgetPage.xaml`:

```xml
<UserControl x:Class="Sanduhr.App.Views.WidgetPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:views="clr-namespace:Sanduhr.App.Views"
             xmlns:vm="clr-namespace:Sanduhr.App.ViewModels"
             xmlns:core="clr-namespace:Sanduhr.Core;assembly=Sanduhr.Core"
             Focusable="False">
    <!-- Settings, Widget (spec, Widget; WF15, WF24, WF32): the widget's own options, which had no Settings home. Hosted in
         SettingsWindow. DataContext: the WidgetPageViewModel. -->
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BoolVis" />
    </UserControl.Resources>
    <Border Background="{DynamicResource Sanduhr.Brush.Glass}" CornerRadius="8">
        <ScrollViewer VerticalScrollBarVisibility="Auto" Focusable="False">
            <ScrollViewer.Resources>
                <Style TargetType="ScrollBar" BasedOn="{StaticResource SlimThemeScrollBar}" />
            </ScrollViewer.Resources>
            <StackPanel x:Name="PageBody" Margin="20">
                <TextBlock Text="{Binding PageTitle}" FontSize="14" FontWeight="SemiBold"
                           AutomationProperties.HeadingLevel="Level1"
                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,12" />
                <TextBlock Text="{x:Static core:SettingsNames.WidgetLine}" FontSize="11" TextWrapping="Wrap"
                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,-8,0,12" />

                <!-- Task 10: Just the Desk's section goes here, above the options: while it is on, it is the way back. -->

                <StackPanel views:SettingsAnchor.Id="widget-options">
                    <CheckBox Style="{DynamicResource ThemedCheckBox}"
                              Content="{x:Static core:SettingsNames.CompactMode}"
                              IsChecked="{Binding IsCompact, Mode=OneWay}"
                              Command="{Binding ToggleCompactCommand}"
                              AutomationProperties.Name="{x:Static core:SettingsNames.CompactMode}" />
                    <TextBlock Text="{x:Static core:SettingsNames.CompactModeCaption}" FontSize="10.5" TextWrapping="Wrap"
                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}" Margin="22,3,0,12" />

                    <StackPanel Orientation="Horizontal" Margin="0,0,0,12">
                        <TextBlock Text="{x:Static core:SettingsNames.GraphStyle}" FontSize="12" VerticalAlignment="Center" Margin="0,0,8,0"
                                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                        <ComboBox Style="{DynamicResource ThemedComboBox}" Width="170" VerticalAlignment="Center"
                                  ItemsSource="{x:Static vm:WidgetPageViewModel.GraphStyles}"
                                  DisplayMemberPath="Display"
                                  SelectedItem="{Binding SelectedGraphStyle, Mode=TwoWay}"
                                  AutomationProperties.Name="{x:Static core:SettingsNames.GraphStyle}" />
                    </StackPanel>

                    <CheckBox Style="{DynamicResource ThemedCheckBox}" Margin="0,0,0,12"
                              Content="{x:Static core:SettingsNames.KeepWidgetOnTop}"
                              IsChecked="{Binding KeepOnTop, Mode=TwoWay}"
                              AutomationProperties.Name="{x:Static core:SettingsNames.KeepWidgetOnTop}" />

                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="{x:Static core:SettingsNames.DeepWorkLength}" FontSize="12" VerticalAlignment="Center" Margin="0,0,8,0"
                                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                        <TextBox Style="{DynamicResource Input}" Width="64"
                                 Text="{Binding DeepWorkMinutes, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                                 AutomationProperties.Name="{x:Static core:SettingsNames.DeepWorkLengthSpoken}" />
                        <TextBlock Text="{x:Static core:SettingsNames.DeepWorkMinutes}" FontSize="12" VerticalAlignment="Center" Margin="8,0,0,0"
                                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                    </StackPanel>
                    <TextBlock Text="{Binding DeepWorkHint}" FontSize="11" TextWrapping="Wrap" Margin="0,4,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.PaceMarker}"
                               views:LiveRegion.Shown="{Binding HasDeepWorkHint}"
                               Visibility="{Binding HasDeepWorkHint, Converter={StaticResource BoolVis}}" />

                    <TextBlock Text="{x:Static core:SettingsNames.LimitOrder}" FontSize="13" FontWeight="SemiBold"
                               AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,16,0,6" />
                    <TextBlock x:Name="LimitOrderEmpty" Style="{DynamicResource EmptyLine}"
                               Text="{x:Static core:SettingsNames.EachLimitNotYet}"
                               Tag="{Binding HasItems, ElementName=LimitOrderList}" />
                    <ItemsControl x:Name="LimitOrderList" ItemsSource="{Binding Limits}" Focusable="False"
                                  AutomationProperties.Name="{x:Static core:SettingsNames.LimitOrderListName}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="0,0,0,4">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*" MinWidth="180" />
                                        <ColumnDefinition Width="96" />
                                        <ColumnDefinition Width="104" />
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Text="{Binding Label}" FontSize="12" VerticalAlignment="Center"
                                               Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                    <Button Grid.Column="1" Style="{DynamicResource FlatButton}"
                                            Content="{x:Static core:SettingsNames.MoveUp}"
                                            Command="{Binding MoveUpCommand}"
                                            AutomationProperties.Name="{Binding MoveUpName}"
                                            Visibility="{Binding CanMoveUp, Converter={StaticResource BoolVis}}" />
                                    <Button Grid.Column="2" Style="{DynamicResource FlatButton}"
                                            Content="{x:Static core:SettingsNames.MoveDown}"
                                            Command="{Binding MoveDownCommand}"
                                            AutomationProperties.Name="{Binding MoveDownName}"
                                            Visibility="{Binding CanMoveDown, Converter={StaticResource BoolVis}}" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </StackPanel>
        </ScrollViewer>
    </Border>
</UserControl>
```

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`, right before the Themes TabItem (`<TabItem Header="Themes" Tag="Themes" …>` and the comment above it), add:

```xml
                <!-- Settings, Widget (slice 5): the widget's own options and Just the Desk's way back (WF15, WF24, WF32). -->
                <TabItem Header="Widget" Tag="Widget" AutomationProperties.Name="Widget" DataContext="{Binding WidgetPage}">
                    <views:WidgetPage />
                </TabItem>
```

- [ ] **Step 5: The link rule, the index and the sidebar**

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`:
- After `// About`'s constants block, add:

```csharp
    // Widget (slice 5)
    public const string WidgetOptions = "widget-options";
```

- In `Anchors`, after the Launcher rows and before `"Version"`, add (sidebar order puts Widget before Themes, but `Anchors` is searched by page order, so its place in the list only orders ties on the same page):

```csharp
        E(SettingsNames.WidgetOptions, SettingsPage.Widget, WidgetOptions,
            [SettingsNames.CompactMode, "compact", "Ctrl+D", SettingsNames.GraphStyle, "graph", "sparkline", "classic", "horizon",
             SettingsNames.KeepWidgetOnTop, "on top", "pin", "always on top", SettingsNames.DeepWorkLength, "deep work", "focus", "timer",
             SettingsNames.LimitOrder, "tier order", "order", "reorder", "move up", "move down"]),
```

- In `PageSynonyms`, add `[SettingsPage.Widget] = ["floating window", "meters window"],`.
- In `NameAnchors`, add:

```csharp
        [SettingsNames.CompactMode] = WidgetOptions,
        [SettingsNames.GraphStyle] = WidgetOptions,
        [SettingsNames.KeepWidgetOnTop] = WidgetOptions,
        [SettingsNames.DeepWorkLength] = WidgetOptions,
        [SettingsNames.LimitOrder] = WidgetOptions,
```

- In `MenuAnchor`, add `SettingsPage.Widget => WidgetOptions,`.
- After `ParseLink`, add:

```csharp
    /// <summary>
    /// Whether a place means the widget itself rather than Settings (Settings v2, slice 5, decision 1): <see cref="SettingsPage.Widget"/>
    /// with no anchor is what cards, the tour and <c>--settings widget</c> have always meant; with an anchor it is the Widget page.
    /// </summary>
    public static bool ShowsTheWidget(SettingsLink link) => link.Page == SettingsPage.Widget && link.Anchor is null;
```

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`:
- In `Sidebar`, change `new("Widget", [SettingsPage.Themes]),` to `new("Widget", [SettingsPage.Widget, SettingsPage.Themes]),`, and replace the summary's "Widget is not a page (Show me on a widget card shows the widget itself)." with "Widget is a page since slice 5; Widget with no anchor still means the widget itself (SettingsIndex.ShowsTheWidget).".
- In `PageTitle`'s summary, drop "Widget, which is not a page, reads "Widget"." (it is a page now).
- In `Table`, after `(WidgetCompanionRoam, SettingsPage.Desk),`, add:

```csharp
        (CompactMode, SettingsPage.Widget),
        (GraphStyle, SettingsPage.Widget),
        (KeepWidgetOnTop, SettingsPage.Widget),
        (DeepWorkLength, SettingsPage.Widget),
        (LimitOrder, SettingsPage.Widget),
```

In `windows-dotnet/src/Sanduhr.App/App.xaml.cs`: in `ShowMe`, change `if (page == SettingsPage.Widget)` to `if (SettingsIndex.ShowsTheWidget(new SettingsLink(page, anchor)))`; in `OpenSettingsLink`, change `if (link.Page == SettingsPage.Widget)` to `if (SettingsIndex.ShowsTheWidget(link))`.

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs`: in `OpenLink`, change `&& link.Page != SettingsPage.Widget` to `&& !SettingsIndex.ShowsTheWidget(link)` and its summary "the widget is not a Settings page" to "Widget with no anchor is the widget itself, not Settings"; in `SelectPage`'s summary, change "Widget is not a Settings page and is ignored; an anchor the page doesn't have opens the page's top." to "An anchor the page doesn't have opens the page's top.".

In `windows-dotnet/src/Sanduhr.App/ViewModels/WhatsNewViewModel.cs`, change `public string ShowMeToolTip => Card.Destination == SettingsPage.Widget` to `public string ShowMeToolTip => SettingsIndex.ShowsTheWidget(new SettingsLink(Card.Destination, Card.Anchor))`.

- [ ] **Step 6: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSlice5IndexTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsLinkTests|FullyQualifiedName~SettingsReachIndexTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~WhatsNewTests|FullyQualifiedName~WelcomeTourTests" > %TEMP%\slice5-t9-core.log 2>&1`
Then: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~WidgetPageTests|FullyQualifiedName~SettingsWindowNamesTests|FullyQualifiedName~WhatsNewShowMeTests|FullyQualifiedName~SidebarTabControlAutomationTests|FullyQualifiedName~SettingsReachTests" > %TEMP%\slice5-t9-app.log 2>&1`
Expected: PASS. `A_link_reads_as_a_page_and_an_anchor_on_it`'s `widget` and `widget#tray` rows still read (Widget, null); `Card_destinations_are_sidebar_pages_or_Widget` still passes.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/WidgetPageViewModel.cs windows-dotnet/src/Sanduhr.App/Views/WidgetPage.xaml windows-dotnet/src/Sanduhr.App/Views/WidgetPage.xaml.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/WhatsNewViewModel.cs windows-dotnet/src/Sanduhr.App/App.xaml.cs windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.App.Tests/WidgetPageTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsSlice5IndexTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs
git commit -m "feat(settings-v2): the Widget page: compact mode, graph style, keep on top, Deep Work length and limit order"
```

---

### Task 10: Just the Desk's "Show the widget"

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetPageViewModel.cs` (`IsJustTheDesk`, `ShowWidgetCommand`, `ShowWidgetRequested`)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/WidgetPage.xaml` (the section where Task 9 left its comment)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (`ShowSettingsAsync`: the request's handler)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (`JustTheDeskSection`, its row, `NameAnchors`)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (`Table`; `JustTheDeskButton`'s summary)
- Test: append to `windows-dotnet/tests/Sanduhr.App.Tests/WidgetPageTests.cs`

**Interfaces:**
- Consumes: `WidgetViewModel.EndJustTheDesk`, `JustTheDesk` (Task 5); `JustTheDesk.EndByShowWidget` (Task 2, through `End`).
- Produces: `WidgetPageViewModel.IsJustTheDesk`, `ShowWidgetCommand`, `public Action? ShowWidgetRequested { get; set; }` (App shows the window and syncs the open Desk box); `SettingsIndex.JustTheDeskSection = "just-the-desk"`.

- [ ] **Step 1: Write the failing tests**

Append to the class in `windows-dotnet/tests/Sanduhr.App.Tests/WidgetPageTests.cs`:

```csharp
    /// <summary>Just the Desk on, with no account, the reminder silenced by Just the Desk (the first-run card's choice).</summary>
    private Sanduhr.App.Services.SettingsStore InJustTheDesk(bool reminderWasOn)
    {
        var store = new Sanduhr.App.Services.SettingsStore(ScratchPaths());
        var (state, reminderOn, _) = JustTheDesk.Choose(JustTheDeskState.Off, reminderWasOn);
        store.SaveJustTheDesk(state);
        store.SaveDeskSignInReminder(reminderOn);
        return store;
    }

    [Fact]
    public void Show_the_widget_ends_Just_the_Desk_and_shows_the_first_run_card() => AppHost.Run(() =>
    {
        var store = InJustTheDesk(reminderWasOn: true);
        var (widget, _) = ScratchWidget();
        var page = new WidgetPageViewModel(widget);
        int shown = 0;
        page.ShowWidgetRequested = () => shown++;
        try
        {
            Assert.True(page.IsJustTheDesk);

            page.ShowWidgetCommand.Execute(null);

            Assert.False(page.IsJustTheDesk);
            Assert.False(widget.JustTheDesk);
            Assert.Equal(1, shown);
            Assert.False(store.LoadJustTheDesk().On);
            Assert.True(store.LoadDeskSignInReminder());
            Assert.False(widget.ShowNotSignedInLine);   // the first-run card, not the one-line way in: never a blank widget
        }
        finally { page.Detach(); }
    });

    [Fact]
    public void A_reminder_the_person_had_turned_off_stays_off() => AppHost.Run(() =>
    {
        var store = InJustTheDesk(reminderWasOn: false);
        var (widget, _) = ScratchWidget();
        var page = new WidgetPageViewModel(widget);
        try
        {
            page.ShowWidgetCommand.Execute(null);
            Assert.False(store.LoadDeskSignInReminder());
        }
        finally { page.Detach(); }
    });

    [Fact]
    public void The_next_launch_shows_the_widget() => AppHost.Run(() =>
    {
        InJustTheDesk(reminderWasOn: true);
        var (widget, accounts) = ScratchWidget();
        var page = new WidgetPageViewModel(widget);
        page.ShowWidgetCommand.Execute(null);
        page.Detach();

        var relaunched = new WidgetViewModel(ScratchPaths(), accounts);
        Assert.False(relaunched.JustTheDesk);
        Assert.False(JustTheDesk.WidgetStartsHidden(relaunched.JustTheDesk, hasAccount: false, deskOn: true));
    });

    [Fact]
    public void The_section_shows_only_while_Just_the_Desk_is_on() => AppHost.Run(() =>
    {
        InJustTheDesk(reminderWasOn: true);
        var (widget, _) = ScratchWidget();
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        try
        {
            window.SelectPage(SettingsPage.Widget, SettingsIndex.JustTheDeskSection);
            window.UpdateLayout();
            Pump();
            guard.Check();
            var tab = ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == "Widget");
            var section = SettingsAnchor.Find((DependencyObject)tab.Content, SettingsIndex.JustTheDeskSection)!;
            Assert.True(section.IsVisible);

            svm.WidgetPage.ShowWidgetCommand.Execute(null);
            window.UpdateLayout();
            Pump();
            Assert.False(section.IsVisible);
        }
        finally { window.Close(); }
    });
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~WidgetPageTests > %TEMP%\slice5-t10-red.log 2>&1`
Expected: build FAILS with `'WidgetPageViewModel' does not contain a definition for 'ShowWidgetRequested'`.

- [ ] **Step 3: The command, the section and App's half**

In `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetPageViewModel.cs`, add to `WidgetPageViewModel` after `KeepOnTop`:

```csharp
    /// <summary>Just the Desk is on ("just_the_desk"): the section with "Show the widget" shows (WF32).</summary>
    public bool IsJustTheDesk => _widget.JustTheDesk;

    /// <summary>Connected by App: show the widget window and sync an open Settings window's reminder box.</summary>
    public Action? ShowWidgetRequested { get; set; }

    /// <summary>"Show the widget": ends Just the Desk the way signing in does (the reminder back only if Just the Desk silenced it),
    /// then asks App to show the widget, which offers Sign in.</summary>
    [RelayCommand]
    private void ShowWidget()
    {
        _widget.EndJustTheDesk();
        ShowWidgetRequested?.Invoke();
    }
```

and in `OnWidgetChanged`'s switch add `case nameof(WidgetViewModel.JustTheDesk): OnPropertyChanged(nameof(IsJustTheDesk)); break;`.

In `windows-dotnet/src/Sanduhr.App/Views/WidgetPage.xaml`, replace the comment line `<!-- Task 10: Just the Desk's section goes here, above the options: while it is on, it is the way back. -->` with:

```xml
                <!-- Just the Desk (WF32): shown only while it is on; above the options, because then it is the way back. -->
                <StackPanel views:SettingsAnchor.Id="just-the-desk" Margin="0,0,0,16"
                            Visibility="{Binding IsJustTheDesk, Converter={StaticResource BoolVis}}">
                    <TextBlock Text="{x:Static core:SettingsNames.JustTheDeskButton}" FontSize="13" FontWeight="SemiBold"
                               AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,6" />
                    <TextBlock Text="{x:Static core:SettingsNames.JustTheDeskOn}" FontSize="11" TextWrapping="Wrap" Margin="0,0,0,8"
                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                    <Button Style="{DynamicResource AccentButton}" HorizontalAlignment="Left"
                            Content="{x:Static core:SettingsNames.ShowTheWidget}"
                            Command="{Binding ShowWidgetCommand}"
                            AutomationProperties.Name="{x:Static core:SettingsNames.ShowTheWidget}"
                            AutomationProperties.HelpText="{x:Static core:SettingsNames.ShowTheWidgetCaption}" />
                    <TextBlock Text="{x:Static core:SettingsNames.ShowTheWidgetCaption}" FontSize="10.5" TextWrapping="Wrap" Margin="0,4,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                    <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,0" />
                </StackPanel>
```

In `windows-dotnet/src/Sanduhr.App/App.xaml.cs`, in `ShowSettingsAsync`, after `svm.QuitRequested = QuitApp;`, add:

```csharp
        // Settings, Widget, Just the Desk's "Show the widget" (WF32): the widget view model has ended it and redrawn the Desk; the window is App's.
        svm.WidgetPage.ShowWidgetRequested = () =>
        {
            if (_vm is { } widgetVm) svm.Alerts.DeskSignInReminder = widgetVm.LoadDeskSignInReminder();
            ShowWindow();
        };
```

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`, under `// Widget (slice 5)` add `public const string JustTheDeskSection = "just-the-desk";`, after the `WidgetOptions` anchor row add

```csharp
        E(JustTheDesk.ButtonTitle, SettingsPage.Widget, JustTheDeskSection,
            [SettingsNames.ShowTheWidget, "show the widget", "widget hidden", "hidden widget", "desk only", "no account", "without an account"]),
```

and in `NameAnchors` add `[SettingsNames.ShowTheWidget] = JustTheDeskSection,`. Change the Desk `SignInReminders` row's synonym `"Just the Desk"` to `"sign-in reminder"` (the words now find their own section).

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, add `(ShowTheWidget, SettingsPage.Widget),` to `Table` after the Widget rows, and change `JustTheDeskButton`'s summary to "The first-run card's third action (settings.json "just_the_desk"), and the Widget page's Just the Desk section.".

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~WidgetPageTests|FullyQualifiedName~WidgetLimitsTests" > %TEMP%\slice5-t10-app.log 2>&1`
Then: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsSlice5IndexTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~JustTheDeskTests" > %TEMP%\slice5-t10-core.log 2>&1`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/WidgetPageViewModel.cs windows-dotnet/src/Sanduhr.App/Views/WidgetPage.xaml windows-dotnet/src/Sanduhr.App/App.xaml.cs windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.App.Tests/WidgetPageTests.cs
git commit -m "feat(settings-v2): Just the Desk has a Settings way out: Show the widget ends it the way signing in does"
```

---

### Task 11: Publish usage, What gets sent: a sample built by the real builder

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Services/UsagePublishService.cs` (constructor; `Handler`; `PublishRecord`, `BuildRecord`, `SampleAsync`; `PublishAsync` builds through `BuildRecord`)
- Create: `windows-dotnet/src/Sanduhr.App/Views/PublishSampleWindow.xaml`, `windows-dotnet/src/Sanduhr.App/Views/PublishSampleWindow.xaml.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/PublishViewModel.cs` (`ShowSampleCommand`, `IsSampling`, `SampleHook`)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (the Publish usage TabItem: a `sample` section)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (`Sample`, its row, `NameAnchors`)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (`Table`)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/PublishSampleTests.cs`

**Interfaces:**
- Consumes: `UsagePublisher.BuildPayload`, `ReadQuota`, `DefaultDate`, `PublishAsync(payload, target, token, handler)` (Core, unchanged); Task 3's Publish names.
- Produces: `UsagePublishService(SettingsStore settings, CcLogReader reader, Paths paths, PublishTokenStore tokens, Func<string>? home = null)` (the home seam; App passes nothing); `internal HttpMessageHandler? Handler { get; set; }`; `public sealed record PublishRecord(JsonObject Payload, IReadOnlyList<string> Homes, long TotalTokens, string QuotaStatus)`; `public PublishRecord BuildRecord(DateOnly date)`; `public Task<PublishRecord> SampleAsync(DateTimeOffset nowLocal)`; `PublishViewModel.ShowSampleCommand`, `IsSampling`, `internal Action<string, string, string>? SampleHook` (header, note, JSON); `SettingsIndex.Sample = "sample"`.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/PublishSampleTests.cs`:

```csharp
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 5: Publish usage, What gets sent (WF28). The sample is built by the function that builds the real record, and
/// building it sends nothing. Scratch folders, an in-memory token store, and a recording handler in place of the network.
/// </summary>
[Collection("SettingsWindow")]
public sealed class PublishSampleTests : AppWindowFixture
{
    private sealed class Recorder : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private (UsagePublishService Service, Recorder Net) Service(bool tickFolder)
    {
        var paths = ScratchPaths();
        var home = Path.Combine(Root, "home");
        Directory.CreateDirectory(Path.Combine(home, ".claude", "projects"));
        var settings = new SettingsStore(paths);
        settings.SavePublishEndpointUrl("https://example.test/ingest");
        settings.SavePublishAuthScheme(PublishAuthScheme.None);
        if (tickFolder)
            settings.SavePublishRoots(new Dictionary<string, bool> { [".claude"] = true });
        var service = new UsagePublishService(settings, new CcLogReader(home, () => [".claude"]), paths,
            new PublishTokenStore(new MemoryCredentials()), () => home);
        var net = new Recorder();
        service.Handler = net;
        return (service, net);
    }

    [Fact]
    public void The_sample_is_the_record_publish_now_sends()
    {
        var (service, net) = Service(tickFolder: true);
        var now = DateTimeOffset.Now;

        var sample = service.SampleAsync(now).GetAwaiter().GetResult();
        var outcome = service.PublishAsync(UsagePublisher.DefaultDate(now)).GetAwaiter().GetResult();

        Assert.NotNull(outcome);
        Assert.True(outcome.Result.Ok, outcome.Result.Message);
        var sent = JsonNode.Parse(Assert.Single(net.Bodies));
        Assert.True(JsonNode.DeepEquals(sample.Payload, sent), $"sample {sample.Payload.ToJsonString()}{Environment.NewLine}sent {sent?.ToJsonString()}");
        Assert.Equal([".claude"], sample.Homes);
    }

    [Fact]
    public void Building_the_sample_sends_nothing()
    {
        var (service, net) = Service(tickFolder: true);

        service.SampleAsync(DateTimeOffset.Now).GetAwaiter().GetResult();

        Assert.Empty(net.Bodies);
    }

    [Fact]
    public void With_no_folder_ticked_the_sample_says_so_and_names_the_destination()
    {
        var (service, net) = Service(tickFolder: false);
        string? header = null, note = null, json = null;
        var vm = new PublishViewModel(service) { SampleHook = (h, n, j) => (header, note, json) = (h, n, j) };

        vm.ShowSampleCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.Equal(SettingsNames.SampleNoFolders, note);
        var date = UsagePublisher.DefaultDate(DateTimeOffset.Now).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(SettingsNames.SampleHeader(date, "example.test"), header);
        Assert.Empty(((JsonArray)JsonNode.Parse(json!)!["homes"]!));
        Assert.Empty(net.Bodies);
        Assert.False(vm.IsSampling);
    }
}
```

Append to the class in `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice5IndexTests.cs`:

```csharp
    [Theory]
    [InlineData("sample")]
    [InlineData("what gets sent")]
    [InlineData("payload")]
    public void Sample_words_find_what_gets_sent(string query)
    {
        var top = Top(query);
        Assert.Equal((SettingsPage.Publish, SettingsIndex.Sample), (top.Entry.Page, top.Entry.Anchor));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~PublishSampleTests > %TEMP%\slice5-t11-red.log 2>&1`
Expected: build FAILS with `'UsagePublishService' does not contain a constructor that takes 5 arguments`.

- [ ] **Step 3: One builder**

In `windows-dotnet/src/Sanduhr.App/Services/UsagePublishService.cs`:

Replace the fields from `private readonly PublishTokenStore _tokens;` and the constructor with:

```csharp
    private readonly PublishTokenStore _tokens;
    private readonly Func<string> _home;
    private int _publishRunning;
    private int _tickRunning;

    /// <summary>Raised after any attempt (scheduled, manual, or MCP-queued)
    /// records its outcome — ON A WORKER THREAD; UI subscribers marshal.</summary>
    public event Action? StatusChanged;

    /// <summary><paramref name="home"/> is the folder the shared Claude Code folders sit in (the user profile; tests give a scratch one).</summary>
    public UsagePublishService(SettingsStore settings, CcLogReader reader, Paths paths, PublishTokenStore tokens, Func<string>? home = null)
    {
        _settings = settings;
        _reader = reader;
        _paths = paths;
        _tokens = tokens;
        _home = home ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    /// <summary>Tests: the network the upload goes to. Null is the real one.</summary>
    internal System.Net.Http.HttpMessageHandler? Handler { get; set; }
```

(delete the old `private int _publishRunning;`, `private int _tickRunning;` and `StatusChanged` declarations that sat between them, so each is declared once).

Replace the `PublishOutcome` record and `PublishAsync` with:

```csharp
    public sealed record PublishOutcome(
        UsagePublishResult Result, IReadOnlyList<string> Homes, long TotalTokens, int ProjectCount, string QuotaStatus);

    /// <summary>One day's record, as it would be sent: the payload, the folders in it, the token total and the quota's state.</summary>
    public sealed record PublishRecord(JsonObject Payload, IReadOnlyList<string> Homes, long TotalTokens, string QuotaStatus);

    /// <summary>
    /// The one builder (Settings v2, WF28): the scheduled publish, Publish now, the MCP's publish_usage and Settings' "Show a sample…"
    /// all read the record here. Reads the shared folders' logs for <paramref name="date"/> and the widget's snapshot; sends nothing.
    /// </summary>
    public PublishRecord BuildRecord(DateOnly date)
    {
        var shared = SharedRootNames();
        var home = _home();
        var burn = new List<RootDayBurn>(shared.Count);
        foreach (var root in shared)
            burn.Add(_reader.BurnForLocalDay(root, Path.Combine(home, root), date));

        var settings = _settings.LoadPublishSettings();
        var quota = UsagePublisher.ReadQuota(_paths.SnapshotFile, DateTimeOffset.UtcNow);
        var payload = UsagePublisher.BuildPayload(
            date, Environment.MachineName, burn,
            new HashSet<string>(shared, StringComparer.Ordinal), quota, settings.BodyFormat);
        return new PublishRecord(payload, shared, burn.Sum(b => b.Total), quota.Status);
    }

    /// <summary>"Show a sample…": the record Publish now would send at <paramref name="nowLocal"/> (yesterday's, local), built off the
    /// UI thread. Nothing is posted.</summary>
    public Task<PublishRecord> SampleAsync(DateTimeOffset nowLocal) =>
        Task.Run(() => BuildRecord(UsagePublisher.DefaultDate(nowLocal)));

    /// <summary>Collect, build, post, record. Single-flight: returns null when
    /// another run holds the latch. Never throws — a fault records as a failed
    /// attempt so the status line says so.</summary>
    public async Task<PublishOutcome?> PublishAsync(DateOnly date)
    {
        if (Interlocked.CompareExchange(ref _publishRunning, 1, 0) != 0)
            return null;
        try
        {
            return await Task.Run(async () =>
            {
                var record = BuildRecord(date);
                var settings = _settings.LoadPublishSettings();

                // The publisher refuses (status 0, no network) on a missing endpoint
                // or a missing token for a scheme that needs one.
                var result = await UsagePublisher.PublishAsync(record.Payload, settings.Target, _tokens.Load(), Handler).ConfigureAwait(false);

                Record(result, date);
                return new PublishOutcome(result, record.Homes, record.TotalTokens,
                    ((JsonArray)record.Payload["byProject"]!).Count, record.QuotaStatus);
            }).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            LogBestEffort("publish", e);
            var failed = new UsagePublishResult(0, false, $"Publish failed ({e.GetType().Name}).", DateTimeOffset.Now);
            Record(failed, date);
            return new PublishOutcome(failed, Array.Empty<string>(), 0, 0, "no_data");
        }
        finally
        {
            Interlocked.Exchange(ref _publishRunning, 0);
        }
    }
```

- [ ] **Step 4: The window, the command and the section**

Create `windows-dotnet/src/Sanduhr.App/Views/PublishSampleWindow.xaml`:

```xml
<Window x:Class="Sanduhr.App.Views.PublishSampleWindow"
        x:ClassModifier="internal"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:core="clr-namespace:Sanduhr.Core;assembly=Sanduhr.Core"
        Title="{x:Static core:SettingsNames.SampleTitle}"
        Height="520" Width="640" MinHeight="320" MinWidth="420"
        WindowStartupLocation="CenterOwner"
        ShowInTaskbar="False"
        Background="{DynamicResource Sanduhr.Brush.Bg}">
    <!-- Publish usage, What gets sent (WF28): the next record, read-only, exactly as Publish now would send it. -->
    <Grid Margin="24">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="{x:Static core:SettingsNames.SampleTitle}" FontSize="16" FontWeight="Bold"
                   AutomationProperties.HeadingLevel="Level1"
                   Foreground="{DynamicResource Sanduhr.Brush.Accent}" />
        <TextBlock Grid.Row="1" x:Name="HeaderText" Margin="0,6,0,0" TextWrapping="Wrap" FontSize="11"
                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
        <TextBlock Grid.Row="2" x:Name="NoteText" Margin="0,6,0,0" TextWrapping="Wrap" FontSize="11"
                   Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
        <TextBox Grid.Row="3" x:Name="RecordBox" Margin="0,12,0,0" IsReadOnly="True" IsReadOnlyCaretVisible="True"
                 FontFamily="Cascadia Code, Consolas" FontSize="12" TextWrapping="NoWrap"
                 HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Auto"
                 AutomationProperties.Name="{x:Static core:SettingsNames.SampleTitle}"
                 Background="{DynamicResource Sanduhr.Brush.Glass}"
                 Foreground="{DynamicResource Sanduhr.Brush.Text}"
                 BorderBrush="{DynamicResource Sanduhr.Brush.Border}"
                 CaretBrush="{DynamicResource Sanduhr.Brush.Text}" />
        <StackPanel Grid.Row="4" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,12,0,0">
            <Button Content="{x:Static core:SettingsNames.SampleClose}" Padding="20,8" IsDefault="True" IsCancel="True"
                    AutomationProperties.Name="{x:Static core:SettingsNames.SampleClose}"
                    Background="{DynamicResource Sanduhr.Brush.Accent}"
                    Foreground="{DynamicResource Sanduhr.Brush.Bg}" BorderThickness="0"
                    FontWeight="Bold" Cursor="Hand" Click="OnCloseClick" />
        </StackPanel>
    </Grid>
</Window>
```

(Close is the default button on purpose: closing a read-only preview destroys nothing.)

Create `windows-dotnet/src/Sanduhr.App/Views/PublishSampleWindow.xaml.cs`:

```csharp
using System.Windows;

namespace Sanduhr.App.Views;

/// <summary>Publish usage's "Show a sample…" (WF28): the next record, read-only and selectable, with where it would go.</summary>
internal partial class PublishSampleWindow : Window
{
    public PublishSampleWindow(string header, string note, string json)
    {
        InitializeComponent();
        HeaderText.Text = header;
        NoteText.Text = note;
        NoteText.Visibility = note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecordBox.Text = json;
        Loaded += (_, _) => RecordBox.Focus();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/PublishViewModel.cs`, add `using System.Text.Json;` and `using System.Text.Json.Nodes;` to the usings, and these members after `PublishNow` (or at the end of the class):

```csharp
    // ---- What gets sent (Settings v2, slice 5; WF28) ----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowSampleCommand))]
    private bool _isSampling;

    private bool CanSample => _service is not null && !IsSampling;

    /// <summary>Tests read the sample here (header, note, JSON) instead of opening the window.</summary>
    internal Action<string, string, string>? SampleHook { get; set; }

    private static readonly JsonSerializerOptions SampleJson = new() { WriteIndented = true };

    /// <summary>"Show a sample…": the record Publish now would send, built by the same function, shown read-only. Sends nothing.</summary>
    [RelayCommand(CanExecute = nameof(CanSample))]
    private async Task ShowSample()
    {
        if (_service is null)
            return;
        IsSampling = true;
        try
        {
            var record = await _service.SampleAsync(DateTimeOffset.Now);
            var date = record.Payload["date"]?.GetValue<string>() ?? "";
            var endpoint = _service.Load().Target.EndpointUrl;
            string? host = Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) ? uri.Host : null;
            var header = SettingsNames.SampleHeader(date, host);
            var note = record.Homes.Count == 0 ? SettingsNames.SampleNoFolders : "";
            var json = record.Payload.ToJsonString(SampleJson);
            if (SampleHook is { } hook)
            {
                hook(header, note, json);
                return;
            }
            var window = new PublishSampleWindow(header, note, json);
            if (_owner is { IsLoaded: true })
                window.Owner = _owner;
            window.ShowDialog();
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            StatusText = SettingsNames.SampleFailed;
        }
        finally
        {
            IsSampling = false;
        }
    }
```

(`PublishSettings.Target` is `PublishTarget(string EndpointUrl, PublishAuthScheme AuthScheme, string AuthHeaderName)`, the target `UsagePublisher.PublishAsync` posts to, so the header names the host the record would really go to.)

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`, in the Publish usage TabItem, right after the `TimeError` TextBlock (the one bound to `{Binding TimeError}`), add:

```xml
                            <!-- What gets sent (WF28): every field group of the record, and a sample built by the real builder. -->
                            <StackPanel Margin="0,6,0,4" views:SettingsAnchor.Id="sample">
                                <TextBlock Text="{x:Static core:SettingsNames.PublishSampleSection}" FontSize="9" Margin="0,0,0,4"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                <TextBlock Text="{x:Static core:SettingsNames.PublishRecordLine}" TextWrapping="Wrap" FontSize="10.5"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextDim}" Margin="0,0,0,6" />
                                <Button Style="{StaticResource FlatButton}" HorizontalAlignment="Left"
                                        Content="{x:Static core:SettingsNames.ShowASample}"
                                        AutomationProperties.Name="{x:Static core:SettingsNames.ShowASample}"
                                        Command="{Binding ShowSampleCommand}" />
                            </StackPanel>
```

- [ ] **Step 5: The index and the table**

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`, under `// Publish usage` add `public const string Sample = "sample";`; after the `"Publish daily"` anchor row add

```csharp
        E(SettingsNames.PublishSampleSection, SettingsPage.Publish, Sample,
            ["sample", "preview", "what gets sent", "what is sent", "record", "payload", "json", "project names", "machine name",
             SettingsNames.ShowASample]),
```

and in `NameAnchors` add `[SettingsNames.ShowASample] = Sample,`. In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, add `(ShowASample, SettingsPage.Publish),` to `Table` after `(FoldersToShare, SettingsPage.Publish),`.

- [ ] **Step 6: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~PublishSampleTests|FullyQualifiedName~SettingsReachTests" > %TEMP%\slice5-t11-app.log 2>&1`
Then: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSlice5IndexTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~UsagePublisherTests|FullyQualifiedName~PublishHandoffTests" > %TEMP%\slice5-t11-core.log 2>&1`
Then the MCP side, whose `publish_usage` waits on this service's handoff: `dotnet test tests/Sanduhr.Mcp.Tests > %TEMP%\slice5-t11-mcp.log 2>&1`
Expected: PASS. `Alerts_and_Publish_usage_scroll_on_a_short_window` still lands `schedule` in a scrolling page.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Services/UsagePublishService.cs windows-dotnet/src/Sanduhr.App/Views/PublishSampleWindow.xaml windows-dotnet/src/Sanduhr.App/Views/PublishSampleWindow.xaml.cs windows-dotnet/src/Sanduhr.App/ViewModels/PublishViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.App.Tests/PublishSampleTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsSlice5IndexTests.cs
git commit -m "feat(settings-v2): Publish usage shows a sample of the next record, built by the same function, sending nothing"
```

---

### Task 12: About: "Don't show What's New after updates"

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/AboutVersionViewModel.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (`AboutVersion`)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (About, Version: one CheckBox)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs` (`Tabs_SelectionChanged`: About reloads and requeries)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (`Version`'s synonyms, `NameAnchors`)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (`Table`; `HideWhatsNewAfterUpdates`'s summary)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/AboutWhatsNewSwitchTests.cs`

**Interfaces:**
- Consumes: `SettingsStore.LoadWhatsNewHideAfterUpdates`, `SaveWhatsNewHideAfterUpdates`; `WhatsNewViewModel.HideAfterUpdates` (the window's box, unchanged: decision 2).
- Produces: `public sealed partial class AboutVersionViewModel : ObservableObject` (`HideWhatsNewAfterUpdates`, `Reload()`); `SettingsViewModel.AboutVersion`.

- [ ] **Step 1: Write the failing test**

Create `windows-dotnet/tests/Sanduhr.App.Tests/AboutWhatsNewSwitchTests.cs`:

```csharp
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 5: About, Version carries "Don't show What's New after updates" (WF24); the What's New window keeps its own box
/// on the same key (decision 2), and About reads the key again on each visit.</summary>
[Collection("SettingsWindow")]
public sealed class AboutWhatsNewSwitchTests : AppWindowFixture
{
    private static IEnumerable<T> Below<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Below<T>(child)) yield return deeper;
        }
    }

    [Fact]
    public void About_and_the_What_s_New_window_share_one_key() => AppHost.Run(() =>
    {
        // SettingsViewModel reads settings through new Paths(), which this fixture points at a scratch profile.
        var paths = new Paths();
        Assert.StartsWith(Path.GetTempPath(), paths.SettingsFile, StringComparison.OrdinalIgnoreCase);
        var store = new SettingsStore(paths);
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        try
        {
            window.SelectPage(SettingsPage.About, SettingsIndex.Version);
            window.UpdateLayout();
            Pump();
            guard.Check();
            var box = Below<CheckBox>(window).Single(c =>
                System.Windows.Automation.AutomationProperties.GetName(c) == SettingsNames.HideWhatsNewAfterUpdates);

            box.IsChecked = true;
            Assert.True(store.LoadWhatsNewHideAfterUpdates());
            Assert.True(new WhatsNewViewModel([], "Version 4.0.0", store).HideAfterUpdates);

            // The What's New window turns it back off; About shows that on its next visit.
            new WhatsNewViewModel([], "Version 4.0.0", store).HideAfterUpdates = false;
            window.SelectPage(SettingsPage.General);
            window.SelectPage(SettingsPage.About, SettingsIndex.Version);
            window.UpdateLayout();
            Pump();
            Assert.False(svm.AboutVersion.HideWhatsNewAfterUpdates);
            Assert.False(box.IsChecked);
        }
        finally { window.Close(); }
    });
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~AboutWhatsNewSwitchTests > %TEMP%\slice5-t12-red.log 2>&1`
Expected: build FAILS with `'SettingsViewModel' does not contain a definition for 'AboutVersion'`.

- [ ] **Step 3: The view model, the box and the visit**

Create `windows-dotnet/src/Sanduhr.App/ViewModels/AboutVersionViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.App.Services;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// About, Version (Settings v2, slice 5; WF24): "Don't show What's New after updates" ("whats_new_hide_after_updates"). The What's New
/// window keeps its own box on the same key, so About reads it again on each visit (<see cref="Reload"/>).
/// </summary>
public sealed partial class AboutVersionViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private bool _loading;

    [ObservableProperty] private bool _hideWhatsNewAfterUpdates;

    public AboutVersionViewModel(SettingsStore store)
    {
        _store = store;
        Reload();
    }

    /// <summary>Read the key again (About was opened; the What's New window may have changed it).</summary>
    public void Reload()
    {
        _loading = true;
        HideWhatsNewAfterUpdates = _store.LoadWhatsNewHideAfterUpdates();
        _loading = false;
    }

    partial void OnHideWhatsNewAfterUpdatesChanged(bool value)
    {
        if (!_loading)
            _store.SaveWhatsNewHideAfterUpdates(value);
    }
}
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`, add after `ShowTourCommand`:

```csharp
    /// <summary>About, Version's "Don't show What's New after updates" (slice 5).</summary>
    public AboutVersionViewModel AboutVersion { get; }
```

and in the constructor, after `ShowTourCommand = …;`, add `AboutVersion = new AboutVersionViewModel(new SettingsStore(new Paths()));`.

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`, in About's `version` StackPanel, after the closing `</StackPanel>` of the What's New and tour buttons row, add:

```xml
                                <CheckBox Style="{StaticResource ThemedCheckBox}" Margin="0,10,0,0" DataContext="{Binding AboutVersion}"
                                          Content="{x:Static core:SettingsNames.HideWhatsNewAfterUpdates}"
                                          IsChecked="{Binding HideWhatsNewAfterUpdates, Mode=TwoWay}"
                                          AutomationProperties.Name="{x:Static core:SettingsNames.HideWhatsNewAfterUpdates}" />
```

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs`, in `Tabs_SelectionChanged`, after the Accounts branch (Task 7), add:

```csharp
        // About: the What's New window shares its switch, and "Show in Explorer" for the session archive dims until there is one
        // (slice 1 T4): read the one and ask the commands again on each visit.
        if (page == SettingsPage.About)
        {
            ViewModel.AboutVersion.Reload();
            CommandManager.InvalidateRequerySuggested();
        }
```

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`, add `"don't show", "after updates", SettingsNames.HideWhatsNewAfterUpdates` to the `"Version"` row's synonyms, and `[SettingsNames.HideWhatsNewAfterUpdates] = Version,` to `NameAnchors`. In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, add `(HideWhatsNewAfterUpdates, SettingsPage.About),` to `Table` after `(TakeTheTour, SettingsPage.About),`, and change `HideWhatsNewAfterUpdates`'s summary to "About, Version, and the What's New window (settings.json "whats_new_hide_after_updates"): one key, two boxes.".

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~AboutWhatsNewSwitchTests|FullyQualifiedName~SettingsReachTests|FullyQualifiedName~TrademarkFooterTests" > %TEMP%\slice5-t12-app.log 2>&1`
Then: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~WhatsNewTests" > %TEMP%\slice5-t12-core.log 2>&1`
Expected: PASS. `The_graph_pin_and_What_s_New_controls_are_named_from_the_table` still finds the window's own box.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/AboutVersionViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.App.Tests/AboutWhatsNewSwitchTests.cs
git commit -m "feat(settings-v2): About carries Don't show What's New after updates, on the What's New window's own key"
```

---

### Task 13: The Done line, and the slice's pages by keyboard and screen reader

**Files:**
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/Slice5PagesTests.cs`
- Modify only if a test finds a gap: the Accounts, Alerts, Claude Usage, Publish usage and About TabItems of `Views/SettingsWindow.xaml`, `Views/WidgetPage.xaml`

**Interfaces:**
- Consumes: everything above.
- Produces: the spec's Done line as tests. Its first clause, "every key in the widget-only list has a Settings control", is here (the list is inventory W18: `sparkline_style`, `pinned`, `focus_mode_duration`, `tier_order`, `whats_new_hide_after_updates`, and compact mode, which is session state). Its second, "a muted limit can be restored from Alerts", is Task 6's `A_muted_limit_can_be_restored_from_Alerts`; its third, "the first sign-in asks for a label", is Task 4's `The_first_sign_in_asks_for_a_label_with_Personal_filled_in`. The spoken-name and landing checks for the six pages this slice touches.

- [ ] **Step 1: Write the tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/Slice5PagesTests.cs`:

```csharp
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 5: the Done line, and the six pages it touches read and land the way the spec says.</summary>
[Collection("SettingsWindow")]
public sealed class Slice5PagesTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Svm, WidgetViewModel Widget, NoActivate Guard) Open()
    {
        var (widget, _) = ScratchWidget("Personal");
        widget.StartDemo();   // sample limits for Each limit and Limit order, over scratch folders
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

    private static T Named<T>(DependencyObject root, string name) where T : UIElement =>
        Below<T>(root).Single(e => e.IsVisible && Spoken(e) == name);

    private static string Bare(string label) => label.Trim().TrimEnd('…').TrimEnd(':').Trim();

    /// <summary>Done (spec, slice 5): every key in the widget-only list has a Settings control. Each control is found by its spoken
    /// name and acted on the way a person would, and the key it writes is read back from the file it lives in.</summary>
    [Fact]
    public void Every_widget_only_key_has_a_Settings_control() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            window.SelectPage(SettingsPage.Widget, SettingsIndex.WidgetOptions);
            Settle(window, guard);

            Named<ComboBox>(window, SettingsNames.GraphStyle).SelectedItem = WidgetPageViewModel.GraphStyles.Single(o => o.Value == SparklineStyle.Horizon);
            Named<CheckBox>(window, SettingsNames.KeepWidgetOnTop).IsChecked = true;
            var minutes = Named<TextBox>(window, SettingsNames.DeepWorkLengthSpoken);
            minutes.Text = "50";
            minutes.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();   // what leaving the box does
            var compact = Named<CheckBox>(window, SettingsNames.CompactMode);
            compact.Command.Execute(compact.CommandParameter);                   // what a click runs
            var first = widget.ShownLimitOrder[0];
            var down = Named<Button>(window, SettingsNames.MoveLimitDown(TierModel.Label(first)));
            down.Command.Execute(null);
            Settle(window, guard);

            window.SelectPage(SettingsPage.About, SettingsIndex.Version);
            Settle(window, guard);
            Named<CheckBox>(window, SettingsNames.HideWhatsNewAfterUpdates).IsChecked = true;

            // The widget's own keys live in its settings.json (scratch); About's in the Settings window's (the fixture's dev profile).
            var widgetKeys = (JsonObject)JsonNode.Parse(File.ReadAllText(ScratchPaths().SettingsFile))!;
            Assert.Equal("Horizon", (string?)widgetKeys["sparkline_style"]);
            Assert.True((bool?)widgetKeys["pinned"]);
            Assert.Equal(50, (int?)widgetKeys["focus_mode_duration"]);
            Assert.NotEqual(first, (string?)((JsonArray)widgetKeys["tier_order"]!)[0]);
            Assert.True(widget.IsCompact);
            var aboutKeys = (JsonObject)JsonNode.Parse(File.ReadAllText(new Sanduhr.Core.Paths().SettingsFile))!;
            Assert.True((bool?)aboutKeys["whats_new_hide_after_updates"]);
        }
        finally { window.Close(); }
    });

    /// <summary>Every button and box on the slice's pages is spoken with its visible label first (WF34; WCAG 2.5.3), item templates included,
    /// and no name leaks a type.</summary>
    [Fact]
    public void Every_control_on_the_slice_5_pages_is_spoken_with_its_label_first() => AppHost.Run(() =>
    {
        var (window, svm, _, guard) = Open();
        try
        {
            var bad = new List<string>();
            // Whole pages for the five this slice rebuilds; on About only the Version section, the one part this slice changes.
            foreach (var (page, anchor, wholePage) in new (SettingsPage, string?, bool)[]
            {
                (SettingsPage.Accounts, null, true), (SettingsPage.Alerts, SettingsIndex.EachLimit, true),
                (SettingsPage.ClaudeUsage, SettingsIndex.MeterHistory, true), (SettingsPage.Publish, SettingsIndex.Sample, true),
                (SettingsPage.Widget, SettingsIndex.WidgetOptions, true), (SettingsPage.About, SettingsIndex.Version, false),
            })
            {
                window.SelectPage(page, anchor);
                Settle(window, guard);
                var tab = ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == page.ToString());
                DependencyObject root = wholePage ? (DependencyObject)tab.Content : SettingsAnchor.Find((DependencyObject)tab.Content, anchor!)!;
                foreach (var b in Below<ButtonBase>(root).Where(b => b.IsVisible && b.Content is string))
                {
                    var name = Spoken(b);
                    if (name.Length == 0 || !name.StartsWith(Bare((string)b.Content), StringComparison.Ordinal))
                        bad.Add($"{page}: \"{b.Content}\" is spoken \"{name}\"");
                }
            }
            Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
            Assert.Empty(LeaksIn(NamesOf(window)));
        }
        finally { window.Close(); }
    });

    /// <summary>A landing on Each limit puts the keyboard on the first limit's Warn box; on the Widget page, on Compact mode.</summary>
    [DesktopFact]
    public void A_landing_puts_the_keyboard_on_the_sections_first_control() => AppHost.Run(() =>
    {
        var (window, svm, _, guard) = Open();
        try
        {
            guard.RouteKeyboard();
            foreach (var (page, anchor, expected) in new[]
            {
                (SettingsPage.Alerts, SettingsIndex.EachLimit, SettingsNames.WarnForLimit(svm.Alerts.Limits[0].Label)),
                (SettingsPage.Widget, SettingsIndex.WidgetOptions, SettingsNames.CompactMode),
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
        finally { window.Close(); }
    });
}
```

- [ ] **Step 2: Run them**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~Slice5PagesTests > %TEMP%\slice5-t13.log 2>&1`
Expected: PASS, the `[DesktopFact]` included. These pin behavior built in Tasks 4 to 12, so a first run may pass at once; a failure names the control, its name or the place and points at a XAML gap (most likely a spoken name that doesn't start with its label, such as a button whose `AutomationProperties.Name` was set to a sentence). Fix it in the page's XAML, run once more, never in a loop.

- [ ] **Step 3: Commit**

```bash
git add windows-dotnet/tests/Sanduhr.App.Tests/Slice5PagesTests.cs
git commit -m "test(settings-v2): slice 5's Done line, and its six pages spoken with their labels and landing the keyboard"
```

(Add the XAML file to the commit if Step 2 needed a fix.)

---

### Task 14: End to end: the harness probes the new landings; the whole suite; the owner's checks

**Files:**
- Modify: `windows-dotnet/tools/Sanduhr.RenderHarness/SettingsPassScenario.cs` (the Claude Usage units; the History focus shot)

**Interfaces:**
- Consumes: everything above.
- Produces: `anchors-landing.txt` lines for every new anchor at 800 and 600; a green suite; the Tier 2 hand-off.

- [ ] **Step 1: The harness entries**

In `windows-dotnet/tools/Sanduhr.RenderHarness/SettingsPassScenario.cs`:
- In the pages loop, add a Meters unit first for Claude Usage: before `units.Add(new PassUnit(dir + "-overview", …));` add `units.Add(new PassUnit(dir + "-meters", page, SettingsIndex.MetersView, header + " (Meters view)"));`.
- Give `Shot` a view: change `async Task Shot(string name, SettingsPage page, Func<UIElement?> find)` to `async Task Shot(string name, SettingsPage page, Func<UIElement?> find, string? view = null)` and its first line to `await SelectUnitTop(c, page, view);`.
- Replace `await Shot("history-week-pill", SettingsPage.History, () => ByName(c.Host, "Week"));` with `await Shot("meters-week-pill", SettingsPage.ClaudeUsage, () => ByName(c.Host, "Week"), SettingsIndex.MetersView);` (the History tab is gone; `TabOf` would find nothing).
- Then: `git grep -n "SettingsPage\.History\|SettingsIndex\.Chart\|SettingsIndex\.HistoryExport" -- windows-dotnet/tools` prints nothing else, or only an entry that exists to prove an old link lands (leave that one; the alias carries it).

- [ ] **Step 2: Build and run the harness's links part off screen, against a scratch profile**

Run: `dotnet build tools/Sanduhr.RenderHarness > %TEMP%\slice5-t14-build.log 2>&1` (from `windows-dotnet/`)
Expected: Build succeeded, 0 errors, no new warnings.

From the repo root, in PowerShell:

```powershell
$env:SANDUHR_DEV_PROFILE = Join-Path $env:TEMP "sanduhr-slice5-profile"
New-Item -ItemType Directory -Force $env:SANDUHR_DEV_PROFILE | Out-Null
dotnet run --project windows-dotnet/tools/Sanduhr.RenderHarness -- --scenario settings-pass --parts links --out (Join-Path $env:TEMP "sanduhr-slice5-out") *> (Join-Path $env:TEMP "slice5-t14-harness.log")
```

Expected: the run ends normally and its `settings-pass: foreground window before X, after X` line shows the same handle twice.

- [ ] **Step 3: Check every new landing**

```powershell
$out = Join-Path $env:TEMP "sanduhr-slice5-out\settings-pass"
Select-String -Path (Join-Path $out "anchors-landing.txt") -Pattern "^(Accounts|Alerts|Claude Usage|Publish usage|Widget|About) / .* \| (800|600) \|" |
    Where-Object { $_.Line -notmatch "\| (fully in view|top in view)[^|]* \|[^|]*\| ok$" }
Select-String -Path (Join-Path $out "deeplinks.txt") -Pattern "(CARD|TOUR)-LANDING" |
    Where-Object { $_.Line -notmatch "at 800: (fully in view|top in view)[^;]*; ok" }
```

Expected: no output, with two allowed exceptions that are hidden by state, not by layout: `Accounts / data` when the harness profile has no account, and `Widget / just-the-desk` while Just the Desk is off. Every other anchor (`accounts`, `follow`, `sign-in`, `notifications`, `thresholds`, `each-limit`, `sound`, `test`, `meters`, `meters-export`, `destination`, `token`, `homes`, `schedule`, `sample`, `widget-options`, `version`) lands in view at 800 and 600, and every What's New and tour card still lands in view (the "limits-come-and-go" card on Alerts, Each limit). A printed line names the anchor or card and why; fix the landing and run Steps 2 and 3 once more. The evidence under `%TEMP%` is never committed.

- [ ] **Step 4: The whole suite and the contracts**

Run: `dotnet test Sanduhr.slnx > %TEMP%\slice5-t14-suite.log 2>&1` (from `windows-dotnet/`)
Expected: PASS across `Sanduhr.Tests`, `Sanduhr.App.Tests` and `Sanduhr.Mcp.Tests`, every `[DesktopFact]` included (this is a desktop session). Read failures from the log file. Then:

```bash
git diff release/4.0 --stat -- windows-dotnet/tests/Sanduhr.Mcp.Tests/TrustBoundaryTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsKeysContractTests.cs windows-dotnet/src/Sanduhr.Mcp windows-dotnet/src/Sanduhr.Core/TierModel.cs windows-dotnet/src/Sanduhr.Core/CcLogReader.cs
```

prints nothing: the MCP's link list, its pin, the MCP source, the two linked Core files and the frozen settings keys are untouched.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/tools/Sanduhr.RenderHarness/SettingsPassScenario.cs
git commit -m "test(settings-v2): the harness lands every slice 5 anchor at 800 and 600 and shoots the Meters view"
```

- [ ] **Step 6: Hand the Tier 2 checks and the owner's calls to the owner**

Not code, and not for the agent: they need the real app on real screens and a real profile, which this plan never launches. Post them to the owner as the slice's open checks:

- **Item 7 (WF32, signed out):** with no account, Claude Usage, Meters reads "Sign in to start recording your meters. Sanduhr keeps 30 days on this PC."; Accounts shows "Claude Usage, Alerts and the meters fill in after you sign in." and no Data section; Alerts, Each limit reads "Your limits show here after you sign in."; the Claude Code card's Linked account reads "Add an account to link this folder." (slice 4). Then Just the Desk: choose it on the first-run card, open Settings, Widget: "Sanduhr is in Just the Desk: the widget is hidden." and "Show the widget" brings the widget back with the first-run card, restores the Desk's sign-in reminder (it was on before), and a relaunch starts with the widget shown. The tray while Just the Desk is on (its menu and a click).
- **Item 8 (WF14 and WF28, real homes):** on the real profile, Claude Usage, Overview's Session archive list and Publish usage's "Folders to share" list every Claude Code folder, and each reads its empty line on a profile with none. **Load time (P22, carried from slice 4):** open Claude Usage, Publish usage, Accounts and Claude Code a few times on the real profile (every folder, 40+ plugins) and note any visible stall as a page appears; both folder lists and the Overview read folders on the UI thread.
- **The sample on a real endpoint:** tick one folder, press "Show a sample…", then "Publish now": the endpoint receives what the sample showed, the PC's name and the project names included. With no folder ticked, the sample says so.
- **Each limit with a real account:** mute a limit from the card menu with Settings open on Alerts: the row follows and keeps keyboard focus; "Hide until it resets" on a promo limit shows "Hidden until it resets…" under its row, and ticking Show this limit brings it back.
- **Accounts:** add a second account through the embedded window: the prompt reads "Name this account", field "Label", "Account 2" filled in. Clear one account's history: only its sparklines empty. Remove account…: the question names everything removed and Enter picks neither button.
- **The owner's taste list** (above, "Owner's taste"): Claude Usage opening on Meters, the label prompt's words, the hidden-until-reset caption, the graph style names, the minutes box, the sample window, the retargeted 3.5.0 card, "Widget options" in search, Remove's caption, the strip tooltips, the tour's four core words, and the Linked folders link's landing.

---

## Carried in, and where each went

Every row of `.superpowers/sdd/carry-into-slice-5/` addressed to slice 5, and the slice-wide lessons:

| # | Carried item (source) | Where |
| --- | --- | --- |
| 1 | WF32's "Add an account to link this folder." on the Claude Code card (spec, slice 5; slice 4 decision 21, preflight: "slice 5's plan must drop its copy") | **Ruled out: shipped in slice 4** (`SettingsNames.AddAccountToLink`, `ClaudeCodePage.xaml`). This plan does only Accounts' "Linked folders" half (Task 7) and checks the card line in Tier 2 item 7. |
| 2 | Tier 2 item 8's History vault and "Homes to share" halves (slice 4 Task 15 Step 6: "are slice 5's") | Task 14 Step 6 |
| 3 | P22 / T8 m7: UI-thread folder reads need a load-time note (slice 4 preflight, final review triage 8) | Task 14 Step 6 (item 8's load-time note covers Claude Usage, Publish usage, Accounts and Claude Code) |
| 4 | P14: `DefineWidget` becomes the Widget page's first line; the owner's sign-off that the tour carries 4 of the 7 core words (slice 1 final review, ledger) | Task 3 (`WidgetLine`) and Task 9 (the page opens with it); the sign-off is owner taste 11 |
| 5 | `MenuAnchor` null case pinned only for Themes; "carry to slices 3 and 5 (Launcher, `each-limit`)" (slice 2 final review) | Task 6 (`Menu_links_land_on_their_section` pins Alerts → `each-limit`; `The_tier_card_and_the_limits_card_land_on_each_limit`) |
| 6 | Callers (the tier card) must reach the right places while `each-limit` is absent; T11 passes only checked anchors (slice 2 ledger, Task 3) | Task 6: the anchor and the menu change land in one commit, and App routes the tier card through `SettingsIndex.Check` as before |
| 7 | "No history yet" in two literals; delete `HistoryChart.cs:82`'s branch "when History folds into Claude Usage meters" (slice 2 final review T15) | Task 8 (both lines in `SettingsNames`; the chart's dead text deleted) |
| 8 | Slice 4 final review I2: "Slice 5 needs the same [tooltip fix] for History, and this fix covers it" | Task 8 (`A_card_that_named_History_says_it_opens_Claude_Usage` pins it); no shipped card names History today |
| 9 | Slice 4 preflight P4: `SidebarPages` excludes retired pages; "Slice 5 does the same for History" | Task 8 (`SidebarPages` excludes History) and Task 9 (Widget joins the sidebar) |
| 10 | Slice 1 Minor 5: History's intro said "usage history" with an em-dash; "slice 5 could take it" | **Ruled out: already fixed** (the intro reads "Rolling 30-day meter history. Stored locally only, never uploaded."); Task 8 moves the line into `SettingsNames.MetersIntro` |
| 11 | Slice 1 T4: "Nothing archived yet." can go stale until reopen; "slice 5 reworks the row" | Task 12 (About requeries its commands on each visit) |
| 12 | Slice 1 T6: the strip's tooltip copy, for the owner to restyle "in slice 5's Widget page pass" | Owner taste 10 (kept as is) |
| 13 | Every new user-facing string in `SettingsNames`, and `PathProblems` passes; moved copy may stay (slice 4 lesson, P10) | Global Constraints; Task 3 (one pinning fact per group); each page task |
| 14 | `[Collection("SettingsWindow")]` on every App class touching `AppWindowFixture`, `ModsPageFixture` or process-wide statics (theme-match, slice 4) | Global Constraints; every new App test class carries it |
| 15 | `NoActivate.Show` + `Check()`; `RouteKeyboard` is WM_SETFOCUS only; `[DesktopFact]` is skippable (slice 4) | Global Constraints; Tasks 6 to 13 |
| 16 | `DecorativeText` for decorative text outside templates; Expander toggles use `HeaderSite` (slice 4) | Global Constraints (this slice adds neither) |
| 17 | Page ScrollViewers `Focusable="False"`; focus rings from the window's styles (slice 2 P5, slice 3 P25) | Global Constraints; the Accounts page's new ScrollViewer, `WidgetPage.xaml` |
| 18 | A ListBox needs an explicit `TabNavigation` (slice 4 T9) | Global Constraints; this slice's new lists are `ItemsControl`s, `AccountsList` keeps its single-selection list behavior |
| 19 | A live region must raise `LiveRegionChanged` (`LiveRegion.Shown`) (slice 4 T12) | Tasks 8 and 9 (the meter history's empty line and the Deep Work hint use `LiveRegion.Shown`) |
| 20 | No control that can't act (slice 4 Done) | Decision 15 (Limit order's end buttons collapse); Task 13's checks |
| 21 | Destructive confirms have no default button (slice 4 T9) | Task 7 (`ShowConfirm` for Clear and Remove) |
| 22 | Privacy copy must be exactly true (slice 4 final ruling) | Decisions 7 and 10; Task 3's pins |
| 23 | No test reads the real AppData; implementers keep full test logs; no `sed -i` (slice 4) | Global Constraints; every run step writes a log under `%TEMP%` |

## Carried, not in this slice

- **Jump links on Alerts, Publish usage and About** (decision 19): to slice 8's accessibility sweep.
- **Slice 4's other carries** (final review triage rows 5, 6, 13 to 16, 20 to 32, 34, 35: catalog row walks, "keyboard routing worked: False", Unreadable folder plugins, meters row peers, LiveSetting-only status lines, the double `Desk.Reload`, the copy and comment sweeps): addressed to slice 8 or the docs pass, not to slice 5.
- **The touches wording outside `SettingsNames`** (slice 4 calls review): slice 8's copy sweep.
- **Streamer mode on these pages** (account labels, the Meters legend and picker, the sample record's endpoint host and folder names, "Exports keep real names."): slice 7.
- **About, Updates and the Privacy page** (`update_check`, "What leaves this PC"): slice 6. The Publish row there should quote this slice's `PublishRecordLine` facts.
- **Demo mode's "No history yet"** (slice 2 final review): slice 7 (demo and streamer mode).
- **The vault link's click test** (slice 2 T16: same handler and tag as the Publish link): left as triaged.
- **The Tier 2 checks** (Task 14 Step 6): the owner, on real screens.

---

## Spec coverage (slice 5)

| Slice 5 bullet, Done item or Tier 2 item (spec) | Task |
| --- | --- |
| The Meters view (chart, Week and Month, Account:, "Export meter history…"), first pill; the signed-out text (WF14, WF32) | 8 |
| History retired and aliased (`history`, `chart`, `export`, the page row; the retired title) | 8 |
| Session archive names (WF14): "Session archive" on Overview and About, "Erase session archive…", the no-folders line | Shipped in slices 1 and 2; Task 8 keeps them (`Lists_the_close_button_…` pins "Session archive folders") |
| Accounts' Data section: Linked folders with "Claude Code settings…", "Clear this account's history…", "Remove account…" with its caption (WF1, WF27, WF29) | 7 |
| Accounts' Sign in section; signed-out line (Principle 6) | 7 |
| The Label prompt on first sign-in, "Personal" filled in (WF30) | 2, 4 |
| Alerts, Each limit: Warn for this limit, Show this limit, Hidden until it resets; the tier card's link lands there (WF13) | 1, 5, 6 |
| The Widget page: Compact mode, Graph style, Keep the widget on top, Deep Work length, Limit order (WF15, WF24) | 1, 5, 9 |
| Just the Desk's "Show the widget", ending the mode the way signing in does (WF32) | 2, 5, 10 |
| Publish usage's sample, built by the same function (WF28), and its empty states | 11 |
| "Don't show What's New after updates" on About (WF24) | 12 |
| Tests named by the spec: `SettingsIndexTests`, `JustTheDeskTests` (Show the widget ends the mode), a publish-sample test (the sample equals the next real record's fields), a per-limit settings round-trip | 2, 5, 6, 8, 9, 11 (`SettingsSlice5IndexTests` beside `SettingsIndexTests`; `ShowingTheWidgetEndsItTheWaySigningInDoes`; `The_sample_is_the_record_publish_now_sends`; `Muting_hiding_and_showing_a_limit_reach_the_rows_and_the_file_and_say_so`) |
| Done: every key in the widget-only list has a Settings control | 9, 12, 13 |
| Done: a muted limit can be restored from Alerts | 6 |
| Done: the first sign-in asks for a label | 4 |
| Tier 2 items 7 and 8 | 14 Step 6 |
| No settings key added, renamed or removed; no MCP link list or MCP source change; no MCP-linked Core file edited | Global Constraints, 14 Step 4 |

## Self-review (writing-plans checklist, run once)

1. **Spec coverage:** every slice 5 bullet, the Done line and Tier 2 items 7 and 8 map to a task above. The spec's file list is covered, with `Views/TierCard.xaml.cs` needing no edit (decision 14) and `Sanduhr.Core/JustTheDesk.cs` gaining `End` and `EndByShowWidget`.
2. **Placeholder scan:** no "TBD", "add appropriate", "similar to Task N" or code-free code step; Task 9's page carries one comment marking where Task 10's section goes, which Task 10 replaces verbatim.
3. **Type consistency:** `LimitRow`, `LimitSettings.Rows/Move/SavedOrder` (Task 1) are what Tasks 5, 6 and 9 call; `JustTheDesk.End` (Task 2) is what `WidgetViewModel.EndJustTheDesk` (Task 5) calls and Tasks 10 and App use; `AccountLabels.Suggested/Accept` (Task 2) are what Task 4 calls; `SettingsIndex.EachLimit`, `SignInSection`, `AccountData`, `MeterHistory`, `MeterHistoryExport`, `MetersView`, `WidgetOptions`, `JustTheDeskSection`, `Sample` and `ShowsTheWidget` are declared in the task that marks them and used only after; `HistoryTabViewModel.Refresh` (Task 7) is used by Task 8; `UsagePublishService.BuildRecord/SampleAsync/Handler` (Task 11) match the test.
4. **Review Focus:** the five lines above each have their test in the owning task (Tasks 6, 7, 10, 11, 8/6/9).
