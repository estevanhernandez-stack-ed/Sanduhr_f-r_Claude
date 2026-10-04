# Mac smoke test

By hand, on the build from `cd mac && SIGN_IDENTITY=- ./build.sh`. Run before every Mac release;
M0 is the first pass. Each line is a check: do the action, see the result.

Much of it runs by itself: `mac/smoke/smoke run` drives the app through the automatable checks
(Settings sections, Surfaces switches, Desk and notch on and off, meters, widget, Tools, the Desk
pulse, the menu) and snapshots the windows; see [mac/smoke/README.md](../mac/smoke/README.md).
Fresh-install and migration checks stay manual.

## 0. Setup

- [ ] Quit any running Sanduhr and Sanduhr Desk (`pkill -x Sanduhr; pkill -f "Sanduhr Desk"`).
- [ ] `lipo -archs mac/Sanduhr.app/Contents/MacOS/Sanduhr` prints `x86_64 arm64`.
- [ ] Back up every domain the app reads, so section 1 can start empty and section 7 can restore:
  ```
  mkdir -p ~/sanduhr-smoke
  for d in com.626labs.sanduhr com.626labs.sanduhr.desk com.626labs.sanduhrdesk com.estevan.desk; do
    defaults export $d ~/sanduhr-smoke/$d.plist 2>/dev/null
  done
  ```
- [ ] URL schemes go to whichever copy LaunchServices registered last. If
  `/Applications/Sanduhr.app` is the old 2.0.4, `open sanduhr://…` lands there. Register the dev
  build first: `/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f mac/Sanduhr.app`.

## 1. Fresh defaults: Desk is home

- [ ] `for d in com.626labs.sanduhr com.626labs.sanduhr.desk com.626labs.sanduhrdesk com.estevan.desk; do defaults delete $d 2>/dev/null; done`
  (credentials and history are files under `~/Library/Application Support/Sanduhr/`, not touched).
- [ ] `open mac/Sanduhr.app`. Desk is on at once: message top left, clock and the Claude meters
  bottom left on the desktop. No black island around the notch; no Calendar permission prompt.
- [ ] The widget appears top right for sign-in. With no session key, the onboarding sheet shows;
  Continue opens Sanduhr Settings at Accounts; paste the key and Save (it becomes the Personal account). (A session key left over from before the wipe skips the sheet.)
- [ ] After the first successful fetch the widget hides by itself (Desk is on, and a new install's
  General, Surfaces, Widget is "Hidden while Desk is on") and the meters fill in. The menu bar
  hourglass shows the percent; left-click brings the widget back.
- [ ] `defaults read com.626labs.sanduhr.desk` shows `deskEnabled = 1`, `layout = "message:tl clock:bl meters:bl meetings:bl"`,
  `showMeetings = 0`, `notch = 0` and `migrated = 1`. `defaults read com.626labs.sanduhr` shows
  `deskFirstRunDone = 1`, `panelHidden = 1`, `widgetVisibility = whileDeskOff` and no `tuckAfterFirstFetch`.
- [ ] Show the widget, quit and relaunch: the widget is hidden again (Desk is on); Desk is unchanged.
  General, Surfaces, turn Desk off: the widget shows; on: it hides.
- [ ] Existing user: quit Sanduhr, `defaults import` each backed-up domain from `~/sanduhr-smoke/`
  (section 0), relaunch: widget and Desk come back exactly as before the wipe (Desk still off if it
  was off; the widget not tucked; General, Surfaces, Widget reads "Always shown"). The rest of the
  run continues from these settings.

## 2. Widget

- [ ] Cards fill in after the first fetch (sign in via Settings, Accounts if the key is gone).
- [ ] Left-click the menu bar hourglass hides the widget; again shows it. Hidden survives a relaunch.
- [ ] Right-click opens the status menu: Hide (or Show) Widget; Tools: Deep Work, Pacing Calculators, Cooldown Snake; Refresh, Settings…, Check for Updates…; Quit Sanduhr für Claude. Two-finger click on the widget lists the same items in the same order (always Hide Widget there). Check for Updates… from the widget's menu opens Sparkle's check.
- [ ] Refresh updates the footer time; `~/Library/Application Support/Sanduhr/snapshot.json` has a new `captured_at`.
- [ ] Gear on the widget opens Sanduhr Settings. Widget, Look: pick a font and subtle mode, the widget changes as you pick. Themes: Reload lists the installed user themes. Switch a theme on the widget's strip, toggle compact (panel resizes, top edge stays put), open the focus timer and close it.
- [ ] Warning bars: Settings, Desk, Meters, drop the weekly "At" slider below its percent with a "more than" shorter than its reset. The widget's weekly card turns red at once (bar and percent) with a steady glow in the theme's accent around the bar and a warning triangle before the percent in the theme's text color, and the Desk meter does the same in the Desk ink (triangle included); with VoiceOver on, the warning card and meter read "<n>%, nearly full", the others just the percent; the session card stays as it was. Pick Match Desk: the widget's glow takes the Desk ink. Turn the weekly "Warn when nearly full" off: both go back at once. Put the settings back (weekly: on, 90%, 1 day) and the theme you had.
- [ ] Theme deleted outside the app (item 34): Settings, Widget, Themes, paste `docs/themes/examples/626-labs.json` with its `"name"` changed to `Smoke Test` and Save, so `smoke-test.json` is current (a file named after a built-in would fall back to that built-in instead). Open Themes Folder and, in Finder, move `smoke-test.json` to the Trash. Within a second the widget goes back to Obsidian, the gallery marks Obsidian and no longer lists the theme, the Theme menu and the Installed user themes list drop it, all without Reload or a relaunch; Console (process Sanduhr) shows `Theme <id> was removed from the themes folder; using Obsidian`. Put the file back: it reappears in the gallery (not applied). Pick the theme you had.
- [ ] Settings, Sanduhr, Updates: Installed shows the version and build from About; Last checked shows a date (or "Never" on a fresh install). Check Now opens Sparkle's check (the same window as Check for Updates…) and is disabled until it finishes; Last checked moves to now. Turn "Check for updates automatically" off: `defaults read com.626labs.sanduhr SUEnableAutomaticChecks` prints 0, and after a relaunch the switch is still off; turn it back on. "Download and install updates automatically" sets `SUAutomaticallyUpdate` the same way and is greyed out while automatic checks are off. "Release notes for this version" opens the GitHub release `v<version>-mac`. About: the app icon, "Sanduhr für Claude", "Version X (build N)", the one-line description and the independence line; Website, GitHub, Release Notes, Privacy and License each open their page (License goes to the README's license section until a LICENSE file lands), "Updates by Sparkle" opens sparkle-project.org, and the copyright line reads "MIT License. 626Labs.".

- [ ] Sign out (item 32; this deletes the real key, so back it up first). Save the key:
  release build `(umask 077; security find-generic-password -s com.626labs.sanduhr -a sessionKey:Personal -w > ~/sanduhr-smoke/sessionKey)`,
  dev build `cp -p ~/Library/Application\ Support/Sanduhr/credentials.json ~/sanduhr-smoke/`
  (owner-only either way; delete the copy after the run). Settings, Accounts, select the active
  account, Sign Out: a dialog "Sign out of this account?" with "Its session key is removed from
  this Mac. The account stays in the list with its usage history and settings; paste a key into it
  to sign in again." Cancel changes nothing. Sign Out: the note says "Signed out." and the button greys out as
  "Signed Out" (no password prompt on reopening the page); the list marks the account "Signed out"; the widget's cards
  go and it reads "Signed out — sign in" (click it: Settings opens at Accounts; with "Hidden
  while Desk is on" the widget shows itself); Desk's meters give way to "sign in again in Sanduhr",
  the notch's meters to "sign in to Sanduhr"; the hourglass loses its percent.
  `security find-generic-password -s com.626labs.sanduhr -a sessionKey:Personal` finds nothing and
  `credentials.json` is gone; `snapshot.json` has `"status":"error"`, `"error_kind":"session_expired"`,
  `"tiers":[]`; `history.Personal.json` and the theme are unchanged. Wait past five minutes: no new
  `captured_at`. Paste the saved key and Save: cards, Desk meters and notch come back without a relaunch.

## 3. Alerts

- [ ] Settings, Alerts: turn alerts on; macOS asks for notification permission once. Allow.
- [ ] Set the session line below the current 5-hour percent, Refresh: one banner.
- [ ] Refresh again: no second banner (once per tier per reset window).
- [ ] Put the line back where it was.
- [ ] Turn on "Warn when I'm on pace to run out before the reset". On a busy session (past 10% of the window, 20% or more used, the widget showing "At current pace, expires in …"), Refresh: one banner "Session on pace to run out at <time>", body "Resets <time>." Refresh again: no second one.
- [ ] Sound: pick Glass, Preview plays it; Send a Test plays Glass with the banner. Pick None: Preview is off and Send a Test is silent. Back to Default.
- [ ] Where alerts show, Desk pulse (Desk on, meters on the desktop, notch on): Send a Test posts no banner; the session meter glows three times over about three seconds and the notch glows once (the soft halo down the island's sides and along its bottom, no light along the screen edge). Banner and Desk pulse: both. Turn Desk off with Desk pulse chosen: Send a Test shows a banner.
- [ ] Quiet hours on, from a minute ago to an hour from now, Banner: set the session line below the current percent, Refresh: no banner, no sound. With Banner and Desk pulse, the meter still pulses. Turn quiet hours off and Refresh: still no banner (that window's alert was recorded). Put everything back.

## 4. Desk

- [ ] Hide the widget, with Ice hiding the menu bar hourglass: Option+S opens one window titled "Sanduhr Settings", a sidebar with General, Alerts, Accounts; Desk: Layout, Look, Meters, Message, Notch; Widget: Look, Themes, Pacing & Focus; Sanduhr: Updates, About. Every section opens and edits with the widget still hidden. Settings… in the widget's menu and the hourglass's menu bring the same window forward (never a second one); there is no "Sanduhr Desk" window and no settings sheet on the widget.
- [ ] General, Surfaces: "Show the widget now" off hides the widget and on shows it (the switch follows the hourglass too); Notch flips the island (Desk on).
- [ ] General, Surfaces, Widget picker: "Hidden while Desk is on" hides it at once with Desk on; turn Desk off, it shows; on, it hides. Show it from the hourglass: it stays until Desk next flips. "Only when I open it": hidden after every Desk flip and after a relaunch until Show Widget (any menu, a Desk meter's two-finger menu included) or Tools brings it. "Always shown": choosing it brings a hidden widget back; Desk flips leave it where it is. Put back "Always shown" (or what it was). Pacing & Focus, "Pin the pacing calculators" and Tools, Pacing Calculators show the same state: flip one, the other follows.
- [ ] After section 1, Desk is already on: clock, date, message and the meters (a bar per limit with a pace tick and reset time). General, Surfaces, Desk is on. The notch stays plain (its own switch, off).
- [ ] General, "Read today's meetings" (off on a fresh install): switch it on and the Calendar prompt appears right away, no restart. Allow: today's remaining timed meetings show. Switch it off: they go.
- [ ] Meters: their pace ticks sit where the widget's do; hide the widget and refresh from the menu, and the meters still update.
- [ ] No hint: nothing is written under the meters (the old "Click the meters for history and tools" line is gone, item 41).
- [ ] Passive meters (item 41): hide the widget, point at the meters: the pointer stays an arrow. Click a meter, a bar, the gap between the label and the percent, and beside the rows: nothing happens: no widget, no menu. Two-finger click (or Control-click) a meter: the menu starts with Show Widget; choose it and the widget appears beside the meters (to their right in a left corner, to their left in a right corner), fully on screen, in front. With the widget showing the same item reads Hide Widget and hides it, and the widget's usual items below have no second Show or Hide Widget.
- [ ] Show Widget from a meter's menu again after hiding it: beside the meters again. Drag it elsewhere, hide it with the hourglass and show it again with the hourglass: it comes back where it was dragged, not beside the meters.
- [ ] Click empty desktop beside the meters: Finder gets it (desktop icons select). Put a Finder window over the meters and click it there: the window takes the click, no widget.
- [ ] Tools with Ice hiding the hourglass and the widget hidden: two-finger click a Desk meter, Show Widget, two-finger click the widget, Tools: Deep Work opens the hourglass overlay, Cooldown Snake the game, Pacing Calculators keeps cool-down or surplus showing on every card (checked; choose again to put them back under the pointer), Hide Widget hides it.
- [ ] Tools from the menu bar hourglass's menu, widget hidden first each time: Show Widget shows it; Deep Work, Cooldown Snake and Pacing Calculators each show the widget with that tool open. With `menuIcon` on, Desk's clock menu lists today's meetings and Join next meeting, then the same items as the hourglass's menu in the same order. A tool open on the widget shows checked in all three menus.
- [ ] Meters: Settings, Desk, Meters lists Session and Weekly — All Models (plus any other weekly limit the account reports), each with "Warn when nearly full", an "At" slider (50 to 100%) and "Only while the reset is more than". Drop the weekly slider below its current percent with a "more than" shorter than its reset: the weekly bar turns red at once with a steady glow in the Desk ink around it, and its percent turns red. Raise the slider back, or pick a "more than" longer than the time to reset: it goes back to the ink. The session bar never turns red until its own switch is on. Put the settings back (weekly: on, 90%, 1 day).
- [ ] Layout: move Clock to Top right; it moves live. Hide Meetings; they go. Put both back.
- [ ] Look: ink `9ad7ff, 012089` (space after the comma) on the message draws blue to navy, no gray.
- [ ] Message: `~/Library/Application Support/Desk/messages.txt` exists with the starter lines; add a `MM-DD: smoke test` line for today, and within a minute the desktop shows it.
- [ ] Meeting rows with a Teams/Zoom/Meet link: pointer turns into a hand; click opens it. Clicking empty desktop still reaches Finder.
- [ ] General, Surfaces, turn Desk off: desktop layer (and notch, if on) go. On again: they come back (repeat twice; no doubled refreshes in Console).

## 5. Notch (Mac with a notch only)

- [ ] Settings, Notch shows "Extend the camera notch" off. Turn it on: black wings extend the notch left and right; left wing shows the time or the next meeting within the hour, right wing shows `5h N%  wk N%`.
- [ ] Strip under the notch draws when Notch, "Extra height below" is above 0; its text appears with "Text under the camera too".
- [ ] Notch, Text: set Right wing to Message: the right wing shows today's message within 15 seconds, growing to fit. Set Left wing to Nothing: a plain black wing. "Under the camera" to Time: the strip shows the time. Put all three back to their first choices (Next meeting, or the time; Claude meters; Next meeting, or the Claude meters).
- [ ] Click the island: Sanduhr Settings opens.
- [ ] Quit and relaunch: the notch is still on (the switch saved). Turn it off: wings and strip go, Desk layer stays.
- [ ] Full-screen an app: wings stay above it. Switch Spaces: wings stay.
- [ ] External display as main: no island drawn.
- [ ] Camera light: Notch, "Light up for the camera" on. Open Photo Booth: within a second a soft white light glows around the notch (over the island, the menu bar beside it and a band below). Click through it: the click reaches the app underneath. Brightness and "Reach below the menu bar" change it live. Full-screen Photo Booth: the light stays above it. Quit Photo Booth: it fades out. With Desk off, same result. Tools, Camera Light shows it with no camera and shows checked; chosen again it goes. Switch off: a camera no longer lights it.
- [ ] Glow: Notch, Glow, turn on "For Sanduhr alerts". Settings, Alerts, Send a Test: a soft halo in the notch text color fades in around the island (wings and strip) and out within about three seconds, once. The wings' text stays readable, and clicking the island during the glow still opens Settings, while a click on the menu bar beside it reaches the menu bar. Switch it off and send another test: no glow. "A minute before a meeting" on, with a calendar event starting in two minutes: one glow between 60 and 45 seconds before it starts, none after. "When the camera light comes on" on: Tools, Camera Light glows once as the light comes on. All three off: nothing glows.
- [ ] One glow (item 27): Desk on, notch on, Desk pulse chosen. Send a Test (or `smoke/smoke do pulse`): one soft halo down the island's sides and along its bottom, the same look as the Glow switches; no light above the wings, beside their top corners or along the screen edge, and no outline stroke on the island. Glow switches all off: a pulse still glows.
- [ ] Plain notch glow (item 27): turn the notch island off (Notch, "Extend the camera notch" off). The Glow section stays usable. Test Glow (or `smoke/smoke do glow`, or an enabled alert): a halo hugs the hardware notch itself, its width and height with rounded bottom corners, no wings, nothing at the screen edge, the camera area untouched. Turn Desk off too: Tools, Camera Light with "When the camera light comes on" on still glows the plain notch. Put the notch back on: the glow goes back around the island.
- [ ] Covered strip (item 34): Desk on, notch on, "Extra height" above 0 so the strip shows under the camera. Move an app window (Finder will do) so it covers the strip, or maximize one (not full screen). Glow, **Test Glow** (or `smoke/smoke do glow`, or Send a Test with Desk pulse): the halo hugs the wings only, its bottom edge level with the menu bar, nothing traced around the hidden strip. Move the window away and glow again: the halo goes around wings and strip.

## 6. Links and keys

- [ ] `open sanduhr://settings` and `open estedesk://settings` both open Sanduhr Settings.
- [ ] `open estedesk://join-next` opens the next meeting with a link, or beeps when there is none.
- [ ] Remove or comment out any skhd binding for alt - j and alt - s first (`skhd --reload`), or skhd answers before Sanduhr and this proves nothing.
- [ ] With Desk on, from any app: Option+J joins the next meeting (or beeps), Option+S opens Sanduhr Settings. No Accessibility prompt.
- [ ] In a text field, Option+S does not type ß while Desk is on (expected cost). General, Shortcuts off: Option+S types ß again, Option+J types ∆. Back on: shortcuts work again.
- [ ] Desk off: Option+S types ß; the shortcuts are gone.

## 7. Migration from Sanduhr Desk, then restore

- [ ] Quit Sanduhr für Claude. `defaults delete com.626labs.sanduhr.desk`, then
  `defaults import com.626labs.sanduhrdesk ~/sanduhr-smoke/com.626labs.sanduhrdesk.plist`.
- [ ] Launch: Desk comes on by itself with the old layout, colors and font (EsteFont if none was set).
  A running Sanduhr Desk app is quit.
- [ ] The notch is on (the old apps defaulted it on), unless it was switched off there.
- [ ] Restore: quit Sanduhr, then
  `for f in ~/sanduhr-smoke/*.plist; do defaults import "$(basename "$f" .plist)" "$f"; done`.

## 8. Intel slice

- [ ] On an Intel Mac, or `arch -x86_64 mac/Sanduhr.app/Contents/MacOS/Sanduhr` under Rosetta:
  launches, widget fetches. Quit.

## 9. Accounts (item 36)

Dev and release builds share the app's defaults (`com.626labs.sanduhr`) and
`~/Library/Application Support/Sanduhr/`, so this section changes what the installed app sees.
Run it on the dev build, which keeps keys in `credentials.json`, and back up first:

- [ ] Quit Sanduhr. Back up the defaults, the history files and the credentials file:
  ```
  mkdir -p ~/sanduhr-smoke/accounts
  defaults export com.626labs.sanduhr ~/sanduhr-smoke/accounts/com.626labs.sanduhr.plist
  cp -p ~/Library/Application\ Support/Sanduhr/history*.json ~/sanduhr-smoke/accounts/ 2>/dev/null
  cp -p ~/Library/Application\ Support/Sanduhr/credentials.json ~/sanduhr-smoke/accounts/ 2>/dev/null
  ```
  (`history.json` from before 2.4.0 becomes `history.Personal.json` on the first 2.4.0 launch.)
- [ ] Upgrade: on a 2.3.x setup, launch the dev build. `defaults read com.626labs.sanduhr accounts`
  lists `Personal`, `activeAccount` is `Personal`; `credentials.json` holds `sessionKey:Personal`
  and no bare `sessionKey`; `history.json` is now `history.Personal.json`. With one account there
  is no chip, no Accounts submenu, and Desk's line still starts with "claude".
- [ ] Settings, Accounts: Personal is listed, marked Active. Add Account…, label `Work!`: "Label
  must be 1 to 32 letters, digits, spaces, underscores or hyphens." and Add Account stays off.
  Label `personal`: "An account with that label already exists." under the field as you type,
  before Add, and Add Account stays off. Label `Work` with a second
  claude.ai login's session key, "Make it the active account" on: the widget clears, says
  "Switching account…", then shows Work's numbers. `credentials.json` has `sessionKey:Work`.
- [ ] Two accounts: the widget's title shows a `Work` chip; click it: Personal's numbers come
  back (no relaunch), click again: Work. The hourglass's menu and the widget's two-finger menu
  have Accounts ▸ Personal, Work (the active one checked), Manage Accounts… (opens Settings,
  Accounts). Desk's claude line reads "Work   N% session …". `smoke/smoke state` shows
  `accounts_count: 2`, an 8-digit `account_ref` that changes with each switch, `follow: false`,
  and no label anywhere; `snapshot.json` has the same `account_ref` and no label.
  `smoke/smoke do account next` switches like a chip click.
- [ ] Rename Work to `Office`: the chip, menu and list follow; `history.Office.json` replaces
  `history.Work.json`; `credentials.json` holds `sessionKey:Office`.
- [ ] Sign Out an inactive account: it stays listed, marked "Signed out"; its history file stays;
  the active account keeps fetching. Paste its key and Save: "Signed out" goes.
- [ ] Following: Follow the account I'm using is off; turn it on (`follow: true`). Make Personal
  active by hand (`follow_paused: true`), then use the other account in claude.ai for a while:
  no switch during the pause. Wait out the pause (3 hours, or 30 minutes with Personal idle),
  keep using the other account and leave Personal idle: within 15 minutes or so Sanduhr switches,
  the chip and Desk's line read "Office (in use)" for a minute, no notification. Use both: no
  switch, the menu shows "Office · in use" and the chip a dot. Turn following off.
- [ ] Remove Account… on Office: the dialog names its key and its usage history. Remove: it leaves
  the list, `history.Office.json` and its slots are gone, Personal is active, and with one account
  the chip, the submenu and Desk's label go.
- [ ] Restore. Quit Sanduhr. `defaults import` merges, so first delete what the run added:
  `defaults delete com.626labs.sanduhr accounts; defaults delete com.626labs.sanduhr activeAccount;
  defaults delete com.626labs.sanduhr followAccount; defaults delete com.626labs.sanduhr signedInAccounts`
  (ignore "does not exist"), then `defaults import com.626labs.sanduhr ~/sanduhr-smoke/accounts/com.626labs.sanduhr.plist`.
  Delete the run's `history.*.json` files and copy the backed-up ones and `credentials.json` back.
  A release build moves the file's keys into the Keychain on its next launch (the file wins).
  Delete `~/sanduhr-smoke/accounts/credentials.json` afterwards.

## 10. Menu bar, hidden limits, duplicate names (item 38)

- [ ] Menu bar: Settings, General, Menu bar shows "Percent beside the hourglass" set to Whichever
  is higher (`smoke/smoke state` reads `menu_bar: higher`). With a model-specific or promo limit
  (say Weekly — Special) above both the session and the weekly limit, the hourglass shows the
  higher of those two, never the special one. Session: the session percent; Weekly: the weekly
  percent (`menu_bar: session` / `weekly`). Rotate: "S 12%", then 8 seconds later "W 96%", and
  so on, also while a menu is open; nothing else in the menu bar moves. Back to Whichever is
  higher: the letters go and the text stops changing.
- [ ] Hidden limits: with a limit other than Session and Weekly reported (Weekly — Opus, Weekly —
  Special…), Settings, Desk, Meters shows "Show this limit" on in its group (none on Session or
  Weekly — All Models). Switch it off: its card leaves the widget and its row the Desk meters at
  once (the widget shrinks to fit), its warning controls grey out, and `smoke/smoke state` lists
  it under `hidden_limits`. Settings, Alerts, a line low enough to cross it: no alert for it on
  the next refresh. Compact mode shows the fullest limit still shown. Switch it on: it comes back
  everywhere. `smoke/smoke run smoke/scenarios/hide-limit.yaml` (from `mac/`) does the switch and puts it back.
- [ ] Duplicate names: with Personal and Work, Add Account…, type `WORK`: "An account with that
  label already exists." shows under the field as you type (where `Work!` shows the character
  rule) and Add Account stays off; change it to `Team` and the line goes. On Work's page, type
  `personal` in the Rename field: the same line under it and Rename off; `work` (its own name in
  another case) is accepted.

## 11. Switch, hide and silence from the meters (item 39)

- [ ] Desk account name: with two accounts and Desk's Claude line showing, point at the name at
  the start of the line: the pointer turns into a hand and the name underlines; the rest of the
  line keeps the arrow and a click there reaches the desktop. Click the name: the next account's
  numbers come in, as with a chip click (`account_ref` changes, `follow_paused: true` with
  following on). A plain click on the meters does nothing (item 41). With one account the
  line starts with "claude" and nothing on it is clickable.
- [ ] Limit menu on Desk: two-finger click (or Control-click) the Weekly — All Models meter: an
  Accounts submenu (two accounts, the active one checked), then "Stop warnings for this limit"
  (no Hide), then "Meter Settings…", then the widget's usual items (Tools, Refresh, Settings…,
  Quit) with no second Accounts submenu. On the Session meter the warnings item reads
  "Warn again for this limit" (off by default). On a limit beyond those two (Weekly — Opus,
  Weekly — Special…) "Hide Weekly — …" shows too. With one account there is no Accounts submenu.
  The Desk menu starts with Show Widget (Hide Widget when it shows) since item 41, and the widget's
  usual items below it have no second one. A plain click on a meter does nothing; the Finder's own
  desktop menu never opens over the meters.
- [ ] Switch from the menu: pick the other account in the Accounts submenu: its numbers come in
  (`account_ref` changes), as from the chip.
- [ ] Silence: with Settings, Desk, Meters open beside it, choose "Stop warnings for this limit"
  on the weekly meter: its "Warn when nearly full" switch turns off at once, and
  `smoke/smoke state` lists `seven_day` under `silenced_limits`. Open the menu again: "Warn again
  for this limit"; choose it: the switch is back on. Turn the switch off in Settings: the menu
  reads "Warn again" next time. `smoke/smoke run smoke/scenarios/silence-limit.yaml` (from
  `mac/`) writes the same key and puts it back.
- [ ] Hide: choose "Hide Weekly — …": the row leaves the Desk and the card the widget at once, its
  "Show this limit" turns off in Settings, and it is under `hidden_limits`. Switch it back on in
  Settings.
- [ ] Widget cards: two-finger click a tier card: the same items as the Desk meter, then the widget
  menu's items. Two-finger click elsewhere on the widget (the title, the action row): the widget
  menu as before, with its Accounts submenu. "Meter Settings…" from either opens Settings on
  Desk, Meters.

## 12. Desk meter menu, menu bar submenu, graceful switch (item 40)

- [ ] Desk meter menu with another app in front: click into a Finder or Terminal window, then
  move the pointer onto the Desk meters (off that window) and two-finger click a row, then the
  gap between its label and percent, then just under its bar: each time the limit menu opens for
  that row, Sanduhr does not come to the front, and no Finder desktop menu opens (check with
  Finder's Show items on desktop both on and off). Control-click does the same. Escape closes
  it; the app you were in is still in front.
- [ ] Without moving first: rest the pointer on a meter, Command-Tab to another app, two-finger
  click without moving the pointer: the menu opens (a moment later, at most 0.15 s).
- [ ] Left clicks unchanged: a click on the bars shows the widget beside them; with two accounts
  a click on the account name cycles; a click on empty desktop beside the meters reaches Finder.
  No visible tint behind the meters on a light and a dark wallpaper.
- [ ] Menu Bar Shows: right-click the hourglass: under Hide (or Show) Widget, and under Accounts
  with two accounts, a Menu Bar Shows submenu lists Session, Weekly, Whichever is higher and
  Rotate (session and weekly), the current one checked. Choose Weekly with Settings, General
  open: the percent beside the hourglass changes at once and the Menu bar picker reads Weekly;
  `smoke/smoke state` shows `menu_bar: weekly`. Choose Rotate: "S 12%" now, "W 96%" 8 s later.
  Change the picker in Settings: the submenu checks the new choice next time. The widget's
  two-finger menu and Desk's clock menu have no Menu Bar Shows. Put back Whichever is higher.
- [ ] Graceful switch: two signed-in accounts, the widget and the Desk meters and Claude line in
  view. Click the chip: the cards fade out gently (about 0.6 s since item 41), the widget keeps
  its height, then the other account's cards fade in where they were; the Desk meters and line do
  the same, the notch shows no numbers in between, and the menu bar percent clears at once and
  comes back with the new account's. At no point does anything flash blank or show the old
  numbers at full strength after the switch. On a slow network (Network Link Conditioner, or
  turn Wi-Fi off just before the click): after the fade a faint "Switching account…" shows over
  the empty cards and "switching account…" on the Desk line (on the meters when the line is not
  in the layout); `snapshot.json` is gone as soon as you click. Two accounts with a different
  number of limits: the widget settles to its new height once, as the new cards fade in.
- [ ] Calm handoff (item 41): watch the chip and the Desk line's name through a switch. The old
  name stays while the old numbers fade out, then crossfades to the new name once they are gone;
  the new numbers come in after. The new name never sits over the old numbers.
- [ ] Reduce motion (System Settings, Accessibility, Display): a switch swaps the cards, the Desk
  meters and the name with no fade; the widget still keeps its height while it waits.
- [ ] Switching to an account with no key: the old cards fade out and "Signed out — sign in"
  shows; the Desk shows the sign-in line.
