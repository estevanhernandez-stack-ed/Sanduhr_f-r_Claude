# Settings v2 for Sanduhr für Claude on Windows

Spec, 2026-10-24, for 4.0 on `release/4.0`. Inputs: `docs/settings-v2-windows/findings.md` (cited **WF1** to **WF49**), `inventory.md` (**W**, page map and control tables), `mods-parity.md` (**M** rows, gaps **R1** to **R12**, §4 data model), and the Mac's finished `docs/settings-v2-spec.md` (its structure and page names). Paths are under `windows-dotnet/src/` unless they start with `docs/` or `tests/`. File abbreviations follow the inventory: SW is `Sanduhr.App/Views/SettingsWindow.xaml`, SWcs its code-behind, SN `Sanduhr.Core/SettingsNames.cs`, SI `Sanduhr.Core/SettingsIndex.cs`.

## Verdict: keep the window, the search and the names table, and give every feature one home that every entry point lands on

Windows Settings already has the parts the Mac had to build: a names table with tests, search over 67 anchors, anchors that scroll and flash, and an in-app reset. The problems sit between pages. The Desk page holds 14 sections (WF22). A piece is switched on one page and placed on another (WF8). The meters mod, the glow, the agent consents and the word "history" each have two to four homes (WF9 to WF14). Lists show state they can't change, and actions do things their screen doesn't show (WF1, WF2, WF3, WF39). No entry point lands on a section (WF17). No page answers "what leaves this PC" or "hide who I am before I share my screen" (WF6, WF7).

v2 changes four things:

1. **One home per feature.** Every other mention is a status line plus a link.
2. **One list for everything you can install.** Mods and widget packs go in one list. Each row says whether Sanduhr can switch it, and the catalog is a collapsible section.
3. **Every entry point lands on its section.** This covers Show me, menus, search and a `--settings` switch.
4. **A Privacy page.** It holds streamer mode, the list of what leaves this PC and the list of what Claude can change.

WF1 and WF2 are fixed in 4.0, slice 0.

## The new information architecture

### Principles

1. **One home per feature.** A switch, picker or slider is shown as a control on exactly one page. Any other page that mentions it shows a one-line status ("Weather: on the Desk, bottom right.") and a link button "<Page> settings…" that opens the home at its anchor. A status line is never a second control. There is one exception: a feature page whose Desk piece is Hidden shows a "Place it on the Desk" button. That button places the piece at its default spot (Principle 4) and then turns into the status line. It writes the same `desk_layout` value the Desk page edits, so the two can't disagree.
2. **One name per control.** The name in Settings is the name in the tray, the widget menu, the Desk menu, tooltips, the tour, What's New and text written for agents. Every name lives in `SettingsNames`, and tests scan App, Core and Mcp for drift (WF18, WF19).
3. **Buttons name pages exactly.** A button or menu item that opens Settings reads "<Page> settings…", in sentence case as SN:14-16 requires, and passes an anchor. Prose that names a place in Settings uses one form only: "Settings, <Page>, <Section>".
4. **A dependency says itself, or sets itself.** Turning on a feature that draws on the Desk places its piece if it is Hidden. Each piece gets a default place from one table in Core (below). A control that needs another setting dims and shows one line naming that setting and its page. Example: the Alerts sub-controls under "Enable alerts" (WF36).
5. **A list is either actionable or labelled read-only.** A row that Sanduhr can't switch says so in words ("Read-only: switch it in Claude Code"). It never shows a dead control.
6. **Signed out is a state.** Pages that show account data have a signed-out form that says what will appear and offers "Add account…" (WF32).
7. **The keyboard reaches everything a mouse does.** That includes the order of pieces in a stack (WF5). Every list has a name and an empty-state line (WF35). Quit is never one Tab away from page content (WF33).
8. **No settings.json key is renamed or removed.** Controls move and keep their keys. Two keys are added (see "Settings keys and migrations").

### Sidebar: 17 pages in 5 groups

The Mac's groups are (none), Desktop, Claude Code, Widget and Help. Windows keeps four of them with the Mac's names. It changes one ("Claude Code" becomes "Extend") and adds one ("Usage"). Help keeps About only.

| Group | Page | Enum (`SettingsPage`) | Was | Why |
| --- | --- | --- | --- | --- |
| (none) | **General** | General | General | Unchanged. Quit moves out of the Tab path (WF33). |
| | **Accounts** | Accounts | Accounts | Adds per-account Data: clear history, linked folders (WF1, WF27). |
| | **Alerts** | Alerts | Alerts, plus the tier card's per-limit menu | Each limit gets a Settings home (WF13). Sign-in reminders leave (WF32). |
| | **Privacy** | Privacy (new) | nothing; scattered notes on About, Weather, Widgets, Publish | Home for streamer mode, "What leaves this PC" and "What Claude can change" (WF6, WF7, WF10). Windows-only for now; the Mac can adopt it. |
| Usage | **Claude Usage** | ClaudeUsage | History, plus Claude Usage | One question, "how much have I used", answered in four views (WF14). The title is kept because Store copy and `docs/PRIVACY.md` quote it (inventory keep 17). |
| | **Publish usage** | Publish | Publish usage | Kept as a page and under its quoted title. It has a token, a schedule and its own consent, too much to fold into a view. |
| Desktop | **Desk** | Desk | Desk sections 1 to 3, 6, 7, 9, 10, 14, plus the companion block and Sign-in reminders | The Desk's on switch, layout and behavior, with the map on top (WF22, WF8). |
| | **Desk Look** | DeskLook (new) | Desk Look, Back plate and Ink; Message's Look | Same as the Mac. One page for fonts, sizes and colors, which removes the two "Font"s (WF12). |
| | **Message** | DeskMessage | Message, minus Look | Same as the Mac. |
| | **Now playing** | NowPlaying | Now playing | Unchanged, plus the placement status (WF8). |
| | **Weather** | Weather | Weather | Unchanged, plus the placement status (WF8). |
| | **Watchers** | Watchers (new) | Desk section 4 | Same as the Mac. A feature with its own switches, not a layout option (WF22, WF8). |
| | **Launcher** | Launcher (new) | Desk section 8 | Windows-only. Up to eight editable rows with images, so it is a feature (WF22). |
| Extend | **Claude Code** | ClaudeCode | Claude Code, plus Mods & Config's one-folder report | Folders as cards, each holding everything Sanduhr knows and installs for it. The second and third folder pickers go (WF4, WF27, WF9). |
| | **Mods & Widgets** | Mods | Mods & Config, Widgets | The one list (owner decision; WF3, WF39, WF40). It holds the meters mod's controls, like the Mac's Mods & Config. |
| Widget | **Widget** | Widget (enum value exists, becomes a page) | nothing in Settings | The widget's own options, so the "Widget" group finally holds the widget (WF15, WF24). |
| | **Themes** | Themes | Themes | Unchanged, plus a link to theme packs. |
| Help | **About** | About | About | Adds Updates (WF6) and "Don't show What's New after updates" (WF24). |

Seventeen pages, up from 15. The count matters less than the homes, as on the Mac. Every new page takes sections off the Desk page or gives an orphaned control a home.

**Group names, and why they differ from the Mac.**

- **"Extend" replaces "Claude Code".** The group now holds widget packs, which have nothing to do with Claude Code. The old name also repeated its first page's title (WF43).
- **"Desktop" replaces "Desk".** This matches the Mac, and it means the group heading no longer repeats its first page's title.
- **"Usage" keeps its name.**
- **Group headings are set in one place**, `SettingsNames.Sidebar`, and the five literal `GroupHeader` values in SW are removed (inventory §1.1).

**Retired page titles:** "History", "Widgets", "Mods & Config". Each goes on `SettingsNames`' retired list, so the scan test fails if one reappears. The enum values `History` and `Widgets` stay as aliases (see "Deep links").

### General (`general`)

- **Tray shows** (`tray`), with its preview, as today.
- **Startup** (`startup`), as today, including the Store note.
- **Shortcuts** (`shortcuts`). The shortcut switch gets visible text, "Shortcut to open Settings", which matches its spoken name (WF35). The key recorder and reset link stay.
- **Quit Sanduhr** stays at the sidebar foot. Its `TabIndex` moves to the end of the sidebar order, after the page list. It is no longer the first stop after page content: Tab from a page's last control goes to the search box (WF33). Quit asks first: "Quit Sanduhr? The widget and the Desk close." with "Quit" and "Cancel". The tray's Quit stays immediate, because using the tray is an explicit act.

### Accounts (`accounts`)

- **Account list** (`accounts`): the list gets the name "Accounts" (WF38). The buttons explain why they're dim: "Pick an account to switch, rename, update or remove it." (WF29). "Follow the account I'm using" (`follow`) gets a caption saying how long a manual switch pauses it. The value comes from the follow service.
- **Sign in** (`sign-in`): "Add account…" asks for a **Label** before saving. "Personal" is filled in and editable (WF30). The manual key window gets plain steps for Edge and Chrome, and "cf_clearance" goes behind "Seeing a Cloudflare error?" (WF31).
- **Data for this account** (`data`, new): for the selected account:
  - "Linked folders", a status list with "Claude Code settings…" to each folder's card (WF27)
  - "Clear this account's history…", the one per-account erase (WF1)
  - "Remove account…", renamed from nothing (same label), whose confirmation is now repeated as a caption under the button (WF29)
- **Signed out**: the empty hint as today, plus one line: "Claude Usage, Alerts and the meters fill in after you sign in."

### Alerts (`alerts`)

- **Alerts** (`notifications`): the page gets its title (WF43). Under "Enable alerts", every sub-control binds `IsEnabled` to it. When alerts are off, one line reads "Alerts are off. Nothing below sends." (WF36).
- **Warn at and Urgent at** (`thresholds`): the boxes show a "%" suffix (WF43).
- **Each limit** (`each-limit`, new): one row per limit the account has, with "Warn for this limit" (`alert_muted_tiers`), "Show this limit" (`hidden_tiers`) and "Hidden until it resets" (`limits_hidden_until_reset`, shown and clearable). The tier card menu keeps its items as shortcuts that write the same keys. Its link becomes "Alerts settings…", opening `alerts#each-limit` (WF13).
- **Sound** (`sound`): "100% plays the ! (you know the one)" becomes "Play the cooldown snake's sound at 100%" (WF43, WF38).
- **Send test alert** (`test`).
- "Sign-in reminders on the Desk" moves to Desk, Behavior. Alerts shows a status line with "Desk settings…" (WF32).

### Privacy (`privacy`, new)

One page for "what does Sanduhr show, send and allow". Sections, in this order:

1. **Streamer mode** (`streamer`): see "Streamer mode" below.
2. **What leaves this PC** (`network`, WF6): a table generated from one Core list, `NetworkDisclosure.All`. Each row names the destination, when the call happens, what it carries, and its off switch or the link to it.
   - claude.ai usage: while an account is signed in. Carries the account's session key. Off: "Accounts settings…".
   - claude.ai sign-in window: only when you add or update an account.
   - Open-Meteo: on "Look up". The forecast runs only while the Desk is on, a place is set and Weather is placed, at most every 30 minutes. Off: "Weather settings…".
   - GitHub, widget catalog: only when the Catalog section is opened or "Check the catalog again" is pressed. This is a change: the page no longer fetches on open. Off: don't open the catalog.
   - GitHub, update check: once a day on installs not from the Store. Off: "Check for updates once a day" on About (new key `update_check`).
   - Your endpoint, Publish usage: daily at the set time, or "Publish now". Carries token counts per project name, so project names do leave. The row says so in those words (WF28). Off: "Publish usage settings…".
   - MCP `publish_usage`: an agent's request, carried out by the widget to the same endpoint, under the same switch.

   Under the table: "Nothing else leaves this PC. Sanduhr has no analytics." A line links `docs/PRIVACY.md`'s published page.
3. **What Claude can change** (`agents`, WF10): a status list of every agent consent, each with its state and a link to its home:
   - "Let Claude show watchers" → `watchers#watchers`
   - "Let Claude change the Desk directly" → `desk#desk-designs`
   - "Let Claude style songs directly" → `nowplaying#np-looks`
   - "Let Claude read this folder's project totals", per folder → `claudecode#folders`

   Above the list: "Claude means any agent that uses Sanduhr's MCP server. None of these work until the MCP server is installed in a folder." Plus that install's status. The wording is unified on "Claude" (WF10).
4. **Where settings live**: a status line and "About settings…" to `about#where`. The rows stay on About, as on the Mac.

### Claude Usage (`claudeusage`)

Pills become **Meters | Overview | Trends | Sessions**.

- **Meters** (`meters`, new): the History chart, Week and Month, the "Account:" picker and "Export CSV…", renamed "Export meter history…" (`meters-export`). "Clear history" leaves this view. Its home is Accounts, Data, as "Clear this account's history…" (WF1, WF14). Signed out, the empty text reads "Sign in to start recording your meters. Sanduhr keeps 30 days on this PC." (WF32).
- **Overview** (`overview`): as today. "History vault" is renamed **"Session archive"** (`vault`). It shares one name with About's row, which also becomes "Session archive" (WF14). With no Claude Code homes, it shows "No Claude Code folders found. Add one in Claude Code settings…" instead of a 0-high list (WF35).
- **Trends** (`trends`), **Sessions** (`sessions`): as today. Sessions' export becomes "Export sessions…" (WF14).

Names after v2: "meter history" (the 30-day chart), "session archive" (the vault), and "Claude Code keeps transcripts for" (WF14, below). "Erase archive" becomes "Erase session archive…".

### Publish usage (`publish`)

- **Destination** (`destination`), **Token** (`token`): visible labels and spoken names match (WF34). The spoken names become "Preset", "Endpoint URL", "Auth", "Set token…" and "Clear token".
- **Homes to share** (`homes`): an empty-state line when there are none (WF35). Each row names the home's codename while streamer mode is on.
- **Publish daily** (`schedule`): the checkbox reads "Publish daily at", and agent text quotes it exactly (WF18).
- **What gets sent** (`sample`, new): "Show a sample…" opens a read-only preview of the next record, built by the same function that builds the real one (WF28).

### Desk (`desk`): the split

WF8 and WF22 are the core. The decision: **every piece is switched on and placed in one place, the Desk page's Pieces section.** Feature pages (Message, Now playing, Weather, Watchers, Launcher, Claude Code's glow) show the piece's status with "Desk settings…" to `desk#pieces`, or "Place it on the Desk" when it is Hidden (Principle 1). Turning on a feature never leaves it invisible: the feature's own on-action places a Hidden piece at its default (Principle 4).

Sections, in order:

1. **Desk** (`desk`): the switch, renamed **"Desk: a clock, your meters, a message and more on the desktop"** (WF16). The same words go in the tour, and the Desk menu's "Turn off the Desk" stays.
2. **Arrange Desk…** (`arrange`): as today. The caption adds "Or use the Order buttons below." (WF5).
3. **Layout map** (`map`): the page's preview, moved to the top. It folds below 600 DIP (see "Short windows"). Its caption becomes "Where each piece sits now. Move pieces with the rows below or Arrange Desk…" because it isn't a control (WF5).
4. **Pieces** (`pieces`): was "Where each piece sits". One row per piece:
   - **Show** (checkbox). On means Place is not Hidden. Checking it sets the piece's default place; unchecking sets Hidden, and the last place is remembered for the window's life.
   - **Place**, **Size**, **Monitor**, and **Style** where it applies.
   - **Order** (new, WF5): "Move up" and "Move down" buttons, enabled when another piece shares the place, spoken "Move <piece> up in <place>".
   - **Settings…** to the piece's page where it has one.
   - Piece names, in `SettingsNames`: "Message", "Clock and date", "Claude meters (line)", "Claude meters (bars)", "Now playing", "System (CPU, RAM, disk)", "Weather", "Watchers", "Launcher", "Claude Code glow", "Companion".
   - The **Companion** row carries the old Widgets companion block inline: "Companion sprite" and "Let it roam". "Show the companion on the Desk" is retired, because the row's Show is that control and the key is the same (`desk_layout`). With no sprite installed, the row reads "No companion yet. Get one in Mods & Widgets…", linking to `mods#catalog` filtered to sprites.
   - Layout: rows wrap to two lines below 760 DIP of page width. The page host gets a horizontal scroll bar as a last resort, so nothing clips at 680 (WF26).
5. **Behavior** (`desk-clicks`): "Clock, message and Claude line take clicks" and **"Sign-in reminders on the Desk"** (`signin-reminders`, moved from Alerts; WF32).
6. **Designs** (`desk-designs`): collapsed, with a one-line summary ("3 saved. Put my Desk back is ready."). It holds "Put my Desk back", the saved rows and **"Let Claude change the Desk directly"** (`desk-direct`), because direct changes are what Designs undoes.
7. **Advanced** (`desk-advanced`): collapsed. Holds **Margins** (`margins`) and **OLED care** (`desk-oled`).

The trademark footer is a shared control (WF44).

**Default places** (new `DeskArrangement.DefaultPlaceFor(piece)`, Core, pure, tested): Message bottom left, Now playing bottom right, Weather top right, Watchers right, Launcher top left, Companion bottom center, System top right. Clock, Claude meters and Claude Code glow keep today's behavior. These values are proposals for the owner to confirm in slice 3. They follow existing auto-place code (`ClaudeCodeViewModel.cs:270-291`, `PlaceLauncherIfHidden`, `WidgetsPageViewModel.cs:198-210`), and those three call sites move to the one table.

### Desk Look (`desklook`, new)

- **Fonts** (`look`): **"Desk font"** (was "Font" on Desk) and **"Message font"** (was "Font" on Message). Font packs join both lists, as today.
- **Sizes** (`sizes`): "Clock size" and "Message size".
- **Ink and color** (`ink`):
  - "Clock, date, Claude" ink. Its spoken name becomes the visible one (WF34).
  - "Colors" presets are visible whenever Ink is not Automatic. Choosing a preset sets Custom. Palette packs show as "<palette> · <pack>" with no detour through Custom (WF3).
  - The hex box stays.
  - "Drop shadow under the text".
  - "Message color" with its hex.
  - "Glow around the message".
- **Back plate** (`desk-plate`).
- No preview in 4.0 (see "Not in 4.0").

### Message (`deskmessage`)

As today, minus Look. Changes:

- The mode toggle becomes a two-button segment, **List | Text**, with no ellipsis (WF48).
- The `suggestion` anchor joins the index as "Claude's suggestion" (WF45).
- The page top shows the Message piece's status line (WF8).

### Now playing (`nowplaying`), Weather (`weather`)

Each gets a status line at the top: "On the Desk: bottom right. Desk settings…", or "Not on the Desk. Place it on the Desk" (WF8).

- **Weather**: the prose "Place Weather on the Desk page." goes. "Look up" for a new city places the piece if it is Hidden.
- **Now playing**: "Let Claude style songs directly" keeps its home here, with the unified wording.

### Watchers (`watchers`, new)

- **"Let Claude show watchers"** (`watchers`, was "Let agents show watchers"; key `watchers_agents`).
- **"Show my CI runners' jobs"** (`ci-runners`; key `watchers_ci_runners`) with its live status line, and the caption "Independent of the switch above." (WF8).
- **Where they show** (`watchers-place`): the Watchers piece's status, or "Place it on the Desk". Turning on either switch places a Hidden Watchers piece (Principle 4).
- Agent text "(Settings, Desk, Watchers)" becomes "Settings, Watchers" (WF18).

### Launcher (`launcher`, new)

"Add app…", "Add link…", "Buttons in a", and the rows (`launcher-buttons`), moved as they are. The page top shows the piece's status. The Desk menu's "Edit buttons…" becomes **"Launcher settings…"**, and the missing-app click lands here (WF17, WF19).

### Claude Code (`claudecode`)

The folder cards become the one place for everything per folder (WF4, WF9, WF27).

- **Intro**, as today. "Add folder…" moves beside the Folders heading.
- **Folders** (`folders`): one card per folder. Each card has:
  - **Linked account**, with the caption made true: "Once the statusline or meters are installed here, they show this account's meters." (WF27).
  - **"Let Claude read this folder's project totals"**, the MCP read (renamed; WF10).
  - **This folder's pieces**: Statusline, MCP server and **"Claude Code glow hook"** (one name, the Mac's; WF11), each with status and Install…, Change… or Update…, and Remove, acting on this card's folder. The separate "Folder" combo under "Sanduhr in Claude Code" is removed (WF9, P2). "Install again to update it." goes, because "Update…" is the verb (WF42).
  - **"Meters above the prompt"**: a status line only ("On", "Off", "Not installed") with "Mods & Widgets settings…" to `mods#meters`, matching the Mac (WF9).
  - **"What this folder holds"** (collapsed, `folder-config`): Mods & Config's read-only report (settings.json, statusline, hooks, plugin switches, MCP servers, CLAUDE.md, and "Claude Code keeps transcripts for <n> days", renamed from "History kept"; WF14), with "Refresh". This removes Mods & Config's folder picker.
  - **"Forget folder"**, for hand-added folders. Its spoken name is "Forget folder <folder>" (WF34).
- **Glow** (`glow`): "Glow when Claude is waiting on you", "Glow when Claude finishes", "Not while a terminal or editor is in front", "Try it: waiting", "Try it: done", and **Where the glow shows** (`glow-where`). These sit under one heading, "Claude Code glow", with the line "Needs the Claude Code glow hook in a folder above." The line shows a status of which folders have it (WF11).
- **Glow names after v2:** "Claude Code glow hook" is the install, "Claude Code glow" is the section and the Desk piece, and "Where the glow shows" is the target. Message's "Glow" and the Launcher's "Glow color" keep their names. They describe a look, and their pages say so.

### Mods & Widgets (`mods`): one list

This page uses mods-parity §4's data model. Core gets a pure `InstalledItems.Build(...)` over `ModInventory.Read` (every folder), `ClaudeCodeIntegrations.OwnModStateOf`, `WidgetStore.Load()` and the widget catalog. It returns `InstalledItem` records with:

- id, display name
- kind: Claude Code mod, Claude Code plugin, widget pack (sprite, theme, palette, font or effects), or Sanduhr integration
- source
- version and update
- **scope**: the app, or a map of folder → On, Off, Missing or Not installed
- **`CanSwitch`**, with its route: `OwnModReceipt`, `PackEnabled`, or `None`, plus the reason

It is outside the MCP link list (`TrustBoundaryTests` unchanged). The page and `SettingsIndex` read it.

Sections:

1. **Meters above the prompt** (`meters`): the sanduhr-meters mod, every folder at once. Each folder row has a switch (On or Off, writing `enabledPlugins` through `SwitchOwnMod`), "Update…" and "Remove…". Turning a folder On where the mod isn't installed opens the existing consent dialog and installs it. One caption states the difference: "Off keeps the mod's entry and files; Remove takes the entry out and puts settings.json back exactly." (WF9). This is the Mac's layout.
2. **Installed** (`mods`, keeps the id): the one list. Filter chips are "All", "Claude Code" and "Widgets", plus a text filter. Rows by `CanSwitch`:
   - **Widget packs (switchable in 4.0):** a switch bound to `widgets.json` `enabled`. Every consumer must honor it: theme list, ink presets, font lists, companion. Each row has a **"Where it shows"** line by kind (WF3):
     - Theme: "In Themes" with "Themes settings…"
     - Palette: "In Desk Look, Ink and color" with a link
     - Font: "In Desk Look, Fonts"
     - Sprite: "As the Desk companion" with "Desk settings…" to `desk#pieces`
     - Effects: "Not used yet: Sanduhr has nothing that draws effects packs." (WF3)

     The row also has "Uninstall…", and "Use my own art…" for sprites made here.
   - **Sanduhr's meters mod:** one row, "Meters above the prompt", with "On in 2 of 3 folders" and a link up to section 1.
   - **Other Claude Code plugins and mods (read-only in 4.0):** a scope summary ("On in 1 of 3 folders") and the label **"Read-only: Sanduhr can't switch this yet. Use `claude plugin` in a terminal."** No dead switch (Principle 5, WF39, mods-parity R1 and R6).
   - The page intro drops "Sanduhr runs nothing here" and says instead: "Everything installed for Sanduhr and for Claude Code, in one list. You can switch Sanduhr's mod and widget packs here; other Claude Code mods are shown read-only." (WF39). The plugin-count reconcile note keeps its facts in plain words: "<n> plugins have no on/off entry in settings.json, so Claude Code treats them as on."
3. **Catalog** (`catalog`): collapsed by default. It fetches only when opened or on "Check the catalog again", and its disclosure line ("The catalog comes from GitHub. Opening it is the only time Sanduhr asks.") sits above the list (WF6). Catalog rows merge with installed ones by id, so a pack appears once: Install, Update, or the switch. In 4.0 the catalog holds widget packs only. The Claude Code marketplace catalog arrives with R2 (see "Not in 4.0"). When offline, it reads: "Couldn't reach the widget catalog. Widget packs add themes, colors, fonts and a Desk companion. Check your connection and try again." (WF40).

The Widgets page's "Desk companion" block moves to Desk, Pieces, on the Companion row. Themes gets a status line, "Theme packs: <n> installed. Mods & Widgets settings…" (WF40).

**What can be switched in 4.0:** widget packs (all five kinds, through `enabled`) and the meters mod (per folder). Everything else is listed, scoped per folder and labelled read-only.

### Widget (`widget`, new; WF24, WF15)

- **Widget** (`widget-options`): "Compact mode" (Ctrl+D, session state as today), "Graph style" (`sparkline_style`), "Keep the widget on top" (`pinned`), "Deep Work length" (`focus_mode_duration`), and "Limit order" (`tier_order`, a reorderable list with Move up and Move down).
- **Just the Desk** (`just-the-desk`): shown only while `just_the_desk` is on: "Sanduhr is in Just the Desk: the widget is hidden." with **"Show the widget"**, which ends the mode the same way signing in does (WF32).
- The widget's icon strip and right-click menu keep their items, with the names unified (WF19): "Compact mode", "Deep Work", "Cooldown snake", "Refresh usage now", "Settings…".

### Themes (`themes`)

As today, plus:

- The gallery and the installed themes lists grow to fill the page, with no 112 and 72 DIP caps (WF49).
- The name and slug fields and both lists get names (WF35).
- "Looks good" becomes "This theme's JSON is valid." "Save as: .json" becomes "Save the theme as" (WF43).
- One spelling: "Save & apply".

### About (`about`)

- **Version** (`version`): What's New…, Take the tour…, and **"Don't show What's New after updates"** (moved from the What's New window, which keeps a link; key unchanged).
- **Updates** (`updates`, new): on GitHub installs, **"Check for updates once a day"** (new key `update_check`, default on) and "Check now". On the Store build: "The Microsoft Store keeps Sanduhr up to date." (WF6). Why a section and not the Mac's page: one switch doesn't earn a page.
- **Trademark** (`trademark`), the shared footer.
- **Where settings live** (`where`): the paths wrap instead of cutting with "…". "Show in Explorer" for the session archive explains itself when dim: "Nothing archived yet." (WF43). The rows follow streamer mode.
- **Reset Sanduhr…** (`reset`), see "Reset".
- **Third-party notices** (`notices`).

## Streamer mode

**Decided by the owner (2026-10-24): style C with codenames.** Every identifying thing gets a stable two-word codename. A per-item allowlist shows the real name for things that are fine on stream. Anything new or unmapped is masked until you allowlist it. Demo mode stays separate: it swaps in sample data and still starts only with `--demo` or `SANDUHR_DEMO=1`.

**What it is.** A privacy switch that keeps live data (meters, numbers, songs, the clock) and hides what identifies people.

**Where it lives.**

- **Home:** Privacy, Streamer mode (`privacy#streamer`). The switch is **"Streamer mode: hide accounts, folders and projects"**, with the caption "Live numbers stay. Names become codenames until you choose to show one."
- **Shortcuts, writing the same key:** a tray item "Streamer mode" (checked state) and a widget right-click item of the same name. These are menu shortcuts, as the tier card's items are for Alerts.
- **While on:**
  - The tray tooltip starts with "Streamer mode · ".
  - The widget's account chip carries a small eye-slash glyph, spoken "Streamer mode on".
  - Settings shows a one-line banner at the top of every page that masks something: "Streamer mode is on: names are codenames. Privacy settings…".

**Codenames.**

- **The pool.** One built-in list in Core, `Codenames.Pool`: 14 first words × 14 second words, 196 pairs. The words come from sand, time, desert, sky and glass, with no people, brands or trademarks.
  - First words: Dune, Amber, Mesa, Dusk, Ember, Quartz, Ochre, Mirage, Saffron, Drift, Tawny, Opal, Cinder, Zenith.
  - Second words: Fox, Wren, Heron, Lark, Moth, Ibis, Gecko, Hare, Kestrel, Owl, Oryx, Swift, Finch, Plover.
  - Examples: "Dune Fox", "Quartz Heron", "Saffron Moth".
  - A test pins the pool's size and checks it against a small deny list, so that no pair forms a known product name. "Desert Eagle" can't occur, because "Desert" and "Eagle" aren't in the pool.
  - Past 196 in one category, names gain a numeral ("Dune Fox 2").
- **Categories, one pool.** Codenames are assigned separately per category: account, email, Claude Code folder, project (a Claude Code project or folder name), skill, repo, CI runner and job source, publish endpoint host, and city. Assignment draws from one shared "used" set, so an account and a repo never share a codename.
- **Assignment: first-seen and kept.**
  - The first time a name in a category needs masking, Core picks a pair by HMAC-SHA256 of (category, real name), keyed with a random per-install salt, and steps to the next free pair on a collision. The result is written to the map.
  - Codenames then stay the same across sessions and restarts, so a viewer can follow "Dune Fox failed again".
  - The salt means two PCs give different codenames, so a codename can't be reversed from a public list.
- **Where the mapping lives.**
  - `%APPDATA%\Sanduhr\codenames.json`: the salt, the map per category (real name → codename) and the allowlist. It is written atomically and never sent anywhere. It is not in settings.json, because settings.json has a frozen key contract and the map holds real names that have no business there.
  - **Two locations, on purpose.** The owner's location holds for everything the app reads. One CLAUDE.md rule forces a second location for the statusline. The statusline runs outside the package, and MSIX virtualizes AppData (repo CLAUDE.md, Windows gotchas). So the widget also writes `%USERPROFILE%\.sanduhr\streamer.json`. It holds `on` and a map of folder ref → codename, where the ref is `SnapshotContract.AccountRef` applied to the folder's full path. No real name goes into that file. Both files get their own names in Reset, and Reset removes only those names.
- **"Shuffle codenames…"** (Privacy, Streamer mode): confirms "Every codename changes. Viewers following a name will lose it." Then it draws a new salt and clears the map. The allowlist is kept.
- **The allowlist (C).**
  - Under the switch, **"Show the real name"** (`streamer-allowlist`) lists every mapped item by category, with the codename and a "Show the real name on stream" checkbox. While streamer mode is on, the list shows codenames only, so editing it on stream leaks nothing. Real names show only while streamer mode is off.
  - Quick routes write the same list: a "Show real name on stream" item on the right-click of a masked name in Claude Usage's tables, on a folder card's menu, and on a Mods & Widgets row.
  - Two entries are allowlisted by default, because they identify no one: Sanduhr's own mod (`sanduhr-meters`) and the "Sanduhr" folder label on the widget.
  - Everything else, including anything new or not yet mapped, is masked until allowlisted.

**Reach inside Settings** (from the findings' "Feeds for streamer mode"):

| Page | Masked while on |
| --- | --- |
| Accounts | Account labels and any email (codename), in the list, the Label field and the data section |
| Claude Usage | The Meters legend and "Account:" picker; Overview's top projects and top skills; Trends; the Sessions table and Stack by project; the calendar's day view; the Session archive home labels. Exports write real data and say so beside the button ("Exports keep real names.") |
| Publish usage | Endpoint URL (host as codename, path hidden); "Homes to share" labels; the sample record |
| Claude Code | Folder card titles and paths, the linked account, every consent dialog's folder text, and spoken names ("Forget folder Ember Lark") |
| Mods & Widgets | Folder names in scope summaries; plugin and marketplace names for non-allowlisted items; packs "made here" |
| About | Paths show `%USERPROFILE%` in place of the profile folder; the Credential Manager target shows the codename |
| Weather | City and Places found: the "Show it as" name if set, else the city codename |
| Watchers | The CI runners status line (runner codename) |
| Desk, Message, Now playing, General, Alerts, Themes, Widget, Launcher | Nothing on the page. Message lines are your own words and stay; songs stay (live data) |

**Reach outside Settings:**

- The widget's account chip and account menu; the tray account menu and tooltip; toasts (account label).
- The Desk: watcher cards (job titles' repo and runner parts become codenames; other text stays) and the weather name.
- The statusline's account segment in every Claude Code folder. `StatuslineScript` reads `streamer.json` and prints the folder's codename, or no label when the folder is unmapped.
- The meters mod, if it draws a label: it reads the same `streamer.json`. Slice 7 checks whether it draws one.
- What it can't reach: Claude Code's own interface, which shows paths and project names, and MCP answers to agents (see "Not in 4.0"). The streamer caption says so plainly: "Claude Code's own screen isn't covered. Check what it shows before you share it."

## Search

Keep the index, its ranking and the 1.5 s flash. Changes (WF23):

- Every new anchor is indexed, and `suggestion` joins (WF45). The index test also fails on an anchor that isn't marked in XAML or is missing from the index.
- **Synonyms:**
  - "font" → Desk Look, Fonts (`look`) first.
  - "update", "updates" → About, Updates, ranked above Accounts' "Update sign-in…".
  - "docker", "runner" → Watchers, `ci-runners`.
  - "privacy", "offline", "network", "send" → Privacy, `network`.
  - "stream", "streamer", "screen share", "hide names", "obs" → Privacy, `streamer`.
  - "demo" → Privacy, `streamer`, whose caption explains demo mode and its `--demo` start.
  - "codename" → `streamer-allowlist`.
  - "widgets", "packs", "pack" → Mods & Widgets.
- **Results say when a hit is in a collapsed section.** A "(in Advanced)" suffix shows, and choosing it opens the section.

## Deep links and entry points (WF17, WF25)

**One landing rule.** Every way into Settings passes a page and an anchor: `SettingsWindow.SelectPage(page, anchor)` (SWcs:222). Nothing opens a page top except "Settings…" itself.

- **What's New and the tour.** `WhatsNewCard` and the tour's card record gain `Anchor`. Every card that points into Settings carries one (`WhatsNewCards.cs`, `WelcomeTour.cs`; App:285-297 and 316-330 pass it). `WhatsNewTests` gains a check that every card with a Settings page names an anchor that exists in `SettingsIndex`. The Desk card bodies drop "in Settings, Desk" for the exact place, for example "Settings, Desk Look, Ink and color".
- **Menus:**

| Entry point | Today | v2 label | Lands on |
| --- | --- | --- | --- |
| Widget gear, widget "Settings…", tray "Settings…" | General | same | Last page this session, else General (see below) |
| Tier card "Alerts settings…" | Alerts top | same | `alerts#each-limit` |
| Desk menu "Edit messages…" | Message top | "Message settings…" | `deskmessage#lines` |
| Desk menu "Desk settings…" | Desk top | same | `desk#pieces` |
| Desk menu "Edit buttons…", missing-app click | `desk#desk-launcher` | "Launcher settings…" | `launcher#launcher-buttons` |
| Account menus | (built, never shown) | "Accounts settings…", now shown in tray and widget account menus | `accounts#accounts` |
| Vault consent dialog prose | "Settings ▸ Claude Usage" | "Settings, Claude Usage, Overview" | (prose) |
| `publish_usage` results | "Settings > Publish usage > Endpoint URL" | "Settings, Publish usage, Destination" | (prose) |
| Watchers MCP text | "(Settings, Desk, Watchers)" | "Settings, Watchers" | (prose) |
| Weather, Now playing, Watchers, Message, Launcher pages | prose | "Desk settings…" or "Place it on the Desk" | `desk#pieces` |
| Themes | nothing | "Mods & Widgets settings…" | `mods#installed`, Widgets filter |

- **Reopen where it was left.** `App` keeps the last page and anchor in memory for the app's life (no key). Settings… reopens there, and "Settings…" from the tray after a restart opens General. The two wrong comments are fixed (`SettingsNames.cs:80`, `WidgetViewModel.cs:311`; WF25, WF47).
- **From outside the app.** `Sanduhr.exe --settings <page>[#<anchor>]`. Page and anchor are the lowercase ids in this spec. A second launch forwards the argument to the running instance through `Services/SingleInstance.cs`, which slice 2 extends with an argument hand-off if it carries none today (to verify at the start of slice 2). The smoke harness uses the same switch. A `sanduhr://` URL scheme needs a manifest protocol extension and a Store review, so it is under "Not in 4.0".
- **Aliases.** Old pages and anchors still resolve, so cards, tests and agent prose written before 4.0 land correctly:

| Old | New |
| --- | --- |
| History (enum), `chart` | ClaudeUsage `meters` |
| History `export` | ClaudeUsage `meters-export` |
| Widgets (enum), `widgets-catalog` | Mods `catalog` |
| Widgets `widgets-installed` | Mods `mods` |
| Widgets `widgets-companion` | Desk `pieces` (Companion row) |
| Desk `look` | DeskLook `look` |
| Desk `ink`, `desk-plate` | DeskLook `ink`, `desk-plate` |
| Desk `desk-watchers` | Watchers `watchers` |
| Desk `desk-launcher` | Launcher `launcher` |
| Desk `desk-direct` | Desk `desk-designs` |
| DeskMessage `message-look` | DeskLook `sizes` |
| Alerts `signin-reminders` | Desk `signin-reminders` |
| ClaudeCode `meters` | Mods `meters` |
| ClaudeCode `statusline`, `mcp` | ClaudeCode `folders` |
| Mods `config`, `mcpservers`, `claudemd`, `retention` | ClaudeCode `folder-config` |

`SettingsIndex.Resolve(page, anchor)` owns the alias table. `SettingsIndexTests` covers every row.

## Keyboard and screen reader (WF5, WF33 to WF38)

- **Order without a mouse.** Pieces rows get Move up and Move down (WF5). Arrange Desk gains arrow keys: Tab picks a piece, the arrows move it between places, Shift+arrows change its order, and Return and Escape stay. Its bar hint names them.
- **Headings are stops.** Each section heading is exposed as a heading (`AutomationProperties.HeadingLevel`). On pages with four or more sections, a row of jump links at the top ("Pieces · Behavior · Designs · Advanced") reaches each one (WF22).
- **Names match labels** for every control in WF34. A test in `SettingsSourceScanTests` checks that a control with visible text has a spoken name starting with that text. Per-row context ("for <folder>") may follow.
- **Empty lists** collapse to an empty-state line, not a 0-high stop (WF35): Homes to share, Session archive, Overview tables, Meters chart, Apps.
- **Alerts dims** (WF36). **Quit** moves and confirms (WF33).
- **Focus visuals** (WF38): a focus ring on pills and link buttons. The close button is spoken "Close Settings". Duplicate spoken headings go ("Sign-in reminders" is the checkbox only). The Claude Usage lists get names. A disabled combo draws disabled.
- **Contrast** (WF37): TextDim and TextMuted are raised to 4.5:1 on the page and sidebar in all six built-in themes, and Obsidian's selected label uses Text with an accent bar. `ThemeContrastTests` (new) computes the ratios from the theme tokens.

## Short windows (WF41, WF26)

- **On open,** the window clamps to the monitor's work area (`SystemParameters.WorkArea` for the monitor it opens on) at 800 x 780, MinHeight 520 and MinWidth 680. It centers within the work area, so the foot and Quit never sit off screen.
- **Below 600 DIP of window height,** the Desk's Layout map folds to a 44 DIP strip, "Layout map" with a disclosure arrow. That choice is remembered for the window's life, the same rule the Mac uses.
- **Every page sits in a ScrollViewer.** Alerts and Publish usage get one (W2). Horizontal scroll is enabled only when content can't wrap.

## Reset (WF20)

"Reset Sanduhr…" keeps its five parts and its order.

- The Claude Code part reads **"Sanduhr's entries in Claude Code (statusline, MCP server, meters mod, glow hook)"**, with a checkbox per folder that has receipts. `UndoAll(folders)` already takes the list (R12).
- The About row gains one caption, "Removes your accounts, history, session archive, settings, Desk messages and Sanduhr's Claude Code entries. You choose which.", so a person doesn't have to open the dialog to learn this.
- Reset also removes `codenames.json` (with "All settings") and `streamer.json` (by name, from `~\.sanduhr`).

## Outside Settings, same release

- **Names (WF19)** across the tray, widget menu and tooltips, from `SettingsNames`: "Compact mode", "Deep Work", "Cooldown snake", "Refresh usage now", "Settings…", "Quit Sanduhr". The tray's literal "Quit Sanduhr" (App:447) uses the table.
- **Core words defined once (WF21).** Each definition appears as the first line of its home page and in the tour:
  - Desk: "the clock, meters and pieces Sanduhr draws on your desktop"
  - Widget: "the small floating window with your meters"
  - Piece: "one thing on the Desk"
  - Watchers: "cards that show work in progress, from Claude or your CI"
  - Companion: "a small animated sprite on the Desk"
  - Mod: "an add-on that Claude Code loads"
  - Widget pack: "an add-on for Sanduhr: a theme, colors, a font or a companion"

  "Folder" is the one word for a Claude Code config folder. "Home" goes from Publish usage and Claude Usage ("Folders to share", "Session archive: folders").
- **Streamer mode's reach:** widget, Desk, tray, toasts, statusline (above).
- **What's New card for 4.0:** "Settings, rearranged", landing on `privacy#streamer`, plus one for streamer mode. `WhatsNewTests` pins the count and ids, so the cards ship with their test lines.

## Mapping: every current control to its new home

"Same key" means the control moves and its settings.json key is unchanged. Labels in quotes are the new names where they changed.

| Current page | Current control | New page, section | Name in v2 | Key |
| --- | --- | --- | --- | --- |
| General | Tray preview, Tray shows | General, Tray shows | same | `tray_mode` |
| General | Start Sanduhr when Windows starts; Open Windows Startup settings… | General, Startup | same | Run entry |
| General | Shortcut keys button, shortcut switch, reset link | General, Shortcuts | switch gets visible text "Shortcut to open Settings" | `hotkey_settings` |
| Sidebar | Quit Sanduhr | Sidebar foot, last in Tab order, confirms | same | none |
| Sidebar | Search | Sidebar | same | none |
| Accounts | Follow the account I'm using | Accounts, Account list | same, caption adds pause length | `follow_account` |
| Accounts | Account list | Accounts, Account list | spoken "Accounts" | registry |
| Accounts | Switch to, Rename…, Update sign-in… | Accounts, Account list | same | registry |
| Accounts | Remove account… | Accounts, Data for this account | same | credential + history file |
| Accounts | Add account… | Accounts, Sign in | same, asks Label | registry |
| Alerts | Enable alerts | Alerts, Alerts | same | `alerts_enabled` |
| Alerts | Warn at, Urgent at | Alerts, Warn at and Urgent at | "%" suffix | `alert_warn_pct`, `alert_urgent_pct` |
| Alerts | Projection alerts…, Reset notifications | Alerts, Alerts | same | `alert_projection`, `alert_reset` |
| Alerts | Sound, Chime for model-scoped weekly tiers | Alerts, Sound | same | `alert_sound`, `alert_sound_scoped` |
| Alerts | 100% plays the ! (you know the one) | Alerts, Sound | "Play the cooldown snake's sound at 100%" | `alert_snake_full` |
| Alerts | Send test alert | Alerts, Send test alert | same | none |
| Alerts | Sign-in reminders on the Desk | Desk, Behavior | same | `desk_sign_in_reminder` |
| Tier card menu | Stop warnings / Warn again for this limit | Alerts, Each limit (menu keeps a shortcut) | "Warn for this limit" | `alert_muted_tiers` |
| Tier card menu | Hide this tier, Hidden limits | Alerts, Each limit | "Show this limit" | `hidden_tiers` |
| Tier card menu | Hide until it resets | Alerts, Each limit | "Hidden until it resets" | `limits_hidden_until_reset` |
| History | Week / Month | Claude Usage, Meters | same | view only |
| History | Account: picker | Claude Usage, Meters | same | view only |
| History | Chart | Claude Usage, Meters | signed-out text changes | none |
| History | Export CSV… | Claude Usage, Meters | "Export meter history…" | file |
| History | Clear history | Accounts, Data for this account | "Clear this account's history…", acts on the selected account | history file |
| Claude Usage | Overview / Trends / Sessions pills | Claude Usage | adds "Meters" first | view only |
| Claude Usage | Show project & skill breakdown | Claude Usage, Overview | same | `local_cc_show_breakdowns` |
| Claude Usage | History vault boxes | Claude Usage, Overview, Session archive | "Session archive" | `vault_roots` |
| Claude Usage | Erase archive, Open vault folder | Claude Usage, Session archive | "Erase session archive…", "Open archive folder" | vault |
| Claude Usage | Trends 4/12/26 weeks | Claude Usage, Trends | same | view only |
| Claude Usage | Sessions chips, sort headers | Claude Usage, Sessions | same | view only |
| Claude Usage | Stack by project | Claude Usage, Sessions | same | `ledger_group_by_project` |
| Claude Usage | Export CSV… (sessions) | Claude Usage, Sessions | "Export sessions…" | file |
| Publish usage | Preset, Endpoint URL, Auth, Header name | Publish usage, Destination | spoken names match labels | `publish.preset`, `.endpoint_url`, `.auth_scheme`, `.auth_header_name` |
| Publish usage | Set token…, Clear token | Publish usage, Token | same | Credential Manager |
| Publish usage | Homes to share boxes | Publish usage, Folders to share | "Folders to share (off until you tick them)" | `publish.roots` |
| Publish usage | Publish daily at, time, Publish now | Publish usage, Publish daily | same | `publish.enabled`, `publish.time` |
| (new) | Show a sample… | Publish usage, What gets sent | new | none |
| Claude Code | Folder cards | Claude Code, Folders | same | none |
| Claude Code | Forget folder | Claude Code, folder card | same; spoken "Forget folder <folder>" | `cc_folders_added` |
| Claude Code | Linked account | Claude Code, folder card | same, caption corrected | `account_folders` |
| Claude Code | Claude can read this folder's project totals (MCP) | Claude Code, folder card | "Let Claude read this folder's project totals" | `mcp_roots` |
| Claude Code | Add folder… | Claude Code, beside Folders heading | same | `cc_folders_added` |
| Claude Code | Folder combo (install target) | retired: each card acts on its own folder | none | none (session) |
| Claude Code | Statusline Install…/Change…/Remove | Claude Code, folder card | same | folder files + receipt |
| Claude Code | MCP server Install…/Update…/Remove | Claude Code, folder card | same | folder files + receipt |
| Claude Code | Meters above the prompt Install…/Update…/Remove | Mods & Widgets, Meters above the prompt (card shows status) | switch, "Update…", "Remove…" | receipts unchanged |
| Claude Code | Glow when Claude needs you Install…/Update…/Remove | Claude Code, folder card | "Claude Code glow hook" | receipt |
| Claude Code | Glow when Claude is waiting on you, Glow when Claude finishes, Not while a terminal…, Try it ×2 | Claude Code, Claude Code glow | same | `claude_code_glow.*` |
| Claude Code | Where the glow shows, edge side, monitor | Claude Code, Claude Code glow | same | `claude_code_glow.target`, `.edge_side`, `.edge_monitor` |
| Mods & Config | Folder combo | retired: Claude Code folder cards, "What this folder holds" | none | none (session) |
| Mods & Config | Refresh | Claude Code, What this folder holds | same | none |
| Mods & Config | Settings and statusline summary, MCP servers, CLAUDE.md | Claude Code, What this folder holds | same | read-only |
| Mods & Config | History kept | Claude Code, What this folder holds | "Claude Code keeps transcripts for" | read-only |
| Mods & Config | Plugins list | Mods & Widgets, Installed | read-only rows labelled | read-only |
| Mods & Config | Turn off / Turn on, Update…, Remove… (Sanduhr row) | Mods & Widgets, Meters above the prompt | On/Off switch per folder, "Update…", "Remove…" | `enabledPlugins`, receipts |
| Themes | Choose a theme | Themes, Choose a theme | same, uncapped | `theme` |
| Themes | Studio switch and editor; Paste editor | Themes | "Save & apply"; field names added | `themes_studio`, themes folder |
| Themes | Installed user themes Apply/Reload/Delete | Themes, Installed user themes | uncapped | themes folder |
| Desk | Desk: the clock and your Claude meters on the desktop | Desk, Desk | "Desk: a clock, your meters, a message and more on the desktop" | `desk_enabled` |
| Desk | Arrange Desk… | Desk, Arrange | same | none |
| Desk | Clock, message and Claude line take clicks | Desk, Behavior | same | `desk_pieces_take_clicks` |
| Desk | Let agents show watchers | Watchers | "Let Claude show watchers" | `watchers_agents` |
| Desk | Show my CI runners' jobs | Watchers | same | `watchers_ci_runners` |
| Desk | Let Claude change the Desk directly | Desk, Designs | same | `desk_direct` |
| Desk | OLED care | Desk, Advanced | same | `desk_oled_care` |
| Desk | Designs: Put my Desk back, Switch to, Rename, Pin, Delete | Desk, Designs (collapsed) | same | `desk_put_back`, designs folder |
| Desk | Launcher: Add app…, Add link…, Buttons in a, rows (Move up, Move down, Remove, Label, Glow color, Change image…, default image) | Launcher | same | `desk_launcher` |
| Desk | Where each piece sits: Place, Size, Monitor, Style | Desk, Pieces | adds Show, Order, Settings… | `desk_layout` |
| Desk | Layout map | Desk, Layout map (top) | caption changed | none |
| Desk | Look: Font | Desk Look, Fonts | "Desk font" | `desk_font` |
| Desk | Look: Clock size | Desk Look, Sizes | same | `desk_time_size` |
| Desk | Back plate | Desk Look, Back plate | same | `desk_plate` |
| Desk | Ink, Colors preset, hex, Drop shadow | Desk Look, Ink and color | presets shown without Custom | `desk_ink` |
| Desk | Margins Left/Right/Top/Bottom | Desk, Advanced | same | `desk_margins` |
| Message | Claude's suggestion: Add / Review / Dismiss | Message, Claude's suggestion (now indexed) | same | `messages.txt` |
| Message | Add line, Save, Revert, Reload, per-line controls | Message, Your lines | same | `messages.txt` |
| Message | Edit as text… / Back to the list | Message, Your lines | "List" / "Text" segment | view only |
| Message | Rotation, Mix, Pin a line, Clear | Message, Rotation | same | `desk_message.rotate`, `.mix`, `.pin` |
| Message | On special days, Each line shows for | Message, On special days | same | `desk_message.special`, `.seconds` |
| Message | Look: Message size | Desk Look, Sizes | same | `desk_message.size` |
| Message | Look: Color + hex | Desk Look, Ink and color | "Message color" | `desk_message.color` |
| Message | Look: Glow around the message | Desk Look, Ink and color | same | `desk_message.glow` |
| Message | Look: Font | Desk Look, Fonts | "Message font" | `desk_message.font` |
| Now playing | Show what's playing in the widget, Keep showing while paused | Now playing | same | `now_playing.widget`, `.keep_paused` |
| Now playing | Apps boxes | Now playing, Apps | empty-state line | `now_playing.excluded` |
| Now playing | Bars beside it on the Desk, Gradient bars | Now playing, Bars | same | `now_playing.visualizer`, `.bars_gradient` |
| Now playing | Style what's playing, Clear Looks…, suggestion card | Now playing, Looks | same | `now_playing.style` |
| Now playing | Let Claude style songs directly | Now playing, Looks | same | `now_playing.looks_direct` |
| Weather | City, Look up, Clear, Places found, Show it as, Unit, credits | Weather | same | `desk_weather.*` |
| Widgets | Companion sprite | Desk, Pieces, Companion row | same | `desk_companion.pack` |
| Widgets | Show the companion on the Desk | retired: Companion row's Show | none | `desk_layout` |
| Widgets | Let it roam | Desk, Pieces, Companion row | same | `desk_companion.roam` |
| Widgets | Check the catalog again | Mods & Widgets, Catalog | same | none |
| Widgets | Catalog rows Install / Update | Mods & Widgets, Catalog (merged by id) | same | pack files |
| Widgets | Installed rows Uninstall, Use my own art… | Mods & Widgets, Installed | "Uninstall…", same | pack files |
| (new) | Pack on/off | Mods & Widgets, Installed | switch | `widgets.json` `enabled` (existing field) |
| About | What's New…, Take the tour… | About, Version | same | none |
| What's New window | Don't show after updates | About, Version (window keeps it too, same key) | "Don't show What's New after updates" | `whats_new_hide_after_updates` |
| (new) | Check for updates once a day, Check now | About, Updates | new | `update_check` (new) |
| About | Trademark notice (×6) | shared footer on Desk, Desk Look, Message, Now playing, Weather, Mods & Widgets, About | same text | none |
| About | Where settings live rows, Show in Explorer ×3 | About, Where settings live | paths wrap; "Session archive" | none |
| About | Reset Sanduhr… | About, Reset | caption added | none |
| About | Third-party notices | About | same | none |
| Widget (icon strip, menu) | Graph style cycle | Widget, Widget (strip keeps it) | "Graph style" | `sparkline_style` |
| Widget | Pin | Widget, Widget | "Keep the widget on top" | `pinned` |
| Widget | Deep Work length | Widget, Widget | "Deep Work length" | `focus_mode_duration` |
| Widget | Tier order (drag) | Widget, Widget | "Limit order" | `tier_order` |
| Widget | Compact (Ctrl+D) | Widget, Widget | "Compact mode" | session |
| First run | Just the Desk | Widget, Just the Desk | "Show the widget" (ends it) | `just_the_desk` |
| (new) | Streamer mode, Shuffle codenames…, Show the real name list | Privacy, Streamer mode | new | `streamer_mode` (new); `codenames.json` |
| (new) | What leaves this PC, What Claude can change | Privacy | status only | none |

## Settings keys and migrations

- **No settings.json key is renamed, removed or changes meaning.** Every moved control keeps its key, so `SettingsKeysContractTests.Frozen` stays as it is and an existing install keeps every choice.
- **Two keys are added**, which the contract allows ("A key may be added"):
  - `update_check` (bool, default true when absent; read by `Updates/UpdateChecker.cs`).
  - `streamer_mode` (bool, default false).

  Both are added to `Frozen` in the slice that introduces them.
- **No migration code is needed.** One existing field becomes live: `widgets.json`'s per-pack `enabled`. It is already written as true on install and kept on reinstall (`WidgetStore.cs:110`, `:305`), so honoring it changes nothing for existing installs.
- **New files outside settings.json:**
  - `%APPDATA%\Sanduhr\codenames.json` (salt, maps, allowlist)
  - `%USERPROFILE%\.sanduhr\streamer.json` (`on` and folder ref → codename)

  Both are added to Reset by name. `streamer.json` joins the `~\.sanduhr` shared-ground rule: Sanduhr touches only its own names there.
- **No change to the MCP link list or `TrustBoundaryTests`.** `InstalledItems`, `Codenames` and `NetworkDisclosure` are App-side Core files, not linked into the MCP.

## Shippable slices, in build order

Each slice merges into `release/4.0` on its own, keeps CI green (`dotnet test` over `tests/Sanduhr.Tests`) and updates `SettingsNames`, `SettingsIndex` and their tests in the same PR as the controls it moves. **Parallel:** slice 0 runs alone. Then slices 1 and 2 run in parallel; slice 2 alone edits `SettingsIndex.cs`, and slice 1 alone edits `SettingsNames.cs` strings. After those land, slices 3, 4 and 5 run in parallel, because they touch different TabItems in SW. Rebase on SW conflicts; each slice owns its pages' XAML blocks. Slice 6 follows 4 (the catalog fetch moves). Slice 7 follows 6 (the Privacy page). Slice 8 is last.

### Slice 0: the two bugs (fixed in 4.0, slice 0)

**WF1:** Clear history acts on a chosen account. `UsageHistory.ClearAll(account)` is called with the picked account, and "All accounts" offers no Clear. The confirmation names the account. This becomes the "Clear this account's history…" behavior that slice 5 moves to Accounts.

**WF2:** reopening Claude Code no longer unlinks folders. `OnLinkedAccountChanged` ignores a null that comes from the options being rebuilt, and Reload rebuilds `AccountOptions` without clearing them under live bindings.

Files: `Sanduhr.App/ViewModels/HistoryTabViewModel.cs`, `Sanduhr.Core/UsageHistory.cs`, `Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs`, `Sanduhr.App/ViewModels/WidgetViewModel.cs:946` (null guard in `SetAccountFolder`).
Tests: `UsageHistoryTests` (clear one of two accounts, the other untouched); a `ClaudeCodeViewModel` test that Reload with linked rows leaves `account_folders` unchanged and never calls `SetAccountFolder(null, …)`.
Done: Tier 2 checks 1 and 2 pass on a real profile.
Tier 2: findings items 1 and 2.

### Slice 1: names and copy

Covers WF15 to WF21 names, WF18, WF19, WF21, WF29 copy, WF31, WF34, WF38 names, WF42, WF43, WF44, WF47 and WF48. Every rename in the mapping table and every "<Page> settings…" label:

- Menus unified (tray, widget, strip).
- Agent prose in the "Settings, <Page>, <Section>" form.
- The shared trademark footer control.
- Spoken names matching labels.
- Core-word definitions.
- "Install again to update it." removed.
- Plain key help.
- Stale comments fixed.

No page moves yet; new page titles go into the table but stay unused until their slice.

Files: `Sanduhr.Core/SettingsNames.cs`, `WhatsNewCards.cs`, `WelcomeTour.cs`; `Sanduhr.App/App.xaml.cs`, `Views/MainWindow.xaml(.cs)`, `Desk/DeskClicks.cs`, `Views/TierCard.xaml.cs`, `Views/VaultConsentDialog.xaml`, `Views/ManualKeyWindow.xaml`, `Views/ResetWindow.xaml`, `Services/UsagePublishService.cs`, `Sanduhr.Mcp/ToolLogic.Watchers.cs`, `Views/SettingsWindow.xaml` (labels and automation names only), `Views/DeskMessagePage.xaml`, `ViewModels/DeskMessagePageViewModel.cs`, a new `Views/TrademarkFooter.xaml`.
Tests: `SettingsSourceScanTests` extended to Core and Mcp for "Settings, X" prose and the retired names, and to visible-label-equals-spoken-name. `SettingsNamesTests` covers the menus.
Done: every scan passes, and no string names a page or section that doesn't exist.
Tier 2: item 12 (tray menu labels).

### Slice 2: reach

Covers WF17, WF23, WF25, WF26 (scroll), WF33, WF35, WF36, WF38 focus and WF41:

- Anchors on What's New and tour cards.
- Menu entry points landing on anchors.
- The alias table and `SettingsIndex.Resolve`.
- Synonyms, and the "(in Advanced)" suffix, inert until slice 3.
- Reopening on the last page.
- `--settings` and the SingleInstance argument hand-off.
- Quit moved and confirming.
- Empty-state lines.
- Alerts dimming.
- Focus rings.
- Work-area clamp.
- ScrollViewers on Alerts and Publish usage.

Files: `Sanduhr.Core/SettingsIndex.cs`, `WhatsNew.cs`, `WhatsNewCards.cs`, `WelcomeTour.cs`; `Sanduhr.App/App.xaml.cs`, `Program.cs`, `Services/SingleInstance.cs`, `Views/SettingsWindow.xaml(.cs)`, `ViewModels/SettingsViewModel.cs`, `ViewModels/HistoryTabViewModel.cs` (empty text).
Tests: `SettingsIndexTests` (every alias resolves, every card anchor exists, synonyms rank as specified); `WhatsNewTests` (anchor on every Settings card); an argument-parse test for `--settings`.
Done: every What's New card with a page lands with its target in view at 800 high, and Tab never reaches Quit from page content.
Tier 2: items 5, 9 and 11.

### Slice 3: the Desk split

Covers WF5, WF8, WF11 (the Desk side), WF12, WF22, WF26 and WF32 (reminder):

- New pages Desk Look, Watchers and Launcher (enum values, sidebar, names, index).
- The Desk page in its new order with the map on top.
- Pieces rows with Show, Order and Settings….
- `DeskArrangement.DefaultPlaceFor` and auto-place from Weather, Now playing, Message, Watchers and Launcher.
- The Companion row.
- Message Look moved.
- Sign-in reminders moved.
- Palette presets shown without Custom.
- Arrange arrow keys.
- Heading stops and jump links.
- Map folding below 600.

Files: `Sanduhr.Core/WhatsNew.cs` (enum), `DeskArrangement.cs`, `SettingsNames.cs`, `SettingsIndex.cs`; `Sanduhr.App/Views/SettingsWindow.xaml(.cs)`, `Views/DeskPiecesPanel.xaml(.cs)`, `Views/DeskMessagePage.xaml`, `Views/DesignsSettingsPanel.xaml`, `Views/OledCarePanel.xaml`, `Views/DeskLayoutMap.cs`, `ViewModels/DeskSettingsViewModel.cs`, `ViewModels/WidgetsPageViewModel.cs` (companion moves out), `ViewModels/ClaudeCodeViewModel.cs` (auto-place call), `Desk/DeskArrangeMode.cs`.
Tests: `DeskArrangementTests` (default places, order moves), `DeskSettingsTests` (Show writes Hidden or the default), `DeskArrangeTests` (arrow keys), `SettingsIndexTests`.
Done: turning on Weather, Watchers or Now playing from their pages shows the piece with no visit to Desk; two pieces in one corner reorder by keyboard; Desk reaches Pieces in under 10 Tab stops.
Tier 2: items 4, 5 and 11.

### Slice 4: Claude Code cards and Mods & Widgets

Covers WF3, WF4 (inside one page; full cross-folder catalog is R2), WF9, WF11 (hook name), WF27, WF39, WF40 and WF42:

- `InstalledItems.Build`.
- The one list with filters and the read-only labels.
- Pack switches, and every consumer honoring `enabled`.
- "Where it shows" per kind.
- The meters mod section across folders.
- Folder cards acting on their own folder, with "What this folder holds".
- The Folder combos retired.
- Catalog collapsible, fetching on expand.
- The Widgets page retired, with an alias.

Files:
- New: `Sanduhr.Core/InstalledItems.cs`.
- Core: `WidgetStore.cs`, `PackThemes.cs`, `PackInkPresets.cs`, `ModInventory.cs`, `ClaudeCodeIntegrations.cs` (read only).
- App: `Desk/DeskFonts.cs`, `Desk/DeskCompanion.cs`, `Views/WidgetsPage.xaml(.cs)` (becomes the Mods & Widgets view), `ViewModels/WidgetsPageViewModel.cs`, `ViewModels/ModsConfigViewModel.cs`, `ViewModels/ClaudeCodeViewModel.cs`, `Views/SettingsWindow.xaml`.

Tests: `InstalledItemsTests` (merge by id, scope map, `CanSwitch` per kind), `WidgetStore` enabled-honoring tests per consumer, `ModsPageTextTests`, `ModsFolderChoiceTests` (updated for cards).
Done: an installed palette is one click from where it shows; switching a pack off removes it from its picker; no row shows a control that can't act.
Tier 2: items 3, 6 (catalog) and 8.

### Slice 5: Usage, Accounts, Alerts and Widget

Covers WF1 (move), WF13, WF14, WF24, WF28 (sample), WF29, WF30, WF32 and WF35:

- The Meters view, with History retired and aliased.
- Session archive names.
- Accounts' Data section, with the Label prompt on first sign-in.
- Alerts, Each limit.
- The Widget page, and Just the Desk's "Show the widget".
- Publish usage's sample and its empty states.
- "Don't show What's New after updates" on About.

Files: `Sanduhr.App/Views/SettingsWindow.xaml`, `ViewModels/HistoryTabViewModel.cs`, `ViewModels/AccountsViewModel.cs`, `Services/SignInCoordinator.cs`, `Views/TierCard.xaml.cs`, `Views/HistoryChart.cs`, `Services/UsagePublishService.cs` (sample builder), `Sanduhr.Core/JustTheDesk.cs`, `SettingsNames.cs`, `SettingsIndex.cs`.
Tests: `SettingsIndexTests`, `JustTheDeskTests` (Show the widget ends the mode), a publish-sample test (the sample equals the next real record's fields), a per-limit settings round-trip.
Done: every key in the widget-only list has a Settings control; a muted limit can be restored from Alerts; the first sign-in asks for a label.
Tier 2: items 7 and 8.

### Slice 6: Privacy, network and agents

Covers WF6 and WF10:

- The Privacy page with "What leaves this PC" from `NetworkDisclosure.All`.
- "What Claude can change".
- The `update_check` key and "Check for updates once a day" on About.
- `UpdateChecker` honoring it.

Files: `Sanduhr.Core/NetworkDisclosure.cs` (new), `SettingsNames.cs`, `SettingsIndex.cs`, `WhatsNew.cs` (enum Privacy); `Sanduhr.App/Updates/UpdateChecker.cs`, `Services/SettingsStore.cs`, `Views/SettingsWindow.xaml`, `tests/Sanduhr.Tests/SettingsKeysContractTests.cs` (add `update_check`).
Tests: `NetworkDisclosureTests` (each disclosed caller exists: a source scan for `HttpClient` call sites in App and Core, each mapped to a row, so a new outbound call fails CI until disclosed), an update-check off test.
Done: "privacy" in search opens the Privacy page; with the switch off, no update probe runs.
Tier 2: item 6.

### Slice 7: streamer mode

Covers WF7:

- `Codenames` (pool, salt, first-seen-and-kept maps, shared used set, allowlist, Shuffle) and `codenames.json`.
- `StreamerMask` applied at every surface in the reach table.
- The tray and widget toggles and indicator.
- `streamer.json` and the statusline script reading it.
- Reset entries.
- The `streamer_mode` key.
- The allowlist list and its quick routes.
- The What's New card.

Files:
- New Core: `Sanduhr.Core/Codenames.cs`, `StreamerMask.cs`.
- Core: `StatuslineScript.cs`, `ResetPlan.cs`, `SettingsNames.cs`, `SettingsIndex.cs`, `WhatsNewCards.cs`.
- App: `Services/SettingsStore.cs`, `App.xaml.cs` (tray item, tooltip), `Services/AccountMenuBuilder.cs`, `Views/MainWindow.xaml(.cs)`, toast code, `Desk/` watcher and weather pieces, and the Settings pages listed in the reach table (Accounts, Claude Usage views and `CcCalendarControl.cs`, Publish usage, Claude Code cards and consent dialogs, Mods & Widgets, About, Weather, Watchers).
- Tests: `SettingsKeysContractTests.cs` (add `streamer_mode`).

Tests:
- `CodenamesTests`: pool size 196 and the deny list; a name keeps its codename across reloads; no codename is shared across categories; Shuffle changes all and keeps the allowlist; unmapped names are masked; overflow numbering.
- `StreamerMaskTests`: each surface's formatter.
- A statusline script test with `streamer.json` on and off.
- `ResetPlanTests`: both files removed by name only.
- `WhatsNewTests`: the new card.

Done: with streamer mode on and a two-account, three-folder profile, no account label, email, folder path, project, repo or runner name appears in Settings, the widget, the tray, toasts, the Desk or the statusline, except allowlisted ones.
Tier 2: a new check, "streamer sweep": walk every page and surface on a real profile with streamer mode on and record any real name seen.

### Slice 8: Reset, themes and contrast

Covers WF20, WF37, WF49 and R12: Reset's per-folder list and full label; uncapped theme lists; theme token contrast; Obsidian's selected label.

Files: `Sanduhr.App/Views/ResetWindow.xaml(.cs)`, `Sanduhr.Core/ResetPlan.cs`, the built-in theme files, `Views/SettingsWindow.xaml` (Themes block).
Tests: `ResetPlanTests` (per-folder undo), `ThemeContrastTests` (new, 4.5:1 for TextDim and TextMuted on page and sidebar).
Done: the contrast test passes for all six themes, and Reset can leave one folder's entries in place.
Tier 2: item 10.

## Disposition of every finding

| WF | Disposition | Where |
| --- | --- | --- |
| 1, 2 | Fixed in 4.0, slice 0 | Slice 0 |
| 3 | Resolved: pack switches, "Where it shows", palettes without Custom; effects labelled unused | Mods & Widgets; slice 4 |
| 4 | Resolved for 4.0: every folder's mods in one list with scope; full cross-folder catalog (R2) deferred | Mods & Widgets; slice 4 |
| 5 | Resolved: Order buttons, Arrange arrow keys, map caption | Desk; slice 3 |
| 6 | Resolved: Privacy, What leaves this PC; update switch; catalog fetch on expand | Slice 6, slice 4 |
| 7 | Resolved: streamer mode; demo mode unchanged in this draft (a starter is open for the owner) | Slice 7 |
| 8 | Resolved: Pieces with Show, default places, status on feature pages | Slice 3 |
| 9 | Resolved: meters mod controls on Mods & Widgets only; Off vs Remove stated | Slice 4 |
| 10 | Resolved: "Let Claude…" wording, What Claude can change | Slices 1, 6 |
| 11 | Resolved: "Claude Code glow hook", "Claude Code glow" section and piece | Slices 1, 4 |
| 12 | Resolved: Desk Look | Slice 3 |
| 13 | Resolved: Alerts, Each limit | Slice 5 |
| 14 | Resolved: meter history, session archive, transcripts | Slices 1, 5 |
| 15 to 21 | Resolved: names contract, landing rule, Reset label, core words | Slices 1, 2, 8 |
| 22 | Resolved: Desk split and jump links | Slice 3 |
| 23 | Resolved: synonyms and the Advanced suffix | Slice 2 |
| 24 | Resolved: Widget page | Slice 5 |
| 25 | Resolved: reopen in session, `--settings`; URL scheme deferred | Slice 2 |
| 26 | Resolved: wrapping rows, horizontal scroll fallback | Slices 2, 3 |
| 27 | Resolved: folder cards, Accounts' Linked folders | Slices 4, 5 |
| 28 | Resolved: sample record, plain project-name disclosure | Slices 5, 6 |
| 29, 30, 31 | Resolved: Data section copy, Label prompt, plain key help | Slices 1, 5 |
| 32 | Resolved: signed-out text, Show the widget, reminder moved. "Linked account" with only "None" now shows "Add an account to link this folder." in place of the combo | Slices 2, 3, 5 |
| 33 to 36, 38 | Resolved | Slice 2 |
| 37 | Resolved | Slice 8 |
| 39, 40 | Resolved for what 4.0 can switch; other Claude Code mods read-only and labelled (generic switch needs R1 and R6) | Slice 4 |
| 41 | Resolved: work-area clamp, ScrollViewers, map folding | Slices 2, 3 |
| 42 to 45, 47, 48 | Resolved | Slice 1 (45 in slice 2) |
| 46 | Deferred: color wells on Ink, Message color, Launcher glow and Studio. Hex entry works and is captioned; wells are a control project of their own with no blocker behind them | Not in 4.0 |
| 49 | Resolved | Slice 8 |

## Not in 4.0

- **An OBS-friendly capture source for the Desk.** A window or virtual source a streamer can add to OBS, showing the Desk with streamer mode's masking. A later sub-project, after slice 7.
- **Masking MCP answers.** `get_local_burn_by_project` and similar tools return real project names to agents, whose transcripts may be on screen. Masking needs `StreamerMask` source-linked into `Sanduhr.Mcp`, which widens the trust-boundary allowlist: a design decision that must break CI on purpose.
- **Switching other Claude Code mods** (mods-parity R1 kept mods, R6 agent proposals, and the CLI route for marketplace plugins).
- **The cross-folder Claude Code catalog and Compare** (R2, R9), Mod Studio (R3), config packs (R4), statusline segments (R5).
- **`sanduhr://settings/<page>#<anchor>`.** It needs a manifest protocol extension and a Store review; `--settings` covers the need in 4.0.
- **Live previews** for Desk Look, Message and Now playing (inventory W19).
- **Color wells** (WF46).
- **A demo-mode starter** in the tray or Settings. Demo mode stays command-line only in this draft; whether to add a starter is open for the owner (WF7).

