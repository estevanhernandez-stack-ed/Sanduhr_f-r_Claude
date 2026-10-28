# Settings v2 for Windows: findings

Synthesis, 2026-10-24, against main at 3.9.1 (branch `docs/settings-v2-windows`, harness commit `b281d68`). Inputs: the code audit `inventory.md` (cited **W1** to **W33**), the mods parity audit `mods-parity.md` (cited **M** plus the capability-matrix row, e.g. **M11**; its ranked gaps are R1 to R12 and its looks-only list L1 to L8), the off-screen harness evidence for the `settings-pass` scenario (cited **H**, with the item number from its index where it answers one of the inventory's 8 open items; the PNGs and dumps are not committed because the harness reads real Claude Code folders), and four persona passes that read only that evidence (**P1** first-timer, **P2** daily Claude Code developer, **P3** two accounts and privacy, **P4** small screen, keyboard and screen reader). The method and format follow the Mac's `docs/settings-v2-spec.md`.

File abbreviations are the inventory's (SW is `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`, SWcs its code-behind, WP `WidgetsPage.xaml`, and so on). Other paths are under `windows-dotnet/src/`.

## Verdict: Windows Settings grew by accretion, shows state it can't change, and has reach it doesn't use

Every Windows feature landed on whichever page was nearest when it shipped, and the Desk page took most of them. It is now 14 sections and about 1,900 DIP of scrolling at the default height, with its one preview in the 10th section and the piece rows a keyboard user needs most 16 Tab stops deep. A piece is switched on one page and placed on another, and only some features place themselves, so "turn on the weather" means a city on Weather, a corner on Desk and the Desk switch on, and nothing on screen lists the three. The meters mod, the glow, the "let Claude do X" consents, the per-folder choices and the word "history" each have two to four homes. This is the Mac's disease: the trouble is between pages, not inside them.

The second cause is Windows-only. Settings shows lists you can read but not run, and runs actions it doesn't show. Mods & Config lists every plugin and can switch one. The Widgets page installs five pack kinds, and only the Desk companion says where its pack goes: theme, palette and font packs do join existing pickers, but nothing tells you which ones, a palette only appears once Ink is set to Custom, and an effects pack does nothing. Two actions do something other than what their screen implies. Clear history sits under an Account picker, ignores it and empties the active account. Opening the Claude Code page a second time is likely to unlink a folder from its account, and the only trace is the line "Couldn't save that link."

The third cause is reach that's already built and not used. Windows has a names table, a sidebar search over 67 anchors, and anchors that scroll and flash, but no "Show me" and no menu entry passes an anchor, so 12 What's New cards land at the top of the Desk page with their feature up to 2,100 DIP below. Arrange Desk and the order of pieces in a stack are mouse-only, and there is no page or switch for the two questions a privacy-minded person asks: "what leaves this PC" and "how do I hide who I am before I share my screen". Settings v2 for Windows should keep the window, the search and the names table, and change three things: one home per feature with links everywhere else, a control for every list it shows (or no list), and every entry point landing on its section.

## Findings, ranked

Duplicates across the code audit, the mods parity audit, the harness and the four personas are merged, and each finding lists every source. Severity follows the persona scale: **blocker** stops the task, **confusing** makes a person stop and guess, **polish** is wrong but survivable. Where a persona read the screenshots one way and the code says otherwise, the code was checked and the finding says what is true. IDs are WF1 onward, apart from the inventory's W and the Mac's F.

### Blockers

**WF1. Clear history empties the active account's history, whatever the Account picker shows, and no control clears one chosen account.** Sources: P3 (blocker), W23. Correction to W23, which says the button clears every account, and to P3, who could not tell: History's command calls `History.ClearAll()` with no account (`Sanduhr.App/ViewModels/HistoryTabViewModel.cs:227`), and `UsageHistory.ClearAll(null)` resolves to the active account and empties only that file (`Sanduhr.Core/UsageHistory.cs:63`, `:188-194`). The button sits on the row under "Account:" (SW:1117, 1156), whose choices are "All accounts" and each account, and its confirmation says "This permanently removes the local 30-day usage history file", naming no account (`HistoryTabViewModel.cs:218-221`). P3, with Work active and Personal picked, wipes Work. The only way to erase a chosen account's history is "Remove account…", which also deletes its sign-in (`AccountsViewModel.cs:173-177`). Verified in code by the controller.

**WF2. Opening the Claude Code page again likely unlinks its folders from their accounts, and "Couldn't save that link." is the trace.** Sources: P2 (confusing, "error line with no context"), verified in code. Not a harness artifact. The string has one source, the catch in `OnLinkChanged` (`Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs:417-436`). The path: Settings calls `ClaudeCodePage.Reload()` every time the page is opened (SWcs:168-170). Reload clears `AccountOptions` first (`ClaudeCodeViewModel.cs:336-337`) while the old folder cards are still bound to it through `SelectedItem="{Binding LinkedAccount, Mode=TwoWay}"` (SW:1736-1740). Reload's `_loading` flag is the page's, not the row's: each `CcFolderRowViewModel` has its own `_loading`, false after construction (`ClaudeCodeViewModel.cs:17`, `:38`, `:41-44`), so the row forwards the change. A WPF ComboBox whose selected item leaves its items sets the selection to null and writes that back, so each card's `LinkedAccount` becomes null, which reaches `OnLinkChanged(row, null)`. Its loop unlinks every account linked to that folder (`label != account` is true for every label), then `SetAccountFolder(null, path)` throws on the dictionary's null key (`ViewModels/WidgetViewModel.cs:946`), and the catch shows the message. So the message appears after a write that has already removed the link. That breaks P3's task 2 (a work folder shows work meters) without a word. Reasoned from code and consistent with the harness picture (`06-claude-code/page-tall.png`). Confirm in Tier 2 before fixing; the fix is a null guard in `OnLinkedAccountChanged`, or rebuilding the options without clearing them.

**WF3. An installed widget pack doesn't say where it shows up, a palette pack is hidden behind Custom ink, and an effects pack does nothing.** Sources: W1, M31 to M33, mods-parity §4, P1 (blocker, task 3), H item 3. Correction to W1 and to mods-parity §4, which were written against 3.9.0: since 3.9.1 (the pack-wiring PR) three kinds have consumers, and each reads only packs whose `enabled` is true (`Sanduhr.Core/WidgetStore.cs:232-235`).
- Theme packs join the theme list (`Sanduhr.Core/PackThemes.cs:28-34`, `ViewModels/WidgetViewModel.cs:780`).
- Palette packs join the Desk ink presets as "<palette> · <pack>" (`Sanduhr.Core/PackInkPresets.cs:10-21`, `ViewModels/SettingsViewModel.cs:142`). That preset list is visible only when Ink is "Custom" (SW:2646, `IsCustomInk`).
- Font packs join the Desk and Message font lists (`Desk/DeskFonts.cs:36`, `SettingsViewModel.cs:141`, `:145`).
- Effects packs have no consumer outside the validator and Claude's proposal preview (`PackValidator.cs:479`, `WidgetHandoff.cs:135`).

The Widgets page says none of this. After Install, a row reads "Installed." and the installed row offers Uninstall (WP:87). There is no Use, Apply or "find it in…" link, and no control sets `enabled`, so a pack is always on until it is uninstalled. A first-timer who installs a palette has no route to Desk, Ink, Custom, Colors.

**WF4. No view lists the mods across all Claude Code folders.** Sources: P2 (blocker, task 3), M2, M3 (gap R2), L3. Mods & Config reads one folder at a time through its own "Folder" picker (SW:1858-1866), and so does the Claude Code page through a second picker (SW:1769) with separate state (W5). Someone with three folders flips the picker three times and can't compare. The Mac has the cross-folder Catalog (`ModsCatalogView.swift:79-160`). R2's Core `ConfigCatalog` is also the data model WF39 and WF40 need.

**WF5. The order of pieces in a stack and Arrange Desk need a mouse; the layout map is a picture for everyone.** Sources: P4 (blocker), W3. Correction to P4: the map is not a control that keyboard users are locked out of. It is a drawing for everyone: `DeskLayoutMap` is a Canvas with `Focusable = false` and no mouse handling (`Views/DeskLayoutMap.cs:13-38`), redrawn from the layout. A keyboard user *can* move a piece through its row's Place, Size and Monitor combos (DP:36-55, taborder steps 10 to 25). What a keyboard user can't do is change the order of pieces sharing a corner: rows have no Order control, and reordering lives only in Arrange (inventory §3.1, `DeskArrangeCopy.BarHint`). Arrange's key handling is Return and Escape and nothing else (`Desk/DeskArrangeMode.cs:643`). The map caption, "Each piece sits at its place, in its order and at its size." (SW:2577), reads like an instruction to a control that isn't there.

**WF6. Nothing lists what leaves the PC, and the daily update check has no switch or disclosure.** Sources: P3 (blocker, task 3), W25, W18. P3 pieced together the claude.ai fetches, Publish usage, Weather, the Widgets catalog and MCP `publish_usage`, and found no single list and no offline switch. Corrections to P3:
- **Weather is opt-in three times over.** The forecast runs only while the Desk is on, a place is set and the Weather piece is placed (`Desk/DeskController.cs:583-586`), at most every 30 minutes (`Sanduhr.Core/Weather.cs:176`). "Clear" on Weather, or "Hidden" on the piece row, is the off switch. It is not a named switch, but it exists.
- **The Widgets catalog is fetched only when the Widgets page opens** (`WidgetsPageViewModel.Activate`, `ViewModels/WidgetsPageViewModel.cs:280-285`) or on "Check the catalog again"; there is no background fetch. P3 is right that there is no switch, and the disclosure is at the page foot (WP:181), below a fetch that has already happened.
- **One call P3 did not find:** non-Store installs check GitHub for an update once every 24 hours (`Updates/UpdateChecker.cs:22-63`). No page mentions it, and nothing turns it off.

"privacy" in search opens the About page with no section (H search table).

**WF7. Nothing hides who you are before you share your screen, and demo mode can only be started from the command line.** Sources: P3 (blocker, task 5), H search ("demo": 0 rows). Correction to P3, who read "End demo mode from the tray menu" as a sign there is a tray item to start it: there isn't. Demo mode starts only with `--demo` or `SANDUHR_DEMO=1` (`App.xaml.cs:210-214`, `Sanduhr.Core/DemoMode.cs:23`), and the tray only ends it (`App.xaml.cs:397-398`). Demo mode also replaces the data instead of hiding the names, so it doesn't answer P3's task anyway. See "Feeds for streamer mode" below for what such a switch would have to reach.

### Confusing: one feature, several homes

**WF8. A piece is switched on one page and placed on another, and only some features place themselves.** Sources: W4, P1 (task 1), P2 (task 4), H (piece rows at 1147 DIP). The default layout is `clock:bl claude:bl` (`Sanduhr.Core/DeskArrangement.cs:288`); every other piece starts Hidden. Glow, Launcher and Companion place themselves; Weather, Message, Now playing and Watchers don't. P1's reading is confirmed with two corrections. First, the Weather page does point at the Desk in prose: its note ends "Place Weather on the Desk page." (SW:2865), though there is no link. Second, the Weather piece row is not the only switch: the forecast also needs a looked-up city and the Desk on (WF6). Correction to P2's guess that "Show my CI runners' jobs" is a child of "Let agents show watchers": the two are independent (the runner service reads only `watchers_ci_runners`, `App.xaml.cs:574`). Both show nothing until Watchers is placed, under "Where each piece sits" (SW:2369).

**WF9. The meters mod has two homes, two names and two folder pickers, and nothing says how Turn off differs from Remove.** Sources: W5, M4, M5, L1 to L3, P1 (task 4), P2 (tasks 1 and 2). On Claude Code it is "Meters above the prompt", with Install…, Update… and Remove (SW:1779). On Mods & Config it is `sanduhr-meters`, with Turn off or Turn on, Update… and Remove… (SW:1980-1990). Turn off exists only on Mods. P2 also found that nothing on the Claude Code page says its Install buttons act on the "Folder" combo under "Sanduhr in Claude Code" rather than the folder cards above it (SW:1764-1770). The Mac moved every meters control to Mods & Config (`docs/settings-v2-spec.md`, Mods & Config).

**WF10. "Let Claude or agents do X" is three switches on two pages, plus a consent on each folder card.** Sources: W9, P2 (tasks 5 and 6). `watchers_agents` (SW:2365), `desk_direct` (SW:2388), `now_playing.looks_direct` (NL:65), and "Claude can read this folder's project totals (MCP)" on each folder card (SW:1745). All of them need the MCP server installed on another page. The wording drifts between "agents", "Claude" and "Claude Code" (P2). Nothing answers "what can an agent change on my machine".

**WF11. The glow has four names, and its switches are in a separate unlabelled card from its Install row.** Sources: W7, P2 (task 7). "Glow when Claude needs you" (the piece), "Glow when Claude is waiting on you" and "Glow when Claude finishes" (the switches), "Where the glow shows", and "Claude Code glow" on the Desk piece rows. The switches don't say they need the hooks installed. "Glow" also names a Message line effect, the Message look switch and a Launcher color (W7).

**WF12. "Look" is spread over four pages, and two controls are both called "Font".** Sources: W8, H (search "font" opens the Desk page top, with Look 1,342 DIP down). Desk (Font, Clock size, Back plate, Ink), Message (Look and per-line looks), Now playing (Style, Gradient bars), and the Launcher rows' Glow color.

**WF13. Per-limit warnings and hiding have no Settings home.** Source: W10. Only the tier card's menu has them (`TierCard.xaml.cs:68-84`), and its one Settings link, "Alerts settings…", lands on a page with nothing per limit. A muted or hidden limit can't be listed or restored from Settings.

**WF14. Three "history" and two "vault" names cover four different things.** Sources: W11, P3 (task 8), H (search "history" returns 4 pages). The History page (30 days of meter percentages per account), "History vault" on Claude Usage (an archive of Claude Code session logs), "Usage vault" on About (the same vault), and "History kept: Kept for 90 days" on Mods (Claude Code's own retention). There are also "Erase archive", "Clear history" and two "Export CSV…" buttons that export different data. Correction to P3, who couldn't find the vault opt-in the Sessions view points to ("enable a Claude Code home in Overview"): it exists, as one checkbox per home under "History vault" (SW:1314-1326). The harness profile produced no homes, so the list rendered empty. With nothing under the heading, the page shows a label, a 0-high list (P4: a tab stop with no name) and no empty-state line.

### Confusing: names that don't match

**WF15. The sidebar group "Widget" holds only Themes, and the page "Widgets" sits under "Desk".** Sources: W12, P1 ("backwards"). `SettingsPage.Widget` is not a page at all; it is the "show the widget" target (W12).

**WF16. The Desk switch is named after two of the things the Desk draws.** Sources: W13, P4 (polish, a long spoken name). "Desk: the clock and your Claude meters on the desktop" (SW:2325), against "Turn off the Desk" on the Desk menu and "a clock, your meters, a message and more" in the tour.

**WF17. "Show me" and every menu entry open the page top, never the section.** Sources: W15, H (deep links), P4 (task 4). All 30 What's New cards and the tour's cards carry a page only (`App.xaml.cs:285-297`, `:316-330`), though `SelectPage` takes an anchor (SWcs:222). Twelve cards point at Desk. At 800 high, `launcher`, `per-monitor`, `meter-styles`, `saved-designs`, `desk-fold` and `desk-plate` all land with their target below the fold (897 to 2,137 DIP down), and three card bodies end "in Settings, Desk", naming the page and no section. At 600 high, Claude Code's Statusline, MCP server, Meters and Glow cards also land below the fold.

**WF18. Text written for agents names pages in three styles and one section that has no heading.** Source: W14. "Settings, Desk", "Settings ▸ Claude Usage" and "Settings > Publish usage > Endpoint URL", plus "(Settings, Desk, Watchers)" for a section with no "Watchers" heading (`Mcp/ToolLogic.Watchers.cs:114`), and "Turn on 'Publish daily'" for a checkbox that reads "Publish daily at". `SettingsSourceScanTests` doesn't read Core or Mcp.

**WF19. Menu names drift between the tray, the widget menu and the icon-strip tooltips.** Source: W16. "Compact Mode" against "Compact mode (Ctrl+D)", "Deep Work (Focus)" against "Deep Work focus timer (Ctrl+P)", "Refresh" against "Refresh usage now".

**WF20. "Reset Sanduhr…" lists less than it removes, and nothing next to the button says what it removes.** Sources: W17, M30 (gap R12), P3 (task 7), P1 (task 7). The checkbox reads "Sanduhr's entries in Claude Code (statusline, MCP server)" (`ResetWindow.xaml:161`) while the step undoes every receipt, meters mod and glow hooks included (`Sanduhr.Core/ClaudeCodeIntegrations.cs:1017`). There is no choice per folder. P3 had to guess the parts from scattered notes on About and Widgets.

**WF21. The core words are never defined, and the same thing gets two names.** Sources: P1, P2. "Desk", "widget", "Desk companion", "watchers", "pieces" and "glow" have no definition anywhere a newcomer looks. "Folder" on Claude Code is "Home" on Publish usage and Claude Usage ("Homes to share"); "Meters above the prompt" is `sanduhr-meters`, "mod" and "Plugins" on Mods.

### Confusing: reach and layout

**WF22. The Desk page is 14 sections with no way to reach a section except search.** Sources: W3, W19, H item 1, P1, P2, P4 (blocker 2, rated confusing here because search does land on every anchor). At 800 high the first screen ends after OLED care, "Where each piece sits" starts 1,147 DIP down and "Layout map" 1,793 DIP down. At 600 high it takes six screens. The page has no headings exposed as stops and no jump links; keyboard users reach "Font" after 26 Tab stops (P4 task 5). Four sections are features of their own, not layout (Watchers, Direct design, Designs, Launcher). The map, the page's only preview, is in the 10th section.

**WF23. Search misses on common words, and the results don't say which hits are below the fold.** Sources: H item 6 and the search table, P1, P2, P3, P4. "font" opens the Desk page top, not Look. "update" opens Accounts (synonym "update sign-in"), not About, Version. "docker" opens "Let agents show watchers", not the CI runners switch that names Docker. "demo" returns nothing, and "privacy" returns only the About page row. Every anchored hit does land (`found: ok` throughout). Corrections against `search.txt`:
- P4 says "meters" ranks Mods & Config 8th. It is the last of three page groups: the page row is 7th of 8 rows, and Plugins 8th.
- P4 says "shortcut" lists Launcher 4th. The Desk page row is 4th and Launcher 5th.
- P1 says "reset" lists "Alerts / Reset notifications" second. The second group is the Alerts page row and its "Alerts" section, matched by synonym.
- P4 says the "glow" results don't include Message's "Your lines" and "Message look". They are rows 5 and 6.

**WF24. The widget's own options have no Settings page, so they have no search hit and no link.** Source: W18. Compact, graph style, pin, focus length, tier order, "Don't show after updates", and updates (WF6).

**WF25. There is no way into a page from outside the app, and Settings always reopens on General.** Sources: W20, W21. No URL scheme, switch or smoke hook opens a page or anchor. The window is rebuilt on every open (`App.xaml.cs:665-735`), and two comments claim otherwise (`SettingsNames.cs:80`, `WidgetViewModel.cs:311`).

**WF26. At the minimum width the piece rows clip the Monitor combo, and no scroll bar appears.** Sources: H item 8, P1, P2, P4. At 680 wide, "Message, monitor" spans x 581 to 727 in a page host that ends at 663, so 64 DIP are cut ("All moni"). The Style combo wraps under Place, and "System (CPU, RAM, …" is truncated even at 780 (`09-desk/w680-h800/page-section-03.png`). The scroller is vertical only.

### Confusing: accounts, privacy and folders

**WF27. The per-folder choices are made on four pages, and linking a folder shows nothing until a piece is installed.** Sources: W24, P2 (task 8), P3 (task 2). The account link and MCP read are on the Claude Code cards, vault consent is on Claude Usage, share consent on Publish usage, and the install and view targets are two more pickers. The Accounts page lists no folders. P3 linked the work folder and saw "Statusline … Not installed": the caption "The statusline in this folder shows that account's meters." (SW:1742) is true only after an install the card doesn't mention. See WF2 for the link itself not surviving a revisit.

**WF28. Publish usage can't show what it sends, and "folder names only" means project names leave the PC.** Sources: P2 (blocker on task 6, rated confusing here because the page does state the fields), P3 (polish), P4 (an empty tab stop). The page says "token counts per project name (folder names only, never paths)" (SW:1553) and offers "Publish now" with no sample of the record. Claude Code folder names are project names, so that is what leaves. "Homes to share (off until you tick them)" rendered empty in the harness profile. That is the same home list as WF14, so a real PC fills it (Tier 2), but when it is empty there is a heading with nothing under it.

**WF29. Signing out is called "Remove account…", and what it deletes is said only in its own confirmation and on About.** Source: P3 (task 6). Correction to P3, who didn't know whether Remove deletes the history file: it does, and the confirmation says so: "This deletes its saved sign-in from the Windows Credential Manager and its usage-history file (history.<account>.json). Cannot be undone." (`AccountsViewModel.cs:173-177`). P3 also found the account buttons dim until a row is picked with no hint, and that "A manual switch pauses it" under "Follow the account I'm using" doesn't say for how long.

**WF30. The first account is named "Personal" without asking.** Source: W22. `SignInCoordinator.cs:198`, `:237`; "Rename…" exists after the fact. A work account signed in first is labelled Personal.

**WF31. The manual key help is developer shorthand, Chrome only.** Source: W26. "open DevTools (F12) → Application → Cookies → claude.ai, then copy the value of the sessionKey cookie" (`Views/ManualKeyWindow.xaml:47`), and "cf_clearance" goes unexplained.

### Confusing: the signed-out first run

**WF32. Signed out, three pages tell things that aren't true or offer what doesn't work.** Sources: W27, H item 5. History says "No history yet. Data accumulates as you use Sanduhr." (`HistoryChart.cs:82`), which can't happen without an account. "Linked account" is enabled with "None" as its only choice (`LinkEnabled = !IsDemo`, `ClaudeCodeViewModel.cs:341`). Just the Desk is a mode with no Settings control, and the only way out is signing in. The sign-in reminder switch, a Desk behavior, is on Alerts (SW:1079).

### Keyboard and screen reader

**WF33. Quit Sanduhr is the first Tab stop after every page, and it quits without asking (confusing).** Source: P4 (task 5, finding 4). The button has `KeyboardNavigation.TabIndex="1"` at the sidebar foot (SW:691-694), and its command raises Quit at once (`ViewModels/SettingsViewModel.cs:173`). One Tab past the last margin slider and an Enter closes the widget and the Desk. Getting back to the page list takes four stops (Quit, close, search, sidebar).

**WF34. Visible labels and spoken names differ (confusing).** Source: P4 (finding 5). This hurts voice control, which matches the visible words. "Preset", "Endpoint URL" and "Auth" are spoken "Publish preset", "Publish endpoint URL" and "Publish auth scheme". "Set token…" is "Set publish token", "Add folder…" is "Add a Claude Code folder", "Forget folder" is "Forget <folder>", "Reset Sanduhr…" is "Reset Sanduhr, remove what Sanduhr keeps on this PC", and the Ink combo captioned "Clock, date, Claude" is spoken "Ink".

**WF35. Empty lists are 0-high tab stops with no name, and some fields have no label at all (confusing).** Sources: P4 (findings 6 and 7), P1. Focus lands on nothing and nothing is spoken: "Homes to share" on Publish usage (step 5), Claude Usage Overview and Trends (step 7), History (step 4). On Themes, the theme-name and slug text boxes and two item lists are unnamed (`08-themes/taborder.txt` steps 4, 5, 22, 23). The shortcut's on/off checkbox on General has no visible text (SW:862-864); it has an automation name, but sighted users see a bare box next to the key button (P1).

**WF36. The Alerts controls don't dim when "Enable alerts" is off (confusing).** Sources: H item 4, P1, P2, P3, P4. With the master off, the thresholds, five switches and "Send test alert" stay enabled, tab-stoppable and full strength (`probe/alerts-enable-off.png`). Nothing says whether they still act.

**WF37. Body text fails contrast in every built-in theme (polish, but a WCAG AA failure).** Sources: H item 7, P2, P4. TextDim is 3.1 to 3.9 on the page in five of six themes, and TextMuted is 1.8 to 3.1 in all of them (`contrast.txt`). Obsidian's accent as text is 4.0 on the page and 4.2 on the sidebar, and the selected sidebar label is drawn in it. On a short window, the small captions are what P4 reads most.

**WF38. Focus is hard to see on pills and link buttons, and several names read badly (polish).** Sources: H item 7, P4 (findings 11 to 13, 15, 16, 18). The selected History pill looks the same focused and unfocused, and a link-style button shows only an underline. The close button is spoken "✕". "Sign-in reminders" is spoken twice (heading and checkbox). The accounts list is "AccountsList", and the Claude Usage lists have no name. "100% plays the ! (you know the one)" reads badly aloud. A disabled combo (demo mode's Linked account) is drawn the same as an enabled one (P2, P3, P4).

### Mods and widgets

The owner's model for 4.0 is one list for every mod and pack, with the catalog as a collapsible section. The mods parity audit (§4) sets out what that needs from the data: one installed-item record across kinds, state per scope (a Claude Code mod is on or off per folder; a widget pack is app-wide), a flag per row for whether Sanduhr can switch it, and catalog and installed merged by id. WF3 and WF4 above are the blockers inside this group.

**WF39. Mods & Config lists every plugin and can switch exactly one, and its framing says it switches none.** Sources: W6, M11, M12, L4, L6, P1, P2. Only Sanduhr's own row has buttons (`ModsConfigViewModel.cs:81`, SW:1978-1979). The page opens with "What one Claude Code folder holds, read from its files. Sanduhr runs nothing here", then shows Turn off, Update… and Remove… on one row (P1: "contradictory tone"). "Plugins (1 installed, 1 on) … The list differs because 1 has no switch in settings.json" is jargon (P1). The Mac can't switch other mods either (M12); a generic switch needs the CLI route neither platform has (mods-parity R1, R6). The Windows decision is whether "Mods" means a list you can act on, and if so the first slice is kept mods (R1).

**WF40. The catalog and the installed packs are two lists with different buttons, and Themes doesn't point to either.** Sources: W1, M31, M32, P1 (blocker on task 2, rated confusing here because search "theme" finds "Widget catalog" through its synonym "theme pack"). The catalog row can Install or Update; the installed row can Uninstall or "Use my own art…" (WP:57-61, 87-97). The companion controls are a third block, shown only after a sprite is installed (WP:119). The Themes page has no link to the catalog, though theme packs land in its list (WF3). In the harness the catalog was offline, and the failure state ("Sanduhr couldn't reach the widget catalog. Check your connection and try again.") gives a newcomer no idea what a pack is.

### Polish

**WF41. Short windows clip less than feared, but the window can still overrun a small screen.** Sources: W2, H item 2, P4 (finding 14). Correction to W2: at the 520 MinHeight, Alerts fits (content ends at about 373 DIP), and Publish usage loses only the second line of its last note. Quit stays pinned at the sidebar foot at every height, and the sidebar list scrolls instead. Still open: the window is 800 DIP high with `CenterScreen` and no work-area clamp, so on a 1366x768 screen or 1080p at 150% its foot may sit off screen (Tier 2).

**WF42. "Install again to update it." sits beside a button labelled "Update…".** Sources: P1, P2. Claude Code, "Meters above the prompt", when an older copy is installed.

**WF43. Small copy problems.** Sources: P1, P3, P4. Alerts is the one page with no title (SW:1013-1020). "Warn at" and "Urgent at" show no % sign (the spoken names say "percent"). "100% plays the ! (you know the one)" is an inside joke. On Themes, "Looks good" floats with no subject and "Save as: .json" doesn't say what it saves. About's paths are cut with "…" (full text only in the tooltip), and "Show in Explorer" for the vault is greyed with no reason. The sidebar group "Claude Code" repeats its page's name, and the page's second heading is "Sanduhr in Claude Code" (P2).

**WF44. The trademark notice appears six times.** Sources: W28, P3. It is required wherever Claude is named (Store test 10.1.4.4): keep the text, make it one shared footer.

**WF45. The `suggestion` anchor on Message isn't in the search index.** Source: W29.

**WF46. Colors are hex-only on four controls.** Source: W30. Ink, Message custom color, the Launcher glow and the Studio rows.

**WF47. Code comments describe pages and tabs that no longer exist.** Source: W31. Not user-facing.

**WF48. The Message mode button has two labels, and its ellipsis opens no window.** Source: W32.

**WF49. The theme gallery is capped at 112 px and the installed themes at 72 px, on a page with room.** Source: W33.

### What already works, and stays

The inventory's keep list (§4) stands, and the personas confirmed several entries. Search lands every anchored hit and moves focus (H). Tab order follows visual order with no traps (P4). Per-row spoken names include the row ("Linked account for <folder>", "Message, place") (P4). Arrange Desk explains itself when the Desk is off (P1, P4). Remove account… states exactly what it deletes (WF29). Weather discloses its two calls in plain words and offers "Show it as" to hide the city (P3). The Claude Code page writes nothing until you install and confirm.

## Feeds for streamer mode

Source: P3's per-page notes, checked against the inventory's page map. "Reach" is where a hide-identity switch has to act for the page to be safe on a shared screen.

| Page | What identifies you | Reach |
| --- | --- | --- |
| Accounts | Account labels; with real sign-in, possibly an email or org | Account list rows, the tray and widget account menus (same `SettingsNames` strings), the tray tooltip, which carries the active account's label (`App.xaml.cs:892-893`) |
| History | Account names in the legend and the "Account:" picker; Export CSV… | Chart legend, picker items, the export |
| Claude Usage (Overview, Trends, Sessions) | Top projects, top skills, the Sessions table by project, Stack by project, Export CSV…, home names on the vault boxes. Highest risk | Every project and skill name, the calendar's day view, the export, the vault home labels |
| Publish usage | Endpoint URL, token state, home names | Endpoint field, "Homes to share" labels |
| Claude Code | Folder paths (profile folder shortened to `~`, hand-added folders in full), linked account names | Folder cards, both "Folder" pickers, consent dialogs, spoken names ("Forget <folder>") |
| Mods & Config | Folder picker, plugin and marketplace names, MCP server names, CLAUDE.md status | Whole page below the picker |
| About | Full file paths with the Windows user name, the Credential Manager target, profile folder | "Where settings live" rows and their tooltips |
| Weather | City name and coordinates | The City field and Places found. "Show it as" already hides the name on the Desk |
| Now playing | Song titles, artists, apps seen playing | The Apps list here, the widget line and the Desk piece |
| Desk, Message | Typed message lines, watcher cards (CI job titles), CI runners' jobs | Message editor, the Desk pieces themselves, the suggestion card |
| Widgets | Packs "made here" (names chosen by the user or Claude) | Installed rows |
| General, Alerts, Themes | Low: tray number and limit names | None needed |

Outside Settings, the same switch has to reach the widget's account label, the Desk (message, watchers, now playing, weather), the tray tooltip, toasts, and the statusline's account segment in each Claude Code folder (`Sanduhr.Core/StatuslineInstaller.cs:54`). Demo mode is not this switch: it swaps in sample data and starts only from the command line (WF7).

## Needs the live pass (Tier 2)

1. **WF2, the link drop.** Link a folder to an account, open another page, reopen Claude Code: does the link survive, and does "Couldn't save that link." appear on the second open? Check `account_folders` in settings.json before and after.
2. **WF1, Clear history.** With two accounts, pick the non-active one in "Account:" and press Clear history: confirm that the active account's file is emptied and the picked one is untouched.
3. **WF3, packs in use.** Install a theme, palette, font and effects pack from the live catalog. Confirm each appears in the Themes list, Ink, Custom, Colors and the Desk and Message font lists; that effects changes nothing; and what "Installed." looks like on the merged row.
4. **WF5, keyboard Arrange.** With Narrator on, try to reorder two pieces in one corner without a mouse; confirm there is no route. Listen to the Desk page from top to the piece rows.
5. **WF41 and H items 1 and 2, real screens.** A 1366x768 screen at 100%, and 1080p at 125% and 150%: whether the 800 DIP window overruns the work area, where Quit sits, and where the piece rows and Layout map fall in pixels.
6. **WF6, network.** With Weather placed but no city, and with a city but the piece Hidden: confirm no forecast call. Open the Widgets page offline and online. Confirm the 24-hour update probe on a GitHub install and its absence on the Store build.
7. **WF32, signed out.** The History text, the Claude Code page's "Linked account" with only "None", and the Just the Desk tray.
8. **WF14 and WF28, real homes.** The "History vault" and "Homes to share" lists on a real profile, and how each looks when there is no home.
9. **WF33, Quit by keyboard.** Tab past the last control on a page and press Enter: confirm the app quits without a prompt.
10. **WF37 and WF38.** Windows high-contrast themes and the real Mica backdrop, which the contrast table doesn't composite.
11. **WF17, Show me on a real screen.** Each Desk card's landing at 125% and 150%.
12. **The rest of the live-only list from the harness index:** the tray menu and its labels (WF19), toasts and "Send test alert", the Claude Usage calendar day click, a populated History and vault, real watcher and CI runner cards, and the Store build's startup note.
