# Windows Settings inventory, for Settings v2 for Windows (4.0)

Audit of 2026-10-24, against 3.9.0 on branch `docs/settings-v2-windows`. Code-only: nothing was run or launched, so every statement about layout is a statement about XAML, not about pixels. Where a live look is needed, the item is listed in "Open items for the live pass" at the end. The Mac method is `docs/settings-v2-spec.md` (Verdict, Findings, IA).

File abbreviations (all under `windows-dotnet/src/`):

| Short | Path |
| --- | --- |
| SW | `Sanduhr.App/Views/SettingsWindow.xaml` (3,109 lines) |
| SWcs | `Sanduhr.App/Views/SettingsWindow.xaml.cs` |
| MP | `Sanduhr.App/Views/DeskMessagePage.xaml` |
| WP | `Sanduhr.App/Views/WidgetsPage.xaml` |
| DP | `Sanduhr.App/Views/DeskPiecesPanel.xaml` |
| DS | `Sanduhr.App/Views/DesignsSettingsPanel.xaml` |
| OC | `Sanduhr.App/Views/OledCarePanel.xaml` |
| NL | `Sanduhr.App/Views/NowPlayingLooksPanel.xaml` |
| SN / SI | `Sanduhr.Core/SettingsNames.cs` / `SettingsIndex.cs` |
| WN | `Sanduhr.Core/WhatsNew.cs`, `WhatsNewCards.cs`, `WelcomeTour.cs` |
| App | `Sanduhr.App/App.xaml.cs` |

Counting rule for "controls": every CheckBox, ComboBox, Button, TextBox, Slider, RadioButton, ListBox, ToggleButton and Hyperlink declared in the XAML of a page, with a row template counted once. Pages with per-row templates (Desk pieces, Launcher buttons, Message lines, pack rows, folder cards) have more at run time.

## Verdict: Windows Settings grew by adding a section to whichever page was nearest, and the Desk page took most of it

The sidebar is 15 pages in 6 groups (SN:24-32). The `SettingsPage` enum has 16 values because `Widget` is not a page: it means "show the widget" and is the Show me target for cards about the widget itself (WN WhatsNew.cs:7, SWcs:222-226). The pages hold 211 declared controls and 68 anchors (68 `SettingsAnchor.Id` marks in XAML; 67 in `SettingsIndex` plus `quit`, which lives on the sidebar foot; `suggestion` on Message is the one mark the index lacks).

Windows already did a good share of the Mac's v2 work: a names table with tests, a sidebar search over 67 anchors with synonyms, anchors that scroll and flash for 1.5 s, an in-app Reset Sanduhr…, a Claude Code page with per-folder consent. What is left is the same disease the Mac had, plus four Windows-only growths:

1. **The Desk page is 14 anchored sections and 37 declared controls** (plus 36 piece pickers at run time: Place, Size, Monitor on 11 rows, Style on 3). Its only preview, the layout map, is the 10th section, below Launcher, Designs and three agent switches.
2. **A piece's switch and its placement live on different pages**, and the default layout places only the clock and the Claude line (`DeskArrangement.Standard = "clock:bl claude:bl"`, Core DeskArrangement.cs:288). Turning on Weather, Message, Now playing or Watchers shows nothing until the piece is placed on the Desk page.
3. **The Widget has no Settings page.** Compact, graph style, pin, focus length, hidden limits, per-limit warnings and the theme popup live only on the widget and its menus. The "Widget" sidebar group holds one page (Themes), and the page called "Widgets" sits in the "Desk" group.
4. **Widgets and Mods are lists you can read but mostly cannot switch.** Of five pack kinds the catalog installs, only sprites have a consumer in the app code. Of the plugins listed on Mods & Config, only Sanduhr's own mod has a switch.

---

## 1. The page map

### 1.1 Sidebar order

| # | Group | Page (sidebar title) | Enum | Anchors | Controls | Scroller | Lines |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | (none) | General | General | 3 | 6 | yes | SW:779-894 |
| 2 | (none) | Accounts | Accounts | 2 | 7 | list scrolls | SW:897-1010 |
| 3 | (none) | Alerts | Alerts | 5 | 10 | **none** | SW:1013-1088 |
| 4 | Usage | History | History | 2 | 5 | chart fills | SW:1091-1162 |
| 5 | Usage | Claude Usage | ClaudeUsage | 4 | 22 | per view | SW:1169-1540 |
| 6 | Usage | Publish usage | Publish | 4 | 10 | **none** | SW:1546-1680 |
| 7 | Claude Code | Claude Code | ClaudeCode | 6 | 15 | yes | SW:1684-1842 |
| 8 | Claude Code | Mods & Config | Mods | 5 | 6 | yes | SW:1845-2047 |
| 9 | Widget | Themes | Themes | 4 | 20 | editor row only | SW:2050-2308 |
| 10 | Desk | Desk | Desk | 14 | 37 | yes | SW:2312-2734 |
| 11 | Desk | Message | DeskMessage | 5 | 40 | yes | MP:1-442, hosted SW:2737 |
| 12 | Desk | Now playing | NowPlaying | 4 | 10 | yes | SW:2741-2805, NL:1-79 |
| 13 | Desk | Weather | Weather | 2 | 8 | yes | SW:2807-2880 |
| 14 | Desk | Widgets | Widgets | 3 | 7 | yes | WP:1-191, hosted SW:2883 |
| 15 | Help | About | About | 5 | 8 | yes | SW:2889-3105 |
| | | **Total** | | **68** | **211** | | |

Plus the sidebar's own search box (SW:699) and Quit Sanduhr button (SW:691), not counted. Group headings are set as literals on five TabItems (SW:1091, 1685, 2050, 2312, 2889) and again in `SettingsNames.Sidebar`, so two sources of truth for the grouping.

The window is 800 x 780, MinHeight 520, MinWidth 680, `CenterScreen`, no fit-to-screen code (SW:16-18; grep for `WorkArea` finds nothing in the Settings window or its code-behind). It is rebuilt on every open (App:665-735), so it always opens on General unless a page is passed.

Per-control tables follow. "Writes" is the settings.json key when the store shows it, else the view-model property. "Hidden" marks a control that is not shown or not enabled until something else is set.

### 1.2 General (SW:779-894)

| Label | Kind | Writes | At | Note |
| --- | --- | --- | --- | --- |
| Tray preview (icon + "No numbers yet: the tray shows --" or "Session 42%") | display | `General.TrayPreviewImage/Text` | SW:793-803 | 32 px, above the picker |
| Tray shows | ComboBox, 5 options | `tray_mode` (SettingsStore.cs:307-319) | SW:805-811 | Same name as the tray submenu (App:886) |
| Start Sanduhr when Windows starts | CheckBox | HKCU Run entry via `StartupManager` | SW:826-829 | Hidden on a Store install |
| Open Windows Startup settings… | Button | opens `ms-settings:startupapps` | SW:837-839 | Hidden unless Store install; caption at SW:834-836 |
| (keys, e.g. "Ctrl+Alt+S") | Button, records a combo | `hotkey_settings` | SW:856-861 | Label text from `HotKeys.Label` |
| (shortcut switch, named "Shortcut to open Settings") | CheckBox, no visible text | `hotkey_settings.on` | SW:862-864 | Off by default |
| (reset link, text from `Shortcuts.ResetText`) | Button styled as link | back to default combo | SW:866-882 | Hidden unless the combo changed |
| Quit Sanduhr | Button, sidebar foot | tray's quit | SW:691-697 | On every page, not in General |

### 1.3 Accounts (SW:897-1010)

| Label | Kind | Writes | At | Note |
| --- | --- | --- | --- | --- |
| Follow the account I'm using | CheckBox | `follow_account` | SW:916-919 | |
| (account list; row shows "●", label, "active") | ListBox | `Accounts.SelectedAccount` | SW:930-981 | Empty hint SW:984: `No accounts yet. Click "Add account…" to sign in.` |
| Switch to | Button | active account | SW:993 | |
| Rename… | Button | account label | SW:995 | |
| Update sign-in… | Button | re-auth | SW:997 | |
| Remove account… | Button | deletes credential and history file | SW:999 | Bound to `SignOutCommand` |
| Add account… | Button | sign-in flow | SW:1005 | First embedded sign-in names the account "Personal" with no prompt (Services/SignInCoordinator.cs:229-238) |

### 1.4 Alerts (SW:1013-1088)

| Label | Kind | Writes | At | Note |
| --- | --- | --- | --- | --- |
| Enable alerts | CheckBox | `alerts_enabled` | SW:1020 | Sub-controls have no `IsEnabled` binding to it (VM not checked) |
| Warn at | TextBox (percent) | `alert_warn_pct` | SW:1031-1039 | Global, not per limit |
| Urgent at | TextBox (percent) | `alert_urgent_pct` | SW:1040-1048 | |
| Projection alerts (on pace to hit the cap before reset) | CheckBox | `alert_projection` | SW:1056 | |
| Reset notifications | CheckBox | `alert_reset` | SW:1059 | |
| Sound | CheckBox | `alert_sound` | SW:1062 | |
| Chime for model-scoped weekly tiers | CheckBox | `alert_sound_scoped` | SW:1064 | |
| 100% plays the ! (you know the one) | CheckBox | `alert_snake_full` | SW:1067 | |
| Send test alert | Button | none | SW:1071 | |
| Sign-in reminders on the Desk | CheckBox | `desk_sign_in_reminder` | SW:1079-1082 | A Desk setting on the Alerts page; caption `SignInReminder.Caption` |

### 1.5 History (SW:1091-1162)

| Label | Kind | Writes | At | Note |
| --- | --- | --- | --- | --- |
| Week / Month | 2 Buttons | `History.WeekMode` (view only) | SW:1111-1114 | |
| Account: (picker, includes "All accounts") | ComboBox | view only | SW:1117-1122 | |
| (chart) | display | | SW:1146-1150 | Empty text "No history yet.\nData accumulates as you use Sanduhr." (HistoryChart.cs:82) |
| Export CSV… | Button | file | SW:1154 | Same label as Sessions' export (SW:1414) |
| Clear history | Button | `History.ClearAll()` (all accounts) | SW:1156 | Sits on the row under the Account picker but clears every account (HistoryTabViewModel.cs:216-237) |

### 1.6 Claude Usage (SW:1169-1540), three views behind pills

| View / label | Kind | Writes | At | Note |
| --- | --- | --- | --- | --- |
| Overview / Trends / Sessions | 3 pill Buttons | `ClaudeCode.Section` | SW:1179-1184 | Anchors carry a `Subsection` |
| Overview: Today, Last 30 days, bar strip, calendar | display | | SW:1210-1234 | Calendar day click jumps to Sessions for that day |
| Show project & skill breakdown | CheckBox | `local_cc_show_breakdowns` | SW:1236 | Gates the two tables (SW:1241) |
| History vault: (one checkbox per Claude Code home) | CheckBox per row | `vault_roots` | SW:1324-1326 | Anchor `vault` SW:1314 |
| Erase archive | Button | deletes vault | SW:1331 | Global to the vault |
| Open vault folder | Button | | SW:1334 | |
| Trends: 4 weeks / 12 weeks / 26 weeks | 3 pills | view only | SW:1355-1360 | |
| Sessions: Today / Yesterday / 7 days / All, day chip | 5 chips | view only | SW:1420-1431 | |
| Stack by project | CheckBox | `ledger_group_by_project` | SW:1415 | |
| Export CSV… | Button | file | SW:1413 | |
| Sort headers (last active, project, tokens) | 3 Buttons | view only | SW:1443-1453 | |

### 1.7 Publish usage (SW:1546-1680)

| Label | Kind | Writes | At | Note |
| --- | --- | --- | --- | --- |
| Preset | ComboBox | `publish.preset` | SW:1559 | |
| Endpoint URL | TextBox | `publish.endpoint_url` | SW:1575 | |
| Auth | ComboBox | `publish.auth_scheme` | SW:1588 | |
| Header name | TextBox | `publish.auth_header_name` | SW:1599 | Hidden unless Auth names a header |
| Set token… | Button | Credential Manager `publish:token` | SW:1609 | Token never in settings.json |
| Clear token | Button | | SW:1613 | Hidden until a token is stored |
| Homes to share (off until you tick them): one box per home | CheckBox per row | `publish.roots` | SW:1627-1643 | Same home names as the vault boxes on Claude Usage |
| Publish daily at | CheckBox (master) | `publish.enabled` | SW:1646 | |
| (time, 24-hour) | TextBox | `publish.time` | SW:1651 | |
| Publish now | Button | | SW:1667 | |

### 1.8 Claude Code (SW:1684-1842)

| Label | Kind | Writes | At | Note |
| --- | --- | --- | --- | --- |
| Folders: one card per Claude Code folder | ItemsControl | | SW:1705-1758 | Anchor `folders` |
| Forget folder | Button per card | removes added folder | SW:1726 | Only for folders added by hand |
| Linked account | ComboBox per card | `account_folders` (name from SettingsStore; mapping inferred) | SW:1736 | Plain-language line SW:1742; the only place a folder is tied to an account |
| Claude can read this folder's project totals (MCP) | CheckBox per card | `mcp_roots` (inferred) | SW:1745 | Hidden for hand-added folders (SW:1749-1753) |
| Add folder… | Button | `cc_folders_added` | SW:1759 | Sits under the list, above "Sanduhr in Claude Code" |
| Folder (to install into) | ComboBox | `ClaudeCodePage.SelectedFolder` | SW:1769 | A second folder picker on the same page |
| Statusline: Install… / Change… / Remove | piece card | folder `settings.json` + receipt | SW:1775, template SW:259-288 | |
| MCP server: Install… / Update… / Remove | piece card | folder `.claude.json` + receipt | SW:1777 | |
| Meters above the prompt: Install… / Update… / Remove | piece card | `env.CLAUDE_CODE_PLUGIN_DIRS` + `enabledPlugins` | SW:1779 | Same mod as the Mods row below |
| Glow when Claude needs you: Install… / Update… / Remove | piece card | two hooks | SW:1782 | |
| Glow when Claude is waiting on you | CheckBox | `claude_code_glow.waiting` | SW:1787 | |
| Glow when Claude finishes | CheckBox | `claude_code_glow.done` | SW:1790 | |
| Not while a terminal or editor is in front | CheckBox | `claude_code_glow.skip_terminal` | SW:1793 | |
| Try it: waiting / Try it: done | 2 Buttons | | SW:1797-1800 | |
| Where the glow shows | ComboBox, 4 options | `claude_code_glow.target` | SW:1807 | Choosing "On a spot on the Desk" auto-places the Desk piece (ClaudeCodeViewModel.cs:270-291) |
| (edge side, monitor) | 2 ComboBoxes | `edge_side`, `edge_monitor` | SW:1813-1817 | Hidden unless target is "Along an edge of a screen" |

### 1.9 Mods & Config (SW:1845-2047)

| Label | Kind | Writes | At | Note |
| --- | --- | --- | --- | --- |
| Folder | ComboBox | session-only `s_sessionFolder` | SW:1860 | A third folder picker; state kept apart from Claude Code's |
| Refresh | Button | | SW:1864 | |
| Settings and statusline (read-only summary: Settings, Statusline, Hooks, Plugin switches) | text | | SW:1870-1940 | Repeats what the Claude Code page installs |
| Plugins (N installed, M on) | list | | SW:1943-1997 | Rows are read-only except Sanduhr's own |
| Turn off / Turn on | Button, Sanduhr row only | `enabledPlugins` | SW:1980-1985 | `IsSanduhr` gate: SW:1979, ModsConfigViewModel.cs:81 |
| Update… / Remove… | Buttons, Sanduhr row only | mod files + settings.json | SW:1986-1990 | |
| MCP servers | read-only list | | SW:2000-2015 | |
| CLAUDE.md | read-only line | | SW:2018 | |
| History kept | read-only line | `cleanupPeriodDays` (read) | SW:2023-2038 | A fourth "history" |

### 1.10 Themes (SW:2050-2308)

| Label | Kind | Writes | At | Note |
| --- | --- | --- | --- | --- |
| Choose a theme (swatch grid, capped at 112 px high) | ListBox | `theme` | SW:2070-2084 | Mirrors the widget's theme popup (MainWindow.xaml:247-263) |
| Studio: edit the tokens, watch the widget re-tint live | CheckBox | `themes_studio` | SW:2100 | Switches the editor between Paste and Studio |
| Paste mode: JSON box, Filename, Save & apply, Copy agent prompt, Open themes folder | TextBoxes, 3 Buttons | themes folder | SW:2105-2158 | |
| Studio mode: Start from, Name, 2-column hex rows, Glass alpha, Border alpha, Save as, Save & apply, Revert, Copy JSON | ComboBox, TextBoxes, 2 Sliders, 3 Buttons | themes folder | SW:2161-2272 | Hex text only, no color wells |
| Installed user themes: Apply / Reload / Delete | ListBox + 3 Buttons | | SW:2276-2305 | List fixed at 72 px |

### 1.11 Desk (SW:2312-2734), 14 anchored sections in this order

| # | Section / label | Kind | Writes | At | Note |
| --- | --- | --- | --- | --- | --- |
| 1 | Desk: the clock and your Claude meters on the desktop | CheckBox | `desk_enabled` | SW:2324-2327 | Label is SN:125; the Desk now also draws message, now playing, weather and more |
| 2 | Arrange Desk… | Button | starts Arrange mode | SW:2336-2340 | Disabled with a reason when the Desk is off (DeskArrangeCopy.SettingsDeskOff); note names Return and Escape |
| 3 | Clock, message and Claude line take clicks (`DeskPieceClicks.Title`) | CheckBox | `desk_pieces_take_clicks` | SW:2350-2354 | |
| 4 | Let agents show watchers | CheckBox | `watchers_agents` | SW:2363-2366 | No "Watchers" heading; MCP text points here as "(Settings, Desk, Watchers)" (Mcp/ToolLogic.Watchers.cs:114) |
| 4 | Show my CI runners' jobs | CheckBox | `watchers_ci_runners` | SW:2370-2373 | Live status line SW:2377 |
| 5 | Let Claude change the Desk directly | CheckBox | `desk_direct` | SW:2386-2389 | |
| 6 | (OLED care: switch from `OledCare.Title`) | CheckBox | `desk_oled_care` | OC:9-13 | |
| 7 | Designs: Put my Desk back; per row Switch to, Rename, Pin, Delete | 1 + 4 Buttons | `desk_put_back` + designs folder | DS:19-23, 59-78 | Up to 10 rows |
| 8 | Launcher: Add app…, Add link…, "Buttons in a" (row or column) | 2 Buttons, ComboBox | `desk_launcher` | SW:2412-2433 | |
| 8 | Launcher rows: Move up, Move down, Remove, Label, Glow color, Change image…, use-default-image | per row, 7 | `desk_launcher` | SW:2496-2547 | Up to 8 rows; hex-only glow color |
| 9 | Where each piece sits: 11 rows | Place, Size, Monitor ComboBoxes per row; Style on 3 rows | `desk_layout` | DP:36-55, 10-16 | Pieces: Message, Clock and date, Claude meters (line), Claude meters (bars), Now playing, System (CPU, RAM, disk), Weather, Watchers, Launcher, Claude Code glow, Companion (DeskSettingsViewModel.cs:165-176) |
| 10 | Layout map | display | | SW:2572-2578 | The only preview on the page; 10th section |
| 11 | Look: Font; Clock size | ComboBox; Slider | `desk_font`, `desk_time_size` | SW:2591-2612 | |
| 12 | Back plate (`DeskPlate.Title`) | CheckBox | `desk_plate` | SW:2619-2622 | |
| 13 | Ink: "Clock, date, Claude"; Colors preset; hex; Drop shadow under the text | ComboBox, ComboBox, TextBox, CheckBox | `desk_ink` | SW:2641, 2655, 2659, 2663 | Preset and hex hidden unless Custom (SW:2646) |
| 14 | Margins: Left, Right, Top, Bottom | 4 Sliders | `desk_margins` | SW:2688-2719 | |
| | Trademark notice | text | | SW:2728 | |

### 1.12 Message (MP, hosted at SW:2737)

| Label | Kind | Writes | At | Note |
| --- | --- | --- | --- | --- |
| Claude's suggestion: Add / Review / Dismiss | 3 Buttons | `messages.txt` | MP:233-242 | Only while a suggestion waits; anchor `suggestion` is not in the search index |
| Add line | Button | | MP:267 | At the top of "Your lines" |
| Edit as text… / Back to the list | Button (mode toggle) | view only | MP:270; text from DeskMessagePageViewModel.cs:593 | The ellipsis opens no window |
| Save, Revert, Reload | Buttons | `messages.txt` | MP:274, 276, 286 | Reload only if the file changed |
| Per line: When (+ Month, Day), Text, Up, Down, Remove | ComboBoxes, TextBox, 3 Buttons | `messages.txt` | MP:90-118 | Month/Day hidden unless a date |
| Per line: Color (+ hex), Glow, Size, Letters, Motion | 5 ComboBoxes, TextBox | `messages.txt` tags | MP:125-158 | "Look" is not a visible name for this group |
| Rotation: Daily / Hourly | 2 Radios | `desk_message.rotate` | MP:317-320 | |
| Mix every-day lines in on days with their own line | CheckBox | `desk_message.mix` | MP:322 | |
| Pin a line (+ Clear) | TextBox, Button | `desk_message.pin` | MP:334-340 | Pin is a text field here, not a row control |
| On special days: Stack / Take turns / Scroll | 3 Radios | `desk_message.special` | MP:353-360 | |
| Each line shows for | ComboBox | `desk_message.seconds` | MP:367-371 | Disabled unless `SecondsApply` |
| Look: Message size | Slider | `desk_message.size` | MP:386-394 | |
| Look: Color (+ custom hex) | ComboBox, TextBox | `desk_message.color` | MP:402-412 | Hex hidden unless Custom |
| Look: Glow around the message | CheckBox | `desk_message.glow` | MP:418 | |
| Look: Font | ComboBox (automation name "Message font") | `desk_message.font` | MP:426-431 | A second "Font" next to the Desk page's |

### 1.13 Now playing (SW:2741-2805, NL)

| Label | Kind | Writes | At | Note |
| --- | --- | --- | --- | --- |
| Show what's playing in the widget | CheckBox | `now_playing.widget` | SW:2751 | |
| Keep showing while paused | CheckBox | `now_playing.keep_paused` | SW:2754 | |
| Apps: one box per app seen | CheckBox per row | `now_playing.excluded` | SW:2767-2775 | List fills only after something plays |
| Bars beside it on the Desk | CheckBox | `now_playing.visualizer` | SW:2782 | |
| Gradient bars | CheckBox | `now_playing.bars_gradient` | SW:2788 | Disabled unless Bars |
| Style what's playing | CheckBox | `now_playing.style` | NL:56 | |
| Let Claude style songs directly | CheckBox | `now_playing.looks_direct` | NL:63 | Agent switch #3 |
| Clear Looks… | Button | | NL:74 | |
| Suggestion card: Save / Dismiss | 2 Buttons | | NL:46-51 | Only while a suggestion waits |

### 1.14 Weather (SW:2807-2880)

| Label | Kind | Writes | At | Note |
| --- | --- | --- | --- | --- |
| City | TextBox | `desk_weather` | SW:2822 | |
| Look up / Clear | 2 Buttons | | SW:2824, 2827 | Look up is the one network call, disclosed SW:2865 |
| Places found | ListBox | | SW:2833 | Hidden until a lookup |
| Show it as | TextBox | `desk_weather.nickname` | SW:2844 | |
| Unit | ComboBox | `desk_weather.unit` | SW:2859 | |
| Weather data by Open-Meteo.com, licence | 2 Hyperlinks | | SW:2869-2871 | |

### 1.15 Widgets (WP, hosted at SW:2883)

| Label | Kind | Writes | At | Note |
| --- | --- | --- | --- | --- |
| Companion sprite | ComboBox | `desk_companion.pack` | WP:125 | Hidden until a sprite pack is installed (`HasCompanions`) |
| Show the companion on the Desk | CheckBox | `desk_layout` (places the "companion" piece) | WP:130 | Writes the Desk page's data (WidgetsPageViewModel.cs:198-210) |
| Let it roam | CheckBox | `desk_companion.roam` | WP:134 | |
| Check the catalog again | Button | | WP:162 | The page also fetches the catalog on open (WidgetsPageViewModel.cs:282-287) |
| Catalog rows: Install / Update | Button per row | pack files | WP:57-61 | A row already installed reads "Installed." and has no button |
| Installed rows: Uninstall | Button per row | | WP:87-90 | Only here |
| Installed rows: Use my own art… | Button per row | | WP:92-97 | Only for sprites "made here" |

### 1.16 About (SW:2889-3105)

| Label | Kind | Writes | At | Note |
| --- | --- | --- | --- | --- |
| What's New… | Button | opens window | SW:2903 | |
| Take the tour… | Button | opens window | SW:2907 | |
| Trademark notice | text | | SW:2916 | Also at the foot of Desk, Message, Now playing, Weather, Widgets (a Store rule, see "what stays") |
| Where settings live: 3 x Show in Explorer; Credential Manager line; Account profiles line | Buttons, text | | SW:2946, 2958, 2970, 2978, 2987 | The vault is "Usage vault" here, "History vault" on Claude Usage |
| Reset Sanduhr… | Button | opens ResetWindow | SW:2993 | |
| Third-party notices: packages, data services, fonts, per-row Project page | Buttons | opens browser | SW:3020, 3060 | |

### 1.17 Outside Settings: every place that opens a page or names one

| Where | Label | Opens | Evidence |
| --- | --- | --- | --- |
| Widget icon strip | gear, tooltip "Settings" | General | MainWindow.xaml:270-272 |
| Widget right-click | Settings… | General | MainWindow.xaml.cs:104 |
| Tray | Settings… | General | App:385 |
| Tray | What's New…, Take the tour… | windows | App:388-392 |
| Tray | Arrange Desk…, Put my Desk back | mirror Desk page buttons | App:407-420 |
| Tray | Tray shows (submenu) | mirrors General's picker | App:886 |
| Tray, widget right-click | Accounts submenu (switch list), Add account… | sign-in flow | App:430, MainWindow.xaml.cs:100, AccountMenuBuilder.cs:56-60. The "Accounts settings…" item (AccountMenuBuilder.cs:62) exists but both callers pass `includeManage: false`, so it never shows |
| Tier card right-click | Alerts settings… | Alerts (top) | TierCard.xaml.cs:89 |
| Tier card right-click | Hide until it resets, Hide this tier, Stop warnings for this limit / Warn again for this limit, Hidden limits | no Settings home | TierCard.xaml.cs:68-84 |
| Desk right-click | Edit messages… | Message (top) | DeskClicks.cs:50, App:485 |
| Desk right-click | Desk settings… | Desk (top) | DeskClicks.cs:51, App:486 |
| Desk right-click | Edit buttons… | Desk#desk-launcher | DeskClicks.cs:52, App:487 |
| Desk right-click | Arrange Desk…, Turn off the Desk | Arrange; writes `desk_enabled` | DeskClicks.cs:53 |
| Launcher button for a missing app | (click) | Desk#desk-launcher | App:505-508 |
| Global shortcut | Ctrl+Alt+S by default, off | Settings | App:633-641 |
| What's New / tour | Show me | **page only**, never an anchor | App:285-297, 316-330 |
| Vault consent dialog | text "Settings ▸ Claude Usage" | prose | Views/VaultConsentDialog.xaml:53 |
| First run | Just the Desk | `just_the_desk`, no Settings control | SN:185-186, Core JustTheDesk.cs |
| What's New window | Don't show after updates | `whats_new_hide_after_updates`, no Settings control | Views/WhatsNewWindow.xaml:314-318 |
| MCP and publish_usage results | "Settings > Publish usage > Endpoint URL", "(Settings, Desk, Watchers)" | prose read by agents | Services/UsagePublishService.cs:164-174, Mcp/ToolLogic.Watchers.cs:114 |

No string found names a Settings page that does not exist (grep of `Settings, X`, `Settings ▸ X`, `Settings > X` and `X settings…` across App, Core and Mcp). The Mac's F8 problem is absent on Windows. One section name is wrong: "Watchers" under Desk has no heading.

---

## 2. Findings, ranked

Severity follows the Mac spec: **blocker** stops a task, **confusing** makes a person stop and guess, **polish** is wrong but survivable. IDs are W1 onward so they do not collide with the Mac's F1 to F31. Items marked "needs live check" cannot be settled from code.

### Blockers

**W1. Four of five widget pack kinds can be installed but not used from Settings, and the catalog sits apart from the installed list.** Needs confirmation by the owner, since code shows no consumer rather than proving none. The catalog says it carries "Sprites, themes, palettes, effects and fonts" (WP:152) and `PackValidator.Kinds` lists `theme, palette, effects, font, sprite` (Core PackValidator.cs:47). The only code that reads an installed pack for use is the companion (Core CompanionSettings.cs, CompanionSprite.cs, App Desk/DeskCompanion.cs, Desk/WidgetReviewService.cs); a grep for the other kinds finds validators and the Claude-made preview in `WidgetHandoff`, and no applier. So after Install, a theme, palette, effects or font pack shows "Installed." in the catalog row (WidgetsPageViewModel.cs:42-46) and an Uninstall button in the installed row (WP:87), and no switch, Use or Apply anywhere. The catalog list (WP:149-169) and the installed list (WP:173-182) are two sections with different buttons: a catalog row can only Install or Update, an installed row can only Uninstall or Use my own art… (sprites made here). This is the owner's observation, verified: the catalog and installed packs should be one list, with the catalog as a collapsible section, and each row should say what "on" means for its kind. The companion controls (Companion sprite, Show the companion on the Desk, Let it roam) are a separate block above that list, visible only after a sprite is installed (WP:119, `HasCompanions`).

**W2. Short windows can hide controls with no scroller.** Needs live check. The window is fixed at 800 high with no fit-to-screen (SW:16, no `WorkArea` use), and Alerts (SW:1013-1088) and Publish usage (SW:1546-1680) are a `StackPanel` inside a `Border` with no `ScrollViewer`. Alerts holds 10 controls and 3 captions; Publish holds 10 controls, two divider rules and 4 paragraphs. At MinHeight 520 the content area is about 456 high after chrome, which these two pages likely exceed; on a work area under 800 DIPs (a 1366x768 screen, or 1080p at 150%) the window itself overruns the screen, taking the Quit button at the sidebar foot with it. Every other long page has a `ScrollViewer`.

### Confusing: one feature, several homes

**W3. The Desk page is 14 sections, ordered by when each was added, with its one preview in the middle.** Evidence: anchors at SW:2323, 2335, 2349, 2362, 2385, 2397, 2401, 2405, 2561, 2572, 2583, 2618, 2631, 2673. Order is switch, Arrange, clicks, watchers (2 switches), direct design, OLED care, Designs, Launcher (up to 8 editable rows), Where each piece sits (11 rows), Layout map, Look, Plate, Ink, Margins. A person who wants to move the clock passes four agent/feature sections and Launcher's row editors first; the 11 piece rows start after roughly 600 lines of XAML. Four sections are not about the Desk's layout at all (Watchers, Direct design, Designs, Launcher) and each is a feature with its own settings. The Mac's answer was Desk, Desk Look and Watchers as separate pages with the preview on top.

**W4. A piece's switch and its placement are on different pages, and only some features place themselves.** The default layout is `clock:bl claude:bl` (Core DeskArrangement.cs:288); every other piece starts Hidden. Auto-place exists for the glow target (ClaudeCodeViewModel.cs:270-291), the Launcher (PlaceLauncherIfHidden, DeskSettingsViewModel.cs:239) and the Companion switch (WidgetsPageViewModel.cs:198-210). It does not exist for:
- Weather: City and Unit on Weather, the piece on Desk. Copy: "Place Weather on the Desk page." (SW:2865).
- Message: lines on Message, the piece on Desk. No hint on the Message page.
- Now playing: "To show it on the Desk, place Now playing on the Desk page." (SW:2759).
- Watchers: both switches in section 4 of Desk, the piece in section 9 of the same page; "They show where you place Watchers under Where each piece sits." (SW:2369). A person who switches Let agents show watchers on sees nothing happen. Same trap as the Mac's F3, with no glow involved.
None of these pages has a link button to the Desk page; the dependency is prose only. No Settings page contains a "<Page> settings…" button. The pattern is live in two places only: the tier card's "Alerts settings…" (TierCard.xaml.cs:89) and the Desk right-click's "Desk settings…" (DeskClicks.cs:51); "Accounts settings…" is built but never shown (see section 1.17).

**W5. The meters mod has two homes with different verbs and two names.** Claude Code page: "Meters above the prompt", Install… / Update… / Remove (SW:1779, ClaudeCodeViewModel.cs:197-200). Mods & Config: the plugin row named `sanduhr-meters` (Core ModInventory.cs:103), Turn off / Turn on / Update… / Remove… (SW:1980-1990). Each page has its own folder picker (SW:1769, 1860) with unrelated selection state. Neither page says how Turn off differs from Remove. The Mac resolved this by moving all controls to Mods & Config (spec lines 232-238).

**W6. Mods & Config can switch exactly one row.** Every plugin row is read-only except Sanduhr's (`IsSanduhr = item.IsSanduhr && own is not null`, ModsConfigViewModel.cs:81; the button stack is gated at SW:1978-1979). The list title reads "Plugins (46 installed, 41 on)" (ModsPageText.cs `ListTitle`) under a page called "Mods & Config", the tag "mod" marks only plugins with mod hooks, and a note explains why the list and the settings.json switches disagree (`Reconcile`). So a person looking at 41 "on" plugins cannot turn one off here. This is the owner's observation, verified. The Mac's item 71 shows the same inventory read-only on purpose; the Windows decision is whether "Mods" means a list you can run.

**W7. Glow has four names and three homes.** Claude Code page: piece "Glow when Claude needs you" (SW:1782, SI:163), checkboxes "Glow when Claude is waiting on you", "Glow when Claude finishes", "Not while a terminal or editor is in front" (SW:1787-1795), selector "Where the glow shows" (SN:202). Desk page: piece "Claude Code glow" (SN:136). The Mac's F9 collapsed this to "Notch glow" and "Claude Code glow hook". Also "Glow" means something else on Message lines (MP:137), Message look (MP:418) and Launcher buttons (SW:2526).

**W8. "Look" is split across four pages.** Desk page: Font, Clock size, Back plate, Ink, shadow (SW:2583-2669). Message: Look (size, Color, Glow around the message, Font) plus per-line Color, Glow, Size, Letters, Motion (MP:378-432, 125-158). Now playing: Style what's playing, Gradient bars, Looks panel (SW:2782-2791, NL). Launcher rows: Glow color (SW:2526). Two controls are both labelled "Font" (SW:2591, MP:426), one with automation name "Message font". Ink uses a hex box with presets and Message uses its own Color + hex, with different preset lists (not checked beyond the XAML).

**W9. "Let Claude/agents do X without asking" is three switches on two pages, plus a per-folder one.** `watchers_agents` (SW:2365), `desk_direct` (SW:2388), `now_playing.looks_direct` (NL:65), and per folder "Claude can read this folder's project totals (MCP)" (SW:1745). Each carries a good caption; nothing shows them together, so "what can an agent change on my machine" has no single answer.

**W10. Per-limit warnings and hiding have no Settings home.** On the tier card menu only: Hide until it resets, Hide this tier, Stop warnings for this limit / Warn again for this limit, and a Hidden limits submenu (TierCard.xaml.cs:68-84; keys `alert_muted_tiers`, `hidden_tiers`, `limits_hidden_until_reset`, `tier_order`). Alerts holds only the global Warn at / Urgent at (SW:1031-1048). A muted limit cannot be listed or restored from Settings. The tier menu's one Settings link, "Alerts settings…", lands on a page that has nothing per limit. The Mac moved per-limit controls into Alerts (spec lines 154-158).

**W11. Four "history" and three "vault" names for related things.** Page "History" (meter chart + Clear history), box "History vault" (SW:1314), "Usage vault" in About (SW:2951), "History kept" on Mods (SW:2024, Claude Code's own retention), "Erase archive" (SW:1331), "Clear history" (SW:1156). Two buttons named "Export CSV…" (SW:1154, 1413) export different data; the names table binds only the History one (SN:233, SI:271). The vault's home list is ticked on Claude Usage, and the same homes are ticked again for sharing on Publish usage ("Homes to share", SW:1627), and a third consent per folder sits on the Claude Code page.

### Confusing: names that don't match

**W12. The sidebar group "Widget" holds only Themes, and the page "Widgets" sits in group "Desk".** SN:29-30. `SettingsPage.Widget` is not a page (WN WhatsNew.cs:7). A person told "widget settings" finds Themes; one told "Widgets" finds a catalog of Desk packs.

**W13. The Desk switch is named for two of the things the Desk draws.** "Desk: the clock and your Claude meters on the desktop" (SN:125, SW:2325). The Desk right-click menu says "Turn off the Desk" (DeskClicks.cs:53); the tour says "a clock, your meters, a message and more" (WN WelcomeTour.cs:45). The Mac's switch name lists its content.

**W14. Separator styles differ in agent-facing and dialog text.** Most copy writes "Settings, Desk". Others write "Settings ▸ Claude Usage" (VaultConsentDialog.xaml:53, and doc comments), and `publish_usage` returns "Settings > Publish usage > Endpoint URL" (Services/UsagePublishService.cs:164-174) plus "Turn on 'Publish daily'" where the checkbox reads "Publish daily at" (SW:1647). `SettingsSourceScanTests` scans App literals for retired names and for "<Page> settings…" only; it does not read "Settings, <Page>" forms, anything in Core or Mcp, or section names (so "Settings, Desk, Watchers" passes).

**W15. "Show me" and the open-Settings strings never name the page, and none of them takes an anchor.** The What's New and tour button reads "Show me" (ShowMeTitle default, WN WelcomeTour.cs:17); its tooltip names the page (WhatsNewViewModel.cs:70-72). Both callbacks pass a page only (App:285-297, 316-330) though `SelectPage` accepts an anchor (SWcs:222) and every needed anchor exists. Examples: "Your CI runners on the Desk" opens Desk at the top, the switch is section 4 of 14; "Watchers on the Desk" says "Turn it on in Settings, Desk, then place Watchers" (two jumps, WhatsNewCards.cs:94). The Desk right-click uses "Edit messages…" and "Edit buttons…" (DeskClicks.cs:50, 52), which name the task, not the page; only "Desk settings…" follows the contract.

**W16. Menu names drift between the tray, the widget menu and the icon-strip tooltips.** "Compact Mode" / "Expand" (MainWindow.xaml.cs:111) vs "Compact mode (Ctrl+D)" (MainWindow.xaml:292); "Deep Work (Focus)" (MainWindow.xaml.cs:116, App:437) vs "Deep Work focus timer (Ctrl+P)" (MainWindow.xaml:296); "Cooldown Snake" vs "Cooldown snake game"; "Refresh" vs "Refresh usage now"; "Settings…" vs "Settings"; the tray's "Quit Sanduhr" is a literal (App:447) beside `SettingsNames.QuitSanduhr`. SN:14-16 says commands are sentence case; "Compact Mode" is not.

**W17. Reset Sanduhr… lists "Sanduhr's entries in Claude Code (statusline, MCP server)" (ResetWindow.xaml:161, ResetWindow.xaml.cs:57)** while the Claude Code page installs four pieces (statusline, MCP server, meters mod, glow hooks). `UndoAll` covers every kind (Core ClaudeCodeIntegrations.cs:1017), so this is a copy gap, not a behavior gap.

### Confusing: reach and layout

**W18. The widget has no Settings page, so its options have no search hit and no deep link.** On the widget only: Theme popup (also on Themes), graph style cycle (`sparkline_style`), Compact (Ctrl+D), Deep Work length (`focus_mode_duration`), Pin on top (`pinned`), tier order (`tier_order`). "Don't show after updates" lives in the What's New window (`whats_new_hide_after_updates`). Update checking has no control at all: a Velopack probe runs once per 24 h on non-Store installs and offers Restart to update (Updates/UpdateChecker.cs:37-63), the Store updates itself. The Mac has Widget and Updates pages for this.

**W19. No preview on any page except small ones.** Windows has a tray preview on General (SW:793-803), the theme swatches, the History chart and the Desk layout map (SW:2575). No page shows the Message as the Desk draws it, the Now playing line, Weather, or the companion. The map is under 9 sections.

**W20. No way in from outside the app.** No `sanduhr://` scheme, command-line switch or smoke hook opens a page or an anchor (grep finds none; the Mac has `sanduhr://settings/<page>#<anchor>`). Agents are told page names in prose only (W14).

**W21. Settings does not remember its page and three comments disagree about where it opens.** The window is rebuilt on every open and starts on General (App:665-735; the Mods folder is the one remembered value, ModsConfigViewModel.cs:116). `SettingsNames.OpenSettings` says "opens Settings where it was left" (SN:80); `WidgetViewModel.cs:311` says "the Accounts tab"; both are wrong for a closed window.

### Confusing: accounts, privacy and folders

**W22. The first account is named "Personal" with no question.** Embedded sign-in on an empty registry saves as "Personal" (SignInCoordinator.cs:198, 237); the paste path suggests "Personal" in an editable field (line 214). Rename… exists after the fact (SW:995). Same as the Mac's F17.

**W23. Clear history says "all" while sitting under an account picker.** The Account: ComboBox (SW:1117) scopes the chart, and the neighbouring Clear history removes the whole local history file (`History.ClearAll()`, HistoryTabViewModel.cs:227). There is no per-account erase anywhere (the Mac has "Erase this account's data"). Remove account… does delete that account's history file (AccountsViewModel.cs:175-177).

**W24. The per-folder choices are made on four pages and shown on none together.** Linked account and MCP read (Claude Code cards, SW:1736, 1745), vault consent (Claude Usage, SW:1324), share consent (Publish, SW:1637), install target and view target (Claude Code SW:1769, Mods SW:1860). The Accounts page lists no linked folders. The statusline sentence is clear ("The statusline in this folder shows that account's meters.", SW:1742), which is better than the Mac had.

**W25. Privacy statements are per page; the network calls are not gathered.** Three features call out: Weather (Open-Meteo, disclosed SW:2865), Widgets (GitHub, disclosed at the foot of the page, WP:181, after the page has already fetched the catalog on open: WidgetsPageViewModel.cs:282-287), Publish usage (your endpoint, SW:1553). History, Claude Usage and Publish each carry a local-only sentence. About's "Where settings live" lists folders and the Credential Manager but not what leaves the PC.

**W26. Manual key help is developer shorthand.** "open DevTools (F12) → Application → Cookies → claude.ai, then copy the value of the sessionKey cookie" (Views/ManualKeyWindow.xaml:47), Chrome-style only. Same as the Mac's F19. The labels there ("sessionKey (required)", "cf_clearance (optional — only if Cloudflare blocks)") are not explained.

### Confusing: the signed-out first run

**W27. Windows handles signed-out better than the Mac did, with three gaps.** Good: Accounts has an empty hint (SW:984); the tour has a signed-out step 1 and a Just the Desk tour (WN WelcomeTour.cs:41-72, 131-140); What's New shows nothing on a fresh install (`WhatsNew.IsFresh`, WhatsNew.cs:156); Linked account is disabled without accounts (`LinkEnabled`). Gaps:
- History reads "No history yet. Data accumulates as you use Sanduhr." signed out (HistoryChart.cs:82), which is not true without an account.
- Just the Desk is a mode (widget starts hidden, sign-in reminder off, JustTheDesk.cs:49-54) with no Settings control; the only way back is signing in (SN:185-186). The tray is the only way to reach Settings in that state, and the tray label is "Settings…" only.
- The sign-in reminder switch is on Alerts (SW:1076) although it is a Desk behavior.

### Polish

**W28.** Trademark notice appears 6 times (Desk, Message, Now playing, Weather, Widgets, About; SW:2728, 2799, 2874; MP:436; WP:185; SW:2916). The repeat is deliberate: the Store test 10.1.4.4 asks for the disclaimer wherever Claude is named (repo CLAUDE.md). Keep it, but one shared footer control would remove 5 copies.

**W29.** `suggestion` anchor on Message is not in `SettingsIndex`, so search cannot reach Claude's pending suggestion (MP:220).

**W30.** Hex-only color entry: Ink Colors (SW:2659), Message custom color (MP:128, 407), Launcher Glow color (SW:2528), Studio rows (SW:2201). No color wells. Captions explain the format, as on the Mac (F27).

**W31.** Code comments out of date: `SettingsWindow.xaml.cs:13-14` lists 10 pages; `WidgetViewModel.cs:311` says Accounts; `SettingsViewModel.cs:9-14` describes four tabs. Not user-facing.

**W32.** Message mode button is two different labels with an ellipsis that opens no window (DeskMessagePageViewModel.cs:593). Same shape as the Mac's F10.

**W33.** Windows-only list cap hides state: the theme gallery is capped at 112 px (SW:2077) and Installed user themes at 72 px (SW:2280) inside a page that otherwise has room.

---

## 3. Mac v2 cross-walk

### 3.1 Mac pages against Windows today

Status words are the ones asked for: same home, split across N pages, Windows-only, Mac-only, missing on Windows.

| Mac v2 page | Mac content | Where it lives on Windows today | Status |
| --- | --- | --- | --- |
| General | Surfaces status, Menu Bar Shows, Meetings menu, Startup, 2 shortcuts, Quit | General: Tray shows (the Menu Bar Shows equivalent, same name in tray menu), Startup, 1 shortcut. Quit is on the sidebar foot. No Surfaces rows. No meetings. | same home (smaller; Quit and Surfaces differ) |
| Accounts | Sign in with Label, plain key help, list, Data per account (history, folder, activity, share, work flag, erase) | Accounts: list and actions, Follow. Data pieces: History (Clear history), Claude Usage (vault), Publish (share), Claude Code (folder link). Label prompt absent. No work-account flag found. | split across 5 pages |
| Usage | Claude Usage | History, Claude Usage, Publish usage, plus Mods "History kept" | split across 3 pages (4 with Mods) |
| Alerts | Notifications, per-limit warn/hide, preview | Alerts (global thresholds, switches, sound, test) + tier card menu (per limit). No Quiet hours, "Where alerts show" or Notification Settings. Sign-in reminders is Windows-only. | split across 2 places; per-limit group missing on Windows |
| Desk | Desk switch, Arrange, Where each piece sits, Order, Clicks, Advanced Margins, map preview | Desk page, sections 1, 2, 3, 9, 10, 14. No Order control in rows (reordering is in Arrange, DeskArrangeCopy.BarHint). Map not on top. | same home (page also holds 8 other things) |
| Desk Look | Fonts, sizes, colors, glow, shadow | Desk (Look, Back plate, Ink), Message (Look), Now playing (Style, Gradient bars) | split across 3 pages |
| Message | Top bar (Add Line, List/Text, Save, Revert), rotation, look group, text mode | Message page: Add line, Edit as text… toggle, Save, Revert at the top; rotation; special days; Look; pin as text field | same home |
| Notch | Notch switch, wings, glow, camera and mic, fill light, size | none. Nearest: Claude Code glow targets (window top, screen edge, taskbar flash, Desk spot) | Mac-only |
| Now Playing | Now playing, placement links | Now playing page (widget line, apps, bars, looks). Placement on Desk page, prose only. | same home |
| Watchers | Switches, inline placement, glow rule, band switch | Desk page: Let agents show watchers, Show my CI runners' jobs (switches), Watchers piece row (placement), "Claude Code glow" row. No page. No "Show Claude Code's background work" or "above the prompt" switch found. CI runners are Windows-only. | split within one page (Desk), no own page |
| Claude Code | Python, folders with account link, MCP, statusline, glow hook | Claude Code: folders with Linked account, MCP read, 4 pieces, glow switches and target | same home |
| Mods & Config | Meters mod at top, inventory read-only, seat picker | Mods & Config: read-only inventory plus Sanduhr row controls. Meters mod also on Claude Code. | split across 2 pages (the mod) |
| Widget | Show the widget, font, Subtle mode, pacing calculators | none in Settings. Widget options live on the widget (icon strip, right-click, tier menu). | missing on Windows |
| Themes | Themes | Themes (+ mirrored popup on the widget) | same home |
| Updates | Check for Updates…, two automatic switches | none. Velopack probe every 24 h on GitHub installs, Store updates itself, Restart to update offer (UpdateChecker.cs, Core RestartToUpdate.cs) | missing on Windows |
| About | Version, What's New, tour, links, plans, where settings live, Reset | About: version, What's New…, Take the tour…, trademark, Where settings live, Reset Sanduhr…, Third-party notices. No Plans line found. | same home |

Mac features that Settings v2 introduced, against Windows:

| Mac v2 feature | Windows today |
| --- | --- |
| One names table + tests | present: `SettingsNames`, `SettingsNamesTests`, `SettingsSourceScanTests`; checks cards, tour and App literals only (W14) |
| Sidebar search with synonyms | present: `SettingsIndex` (67 anchors), highlight 1.5 s, focus handoff |
| Anchors and deep links `sanduhr://settings/<page>#<anchor>` | anchors yes; URL, CLI and smoke hook no (W20); Show me and menus pass no anchor (W15) |
| Previews collapse below 600 pt | no previews to collapse; no height handling at all (W2) |
| Buttons read "<Page> Settings…" and open the named page | 2 do (Alerts settings…, Desk settings…); Accounts settings… is built but never shown; "Edit messages…", "Edit buttons…", "Show me" do not name the page |
| Dependencies say themselves | partly: Arrange explains itself when off; glow, Launcher and Companion auto-place; Weather, Message, Now playing, Watchers do not (W4) |
| Reset Sanduhr… | present, ordered, Claude Code first (Core ResetPlan.cs:5-16) |
| Simple/Advanced via collapsed sections | none; no collapsible section on any Windows page |
| Signed-out forms | Accounts only; others unchanged (W27) |

### 3.2 Windows-only pages and parts, and where each would sit in a task-organized layout

This is a proposal to react to, not a decision. "Task" means what a person is trying to do.

| Windows-only | Today | Proposed home | Why |
| --- | --- | --- | --- |
| **History** page | Usage group, own page | Usage page as the first view ("Meters over time"), beside Overview, Trends, Sessions, which are already pills inside Claude Usage (SW:1179-1184). Clear history moves to Accounts, Data, per account. | One question: how much have I used. Four views, one page. |
| **Publish usage** page | Usage group | Usage page, section "Share" (preset, endpoint, auth, token, homes, schedule). | It uploads the same per-project totals Usage shows. Its homes list is the vault's. |
| **Weather** page | Desk group | Keep as a small page in the Desk group next to Now playing, but add an inline "Where it shows" picker that writes the same layout key (the Mac's Watchers pattern). | City and unit are content settings; placement should not need another page. |
| **Widgets** page (companion, catalog, installed) | Desk group | Widget group, page "Widgets", next to Themes: one list of installed packs with a collapsible "Get more" catalog; each row says what On does for its kind. Companion choice becomes a row action on sprite packs, with Show and Roam beside it. | Fixes W1 and the split list; fits "Widget" group once that group has a real page. |
| **Launcher** (Desk section 8) | Desk page | Its own page in the Desk group, or a section on a "Desk pieces" page with Weather and Now playing settings. | Eight rows with images and hex fields are a feature, not an option of the layout. |
| **Designs / Put my Desk back** (section 7) | Desk page | Desk page, collapsed "Designs" at the bottom, with the agent switch. | It is history for the layout. |
| **OLED care** (section 6) | Desk page | Desk page, Advanced (collapsed). | A rarely used display option. |
| **CI runners** switch | Desk page | Watchers section, with Let agents show watchers. | Same feature family. |
| **Let Claude change the Desk directly**, **Let agents show watchers**, **Let Claude style songs directly** | 3 switches, 2 pages | One "Agents" section (Accounts page Data area, or the Claude Code page), each switch named once, each page keeping a one-line status. | W9: one answer to "what can an agent do". |
| **Sign-in reminders on the Desk** | Alerts | Desk page, with a status line on Alerts. | It is a Desk piece's behavior. |
| **System (CPU, RAM, disk)** and **Companion** pieces | Desk piece rows | stay in Where each piece sits | |
| **Shortcut** (1 key) | General | stay; the Mac has 2 | |

A straw-man sidebar that follows tasks, for comparison with the Mac's 16: General, Accounts, Usage, Alerts | Desk, Desk Look, Message, Now playing, Weather, Watchers | Claude Code, Mods & Config | Widget, Widgets, Themes | Updates (only if a setting exists), About. That is 17 or 18 pages, so the page count is not the point; the point is that each row in the Windows-only table above has one home.

---

## 4. What already works and should stay

1. **Sidebar search and anchors.** `SettingsIndex` covers every page and 67 anchors with synonyms, title matches rank before synonym matches, a hit scrolls, flashes for 1.5 s and moves keyboard focus (SWcs:222-280, SI:118-259, `SettingsIndexTests`). Quit is findable and focuses its button (SWcs:356-365). A guard test checks every index anchor is marked in the XAML.
2. **The names table and its tests.** `SettingsNames` holds page titles, link titles, 37 control names and a retired-names list; cards, tour and App literals are scanned (`SettingsNamesTests`, `SettingsSourceScanTests`). Extend it, do not replace it.
3. **Tray shows.** One name in General and the tray submenu, 5 modes, a live preview (SW:790-816, App:886).
4. **Arrange Desk copy.** The button explains itself when the Desk is off and while arranging, and names Return/Done and Escape/Cancel (Core DeskArrange.cs:155-162).
5. **Self-placing features.** Choosing the glow's Desk spot places it (ClaudeCodeViewModel.cs:270-291); adding Launcher buttons places the Launcher; Show the companion writes the layout. Use this pattern for Weather, Message, Now playing and Watchers.
6. **Claude Code page trust and consent.** "Nothing is written to a folder until you install a piece and confirm." (SW:1696), per-folder Linked account with a plain consequence line (SW:1742), per-folder MCP read consent, receipts so Remove undoes exactly.
7. **Reset Sanduhr….** Five parts with checkboxes, Claude Code entries undone first because their receipts live in a folder a later part deletes, a warning when that part is unchecked, and a result list (Core ResetPlan.cs:5-16, ResetWindow.xaml:155-185).
8. **About, Where settings live.** Real folders with Show in Explorer, the Credential Manager target, the profile-folder note (SW:2922-2991).
9. **Message editor shape.** Add line, mode toggle, Save and Revert sit at the top of the list, not under it (MP:264-279); lines the editor cannot represent are kept as written (MP:166-202).
10. **Mods & Config honesty.** Counts reconcile with settings.json and say why they differ (`ModsPageText.Reconcile`), secrets are masked (`ConfigMask`), "no files here" is stated.
11. **Accounts.** An empty hint, a visible active marker, and one `SettingsNames` string shared by Settings, tray and widget menus (AccountMenuBuilder.cs:56-62).
12. **Startup honesty on Store installs.** The toggle becomes a note and a deep link to Windows Startup apps (SW:831-840).
13. **Shortcut recorder.** Records a combo, shows the OS refusal as a note, resets, and works whenever Sanduhr runs (SW:850-890, `HotKeys.Caption`). The Mac asked for exactly this (its F15).
14. **Accessibility names.** Nearly every control and every per-row control has an automation name that includes the row ("Line 3 color", "Linked account for <folder>", "Install <pack>").
15. **Fresh-install quiet.** No What's New on a first launch; a tour with signed-out and Just-the-Desk variants (WN WhatsNew.cs:156-174, WelcomeTour.cs:41-140).
16. **The trademark footer where Claude is named** (Store rule). Keep the text; fold the copies into one control.
17. **Page titles that Store copy and PRIVACY.md quote** ("Claude Usage", "Publish usage"; SN:14-17). Renaming them needs those documents updated in the same release.

---

## Open items for the live pass

These cannot be settled from code:

1. Desk page: pixel position of the Layout map and of "Where each piece sits" at 800 high, 100% and 150% scale; what a person sees before scrolling.
2. Alerts and Publish usage at MinHeight 520 and on a 1366x768 screen: whether controls and the Quit button are clipped or off-screen (W2).
3. Whether any theme, palette, effects or font pack takes effect after install by some path the grep did not find (W1), for example through a Claude-made Desk design that references a pack id.
4. Whether Alerts sub-controls dim when Enable alerts is off (no XAML binding; the VM may ignore them).
5. Whether the History chart text reads as wrong to a signed-out person (W27), and how Linked account looks disabled.
6. Search quality on common words: "glow" (Claude Code, Message lines, Message look), "font" (Desk, Message), "history" (4 targets), "watchers", "weather".
7. Contrast and focus rings on the sidebar rows, pills and link-style buttons across themes.
8. How the 11 piece rows wrap at MinWidth 680 (the Place/Size/Monitor row is 138 + 72 + 146 wide plus name, DP:27-32).
