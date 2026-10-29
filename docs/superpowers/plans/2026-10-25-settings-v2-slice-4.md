# Settings v2 for Windows, Slice 4 ("Claude Code cards and Mods & Widgets") Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Everything installed for Sanduhr and for Claude Code sits in one list on one page, Mods & Widgets, where Sanduhr's meters mod is switched per folder and every widget pack has an on switch that every consumer honors and a line saying where it shows; each Claude Code folder is one card that holds everything Sanduhr knows and installs for it, acting on its own folder; the catalog fetches only when opened; the Widgets and Mods & Config pages retire, and every old link still lands.

**Architecture:** Core first. `WidgetStore.SetEnabled` makes `widgets.json`'s existing `enabled` field writable, and `CompanionSettings.ActivePack` joins the three consumers that already read only enabled packs. A new pure `InstalledItems` (Core, not linked into the MCP) turns already-read data (each folder's `ModInventory.Read` report, `OwnModState` and meters status, the widget registry, the catalog) into `InstalledItem` rows with a scope map and a switch route, plus the merged catalog rows, the "Where it shows" line per pack kind, the filters and the plain reconcile note. App: every picker that lists pack content (the four font pickers, the ink presets, the companion, the theme strip) follows a switch while Settings is open; the Claude Code folder cards gain their own Statusline, MCP server and glow hook pieces, a meters status line and a collapsed "What this folder holds" (a new `FolderHoldsViewModel`), and the page moves into its own `ClaudeCodePage.xaml`; `WidgetsPage`/`WidgetsPageViewModel` become `ModsPage`/`ModsPageViewModel` and grow the one list, the per-folder meters section and the catalog that fetches on expand. Then the page flip: the Mods tab hosts `ModsPage` as "Mods & Widgets", the Widgets tab and Mods & Config's view model go, and the index, names and aliases move in the same commit. Last, the companion row and Themes point at the new page, the Done criteria run as tests, and the harness reports where every new anchor lands.

**Tech Stack:** .NET 10, WPF, xUnit 2.9 (Xunit.SkippableFact for `[DesktopFact]`), CommunityToolkit.Mvvm 8.4 (two-argument `On…Changed` hooks), the render harness (`tools/Sanduhr.RenderHarness`).

**Spec:** `docs/settings-v2-windows/spec.md`, "Shippable slices, Slice 4: Claude Code cards and Mods & Widgets" (the authority on scope), with the page sections "Claude Code (`claudecode`)", "Mods & Widgets (`mods`): one list", "Themes", and "Principles", "Sidebar", "Search", "Deep links and entry points" (Menus and Aliases), "Keyboard and screen reader", "Mapping", "Settings keys and migrations", "Outside Settings, same release" (core words) and the disposition rows for WF3, WF4, WF9, WF11, WF27, WF39, WF40, WF42. Findings in `docs/settings-v2-windows/findings.md` (WF3, WF4, WF9, WF11, WF27, WF39, WF40, WF42, and Tier 2 items 3, 6 and 8 under "Needs the live pass"); `mods-parity.md` §4 for the data model. The executed slice 3 plan, `docs/superpowers/plans/2026-10-24-settings-v2-slice-3.md`, is the model for conventions and helpers (`AppWindowFixture`, `ScratchWidget`, `NoActivate`, `Pump`, `SettingsAnchor.Find`, `SettingsLinkTo.Link`, `SectionExpander`). The records carried in are in `.superpowers/sdd/carry-into-slice-4/`.

## Spec questions decided in this plan

1. **`InstalledItems.Build` takes data, not paths.** "Pure" (spec) means no IO inside it: the App reads each folder (`ModInventory.Read`, `ClaudeCodeIntegrations.OwnModStateOf`, `.Status(Meters)`), the registry (`WidgetStore.Load`, `ReadInfo`) and the catalog document, and passes them in as `FolderMods` records. That keeps it testable without a disk. **`SettingsIndex` does not read it**, although the spec says "the page and SettingsIndex read it": the index is a static table built once at type load and search runs on every keystroke, so reading folders there would put IO into search. Search finds the sections through their synonyms (every plugin word, every pack kind) instead.
2. **The pages are renamed with their content.** `Views/WidgetsPage.xaml(.cs)` becomes `Views/ModsPage.xaml(.cs)` and `ViewModels/WidgetsPageViewModel.cs` becomes `ViewModels/ModsPageViewModel.cs` (`git mv`, Task 5), because the Mods tab ("Mods & Widgets") hosts it and a class named after the retired page would mislead every later reader. The spec's "WidgetsPage.xaml(.cs) (becomes the Mods & Widgets view)" is honored in substance. The `Not`/`NotVisible` converters move to `Converters/BoolConverters.cs` first (carried T9).
3. **`ModsConfigViewModel` retires in two steps.** Its per-folder report becomes a new `FolderHoldsViewModel` (Task 7), one per card; its plugin rows become `InstalledItems`; its meters commands move to the Mods & Widgets meters section. The old class and its `ModsConfigHost` are deleted in the page flip (Task 12), so every commit in between still drives the old Mods & Config tab. `ModsConfigHost` is replaced by `ClaudeCodeHost`, which adds the mod source folder so tests can install the meters mod into a scratch profile.
4. **The Claude Code page's own file.** Its TabItem body moves into `Views/ClaudeCodePage.xaml` (Task 8), as slice 3 did for Desk Look, Watchers and Launcher: slice 5 edits other TabItems of `SettingsWindow.xaml` in parallel, and an own file keeps the two slices' diffs apart.
5. **`folder-config` is one anchor on the page, opening one card.** Anchors are page-local and `SettingsAnchor.Find` walks the logical tree, which never reaches inside an `ItemsControl`'s templates. So `folder-config` marks the folder cards list, and a landing on it opens the "What this folder holds" of one card: the card `ModsFolderChoice.Pick` chooses (the folder whose holds were opened last this session, then `CLAUDE_CONFIG_DIR`, then the active account's folder, then the one with the most plugins on, then the first). That is the spec's "ModsFolderChoiceTests (updated for cards)": the rule now picks which card opens, not what a combo shows.
6. **Turning a folder's meters on where the mod is not installed asks first.** `ClaudeCodeIntegrations.SwitchOwnMod(folder, true)` installs silently when the entry is missing; the spec wants "the existing consent dialog". The section calls `Install(Meters, folder)` behind the consent dialog in that case and `SwitchOwnMod` only when the entry is listed.
7. **A switch shows the truth after a refusal.** Pack and meters switches bind `IsChecked` one way and act through a command; after the command the row is read back from disk. A declined consent, a failed write or an unreadable `settings.json` leaves the box showing what is really there (Review Focus 2).
8. **No dead control (Principle 5).** On a meters row, "Update…" shows only while the folder's entry is outdated and "Remove…" only while it is listed; a folder whose `settings.json` can't be read shows the reason and no switch. A read-only row carries no button or checkbox at all. That adds one value to the spec's scope states: `FolderState.Unreadable` ("Can't be read"), beside On, Off, Missing and Not installed.
9. **"A pack appears once" means once per list, with the same buttons.** WF40's complaint is two lists with different buttons. The Installed list shows every installed pack (catalog or made here) with its switch; the Catalog section shows each catalog pack once, merged by id: Install, Update, or the same switch the Installed row has. An update available shows Update on the catalog row; the switch stays on the Installed row.
10. **Effects packs are switchable.** The spec makes all five kinds switchable; an effects row says "Not used yet: Sanduhr has nothing that draws effects packs." and has no link.
11. **Switching off a pack that is in use.** The theme in use falls back to Obsidian without saving (the existing `ReloadUserThemes` rule) and comes back when the pack is switched on. The Desk ink keeps its colors (they are stored, not referenced); the preset just stops being selected. A font family that is the saved Desk or Message font stays listed as the saved value (so the box is never blank and nothing rewrites the setting) while the Desk draws its fallback, as an uninstalled font does today; every other family of the pack leaves the list. The companion draws the next enabled sprite, or none; the chosen pack stays saved, so switching it back on restores it.
12. **The Themes link and the Installed anchor.** The spec's Menus table sends Themes to `mods#installed`, but the Installed section "keeps the id" `mods` and Themes has its own `installed` anchor. The link is `mods#mods` and it sets the Widgets filter.
13. **The catalog fetches once per Settings window.** Opening the section (by click, by a landing on `mods#catalog`, or the companion row's link) fetches when nothing was fetched in this window; "Check the catalog again" always fetches. Opening the page, a search hit or a link elsewhere on the page fetches nothing.
14. **The companion row's "filtered to sprites".** The link lands on `mods#catalog` and also sets the catalog's kind filter to sprites; a "Show every pack" link clears it. The Installed filter chips are untouched.
15. **The reconcile note is summed over folders**, in the spec's words, under the Installed list: "<n> plugins have no on/off entry in settings.json, so Claude Code treats them as on." and "<n> plugins are switched on in settings.json, but their files aren't in that folder."
16. **The read-only label drops the spec's backticks**: "Read-only: Sanduhr can't switch this yet. Use claude plugin in a terminal." The backticks are Markdown in the spec, not copy.
17. **"Uninstall…" asks first.** The spec gives it an ellipsis, which in this app means a question follows.
18. **Retired names: "Mods & Config" and "Widgets settings…".** The spec retires the titles "Widgets" and "Mods & Config". "Widgets" can't be a retired whole name, because the spec's own filter chip is labelled "Widgets"; the page title is guarded instead by `PathProblems` (no page "Widgets" after "Settings, ") and the retired link form "Widgets settings…".
19. **Neither page gets jump links.** Claude Code has two sections (Folders, Claude Code glow) and Mods & Widgets three (Meters above the prompt, Installed, Catalog); the spec gives jump links to pages with four or more.
20. **The glow hook's name points at the cards.** "Claude Code glow hook" (`SettingsNames.ClaudeCodeGlowHook`) is installed from a card now, so its index anchor moves from `glow` to `folders`; the `glow` section's title becomes "Claude Code glow" (`SettingsNames.DeskGlow`, the piece's name).
21. **WF32's linked-account line rides along.** "Add an account to link this folder." in place of the combo when only "None" is offered is WF32 and listed under slice 5, but slice 4 rewrites the card into its own file; adding it here saves slice 5 a merge into a file it doesn't own (see "Merge surface").
22. **`statusline_enabled` follows a meters change on the Mods page.** It is recomputed by `ClaudeCodeViewModel` today; the meters section raises `MetersChanged(installed)` and the Claude Code page runs its sync (with a forced snapshot write after an install, as its own installs do).

## Owner's taste (decided with a default; each is one line to change)

1. **The pack switch's word**: visible "On", spoken "On: <pack name>" (`SettingsNames.PackOn`, `PackOnFor`). The meters rows use the same word ("On: Meters above the prompt in ~\.claude").
2. **Font packs get a link** ("Desk Look settings…" to `desklook#look`), though the spec lists a link only for themes, palettes and sprites.
3. **One folder reads plainly**: with one Claude Code folder, a scope reads "On", "Off" or "Files missing" instead of "On in 1 of 1 folders".
4. **The Installed row's link to section 1** reads "Switch it per folder…" (`SettingsNames.MetersLinkUp`).
5. **"Uninstall…" asks first**, "Uninstall <pack>?" with "Uninstall" and "Cancel" (decision 17).
6. **The theme pack count on Themes counts packs that are off too** ("Theme packs: 3 installed.").
7. **The folder-config landing order** (decision 5): the folder you opened last, then `CLAUDE_CONFIG_DIR`, the active account's, the most plugins on, the first.

## Global Constraints

- **CRLF working tree, LF index.** Edit tracked files with the Edit tool only. Never `sed -i`, never rewrite a tracked file wholesale with Write. New files may be created with Write. Renames are `git mv` followed by Edit.
- **Keys.** No `settings.json` key is renamed, removed or added in this slice (spec, "Settings keys and migrations": the two additions, `update_check` and `streamer_mode`, belong to slices 6 and 7). `SettingsKeysContractTests` is not touched. The pack switch writes `widgets.json`'s existing per-pack `enabled` field (`WidgetStore.cs:110`, `:305`), which is not a settings key. The meters switch writes the folder's own `settings.json` through `ClaudeCodeIntegrations` and its receipts, as Mods & Config did. The remembered holds folder and the filters are memory only.
- **Trust boundary.** No file is added to `Sanduhr.Mcp.csproj`'s `Compile Include` list. The new `InstalledItems.cs` and every Core file this slice changes (`WidgetStore.cs`, `CompanionSettings.cs`, `ModsPageText.cs`, `WidgetCatalogClient.cs`, `SettingsNames.cs`, `SettingsIndex.cs`, `WhatsNewCards.cs`, `ModsFolderChoice.cs`) are unlinked and must stay unlinked: none of them is in the list, and `InstalledItems` reads `WidgetStore`, `WidgetCatalog` and `ClaudeCodeIntegrations` types the MCP must never learn. The one Mcp edit is a string in `ToolCatalog.cs` ("Settings, Widgets" becomes "Settings, Mods & Widgets"). `TrustBoundaryTests` stays green untouched.
- **`~\.sanduhr` is shared ground.** This slice adds no file there. The meters mod's copy under `%USERPROFILE%\.sanduhr\mods` is written and pruned only by `ClaudeCodeIntegrations`, as today.
- **Hands off the live app.** Never start, stop, kill or attach to `Sanduhr.exe`. If a build fails because a `Sanduhr.exe` holds a file, stop and ask the owner. No test reads the real AppData, LocalAppData, `~\.sanduhr`, a real Claude Code folder or Credential Manager: App tests derive from `AppWindowFixture` (scratch `SANDUHR_DEV_PROFILE`, `ClaudeCodeProfile` overrides) and build widgets with `ScratchWidget(...)`, or build stores and hosts over scratch folders under `%TEMP%`. Never `DemoWidget()` in a new test, and never the real network (`WidgetCatalogService` gets a `FakeGitHub` or the fixture's `OfflineHandler`).
- **Test hygiene (theme-match and slice 3 lessons).** Any App test class that derives from `AppWindowFixture`, builds a `SettingsWindow` or `SettingsViewModel`, uses `ScratchWidget`, or touches a process-wide static (`SANDUHR_DEV_PROFILE`, `ClaudeCodeProfile.*Override`, `WidgetCatalogService.HandlerOverride`, `NowPlayingHub/LookService.Current`, `ClaudeCodeViewModel.ForgetSessionChoice`) carries `[Collection("SettingsWindow")]`. A window is shown through `NoActivate.Show(window)`, and `guard.Check()` follows anything that moves focus; keyboard focus is routed with `guard.RouteKeyboard()` (WM_SETFOCUS only). `[DesktopFact]` is skippable and runs locally: it must pass before the slice is done, once per change, **never re-run in a loop**. Assert messages in focus tests name the focused element and the page.
- **Run tests from `windows-dotnet/`.** `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~<Class>` for Core, `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~<Class>` for App, `dotnet test tests/Sanduhr.Mcp.Tests` for the server.
- **Names.** Every new visible or spoken string is a `SettingsNames` member or built from one (the names table holds the copy). Buttons that open Settings read "<Page> settings…" (`SettingsNames.LinkTitle`); prose that names a place uses "Settings, <Page>, <Section>" with a section title from `SettingsIndex` (`PathProblems` is the scan). A control's spoken name starts with its visible text (`Visible_labels_start_their_spoken_names`).
- **Index with the XAML.** An anchor joins `SettingsIndex.Anchors` in the same commit that marks it in XAML and leaves it in the commit that removes the mark. When an anchor leaves, its alias target must be live in the same commit, or the alias's fallback must land somewhere real (`WhatsNewTests.Every_Settings_card_names_an_anchor_in_the_index`). Copy that names a page from `TitlesToCome` lands in the same commit as the page (slice 1 review).
- **Own-file pages and controls.** Pages are `UserControl`s named `<Name>Page` in `Views/` (the anchor scans read `<views:\w+Page>`). They use the window's styles through `DynamicResource` (`FlatButton`, `AccentButton`, `SettingsLinkButton`, `ThemedCheckBox`, `ThemedComboBox`, `Input`, `SectionExpander`, `EmptyLine`), never a local style with `FocusVisualStyle="{x:Null}"` and no keyboard-focus trigger; focus rings come from the window's ring styles. Their `ScrollViewer` is `Focusable="False"`. A `TextBlock` outside a template that only decorates (a separator dot, a glyph) is `controls:DecorativeText`, because a plain one is a UIA control element. Every `SectionExpander` gets a non-empty `AutomationProperties.HelpText` (slice 3 review M3: a null one leaves an empty line).
- **Branch.** Tasks run in numeric order on `feat/4.0-slice-4`, cut from `release/4.0` at 3519257. Slice 5 runs in parallel: each slice owns its pages' XAML blocks; rebase on `SettingsWindow.xaml`, `SettingsIndex.cs` and `SettingsNames.cs` conflicts (see "Merge surface"). The owner pre-approved executing without a plan-review stop.

## Review Focus

1. **Switching off a pack that is in use.** A person switches off the theme pack whose theme the widget wears, the palette pack whose colors the Desk ink uses, the font pack whose family is the Desk font, or the sprite that is the companion. Expected: the widget wears Obsidian at once without the saved theme changing, and wears the pack's theme again when it is switched back on; the ink keeps its colors; the Desk font box still shows the saved family; the companion draws the next enabled sprite and returns to the chosen one when it is back on. Pinned in Task 6 (`Switching_off_the_theme_in_use_falls_back_unsaved_and_back_on_restores_it`, `A_saved_font_from_a_pack_switched_off_stays_selected`, `The_companion_skips_a_sprite_that_is_off_and_returns_to_it`) and Task 1 (`ActivePack_skips_a_sprite_that_is_off`).
2. **A meters switch that can't do what it was asked shows the truth.** Declining the consent dialog on a folder without the mod, or a folder whose `settings.json` is not JSON, must leave the box unchecked, write nothing, and say why. Pinned in Task 10 (`Declining_consent_leaves_the_folder_off_and_unwritten`, `An_unreadable_folder_offers_no_switch_and_says_why`).
3. **Nothing leaves the PC until the catalog is opened.** Opening Mods & Widgets, landing on `mods#meters` or `mods#mods`, a search hit, or switching a pack sends no request; opening the Catalog section sends exactly one fetch, re-opening sends none, "Check the catalog again" sends one more. Pinned in Task 11 (`Opening_the_page_fetches_nothing_and_opening_the_catalog_fetches_once`) and Task 12 (`Landing_on_the_page_sends_no_request`).
4. **Old links land on the new pages.** `widgets`, `widgets#widgets-catalog`, `widgets#widgets-installed`, `widgets#widgets-companion`, `claudecode#meters`, `claudecode#statusline`, `claudecode#mcp`, `mods#config`, `mods#mcpservers`, `mods#claudemd`, `mods#retention`, the What's New cards with `SettingsPage.Widgets`, `--settings widgets` and agent text naming "Settings, Mods & Widgets" each land where the spec's alias table says. Pinned in Task 8 (`Every_claude_code_and_config_alias_lands_on_a_card`, the window-level `A_link_to_folder_config_opens_the_chosen_cards_report_in_view`) and Task 12 (`Every_slice_4_alias_lands_on_its_new_home`, the window-level `Old_widget_and_mods_links_land_on_the_new_page`).
5. **Each card acts on its own folder, and only on it.** With three folders, a Remove of the glow hook on one card, or its install on another, changes that folder's `settings.json` and leaves the other two byte for byte; a card for a folder added by hand offers no MCP server. Pinned in Task 8 (`A_card_removes_only_its_own_folders_glow_hook`, `The_glow_hook_installs_into_the_cards_folder_only`, `Each_cards_consent_names_its_own_folder_and_a_no_writes_nothing`, `A_card_for_a_folder_added_by_hand_offers_no_MCP_server`), and for the meters in Task 10 (`Turning_on_where_it_isnt_installed_asks_then_installs_into_that_folder_only`).

---

## Where this plan steps outside the spec's slice 4 file list

The spec's list is the core; these extra files deliver its own bullets:

- Core: `CompanionSettings.cs` (the companion honors `enabled`), `ModsPageText.cs` (the plain note), `WidgetCatalogClient.cs` (the offline copy, WF40), `ModsFolderChoice.cs` (its summary: it picks a card now), `SettingsNames.cs`, `SettingsIndex.cs`, `WhatsNewCards.cs` (one card body names "Settings, Mods & Widgets").
- Mcp: `ToolCatalog.cs` (one string).
- App: new `Converters/BoolConverters.cs`, `ViewModels/ClaudeCodeHost.cs`, `ViewModels/FolderHoldsViewModel.cs`, `ViewModels/FontListSync.cs`, `Views/ClaudeCodePage.xaml(.cs)`; renamed `Views/ModsPage.xaml(.cs)` and `ViewModels/ModsPageViewModel.cs`; deleted `ViewModels/ModsConfigViewModel.cs`; modified `ViewModels/SettingsViewModel.cs`, `DeskSettingsViewModel.cs`, `DeskMessagePageViewModel.cs`, `ThemeDeskEditorViewModel.cs`, `ThemeStudioViewModel.cs`, `ThemesViewModel.cs`, `DeskCompanionViewModel.cs`, `Views/DeskPiecesPanel.xaml`, `Views/SettingsWindow.xaml.cs`, `App.xaml.cs`.
- Harness: `tools/Sanduhr.RenderHarness/CompanionScenario.cs`, `ProposeWidgetScenario.cs` (the rename), `SettingsPassScenario.cs` (the new landings).
- Tests: new `tests/Sanduhr.Tests/InstalledItemsTests.cs`, `PackEnabledTests.cs`, `SettingsModsIndexTests.cs`; new `tests/Sanduhr.App.Tests/PackSwitchConsumersTests.cs`, `FolderHoldsTests.cs`, `ClaudeCodeCardsTests.cs`, `ModsPageFixture.cs`, `ModsPageListTests.cs`, `ModsPageMetersTests.cs`, `ModsPageCatalogTests.cs`, `ModsAndCardsPagesTests.cs`; renamed `ModsPageNamesTests.cs` (from `WidgetsPageNamesTests.cs`); rewritten `ClaudeCodeFolderChoiceTests.cs`; deleted `ModsConfigFixture.cs`, `ModsConfigFolderTests.cs`, `ModsConfigPluginsTests.cs` (their facts move to `FolderHoldsTests`, `InstalledItemsTests` and `ModsPageListTests`).

Not in this slice, though the spec mentions it nearby: the streamer-mode masks on these pages and the "Show real name on stream" route on a Mods & Widgets row (slice 7), Accounts' "Linked folders" with "Claude Code settings…" (slice 5), Reset's per-folder list (slice 8), the cross-folder Claude Code catalog (R2, not in 4.0).

## Merge surface with slice 5

| File | Slice 4 touches | Slice 5 likely touches | Risk |
| --- | --- | --- | --- |
| `Views/SettingsWindow.xaml` | resources (none added), the Claude Code TabItem (body replaced by `<views:ClaudeCodePage />`), the Mods TabItem (header, body replaced by `<views:ModsPage />`), the Widgets TabItem (deleted), the Themes TabItem (one status line in row 0) | Alerts, Accounts, History, Claude Usage, Publish TabItems; a new Widget TabItem | Low: whole-block edits in different TabItems. Keep both sides. |
| `Core/SettingsIndex.cs` | Claude Code and Mods rows of `Anchors`, `NameAnchors`, `PageSynonyms`, `CollapsedSections`; the Widgets rows out | Alerts `each-limit`, History rows out, Claude Usage `meters`, Widget page rows, `MenuAnchor(Alerts)` | Medium: line-per-entry; keep both sides' rows. |
| `Core/SettingsNames.cs` | `Sidebar` (Extend and Desktop groups), `PageTitle(Mods)`, `TitlesToCome`, `Table`, `Retired`, new constants | `Sidebar` (Usage and Widget groups), `PageTitle`, `Table`, `Retired` | Medium: `Sidebar` and `TitlesToCome` are shared lines; keep both sides' entries. |
| `ViewModels/SettingsViewModel.cs` | `ModsPage` replaces `WidgetsPage` and `Mods`; `PacksChanged`; `PickerFonts` | History and Accounts members | Low |
| `Views/ClaudeCodePage.xaml` (new) | the whole card, including WF32's "Add an account to link this folder." | slice 5's WF32 line on the card (now done here) | Low once slice 5 drops that line from its own plan. |

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Sanduhr.Core/WidgetStore.cs` (modify) | `SetEnabled(id, enabled)`. |
| `src/Sanduhr.Core/CompanionSettings.cs` (modify) | `ActivePack` reads enabled sprites only. |
| `src/Sanduhr.Core/InstalledItems.cs` (create) | The one list's model: `FolderMods`, `FolderScope`, `InstalledItem`, `Build`, `MetersState`, `StateWord`, `ScopeSummary`, `Matches`, `CatalogRows`, `Where`. |
| `src/Sanduhr.Core/ModsPageText.cs` (modify) | `PlainNote` in the spec's words; `Reconcile` goes. |
| `src/Sanduhr.Core/WidgetCatalogClient.cs` (modify) | `Offline` in WF40's words. |
| `src/Sanduhr.Core/SettingsNames.cs` (modify) | Slice 4 copy; Mods & Widgets title, sidebar, retired names, table rows. |
| `src/Sanduhr.Core/SettingsIndex.cs` (modify) | Claude Code and Mods anchors, collapsed sections, name anchors, synonyms; the aliases go live. |
| `src/Sanduhr.App/Converters/BoolConverters.cs` (create) | `Bools.Not`, `Bools.NotVisible`, shared by pages and panels. |
| `src/Sanduhr.App/ViewModels/FontListSync.cs` (create) | Syncs a bound list in place, so a picker keeps its selection. |
| `src/Sanduhr.App/ViewModels/ClaudeCodeHost.cs` (create) | What the Claude Code views need from the app; builds `ClaudeCodeIntegrations`. |
| `src/Sanduhr.App/ViewModels/FolderHoldsViewModel.cs` (create) | One folder's "What this folder holds". |
| `src/Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs` (modify) | Cards with their own pieces, meters line and holds; no install combo; glow hook status; landing on `folder-config`. |
| `src/Sanduhr.App/ViewModels/ModsPageViewModel.cs` (renamed, rewritten) | The one list, the meters section, the catalog on expand. |
| `src/Sanduhr.App/Views/ClaudeCodePage.xaml(.cs)` (create) | The Claude Code page. |
| `src/Sanduhr.App/Views/ModsPage.xaml(.cs)` (renamed, rewritten) | The Mods & Widgets page. |
| `src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (modify) | `ModsPage`, `PacksChanged`, `PickerFonts`, the page wiring. |
| `src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs`, `DeskMessagePageViewModel.cs`, `ThemeDeskEditorViewModel.cs`, `ThemeStudioViewModel.cs`, `ThemesViewModel.cs`, `DeskCompanionViewModel.cs` (modify) | Each picker follows a pack switch; the companion's link; Themes' pack line. |
| `src/Sanduhr.App/Views/SettingsWindow.xaml(.cs)`, `DeskPiecesPanel.xaml`, `App.xaml.cs` (modify) | TabItems, landing hooks, wiring. |
| `tests/Sanduhr.Tests/PackEnabledTests.cs`, `InstalledItemsTests.cs`, `SettingsModsIndexTests.cs` (create) | Core tests for this slice. |
| `tests/Sanduhr.App.Tests/PackSwitchConsumersTests.cs`, `FolderHoldsTests.cs`, `ClaudeCodeCardsTests.cs`, `ModsPageFixture.cs`, `ModsPageListTests.cs`, `ModsPageMetersTests.cs`, `ModsPageCatalogTests.cs`, `ModsAndCardsPagesTests.cs` (create) | App tests for this slice. |

---

### Task 1: The pack switch in Core, and the companion honors it

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/WidgetStore.cs` (after `Uninstall`)
- Modify: `windows-dotnet/src/Sanduhr.Core/CompanionSettings.cs` (`ActivePack`)
- Test: create `windows-dotnet/tests/Sanduhr.Tests/PackEnabledTests.cs`; modify `windows-dotnet/tests/Sanduhr.Tests/CompanionTests.cs`

**Interfaces:**
- Produces: `public WidgetStoreResult WidgetStore.SetEnabled(string id, bool enabled)` (writes only that row's `enabled`; Failed with a sentence for an unknown or unsafe id; Done and no write when the value is already that). `CompanionSettings.ActivePack(WidgetRegistry, Func<string, string?>)` now considers enabled rows only (same signature).
- Consumes: nothing new. The three consumers that already filter (`InstalledOfKind` behind `PackThemes.Merged`, `PackInkPresets.Merged`, `PackFonts.Folders`) are pinned here, one test each, so a later refactor can't drop the filter silently.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/PackEnabledTests.cs`:

```csharp
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// Settings v2, slice 4: a widget pack's on switch is widgets.json's existing "enabled" field, and every consumer honors it (spec,
/// Mods & Widgets: "Every consumer must honor it: theme list, ink presets, font lists, companion"). One test per Core consumer.
/// </summary>
public sealed class PackEnabledTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-packon-" + Guid.NewGuid().ToString("N")[..10]);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private WidgetStore Store() => new(Path.Combine(_root, "widgets"), WidgetTestKit.EncodePng,
        () => new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    private static void Install(WidgetStore store, string name, WidgetSource source = WidgetSource.Catalog)
    {
        var files = WidgetPackFixtures.Load("good", name);
        var v = PackValidator.Validate(files, name, strict: true);
        Assert.True(v.Ok, string.Join(" | ", v.Errors));
        Assert.True(store.Install(files, v.Manifest!, source).Ok);
    }

    [Fact]
    public void SetEnabled_writes_only_that_rows_flag_and_reads_back()
    {
        var store = Store();
        Install(store, "good-palette");
        Install(store, "good-theme");

        Assert.True(store.SetEnabled("good-palette", false).Ok);

        var reg = store.Load();
        Assert.False(reg.Find("good-palette")!.Enabled);
        Assert.True(reg.Find("good-theme")!.Enabled);
        Assert.Contains("\"enabled\": false", File.ReadAllText(store.RegistryFile));

        Assert.True(store.SetEnabled("good-palette", true).Ok);
        Assert.True(store.Load().Find("good-palette")!.Enabled);
    }

    [Fact]
    public void SetEnabled_to_the_same_value_writes_nothing()
    {
        var store = Store();
        Install(store, "good-palette");
        var stamped = DateTime.UtcNow.AddMinutes(-5);
        File.SetLastWriteTimeUtc(store.RegistryFile, stamped);

        Assert.True(store.SetEnabled("good-palette", true).Ok);

        Assert.Equal(stamped, File.GetLastWriteTimeUtc(store.RegistryFile));
    }

    [Theory]
    [InlineData("not-installed")]
    [InlineData("..\\evil")]
    [InlineData("")]
    public void SetEnabled_refuses_an_id_that_is_not_an_installed_pack(string id)
    {
        var store = Store();
        Install(store, "good-palette");
        var json = File.ReadAllText(store.RegistryFile);

        var r = store.SetEnabled(id, false);

        Assert.False(r.Ok);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
        Assert.Equal(json, File.ReadAllText(store.RegistryFile));
    }

    [Fact]
    public void An_update_keeps_the_switch_off()
    {
        var store = Store();
        Install(store, "good-palette");
        Assert.True(store.SetEnabled("good-palette", false).Ok);

        Install(store, "good-palette");   // same source again: an update

        Assert.False(store.Load().Find("good-palette")!.Enabled);
    }

    [Fact]
    public void A_theme_pack_that_is_off_leaves_the_theme_list()
    {
        var store = Store();
        Install(store, "good-theme");
        var themes = Path.Combine(_root, "themes");
        Assert.Contains("good-theme:fixture-dark", PackThemes.Merged(themes, store).Keys);

        store.SetEnabled("good-theme", false);

        Assert.DoesNotContain("good-theme:fixture-dark", PackThemes.Merged(themes, store).Keys);
    }

    [Fact]
    public void A_palette_pack_that_is_off_leaves_the_ink_presets()
    {
        var store = Store();
        Install(store, "good-palette");
        Assert.Equal(DeskInkPresets.All.Count + 1, PackInkPresets.Merged(store).Count);

        store.SetEnabled("good-palette", false);

        Assert.Equal(DeskInkPresets.All.Select(p => p.Name), PackInkPresets.Merged(store).Select(p => p.Name));
    }

    [Fact]
    public void A_font_pack_that_is_off_leaves_the_font_folders_and_comes_back_on()
    {
        var store = Store();
        Install(store, "good-font");
        Assert.Single(PackFonts.Folders(store));

        store.SetEnabled("good-font", false);
        Assert.Empty(PackFonts.Folders(store));

        store.SetEnabled("good-font", true);
        Assert.Equal("good-font", Assert.Single(PackFonts.Folders(store)).PackId);
    }

    [Fact]
    public void ActivePack_skips_a_sprite_that_is_off()
    {
        var store = Store();
        Install(store, "good-sprite");
        Install(store, "good-sprite-stages");
        string? Kind(string id) => store.ReadInfo(id)?.Kind;
        var chosen = new CompanionSettings("good-sprite-stages", true);
        Assert.Equal("good-sprite-stages", chosen.ActivePack(store.Load(), Kind));

        store.SetEnabled("good-sprite-stages", false);
        Assert.Equal("good-sprite", chosen.ActivePack(store.Load(), Kind));

        store.SetEnabled("good-sprite", false);
        Assert.Null(chosen.ActivePack(store.Load(), Kind));

        store.SetEnabled("good-sprite-stages", true);
        Assert.Equal("good-sprite-stages", chosen.ActivePack(store.Load(), Kind));
    }
}
```

In `windows-dotnet/tests/Sanduhr.Tests/CompanionTests.cs`, below `The_active_pack_is_the_pick_while_it_is_an_installed_sprite_else_the_first_sprite`, add:

```csharp
    [Fact]
    public void The_active_pack_is_never_a_sprite_that_is_off()
    {
        var reg = new WidgetRegistry(null,
        [
            new InstalledWidget("hourglass", "1.0.0", WidgetSource.Catalog, DateTimeOffset.UnixEpoch, false),
            new InstalledWidget("my-sprite", "1.0.0", WidgetSource.Claude, DateTimeOffset.UnixEpoch, true),
        ]);
        Assert.Equal("my-sprite", new CompanionSettings("hourglass", true).ActivePack(reg, Kind));
        Assert.Equal("my-sprite", new CompanionSettings(null, true).ActivePack(reg, Kind));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~PackEnabledTests|FullyQualifiedName~CompanionTests"`
Expected: build FAILS with `'WidgetStore' does not contain a definition for 'SetEnabled'`.

- [ ] **Step 3: Add `SetEnabled`, and filter the companion**

In `windows-dotnet/src/Sanduhr.Core/WidgetStore.cs`, after `Uninstall`'s closing brace, add:

```csharp

    /// <summary>
    /// Switches an installed pack on or off (Settings v2, Mods &amp; Widgets): its row's <c>enabled</c>, which <see cref="InstalledOfKind"/>
    /// and the companion read. Only that row changes; the pack's files stay. Writing the value it already has writes nothing.
    /// </summary>
    public WidgetStoreResult SetEnabled(string id, bool enabled)
    {
        if (!IsSafeId(id))
            return WidgetStoreResult.Failed("That isn't an installed widget.");
        lock (_gate)
        {
            var reg = Load();
            if (reg.Find(id) is not { } row)
                return WidgetStoreResult.Failed("That widget isn't installed.");
            if (row.Enabled == enabled)
                return WidgetStoreResult.Done;
            try
            {
                Save(reg with { Packs = [.. reg.Packs.Select(p => p.Id == id ? p with { Enabled = enabled } : p)] });
                return WidgetStoreResult.Done;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return WidgetStoreResult.Failed("The widget couldn't be switched: " + Plain(e));
            }
        }
    }
```

In `windows-dotnet/src/Sanduhr.Core/CompanionSettings.cs`, change `ActivePack`'s summary first sentence to `The pack the companion draws: the chosen one while it is installed as a sprite and switched on, otherwise the first such sprite by id.` and its first line to:

```csharp
        var sprites = registry.Packs.Where(p => p.Enabled && kindOf(p.Id) == "sprite").Select(p => p.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~PackEnabledTests|FullyQualifiedName~CompanionTests|FullyQualifiedName~WidgetStoreTests|FullyQualifiedName~PackFontsTests|FullyQualifiedName~DeskInkTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/WidgetStore.cs windows-dotnet/src/Sanduhr.Core/CompanionSettings.cs windows-dotnet/tests/Sanduhr.Tests/PackEnabledTests.cs windows-dotnet/tests/Sanduhr.Tests/CompanionTests.cs
git commit -m "feat(widgets): a pack's on switch is writable, and the companion reads only packs that are on"
```

---

### Task 2: The slice's names

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (constants after `WidgetCompanionRoam`; `WidgetUninstall`)
- Modify: `windows-dotnet/src/Sanduhr.Core/WidgetCatalogClient.cs` (`Offline`)
- Test: `windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs`

**Interfaces:**
- Produces (`public const string` unless shown): `ModsPageLine`, `ModsIntro`, `MetersSection`, `InstalledSection`, `CatalogSection`, `FolderHolds`, `MetersOffVsRemove`, `ReadOnlyClaudeCode`, `ShowsInThemes`, `ShowsInInk`, `ShowsInFonts`, `ShowsAsCompanion`, `EffectsUnused`, `CatalogDisclosure`, `FilterAll`, `FilterClaudeCode`, `FilterWidgets`, `FilterBoxName`, `PackOn`, `MetersLinkUp`, `FolderOn`, `FolderOff`, `FolderMissing`, `FolderNotInstalled`, `FolderUnreadable`, `ShowingSprites`, `ShowEveryPack`, `UninstallDetail`, `UninstallConfirm`, `UninstallCancel`, `GlowNeedsHook`, `AddAccountToLink`; `public static string PackOnFor(string pack)`, `MetersOnFor(string folder)`, `OnInFolders(int on, int of)`, `NoSwitchNote(int n)`, `NoFilesNote(int n)`, `UninstallQuestion(string pack)`, `ThemePacksLine(int n)`, `GlowHookIn(IReadOnlyList<string> folders)`. `WidgetUninstall` becomes `"Uninstall…"`. `WidgetCatalogClient.Offline` becomes WF40's sentence.
- Consumes: nothing. No page title, sidebar or table row changes here; they move with the pages (Tasks 8 and 12).

- [ ] **Step 1: Write the failing test**

Append to the class in `windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs`:

```csharp
    // ---- Settings v2, slice 4: Mods & Widgets and the Claude Code cards (spec, Mods & Widgets; Claude Code) ----

    [Fact]
    public void Mods_and_Widgets_copy_is_the_specs()
    {
        Assert.Equal("Everything installed for Sanduhr and for Claude Code, in one list. You can switch Sanduhr's mod and widget packs here; other Claude Code mods are shown read-only.",
            SettingsNames.ModsIntro);
        Assert.Equal("Off keeps the mod's entry and files; Remove takes the entry out and puts settings.json back exactly.",
            SettingsNames.MetersOffVsRemove);
        Assert.Equal("Read-only: Sanduhr can't switch this yet. Use claude plugin in a terminal.", SettingsNames.ReadOnlyClaudeCode);
        Assert.Equal("The catalog comes from GitHub. Opening it is the only time Sanduhr asks.", SettingsNames.CatalogDisclosure);
        Assert.Equal("Couldn't reach the widget catalog. Widget packs add themes, colors, fonts and a Desk companion. Check your connection and try again.",
            WidgetCatalogClient.Offline);
        Assert.Equal(("In Themes", "In Desk Look, Ink and color", "In Desk Look, Fonts", "As the Desk companion"),
            (SettingsNames.ShowsInThemes, SettingsNames.ShowsInInk, SettingsNames.ShowsInFonts, SettingsNames.ShowsAsCompanion));
        Assert.Equal("Not used yet: Sanduhr has nothing that draws effects packs.", SettingsNames.EffectsUnused);
        Assert.Equal(("All", "Claude Code", "Widgets"), (SettingsNames.FilterAll, SettingsNames.FilterClaudeCode, SettingsNames.FilterWidgets));
        Assert.Equal(("Meters above the prompt", "Installed", "Catalog", "What this folder holds"),
            (SettingsNames.MetersSection, SettingsNames.InstalledSection, SettingsNames.CatalogSection, SettingsNames.FolderHolds));
        Assert.Equal("A widget pack is " + SettingsNames.DefineWidgetPack + ". A mod is " + SettingsNames.DefineMod + ".",
            SettingsNames.ModsPageLine);
    }

    [Fact]
    public void Counts_and_scopes_read_in_plain_words()
    {
        Assert.Equal("On in 2 of 3 folders", SettingsNames.OnInFolders(2, 3));
        Assert.Equal("1 plugin has no on/off entry in settings.json, so Claude Code treats it as on.", SettingsNames.NoSwitchNote(1));
        Assert.Equal("3 plugins have no on/off entry in settings.json, so Claude Code treats them as on.", SettingsNames.NoSwitchNote(3));
        Assert.Equal("1 plugin is switched on in settings.json, but its files aren't in that folder.", SettingsNames.NoFilesNote(1));
        Assert.Equal("2 plugins are switched on in settings.json, but their files aren't in that folder.", SettingsNames.NoFilesNote(2));
        Assert.Equal("Theme packs: 3 installed.", SettingsNames.ThemePacksLine(3));
        Assert.Equal("Not installed in any folder yet.", SettingsNames.GlowHookIn([]));
        Assert.Equal(@"Installed in ~\.claude.", SettingsNames.GlowHookIn([@"~\.claude"]));
        Assert.Equal(@"Installed in ~\.claude, ~\.claude-personal and ~\.claude-work.",
            SettingsNames.GlowHookIn([@"~\.claude", @"~\.claude-personal", @"~\.claude-work"]));
    }

    [Fact]
    public void Switches_are_spoken_with_their_visible_word_first()
    {
        Assert.Equal("On", SettingsNames.PackOn);
        Assert.Equal("On: Fixture palette", SettingsNames.PackOnFor("Fixture palette"));
        Assert.Equal(@"On: Meters above the prompt in ~\.claude", SettingsNames.MetersOnFor(@"~\.claude"));
        Assert.Equal("Uninstall…", SettingsNames.WidgetUninstall);
        Assert.Equal("Uninstall Fixture palette?", SettingsNames.UninstallQuestion("Fixture palette"));
    }

    [Fact]
    public void Slice_4_copy_uses_no_retired_name_and_no_bad_path()
    {
        foreach (var text in new[]
        {
            SettingsNames.ModsIntro, SettingsNames.MetersOffVsRemove, SettingsNames.ReadOnlyClaudeCode, SettingsNames.CatalogDisclosure,
            SettingsNames.ShowsInThemes, SettingsNames.ShowsInInk, SettingsNames.ShowsInFonts, SettingsNames.ShowsAsCompanion,
            SettingsNames.EffectsUnused, SettingsNames.GlowNeedsHook, SettingsNames.AddAccountToLink, SettingsNames.MetersLinkUp,
            SettingsNames.UninstallDetail, SettingsNames.ModsPageLine, WidgetCatalogClient.Offline,
        })
        {
            Assert.Empty(SettingsNames.RetiredNames(text));
            Assert.Empty(SettingsNames.PathProblems(text));
        }
    }
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsNamesTests`
Expected: build FAILS with `'SettingsNames' does not contain a definition for 'ModsIntro'`.

- [ ] **Step 3: Add the names**

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, change `public const string WidgetUninstall = "Uninstall";` to `public const string WidgetUninstall = "Uninstall…";`, and after `WidgetCompanionRoam` add:

```csharp

    // ---- Settings v2, slice 4: Mods & Widgets (spec, Mods & Widgets: one list) ----

    /// <summary>Mods &amp; Widgets: the page's first line (WF21).</summary>
    public const string ModsPageLine = "A widget pack is " + DefineWidgetPack + ". A mod is " + DefineMod + ".";
    /// <summary>Mods &amp; Widgets: the intro (WF39). It no longer says "Sanduhr runs nothing here".</summary>
    public const string ModsIntro = "Everything installed for Sanduhr and for Claude Code, in one list. You can switch Sanduhr's mod and widget packs here; other Claude Code mods are shown read-only.";
    /// <summary>Mods &amp; Widgets' three sections, and the Claude Code card's collapsed report.</summary>
    public const string MetersSection = "Meters above the prompt";
    public const string InstalledSection = "Installed";
    public const string CatalogSection = "Catalog";
    public const string FolderHolds = "What this folder holds";
    /// <summary>Meters above the prompt: how Off differs from Remove (WF9).</summary>
    public const string MetersOffVsRemove = "Off keeps the mod's entry and files; Remove takes the entry out and puts settings.json back exactly.";
    /// <summary>A Claude Code plugin or mod Sanduhr can't switch (Principle 5, WF39, mods-parity R1 and R6).</summary>
    public const string ReadOnlyClaudeCode = "Read-only: Sanduhr can't switch this yet. Use claude plugin in a terminal.";
    /// <summary>Where an installed widget pack shows (WF3), by kind.</summary>
    public const string ShowsInThemes = "In Themes";
    public const string ShowsInInk = "In Desk Look, Ink and color";
    public const string ShowsInFonts = "In Desk Look, Fonts";
    public const string ShowsAsCompanion = "As the Desk companion";
    public const string EffectsUnused = "Not used yet: Sanduhr has nothing that draws effects packs.";
    /// <summary>Catalog: the only network call on the page (WF6).</summary>
    public const string CatalogDisclosure = "The catalog comes from GitHub. Opening it is the only time Sanduhr asks.";
    /// <summary>Installed: the filter chips and the text filter's spoken name.</summary>
    public const string FilterAll = "All";
    public const string FilterClaudeCode = "Claude Code";
    public const string FilterWidgets = "Widgets";
    public const string FilterBoxName = "Filter the installed list";
    /// <summary>A pack's or a folder's on switch: the visible word, then what it switches, for a screen reader.</summary>
    public const string PackOn = "On";
    public static string PackOnFor(string pack) => $"{PackOn}: {pack}";
    public static string MetersOnFor(string folder) => $"{PackOn}: {MetersSection} in {folder}";
    /// <summary>Installed: Sanduhr's meters mod row links up to its section.</summary>
    public const string MetersLinkUp = "Switch it per folder…";
    /// <summary>A scope over folders: "On in 2 of 3 folders". One folder reads its state alone.</summary>
    public static string OnInFolders(int on, int of) => $"On in {on} of {of} folders";
    public const string FolderOn = "On";
    public const string FolderOff = "Off";
    public const string FolderMissing = "Files missing";
    public const string FolderNotInstalled = "Not installed";
    public const string FolderUnreadable = "Can't be read";
    /// <summary>Installed: the reconcile note in plain words (spec, Installed), summed over the folders.</summary>
    public static string NoSwitchNote(int n) => n == 1
        ? "1 plugin has no on/off entry in settings.json, so Claude Code treats it as on."
        : $"{n} plugins have no on/off entry in settings.json, so Claude Code treats them as on.";
    public static string NoFilesNote(int n) => n == 1
        ? "1 plugin is switched on in settings.json, but its files aren't in that folder."
        : $"{n} plugins are switched on in settings.json, but their files aren't in that folder.";
    /// <summary>Catalog: the companion row's link shows sprites only until "Show every pack".</summary>
    public const string ShowingSprites = "Showing sprites only.";
    public const string ShowEveryPack = "Show every pack";
    /// <summary>Uninstall… asks first.</summary>
    public static string UninstallQuestion(string pack) => $"Uninstall {pack}?";
    public const string UninstallDetail = "Its files leave Sanduhr's widgets folder on this PC.";
    public const string UninstallConfirm = "Uninstall";
    public const string UninstallCancel = "Cancel";
    /// <summary>Themes: the theme packs line above the gallery (WF40).</summary>
    public static string ThemePacksLine(int n) => $"Theme packs: {n} installed.";

    // ---- Settings v2, slice 4: the Claude Code cards (spec, Claude Code) ----

    /// <summary>The Claude Code glow section: what it needs, and which folders have it (WF11).</summary>
    public const string GlowNeedsHook = "Needs the " + ClaudeCodeGlowHook + " in a folder above.";
    public static string GlowHookIn(IReadOnlyList<string> folders) => folders.Count switch
    {
        0 => "Not installed in any folder yet.",
        1 => $"Installed in {folders[0]}.",
        _ => $"Installed in {string.Join(", ", folders.Take(folders.Count - 1))} and {folders[^1]}.",
    };
    /// <summary>A folder card's Linked account when no account is signed in (WF32, carried here with the card).</summary>
    public const string AddAccountToLink = "Add an account to link this folder.";
```

In `windows-dotnet/src/Sanduhr.Core/WidgetCatalogClient.cs`, change the `Offline` line to:

```csharp
    /// <summary>Offline (WF40): it says what a pack is, since a newcomer often meets this line first.</summary>
    public const string Offline = "Couldn't reach the widget catalog. Widget packs add themes, colors, fonts and a Desk companion. Check your connection and try again.";
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~WidgetCatalog"`
Then: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~WidgetCatalogServiceTests|FullyQualifiedName~WidgetsPageNamesTests"`
Expected: PASS. The App tests read `WidgetCatalogClient.Offline` by name. `InstalledWidgetRowViewModel.UninstallName` builds "Uninstall <pack>" from the constant: change its body to `$"Uninstall {Name}"` in `ViewModels/WidgetsPageViewModel.cs` so the spoken name stays "Uninstall Fixture palette" (`WidgetsPageNamesTests` pins it).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.Core/WidgetCatalogClient.cs windows-dotnet/src/Sanduhr.App/ViewModels/WidgetsPageViewModel.cs windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs
git commit -m "feat(settings-v2): slice 4 copy for Mods & Widgets and the folder cards, in the names table"
```

---

### Task 3: `InstalledItems.Build`: every folder's mods, Sanduhr's mod, and the packs, in one list

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/InstalledItems.cs`
- Test: create `windows-dotnet/tests/Sanduhr.Tests/InstalledItemsTests.cs`

**Interfaces:**
- Consumes: `FolderReport`, `ModItem`, `ModState`, `ModOriginKind`, `SettingsKind` (`ModInventory.cs`), `OwnModState`, `IntegrationStatus` (`ClaudeCodeIntegrations.cs`), `WidgetRegistry`, `InstalledWidget`, `InstalledWidgetInfo` (`WidgetStore.cs`), `WidgetCatalogDocument`, `WidgetCatalog.CompareVersions`, Task 2's names.
- Produces:
  - `public enum InstalledKind { SanduhrIntegration, WidgetPack, ClaudeCodeMod, ClaudeCodePlugin }`
  - `public enum SwitchRoute { None, OwnModReceipt, PackEnabled }`
  - `public enum FolderState { On, Off, Missing, NotInstalled, Unreadable }`
  - `public sealed record FolderMods(string Folder, string Display, FolderReport Report, OwnModState? Own, IntegrationStatus Meters)`
  - `public sealed record FolderScope(string Folder, string Display, FolderState State)`
  - `public sealed record InstalledItem(string Id, string Name, InstalledKind Kind, string? PackKind, string Source, string? Version, string? Update, IReadOnlyList<FolderScope>? Scope, bool Enabled, SwitchRoute Route, string? ReadOnlyReason, bool MadeHere, bool IsMod, string Description)` with `bool CanSwitch` and `bool AppScoped`
  - `public static IReadOnlyList<InstalledItem> InstalledItems.Build(IReadOnlyList<FolderMods> folders, WidgetRegistry widgets, Func<string, InstalledWidgetInfo?> info, WidgetCatalogDocument? catalog)`
  - `public static FolderState InstalledItems.MetersState(FolderMods folder)`, `public static string InstalledItems.StateWord(FolderState state)`, `public static string InstalledItems.ScopeSummary(InstalledItem item)`, `public const string InstalledItems.SanduhrModId`

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/InstalledItemsTests.cs`:

```csharp
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// Settings v2, slice 4: the one list (spec, Mods &amp; Widgets; mods-parity §4). Built from data the App has already read, so these need no
/// disk: merge by id, the scope map, and the switch route per kind.
/// </summary>
public sealed class InstalledItemsTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static ModItem Plugin(string name, ModState state, string market = "m", bool isMod = false, string? version = "1.0.0") =>
        new(name, version, "", state == ModState.Missing ? null : @"C:\plugins\" + name, ModOriginKind.Installed, market,
            $"{name}@{market}", isMod, state, ModTouches.None, false);

    private static ModItem Meters(ModState state = ModState.On) =>
        new(ModInventory.SanduhrModName, "0.3.0", "", @"C:\x\sanduhr-meters", ModOriginKind.PluginDirs, null,
            ModInventory.SanduhrModName + "@inline", true, state, ModTouches.None, true);

    private static FolderReport Report(string folder, params ModItem[] mods) =>
        new(folder, true, SettingsState.Readable, null, [], new Dictionary<string, bool>(), mods,
            new McpSummary([], 0), new ClaudeMdInfo(false, 0, 0), new RetentionInfo(30, false, null));

    private static OwnModState NoOwn(string name) => new(@"C:\home\" + name, true, null, null, null, null, false);
    private static OwnModState OwnOn(string name) => new(@"C:\home\" + name, true, @"C:\x\sanduhr-meters", "20261025", null, null, true);
    private static OwnModState OwnOff(string name) => new(@"C:\home\" + name, true, @"C:\x\sanduhr-meters", "20261025", false, true, true);

    private static FolderMods Folder(string name, OwnModState? own = null, IntegrationStatus meters = IntegrationStatus.NotInstalled,
        params ModItem[] mods) =>
        new(@"C:\home\" + name, @"~\" + name, Report(@"C:\home\" + name, mods), own ?? NoOwn(name), meters);

    private static WidgetRegistry Packs(params InstalledWidget[] rows) => new(null, rows);

    private static InstalledWidget Pack(string id, bool on = true, WidgetSource source = WidgetSource.Catalog, string version = "1.0.0") =>
        new(id, version, source, At, on);

    private static InstalledWidgetInfo? Info(string id) => id switch
    {
        "good-palette" => new("Fixture palette", "palette", "", null),
        "good-theme" => new("Fixture theme", "theme", "", null),
        "good-font" => new("Fixture font", "font", "", null),
        "my-sprite" => new("My sprite", "sprite", "", null),
        "good-effects" => new("Fixture effects", "effects", "", null),
        _ => null,
    };

    [Fact]
    public void A_plugin_in_two_folders_is_one_row_with_every_folder_in_its_scope()
    {
        var items = InstalledItems.Build(
        [
            Folder(".claude", mods: Plugin("alpha", ModState.On)),
            Folder(".claude-work", mods: Plugin("alpha", ModState.Off)),
            Folder(".claude-personal"),
        ], WidgetRegistry.Empty, Info, null);

        var alpha = Assert.Single(items);
        Assert.Equal(("alpha@m", "alpha", InstalledKind.ClaudeCodePlugin), (alpha.Id, alpha.Name, alpha.Kind));
        Assert.Equal(
            [(@"~\.claude", FolderState.On), (@"~\.claude-work", FolderState.Off), (@"~\.claude-personal", FolderState.NotInstalled)],
            alpha.Scope!.Select(s => (s.Display, s.State)));
        Assert.Equal("On in 1 of 3 folders", InstalledItems.ScopeSummary(alpha));
    }

    [Fact]
    public void A_plugin_switched_on_without_files_reads_files_missing_there()
    {
        var items = InstalledItems.Build([Folder(".claude", mods: Plugin("ghost", ModState.Missing))], WidgetRegistry.Empty, Info, null);
        Assert.Equal(FolderState.Missing, Assert.Single(Assert.Single(items).Scope!).State);
        Assert.Equal("Files missing", InstalledItems.ScopeSummary(items[0]));
    }

    [Fact]
    public void Other_Claude_Code_items_are_read_only_and_say_why()
    {
        var items = InstalledItems.Build([Folder(".claude", mods: [Plugin("alpha", ModState.On), Plugin("beta-mod", ModState.Off, isMod: true)])],
            WidgetRegistry.Empty, Info, null);

        Assert.All(items, i =>
        {
            Assert.False(i.CanSwitch);
            Assert.Equal(SwitchRoute.None, i.Route);
            Assert.Equal(SettingsNames.ReadOnlyClaudeCode, i.ReadOnlyReason);
        });
        Assert.Equal(InstalledKind.ClaudeCodeMod, items.Single(i => i.Name == "beta-mod").Kind);
        Assert.True(items.Single(i => i.Name == "beta-mod").IsMod);
    }

    [Fact]
    public void Sanduhrs_meters_mod_is_one_switchable_row_scoped_by_its_own_state()
    {
        var items = InstalledItems.Build(
        [
            Folder(".claude", OwnOn(".claude"), IntegrationStatus.Installed, Meters()),
            Folder(".claude-work", OwnOff(".claude-work"), IntegrationStatus.Installed, Meters(ModState.Off)),
            Folder(".claude-personal"),
        ], WidgetRegistry.Empty, Info, null);

        var meters = Assert.Single(items);
        Assert.Equal((InstalledItems.SanduhrModId, InstalledKind.SanduhrIntegration, SwitchRoute.OwnModReceipt),
            (meters.Id, meters.Kind, meters.Route));
        Assert.True(meters.CanSwitch);
        Assert.Null(meters.ReadOnlyReason);
        Assert.Equal(SettingsNames.MetersSection, meters.Name);
        Assert.Equal([FolderState.On, FolderState.Off, FolderState.NotInstalled], meters.Scope!.Select(s => s.State));
        Assert.Equal("On in 1 of 3 folders", InstalledItems.ScopeSummary(meters));
    }

    [Fact]
    public void Sanduhrs_mod_is_not_listed_while_no_folder_has_it()
    {
        Assert.Empty(InstalledItems.Build([Folder(".claude"), Folder(".claude-work")], WidgetRegistry.Empty, Info, null));
    }

    [Fact]
    public void A_folder_whose_settings_json_cant_be_read_says_so_in_the_scope()
    {
        var unreadable = new FolderMods(@"C:\home\.claude", @"~\.claude",
            Report(@"C:\home\.claude", Meters()) with { Settings = new SettingsState(SettingsKind.Unreadable, 3, 7, "bad") },
            new OwnModState(@"C:\home\.claude", false, null, null, null, null, true), IntegrationStatus.Unreadable);

        Assert.Equal(FolderState.Unreadable, InstalledItems.MetersState(unreadable));
        var meters = Assert.Single(InstalledItems.Build(
            [unreadable, Folder(".claude-work", OwnOn(".claude-work"), IntegrationStatus.Installed, Meters())], WidgetRegistry.Empty, Info, null));
        Assert.Equal([FolderState.Unreadable, FolderState.On], meters.Scope!.Select(s => s.State));
        Assert.Equal("Can't be read", InstalledItems.StateWord(FolderState.Unreadable));
    }

    [Fact]
    public void Meters_whose_files_are_gone_read_files_missing()
    {
        Assert.Equal(FolderState.Missing,
            InstalledItems.MetersState(Folder(".claude", OwnOn(".claude"), IntegrationStatus.Outdated, Meters(ModState.Missing))));
    }

    [Fact]
    public void Widget_packs_are_app_scoped_and_switch_through_their_enabled_flag()
    {
        var items = InstalledItems.Build([Folder(".claude")],
            Packs(Pack("good-palette"), Pack("my-sprite", on: false, source: WidgetSource.Claude)), Info, null);

        var palette = items.Single(i => i.Id == "good-palette");
        Assert.Equal((InstalledKind.WidgetPack, "palette", SwitchRoute.PackEnabled, true, false),
            (palette.Kind, palette.PackKind, palette.Route, palette.Enabled, palette.MadeHere));
        Assert.True(palette.AppScoped);
        Assert.Equal("", InstalledItems.ScopeSummary(palette));

        var sprite = items.Single(i => i.Id == "my-sprite");
        Assert.Equal((false, true, SettingsNames.WidgetMadeHere), (sprite.Enabled, sprite.MadeHere, sprite.Source));
        Assert.True(sprite.CanSwitch);
    }

    [Theory]
    [InlineData("good-palette", "palette")]
    [InlineData("good-theme", "theme")]
    [InlineData("good-font", "font")]
    [InlineData("my-sprite", "sprite")]
    [InlineData("good-effects", "effects")]
    public void Every_pack_kind_switches_through_enabled(string id, string kind)
    {
        var item = Assert.Single(InstalledItems.Build([], Packs(Pack(id)), Info, null));
        Assert.Equal((kind, SwitchRoute.PackEnabled), (item.PackKind, item.Route));
    }

    [Fact]
    public void A_pack_whose_pack_json_cant_be_read_is_listed_by_id_and_can_still_switch()
    {
        var item = Assert.Single(InstalledItems.Build([], Packs(Pack("broken-pack")), Info, null));
        Assert.Equal(("broken-pack", "", true), (item.Name, item.PackKind, item.CanSwitch));
    }

    [Fact]
    public void A_newer_catalog_version_is_merged_onto_the_installed_row_by_id()
    {
        var catalog = new WidgetCatalogDocument(At, "2026-10-25T12:00:00Z",
        [
            new WidgetCatalogEntry("good-palette", "Fixture palette", "palette", "1.1.0", "3.8.0", "https://x/p.zip", new string('a', 64), 10),
            new WidgetCatalogEntry("good-theme", "Fixture theme", "theme", "1.0.0", "3.8.0", "https://x/t.zip", new string('b', 64), 10),
            new WidgetCatalogEntry("my-sprite", "Not mine", "sprite", "9.0.0", "3.8.0", "https://x/s.zip", new string('c', 64), 10),
        ]);
        var items = InstalledItems.Build([], Packs(Pack("good-palette"), Pack("good-theme"), Pack("my-sprite", source: WidgetSource.Claude)),
            Info, catalog);

        Assert.Equal("1.1.0", items.Single(i => i.Id == "good-palette").Update);
        Assert.Null(items.Single(i => i.Id == "good-theme").Update);
        Assert.Null(items.Single(i => i.Id == "my-sprite").Update);
        Assert.Equal(3, items.Count);   // the catalog adds no row to the installed list
    }

    [Fact]
    public void The_list_reads_Sanduhrs_mod_then_packs_then_Claude_Code_each_by_name()
    {
        var items = InstalledItems.Build(
            [Folder(".claude", OwnOn(".claude"), IntegrationStatus.Installed, Meters(), Plugin("zeta", ModState.On), Plugin("alpha", ModState.On))],
            Packs(Pack("good-theme"), Pack("good-palette")), Info, null);
        Assert.Equal([SettingsNames.MetersSection, "Fixture palette", "Fixture theme", "alpha", "zeta"], items.Select(i => i.Name));
    }

    [Fact]
    public void With_one_folder_a_scope_reads_its_state_alone()
    {
        var items = InstalledItems.Build([Folder(".claude", mods: Plugin("alpha", ModState.Off))], WidgetRegistry.Empty, Info, null);
        Assert.Equal("Off", InstalledItems.ScopeSummary(Assert.Single(items)));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~InstalledItemsTests`
Expected: build FAILS with `The type or namespace name 'InstalledItems' could not be found`.

- [ ] **Step 3: Write `InstalledItems`**

Create `windows-dotnet/src/Sanduhr.Core/InstalledItems.cs`:

```csharp
namespace Sanduhr.Core;

/// <summary>What an installed item is (spec, Mods &amp; Widgets; mods-parity §4).</summary>
public enum InstalledKind { SanduhrIntegration, WidgetPack, ClaudeCodeMod, ClaudeCodePlugin }

/// <summary>How Sanduhr can switch an item: its own meters mod through the receipt (per folder), a widget pack through widgets.json's
/// <c>enabled</c>, or not at all (read-only in 4.0).</summary>
public enum SwitchRoute { None, OwnModReceipt, PackEnabled }

/// <summary>An item's state in one Claude Code folder. <see cref="Unreadable"/> is the folder's settings.json failing to parse, which
/// Sanduhr never writes through (slice 4, decision 8).</summary>
public enum FolderState { On, Off, Missing, NotInstalled, Unreadable }

/// <summary>One Claude Code folder as the App read it: its report, Sanduhr's mod state there and the meters entry's status.</summary>
public sealed record FolderMods(string Folder, string Display, FolderReport Report, OwnModState? Own, IntegrationStatus Meters);

/// <summary>An item's state in one folder, with the folder as Settings shows it.</summary>
public sealed record FolderScope(string Folder, string Display, FolderState State);

/// <summary>
/// One row of the Installed list. <paramref name="Scope"/> is null for an app-wide widget pack and one entry per known folder for a Claude
/// Code item. <paramref name="Update"/> is the catalog's newer version of an installed catalog pack.
/// </summary>
public sealed record InstalledItem(
    string Id,
    string Name,
    InstalledKind Kind,
    string? PackKind,
    string Source,
    string? Version,
    string? Update,
    IReadOnlyList<FolderScope>? Scope,
    bool Enabled,
    SwitchRoute Route,
    string? ReadOnlyReason,
    bool MadeHere,
    bool IsMod,
    string Description)
{
    public bool CanSwitch => Route != SwitchRoute.None;
    public bool AppScoped => Scope is null;
}

/// <summary>
/// Settings v2, Mods &amp; Widgets: everything installed for Sanduhr and for Claude Code in one list, built from what the App has already
/// read (<see cref="ModInventory.Read"/> per folder, <see cref="ClaudeCodeIntegrations.OwnModStateOf"/> and the meters status, the widget
/// registry, the catalog). Pure: no disk, no network. App-side only: not linked into the MCP server, whose link list is fixed
/// (TrustBoundaryTests).
/// </summary>
public static partial class InstalledItems
{
    public const string SanduhrModId = ModInventory.SanduhrModName;

    /// <summary>The list, in order: Sanduhr's meters mod (while any folder has it), the widget packs by name, then every other Claude Code
    /// plugin and mod by name, one row per id across the folders.</summary>
    public static IReadOnlyList<InstalledItem> Build(IReadOnlyList<FolderMods> folders, WidgetRegistry widgets,
        Func<string, InstalledWidgetInfo?> info, WidgetCatalogDocument? catalog)
    {
        var items = new List<InstalledItem>();

        var meters = folders.Select(f => new FolderScope(f.Folder, f.Display, MetersState(f))).ToList();
        if (meters.Any(s => s.State != FolderState.NotInstalled))
        {
            var version = folders.SelectMany(f => f.Report.Mods).FirstOrDefault(m => m.IsSanduhr && m.Version is not null)?.Version;
            items.Add(new InstalledItem(SanduhrModId, SettingsNames.MetersSection, InstalledKind.SanduhrIntegration, null, "Sanduhr",
                version, null, meters, meters.Any(s => s.State == FolderState.On), SwitchRoute.OwnModReceipt, null, false, true, ""));
        }

        var packs = new List<InstalledItem>();
        foreach (var row in widgets.Packs)
        {
            var i = info(row.Id);
            string? update = null;
            if (!row.MadeHere && catalog?.Packs.FirstOrDefault(e => e.Id == row.Id) is { } entry
                && WidgetCatalog.CompareVersions(entry.Version, row.Version) > 0)
                update = entry.Version;
            packs.Add(new InstalledItem(row.Id, i?.Name ?? row.Id, InstalledKind.WidgetPack, i?.Kind ?? "",
                row.MadeHere ? SettingsNames.WidgetMadeHere : "from the catalog", row.Version, update, null, row.Enabled,
                SwitchRoute.PackEnabled, null, row.MadeHere, false, i?.Description ?? ""));
        }
        items.AddRange(packs.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Id, StringComparer.Ordinal));

        var byId = new Dictionary<string, List<(string Folder, ModItem Item)>>(StringComparer.Ordinal);
        foreach (var f in folders)
            foreach (var m in f.Report.Mods.Where(m => !m.IsSanduhr))
            {
                var id = m.Key ?? m.Name;
                if (!byId.TryGetValue(id, out var seen)) byId[id] = seen = [];
                seen.Add((f.Folder, m));
            }
        var claude = new List<InstalledItem>();
        foreach (var (id, seen) in byId)
        {
            var first = seen[0].Item;
            var scope = folders.Select(f =>
            {
                var here = seen.FirstOrDefault(s => string.Equals(s.Folder, f.Folder, StringComparison.OrdinalIgnoreCase)).Item;
                var state = here is null ? FolderState.NotInstalled : here.State switch
                {
                    ModState.On => FolderState.On,
                    ModState.Off => FolderState.Off,
                    _ => FolderState.Missing,
                };
                return new FolderScope(f.Folder, f.Display, state);
            }).ToList();
            bool isMod = seen.Any(s => s.Item.IsMod);
            string source = first.Marketplace ?? (first.Origin == ModOriginKind.SkillsDir ? "skills folder" : "plugin folder");
            claude.Add(new InstalledItem(id, first.Name, isMod ? InstalledKind.ClaudeCodeMod : InstalledKind.ClaudeCodePlugin, null,
                source, seen.Select(s => s.Item.Version).FirstOrDefault(v => v is not null), null, scope,
                scope.Any(s => s.State == FolderState.On), SwitchRoute.None, SettingsNames.ReadOnlyClaudeCode, false, isMod,
                seen.Select(s => s.Item.Description).FirstOrDefault(d => d.Length > 0) ?? ""));
        }
        items.AddRange(claude.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Id, StringComparer.Ordinal));
        return items;
    }

    /// <summary>Where Sanduhr's meters mod stands in one folder: an unreadable settings.json first, then files gone, then on, off or absent.</summary>
    public static FolderState MetersState(FolderMods folder)
    {
        if (folder.Report.Settings.Kind == SettingsKind.Unreadable || folder.Own is { Readable: false }
            || folder.Meters == IntegrationStatus.Unreadable)
            return FolderState.Unreadable;
        if (folder.Report.Mods.Any(m => m.IsSanduhr && m.State == ModState.Missing))
            return FolderState.Missing;
        if (folder.Own is not { Listed: true } own)
            return FolderState.NotInstalled;
        return own.IsOn ? FolderState.On : FolderState.Off;
    }

    /// <summary>A state in words, as a scope with one folder reads it.</summary>
    public static string StateWord(FolderState state) => state switch
    {
        FolderState.On => SettingsNames.FolderOn,
        FolderState.Off => SettingsNames.FolderOff,
        FolderState.Missing => SettingsNames.FolderMissing,
        FolderState.Unreadable => SettingsNames.FolderUnreadable,
        _ => SettingsNames.FolderNotInstalled,
    };

    /// <summary>"On in 2 of 3 folders"; one folder reads its state alone; an app-wide pack has none.</summary>
    public static string ScopeSummary(InstalledItem item) => item.Scope switch
    {
        null or { Count: 0 } => "",
        { Count: 1 } one => StateWord(one[0].State),
        var many => SettingsNames.OnInFolders(many.Count(s => s.State == FolderState.On), many.Count),
    };
}
```

(`partial`: Task 4 adds the catalog rows, "Where it shows" and the filters to the same file.)

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~InstalledItemsTests`
Expected: PASS. Then `dotnet test tests/Sanduhr.Mcp.Tests --filter FullyQualifiedName~TrustBoundaryTests`: PASS (the file is not linked).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/InstalledItems.cs windows-dotnet/tests/Sanduhr.Tests/InstalledItemsTests.cs
git commit -m "feat(mods): InstalledItems, one list of every folder's mods, Sanduhr's mod and the widget packs, with scope and switch route"
```

---

### Task 4: The catalog merged by id, "Where it shows", the filters and the plain note

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/InstalledItems.cs` (append)
- Modify: `windows-dotnet/src/Sanduhr.Core/ModsPageText.cs` (`Reconcile` becomes `PlainNote`)
- Modify: `windows-dotnet/src/Sanduhr.Core/ModsFolderChoice.cs` (summary only)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/ModsConfigViewModel.cs` (its one `Reconcile` call)
- Test: `windows-dotnet/tests/Sanduhr.Tests/InstalledItemsTests.cs`, `ModsPageTextTests.cs`, `ModsFolderChoiceTests.cs`; `windows-dotnet/tests/Sanduhr.App.Tests/ModsConfigPluginsTests.cs`

**Interfaces:**
- Consumes: Task 3's types; `WidgetCatalog.MeetsMinimum`, `SettingsNames.LinkTitle`, `SettingsIndex.Gallery`, `DeskInk`, `DeskLook`, `DeskPieces`, `SettingsIndex.PageId`.
- Produces:
  - `public enum CatalogAction { Install, Update, Switch, None }`
  - `public sealed record CatalogRow(WidgetCatalogEntry Entry, CatalogAction Action, string State, InstalledItem? Installed)`
  - `public static IReadOnlyList<CatalogRow> InstalledItems.CatalogRows(WidgetCatalogDocument catalog, IReadOnlyList<InstalledItem> items, Version running, string? kind = null)`
  - `public sealed record WhereItShows(string Text, string? Link, string? LinkTitle)`; `public static WhereItShows InstalledItems.Where(string? packKind)`
  - `public enum InstalledFilter { All, ClaudeCode, Widgets }`; `public static bool InstalledItems.Matches(InstalledItem item, InstalledFilter filter, string? text)`
  - `public static string ModsPageText.PlainNote(IReadOnlyList<FolderMods> folders)`. `ModsPageText.Reconcile` is removed.

- [ ] **Step 1: Write the failing tests**

Append to `InstalledItemsTests`:

```csharp
    // ---- the catalog merged by id, where it shows, the filters ----

    private static readonly Version Running = new(4, 0, 0, 0);

    private static WidgetCatalogDocument Catalog(params WidgetCatalogEntry[] entries) => new(At, "2026-10-25T12:00:00Z", entries);

    private static WidgetCatalogEntry Entry(string id, string kind, string version = "1.0.0", string min = "3.8.0") =>
        new(id, "Name of " + id, kind, version, min, $"https://x/{id}.zip", new string('a', 64), 10);

    [Fact]
    public void A_catalog_pack_appears_once_with_install_update_or_the_switch()
    {
        var catalog = Catalog(Entry("good-palette", "palette", "1.1.0"), Entry("good-theme", "theme"), Entry("good-font", "font"));
        var items = InstalledItems.Build([], Packs(Pack("good-palette"), Pack("good-theme", on: false)), Info, catalog);

        var rows = InstalledItems.CatalogRows(catalog, items, Running);

        Assert.Equal(["good-palette", "good-theme", "good-font"], rows.Select(r => r.Entry.Id));
        Assert.Equal((CatalogAction.Update, "Version 1.0.0 is installed."), (rows[0].Action, rows[0].State));
        Assert.Equal((CatalogAction.Switch, "Installed."), (rows[1].Action, rows[1].State));
        Assert.False(rows[1].Installed!.Enabled);
        Assert.Equal((CatalogAction.Install, ""), (rows[2].Action, rows[2].State));
        Assert.Null(rows[2].Installed);
    }

    [Fact]
    public void A_pack_above_the_running_version_offers_nothing_but_says_why()
    {
        var rows = InstalledItems.CatalogRows(Catalog(Entry("good-font", "font", min: "9.0.0")), [], Running);
        Assert.Equal((CatalogAction.None, "Needs Sanduhr 9.0.0 or later."), (rows[0].Action, rows[0].State));
    }

    [Fact]
    public void An_update_above_the_running_version_keeps_the_switch_and_says_why()
    {
        var items = InstalledItems.Build([], Packs(Pack("good-palette")), Info, null);
        var rows = InstalledItems.CatalogRows(Catalog(Entry("good-palette", "palette", "2.0.0", min: "9.0.0")), items, Running);
        Assert.Equal((CatalogAction.Switch, "Version 1.0.0 is installed. 2.0.0 needs Sanduhr 9.0.0 or later."), (rows[0].Action, rows[0].State));
    }

    [Fact]
    public void A_catalog_id_that_a_pack_made_here_holds_offers_nothing()
    {
        var items = InstalledItems.Build([], Packs(Pack("my-sprite", source: WidgetSource.Claude)), Info, null);
        var rows = InstalledItems.CatalogRows(Catalog(Entry("my-sprite", "sprite")), items, Running);
        Assert.Equal((CatalogAction.None, $"A widget {SettingsNames.WidgetMadeHere} already uses this id."), (rows[0].Action, rows[0].State));
    }

    [Fact]
    public void The_catalog_can_show_one_kind()
    {
        var rows = InstalledItems.CatalogRows(Catalog(Entry("good-sprite", "sprite"), Entry("good-font", "font")), [], Running, kind: "sprite");
        Assert.Equal(["good-sprite"], rows.Select(r => r.Entry.Id));
    }

    [Theory]
    [InlineData("theme", "In Themes", "themes#gallery", "Themes settings…")]
    [InlineData("palette", "In Desk Look, Ink and color", "desklook#ink", "Desk Look settings…")]
    [InlineData("font", "In Desk Look, Fonts", "desklook#look", "Desk Look settings…")]
    [InlineData("sprite", "As the Desk companion", "desk#pieces", "Desk settings…")]
    public void Each_kind_says_where_it_shows_and_links_there(string kind, string text, string link, string title)
    {
        var where = InstalledItems.Where(kind);
        Assert.Equal((text, link, title), (where.Text, where.Link, where.LinkTitle));
        var place = SettingsIndex.ParseLink(where.Link)!;
        Assert.NotNull(place.Anchor);   // the link lands on its section, not a page top
    }

    [Fact]
    public void Effects_say_they_are_unused_and_link_nowhere()
    {
        Assert.Equal(new WhereItShows(SettingsNames.EffectsUnused, null, null), InstalledItems.Where("effects"));
        Assert.Equal(new WhereItShows("", null, null), InstalledItems.Where(null));
    }

    [Fact]
    public void The_filters_split_Claude_Code_from_widgets_and_the_text_matches_name_kind_or_source()
    {
        var items = InstalledItems.Build(
            [Folder(".claude", OwnOn(".claude"), IntegrationStatus.Installed, Meters(), Plugin("alpha", ModState.On))],
            Packs(Pack("good-palette"), Pack("my-sprite", source: WidgetSource.Claude)), Info, null);

        string[] Names(InstalledFilter f, string? text = null) =>
            [.. items.Where(i => InstalledItems.Matches(i, f, text)).Select(i => i.Name)];

        Assert.Equal(4, Names(InstalledFilter.All).Length);
        Assert.Equal([SettingsNames.MetersSection, "alpha"], Names(InstalledFilter.ClaudeCode));
        Assert.Equal(["Fixture palette", "My sprite"], Names(InstalledFilter.Widgets));
        Assert.Equal(["My sprite"], Names(InstalledFilter.All, "SPRITE"));
        Assert.Equal(["My sprite"], Names(InstalledFilter.All, "made here"));
        Assert.Equal(["alpha"], Names(InstalledFilter.ClaudeCode, "alp"));
        Assert.Empty(Names(InstalledFilter.Widgets, "alpha"));
        Assert.Equal(4, Names(InstalledFilter.All, "   ").Length);
    }
```

In `windows-dotnet/tests/Sanduhr.Tests/ModsPageTextTests.cs`, delete every `ModsPageText.Reconcile(...)` assert from `TheTitleCountsWhatIsInstalledAndWhatLoads`, `ExplainsWhyTheListCountsMoreThanTheSwitches` and `ARowSwitchedOnWithoutFilesIsListedButNotInstalledOrOn` (their `Tally` and `ListTitle` asserts stay), then append:

```csharp
    [Fact]
    public void The_note_counts_plugins_without_a_switch_and_without_files_over_every_folder_in_plain_words()
    {
        static ModItem Item(string name, ModState state, string? path, string? key) =>
            new(name, null, "", path, ModOriginKind.Installed, "m", key, false, state, ModTouches.None, false);
        static FolderMods Folder(string name, IReadOnlyDictionary<string, bool> switches, params ModItem[] mods) =>
            new(name, name, new FolderReport(name, true, SettingsState.Readable, null, [], switches, mods,
                new McpSummary([], 0), new ClaudeMdInfo(false, 0, 0), new RetentionInfo(30, false, null)), null, IntegrationStatus.NotInstalled);

        var a = Folder("a", new Dictionary<string, bool> { ["x@m"] = true, ["ghost@m"] = true },
            Item("x", ModState.On, @"C:\x", "x@m"), Item("inline", ModState.On, @"C:\i", null), Item("ghost", ModState.On, null, "ghost@m"));
        var b = Folder("b", new Dictionary<string, bool>(), Item("skills", ModState.On, @"C:\s", "skills@skills-dir"));

        Assert.Equal(SettingsNames.NoSwitchNote(2) + " " + SettingsNames.NoFilesNote(1), ModsPageText.PlainNote([a, b]));
        Assert.Equal("", ModsPageText.PlainNote([Folder("c", new Dictionary<string, bool> { ["x@m"] = true }, Item("x", ModState.On, @"C:\x", "x@m"))]));
    }
```

In `windows-dotnet/tests/Sanduhr.Tests/ModsFolderChoiceTests.cs`, change the class summary to `Which folder card opens "What this folder holds" when a link lands on Claude Code, folder-config (Settings v2 slice 4; before, it chose the Mods &amp; Config combo's folder). The rule is unchanged.` The facts stay as they are.

In `windows-dotnet/tests/Sanduhr.App.Tests/ModsConfigPluginsTests.cs`, `TheSwitchSummaryCountsSettingsJsonAndTheNoteExplainsTheDifference`: replace its three `PluginListNote` asserts with `Assert.Equal(SettingsNames.NoSwitchNote(1) + " " + SettingsNames.NoFilesNote(1), page.PluginListNote);` and add `using Sanduhr.Core;` to the file.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~InstalledItemsTests|FullyQualifiedName~ModsPageTextTests"`
Expected: build FAILS with `'InstalledItems' does not contain a definition for 'CatalogRows'` and `'ModsPageText' does not contain a definition for 'PlainNote'`.

- [ ] **Step 3: Implement**

Append to `windows-dotnet/src/Sanduhr.Core/InstalledItems.cs`:

```csharp

/// <summary>What a catalog row's one control does (spec, Catalog: "Install, Update, or the switch").</summary>
public enum CatalogAction { Install, Update, Switch, None }

/// <summary>A catalog pack merged with its installed row by id, and the line under it.</summary>
public sealed record CatalogRow(WidgetCatalogEntry Entry, CatalogAction Action, string State, InstalledItem? Installed);

/// <summary>Where an installed pack shows (WF3), and the link that opens that place.</summary>
public sealed record WhereItShows(string Text, string? Link, string? LinkTitle);

/// <summary>The Installed list's filter chips.</summary>
public enum InstalledFilter { All, ClaudeCode, Widgets }

public static partial class InstalledItems
{
    /// <summary>
    /// The catalog's packs in catalog order, each once, with its control: Install when not installed, Update when the catalog is newer and
    /// this Sanduhr meets its minimum, the switch otherwise. An id held by a pack made here offers nothing. <paramref name="kind"/> shows one
    /// pack kind (the companion row's link shows sprites).
    /// </summary>
    public static IReadOnlyList<CatalogRow> CatalogRows(WidgetCatalogDocument catalog, IReadOnlyList<InstalledItem> items, Version running,
        string? kind = null)
    {
        var rows = new List<CatalogRow>();
        foreach (var entry in catalog.Packs)
        {
            if (kind is not null && entry.Kind != kind) continue;
            var installed = items.FirstOrDefault(i => i.Kind == InstalledKind.WidgetPack && i.Id == entry.Id);
            bool meets = WidgetCatalog.MeetsMinimum(entry.SanduhrMin, running);
            rows.Add(installed switch
            {
                { MadeHere: true } => new(entry, CatalogAction.None, $"A widget {SettingsNames.WidgetMadeHere} already uses this id.", installed),
                null => meets
                    ? new(entry, CatalogAction.Install, "", null)
                    : new(entry, CatalogAction.None, $"Needs Sanduhr {entry.SanduhrMin} or later.", null),
                _ when WidgetCatalog.CompareVersions(entry.Version, installed.Version) > 0 => meets
                    ? new(entry, CatalogAction.Update, $"Version {installed.Version} is installed.", installed)
                    : new(entry, CatalogAction.Switch,
                        $"Version {installed.Version} is installed. {entry.Version} needs Sanduhr {entry.SanduhrMin} or later.", installed),
                _ => new(entry, CatalogAction.Switch, "Installed.", installed),
            });
        }
        return rows;
    }

    /// <summary>Where a pack of <paramref name="packKind"/> shows, with "&lt;Page&gt; settings…" to that section. Effects have no consumer yet.</summary>
    public static WhereItShows Where(string? packKind) => packKind switch
    {
        "theme" => new(SettingsNames.ShowsInThemes, Link(SettingsPage.Themes, SettingsIndex.Gallery), SettingsNames.LinkTitle(SettingsPage.Themes)),
        "palette" => new(SettingsNames.ShowsInInk, Link(SettingsPage.DeskLook, SettingsIndex.DeskInk), SettingsNames.LinkTitle(SettingsPage.DeskLook)),
        "font" => new(SettingsNames.ShowsInFonts, Link(SettingsPage.DeskLook, SettingsIndex.DeskLook), SettingsNames.LinkTitle(SettingsPage.DeskLook)),
        "sprite" => new(SettingsNames.ShowsAsCompanion, Link(SettingsPage.Desk, SettingsIndex.DeskPieces), SettingsNames.LinkTitle(SettingsPage.Desk)),
        "effects" => new(SettingsNames.EffectsUnused, null, null),
        _ => new("", null, null),
    };

    private static string Link(SettingsPage page, string anchor) => SettingsIndex.PageId(page) + "#" + anchor;

    /// <summary>The chips (All, Claude Code, Widgets) and the text filter, any case, over the name, id, pack kind, source and mod tag.</summary>
    public static bool Matches(InstalledItem item, InstalledFilter filter, string? text)
    {
        bool kind = filter switch
        {
            InstalledFilter.ClaudeCode => item.Kind != InstalledKind.WidgetPack,
            InstalledFilter.Widgets => item.Kind == InstalledKind.WidgetPack,
            _ => true,
        };
        if (!kind) return false;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var t = text.Trim();
        return new[] { item.Name, item.Id, item.PackKind ?? "", item.Source, item.IsMod ? "mod" : "" }
            .Any(s => s.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
}
```

In `windows-dotnet/src/Sanduhr.Core/ModsPageText.cs`, replace the whole `Reconcile` method and its summary with:

```csharp
    /// <summary>
    /// The note under the Installed list, summed over the folders, in the spec's plain words: how many plugins have no on/off entry in
    /// settings.json (Claude Code treats them as on), then how many are switched on with no files. "" when both are 0.
    /// </summary>
    public static string PlainNote(IReadOnlyList<FolderMods> folders)
    {
        int noSwitch = 0, noFiles = 0;
        foreach (var f in folders)
        {
            var t = Tally([.. f.Report.Mods.Where(m => !m.IsSanduhr)], f.Report.EnabledPlugins);
            noSwitch += t.NoSwitch;
            noFiles += t.NoFiles;
        }
        var parts = new List<string>();
        if (noSwitch > 0) parts.Add(SettingsNames.NoSwitchNote(noSwitch));
        if (noFiles > 0) parts.Add(SettingsNames.NoFilesNote(noFiles));
        return string.Join(" ", parts);
    }
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/ModsConfigViewModel.cs` (`Refresh`, the plugins block), replace

```csharp
        PluginListNote = ModsPageText.Reconcile(tally, pluginsOn, pluginsOff);
```

with

```csharp
        PluginListNote = ModsPageText.PlainNote([new FolderMods(folder.Path, folder.Display, report, own, IntegrationStatus.NotInstalled)]);
```

In `windows-dotnet/src/Sanduhr.Core/ModsFolderChoice.cs`, change the class summary's first sentence to `Which Claude Code folder card opens "What this folder holds" when a link lands on Claude Code, folder-config (Settings v2 slice 4).` and keep the rest.

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~InstalledItemsTests|FullyQualifiedName~ModsPageTextTests|FullyQualifiedName~ModsFolderChoiceTests"`
Then: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~ModsConfigPluginsTests`
Expected: PASS. (In `ModsConfigPluginsTests`'s fixture the inline `meters` plugin is not Sanduhr's, so it counts as one without a switch, and `gamma` as one without files.)

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/InstalledItems.cs windows-dotnet/src/Sanduhr.Core/ModsPageText.cs windows-dotnet/src/Sanduhr.Core/ModsFolderChoice.cs windows-dotnet/src/Sanduhr.App/ViewModels/ModsConfigViewModel.cs windows-dotnet/tests/Sanduhr.Tests/InstalledItemsTests.cs windows-dotnet/tests/Sanduhr.Tests/ModsPageTextTests.cs windows-dotnet/tests/Sanduhr.Tests/ModsFolderChoiceTests.cs windows-dotnet/tests/Sanduhr.App.Tests/ModsConfigPluginsTests.cs
git commit -m "feat(mods): catalog rows merged by id, where each pack shows, the list's filters, and the reconcile note in plain words"
```

---

### Task 5: Shared converters, and the Widgets page renamed for the page it becomes

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/Converters/BoolConverters.cs`
- Rename (`git mv`) and edit: `Views/WidgetsPage.xaml` → `Views/ModsPage.xaml`, `Views/WidgetsPage.xaml.cs` → `Views/ModsPage.xaml.cs`, `ViewModels/WidgetsPageViewModel.cs` → `ViewModels/ModsPageViewModel.cs` (all under `windows-dotnet/src/Sanduhr.App/`)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/DeskPiecesPanel.xaml` (line 43), `ViewModels/SettingsViewModel.cs` (`WidgetsPage`), `Views/SettingsWindow.xaml` (the Widgets TabItem), `App.xaml.cs` (`svm.WidgetsPage`, `ViewModel.WidgetsPage`)
- Modify (harness): `windows-dotnet/tools/Sanduhr.RenderHarness/CompanionScenario.cs:155`, `ProposeWidgetScenario.cs:159,172`
- Rename (`git mv`) and edit tests: `tests/Sanduhr.App.Tests/WidgetsPageNamesTests.cs` → `ModsPageNamesTests.cs`; modify `WidgetCatalogServiceTests.cs`, `WidgetReviewServiceTests.cs`, `CompanionPieceTests.cs:355`, `tests/Sanduhr.Tests/SettingsSourceScanTests.cs`

**Interfaces:**
- Produces: `Sanduhr.App.Converters.Bools.Not` and `Bools.NotVisible` (`IValueConverter`, true to false, true to Collapsed). Types renamed, members unchanged: `ModsPage` (view), `ModsPageViewModel` (was `WidgetsPageViewModel`), `SettingsViewModel.ModsPage` (was `WidgetsPage`, still `internal set`). `WidgetCatalogRowViewModel` and `InstalledWidgetRowViewModel` keep their names until Tasks 9 and 11 replace them.
- Consumes: nothing new. No behavior changes in this task: it is the carried T9 fix (the Desk's companion row borrowed `views:WidgetsPage.NotVisible`) and a rename, so the later tasks edit files whose names match the page.

- [ ] **Step 1: Write the failing scan**

Append to `windows-dotnet/tests/Sanduhr.Tests/SettingsSourceScanTests.cs`:

```csharp
    [Fact]
    public void No_view_borrows_another_pages_statics()
    {
        // Slice 3 final review T9: the companion row used views:WidgetsPage.NotVisible, which breaks the Desk page when that page goes.
        // Shared converters live in Converters/; a view references its own statics or those.
        var bad = Files("*.xaml")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\{x:Static views:(\w+Page)\.\w+\}").Select(m => (File: f, Page: m.Groups[1].Value)))
            .Where(x => !Path.GetFileNameWithoutExtension(x.File).Equals(x.Page, StringComparison.Ordinal))
            .Select(x => $"{Path.GetFileName(x.File)} uses {x.Page}'s statics")
            .ToList();
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
        Assert.False(File.Exists(Path.Combine(AppDir(), "Views", "WidgetsPage.xaml")), "the Widgets view is ModsPage.xaml since slice 4");
    }
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsSourceScanTests.No_view_borrows_another_pages_statics`
Expected: FAIL with `DeskPiecesPanel.xaml uses WidgetsPage's statics`.

- [ ] **Step 3: Move the converters**

Create `windows-dotnet/src/Sanduhr.App/Converters/BoolConverters.cs`:

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Sanduhr.App.Converters;

/// <summary>Two bool converters the Settings pages and panels share (they lived on the Widgets page until slice 4).</summary>
public static class Bools
{
    /// <summary>true to false: IsEnabled while a page is busy.</summary>
    public static readonly IValueConverter Not = new NotConverter();

    /// <summary>true to Collapsed, false to Visible: an empty-list line.</summary>
    public static readonly IValueConverter NotVisible = new NotVisibleConverter();

    private sealed class NotConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    }

    private sealed class NotVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is true ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
```

In `windows-dotnet/src/Sanduhr.App/Views/DeskPiecesPanel.xaml`, add `xmlns:conv="clr-namespace:Sanduhr.App.Converters"` to the root element and change line 43's converter to `{x:Static conv:Bools.NotVisible}`.

- [ ] **Step 4: Rename the page and its view model**

```bash
git mv windows-dotnet/src/Sanduhr.App/Views/WidgetsPage.xaml windows-dotnet/src/Sanduhr.App/Views/ModsPage.xaml
git mv windows-dotnet/src/Sanduhr.App/Views/WidgetsPage.xaml.cs windows-dotnet/src/Sanduhr.App/Views/ModsPage.xaml.cs
git mv windows-dotnet/src/Sanduhr.App/ViewModels/WidgetsPageViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/ModsPageViewModel.cs
git mv windows-dotnet/tests/Sanduhr.App.Tests/WidgetsPageNamesTests.cs windows-dotnet/tests/Sanduhr.App.Tests/ModsPageNamesTests.cs
```

Then, with the Edit tool:

- `Views/ModsPage.xaml`: `x:Class="Sanduhr.App.Views.ModsPage"`; add `xmlns:conv="clr-namespace:Sanduhr.App.Converters"`; every `{x:Static views:WidgetsPage.Not}` becomes `{x:Static conv:Bools.Not}` and `{x:Static views:WidgetsPage.NotVisible}` becomes `{x:Static conv:Bools.NotVisible}`.
- `Views/ModsPage.xaml.cs`: the class is `public partial class ModsPage : UserControl`; delete the `Not` and `NotVisible` fields and the two nested converter classes (they are `Bools` now); `Activate` casts to `ModsPageViewModel`; the summary reads `Settings, Mods &amp; Widgets: the one list (spec, Mods &amp; Widgets). Until slice 4's page flip it is hosted in the Widgets tab.`
- `ViewModels/ModsPageViewModel.cs`: `public sealed partial class ModsPageViewModel`, its constructor `internal ModsPageViewModel(Func<WidgetCatalogService>? service = null, SettingsStore? store = null)`, and every `WidgetsPageViewModel` in the file (the two row view models' `_owner` fields and constructor parameters, `LoadPreview` calls) becomes `ModsPageViewModel`.
- `ViewModels/SettingsViewModel.cs`: `public WidgetsPageViewModel WidgetsPage { get; internal set; } = new();` becomes `public ModsPageViewModel ModsPage { get; internal set; } = new();`, with the summary `Settings, Mods &amp; Widgets (slice 4): the one list. Nothing is fetched until the catalog is opened.`
- `Views/SettingsWindow.xaml`, the Widgets TabItem: `DataContext="{Binding ModsPage}"` and `<views:ModsPage />`.
- `App.xaml.cs`: `svm.WidgetsPage.Changed` becomes `svm.ModsPage.Changed`; `_settingsWindow?.ViewModel.WidgetsPage.ReloadLocal()` becomes `_settingsWindow?.ViewModel.ModsPage.ReloadLocal()`.
- `tools/Sanduhr.RenderHarness/CompanionScenario.cs:155` and `ProposeWidgetScenario.cs:159`: `WidgetsPage = new WidgetsPageViewModel(` becomes `ModsPage = new ModsPageViewModel(`; `ProposeWidgetScenario.cs:172`: `svm.WidgetsPage.Installed` becomes `svm.ModsPage.Installed`.
- `tests/Sanduhr.App.Tests/ModsPageNamesTests.cs`: class `ModsPageNamesTests`; `WidgetsPage = new WidgetsPageViewModel(` becomes `ModsPage = new ModsPageViewModel(`; every `svm.WidgetsPage` becomes `svm.ModsPage`.
- `tests/Sanduhr.App.Tests/WidgetCatalogServiceTests.cs` and `WidgetReviewServiceTests.cs`: `new WidgetsPageViewModel(` becomes `new ModsPageViewModel(`.
- `tests/Sanduhr.App.Tests/CompanionPieceTests.cs:355`: `"WidgetsPage.xaml"` becomes `"ModsPage.xaml"`.
- `tests/Sanduhr.Tests/SettingsSourceScanTests.cs`: in `The_trademark_footer_is_one_control_on_seven_pages`, `Count("WidgetsPage.xaml")` becomes `Count("ModsPage.xaml")`; in `The_Desk_and_Widgets_pages_open_with_their_definitions`, `"WidgetsPage.xaml"` becomes `"ModsPage.xaml"`.

Check nothing is left: `git grep -n "WidgetsPage" -- windows-dotnet ':!*.md'` prints only comments that name the retired page in the past tense (`DeskCompanionViewModel.cs`, `DeskSettingsViewModel.cs` summaries), and `git grep -n "views:WidgetsPage" -- windows-dotnet` prints nothing.

- [ ] **Step 5: Build and run the touched suites**

Run: `dotnet build Sanduhr.slnx` (from `windows-dotnet/`): 0 errors.
Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsSourceScanTests`
Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~ModsPageNamesTests|FullyQualifiedName~WidgetCatalogServiceTests|FullyQualifiedName~WidgetReviewServiceTests|FullyQualifiedName~CompanionPieceTests|FullyQualifiedName~DeskPiecesSettingsTests|FullyQualifiedName~DeskSplitPagesTests"`
Expected: PASS (the companion row's "No companion yet." line still collapses while a sprite is installed: `DeskPiecesSettingsTests` covers it).

- [ ] **Step 6: Commit**

```bash
git add -A windows-dotnet/src/Sanduhr.App/Converters/BoolConverters.cs windows-dotnet/src/Sanduhr.App/Views/ModsPage.xaml windows-dotnet/src/Sanduhr.App/Views/ModsPage.xaml.cs windows-dotnet/src/Sanduhr.App/ViewModels/ModsPageViewModel.cs windows-dotnet/src/Sanduhr.App/Views/DeskPiecesPanel.xaml windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/App.xaml.cs windows-dotnet/tools/Sanduhr.RenderHarness/CompanionScenario.cs windows-dotnet/tools/Sanduhr.RenderHarness/ProposeWidgetScenario.cs windows-dotnet/tests/Sanduhr.App.Tests/ModsPageNamesTests.cs windows-dotnet/tests/Sanduhr.App.Tests/WidgetCatalogServiceTests.cs windows-dotnet/tests/Sanduhr.App.Tests/WidgetReviewServiceTests.cs windows-dotnet/tests/Sanduhr.App.Tests/CompanionPieceTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsSourceScanTests.cs
git commit -m "refactor(settings-v2): shared bool converters; the Widgets view and its view model become ModsPage for the page they turn into"
```

(`git add -A` on the listed paths records the renames' old paths as deleted.)

---

### Task 6: Every picker follows a pack switch while Settings is open

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/FontListSync.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs` (`FontChoices`, new `ReloadFonts`), `DeskMessagePageViewModel.cs` (`FontOptions`, `_fontFamilies`, new `ReloadFonts`), `ThemeDeskEditorViewModel.cs` and `ThemeStudioViewModel.cs` (new `ReloadFonts`), `DeskCompanionViewModel.cs` (`Reload`), `ModsPageViewModel.cs` (`SettingsChanged`, `Notify`), `SettingsViewModel.cs` (`ModsPage` setter, `PickerFonts`, `PacksChanged`)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/PackSwitchConsumersTests.cs`

**Interfaces:**
- Consumes: `WidgetStore.SetEnabled` (Task 1), `SettingsViewModel.ModsPage` (Task 5), `InstalledPackKit.Install/Store` (`PackThemeWiringTests.cs`).
- Produces:
  - `internal static void FontListSync.Sync<T>(ObservableCollection<T> list, IReadOnlyList<T> wanted)` (in place: moves, inserts and removes, so a bound combo keeps its selection)
  - `public ObservableCollection<string> DeskSettingsViewModel.FontChoices` (was `IReadOnlyList<string>`); `public void DeskSettingsViewModel.ReloadFonts(IReadOnlyList<string> families)`
  - `public ObservableCollection<MessageChoice<string>> DeskMessagePageViewModel.FontOptions`; `public void DeskMessagePageViewModel.ReloadFonts(IReadOnlyList<string> families)`
  - `public void ThemeDeskEditorViewModel.ReloadFonts(IReadOnlyList<string> families)`, `public void ThemeStudioViewModel.ReloadFonts(IReadOnlyList<string> families)`
  - `internal Func<IReadOnlyList<string>> SettingsViewModel.PickerFonts { get; set; }` (default: `DeskFonts.ForThisPc(new Paths()).PickerFamilies()`), `public void SettingsViewModel.PacksChanged()`
  - `internal Action? ModsPageViewModel.SettingsChanged { get; set; }`, raised with `Changed` after every install, uninstall, own-art change (and, from Task 9, every switch); `SettingsViewModel.ModsPage`'s setter sets it to `PacksChanged`, so the pickers follow without App (App's `Changed` keeps reloading the widget's themes, the Desk and its fonts).
  - `DeskCompanionViewModel.Reload` lists enabled sprites only.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/PackSwitchConsumersTests.cs`:

```csharp
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 4 Done: "switching a pack off removes it from its picker", in the App: the theme strip, the ink presets, the four font
/// pickers and the companion's sprites follow a switch while Settings is open, and a pack that is in use falls back without losing the
/// saved choice (Review Focus 1).
/// </summary>
[Collection("SettingsWindow")]
public sealed class PackSwitchConsumersTests : AppWindowFixture
{
    private const string PackTheme = "good-theme:fixture-dark";

    [Fact]
    public void Switching_off_the_theme_in_use_falls_back_unsaved_and_back_on_restores_it() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var paths = ScratchPaths();
        InstalledPackKit.Install(paths, "good-theme");
        widget.ReloadUserThemes();
        widget.ApplyThemeByKey(PackTheme);
        Assert.Equal(PackTheme, widget.Palette.Key);

        Assert.True(InstalledPackKit.Store(paths).SetEnabled("good-theme", false).Ok);
        widget.ReloadUserThemes();

        Assert.DoesNotContain(widget.Themes, t => t.Key == PackTheme);
        Assert.Equal("obsidian", widget.Palette.Key);
        Assert.Equal(PackTheme, new SettingsStore(paths).LoadTheme());   // shown, not saved

        Assert.True(InstalledPackKit.Store(paths).SetEnabled("good-theme", true).Ok);
        widget.ReloadUserThemes();
        Assert.Equal(PackTheme, widget.Palette.Key);
    });

    [Fact]
    public void A_palette_pack_switched_off_leaves_the_ink_presets_and_the_ink_keeps_its_colors() => AppHost.Run(() =>
    {
        var paths = ScratchPaths();
        InstalledPackKit.Install(paths, "good-palette");
        var store = new SettingsStore(paths);
        var desk = new DeskSettingsViewModel(store, ["Segoe UI"], () => [], () => PackInkPresets.Merged(InstalledPackKit.Store(paths)),
            () => InstalledPackKit.Store(paths));
        desk.InkPreset = "plain · Fixture palette";

        Assert.True(InstalledPackKit.Store(paths).SetEnabled("good-palette", false).Ok);
        desk.Reload();

        Assert.DoesNotContain("plain · Fixture palette", desk.InkPresetNames);
        Assert.Null(desk.InkPreset);
        Assert.Equal("ff2a6d,05d9e8", store.LoadDeskInk().Color);
    });

    [Fact]
    public void Font_pickers_drop_a_family_that_leaves_and_keep_their_selection() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        svm.PickerFonts = () => ["EsteFont Pro", "Fixture Sans", "Segoe UI"];
        svm.PacksChanged();
        svm.Desk.Font = "Segoe UI";
        Assert.Contains("Fixture Sans", svm.Desk.FontChoices);
        Assert.Contains(svm.MessagePage.FontOptions, c => c.Value == "Fixture Sans");
        Assert.Contains("Fixture Sans", svm.ThemeMatch.Editor.FontChoices);
        Assert.Contains("Fixture Sans", svm.Themes.Studio.FontChoices);
        Assert.Contains("Fixture Sans", svm.Themes.Studio.DeskEditor.FontChoices);

        svm.PickerFonts = () => ["EsteFont Pro", "Segoe UI"];
        svm.PacksChanged();

        Assert.DoesNotContain("Fixture Sans", svm.Desk.FontChoices);
        Assert.DoesNotContain(svm.MessagePage.FontOptions, c => c.Value == "Fixture Sans");
        Assert.DoesNotContain("Fixture Sans", svm.ThemeMatch.Editor.FontChoices);
        Assert.DoesNotContain("Fixture Sans", svm.Themes.Studio.FontChoices);
        Assert.DoesNotContain("Fixture Sans", svm.Themes.Studio.DeskEditor.FontChoices);
        Assert.Equal("Segoe UI", svm.Desk.Font);
        Assert.Equal(DeskSettingsViewModel.SystemFont, svm.Desk.FontChoices[^1]);
    });

    [Fact]
    public void A_saved_font_from_a_pack_switched_off_stays_selected() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        svm.PickerFonts = () => ["EsteFont Pro", "Fixture Sans", "Segoe UI"];
        svm.PacksChanged();
        svm.Desk.Font = "Fixture Sans";
        svm.MessagePage.Font = svm.MessagePage.FontOptions.Single(c => c.Value == "Fixture Sans");

        svm.PickerFonts = () => ["EsteFont Pro", "Segoe UI"];
        svm.PacksChanged();

        Assert.Equal("Fixture Sans", svm.Desk.Font);
        Assert.Contains("Fixture Sans", svm.Desk.FontChoices);
        Assert.Equal("Fixture Sans", svm.MessagePage.Font.Value);
        Assert.Equal("Fixture Sans", new SettingsStore(new Paths()).LoadDesk().Font);   // Settings' stores live in the dev profile
    });

    [Fact]
    public void The_companion_skips_a_sprite_that_is_off_and_returns_to_it() => AppHost.Run(() =>
    {
        var paths = ScratchPaths();
        InstalledPackKit.Install(paths, "good-sprite");
        InstalledPackKit.Install(paths, "good-sprite-stages");
        var store = new SettingsStore(paths);
        store.SaveCompanion(new CompanionSettings("good-sprite-stages", true));
        var companion = new DeskCompanionViewModel(store, () => InstalledPackKit.Store(paths));
        companion.Reload();
        Assert.Equal("good-sprite-stages", companion.SelectedCompanion!.Id);

        Assert.True(InstalledPackKit.Store(paths).SetEnabled("good-sprite-stages", false).Ok);
        companion.Reload();
        Assert.Equal(["good-sprite"], companion.Companions.Select(c => c.Id));
        Assert.Equal("good-sprite", companion.SelectedCompanion!.Id);
        Assert.Equal("good-sprite-stages", store.LoadCompanion().Pack);   // the choice is kept

        Assert.True(InstalledPackKit.Store(paths).SetEnabled("good-sprite-stages", true).Ok);
        companion.Reload();
        Assert.Equal("good-sprite-stages", companion.SelectedCompanion!.Id);
    });

    [Fact]
    public void FontListSync_moves_and_trims_in_place()
    {
        var list = new System.Collections.ObjectModel.ObservableCollection<string>(["a", "b", "c", "d"]);
        var seen = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, e) => seen.Add(e.Action);

        FontListSync.Sync(list, ["a", "c", "e", "d"]);

        Assert.Equal(["a", "c", "e", "d"], list);
        Assert.DoesNotContain(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, seen);
    }
}
```

(`InstalledPackKit.Install` asserts its paths are under `%TEMP%`; `ScratchPaths()` is.)

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~PackSwitchConsumersTests`
Expected: build FAILS with `'SettingsViewModel' does not contain a definition for 'PickerFonts'` and `The name 'FontListSync' does not exist`.

- [ ] **Step 3: Sync in place**

Create `windows-dotnet/src/Sanduhr.App/ViewModels/FontListSync.cs`:

```csharp
using System.Collections.ObjectModel;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Brings a bound list to <c>wanted</c> without clearing it: items that left are removed, the rest are moved or inserted into order. A combo
/// bound to the list keeps its selection, because the selected item is never removed while it is wanted (a combo whose selected item leaves
/// its items writes null back, WF2).
/// </summary>
internal static class FontListSync
{
    public static void Sync<T>(ObservableCollection<T> list, IReadOnlyList<T> wanted)
    {
        var eq = EqualityComparer<T>.Default;
        for (int i = list.Count - 1; i >= 0; i--)
            if (!wanted.Contains(list[i], eq)) list.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            if (i < list.Count && eq.Equals(list[i], wanted[i])) continue;
            int at = -1;
            for (int j = i + 1; j < list.Count; j++)
                if (eq.Equals(list[j], wanted[i])) { at = j; break; }
            if (at >= 0) list.Move(at, i);
            else list.Insert(i, wanted[i]);
        }
    }
}
```

- [ ] **Step 4: Each picker reloads its fonts**

In `windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs`:

- `public IReadOnlyList<string> FontChoices { get; }` becomes `public ObservableCollection<string> FontChoices { get; }`, and the constructor's `FontChoices = [.. fontFamilies, SystemFont];` becomes `FontChoices = new([.. fontFamilies, SystemFont]);`.
- Below `Reload()`, add:

```csharp
    /// <summary>The font packs changed (one switched, installed or removed): the Desk font list follows. The saved family stays listed, so the
    /// box never goes blank and nothing rewrites the setting, while the Desk draws its fallback for a family this PC no longer has.</summary>
    public void ReloadFonts(IReadOnlyList<string> families)
    {
        var wanted = new List<string>(families);
        if (Font != SystemFont && !wanted.Contains(Font)) wanted.Add(Font);
        wanted.Add(SystemFont);
        FontListSync.Sync(FontChoices, wanted);
    }
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/DeskMessagePageViewModel.cs`:

- `private readonly IReadOnlyList<string> _fontFamilies;` becomes `private IReadOnlyList<string> _fontFamilies;`.
- `public IReadOnlyList<MessageChoice<string>> FontOptions { get; }` becomes `public ObservableCollection<MessageChoice<string>> FontOptions { get; }`, and the constructor's `FontOptions = BuildFontOptions();` becomes `FontOptions = new(BuildFontOptions());` (add `using System.Collections.ObjectModel;` if missing).
- After `BuildFontOptions`, add:

```csharp
    /// <summary>The font packs changed: the Message font list follows, keeping the saved family listed (BuildFontOptions already does).</summary>
    public void ReloadFonts(IReadOnlyList<string> families)
    {
        _fontFamilies = families;
        FontListSync.Sync(FontOptions, BuildFontOptions());
    }
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/ThemeDeskEditorViewModel.cs`, after the constructor, add:

```csharp
    /// <summary>The font packs changed: the Desk font row's choices follow; the shown theme's font stays listed (as <see cref="Load"/> keeps it).</summary>
    public void ReloadFonts(IReadOnlyList<string> families)
    {
        var wanted = new List<string> { SettingsNames.ThemeDeskFromTheme };
        wanted.AddRange(families);
        if (_desk.Font is { Length: > 0 } f && !wanted.Contains(f)) wanted.Add(f);
        FontListSync.Sync(_fontChoices, wanted);
    }
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/ThemeStudioViewModel.cs`, after the constructor, add:

```csharp
    /// <summary>The font packs changed: the Font picker and the Desk font row follow; the draft's font stays listed.</summary>
    public void ReloadFonts(IReadOnlyList<string> families)
    {
        var wanted = new List<string> { SettingsNames.ThemeDefaultFont };
        wanted.AddRange(families);
        if (_draft.Font is { Length: > 0 } f && !wanted.Contains(f)) wanted.Add(f);
        FontListSync.Sync(_fontChoices, wanted);
        DeskEditor.ReloadFonts(families);
    }
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/DeskCompanionViewModel.cs` (`Reload`), change `foreach (var row in reg.Packs)` to `foreach (var row in reg.Packs.Where(p => p.Enabled))`, and the summary of `Companions` to `The installed sprite packs that are switched on: the companion's choices.`

In `windows-dotnet/src/Sanduhr.App/ViewModels/ModsPageViewModel.cs`, after `public Action? Changed { get; set; }`, add:

```csharp
    /// <summary>Raised with <see cref="Changed"/>: the Settings window's own pickers read the packs again (SettingsViewModel sets it).</summary>
    internal Action? SettingsChanged { get; set; }

    private void Notify()
    {
        Changed?.Invoke();
        SettingsChanged?.Invoke();
    }
```

and change every `Changed?.Invoke();` and `if (result.Ok) Changed?.Invoke();` / `if (installed.Ok) Changed?.Invoke();` in the file to call `Notify()` instead (three places: `ActAsync`, `UninstallAsync`, `UseOwnArtAsync`).

In `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`, replace `public ModsPageViewModel ModsPage { get; internal set; } = new();` with:

```csharp
    private ModsPageViewModel _modsPage = null!;

    /// <summary>Settings, Mods &amp; Widgets (slice 4): the one list. Tests and the harness put in their own (a fake catalog); the setter wires
    /// it to this window's pickers.</summary>
    public ModsPageViewModel ModsPage
    {
        get => _modsPage;
        internal set
        {
            _modsPage = value;
            value.SettingsChanged = PacksChanged;
        }
    }
```

and in the constructor, right after `General = new GeneralViewModel(widget: widget);`, add `ModsPage = new ModsPageViewModel();`. After the `ModsPage` property, add:

```csharp
    /// <summary>This PC's font families for the pickers (the bundled ones, the system's, the font packs that are on). A seam for tests.</summary>
    internal Func<IReadOnlyList<string>> PickerFonts { get; set; } =
        () => Sanduhr.App.Desk.DeskFonts.ForThisPc(new Paths()).PickerFamilies();

    /// <summary>
    /// A widget pack was switched, installed or removed (Settings v2 slice 4: every consumer honors the switch): the four font pickers, the Ink
    /// presets and the companion's sprites read again. The widget's theme list is App's (it reloads the themes in the same handler).
    /// </summary>
    public void PacksChanged()
    {
        var fonts = PickerFonts();
        Desk.ReloadFonts(fonts);
        MessagePage.ReloadFonts(fonts);
        ThemeMatch.Editor.ReloadFonts(fonts);
        Themes.Studio.ReloadFonts(fonts);
        Desk.Reload();   // the Ink presets and the companion's sprites
    }
```

App's `svm.ModsPage.Changed` lambda is unchanged: it still reloads the widget's themes and resources, the Desk's fonts, the design service's fonts and the Desk. Its last line `svm.Desk.Reload();` stays harmless (the pickers' own reload runs through `SettingsChanged`); change the comment above the lambda to `// A pack installed, uninstalled or switched on or off (a companion sprite included), theme, font or palette pack.`

Add to `PackSwitchConsumersTests`:

```csharp
    [Fact]
    public void The_page_tells_the_windows_pickers_when_a_pack_changes() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        int asked = 0;
        svm.PickerFonts = () => { asked++; return ["EsteFont Pro"]; };
        var page = new ModsPageViewModel();
        svm.ModsPage = page;

        page.SettingsChanged!.Invoke();

        Assert.Equal(1, asked);
    });
```

- [ ] **Step 5: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~PackSwitchConsumersTests|FullyQualifiedName~DeskInkPackPresetsTests|FullyQualifiedName~DeskFontsPackTests|FullyQualifiedName~ThemeDeskEditorTests|FullyQualifiedName~ThemeStudioDeskTests|FullyQualifiedName~MessageModeSegmentTests|FullyQualifiedName~PackThemeWiringTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/FontListSync.cs windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/DeskMessagePageViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/ThemeDeskEditorViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/ThemeStudioViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/DeskCompanionViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/ModsPageViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.App/App.xaml.cs windows-dotnet/tests/Sanduhr.App.Tests/PackSwitchConsumersTests.cs
git commit -m "feat(widgets): the font pickers, ink presets, companion and theme list follow a pack switch while Settings is open"
```

---

### Task 7: One folder's "What this folder holds", and the host the Claude Code views share

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeHost.cs`, `windows-dotnet/src/Sanduhr.App/ViewModels/FolderHoldsViewModel.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (one constant, `FolderHoldsHelp`)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/ModsPageFixture.cs`, `windows-dotnet/tests/Sanduhr.App.Tests/FolderHoldsTests.cs`

**Interfaces:**
- Consumes: `ModInventory.Read`, `ConfigMask` (already applied by `ModInventory`), `ModsPageText.SwitchSummary`, `SettingsNames.TranscriptsKept`, `ClaudeCodeFolders.Build/Display`, `ClaudeCodeIntegrations`, `FolderMods` (Task 3).
- Produces:
  - `internal sealed record ClaudeCodeHost(string Profile, string? ConfigDir, Func<IReadOnlyList<string>> AddedFolders, string SanduhrAppDataDir, string SanduhrExternalDir, string SanduhrLegacyModsRoot, Func<string?> ActiveAccountFolder, string ModSourceDir)` with `static ClaudeCodeHost For(WidgetViewModel widget)`, `ClaudeCodeIntegrations Integrations()`, `IReadOnlyList<CcFolder> Folders()`, `string Display(string path)`, `IReadOnlyList<FolderMods> ReadFolders()`
  - `public sealed partial class FolderHoldsViewModel` with `internal FolderHoldsViewModel(ClaudeCodeHost host, string folder)`; `void EnsureLoaded()`; `[RelayCommand] void Refresh()`; observable `SettingsSummary`, `SettingsDetail`, `SettingsUnreadable`, `StatuslineSummary`, `StatuslineCommand`, `HooksSummary`, `PluginsSummary`, `McpSummary`, `McpProjects`, `ClaudeMdSummary`, `RetentionSummary`, `RetentionWarning`, `Loaded`; collections `HookLines`, `PluginLines` (`ModsLine`), `McpNames`; `string RefreshName`
  - `SettingsNames.FolderHoldsHelp` (the collapsed section's summary line)
  - Test helper `ModsPageFixture` (scratch profile and host; reused by Tasks 8 to 11)

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/ModsPageFixture.cs`:

```csharp
using System.IO;
using System.Text;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Tests;

/// <summary>
/// A scratch profile with Claude Code folders, a scratch Sanduhr AppData and external folder, and a stand-in for the meters mod's files,
/// for the Claude Code cards and Mods &amp; Widgets (slice 4). Touches no process-wide static, never the real profile or AppData.
/// </summary>
public abstract class ModsPageFixture : IDisposable
{
    protected readonly string Root = Path.Combine(Path.GetTempPath(), "sanduhr-mods4-" + Guid.NewGuid().ToString("N")[..10]);
    protected string Profile => Path.Combine(Root, "profile");
    protected string ModSource => Path.Combine(Root, "app", "mods", "sanduhr-meters");

    protected ModsPageFixture()
    {
        Directory.CreateDirectory(Profile);
        Put(ModSource, ".claude-plugin/plugin.json", """{ "name": "sanduhr-meters", "version": "0.3.0" }""");
        Put(ModSource, "hooks/hooks.json", """{ "modules": ["./register.tsx"] }""");
        Put(ModSource, "hooks/register.tsx", "export {};");
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>A Claude Code home (it has a projects folder), with the given settings.json text when not null.</summary>
    protected string Home(string name, string? settings = null)
    {
        var dir = Path.Combine(Profile, name);
        Directory.CreateDirectory(Path.Combine(dir, "projects"));
        if (settings is not null) File.WriteAllText(Path.Combine(dir, "settings.json"), settings, new UTF8Encoding(false));
        return dir;
    }

    protected static void Put(string folder, string relative, string content)
    {
        var path = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    internal ClaudeCodeHost Host(string? configDir = null, string? activeAccountFolder = null, params string[] added) =>
        new(Profile, configDir, () => added, Path.Combine(Root, "appdata"), Path.Combine(Root, "external"), Path.Combine(Root, "legacy"),
            () => activeAccountFolder, ModSource);

    protected static string SettingsOf(string folder) => Path.Combine(folder, "settings.json");
}
```

Create `windows-dotnet/tests/Sanduhr.App.Tests/FolderHoldsTests.cs`:

```csharp
using System.IO;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>A folder card's "What this folder holds" (slice 4): Mods &amp; Config's read-only report for one folder, read when opened.</summary>
public sealed class FolderHoldsTests : ModsPageFixture
{
    private string Seeded()
    {
        var home = Home(".claude-personal",
            "{\"enabledPlugins\":{\"alpha@m\":true,\"beta@m\":false,\"gamma@m\":true},"
            + "\"statusLine\":{\"type\":\"command\",\"command\":\"node other.js\"},"
            + "\"hooks\":{\"Notification\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"echo hi\"}]}]},"
            + "\"cleanupPeriodDays\":45}");
        Put(home, ".claude.json", """{"mcpServers":{"files":{"command":"x"}},"projects":{"a":{"mcpServers":{"y":{}}}}}""");
        Put(home, "CLAUDE.md", "one\ntwo\n");
        return home;
    }

    [Fact]
    public void Nothing_is_read_until_the_section_is_opened()
    {
        var holds = new FolderHoldsViewModel(Host(), Seeded());
        Assert.False(holds.Loaded);
        Assert.Equal("", holds.SettingsSummary);

        holds.EnsureLoaded();

        Assert.True(holds.Loaded);
        Assert.Equal("Readable", holds.SettingsSummary);
    }

    [Fact]
    public void It_reports_the_folders_settings_statusline_hooks_switches_MCP_CLAUDE_md_and_retention()
    {
        var holds = new FolderHoldsViewModel(Host(), Seeded());
        holds.EnsureLoaded();

        Assert.Equal("Someone else's statusline", holds.StatuslineSummary);
        Assert.Equal("node other.js", holds.StatuslineCommand);
        Assert.Equal("1 group on 1 event", holds.HooksSummary);
        Assert.Equal("Notification, 1 group", Assert.Single(holds.HookLines).AutomationName);
        Assert.Equal("2 on, 1 off", holds.PluginsSummary);
        Assert.Equal(["alpha@m, On", "beta@m, Off", "gamma@m, On"], holds.PluginLines.Select(l => l.AutomationName));
        Assert.Equal("1 server", holds.McpSummary);
        Assert.Equal(["files"], holds.McpNames);
        Assert.Equal("1 project has their own servers.", holds.McpProjects);
        Assert.StartsWith("2 lines", holds.ClaudeMdSummary);
        Assert.Equal("45 days.", holds.RetentionSummary);
        Assert.False(holds.RetentionWarning);
    }

    [Fact]
    public void An_unreadable_settings_json_says_where_and_changes_nothing()
    {
        var home = Home(".claude", "{ \"a\": 1, }");
        var before = File.ReadAllBytes(SettingsOf(home));
        var holds = new FolderHoldsViewModel(Host(), home);
        holds.EnsureLoaded();

        Assert.True(holds.SettingsUnreadable);
        Assert.StartsWith("Unreadable: line 1", holds.SettingsSummary);
        Assert.Equal("Unknown while settings.json is unreadable.", holds.RetentionSummary);
        Assert.Equal(before, File.ReadAllBytes(SettingsOf(home)));
    }

    [Fact]
    public void Unset_retention_warns_in_the_words_of_Claude_Codes_rule()
    {
        var holds = new FolderHoldsViewModel(Host(), Home(".claude", "{}"));
        holds.EnsureLoaded();
        Assert.True(holds.RetentionWarning);
        Assert.Equal(ModInventory.RetentionWarning + ".", holds.RetentionSummary);
    }

    [Fact]
    public void Refresh_reads_the_folder_again()
    {
        var home = Home(".claude", "{}");
        var holds = new FolderHoldsViewModel(Host(), home);
        holds.EnsureLoaded();
        Assert.Equal("None", holds.McpSummary);

        Put(home, ".claude.json", """{"mcpServers":{"a":{},"b":{}}}""");
        holds.RefreshCommand.Execute(null);

        Assert.Equal("2 servers", holds.McpSummary);
        Assert.StartsWith("Refresh", holds.RefreshName);
    }

    [Fact]
    public void The_host_reads_every_known_folder_with_its_meters_state()
    {
        Home(".claude", "{}");
        Home(".claude-work", "{ broken");
        var folders = Host().ReadFolders();
        Assert.Equal([@"~\.claude", @"~\.claude-work"], folders.Select(f => f.Display));
        Assert.Equal([FolderState.NotInstalled, FolderState.Unreadable], folders.Select(InstalledItems.MetersState));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~FolderHoldsTests`
Expected: build FAILS with `The type or namespace name 'ClaudeCodeHost' could not be found` and `'FolderHoldsViewModel' could not be found`.

- [ ] **Step 3: Write the host and the report**

Create `windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeHost.cs`:

```csharp
using System.IO;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// What the Claude Code cards and Mods &amp; Widgets need from the app: the profile, the folders, Sanduhr's own folders and the meters mod's
/// files. A record, so tests build the views over a scratch profile (it replaces Mods &amp; Config's host, and adds the mod source).
/// </summary>
internal sealed record ClaudeCodeHost(
    string Profile,
    string? ConfigDir,
    Func<IReadOnlyList<string>> AddedFolders,
    string SanduhrAppDataDir,
    string SanduhrExternalDir,
    string SanduhrLegacyModsRoot,
    Func<string?> ActiveAccountFolder,
    string ModSourceDir)
{
    public static ClaudeCodeHost For(WidgetViewModel widget) => new(
        ClaudeCodeProfile.Dir, ClaudeCodeProfile.ConfigDir, widget.LoadCcFoldersAdded,
        widget.SanduhrAppDataDir, widget.SanduhrExternalDir, widget.SanduhrLegacyModsRoot,
        () => widget.ShownActiveAccount is { } label ? widget.AccountFolder(label) : null,
        Path.Combine(AppContext.BaseDirectory, "mods", ClaudeCodeIntegrations.ModName));

    public ClaudeCodeIntegrations Integrations() =>
        new(Profile, SanduhrAppDataDir, ModSourceDir, externalDir: SanduhrExternalDir) { LegacyModsRoots = [SanduhrLegacyModsRoot] };

    /// <summary>Every known folder: found first, then added by hand.</summary>
    public IReadOnlyList<CcFolder> Folders() => ClaudeCodeFolders.Build(Profile, ConfigDir, AddedFolders());

    /// <summary>The path with the profile folder written as ~.</summary>
    public string Display(string path) => ClaudeCodeFolders.Display(path, Profile);

    /// <summary>Every known folder read for the one list: its report, Sanduhr's mod state and the meters status. Files only; never throws.</summary>
    public IReadOnlyList<FolderMods> ReadFolders()
    {
        var integ = Integrations();
        var list = new List<FolderMods>();
        foreach (var f in Folders())
        {
            var report = ModInventory.Read(f.Path, Profile);
            OwnModState? own = null;
            var meters = IntegrationStatus.NotInstalled;
            try
            {
                own = integ.OwnModStateOf(f.Path);
                meters = integ.Status(IntegrationKind.Meters, f.Path);
            }
            catch (Exception) { /* a folder that can't be read reads as nothing of Sanduhr's */ }
            list.Add(new FolderMods(f.Path, Display(f.Path), report, own, meters));
        }
        return list;
    }
}
```

Create `windows-dotnet/src/Sanduhr.App/ViewModels/FolderHoldsViewModel.cs` (the report part of `ModsConfigViewModel.Refresh`, moved as it is, for one folder):

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// A Claude Code folder card's "What this folder holds" (Settings v2 slice 4; Mods &amp; Config's read-only report, WF4, WF9): settings.json,
/// the statusline, hooks, plugin switches, MCP servers, CLAUDE.md and how long Claude Code keeps transcripts. Read from files only, when the
/// section is first opened and on Refresh; nothing runs and nothing is written. Secret values never show (<see cref="ConfigMask"/>).
/// </summary>
public sealed partial class FolderHoldsViewModel : ObservableObject
{
    private readonly ClaudeCodeHost _host;

    internal FolderHoldsViewModel(ClaudeCodeHost host, string folder)
    {
        _host = host;
        Folder = folder;
    }

    public string Folder { get; }

    public ObservableCollection<ModsLine> HookLines { get; } = [];
    public ObservableCollection<ModsLine> PluginLines { get; } = [];
    public ObservableCollection<string> McpNames { get; } = [];

    [ObservableProperty] private bool _loaded;
    [ObservableProperty] private string _settingsSummary = "";
    [ObservableProperty] private string _settingsDetail = "";
    [ObservableProperty] private bool _settingsUnreadable;
    [ObservableProperty] private string _statuslineSummary = "";
    [ObservableProperty] private string _statuslineCommand = "";
    [ObservableProperty] private string _hooksSummary = "";
    [ObservableProperty] private string _pluginsSummary = "";
    [ObservableProperty] private string _mcpSummary = "";
    [ObservableProperty] private string _mcpProjects = "";
    [ObservableProperty] private string _claudeMdSummary = "";
    [ObservableProperty] private string _retentionSummary = "";
    [ObservableProperty] private bool _retentionWarning;

    /// <summary>Refresh's spoken name: the folder it reads.</summary>
    public string RefreshName => $"{SettingsNames.RefreshFolder}, read {_host.Display(Folder)} again";

    /// <summary>Reads the folder the first time the section opens.</summary>
    public void EnsureLoaded()
    {
        if (!Loaded) Refresh();
    }

    /// <summary>Reads the folder again. Files only.</summary>
    [RelayCommand]
    public void Refresh()
    {
        HookLines.Clear();
        PluginLines.Clear();
        McpNames.Clear();
        FolderReport report;
        try { report = ModInventory.Read(Folder, _host.Profile); }
        catch (Exception)
        {
            SettingsSummary = "Couldn't read this folder.";
            Loaded = true;
            return;
        }

        SettingsUnreadable = report.Settings.Kind == SettingsKind.Unreadable;
        (SettingsSummary, SettingsDetail) = report.Settings.Kind switch
        {
            SettingsKind.Readable => ("Readable", "settings.json parses as JSON."),
            SettingsKind.Unreadable when report.Settings.Line > 0 =>
                ($"Unreadable: line {report.Settings.Line}, column {report.Settings.Column}",
                 "Claude Code reads settings.json strictly: no comments, no trailing commas. Sanduhr changes nothing in it while it is unreadable."),
            SettingsKind.Unreadable => ("Unreadable", "settings.json can't be opened. Sanduhr changes nothing in it."),
            _ => ("Missing", "This folder has no settings.json yet."),
        };

        if (report.Statusline is { } sl)
        {
            StatuslineSummary = sl.IsSanduhr ? "Sanduhr's statusline" : "Someone else's statusline";
            StatuslineCommand = sl.Command;
        }
        else
        {
            StatuslineSummary = "None";
            StatuslineCommand = "";
        }

        foreach (var h in report.Hooks)
            HookLines.Add(new ModsLine(h.Event,
                $"{h.Groups} {(h.Groups == 1 ? "group" : "groups")}" + (h.Sanduhr > 0 ? $", {h.Sanduhr} from Sanduhr" : "")));
        int groups = report.Hooks.Sum(h => h.Groups);
        HooksSummary = report.Hooks.Count == 0
            ? "None"
            : $"{groups} {(groups == 1 ? "group" : "groups")} on {report.Hooks.Count} {(report.Hooks.Count == 1 ? "event" : "events")}";

        foreach (var (name, on) in report.EnabledPlugins.OrderBy(p => p.Key, StringComparer.Ordinal))
            PluginLines.Add(new ModsLine(name, on ? "On" : "Off"));
        int pluginsOn = report.EnabledPlugins.Count(p => p.Value);
        PluginsSummary = ModsPageText.SwitchSummary(pluginsOn, report.EnabledPlugins.Count - pluginsOn);

        foreach (var n in report.Mcp.UserServers) McpNames.Add(n);
        McpSummary = report.Mcp.UserServers.Count == 0
            ? "None"
            : $"{report.Mcp.UserServers.Count} {(report.Mcp.UserServers.Count == 1 ? "server" : "servers")}";
        McpProjects = report.Mcp.LocalProjectCount > 0
            ? $"{report.Mcp.LocalProjectCount} {(report.Mcp.LocalProjectCount == 1 ? "project has" : "projects have")} their own servers."
            : "";

        ClaudeMdSummary = report.ClaudeMd.Exists
            ? $"{report.ClaudeMd.Lines} {(report.ClaudeMd.Lines == 1 ? "line" : "lines")}, {FormatBytes(report.ClaudeMd.Bytes)}"
            : "None";

        RetentionWarning = report.Retention.Warning;
        RetentionSummary = report.Retention.Warning
            ? ModInventory.RetentionWarning + "."
            : report.Retention.CleanupPeriodDays is { } days ? $"{days} days." : "Unknown while settings.json is unreadable.";
        Loaded = true;
    }

    private static string FormatBytes(long bytes) => bytes < 1024 ? $"{bytes} bytes" : $"{bytes / 1024.0:0.#} KB";
}
```

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, after `AddAccountToLink`, add:

```csharp
    /// <summary>A folder card's collapsed "What this folder holds": its summary line (a SectionExpander always has one, slice 3 M3).</summary>
    public const string FolderHoldsHelp = "settings.json, the statusline, hooks, plugin switches, MCP servers, CLAUDE.md and how long transcripts are kept.";
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~FolderHoldsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeHost.cs windows-dotnet/src/Sanduhr.App/ViewModels/FolderHoldsViewModel.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.App.Tests/ModsPageFixture.cs windows-dotnet/tests/Sanduhr.App.Tests/FolderHoldsTests.cs
git commit -m "feat(claude-code): one folder's What this folder holds, read when opened, and the host the Claude Code views share"
```

---

### Task 8: Claude Code folder cards act on their own folder; the page in its own file

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs` (the row class, the page's pieces, `Reload`, `RefreshStatuses`, every install and remove, the session choice)
- Create: `windows-dotnet/src/Sanduhr.App/Views/ClaudeCodePage.xaml`, `windows-dotnet/src/Sanduhr.App/Views/ClaudeCodePage.xaml.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (the Claude Code TabItem body; the Mods &amp; Config TabItem's `config`, `mcpservers`, `claudemd` and `retention` sections; the `CcPieceTemplate` resource), `Views/SettingsWindow.xaml.cs` (`SelectPage`)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (`FolderConfig`, Claude Code and Mods rows, `NameAnchors`, `CollapsedSections`, `PageSynonyms`), `SettingsNames.cs` (`Table`: `TranscriptsKept` moves page)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/ClaudeCodeCardsTests.cs`, `windows-dotnet/tests/Sanduhr.App.Tests/ModsAndCardsPagesTests.cs`, `windows-dotnet/tests/Sanduhr.Tests/SettingsModsIndexTests.cs`; rewrite `windows-dotnet/tests/Sanduhr.App.Tests/ClaudeCodeFolderChoiceTests.cs`; modify `CcPieceViewModelTests.cs` (unchanged facts), `SettingsWindowNamesTests.cs`, `tests/Sanduhr.Tests/SettingsIndexTests.cs`

**Interfaces:**
- Consumes: `ClaudeCodeHost`, `FolderHoldsViewModel` (Task 7), `ModsFolderChoice.Pick`, `SettingsNames.GlowHookIn`, `GlowNeedsHook`, `AddAccountToLink`, `FolderHolds`, `FolderHoldsHelp`, `MetersSection`, `DeskGlow`, `FolderOn/Off/NotInstalled/Unreadable` (Task 2, 7), `IntegrationConsentDialog.Ask`, `ConsentSpec`, `ConsentChoice`.
- Produces:
  - `CcFolderRowViewModel`: `CcPieceViewModel Statusline`, `Mcp`, `Hooks`; `[ObservableProperty] string MetersText`; `[ObservableProperty] bool HoldsOpen`; `FolderHoldsViewModel Holds`; `bool HasAccounts`, `bool NoAccounts`; `string MetersLink` (`"mods#meters"`), `MetersLinkTitle`, `MetersLinkName`, `HoldsName`, `PiecesHeading`
  - `ClaudeCodeViewModel`: `[ObservableProperty] string GlowHookStatus`; `internal Func<ConsentSpec, ConsentChoice?> AskConsent { get; set; }`; `internal CcFolderRowViewModel? OpenHoldsForLanding()`; `public void AfterMetersChange(bool installed)`; `public event Action<CcFolderRowViewModel>? HoldsLanded`; `ForgetSessionChoice()` now forgets the last opened holds. Removed: `SelectedFolder`, `FolderChoices`, the page-level `Statusline`, `Mcp`, `Meters`, `Hooks`, and the meters install and remove (the meters move to Mods &amp; Widgets, Task 10; until then Mods &amp; Config's own-mod row still switches, updates and removes them).
  - `SettingsIndex.FolderConfig = "folder-config"`; Claude Code anchors `folders`, `folder-config`, `glow`, `glow-where`; the Claude Code `statusline`, `mcp`, `meters` rows and the Mods `config`, `mcpservers`, `claudemd`, `retention` rows leave the index (their aliases go live, except `claudecode#meters`, whose target arrives in Task 12).
  - `SettingsNames.Table`: `(TranscriptsKept, SettingsPage.ClaudeCode)`.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/ClaudeCodeCardsTests.cs`:

```csharp
using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 4: each Claude Code folder card holds everything Sanduhr installs for that folder and acts on that folder only
/// (WF4, WF9, WF27; Review Focus 5). The install combo is gone.
/// </summary>
[Collection("SettingsWindow")]
public sealed class ClaudeCodeCardsTests : AppWindowFixture
{
    private string Home(string name, string settings = "{}")
    {
        var dir = Path.Combine(Profile, name);
        Directory.CreateDirectory(Path.Combine(dir, "projects"));
        File.WriteAllText(Path.Combine(dir, "settings.json"), settings);
        return dir;
    }

    private (ClaudeCodeViewModel Page, List<ConsentSpec> Asked) Page(Func<ConsentSpec, ConsentChoice?>? answer = null)
    {
        var (widget, _) = ScratchWidget("Personal");
        var page = new ClaudeCodeViewModel(widget);
        var asked = new List<ConsentSpec>();
        page.AskConsent = spec => { asked.Add(spec); return answer?.Invoke(spec); };
        return (page, asked);
    }

    private static readonly ConsentChoice Yes = new(StatuslineMode.Replace, [], "");

    private static CcFolderRowViewModel Card(ClaudeCodeViewModel page, string name) =>
        page.Folders.Single(f => f.Display == @"~\" + name);

    [Fact]
    public void Every_card_has_its_own_pieces_and_there_is_no_install_combo() => AppHost.Run(() =>
    {
        Home(".claude-personal");
        Home(".claude-work");
        var (page, _) = Page();

        Assert.Equal(3, page.Folders.Count);   // .claude (the fixture's), .claude-personal, .claude-work
        Assert.All(page.Folders, f =>
        {
            Assert.Equal("Statusline", f.Statusline.Title);
            Assert.Equal("MCP server", f.Mcp.Title);
            Assert.Equal(SettingsNames.ClaudeCodeGlowHook, f.Hooks.Title);
        });
        Assert.Null(typeof(ClaudeCodeViewModel).GetProperty("SelectedFolder"));
        Assert.Null(typeof(ClaudeCodeViewModel).GetProperty("Meters"));
    });

    [Fact]
    public void Each_cards_consent_names_its_own_folder_and_a_no_writes_nothing() => AppHost.Run(() =>
    {
        var work = Home(".claude-work");
        var before = File.ReadAllBytes(Path.Combine(work, "settings.json"));
        var (page, asked) = Page(answer: _ => null);

        Card(page, ".claude-work").Statusline.InstallCommand.Execute(null);
        Card(page, ".claude-work").Hooks.InstallCommand.Execute(null);

        Assert.Equal([@"~\.claude-work", @"~\.claude-work"], asked.Select(a => a.Folder));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(work, "settings.json")));
    });

    [Fact]
    public void The_glow_hook_installs_into_the_cards_folder_only() => AppHost.Run(() =>
    {
        var personal = Home(".claude-personal");
        var work = Home(".claude-work");
        var personalBefore = File.ReadAllBytes(Path.Combine(personal, "settings.json"));
        var (page, _) = Page(answer: _ => Yes);
        Assert.Equal(SettingsNames.GlowHookIn([]), page.GlowHookStatus);

        Card(page, ".claude-work").Hooks.InstallCommand.Execute(null);

        Assert.Contains("glow-waiting.signal", File.ReadAllText(Path.Combine(work, "settings.json")));
        Assert.Equal(personalBefore, File.ReadAllBytes(Path.Combine(personal, "settings.json")));
        Assert.True(Card(page, ".claude-work").Hooks.IsInstalled);
        Assert.False(Card(page, ".claude-personal").Hooks.IsInstalled);
        Assert.Equal(SettingsNames.GlowHookIn([@"~\.claude-work"]), page.GlowHookStatus);
    });

    [Fact]
    public void A_card_removes_only_its_own_folders_glow_hook() => AppHost.Run(() =>
    {
        var personal = Home(".claude-personal");
        var work = Home(".claude-work");
        var (page, _) = Page(answer: _ => Yes);
        Card(page, ".claude-personal").Hooks.InstallCommand.Execute(null);
        Card(page, ".claude-work").Hooks.InstallCommand.Execute(null);
        var workBefore = File.ReadAllBytes(Path.Combine(work, "settings.json"));

        Card(page, ".claude-personal").Hooks.RemoveCommand.Execute(null);

        Assert.Equal("{}", File.ReadAllText(Path.Combine(personal, "settings.json")));   // put back exactly
        Assert.Equal(workBefore, File.ReadAllBytes(Path.Combine(work, "settings.json")));
        Assert.Equal(SettingsNames.GlowHookIn([@"~\.claude-work"]), page.GlowHookStatus);
    });

    [Fact]
    public void A_card_for_a_folder_added_by_hand_offers_no_MCP_server() => AppHost.Run(() =>
    {
        var elsewhere = Path.Combine(Root, "elsewhere", "cc");
        Directory.CreateDirectory(elsewhere);
        var (widget, _) = ScratchWidget("Personal");
        widget.SaveCcFoldersAdded([elsewhere]);
        var page = new ClaudeCodeViewModel(widget);

        var card = page.Folders.Single(f => f.IsAdded);
        Assert.False(card.Mcp.CanInstall);
        Assert.Equal("Not available for folders added by hand.", card.Mcp.StatusText);
    });

    [Fact]
    public void The_meters_line_reads_each_folders_own_state() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var entry = Path.Combine(widget.SanduhrExternalDir, "mods", "v0.3.0-20261025120000", "sanduhr-meters").Replace("\\", "\\\\");
        Home(".claude-on", "{\"env\":{\"CLAUDE_CODE_PLUGIN_DIRS\":\"" + entry + "\"}}");
        Home(".claude-off", "{\"env\":{\"CLAUDE_CODE_PLUGIN_DIRS\":\"" + entry + "\"},\"enabledPlugins\":{\"sanduhr-meters@inline\":false}}");
        Home(".claude-broken", "{ nope");
        var page = new ClaudeCodeViewModel(widget);

        Assert.Equal(SettingsNames.FolderOn, Card(page, ".claude-on").MetersText);
        Assert.Equal(SettingsNames.FolderOff, Card(page, ".claude-off").MetersText);
        Assert.Equal(SettingsNames.FolderNotInstalled, Card(page, ".claude").MetersText);
        Assert.Equal(SettingsNames.FolderUnreadable, Card(page, ".claude-broken").MetersText);
        Assert.Equal("mods#meters", Card(page, ".claude").MetersLink);
        Assert.StartsWith(SettingsNames.LinkTitle(SettingsPage.Mods), Card(page, ".claude").MetersLinkName);
    });

    [Fact]
    public void With_no_account_the_card_says_how_to_link_one() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget();
        var page = new ClaudeCodeViewModel(widget);
        var card = Assert.Single(page.Folders);
        Assert.True(card.NoAccounts);

        var (signedIn, _) = ScratchWidget("Personal");
        Assert.True(Assert.Single(new ClaudeCodeViewModel(signedIn).Folders).HasAccounts);
    });

    [Fact]
    public void Opening_a_cards_holds_reads_that_folder_and_a_reload_keeps_it_open() => AppHost.Run(() =>
    {
        Home(".claude-work", """{"cleanupPeriodDays":12}""");
        var (page, _) = Page();
        var card = Card(page, ".claude-work");
        Assert.False(card.Holds.Loaded);

        card.HoldsOpen = true;
        Assert.Equal("12 days.", card.Holds.RetentionSummary);

        page.Reload();
        Assert.True(Card(page, ".claude-work").HoldsOpen);
        Assert.False(Card(page, ".claude").HoldsOpen);
    });
}
```

Replace the whole body of `windows-dotnet/tests/Sanduhr.App.Tests/ClaudeCodeFolderChoiceTests.cs` with:

```csharp
using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Tests;

/// <summary>Which folder card a link to Claude Code, folder-config opens (slice 4, decision 5; the same order Mods &amp; Config's combo used).</summary>
[Collection("SettingsWindow")]
public sealed class ClaudeCodeFolderChoiceTests : AppWindowFixture
{
    private const string Off = """{"enabledPlugins":{"a@m":false}}""";
    private const string Many = """{"enabledPlugins":{"a@m":true,"b@m":true,"c@m":true}}""";

    public ClaudeCodeFolderChoiceTests() => ClaudeCodeViewModel.ForgetSessionChoice();

    private string Home(string name, string settings)
    {
        var dir = Path.Combine(Profile, name);
        Directory.CreateDirectory(Path.Combine(dir, "projects"));
        File.WriteAllText(Path.Combine(dir, "settings.json"), settings);
        return dir;
    }

    private static string Opened(ClaudeCodeViewModel page)
    {
        var card = page.OpenHoldsForLanding()!;
        Assert.True(card.HoldsOpen);
        Assert.Single(page.Folders, f => f.HoldsOpen);
        return card.Display;
    }

    [Fact]
    public void Opens_the_folder_CLAUDE_CONFIG_DIR_names_not_the_first() => AppHost.Run(() =>
    {
        File.WriteAllText(Path.Combine(Profile, ".claude", "settings.json"), Off);
        var personal = Home(".claude-personal", Many);
        Home(".claude-work", Many);
        ClaudeCodeProfile.ConfigDirOverride = personal;
        Assert.Equal(@"~\.claude-personal", Opened(new ClaudeCodeViewModel(ScratchWidget("Personal").Widget)));
    });

    [Fact]
    public void Without_the_variable_it_opens_the_active_accounts_folder() => AppHost.Run(() =>
    {
        File.WriteAllText(Path.Combine(Profile, ".claude", "settings.json"), Many);
        var work = Home(".claude-work", Off);
        var (widget, _) = ScratchWidget("Personal");
        widget.SetAccountFolder("Personal", work);
        Assert.Equal(@"~\.claude-work", Opened(new ClaudeCodeViewModel(widget)));
    });

    [Fact]
    public void With_neither_it_opens_the_folder_with_the_most_plugins_on() => AppHost.Run(() =>
    {
        File.WriteAllText(Path.Combine(Profile, ".claude", "settings.json"), Off);
        Home(".claude-personal", Many);
        Assert.Equal(@"~\.claude-personal", Opened(new ClaudeCodeViewModel(ScratchWidget("Personal").Widget)));
    });

    [Fact]
    public void With_nothing_on_anywhere_it_opens_the_first() => AppHost.Run(() =>
    {
        File.WriteAllText(Path.Combine(Profile, ".claude", "settings.json"), Off);
        Home(".claude-personal", Off);
        Assert.Equal(@"~\.claude", Opened(new ClaudeCodeViewModel(ScratchWidget("Personal").Widget)));
    });

    [Fact]
    public void The_folder_you_opened_last_wins_for_the_session_even_in_a_new_page() => AppHost.Run(() =>
    {
        var personal = Home(".claude-personal", Many);
        ClaudeCodeProfile.ConfigDirOverride = personal;
        var (widget, _) = ScratchWidget("Personal");
        new ClaudeCodeViewModel(widget).Folders.Single(f => f.Display == @"~\.claude").HoldsOpen = true;

        Assert.Equal(@"~\.claude", Opened(new ClaudeCodeViewModel(widget)));
        ClaudeCodeViewModel.ForgetSessionChoice();
        Assert.Equal(@"~\.claude-personal", Opened(new ClaudeCodeViewModel(widget)));
    });

    [Fact]
    public void No_folders_opens_nothing() => AppHost.Run(() =>
    {
        Directory.Delete(Path.Combine(Profile, ".claude"), true);
        Assert.Null(new ClaudeCodeViewModel(ScratchWidget("Personal").Widget).OpenHoldsForLanding());
    });
}
```

Create `windows-dotnet/tests/Sanduhr.Tests/SettingsModsIndexTests.cs`:

```csharp
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 4: the Claude Code cards and Mods &amp; Widgets in the index, the names table and the alias table.</summary>
public sealed class SettingsModsIndexTests
{
    [Fact]
    public void The_Claude_Code_page_holds_folders_their_report_and_the_glow()
    {
        Assert.Equal([SettingsIndex.Folders, SettingsIndex.FolderConfig, SettingsIndex.Glow, SettingsIndex.GlowWhere],
            SettingsIndex.EntriesOn(SettingsPage.ClaudeCode).Select(e => e.Anchor));
        Assert.Equal(SettingsNames.FolderHolds, SettingsIndex.Find(SettingsPage.ClaudeCode, SettingsIndex.FolderConfig)!.Title);
        Assert.Equal(SettingsNames.DeskGlow, SettingsIndex.Find(SettingsPage.ClaudeCode, SettingsIndex.Glow)!.Title);
        Assert.Equal(SettingsNames.FolderHolds, SettingsIndex.CollapsedSections[(SettingsPage.ClaudeCode, SettingsIndex.FolderConfig)]);
        Assert.Empty(SettingsIndex.JumpLinks(SettingsPage.ClaudeCode));   // two sections: no jump links (decision 19)
    }

    [Theory]
    [InlineData(SettingsPage.ClaudeCode, "statusline", SettingsPage.ClaudeCode, "folders")]
    [InlineData(SettingsPage.ClaudeCode, "mcp", SettingsPage.ClaudeCode, "folders")]
    [InlineData(SettingsPage.Mods, "config", SettingsPage.ClaudeCode, "folder-config")]
    [InlineData(SettingsPage.Mods, "mcpservers", SettingsPage.ClaudeCode, "folder-config")]
    [InlineData(SettingsPage.Mods, "claudemd", SettingsPage.ClaudeCode, "folder-config")]
    [InlineData(SettingsPage.Mods, "retention", SettingsPage.ClaudeCode, "folder-config")]
    public void Every_claude_code_and_config_alias_lands_on_a_card(SettingsPage from, string anchor, SettingsPage to, string toAnchor)
    {
        Assert.Null(SettingsIndex.Find(from, anchor));
        Assert.Equal(new SettingsLink(to, toAnchor), SettingsIndex.Resolve(from, anchor));
        Assert.Equal(new SettingsLink(to, toAnchor), SettingsIndex.ParseLink(SettingsIndex.PageId(from) + "#" + anchor));
    }

    [Theory]
    [InlineData("statusline", SettingsIndex.Folders)]
    [InlineData("MCP", SettingsIndex.Folders)]
    [InlineData("glow hook", SettingsIndex.Folders)]
    [InlineData("cleanupPeriodDays", SettingsIndex.FolderConfig)]
    [InlineData("CLAUDE.md", SettingsIndex.FolderConfig)]
    [InlineData("glow", SettingsIndex.Glow)]
    public void Claude_Code_words_land_on_the_card_or_the_glow(string query, string anchor)
    {
        var top = SettingsIndex.Search(query)[0];
        Assert.Equal((SettingsPage.ClaudeCode, anchor), (top.Entry.Page, top.Entry.Anchor));
    }

    [Fact]
    public void Names_inside_the_report_say_it_is_collapsed_and_the_hook_lives_on_the_cards()
    {
        Assert.Equal(SettingsIndex.FolderConfig, SettingsIndex.NameAnchors[SettingsNames.TranscriptsKept]);
        Assert.Contains((SettingsNames.TranscriptsKept, SettingsPage.ClaudeCode), SettingsNames.Table);
        var row = SettingsIndex.All.Single(e => e.Title == SettingsNames.TranscriptsKept);
        Assert.Equal(SettingsNames.FolderHolds, row.CollapsedIn);
        Assert.Equal(SettingsIndex.Folders, SettingsIndex.NameAnchors[SettingsNames.ClaudeCodeGlowHook]);
    }

    [Fact]
    public void The_tour_and_the_statusline_card_still_land()
    {
        var card = WhatsNewCards.Table.Single(c => c.Id == "statusline-label");
        Assert.Equal(new SettingsLink(SettingsPage.ClaudeCode, SettingsIndex.Folders), SettingsIndex.Check(card.Destination, card.Anchor));
    }
}
```

In `windows-dotnet/tests/Sanduhr.Tests/SettingsIndexTests.cs`: in `Synonyms_and_words_resolve_to_the_expected_top_hit`, change the rows `("statusline", ClaudeCode, Statusline)` and `("MCP", ClaudeCode, Mcp)` to `SettingsIndex.Folders`, and `("cleanupPeriodDays", SettingsPage.Mods, SettingsIndex.Retention)` to `(SettingsPage.ClaudeCode, SettingsIndex.FolderConfig)`. In `Statusline_and_MCP_left_Claude_Usage_for_the_Claude_Code_page`, replace the two `Assert.NotNull(SettingsIndex.Find(SettingsPage.ClaudeCode, …))` lines with `Assert.Equal(new SettingsLink(SettingsPage.ClaudeCode, SettingsIndex.Folders), SettingsIndex.Resolve(SettingsPage.ClaudeCode, SettingsIndex.Statusline));`.

In `windows-dotnet/tests/Sanduhr.App.Tests/SettingsWindowNamesTests.cs`, `NoPageReadsAsATypeNameOrARecord`: before the tab loop, add `svm.ClaudeCodePage.Folders.Single(f => f.Display == @"~\.claude-personal").HoldsOpen = true;`, and change the two asserts `Assert.Contains("Notification, 1 group", seen["Mods"]);` and `Assert.Contains("alpha@m, On", seen["Mods"]);` to read `seen["ClaudeCode"]`.

Create `windows-dotnet/tests/Sanduhr.App.Tests/ModsAndCardsPagesTests.cs` (window-level tests for this slice; Tasks 12 to 14 append to it):

```csharp
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>The Claude Code cards and Mods &amp; Widgets in a real Settings window, shown off screen and never activated (slice 4).</summary>
[Collection("SettingsWindow")]
public sealed class ModsAndCardsPagesTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Vm, NoActivate Guard) Open(params string[] accounts)
    {
        var (widget, _) = ScratchWidget(accounts);
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        return (window, svm, guard);
    }

    private static void Settle(Window window, NoActivate guard)
    {
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

    private string Home(string name, string settings = "{}")
    {
        var dir = Path.Combine(Profile, name);
        Directory.CreateDirectory(Path.Combine(dir, "projects"));
        File.WriteAllText(Path.Combine(dir, "settings.json"), settings);
        return dir;
    }

    [Fact]
    public void A_link_to_folder_config_opens_the_chosen_cards_report_in_view() => AppHost.Run(() =>
    {
        Home(".claude-personal", """{"enabledPlugins":{"a@m":true,"b@m":true}}""");
        Home(".claude-work");
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Mods, SettingsIndex.Retention);   // an old Mods & Config link
            Settle(window, guard);

            Assert.Equal(new SettingsLink(SettingsPage.ClaudeCode, SettingsIndex.FolderConfig), window.Place);
            var open = Assert.Single(svm.ClaudeCodePage.Folders, f => f.HoldsOpen);
            Assert.Equal(@"~\.claude-personal", open.Display);
            var expander = Below<Expander>(window).Single(e => e.IsExpanded && e.DataContext == open);
            Assert.True(expander.IsVisible, "the opened report is not shown");
            Assert.NotEqual("", open.Holds.SettingsSummary);
            Assert.Contains(Below<TextBlock>(expander), t => t.Text.Contains(open.Holds.SettingsSummary, StringComparison.Ordinal));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_Claude_Code_page_has_no_install_combo_and_each_card_shows_its_pieces() => AppHost.Run(() =>
    {
        Home(".claude-work");
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.ClaudeCode);
            Settle(window, guard);
            var names = Below<FrameworkElement>(window).Select(System.Windows.Automation.AutomationProperties.GetName).ToList();
            Assert.DoesNotContain("Folder to install into", names);
            Assert.Contains(@"Install Statusline", names);
            Assert.Equal(2, names.Count(n => n == "Install Statusline"));   // one per card, each named for its piece
            Assert.Contains(names, n => n.StartsWith(SettingsNames.FolderHolds + " ") && n.EndsWith(@"~\.claude-work"));
        }
        finally { window.Close(); }
    });
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsModsIndexTests|FullyQualifiedName~SettingsIndexTests"`
Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~ClaudeCodeCardsTests|FullyQualifiedName~ClaudeCodeFolderChoiceTests|FullyQualifiedName~ModsAndCardsPagesTests"`
Expected: build FAILS with `'SettingsIndex' does not contain a definition for 'FolderConfig'` and `'CcFolderRowViewModel' does not contain a definition for 'Statusline'`.

- [ ] **Step 3: The cards hold their pieces**

In `windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs`:

Replace the whole `CcFolderRowViewModel` class (from its summary to the end of the class) with:

```csharp
/// <summary>One Claude Code folder card (Settings v2 slice 4): its path, the linked account, the MCP read switch, Sanduhr's pieces in this
/// folder, the meters' state here and the collapsed "What this folder holds". Every action on the card acts on this folder.</summary>
public sealed partial class CcFolderRowViewModel : ObservableObject
{
    private readonly ClaudeCodeViewModel _owner;
    private bool _loading = true;
    private FolderHoldsViewModel? _holds;

    internal CcFolder Folder { get; }
    public string Path => Folder.Path;
    public string Name => Folder.Name;
    public bool Discovered => Folder.Discovered;
    public bool IsAdded => !Folder.Discovered;

    /// <summary>The path with the profile folder written as ~.</summary>
    public string Display { get; }

    [ObservableProperty] private string _linkedAccount = ClaudeCodeViewModel.NoAccount;
    [ObservableProperty] private bool _mcpReadable;
    /// <summary>The meters above the prompt in this folder: On, Off, Not installed or Can't be read. A status line, never a control (WF9).</summary>
    [ObservableProperty] private string _metersText = "";
    /// <summary>"What this folder holds" is open: the folder is read then.</summary>
    [ObservableProperty] private bool _holdsOpen;

    internal CcFolderRowViewModel(ClaudeCodeViewModel owner, CcFolder folder, string display, string linked, bool mcpReadable, bool hasAccounts)
    {
        _owner = owner;
        Folder = folder;
        Display = display;
        _linkedAccount = linked;
        _mcpReadable = mcpReadable;
        HasAccounts = hasAccounts;
        Statusline = owner.NewPiece(CcPieceKind.Statusline, this);
        Mcp = owner.NewPiece(CcPieceKind.Mcp, this);
        Hooks = owner.NewPiece(CcPieceKind.Hooks, this);
        _loading = false;
    }

    public CcPieceViewModel Statusline { get; }
    public CcPieceViewModel Mcp { get; }
    public CcPieceViewModel Hooks { get; }

    /// <summary>The folder's report, made the first time it is asked for.</summary>
    public FolderHoldsViewModel Holds => _holds ??= _owner.NewHolds(Path);

    /// <summary>An account is signed in to link; otherwise the card says how to get one (WF32).</summary>
    public bool HasAccounts { get; }
    public bool NoAccounts => !HasAccounts;

    partial void OnLinkedAccountChanged(string value)
    {
        // A combo whose selected item leaves its list writes null back. That is not a choice: "None" is the
        // choice that unlinks.
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!_loading) _owner.OnLinkChanged(this, value);
    }

    partial void OnMcpReadableChanged(bool value)
    {
        if (!_loading) _owner.OnMcpReadableChanged(this, value);
    }

    partial void OnHoldsOpenChanged(bool value)
    {
        if (!value) return;
        Holds.EnsureLoaded();
        if (!_loading) _owner.RememberHolds(this);
    }

    internal void SetLinkedQuietly(string value)
    {
        _loading = true;
        LinkedAccount = value;
        _loading = false;
    }

    internal void OpenHoldsQuietly()
    {
        _loading = true;
        HoldsOpen = true;
        _loading = false;
    }

    public string AutomationName => $"Folder {Display}";

    // Each card's controls are named for its folder, so a screen reader tells one card's from another's.
    public string LinkedAccountName => $"Linked account for {Display}";
    public string ForgetName => SettingsNames.ForgetFolderFor(Display);
    public string McpReadableName => SettingsNames.LetClaudeReadTotalsFor(Display);
    public string HoldsName => $"{SettingsNames.FolderHolds} {Display}";
    public string MetersLine => SettingsNames.MetersSection + ":";
    public string MetersLink => SettingsIndex.PageId(SettingsPage.Mods) + "#meters";
    public string MetersLinkTitle => SettingsNames.LinkTitle(SettingsPage.Mods);
    public string MetersLinkName => $"{SettingsNames.LinkTitle(SettingsPage.Mods)} {SettingsNames.MetersSection} in {Display}";

    public override string ToString() => AutomationName;

    /// <summary>The account choices (the page's list), bound directly so the combo never depends on a RelativeSource walk to the page.</summary>
    public ObservableCollection<string> AccountOptions => _owner.AccountOptions;
    public bool LinkEnabled => _owner.LinkEnabled;
    public System.Windows.Input.ICommand ForgetCommand => _owner.ForgetFolderCommand;
}

/// <summary>The three pieces a folder card installs (the meters are Mods &amp; Widgets' since slice 4).</summary>
internal enum CcPieceKind { Statusline, Mcp, Hooks }
```

In `ClaudeCodeViewModel`:

- Change the class summary's first sentence to `The Claude Code page (Settings v2 slice 4): one card per folder Sanduhr knows (found, then added by hand), each holding the account linked to it, the MCP read switch, Sanduhr's pieces in that folder (statusline, MCP server, Claude Code glow hook), the meters' state there and "What this folder holds", plus the Claude Code glow switches.` (keep the `statusline_enabled` paragraph).
- Replace

```csharp
    /// <summary>The folder picked in the install combo earlier in the session. The Settings window is rebuilt each time
    /// it opens; this is what keeps the choice.</summary>
    private static string? s_sessionFolder;

    internal static void ForgetSessionChoice() => s_sessionFolder = null;
```

with

```csharp
    /// <summary>The folder whose "What this folder holds" was opened last this session: a link to folder-config opens it first (decision 5).
    /// The Settings window is rebuilt each time it opens; this is what keeps it.</summary>
    private static string? s_holdsFolder;

    internal static void ForgetSessionChoice() => s_holdsFolder = null;

    private readonly ClaudeCodeHost _host;
```

- Delete `public ObservableCollection<CcFolderRowViewModel> FolderChoices => Folders;`, the four `public CcPieceViewModel Statusline/Mcp/Meters/Hooks { get; }` lines and `[ObservableProperty] private CcFolderRowViewModel? _selectedFolder;`; add after `_glowSkipTerminal`:

```csharp
    /// <summary>The Claude Code glow section's line: which folders have the hook (WF11).</summary>
    [ObservableProperty] private string _glowHookStatus = "";

    /// <summary>Asks before a write to a folder (the consent dialog). A seam for tests.</summary>
    internal Func<ConsentSpec, ConsentChoice?> AskConsent { get; set; }

    /// <summary>A link landed on folder-config and opened this card's report: the page brings the card into view.</summary>
    public event Action<CcFolderRowViewModel>? HoldsLanded;
```

- Change the summaries of `FoldersChanged` and `PiecesChanged` to `Raised after folders were added or forgotten, so Mods &amp; Widgets follows.` and `Raised after an install or remove, so Mods &amp; Widgets reads the folders again.`
- In the constructor, replace the four `… = new CcPieceViewModel(…);` statements with:

```csharp
        _host = ClaudeCodeHost.For(widget);
        AskConsent = spec => IntegrationConsentDialog.Ask(_owner, spec);
```

- Replace `MakeIntegrations()` with `private ClaudeCodeIntegrations MakeIntegrations() => _host.Integrations();`, and `KnownFolders()` with `internal IReadOnlyList<CcFolder> KnownFolders() => _host.Folders();`.
- Add, after `KnownFolders()`:

```csharp
    /// <summary>A card's piece, its buttons acting on that card's folder.</summary>
    internal CcPieceViewModel NewPiece(CcPieceKind kind, CcFolderRowViewModel row) => kind switch
    {
        CcPieceKind.Statusline => new CcPieceViewModel("Statusline",
            "Shows your session and weekly meters under the Claude Code prompt.",
            () => InstallStatusline(row), () => RemoveStatusline(row), "Change…",
            "Asks first, then lets you replace or adjust the statusline in this folder."),
        CcPieceKind.Mcp => new CcPieceViewModel("MCP server",
            "Lets Claude Code ask for your live headroom before it spawns agents. Read-only.",
            () => InstallMcp(row), () => RemoveMcp(row), "Update…",
            "Asks first, then copies the current server files again and refreshes Sanduhr's entry in this folder's .claude.json."),
        _ => new CcPieceViewModel(SettingsNames.ClaudeCodeGlowHook,
            "Two hooks that light the edge of your window when Claude Code waits on you or finishes.",
            () => InstallHooks(row), () => RemoveHooks(row), "Update…",
            "Asks first, then writes Sanduhr's two hooks into this folder's settings.json again."),
    };

    /// <summary>A card's "What this folder holds".</summary>
    internal FolderHoldsViewModel NewHolds(string folder) => new(_host, folder);

    /// <summary>The person opened a card's report: a later link to folder-config opens that folder first.</summary>
    internal void RememberHolds(CcFolderRowViewModel row) => s_holdsFolder = row.Path;

    /// <summary>
    /// A link landed on folder-config: opens the report of the card <see cref="ModsFolderChoice.Pick"/> chooses (the one opened last this
    /// session, then CLAUDE_CONFIG_DIR, the active account's, the most plugins on, the first) and returns it; null with no folders.
    /// </summary>
    internal CcFolderRowViewModel? OpenHoldsForLanding()
    {
        string? active = null;
        try { active = _widget.ShownActiveAccount is { } label ? _widget.AccountFolder(label) : null; } catch (Exception) { }
        var pick = ModsFolderChoice.Pick([.. Folders.Select(f => f.Path)], s_holdsFolder, ClaudeCodeProfile.ConfigDir, active,
            ModInventory.EnabledPluginCount);
        if (Folders.FirstOrDefault(f => pick is not null && ClaudeCodeFolders.Same(f.Path, pick)) is not { } row) return null;
        row.HoldsOpen = true;
        HoldsLanded?.Invoke(row);
        return row;
    }

    /// <summary>The meters were switched, installed, updated or removed on Mods &amp; Widgets: the cards' lines follow, and the snapshot switch
    /// is recomputed (with a fresh snapshot after an install, as this page's installs do).</summary>
    public void AfterMetersChange(bool installed)
    {
        RefreshStatuses();
        SyncSnapshotConsumers(forceWrite: installed);
    }
```

- Replace `Reload()` and `OnSelectedFolderChanged` with:

```csharp
    /// <summary>Re-reads the folder list, the account choices and every card's status. A card whose report was open stays open.</summary>
    public void Reload()
    {
        var open = Folders.Where(f => f.HoldsOpen).Select(f => f.Path).ToList();
        _loading = true;
        SyncAccountOptions();
        LinkEnabled = !_widget.IsDemo;

        var roots = _widget.LoadMcpRoots();
        bool hasAccounts = AccountOptions.Count > 1;
        Folders.Clear();
        foreach (var f in KnownFolders())
        {
            var linked = _widget.ShownAccounts.FirstOrDefault(l =>
                _widget.AccountFolder(l) is { } p && ClaudeCodeFolders.Same(p, f.Path)) ?? NoAccount;
            var row = new CcFolderRowViewModel(this, f, _host.Display(f.Path), linked,
                f.Discovered && roots.GetValueOrDefault(f.Name), hasAccounts);
            if (open.Any(p => ClaudeCodeFolders.Same(p, f.Path))) row.OpenHoldsQuietly();
            Folders.Add(row);
        }
        HasFolders = Folders.Count > 0;
        _loading = false;
        RefreshStatuses();
        SyncSnapshotConsumers(forceWrite: false);
    }
```

- In `AddFolder`, delete the line `SelectedFolder = Folders.FirstOrDefault(f => ClaudeCodeFolders.Same(f.Path, path)) ?? SelectedFolder;`.
- Replace `RefreshStatuses()` with:

```csharp
    /// <summary>Recomputes every card's pieces and meters line, and the glow section's line.</summary>
    public void RefreshStatuses()
    {
        foreach (var row in Folders) RefreshRow(row);
        GlowHookStatus = SettingsNames.GlowHookIn([.. Folders.Where(f => f.Hooks.IsInstalled).Select(f => f.Display)]);
    }

    private void RefreshRow(CcFolderRowViewModel row)
    {
        try
        {
            var sl = MakeStatusline().Status(row.Path);
            row.Statusline.CanInstall = sl != StatuslineStatus.Unreadable;
            row.Statusline.IsInstalled = sl is StatuslineStatus.Installed or StatuslineStatus.Outdated;
            row.Statusline.StatusText = sl switch
            {
                StatuslineStatus.Installed => "Installed.",
                StatuslineStatus.Outdated => "Installed, but not the current version. Change it to update.",
                StatuslineStatus.Other => "This folder has another statusline. Install offers to replace it or add to it.",
                StatuslineStatus.Unreadable => "This folder's settings.json can't be read, so nothing is changed.",
                _ => "Not installed.",
            };

            if (row.Discovered)
            {
                bool registered = MakeMcp().IsRegistered(row.Name);
                row.Mcp.CanInstall = true;
                row.Mcp.IsInstalled = registered;
                row.Mcp.StatusText = registered ? "Installed." : "Not installed.";
            }
            else
            {
                row.Mcp.CanInstall = false;
                row.Mcp.IsInstalled = false;
                row.Mcp.StatusText = "Not available for folders added by hand.";
            }

            var integ = MakeIntegrations();
            var hooks = integ.Status(IntegrationKind.Hooks, row.Path);
            row.Hooks.IsInstalled = hooks is IntegrationStatus.Installed or IntegrationStatus.Outdated;
            row.Hooks.CanInstall = hooks != IntegrationStatus.Unreadable;
            row.Hooks.StatusText = StatusWords(hooks);

            var own = integ.OwnModStateOf(row.Path);
            var meters = integ.Status(IntegrationKind.Meters, row.Path);
            row.MetersText = !own.Readable || meters == IntegrationStatus.Unreadable ? SettingsNames.FolderUnreadable
                : !own.Listed ? SettingsNames.FolderNotInstalled
                : own.IsOn ? SettingsNames.FolderOn : SettingsNames.FolderOff;
        }
        catch (Exception)
        {
            StatusText = "Couldn't read this folder.";
        }
    }
```

- Change every install and remove to take its card. Their bodies stay as they are, with these edits: the signatures become `private void InstallStatusline(CcFolderRowViewModel row)`, `RemoveStatusline(CcFolderRowViewModel row)`, `InstallMcp(CcFolderRowViewModel row)`, `RemoveMcp(CcFolderRowViewModel row)`, `InstallHooks(CcFolderRowViewModel row)`, `RemoveHooks(CcFolderRowViewModel row)`; each first line `if (SelectedFolder is not { } row) return;` is deleted, and `InstallMcp`'s and `RemoveMcp`'s first line becomes `if (!row.Discovered) return;`; every `IntegrationConsentDialog.Ask(_owner, new ConsentSpec(…))` becomes `AskConsent(new ConsentSpec(…))`. Delete `InstallMeters` and `RemoveMeters` and their section comment.

- [ ] **Step 4: The page in its own file**

Create `windows-dotnet/src/Sanduhr.App/Views/ClaudeCodePage.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Views;

/// <summary>Settings, Claude Code (Settings v2 slice 4): one card per folder, and the Claude Code glow. DataContext: the ClaudeCodeViewModel.</summary>
public partial class ClaudeCodePage : UserControl
{
    private ClaudeCodeViewModel? _vm;

    public ClaudeCodePage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.HoldsLanded -= OnHoldsLanded;
            _vm = DataContext as ClaudeCodeViewModel;
            if (_vm is not null) _vm.HoldsLanded += OnHoldsLanded;
        };
    }

    /// <summary>A link to folder-config opened a card's report: after the window has brought the cards list into view, bring that card in.</summary>
    private void OnHoldsLanded(CcFolderRowViewModel row) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (Cards.ItemContainerGenerator.ContainerFromItem(row) is FrameworkElement card)
            {
                card.UpdateLayout();
                card.BringIntoView();
            }
        }));
}
```

Create `windows-dotnet/src/Sanduhr.App/Views/ClaudeCodePage.xaml`:

```xml
<UserControl x:Class="Sanduhr.App.Views.ClaudeCodePage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:views="clr-namespace:Sanduhr.App.Views"
             xmlns:vm="clr-namespace:Sanduhr.App.ViewModels"
             xmlns:core="clr-namespace:Sanduhr.Core;assembly=Sanduhr.Core"
             xmlns:conv="clr-namespace:Sanduhr.App.Converters"
             Focusable="False">
    <!-- Settings, Claude Code (Settings v2 slice 4): one card per folder, holding everything Sanduhr knows and installs for it (WF4, WF9,
         WF27), then the Claude Code glow (WF11). Every write goes through a consent dialog and only to that card's folder. Hosted in
         SettingsWindow, whose styles it finds with DynamicResource. DataContext: the ClaudeCodeViewModel. -->
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BoolVis" />
        <conv:StringToVisibilityConverter x:Key="StrVis" />

        <!-- One of a card's pieces: its name, what it does, its status, then Install…/Change…/Update… and Remove for this folder. -->
        <DataTemplate x:Key="PieceTemplate" DataType="{x:Type vm:CcPieceViewModel}">
            <Grid Margin="0,0,0,8">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <StackPanel>
                    <TextBlock Text="{Binding Title}" FontSize="12" FontWeight="SemiBold" Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                    <TextBlock Text="{Binding Description}" FontSize="10.5" TextWrapping="Wrap" Margin="0,2,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                    <TextBlock Text="{Binding StatusText}" FontSize="10.5" TextWrapping="Wrap" Margin="0,4,0,0"
                               AutomationProperties.LiveSetting="Polite" Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                </StackPanel>
                <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Top" Margin="12,0,0,0">
                    <Button Style="{DynamicResource FlatButton}" Content="{Binding InstallLabel}" Padding="10,5" FontSize="11"
                            Command="{Binding InstallCommand}" IsEnabled="{Binding CanInstall}"
                            Visibility="{Binding CanInstall, Converter={StaticResource BoolVis}}"
                            AutomationProperties.Name="{Binding InstallName}" AutomationProperties.HelpText="{Binding InstallHelp}" />
                    <Button Style="{DynamicResource FlatButton}" Content="Remove" Padding="10,5" FontSize="11"
                            Command="{Binding RemoveCommand}" AutomationProperties.Name="{Binding RemoveName}"
                            Visibility="{Binding IsInstalled, Converter={StaticResource BoolVis}}" />
                </StackPanel>
            </Grid>
        </DataTemplate>

        <Style x:Key="ReportLabel" TargetType="Run">
            <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.TextSecondary}" />
        </Style>
    </UserControl.Resources>

    <Border Background="{DynamicResource Sanduhr.Brush.Glass}" CornerRadius="8">
        <ScrollViewer VerticalScrollBarVisibility="Auto" Focusable="False">
            <ScrollViewer.Resources>
                <Style TargetType="ScrollBar" BasedOn="{StaticResource SlimThemeScrollBar}" />
            </ScrollViewer.Resources>
            <StackPanel x:Name="PageBody" Margin="20">
                <TextBlock Text="Claude Code" FontSize="14" FontWeight="SemiBold" AutomationProperties.HeadingLevel="Level1"
                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,4" />
                <TextBlock TextWrapping="Wrap" FontSize="11" Margin="0,0,0,14" Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                           Text="The folders Sanduhr found, and what Sanduhr adds to each. Nothing is written to a folder until you install a piece and confirm." />

                <!-- Folders, with Add folder… beside the heading (spec, Claude Code: Intro). -->
                <DockPanel views:SettingsAnchor.Id="folders" Margin="0,0,0,8" LastChildFill="True">
                    <Button DockPanel.Dock="Right" Style="{DynamicResource FlatButton}" Content="{x:Static core:SettingsNames.AddFolder}"
                            Command="{Binding AddFolderCommand}" AutomationProperties.Name="{x:Static core:SettingsNames.AddFolder}" />
                    <TextBlock Text="Folders" FontSize="13" FontWeight="SemiBold" VerticalAlignment="Center"
                               AutomationProperties.HeadingLevel="Level2" Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                </DockPanel>
                <TextBlock Text="No Claude Code folder found. Add one with Add folder…" Style="{DynamicResource EmptyLine}"
                           Visibility="{Binding HasNoFolders, Converter={StaticResource BoolVis}}" />

                <!-- folder-config marks the cards list: anchors are page-local, and a link to it opens one card's report (decision 5). -->
                <ItemsControl x:Name="Cards" ItemsSource="{Binding Folders}" Focusable="False" views:SettingsAnchor.Id="folder-config"
                              AutomationProperties.Name="Claude Code folders">
                    <ItemsControl.ItemContainerStyle>
                        <Style TargetType="ContentPresenter">
                            <Setter Property="AutomationProperties.Name" Value="{Binding AutomationName}" />
                        </Style>
                    </ItemsControl.ItemContainerStyle>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate DataType="{x:Type vm:CcFolderRowViewModel}">
                            <Border Background="{DynamicResource Sanduhr.Brush.Bg}" CornerRadius="6" Padding="12,10" Margin="0,0,0,10"
                                    BorderBrush="{DynamicResource Sanduhr.Brush.Border}" BorderThickness="1">
                                <StackPanel>
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="*" />
                                            <ColumnDefinition Width="Auto" />
                                        </Grid.ColumnDefinitions>
                                        <TextBlock Text="{Binding Display}" FontFamily="Cascadia Code, Consolas" FontSize="12"
                                                   TextWrapping="Wrap" VerticalAlignment="Center" AutomationProperties.HeadingLevel="Level3"
                                                   Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                        <Button Grid.Column="1" Style="{DynamicResource FlatButton}" Content="{x:Static core:SettingsNames.ForgetFolder}"
                                                Margin="8,0,0,0" Padding="10,4" FontSize="11"
                                                AutomationProperties.Name="{Binding ForgetName}" Command="{Binding ForgetCommand}" CommandParameter="{Binding}"
                                                Visibility="{Binding IsAdded, Converter={StaticResource BoolVis}}" />
                                    </Grid>

                                    <StackPanel Orientation="Horizontal" Margin="0,8,0,0" Visibility="{Binding HasAccounts, Converter={StaticResource BoolVis}}">
                                        <TextBlock Text="Linked account" FontSize="11.5" VerticalAlignment="Center" Margin="0,0,8,0"
                                                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                        <ComboBox Style="{DynamicResource ThemedComboBox}" Width="170" ItemsSource="{Binding AccountOptions}"
                                                  SelectedItem="{Binding LinkedAccount, Mode=TwoWay}" IsEnabled="{Binding LinkEnabled}"
                                                  AutomationProperties.Name="{Binding LinkedAccountName}" />
                                    </StackPanel>
                                    <TextBlock Text="{x:Static core:SettingsNames.AddAccountToLink}" FontSize="11" Margin="0,8,0,0"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                                               Visibility="{Binding NoAccounts, Converter={StaticResource BoolVis}}" />
                                    <TextBlock Text="{x:Static core:SettingsNames.LinkedAccountCaption}" FontSize="10.5" Margin="0,4,0,0"
                                               TextWrapping="Wrap" Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                                    <CheckBox Style="{DynamicResource ThemedCheckBox}" Margin="0,8,0,0" Content="{x:Static core:SettingsNames.LetClaudeReadTotals}"
                                              IsChecked="{Binding McpReadable, Mode=TwoWay}" AutomationProperties.Name="{Binding McpReadableName}"
                                              Visibility="{Binding Discovered, Converter={StaticResource BoolVis}}" />
                                    <TextBlock Text="Folders added by hand are never read by the MCP server." FontSize="10.5" Margin="0,8,0,0"
                                               TextWrapping="Wrap" Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                               Visibility="{Binding IsAdded, Converter={StaticResource BoolVis}}" />

                                    <!-- This folder's pieces (WF9: each acts on this card's folder). -->
                                    <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,10,0,10" />
                                    <ContentControl Content="{Binding Statusline}" ContentTemplate="{StaticResource PieceTemplate}" Focusable="False" />
                                    <ContentControl Content="{Binding Mcp}" ContentTemplate="{StaticResource PieceTemplate}" Focusable="False" />
                                    <ContentControl Content="{Binding Hooks}" ContentTemplate="{StaticResource PieceTemplate}" Focusable="False" />

                                    <!-- Meters above the prompt: a status line here, the controls on Mods & Widgets (WF9, the Mac's layout). -->
                                    <WrapPanel Margin="0,2,0,0">
                                        <TextBlock FontSize="11.5" VerticalAlignment="Center" Margin="0,0,8,0">
                                            <Run Text="{Binding MetersLine, Mode=OneWay}" Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                            <Run Text="{Binding MetersText, Mode=OneWay}" FontWeight="SemiBold" Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                        </TextBlock>
                                        <Button Style="{DynamicResource SettingsLinkButton}" Content="{Binding MetersLinkTitle}"
                                                views:SettingsLinkTo.Link="{Binding MetersLink}" AutomationProperties.Name="{Binding MetersLinkName}" />
                                    </WrapPanel>

                                    <!-- What this folder holds: Mods & Config's read-only report, collapsed, read when opened. -->
                                    <Expander Style="{DynamicResource SectionExpander}" Margin="0,8,0,0" Header="{x:Static core:SettingsNames.FolderHolds}"
                                              IsExpanded="{Binding HoldsOpen, Mode=TwoWay}"
                                              AutomationProperties.Name="{Binding HoldsName}"
                                              AutomationProperties.HelpText="{x:Static core:SettingsNames.FolderHoldsHelp}">
                                        <StackPanel DataContext="{Binding Holds}">
                                            <TextBlock FontSize="11.5" TextWrapping="Wrap">
                                                <Run Text="Settings: " Style="{StaticResource ReportLabel}" /><Run Text="{Binding SettingsSummary, Mode=OneWay}" FontWeight="SemiBold" Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                            </TextBlock>
                                            <TextBlock Text="{Binding SettingsDetail}" FontSize="10.5" TextWrapping="Wrap" Margin="0,2,0,0">
                                                <TextBlock.Style>
                                                    <Style TargetType="TextBlock">
                                                        <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.TextDim}" />
                                                        <Style.Triggers>
                                                            <DataTrigger Binding="{Binding SettingsUnreadable}" Value="True">
                                                                <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.PaceMarker}" />
                                                            </DataTrigger>
                                                        </Style.Triggers>
                                                    </Style>
                                                </TextBlock.Style>
                                            </TextBlock>
                                            <TextBlock FontSize="11.5" TextWrapping="Wrap" Margin="0,8,0,0">
                                                <Run Text="Statusline: " Style="{StaticResource ReportLabel}" /><Run Text="{Binding StatuslineSummary, Mode=OneWay}" FontWeight="SemiBold" Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                            </TextBlock>
                                            <TextBlock Text="{Binding StatuslineCommand}" FontFamily="Cascadia Code, Consolas" FontSize="10.5" TextWrapping="Wrap"
                                                       Margin="0,2,0,0" Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                                       Visibility="{Binding StatuslineCommand, Converter={StaticResource StrVis}}" />
                                            <TextBlock FontSize="11.5" TextWrapping="Wrap" Margin="0,8,0,0">
                                                <Run Text="Hooks: " Style="{StaticResource ReportLabel}" /><Run Text="{Binding HooksSummary, Mode=OneWay}" FontWeight="SemiBold" Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                            </TextBlock>
                                            <ItemsControl ItemsSource="{Binding HookLines}" Focusable="False" Margin="0,2,0,0" AutomationProperties.Name="Hooks">
                                                <ItemsControl.ItemContainerStyle>
                                                    <Style TargetType="ContentPresenter">
                                                        <Setter Property="AutomationProperties.Name" Value="{Binding AutomationName}" />
                                                    </Style>
                                                </ItemsControl.ItemContainerStyle>
                                                <ItemsControl.ItemTemplate>
                                                    <DataTemplate>
                                                        <TextBlock FontSize="10.5" TextWrapping="Wrap" Margin="12,1,0,0" Foreground="{DynamicResource Sanduhr.Brush.TextDim}">
                                                            <Run Text="{Binding Name, Mode=OneWay}" Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" /><Run Text=": " /><Run Text="{Binding Text, Mode=OneWay}" />
                                                        </TextBlock>
                                                    </DataTemplate>
                                                </ItemsControl.ItemTemplate>
                                            </ItemsControl>
                                            <TextBlock FontSize="11.5" TextWrapping="Wrap" Margin="0,8,0,0">
                                                <Run Text="Plugin switches: " Style="{StaticResource ReportLabel}" /><Run Text="{Binding PluginsSummary, Mode=OneWay}" FontWeight="SemiBold" Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                            </TextBlock>
                                            <ItemsControl ItemsSource="{Binding PluginLines}" Focusable="False" Margin="0,2,0,0" AutomationProperties.Name="Plugin switches">
                                                <ItemsControl.ItemContainerStyle>
                                                    <Style TargetType="ContentPresenter">
                                                        <Setter Property="AutomationProperties.Name" Value="{Binding AutomationName}" />
                                                    </Style>
                                                </ItemsControl.ItemContainerStyle>
                                                <ItemsControl.ItemTemplate>
                                                    <DataTemplate>
                                                        <TextBlock FontSize="10.5" TextWrapping="Wrap" Margin="12,1,0,0" Foreground="{DynamicResource Sanduhr.Brush.TextDim}">
                                                            <Run Text="{Binding Name, Mode=OneWay}" Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" /><Run Text=": " /><Run Text="{Binding Text, Mode=OneWay}" />
                                                        </TextBlock>
                                                    </DataTemplate>
                                                </ItemsControl.ItemTemplate>
                                            </ItemsControl>
                                            <TextBlock FontSize="11.5" TextWrapping="Wrap" Margin="0,8,0,0">
                                                <Run Text="MCP servers: " Style="{StaticResource ReportLabel}" /><Run Text="{Binding McpSummary, Mode=OneWay}" FontWeight="SemiBold" Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                            </TextBlock>
                                            <ItemsControl ItemsSource="{Binding McpNames}" Focusable="False" AutomationProperties.Name="MCP servers">
                                                <ItemsControl.ItemTemplate>
                                                    <DataTemplate>
                                                        <TextBlock Text="{Binding}" FontFamily="Cascadia Code, Consolas" FontSize="11" Margin="12,1,0,0"
                                                                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                                    </DataTemplate>
                                                </ItemsControl.ItemTemplate>
                                            </ItemsControl>
                                            <TextBlock Text="{Binding McpProjects}" FontSize="10.5" Margin="12,2,0,0" Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                                       Visibility="{Binding McpProjects, Converter={StaticResource StrVis}}" />
                                            <TextBlock FontSize="11.5" TextWrapping="Wrap" Margin="0,8,0,0">
                                                <Run Text="CLAUDE.md: " Style="{StaticResource ReportLabel}" /><Run Text="{Binding ClaudeMdSummary, Mode=OneWay}" FontWeight="SemiBold" Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                            </TextBlock>
                                            <TextBlock FontSize="11.5" TextWrapping="Wrap" Margin="0,8,0,0">
                                                <Run Text="{x:Static core:SettingsNames.TranscriptsKept}" Style="{StaticResource ReportLabel}" /><Run Text=" " /><Run Text="{Binding RetentionSummary, Mode=OneWay}" FontWeight="SemiBold">
                                                    <Run.Style>
                                                        <Style TargetType="Run">
                                                            <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.Text}" />
                                                            <Style.Triggers>
                                                                <DataTrigger Binding="{Binding RetentionWarning}" Value="True">
                                                                    <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.PaceMarker}" />
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </Run.Style>
                                                </Run>
                                            </TextBlock>
                                            <Button Style="{DynamicResource FlatButton}" Content="{x:Static core:SettingsNames.RefreshFolder}" HorizontalAlignment="Left"
                                                    Margin="0,10,0,0" Padding="12,5" Command="{Binding RefreshCommand}"
                                                    AutomationProperties.Name="{Binding RefreshName}" />
                                        </StackPanel>
                                    </Expander>
                                </StackPanel>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <!-- Claude Code glow (WF11): the section and the Desk piece share this name; the hook is installed from a card above. -->
                <StackPanel views:SettingsAnchor.Id="glow" Margin="0,8,0,0">
                    <TextBlock Text="{x:Static core:SettingsNames.DeskGlow}" FontSize="13" FontWeight="SemiBold" AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,4" />
                    <TextBlock Text="{x:Static core:SettingsNames.GlowNeedsHook}" FontSize="11" TextWrapping="Wrap"
                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                    <TextBlock Text="{Binding GlowHookStatus}" FontSize="11" TextWrapping="Wrap" Margin="0,2,0,10"
                               AutomationProperties.LiveSetting="Polite" Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                    <CheckBox Style="{DynamicResource ThemedCheckBox}" Margin="0,0,0,6" Content="Glow when Claude is waiting on you"
                              IsChecked="{Binding GlowWaiting, Mode=TwoWay}" />
                    <CheckBox Style="{DynamicResource ThemedCheckBox}" Margin="0,0,0,6" Content="Glow when Claude finishes"
                              IsChecked="{Binding GlowDone, Mode=TwoWay}" />
                    <CheckBox Style="{DynamicResource ThemedCheckBox}" Margin="0,0,0,8" Content="Not while a terminal or editor is in front"
                              IsChecked="{Binding GlowSkipTerminal, Mode=TwoWay}" />
                    <StackPanel Orientation="Horizontal">
                        <Button Style="{DynamicResource FlatButton}" Content="Try it: waiting" Padding="10,5" Command="{Binding TryWaitingCommand}"
                                AutomationProperties.Name="Try it: waiting" />
                        <Button Style="{DynamicResource FlatButton}" Content="Try it: done" Padding="10,5" Command="{Binding TryDoneCommand}"
                                AutomationProperties.Name="Try it: done" />
                    </StackPanel>
                    <StackPanel views:SettingsAnchor.Id="glow-where" Margin="0,12,0,0">
                        <TextBlock Text="{x:Static core:SettingsNames.GlowWhere}" FontSize="12" FontWeight="SemiBold" AutomationProperties.HeadingLevel="Level3"
                                   Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,6" />
                        <ComboBox Style="{DynamicResource ThemedComboBox}" Width="300" HorizontalAlignment="Left"
                                  ItemsSource="{x:Static vm:ClaudeCodeViewModel.GlowTargets}" SelectedItem="{Binding GlowTarget, Mode=TwoWay}"
                                  AutomationProperties.Name="{x:Static core:SettingsNames.GlowWhere}" />
                        <StackPanel Orientation="Horizontal" Margin="0,8,0,0" Visibility="{Binding GlowOnEdge, Converter={StaticResource BoolVis}}">
                            <ComboBox Style="{DynamicResource ThemedComboBox}" Width="110" ItemsSource="{x:Static vm:ClaudeCodeViewModel.GlowSides}"
                                      SelectedItem="{Binding GlowSide, Mode=TwoWay}" AutomationProperties.Name="Glow edge" />
                            <ComboBox Style="{DynamicResource ThemedComboBox}" Width="230" Margin="8,0,0,0" ItemsSource="{Binding GlowMonitors}"
                                      SelectedItem="{Binding GlowMonitor, Mode=TwoWay}" AutomationProperties.Name="Glow monitor" />
                        </StackPanel>
                        <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,8,0,0" Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                   Text="{x:Static core:GlowTargetSettings.DeskCaption}" Visibility="{Binding GlowOnDesk, Converter={StaticResource BoolVis}}" />
                        <Border Margin="0,6,0,0" Visibility="{Binding GlowOnDesk, Converter={StaticResource BoolVis}}">
                            <views:DeskPieceStatusPanel DataContext="{Binding DataContext.Desk.GlowPiece, RelativeSource={RelativeSource AncestorType=Window}}" />
                        </Border>
                        <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,6,0,0" Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                   Text="The taskbar button flashes in every choice. Only flash skips the light." />
                    </StackPanel>
                </StackPanel>

                <TextBlock TextWrapping="Wrap" FontSize="11" Margin="0,8,0,0" AutomationProperties.LiveSetting="Polite"
                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Text="{Binding StatusText}"
                           Visibility="{Binding StatusText, Converter={StaticResource StrVis}}" />
            </StackPanel>
        </ScrollViewer>
    </Border>
</UserControl>
```

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`:

- Replace the whole body of the Claude Code TabItem (from its `<Border …>` to the line before `</TabItem>`) with `<views:ClaudeCodePage />`, and its comment with `<!-- Settings, Claude Code (slice 4): one card per folder, drawn by its own control. -->`.
- In the Mods &amp; Config TabItem, delete the four sections marked `views:SettingsAnchor.Id="config"`, `"mcpservers"`, `"claudemd"` and `"retention"` (with their comments); the folder picker, the plugin list (`mods`) and the status line stay until the page flip (Task 12).
- Delete the `CcPieceTemplate` resource (its one user was the old Claude Code body).

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs`, `SelectPage`, right after `Tabs.SelectedItem = tab;` add:

```csharp
        // Claude Code, What this folder holds is one anchor on the page: a link to it opens one card's report (slice 4, decision 5). After the
        // selection, whose page reload rebuilds the cards.
        if (page == SettingsPage.ClaudeCode && entry?.Anchor == SettingsIndex.FolderConfig)
            ViewModel.ClaudeCodePage.OpenHoldsForLanding();
```

- [ ] **Step 5: The index and the names follow the cards**

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`:

- Under `// Claude Code`, add `public const string FolderConfig = "folder-config";` and keep `Statusline`, `Mcp`, `Meters` and the Mods constants (old cards, agent text and aliases name them).
- Replace the Claude Code and Mods &amp; Config rows of `Anchors` (from `E("Folders", SettingsPage.ClaudeCode, Folders,` through the `E(SettingsNames.TranscriptsKept, SettingsPage.Mods, Retention, …)` row) with:

```csharp
        E("Folders", SettingsPage.ClaudeCode, Folders,
            ["folder", "seat", "home", ".claude", "add folder", "forget folder", "linked account", "account folder", "CLAUDE_CONFIG_DIR",
             "statusline", "status line", "prompt", "install statusline", "remove statusline", "segments", "combine", "replace",
             "MCP", "MCP server", "sanduhr-mcp", "agents", "tools", "project totals", "quota tools", "burn attribution",
             "glow hook", SettingsNames.ClaudeCodeGlowHook, "hooks", "install", "update", "remove"]),
        E(SettingsNames.FolderHolds, SettingsPage.ClaudeCode, FolderConfig,
            ["settings.json", "readable", "unreadable", "statusline command", "hook groups", "plugin switches", "enabledPlugins",
             "MCP servers", "mcpServers", "CLAUDE.md", "memory", "instructions", "transcripts", "retention", "cleanupPeriodDays",
             "history kept", "refresh", "inventory", "config"]),
        E(SettingsNames.DeskGlow, SettingsPage.ClaudeCode, Glow,
            ["glow when Claude needs you", "glow", "waiting", "finishes", "edge light", "terminal", "try it"]),
        E(SettingsNames.GlowWhere, SettingsPage.ClaudeCode, GlowWhere,
            ["where the glow shows", "screen edge", "monitor", "flash", "taskbar flash", "glow target"]),

        E("Plugins", SettingsPage.Mods, ModList,
            ["mod", "mods", "plugin", "plugins", "installed", "sanduhr-meters", "switch", "on", "off", "update", "remove"]),
```

  (The existing `E(SettingsNames.GlowWhere, …)` row is the same as before; it moves into this block, so delete its old copy if it sits elsewhere.)
- In `NameAnchors`, change `[SettingsNames.ClaudeCodeGlowHook] = Glow,` to `[SettingsNames.ClaudeCodeGlowHook] = Folders,` and `[SettingsNames.TranscriptsKept] = Retention,` to `[SettingsNames.TranscriptsKept] = FolderConfig,`.
- In `CollapsedSections`, add `[(SettingsPage.ClaudeCode, FolderConfig)] = SettingsNames.FolderHolds,`.
- In `PageSynonyms`, change the Claude Code row to `["Claude Code folders", "folder", "seat", "pieces"]` (a page row that shares "statusline" or "MCP" with the Folders section ties it and wins, as the "vault" comment says) and the Mods row to `["mod", "plugin"]`.

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, `Table`: change `(TranscriptsKept, SettingsPage.Mods),` to `(TranscriptsKept, SettingsPage.ClaudeCode),`.

- [ ] **Step 6: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsModsIndexTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsReachIndexTests|FullyQualifiedName~SettingsLinkTests|FullyQualifiedName~WhatsNewTests|FullyQualifiedName~SettingsNamesTests"`
Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~ClaudeCodeCardsTests|FullyQualifiedName~ClaudeCodeFolderChoiceTests|FullyQualifiedName~ModsAndCardsPagesTests|FullyQualifiedName~ClaudeCodeLinkTests|FullyQualifiedName~CcPieceViewModelTests|FullyQualifiedName~SettingsWindowNamesTests|FullyQualifiedName~SettingsReachTests|FullyQualifiedName~DeskSplitPagesTests|FullyQualifiedName~ModsConfig"`
Expected: PASS, the `[DesktopFact]`s included (this is a desktop session). `Every_search_anchor_is_marked_in_its_pages_TabItem` finds `folder-config`, `glow` and `glow-where` in `ClaudeCodePage.xaml` through `<views:ClaudeCodePage`. A failure names what it found; fix it and run once more, never in a loop.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs windows-dotnet/src/Sanduhr.App/Views/ClaudeCodePage.xaml windows-dotnet/src/Sanduhr.App/Views/ClaudeCodePage.xaml.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.App.Tests/ClaudeCodeCardsTests.cs windows-dotnet/tests/Sanduhr.App.Tests/ClaudeCodeFolderChoiceTests.cs windows-dotnet/tests/Sanduhr.App.Tests/ModsAndCardsPagesTests.cs windows-dotnet/tests/Sanduhr.App.Tests/SettingsWindowNamesTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsModsIndexTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsIndexTests.cs
git commit -m "feat(claude-code): each folder card holds its pieces, its meters line and What this folder holds; the install combo goes"
```

---

### Task 9: Mods & Widgets, the Installed list: one row per item, switches for packs, read-only rows that say so

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/ModsPageViewModel.cs` (the installed row class, the constructor, the installed list, filters, the pack switch, Uninstall… asks)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/ModsPage.xaml` (the installed row template and the installed section's empty lines)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (the page gets its host)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (one constant, `NothingMatches`)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/ModsPageListTests.cs`; modify `ModsPageFixture.cs`, `WidgetCatalogServiceTests.cs` (the uninstall answers yes), `ModsPageNamesTests.cs` (same)

**Interfaces:**
- Consumes: `InstalledItems.Build`, `Matches`, `Where`, `ScopeSummary` (Tasks 3, 4), `ModsPageText.PlainNote`, `ModsPageText.RowName`, `WidgetStore.SetEnabled` (Task 1), `ClaudeCodeHost.ReadFolders` (Task 7), `ModsPageViewModel.Notify` (Task 6).
- Produces:
  - `public sealed partial class InstalledRowViewModel` (replaces `InstalledWidgetRowViewModel`): `Item`, `Id`, `Name`, `Description`, `HasDescription`, `Preview`, `HasPreview`, `IsPack`, `IsSanduhrMod`, `IsReadOnly`, `Detail`, `ScopeText`, `HasScope`, `ReadOnlyText`, `WhereText`, `WhereLink`, `WhereLinkTitle`, `WhereLinkName`, `HasWhere`, `HasWhereLink`, `MetersLink`, `MetersLinkTitle`, `[ObservableProperty] bool Enabled`, `SwitchName`, `ToggleCommand`, `CanUseOwnArt`, `UseOwnArtName`, `UseOwnArtCommand`, `UninstallName`, `UninstallCommand`, `RowName`; `internal void ShowEnabled(bool value)`
  - `ModsPageViewModel`: constructor `internal ModsPageViewModel(Func<WidgetCatalogService>? service = null, SettingsStore? store = null, ClaudeCodeHost? host = null)`; `ObservableCollection<InstalledRowViewModel> Installed`; `[ObservableProperty] InstalledFilter Filter`, `string FilterText`, `string Note`, `bool NothingMatches`; `bool IsFilterAll`, `IsFilterClaudeCode`, `IsFilterWidgets` (radio bindings); `public void ShowWidgetPacks()`; `internal void SwitchPack(string id, string name, bool on)`; `internal Func<string, string, bool> Confirm { get; set; }`; `public void AttachOwner(Window owner)`; `internal IReadOnlyList<FolderMods> Folders` (the last read)
  - `SettingsNames.NothingMatches`

- [ ] **Step 1: Write the failing tests**

Add to `windows-dotnet/tests/Sanduhr.App.Tests/ModsPageFixture.cs` (inside the class):

```csharp
    /// <summary>The scratch Sanduhr folders a page's widget store lives under (never the real LocalAppData).</summary>
    protected Sanduhr.Core.Paths Paths => new(Path.Combine(Root, "appdata"), Path.Combine(Root, "home"), Path.Combine(Root, "local"));

    /// <summary>A Mods &amp; Widgets page over the scratch profile and widgets folder; the network is <paramref name="net"/>, else offline.</summary>
    internal ModsPageViewModel Page(System.Net.Http.HttpMessageHandler? net = null, Version? running = null, ClaudeCodeHost? host = null) =>
        new(() => new Sanduhr.App.Services.WidgetCatalogService(Paths, net ?? new AppWindowFixture.OfflineHandler(), running ?? WidgetKit.V380),
            null, host ?? Host())
        {
            Confirm = (_, _) => true,
        };

    /// <summary>A plugin installed from marketplace "m" into <paramref name="home"/>, its files under the scratch cache.</summary>
    protected void Plugin(string home, string name, string version, bool mod = false)
    {
        var dir = Path.Combine(Root, "cache", name);
        Put(dir, ".claude-plugin/plugin.json", $$"""{"name":"{{name}}","version":"{{version}}"}""");
        if (mod) Put(dir, "hooks/hooks.json", """{"modules":["register.tsx"]}""");
        var record = Path.Combine(home, "plugins", "installed_plugins.json");
        var rows = File.Exists(record) ? File.ReadAllText(record)[..^2] + "," : "{\"version\":2,\"plugins\":{";
        File.WriteAllText(record, rows + $"\"{name}@m\":[{{\"installPath\":\"{dir.Replace("\\", "\\\\")}\",\"version\":\"{version}\"}}]}}}}");
    }
```

(`Plugin` keeps `installed_plugins.json` well formed while adding rows: it drops the closing `}}` and appends the new row with its own.)

Create `windows-dotnet/tests/Sanduhr.App.Tests/ModsPageListTests.cs`:

```csharp
using System.IO;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 4: Mods &amp; Widgets' Installed list (spec, Mods &amp; Widgets, section 2; WF3, WF39). Over a scratch profile and widgets
/// folder, never the real ones and never the network.
/// </summary>
public sealed class ModsPageListTests : ModsPageFixture
{
    private string MetersEntry => Path.Combine(Root, "external", "mods", "v0.3.0-20261025120000", "sanduhr-meters").Replace("\\", "\\\\");

    private ModsPageViewModel Seeded()
    {
        var personal = Home(".claude-personal", "{\"enabledPlugins\":{\"alpha@m\":true,\"beta-mod@m\":false}}");
        Plugin(personal, "alpha", "1.2.0");
        Plugin(personal, "beta-mod", "0.1.0", mod: true);
        Home(".claude-work", "{\"env\":{\"CLAUDE_CODE_PLUGIN_DIRS\":\"" + MetersEntry + "\"}}");
        Put(Path.Combine(Root, "external", "mods", "v0.3.0-20261025120000", "sanduhr-meters"), ".claude-plugin/plugin.json",
            """{"name":"sanduhr-meters","version":"0.3.0"}""");   // the entry's files are there, so it reads On, not Files missing
        InstalledPackKit.Install(Paths, "good-palette");
        InstalledPackKit.Install(Paths, "good-theme");
        var page = Page();
        page.ReloadLocal();
        return page;
    }

    [Fact]
    public void One_list_holds_Sanduhrs_mod_the_packs_and_every_folders_plugins()
    {
        var page = Seeded();
        Assert.Equal([SettingsNames.MetersSection, "Fixture palette", "Fixture theme", "alpha", "beta-mod"], page.Installed.Select(r => r.Name));
        Assert.True(page.HasInstalled);
    }

    [Fact]
    public void Rows_show_only_controls_that_can_act()
    {
        var page = Seeded();
        var palette = page.Installed.Single(r => r.Id == "good-palette");
        Assert.True(palette.IsPack);
        Assert.False(palette.IsReadOnly);
        Assert.True(palette.Enabled);

        var alpha = page.Installed.Single(r => r.Name == "alpha");
        Assert.True(alpha.IsReadOnly);
        Assert.False(alpha.IsPack);
        Assert.Equal(SettingsNames.ReadOnlyClaudeCode, alpha.ReadOnlyText);
        Assert.Equal("On in 1 of 2 folders", alpha.ScopeText);
        Assert.Equal("alpha, version 1.2.0, on in 1 of 2 folders, read-only", alpha.RowName);

        var meters = page.Installed.Single(r => r.IsSanduhrMod);
        Assert.False(meters.IsReadOnly);
        Assert.False(meters.IsPack);
        Assert.Equal("On in 1 of 2 folders", meters.ScopeText);
        Assert.Equal("mods#meters", meters.MetersLink);
        Assert.Equal(SettingsNames.MetersLinkUp, meters.MetersLinkTitle);
    }

    [Fact]
    public void A_pack_says_where_it_shows_and_links_there()
    {
        var page = Seeded();
        var palette = page.Installed.Single(r => r.Id == "good-palette");
        Assert.Equal((SettingsNames.ShowsInInk, "desklook#ink", "Desk Look settings…"), (palette.WhereText, palette.WhereLink, palette.WhereLinkTitle));
        Assert.StartsWith(palette.WhereLinkTitle, palette.WhereLinkName);
        Assert.True(palette.HasWhereLink);
        Assert.False(page.Installed.Single(r => r.Name == "alpha").HasWhere);
    }

    [Fact]
    public void Switching_a_pack_writes_its_flag_shows_the_truth_and_tells_the_pickers()
    {
        var page = Seeded();
        int told = 0;
        page.SettingsChanged = () => told++;
        var palette = page.Installed.Single(r => r.Id == "good-palette");

        palette.ToggleCommand.Execute(null);

        Assert.False(InstalledPackKit.Store(Paths).Load().Find("good-palette")!.Enabled);
        Assert.False(palette.Enabled);
        Assert.EndsWith(", off", palette.RowName);
        Assert.Equal("Fixture palette is off.", page.Status);
        Assert.Equal(1, told);

        palette.ToggleCommand.Execute(null);
        Assert.True(InstalledPackKit.Store(Paths).Load().Find("good-palette")!.Enabled);
        Assert.Equal(2, told);
    }

    [Fact]
    public void A_switch_that_cant_write_says_why_and_shows_what_is_on_disk()
    {
        var page = Seeded();
        var palette = page.Installed.Single(r => r.Id == "good-palette");
        Assert.True(InstalledPackKit.Store(Paths).Uninstall("good-palette").Ok);   // gone behind the page's back

        palette.ToggleCommand.Execute(null);

        Assert.Equal("That widget isn't installed.", page.Status);
        Assert.True(palette.Enabled);   // unchanged: nothing was written
    }

    [Fact]
    public void The_chips_and_the_text_filter_narrow_the_list()
    {
        var page = Seeded();
        page.IsFilterClaudeCode = true;
        Assert.Equal(InstalledFilter.ClaudeCode, page.Filter);
        Assert.Equal([SettingsNames.MetersSection, "alpha", "beta-mod"], page.Installed.Select(r => r.Name));

        page.IsFilterWidgets = true;
        Assert.Equal(["Fixture palette", "Fixture theme"], page.Installed.Select(r => r.Name));

        page.FilterText = "theme";
        Assert.Equal(["Fixture theme"], page.Installed.Select(r => r.Name));

        page.FilterText = "nothing like it";
        Assert.Empty(page.Installed);
        Assert.True(page.NothingMatches);

        page.ShowWidgetPacks();
        Assert.Equal(("", InstalledFilter.Widgets), (page.FilterText, page.Filter));
        Assert.False(page.NothingMatches);
    }

    [Fact]
    public void Uninstall_asks_first_and_a_no_keeps_the_pack()
    {
        var page = Seeded();
        var asked = new List<string>();
        page.Confirm = (question, _) => { asked.Add(question); return false; };

        page.Installed.Single(r => r.Id == "good-theme").UninstallCommand.Execute(null);

        Assert.Equal(["Uninstall Fixture theme?"], asked);
        Assert.NotNull(InstalledPackKit.Store(Paths).Load().Find("good-theme"));

        page.Confirm = (_, _) => true;
        page.Installed.Single(r => r.Id == "good-theme").UninstallCommand.Execute(null);
        Assert.Null(InstalledPackKit.Store(Paths).Load().Find("good-theme"));
        Assert.DoesNotContain(page.Installed, r => r.Id == "good-theme");
    }

    [Fact]
    public void The_note_counts_plugins_without_a_switch_or_files_in_plain_words()
    {
        var personal = Home(".claude", "{\"enabledPlugins\":{\"ghost@m\":true}}");
        Plugin(personal, "alpha", "1.0.0");   // installed, never switched
        var page = Page();
        page.ReloadLocal();
        Assert.Equal(SettingsNames.NoSwitchNote(1) + " " + SettingsNames.NoFilesNote(1), page.Note);
    }

    [Fact]
    public void A_page_without_a_host_lists_the_packs_only()
    {
        InstalledPackKit.Install(Paths, "good-palette");
        var page = new ModsPageViewModel(() => new Sanduhr.App.Services.WidgetCatalogService(Paths, new AppWindowFixture.OfflineHandler()));
        page.ReloadLocal();
        Assert.Equal(["Fixture palette"], page.Installed.Select(r => r.Name));
        Assert.Equal("", page.Note);
    }
}
```

In `windows-dotnet/tests/Sanduhr.App.Tests/WidgetCatalogServiceTests.cs`, in `Install_update_and_uninstall_from_the_page_and_a_bad_update_leaves_the_old_version`, add `page.Confirm = (_, _) => true;` right after the page is created. In `ModsPageNamesTests.Each_widget_row_names_its_button_for_its_pack`, after `ModsPage = new ModsPageViewModel(() => service),` the object initializer gets `Confirm = (_, _) => true,`.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~ModsPageListTests`
Expected: build FAILS with `'ModsPageViewModel' does not contain a definition for 'Confirm'` (and `InstalledRowViewModel` not found).

- [ ] **Step 3: The row**

In `windows-dotnet/src/Sanduhr.App/ViewModels/ModsPageViewModel.cs`, add `using System.Windows;` and `using Sanduhr.App.Views;` (for `ThemedDialog` and, in Task 10, the consent dialog), and replace the whole `InstalledWidgetRowViewModel` class with:

```csharp
/// <summary>
/// One row of Mods &amp; Widgets' Installed list (Settings v2 slice 4): a widget pack with its on switch and where it shows, Sanduhr's meters
/// mod with its scope and a link up to its section, or another Claude Code plugin or mod, read-only, with its scope. No row shows a control
/// that can't act (Principle 5).
/// </summary>
public sealed partial class InstalledRowViewModel : ObservableObject
{
    private readonly ModsPageViewModel _owner;

    internal InstalledRowViewModel(ModsPageViewModel owner, InstalledItem item, string? previewPath)
    {
        _owner = owner;
        Item = item;
        _enabled = item.Enabled;
        Preview = item.Kind == InstalledKind.WidgetPack ? ModsPageViewModel.LoadPreview(previewPath) : null;
        var where = InstalledItems.Where(item.Kind == InstalledKind.WidgetPack ? item.PackKind : null);
        WhereText = where.Text;
        WhereLink = where.Link;
        WhereLinkTitle = where.LinkTitle ?? "";
    }

    public InstalledItem Item { get; }
    public string Id => Item.Id;
    public string Name => Item.Name;
    public string Description => Item.Description;
    public bool HasDescription => Description.Length > 0;
    public ImageSource? Preview { get; }
    public bool HasPreview => Preview is not null;

    public bool IsPack => Item.Kind == InstalledKind.WidgetPack;
    public bool IsSanduhrMod => Item.Kind == InstalledKind.SanduhrIntegration;
    public bool IsReadOnly => !Item.CanSwitch;

    /// <summary>"palette · 1.0.0 · from the catalog"; "Claude Code mod · 0.1.0 · m".</summary>
    public string Detail => string.Join(" · ", new[] { KindWord, Item.Version ?? "", Item.Source }.Where(s => s.Length > 0));

    private string KindWord => Item.Kind switch
    {
        InstalledKind.WidgetPack => Item.PackKind ?? "",
        InstalledKind.SanduhrIntegration => "Sanduhr's mod",
        InstalledKind.ClaudeCodeMod => "Claude Code mod",
        _ => "Claude Code plugin",
    };

    /// <summary>"On in 2 of 3 folders" for a Claude Code item; nothing for an app-wide pack.</summary>
    public string ScopeText => InstalledItems.ScopeSummary(Item);
    public bool HasScope => ScopeText.Length > 0;
    public string ReadOnlyText => Item.ReadOnlyReason ?? "";

    /// <summary>Where a pack shows (WF3), and "&lt;Page&gt; settings…" to it.</summary>
    public string WhereText { get; }
    public string? WhereLink { get; }
    public string WhereLinkTitle { get; }
    public bool HasWhere => IsPack && WhereText.Length > 0;
    public bool HasWhereLink => IsPack && WhereLink is not null;
    public string WhereLinkName => $"{WhereLinkTitle} for {Name}";

    /// <summary>Sanduhr's meters mod is switched per folder in its own section, above the list.</summary>
    public string MetersLink => SettingsIndex.PageId(SettingsPage.Mods) + "#" + SettingsIndex.Meters;
    public string MetersLinkTitle => SettingsNames.MetersLinkUp;

    /// <summary>The pack's switch, bound one way: the command writes, then the row shows what is on disk (decision 7).</summary>
    [ObservableProperty] private bool _enabled;
    public string SwitchName => SettingsNames.PackOnFor(Name);

    internal void ShowEnabled(bool value)
    {
        _enabled = value;
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(RowName));
    }

    [RelayCommand]
    private void Toggle() => _owner.SwitchPack(Id, Name, !Enabled);

    /// <summary>A sprite made here (Claude's or the user's own) takes the user's own PNG sheets (spec 3.8 section 6); a catalog pack never does.</summary>
    public bool CanUseOwnArt => Item.MadeHere && Item.PackKind == "sprite";
    public string UseOwnArtName => $"Use my own art for {Name}";

    [RelayCommand]
    private Task UseOwnArt() => _owner.UseOwnArtAsync(Id, Name);

    public string UninstallName => $"Uninstall {Name}";

    [RelayCommand]
    private Task Uninstall() => _owner.UninstallAsync(Id, Name);

    /// <summary>The row read as one line: a pack's name, kind, version, source and "on" or "off"; a Claude Code item's name, version, scope and
    /// mod tag, then "read-only" when Sanduhr can't switch it.</summary>
    public string RowName => IsPack
        ? string.Join(", ", new[] { Name, Item.PackKind ?? "", "version " + Item.Version, Item.Source, Enabled ? "on" : "off" }
            .Where(s => s.Length > 0))
        : ModsPageText.RowName(Name, Item.Version, ScopeText, Item.IsMod && !IsSanduhrMod,
              hasFiles: Item.Scope?.Any(s => s.State is FolderState.On or FolderState.Off) == true)
          + (IsReadOnly ? ", read-only" : "");

    public override string ToString() => RowName;
}
```

- [ ] **Step 4: The list, its filters, the switch and the question**

In `ModsPageViewModel`:

- Change the constructor to:

```csharp
    private readonly ClaudeCodeHost? _host;
    private IReadOnlyList<InstalledItem> _items = [];
    private Window? _window;

    /// <param name="host">The Claude Code folders the list reads; null lists the widget packs only (tests of the catalog and the review).</param>
    internal ModsPageViewModel(Func<WidgetCatalogService>? service = null, SettingsStore? store = null, ClaudeCodeHost? host = null)
    {
        _service = new Lazy<WidgetCatalogService>(service ?? (() => new WidgetCatalogService()));
        _host = host;
        Confirm = (question, detail) => ThemedDialog.Show(_window, question, detail, MessageBoxButton.YesNo, ThemedDialogKind.Warning,
            primaryLabel: SettingsNames.UninstallConfirm, secondaryLabel: SettingsNames.UninstallCancel) == MessageBoxResult.Yes;
        // settings.json is not read here; the store parameter stays for callers.
    }

    /// <summary>Dialogs parent to the Settings window.</summary>
    public void AttachOwner(Window owner) => _window = owner;

    /// <summary>Asks before Uninstall… (decision 17): the question and its detail in, yes or no out. A seam for tests.</summary>
    internal Func<string, string, bool> Confirm { get; set; }

    /// <summary>The Claude Code folders as last read (the meters section reads them too).</summary>
    internal IReadOnlyList<FolderMods> Folders { get; private set; } = [];
```

- Replace `public ObservableCollection<InstalledWidgetRowViewModel> Installed { get; } = [];` with:

```csharp
    /// <summary>The Installed list as the filters show it (spec, Installed: "All", "Claude Code", "Widgets", and a text filter).</summary>
    public ObservableCollection<InstalledRowViewModel> Installed { get; } = [];

    [ObservableProperty] private InstalledFilter _filter = InstalledFilter.All;
    [ObservableProperty] private string _filterText = "";
    /// <summary>The plugin-count note in plain words, summed over the folders; "" when there is nothing to explain.</summary>
    [ObservableProperty] private string _note = "";
    /// <summary>Something is installed, but the filters hide all of it.</summary>
    [ObservableProperty] private bool _nothingMatches;

    public bool IsFilterAll { get => Filter == InstalledFilter.All; set { if (value) Filter = InstalledFilter.All; } }
    public bool IsFilterClaudeCode { get => Filter == InstalledFilter.ClaudeCode; set { if (value) Filter = InstalledFilter.ClaudeCode; } }
    public bool IsFilterWidgets { get => Filter == InstalledFilter.Widgets; set { if (value) Filter = InstalledFilter.Widgets; } }

    partial void OnFilterChanged(InstalledFilter value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterClaudeCode));
        OnPropertyChanged(nameof(IsFilterWidgets));
        ApplyFilter();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    /// <summary>Themes' "Mods &amp; Widgets settings…" lands on the list showing widget packs (spec, Menus).</summary>
    public void ShowWidgetPacks()
    {
        FilterText = "";
        Filter = InstalledFilter.Widgets;
    }
```

- Replace `ReloadInstalled()` with:

```csharp
    /// <summary>Reads every Claude Code folder, the installed packs and the catalog document already fetched (no fetch), and rebuilds the list.</summary>
    private void ReloadInstalled()
    {
        WidgetRegistry reg;
        try { reg = Service.Store.Load(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { reg = WidgetRegistry.Empty; }
        Folders = _host?.ReadFolders() ?? [];
        _items = InstalledItems.Build(Folders, reg, id => Service.Store.ReadInfo(id), Service.Client.Current);
        Note = ModsPageText.PlainNote(Folders);
        HasInstalled = _items.Count > 0;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Installed.Clear();
        foreach (var item in _items.Where(i => InstalledItems.Matches(i, Filter, FilterText)))
            Installed.Add(new InstalledRowViewModel(this, item,
                item.Kind == InstalledKind.WidgetPack ? Service.Store.ReadInfo(item.Id)?.PreviewPath : null));
        NothingMatches = HasInstalled && Installed.Count == 0;
    }

    /// <summary>
    /// A pack's switch (spec, Installed: "a switch bound to widgets.json enabled"): writes the flag, then shows what is on disk on every row of
    /// that pack, and tells the consumers (App's Changed, the window's pickers) when it changed.
    /// </summary>
    internal void SwitchPack(string id, string name, bool on)
    {
        if (Busy)
        {
            foreach (var row in Installed.Where(r => r.Id == id)) row.ShowEnabled(row.Enabled);
            return;
        }
        var result = Service.Store.SetEnabled(id, on);
        // What is on disk now; a pack gone behind the page's back keeps the row's last state (nothing was written).
        bool now = Service.Store.Load().Find(id)?.Enabled ?? Installed.FirstOrDefault(r => r.Id == id)?.Enabled ?? !on;
        Status = result.Ok ? (now ? $"{name} is on." : $"{name} is off.") : result.Error ?? "The widget couldn't be switched.";
        _items = [.. _items.Select(i => i.Id == id && i.Kind == InstalledKind.WidgetPack ? i with { Enabled = now } : i)];
        foreach (var row in Installed.Where(r => r.Id == id)) row.ShowEnabled(now);
        if (result.Ok) Notify();
    }
```

- In `UninstallAsync`, after `if (Busy) return Task.CompletedTask;`, add:

```csharp
        if (!Confirm(SettingsNames.UninstallQuestion(name), SettingsNames.UninstallDetail)) return Task.CompletedTask;
```

- Change the class summary to `Settings, Mods &amp; Widgets (Settings v2 slice 4, spec "Mods &amp; Widgets: one list"): the Installed list over every Claude Code folder and the widget packs, the catalog, and a status line. Until the page flip (Task 12) it is hosted in the Widgets tab.`

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, after `FilterBoxName`, add:

```csharp
    /// <summary>Installed: the filters hide everything (an empty-state line, WF35).</summary>
    public const string NothingMatches = "Nothing installed matches the filter.";
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`, change the constructor's `ModsPage = new ModsPageViewModel();` to `ModsPage = new ModsPageViewModel(host: ClaudeCodeHost.For(widget));`.

- [ ] **Step 5: The row on the page**

In `windows-dotnet/src/Sanduhr.App/Views/ModsPage.xaml`, replace the `DataTemplate DataType="{x:Type vm:InstalledWidgetRowViewModel}"` (the whole template) with:

```xml
        <DataTemplate DataType="{x:Type vm:InstalledRowViewModel}">
            <Border Background="{DynamicResource Sanduhr.Brush.Bg}" BorderBrush="{DynamicResource Sanduhr.Brush.Border}"
                    BorderThickness="1" CornerRadius="6" Padding="10,8" Margin="0,0,0,8"
                    AutomationProperties.Name="{Binding RowName}">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <Image Style="{StaticResource Preview}" Source="{Binding Preview}"
                           Visibility="{Binding HasPreview, Converter={StaticResource BoolVis}}"
                           AutomationProperties.Name="{Binding Name, StringFormat='Preview of {0}'}" />
                    <StackPanel Grid.Column="1">
                        <TextBlock Text="{Binding Name}" FontSize="12" FontWeight="SemiBold" Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                        <TextBlock Text="{Binding Detail}" FontSize="11" Margin="0,2,0,0" Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                        <TextBlock Text="{Binding Description}" Style="{StaticResource Dim}" Margin="0,2,0,0"
                                   Visibility="{Binding HasDescription, Converter={StaticResource BoolVis}}" />
                        <!-- Where it shows (WF3): the place and one click to it. -->
                        <WrapPanel Margin="0,4,0,0" Visibility="{Binding HasWhere, Converter={StaticResource BoolVis}}">
                            <TextBlock Text="{Binding WhereText}" Style="{StaticResource Dim}" VerticalAlignment="Center" Margin="0,0,8,0" />
                            <Button Style="{DynamicResource SettingsLinkButton}" Content="{Binding WhereLinkTitle}"
                                    views:SettingsLinkTo.Link="{Binding WhereLink}" AutomationProperties.Name="{Binding WhereLinkName}"
                                    Visibility="{Binding HasWhereLink, Converter={StaticResource BoolVis}}" />
                        </WrapPanel>
                        <!-- A Claude Code item: its scope; Sanduhr's mod links up to its switches, the others say why they can't switch. -->
                        <TextBlock Text="{Binding ScopeText}" FontSize="11" Margin="0,4,0,0" Foreground="{DynamicResource Sanduhr.Brush.Text}"
                                   Visibility="{Binding HasScope, Converter={StaticResource BoolVis}}" />
                        <TextBlock Text="{Binding ReadOnlyText}" Style="{StaticResource Dim}" Margin="0,2,0,0"
                                   Visibility="{Binding IsReadOnly, Converter={StaticResource BoolVis}}" />
                        <Button Style="{DynamicResource SettingsLinkButton}" Content="{Binding MetersLinkTitle}" HorizontalAlignment="Left" Margin="0,4,0,0"
                                views:SettingsLinkTo.Link="{Binding MetersLink}" AutomationProperties.Name="{Binding MetersLinkTitle}"
                                Visibility="{Binding IsSanduhrMod, Converter={StaticResource BoolVis}}" />
                    </StackPanel>
                    <StackPanel Grid.Column="2" VerticalAlignment="Top" Margin="8,0,0,0" Visibility="{Binding IsPack, Converter={StaticResource BoolVis}}">
                        <CheckBox Style="{DynamicResource ThemedCheckBox}" Content="{x:Static core:SettingsNames.PackOn}"
                                  IsChecked="{Binding Enabled, Mode=OneWay}" Command="{Binding ToggleCommand}"
                                  AutomationProperties.Name="{Binding SwitchName}" />
                        <Button Style="{DynamicResource FlatButton}" Margin="0,6,0,0"
                                Content="{x:Static core:SettingsNames.WidgetUninstall}" Command="{Binding UninstallCommand}"
                                IsEnabled="{Binding DataContext.Busy, RelativeSource={RelativeSource AncestorType=UserControl}, Converter={x:Static conv:Bools.Not}}"
                                AutomationProperties.Name="{Binding UninstallName}" />
                        <!-- Spec 3.8 section 6: a sprite made here takes the user's own PNG sheets; they stay on this PC. -->
                        <Button Style="{DynamicResource FlatButton}" Margin="0,6,0,0"
                                Content="{x:Static core:SettingsNames.WidgetUseOwnArt}" Command="{Binding UseOwnArtCommand}"
                                Visibility="{Binding CanUseOwnArt, Converter={StaticResource BoolVis}}"
                                IsEnabled="{Binding DataContext.Busy, RelativeSource={RelativeSource AncestorType=UserControl}, Converter={x:Static conv:Bools.Not}}"
                                AutomationProperties.Name="{Binding UseOwnArtName}"
                                AutomationProperties.HelpText="Choose a PNG sprite sheet for each stage. It is copied into the widget and never sent anywhere." />
                    </StackPanel>
                </Grid>
            </Border>
        </DataTemplate>
```

and in the installed section (`views:SettingsAnchor.Id="widgets-installed"`), add under the existing empty line:

```xml
                    <TextBlock Style="{StaticResource Dim}" Margin="0,0,0,8" Text="{x:Static core:SettingsNames.NothingMatches}"
                               Visibility="{Binding NothingMatches, Converter={StaticResource BoolVis}}" />
```

and under the list, `<TextBlock Text="{Binding Note}" Style="{StaticResource Dim}" Margin="0,4,0,0" Visibility="{Binding Note, Converter={StaticResource StrVis}}" />`, adding `<conv:StringToVisibilityConverter x:Key="StrVis" />` to the page's resources. Change the list's `AutomationProperties.Name="Installed widgets"` to `AutomationProperties.Name="{x:Static core:SettingsNames.InstalledSection}"`.

- [ ] **Step 6: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~ModsPageListTests|FullyQualifiedName~WidgetCatalogServiceTests|FullyQualifiedName~WidgetReviewServiceTests|FullyQualifiedName~ModsPageNamesTests|FullyQualifiedName~PackSwitchConsumersTests"`
Then: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsNamesTests"`
Expected: PASS. Build the harness too (`dotnet build tools/Sanduhr.RenderHarness`): `ProposeWidgetScenario` reads `RowName` and `CanUseOwnArt`, which the new row keeps.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/ModsPageViewModel.cs windows-dotnet/src/Sanduhr.App/Views/ModsPage.xaml windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.App.Tests/ModsPageFixture.cs windows-dotnet/tests/Sanduhr.App.Tests/ModsPageListTests.cs windows-dotnet/tests/Sanduhr.App.Tests/WidgetCatalogServiceTests.cs windows-dotnet/tests/Sanduhr.App.Tests/ModsPageNamesTests.cs
git commit -m "feat(mods): the one list: packs with their switch and where they show, Sanduhr's mod scoped per folder, other Claude Code items read-only"
```

---

### Task 10: Meters above the prompt, every folder at once

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/ModsPageViewModel.cs` (a meters section and its row class)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/ModsPageMetersTests.cs`

**Interfaces:**
- Consumes: `ClaudeCodeHost.Integrations()` (Task 7), `ClaudeCodeIntegrations.OwnModStateOf`, `Status`, `Install`, `SwitchOwnMod`, `UpdateOwnMod`, `Remove`, `CurrentModStamp`; `InstalledItems.MetersState`, `StateWord` (Task 3); `IntegrationConsentDialog.Ask`, `ConsentSpec`; `ThemedDialog.Show`; `SettingsNames.MetersOnFor`, `MetersOffVsRemove`.
- Produces:
  - `public sealed partial class MetersFolderRowViewModel`: `Folder`, `Display`, `RowName`, `State` (`FolderState`), `IsOn`, `CanSwitch`, `Reason`, `HasReason`, `CanUpdate`, `CanRemove`, `StateText`, `SwitchName`, `UpdateName`, `RemoveName`, `ToggleCommand`, `UpdateCommand`, `RemoveCommand`; `internal void Show(FolderMods folder)`
  - `ModsPageViewModel`: `ObservableCollection<MetersFolderRowViewModel> Meters`; `[ObservableProperty] string MetersStatus`; `bool HasMeterFolders`; `public Action<bool>? MetersChanged { get; set; }` (true after an install); `internal Func<string, bool> AskMetersConsent { get; set; }` (folder display in, yes out); `internal Func<string, string, string, bool> Ask { get; set; }` (title, message, primary label in, yes out); `string MetersCaption`

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/ModsPageMetersTests.cs`:

```csharp
using System.IO;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 4: Meters above the prompt on Mods &amp; Widgets (WF9; the Mac's layout): one row per folder with an On switch (writing
/// enabledPlugins through SwitchOwnMod), Update… and Remove…; On where the mod isn't installed asks and installs (decision 6).
/// </summary>
public sealed class ModsPageMetersTests : ModsPageFixture
{
    private (ModsPageViewModel Page, List<string> Asked, List<bool> Changed) Page(bool consent)
    {
        var page = Page();
        var asked = new List<string>();
        var changed = new List<bool>();
        page.AskMetersConsent = folder => { asked.Add(folder); return consent; };
        page.Ask = (_, _, _) => true;
        page.MetersChanged = installed => changed.Add(installed);
        page.ReloadLocal();
        return (page, asked, changed);
    }

    private static MetersFolderRowViewModel Row(ModsPageViewModel page, string name) => page.Meters.Single(m => m.Display == @"~\" + name);

    [Fact]
    public void Every_folder_has_a_row_with_its_state()
    {
        Home(".claude", "{}");
        Home(".claude-work", "{ broken");
        var (page, _, _) = Page(consent: false);

        Assert.Equal([@"~\.claude", @"~\.claude-work"], page.Meters.Select(m => m.Display));
        Assert.Equal((FolderState.NotInstalled, "Not installed"), (Row(page, ".claude").State, Row(page, ".claude").StateText));
        Assert.Equal(SettingsNames.MetersOffVsRemove, page.MetersCaption);
        Assert.True(page.HasMeterFolders);
    }

    [Fact]
    public void An_unreadable_folder_offers_no_switch_and_says_why()
    {
        var work = Home(".claude-work", "{ broken");
        var (page, _, _) = Page(consent: true);
        var row = Row(page, ".claude-work");

        Assert.False(row.CanSwitch);
        Assert.False(row.CanUpdate);
        Assert.False(row.CanRemove);
        Assert.True(row.HasReason);
        row.ToggleCommand.Execute(null);
        Assert.Equal("{ broken", File.ReadAllText(SettingsOf(work)));
    }

    [Fact]
    public void Declining_consent_leaves_the_folder_off_and_unwritten()
    {
        var home = Home(".claude", "{}");
        var (page, asked, changed) = Page(consent: false);

        Row(page, ".claude").ToggleCommand.Execute(null);

        Assert.Equal([@"~\.claude"], asked);
        Assert.Equal("{}", File.ReadAllText(SettingsOf(home)));
        Assert.False(Row(page, ".claude").IsOn);
        Assert.Empty(changed);
    }

    [Fact]
    public void Turning_on_where_it_isnt_installed_asks_then_installs_into_that_folder_only()
    {
        var home = Home(".claude", "{}");
        var other = Home(".claude-work", "{}");
        var (page, asked, changed) = Page(consent: true);

        Row(page, ".claude").ToggleCommand.Execute(null);

        Assert.Equal([@"~\.claude"], asked);
        Assert.Contains("CLAUDE_CODE_PLUGIN_DIRS", File.ReadAllText(SettingsOf(home)));
        Assert.Equal("{}", File.ReadAllText(SettingsOf(other)));
        Assert.True(Row(page, ".claude").IsOn);
        Assert.True(Row(page, ".claude").CanRemove);
        Assert.False(Row(page, ".claude").CanUpdate);
        Assert.Equal([true], changed);
        Assert.Contains(page.Installed, r => r.IsSanduhrMod);   // the list follows
    }

    [Fact]
    public void Off_keeps_the_entry_and_on_again_asks_nothing()
    {
        var home = Home(".claude", "{}");
        var (page, asked, changed) = Page(consent: true);
        Row(page, ".claude").ToggleCommand.Execute(null);
        asked.Clear();

        Row(page, ".claude").ToggleCommand.Execute(null);

        var text = File.ReadAllText(SettingsOf(home));
        Assert.Contains("CLAUDE_CODE_PLUGIN_DIRS", text);
        Assert.False(System.Text.Json.Nodes.JsonNode.Parse(text)!["enabledPlugins"]!["sanduhr-meters@inline"]!.GetValue<bool>());
        Assert.False(Row(page, ".claude").IsOn);
        Assert.Equal(SettingsNames.FolderOff, Row(page, ".claude").StateText);

        Row(page, ".claude").ToggleCommand.Execute(null);
        Assert.Empty(asked);
        Assert.True(Row(page, ".claude").IsOn);
        Assert.Equal([true, false, false], changed);
    }

    [Fact]
    public void Remove_asks_and_puts_settings_json_back_exactly()
    {
        var home = Home(".claude", "{}");
        var (page, _, _) = Page(consent: true);
        Row(page, ".claude").ToggleCommand.Execute(null);
        var questions = new List<string>();
        page.Ask = (title, _, primary) => { questions.Add(title + "|" + primary); return true; };

        Row(page, ".claude").RemoveCommand.Execute(null);

        Assert.Equal(["Remove the meters mod?|Remove"], questions);
        Assert.Equal("{}", File.ReadAllText(SettingsOf(home)));
        Assert.False(Row(page, ".claude").CanRemove);
        Assert.Equal(FolderState.NotInstalled, Row(page, ".claude").State);
    }

    [Fact]
    public void Update_shows_only_while_the_entry_is_outdated_and_moves_it_to_this_version()
    {
        var old = Path.Combine(Root, "external", "mods", "v0.1.0-20200101000000", "sanduhr-meters").Replace("\\", "\\\\");
        var home = Home(".claude", "{\"env\":{\"CLAUDE_CODE_PLUGIN_DIRS\":\"" + old + "\"}}");
        var (page, _, _) = Page(consent: true);
        Assert.True(Row(page, ".claude").CanUpdate);

        Row(page, ".claude").UpdateCommand.Execute(null);

        Assert.DoesNotContain("v0.1.0-20200101000000", File.ReadAllText(SettingsOf(home)));
        Assert.False(Row(page, ".claude").CanUpdate);
        Assert.Equal("Meters mod updated.", page.MetersStatus);
    }

    [Fact]
    public void Each_switch_names_its_folder_for_a_screen_reader()
    {
        Home(".claude", "{}");
        var (page, _, _) = Page(consent: false);
        var row = Row(page, ".claude");
        Assert.Equal(SettingsNames.MetersOnFor(@"~\.claude"), row.SwitchName);
        Assert.StartsWith("Update…".TrimEnd('…'), row.UpdateName);
        Assert.StartsWith("Remove", row.RemoveName);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~ModsPageMetersTests`
Expected: build FAILS with `'ModsPageViewModel' does not contain a definition for 'Meters'`.

- [ ] **Step 3: The section**

In `windows-dotnet/src/Sanduhr.App/ViewModels/ModsPageViewModel.cs`, add above the `ModsPageViewModel` class:

```csharp
/// <summary>
/// One folder's row under Meters above the prompt (Settings v2 slice 4, WF9): an On switch that writes enabledPlugins through SwitchOwnMod (or,
/// where the mod isn't installed, asks and installs it), "Update…" while the entry is outdated, "Remove…" while it is listed. A folder whose
/// settings.json can't be read shows why and nothing that would write.
/// </summary>
public sealed partial class MetersFolderRowViewModel : ObservableObject
{
    private readonly ModsPageViewModel _owner;

    internal MetersFolderRowViewModel(ModsPageViewModel owner, FolderMods folder)
    {
        _owner = owner;
        Folder = folder.Folder;
        Display = folder.Display;
        Show(folder);
    }

    public string Folder { get; }
    public string Display { get; }

    [ObservableProperty] private FolderState _state;
    [ObservableProperty] private bool _isOn;
    [ObservableProperty] private bool _canUpdate;
    [ObservableProperty] private bool _canRemove;

    public bool CanSwitch => State != FolderState.Unreadable;
    public string StateText => InstalledItems.StateWord(State);
    public string Reason => State == FolderState.Unreadable
        ? "This folder's settings.json can't be read, so Sanduhr changes nothing in it."
        : "";
    public bool HasReason => Reason.Length > 0;

    public string SwitchName => SettingsNames.MetersOnFor(Display);
    /// <summary>The row read as one line: "~\.claude, On".</summary>
    public string RowName => $"{Display}, {StateText}";
    public string UpdateName => $"Update {SettingsNames.MetersSection} in {Display}";
    public string RemoveName => $"Remove {SettingsNames.MetersSection} from {Display}";

    /// <summary>Shows what the folder holds now. Always raises IsOn, so a switch the person flipped and that was refused flips back (decision 7).</summary>
    internal void Show(FolderMods folder)
    {
        State = InstalledItems.MetersState(folder);
        _isOn = State == FolderState.On;
        OnPropertyChanged(nameof(IsOn));
        CanRemove = folder.Own is { Listed: true } && State != FolderState.Unreadable;
        CanUpdate = CanRemove && folder.Meters == IntegrationStatus.Outdated;
        OnPropertyChanged(nameof(CanSwitch));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(RowName));
        OnPropertyChanged(nameof(Reason));
        OnPropertyChanged(nameof(HasReason));
    }

    [RelayCommand]
    private void Toggle() => _owner.SwitchMeters(this);

    [RelayCommand]
    private void Update() => _owner.UpdateMeters(this);

    [RelayCommand]
    private void Remove() => _owner.RemoveMeters(this);
}
```

In `ModsPageViewModel`, add after `Folders`:

```csharp
    // ---- Meters above the prompt (spec, Mods & Widgets, section 1): every folder at once ----

    public ObservableCollection<MetersFolderRowViewModel> Meters { get; } = [];
    [ObservableProperty] private string _metersStatus = "";
    public bool HasMeterFolders => Meters.Count > 0;
    public string MetersCaption => SettingsNames.MetersOffVsRemove;

    /// <summary>Raised after a folder's meters were switched, installed, updated or removed (true after an install): the Claude Code cards
    /// read again and the snapshot switch follows (decision 22).</summary>
    public Action<bool>? MetersChanged { get; set; }

    /// <summary>Asks before the mod is installed into a folder (the consent dialog that names it). A seam for tests.</summary>
    internal Func<string, bool> AskMetersConsent { get; set; } = _ => false;

    /// <summary>Asks before Update… and Remove…: title, message and the primary label in, yes out. A seam for tests.</summary>
    internal Func<string, string, string, bool> Ask { get; set; } = (_, _, _) => false;

    /// <summary>Brings the rows to the folders as read, in place where a folder is still there, so a switch keeps keyboard focus.</summary>
    private void ShowMeters()
    {
        var wanted = Folders;
        for (int i = Meters.Count - 1; i >= 0; i--)
            if (!wanted.Any(f => ClaudeCodeFolders.Same(f.Folder, Meters[i].Folder))) Meters.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            var row = Meters.FirstOrDefault(m => ClaudeCodeFolders.Same(m.Folder, wanted[i].Folder));
            if (row is null) Meters.Insert(Math.Min(i, Meters.Count), new MetersFolderRowViewModel(this, wanted[i]));
            else row.Show(wanted[i]);
        }
        OnPropertyChanged(nameof(HasMeterFolders));
    }

    internal void SwitchMeters(MetersFolderRowViewModel row)
    {
        if (_host is null || !row.CanSwitch)
        {
            ReloadLocal();
            return;
        }
        bool want = !row.IsOn;
        bool installed = false, changed = false;
        try
        {
            var integ = _host.Integrations();
            if (want && !integ.OwnModStateOf(row.Folder).Listed)
            {
                if (AskMetersConsent(row.Display))
                {
                    var res = integ.Install(IntegrationKind.Meters, row.Folder);
                    installed = changed = res.Ok;
                    MetersStatus = res.Ok ? "Meters installed. New Claude Code sessions show them." : Short(res.Message, "Couldn't install the meters.");
                }
            }
            else
            {
                var res = integ.SwitchOwnMod(row.Folder, want);
                changed = res.Ok;
                MetersStatus = res.Ok ? (want ? "Meters mod turned on." : "Meters mod turned off.") : Short(res.Message, "Couldn't change the switch.");
            }
        }
        catch (Exception) { MetersStatus = "Couldn't change the switch."; }
        ReloadLocal();
        if (changed) MetersChanged?.Invoke(installed);
    }

    internal void UpdateMeters(MetersFolderRowViewModel row)
    {
        if (_host is null || !row.CanUpdate) return;
        try
        {
            var integ = _host.Integrations();
            var state = integ.OwnModStateOf(row.Folder);
            var version = state.Pinned ?? "the installed version";
            var current = integ.CurrentModStamp() ?? "the new version";
            if (!Ask("Update the meters mod?",
                    $"This folder's meters mod is pinned to {version}. Updating points it at {current}, the version that ships with this Sanduhr. "
                    + "The mod's files are copied again and one entry in this folder's settings.json changes. The old version's files stay until "
                    + "Sanduhr prunes them.", "Update"))
                return;
            var res = integ.UpdateOwnMod(row.Folder);
            MetersStatus = !res.Ok ? Short(res.Message, "Couldn't update the meters mod.")
                : res.Outcome == ModUpdateOutcome.Updated ? "Meters mod updated." : "Meters mod is already current.";
        }
        catch (Exception) { MetersStatus = "Couldn't update the meters mod."; }
        ReloadLocal();
        MetersChanged?.Invoke(false);
    }

    internal void RemoveMeters(MetersFolderRowViewModel row)
    {
        if (_host is null || !row.CanRemove) return;
        try
        {
            if (!Ask("Remove the meters mod?",
                    "Takes Sanduhr's entry out of this folder's settings.json and leaves any other mod alone.", "Remove"))
                return;
            var res = _host.Integrations().Remove(IntegrationKind.Meters, row.Folder);
            MetersStatus = res.Ok ? "Meters mod removed." : Short(res.Message, "Couldn't remove the meters mod.");
        }
        catch (Exception) { MetersStatus = "Couldn't remove the meters mod."; }
        ReloadLocal();
        MetersChanged?.Invoke(false);
    }

    private string Short(string message, string fallback) =>
        string.IsNullOrWhiteSpace(message) ? fallback
            : _host is null ? message : message.Replace(_host.Profile, "~", StringComparison.OrdinalIgnoreCase);
```

In the constructor, after the `Confirm = …;` statement, add:

```csharp
        AskMetersConsent = folder => IntegrationConsentDialog.Ask(_window, new ConsentSpec(
            "Draw your meters above the Claude Code prompt?",
            "Sanduhr adds a mod that draws your session and weekly meters in a band above the prompt.",
            folder,
            "What gets written: the mod's folder, copied under %USERPROFILE%\\.sanduhr\\mods, and one entry in this folder's settings.json under "
            + "env, CLAUDE_CODE_PLUGIN_DIRS. The mod reads only Sanduhr's snapshot file. Off keeps the entry and files; Remove takes out only "
            + "Sanduhr's entry and leaves any other folder in that list alone.",
            "Install")) is not null;
        Ask = (title, message, primary) => ThemedDialog.Show(_window, title, message, MessageBoxButton.YesNo,
            primary == "Remove" ? ThemedDialogKind.Warning : ThemedDialogKind.Info,
            primaryLabel: primary, secondaryLabel: SettingsNames.UninstallCancel) == MessageBoxResult.Yes;
```

At the end of `ReloadInstalled()`, after `ApplyFilter();`, add `ShowMeters();`.

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~ModsPageMetersTests|FullyQualifiedName~ModsPageListTests"`
Expected: PASS (the fixture's fake mod source lets `Install` copy the mod into the scratch `external\mods`).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/ModsPageViewModel.cs windows-dotnet/tests/Sanduhr.App.Tests/ModsPageMetersTests.cs
git commit -m "feat(mods): Meters above the prompt per folder on Mods & Widgets: a switch, Update… while outdated, Remove…, and consent before an install"
```

---

### Task 11: The catalog fetches only when opened, and merges with the installed packs by id

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/ModsPageViewModel.cs` (`WidgetCatalogRowViewModel` becomes `CatalogRowViewModel`; `Activate`, `RefreshAsync`, `Rebuild`; the catalog's open state and kind filter)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/ModsPage.xaml` (the catalog row template)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/ModsPageCatalogTests.cs`; modify `WidgetCatalogServiceTests.cs`, `ModsPageNamesTests.cs`

**Interfaces:**
- Consumes: `InstalledItems.CatalogRows`, `CatalogAction`, `CatalogRow` (Task 4); `SwitchPack` (Task 9); `WidgetCatalogClient.RefreshAsync`, `InstallAsync`, `Current`, `Running`; `SettingsNames.CatalogDisclosure`, `ShowingSprites`, `ShowEveryPack`.
- Produces:
  - `public sealed partial class CatalogRowViewModel` (replaces `WidgetCatalogRowViewModel`; `WidgetRowAction` is deleted): `Entry`, `Id`, `Name`, `Kind`, `Version`, `Description`, `Preview`, `HasPreview`, `State`, `Action` (`CatalogAction`), `HasButton`, `IsSwitch`, `ActionLabel`, `ActionName`, `[ObservableProperty] bool Enabled`, `SwitchName`, `ActCommand`, `ToggleCommand`, `Detail`, `RowName`; `internal void ShowEnabled(bool value)`
  - `ModsPageViewModel`: `ObservableCollection<CatalogRowViewModel> Catalog`; `[ObservableProperty] bool CatalogOpen` (opening fetches once); `public Task OpenCatalog()`; `public Task Activate()` (no fetch); `[ObservableProperty] string? CatalogKind`; `public void ShowSprites()`; `[RelayCommand] ShowEveryPack`; `string CatalogDisclosure`

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/ModsPageCatalogTests.cs`:

```csharp
using System.Security.Cryptography;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 4: the Catalog section (spec, Mods &amp; Widgets, section 3; WF6, WF40). Collapsed by default, it fetches only when opened or
/// on "Check the catalog again"; a pack appears once, merged with its installed row by id. Over a fake GitHub only.
/// </summary>
public sealed class ModsPageCatalogTests : ModsPageFixture
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly FakeGitHub _net = new();

    private ModsPageViewModel Published(Version? running = null)
    {
        var palette = WidgetKit.Zip(WidgetKit.Pack("good-palette"));
        var sprite = WidgetKit.Zip(WidgetKit.Pack("good-sprite"));
        WidgetKit.Publish(_net, _key, "2026-10-25T02:00:00Z",
            (WidgetKit.Entry("good-palette", "Fixture palette", "palette", "1.0.0", palette), palette),
            (WidgetKit.Entry("good-sprite", "Fixture sprite", "sprite", "1.0.0", sprite), sprite));
        return new ModsPageViewModel(() => new Sanduhr.App.Services.WidgetCatalogService(Paths, _net, running ?? WidgetKit.V380, [WidgetKit.Spki(_key)]),
            null, Host())
        {
            Confirm = (_, _) => true,
        };
    }

    [Fact]
    public async Task Opening_the_page_fetches_nothing_and_opening_the_catalog_fetches_once()
    {
        var page = Published();
        await page.Activate();
        page.FilterText = "palette";
        Assert.Empty(_net.Seen);

        page.CatalogOpen = true;
        await page.OpenCatalog();
        int first = _net.Seen.Count;
        Assert.True(first > 0);
        Assert.Equal(2, page.Catalog.Count);

        page.CatalogOpen = false;
        page.CatalogOpen = true;
        await page.OpenCatalog();
        Assert.Equal(first, _net.Seen.Count);

        await page.CheckAgainCommand.ExecuteAsync(null);
        Assert.True(_net.Seen.Count > first);
    }

    [Fact]
    public async Task An_installed_pack_appears_once_with_the_same_switch_as_its_installed_row()
    {
        InstalledPackKit.Install(Paths, "good-palette");
        var page = Published();
        page.CatalogOpen = true;
        await page.OpenCatalog();

        var row = page.Catalog.Single(r => r.Id == "good-palette");
        Assert.Equal((CatalogAction.Switch, true, false), (row.Action, row.IsSwitch, row.HasButton));
        Assert.True(row.Enabled);

        row.ToggleCommand.Execute(null);

        Assert.False(InstalledPackKit.Store(Paths).Load().Find("good-palette")!.Enabled);
        Assert.False(row.Enabled);
        Assert.False(page.Installed.Single(r => r.Id == "good-palette").Enabled);
    }

    [Fact]
    public async Task Install_from_the_catalog_turns_the_row_into_the_switch()
    {
        var page = Published();
        page.CatalogOpen = true;
        await page.OpenCatalog();
        var sprite = page.Catalog.Single(r => r.Id == "good-sprite");
        Assert.Equal((CatalogAction.Install, "Install Fixture sprite"), (sprite.Action, sprite.ActionName));

        await sprite.ActCommand.ExecuteAsync(null);

        Assert.Equal("Fixture sprite is installed.", page.Status);
        Assert.Equal(CatalogAction.Switch, page.Catalog.Single(r => r.Id == "good-sprite").Action);
        Assert.Contains(page.Installed, r => r.Id == "good-sprite");
    }

    [Fact]
    public async Task Offline_the_catalog_says_what_packs_are_and_the_installed_list_stays()
    {
        InstalledPackKit.Install(Paths, "good-palette");
        var page = Page();   // the fixture's offline network
        page.CatalogOpen = true;
        await page.OpenCatalog();
        Assert.Equal(WidgetCatalogClient.Offline, page.Status);
        Assert.Empty(page.Catalog);
        Assert.Single(page.Installed, r => r.Id == "good-palette");
    }

    [Fact]
    public async Task The_companion_link_shows_sprites_until_every_pack_is_asked_for()
    {
        var page = Published();
        page.ShowSprites();
        await page.OpenCatalog();
        Assert.True(page.CatalogOpen);
        Assert.Equal(["good-sprite"], page.Catalog.Select(r => r.Id));

        page.ShowEveryPackCommand.Execute(null);
        Assert.Null(page.CatalogKind);
        Assert.Equal(2, page.Catalog.Count);
    }

    [Fact]
    public void The_catalog_says_where_it_comes_from()
    {
        Assert.Equal(SettingsNames.CatalogDisclosure, Published().CatalogDisclosure);
    }
}
```

In `windows-dotnet/tests/Sanduhr.App.Tests/WidgetCatalogServiceTests.cs`: every `await page.Activate();` becomes `await page.OpenCatalog();`; `WidgetRowAction.Install` becomes `CatalogAction.Install`, `WidgetRowAction.Update` becomes `CatalogAction.Update`, `WidgetRowAction.None` becomes `CatalogAction.None`; in `Install_update_and_uninstall_…`, the two asserts after the first install become `Assert.Equal("Installed.", page.Catalog.Single(r => r.Entry.Id == "good-palette").State);` and `Assert.Equal(CatalogAction.Switch, page.Catalog.Single(r => r.Entry.Id == "good-palette").Action);`; add `using Sanduhr.Core;` if missing.

In `windows-dotnet/tests/Sanduhr.App.Tests/ModsPageNamesTests.cs`: in both facts, right after the tab is selected, add `svm.ModsPage.CatalogOpen = true;` (the page no longer fetches on open). In `Each_widget_row_names_its_button_for_its_pack`, the installed palette's catalog row is now the same switch as its installed row; add this helper to the class:

```csharp
    private static List<string> CheckNames(DependencyObject root)
    {
        var names = new List<string>();
        void Collect(DependencyObject node)
        {
            if (node is CheckBox && AutomationProperties.GetName(node) is { Length: > 0 } name) names.Add(name);
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Collect(VisualTreeHelper.GetChild(node, i));
        }
        Collect(root);
        return names;
    }
```

and, after the button asserts, `Assert.Equal(2, CheckNames(window).Count(n => n == SettingsNames.PackOnFor("Fixture palette")));` (the installed row and the catalog row, the same switch).

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~ModsPageCatalogTests`
Expected: build FAILS with `'ModsPageViewModel' does not contain a definition for 'OpenCatalog'`.

- [ ] **Step 3: The catalog row**

In `windows-dotnet/src/Sanduhr.App/ViewModels/ModsPageViewModel.cs`, replace the whole `WidgetCatalogRowViewModel` class and the `public enum WidgetRowAction { None, Install, Update }` line with:

```csharp
/// <summary>One pack of the verified catalog, merged with its installed row by id (spec, Catalog): Install, Update, or the same switch the
/// Installed row has, and the line under it.</summary>
public sealed partial class CatalogRowViewModel : ObservableObject
{
    private readonly ModsPageViewModel _owner;

    internal CatalogRowViewModel(ModsPageViewModel owner, CatalogRow row, InstalledWidgetInfo? info)
    {
        _owner = owner;
        Entry = row.Entry;
        Action = row.Action;
        State = row.State;
        _enabled = row.Installed?.Enabled ?? false;
        Description = info?.Description ?? "";
        Preview = ModsPageViewModel.LoadPreview(info?.PreviewPath);
    }

    public WidgetCatalogEntry Entry { get; }
    public string Id => Entry.Id;
    public string Name => Entry.Name;
    public string Kind => Entry.Kind;
    public string Version => Entry.Version;
    public string Description { get; }
    public ImageSource? Preview { get; }
    public bool HasPreview => Preview is not null;
    public string State { get; }
    public CatalogAction Action { get; }
    public bool HasButton => Action is CatalogAction.Install or CatalogAction.Update;
    public bool IsSwitch => Action == CatalogAction.Switch;

    /// <summary>"sprite · 1.0.0".</summary>
    public string Detail => $"{Kind} · {Version}";

    public string ActionLabel => Action switch
    {
        CatalogAction.Install => SettingsNames.WidgetInstall,
        CatalogAction.Update => SettingsNames.WidgetUpdate,
        _ => "",
    };

    /// <summary>The button's name: the action and the pack ("Install Hourglass", "Update Hourglass to 1.1.0").</summary>
    public string ActionName => Action switch
    {
        CatalogAction.Update => $"{SettingsNames.WidgetUpdate} {Name} to {Version}",
        CatalogAction.Install => $"{SettingsNames.WidgetInstall} {Name}",
        _ => "",
    };

    [ObservableProperty] private bool _enabled;
    public string SwitchName => SettingsNames.PackOnFor(Name);

    internal void ShowEnabled(bool value)
    {
        _enabled = value;
        OnPropertyChanged(nameof(Enabled));
    }

    [RelayCommand]
    private Task Act() => _owner.ActAsync(this);

    [RelayCommand]
    private void Toggle() => _owner.SwitchPack(Id, Name, !Enabled);

    /// <summary>The row read as one line: "Hourglass, sprite, version 1.0.0, installed."</summary>
    public string RowName => string.Join(", ", new[] { Name, Kind, "version " + Version, State.TrimEnd('.') }.Where(s => !string.IsNullOrEmpty(s)));

    public override string ToString() => RowName;
}
```

- [ ] **Step 4: Fetch on open, never on show**

In `ModsPageViewModel`:

- Replace `public ObservableCollection<WidgetCatalogRowViewModel> Catalog { get; } = [];` with `public ObservableCollection<CatalogRowViewModel> Catalog { get; } = [];` and add:

```csharp
    /// <summary>The Catalog section is open. Opening it fetches the catalog when nothing was fetched in this window (WF6, decision 13).</summary>
    [ObservableProperty] private bool _catalogOpen;
    /// <summary>One pack kind to show ("sprite" from the companion row's link), null for every pack (decision 14).</summary>
    [ObservableProperty] private string? _catalogKind;
    private bool _asked;

    public string CatalogDisclosure => SettingsNames.CatalogDisclosure;
    public bool ShowingOneKind => CatalogKind is not null;

    partial void OnCatalogOpenChanged(bool value)
    {
        if (value) _ = OpenCatalog();
    }

    partial void OnCatalogKindChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowingOneKind));
        RebuildCatalog();
    }

    /// <summary>The catalog was opened: fetch once in this window (an attempt counts; "Check the catalog again" retries).</summary>
    public Task OpenCatalog()
    {
        if (!CatalogOpen) CatalogOpen = true;   // a landing on mods#catalog or the companion link opens it
        if (_refreshing is { IsCompleted: false } running) return running;
        if (_asked) { RebuildCatalog(); return Task.CompletedTask; }
        return _refreshing = RefreshAsync();
    }

    /// <summary>The companion row's "Get one in Mods &amp; Widgets…": the catalog, sprites only.</summary>
    public void ShowSprites()
    {
        CatalogKind = "sprite";
        _ = OpenCatalog();
    }

    [RelayCommand]
    private void ShowEveryPack() => CatalogKind = null;
```

- Replace `Activate()` with:

```csharp
    /// <summary>The page became visible: list what is installed. Nothing is fetched (the catalog asks only when opened, WF6).</summary>
    public Task Activate()
    {
        ReloadLocal();
        return Task.CompletedTask;
    }
```

- In `RefreshAsync`, set `_asked = true;` as its first line.
- Replace `Rebuild()` with:

```csharp
    private void Rebuild()
    {
        ReloadInstalled();
        RebuildCatalog();
    }

    private void RebuildCatalog()
    {
        Catalog.Clear();
        if (Service.Client.Current is { } doc)
            foreach (var row in InstalledItems.CatalogRows(doc, _items, Service.Client.Running, CatalogKind))
                Catalog.Add(new CatalogRowViewModel(this, row, row.Installed is null ? null : Service.Store.ReadInfo(row.Entry.Id)));
        HasCatalog = Catalog.Count > 0;
    }
```

- In `ActAsync`, the parameter becomes `CatalogRowViewModel row` and its guard `if (row.Action is not (CatalogAction.Install or CatalogAction.Update)) return;`; its status uses `row.Action == CatalogAction.Update`.
- In `SwitchPack`, after the `Installed` loop, add `foreach (var c in Catalog.Where(c => c.Id == id)) c.ShowEnabled(now);`.
- Change `ReloadLocal()`'s summary to `A pack or a folder changed somewhere else (a widget made with Claude was applied, a card installed a piece): the lists are read again. The catalog is not fetched.`

In `windows-dotnet/src/Sanduhr.App/Views/ModsPage.xaml`, replace the `DataTemplate DataType="{x:Type vm:WidgetCatalogRowViewModel}"` with:

```xml
        <DataTemplate DataType="{x:Type vm:CatalogRowViewModel}">
            <Border Background="{DynamicResource Sanduhr.Brush.Bg}" BorderBrush="{DynamicResource Sanduhr.Brush.Border}"
                    BorderThickness="1" CornerRadius="6" Padding="10,8" Margin="0,0,0,8"
                    AutomationProperties.Name="{Binding RowName}">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <Image Style="{StaticResource Preview}" Source="{Binding Preview}"
                           Visibility="{Binding HasPreview, Converter={StaticResource BoolVis}}"
                           AutomationProperties.Name="{Binding Name, StringFormat='Preview of {0}'}" />
                    <StackPanel Grid.Column="1">
                        <TextBlock Text="{Binding Name}" FontSize="12" FontWeight="SemiBold" Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                        <TextBlock Text="{Binding Detail}" FontSize="11" Margin="0,2,0,0" Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                        <TextBlock Text="{Binding Description}" Style="{StaticResource Dim}" Margin="0,2,0,0" />
                        <TextBlock Text="{Binding State}" Style="{StaticResource Dim}" Margin="0,2,0,0" />
                    </StackPanel>
                    <StackPanel Grid.Column="2" VerticalAlignment="Center" Margin="8,0,0,0">
                        <Button Style="{DynamicResource FlatButton}" Content="{Binding ActionLabel}" Command="{Binding ActCommand}"
                                Visibility="{Binding HasButton, Converter={StaticResource BoolVis}}"
                                IsEnabled="{Binding DataContext.Busy, RelativeSource={RelativeSource AncestorType=UserControl}, Converter={x:Static conv:Bools.Not}}"
                                AutomationProperties.Name="{Binding ActionName}" />
                        <CheckBox Style="{DynamicResource ThemedCheckBox}" Content="{x:Static core:SettingsNames.PackOn}"
                                  IsChecked="{Binding Enabled, Mode=OneWay}" Command="{Binding ToggleCommand}"
                                  Visibility="{Binding IsSwitch, Converter={StaticResource BoolVis}}"
                                  AutomationProperties.Name="{Binding SwitchName}" />
                    </StackPanel>
                </Grid>
            </Border>
        </DataTemplate>
```

and in `ModsPage.xaml.cs`, the page's `Activate()` call stays (it lists, it no longer fetches); change the class summary's second sentence to `Each time the page becomes visible it lists what is installed; the catalog is fetched only when its section opens.`

- [ ] **Step 5: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~ModsPageCatalogTests|FullyQualifiedName~ModsPageListTests|FullyQualifiedName~ModsPageMetersTests|FullyQualifiedName~WidgetCatalogServiceTests|FullyQualifiedName~ModsPageNamesTests|FullyQualifiedName~WidgetReviewServiceTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/ModsPageViewModel.cs windows-dotnet/src/Sanduhr.App/Views/ModsPage.xaml windows-dotnet/src/Sanduhr.App/Views/ModsPage.xaml.cs windows-dotnet/tests/Sanduhr.App.Tests/ModsPageCatalogTests.cs windows-dotnet/tests/Sanduhr.App.Tests/WidgetCatalogServiceTests.cs windows-dotnet/tests/Sanduhr.App.Tests/ModsPageNamesTests.cs
git commit -m "feat(mods): the catalog asks GitHub only when opened, and each pack appears once with Install, Update or its switch"
```

---

### Task 12: The page flip: Mods & Widgets in the Mods tab; Widgets and Mods & Config retire; every old link lands

**Files:**
- Rewrite: `windows-dotnet/src/Sanduhr.App/Views/ModsPage.xaml` (the page in the spec's order); modify `Views/ModsPage.xaml.cs` (summary)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (the Mods TabItem; the Widgets TabItem deleted), `Views/SettingsWindow.xaml.cs` (`AttachOwner`, the Mods reload), `ViewModels/SettingsViewModel.cs` (`Mods` goes; the `ModsPage` setter wires the meters and folders), `ViewModels/FolderHoldsViewModel.cs` (`ModsLine` moves in)
- Delete: `windows-dotnet/src/Sanduhr.App/ViewModels/ModsConfigViewModel.cs`, `windows-dotnet/tests/Sanduhr.App.Tests/ModsConfigFixture.cs`, `ModsConfigFolderTests.cs`, `ModsConfigPluginsTests.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (`Sidebar`, `PageTitle`, `TitlesToCome`, `Table`, `Retired`, `WidgetPackLine` goes), `SettingsIndex.cs` (Mods anchors, Widgets rows out, `Catalog`, `CollapsedSections`, `NameAnchors`, `PageSynonyms`), `WhatsNewCards.cs` (one body)
- Modify: `windows-dotnet/src/Sanduhr.Mcp/ToolCatalog.cs` (one string)
- Test: `windows-dotnet/tests/Sanduhr.Tests/SettingsModsIndexTests.cs`, `SettingsIndexTests.cs`, `SettingsDeskSplitIndexTests.cs`, `SettingsLinkTests.cs`, `SettingsReachIndexTests.cs`, `SettingsNamesTests.cs`, `SettingsSourceScanTests.cs`; `windows-dotnet/tests/Sanduhr.App.Tests/ModsAndCardsPagesTests.cs`, `SettingsWindowNamesTests.cs`, `ModsPageNamesTests.cs`, `AppWindowFixture.cs`

**Interfaces:**
- Consumes: everything in Tasks 8 to 11.
- Produces:
  - `SettingsIndex.Catalog = "catalog"`; the Mods page's anchors `meters` (`SettingsIndex.Meters`), `mods` (`ModList`, titled "Installed") and `catalog` (collapsed, "Catalog"); `widgets-catalog` and `widgets-installed` leave the index; the Widgets page leaves the sidebar, so its aliases (`widgets` → `mods#catalog`, `widgets#widgets-installed` → `mods#mods`, `widgets#widgets-catalog` → `mods#catalog`) and `claudecode#meters` → `mods#meters` go live, and the "widgets", "packs", "pack" boost fires.
  - `SettingsNames.PageTitle(SettingsPage.Mods) == "Mods & Widgets"`; `Sidebar`: Extend `[ClaudeCode, Mods]`, Desktop without Widgets; `TitlesToCome == [PrivacyTitle]`; `Retired` gains "Mods & Config" and "Widgets settings…"; `Table`: `(WidgetCatalogCheck, SettingsPage.Mods)`.
  - `SettingsViewModel.Mods` is removed. The `ModsPage` setter sets `SettingsChanged = PacksChanged` and `MetersChanged = ClaudeCodePage.AfterMetersChange`; the Claude Code page's `FoldersChanged` and `PiecesChanged` reload the current `ModsPage`.

- [ ] **Step 1: Write the failing tests**

Append to `windows-dotnet/tests/Sanduhr.Tests/SettingsModsIndexTests.cs`:

```csharp
    // ---- the page flip ----

    [Fact]
    public void Mods_and_Widgets_is_the_Extend_groups_second_page_and_Widgets_leaves_the_sidebar()
    {
        Assert.Equal([SettingsPage.ClaudeCode, SettingsPage.Mods], SettingsNames.Sidebar.Single(g => g.Title == "Extend").Pages);
        Assert.DoesNotContain(SettingsPage.Widgets, SettingsNames.Pages);
        Assert.Equal("Mods & Widgets", SettingsNames.PageTitle(SettingsPage.Mods));
        Assert.Equal("Mods & Widgets settings…", SettingsNames.LinkTitle(SettingsPage.Mods));
        Assert.Equal([SettingsNames.PrivacyTitle], SettingsNames.TitlesToCome);
    }

    [Fact]
    public void The_page_holds_meters_installed_and_the_collapsed_catalog()
    {
        Assert.Equal([SettingsIndex.Meters, SettingsIndex.ModList, SettingsIndex.Catalog], SettingsIndex.EntriesOn(SettingsPage.Mods).Select(e => e.Anchor));
        Assert.Equal([SettingsNames.MetersSection, SettingsNames.InstalledSection, SettingsNames.CatalogSection],
            SettingsIndex.EntriesOn(SettingsPage.Mods).Select(e => e.Title));
        Assert.Equal(SettingsNames.CatalogSection, SettingsIndex.CollapsedSections[(SettingsPage.Mods, SettingsIndex.Catalog)]);
        Assert.Equal(SettingsNames.CatalogSection,
            SettingsIndex.All.Single(e => e.Title == SettingsNames.WidgetCatalogCheck).CollapsedIn);   // "(in Catalog)"
        Assert.Empty(SettingsIndex.JumpLinks(SettingsPage.Mods));   // three sections: no jump links (decision 19)
    }

    [Theory]
    [InlineData(SettingsPage.Widgets, "widgets-catalog", SettingsPage.Mods, "catalog")]
    [InlineData(SettingsPage.Widgets, "widgets-installed", SettingsPage.Mods, "mods")]
    [InlineData(SettingsPage.Widgets, "widgets-companion", SettingsPage.Desk, "pieces")]
    [InlineData(SettingsPage.Widgets, null, SettingsPage.Mods, "catalog")]
    [InlineData(SettingsPage.Widgets, "gone", SettingsPage.Mods, "catalog")]
    [InlineData(SettingsPage.ClaudeCode, "meters", SettingsPage.Mods, "meters")]
    public void Every_slice_4_alias_lands_on_its_new_home(SettingsPage from, string? anchor, SettingsPage to, string toAnchor)
    {
        Assert.Equal(new SettingsLink(to, toAnchor), SettingsIndex.Resolve(from, anchor));
        Assert.Equal(new SettingsLink(to, toAnchor),
            SettingsIndex.ParseLink(SettingsIndex.PageId(from) + (anchor is null ? "" : "#" + anchor)));
    }

    [Fact]
    public void Pack_words_land_on_the_catalog_now_that_it_is_live()
    {
        // Slice 2's per-slice boost check (final review T4): each slice pins its own boost against the real index.
        foreach (var q in new[] { "widgets", "packs", "pack" })
        {
            var top = SettingsIndex.Search(q)[0];
            Assert.Equal((SettingsPage.Mods, SettingsIndex.Catalog, -1), (top.Entry.Page, top.Entry.Anchor, top.Rank));
        }
    }

    [Theory]
    [InlineData("hourglass", SettingsIndex.Catalog)]
    [InlineData("install", SettingsIndex.Catalog)]
    [InlineData("uninstall", SettingsIndex.ModList)]
    [InlineData("made here", SettingsIndex.ModList)]
    [InlineData("read-only", SettingsIndex.ModList)]
    [InlineData("sanduhr-meters", SettingsIndex.Meters)]
    public void Mods_and_Widgets_words_land_on_their_section(string query, string anchor)
    {
        Assert.Contains(SettingsIndex.Search(query), h => h.Entry.Page == SettingsPage.Mods && h.Entry.Anchor == anchor);
    }

    [Fact]
    public void Prose_that_names_the_page_reads_as_the_page()
    {
        // Slice 1 final review T1: "Settings, Mods & Widgets" read as a page "Mods" before the page had this title.
        Assert.Empty(SettingsNames.PathProblems("Install packs in Settings, Mods & Widgets, Catalog."));
        Assert.Empty(SettingsNames.PathProblems("Settings, Mods & Widgets"));
        Assert.NotEmpty(SettingsNames.PathProblems("Settings, Widgets"));
        Assert.Equal(["Mods & Config"], SettingsNames.RetiredNames("Open Mods & Config"));
        Assert.Equal(["Widgets settings…"], SettingsNames.RetiredNames("Widgets settings…"));
        Assert.Empty(SettingsNames.RetiredNames(SettingsNames.FilterWidgets));   // the chip may say "Widgets"
        Assert.Contains("Settings, Mods & Widgets", WhatsNewCards.Table.Single(c => c.Id == "widgets").Body);
    }
```

In `windows-dotnet/tests/Sanduhr.Tests/SettingsIndexTests.cs`, replace `Widgets_is_a_Desk_page_search_finds_by_its_words` with:

```csharp
    [Fact]
    public void Widget_words_find_Mods_and_Widgets()
    {
        Assert.Null(SettingsNames.GroupTitle(SettingsPage.Widgets));
        Assert.Equal("Extend", SettingsNames.GroupTitle(SettingsPage.Mods));
        Assert.Equal(SettingsPage.Mods, SettingsIndex.Search("widgets")[0].Entry.Page);
        Assert.Contains((SettingsNames.WidgetCatalogCheck, SettingsPage.Mods), SettingsNames.Table);
    }
```

In `windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs`: in `The_Desktop_group_holds_the_split_pages_in_the_specs_order`, drop `SettingsPage.Widgets` from the expected list; replace `A_live_page_keeps_an_anchor_no_alias_carries` with

```csharp
    [Fact]
    public void A_retired_pages_unknown_anchor_lands_where_the_page_went()
    {
        // Widgets left the sidebar in slice 4: an anchor no row carries lands on the page row's target.
        Assert.Equal(new SettingsLink(SettingsPage.Mods, SettingsIndex.Catalog), SettingsIndex.Resolve(SettingsPage.Widgets, "gone"));
        Assert.Equal(new SettingsLink(SettingsPage.Mods, SettingsIndex.Catalog), SettingsIndex.Check(SettingsPage.Widgets, "gone"));
    }
```

In `windows-dotnet/tests/Sanduhr.Tests/SettingsLinkTests.cs`, `Check_sends_an_old_place_to_where_it_lives_now`: replace the last block (from the comment `// An alias whose target isn't there yet does nothing` to its assert) with:

```csharp
        // Claude Code's meters landed on Mods & Widgets in slice 4: the alias is live.
        Assert.Equal(new SettingsLink(SettingsPage.Mods, SettingsIndex.Meters), SettingsIndex.Check(SettingsPage.ClaudeCode, SettingsIndex.Meters));
```

In `windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs`, `Titles_to_come_are_pinned_and_open_no_page_yet`: the expected list becomes `["Privacy"]` and its comment `// Desk Look, Watchers, Launcher landed in slice 3; Mods & Widgets in slice 4`. The assert on `SettingsNames.WidgetPackLine` (line 396) becomes `Assert.Equal("A widget pack is an add-on for Sanduhr: a theme, colors, a font or a companion. A mod is an add-on that Claude Code loads.", SettingsNames.ModsPageLine);` (the page's first line now defines both words, WF21).

In `windows-dotnet/tests/Sanduhr.Tests/SettingsSourceScanTests.cs`, rename `The_Desk_and_Widgets_pages_open_with_their_definitions` to `The_Desk_and_Mods_pages_open_with_their_definitions` and change its second assert to read `"core:SettingsNames.ModsPageLine"` in `ModsPage.xaml`.

Append to `windows-dotnet/tests/Sanduhr.App.Tests/ModsAndCardsPagesTests.cs`:

```csharp
    /// <summary>An offline network that counts what was asked of it.</summary>
    private sealed class CountingOffline : System.Net.Http.HttpMessageHandler
    {
        public int Count;
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Count);
            throw new System.Net.Http.HttpRequestException("offline in tests");
        }
    }

    [Fact]
    public void Landing_on_the_page_sends_no_request() => AppHost.Run(() =>
    {
        var net = new CountingOffline();
        Sanduhr.App.Services.WidgetCatalogService.HandlerOverride = () => net;
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Mods, SettingsIndex.Meters);
            Settle(window, guard);
            window.SelectPage(SettingsPage.Mods, SettingsIndex.ModList);
            Settle(window, guard);
            svm.ModsPage.FilterText = "palette";
            Settle(window, guard);
            Assert.Equal(0, net.Count);

            window.SelectPage(SettingsPage.Mods, SettingsIndex.Catalog);
            Settle(window, guard);
            var until = DateTime.UtcNow.AddSeconds(10);
            while ((svm.ModsPage.Busy || net.Count == 0) && DateTime.UtcNow < until) { Pump(); Thread.Sleep(15); }
            Assert.True(net.Count > 0, "opening the catalog asked nothing");
            Assert.True(svm.ModsPage.CatalogOpen);
            Assert.Equal(WidgetCatalogClient.Offline, svm.ModsPage.Status);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Old_widget_and_mods_links_land_on_the_new_page() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Widgets, SettingsIndex.WidgetsCatalog);
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Mods, SettingsIndex.Catalog), window.Place);
            Assert.True(Below<Expander>(window).Single(e => e.Name == "CatalogSection").IsExpanded);

            window.SelectPage(SettingsPage.Widgets);
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Mods, SettingsIndex.Catalog), window.Place);

            window.SelectPage(SettingsPage.ClaudeCode, SettingsIndex.Meters);
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Mods, SettingsIndex.Meters), window.Place);
            Assert.DoesNotContain(((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>(), t => (string)t.Tag == "Widgets");
        }
        finally { window.Close(); }
    });
```

In `windows-dotnet/tests/Sanduhr.App.Tests/SettingsWindowNamesTests.cs`, replace the three `seen["Mods"]` plugin asserts with:

```csharp
        Assert.Contains("alpha, version 1.2.0, on in 1 of 2 folders, read-only", seen["Mods"]);
        Assert.Contains("beta-mod, version 0.1.0, on in 0 of 2 folders, mod, read-only", seen["Mods"]);
        Assert.Contains("ghost, on in 0 of 2 folders, read-only", seen["Mods"]);
        Assert.Contains(@"~\.claude-personal, Not installed", seen["Mods"]);   // the meters row for the folder
```

In `windows-dotnet/tests/Sanduhr.App.Tests/ModsPageNamesTests.cs`, both facts select the tab by `(string)t.Tag == "Mods"` instead of `"Widgets"`. In `windows-dotnet/tests/Sanduhr.App.Tests/AppWindowFixture.cs`, delete the two `ModsConfigViewModel.ForgetSessionChoice();` lines.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsModsIndexTests"`
Expected: FAIL (`Widgets` is still in the sidebar; `SettingsIndex.Catalog` doesn't exist: a build error).

- [ ] **Step 3: The names and the index**

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`:

- `Sidebar`: `new("Extend", [SettingsPage.ClaudeCode, SettingsPage.Mods]),` stays; the Desktop group drops `SettingsPage.Widgets` (it ends `SettingsPage.Watchers, SettingsPage.Launcher]),`). The `Sidebar` summary's second sentence becomes `Settings v2 names the groups: "Extend" (Claude Code and Mods &amp; Widgets, which holds widget packs too) and "Desktop" (the Mac's word, and no longer its first page's title).`
- The comment above the titles becomes `// ---- Page titles added by Settings v2. Desk Look, Watchers and Launcher are pages since slice 3, Mods & Widgets since slice 4; Privacy (slice 6) is still to come.`, and `TitlesToCome` becomes `[PrivacyTitle]`.
- `PageTitle`: `SettingsPage.Mods => ModsAndWidgetsTitle,` (the `Widgets => "Widgets"` arm stays: retired pages keep their old title for aliases and logs).
- `Table`: `(WidgetCatalogCheck, SettingsPage.Widgets),` becomes `(WidgetCatalogCheck, SettingsPage.Mods),`.
- `Retired`: add, after `("Where each piece sits", PiecesSection, true),`:

```csharp
        ("Mods & Config", ModsAndWidgetsTitle, false),
        ("Widgets settings…", LinkTitle(SettingsPage.Mods), false),
```

- Delete `WidgetPackLine` and its summary (`ModsPageLine` replaces it on the page).

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`:

- Replace the `// Mods & Config` constants' comment with `// Mods & Widgets (slice 4): meters reuses Meters ("meters"), the Installed list keeps ModList ("mods")`, and add `public const string Catalog = "catalog";` below `Retention`. Keep `WidgetsCatalog`, `WidgetsCompanion`, `WidgetsInstalled` (old cards and aliases name them) and change their comment to `// Widgets (3.8), retired in slice 4: kept for the aliases`.
- Replace the Mods row of `Anchors` (`E("Plugins", SettingsPage.Mods, ModList, …)`) with:

```csharp
        E(SettingsNames.MetersSection, SettingsPage.Mods, Meters,
            ["meters", "mod", "sanduhr-meters", "band", "above the prompt", "CLAUDE_CODE_PLUGIN_DIRS", "turn on", "turn off", "per folder",
             "update", "remove"]),
        E(SettingsNames.InstalledSection, SettingsPage.Mods, ModList,
            ["installed", "mod", "mods", "plugin", "plugins", "widget pack", "theme pack", "palette", "font", "sprite", "effects",
             "uninstall", "made here", "my widgets", "use my own art", "read-only", "switch", "on", "off", "filter", "where it shows"]),
        E(SettingsNames.CatalogSection, SettingsPage.Mods, Catalog,
            ["catalog", "install", "download", "hourglass", "OLED Neon", "signature", "verified", "GitHub", "companion", "pet",
             "theme pack", "palette", "font", "sprite", "effects", "check the catalog"]),
```

- Delete the two Widgets rows of `Anchors` (`E("Widget catalog", SettingsPage.Widgets, …)` and `E("Installed widgets", SettingsPage.Widgets, …)`).
- `NameAnchors`: `[SettingsNames.WidgetCatalogCheck] = WidgetsCatalog,` becomes `[SettingsNames.WidgetCatalogCheck] = Catalog,`.
- `CollapsedSections`: add `[(SettingsPage.Mods, Catalog)] = SettingsNames.CatalogSection,`.
- `PageSynonyms`: the Mods row becomes `["mod", "plugin", "widget packs", "extensions", "add-ons"]`; delete the `SettingsPage.Widgets` row.
- The `Aliases` summary keeps its rows; nothing changes there (they go live by the sidebar change).

In `windows-dotnet/src/Sanduhr.Core/WhatsNewCards.cs`, the `"widgets"` card's body becomes `"Settings, Mods & Widgets installs packs from the 626 Labs catalog: sprites, themes, palettes and effects. Packs are data only, signed, and checked again before they install."` (the card id, count and anchor stay; `WhatsNewTests` pins them).

In `windows-dotnet/src/Sanduhr.Mcp/ToolCatalog.cs:752`, `"widget made here, listed in Settings, Widgets; companion true when they made a sprite the companion), and Dismiss " +` becomes `"widget made here, listed in Settings, Mods & Widgets, Installed; companion true when they made a sprite the companion), and Dismiss " +` (one string; no new reference, the MCP's link list is untouched).

- [ ] **Step 4: The page**

Replace the whole of `windows-dotnet/src/Sanduhr.App/Views/ModsPage.xaml` with (the two row templates from Tasks 9 and 11 are carried over as they are):

```xml
<UserControl x:Class="Sanduhr.App.Views.ModsPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:views="clr-namespace:Sanduhr.App.Views"
             xmlns:core="clr-namespace:Sanduhr.Core;assembly=Sanduhr.Core"
             xmlns:vm="clr-namespace:Sanduhr.App.ViewModels"
             xmlns:conv="clr-namespace:Sanduhr.App.Converters"
             xmlns:controls="clr-namespace:Sanduhr.App.Controls"
             Focusable="False">
    <!-- Settings, Mods & Widgets (Settings v2 slice 4, spec "Mods & Widgets: one list"): Meters above the prompt per folder, the one
         Installed list, and the collapsed Catalog that asks GitHub only when opened (WF3, WF6, WF9, WF39, WF40). Hosted in SettingsWindow,
         whose styles it finds with DynamicResource. DataContext: the ModsPageViewModel. -->
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BoolVis" />
        <conv:StringToVisibilityConverter x:Key="StrVis" />

        <Style x:Key="Dim" TargetType="TextBlock">
            <Setter Property="FontSize" Value="11" />
            <Setter Property="TextWrapping" Value="Wrap" />
            <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.TextDim}" />
        </Style>

        <Style x:Key="Heading" TargetType="TextBlock">
            <Setter Property="FontSize" Value="13" />
            <Setter Property="FontWeight" Value="SemiBold" />
            <Setter Property="Margin" Value="0,0,0,6" />
            <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.Text}" />
            <Setter Property="AutomationProperties.HeadingLevel" Value="Level2" />
        </Style>

        <!-- A pack's preview: pixel art, so nearest-neighbour, never smoothed. -->
        <Style x:Key="Preview" TargetType="Image">
            <Setter Property="Width" Value="48" />
            <Setter Property="Height" Value="48" />
            <Setter Property="Margin" Value="0,0,12,0" />
            <Setter Property="Stretch" Value="Uniform" />
            <Setter Property="VerticalAlignment" Value="Top" />
            <Setter Property="RenderOptions.BitmapScalingMode" Value="NearestNeighbor" />
        </Style>

        <!-- The Installed list's filter chips: Message's radio look, with its keyboard ring. -->
        <Style x:Key="FilterChip" TargetType="RadioButton">
            <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.Text}" />
            <Setter Property="FontSize" Value="12" />
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="Margin" Value="0,0,16,0" />
            <Setter Property="VerticalAlignment" Value="Center" />
            <Setter Property="FocusVisualStyle" Value="{x:Null}" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="RadioButton">
                        <StackPanel Orientation="Horizontal" Background="Transparent">
                            <Border x:Name="ring" Width="14" Height="14" CornerRadius="7" Margin="0,0,8,0" VerticalAlignment="Center"
                                    Background="{DynamicResource Sanduhr.Brush.Bg}" BorderBrush="{DynamicResource Sanduhr.Brush.Border}" BorderThickness="1">
                                <Ellipse x:Name="dot" Width="6" Height="6" Visibility="Collapsed" Fill="{DynamicResource Sanduhr.Brush.Accent}" />
                            </Border>
                            <ContentPresenter VerticalAlignment="Center" />
                        </StackPanel>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsChecked" Value="True">
                                <Setter TargetName="dot" Property="Visibility" Value="Visible" />
                                <Setter TargetName="ring" Property="BorderBrush" Value="{DynamicResource Sanduhr.Brush.Accent}" />
                            </Trigger>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="ring" Property="BorderBrush" Value="{DynamicResource Sanduhr.Brush.Accent}" />
                            </Trigger>
                            <Trigger Property="IsKeyboardFocused" Value="True">
                                <Setter TargetName="ring" Property="BorderThickness" Value="2" />
                                <Setter TargetName="ring" Property="BorderBrush" Value="{DynamicResource Sanduhr.Brush.Accent}" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- One folder under Meters above the prompt: its state, then only the controls that can act (decision 8). -->
        <DataTemplate DataType="{x:Type vm:MetersFolderRowViewModel}">
            <Border Background="{DynamicResource Sanduhr.Brush.Bg}" BorderBrush="{DynamicResource Sanduhr.Brush.Border}"
                    BorderThickness="1" CornerRadius="6" Padding="10,8" Margin="0,0,0,6">
                <StackPanel>
                    <WrapPanel>
                        <TextBlock Text="{Binding Display}" FontFamily="Cascadia Code, Consolas" FontSize="12" VerticalAlignment="Center"
                                   Margin="0,0,12,0" Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                        <TextBlock Text="{Binding StateText}" FontSize="11" VerticalAlignment="Center" Margin="0,0,12,0"
                                   AutomationProperties.LiveSetting="Polite" Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                        <CheckBox Style="{DynamicResource ThemedCheckBox}" Content="{x:Static core:SettingsNames.PackOn}" VerticalAlignment="Center"
                                  IsChecked="{Binding IsOn, Mode=OneWay}" Command="{Binding ToggleCommand}" Margin="0,0,12,0"
                                  Visibility="{Binding CanSwitch, Converter={StaticResource BoolVis}}"
                                  AutomationProperties.Name="{Binding SwitchName}" />
                        <Button Style="{DynamicResource FlatButton}" Content="Update…" Padding="10,4" FontSize="11" Margin="0,0,6,0"
                                Command="{Binding UpdateCommand}" Visibility="{Binding CanUpdate, Converter={StaticResource BoolVis}}"
                                AutomationProperties.Name="{Binding UpdateName}" />
                        <Button Style="{DynamicResource FlatButton}" Content="Remove…" Padding="10,4" FontSize="11"
                                Foreground="{DynamicResource Sanduhr.Brush.PaceMarker}"
                                Command="{Binding RemoveCommand}" Visibility="{Binding CanRemove, Converter={StaticResource BoolVis}}"
                                AutomationProperties.Name="{Binding RemoveName}" />
                    </WrapPanel>
                    <TextBlock Text="{Binding Reason}" Style="{StaticResource Dim}" Margin="0,4,0,0"
                               Visibility="{Binding HasReason, Converter={StaticResource BoolVis}}" />
                </StackPanel>
            </Border>
        </DataTemplate>

        <!-- An Installed row (Task 9). -->
        <DataTemplate DataType="{x:Type vm:InstalledRowViewModel}">
            <Border Background="{DynamicResource Sanduhr.Brush.Bg}" BorderBrush="{DynamicResource Sanduhr.Brush.Border}"
                    BorderThickness="1" CornerRadius="6" Padding="10,8" Margin="0,0,0,8"
                    AutomationProperties.Name="{Binding RowName}">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <Image Style="{StaticResource Preview}" Source="{Binding Preview}"
                           Visibility="{Binding HasPreview, Converter={StaticResource BoolVis}}"
                           AutomationProperties.Name="{Binding Name, StringFormat='Preview of {0}'}" />
                    <StackPanel Grid.Column="1">
                        <TextBlock Text="{Binding Name}" FontSize="12" FontWeight="SemiBold" Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                        <TextBlock Text="{Binding Detail}" FontSize="11" Margin="0,2,0,0" Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                        <TextBlock Text="{Binding Description}" Style="{StaticResource Dim}" Margin="0,2,0,0"
                                   Visibility="{Binding HasDescription, Converter={StaticResource BoolVis}}" />
                        <!-- Where it shows (WF3): the place and one click to it. -->
                        <WrapPanel Margin="0,4,0,0" Visibility="{Binding HasWhere, Converter={StaticResource BoolVis}}">
                            <TextBlock Text="{Binding WhereText}" Style="{StaticResource Dim}" VerticalAlignment="Center" Margin="0,0,8,0" />
                            <Button Style="{DynamicResource SettingsLinkButton}" Content="{Binding WhereLinkTitle}"
                                    views:SettingsLinkTo.Link="{Binding WhereLink}" AutomationProperties.Name="{Binding WhereLinkName}"
                                    Visibility="{Binding HasWhereLink, Converter={StaticResource BoolVis}}" />
                        </WrapPanel>
                        <!-- A Claude Code item: its scope; Sanduhr's mod links up to its switches, the others say why they can't switch. -->
                        <TextBlock Text="{Binding ScopeText}" FontSize="11" Margin="0,4,0,0" Foreground="{DynamicResource Sanduhr.Brush.Text}"
                                   Visibility="{Binding HasScope, Converter={StaticResource BoolVis}}" />
                        <TextBlock Text="{Binding ReadOnlyText}" Style="{StaticResource Dim}" Margin="0,2,0,0"
                                   Visibility="{Binding IsReadOnly, Converter={StaticResource BoolVis}}" />
                        <Button Style="{DynamicResource SettingsLinkButton}" Content="{Binding MetersLinkTitle}" HorizontalAlignment="Left" Margin="0,4,0,0"
                                views:SettingsLinkTo.Link="{Binding MetersLink}" AutomationProperties.Name="{Binding MetersLinkTitle}"
                                Visibility="{Binding IsSanduhrMod, Converter={StaticResource BoolVis}}" />
                    </StackPanel>
                    <StackPanel Grid.Column="2" VerticalAlignment="Top" Margin="8,0,0,0" Visibility="{Binding IsPack, Converter={StaticResource BoolVis}}">
                        <CheckBox Style="{DynamicResource ThemedCheckBox}" Content="{x:Static core:SettingsNames.PackOn}"
                                  IsChecked="{Binding Enabled, Mode=OneWay}" Command="{Binding ToggleCommand}"
                                  AutomationProperties.Name="{Binding SwitchName}" />
                        <Button Style="{DynamicResource FlatButton}" Margin="0,6,0,0"
                                Content="{x:Static core:SettingsNames.WidgetUninstall}" Command="{Binding UninstallCommand}"
                                IsEnabled="{Binding DataContext.Busy, RelativeSource={RelativeSource AncestorType=UserControl}, Converter={x:Static conv:Bools.Not}}"
                                AutomationProperties.Name="{Binding UninstallName}" />
                        <!-- Spec 3.8 section 6: a sprite made here takes the user's own PNG sheets; they stay on this PC. -->
                        <Button Style="{DynamicResource FlatButton}" Margin="0,6,0,0"
                                Content="{x:Static core:SettingsNames.WidgetUseOwnArt}" Command="{Binding UseOwnArtCommand}"
                                Visibility="{Binding CanUseOwnArt, Converter={StaticResource BoolVis}}"
                                IsEnabled="{Binding DataContext.Busy, RelativeSource={RelativeSource AncestorType=UserControl}, Converter={x:Static conv:Bools.Not}}"
                                AutomationProperties.Name="{Binding UseOwnArtName}"
                                AutomationProperties.HelpText="Choose a PNG sprite sheet for each stage. It is copied into the widget and never sent anywhere." />
                    </StackPanel>
                </Grid>
            </Border>
        </DataTemplate>

        <!-- A Catalog row (Task 11). -->
        <DataTemplate DataType="{x:Type vm:CatalogRowViewModel}">
            <Border Background="{DynamicResource Sanduhr.Brush.Bg}" BorderBrush="{DynamicResource Sanduhr.Brush.Border}"
                    BorderThickness="1" CornerRadius="6" Padding="10,8" Margin="0,0,0,8"
                    AutomationProperties.Name="{Binding RowName}">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <Image Style="{StaticResource Preview}" Source="{Binding Preview}"
                           Visibility="{Binding HasPreview, Converter={StaticResource BoolVis}}"
                           AutomationProperties.Name="{Binding Name, StringFormat='Preview of {0}'}" />
                    <StackPanel Grid.Column="1">
                        <TextBlock Text="{Binding Name}" FontSize="12" FontWeight="SemiBold" Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                        <TextBlock Text="{Binding Detail}" FontSize="11" Margin="0,2,0,0" Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                        <TextBlock Text="{Binding Description}" Style="{StaticResource Dim}" Margin="0,2,0,0" />
                        <TextBlock Text="{Binding State}" Style="{StaticResource Dim}" Margin="0,2,0,0" />
                    </StackPanel>
                    <StackPanel Grid.Column="2" VerticalAlignment="Center" Margin="8,0,0,0">
                        <Button Style="{DynamicResource FlatButton}" Content="{Binding ActionLabel}" Command="{Binding ActCommand}"
                                Visibility="{Binding HasButton, Converter={StaticResource BoolVis}}"
                                IsEnabled="{Binding DataContext.Busy, RelativeSource={RelativeSource AncestorType=UserControl}, Converter={x:Static conv:Bools.Not}}"
                                AutomationProperties.Name="{Binding ActionName}" />
                        <CheckBox Style="{DynamicResource ThemedCheckBox}" Content="{x:Static core:SettingsNames.PackOn}"
                                  IsChecked="{Binding Enabled, Mode=OneWay}" Command="{Binding ToggleCommand}"
                                  Visibility="{Binding IsSwitch, Converter={StaticResource BoolVis}}"
                                  AutomationProperties.Name="{Binding SwitchName}" />
                    </StackPanel>
                </Grid>
            </Border>
        </DataTemplate>
    </UserControl.Resources>

    <Border Background="{DynamicResource Sanduhr.Brush.Glass}" CornerRadius="8">
        <ScrollViewer VerticalScrollBarVisibility="Auto" Focusable="False">
            <ScrollViewer.Resources>
                <Style TargetType="ScrollBar" BasedOn="{StaticResource SlimThemeScrollBar}" />
            </ScrollViewer.Resources>
            <StackPanel x:Name="PageBody" Margin="20">
                <TextBlock Text="{x:Static core:SettingsNames.ModsAndWidgetsTitle}" FontSize="14" FontWeight="SemiBold"
                           AutomationProperties.HeadingLevel="Level1" Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,4" />
                <TextBlock Text="{x:Static core:SettingsNames.ModsPageLine}" Style="{StaticResource Dim}" Margin="0,0,0,4" />
                <TextBlock Text="{x:Static core:SettingsNames.ModsIntro}" FontSize="11" TextWrapping="Wrap" Margin="0,0,0,14"
                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />

                <!-- 1. Meters above the prompt: the sanduhr-meters mod, every folder at once (WF9). -->
                <StackPanel views:SettingsAnchor.Id="meters">
                    <TextBlock Text="{x:Static core:SettingsNames.MetersSection}" Style="{StaticResource Heading}" />
                    <TextBlock Text="{Binding MetersCaption}" Style="{StaticResource Dim}" Margin="0,0,0,8" />
                    <TextBlock Text="No Claude Code folder found. Add one in Claude Code settings…" Style="{DynamicResource EmptyLine}"
                               Visibility="{Binding HasMeterFolders, Converter={x:Static conv:Bools.NotVisible}}" />
                    <ItemsControl ItemsSource="{Binding Meters}" Focusable="False" AutomationProperties.Name="{x:Static core:SettingsNames.MetersSection}">
                        <ItemsControl.ItemContainerStyle>
                            <Style TargetType="ContentPresenter">
                                <Setter Property="AutomationProperties.Name" Value="{Binding RowName}" />
                            </Style>
                        </ItemsControl.ItemContainerStyle>
                    </ItemsControl>
                    <TextBlock Text="{Binding MetersStatus}" FontSize="11" TextWrapping="Wrap" Margin="0,4,0,0" AutomationProperties.LiveSetting="Polite"
                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                               Visibility="{Binding MetersStatus, Converter={StaticResource StrVis}}" />
                </StackPanel>

                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,14,0,14" />

                <!-- 2. Installed: the one list (keeps the "mods" id). -->
                <StackPanel views:SettingsAnchor.Id="mods">
                    <TextBlock Text="{x:Static core:SettingsNames.InstalledSection}" Style="{StaticResource Heading}" />
                    <DockPanel Margin="0,0,0,10" LastChildFill="True">
                        <controls:RadioGroupPanel DockPanel.Dock="Left" Orientation="Horizontal" AutomationProperties.Name="Show">
                            <RadioButton Style="{StaticResource FilterChip}" GroupName="InstalledFilter" Content="{x:Static core:SettingsNames.FilterAll}"
                                         IsChecked="{Binding IsFilterAll, Mode=TwoWay}" AutomationProperties.Name="{x:Static core:SettingsNames.FilterAll}" />
                            <RadioButton Style="{StaticResource FilterChip}" GroupName="InstalledFilter" Content="{x:Static core:SettingsNames.FilterClaudeCode}"
                                         IsChecked="{Binding IsFilterClaudeCode, Mode=TwoWay}" AutomationProperties.Name="{x:Static core:SettingsNames.FilterClaudeCode}" />
                            <RadioButton Style="{StaticResource FilterChip}" GroupName="InstalledFilter" Content="{x:Static core:SettingsNames.FilterWidgets}"
                                         IsChecked="{Binding IsFilterWidgets, Mode=TwoWay}" AutomationProperties.Name="{x:Static core:SettingsNames.FilterWidgets}" />
                        </controls:RadioGroupPanel>
                        <TextBox Style="{DynamicResource Input}" MinWidth="160" Text="{Binding FilterText, UpdateSourceTrigger=PropertyChanged}"
                                 AutomationProperties.Name="{x:Static core:SettingsNames.FilterBoxName}" />
                    </DockPanel>
                    <TextBlock Style="{DynamicResource EmptyLine}"
                               Visibility="{Binding HasInstalled, Converter={x:Static conv:Bools.NotVisible}}"
                               Text="Nothing installed yet. Widgets you make with Claude will show here too, marked made here." />
                    <TextBlock Style="{DynamicResource EmptyLine}" Text="{x:Static core:SettingsNames.NothingMatches}"
                               Visibility="{Binding NothingMatches, Converter={StaticResource BoolVis}}" />
                    <ItemsControl ItemsSource="{Binding Installed}" Focusable="False" AutomationProperties.Name="{x:Static core:SettingsNames.InstalledSection}" />
                    <TextBlock Text="{Binding Note}" Style="{StaticResource Dim}" Margin="0,4,0,0" Visibility="{Binding Note, Converter={StaticResource StrVis}}" />
                    <TextBlock x:Name="StatusLine" Text="{Binding Status}" FontSize="11" TextWrapping="Wrap" Margin="0,6,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" AutomationProperties.LiveSetting="Polite"
                               Visibility="{Binding Status, Converter={StaticResource StrVis}}" />
                </StackPanel>

                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,14,0,8" />

                <!-- 3. Catalog: collapsed; opening it is the only time Sanduhr asks GitHub (WF6, WF40). -->
                <Expander x:Name="CatalogSection" Style="{DynamicResource SectionExpander}" views:SettingsAnchor.Id="catalog"
                          Header="{x:Static core:SettingsNames.CatalogSection}" IsExpanded="{Binding CatalogOpen, Mode=TwoWay}"
                          AutomationProperties.Name="{x:Static core:SettingsNames.CatalogSection}"
                          AutomationProperties.HelpText="{x:Static core:SettingsNames.CatalogDisclosure}">
                    <StackPanel>
                        <TextBlock Text="{Binding CatalogDisclosure}" FontSize="11" TextWrapping="Wrap" Margin="0,0,0,6"
                                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                        <TextBlock Style="{StaticResource Dim}" Margin="0,0,0,8"
                                   Text="Sprites, themes, palettes, effects and fonts from the 626 Labs widget catalog. Packs are data only, no code. Sanduhr checks the catalog's signature, then each pack's size, hash and every file before it installs one." />
                        <Button Style="{DynamicResource FlatButton}" HorizontalAlignment="Left" Margin="0,0,0,8"
                                Content="{x:Static core:SettingsNames.WidgetCatalogCheck}" Command="{Binding CheckAgainCommand}"
                                IsEnabled="{Binding Busy, Converter={x:Static conv:Bools.Not}}"
                                AutomationProperties.Name="{x:Static core:SettingsNames.WidgetCatalogCheck}" />
                        <WrapPanel Margin="0,0,0,8" Visibility="{Binding ShowingOneKind, Converter={StaticResource BoolVis}}">
                            <TextBlock Text="{x:Static core:SettingsNames.ShowingSprites}" Style="{StaticResource Dim}" VerticalAlignment="Center" Margin="0,0,8,0" />
                            <Button Style="{DynamicResource SettingsLinkButton}" Content="{x:Static core:SettingsNames.ShowEveryPack}"
                                    Command="{Binding ShowEveryPackCommand}" AutomationProperties.Name="{x:Static core:SettingsNames.ShowEveryPack}" />
                        </WrapPanel>
                        <ItemsControl ItemsSource="{Binding Catalog}" Focusable="False" AutomationProperties.Name="Widget packs in the catalog" />
                    </StackPanel>
                </Expander>

                <TextBlock Style="{StaticResource Dim}" Margin="0,12,0,0"
                           Text="Installed packs live in Sanduhr's own widgets folder on this PC. Opening the catalog asks GitHub for it, and Install downloads the pack from GitHub; nothing else is sent. Reset Sanduhr removes the folder." />

                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />
                <views:TrademarkFooter />
            </StackPanel>
        </ScrollViewer>
    </Border>
</UserControl>
```

The Installed and Catalog row templates are Task 9's and Task 11's, unchanged. (The "No Claude Code folder found" line is prose; "Claude Code settings…" in it names a page, which `UnknownLinks` accepts.)

In `windows-dotnet/src/Sanduhr.App/Views/ModsPage.xaml.cs`, the class summary becomes `Settings, Mods &amp; Widgets (Settings v2 slice 4). Each time the page becomes visible it lists what is installed; the catalog is fetched only when its section opens.`

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`:

- Replace the whole Mods &amp; Config TabItem (with its comment) with:

```xml
                <!-- Settings, Mods & Widgets (slice 4): the one list, drawn by its own control. -->
                <TabItem Header="Mods &amp; Widgets" Tag="Mods" AutomationProperties.Name="Mods &amp; Widgets" DataContext="{Binding ModsPage}">
                    <views:ModsPage />
                </TabItem>
```

- Delete the Widgets TabItem and its comment.

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs`: `ViewModel.Mods.AttachOwner(this);` becomes `ViewModel.ModsPage.AttachOwner(this);`; in the tab selection handler, delete `else if (page == SettingsPage.Mods) ViewModel.Mods.ReloadFolders();` (the page lists itself when it becomes visible) and change the comment above to `// The Claude Code page reads files other tools change: look again each time it is opened.`

In `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`:

- Delete the `Mods` property and its summary, `Mods = new ModsConfigViewModel(widget);`, and the three lines under `// The two pages share one folder list and one set of installed pieces.`; in their place add:

```csharp
        // The two pages share one folder list and one set of installed pieces: a card's change reloads Mods & Widgets, and a meters change
        // there reloads the cards and the snapshot switch (wired in the ModsPage setter, so a page a test puts in is wired too).
        ClaudeCodePage.FoldersChanged += () => ModsPage.ReloadLocal();
        ClaudeCodePage.PiecesChanged += () => ModsPage.ReloadLocal();
```

- In the `ModsPage` setter, after `value.SettingsChanged = PacksChanged;`, add `value.MetersChanged = installed => ClaudeCodePage.AfterMetersChange(installed);`.

Move `public sealed record ModsLine(…)` (with its summary) from `ModsConfigViewModel.cs` to the end of `FolderHoldsViewModel.cs`, then delete `ModsConfigViewModel.cs`:

```bash
git rm windows-dotnet/src/Sanduhr.App/ViewModels/ModsConfigViewModel.cs windows-dotnet/tests/Sanduhr.App.Tests/ModsConfigFixture.cs windows-dotnet/tests/Sanduhr.App.Tests/ModsConfigFolderTests.cs windows-dotnet/tests/Sanduhr.App.Tests/ModsConfigPluginsTests.cs
```

(Their facts live on: the folder choice in `ClaudeCodeFolderChoiceTests` and `ModsFolderChoiceTests`, the report in `FolderHoldsTests`, the plugin rows and the note in `InstalledItemsTests`, `ModsPageListTests` and `ModsPageTextTests`.) Then `git grep -n "ModsConfig" -- windows-dotnet` prints only `ConfigMaskTests.cs`'s mention of the Mac's `ModsConfigGroundTests`.

- [ ] **Step 5: Run them to see them pass**

Run: `dotnet build Sanduhr.slnx` (from `windows-dotnet/`): 0 errors, the harness included.
Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~Settings|FullyQualifiedName~WhatsNew|FullyQualifiedName~WelcomeTour"`
Run: `dotnet test tests/Sanduhr.Mcp.Tests`
Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~ModsAndCardsPagesTests|FullyQualifiedName~SettingsWindowNamesTests|FullyQualifiedName~ModsPageNamesTests|FullyQualifiedName~ModsPage|FullyQualifiedName~SettingsReachTests|FullyQualifiedName~DeskSplitPagesTests|FullyQualifiedName~SidebarTabControlAutomationTests|FullyQualifiedName~ClaudeCode"`
Expected: PASS, the `[DesktopFact]`s included. `Every_search_anchor_is_marked_in_its_pages_TabItem` finds `meters`, `mods` and `catalog` in `ModsPage.xaml`; `Every_Settings_card_names_an_anchor_in_the_index` carries the three Widgets cards through the live aliases; `Every_radio_in_Settings_sits_in_a_named_group` reads the chips' `RadioGroupPanel`. A failure names what it found; fix it and run once more.

- [ ] **Step 6: Commit**

```bash
git add -A windows-dotnet/src windows-dotnet/tests
git commit -m "feat(settings-v2): Mods & Widgets takes the Mods tab; Widgets and Mods & Config retire, and every old link lands on the new page"
```

(Check `git status` first: only the files this task names are staged.)

---

### Task 13: The companion row and Themes point at Mods & Widgets

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (`GetACompanion`, `NoCompanionYet`'s summary)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/DeskCompanionViewModel.cs` (`GetOneLink`, `GetOne`), `Views/DeskPiecesPanel.xaml` (the button's command), `ViewModels/ThemesViewModel.cs` (the theme packs line), `Views/SettingsWindow.xaml` (Themes, row 0), `ViewModels/SettingsViewModel.cs` (wiring, `PacksChanged`)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/ModsAndCardsPagesTests.cs`, `PackSwitchConsumersTests.cs`; `windows-dotnet/tests/Sanduhr.Tests/SettingsModsIndexTests.cs`

**Interfaces:**
- Consumes: `ModsPageViewModel.ShowSprites()` (Task 11), `ShowWidgetPacks()` (Task 9), `SettingsIndex.Catalog`, `ModList` (Task 12), `SettingsNames.ThemePacksLine` (Task 2).
- Produces: `DeskCompanionViewModel.GetOneLink == "mods#catalog"`, `public Action? GetOne { get; set; }`, `GetOneClickedCommand`; `ThemesViewModel`: `[ObservableProperty] string ThemePacksLine`, `string PacksLink` (`"mods#mods"`), `string PacksLinkTitle`, `public Action? ShowPacks { get; set; }`, `ShowPacksClickedCommand`, `public void RefreshThemePacks()`, `internal Func<WidgetStore> Widgets { get; set; }`.

- [ ] **Step 1: Write the failing tests**

Append to `SettingsModsIndexTests`:

```csharp
    [Fact]
    public void The_companion_row_names_the_new_page() =>
        Assert.Equal("Get one in Mods & Widgets…", SettingsNames.GetACompanion);
```

Append to `PackSwitchConsumersTests`:

```csharp
    [Fact]
    public void Themes_counts_the_theme_packs_and_follows_a_change() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        svm.PickerFonts = () => ["EsteFont Pro"];
        Assert.Equal(SettingsNames.ThemePacksLine(0), svm.Themes.ThemePacksLine);

        InstalledPackKit.Install(new Paths(), "good-theme");
        svm.PacksChanged();

        Assert.Equal(SettingsNames.ThemePacksLine(1), svm.Themes.ThemePacksLine);
        Assert.Equal("mods#mods", svm.Themes.PacksLink);
        Assert.Equal(SettingsNames.LinkTitle(SettingsPage.Mods), svm.Themes.PacksLinkTitle);
    });
```

Append to `ModsAndCardsPagesTests`:

```csharp
    private static void Press(Button button) =>
        ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(button)
            .GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();

    [Fact]
    public void The_companions_get_one_link_lands_on_the_catalog_showing_sprites() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk, SettingsIndex.DeskPieces);
            Settle(window, guard);
            var link = Below<Button>(window).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == SettingsNames.GetACompanion);

            Press(link);
            Settle(window, guard);

            Assert.Equal(new SettingsLink(SettingsPage.Mods, SettingsIndex.Catalog), window.Place);
            Assert.Equal("sprite", svm.ModsPage.CatalogKind);
            Assert.True(svm.ModsPage.CatalogOpen);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Themes_link_lands_on_the_installed_list_showing_widget_packs() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Themes);
            Settle(window, guard);
            var link = Below<Button>(window).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == SettingsNames.LinkTitle(SettingsPage.Mods));
            Assert.Contains(Below<TextBlock>(window), t => t.Text == SettingsNames.ThemePacksLine(0));

            Press(link);
            Settle(window, guard);

            Assert.Equal(new SettingsLink(SettingsPage.Mods, SettingsIndex.ModList), window.Place);
            Assert.Equal(InstalledFilter.Widgets, svm.ModsPage.Filter);
        }
        finally { window.Close(); }
    });
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsModsIndexTests.The_companion_row_names_the_new_page`
Expected: FAIL: `Assert.Equal() Failure: Expected: "Get one in Mods & Widgets…" Actual: "Get one in Widgets…"`.

- [ ] **Step 3: Point both at the page**

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, change

```csharp
    public static string GetACompanion => $"Get one in {PageTitle(SettingsPage.Widgets)}…";
```

to

```csharp
    public static string GetACompanion => $"Get one in {PageTitle(SettingsPage.Mods)}…";
```

and the summary above `NoCompanionYet` to `Desk, Pieces, the Companion row with no sprite installed. The link opens Mods &amp; Widgets' catalog showing sprites (slice 4).`

In `windows-dotnet/src/Sanduhr.App/ViewModels/DeskCompanionViewModel.cs`, replace the `GetOneLink` line and its summary with:

```csharp
    /// <summary>The catalog, where a sprite is installed: Mods &amp; Widgets, Catalog, showing sprites (spec, Desk: the Companion row).</summary>
    public string GetOneLink => SettingsIndex.PageId(SettingsPage.Mods) + "#" + SettingsIndex.Catalog;

    /// <summary>Set by Settings: the catalog shows sprites only (the link itself opens the place).</summary>
    public Action? GetOne { get; set; }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void GetOneClicked() => GetOne?.Invoke();
```

In `windows-dotnet/src/Sanduhr.App/Views/DeskPiecesPanel.xaml`, the "Get one" button gains `Command="{Binding GetOneClickedCommand}"` (it keeps `views:SettingsLinkTo.Link="{Binding GetOneLink}"`: the window opens the place on Click, then the command filters).

In `windows-dotnet/src/Sanduhr.App/ViewModels/ThemesViewModel.cs`, add (and call `RefreshThemePacks();` at the end of the constructor):

```csharp
    /// <summary>The installed widget packs (Settings' own folder). A seam for tests.</summary>
    internal Func<WidgetStore> Widgets { get; set; } =
        () => new WidgetStore(new Sanduhr.Core.Paths().WidgetsDir, Sanduhr.App.Services.WidgetCatalogService.EncodePng);

    /// <summary>"Theme packs: 2 installed." above the gallery, with "Mods &amp; Widgets settings…" (spec, Themes; WF40). Packs that are off count.</summary>
    [ObservableProperty] private string _themePacksLine = "";

    public string PacksLink => SettingsIndex.PageId(SettingsPage.Mods) + "#" + SettingsIndex.ModList;
    public string PacksLinkTitle => SettingsNames.LinkTitle(SettingsPage.Mods);

    /// <summary>Set by Settings: the Installed list shows widget packs (the link itself opens the place).</summary>
    public Action? ShowPacks { get; set; }

    [RelayCommand]
    private void ShowPacksClicked() => ShowPacks?.Invoke();

    /// <summary>Counts the theme packs again (a pack was installed, removed or switched).</summary>
    public void RefreshThemePacks()
    {
        int n = 0;
        try
        {
            var store = Widgets();
            n = store.Load().Packs.Count(p => store.ReadInfo(p.Id)?.Kind == "theme");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        ThemePacksLine = SettingsNames.ThemePacksLine(n);
    }
```

(`using System.IO;` and `using Sanduhr.Core;` if the file lacks them; `RelayCommand` is CommunityToolkit's, already used there.)

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`, the Themes TabItem: replace

```xml
                            <TextBlock Grid.Row="0" Text="Choose a theme" FontSize="11" views:SettingsAnchor.Id="gallery"
                                       FontWeight="SemiBold" Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                       Margin="0,0,0,8" />
```

with

```xml
                            <StackPanel Grid.Row="0">
                                <!-- Theme packs (WF40): where they come from, one click away. -->
                                <WrapPanel Margin="0,0,0,8">
                                    <TextBlock Text="{Binding ThemePacksLine}" FontSize="11" VerticalAlignment="Center" Margin="0,0,8,0"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                    <Button Style="{StaticResource SettingsLinkButton}" Content="{Binding PacksLinkTitle}"
                                            views:SettingsLinkTo.Link="{Binding PacksLink}" Command="{Binding ShowPacksClickedCommand}"
                                            AutomationProperties.Name="{Binding PacksLinkTitle}" />
                                </WrapPanel>
                                <TextBlock Text="Choose a theme" FontSize="11" views:SettingsAnchor.Id="gallery"
                                           FontWeight="SemiBold" Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                           Margin="0,0,0,8" />
                            </StackPanel>
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`, after the two `ClaudeCodePage` wiring lines, add:

```csharp
        // The companion row's "Get one in Mods & Widgets…" shows sprites; Themes' link shows widget packs (spec, Menus).
        Desk.Companion.GetOne = () => ModsPage.ShowSprites();
        Themes.ShowPacks = () => ModsPage.ShowWidgetPacks();
```

and in `PacksChanged`, before `Desk.Reload();`, add `Themes.RefreshThemePacks();`.

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsModsIndexTests|FullyQualifiedName~SettingsDeskSplitIndexTests|FullyQualifiedName~SettingsSourceScanTests"`
Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~ModsAndCardsPagesTests|FullyQualifiedName~PackSwitchConsumersTests|FullyQualifiedName~DeskPiecesSettingsTests|FullyQualifiedName~ThemeGalleryAutomationTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.App/ViewModels/DeskCompanionViewModel.cs windows-dotnet/src/Sanduhr.App/Views/DeskPiecesPanel.xaml windows-dotnet/src/Sanduhr.App/ViewModels/ThemesViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/tests/Sanduhr.Tests/SettingsModsIndexTests.cs windows-dotnet/tests/Sanduhr.App.Tests/ModsAndCardsPagesTests.cs windows-dotnet/tests/Sanduhr.App.Tests/PackSwitchConsumersTests.cs
git commit -m "feat(settings-v2): the companion's Get one lands on the catalog showing sprites; Themes counts its packs and links to them"
```

---

### Task 14: The Done criteria, and the two pages by keyboard and screen reader

**Files:**
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/ModsAndCardsPagesTests.cs`
- Modify only if a test finds a gap: `Views/ModsPage.xaml`, `Views/ClaudeCodePage.xaml`

**Interfaces:**
- Consumes: everything above.
- Produces: the spec's Done line as tests ("an installed palette is one click from where it shows; switching a pack off removes it from its picker; no row shows a control that can't act"), and the pages' keyboard and naming checks.

- [ ] **Step 1: Write the tests**

Append to `ModsAndCardsPagesTests`:

```csharp
    private static string Bare(string label) => label.Trim().TrimEnd('…').TrimEnd(':').Trim();

    /// <summary>Done (spec, slice 4): an installed palette is one click from where it shows.</summary>
    [Fact]
    public void An_installed_palette_is_one_click_from_where_it_shows() => AppHost.Run(() =>
    {
        InstalledPackKit.Install(new Paths(), "good-palette");   // the dev profile's widgets folder (SANDUHR_DEV_PROFILE)
        var (window, svm, guard) = Open("Personal");
        try
        {
            svm.Desk.InkChoice = DeskSettingsViewModel.InkChoices.First(c => c.Mode == DeskInkMode.Light);
            window.SelectPage(SettingsPage.Mods, SettingsIndex.ModList);
            Settle(window, guard);
            var link = Below<Button>(window).Single(b =>
                System.Windows.Automation.AutomationProperties.GetName(b) == $"{SettingsNames.LinkTitle(SettingsPage.DeskLook)} for Fixture palette");

            Press(link);
            Settle(window, guard);

            Assert.Equal(new SettingsLink(SettingsPage.DeskLook, SettingsIndex.DeskInk), window.Place);
            var presets = Below<ComboBox>(window).Where(c => c.IsVisible)
                .Single(c => c.ItemsSource is IEnumerable<string> items && items.Contains("plain · Fixture palette"));
            Assert.True(presets.IsVisible);
        }
        finally { window.Close(); }
    });

    /// <summary>Done (spec, slice 4): switching a pack off removes it from its picker, with no reopen.</summary>
    [Fact]
    public void Switching_a_pack_off_removes_it_from_its_picker() => AppHost.Run(() =>
    {
        InstalledPackKit.Install(new Paths(), "good-palette");
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Mods, SettingsIndex.ModList);
            Settle(window, guard);
            Assert.Contains("plain · Fixture palette", svm.Desk.InkPresetNames);
            var on = Below<CheckBox>(window).First(c => System.Windows.Automation.AutomationProperties.GetName(c) == SettingsNames.PackOnFor("Fixture palette"));

            on.Command.Execute(on.CommandParameter);   // what a click runs: the bound command
            Settle(window, guard);

            Assert.False(on.IsChecked);
            Assert.DoesNotContain("plain · Fixture palette", svm.Desk.InkPresetNames);
            Assert.False(InstalledPackKit.Store(new Paths()).Load().Find("good-palette")!.Enabled);
        }
        finally { window.Close(); }
    });

    /// <summary>Done (spec, slice 4): no row shows a control that can't act (Principle 5). A read-only row has no control; an unreadable folder's
    /// meters row has no switch; every control shown is enabled.</summary>
    [Fact]
    public void No_row_shows_a_control_that_cant_act() => AppHost.Run(() =>
    {
        var personal = Home(".claude-personal", "{\"enabledPlugins\":{\"alpha@m\":true}}");
        var cache = Path.Combine(Root, "cache", "alpha");
        Directory.CreateDirectory(Path.Combine(cache, ".claude-plugin"));
        File.WriteAllText(Path.Combine(cache, ".claude-plugin", "plugin.json"), """{"name":"alpha","version":"1.0.0"}""");
        Directory.CreateDirectory(Path.Combine(personal, "plugins"));
        File.WriteAllText(Path.Combine(personal, "plugins", "installed_plugins.json"),
            "{\"version\":2,\"plugins\":{\"alpha@m\":[{\"installPath\":\"" + cache.Replace("\\", "\\\\") + "\",\"version\":\"1.0.0\"}]}}");
        Home(".claude-broken", "{ nope");
        InstalledPackKit.Install(new Paths(), "good-effects");
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Mods, SettingsIndex.ModList);
            Settle(window, guard);
            var page = Below<ModsPage>(window).Single();

            foreach (var row in Below<ContentPresenter>(page).Where(p => p.Content is InstalledRowViewModel { IsReadOnly: true }))
                Assert.Empty(Below<System.Windows.Controls.Primitives.ButtonBase>(row).Where(b => b.IsVisible));
            foreach (var row in Below<ContentPresenter>(page).Where(p => p.Content is MetersFolderRowViewModel { CanSwitch: false }))
                Assert.Empty(Below<System.Windows.Controls.Primitives.ButtonBase>(row).Where(b => b.IsVisible));
            var effects = Below<ContentPresenter>(page).Single(p => p.Content is InstalledRowViewModel { Id: "good-effects" });
            Assert.DoesNotContain(Below<Button>(effects), b => b.IsVisible && b.Style == window.TryFindResource("SettingsLinkButton"));   // no link: nothing draws effects
            Assert.All(Below<System.Windows.Controls.Primitives.ButtonBase>(page).Where(b => b.IsVisible), b => Assert.True(b.IsEnabled,
                $"{System.Windows.Automation.AutomationProperties.GetName(b)} is shown but can't act"));
        }
        finally { window.Close(); }
    });

    /// <summary>Every button and box on the two pages is spoken with its visible label first (WF34; WCAG 2.5.3), item templates included.</summary>
    [Fact]
    public void Every_control_on_the_two_pages_is_spoken_with_its_label_first() => AppHost.Run(() =>
    {
        Home(".claude-work");
        InstalledPackKit.Install(new Paths(), "good-palette");
        var (window, svm, guard) = Open("Personal");
        try
        {
            var bad = new List<string>();
            foreach (var page in new[] { SettingsPage.ClaudeCode, SettingsPage.Mods })
            {
                window.SelectPage(page);
                Settle(window, guard);
                if (page == SettingsPage.ClaudeCode) svm.ClaudeCodePage.Folders[0].HoldsOpen = true;
                Settle(window, guard);
                DependencyObject root = page == SettingsPage.ClaudeCode ? Below<ClaudeCodePage>(window).Single() : Below<ModsPage>(window).Single();
                foreach (var b in Below<System.Windows.Controls.Primitives.ButtonBase>(root).Where(b => b.IsVisible && b.Content is string))
                {
                    var name = System.Windows.Automation.AutomationProperties.GetName(b);
                    if (string.IsNullOrEmpty(name) || !name.StartsWith(Bare((string)b.Content), StringComparison.Ordinal))
                        bad.Add($"{page}: \"{b.Content}\" is spoken \"{name}\"");
                }
            }
            Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
            Assert.Empty(LeaksIn(NamesOf(window)));
        }
        finally { window.Close(); }
    });

    /// <summary>A landing on Meters above the prompt puts the keyboard on the first folder's switch.</summary>
    [DesktopFact]
    public void A_landing_on_meters_focuses_the_first_folders_switch() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            guard.RouteKeyboard();
            window.SelectPage(SettingsPage.Mods, SettingsIndex.Meters);
            Settle(window, guard);
            var focused = System.Windows.Input.Keyboard.FocusedElement as FrameworkElement;
            Assert.True(focused is CheckBox, $"focus is on {focused?.GetType().Name ?? "nothing"} \"{(focused is null ? "" : System.Windows.Automation.AutomationProperties.GetName(focused))}\" on {window.Place}");
            Assert.Equal(SettingsNames.MetersOnFor(@"~\.claude"), System.Windows.Automation.AutomationProperties.GetName(focused));
            guard.Check();
        }
        finally { window.Close(); }
    });
```

(`Press`, `Below`, `Home`, `Settle` and `Open` are the helpers added in Tasks 8 and 13. Add `using Sanduhr.App.Tests;` is not needed: the file is in that namespace. `InstalledPackKit` asserts the dev profile is under `%TEMP%`.)

- [ ] **Step 2: Run them**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~ModsAndCardsPagesTests`
Expected: PASS, the `[DesktopFact]` included. These pin behavior built in Tasks 8 to 13, so a first run may already pass; a failure names the control, its name or the place, and points at a XAML gap in `ModsPage.xaml` or `ClaudeCodePage.xaml` (most likely a spoken name that doesn't start with its label, or a control shown on a row that can't act). Fix it there, run once more, never in a loop.

- [ ] **Step 3: Commit**

```bash
git add windows-dotnet/tests/Sanduhr.App.Tests/ModsAndCardsPagesTests.cs
git commit -m "test(settings-v2): slice 4's Done criteria; the two pages' controls are spoken with their labels and land the keyboard"
```

(Add `Views/ModsPage.xaml` or `Views/ClaudeCodePage.xaml` to the commit if Step 2 needed a fix.)

---

### Task 15: End to end: the harness probes the two pages; the whole suite; the owner's checks

**Files:**
- Modify: `windows-dotnet/tools/Sanduhr.RenderHarness/SettingsPassScenario.cs` (only if an entry names a retired anchor)

**Interfaces:**
- Consumes: everything above.
- Produces: `anchors-landing.txt` lines for every Claude Code and Mods &amp; Widgets anchor at 800 and 600; a green suite; the Tier 2 hand-off.

- [ ] **Step 1: The harness entries**

In `windows-dotnet/tools/Sanduhr.RenderHarness/SettingsPassScenario.cs`, check every entry that names `SettingsIndex.Statusline`, `Mcp`, `ConfigFiles`, `McpServers`, `ClaudeMd`, `Retention`, `WidgetsCatalog`, `WidgetsInstalled` or `SettingsPage.Widgets`: `git grep -n "SettingsIndex\.\(Statusline\|Mcp\|ConfigFiles\|McpServers\|ClaudeMd\|Retention\|WidgetsCatalog\|WidgetsInstalled\)\|SettingsPage\.Widgets" -- windows-dotnet/tools`. Point each at its new place (`SettingsIndex.Resolve(oldPage, oldAnchor)` gives it) unless the entry exists to prove an old link lands, in which case leave it and let the alias carry it.

- [ ] **Step 2: Build and run the harness's links part off screen, against a scratch profile**

Run: `dotnet build tools/Sanduhr.RenderHarness` (from `windows-dotnet/`)
Expected: Build succeeded, 0 errors, no new warnings.

From the repo root, in PowerShell:

```powershell
$env:SANDUHR_DEV_PROFILE = Join-Path $env:TEMP "sanduhr-slice4-profile"
New-Item -ItemType Directory -Force $env:SANDUHR_DEV_PROFILE | Out-Null
dotnet run --project windows-dotnet/tools/Sanduhr.RenderHarness -- --scenario settings-pass --parts links --out (Join-Path $env:TEMP "sanduhr-slice4-out")
```

Expected: the run ends normally and its `settings-pass: foreground window before X, after X` line shows the same handle twice.

- [ ] **Step 3: Check every new landing**

```powershell
$out = Join-Path $env:TEMP "sanduhr-slice4-out\settings-pass"
Select-String -Path (Join-Path $out "anchors-landing.txt") -Pattern "^(Claude Code|Mods & Widgets) / .* \| (800|600) \|" |
    Where-Object { $_.Line -notmatch "\| (fully in view|top in view)[^|]* \|[^|]*\| ok$" }
Select-String -Path (Join-Path $out "deeplinks.txt") -Pattern "(CARD|TOUR)-LANDING" |
    Where-Object { $_.Line -notmatch "at 800: (fully in view|top in view)[^;]*; ok" }
```

Expected: no output. Every Claude Code anchor (`folders`, `folder-config`, `glow`, `glow-where`) and every Mods &amp; Widgets anchor (`meters`, `mods`, `catalog`, the catalog opened by its own landing) lands in view at 800 and 600 after its jump, and every What's New and tour card still lands in view (the three 3.8.0 Widgets cards through their aliases). A printed line names the anchor or card and why; fix the landing and run Steps 2 and 3 once more. The evidence under `%TEMP%` is never committed. (The links run fetches nothing: no landing in it opens the catalog except `catalog`'s own, and the harness profile's network is the scenario's.)

- [ ] **Step 4: The whole suite and the contracts**

Run: `dotnet test Sanduhr.slnx` (from `windows-dotnet/`)
Expected: PASS across `Sanduhr.Tests`, `Sanduhr.App.Tests` and `Sanduhr.Mcp.Tests`, every `[DesktopFact]` included (this is a desktop session). Then:

```bash
git diff release/4.0 --stat -- windows-dotnet/tests/Sanduhr.Mcp.Tests/TrustBoundaryTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsKeysContractTests.cs windows-dotnet/src/Sanduhr.Mcp/Sanduhr.Mcp.csproj
```

prints nothing: the MCP's link list, its pin and the frozen settings keys are untouched. `git diff release/4.0 --stat -- windows-dotnet/src/Sanduhr.Mcp` shows `ToolCatalog.cs` only, one line.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/tools/Sanduhr.RenderHarness/SettingsPassScenario.cs
git commit -m "test(settings-v2): the harness lands every Claude Code and Mods & Widgets anchor at 800 and 600"
```

(Skip the commit when Step 1 changed nothing.)

- [ ] **Step 6: Hand the Tier 2 checks and the owner's calls to the owner**

Not code, and not for the agent: they need the real app on real screens and a real profile, which this plan never launches. Post them to the owner as the slice's open checks:

- **Item 3 (WF3, packs in use):** install a theme, palette, font and effects pack from the live catalog. Each appears where its row says (Themes; Desk Look, Ink and color; Desk Look, Fonts; the Desk companion for a sprite) and each row's link lands there in one click; effects changes nothing and says so. Switch each off: it leaves its picker at once (the theme strip, the Ink presets, both font lists, the Theme Studio's Font picker, the companion); a theme in use falls back to Obsidian and returns when switched on; a font pack whose family the active theme names switches the widget font back (theme-match Tier 2). What the merged catalog row looks like for an installed pack (the same switch as its Installed row).
- **Item 6 (WF6, the catalog part):** open Mods &amp; Widgets offline and online: nothing is asked of GitHub until the Catalog opens; offline it reads "Couldn't reach the widget catalog. Widget packs add themes, colors, fonts and a Desk companion. Check your connection and try again."; "Check the catalog again" asks once more.
- **Item 8 (real folders):** on a real profile with two or three Claude Code folders, the folder cards (each card's pieces act on its own folder; the meters line; "What this folder holds" opened on a busy folder) and Mods &amp; Widgets' meters rows and scopes ("On in 2 of 3 folders") read true; how each page looks with no folder at all. (The History vault and "Homes to share" halves of item 8 are slice 5's.)
- **The owner's taste list** (above, "Owner's taste"): the switch word "On", the font pack link, one folder reading plainly, "Switch it per folder…", Uninstall… asking, the theme pack count, the folder-config landing order.
- **Meters on and off in Claude Code itself:** switch a folder's meters Off on Mods &amp; Widgets, start a new Claude Code session there: no band; On again: the band; Remove: settings.json as it was before Install.

---

## Carried in, and where each went

Every row of `.superpowers/sdd/carry-into-slice-4/` addressed to slice 4, and the slice-wide lessons:

| # | Carried item (source) | Where |
| --- | --- | --- |
| 1 | The companion row's empty state reuses `views:WidgetsPage.NotVisible`, which breaks the row if WidgetsPage is retired (slice 3 final review, T9; Recommendations: "grep for `WidgetsPage.` references first") | Task 5: the converters move to `Converters/BoolConverters.cs`, the Desk panel uses them, and `No_view_borrows_another_pages_statics` pins it |
| 2 | The companion's empty line names Widgets until slice 4; "filtered to sprites" needs slice 4's filters (slice 3 final review decision 5; slice 3 plan "Carried") | Task 13: "Get one in Mods &amp; Widgets…" lands on `mods#catalog` showing sprites (decision 14) |
| 3 | `widgets#widgets-catalog` → `mods#catalog` alias goes live | Task 12 (`Every_slice_4_alias_lands_on_its_new_home`, the window-level `Old_widget_and_mods_links_land_on_the_new_page`) |
| 4 | "Settings, Mods &amp; Widgets" reads as page "Mods" (slice 1 ledger T1, final review: "slice 4 retitles the page") | Task 12 (`Prose_that_names_the_page_reads_as_the_page`) |
| 5 | Copy naming a page from `TitlesToCome` lands in the same commit as the page (slice 1 final review) | Task 12: the WhatsNew body and the ToolCatalog string change with the sidebar |
| 6 | Each slice pins its boost against the real index (slice 2 final review T4) | Task 12 (`Pack_words_land_on_the_catalog_now_that_it_is_live`) |
| 7 | Jump links on Claude Code and Mods &amp; Widgets "in the slice that rebuilds it" (slice 3 plan, Carried) | Ruled out: neither page has four sections (decision 19), pinned in Tasks 8 and 12 |
| 8 | Test classes touching `AppWindowFixture` or process-wide statics carry `[Collection("SettingsWindow")]` (theme-match ledger T11, T16) | Global Constraints; every new App class that touches them has it; `ModsPageFixture` touches none |
| 9 | `NoActivate.Show` plus `Check()`; `RouteKeyboard` is WM_SETFOCUS only; `[DesktopFact]` is skippable (theme-match ledger; slice 3) | Global Constraints; Tasks 8, 12 to 14 |
| 10 | A `TextBlock` outside a template is a UIA control element; `Controls/DecorativeText` exists (theme-match P16) | Global Constraints (the new pages add no decorative text outside templates; their separators are `Border`s) |
| 11 | Page ScrollViewers `Focusable="False"`; focus rings from the window's styles (slice 2 P5, slice 3 P25) | Global Constraints; both new pages; the chip style keeps a keyboard ring (scan in `SettingsSourceScanTests`) |
| 12 | The names table holds the copy and `PathProblems` is the scan | Global Constraints; Task 2 |
| 13 | A null HelpText leaves an empty line under a SectionExpander (slice 3 final review M3) | Global Constraints: both new expanders carry HelpText (`FolderHoldsHelp`, `CatalogDisclosure`); the fix to the style stays slice 8's |
| 14 | Add the focused element and page to focus-test asserts (slice 3 final review, Recommendations) | Task 14's `[DesktopFact]` message |

## Carried, not in this slice

- **Slice 3's other carries** (M2 companion status name, M4 Order button names, M5 doc comments, T1 to T18 polish, the two FLAKE watches): addressed to slice 8 or to release/4.0 integration, not to slice 4.
- **The Desktop group's place in the sidebar** (slice 3 decision 4): slice 8.
- **Streamer mode on these pages** (masked folder names, "Show real name on stream" on a Mods &amp; Widgets row and a folder card's menu): slice 7.
- **Accounts' "Linked folders" with "Claude Code settings…"** (WF27's other half): slice 5.
- **A mod's touches** ("Draws a band; can run programs"), shown per plugin on the old Mods &amp; Config: the Installed row doesn't carry them in 4.0 (`InstalledItem` has no field for them). Listed for the owner as a possible 4.0.x polish; nothing in the spec asks for it.
- **The cross-folder Claude Code catalog, Compare, switching other mods** (R1, R2, R6, R9): "Not in 4.0".
- **The Tier 2 checks** (Task 15 Step 6): the owner, on real screens.

---

## Spec coverage (slice 4)

| Slice 4 bullet, Done item or Tier 2 item (spec) | Task |
| --- | --- |
| `InstalledItems.Build` (id, name, kind, source, version and update, scope map, `CanSwitch` with its route) | 3, 4 |
| The one list with filters and the read-only labels (WF39; Principle 5) | 4, 9, 12 |
| Pack switches, and every consumer honoring `enabled` (theme list, ink presets, font lists, companion) | 1, 6, 9 |
| "Where it shows" per kind (WF3), effects labelled unused | 4, 9 |
| The meters mod section across folders: switch, Update…, Remove…, consent on install, Off vs Remove caption (WF9) | 10, 12 |
| Folder cards acting on their own folder, with "What this folder holds"; the hook named "Claude Code glow hook"; "Update…" as the verb (WF4, WF11, WF27, WF42) | 7, 8 |
| The Folder combos retired (Claude Code's install combo, Mods &amp; Config's picker) | 8, 12 |
| The Claude Code glow section: heading, needs-line, which folders have the hook (WF11) | 8 |
| Catalog collapsible, fetching on expand; disclosure above the list; offline copy (WF6, WF40) | 2, 11, 12 |
| Catalog rows merged with installed ones by id | 4, 11 |
| The Widgets page retired, with an alias; the Mods &amp; Config title retired | 12 |
| Themes' status line "Theme packs: <n> installed. Mods &amp; Widgets settings…" (WF40) | 13 |
| The companion row's "No companion yet. Get one in Mods &amp; Widgets…" to `mods#catalog` filtered to sprites | 13 |
| Search: "widgets", "packs", "pack" → Mods &amp; Widgets; the "(in Catalog)" and "(in What this folder holds)" suffixes | 8, 12 |
| Aliases: Widgets (page, `widgets-catalog`, `widgets-installed`, `widgets-companion`), ClaudeCode `meters`, `statusline`, `mcp`, Mods `config`, `mcpservers`, `claudemd`, `retention` | 8, 12 |
| Tests named by the spec: `InstalledItemsTests` (merge by id, scope map, `CanSwitch` per kind), `WidgetStore` enabled-honoring tests per consumer (`PackEnabledTests`, `PackSwitchConsumersTests`), `ModsPageTextTests`, `ModsFolderChoiceTests` (updated for cards) | 1, 3, 4, 6 |
| Done: an installed palette is one click from where it shows | 9, 14 |
| Done: switching a pack off removes it from its picker | 1, 6, 14 |
| Done: no row shows a control that can't act | 8, 9, 10, 11, 14 |
| Tier 2 items 3, 6 (catalog) and 8 | 15 Step 6 |
| No settings key added, renamed or removed; no MCP link list change | Global Constraints, 15 Step 4 |
