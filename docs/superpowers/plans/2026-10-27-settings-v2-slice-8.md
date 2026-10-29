# Settings v2 for Windows, Slice 8 ("Reset, themes and contrast") Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reset Sanduhr… lists every Claude Code folder that holds Sanduhr's entries, each with its own checkbox, and a folder left unticked keeps its entries byte for byte along with the files they run. About says what Reset removes. TextDim and TextMuted read at 4.5:1 or better on the Settings page and sidebar in all six built-in themes. Obsidian's selected sidebar label reads in Text beside an accent bar. The two theme lists on Themes grow to fit what they hold.

**Architecture:** Core owns the rules.
- `ResetPlan` lists the folders with entries (receipts plus MCP registrations) and says when receipts can't be read.
- A new `ClaudeCodeReset` moves the Claude Code undo out of the App so it runs against a temp tree in tests. It undoes every folder but the kept ones, and leaves the shared files in place while anything is kept.
- `ResetService` keeps Sanduhr's copies, receipts and switches whenever a folder is kept or the receipts can't be read.
- `ResetWindow` draws the list through a small view model whose rows key on the real folder and show it through `StreamerMode.ShowFolder`. `SettingsViewModel.ReloadNames` renames them on a streamer flip.
- A new pure `ThemeContrast` computes the surfaces Settings paints from a theme's tokens. `ThemeContrastTests` hold the twelve new token values to it. `ThemePalette` takes its chrome alpha and a new `Sanduhr.Brush.SelectedLabel` from it.
- The Themes page becomes one scrolling page with uncapped lists.

**Tech Stack:** .NET 10, WPF, xUnit 2.9 (Xunit.SkippableFact for `[DesktopFact]`), CommunityToolkit.Mvvm 8.4, the render harness (`tools/Sanduhr.RenderHarness`).

**Spec:** `docs/settings-v2-windows/spec.md`, "Shippable slices, Slice 8: Reset, themes and contrast", is the authority on scope. It works with these sections:

- "## Reset (WF20)" (about line 409): the full label, a checkbox per folder that has receipts, `UndoAll(folders)` (R12), the About caption, and the two streamer files.
- "### Themes (`themes`)" (about line 254): the gallery and installed lists with no 112 and 72 DIP caps (WF49).
- "## Keyboard and screen reader", Contrast (WF37; about line 393): TextDim and TextMuted at 4.5:1 on the page and sidebar in all six built-in themes, Obsidian's selected label in Text with an accent bar, `ThemeContrastTests` computing ratios from the tokens.
- "### About (`about`)": Reset Sanduhr… (`reset`).

The other inputs:

- Findings: `docs/settings-v2-windows/findings.md` WF20, WF37, WF49 and "Needs the live pass (Tier 2)" item 10. `docs/settings-v2-windows/mods-parity.md` R12. `docs/settings-v2-windows/inventory.md` (W17, W33, §1.10, the About table).
- Carried in: `.superpowers/sdd/carry-into-slice-8/slice-7/carry.md`, plus every row earlier slices addressed to "slice 8" (see "Carried in, and where each went").
- Lessons: `.superpowers/sdd/2026-10-26-settings-v2-slice-7/progress.md`, `preflight.md` and `final-review.md`.
- The executed slice 7 plan, `docs/superpowers/plans/2026-10-26-settings-v2-slice-7.md`, is the model for conventions and helpers.
- The Mac's Reset (`mac/Sources/Sanduhr/Services/Reset.swift`, `Views/ResetSheet.swift`) already has a per-folder list. Its rules are ported where they fit: unticked folders keep their entries and the files they run, and unreadable receipts keep everything.

## Spec questions decided in this plan

1. **The list covers folders whose only Sanduhr entry is the MCP server.** The spec says "a checkbox per folder that has receipts". On Windows the MCP server writes no receipt: `IntegrationKind.Mcp` exists, but `McpIntegrationInstaller` registers per home in `.claude.json` (`McpIntegrationInstaller.cs:223`) and keeps no record. Reset undoes that entry today (`ClaudeCodeViewModel.UndoEverything`, `mcp.UndoAll(homes)`). A folder holding only the MCP entry would have no row, and Reset would take its entry with no way to keep it. So the list is the receipt folders plus every discovered folder where `IsRegistered` finds Sanduhr's MCP entry (`ResetPlan.FoldersWithEntries`).
2. **A kept folder is never written.** Its `settings.json`, its `.claude.json` and, for `.claude`, the `%USERPROFILE%\.claude.json` beside it stay byte for byte. An undone folder goes through each installer's Remove, which takes out only what still points at Sanduhr's files (`ClaudeCodeIntegrations.cs:676`, `StatuslineInstaller.Remove`, `McpIntegrationInstaller.Deregister`). That rule is unchanged and re-pinned.
3. **While any folder is kept, the files its entries run stay**, as on the Mac ("Unticked folders keep Sanduhr's entries, and its scripts stay so they keep working"). That means the meters mod's copies (`~\.sanduhr\mods`), the MCP launcher and server (`~\.sanduhr\bin`, `~\.sanduhr\mcp`), the statusline script, `mcp-settings.json` and the receipts in `%APPDATA%\Sanduhr\integrations`. The `mcp_enabled` and `statusline_enabled` switches stay on too, so the next launch keeps the script and server up to date (`App.xaml.cs:192-225`). Undone folders' receipts go one by one, through Remove, as today.
4. **Receipts that can't be read keep everything** (the Mac's `keepsIntegrations`). `ResetPlan.ReceiptsUnreadable` is true when `installs.json` doesn't decode in full or a set-aside copy (`installs.json*.unreadable`) exists. It is read **before** the undo runs, because a receipt save moves a damaged file aside and leaves a readable one (`ReceiptLog.cs:128`). Today Reset deletes `integrations\` after an undo with no reported failure, which loses the damaged records. The dialog says so in one line.
5. **About's caption adds "themes, widgets".** The spec's sentence ("Removes your accounts, history, session archive, settings, Desk messages and Sanduhr's Claude Code entries. You choose which.") leaves out the fourth part, "Your themes and widgets". Copy must be exactly true, so the caption reads "Removes your accounts, history, session archive, themes, widgets, settings, Desk messages and Sanduhr's Claude Code entries. You choose which." The About button's spoken help text becomes the same sentence.
6. **The old warning goes.** "An entry you leave unchecked can't be removed from Sanduhr later." (`ResetWindow.xaml:165`) is untrue: an unticked part keeps the receipts, so Settings, Claude Code can still remove those entries. It is replaced by lines that each show only in the state where they are true (Task 5):
   - a folder is kept: "Unticked folders keep Sanduhr's entries and the files they need. Remove them later in Settings, Claude Code."
   - a kept folder has meters, and Accounts and sign-in keys is ticked: "Their meters stay empty until you sign in to Sanduhr again."
   - a kept folder has meters, and Settings is ticked: "With the settings reset, their meters come back once you open Settings, Claude Code." Why: settings.json goes, so `statusline_enabled` reads false until the Claude Code page's `SyncSnapshotConsumers` turns it back on (`ClaudeCodeViewModel.cs:484`).
   - a kept folder was added by hand, and Settings is ticked: "A folder you added yourself leaves that page with the settings: add it there again to remove its entries." Why: `cc_folders_added` lives in settings.json.
7. **The part's label is already full.** It reads "Sanduhr's entries in Claude Code (statusline, MCP server, meters mod, glow hook)" (slice 1, `SettingsNames.ResetClaudeCodeEntries`). This slice builds each row's kinds from the same four words (`SettingsNames.ResetKindWord`) and pins the label against them, so the two can't drift.
8. **"Page" and "sidebar" are what Settings actually paints.**
   - The window is opaque (`AllowsTransparency="False"`, `Background` = `Sanduhr.Brush.Bg`), so Mica never shows under Settings.
   - Every page sits on a card painted `Sanduhr.Brush.Glass`, which `ThemePalette` sets solid (`ThemePalette.cs:158`). Lists and fields inside a page sit on `Sanduhr.Brush.Bg` wells.
   - The sidebar is `Sanduhr.Brush.TitleBg`, which is TitleBg at the chrome alpha (0.92, or 1.0 for Matrix) over the window's Bg (`ThemePalette.cs:164`).

   `ThemeContrast.SettingsSurfaces` returns exactly these three: page (Glass), well (Bg) and sidebar. The findings' `contrast.txt` composited Glass over Bg at the glass alpha, which Settings never draws, so its numbers run a little higher than these (WF37 quotes 3.1 to 3.9 for TextDim). The harness's contrast part is moved onto the same helper (Task 11).
9. **No built-in theme has a light variant**, so "in light and dark where themes have both" covers dark only. The harness's `--light` theme is not a built-in. Its ratios are printed, not asserted (Task 11).
10. **The ramp forces two floors.** ThemeLint's calibration test (`ThemeLintTests.Every_built_in_lints_with_no_findings`) requires TextSecondary > TextDim > TextMuted in luminance, so the two tokens can't share 4.5:1. TextMuted is raised to 4.5:1 on its worst surface, and TextDim to 5.0:1 (owner taste 1). Each value is the smallest raise in HSL lightness that passes, with hue and saturation kept (owner taste 2), so the ramp keeps its hue and every built-in still lints clean. The table is under "The token changes".
11. **The selected label's rule is computed, so custom themes get it too.** `ThemeContrast.SelectedLabel` returns Accent when Accent reads 4.5:1 on the selected row (Glass), and Text when it doesn't. Among the built-ins only Obsidian falls back: its accent reads 3.95:1 there. The accent bar shows on every theme (owner taste 3); the bar is a non-text indicator at 3.0:1, and every built-in accent passes on Glass (Obsidian 3.95).
12. **Themes becomes one scrolling page.** Removing the caps inside today's layout would starve the editor's `*` row on a short window, and a capped list inside a page would swallow the mouse wheel. So the page gets an outer `ScrollViewer` (`ThemesPageScroller`, `Focusable="False"`, like `DeskPageScroller`). The editor's own ScrollViewer becomes a plain `Border`, its row goes to `Auto`, the gallery loses its ScrollViewer, and the installed list's template drops its ScrollViewer (the `ThemeSwatchList` pattern, `App.xaml:80`).
13. **Reset's rows follow `ReloadNames`, with no new subscription.** `SettingsViewModel.ResetFolders` holds the dialog's list only while it is open: `App.ShowReset` sets it and clears it in `finally`. `ReloadNames()` calls `ResetFolders?.RefreshNames()`. Reset opens only from About, over Settings, so the window that reloads names is always the one under the dialog.
14. **Masked values never read back.** A row's checkbox binds `Undo` on a row whose `Path` is the real folder. `Shown` is display only. `KeepFolders` returns real paths, and a test unticks a row showing a codename and asserts the real path comes back.
15. **No settings key, What's New card or search word is added.** `SettingsKeysContractTests`, `WhatsNewCards` and `SettingsIndex` are untouched; the `reset` anchor already exists with its synonyms.
16. **PRIVACY.md gets one Windows sentence,** in "How you remove your data", Clear local storage. The Mac's Reset paragraph already says the same for the Mac.

## The token changes (all twelve, before and after)

Ratios are page / sidebar / well, WCAG, computed from the tokens as `ThemeContrast.SettingsSurfaces` composites them. Raised in HSL lightness only (hue and saturation kept). TextMuted's floor is 4.5:1 and TextDim's is 5.0:1 on the worst surface (the page in every theme).

| Theme | Token | Before | After | Before (page / sidebar / well) | After (page / sidebar / well) |
| --- | --- | --- | --- | --- | --- |
| Obsidian | TextDim | `#777777` | `#8b8b8b` | 3.81 / 4.07 / 4.34 | 5.00 / 5.35 / 5.70 |
| Obsidian | TextMuted | `#555555` | `#848484` | 2.29 / 2.44 / 2.61 | 4.56 / 4.87 / 5.20 |
| Aurora | TextDim | `#64748b` | `#808ea3` | 3.50 / 3.78 / 4.03 | 5.00 / 5.40 / 5.76 |
| Aurora | TextMuted | `#475569` | `#7487a1` | 2.20 / 2.37 / 2.53 | 4.53 / 4.90 / 5.22 |
| Ember | TextDim | `#8b6b60` | `#a18277` | 3.66 / 3.89 / 4.00 | 5.01 / 5.31 / 5.47 |
| Ember | TextMuted | `#6b4b40` | `#a47767` | 2.27 / 2.41 / 2.48 | 4.54 / 4.81 / 4.96 |
| Mint | TextDim | `#5a9a78` | `#5fa17e` | 4.60 / 5.20 / 5.41 | 5.01 / 5.66 / 5.88 |
| Mint | TextMuted | `#3a7a58` | `#4a9b70` | 2.98 / 3.37 / 3.51 | 4.52 / 5.10 / 5.31 |
| Matrix | TextDim | `#00802b` | `#009933` | 3.69 / 3.87 / 3.94 | 5.02 / 5.26 / 5.35 |
| Matrix | TextMuted | `#005a1e` | `#009030` | 2.22 / 2.33 / 2.37 | 4.50 / 4.73 / 4.81 |
| Blueprint | TextDim | `#6a6e99` | `#9194b4` | 3.06 / 3.77 / 3.63 | 5.04 / 6.22 / 5.99 |
| Blueprint | TextMuted | `#484b70` | `#878bb2` | 1.79 / 2.21 / 2.12 | 4.52 / 5.58 / 5.37 |

Closest margins: Matrix TextMuted 4.5035, Obsidian TextDim 5.0020, Aurora TextDim 5.0027. Nothing else moves: Text, TextSecondary, Accent, Bg, Glass and TitleBg stay as they are, so `StreamerBanner`'s Accent border on Bg is unchanged (carry). Where the two tokens draw outside Settings, they get brighter too:
- the widget's tier cards (reset countdown, footer, the pace and burn fallbacks, `TierCardViewModel.cs:171-176`)
- the Claude Usage calendar's dots (`CcCalendarControl.cs:68-192`)
- Arrange Desk's unlit dots (`DeskArrangeMode.cs:386`)
- the Desk review bar's lines (`DeskReviewBar.xaml.cs:96`)
- the Theme Studio's starting rows for each built-in (`ThemeDraft.cs:79-80`)

**Accent as text.** Obsidian's accent reads 3.95:1 on Glass. Spec scope fixes only the selected label (decision 11). Obsidian's link-style buttons (`SettingsLinkButton`, Accent text at 11 px) still read 3.95:1. That is decision D2 below.

## Owner's taste (decided with a default; each is one line to change)

1. **TextDim's floor is 5.0:1** and TextMuted's 4.5:1, which keeps one visible step between them. The alternative is 4.5:1 for both, with TextDim one hex step above TextMuted (Mint's TextDim would then stay `#5a9a78`).
2. **A token is raised in HSL lightness only**, keeping hue and saturation, so each ramp keeps its tint.
3. **The selected sidebar row** gets an accent bar on every theme. Its label is Accent where Accent reads 4.5:1 on the row, else Text (Obsidian only, among the built-ins).
4. **The bar** is 3 × 16 DIP with rounded ends, centred on the row, 2 DIP in from the row's left edge.
5. **Reset's folder rows start ticked**, as on the Mac.
6. **Each row shows its kinds** under the name ("statusline, meters mod") in 11 px TextDim, and speaks them as help text.
7. **Rows dim, not hide, while the Claude Code part is unticked**, and keep their ticks for when it comes back.
8. **The kept-folder lines' words** (decision 6), each shown only in its state.
9. **About's caption** adds "themes, widgets" to the spec's sentence (decision 5). It sits under the button in 11 px TextDim, at most 460 DIP wide.
10. **Themes scrolls as one page.** The editor no longer scrolls on its own, and the Installed list has a 72 DIP minimum and no maximum.
11. **`docs/themes/template.json`** takes Obsidian's two new values, so a theme started from the template passes in Settings. The Mac also reads that template, but nothing on the Mac pins its values.

## Decisions for the owner, apart from taste

**D1. A ThemeLint rule for user and agent themes. Recommendation: none in 4.0.** `ThemeContrastTests` holds the six built-ins only. A custom or pack theme can still set dim text that fails in Settings. A lint rule would have to measure against Settings' surfaces rather than the widget card it measures today, and it would carry these costs:
- `ThemeLint.cs` is on the MCP link list, so the rule widens what the trust boundary's linked, pinned-pure files do, and `TrustBoundaryTests`' purity scan would re-read it.
- `propose_theme` answers would change, with new warnings for themes agents already make.
- The Mac's lint, which shares `mac/Tests/SanduhrTests/Fixtures/theme-builtins.json`, would no longer match.
- `docs/themes/AGENT_PROMPT.md` would need the new threshold.

This plan does none of that. The selected-label rule (decision 11) already adapts to any theme. Revisit after 4.0 as one change across Windows, the Mac and the agent prompt.

**D2. Accent text elsewhere on Obsidian. Recommendation: carry to after 4.0.** Link-style buttons and a few accent captions read 3.95:1 on Glass in Obsidian, a WCAG AA failure the spec doesn't name. The fix is one more task: a computed `Sanduhr.Brush.AccentText` (Accent, or Accent raised in lightness until it reads 4.5:1, `#7870ff` at 4.51:1 for Obsidian), used by `SettingsLinkButton` and the other accent-coloured text in Settings. That is about ten XAML lines plus one test, and the plan has room for it as Task 8b if the owner says so.

**D3. `ThemeModel.cs` is on the MCP link list, and Task 7 edits it.** The edit is twelve hex values in `ThemeCatalog.BuiltIns`: data only, with no new code, no IO and no change to the `Compile Include` list. `TrustBoundaryTests` stays untouched and green, because `ThemeModel.cs` is pinned as linked, and the purity scan covers the newer files only. The visible effect at the boundary: the server's copy of the built-ins (`ThemeLint`'s calibration, `get_desk`'s theme look via `ThemeDeskLook`) carries the new values. No MCP answer field changes shape. The plan treats that as acknowledged unless the owner objects.

**D4. Mac parity. Recommendation: Windows only for 4.0.** The Mac's built-ins (`mac/Sources/Sanduhr/Models/Theme.swift`) and the shared lint fixture keep the old values. After this slice the two apps draw dim text differently in the same theme. The Mac's Settings has the same contrast problem, and it belongs to its own item.

**D5. Earlier slices' "slice 8" polish carries.** Slices 2 to 5 sent about 25 small items to "slice 8 polish", "the slice 8 a11y sweep" or "the slice 8 copy sweep" (listed in "Carried in"). The spec's slice 8 is Reset, themes and contrast only. Three of those items are contrast checks, and this plan takes them. The rest go to the after-4.0 carry file (Task 11), and the owner decides whether a polish wave runs before the Store submission. Recommendation: after 4.0, except any item a Tier 2 pass turns up as visible.

**D6. The sidebar's group order still differs from the spec** (slice 3 decision 4, P25, parked on the dashboard with no slice owning it). Desktop sits after Extend and Widget, where the spec puts it after Usage. It isn't in slice 8's scope. The owner should decide before the Store screenshots are taken.

## Global Constraints

- **CRLF working tree, LF index.** Edit tracked files with the Edit tool only.
  - **Never `sed -i`, ever.** Never rewrite a tracked file wholesale with Write. New files may be created with Write.
  - Before an Edit, confirm its anchor is unique: `git grep -c -F "<first line of the anchor>" -- <file>` prints 1, or Read the file and check the whole block. These anchors repeat, and every edit below carries enough neighbouring lines to be unique:
    - `</ScrollViewer>` and `</Border>` (many times in `SettingsWindow.xaml`)
    - `<RowDefinition Height="*" />` and `<RowDefinition Height="Auto" />`
    - `Value="{DynamicResource Sanduhr.Brush.Accent}" />` (many trigger setters)
    - `<Setter TargetName="bd" Property="Background" Value="{DynamicResource Sanduhr.Brush.Glass}" />` (the sidebar TabItem and the search result styles)
- **Keys.** No `settings.json` key is added, renamed or removed. `SettingsKeysContractTests` is untouched.
- **Trust boundary.** No file is added to or removed from `Sanduhr.Mcp.csproj`'s `Compile Include` list.
  - `ThemeModel.cs` (linked) changes twelve string literals and nothing else (D3).
  - `ThemeLint.cs` (linked) is called by `ThemeContrast`, never edited.
  - The new Core files (`ClaudeCodeReset.cs`, `ThemeContrast.cs`) are off the list, and so are the changed ones: `ResetPlan.cs`, `SettingsNames.cs`, `McpIntegrationInstaller.cs`.
  - No `src/Sanduhr.Mcp` file changes, and `TrustBoundaryTests` stays green untouched.
- **Reset is destructive.**
  - `~\.sanduhr` is shared ground: Reset deletes only Sanduhr's own names there, and no new code lists, clears or deletes the folder.
  - A kept folder's files are not opened for write. Tests compare every file under a kept folder byte for byte, and `%USERPROFILE%\.claude.json` too when `.claude` is kept.
  - An undone folder loses only what still points at Sanduhr's files.
  - `ResetService` failure lines stay kinds, never names (`KindOf`).
  - `backups\` is never touched.
- **Hands off the live app.** Never start, stop, kill or attach to `Sanduhr.exe`. If a build fails because a `Sanduhr.exe` holds a file, stop and report BLOCKED.
- **No test reads the real AppData,** LocalAppData, `~\.sanduhr`, a real Claude Code folder or Credential Manager.
  - Core tests build trees under `Path.GetTempPath()`.
  - App tests use `AppWindowFixture`, whose `ClaudeCodeProfile.ProfileOverride` is a scratch profile. They build every path from `ScratchPaths()` or the scratch widget (`widget.SanduhrAppDataDir`, `SanduhrExternalDir`, `StatuslineBinDir`, `ThemesDir`).
- **No test touches the network.** `NetworkDisclosureTests` stays as it is.
- **Test hygiene.**
  - An App test class carries `[Collection("SettingsWindow")]` when it derives from `AppWindowFixture`, builds a `SettingsWindow` or `SettingsViewModel`, uses `ScratchWidget`, or touches `StreamerMode`. `StreamerCollectionScanTests` enforces the last.
  - Windows are shown through `NoActivate.Show(window)` or `OffScreen` with `ShowActivated=false`.
  - **Every widget a test builds is disposed in a `finally`.**
  - Every window a test opens is closed in a `finally`.
- **Event lifetime.**
  - The only new event is `ResetFolderListViewModel.Changed`. It lives as long as one dialog, and `ResetWindow.Closed` detaches it.
  - `SettingsViewModel.ResetFolders` is cleared in `App.ShowReset`'s `finally`.
  - Nothing subscribes to `StreamerService.Changed` in this slice.
- **Status lines across a streamer flip.** The Reset dialog's only names are its rows, and they are rebuilt by `ReloadNames` (decision 13). No sentence in the dialog embeds a folder name.
- **RED first, and RED must be behavioural.** Where a new member is needed for the test to compile, add it first as a stub (a constant set to `""`, a method returning `[]` or `false`, a parameter that is ignored), run the test once, and record the behavioural failure. Each task's Step 2 names its stub and the expected failure.
- **Implementers keep full test logs.** Redirect each run to `.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-<N>-<what>.log` and read failures from the file.
- **Run tests from `windows-dotnet/`.**
  - Core: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~<Class>`.
  - App: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~<Class>`.
- **Names.** Every new visible or spoken string is a `SettingsNames` member or built from one, and passes `SettingsNames.PathProblems`. A control's spoken name starts with its visible text.
- **Timings in copy come from constants.** This slice's copy quotes no interval.
- **The reach scan.** A new App line that reads a folder path, profile path or name carries a `// streamer: <id> - <why>` marker, and `StreamerReachTests.Known` changes in the same commit. Each task names its rows and counts. The scan's own output is the authority. A count other than the one the task names means a line was written differently: re-read it rather than adjust the pin. The floor stays 322.
- **Branch.** Tasks run in numeric order on `feat/4.0-slice-8`, cut from `release/4.0` at 72dae78a.

## Review Focus

1. **A kept folder written another way, or the `.claude` home.** "C:\…\.CLAUDE-KEEP\" must match `.claude-keep`. Keeping `.claude` must leave the `%USERPROFILE%\.claude.json` beside it alone, because its MCP entry lives there and not inside the folder. Pinned in Task 3 (`A_kept_folder_matches_however_it_is_written`, `Keeping_dot_claude_leaves_the_claude_json_beside_the_profile`).
2. **Streamer mode flips while Reset is open, or was on when it opened.** Rows show codenames. Unticking one keeps the real folder, and a flip renames the rows. Pinned in Task 5 (`With_streamer_mode_on_a_row_shows_its_codename_and_the_choice_keeps_the_real_folder`, `A_streamer_flip_while_Reset_is_open_renames_the_rows`) and Task 10.
3. **Receipts that can't be read, or a set-aside copy beside them.** Nothing of Sanduhr's copies is deleted, the switches stay on, and the dialog says why. Pinned in Task 2 (`Receipts_that_cant_be_read_say_so`), Task 4 (`Unreadable_receipts_keep_every_copy_even_with_nothing_kept`) and Task 5.
4. **A custom theme whose accent fails as text, or a pack theme.** The selected label falls back to Text for any theme, not just Obsidian. Pinned in Task 8 (`A_custom_theme_with_a_dark_accent_gets_Text`).
5. **Twenty themes on a 600 DIP window.** Both lists show every item, the page scrolls instead, and the mouse wheel over the installed list scrolls the page. Pinned in Task 9 (`Every_theme_shows_and_the_page_scrolls`, `Wheel_over_the_installed_list_scrolls_the_page`).

---

## Where the plan steps outside the spec's file list

The spec's list is `ResetWindow.xaml(.cs)`, `ResetPlan.cs`, "the built-in theme files" (`ThemeModel.cs`'s `ThemeCatalog.BuiltIns`; Windows has no theme files on disk), the Themes block of `SettingsWindow.xaml`, `ResetPlanTests` and `ThemeContrastTests`. These extra files deliver its bullets and the carry:

Core:
- New `ClaudeCodeReset.cs`: the Claude Code undo, moved out of `ClaudeCodeViewModel.UndoEverything` so a Core test runs it on a temp tree with real installers (decision 2).
- New `ThemeContrast.cs`: the surfaces and the selected-label rule (decisions 8 and 11).
- `McpIntegrationInstaller.cs`: `UndoAll(homes, removeFiles)` (decision 3).
- `SettingsNames.cs`: the slice 8 block.

App:
- `Services/ResetService.cs`: keeps files, receipts and switches (decisions 3 and 4).
- `ViewModels/ClaudeCodeViewModel.cs`: `UndoEverything(widget, keep)` delegates to Core.
- New `ViewModels/ResetFolderListViewModel.cs`: rows, lines and `Read`.
- `ViewModels/SettingsViewModel.cs`: `ResetFolders`, one `ReloadNames` line.
- `App.xaml.cs`: `ShowReset` passes the list and the keep set.
- `Theming/ThemePalette.cs`: `ChromeAlpha` from Core, `Sanduhr.Brush.SelectedLabel`.
- `Views/SettingsWindow.xaml`, beyond the Themes block: About's caption and the sidebar TabItem style.

Docs: `docs/PRIVACY.md` (decision 16), `docs/themes/template.json` (taste 11).

Harness: `tools/Sanduhr.RenderHarness/SettingsPassScenario.cs` (`PassContrast` on `ThemeContrast`).

Tests:
- New Core tests: `SettingsSlice8NamesTests.cs`, `ClaudeCodeResetTests.cs` and `ThemeContrastTests.cs`.
- New App tests: `ResetServiceFoldersTests.cs`, `ResetWindowFoldersTests.cs`, `SettingsContrastTests.cs`, `ThemesListsTests.cs` and `Slice8DoneTests.cs`.
- Changed: `ResetPlanTests.cs` and `StreamerReachTests.cs` (Core).

Not in this slice, though the spec or the carry puts these near it: a ThemeLint contrast rule (D1), accent text beyond the selected label (D2), the Mac's built-ins (D4), the earlier polish carries (D5), the sidebar group order (D6), and the MCP answers under streamer mode (slice 7, P6 = A).

## Merge surface

Slice 8 is the last slice. It merges into `release/4.0`, and no later slice shares its lines. Within `release/4.0`:

| File | Slice 8 touches | Shared with | Risk |
| --- | --- | --- | --- |
| `Views/SettingsWindow.xaml` | the sidebar TabItem style (about :230-265), the Themes TabItem (:2085-2364), About's reset row (:2897-2901) | slice 7's banners and About `*Shown` rows (no shared line) | Low |
| `Core/SettingsNames.cs` | a slice 8 block after `EndpointHiddenNote` | slice 7's block ends there | None |
| `ViewModels/SettingsViewModel.cs` | `ResetFolders` and one line in `ReloadNames` | slice 7's `ReloadNames` | None (one added line) |
| `Core/ResetPlan.cs` | new members after `AccountProfileDirs` | slice 7's two Settings-part lines (untouched) | None |
| `tests/Sanduhr.Tests/ResetPlanTests.cs` | new facts at the end | slice 7's last fact | None |
| `docs/PRIVACY.md` | the Clear local storage bullet | slice 7's rows 32-33 | None |

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Sanduhr.Core/SettingsNames.cs` (modify) | The slice 8 block: the About caption, the list's name, the four kept lines, the unreadable and empty lines, `ResetKindOrder`, `ResetKindWord`, `ResetFolderKinds`. |
| `src/Sanduhr.Core/ResetPlan.cs` (modify) | `ResetFolder`, `ReceiptsFileName`, `ReceiptsFile`, `ReceiptsUnreadable`, `FoldersWithEntries`. |
| `src/Sanduhr.Core/ClaudeCodeReset.cs` (create) | `ClaudeCodeResetScope`, `ClaudeCodeReset.Undo(scope, keep)`. |
| `src/Sanduhr.Core/McpIntegrationInstaller.cs` (modify) | `UndoAll(homes, removeFiles = true)`. |
| `src/Sanduhr.App/Services/ResetService.cs` (modify) | The keep set; files, receipts and switches stay while anything is kept or unreadable. |
| `src/Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs` (modify) | `UndoEverything(widget, keep)` builds the scope and calls Core. |
| `src/Sanduhr.App/ViewModels/ResetFolderListViewModel.cs` (create) | `ResetFolderRow`, `ResetFolderLines`, `ResetFolderListViewModel` (`Read`, `Kept`, `UndoesAny`, `LinesFor`, `RefreshNames`, `Changed`). |
| `src/Sanduhr.App/Views/ResetWindow.xaml(.cs)` (modify) | The folder list, the lines, `EffectiveChoices`, `KeepFolders`. |
| `src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (modify) | `ResetFolders`; `ReloadNames` renames its rows. |
| `src/Sanduhr.App/App.xaml.cs` (modify) | `ShowReset` reads the list, hands it to Settings and the dialog, and passes the keep set to the undo. |
| `src/Sanduhr.App/Views/SettingsWindow.xaml` (modify) | About's caption; the sidebar's bar and selected label; the Themes page scroller and uncapped lists. |
| `src/Sanduhr.Core/ThemeContrast.cs` (create) | `ContrastSurface`, `ThemeContrast` (floors, `ChromeAlpha`, `SettingsSurfaces`, `Ratio`, `Worst`, `SelectedLabel`, `Hex`). |
| `src/Sanduhr.Core/ThemeModel.cs` (modify) | Twelve hex values. |
| `src/Sanduhr.App/Theming/ThemePalette.cs` (modify) | `ChromeAlpha` from Core; `Sanduhr.Brush.SelectedLabel`. |
| `docs/PRIVACY.md`, `docs/themes/template.json` (modify) | One sentence; two values. |
| `tools/Sanduhr.RenderHarness/SettingsPassScenario.cs` (modify) | `PassContrast` measures `ThemeContrast`'s surfaces. |

---

### Task 1: The slice's words

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (after `public const string EndpointHiddenNote = …;`)
- Test: create `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice8NamesTests.cs`

**Interfaces:**
- Consumes: `IntegrationKind` (Core), `SettingsNames.PathProblems`, `SettingsNames.ResetClaudeCodeEntries`.
- Produces:
  - `public const string ResetSanduhrCaption`, `ResetFoldersName`, `ResetNoFolders`, `ResetFoldersKept`, `ResetKeptSignIn`, `ResetKeptSettings`, `ResetKeptAdded`, `ResetReceiptsUnreadable`
  - `public static readonly IReadOnlyList<IntegrationKind> ResetKindOrder`
  - `public static string ResetKindWord(IntegrationKind kind)`
  - `public static string ResetFolderKinds(IEnumerable<IntegrationKind> kinds)`

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice8NamesTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 8: Reset's folder list and About's caption (spec, Reset; WF20, R12). Each line is exactly true in the
/// state it shows in (decision 6); the part's label and the rows' kinds come from the same four words (decision 7).</summary>
public class SettingsSlice8NamesTests
{
    [Fact]
    public void About_says_what_Reset_removes_including_themes_and_widgets()
    {
        Assert.Equal("Removes your accounts, history, session archive, themes, widgets, settings, Desk messages and Sanduhr's Claude Code entries. You choose which.",
            SettingsNames.ResetSanduhrCaption);
    }

    [Fact]
    public void The_part_label_lists_the_four_kinds_the_rows_use_in_the_same_order()
    {
        Assert.Equal([IntegrationKind.Statusline, IntegrationKind.Mcp, IntegrationKind.Meters, IntegrationKind.Hooks], SettingsNames.ResetKindOrder);
        Assert.Equal($"Sanduhr's entries in Claude Code ({SettingsNames.ResetFolderKinds(SettingsNames.ResetKindOrder)})",
            SettingsNames.ResetClaudeCodeEntries);
        Assert.Equal("statusline, meters mod", SettingsNames.ResetFolderKinds([IntegrationKind.Meters, IntegrationKind.Statusline]));
        Assert.Equal("MCP server", SettingsNames.ResetFolderKinds([IntegrationKind.Mcp]));
    }

    [Fact]
    public void The_list_and_its_lines_are_the_plans_words()
    {
        Assert.Equal("Claude Code folders to reset", SettingsNames.ResetFoldersName);
        Assert.Equal("No Claude Code folder holds anything of Sanduhr's.", SettingsNames.ResetNoFolders);
        Assert.Equal("Unticked folders keep Sanduhr's entries and the files they need. Remove them later in Settings, Claude Code.", SettingsNames.ResetFoldersKept);
        Assert.Equal("Their meters stay empty until you sign in to Sanduhr again.", SettingsNames.ResetKeptSignIn);
        Assert.Equal("With the settings reset, their meters come back once you open Settings, Claude Code.", SettingsNames.ResetKeptSettings);
        Assert.Equal("A folder you added yourself leaves that page with the settings: add it there again to remove its entries.", SettingsNames.ResetKeptAdded);
        Assert.Equal("Sanduhr's record of what it installed can't be read, so some entries can't be listed or removed here. Sanduhr keeps its files for them; remove the entries in Settings, Claude Code, or by hand.",
            SettingsNames.ResetReceiptsUnreadable);
    }

    [Fact]
    public void Every_line_names_places_the_one_way()
    {
        foreach (var text in new[]
        {
            SettingsNames.ResetSanduhrCaption, SettingsNames.ResetFoldersName, SettingsNames.ResetNoFolders, SettingsNames.ResetFoldersKept,
            SettingsNames.ResetKeptSignIn, SettingsNames.ResetKeptSettings, SettingsNames.ResetKeptAdded, SettingsNames.ResetReceiptsUnreadable,
        })
            Assert.Empty(SettingsNames.PathProblems(text));
    }
}
```

- [ ] **Step 2: Add the stubs and see the behavioural RED**

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, after the line `    public const string EndpointHiddenNote = "Streamer mode hides the address. Turn it off to edit it.";`, add the block from Step 3 with every string constant set to `""`, `ResetKindOrder` set to `[]`, and both methods returning `""`.

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsSlice8NamesTests > ../.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-1-red.log 2>&1`
Expected: 3 failures (the caption, the kinds and the words, each `Assert.Equal() Failure` against `""`). `Every_line_names_places_the_one_way` passes on empty strings, as a pin and not a gate.

- [ ] **Step 3: Write the block**

Replace the stub block with:

```csharp

    // ---- Settings v2, slice 8: Reset's folders (WF20, R12) and About's caption ----

    /// <summary>About, under Reset Sanduhr…: what it removes, so nobody has to open the dialog to learn it (spec, Reset). The spec's
    /// sentence left out the fourth part, themes and widgets (slice 8 decision 5).</summary>
    public const string ResetSanduhrCaption =
        "Removes your accounts, history, session archive, themes, widgets, settings, Desk messages and Sanduhr's Claude Code entries. You choose which.";
    /// <summary>Reset Sanduhr…: the folder list under the Claude Code part, spoken.</summary>
    public const string ResetFoldersName = "Claude Code folders to reset";
    /// <summary>Under the part when no folder holds a receipt or Sanduhr's MCP entry.</summary>
    public const string ResetNoFolders = "No Claude Code folder holds anything of Sanduhr's.";
    /// <summary>Shown while a folder is kept: the part is unticked, or a row is (decision 6; the Mac's sheet says the same).</summary>
    public const string ResetFoldersKept = "Unticked folders keep Sanduhr's entries and the files they need. Remove them later in Settings, Claude Code.";
    /// <summary>Also shown while a kept folder has meters and Accounts and sign-in keys is ticked.</summary>
    public const string ResetKeptSignIn = "Their meters stay empty until you sign in to Sanduhr again.";
    /// <summary>Also shown while a kept folder has meters and Settings is ticked: statusline_enabled goes with settings.json and comes back
    /// when the Claude Code page reads the folders (ClaudeCodeViewModel.Reload, SyncSnapshotConsumers).</summary>
    public const string ResetKeptSettings = "With the settings reset, their meters come back once you open Settings, Claude Code.";
    /// <summary>Also shown while a kept folder was added by hand and Settings is ticked: cc_folders_added goes with settings.json.</summary>
    public const string ResetKeptAdded = "A folder you added yourself leaves that page with the settings: add it there again to remove its entries.";
    /// <summary>installs.json (or a copy set aside) can't be read: everything of Sanduhr's in Claude Code stays (the Mac's rule).</summary>
    public const string ResetReceiptsUnreadable =
        "Sanduhr's record of what it installed can't be read, so some entries can't be listed or removed here. Sanduhr keeps its files for them; remove the entries in Settings, Claude Code, or by hand.";

    /// <summary>The order the part's label names the kinds in, and each row's kinds line.</summary>
    public static readonly IReadOnlyList<IntegrationKind> ResetKindOrder =
        [IntegrationKind.Statusline, IntegrationKind.Mcp, IntegrationKind.Meters, IntegrationKind.Hooks];

    /// <summary>One kind of entry as the part's label names it.</summary>
    public static string ResetKindWord(IntegrationKind kind) => kind switch
    {
        IntegrationKind.Statusline => "statusline",
        IntegrationKind.Mcp => "MCP server",
        IntegrationKind.Meters => "meters mod",
        IntegrationKind.Hooks => "glow hook",
        _ => "",
    };

    /// <summary>A row's kinds line: "statusline, meters mod", in <see cref="ResetKindOrder"/>.</summary>
    public static string ResetFolderKinds(IEnumerable<IntegrationKind> kinds)
    {
        var set = kinds.ToHashSet();
        return string.Join(", ", ResetKindOrder.Where(set.Contains).Select(ResetKindWord));
    }
```

- [ ] **Step 4: Run the class, then the names suites**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSlice8NamesTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests" > ../.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-1-green.log 2>&1`
Expected: PASS. If a names-wide scan (`SettingsNamesTests`) flags a new constant, fix the constant's words. Never exempt it.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.Tests/SettingsSlice8NamesTests.cs
git commit -m "feat(settings-v2): slice 8 words for Reset's folder list and About's caption

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `ResetPlan` lists the folders that hold Sanduhr's entries, and says when receipts can't be read

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/ResetPlan.cs`
- Test: modify `windows-dotnet/tests/Sanduhr.Tests/ResetPlanTests.cs` (new facts at the end of the class)

**Interfaces:**
- Consumes: `CcFolder`, `ClaudeCodeFolders.Norm`, `ClaudeCodeFolders.Same`, `IntegrationReceipt`, `ReceiptStore`, `ReceiptLog.AsideSuffix`, `SettingsNames.ResetKindOrder` (Task 1).
- Produces:
  - `public sealed record ResetFolder(string Path, IReadOnlyList<IntegrationKind> Kinds)`
  - `ResetPlan.ReceiptsFileName` (`"installs.json"`)
  - `public static string ResetPlan.ReceiptsFile(Paths paths)`
  - `public static bool ResetPlan.ReceiptsUnreadable(Paths paths)`
  - `public static IReadOnlyList<ResetFolder> ResetPlan.FoldersWithEntries(IEnumerable<IntegrationReceipt> receipts, IEnumerable<CcFolder> known, Func<CcFolder, bool> mcpRegistered)`

- [ ] **Step 1: Write the failing tests**

Append inside `ResetPlanTests`, after `Reset_lists_both_files_by_name_only`:

```csharp

    // ---- Settings v2, slice 8: the folders Reset lists (R12; decisions 1 and 4) ----

    private static IntegrationReceipt R(IntegrationKind kind, string folder) =>
        new() { Kind = kind, Folder = folder, File = Path.Combine(folder, "settings.json") };

    [Fact]
    public void Reset_lists_each_folder_with_entries_once_in_Settings_order_with_its_kinds()
    {
        string home = Path.Combine(_root, "home");
        string a = Path.Combine(home, ".claude"), b = Path.Combine(home, ".claude-work"), empty = Path.Combine(home, ".claude-empty");
        string gone = Path.Combine(_root, "elsewhere", ".claude-old");   // a receipt names it; Settings no longer lists it
        var known = new[] { new CcFolder(a, ".claude", true), new CcFolder(b, ".claude-work", true), new CcFolder(empty, ".claude-empty", true) };
        var receipts = new[]
        {
            R(IntegrationKind.Hooks, b),
            R(IntegrationKind.Meters, b.ToUpperInvariant() + "\\"),   // the same folder written another way
            R(IntegrationKind.Statusline, gone),
            R(IntegrationKind.Statusline, a),
        };

        var list = ResetPlan.FoldersWithEntries(receipts, known, f => f.Name == ".claude");

        Assert.Equal(new[] { a, b, gone }, list.Select(f => f.Path));
        Assert.Equal(new[] { IntegrationKind.Statusline, IntegrationKind.Mcp }, list[0].Kinds);
        Assert.Equal(new[] { IntegrationKind.Meters, IntegrationKind.Hooks }, list[1].Kinds);
        Assert.Equal(new[] { IntegrationKind.Statusline }, list[2].Kinds);
    }

    [Fact]
    public void A_folder_with_only_the_MCP_entry_is_listed_and_one_with_nothing_is_not()
    {
        string home = Path.Combine(_root, "home");
        var mcpOnly = new CcFolder(Path.Combine(home, ".claude-mcp"), ".claude-mcp", true);
        var nothing = new CcFolder(Path.Combine(home, ".claude-none"), ".claude-none", true);

        var list = ResetPlan.FoldersWithEntries([], [mcpOnly, nothing], f => f == mcpOnly);

        var only = Assert.Single(list);
        Assert.Equal(mcpOnly.Path, only.Path);
        Assert.Equal(new[] { IntegrationKind.Mcp }, only.Kinds);
    }

    [Fact]
    public void Receipts_that_cant_be_read_say_so()
    {
        var paths = Make();
        string file = ResetPlan.ReceiptsFile(paths);
        Assert.Equal(Path.Combine(paths.AppDataDir, ResetPlan.IntegrationsName, ResetPlan.ReceiptsFileName), file);
        Assert.False(ResetPlan.ReceiptsUnreadable(paths));   // no file: nothing to lose

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "[]");
        Assert.False(ResetPlan.ReceiptsUnreadable(paths));

        File.WriteAllText(file, "{ not a list");
        Assert.True(ResetPlan.ReceiptsUnreadable(paths));

        File.WriteAllText(file, "[]");
        File.WriteAllText(file + ReceiptLog.AsideSuffix, "[{\"kind\":\"meters\"");   // a damaged copy set aside earlier
        Assert.True(ResetPlan.ReceiptsUnreadable(paths));
    }
```

- [ ] **Step 2: Add the stubs and see the behavioural RED**

Add the members from Step 3 with these bodies: `ReceiptsUnreadable` returns `false`, and `FoldersWithEntries` returns `[]`. `ResetFolder`, `ReceiptsFileName` and `ReceiptsFile` go in as written.

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~ResetPlanTests > ../.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-2-red.log 2>&1`
Expected: 3 failures. `Reset_lists_each_folder…` fails on "Expected … Actual: []". `A_folder_with_only_the_MCP_entry…` fails on "Assert.Single() Failure: The collection was empty". `Receipts_that_cant_be_read…` fails on `Assert.True()` after the `{ not a list` write. Every existing fact passes.

- [ ] **Step 3: Implement**

In `windows-dotnet/src/Sanduhr.Core/ResetPlan.cs`, after the `ResetItem` record (`public sealed record ResetItem(string Path, bool IsDirectory);`), add:

```csharp

/// <summary>One Claude Code folder that holds Sanduhr's entries, for Reset's list (Settings v2, slice 8; R12): the real folder and the
/// kinds Sanduhr put there, in <see cref="SettingsNames.ResetKindOrder"/>.</summary>
public sealed record ResetFolder(string Path, IReadOnlyList<IntegrationKind> Kinds);
```

In `ResetPlan`, after the `AccountProfileDirs` method, add:

```csharp

    public const string ReceiptsFileName = "installs.json";

    /// <summary>The receipts file the installers share: <c>integrations\installs.json</c> under Sanduhr's AppData folder.</summary>
    public static string ReceiptsFile(Paths paths) => Path.Combine(paths.AppDataDir, IntegrationsName, ReceiptsFileName);

    /// <summary>
    /// True when the receipts file is there and some of it can't be read, or a damaged copy was set aside beside it
    /// (<c>installs.json*.unreadable</c>). Reset then keeps every file Sanduhr's Claude Code entries need, and the receipts, so nothing
    /// it can't list is left without a way to remove it (the Mac's <c>keepsIntegrations</c>; slice 8 decision 4). Read before the undo
    /// runs: a receipt save moves a damaged file aside.
    /// </summary>
    public static bool ReceiptsUnreadable(Paths paths)
    {
        string file = ReceiptsFile(paths);
        try
        {
            string dir = Path.GetDirectoryName(file)!;
            if (Directory.Exists(dir)
                && Directory.EnumerateFiles(dir, ReceiptsFileName + "*" + ReceiptLog.AsideSuffix).Any())
                return true;
            return File.Exists(file) && new ReceiptStore(file).Load().Skipped > 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// Every Claude Code folder that holds Sanduhr's entries, once each: the folders Settings lists, in its order, then any folder a
    /// receipt names that the list no longer has. A folder's kinds are its receipts' kinds, plus <see cref="IntegrationKind.Mcp"/> when
    /// <paramref name="mcpRegistered"/> finds Sanduhr's MCP entry in a listed folder: Windows keeps no receipt for that entry
    /// (slice 8 decision 1). A folder written two ways is one folder.
    /// </summary>
    public static IReadOnlyList<ResetFolder> FoldersWithEntries(
        IEnumerable<IntegrationReceipt> receipts, IEnumerable<CcFolder> known, Func<CcFolder, bool> mcpRegistered)
    {
        var order = new List<string>();
        var kinds = new Dictionary<string, HashSet<IntegrationKind>>(StringComparer.OrdinalIgnoreCase);
        void Add(string path, IntegrationKind kind)
        {
            string norm = ClaudeCodeFolders.Norm(path);
            if (norm.Length == 0) return;
            if (!kinds.TryGetValue(norm, out var set))
            {
                kinds[norm] = set = [];
                order.Add(norm);
            }
            set.Add(kind);
        }

        var receiptList = receipts.ToList();
        foreach (var folder in known)
        {
            foreach (var r in receiptList.Where(r => ClaudeCodeFolders.Same(r.Folder, folder.Path)))
                Add(folder.Path, r.Kind);
            if (mcpRegistered(folder))
                Add(folder.Path, IntegrationKind.Mcp);
        }
        foreach (var r in receiptList)
            Add(r.Folder, r.Kind);
        return [.. order.Select(p => new ResetFolder(p, [.. SettingsNames.ResetKindOrder.Where(kinds[p].Contains)]))];
    }
```

- [ ] **Step 4: Run the class**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~ResetPlanTests > ../.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-2-green.log 2>&1`
Expected: PASS, all facts.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/ResetPlan.cs windows-dotnet/tests/Sanduhr.Tests/ResetPlanTests.cs
git commit -m "feat(settings-v2): ResetPlan lists the Claude Code folders that hold Sanduhr's entries and says when receipts can't be read

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: `ClaudeCodeReset`: undo every folder but the kept ones, byte for byte

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/ClaudeCodeReset.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/McpIntegrationInstaller.cs` (`UndoAll`, about :402)
- Test: create `windows-dotnet/tests/Sanduhr.Tests/ClaudeCodeResetTests.cs`

**Interfaces:**
- Consumes: `ClaudeCodeIntegrations` (`Install`, `UndoAll(IEnumerable<string>?)`, `ModsRoot`), `StatuslineInstaller` (`InstallScript`, `Install`, `IsRegistered`, `Remove`, `RemoveScript`, `ScriptPath`), `McpIntegrationInstaller` (`Register`, `IsRegistered`, `Deregister`, `LauncherPath`), `SnapshotConsumers.ReadReceipts`, `SnapshotConsumers.StatuslineReceiptFolders`, `ClaudeCodeFolders`, `ResetPlan.IntegrationsName`, `ResetPlan.ReceiptsFileName` (Task 2).
- Produces:
  - `public sealed record ClaudeCodeResetScope(string ProfileDir, string SanduhrDir, string ExternalDir, string StatuslineBinDir, IReadOnlyList<CcFolder> Folders, string? SavedMcpHome, IReadOnlyList<string> LegacyModsRoots)`
  - `public static IReadOnlyList<string> ClaudeCodeReset.Undo(ClaudeCodeResetScope scope, IReadOnlyCollection<string> keep)`, which returns kinds of failures and never a path
  - `public bool McpIntegrationInstaller.UndoAll(IEnumerable<string> ccHomeNames, bool removeFiles = true)`

- [ ] **Step 1: Move today's undo into Core unchanged (the stub)**

Create `windows-dotnet/src/Sanduhr.Core/ClaudeCodeReset.cs` holding today's logic from `ClaudeCodeViewModel.UndoEverything` (`ClaudeCodeViewModel.cs:860-959`), taking a `keep` it doesn't use yet:

```csharp
namespace Sanduhr.Core;

/// <summary>Where Reset's Claude Code part works: the profile its folders sit under, Sanduhr's two folders, the statusline's bin
/// folder, the folders Settings lists and the MCP home the person picked. App code builds it from the widget; tests point it at a
/// temp tree.</summary>
public sealed record ClaudeCodeResetScope(
    string ProfileDir,
    string SanduhrDir,
    string ExternalDir,
    string StatuslineBinDir,
    IReadOnlyList<CcFolder> Folders,
    string? SavedMcpHome,
    IReadOnlyList<string> LegacyModsRoots);

/// <summary>
/// Reset Sanduhr…, the Claude Code part (spec, Reset; R12; Settings v2, slice 8): takes Sanduhr's entries out of every folder but the
/// ones in <c>keep</c>, each through its installer's Remove, which takes out only what still points at Sanduhr's files. A kept folder is
/// not opened for writing. While any folder is kept, the files its entries run stay (the statusline script, the MCP launcher and
/// server copies); Reset's service then keeps Sanduhr's copies, receipts and switches too. Returns kinds of failures, never a path.
/// </summary>
public static class ClaudeCodeReset
{
    public static IReadOnlyList<string> Undo(ClaudeCodeResetScope scope, IReadOnlyCollection<string> keep)
    {
        var failures = new List<string>();
        string receiptsPath = Path.Combine(scope.SanduhrDir, ResetPlan.IntegrationsName, ResetPlan.ReceiptsFileName);
        var (receipts, _) = SnapshotConsumers.ReadReceipts(receiptsPath);

        // The meters mod and the glow hooks, receipt by receipt.
        try
        {
            var integ = new ClaudeCodeIntegrations(scope.ProfileDir, scope.SanduhrDir, externalDir: scope.ExternalDir)
            {
                LegacyModsRoots = scope.LegacyModsRoots,
            };
            if (integ.UndoAll().Any(r => !r.Ok))
                failures.Add("a Claude Code entry");
        }
        catch (Exception) { failures.Add("a Claude Code entry"); }

        // The statusline: known folders first, then any folder a receipt names that is no longer listed, so Reset never leaves an
        // orphan statusLine. Each folder once.
        var statusline = new StatuslineInstaller(scope.ProfileDir, scope.StatuslineBinDir, null, scope.SanduhrDir);
        var statuslineFolders = scope.Folders.Select(f => f.Path).ToList();
        foreach (var folder in SnapshotConsumers.StatuslineReceiptFolders(receipts))
            if (!statuslineFolders.Any(p => ClaudeCodeFolders.Same(p, folder)))
                statuslineFolders.Add(folder);
        foreach (var folder in statuslineFolders)
        {
            try
            {
                // A rooted second argument to Path.Combine returns it, so a full folder path works as the home name.
                if (statusline.IsRegistered(folder) && !statusline.Remove(folder))
                    failures.Add("a statusline entry");
            }
            catch (Exception) { failures.Add("a statusline entry"); }
        }
        try { statusline.RemoveScript(); } catch (Exception) { failures.Add("the statusline script"); }

        // The MCP server: deregistered from every discovered home and the one the person picked.
        try
        {
            var mcp = new McpIntegrationInstaller(scope.ProfileDir, scope.SanduhrDir, externalDir: scope.ExternalDir);
            var homes = scope.Folders.Where(f => f.Discovered).Select(f => f.Name)
                .Concat(scope.SavedMcpHome is { Length: > 0 } saved ? [saved] : [])
                .Distinct(StringComparer.OrdinalIgnoreCase);
            // A failed deregister keeps the launcher and server files, as the reset promises.
            if (!mcp.UndoAll(homes))
                failures.Add("an MCP server entry");
        }
        catch (Exception) { failures.Add("an MCP server entry"); }

        return [.. failures.Distinct()];
    }
}
```

This step changes no App code: `UndoEverything` keeps working until Task 4 moves it here.

- [ ] **Step 2: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/ClaudeCodeResetTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Settings v2, slice 8 (R12; decisions 2 and 3): Reset's Claude Code part undoes every folder but the kept ones. A kept folder ends
/// byte for byte as it was, the files its entries run stay, and an undone folder loses only what still points at Sanduhr's files.
/// Real installers on a made-up profile under the temp folder.
/// </summary>
public sealed class ClaudeCodeResetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-ccreset-" + Guid.NewGuid().ToString("N"));
    private string Profile => Path.Combine(_root, "home");
    private string SanduhrDir => Path.Combine(_root, "appdata", "Sanduhr");
    private string ExternalDir => Path.Combine(Profile, ".sanduhr");
    private string BinDir => Path.Combine(ExternalDir, "bin");
    private string Source => Path.Combine(_root, "app", "mods", "sanduhr-meters");
    private string Receipts => Path.Combine(SanduhrDir, ResetPlan.IntegrationsName, ResetPlan.ReceiptsFileName);
    private const string Settings = "{\n  \"theme\": \"dark\"\n}\n";

    public ClaudeCodeResetTests()
    {
        Directory.CreateDirectory(Profile);
        Write(Source, ".claude-plugin/plugin.json", "{ \"name\": \"sanduhr-meters\", \"version\": \"0.3.0\" }");
        Write(Source, "hooks/hooks.json", "{ \"modules\": [\"./register.tsx\"] }");
        Write(Source, "hooks/register.tsx", "export {};");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void Write(string dir, string rel, string text)
    {
        var p = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    /// <summary>A discovered home (it has projects\) with a settings.json of its own.</summary>
    private string Home(string name)
    {
        var dir = Path.Combine(Profile, name);
        Directory.CreateDirectory(Path.Combine(dir, "projects"));
        File.WriteAllText(Path.Combine(dir, "settings.json"), Settings);
        return dir;
    }

    private McpIntegrationInstaller Mcp() => new(Profile, SanduhrDir, externalDir: ExternalDir);

    /// <summary>All four of Sanduhr's entries in one folder, the way Settings installs them.</summary>
    private void InstallAll(string folder)
    {
        var integ = new ClaudeCodeIntegrations(Profile, SanduhrDir, Source, externalDir: ExternalDir);
        var meters = integ.Install(IntegrationKind.Meters, folder);
        Assert.True(meters.Ok, meters.Message);
        var hooks = integ.Install(IntegrationKind.Hooks, folder);
        Assert.True(hooks.Ok, hooks.Message);
        var statusline = new StatuslineInstaller(Profile, BinDir, null, SanduhrDir);
        Assert.True(statusline.InstallScript());
        Assert.True(statusline.Install(folder, StatuslineMode.Replace, StatuslineInstaller.DefaultSegments, StatuslineInstaller.JoinLine));
        var mcp = Mcp();
        Assert.True(mcp.Register(Path.GetFileName(folder)).Ok);
        Directory.CreateDirectory(Path.GetDirectoryName(mcp.LauncherPath)!);
        File.WriteAllText(mcp.LauncherPath, "@echo off");
    }

    private ClaudeCodeResetScope Scope() =>
        new(Profile, SanduhrDir, ExternalDir, BinDir, ClaudeCodeFolders.Build(Profile, null, []), null, []);

    /// <summary>Every file under a folder, by path, with its bytes.</summary>
    private static Dictionary<string, byte[]> Snapshot(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

    private static void Same(Dictionary<string, byte[]> before, Dictionary<string, byte[]> after, string what)
    {
        Assert.Equal(before.Keys.OrderBy(k => k), after.Keys.OrderBy(k => k));
        foreach (var (file, bytes) in before)
            Assert.True(bytes.AsSpan().SequenceEqual(after[file]), $"{what}: {Path.GetFileName(file)} changed");
    }

    private void Clean(string folder)
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "settings.json")))!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Settings), json), $"{Path.GetFileName(folder)} still holds: {json.ToJsonString()}");
        Assert.False(Mcp().IsRegistered(Path.GetFileName(folder)));
    }

    [Fact]
    public void A_kept_folder_ends_byte_for_byte_and_keeps_its_receipts()
    {
        string undo = Home(".claude-undo"), keep = Home(".claude-keep");
        InstallAll(undo);
        InstallAll(keep);
        var before = Snapshot(keep);

        var failures = ClaudeCodeReset.Undo(Scope(), [keep]);

        Assert.Empty(failures);
        Same(before, Snapshot(keep), "the kept folder");
        Clean(undo);
        var left = new ReceiptStore(Receipts).Load().Records;
        Assert.NotEmpty(left);
        Assert.All(left, r => Assert.True(ClaudeCodeFolders.Same(r.Folder, keep), $"a receipt for {Path.GetFileName(r.Folder)} survived"));
    }

    [Fact]
    public void While_a_folder_is_kept_the_files_its_entries_run_stay()
    {
        string undo = Home(".claude-undo"), keep = Home(".claude-keep");
        InstallAll(undo);
        InstallAll(keep);

        ClaudeCodeReset.Undo(Scope(), [keep]);

        Assert.True(File.Exists(Mcp().LauncherPath), "the MCP launcher went");
        Assert.True(File.Exists(new StatuslineInstaller(Profile, BinDir).ScriptPath), "the statusline script went");
        Assert.True(Directory.EnumerateFileSystemEntries(Path.Combine(ExternalDir, "mods")).Any(), "the meters mod's copy went");
    }

    [Fact]
    public void A_kept_folder_matches_however_it_is_written()
    {
        string undo = Home(".claude-undo"), keep = Home(".claude-keep");
        InstallAll(undo);
        InstallAll(keep);
        var before = Snapshot(keep);

        ClaudeCodeReset.Undo(Scope(), [keep.ToUpperInvariant() + "\\"]);

        Same(before, Snapshot(keep), "the kept folder, written in capitals with a trailing slash");
        Clean(undo);
    }

    [Fact]
    public void Keeping_dot_claude_leaves_the_claude_json_beside_the_profile()
    {
        string dotClaude = Home(".claude"), undo = Home(".claude-undo");
        InstallAll(dotClaude);
        InstallAll(undo);
        string beside = Mcp().ConfigPathFor(".claude");   // .claude's MCP entry lives here, outside the folder
        Assert.True(File.Exists(beside));
        var before = Snapshot(dotClaude);
        var besideBytes = File.ReadAllBytes(beside);

        ClaudeCodeReset.Undo(Scope(), [dotClaude]);

        Same(before, Snapshot(dotClaude), ".claude");
        Assert.Equal(besideBytes, File.ReadAllBytes(beside));
        Assert.True(Mcp().IsRegistered(".claude"));
        Clean(undo);
    }

    /// <summary>Today's reset, pinned: it passes before and after this task.</summary>
    [Fact]
    public void With_nothing_kept_every_folder_is_undone_and_the_launcher_and_script_go()
    {
        string a = Home(".claude-a"), b = Home(".claude-b");
        InstallAll(a);
        InstallAll(b);

        Assert.Empty(ClaudeCodeReset.Undo(Scope(), []));

        Clean(a);
        Clean(b);
        Assert.False(File.Exists(Mcp().LauncherPath));
        Assert.False(File.Exists(new StatuslineInstaller(Profile, BinDir).ScriptPath));
    }

    /// <summary>Remove's rule, through Reset: what someone else put beside Sanduhr's entry stays. Passes before and after this task.</summary>
    [Fact]
    public void An_undone_folder_keeps_what_does_not_point_at_Sanduhr()
    {
        string undo = Home(".claude-undo");
        InstallAll(undo);
        string file = Path.Combine(undo, "settings.json");
        var root = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        string theirs = Path.Combine(_root, "theirs", "plugin");
        var env = root["env"]!.AsObject();
        env[ClaudeCodeIntegrations.PluginDirsKey] = env[ClaudeCodeIntegrations.PluginDirsKey]!.GetValue<string>()
            + ClaudeCodeIntegrations.PluginDirsSeparator + theirs;
        File.WriteAllText(file, root.ToJsonString());

        ClaudeCodeReset.Undo(Scope(), []);

        var after = JsonNode.Parse(File.ReadAllText(file))!;
        Assert.Equal(theirs, after["env"]?[ClaudeCodeIntegrations.PluginDirsKey]?.GetValue<string>());
        Assert.Null(after["statusLine"]);
        Assert.Null(after["hooks"]);
    }
}
```

- [ ] **Step 3: Run them and see the behavioural RED**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~ClaudeCodeResetTests > ../.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-3-red.log 2>&1`
Expected: 4 failures, all behavioural:
- `A_kept_folder_ends_byte_for_byte…`: "the kept folder: settings.json changed".
- `While_a_folder_is_kept…`: "the MCP launcher went".
- `A_kept_folder_matches…`: "…settings.json changed".
- `Keeping_dot_claude…`: ".claude: settings.json changed".

`With_nothing_kept…` and `An_undone_folder_keeps…` pass: they pin today's reset. If `InstallAll` itself fails (an installer refuses the made-up tree), fix the setup first. That is not the RED.

- [ ] **Step 4: Implement the keep set**

In `windows-dotnet/src/Sanduhr.Core/McpIntegrationInstaller.cs`, replace

```csharp
    public bool UndoAll(IEnumerable<string> ccHomeNames)
    {
        bool all = true;
        foreach (var home in ccHomeNames)
            all &= Deregister(home);
        if (all)
            RemoveIntegrationFiles();
        return all;
    }
```

with

```csharp
    /// <param name="removeFiles">False while Reset keeps a folder whose entry still runs the launcher (slice 8, R12).</param>
    public bool UndoAll(IEnumerable<string> ccHomeNames, bool removeFiles = true)
    {
        bool all = true;
        foreach (var home in ccHomeNames)
            all &= Deregister(home);
        if (all && removeFiles)
            RemoveIntegrationFiles();
        return all;
    }
```

Then replace the body of `ClaudeCodeReset.Undo` with:

```csharp
        var failures = new List<string>();
        bool keepsAny = keep.Count > 0;
        bool Kept(string path) => keep.Any(k => ClaudeCodeFolders.Same(k, path));
        string receiptsPath = Path.Combine(scope.SanduhrDir, ResetPlan.IntegrationsName, ResetPlan.ReceiptsFileName);
        var (receipts, _) = SnapshotConsumers.ReadReceipts(receiptsPath);

        // The meters mod and the glow hooks, receipt by receipt. With nothing kept, every receipt (today's reset); else only the
        // folders not kept, listed or named by a receipt.
        try
        {
            var integ = new ClaudeCodeIntegrations(scope.ProfileDir, scope.SanduhrDir, externalDir: scope.ExternalDir)
            {
                LegacyModsRoots = scope.LegacyModsRoots,
            };
            IEnumerable<string>? only = keepsAny
                ? [.. receipts.Select(r => r.Folder).Concat(scope.Folders.Select(f => f.Path)).Where(p => !Kept(p))]
                : null;
            if (integ.UndoAll(only).Any(r => !r.Ok))
                failures.Add("a Claude Code entry");
        }
        catch (Exception) { failures.Add("a Claude Code entry"); }

        // The statusline: known folders first, then any folder a receipt names that is no longer listed, so Reset never leaves an
        // orphan statusLine. Each folder once; a kept folder is skipped, and its script stays.
        var statusline = new StatuslineInstaller(scope.ProfileDir, scope.StatuslineBinDir, null, scope.SanduhrDir);
        var statuslineFolders = scope.Folders.Select(f => f.Path).ToList();
        foreach (var folder in SnapshotConsumers.StatuslineReceiptFolders(receipts))
            if (!statuslineFolders.Any(p => ClaudeCodeFolders.Same(p, folder)))
                statuslineFolders.Add(folder);
        foreach (var folder in statuslineFolders.Where(f => !Kept(f)))
        {
            try
            {
                // A rooted second argument to Path.Combine returns it, so a full folder path works as the home name.
                if (statusline.IsRegistered(folder) && !statusline.Remove(folder))
                    failures.Add("a statusline entry");
            }
            catch (Exception) { failures.Add("a statusline entry"); }
        }
        if (!keepsAny)
        {
            try { statusline.RemoveScript(); } catch (Exception) { failures.Add("the statusline script"); }
        }

        // The MCP server: deregistered from every discovered home and the one the person picked, except kept ones. The launcher and
        // server files go only when nothing is kept and every deregister worked.
        try
        {
            var mcp = new McpIntegrationInstaller(scope.ProfileDir, scope.SanduhrDir, externalDir: scope.ExternalDir);
            var homes = scope.Folders.Where(f => f.Discovered).Select(f => f.Name)
                .Concat(scope.SavedMcpHome is { Length: > 0 } saved ? [saved] : [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(home => !Kept(Path.Combine(scope.ProfileDir, home)));
            if (!mcp.UndoAll(homes, removeFiles: !keepsAny))
                failures.Add("an MCP server entry");
        }
        catch (Exception) { failures.Add("an MCP server entry"); }

        return [.. failures.Distinct()];
```

- [ ] **Step 5: Run the class and the installers' suites**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~ClaudeCodeResetTests|FullyQualifiedName~McpIntegrationInstallerTests|FullyQualifiedName~ClaudeCodeIntegrationsTests|FullyQualifiedName~StatuslineInstaller" > ../.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-3-green.log 2>&1`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/ClaudeCodeReset.cs windows-dotnet/src/Sanduhr.Core/McpIntegrationInstaller.cs windows-dotnet/tests/Sanduhr.Tests/ClaudeCodeResetTests.cs
git commit -m "feat(settings-v2): Reset's Claude Code undo leaves kept folders byte for byte and keeps the files their entries run

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: `ResetService` keeps copies, receipts and switches while a folder is kept or receipts can't be read

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Services/ResetService.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs` (`UndoEverything`, :858-959)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (`ShowReset`, :1079-1103)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/ResetWindow.xaml.cs` (`Reset_Click`'s call, keep `[]` until Task 5)
- Modify: `windows-dotnet/tests/Sanduhr.Tests/StreamerReachTests.cs` (one count)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/ResetServiceFoldersTests.cs`

**Interfaces:**
- Consumes: `ClaudeCodeReset.Undo`, `ClaudeCodeResetScope` (Task 3), `ResetPlan.ReceiptsUnreadable`, `ResetPlan.ReceiptsFile` (Task 2).
- Produces:
  - `ResetService(Paths, AccountStore, PublishTokenStore, Func<IReadOnlyCollection<string>, Task<IReadOnlyList<string>>> undoClaudeCodeEntries, Action stopBackgroundWork, Func<Task<int>>? deleteAccountProfiles = null, Action? clearClaudeCodeSwitches = null)`
  - `Task<IReadOnlyList<ResetStepResult>> ResetService.RunAsync(ResetChoices choices, IReadOnlyCollection<string> keepFolders, Action<ResetPart>? started = null)`
  - `public static IReadOnlyList<string> ClaudeCodeViewModel.UndoEverything(WidgetViewModel widget, IReadOnlyCollection<string>? keep = null)`

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/ResetServiceFoldersTests.cs`:

```csharp
using System.IO;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 8 (R12; decisions 3 and 4): Reset's Claude Code part keeps Sanduhr's copies, its receipts and the two switches
/// while any folder is kept or the receipts can't be read, and removes them as before when every folder was undone. No window, no
/// static: a scratch tree and a fake undo.
/// </summary>
public sealed class ResetServiceFoldersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-resetsvc-" + Guid.NewGuid().ToString("N"));
    private readonly Paths _paths;

    public ResetServiceFoldersTests()
    {
        _paths = new Paths(Path.Combine(_root, "roaming"), Path.Combine(_root, "home"), Path.Combine(_root, "local"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    /// <summary>Sanduhr's copies of its Claude Code pieces, in both places the inventory lists them.</summary>
    private List<string> Seed()
    {
        string ext = _paths.ExternalDirPath;
        var files = new List<string>
        {
            Path.Combine(ext, ResetPlan.ModsName, "sanduhr-meters", "x"),
            Path.Combine(ext, ResetPlan.McpName, "v1", "x"),
            Path.Combine(ext, ResetPlan.BinName, "sanduhr-mcp.cmd"),
            Path.Combine(ext, ResetPlan.McpSettingsName),
        };
        foreach (var f in files) Touch(f);
        Directory.CreateDirectory(Path.GetDirectoryName(ResetPlan.ReceiptsFile(_paths))!);
        File.WriteAllText(ResetPlan.ReceiptsFile(_paths), "[]");
        files.Add(ResetPlan.ReceiptsFile(_paths));
        return files;
    }

    private (ResetService Service, List<IReadOnlyCollection<string>> Asked, Func<int> Cleared) Make()
    {
        var asked = new List<IReadOnlyCollection<string>>();
        int cleared = 0;
        var service = new ResetService(
            _paths,
            new AccountStore(new AppWindowFixture.MemoryCredentials()),
            new PublishTokenStore(new AppWindowFixture.MemoryCredentials()),
            keep => { asked.Add(keep); return Task.FromResult<IReadOnlyList<string>>([]); },
            () => { },
            clearClaudeCodeSwitches: () => cleared++);
        return (service, asked, () => cleared);
    }

    private static readonly ResetChoices ClaudeCodeOnly = new(ClaudeCodeEntries: true, Accounts: false, History: false, Themes: false, Settings: false);

    [Fact]
    public async Task A_kept_folder_keeps_every_copy_the_receipts_and_the_switches()
    {
        var files = Seed();
        var (service, asked, cleared) = Make();
        string kept = Path.Combine(_root, "home", ".claude-keep");

        var results = await service.RunAsync(ClaudeCodeOnly, [kept]);

        Assert.True(Assert.Single(results).Ok);
        Assert.Equal(kept, Assert.Single(Assert.Single(asked)));   // the undo is told which folder stays
        Assert.All(files, f => Assert.True(File.Exists(f), $"{Path.GetFileName(f)} was deleted"));
        Assert.Equal(0, cleared());
    }

    [Fact]
    public async Task Unreadable_receipts_keep_every_copy_even_with_nothing_kept()
    {
        var files = Seed();
        File.WriteAllText(ResetPlan.ReceiptsFile(_paths), "{ not a list");
        var (service, _, cleared) = Make();

        await service.RunAsync(ClaudeCodeOnly, []);

        Assert.All(files, f => Assert.True(File.Exists(f), $"{Path.GetFileName(f)} was deleted"));
        Assert.Equal(0, cleared());
    }

    /// <summary>Today's reset, pinned: passes before and after this task.</summary>
    [Fact]
    public async Task With_nothing_kept_the_copies_and_switches_go_as_before()
    {
        var files = Seed();
        var (service, _, cleared) = Make();

        await service.RunAsync(ClaudeCodeOnly, []);

        Assert.All(files, f => Assert.False(File.Exists(f), $"{Path.GetFileName(f)} stayed"));
        Assert.Equal(1, cleared());
    }
}
```

- [ ] **Step 2: Add the parameter, change nothing else, and see the behavioural RED**

In `ResetService.cs`:
- change the field and the constructor parameter `undoClaudeCodeEntries` to `Func<IReadOnlyCollection<string>, Task<IReadOnlyList<string>>>`
- change `RunAsync(ResetChoices choices, Action<ResetPart>? started = null)` to `RunAsync(ResetChoices choices, IReadOnlyCollection<string> keepFolders, Action<ResetPart>? started = null)`
- pass `keepFolders` to `ClaudeCodeEntriesAsync(keepFolders)`, whose new parameter `keep` only goes to `await _undoClaudeCodeEntries(keep)`

Make two callers compile:
- In `App.xaml.cs`, `ShowReset`'s fourth argument becomes `keep => Task.Run<IReadOnlyList<string>>(() => ClaudeCodeViewModel.UndoEverything(vm, keep)),`.
- In `ResetWindow.xaml.cs`, `Reset_Click` calls `_service.RunAsync(choices, [], part => ShowRunning($"Resetting: {PartLabel(part)}"))`.

In `ClaudeCodeViewModel.cs`, add the parameter `IReadOnlyCollection<string>? keep = null` to `UndoEverything` and leave the body as it is.

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~ResetServiceFoldersTests > ../.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-4-red.log 2>&1`
Expected: 2 failures. `A_kept_folder_keeps…` fails on "sanduhr-meters … was deleted" (or the first seeded file's name), and so does `Unreadable_receipts_keep…`. `With_nothing_kept…` passes.

- [ ] **Step 3: Implement**

In `ResetService.cs`, replace the whole `ClaudeCodeEntriesAsync` method with:

```csharp
    private async Task<ResetStepResult> ClaudeCodeEntriesAsync(IReadOnlyCollection<string> keep)
    {
        var failures = new List<string>();
        // Read before the undo: a receipt save moves a damaged file aside and leaves a readable one (slice 8 decision 4).
        bool unreadable = ResetPlan.ReceiptsUnreadable(_paths);
        try
        {
            failures.AddRange(await _undoClaudeCodeEntries(keep));
        }
        catch (Exception)
        {
            failures.Add("a Claude Code entry");
        }

        // A failed entry keeps its receipt (and the files it points at) so it can still be removed later. So does a folder the person
        // left unticked, and every entry when the receipts can't be read (slice 8, R12): Sanduhr's copies, the receipts and the two
        // switches stay, so those entries keep working and Settings, Claude Code can remove them.
        if (failures.Count == 0 && keep.Count == 0 && !unreadable)
        {
            // The entries are out: their switches go off too, or the next launch re-creates the projection,
            // the launcher and the script. A Settings part that runs later deletes the file anyway.
            try { _clearClaudeCodeSwitches?.Invoke(); } catch (Exception) { /* best effort */ }
            failures.AddRange(await DeleteItemsAsync(ResetPart.ClaudeCodeEntries));
        }
        return ResetStepResult.Failed(ResetPart.ClaudeCodeEntries, Distinct(failures));
    }
```

Update the class summary's second paragraph to read: "The Claude Code undo is the orchestrator's: `undoClaudeCodeEntries` takes the folders to leave as they are and returns the kinds of failures (empty when every entry went). Sanduhr's own copies of those entries (receipts, mods, the MCP server copy, the bin scripts) are deleted only when the undo reported none, no folder was kept and the receipts could be read, because a kept or failed folder keeps its receipt and the files its entries run."

In `ClaudeCodeViewModel.cs`, replace the whole `UndoEverything` method, from its `/// <summary>` through its closing brace, with:

```csharp
    /// <summary>
    /// Reset Sanduhr…: undoes Sanduhr's entries in every Claude Code folder but the ones in <paramref name="keep"/> (real folders, never
    /// what a row shows), through <see cref="ClaudeCodeReset"/>. Returns the kinds of failures, never a path. Safe to call off the UI thread.
    /// </summary>
    public static IReadOnlyList<string> UndoEverything(WidgetViewModel widget, IReadOnlyCollection<string>? keep = null)
    {
        var profile = ClaudeCodeProfile.Dir;   // streamer: key - profile folder handed to the reset logic, never drawn
        var scope = new ClaudeCodeResetScope(
            profile,
            widget.SanduhrAppDataDir,
            widget.SanduhrExternalDir,
            widget.StatuslineBinDir,
            ClaudeCodeFolders.Build(profile, ClaudeCodeProfile.ConfigDir, widget.LoadCcFoldersAdded()),   // streamer: key - folder list handed to the reset logic, never drawn
            widget.LoadMcpCcHome(),
            [widget.SanduhrLegacyModsRoot]);
        return ClaudeCodeReset.Undo(scope, keep ?? []);
    }
```

The old body had three marked readers: the profile, the folder list, and `folders.Select(f => f.Path)`. The new one has two.

In `windows-dotnet/tests/Sanduhr.Tests/StreamerReachTests.cs`, change

```csharp
        ("Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs", "key", 17),
```

to

```csharp
        ("Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs", "key", 16),   // slice 8: the undo moved to Core (ClaudeCodeReset)
```

- [ ] **Step 4: Run the touched classes, then both suites once**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~ResetServiceFoldersTests > ../.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-4-green.log 2>&1`
Expected: PASS.

Run: `dotnet test tests/Sanduhr.Tests > ../.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-4-core.log 2>&1` and `dotnet test tests/Sanduhr.App.Tests > ../.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-4-app.log 2>&1`
Expected: PASS. `StreamerReachTests` reports `ClaudeCodeViewModel.cs key 16`. If it reports another count, read the method you wrote against Step 3.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Services/ResetService.cs windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs windows-dotnet/src/Sanduhr.App/App.xaml.cs windows-dotnet/src/Sanduhr.App/Views/ResetWindow.xaml.cs windows-dotnet/tests/Sanduhr.App.Tests/ResetServiceFoldersTests.cs windows-dotnet/tests/Sanduhr.Tests/StreamerReachTests.cs
git commit -m "feat(settings-v2): Reset keeps Sanduhr's Claude Code copies, receipts and switches while a folder is kept or receipts can't be read

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Reset's per-folder list: codenames on stream, real folders underneath, lines true in their state

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/ResetFolderListViewModel.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/ResetWindow.xaml`, `ResetWindow.xaml.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (`ResetFolders`, `ReloadNames`)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (`ShowReset`)
- Modify: `windows-dotnet/tests/Sanduhr.Tests/StreamerReachTests.cs` (two rows)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/ResetWindowFoldersTests.cs`

**Interfaces:**
- Consumes: `ResetPlan.FoldersWithEntries`, `ResetPlan.ReceiptsFile`-equivalent path, `ReceiptStore`, `McpIntegrationInstaller.IsRegistered`, `StreamerMode.ShowFolder`, `ClaudeCodeProfile`, the slice 8 words (Task 1), `ResetService.RunAsync(choices, keep, started)` (Task 4).
- Produces:
  - `internal sealed class ResetFolderRow`: `Path`, `Discovered`, `HasMeters`, `Kinds`, `Shown`, `Undo`, `RefreshName()`
  - `internal readonly record struct ResetFolderLines(bool NoFolders, bool Unreadable, bool Kept, bool KeptSignIn, bool KeptSettings, bool KeptAdded)`
  - `internal sealed class ResetFolderListViewModel`: `Rows`, `ReceiptsUnreadable`, `Kept`, `UndoesAny`, `Changed`, `RefreshNames()`, `LinesFor(bool part, bool accounts, bool settings)`, `static Read(WidgetViewModel)`, `static For(IReadOnlyList<ResetFolderRow>, bool)` (tests)
  - `ResetWindow(ResetService? service = null, ResetFolderListViewModel? folders = null)`, `ShowFor(Window?, ResetService, ResetFolderListViewModel, Action)`, `internal ResetChoices EffectiveChoices`, `internal IReadOnlyList<string> KeepFolders`
  - `internal ResetFolderListViewModel? SettingsViewModel.ResetFolders { get; set; }`

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/ResetWindowFoldersTests.cs`:

```csharp
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Sanduhr.App.Services;
using Sanduhr.App.Updates;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 8 (spec, Reset; R12; decisions 6, 13 and 14): the folder list under Reset's Claude Code part. Rows show folders
/// as every page does (codenames while streamer mode hides them) and key on the real folder; a streamer flip renames them through
/// ReloadNames; each line shows only in the state it is true for.
/// </summary>
[Collection("SettingsWindow")]
public sealed class ResetWindowFoldersTests : AppWindowFixture
{
    private static void NoSentinel(string where, string? text) =>
        Assert.False((text ?? "").Contains("qx", StringComparison.OrdinalIgnoreCase), $"{where}: \"{text}\"");

    /// <summary>Two discovered folders with receipts (statusline and meters in one, hooks in the other) and one added by hand.</summary>
    private (WidgetViewModel Widget, string Keep, string Other, string Added) Make()
    {
        string keep = Home(".claude-qxkeep"), other = Home(".claude-qxother");
        string added = Path.Combine(Root, "elsewhere", "qxadded");
        Directory.CreateDirectory(added);
        var (widget, _) = ScratchWidget("Qxalpha");
        new SettingsStore(ScratchPaths()).SaveCcFoldersAdded([added]);
        var file = Path.Combine(widget.SanduhrAppDataDir, ResetPlan.IntegrationsName, ResetPlan.ReceiptsFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        new ReceiptStore(file).Save(
        [
            new IntegrationReceipt { Kind = IntegrationKind.Statusline, Folder = keep, File = Path.Combine(keep, "settings.json") },
            new IntegrationReceipt { Kind = IntegrationKind.Meters, Folder = keep, File = Path.Combine(keep, "settings.json") },
            new IntegrationReceipt { Kind = IntegrationKind.Hooks, Folder = other, File = Path.Combine(other, "settings.json") },
            new IntegrationReceipt { Kind = IntegrationKind.Meters, Folder = added, File = Path.Combine(added, "settings.json") },
        ]);
        return (widget, keep, other, added);
    }

    [Fact]
    public void The_list_holds_each_folder_with_Sanduhrs_entries_and_its_kinds() => AppHost.Run(() =>
    {
        var (widget, keep, other, added) = Make();
        try
        {
            var list = ResetFolderListViewModel.Read(widget);
            Assert.False(list.ReceiptsUnreadable);
            Assert.Equal(new[] { keep, other, added }, list.Rows.Select(r => r.Path));
            Assert.Equal("statusline, meters mod", list.Rows[0].Kinds);
            Assert.Equal("glow hook", list.Rows[1].Kinds);
            Assert.True(list.Rows[0].Discovered);
            Assert.False(list.Rows[2].Discovered);
            Assert.All(list.Rows, r => Assert.True(r.Undo));   // owner taste 5: all ticked
            Assert.Equal("~\\.claude-qxkeep", list.Rows[0].Shown);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void With_streamer_mode_on_a_row_shows_its_codename_and_the_choice_keeps_the_real_folder() => AppHost.Run(() =>
    {
        var (widget, keep, _, _) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            var list = ResetFolderListViewModel.Read(widget);
            Assert.All(list.Rows.Take(2), r => NoSentinel("row", r.Shown + r.ToString()));
            Assert.All(list.Rows.Take(2), r => Assert.True(Codenames.IsCodename(r.Shown), r.Shown));

            var window = new ResetWindow(null, list);
            OffScreen(window);
            window.Show();
            try
            {
                var box = Boxes(window).Single(b => (string)b.Content == list.Rows[0].Shown);
                box.IsChecked = false;   // through the binding, as a click would
                Assert.Equal(new[] { keep }, window.KeepFolders);   // the real folder, never the codename
                Assert.All(NamesOf(window), n => NoSentinel("spoken", n));
            }
            finally { window.Close(); }
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_streamer_flip_while_Reset_is_open_renames_the_rows() => AppHost.Run(() =>
    {
        var (widget, _, _, _) = Make();
        try
        {
            var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
                checkForUpdatesNow: () => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate)), isStoreBuild: false);
            var list = ResetFolderListViewModel.Read(widget);
            Assert.Equal("~\\.claude-qxkeep", list.Rows[0].Shown);   // off: the real name, so the check below is not vacuous
            svm.ResetFolders = list;

            widget.Streamer.SetOn(true);
            svm.ReloadNames();   // what StreamerSettingsViewModel does on the service's Changed

            Assert.All(list.Rows.Take(2), r => NoSentinel("row after the flip", r.Shown));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Each_line_shows_only_in_the_state_it_is_true_for() => AppHost.Run(() =>
    {
        var (widget, _, _, _) = Make();
        try
        {
            var list = ResetFolderListViewModel.Read(widget);

            Assert.Equal(default, list.LinesFor(part: true, accounts: false, settings: false));   // every row ticked: nothing kept

            list.Rows[1].Undo = false;   // keep the hooks-only folder: no meters, discovered
            var l = list.LinesFor(part: true, accounts: true, settings: true);
            Assert.True(l.Kept);
            Assert.False(l.KeptSignIn);   // it has no meters
            Assert.False(l.KeptSettings);
            Assert.False(l.KeptAdded);

            list.Rows[0].Undo = false;   // and the one with meters
            l = list.LinesFor(part: true, accounts: true, settings: false);
            Assert.True(l.KeptSignIn);
            Assert.False(l.KeptSettings);

            list.Rows[2].Undo = false;   // and the one added by hand
            l = list.LinesFor(part: true, accounts: false, settings: true);
            Assert.True(l.KeptSettings);
            Assert.True(l.KeptAdded);
            Assert.False(l.KeptSignIn);

            foreach (var r in list.Rows) r.Undo = true;
            l = list.LinesFor(part: false, accounts: false, settings: false);   // the part off keeps every folder
            Assert.True(l.Kept);
            Assert.False(l.NoFolders);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void An_empty_list_says_so_and_unreadable_receipts_say_why() => AppHost.Run(() =>
    {
        var empty = ResetFolderListViewModel.For([], unreadable: false);
        Assert.Equal(new ResetFolderLines(NoFolders: true, false, false, false, false, false), empty.LinesFor(true, true, true));
        var unreadable = ResetFolderListViewModel.For([], unreadable: true);
        Assert.Equal(new ResetFolderLines(NoFolders: false, Unreadable: true, false, false, false, false), unreadable.LinesFor(true, true, true));

        var window = new ResetWindow(null, unreadable);
        OffScreen(window);
        window.Show();
        try
        {
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("UnreadableText")).Visibility);
            Assert.Equal(SettingsNames.ResetReceiptsUnreadable, ((TextBlock)window.FindName("UnreadableText")).Text);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("NoFoldersText")).Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Reset_is_off_when_the_only_part_left_would_change_nothing() => AppHost.Run(() =>
    {
        var (widget, _, _, _) = Make();
        try
        {
            var list = ResetFolderListViewModel.Read(widget);
            var window = new ResetWindow(null, list);
            OffScreen(window);
            window.Show();
            try
            {
                foreach (var name in new[] { "AccountsBox", "HistoryBox", "ThemesBox", "SettingsBox" })
                    ((CheckBox)window.FindName(name)).IsChecked = false;
                var reset = (Button)window.FindName("ResetButton");
                Assert.True(reset.IsEnabled);
                foreach (var box in Boxes(window)) box.IsChecked = false;
                Assert.False(reset.IsEnabled);   // the part is ticked, but no folder would change
                Assert.False(window.EffectiveChoices.ClaudeCodeEntries);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("ClaudeCodeWarning")).Visibility);
                Boxes(window).First().IsChecked = true;
                Assert.True(reset.IsEnabled);
            }
            finally { window.Close(); }
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_row_speaks_its_shown_name_with_its_kinds_as_help() => AppHost.Run(() =>
    {
        var (widget, _, _, _) = Make();
        try
        {
            var list = ResetFolderListViewModel.Read(widget);
            var window = new ResetWindow(null, list);
            OffScreen(window);
            window.Show();
            try
            {
                var boxes = Boxes(window).ToList();
                Assert.Equal(list.Rows.Count, boxes.Count);
                for (int i = 0; i < boxes.Count; i++)
                {
                    Assert.Equal(list.Rows[i].Shown, System.Windows.Automation.AutomationProperties.GetName(boxes[i]));
                    Assert.Equal(list.Rows[i].Kinds, System.Windows.Automation.AutomationProperties.GetHelpText(boxes[i]));
                }
                Assert.Equal(SettingsNames.ResetFoldersName,
                    System.Windows.Automation.AutomationProperties.GetName((FrameworkElement)window.FindName("FolderList")));
                Assert.DoesNotContain(NamesOf(window), Leaks);
            }
            finally { window.Close(); }
        }
        finally { widget.Dispose(); }
    });

    private static IEnumerable<CheckBox> Boxes(ResetWindow window)
    {
        window.UpdateLayout();
        var list = (ItemsControl)window.FindName("FolderList");
        for (int i = 0; i < list.Items.Count; i++)
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is DependencyObject c && Descendant<CheckBox>(c) is { } box)
                yield return box;
    }

    private static T? Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t) return t;
            if (Descendant<T>(child) is { } found) return found;
        }
        return null;
    }
}
```

`SettingsStore.SaveCcFoldersAdded(IEnumerable<string>)` writes `cc_folders_added` (`SettingsStore.cs:621`). The widget reads it fresh on every `LoadCcFoldersAdded()`, so saving after the widget is built is fine.

- [ ] **Step 2: Add the stubs and see the behavioural RED**

Create `ResetFolderListViewModel.cs` with the Step 3 code, except for three stubs:
- `ResetFolderRow.RefreshName()` does nothing.
- `LinesFor` returns `default`.
- `Read` builds the rows as in Step 3.

Then add the window members:
- the Step 3 XAML named elements, each `Visibility="Collapsed"`
- the `ResetWindow` constructor overload and `KeepFolders`
- `EffectiveChoices => Choices`
- `UpdateChoiceState` unchanged

Add `SettingsViewModel.ResetFolders`, but not the `ReloadNames` line.

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~ResetWindowFoldersTests > ../.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-5-red.log 2>&1`
Expected failures, all behavioural:
- `A_streamer_flip…`: "row after the flip: \"~\\.claude-qxkeep\"" (the slice 7 Critical's shape).
- `Each_line_shows…`: "Assert.True() Failure" on `l.Kept`.
- `An_empty_list…`: the `Assert.Equal` on `LinesFor`.
- `Reset_is_off…`: "Assert.False() Failure" on `reset.IsEnabled`.

The other three pass, because the rows and their bindings are already in place.

- [ ] **Step 3: Implement**

Create `windows-dotnet/src/Sanduhr.App/ViewModels/ResetFolderListViewModel.cs`:

```csharp
using System.ComponentModel;
using System.IO;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One Claude Code folder in Reset's list (Settings v2, slice 8; R12). The choice keys on <see cref="Path"/>, the real folder;
/// <see cref="Shown"/> is what the row draws, its codename while streamer mode hides it (decision 14).</summary>
internal sealed class ResetFolderRow : INotifyPropertyChanged
{
    private bool _undo = true;
    private string _shown;

    public ResetFolderRow(ResetFolder folder, bool discovered)
    {
        Path = folder.Path;
        Discovered = discovered;
        HasMeters = folder.Kinds.Contains(IntegrationKind.Statusline) || folder.Kinds.Contains(IntegrationKind.Meters);
        Kinds = SettingsNames.ResetFolderKinds(folder.Kinds);
        _shown = ShownFor(Path);
    }

    /// <summary>The real folder: what Reset keeps or undoes.</summary>
    public string Path { get; }
    /// <summary>Found by Settings, not added by hand (an added folder leaves the Claude Code page with the settings).</summary>
    public bool Discovered { get; }
    /// <summary>It holds a statusline or the meters mod, so it shows usage.</summary>
    public bool HasMeters { get; }
    public string Kinds { get; }
    public string Shown => _shown;

    /// <summary>Ticked: Reset takes Sanduhr's entries out of this folder.</summary>
    public bool Undo
    {
        get => _undo;
        set
        {
            if (_undo == value) return;
            _undo = value;
            Raise(nameof(Undo));
            UndoChanged?.Invoke();
        }
    }

    internal event Action? UndoChanged;

    /// <summary>Streamer mode moved: the row draws its folder again (ReloadNames, decision 13).</summary>
    internal void RefreshName()
    {
        _shown = ShownFor(Path);
        Raise(nameof(Shown));
    }

    private static string ShownFor(string path) =>
        StreamerMode.ShowFolder(path, ClaudeCodeProfile.Dir);   // streamer: masked - CP 7: ShowFolder masks on this line

    public override string ToString() => Shown;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Which of the dialog's lines show (decision 6): each one only in the state it is true for.</summary>
internal readonly record struct ResetFolderLines(bool NoFolders, bool Unreadable, bool Kept, bool KeptSignIn, bool KeptSettings, bool KeptAdded);

/// <summary>
/// Reset's Claude Code folders while the dialog is open (Settings v2, slice 8; R12): every folder that holds Sanduhr's entries, ticked.
/// Lives as long as one dialog; Settings holds it meanwhile so ReloadNames renames its rows.
/// </summary>
internal sealed class ResetFolderListViewModel
{
    private ResetFolderListViewModel(IReadOnlyList<ResetFolderRow> rows, bool unreadable)
    {
        Rows = rows;
        ReceiptsUnreadable = unreadable;
        foreach (var row in rows)
            row.UndoChanged += () => Changed?.Invoke();
    }

    public IReadOnlyList<ResetFolderRow> Rows { get; }

    /// <summary>installs.json, or a copy set aside beside it, can't be read in full: Reset keeps every copy (decision 4).</summary>
    public bool ReceiptsUnreadable { get; }

    /// <summary>The real folders the person unticked.</summary>
    public IReadOnlyList<string> Kept => [.. Rows.Where(r => !r.Undo).Select(r => r.Path)];

    /// <summary>The Claude Code part would change something: a ticked row, or no rows at all (Sanduhr's leftover copies still go).</summary>
    public bool UndoesAny => Rows.Count == 0 || Rows.Any(r => r.Undo);

    /// <summary>A row was ticked or unticked.</summary>
    public event Action? Changed;

    public void RefreshNames()
    {
        foreach (var row in Rows)
            row.RefreshName();
    }

    public ResetFolderLines LinesFor(bool part, bool accounts, bool settings)
    {
        var kept = part ? Rows.Where(r => !r.Undo).ToList() : [.. Rows];
        bool any = kept.Count > 0, meters = kept.Any(r => r.HasMeters);
        return new ResetFolderLines(
            NoFolders: Rows.Count == 0 && !ReceiptsUnreadable,
            Unreadable: ReceiptsUnreadable,
            Kept: any,
            KeptSignIn: meters && accounts,
            KeptSettings: meters && settings,
            KeptAdded: settings && kept.Any(r => !r.Discovered));
    }

    /// <summary>A list from rows already made (tests).</summary>
    internal static ResetFolderListViewModel For(IReadOnlyList<ResetFolderRow> rows, bool unreadable) => new(rows, unreadable);

    /// <summary>The folders as Reset finds them now: the receipts, plus Sanduhr's MCP entry in each discovered folder (decision 1). A read
    /// that throws reads as unreadable, so Reset keeps everything rather than guess.</summary>
    public static ResetFolderListViewModel Read(WidgetViewModel widget)
    {
        try
        {
            var profile = ClaudeCodeProfile.Dir;   // streamer: key - the profile Reset's folder list is built under, never drawn
            var known = ClaudeCodeFolders.Build(profile, ClaudeCodeProfile.ConfigDir, widget.LoadCcFoldersAdded());   // streamer: key - folder list for Reset's rows; each row draws through ShowFolder
            var ledger = new ReceiptStore(Path.Combine(widget.SanduhrAppDataDir, ResetPlan.IntegrationsName, ResetPlan.ReceiptsFileName)).Load();
            var mcp = new McpIntegrationInstaller(profile, widget.SanduhrAppDataDir, externalDir: widget.SanduhrExternalDir);
            var folders = ResetPlan.FoldersWithEntries(ledger.Records, known, k => k.Discovered && mcp.IsRegistered(k.Name));
            var rows = folders.Select(f => new ResetFolderRow(f, known.Any(k => k.Discovered && ClaudeCodeFolders.Same(k.Path, f.Path)))).ToList();   // streamer: key - the real folder each row keys on; the row draws it through ShowFolder
            // Unreadable: a record that didn't decode, or a damaged copy set aside beside the file (ResetPlan.ReceiptsUnreadable's rule,
            // read from the widget's folder so tests stay in their scratch tree).
            string dir = Path.Combine(widget.SanduhrAppDataDir, ResetPlan.IntegrationsName);
            bool unreadable = ledger.Skipped > 0
                || (Directory.Exists(dir) && Directory.EnumerateFiles(dir, ResetPlan.ReceiptsFileName + "*" + ReceiptLog.AsideSuffix).Any());
            return new ResetFolderListViewModel(rows, unreadable);
        }
        catch (Exception)
        {
            return new ResetFolderListViewModel([], true);
        }
    }
}
```

`Read` checks the widget's own folder rather than calling `ResetPlan.ReceiptsUnreadable(new Paths())`, because `new Paths()` is the real AppData and no test may read it. `ReceiptLog.AsideSuffix` is `public const string AsideSuffix = ".unreadable"` (`ReceiptLog.cs:18`).

In `windows-dotnet/src/Sanduhr.App/Views/ResetWindow.xaml`, add the core namespace: replace

```xml
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Reset Sanduhr"
```

with

```xml
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:core="clr-namespace:Sanduhr.Core;assembly=Sanduhr.Core"
        Title="Reset Sanduhr"
```

Then replace

```xml
                <TextBlock x:Name="ClaudeCodeWarning" Margin="24,0,0,10" TextWrapping="Wrap" FontSize="11"
                           Text="An entry you leave unchecked can't be removed from Sanduhr later."
                           AutomationProperties.Name="Warning about leaving the Claude Code entries unchecked"
                           Foreground="{DynamicResource Sanduhr.Brush.PaceMarker}" />
```

with

```xml
                <!-- Settings v2, slice 8 (R12): a checkbox per folder that holds Sanduhr's entries, then the lines that are true for
                     what is ticked (decision 6). Rows show folders as every page does; the choice keys on the real folder. -->
                <ItemsControl x:Name="FolderList" Margin="24,2,0,4" Focusable="False" Visibility="Collapsed"
                              AutomationProperties.Name="{x:Static core:SettingsNames.ResetFoldersName}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Margin="0,0,0,6">
                                <CheckBox Style="{StaticResource ThemedCheckBox}" Content="{Binding Shown}"
                                          IsChecked="{Binding Undo, Mode=TwoWay}"
                                          AutomationProperties.Name="{Binding Shown}"
                                          AutomationProperties.HelpText="{Binding Kinds}" />
                                <TextBlock Text="{Binding Kinds}" Margin="24,1,0,0" FontSize="11" TextWrapping="Wrap"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <TextBlock x:Name="NoFoldersText" Margin="24,0,0,10" TextWrapping="Wrap" FontSize="11" Visibility="Collapsed"
                           Text="{x:Static core:SettingsNames.ResetNoFolders}"
                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                <TextBlock x:Name="UnreadableText" Margin="24,0,0,10" TextWrapping="Wrap" FontSize="11" Visibility="Collapsed"
                           Text="{x:Static core:SettingsNames.ResetReceiptsUnreadable}"
                           Foreground="{DynamicResource Sanduhr.Brush.PaceMarker}" />
                <TextBlock x:Name="ClaudeCodeWarning" Margin="24,0,0,4" TextWrapping="Wrap" FontSize="11" Visibility="Collapsed"
                           Text="{x:Static core:SettingsNames.ResetFoldersKept}"
                           Foreground="{DynamicResource Sanduhr.Brush.PaceMarker}" />
                <TextBlock x:Name="KeptSignInText" Margin="24,0,0,4" TextWrapping="Wrap" FontSize="11" Visibility="Collapsed"
                           Text="{x:Static core:SettingsNames.ResetKeptSignIn}"
                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                <TextBlock x:Name="KeptSettingsText" Margin="24,0,0,4" TextWrapping="Wrap" FontSize="11" Visibility="Collapsed"
                           Text="{x:Static core:SettingsNames.ResetKeptSettings}"
                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                <TextBlock x:Name="KeptAddedText" Margin="24,0,0,10" TextWrapping="Wrap" FontSize="11" Visibility="Collapsed"
                           Text="{x:Static core:SettingsNames.ResetKeptAdded}"
                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
```

The `ClaudeCodeBox` CheckBox above it keeps its `Margin="0,0,0,4"`.

In `ResetWindow.xaml.cs`:

1. Add `using Sanduhr.App.ViewModels;`.
2. Replace the constructor and `ShowFor`:

```csharp
    private readonly ResetFolderListViewModel? _folders;

    public ResetWindow(ResetService? service = null, ResetFolderListViewModel? folders = null)
    {
        InitializeComponent();
        _service = service;
        _folders = folders;
        FolderList.ItemsSource = folders?.Rows;
        if (folders is not null)
            folders.Changed += UpdateChoiceState;   // the list lives as long as this window; detached on Closed
        _ready = true;
        UpdateChoiceState();
        Closing += (_, e) =>
        {
            if (_running) e.Cancel = true;   // never close on a reset in flight
        };
        Closed += (_, _) =>
        {
            if (_folders is not null)
                _folders.Changed -= UpdateChoiceState;
            if (_finished) SendQuit();
        };
    }

    /// <summary>Opens the dialog over <paramref name="owner"/> (or the screen). Returns when it closes.</summary>
    public static void ShowFor(Window? owner, ResetService service, ResetFolderListViewModel folders, Action quit)
    {
        var dlg = new ResetWindow(service, folders) { QuitRequested = quit };
        if (owner is not null && owner.IsLoaded)
            dlg.Owner = owner;
        else
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
    }
```

3. After the `Choices` property, add:

```csharp
    /// <summary>The parts as the service runs them: the Claude Code part only while it would change something (a ticked folder, or
    /// no list at all, when Sanduhr's leftover copies still go).</summary>
    internal ResetChoices EffectiveChoices => Choices with
    {
        ClaudeCodeEntries = ClaudeCodeBox.IsChecked == true && (_folders?.UndoesAny ?? true),
    };

    /// <summary>The folders left as they are: the real paths of unticked rows, never what a row shows (decision 14).</summary>
    internal IReadOnlyList<string> KeepFolders => ClaudeCodeBox.IsChecked == true ? _folders?.Kept ?? [] : [];
```

4. Replace `UpdateChoiceState`:

```csharp
    private void UpdateChoiceState()
    {
        bool part = ClaudeCodeBox.IsChecked == true;
        var lines = _folders?.LinesFor(part, AccountsBox.IsChecked == true, SettingsBox.IsChecked == true) ?? default;
        FolderList.IsEnabled = part;   // owner taste 7: dim, keep the ticks
        FolderList.Visibility = Vis(_folders is { Rows.Count: > 0 });
        NoFoldersText.Visibility = Vis(lines.NoFolders);
        UnreadableText.Visibility = Vis(lines.Unreadable);
        ClaudeCodeWarning.Visibility = Vis(lines.Kept);
        KeptSignInText.Visibility = Vis(lines.KeptSignIn);
        KeptSettingsText.Visibility = Vis(lines.KeptSettings);
        KeptAddedText.Visibility = Vis(lines.KeptAdded);
        ResetButton.IsEnabled = ResetPlan.Steps(EffectiveChoices).Count > 0;
    }

    private static Visibility Vis(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
```

5. In `Reset_Click`, replace `var choices = Choices;` with `var choices = EffectiveChoices;` and `var keep = KeepFolders;`, and pass `keep` in place of `[]` to `_service.RunAsync`.

In `SettingsViewModel.cs`, before `public void ReloadNames()`'s summary, add:

```csharp
    /// <summary>Reset's folder list while the dialog is open (slice 8, decision 13): ReloadNames renames its rows with every page's names.
    /// App.ShowReset sets it and clears it when the dialog closes.</summary>
    internal ResetFolderListViewModel? ResetFolders { get; set; }

```

In `ReloadNames`, replace

```csharp
        WeatherPage.RefreshNames();
        OnPropertyChanged(nameof(SettingsFolderShown));
```

with

```csharp
        WeatherPage.RefreshNames();
        ResetFolders?.RefreshNames();   // slice 8: Reset's rows, while the dialog is open
        OnPropertyChanged(nameof(SettingsFolderShown));
```

In `App.xaml.cs` `ShowReset`, replace

```csharp
        ResetWindow.ShowFor(_settingsWindow, service, QuitApp);
```

with

```csharp
        var folders = ResetFolderListViewModel.Read(vm);
        var svm = _settingsWindow?.ViewModel;
        if (svm is not null) svm.ResetFolders = folders;   // a streamer flip renames the rows (decision 13)
        try { ResetWindow.ShowFor(_settingsWindow, service, folders, QuitApp); }
        finally { if (svm is not null) svm.ResetFolders = null; }
```

In `StreamerReachTests.Known`, after the `("Sanduhr.App/ViewModels/PublishViewModel.cs", "masked", 4),` row, add:

```csharp
        ("Sanduhr.App/ViewModels/ResetFolderListViewModel.cs", "key", 3),      // slice 8: Reset's rows key on real folders
        ("Sanduhr.App/ViewModels/ResetFolderListViewModel.cs", "masked", 1),   // slice 8: and draw them through ShowFolder (carry)
```

The three `key` lines are the profile, the folder list and the rows' `f.Path` line. The `masked` line is `ShownFor`. Keep the rows in the list's alphabetical order by file.

- [ ] **Step 4: Run the touched classes, then both suites once**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~ResetWindowFoldersTests|FullyQualifiedName~AppWindowNamesTests|FullyQualifiedName~StreamerCollectionScanTests" > ../.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-5-green.log 2>&1`
Expected: PASS. `AppWindowNamesTests` still builds `new ResetWindow()` with no list: every line stays collapsed, and nothing leaks.

Run the Core suite and the App suite once each, logging to `task-5-core.log` and `task-5-app.log`.
Expected: PASS. `StreamerReachTests` shows the two new rows exactly. `Every_masked_site_calls_a_mask` (the M4-tightened check) accepts `ShownFor`'s line because it calls `ShowFolder(` on the line.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/ResetFolderListViewModel.cs windows-dotnet/src/Sanduhr.App/Views/ResetWindow.xaml windows-dotnet/src/Sanduhr.App/Views/ResetWindow.xaml.cs windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.App/App.xaml.cs windows-dotnet/tests/Sanduhr.App.Tests/ResetWindowFoldersTests.cs windows-dotnet/tests/Sanduhr.Tests/StreamerReachTests.cs
git commit -m "feat(settings-v2): Reset lists each Claude Code folder with its own checkbox, codenames on stream, real folders underneath

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: About says what Reset removes, and PRIVACY.md says what an unticked folder keeps

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (About's reset row, :2897-2901)
- Modify: `docs/PRIVACY.md` ("How you remove your data", Clear local storage)
- Test: modify `windows-dotnet/tests/Sanduhr.Tests/SettingsSlice8NamesTests.cs` (one fact); create the App test inside `windows-dotnet/tests/Sanduhr.App.Tests/ResetWindowFoldersTests.cs` (one fact)

**Interfaces:**
- Consumes: `SettingsNames.ResetSanduhrCaption` (Task 1).
- Produces: `TextBlock x:Name="ResetCaption"` in About's `reset` section; one PRIVACY sentence, pinned.

- [ ] **Step 1: Write the failing tests**

Append to `SettingsSlice8NamesTests`:

```csharp

    /// <summary>docs/PRIVACY.md says what an unticked folder keeps (decision 16), in the dialog's words.</summary>
    [Fact]
    public void PRIVACY_says_what_an_unticked_folder_keeps()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "PRIVACY.md")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var privacy = File.ReadAllText(Path.Combine(dir!.FullName, "docs", "PRIVACY.md"));
        Assert.Contains("Its first part lists each Claude Code folder that holds Sanduhr's entries, each with its own checkbox. "
            + "A folder you untick keeps its entries exactly as they are, and Sanduhr keeps the files they run and its record of them, "
            + "so they keep working and Settings, Claude Code can remove them later.", privacy);
    }
```

Append to `ResetWindowFoldersTests`:

```csharp
    [Fact]
    public void About_says_what_Reset_removes_under_the_button() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        SettingsWindow? window = null;
        try
        {
            var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
                checkForUpdatesNow: () => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate)), isStoreBuild: false);
            window = new SettingsWindow(svm);
            var guard = NoActivate.Show(window);
            window.SelectPage(SettingsPage.About, SettingsIndex.Reset);
            window.UpdateLayout();
            guard.Check();
            var caption = (TextBlock)window.FindName("ResetCaption");
            Assert.Equal(SettingsNames.ResetSanduhrCaption, caption.Text);
            Assert.Equal(Visibility.Visible, caption.Visibility);
            var button = (Button)window.FindName("ResetSanduhrButton");
            Assert.Equal(SettingsNames.ResetSanduhrCaption, System.Windows.Automation.AutomationProperties.GetHelpText(button));
        }
        finally
        {
            window?.Close();
            widget.Dispose();
        }
    });
```

- [ ] **Step 2: Add the stubs and see the behavioural RED**

Stubs: add `x:Name="ResetSanduhrButton"` to the About button, and an empty `<TextBlock x:Name="ResetCaption" />` under it. Both are part of Step 3's block.

Run both classes, logging to `task-6-red.log`.
Expected:
- The PRIVACY fact fails on `Assert.Contains() Failure`.
- The About fact fails on `Assert.Equal() Failure` between the caption and `""`.

- [ ] **Step 3: Implement**

In `SettingsWindow.xaml`, replace

```xml
                                <StackPanel views:SettingsAnchor.Id="reset" Margin="0,12,0,0" HorizontalAlignment="Left">
                                    <Button Style="{StaticResource FlatButton}" Padding="10,4" Content="{Binding ResetSanduhrTitle}"
                                            AutomationProperties.Name="{x:Static core:SettingsNames.ResetSanduhr}" AutomationProperties.HelpText="Removes what Sanduhr keeps on this PC. You choose which parts."
                                            Command="{Binding ResetSanduhrCommand}" />
                                </StackPanel>
```

with

```xml
                                <StackPanel views:SettingsAnchor.Id="reset" Margin="0,12,0,0" HorizontalAlignment="Left">
                                    <Button x:Name="ResetSanduhrButton" Style="{StaticResource FlatButton}" Padding="10,4" Content="{Binding ResetSanduhrTitle}"
                                            HorizontalAlignment="Left"
                                            AutomationProperties.Name="{x:Static core:SettingsNames.ResetSanduhr}"
                                            AutomationProperties.HelpText="{x:Static core:SettingsNames.ResetSanduhrCaption}"
                                            Command="{Binding ResetSanduhrCommand}" />
                                    <!-- Settings v2, slice 8 (spec, Reset): what it removes, so nobody has to open the dialog to learn it. -->
                                    <TextBlock x:Name="ResetCaption" Text="{x:Static core:SettingsNames.ResetSanduhrCaption}"
                                               FontSize="11" TextWrapping="Wrap" MaxWidth="460" Margin="0,6,0,0"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                                </StackPanel>
```

In `docs/PRIVACY.md`, append two sentences to the end of the bullet that starts `- **Clear local storage:** run Settings ▸ About ▸ Reset Sanduhr… first`, after its closing `%LOCALAPPDATA%\Sanduhr\`.`. The bullet becomes:

```
- **Clear local storage:** run Settings ▸ About ▸ Reset Sanduhr… first (it also takes out Sanduhr's Claude Code entries and its files in `%USERPROFILE%\.sanduhr`), then delete `%APPDATA%\Sanduhr\` and `%LOCALAPPDATA%\Sanduhr\`. Its first part lists each Claude Code folder that holds Sanduhr's entries, each with its own checkbox. A folder you untick keeps its entries exactly as they are, and Sanduhr keeps the files they run and its record of them, so they keep working and Settings, Claude Code can remove them later.
```

Use the Edit tool on that exact bullet. Read it first: the `▸` characters are UTF-8, and the file is CRLF in the working tree.

- [ ] **Step 4: Run, then both suites once**

Run both classes (log `task-6-green.log`). Then run the Core and App suites once (`task-6-core.log`, `task-6-app.log`).
Expected: PASS. `SettingsSourceScanTests` passes. The new TextBlock's text comes from `SettingsNames`, and its spoken name is its text.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml docs/PRIVACY.md windows-dotnet/tests/Sanduhr.Tests/SettingsSlice8NamesTests.cs windows-dotnet/tests/Sanduhr.App.Tests/ResetWindowFoldersTests.cs
git commit -m "feat(settings-v2): About says what Reset removes, and PRIVACY says what an unticked folder keeps

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: `ThemeContrastTests`: TextDim and TextMuted read on the page and sidebar in all six themes

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/ThemeContrast.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/ThemeModel.cs` (twelve values in `ThemeCatalog.BuiltIns`)
- Modify: `windows-dotnet/src/Sanduhr.App/Theming/ThemePalette.cs` (`ChromeAlpha`)
- Modify: `docs/themes/template.json` (two values)
- Test: create `windows-dotnet/tests/Sanduhr.Tests/ThemeContrastTests.cs` and `windows-dotnet/tests/Sanduhr.App.Tests/SettingsContrastTests.cs`

**Interfaces:**
- Consumes: `ThemeLint.TryParseHex`, `Luminance`, `Contrast`, `Composite` (linked file, called only), `ThemeCatalog`.
- Produces:
  - `public sealed record ContrastSurface(string Name, (double R, double G, double B) Color)`
  - `public static class ThemeContrast`:
    - constants `TextMin` (4.5), `DimFloor` (5.0), `NonTextMin` (3.0), `ChromeAlphaOverMica` (0.92), `PrimaryButtonText` (`"#0d0d0d"`), and `Page`, `Well`, `Sidebar`
    - `ChromeAlpha(ThemeDefinition)`, `SettingsSurfaces(ThemeDefinition)`, `Ratio(string, (double,double,double))`, `Worst(string, ThemeDefinition)`, `SelectedLabel(ThemeDefinition)`, `Hex((double,double,double))`

`SelectedLabel` is used in Task 8. It is written here so the surfaces and the rule live in one file.

- [ ] **Step 1: Write the measuring helper (pure; no behaviour changes)**

Create `windows-dotnet/src/Sanduhr.Core/ThemeContrast.cs`:

```csharp
namespace Sanduhr.Core;

/// <summary>One surface Settings draws text on, as ThemePalette paints it.</summary>
public sealed record ContrastSurface(string Name, (double R, double G, double B) Color);

/// <summary>
/// Settings v2, slice 8 (WF37; spec, Keyboard and screen reader, Contrast): the surfaces Settings draws text on, computed from a theme's
/// tokens the way ThemePalette paints them, and the ratios the built-in themes are held to. The window is opaque, so Mica never shows
/// under it: a page is a Glass card (solid), lists and fields sit on Bg wells, and the sidebar is TitleBg at the chrome alpha over Bg
/// (slice 8 decision 8). Pure (colors and arithmetic); not on the MCP link list. It calls ThemeLint's luminance, which is.
/// </summary>
public static class ThemeContrast
{
    /// <summary>WCAG AA for body text.</summary>
    public const double TextMin = 4.5;
    /// <summary>TextDim's floor, one step above TextMuted's <see cref="TextMin"/>: the ramp keeps two visible steps (owner taste 1).</summary>
    public const double DimFloor = 5.0;
    /// <summary>WCAG AA for a non-text indicator (the selected row's accent bar, the banner's border).</summary>
    public const double NonTextMin = 3.0;
    /// <summary>The chrome strips' glass alpha over Mica; a theme that opts out of Mica paints them solid.</summary>
    public const double ChromeAlphaOverMica = 0.92;
    /// <summary>The primary button's text in ThemedDialog (ThemedDialog.xaml), drawn on the theme's accent.</summary>
    public const string PrimaryButtonText = "#0d0d0d";

    public const string Page = "page";
    public const string Well = "well";
    public const string Sidebar = "sidebar";

    /// <summary>The chrome strips' alpha (TitleBg, GlassChrome, FooterBg). ThemePalette reads it here.</summary>
    public static double ChromeAlpha(ThemeDefinition def) => def.OptsOutOfMica ? 1.0 : ChromeAlphaOverMica;

    /// <summary>Page (Glass), well (Bg) and sidebar (TitleBg at <see cref="ChromeAlpha"/> over Bg).</summary>
    public static IReadOnlyList<ContrastSurface> SettingsSurfaces(ThemeDefinition def)
    {
        var bg = Rgb(def.Bg);
        return
        [
            new(Page, Rgb(def.Glass)),
            new(Well, bg),
            new(Sidebar, ThemeLint.Composite(Rgb(def.TitleBg), ChromeAlpha(def), bg)),
        ];
    }

    /// <summary>The WCAG ratio of <paramref name="hex"/> on <paramref name="on"/>, 1 to 21.</summary>
    public static double Ratio(string hex, (double R, double G, double B) on) =>
        ThemeLint.Contrast(ThemeLint.Luminance(Rgb(hex)), ThemeLint.Luminance(on));

    /// <summary>The lowest ratio of <paramref name="hex"/> on any of <see cref="SettingsSurfaces"/>.</summary>
    public static double Worst(string hex, ThemeDefinition def) => SettingsSurfaces(def).Min(s => Ratio(hex, s.Color));

    /// <summary>The selected sidebar row's label (spec: Obsidian's selected label uses Text with an accent bar): Accent where it reads
    /// <see cref="TextMin"/> on the selected row, which is Glass, else Text. Computed, so any theme gets it (slice 8 decision 11).</summary>
    public static string SelectedLabel(ThemeDefinition def) =>
        Ratio(def.Accent, Rgb(def.Glass)) >= TextMin ? def.Accent : def.Text;

    /// <summary>A surface as <c>#rrggbb</c>, for messages and the harness.</summary>
    public static string Hex((double R, double G, double B) c) => $"#{Byte(c.R):x2}{Byte(c.G):x2}{Byte(c.B):x2}";

    private static int Byte(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);

    private static (double R, double G, double B) Rgb(string hex) =>
        ThemeLint.TryParseHex(hex, out var rgb) ? rgb : throw new ArgumentException("not a #rrggbb color", nameof(hex));
}
```

In `windows-dotnet/src/Sanduhr.App/Theming/ThemePalette.cs`, replace

```csharp
    public double ChromeAlpha => Definition.OptsOutOfMica ? 1.0 : 0.92;
```

with

```csharp
    public double ChromeAlpha => ThemeContrast.ChromeAlpha(Definition);
```

- [ ] **Step 2: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/ThemeContrastTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Settings v2, slice 8 (WF37; spec, Keyboard and screen reader, Contrast; Done line): TextDim and TextMuted read at 4.5:1 or better on
/// the Settings page and sidebar in all six built-in themes, computed from the theme tokens. TextDim's floor is a step higher so the
/// ramp keeps its steps (owner taste 1). Every built-in theme is dark: none has a light variant to measure (decision 9).
/// </summary>
public class ThemeContrastTests
{
    public static TheoryData<string> BuiltIns()
    {
        var data = new TheoryData<string>();
        foreach (var key in ThemeCatalog.BuiltInOrder) data.Add(key);
        return data;
    }

    [Fact]
    public void Six_built_in_themes_are_measured() => Assert.Equal(6, ThemeCatalog.BuiltInOrder.Count);

    [Fact]
    public void The_measure_is_WCAGs_and_the_surfaces_are_what_Settings_paints()
    {
        Assert.Equal(21.0, ThemeContrast.Ratio("#ffffff", (0, 0, 0)), 2);
        Assert.Equal(1.0, ThemeContrast.Ratio("#777777", (0x77 / 255.0, 0x77 / 255.0, 0x77 / 255.0)), 6);
        var obsidian = ThemeContrast.SettingsSurfaces(ThemeCatalog.BuiltIns["obsidian"]);
        Assert.Equal(new[] { ThemeContrast.Page, ThemeContrast.Well, ThemeContrast.Sidebar }, obsidian.Select(s => s.Name));
        Assert.Equal("#1c1c1c", ThemeContrast.Hex(obsidian[0].Color));   // Glass, solid
        Assert.Equal("#0d0d0d", ThemeContrast.Hex(obsidian[1].Color));   // Bg
        Assert.Equal("#151515", ThemeContrast.Hex(obsidian[2].Color));   // #161616 at 0.92 over #0d0d0d
        var matrix = ThemeCatalog.BuiltIns["matrix"];   // opts out of Mica: its sidebar is TitleBg itself
        Assert.Equal(matrix.TitleBg, ThemeContrast.Hex(ThemeContrast.SettingsSurfaces(matrix)[2].Color));
    }

    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void TextDim_and_TextMuted_read_on_the_page_the_wells_and_the_sidebar(string key)
    {
        var def = ThemeCatalog.BuiltIns[key];
        var bad = new List<string>();
        foreach (var (token, hex, min) in new[] { ("TextDim", def.TextDim, ThemeContrast.DimFloor), ("TextMuted", def.TextMuted, ThemeContrast.TextMin) })
            foreach (var s in ThemeContrast.SettingsSurfaces(def))
            {
                double r = ThemeContrast.Ratio(hex, s.Color);
                if (r < min) bad.Add($"{key} {token} {hex} reads {r:0.00}:1 on the {s.Name} {ThemeContrast.Hex(s.Color)} (needs {min:0.0}:1)");
            }
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
    }

    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void Text_and_TextSecondary_already_read_and_the_ramp_still_steps_down(string key)
    {
        var def = ThemeCatalog.BuiltIns[key];
        Assert.True(ThemeContrast.Worst(def.Text, def) >= ThemeContrast.TextMin, $"{key} Text");
        Assert.True(ThemeContrast.Worst(def.TextSecondary, def) >= ThemeContrast.TextMin, $"{key} TextSecondary");
        double L(string hex) { ThemeLint.TryParseHex(hex, out var c); return ThemeLint.Luminance(c); }
        Assert.True(L(def.Text) > L(def.TextSecondary) && L(def.TextSecondary) > L(def.TextDim) && L(def.TextDim) > L(def.TextMuted),
            $"{key}'s ramp no longer steps down: {def.Text} {def.TextSecondary} {def.TextDim} {def.TextMuted}");
    }

    /// <summary>The twelve values, pinned: a later change to a built-in's dim text is a reviewed change (the owner's before/after table).</summary>
    [Fact]
    public void The_twelve_values_are_the_plans()
    {
        var expected = new Dictionary<string, (string Dim, string Muted)>
        {
            ["obsidian"] = ("#8b8b8b", "#848484"),
            ["aurora"] = ("#808ea3", "#7487a1"),
            ["ember"] = ("#a18277", "#a47767"),
            ["mint"] = ("#5fa17e", "#4a9b70"),
            ["matrix"] = ("#009933", "#009030"),
            ["blueprint"] = ("#9194b4", "#878bb2"),
        };
        foreach (var (key, (dim, muted)) in expected)
        {
            Assert.Equal(dim, ThemeCatalog.BuiltIns[key].TextDim);
            Assert.Equal(muted, ThemeCatalog.BuiltIns[key].TextMuted);
        }
    }

    /// <summary>Carried from slice 2 (T12, T20): the Quit question's primary button draws #0d0d0d on the accent; it reads in every built-in.
    /// The banner's 1 px accent border on Bg (slice 7 carry) is a non-text indicator.</summary>
    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void The_dialogs_primary_text_and_the_banners_border_read_on_every_accent(string key)
    {
        var def = ThemeCatalog.BuiltIns[key];
        ThemeLint.TryParseHex(def.Accent, out var accent);
        ThemeLint.TryParseHex(def.Bg, out var bg);
        Assert.True(ThemeContrast.Ratio(ThemeContrast.PrimaryButtonText, accent) >= ThemeContrast.TextMin, $"{key}: dialog primary text");
        Assert.True(ThemeContrast.Ratio(def.Accent, bg) >= ThemeContrast.NonTextMin, $"{key}: banner border");
    }
}
```

Create `windows-dotnet/tests/Sanduhr.App.Tests/SettingsContrastTests.cs`:

```csharp
using System.IO;
using System.Windows;
using System.Windows.Media;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 8 (decision 8): ThemeContrast's surfaces are the ones Settings paints. ThemePalette's brushes, composited the way
/// the window draws them, equal ThemeContrast.SettingsSurfaces within one 8-bit step, and the XAML still draws pages, wells and the
/// sidebar on those brushes over an opaque window.
/// </summary>
public sealed class SettingsContrastTests
{
    private static (double R, double G, double B) Over(Color top, Color under)
    {
        double a = top.A / 255.0;
        return ((top.R * a + under.R * (1 - a)) / 255, (top.G * a + under.G * (1 - a)) / 255, (top.B * a + under.B * (1 - a)) / 255);
    }

    private static (double R, double G, double B) Of(Color c) => (c.R / 255.0, c.G / 255.0, c.B / 255.0);

    [Fact]
    public void The_surfaces_ThemeContrast_measures_are_the_brushes_ThemePalette_sets() => Sta.Run(() =>
    {
        foreach (var key in ThemeCatalog.BuiltInOrder)
        {
            var res = new ResourceDictionary();
            new ThemePalette(key, ThemeCatalog.BuiltIns[key]).Apply(res);
            Color bg = ((SolidColorBrush)res["Sanduhr.Brush.Bg"]).Color;
            Color glass = ((SolidColorBrush)res["Sanduhr.Brush.Glass"]).Color;
            Color title = ((SolidColorBrush)res["Sanduhr.Brush.TitleBg"]).Color;
            Assert.Equal(255, glass.A);   // the page card is solid
            var painted = new[] { Of(glass), Of(bg), Over(title, bg) };
            var measured = ThemeContrast.SettingsSurfaces(ThemeCatalog.BuiltIns[key]);
            for (int i = 0; i < 3; i++)
            {
                Assert.True(Math.Abs(painted[i].R - measured[i].Color.R) <= 1 / 255.0
                    && Math.Abs(painted[i].G - measured[i].Color.G) <= 1 / 255.0
                    && Math.Abs(painted[i].B - measured[i].Color.B) <= 1 / 255.0,
                    $"{key} {measured[i].Name}: painted {ThemeContrast.Hex(painted[i])}, measured {ThemeContrast.Hex(measured[i].Color)}");
            }
        }
    });

    [Fact]
    public void Settings_draws_pages_on_Glass_and_the_sidebar_on_TitleBg_over_an_opaque_Bg_window()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Views", "SettingsWindow.xaml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Sanduhr.App", "Views", "SettingsWindow.xaml"));
        Assert.Contains("AllowsTransparency=\"False\"", xaml);
        Assert.Contains("<Border Grid.Column=\"0\" Background=\"{DynamicResource Sanduhr.Brush.TitleBg}\"", xaml);
        Assert.Contains("<Border x:Name=\"RootBorder\"", xaml);
        Assert.Contains("<Border x:Name=\"ThemesRoot\" Background=\"{DynamicResource Sanduhr.Brush.Glass}\"", xaml);
    }
}
```

`Sta.Run(Action)` is the STA helper in `tests/Sanduhr.App.Tests/Sta.cs`. The class touches no process-wide static, so it carries no collection.

- [ ] **Step 3: Run them and see the behavioural RED**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~ThemeContrastTests > ../.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-7-red.log 2>&1`
Expected:
- `TextDim_and_TextMuted_read…` fails for all six themes, with messages such as "obsidian TextDim #777777 reads 3.81:1 on the page #1c1c1c (needs 5.0:1)" and "blueprint TextMuted #484b70 reads 1.79:1 on the page #2a2438 (needs 4.5:1)".
- `The_twelve_values_are_the_plans` fails on Obsidian's `#777777`.
- Everything else passes: the measure, Text and TextSecondary, the ramp, the dialog text and the border.

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SettingsContrastTests > ../.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-7-red-app.log 2>&1`
Expected: PASS. It guards the surfaces, not the tokens.

- [ ] **Step 4: Change the twelve values**

In `windows-dotnet/src/Sanduhr.Core/ThemeModel.cs`, inside `ThemeCatalog.BuiltIns`, change exactly these lines. Each anchor is unique in the file, and the hex is the only change on each line:

| Theme | Old line | New line |
| --- | --- | --- |
| Obsidian | `                TextDim = "#777777",` | `                TextDim = "#8b8b8b",` |
| Obsidian | `                TextMuted = "#555555",` | `                TextMuted = "#848484",` |
| Aurora | `                TextDim = "#64748b",` | `                TextDim = "#808ea3",` |
| Aurora | `                TextMuted = "#475569",` | `                TextMuted = "#7487a1",` |
| Ember | `                TextDim = "#8b6b60",` | `                TextDim = "#a18277",` |
| Ember | `                TextMuted = "#6b4b40",` | `                TextMuted = "#a47767",` |
| Mint | `                TextDim = "#5a9a78",` | `                TextDim = "#5fa17e",` |
| Mint | `                TextMuted = "#3a7a58",` | `                TextMuted = "#4a9b70",` |
| Matrix | `                TextDim = "#00802b",` | `                TextDim = "#009933",` |
| Matrix | `                TextMuted = "#005a1e",` | `                TextMuted = "#009030",` |
| Blueprint | `                TextDim = "#6a6e99",` | `                TextDim = "#9194b4",` |
| Blueprint | `                TextMuted = "#484b70",` | `                TextMuted = "#878bb2",` |

Check uniqueness first: `git grep -c -F 'TextDim = "#777777"' -- windows-dotnet/src/Sanduhr.Core/ThemeModel.cs` prints 1, and the same holds for each of the others. This is the only edit to a linked file in the slice (D3): string literals only.

In `docs/themes/template.json` (owner taste 11), change `"text_dim": "#777777",` to `"text_dim": "#8b8b8b",` and `"text_muted": "#555555",` to `"text_muted": "#848484",`.

- [ ] **Step 5: Run the theme suites and the MCP suite**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~ThemeContrastTests|FullyQualifiedName~ThemeLintTests|FullyQualifiedName~ThemeModelTests|FullyQualifiedName~ThemeDraftTests|FullyQualifiedName~ThemeDeskLookTests|FullyQualifiedName~ThemeProposalsTests|FullyQualifiedName~PackValidator" > ../.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-7-green.log 2>&1`
Expected: PASS. `ThemeLintTests.Every_built_in_lints_with_no_findings` passes for all six: the ramp still steps down, and every hue difference stays under 30° (or the colours are below the saturation floor).

Run the MCP suite once: `dotnet test tests/Sanduhr.Mcp.Tests > ../.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-7-mcp.log 2>&1`
Expected: PASS. `TrustBoundaryTests` and `ToolLogicThemeTests` are untouched. Their sample JSON uses its own values, not the built-ins.

Run the Core and App suites once (`task-7-core.log`, `task-7-app.log`).
Expected: PASS. If an App test pins a built-in's old TextDim or TextMuted hex, it was pinning the token. Update it to read `ThemeCatalog.BuiltIns[key].TextDim`, and say so in the report.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/ThemeContrast.cs windows-dotnet/src/Sanduhr.Core/ThemeModel.cs windows-dotnet/src/Sanduhr.App/Theming/ThemePalette.cs docs/themes/template.json windows-dotnet/tests/Sanduhr.Tests/ThemeContrastTests.cs windows-dotnet/tests/Sanduhr.App.Tests/SettingsContrastTests.cs
git commit -m "feat(settings-v2): TextDim and TextMuted read on the Settings page and sidebar in all six built-in themes

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Obsidian's selected label: Text beside an accent bar, by a rule every theme follows

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Theming/ThemePalette.cs` (`Apply`)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (the sidebar TabItem style, :230-265)
- Test: modify `windows-dotnet/tests/Sanduhr.Tests/ThemeContrastTests.cs`; create the App facts in `windows-dotnet/tests/Sanduhr.App.Tests/SettingsContrastTests.cs` (with `[Collection("SettingsWindow")]` on a second class in that file)

**Interfaces:**
- Consumes: `ThemeContrast.SelectedLabel`, `ThemeContrast.NonTextMin` (Task 7).
- Produces: the resource `Sanduhr.Brush.SelectedLabel`, and a named `bar` Border in the sidebar TabItem template.

- [ ] **Step 1: Write the failing tests**

Append to `ThemeContrastTests`:

```csharp

    /// <summary>Spec: Obsidian's selected label uses Text with an accent bar. The rule is computed, so it holds for any theme (decision 11).</summary>
    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void The_selected_label_reads_and_its_bar_shows(string key)
    {
        var def = ThemeCatalog.BuiltIns[key];
        ThemeLint.TryParseHex(def.Glass, out var row);
        string label = ThemeContrast.SelectedLabel(def);
        Assert.Equal(key == "obsidian" ? def.Text : def.Accent, label);
        Assert.True(ThemeContrast.Ratio(label, row) >= ThemeContrast.TextMin, $"{key}: label {label}");
        Assert.True(ThemeContrast.Ratio(def.Accent, row) >= ThemeContrast.NonTextMin, $"{key}: the bar");
    }

    /// <summary>Review Focus 4: a custom theme whose accent can't carry text on its row gets Text too.</summary>
    [Fact]
    public void A_custom_theme_with_a_dark_accent_gets_Text()
    {
        var custom = ThemeCatalog.BuiltIns["aurora"] with { Accent = "#1e3a8a" };
        Assert.Equal(custom.Text, ThemeContrast.SelectedLabel(custom));
    }
```

Append to `SettingsContrastTests.cs` a second class:

```csharp

/// <summary>Settings v2, slice 8: the sidebar's selected row draws its label in Sanduhr.Brush.SelectedLabel beside an accent bar.</summary>
[Collection("SettingsWindow")]
public sealed class SidebarSelectedLabelTests : AppWindowFixture
{
    [Theory]
    [InlineData("obsidian")]
    [InlineData("aurora")]
    public void The_selected_row_draws_the_themes_selected_label_and_only_it_shows_the_bar(string key) => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        Sanduhr.App.Views.SettingsWindow? window = null;
        try
        {
            var svm = new Sanduhr.App.ViewModels.SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
                checkForUpdatesNow: () => Task.FromResult(new Sanduhr.App.Updates.UpdateProbeResult(Sanduhr.App.Updates.UpdateProbeStatus.UpToDate)),
                isStoreBuild: false);
            window = new Sanduhr.App.Views.SettingsWindow(svm);
            var def = ThemeCatalog.BuiltIns[key];
            new ThemePalette(key, def).Apply(window.Resources);
            var guard = NoActivate.Show(window);
            window.SelectPage(SettingsPage.Themes);
            window.UpdateLayout();
            guard.Check();
            var tabs = (System.Windows.Controls.TabControl)window.FindName("Tabs");
            var selected = (System.Windows.Controls.TabItem)tabs.SelectedItem;
            var expected = (Color)ColorConverter.ConvertFromString(ThemeContrast.SelectedLabel(def));
            Assert.Equal(expected, ((SolidColorBrush)selected.Foreground).Color);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)selected.Template.FindName("bar", selected)).Visibility);
            var other = tabs.Items.Cast<System.Windows.Controls.TabItem>().First(t => !ReferenceEquals(t, selected));
            other.ApplyTemplate();
            Assert.NotEqual(Visibility.Visible, ((FrameworkElement)other.Template.FindName("bar", other)).Visibility);
        }
        finally
        {
            window?.Close();
            widget.Dispose();
        }
    });
}
```

- [ ] **Step 2: See the behavioural RED**

Run both classes, logging to `task-8-red.log`.
Expected:
- Core: `The_selected_label_reads…` passes. The rule exists from Task 7, so this fact pins it. `A_custom_theme…` passes too.
- App: `obsidian` fails on "Assert.Equal() Failure: Expected #FFE8E4DC, Actual #FF6C63FF" (Accent today). `aurora` fails at `Template.FindName("bar", …)` returning `null`.

The App facts are the behavioural RED. The Core facts pin Task 7's rule against the spec's sentence.

- [ ] **Step 3: Implement**

In `ThemePalette.Apply`, after `SetBrush(res, "Sanduhr.Brush.Accent", Accent);`, add:

```csharp
        // The selected sidebar row's label (Settings v2, slice 8; WF37): Accent where it reads 4.5:1 on the row, else Text.
        SetBrush(res, "Sanduhr.Brush.SelectedLabel", Hex(ThemeContrast.SelectedLabel(Definition)));
```

`Hex(string)` is ThemePalette's existing hex-to-Color helper (`Bg = Hex(def.Bg)`).

In `SettingsWindow.xaml`, in the `<Style TargetType="TabItem">` template, replace

```xml
                            <Border x:Name="bd" Padding="10,7" Margin="0,1" CornerRadius="6"
                                    Background="Transparent" BorderBrush="Transparent" BorderThickness="1">
                                <ContentPresenter ContentSource="Header" />
                            </Border>
```

with

```xml
                            <Border x:Name="bd" Padding="10,7" Margin="0,1" CornerRadius="6"
                                    Background="Transparent" BorderBrush="Transparent" BorderThickness="1">
                                <Grid>
                                    <!-- Settings v2, slice 8 (WF37): the selected row's accent bar, 3 x 16 DIP, 2 DIP in from the row's edge. -->
                                    <Border x:Name="bar" Width="3" Height="16" CornerRadius="1.5" Margin="-8,0,0,0"
                                            HorizontalAlignment="Left" VerticalAlignment="Center" Visibility="Hidden"
                                            Background="{DynamicResource Sanduhr.Brush.Accent}" />
                                    <ContentPresenter ContentSource="Header" />
                                </Grid>
                            </Border>
```

Then replace the selected trigger

```xml
                            <Trigger Property="IsSelected" Value="True">
                                <Setter TargetName="bd" Property="Background" Value="{DynamicResource Sanduhr.Brush.Glass}" />
                                <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.Accent}" />
                                <Setter Property="FontWeight" Value="SemiBold" />
                            </Trigger>
```

with

```xml
                            <Trigger Property="IsSelected" Value="True">
                                <Setter TargetName="bd" Property="Background" Value="{DynamicResource Sanduhr.Brush.Glass}" />
                                <Setter TargetName="bar" Property="Visibility" Value="Visible" />
                                <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.SelectedLabel}" />
                                <Setter Property="FontWeight" Value="SemiBold" />
                            </Trigger>
```

The four-line block is unique: the search result style's selected trigger has no Foreground setter.

- [ ] **Step 4: Run, then the App suite once**

Run both classes (log `task-8-green.log`), then the App suite once (`task-8-app.log`).
Expected: PASS. `SettingsReachTests` (the Tab walk) passes, because the bar is a Border and takes no focus.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Theming/ThemePalette.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/tests/Sanduhr.Tests/ThemeContrastTests.cs windows-dotnet/tests/Sanduhr.App.Tests/SettingsContrastTests.cs
git commit -m "feat(settings-v2): the selected sidebar row reads in Text where the accent can't carry text, beside an accent bar

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Themes: uncapped lists on one scrolling page (WF49)

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (the Themes TabItem, about :2085-2364)
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/ThemesListsTests.cs`

**Interfaces:**
- Consumes: `ThemesViewModel.ReloadCommand`, `WidgetViewModel.ThemesDir`, `SettingsWindow.SelectPage`.
- Produces: `ScrollViewer x:Name="ThemesPageScroller"` (`Focusable="False"`); `ThemeGallery` and `ThemesList` with no ScrollViewer of their own.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/ThemesListsTests.cs`:

```csharp
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.Updates;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 8 (WF49; spec, Themes; decision 12): the gallery and the installed list grow to fit what they hold, the
/// page scrolls as one, and the wheel over a list scrolls the page.</summary>
[Collection("SettingsWindow")]
public sealed class ThemesListsTests : AppWindowFixture
{
    private const int UserThemes = 14;

    private (SettingsWindow Window, WidgetViewModel Widget, NoActivate Guard) Open(double height)
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        Directory.CreateDirectory(widget.ThemesDir);
        var o = ThemeCatalog.BuiltIns["obsidian"];
        for (int i = 0; i < UserThemes; i++)
        {
            var json = new JsonObject
            {
                ["name"] = $"Theme {i:00}", ["bg"] = o.Bg, ["glass"] = o.Glass, ["glass_on_mica"] = o.GlassOnMica, ["title_bg"] = o.TitleBg,
                ["border"] = o.Border, ["text"] = o.Text, ["text_secondary"] = o.TextSecondary, ["text_dim"] = o.TextDim,
                ["text_muted"] = o.TextMuted, ["accent"] = o.Accent, ["bar_bg"] = o.BarBg, ["footer_bg"] = o.FooterBg,
                ["pace_marker"] = o.PaceMarker, ["sparkline"] = o.Sparkline,
            };
            File.WriteAllText(Path.Combine(widget.ThemesDir, $"theme-{i:00}.json"), json.ToJsonString());
        }
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
            checkForUpdatesNow: () => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate)), isStoreBuild: false);
        svm.Themes.ReloadCommand.Execute(null);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        window.Width = 800;
        window.Height = height;   // after Show: FitToWorkArea sizes the window on open
        window.SelectPage(SettingsPage.Themes);
        window.UpdateLayout();
        Pump();
        guard.Check();
        return (window, widget, guard);
    }

    private static ScrollViewer? ScrollerAbove(DependencyObject element)
    {
        for (var p = VisualTreeHelper.GetParent(element); p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is ScrollViewer s) return s;
        return null;
    }

    private static int Count<T>(DependencyObject root) where T : DependencyObject
    {
        int n = 0;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T) n++;
            n += Count<T>(c);
        }
        return n;
    }

    /// <summary>Every item container lies inside the list's own bounds: nothing is clipped by a cap.</summary>
    private static void NothingClipped(ListBox list, string what)
    {
        Assert.True(list.Items.Count > 0, $"{what} is empty, so the check below is vacuous");
        for (int i = 0; i < list.Items.Count; i++)
        {
            var c = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(i);
            Assert.NotNull(c);
            var box = c.TransformToAncestor(list).TransformBounds(new Rect(c.RenderSize));
            Assert.True(box.Bottom <= list.ActualHeight + 0.5, $"{what} item {i} ends at {box.Bottom:0} of {list.ActualHeight:0}");
        }
    }

    [Fact]
    public void Every_theme_shows_and_the_page_scrolls() => AppHost.Run(() =>
    {
        var (window, widget, _) = Open(height: 600);
        try
        {
            var page = (ScrollViewer)window.FindName("ThemesPageScroller");
            var gallery = (ListBox)window.FindName("ThemeGallery");
            var installed = (ListBox)window.FindName("ThemesList");
            Assert.Same(page, ScrollerAbove(gallery));
            Assert.Same(page, ScrollerAbove(installed));
            Assert.Equal(0, Count<ScrollViewer>(installed));
            Assert.Equal(UserThemes, installed.Items.Count);
            Assert.True(gallery.Items.Count >= 6 + UserThemes, $"gallery holds {gallery.Items.Count}");
            NothingClipped(gallery, "the gallery");
            NothingClipped(installed, "the installed list");
            Assert.True(page.ExtentHeight > page.ViewportHeight, "at 600 DIP the page should scroll rather than squeeze");
            Assert.False(page.Focusable);
        }
        finally
        {
            window.Close();
            widget.Dispose();
        }
    });

    [Fact]
    public void Wheel_over_the_installed_list_scrolls_the_page() => AppHost.Run(() =>
    {
        var (window, widget, _) = Open(height: 600);
        try
        {
            var page = (ScrollViewer)window.FindName("ThemesPageScroller");
            var installed = (ListBox)window.FindName("ThemesList");
            double before = page.VerticalOffset;
            installed.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.MouseWheelEvent });
            window.UpdateLayout();
            Assert.True(page.VerticalOffset > before, $"the page stayed at {page.VerticalOffset:0}");
        }
        finally
        {
            window.Close();
            widget.Dispose();
        }
    });

    [Fact]
    public void The_Themes_block_has_no_list_caps()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Views", "SettingsWindow.xaml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Sanduhr.App", "Views", "SettingsWindow.xaml"));
        int start = xaml.IndexOf("<TabItem Header=\"Themes\"", StringComparison.Ordinal);
        int end = xaml.IndexOf("</TabItem>", start, StringComparison.Ordinal);
        var themes = xaml[start..end];
        Assert.DoesNotContain("MaxHeight=\"112\"", themes);
        Assert.DoesNotContain("Height=\"72\"", themes.Replace("MinHeight=\"72\"", ""));
    }
}
```

- [ ] **Step 2: See the behavioural RED**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~ThemesListsTests > ../.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-9-red.log 2>&1`
Expected: 3 failures.
- `Every_theme_shows…`: the cast `(ScrollViewer)window.FindName("ThemesPageScroller")` gives `null`, so `Assert.Same` reports the gallery's 112 DIP ScrollViewer instead. Before Step 3, add the `ThemesPageScroller` wrapper alone (Step 3's first edit). The failure then moves to `Assert.Same(page, ScrollerAbove(gallery))`, behavioural: the gallery's own capped ScrollViewer sits between.
- `Wheel_over…`: "the page stayed at 0".
- `The_Themes_block…`: the `MaxHeight="112"` assertion.

- [ ] **Step 3: Implement**

All edits are in `SettingsWindow.xaml`'s Themes TabItem. Read the block first (`<TabItem Header="Themes"` through its `</TabItem>`).

1. Wrap the page. Replace

```xml
                <TabItem Header="Themes" Tag="Themes" AutomationProperties.Name="Themes">
                    <Border x:Name="ThemesRoot" Background="{DynamicResource Sanduhr.Brush.Glass}" CornerRadius="8" Padding="16"
                            DataContext="{Binding Themes}">
                        <Grid>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="*" />
```

with

```xml
                <TabItem Header="Themes" Tag="Themes" AutomationProperties.Name="Themes">
                    <!-- Settings v2, slice 8 (WF49): one scrolling page, so both lists show everything they hold. -->
                    <ScrollViewer x:Name="ThemesPageScroller" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled"
                                  Focusable="False">
                    <Border x:Name="ThemesRoot" Background="{DynamicResource Sanduhr.Brush.Glass}" CornerRadius="8" Padding="16"
                            DataContext="{Binding Themes}">
                        <Grid>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
```

Then close it. Replace

```xml
                        </Grid>
                    </Border>
                </TabItem>

                <!-- Desk (Settings v2, slice 3: the split, WF22, WF8): the switch, Arrange, the map on top, every piece (Show, place, order),
```

with

```xml
                        </Grid>
                    </Border>
                    </ScrollViewer>
                </TabItem>

                <!-- Desk (Settings v2, slice 3: the split, WF22, WF8): the switch, Arrange, the map on top, every piece (Show, place, order),
```

2. The gallery loses its ScrollViewer. Replace

```xml
                                <ScrollViewer VerticalScrollBarVisibility="Auto"
                                              HorizontalScrollBarVisibility="Disabled" MaxHeight="112">
                                    <ScrollViewer.Resources>
                                        <Style TargetType="ScrollBar" BasedOn="{StaticResource SlimThemeScrollBar}" />
                                    </ScrollViewer.Resources>
                                    <ListBox x:Name="ThemeGallery" Style="{StaticResource ThemeSwatchList}"
                                             ItemsSource="{Binding Themes}" AutomationProperties.Name="Choose a theme" />
                                </ScrollViewer>
```

with

```xml
                                <ListBox x:Name="ThemeGallery" Style="{StaticResource ThemeSwatchList}"
                                         ItemsSource="{Binding Themes}" AutomationProperties.Name="Choose a theme" />
```

3. The editor stops scrolling on its own. Replace

```xml
                            <!-- The editor scrolls inside the flexible row: at a short window the token
                                 list would otherwise push "Save & apply" below the frame, and the grid
                                 itself does not scroll. Installed themes and their buttons stay pinned. -->
                            <ScrollViewer Grid.Row="4" VerticalScrollBarVisibility="Auto"
                                          HorizontalScrollBarVisibility="Disabled">
                                <ScrollViewer.Resources>
                                    <Style TargetType="ScrollBar" BasedOn="{StaticResource SlimThemeScrollBar}" />
                                </ScrollViewer.Resources>
                                <StackPanel>
```

with

```xml
                            <!-- The editor sits on the page, which scrolls as one (slice 8, WF49): an inner scroller would
                                 swallow the wheel over the editor. -->
                            <Border Grid.Row="4">
                                <StackPanel>
```

and replace

```xml
                            </ScrollViewer>
                            <!-- Installed user themes -->
```

with

```xml
                            </Border>
                            <!-- Installed user themes -->
```

4. The installed list grows, and has no scroll host of its own. Replace

```xml
                            <Border Grid.Row="8" Height="72" Background="{DynamicResource Sanduhr.Brush.Bg}" CornerRadius="6"
                                    BorderBrush="{DynamicResource Sanduhr.Brush.Border}" BorderThickness="1">
                                <Grid>
                                    <ListBox x:Name="ThemesList" AutomationProperties.Name="Installed user themes"
                                             ItemsSource="{Binding InstalledThemes}"
                                             SelectedItem="{Binding SelectedTheme, Mode=TwoWay}"
                                             Background="Transparent" BorderThickness="0"
                                             Foreground="{DynamicResource Sanduhr.Brush.Text}"
                                             ScrollViewer.HorizontalScrollBarVisibility="Disabled" />
```

with

```xml
                            <Border Grid.Row="8" MinHeight="72" Background="{DynamicResource Sanduhr.Brush.Bg}" CornerRadius="6"
                                    BorderBrush="{DynamicResource Sanduhr.Brush.Border}" BorderThickness="1">
                                <Grid>
                                    <!-- No scroll host of its own (slice 8, WF49): the page scrolls, and the wheel reaches it. -->
                                    <ListBox x:Name="ThemesList" AutomationProperties.Name="Installed user themes"
                                             ItemsSource="{Binding InstalledThemes}"
                                             SelectedItem="{Binding SelectedTheme, Mode=TwoWay}"
                                             Background="Transparent" BorderThickness="0"
                                             Foreground="{DynamicResource Sanduhr.Brush.Text}">
                                        <ListBox.Template>
                                            <ControlTemplate TargetType="ListBox">
                                                <ItemsPresenter Margin="2" />
                                            </ControlTemplate>
                                        </ListBox.Template>
                                    </ListBox>
```

The inner `<Grid>` keeps its "No user themes yet." TextBlock after the ListBox. The `MinHeight` keeps that line's box.

- [ ] **Step 4: Run, then the App suite once**

Run the class, logging to `task-9-green.log`.
Expected: PASS.

Run the App suite once (`task-9-app.log`).
Expected: PASS, including `ThemeGalleryAutomationTests`, `ThemeStudioDeskTests`, `SettingsReachTests` (the Tab walk: the new scroller is not a stop) and `SettingsSourceScanTests` (both list names).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/tests/Sanduhr.App.Tests/ThemesListsTests.cs
git commit -m "feat(settings-v2): Themes scrolls as one page and both theme lists show everything they hold

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: The Done line: the contrast test passes for every theme, and Reset leaves one folder's entries in place

**Files:**
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/Slice8DoneTests.cs`
- Modify only if a test finds a gap: the file it names (Tasks 3 to 9)

**Interfaces:**
- Consumes: everything above.
- Produces: the spec's Done line as tests: "the contrast test passes for all six themes, and Reset can leave one folder's entries in place". Reset runs through the real dialog, the real service and the real undo, over real installs in a scratch profile with streamer mode on.

- [ ] **Step 1: Write the tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/Slice8DoneTests.cs`:

```csharp
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 8, Done: "the contrast test passes for all six themes, and Reset can leave one folder's entries in
/// place." Reset is driven through the dialog's own Reset button, with streamer mode on, over real installs.</summary>
[Collection("SettingsWindow")]
public sealed class Slice8DoneTests : AppWindowFixture
{
    [Fact]
    public void Every_built_in_reads_on_the_page_and_the_sidebar()
    {
        var bad = new List<string>();
        foreach (var key in ThemeCatalog.BuiltInOrder)
        {
            var def = ThemeCatalog.BuiltIns[key];
            foreach (var (token, hex) in new[] { ("TextDim", def.TextDim), ("TextMuted", def.TextMuted), ("SelectedLabel", ThemeContrast.SelectedLabel(def)) })
            {
                double worst = ThemeContrast.Worst(hex, def);
                if (worst < ThemeContrast.TextMin) bad.Add($"{key} {token} {hex}: {worst:0.00}:1");
            }
        }
        Assert.Equal(6, ThemeCatalog.BuiltInOrder.Count);
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
    }

    [Fact]
    public void Reset_leaves_one_folder_byte_for_byte_and_undoes_the_other() => AppHost.Run(() =>
    {
        string keep = Home(".claude-qxkeep", "{\n  \"theme\": \"dark\"\n}\n");
        string undo = Home(".claude-qxundo", "{\n  \"theme\": \"dark\"\n}\n");
        var (widget, accounts) = ScratchWidget("Qxalpha");
        ResetWindow? window = null;
        try
        {
            // Real installs, as Settings makes them, into both folders.
            string source = Path.Combine(Root, "app", "mods", "sanduhr-meters");
            Directory.CreateDirectory(Path.Combine(source, ".claude-plugin"));
            Directory.CreateDirectory(Path.Combine(source, "hooks"));
            File.WriteAllText(Path.Combine(source, ".claude-plugin", "plugin.json"), "{ \"name\": \"sanduhr-meters\", \"version\": \"0.3.0\" }");
            File.WriteAllText(Path.Combine(source, "hooks", "hooks.json"), "{ \"modules\": [\"./register.tsx\"] }");
            File.WriteAllText(Path.Combine(source, "hooks", "register.tsx"), "export {};");
            var integ = new ClaudeCodeIntegrations(Profile, widget.SanduhrAppDataDir, source, externalDir: widget.SanduhrExternalDir);
            var statusline = new StatuslineInstaller(Profile, widget.StatuslineBinDir, null, widget.SanduhrAppDataDir);
            var mcp = new McpIntegrationInstaller(Profile, widget.SanduhrAppDataDir, externalDir: widget.SanduhrExternalDir);
            Assert.True(statusline.InstallScript());
            foreach (var folder in new[] { keep, undo })
            {
                Assert.True(integ.Install(IntegrationKind.Meters, folder).Ok);
                Assert.True(integ.Install(IntegrationKind.Hooks, folder).Ok);
                Assert.True(statusline.Install(folder, StatuslineMode.Replace, StatuslineInstaller.DefaultSegments, StatuslineInstaller.JoinLine));
                Assert.True(mcp.Register(Path.GetFileName(folder)).Ok);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(mcp.LauncherPath)!);
            File.WriteAllText(mcp.LauncherPath, "@echo off");
            var before = Directory.EnumerateFiles(keep, "*", SearchOption.AllDirectories)
                .ToDictionary(f => f, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

            widget.Streamer.SetOn(true);
            var paths = ScratchPaths();
            var service = new ResetService(paths, accounts, new PublishTokenStore(new MemoryCredentials()),
                k => Task.Run<IReadOnlyList<string>>(() => ClaudeCodeViewModel.UndoEverything(widget, k)),
                () => { },
                clearClaudeCodeSwitches: () => new SettingsStore(paths).ClearClaudeCodeSwitchesForReset());
            var list = ResetFolderListViewModel.Read(widget);
            window = new ResetWindow(service, list);
            OffScreen(window);
            window.Show();
            window.UpdateLayout();

            // Only the Claude Code part, and untick the folder to keep, by the codename it shows.
            foreach (var name in new[] { "AccountsBox", "HistoryBox", "ThemesBox", "SettingsBox" })
                ((CheckBox)window.FindName(name)).IsChecked = false;
            var row = list.Rows.Single(r => ClaudeCodeFolders.Same(r.Path, keep));
            Assert.False(row.Shown.Contains("qx", StringComparison.OrdinalIgnoreCase), row.Shown);
            var folderList = (ItemsControl)window.FindName("FolderList");
            var box = Enumerable.Range(0, folderList.Items.Count)
                .Select(i => folderList.ItemContainerGenerator.ContainerFromIndex(i))
                .OfType<FrameworkElement>()
                .Select(c => (CheckBox)((StackPanel)System.Windows.Media.VisualTreeHelper.GetChild(c, 0)).Children[0])
                .Single(b => (string)b.Content == row.Shown);
            box.IsChecked = false;

            var reset = (Button)window.FindName("ResetButton");
            ((IInvokeProvider)new ButtonAutomationPeer(reset).GetPattern(PatternInterface.Invoke)!).Invoke();
            var deadline = DateTime.UtcNow.AddSeconds(30);
            var quit = (Button)window.FindName("QuitButton");
            while (quit.Visibility != Visibility.Visible && DateTime.UtcNow < deadline)
            {
                Pump();
                Thread.Sleep(10);
            }
            Assert.Equal(Visibility.Visible, quit.Visibility);
            Assert.Equal("Reset is done.", ((TextBlock)window.FindName("ProgressText")).Text);

            // The kept folder: byte for byte.
            foreach (var (file, bytes) in before)
                Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(file)), $"{Path.GetFileName(file)} in the kept folder changed");
            Assert.True(mcp.IsRegistered(".claude-qxkeep"));
            // The other: undone.
            var undone = JsonNode.Parse(File.ReadAllText(Path.Combine(undo, "settings.json")))!;
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse("{\"theme\":\"dark\"}"), undone), undone.ToJsonString());
            Assert.False(mcp.IsRegistered(".claude-qxundo"));
            // What the kept folder's entries run, and the record that removes them later, stayed.
            Assert.True(File.Exists(mcp.LauncherPath));
            Assert.True(File.Exists(statusline.ScriptPath));
            Assert.True(Directory.EnumerateFileSystemEntries(integ.ModsRoot).Any());
            var receipts = new ReceiptStore(Path.Combine(widget.SanduhrAppDataDir, ResetPlan.IntegrationsName, ResetPlan.ReceiptsFileName)).Load().Records;
            Assert.NotEmpty(receipts);
            Assert.All(receipts, r => Assert.True(ClaudeCodeFolders.Same(r.Folder, keep)));
            // ~\.sanduhr is shared ground: it is still there, with Sanduhr's kept names in it.
            Assert.True(Directory.Exists(widget.SanduhrExternalDir));
        }
        finally
        {
            window?.Close();
            widget.Dispose();
        }
    });
}
```

`ModsRoot` is `ClaudeCodeIntegrations.ModsRoot` (`ClaudeCodeIntegrations.cs:139`). `MemoryCredentials` is the fixture's nested class. The checkbox walk follows the row template from Task 5: the container's first visual child is the template's `StackPanel`, and its first child is the `CheckBox`. If the generator wraps the template in a `ContentPresenter` first, use Task 5's `Descendant<CheckBox>` helper instead.

- [ ] **Step 2: Run them, and prove the Reset test can fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~Slice8DoneTests > ../.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-10-run.log 2>&1`
Expected: PASS. Every line it checks was built by Tasks 3 to 9. A failure names the surface. Fix it in the task's file it names, with its own RED, and note the fix in the report.

The mutation proof keeps the test honest. It is two runs, with each change reverted after:
1. In `ResetWindow.KeepFolders`, return `[]`. Expected failure: "…settings.json in the kept folder changed".
2. In `ResetService.ClaudeCodeEntriesAsync`, drop `keep.Count == 0 &&` from the condition. Expected failure: `Assert.True(File.Exists(mcp.LauncherPath))`.

Log both runs (`task-10-mutation-1.log`, `task-10-mutation-2.log`) and revert each change before the next. `git diff --stat` must show only the new test file.

- [ ] **Step 3: Commit**

```bash
git add windows-dotnet/tests/Sanduhr.App.Tests/Slice8DoneTests.cs
git commit -m "test(settings-v2): slice 8's Done line: every built-in reads, and Reset leaves one folder byte for byte through the dialog

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: End to end: the harness, the whole suite, the owner's Tier 2 item 10, and what is left before the 4.0 Store submission

**Files:**
- Modify: `windows-dotnet/tools/Sanduhr.RenderHarness/SettingsPassScenario.cs` (`PassContrast`)
- Create (not committed with code):
  - `.superpowers/sdd/2026-10-27-settings-v2-slice-8/tier2-checklist.md`
  - `.superpowers/sdd/2026-10-27-settings-v2-slice-8/hub-reset-folders.md`
  - `.superpowers/sdd/carry-after-4.0/slice-8/carry.md`

**Interfaces:**
- Consumes: everything above.
- Produces:
  - `contrast.txt` measured on `ThemeContrast`'s surfaces
  - Themes anchors landing at 800 and 600
  - a green suite
  - the Tier 2 hand-off
  - the hosted-policy draft
  - the after-4.0 carry

- [ ] **Step 1: The harness measures what the test measures**

In `windows-dotnet/tools/Sanduhr.RenderHarness/SettingsPassScenario.cs`, replace the body of `PassContrast` from `var sb = new StringBuilder();` to `PassWrite("contrast.txt", sb.ToString());` with:

```csharp
        var sb = new StringBuilder();
        sb.AppendLine("WCAG contrast ratios of Settings text brushes on the surfaces Settings paints (ThemeContrast.SettingsSurfaces, slice 8):");
        sb.AppendLine("the page card (Glass), a well inside a page (Bg) and the sidebar (TitleBg at the chrome alpha over Bg). The window is opaque: no Mica.");
        sb.AppendLine($"Thresholds: {ThemeContrast.TextMin} for body text (TextDim's floor is {ThemeContrast.DimFloor}), {ThemeContrast.NonTextMin} for indicators. Marked LOW below 4.5.");
        foreach (var (key, d) in defs)
        {
            var surfaces = ThemeContrast.SettingsSurfaces(d);
            sb.AppendLine();
            sb.AppendLine($"## {key} ({string.Join("; ", surfaces.Select(s => $"{s.Name} {ThemeContrast.Hex(s.Color)}"))})");
            foreach (var (name, hex) in new[] { ("Text", d.Text), ("TextSecondary", d.TextSecondary), ("TextDim", d.TextDim), ("TextMuted", d.TextMuted), ("Accent", d.Accent), ("PaceMarker", d.PaceMarker), ("SelectedLabel", ThemeContrast.SelectedLabel(d)) })
            {
                var parts = surfaces.Select(s => (s.Name, R: ThemeContrast.Ratio(hex, s.Color))).ToList();
                double worst = parts.Min(p => p.R);
                sb.AppendLine($"  {name,-14} {hex}  " + string.Join("  ", parts.Select(p => $"{p.Name} {p.R,5:0.0}")) + (worst < 4.5 ? "  LOW" : ""));
            }
            sb.AppendLine($"  Border vs page {ThemeContrast.Ratio(d.Border, surfaces[0].Color):0.0} (non-text, wants 3.0); Accent vs page {ThemeContrast.Ratio(d.Accent, surfaces[0].Color):0.0} (focus ring and the selected row's bar, wants 3.0)");
        }
        PassWrite("contrast.txt", sb.ToString());
```

The `defs` list above that line (the six built-ins and the harness's light theme) is kept. The `Lum`, `Ratio`, `Hex` and `Over` helpers stay: `PassDialog` uses them.

- [ ] **Step 2: Build and run the harness's contrast and links parts off screen, against a scratch profile**

Run: `dotnet build tools/Sanduhr.RenderHarness > ../.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-11-build.log 2>&1` (from `windows-dotnet/`)
Expected: Build succeeded, 0 errors, no new warnings.

From the repo root, in PowerShell:

```powershell
$env:SANDUHR_DEV_PROFILE = Join-Path $env:TEMP "sanduhr-slice8-profile"
New-Item -ItemType Directory -Force $env:SANDUHR_DEV_PROFILE | Out-Null
dotnet run --project windows-dotnet/tools/Sanduhr.RenderHarness -- --scenario settings-pass --parts contrast,links --out (Join-Path $env:TEMP "sanduhr-slice8-out") *> .superpowers/sdd/2026-10-27-settings-v2-slice-8/task-11-harness.log
```

Expected: the run ends normally. Its `settings-pass: foreground window before X, after X` line shows the same handle twice.

- [ ] **Step 3: Check the contrast table and every Themes landing**

```powershell
$out = Join-Path $env:TEMP "sanduhr-slice8-out\settings-pass"
$c = Get-Content (Join-Path $out "contrast.txt")
$c | Select-String -Pattern "^\s+(TextDim|TextMuted|SelectedLabel)\s.*LOW$" | Where-Object { $_.LineNumber -lt ($c | Select-String "## harness-light").LineNumber }
Select-String -Path (Join-Path $out "anchors-landing.txt") -Pattern "^(Themes|About) / .* \| (800|600) \|" |
    Where-Object { $_.Line -notmatch "\| (fully in view|top in view)[^|]* \|[^|]*\| ok$" }
```

Expected: no output.
- No built-in prints LOW for TextDim, TextMuted or SelectedLabel. The harness's light theme comes last and is reported, not gated (decision 9). Copy its lines into the report.
- `gallery`, `studio`, `paste`, `installed` and `reset` each land in view at 800 and 600.

A printed line names the anchor and why. Fix the landing, then run Steps 2 and 3 once more. The evidence under `%TEMP%` is never committed.

- [ ] **Step 4: The whole suite and the contracts**

Run: `dotnet test Sanduhr.slnx > ../.superpowers/sdd/2026-10-27-settings-v2-slice-8/task-11-suite.log 2>&1` (from `windows-dotnet/`)
Expected: PASS across `Sanduhr.Tests`, `Sanduhr.App.Tests` and `Sanduhr.Mcp.Tests`, with every `[DesktopFact]` and `[PowerShellFact]` included. Run it on a quiet desktop: slice 7's focus flakes came from a game in the foreground. Read failures from the log. Then:

```bash
git diff release/4.0 --stat -- windows-dotnet/tests/Sanduhr.Mcp.Tests/TrustBoundaryTests.cs windows-dotnet/src/Sanduhr.Mcp windows-dotnet/src/Sanduhr.Mcp/Sanduhr.Mcp.csproj
git diff release/4.0 --stat -- windows-dotnet/tests/Sanduhr.Tests/SettingsKeysContractTests.cs mac/
git diff release/4.0 --numstat -- windows-dotnet/src/Sanduhr.Core/ThemeModel.cs
git diff release/4.0 -U0 -- windows-dotnet/src/Sanduhr.Core/ThemeModel.cs | grep '^[+-] ' | grep -v -E '^[+-] +Text(Dim|Muted) = "#[0-9a-f]{6}",$'
```

Expected:
- The first two print nothing: the MCP's source, its link list, its pin, the keys contract and `mac/` are untouched.
- The third prints `12	12	windows-dotnet/src/Sanduhr.Core/ThemeModel.cs`.
- The fourth prints nothing: every changed line in the one linked file is a TextDim or TextMuted value (D3).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/tools/Sanduhr.RenderHarness/SettingsPassScenario.cs
git commit -m "test(settings-v2): the harness's contrast table measures the surfaces Settings paints, as ThemeContrastTests does

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 6: Hand the Tier 2 checks, the hosted-policy draft and the after-4.0 carry over**

None of this is code, and none of it is for the agent. These checks need the real app, a real profile and real Claude Code folders, which this plan never launches.

Write `.superpowers/sdd/2026-10-27-settings-v2-slice-8/tier2-checklist.md` with these items, and post them to the owner:

```markdown
# Slice 8 Tier 2 (owner, real app, real profile)

Findings "Needs the live pass" item 10 (WF37 and WF38), plus what this slice changed that only a real screen shows. About a minute
each; item 5 needs a spare profile. "Tests" says what is proved without you.

1. **Windows high contrast.** Settings, the widget and the Reset dialog under a Windows high-contrast theme (Settings, Accessibility,
   Contrast themes): does Sanduhr stay readable or fight the system colours? Record what you see; Sanduhr paints its own theme today.
   Tests: none (decision 8).
2. **The real Mica backdrop.** Settings is opaque, so Mica never shows under it (decision 8). The widget's cards do sit on Mica: in each
   built-in theme, read the tier cards' reset countdown and footer (TextDim) and the burn line (TextMuted) over a light and a dark
   wallpaper. Tests: the Settings surfaces are pinned against ThemePalette; the widget over a real desktop is yours.
3. **The six themes' new dim text.** Walk Settings in each built-in theme (Themes, choose each): captions, group headings, empty lines.
   The values and the before/after table are in the plan; the look is yours (owner taste 1 and 2).
4. **Obsidian's sidebar.** The selected page reads in Text beside a small accent bar; in the other five the label stays the accent, with
   the bar. Narrator still reads the page name. Tests: colours and the bar's visibility are pinned.
5. **Reset with a kept folder (spare profile).** With two Claude Code folders that both have the statusline, the meters mod and the MCP
   server: open About, Reset Sanduhr…, untick one folder, Reset with every part. Afterwards: the kept folder's statusline and meters mod
   still load in Claude Code; its MCP server still answers `ping`; the other folder has nothing of Sanduhr's; `~\.sanduhr\time\` is
   untouched. Relaunch, sign in, open Settings, Claude Code: the kept folder's meters come back (the line said they would), and Remove
   there takes its entries out. Tests: byte-for-byte and the kept files are pinned through the real dialog; Claude Code loading them is yours.
6. **Reset on stream.** Streamer mode on, open Reset: the folder rows show codenames. Flip streamer mode from the tray while it is open:
   the rows change. Tests: both pinned.
7. **About's caption.** It sits under Reset Sanduhr… and wraps at the page's width. Narrator reads it as the button's help.
8. **Themes with many themes.** Install a dozen user themes (or Paste and save several), then shrink Settings to its minimum height: both
   lists show every theme, the page scrolls, and the wheel over either list scrolls the page. Tests: pinned at 600 DIP with 14 themes.

Owner calls still open: the 11-line taste list (each defaulted in the plan, one line to change each) and decisions D1 to D6.
```

Write `.superpowers/sdd/2026-10-27-settings-v2-slice-8/hub-reset-folders.md`, the hosted-policy update for the controller to post (as slice 7's `hub-275-streamer.md` was):

```markdown
# Hosted privacy policy: Reset's per-folder choice (Windows, 4.0)

PRIVACY.md, "How you remove your data", Clear local storage, gains one sentence (slice 8, decision 16):

> Its first part lists each Claude Code folder that holds Sanduhr's entries, each with its own checkbox. A folder you untick keeps its
> entries exactly as they are, and Sanduhr keeps the files they run and its record of them, so they keep working and Settings, Claude
> Code can remove them later.

No data is added, sent or kept anywhere new. The hosted page should carry the sentence word for word.
```

Write `.superpowers/sdd/carry-after-4.0/slice-8/carry.md`:

```markdown
# After slice 8: what is left before the 4.0 Store submission, and after it

Slice 8 is the last Settings v2 slice. This file lists what stands between release/4.0 and a Store submission, then what waits for 4.0.x.

## Before the Store submission
- **Owner Tier 2 passes.** Slice 8's `tier2-checklist.md` (8 items) and slice 7's streamer sweep (11 items,
  `.superpowers/sdd/2026-10-26-settings-v2-slice-7/tier2-checklist.md`), plus any of findings' "Needs the live pass" items 1 to 12 not
  yet run on a real profile.
- **Owner calls.** Slice 8's taste list (11 lines) and D1 to D6; slice 7's taste list (13 lines, defaulted). D6, the sidebar's group
  order, before screenshots are taken.
- **Hosted policy.** Post `hub-reset-folders.md` (slice 8) beside slice 7's hub #275 comment; the hosted page must match PRIVACY.md.
- **Docs sweep.** `windows-dotnet/README.md` still names Mods & Config and Settings, Widgets (slice 4 final review row 17).
- **Release work, Windows.** `docs/release-runbook.md` phases 1 to 8: the version lockstep (the csproj trio and the manifest; Store
  versions need a `.0` fourth component), the reviewer letter and release notes, the Store MSIX and Velopack builds, the tag, the GitHub
  Release, the submission. `docs/ms-store-submission-playbook.md`: every 10.1.4.4 check (the trademark disclaimer wherever Claude is
  named, the unique-value features live, tool-strip navigation with accessible names, light-mode dialogs). Store copy from the Store
  Listing Console (`../store-listing-console/apps/sanduhr/copy/`), with the 4.0 What's New cards ("Settings, rearranged", "Streamer mode").
- **Screenshots** with made-up data only (demo mode), after D6.
- **Merge** release/4.0 into main.

## After 4.0 (4.0.x or later)
- D1: a ThemeLint contrast rule for user, pack and agent themes (Windows, the Mac and AGENT_PROMPT together).
- D2: accent-coloured text in Obsidian (`SettingsLinkButton` and accent captions, 3.95:1); `AccentText` computed as decision 11 does.
- D4: the Mac's built-in TextDim and TextMuted, and the shared lint fixture.
- Slice 7 P6 option B or C: MCP answers under streamer mode.
- The earlier slices' polish carries addressed to "slice 8" (D5), each one line, from the slice 2 to 5 final reviews:
  - slice 2: the dialog scan blocks only hex Background, and the secondary button has no hover or pressed state (T12); EmptyLinkLine
    lacks LiveSetting (T16); "%" TextBlocks may be read twice by Narrator (T19)
  - slice 3: T1 `meetings` default arm InlineData; T2 Step loop comment and BC→up test; T3/T4/T13 stale doc comments ("Settings, Desk,
    Launcher", "Where each piece sits"); T5 split-literal scan; T6 blank lines and unused helpers; T7 CanPlace announced twice and
    CS8624 at App.xaml.cs:703; T8 re-indent and a click pass-through test; T10 hidden status text unpinned; T11 Colors row after Light
    or Dark, and FontOptions built once; T13 a missing-app click not scrolled into view; T14 the toggle's hover/pressed state and its ring
    colour, HelpText on the Expander, a null HelpText's empty line; T15a `leaked.Dispose` when `Open` throws; T16 heading scan order;
    T18 companion:bc after install
  - slice 4: rows 5, 6 ("keyboard routing worked: False" prints "not run"), 13 to 16 (Unreadable folder plugins, meters row peers,
    LiveSetting-only status lines, the double `Desk.Reload`), 20 to 23, 25 to 29, 31, 32, 34, 35
  - slice 5: jump links on Alerts, Publish usage and About were done in slices 5 and 6; the touches wording outside SettingsNames.
- Spec "Not in 4.0": the OBS capture source, a demo-mode starter, switching other Claude Code mods (R1, R6), the cross-folder catalog and
  Compare (R2, R9), Mod Studio (R3), config packs (R4), statusline segments (R5), `sanduhr://settings`, live previews, colour wells (WF46).
```

---

## Carried in, and where each went

Every row of `.superpowers/sdd/carry-into-slice-8/slice-7/carry.md`, the lessons slices 5 to 7 recorded, and the earlier rows addressed to slice 8:

| # | Carried item (source) | Where |
| --- | --- | --- |
| 1 | The per-folder list shows codenames while streamer mode is on, through `StreamerMode.ShowFolder`, with a reach-scan marker (slice 7 carry, Reset; P23) | Task 5: `ResetFolderRow.ShownFor` calls `ShowFolder` on its marked line; `StreamerReachTests.Known` gains the file's `key 3` and `masked 1` rows; Task 10 drives it with streamer mode on |
| 2 | The per-folder list goes in its own part; don't move `codenames.json` and `streamer.json` (slice 7 carry) | Task 2 adds members after `AccountProfileDirs`; the Settings part's lines are untouched |
| 3 | `ResetPlanTests` merge surface (slice 7 carry) | Merged already; Task 2 appends after slice 7's last fact |
| 4 | Failure lines stay kinds, never names (slice 7 carry) | `ClaudeCodeReset` and `ResetService` return the same kind phrases; no new failure text |
| 5 | About's caption names kinds of things, not files (slice 7 carry) | Decision 5: kinds only, plus themes and widgets |
| 6 | The banner's Accent border on Bg is in scope only if Accent changes (slice 7 carry) | Accent is unchanged; Task 7 pins the border at 3.0:1 in all six anyway |
| 7 | `SettingsViewModel` is `INotifyPropertyChanged`, and `ReloadNames` grew (slice 7 carry) | Task 5 adds one line to `ReloadNames` and an internal property; no property change notification needed |
| 8 | A slice 8 edit to PRIVACY.md's Reset row may conflict (slice 7 carry) | Task 6 edits the Clear local storage bullet, not the rows slice 7 changed |
| 9 | MCP answers under streamer mode (P6 = A), OBS source (slice 7 carry) | Not in this slice; listed in the after-4.0 carry |
| 10 | Copy true in only some states (slices 6 and 7) | Decision 6: four lines, each shown only in its state, pinned by `Each_line_shows_only_in_the_state_it_is_true_for`; the untrue old warning goes |
| 11 | Masked values read back as data (slice 7 final review) | Decision 14; `KeepFolders` returns real paths; tested with a codename on the row (Task 5) and through the dialog (Task 10) |
| 12 | Status lines keep stale names across a streamer flip (slice 7 C1) | Decision 13; `ResetFolders?.RefreshNames()` in `ReloadNames`; `A_streamer_flip_while_Reset_is_open_renames_the_rows` |
| 13 | Subscriptions that must detach on Closed (slice 7) | The one new event lives per dialog and detaches on `Closed`; `ResetFolders` cleared in `finally` |
| 14 | Non-unique Edit anchors (slice 6 P8) | Global Constraints names the repeats; every XAML edit carries its neighbours |
| 15 | Leaked WidgetViewModels (slice 5 P21) | Every new App test disposes its widget and closes its window in `finally` |
| 16 | Process-wide StreamerMode isolation (slice 7 P6, M5) | Every App class that touches StreamerMode or a SettingsWindow carries the collection; `ResetServiceFoldersTests` and the first `SettingsContrastTests` class touch none |
| 17 | Search words that steal another page's queries (slice 6 P2) | No search word added (decision 15) |
| 18 | Timings in copy from constants (slice 6) | No copy quotes an interval |
| 19 | Behavioural RED (slices 6 and 7) | Every task's Step 2 names its stub and the behavioural failure; Task 10's mutation proof |
| 20 | The reach scan's pinned inventory; floor 322 (slice 7 Task 13) | Tasks 4 and 5 change `Known` in the same commit, with the counts named |
| 21 | Reset is destructive; `~\.sanduhr` is shared ground (repo CLAUDE.md, brief) | Decisions 2 to 4; byte-for-byte tests in Tasks 3 and 10; no code lists or deletes the folder |
| 22 | Primary `#0d0d0d` on accent: obsidian 4.50 (slice 2 final review, T12/T20, "carry to slice 8, ThemeContrastTests") | Task 7, `The_dialogs_primary_text_and_the_banners_border_read_on_every_accent` (built-ins; custom and pack accents are D1) |
| 23 | Dialog scan blocks only hex Background; secondary button lacks hover/pressed (slice 2, "carry to slice 8") | Not in this slice (D5); after-4.0 carry |
| 24 | T14 toggle has no hover/pressed state; ring uses Text (slice 3, "carry to slice 8, themes and contrast") | Not in this slice (D5); after-4.0 carry |
| 25 | The remaining slice 3 and slice 4 polish rows addressed to slice 8 | D5; listed one by one in the after-4.0 carry |
| 26 | P25 sidebar group order (slice 3 decision 4) | D6; before screenshots |

## Carried, not in this slice

- **A ThemeLint contrast rule** (D1), **accent text beyond the selected label** (D2), **the Mac's built-ins** (D4): after 4.0.
- **The earlier polish carries** (D5) and **the sidebar's group order** (D6): the owner's call; listed in the after-4.0 carry.
- **MCP answers under streamer mode** (slice 7 P6 = A), **the OBS source**, **a demo-mode starter**: spec "Not in 4.0".
- **The Tier 2 checks** (Task 11 Step 6): the owner, on a real profile and a spare one.

---

## Spec coverage (slice 8)

| Slice 8 bullet, test, Done item or Tier 2 item (spec) | Task |
| --- | --- |
| Reset's per-folder list (WF20, R12): a checkbox per folder that has receipts; `UndoAll(folders)` | 2 (the list), 3 (the undo), 4 (the service), 5 (the dialog) |
| Reset's full label "Sanduhr's entries in Claude Code (statusline, MCP server, meters mod, glow hook)" | Shipped in slice 1; pinned to the rows' kinds in Task 1 (decision 7) |
| About's caption "Removes your accounts, …. You choose which." | 1 (words, decision 5), 6 (the row) |
| Reset removes `codenames.json` and `streamer.json` | Shipped in slice 7; untouched (carry 2) |
| Uncapped theme lists (WF49): no 112 and 72 DIP caps | 9 |
| Theme token contrast (WF37): TextDim and TextMuted at 4.5:1 on page and sidebar, all six built-ins | 7 (tokens and `ThemeContrastTests`) |
| Obsidian's selected label uses Text with an accent bar | 8 |
| `ResetPlanTests` (per-folder undo) | 2 (`ResetPlanTests`); the undo itself in `ClaudeCodeResetTests` (Task 3), because `UndoAll` lives in the installers |
| `ThemeContrastTests` (new, 4.5:1 for TextDim and TextMuted on page and sidebar) | 7, 8 |
| Done: the contrast test passes for all six themes, and Reset can leave one folder's entries in place | 10 |
| Tier 2: item 10 | 11 Step 6 (items 1 and 2), plus items 3 to 8 for what this slice changed |
| Themes: names on the name and slug fields and both lists (WF35); "This theme's JSON is valid."; "Save the theme as"; "Save & apply" | Shipped in slice 1; `SettingsSourceScanTests` still pins both lists' names (Task 9 keeps them) |
| WF20, WF37, WF49 dispositions | 1 to 10 |

## Self-review (writing-plans checklist, run once)

1. **Spec coverage.** Every slice 8 bullet, both named tests, the Done line and Tier 2 item 10 map to a task above. Of the spec's file list, every file is touched. "The built-in theme files" is `ThemeCatalog.BuiltIns` in `ThemeModel.cs`. The extra files are listed with their reasons.
2. **Placeholder scan.** No "TBD", no "add appropriate", no "similar to Task N". `SettingsStore.SaveCcFoldersAdded`, `Sta.Run` and `ReceiptLog.AsideSuffix` were checked against HEAD. One place asks the implementer to confirm a shape before relying on it: the row template's visual tree in Task 10, with Task 5's `Descendant<CheckBox>` helper as the fallback.
3. **Type consistency.**
   - `ResetFolder(Path, Kinds)`, `ResetPlan.FoldersWithEntries`, `ReceiptsFile`, `ReceiptsFileName` and `ReceiptsUnreadable` are what Tasks 3 to 5 use.
   - `ClaudeCodeResetScope` has seven members in the order Task 4 builds it.
   - `ClaudeCodeReset.Undo(scope, keep)` is what `UndoEverything(widget, keep)` calls.
   - `ResetService`'s undo delegate takes `IReadOnlyCollection<string>`, which `App.ShowReset` and Task 10 pass.
   - `RunAsync(choices, keepFolders, started)` is what `ResetWindow.Reset_Click` calls.
   - `ResetFolderRow` (`Path`, `Discovered`, `HasMeters`, `Kinds`, `Shown`, `Undo`, `RefreshName`) and `ResetFolderListViewModel` (`Rows`, `ReceiptsUnreadable`, `Kept`, `UndoesAny`, `Changed`, `RefreshNames`, `LinesFor`, `Read`, `For`) match Tasks 5, 6 and 10.
   - `ResetWindow(service, folders)`, `ShowFor(owner, service, folders, quit)`, `EffectiveChoices` and `KeepFolders` match the tests.
   - `ThemeContrast` (`TextMin`, `DimFloor`, `NonTextMin`, `ChromeAlphaOverMica`, `PrimaryButtonText`, `Page`, `Well`, `Sidebar`, `ChromeAlpha`, `SettingsSurfaces`, `Ratio`, `Worst`, `SelectedLabel`, `Hex`) is what Tasks 7, 8, 10 and 11 call.
   - The slice 8 `SettingsNames` members are what the XAML and the tests name.
4. **Review Focus.** Each of the five lines has its test in the owning task:
   - 1: Task 3
   - 2: Tasks 5 and 10
   - 3: Tasks 2, 4 and 5
   - 4: Task 8
   - 5: Task 9
