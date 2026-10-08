# Settings v2 for Sanduhr for Mac

Spec, 2026-10-07, against 2.10.0 (build 17). Inputs: the 20 writer inputs in `docs/settings-v2-inputs.md` (cited as **W1** to **W20**), the setup guide `docs/setup-guide.md` (both on `docs/setup-guide`), `docs/mods-and-config-spec.md` (on `docs/mods-and-config-spec`, checklist item 71), the current Settings code (`mac/Sources/Sanduhr/Views/SettingsWindow.swift`, `Desk/SettingsView.swift`, `Desk/NowPlayingSettings.swift` and the views under `Views/`), and a fresh-agent pass on a live build: four personas following the guide on a throwaway profile, signed out, with no mouse and no accessibility access (cited as **P1** first-timer, **P2** daily Claude Code developer, **P3** two accounts and privacy, **P4** small screen with many windows). Smoke evidence paths are under `mac/smoke/out/`.

## Verdict: Settings is organized by where code lives, and v2 organizes it by what a person is trying to do

Settings today is seventeen pages that mirror the app's modules: General, Alerts, Accounts, Claude Usage, Integrations, Mods, then six Desk pages, three Widget pages, Updates and About. Every page works on its own, and the window fits a 13-inch screen. The trouble is between pages. One feature has two or three homes (the notch switch in General and on Notch, watchers across Integrations, Notch and Layout, the meters mod in Integrations and Mods), one control has two names ("Menu Bar Shows" and "Percent beside the hourglass"), buttons that open Settings name pages that don't exist ("Desk Layout Settings…" lands on "Layout"), and the controls the guide names sit below a 160 pt preview where nobody without a scroll wheel finds them. Signed out, the app and its Settings explain least at exactly the moment a newcomer needs them most, and there is no way back to a clean slate without Terminal.

Settings v2 keeps the one window, the sidebar and the live previews, and changes four things. **Pages follow tasks**: sixteen pages in five groups, each feature with exactly one home, and every other mention a labelled link to it. **Names are a contract**: a control has one name in Settings, menus, the tour, What's New and the guide, and a button that opens Settings says the page's exact name. **Everything is reachable without scrolling blind**: search in the sidebar, deep links to a section, previews that shrink on short windows, and new rows scrolled into view. **Settings can undo itself**: an in-app Reset that also removes what Sanduhr wrote into Claude Code folders. Mods & Config (item 71) moves in as its own page under a Claude Code group. One Arrange Desk defect blocks a first-timer outright and ships first, ahead of the restructure.

## Findings, ranked

Duplicates across the writer inputs and the four personas are merged; each finding lists every source. Severity follows the persona scale: **blocker** stops the task, **confusing** makes a person stop and guess, **polish** is wrong but survivable. Where a persona's reading contradicted the code, the code was checked and the finding says what it actually is.

### Blockers

**F1. Arrange Desk can strand the user: the Cancel and Done bar is zero size, and the pieces stay under other windows.** Sources: P1 (blocker), P4 (blocker), P2. Evidence: `smoke/out/20261007-212317-t3-arrange/tree.yaml`, `20261007-212713-arrange/tree.yaml`, `20261007-214044-arrange/tree.yaml`, `20261007-214100-arrange2/tree.yaml`: window "Arrange Desk" at frame `[900, 604, 0, 0]` while state reports `bar_visible: true`, measured twice with a delay. While arranging, the Desk takes every click (`click_through: whole`), so with no bar the only exits are Return and Escape, which nothing on screen mentions. With a working window covering the screen, the outlined pieces stay underneath it (P4). Likely cause, not yet confirmed: `DeskArrangeBarController.place(on:)` sizes the panel from `contentView?.fittingSize` before the hosting view has laid out, which can read as zero (`Desk/DeskArrangeViews.swift:288-290`); the bar panel is `.floating`, but the Desk window itself stays at desktop level, so pieces are covered by any normal window.

### Confusing: one feature, several homes

**F2. The Desk and the notch are switched on in two places under two names.** Sources: W2, P1, P4. General says "Notch: the island around the camera (needs Desk)"; Notch says "Extend the camera notch". The Desk can only be turned on in General; Layout says "Turn on Desk to arrange it on the desktop" with no switch.

**F3. Watchers live on three pages, and the waiting glow silently needs a placement.** Sources: W5, W19, P2. The switches are in Integrations, placement is a wing choice on Notch and a piece on Layout, and the caption still says "a Desk corner" where Layout has eight places. P2 started a watcher, set it waiting, and saw no glow (`smoke/out/20261007-212632-watcher`: `glow_count` stayed 1, `watchers.placements: []`). The code is working as written: `WatcherStore.startForApp()` fires the glow only when `WatcherPlacement.places(in:)` is non-empty (`Services/WatcherStore.swift:201-206`). Nothing on screen says so.

**F4. The meters mod has two sets of controls on two pages.** Sources: W7, P2. Integrations has "Meters above the prompt" with Install…, Update and Remove per folder; Mods has "Sanduhr's mod: sanduhr-meters" with an On switch, Update and Remove per folder. Only a caption ("installed from Integrations") connects them, and neither page says how Off differs from Remove. Evidence: `smoke/out/20261007-212542-integrations`, `20261007-212616-mods`.

**F5. Claude meters are named three ways, and camera and mic placement has two controls.** Sources: W16, W17. General: "Show the Claude meters on the desktop"; Layout: "Claude line" and "Claude meters (bars)". On Notch, a wing can be set to "Camera and mic", and separately "Beside the camera" picks a side.

**F6. Limit warnings sit under Desk but change the widget too, and fonts are split across two Look pages with one icon.** Sources: W14, W15. Meters (under Desk) sets each limit's red warning and the hide-a-temporary-limit choice, both of which change the widget and the alerts. Desk Look and Widget Look share the `textformat` symbol.

### Confusing: names that don't match

**F7. The menu bar choice has two names, and the guide's "same menu everywhere" isn't true.** Sources: W3, P1, P3. "Menu Bar Shows" exists only in the menu bar item's own menu (`Models/SanduhrMenu.swift:120-126`, item 40); the shared Sanduhr menu that the widget and Desk use has no such item, and General calls the same setting "Percent beside the hourglass". Correction to P1: `menuIcon = 0` is the separate Desk meetings menu (General, "Desk menu in the menu bar"), not the hourglass, so the hourglass being present is not in doubt.

**F8. Buttons that open Settings name pages that don't exist.** Sources: W1, W4. "Desk Layout Settings…" opens a page titled "Layout". The notch island's click says it opens "these settings" (Notch) but calls `showSettings()` with no page, so it opens wherever Settings was left (`Desk/NotchView.swift:252`).

**F9. One glow, four names.** Source: W8. "Notch glow when Claude needs you", "the notch glow hooks", "Notch, Glow" and the section "Glow for Claude Code".

**F10. The same command is spelled two ways, and edit-mode buttons are uneven.** Sources: W10, W13. "Save & Apply" vs "Save and Apply"; "Check Now" vs "Check for Updates…"; "Edit as text…" opens no window while "Edit as a list" has no ellipsis.

**F11. The Message editor's terms drift from its controls.** Sources: W9, W11, W12. "Look" is named only in a tooltip over Color, Glow, Size, Letters and Motion; pinning is a per-row button in list mode and a separate field in text mode; the `Mon:` reference says "instead of every-day lines" and ignores the Mix switch.

### Confusing: reach and layout

**F12. Controls the guide names are below the fold, and there is no way to jump to them.** Sources: P4, P2, P1. On a 628 pt window, Notch's Size, Glow and Camera and mic, Layout's Order and Margins, Now Playing's "When nothing is playing" and Apps, and Desk Look's "Clock and message take clicks" all sit under a roughly 160 pt preview. Integrations' Watchers section can't be reached without scrolling and has no deep link. Evidence: `smoke/out/20261007-214120-s-notch`, `20261007-214032-layout`, `20261007-214125-s-nowPlaying`, `20261007-214117-s-deskLook`.

**F13. Add Line is below the fold, and a new line opens off screen.** Sources: P1, P4. The top of Message has Change the line, Mix…, On special days, Save and Revert, Edit as text…; Add Line is under the list. After adding, the new row's editor opened at y≈1035 in a window that ends at y=793, so the only sign of the new line was "Unsaved changes". Evidence: `smoke/out/20261007-212252-t5-add`, `20261007-214148-msg-add`.

**F14. Integrations' order contradicts its own captions.** Sources: W6, P2. "Install it for a folder below" sits below the folders; Add Folder… is at the very bottom, away from the list. On Mods, the "Mods and plugins" heading and its five-line paragraph sit above Sanduhr's own section, which contradicts the guide's order.

**F15. Option+S, the advertised way into Settings, works only while the Desk is on.** Source: W18. General: "Work in every app while Desk is on."

**F16. A dependent control turns on and does nothing, with the reason further down the page.** Sources: P1, guide task 4. "Text under the camera too (desktop only)" switches on but "Under the camera" stays dim until "Extra height below" in Size is above 0 (`Desk/SettingsView.swift:597-599`). Evidence: `smoke/out/20261007-212351-t4-chin`.

### Confusing: accounts, privacy and Claude Code folders

**F17. The first account is always called Personal, and the Data section is invisible until sign-in.** Sources: P3. Signed out, Accounts has only Sign In to Claude…, sessionKey, cf_clearance and Save, with "It becomes your Personal account". A work account signed in first is labelled Personal. Share with your agents (Off by default) can't be seen before signing in, though `state.yaml` reports `data.share: "off"`. Evidence: `smoke/out/20261007-213808-t6-accounts`.

**F18. Which account a Claude Code folder follows is decided in Accounts and invisible in Integrations.** Sources: P3. Correction to P3: the link exists. Each account's Data section has a "Claude Code folder" picker, and a folder belongs to one account (`Views/AccountDataSection.swift:185-193`, "A Claude Code folder belongs to one account"). But Integrations shows no link, and its text says sessions "show the active account's" meters, so a work folder's statusline shows personal meters whenever Personal is active. Evidence: `smoke/out/20261007-213820-t7-integrations`.

**F19. The key-finding help is developer shorthand, Chrome only.** Sources: P1, P3. Accounts shows "claude.ai → DevTools (⌥⌘I) → Application → Cookies → sessionKey"; the guide has plain Chrome and Safari steps. Evidence: `smoke/out/20261007-212217-p-credentials`.

**F20. There is no in-app reset, and the documented reset leaves Claude Code folders edited.** Sources: W20, P2. A full reset needs Remove Account per account, deleting a folder and two `defaults delete` commands. The integrations and the meters mod write into each Claude Code folder's `settings.json`, outside Sanduhr's files, so deleting Sanduhr's folder also deletes the receipts that Remove needs to restore them.

**F21. Mods copy uses words it never explains.** Source: P2. "By receipt", "Sanduhr keeps a receipt", "its plugin folder list", and a "27 on" tile that doesn't say what is on. Integrations says the same thing plainly ("adds one entry to that folder's settings, keeps a backup of the file beside it").

**F22. Demo mode fills the Desk and notch but not the widget, the meters or Accounts, and the guide never mentions it.** Source: P3. Evidence: `smoke/out/20261007-213833-demo-accounts`, `20261007-213851-demo-widget`, `20261007-213911-demo-tour`.

### Confusing: the signed-out first run

**F23. The signed-out widget says "Connecting..." forever and offers "Use Sonnet".** Sources: P1, P3. The welcome sheet (440 pt) is wider than the widget (340 pt), floats above every app, and has no way to put it off (P4). Evidence: `smoke/out/20261007-212139-t1-initial/tree.yaml`, `20261007-214006-initial`.

**F24. Two Welcome surfaces, and What's New is a changelog on a fresh install.** Sources: P1. The replayed tour is titled "Welcome to Sanduhr", the same as the sheet still open; step 1 says "Your meters show here after Sanduhr's first fetch" without mentioning sign-in. What's New shows "New in 2.4.0 – 2.10.0" to someone who never had those versions, with a blank title bar. Evidence: `smoke/out/20261007-212409-t12-tour`, `20261007-212401-t12-whatsnew`.

### Polish

**F25. An empty notch wing is a wide black slab.** Sources: P1, P4. Signed out with the right wing on Claude meters, the island is about 671 pt with an empty right half. Evidence: `smoke/out/20261007-212225-p-notch`, `20261007-214210-demo`.

**F26. Arrange Desk skips empty pieces and labels collide.** Sources: P1, P2, P4. The Claude line, placed bottom left, gets no outline while signed out; the "Meetings" chip covers the clock's date line. Evidence: `smoke/out/20261007-214044-arrange/screen-desk.png`.

**F27. Desk Look reads like code.** Source: P1. "Glow around the message ({glow} and {noglow} change one line)" and "Colors (one hex, or several with commas for a gradient)" with raw hex. Evidence: `smoke/out/20261007-212223-p-deskLook`.

**F28. Small copy errors.** Sources: P1, P3. "Nothing else on the calendar today" when the day had no events (`Desk/DeskController.swift:718`); "Requires an active Claude Pro / Team / Enterprise subscription" leaves out Max (About and the tour footer).

**F29. A dev build's credential store isn't labelled.** Source: P3. `state.yaml` reports `credentials_store: file` while the welcome sheet says the key is in the Keychain. Dev builds use the file store on purpose (the Keychain partition list defeats self-signed dev certificates); release builds use the Keychain. The UI should say so in a dev build only.

**F30. Test Glow may be too short to catch on a busy screen.** Source: P4, unconfirmed (the snapshot may have missed the timing). Evidence: `smoke/out/20261007-214227-glow`.

**F31. Integrations and Mods render as blank panels in the smoke tool's in-app render.** Source: P2, likely a render-path artifact (Notch rendered fine in the same session, and on-screen capture was unavailable). Blocks review, not users; fix in the smoke renderer if real windows are fine.

### What already works, and stays

The sidebar groups and every page name the guide uses match; the General surface labels match the guide word for word; the window fits a 13-inch screen with all pages visible; the live previews (item 68), including the Notch sample with Test Glow while signed out; the Message rows drawn as the Desk draws them with plain summaries and Save and Revert; Integrations' trust copy ("Nothing leaves this Mac") and its per-folder boxes; the Now Playing page's "Not placed anywhere" with buttons to place it; Arrange Desk's Cancel restoring the layout; Settings reopening on the page it was on after Arrange.

## The new information architecture

### Principles

1. **One home per feature.** Each switch, picker and slider is stored once and shown once as a control. Every other page that mentions it shows a one-line status ("Watchers: on, on the right wing") and a link button to its home. A status line is never a second control.
2. **One name per control.** The name in Settings is the name in every menu, the tour, What's New, the guide and the smoke vocabulary. A table of names lives in code (`SettingsNames.swift`) and a unit test checks every menu item and tour string that refers to a setting against it.
3. **Buttons name pages exactly.** A button that opens Settings reads "<Page> Settings…" with the page's sidebar title, and opens that page scrolled to the named section. Links inside a page read "Change in <Page>…".
4. **A dependency says itself.** A control that needs another setting either sets a sensible value for it when turned on, or shows a one-line hint right under it naming the other control and page.
5. **Signed out is a state, not a failure.** Every page that shows account data has a signed-out form that says what will appear and offers Sign In.
6. **Previews stay.** Every page that controls something visible keeps its live preview on top (item 68), drawn by the real views.

### Sidebar: sixteen pages in five groups

| Group | Page | Symbol | Was |
| --- | --- | --- | --- |
| (none) | **General** | `gearshape` | General, minus the Surfaces switches |
| | **Accounts** | `person.2` | Accounts |
| | **Usage** | `chart.bar.xaxis` | Claude Usage |
| | **Alerts** | `bell` | Alerts plus Desk, Meters |
| Desktop | **Desk** | `rectangle.3.group` | Layout, the Desk switch, Desk Look's Clicks |
| | **Desk Look** | `paintbrush` | Desk Look |
| | **Message** | `text.quote` | Message |
| | **Notch** | `rectangle.topthird.inset.filled` | Notch, the notch switch, Desk Look's Notch text color |
| | **Now Playing** | `music.note` | Now Playing |
| | **Watchers** | `eye` | Integrations' Watchers, plus watcher placement |
| Claude Code | **Claude Code** | `puzzlepiece.extension` | Integrations, including the meters mod |
| | **Mods & Config** | `cube` | Mods, per item 71 |
| Widget | **Widget** | `textformat` | Widget Look plus Pacing & Focus |
| | **Themes** | `paintpalette` | Themes |
| Help | **Updates** | `arrow.triangle.2.circlepath` | Updates |
| | **About** | `info.circle` | About, plus Reset and where settings live |

That is sixteen pages, down from seventeen, but the count matters less than the homes: every merge above removes a duplicate. Desk and Notch keep their own pages because their previews differ and each is long enough on its own.

### General

What the app is doing and how to reach it.

- **Surfaces** (status, not switches): one row each for Desk, Notch, Widget and the menu bar, each with its state and a "<Page> Settings…" button. "Desk: on. Desk Settings…". The surface switches move to their pages (F2).
- **Menu bar**: **Menu Bar Shows** (Session, Weekly, Whichever is higher, Rotate), the one name for what General called "Percent beside the hourglass" (F7). **Meetings menu in the menu bar**, the renamed "Desk menu in the menu bar (meetings, join, settings)", because the menu it adds is the meetings menu.
- **Startup**: Open Sanduhr at login.
- **Shortcuts**: **Option+S opens Settings** and **Option+J joins the next meeting** as two switches. Both work whenever Sanduhr runs, not only while the Desk is on (F15). The caption keeps the warning that, while on, Option+S and Option+J no longer type ß and ∆.
- **Quit Sanduhr für Claude**, with its caption.
- Preview: the menu bar sample, as today.

### Accounts

- **Signed out**: Sign In to Claude… and Paste a Key Instead, as on the welcome sheet. **Label** (default Personal, editable before saving) so the first account is named by the person (F17). **How do I find this?** expands the guide's plain steps for Chrome and Safari; the DevTools shorthand goes (F19). cf_clearance stays behind "Seeing a Cloudflare error?".
- **Data, shown before sign-in** as a read-only preview of the defaults: Meter history On, Claude Code activity Off, Share with your agents Off. "These apply to the account you sign in with; change them after." (F17)
- **Signed in**: the account list, Add Account…, Rename, Sign Out, Remove Account…, Make Active, Follow the account I'm using; Data per account as today: Meter history, Claude Code folder, Claude Code activity, Project names in the record, Share with your agents, Work account, Erase this account's data….
- **Dev builds only**: a caption under the key field, "Dev build: the key is kept in a file, not the Keychain" (F29).
- Deep link: `#data` scrolls to Data for the selected account (exists today as `scrollToData`).

### Usage

Unchanged, plus a signed-out state: "Sign in to see your usage here", with Sign In and Accounts Settings….

### Alerts

One home for everything that warns about limits, on the widget, the Desk, the notch and in Notification Center (F6).

- **Notifications**: the current Alerts page (Notifications, Session and Weekly thresholds, the reset and pace switches, Where alerts show, Sound, Quiet hours, Send a Test, Notification Settings).
- **Each limit**: one group per limit (from Desk, Meters): **Warn when nearly full**, **At** a percent, **Only while the reset is more than**, and, for a temporary limit, **Show this limit**. The caption says these change the widget, the Desk and the notch.
- Preview: the meter bars in their states (moved from Desk, Meters).

### Desk

The Desk's one home: on, where things go, how it takes clicks.

- **Desk** switch at the top, named **Desk: clock, meters, meetings and the message on the desktop** (the General wording the guide uses). Every other page that needs the Desk shows "Needs the Desk. Desk Settings…" (F2).
- **Arrange Desk…** with its caption, which now names the keys: "Return keeps it, Escape puts it back."
- **Where each piece sits**: a place and size per piece. Pieces are named once and everywhere: **Message**, **Clock and date**, **Claude meters (line)**, **Claude meters (bars)**, **Now playing**, **Watchers**, **Meetings** (F5). Watchers and Now playing rows carry a link to their own pages.
- **Order**, as today.
- **Clicks**: **Clock and message take clicks** (moved from Desk Look, because it is behavior, not look).
- **Advanced** (collapsed): **Margins**.
- Preview: the screen-shaped map.

General's "Show the Claude meters on the desktop" goes: the two Claude meters pieces set to Hidden is the same thing, and the General switch duplicated it (F5).

### Desk Look

- **Fonts**: Desk font, Message font.
- **Sizes**: Clock, Message.
- **Colors**: a preset picker first, then color wells for Message and for Clock, date, meetings and Claude; one well, or two to four for a gradient (F27). The hex field moves under **Advanced** for those who paste values.
- **Glow around the message** and **Drop shadow under the clock text**, without the tag syntax in the label; the `{glow}`/`{noglow}` note moves to Message's text mode.
- Notch text color moves to Notch.
- Preview: a Desk corner, as today.

### Message

- **Top bar, always visible above the list**: **Add Line**, **Edit as Text**, Save, Revert, "Unsaved changes" (F13). The two mode buttons become a segmented control, **List | Text**, so neither has an ellipsis or a different shape (F10).
- **Rotation** (collapsed summary when not in use): Change the line, Mix every-day lines in on days with their own line, On special days, Each line shows for.
- **The list**: each row drawn as the Desk draws it. Add Line inserts the new row at the top of its day group, scrolls it into view, and opens its editor.
- **Row editor**: When, Text, then a visible **Look** group holding Color, Glow, Size, Letters and Motion (F11). Pin is a button on the row in both modes; text mode shows the pinned line with a pin glyph in the gutter (see Open questions on how it is stored).
- **Text mode**: the file, the tag list beside it, the `{glow}`/`{noglow}` note, and a corrected `Mon:` reference: "a line for Mondays; with Mix on, it takes turns with the every-day lines".
- Preview: the message as the Desk draws it.

### Notch

The notch's one home.

- **Notch** switch at the top, named **Notch: the island around the camera**, with "Needs the Desk. Desk Settings…" while the Desk is off (F2). "Extend the camera notch" goes.
- **Text**: Text beside the camera, Left wing, Right wing; **Text under the camera too**. Turning that on when Extra height below is 0 sets it to 18 pt, and a hint reads "Height is in Size, below" (F16). The wing choices drop "Camera and mic" (see Camera and mic).
- **Empty wings**: a wing whose content has nothing to show collapses to the notch edge (Claude meters before sign-in, Message with no line), the way Watchers and Now playing already give way (F25). Signed out, Claude meters shows "Sign in" in the wing instead, as one choice for the open question below.
- **Glow**, one section, one name, **Notch glow** (F9): For Sanduhr alerts, A minute before a meeting, When the camera fill light comes on, When Claude Code is waiting on you, When Claude Code finishes, Not while a terminal is in front, Test Glow. The two Claude Code rows show "Needs the Claude Code glow hook in a folder. Claude Code Settings…" until one is installed. Test Glow runs for 3 s, long enough to see (F30).
- **Camera and mic**: Show the red dot, Pulse the dot gently, Show a mic while the microphone is on, and one placement picker, **Where they show**: Beside the camera, left; Beside the camera, right; In the left wing; In the right wing; Under the camera (F5, W17).
- **Camera fill light**: Light up for the camera, Brightness.
- **Advanced** (collapsed): **Size** (Extra width each side, Extra height below), **Notch text color** (moved from Desk Look).
- Preview: the island at true proportions.
- The island's click opens this page (F8).

### Now Playing

As today, with the placement buttons renamed **Notch Settings…** and **Desk Settings…**, and its status line ("On the right wing", "Not placed anywhere") following the shared status format.

### Watchers

The watchers' one home, switches and placement together (F3).

- **Let agents show watchers**, **Show Claude Code's background work** (with "Needs the Claude Code glow hook. Claude Code Settings…" until installed).
- **Where they show**, inline: **On the notch** (Off, Left wing, Right wing, Under the camera), writing the same notch keys as the Notch page's wing pickers; **On the Desk** (Hidden or one of the eight places), writing the same layout string as Desk. One stored value each, two editors on purpose: this is the exception to "one control", and the Notch and Desk pages show Watchers as a choice that reads and writes the same key, so they never disagree.
- **Glow when a watcher waits on you**: a line stating the rule the code already follows: "The notch glows once when a watcher waits on you, if watchers show somewhere above." Turned into a switch only if someone asks.
- **Above the prompt**: **Show watchers above the prompt**, with the meters mod's status per folder and "Claude Code Settings…".
- Preview: a sample watcher card (exists in Integrations today), moved here.

### Claude Code

Was Integrations. One home for what Sanduhr installs into Claude Code folders, the meters mod included (F4).

- **Intro and Python**: the trust copy as today, Python found, Install Command Line Tools…, Check Again.
- **Folders**, at the top with **Add Folder…** beside the heading (F14). One box per folder:
  - **Account**: the linked account from Accounts' Data, or "Follows the active account", with "Change in Accounts…" (F18).
  - **MCP server**, **Statusline**, **Meters above the prompt**, **Claude Code glow hook** (the one name for "notch glow hooks" and "Notch glow when Claude needs you", F9). Each row: status, Install… or On/Off, Update, Remove.
  - **Meters above the prompt is the sanduhr-meters mod**, and its row is its only control: **On** and **Off** switch the mod for the folder (Off keeps Sanduhr's mod files and takes the folder's entry out), **Remove** also deletes the receipt and restores the folder's `settings.json` byte for byte. The row says so in one line.
  - Each installed row names the file it edits (`<folder>/settings.json`, or `~/.claude.json` for the MCP server) and where the backup and receipt are kept (Sanduhr's Application Support folder, per item 71's slice 0).
- Deep links to each folder box and each row.

### Mods & Config

Item 71, unchanged in substance. This spec only fixes where it sits and what it no longer does: the Sanduhr mod switch leaves the page (it lives in Claude Code, F4) and the page lists sanduhr-meters read-only, like every other mod, with "Claude Code Settings…". Copy changes from F21: "receipt" becomes "a record in Sanduhr's folder, so Remove can put the file back exactly"; the banner tiles read "1 mod", "27 plugins", "27 enabled plugins", "2 folders"; the "Mods and plugins" heading sits directly above the list it describes (F14). Item 71's seat picker, eight sections, wall and secret rules apply as written there.

### Widget

- **Show the widget** (Always, With the Desk off, Never: moved from General's Surfaces) and **Show the widget now**.
- **Font**, Use System Font, Open Font Book; **Subtle mode**.
- **Pacing calculators**: Pin the pacing calculators on every card (was Pacing & Focus).
- Preview: the widget.

### Themes

As today; the one spelling is **Save & Apply** everywhere (F10).

### Updates

**Check for Updates…** becomes the one name, in menus and as the page's button (it is a button that starts a check and shows a sheet, so the ellipsis is right); Check for updates automatically; Download and install updates automatically (F10).

### About

- Version, What's New…, Take the Tour…, Links, Third-Party Notices.
- **Plans**: "Works with a paid Claude plan (Pro, Max, Team or Enterprise)" here and in the tour footer (F28).
- **Where settings live**: the two preferences domains, the Application Support folders and the Keychain, each with a Show in Finder where it is a folder.
- **Reset Sanduhr…** (below).

### Search

A search field at the top of the sidebar, like System Settings. The index is built from the names table: every page, section and control title, plus a short synonyms list per entry (for example "percent", "hourglass" → Menu Bar Shows; "corner", "position" → Where each piece sits; "statusline", "prompt" → Claude Code; "reset", "start over" → Reset Sanduhr…). Typing filters the sidebar to matching pages with the matching control names under each; choosing one opens the page, scrolls to the control and highlights it for 1.5 s. No fuzzy ranking beyond prefix and word match. This also answers "how do I reach Glow without scrolling" (F12).

### Deep links

Every page and section gets an anchor. `sanduhr://settings/<page>` and `sanduhr://settings/<page>#<anchor>` (and the same on `estedesk://`) open there; today's host-only `sanduhr://settings` still opens where Settings was left. `SettingsWindowController.show(_:anchor:)` takes the anchor; every "<Page> Settings…" button passes one. The smoke hook `settings <page> [<anchor>]` uses the same anchors, so Watchers, Glow and Add Line can be reached and checked without scrolling (F12, P2's suggestion).

### Short windows and the previews

Below 600 pt of window height, a page's preview collapses to a 44 pt strip with its title and a disclosure arrow; opening it pushes the controls down, and the choice is remembered per page for the window's life. At 600 pt and above, previews show in full as today. (Was 720 pt; on 2026-10-08 the owner moved the line to 600 so the default 680 pt window opens with full previews and only a small screen squeezed short folds.) The Settings window's default height grows from 600 to 680 pt where the screen allows, and the existing `fitToScreen` still caps it.

### Simple and Advanced: no global switch, collapsed sections instead

A global Simple/Advanced mode isn't warranted. It would hide controls the guide names, give every page two layouts to test, and make search results disappear depending on a mode the person forgot they set. Instead, the few expert controls collapse under an **Advanced** disclosure on their own page: Desk's Margins, Desk Look's hex fields, Notch's Size and Notch text color, Mods & Config's sections beyond the summary (item 71 already shows each section collapsed with a one-line summary). Search always finds them and opens the disclosure.

### Reset Sanduhr…

On About, a sheet with what will be removed, each a checkbox, all checked by default:

- **Claude Code folders**: runs Remove for every receipt (integrations and the meters mod), listing the folders and rows first. Runs before anything else, because the receipts live in the Sanduhr folder that a later step deletes (F20).
- **Accounts and keys**: Remove Account for each account, Keychain items included.
- **History and records**: the vault and usage history.
- **Desk messages** (`messages.txt`, kept as `messages.txt.previous` beside it).
- **Your themes**.
- **All settings**: both preferences domains.

**Reset and Quit** does the checked steps in that order, reports any that failed (a Claude Code folder whose file changed underneath, say) without stopping the rest, and quits; the next launch starts fresh. Unchecking Claude Code folders leaves those entries in place, and the sheet warns that they then can't be removed from Sanduhr later. A smoke action `reset --dry-run` reports what each step would remove, so the sheet is testable without destroying a profile. The guide's Terminal reset stays as the fallback for when the app won't open.

### Outside Settings, same release

These aren't Settings pages but are the same findings, and Settings v2 is incomplete without them:

- **Arrange Desk (F1, F26)**: the bar gets a real size before it is shown (a fixed size or a layout pass first), and a check fails when `bar_visible` is true but the frame is 0×0; while arranging, the Desk window rises above normal windows (or other windows are hidden, as Settings already is) and returns to desktop level when it ends; empty pieces draw a placeholder outline ("Claude meters (line): sign in to see it"); a label chip that would overlap the piece above moves inside its own outline.
- **Signed out (F23, F24)**: the widget says "Not signed in" instead of "Connecting...", and hides model buttons until there are numbers; the welcome sheet gets **Not Now**, which collapses it to a one-line "Sign in" chip on the widget, and is no wider than the widget; the replayed tour is titled **Sanduhr Tour** and its first step says "Sign in to see your meters here" with Sign In; on a first run, What's New shows **Highlights** (the current version's cards) instead of a version range, with a window title.
- **Shared menu (F7)**: Menu Bar Shows goes into the shared Sanduhr menu, so the guide's "the same menu everywhere" becomes true.
- **Copy (F28)**: "Nothing on the calendar today" when the day had no events, keeping "else" for after the last one.
- **Demo mode (F22)**: fills the widget and both Claude meters pieces with sample meters, shows two sample accounts ("Work" and "Personal") in Accounts and on the widget chip, and gets a line in the guide's Get help as the safe way to take screenshots.

## Mapping: every current control to its new home

Nothing is dropped. Storage keys stay unless stated, so existing installs keep every choice. "Same key" means the control moves and the `UserDefaults` key and domain are unchanged.

| Current page | Current control | New home | Migration |
| --- | --- | --- | --- |
| General | Desk: clock, meters, meetings and the message on the desktop | Desk, top switch | Same key (`deskEnabled`, desk domain). General shows status. |
| General | Notch: the island around the camera (needs Desk) | Notch, top switch | Same key. "Extend the camera notch" label retired; both toggles already share the key. |
| General | Widget: the floating window with the tools | Widget, Show the widget | Same key. |
| General | Show the widget now | Widget | Same behavior. |
| General | Open Sanduhr at login | General, Startup | Same. |
| General | Read today's meetings | Desk, under Where each piece sits, on the Meetings row | Same key. |
| General | Show the Claude meters on the desktop | Desk, Where each piece sits (both Claude meters pieces) | Retired. On first launch of v2, if it was off, both Claude meters pieces are set to Hidden in the layout string; the key is then ignored. |
| General | Option+J joins the next meeting, Option+S opens these settings | General, Shortcuts, two switches | One key becomes two; both seeded from the old key. Hotkeys register while Sanduhr runs, not only with the Desk. |
| General | Quit Sanduhr für Claude | General | Same. |
| General | Percent beside the hourglass | General, Menu bar, **Menu Bar Shows** | Same key (`menuBarMode`); renamed. |
| General | Desk menu in the menu bar (meetings, join, settings) | General, Menu bar, **Meetings menu in the menu bar** | Same key (`menuIcon`, desk domain); renamed. |
| Alerts | every control | Alerts, Notifications | Same keys. |
| Accounts | Sign In, sessionKey, cf_clearance, Save | Accounts, signed out | Same; Label field added (defaults to Personal). |
| Accounts | Add Account…, Rename, Sign Out, Remove Account…, Make Active, Follow | Accounts | Same. |
| Accounts, Data | Meter history, Claude Code folder, Claude Code activity, Project names, Share with your agents, Work account, Erase | Accounts, Data | Same; also shown read-only before sign-in; the folder link also shows in each Claude Code folder box. |
| Claude Usage | every control | Usage | Same; section raw value `usage` kept. |
| Integrations | Python, Install Command Line Tools…, Check Again | Claude Code | Same. |
| Integrations | Add Folder… | Claude Code, beside Folders heading | Same action. |
| Integrations | MCP server, Statusline (Combine/Replace) | Claude Code, folder box | Same receipts. |
| Integrations | Meters above the prompt | Claude Code, folder box, as the sanduhr-meters row with On/Off, Update, Remove | Receipts unchanged; the Mods page's switch and this row already write the same entries (item 64's ModSwitch). |
| Integrations | Notch glow when Claude needs you | Claude Code, folder box, **Claude Code glow hook** | Same receipt; renamed. |
| Integrations | Watchers section (Let agents show watchers, Show Claude Code's background work, Show watchers above the prompt, Notch Settings…, Desk Layout Settings…) | Watchers | Same keys; placement buttons replaced by inline pickers on the same keys. |
| Mods | inventory, Check, risk card | Mods & Config | Per item 71. |
| Mods | Sanduhr's mod: On, Update, Remove | Claude Code, folder box | Same receipts; Mods & Config lists it read-only. |
| Layout | Arrange Desk… | Desk | Same. |
| Layout | Where each piece sits, Order | Desk | Same layout string; pieces renamed (Claude line → Claude meters (line)). |
| Layout | Margins | Desk, Advanced | Same keys. |
| Desk Look | Desk font, Message font, Clock and Message sizes | Desk Look | Same keys. |
| Desk Look | Message, Clock/date/meetings/Claude colors | Desk Look, color wells, hex under Advanced | Same keys and value format (comma-separated hex). |
| Desk Look | Glow around the message, Drop shadow under the clock text | Desk Look | Same keys; label shortened. |
| Desk Look | Notch text color | Notch, Advanced | Same key. |
| Desk Look | Clock and message take clicks | Desk, Clicks | Same key. |
| Meters | per limit: Show this limit, Warn when nearly full, At, Only while the reset is more than | Alerts, Each limit | Same keys (desk domain). |
| Message | every control | Message | Same `messages.txt` and keys; mode buttons become List/Text. |
| Notch | Text beside the camera, Left wing, Right wing, Text under the camera too, Under the camera | Notch, Text | Same keys; wing value "Camera and mic" migrates (next row). |
| Notch | wing or strip = Camera and mic; Beside the camera | Notch, Camera and mic, **Where they show** | A wing or strip set to Camera and mic becomes "In the <that> wing" / "Under the camera" and that place reverts to its default content; otherwise the side picker's value maps to "Beside the camera, left/right". One-time, idempotent. |
| Notch | wing or strip = Watchers | Notch (still a wing choice) and Watchers, Where they show | Same key, two editors. |
| Notch | Size: Extra width each side, Extra height below | Notch, Advanced | Same keys. |
| Notch | Glow, Glow for Claude Code, Test Glow | Notch, **Notch glow** | Same keys; one section. |
| Notch | Camera and mic: Show the red dot, Pulse, Mic | Notch, Camera and mic | Same keys. |
| Notch | Camera fill light, Brightness | Notch | Same keys. |
| Now Playing | every control | Now Playing | Same keys; buttons renamed. |
| Widget Look | Font, Subtle mode, Use System Font, Open Font Book | Widget | Same keys. |
| Pacing & Focus | Pin the pacing calculators on every card | Widget | Same (session-only state). |
| Themes | every control | Themes | Same. |
| Updates | Check Now, two automatic switches | Updates (Check Now renamed Check for Updates…) | Same. |
| About | What's New…, Take the Tour…, Links, Notices | About | Same; Plans line, Where settings live and Reset Sanduhr… added. |

### Section raw values, URLs and smoke

`SettingsSection` raw values are a public contract: `sanduhr://debug/action?name=settings&arg=<raw>`, the smoke scenarios, `state.yaml`'s `settings_section`, the tour's Show me, What's New's Show me, and the Desk menu items that carry a raw value (`Desk/DeskController.swift:436`). v2 keeps every raw value that still names a page and aliases the rest:

| Old raw value | New page | Handling |
| --- | --- | --- |
| `general`, `alerts`, `credentials`, `usage`, `deskLook`, `message`, `notch`, `nowPlaying`, `themes`, `updates`, `about` | same | Kept. `credentials` stays the raw value for Accounts. |
| `integrations` | Claude Code | Kept as the raw value. |
| `mods` | Mods & Config | Kept. |
| `deskLayout` | Desk | Kept as the raw value. |
| `deskMeters` | Alerts, `#each-limit` | Alias: parses, opens Alerts at the anchor, and `state.yaml` reports `settings_section: alerts`. |
| `widgetLook`, `pacing` | Widget | `widgetLook` kept as the raw value; `pacing` aliases to `#pacing`. |
| (new) `watchers` | Watchers | New. |

`state.yaml` gains `settings_anchor`. The smoke scenarios that expect `settings_section: deskMeters` (`silence-limit.yaml`, `meter-warning.yaml`) change to `alerts` in the same PR as the alias; `settings-sections.yaml` grows to cover every page and the aliases. The debug `settings` action takes an optional anchor. A new scenario, `settings-names.yaml`, opens each page and checks that every button whose title ends in "Settings…" lands on the page it names.

## Shippable slices

Each slice ships on its own, in the checklist's acceptance and verify style. Slice 0 is independent of the rest and ships first.

### Slice 0: Arrange Desk can always be ended

The bar gets a real frame before it is shown; while arranging, the Desk sits above normal windows; empty pieces get placeholder outlines; label chips never overlap a neighbour; the Layout caption names Return and Escape.

Acceptance: with a full-screen window open, Arrange Desk… shows every piece and a visible Cancel and Done bar centered on screen; signed out, the Claude meters (line) piece shows an outline; ending Arrange returns the Desk to desktop level and clicks pass through as before.
Verify: swift-testing for the bar's frame (never zero) and the placeholder outlines; smoke: `desk-arrange start` then `smoke tree window` reports the bar with a non-zero frame and a new `desk_arrange.bar_frame_ok` key, and a check fails when `bar_visible` is true and the frame is 0×0; by hand with a window covering the screen.

### Slice 1: names and links

The names table, one name per control (Menu Bar Shows, Notch glow, Claude Code glow hook, Claude meters (line) and (bars), Check for Updates…, Save & Apply, List/Text), every Settings button named "<Page> Settings…" with the page's exact title, the notch island opening Notch, Menu Bar Shows in the shared menu, and the copy fixes (Max, "Nothing on the calendar today", the `Mon:` reference, the Mods copy and tiles). No page moves yet.

Acceptance: every button ending in "Settings…" opens the page it names; the island opens Notch; the shared Sanduhr menu has Menu Bar Shows; no control has two names across Settings, menus, the tour and What's New.
Verify: swift-testing that every menu item, tour step and What's New card referring to a setting uses a title from the names table; smoke `settings-names.yaml`; the guide's tasks 2, 4, 7 and 8 updated in the same PR.

### Slice 2: one home per feature

The sidebar of this spec: the Desk and Notch switches on their pages with General showing status; Watchers as its own page with inline placement and the glow rule stated; Meters folded into Alerts; Pacing & Focus into Widget; Notch text color and Clicks moved; the meters mod's controls only in Claude Code; Camera and mic's one placement picker with its migration; the folder-to-account line in each Claude Code box; Add Folder… beside the folder list; Show the Claude meters on the desktop migrated into the layout.

Acceptance: each control in the mapping table appears as a control on exactly one page; an existing install's choices all read the same after the update (layout, wings, camera and mic placement, hotkeys, menu bar); a watcher set to waiting glows the notch when placed, and the Watchers page says why it doesn't when it isn't.
Verify: swift-testing for the migrations (Claude meters switch to Hidden pieces, Camera and mic wing to placement, the hotkey split, each idempotent on a second run) and for the section aliases; smoke scenarios updated (`deskMeters` → `alerts`), `settings-sections.yaml` covering every page; by hand on a profile copied from a 2.10.0 install.

### Slice 3: reach

Sidebar search with the synonyms list, anchors on every section, `sanduhr://settings/<page>#<anchor>`, the smoke hook's anchor argument, collapsing previews below 600 pt, Advanced disclosures, Message's top bar with Add Line and a new row scrolled into view and opened, Text under the camera setting a height and showing its hint.

Acceptance: typing "glow", "percent" or "margins" finds the control and opens it in view; on a 628 pt window, every section the guide names is reachable without scrolling blind (by search or a link); Add Line shows the new row's editor on screen; turning on Text under the camera changes the island at once.
Verify: swift-testing for the search index (every names-table entry indexed, synonyms resolve) and anchor parsing; smoke: `settings notch glow`, `settings watchers`, `message-editor add` each report the target control's frame inside the window's visible area (new `settings_anchor_visible` key); by hand on a 13-inch screen.

### Slice 4: signed out, demo and accounts

The Accounts signed-out form with Label, Paste a Key Instead, plain Chrome and Safari help, and the read-only Data defaults; the dev-build credential caption; Usage's signed-out state; empty notch wings collapsing; the widget's "Not signed in", hidden model buttons and the welcome sheet's Not Now; the tour's title and first step; What's New as Highlights on a first run; demo mode filling the widget, both Claude meters pieces and sample accounts; the guide's demo mode line.

Acceptance: on a fresh profile, nothing says "Connecting..." without an account, the first account can be named Work, Share with your agents reads Off before sign-in, no notch wing is an empty black slab, and demo mode fills every surface with sample data that names no real account or meeting.
Verify: swift-testing for the signed-out view states and demo data coverage (every surface with a sample); a fresh-profile smoke scenario with `demo on` asserting non-empty meters on the widget and Desk and two sample accounts; by hand on a fresh macOS user.

### Slice 5: Reset Sanduhr…

The About sheet, the ordered steps (Claude Code receipts first), failure reporting, Reset and Quit, the `reset --dry-run` smoke action, and the guide's reset section rewritten around it.

Acceptance: after Reset with everything checked, every Claude Code folder's `settings.json` is byte-identical to before Sanduhr's installs, no Sanduhr Keychain item, preferences domain or Application Support folder remains, and the next launch shows the welcome sheet; unchecking Claude Code folders leaves them untouched and the sheet warned.
Verify: swift-testing for step ordering, the dry-run report and a failed step not stopping the rest (a folder whose file changed underneath); smoke `reset --dry-run` on a throwaway profile; by hand on a scratch macOS user with a scratch Claude Code folder, never on a real one.

### Slice 6: Mods & Config

Item 71, in its own slices 0 to 5, built on this page structure. Not repeated here.

## Open questions

1. **How text mode stores a pin.** The list pins a row; text mode has a separate field. Options: a prefix in `messages.txt` (say `pin:`), which changes the file grammar that Claude writes and the Windows build reads, or keep the separate field but draw it as a gutter glyph beside the pinned line. Recommendation: the gutter glyph, no grammar change.
2. **Should a folder's statusline follow its linked account** rather than the active one? Today every folder shows the active account's meters even when Accounts links the folder to another account. Following the link is what the two-account persona expected; it changes what the statusline, the meters mod and the MCP server answer, so it needs its own item.
3. **What a signed-out Claude meters wing shows**: collapse to nothing, or a "Sign in" hint that opens Accounts. The spec picks the hint for the meters wing and collapse for every other empty wing; confirm.
4. **Option+S without the Desk.** Making the hotkeys global changes what Option+S types in every app while the switch is on, whether or not the Desk is. The switch default for new installs: on, or off until the person opts in?
5. **Two editors for watcher placement** on Watchers and on Notch and Desk is the one deliberate exception to "one control". Is a link enough instead?
6. **Reset scope for themes and messages**: checked by default, or opt-in, since they are the person's own work?
7. **Does the Windows app take the same names table?** The two apps share the guide's vocabulary for the tour (item 61); a shared names file would keep them in step.
8. **Integrations as "Claude Code"**: the page title changes but the raw value stays `integrations`. Rename the raw value with an alias in a later release, or keep it forever?

## Checklist entry

- [ ] **72. Settings v2: pages by task, one home and one name per control, search, reset**
  Spec ref: `docs/settings-v2-spec.md` (2026-10-07), from the 20 writer inputs in `docs/settings-v2-inputs.md` and a fresh-agent pass of four personas on 2.10.0. Follows items 68 (previews), 69 (Message editor) and 71 (Mods & Config, which this places).
  What to build, in slices that each ship: (0) Arrange Desk can always be ended: the Cancel and Done bar gets a real frame, the Desk rises above windows while arranging, empty pieces get placeholder outlines, labels never overlap; (1) names and links: one names table, every "<Page> Settings…" button opens the page it names, the island opens Notch, Menu Bar Shows in the shared menu, Notch glow and Claude Code glow hook as the one names, copy fixes (Max, the calendar line, `Mon:`, Mods' "receipt"); (2) one home per feature: sixteen pages in five groups (General, Accounts, Usage, Alerts; Desk, Desk Look, Message, Notch, Now Playing, Watchers; Claude Code, Mods & Config; Widget, Themes; Updates, About), the Desk and Notch switches on their pages, Watchers with inline placement, Meters into Alerts, the meters mod only in Claude Code, one Camera and mic placement, the folder's account shown per folder, with migrations that keep every existing choice; (3) reach: sidebar search with synonyms, section anchors and `sanduhr://settings/<page>#<anchor>`, collapsing previews below 600 pt, Advanced disclosures, Add Line at the top with the new row in view, Text under the camera setting its height; (4) signed out and demo: Label on first sign-in, plain Chrome and Safari key help, Data defaults shown before sign-in, "Not signed in" and Not Now on the widget, empty wings collapse, Sanduhr Tour title, Highlights on first run, demo mode on every surface; (5) Reset Sanduhr… on About, removing Claude Code entries through their receipts first.
  Acceptance: Arrange Desk shows a visible bar over a full-screen window; every control in the spec's mapping table is a control on exactly one page and an existing install keeps every choice; every "Settings…" button lands on the page it names; search finds Glow, Margins and Menu Bar Shows on a 628 pt window; a fresh profile never says "Connecting..." without an account; Reset leaves every Claude Code folder's `settings.json` byte-identical to before Sanduhr.
  Verify: swift-testing for the names table against menus, the tour and What's New, the migrations (each idempotent), the section aliases, the search index, anchors, signed-out states, demo coverage and the reset order; smoke: `desk_arrange.bar_frame_ok`, `settings-names.yaml`, `settings-sections.yaml` over every page, `settings_anchor_visible`, `reset --dry-run`, the `deskMeters` scenarios moved to `alerts`; by hand on a 13-inch screen, a profile copied from 2.10.0, and a scratch macOS user with a scratch Claude Code folder. The setup guide is updated in each slice's PR.
