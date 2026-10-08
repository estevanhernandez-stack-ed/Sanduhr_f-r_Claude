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
- [ ] Right-click opens the status menu: Hide (or Show) Widget; Tools: Deep Work, Pacing Calculators, Cooldown Snake; Refresh, Settings…, Check for Updates…, What's New…; Quit Sanduhr für Claude. Two-finger click on the widget lists the same items in the same order (always Hide Widget there). Check for Updates… from the widget's menu opens Sparkle's check.
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
- [ ] Hidden limits: with a temporary limit reported (Weekly — Special, or another limit listed
  under `temporary_limits` since item 42), Settings, Desk, Meters shows "Show this limit" on in
  its group (none on Session, Weekly — All Models or a model limit with a weekly reset). Switch it off: its card leaves the widget and its row the Desk meters at
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
  "Warn again for this limit" (off by default). On a temporary limit (under `temporary_limits`,
  say Weekly — Special) "Hide Weekly — …" shows too; never on a model limit with a weekly reset
  (item 42). With one account there is no Accounts submenu.
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

## 13. Temporary limits and their return (item 42)

- [ ] Which limits can hide: `smoke/smoke state` lists `temporary_limits` (raw values only). With
  Weekly — Special reported it is there; Session, Weekly — All Models and the model limits
  (Sonnet, Opus, Cowork, Design, OAuth Apps) with a reset within 8 days are not. Settings, Desk,
  Meters shows "Show this limit" only in the groups of `temporary_limits`; the other groups keep
  just their warning controls. The two-finger menus (a Desk meter, a widget card) offer "Hide …"
  only on those limits.
- [ ] Hide records the numbers: hide Weekly — Special from its menu. `defaults read
  com.626labs.sanduhr.desk meterHidden.iguana_necktie` shows `resetsAt` and `utilization` as it
  read at that moment, and `limitFirstSeen` lists each reported limit with the date it first
  showed (re-hiding never changes those dates).
- [ ] Hidden Limits submenu: with Weekly — Special hidden, two-finger click any Desk meter, beside
  the rows, or a widget card: "Hidden Limits ▸" sits above Meter Settings… and lists Weekly —
  Special. Pick it: it is back on the widget and the Desk at once, Settings shows its switch on,
  `hidden_limits` is empty and the `meterHidden.iguana_necktie` key is gone. With nothing hidden
  the submenu is not there.
- [ ] Return on a refill or a new window: hide it, then `defaults write com.626labs.sanduhr.desk
  meterHidden.iguana_necktie -dict utilization 100` (a record 10+ points above what it
  reads now) and Refresh: it shows again, and Console (subsystem `com.626labs.sanduhr`, category
  `limits`) logs `hidden limit iguana_necktie shows again: refill`, no label. Same with
  `-dict resetsAt "2020-01-01T00:00:00Z"`: `new_window`.
- [ ] No longer temporary: `defaults write com.626labs.sanduhr.desk meterShow.seven_day_opus -bool
  false` with Opus on a weekly reset, then Refresh: Opus stays shown, the key is cleared, and the
  log reads `not_temporary`.
- [ ] Old hides: a `meterShow.<tier> -bool false` written by hand (no record) on a temporary limit
  stays hidden; after the next refresh `meterHidden.<tier>` holds that refresh's numbers.
- [ ] The Desk takes the mouse at once: with another app in front, move the pointer quickly onto a
  Desk meter and two-finger click without stopping: the limit menu opens, never the Finder's.
  Launch (or switch Desk off and on) with the pointer resting on where the meters appear, wait
  for them, then two-finger click without moving: the limit menu. Hide a limit so the meters move
  under a still pointer, then two-finger click: the limit menu. With the pointer far from the
  Desk blocks, Activity Monitor shows Sanduhr idle (no timer runs there).

## 14. Thirty days of meter history (item 43)

- [ ] Upgrade: with a 2.4.0 `history.{label}.json` (24 points per limit) in place, launch the new
  build: the sparklines look as they did. After the next fetches the file grows past 24 points
  per limit (`jq '.five_hour | length'`) while the cards still draw the last 24 (about 2 hours).
- [ ] `smoke/smoke state` shows `history_days: 30` and no label.
- [ ] Settings, Accounts, select the active account: Meter history reads 30 days. Choose Off: a
  confirmation asks to erase this account's meter history. Keep It: the file stays, Refresh adds
  nothing to it (its modification time doesn't change), `history_days: 0`, and `defaults read
  com.626labs.sanduhr meterHistoryOff` lists the account. Back to 30 days: the next Refresh writes
  again.
- [ ] Off again, then Erase History: "Meter history erased.", the file is gone and the cards'
  sparklines empty; the meters still update.
- [ ] Rename an account with Meter history off: it stays off under the new name. Remove it: it
  leaves `meterHistoryOff`. Another account's history is never touched.

## 15. The Data section and Claude Code folder linking (item 44)

Use a test Claude Code folder (for example `mkdir -p ~/.claude-smoketest/projects`) or folders
whose names are fine to show; remove it afterwards.

- [ ] Settings, Accounts, select an account: a Data section shows Meter history (30 days),
  Claude Code folder (None), Claude Code activity (Not tracked), Project names in the record
  (Names, dimmed until Keep a record), Share with your agents (Off), with a caption that live
  activity works now and the record and sharing take effect in later updates. The page scrolls
  when it doesn't fit.
- [ ] `smoke/smoke state` shows `data:` with `activity: "off"`, `names: names`, `share: "off"`,
  `folder_linked: false`, and no label or path anywhere.
- [ ] The folder menu lists `~/.claude` and the `~/.claude-*` folders that hold `projects/` or a
  `.claude.json`, and not an empty `~/.claude-x` folder. Choose… on a folder without either says
  it doesn't look like a Claude Code folder and links nothing.
- [ ] With a folder signed in to the same organization as the account (its `.claude.json`
  `oauthAccount.organizationUuid`), "This folder is signed in to this account. Link it?" shows
  with the folder; nothing is linked until Link. Selecting an inactive account fetches its
  organization once; nothing about it appears in Console (`log stream --predicate
  'subsystem == "com.626labs.sanduhr"'`).
- [ ] Link a folder to account A, then pick the same folder for account B: a confirmation names
  A; Cancel keeps it with A, Move It to B unlinks it from A. A's menu then reads None.
- [ ] Set B's activity to Keep a record, names to Hidden, share to Meters; Make B active:
  `state.yaml` `data:` shows `record`, `hidden`, `meters`, `folder_linked: true`. `defaults read
  com.626labs.sanduhr accountData` shows them under B. Rename B: the choices and the link follow.
  Remove B: `accountData` no longer lists it and the folder is free for another account.

## 16. Live Claude Code activity (item 45)

Use a test Claude Code folder, never a real one, and remove it afterwards. Make one from the
shape of the test fixtures (`mac/Tests/SanduhrTests/Fixtures/cc-logs/by-tier.jsonl`), with the
current time so the lines count as new:

```sh
mkdir -p ~/.claude-smoketest/projects/demo
cc_line() {   # model input output
  printf '{"type":"assistant","timestamp":"%s","message":{"model":"%s","usage":{"input_tokens":%s,"output_tokens":%s}},"cwd":"/tmp/demo"}\n' \
    "$(date -u +%Y-%m-%dT%H:%M:%S.000Z)" "$1" "$2" "$3" >> ~/.claude-smoketest/projects/demo/session.jsonl
}
```

- [ ] Link `~/.claude-smoketest` to the active account, activity Not tracked. `cc_line
  claude-sonnet-4-6 1000 499`: no card changes within a minute, and `smoke/smoke state` shows
  `local_activity: {reading: false, events: 0}`. `sudo fs_usage -w -f filesys Sanduhr | grep
  smoketest` shows no open of `session.jsonl`.
- [ ] Activity Live only: `reading: true`. Refresh, then `cc_line claude-sonnet-4-6 1000 499`:
  within 30 seconds the Weekly — Sonnet card shows "+1.5k" before its percent, small and in the
  theme's dim text; hovering it explains the badge. VoiceOver on the percent reads "…%, plus 1.5
  thousand local tokens since the last refresh". `state.yaml` shows `events: 1`.
- [ ] Another `cc_line claude-sonnet-4-6 10000 0`: the badge reads "+12k" after the next scan.
  `cc_line claude-haiku-4-5 2000 0` adds "+2.0k" to Weekly — All Models; a `gpt-4o` line shows
  nowhere. Refresh: every badge goes away until the next line.
- [ ] Switch to an account with no folder: no badge, `reading: false`. Back, then Not tracked:
  the badges go at once and `reading: false`. Keep a record reads like Live only.
- [ ] `state.yaml`, `snapshot.json` and Console (`log stream --predicate 'subsystem ==
  "com.626labs.sanduhr"'`) carry no folder path, project, model or label. Then `rm -rf
  ~/.claude-smoketest` and unlink it.

## 17. The vault (item 46)

Use the test folder from section 16 (`~/.claude-smoketest`), never a real one, with the same
`cc_line`. A folder id is 16 hex digits; find the test folder's with `ls ~/Library/Application\
Support/Sanduhr/vault/` before and after.

- [ ] Activity Live only: `cc_line claude-sonnet-4-6 1000 499`, refresh. No new folder under
  `vault/`, and `state.yaml` shows `vault: {recording: false, months: 0, last_ingest_ok: false}`.
- [ ] Keep a record (names Names): within a refresh a new `vault/<id>/` holds
  `sessions-<this month>.json`, `rollups-…`, `checkpoints.json`, `meta.json`; `state.yaml` shows
  `recording: true, months: 1, last_ingest_ok: true`; the activity caption says "Kept so far: 1
  month". `jq '.sessions[] | {project_name, total}' sessions-*.json` shows `demo` and 1499. The
  folder name and file names hold no path or label; `grep -r smoketest vault/` finds nothing.
- [ ] Another `cc_line … 10000 0`, refresh: the same session's total grows to 11499 (one row,
  no double count).
- [ ] Names Hidden, then a new session (`cc_line` into `projects/demo/second.jsonl` the same
  way): its row's `project_name` and `project_key` are `p-` and 10 hex digits, no `cwd`. The
  first row still reads `demo`; once its file has been quiet for an hour it is read a last time
  and reads `p-…` too. A session that had already gone quiet keeps its name (as the caption
  says).
- [ ] With a second Sanduhr running (a dev build beside the release), refresh both: the vault
  stays consistent and Console shows "ingest skipped (writer lock held)" from one of them.
- [ ] Activity Live only: "Keep the record or erase it?" appears. Keep It: the folder stays.
  Keep a record again, then Live only, Erase Record: `vault/<id>/` is gone and stays gone after
  two refreshes; `state.yaml` `months: 0`.
- [ ] Keep a record again; Erase this account's data…: the confirmation names the meter history
  and the record from `~/.claude-smoketest`; Erase Data: activity reads Live only, the vault
  folder and `history.<account>.json` are gone. Keep a record, refresh, then Remove Account (on
  a throwaway account): its vault folder is gone.
- [ ] Console (`log stream --predicate 'subsystem == "com.626labs.sanduhr" && category ==
  "vault"'`) shows fixed phrases only. Then `rm -rf ~/.claude-smoketest` and unlink it.

## 18. Share with your agents: the MCP tools (item 47)

Use the test folder from section 16 (`~/.claude-smoketest`, linked to a throwaway or test account,
activity Keep a record, names Names) with a few `cc_line`s and one refresh, so the record has a day.
Register the server the way `install.sh` does (`bash mac/integrations/install.sh`, which runs
`claude mcp add sanduhr --scope user -- python3 ".../integrations/sanduhr_mcp.py"`; skip it if
`claude mcp get sanduhr` already lists it). Ask Claude Code by tool name, in a new session after
each change ("call the sanduhr get_usage tool", and so on). `F=~/Library/Application\
Support/Sanduhr/mcp-access.json`.

- [ ] Share Off on every account: `jq . "$F"` shows `"accounts": []`. `get_usage` answers
  `no_data` / `not_shared` with a remedy naming Settings > Accounts > Data; `ping` shows
  `sharing.access_file: "ok"`, `accounts_shared: 0`, `tools_available` with eight tools and
  `tools_not_on_mac` naming `publish_usage`.
- [ ] Meters on the active account: the file lists it with `share: "meters"`, its
  `account_ref` (as in `snapshot.json`), `history_file`, and no `names`, `vault_id` or
  `live_folder`; `ls -l "$F"` shows `-rw-------`. `get_usage` shows the widget's percentages
  with `local_burn_since_snapshot: null`; `get_usage_history` shows `meter_history` for it and
  no days; `get_local_burn_by_project` and `get_model_usage` answer `disabled`.
- [ ] Meters and activity: the entry gains `names: "names"`, a 16-hex `vault_id` and
  `live_folder` = the test folder. `get_local_burn_by_project` lists one root (its
  `account_ref`) with project `demo`; `get_model_usage` lists `claude-sonnet-4-6` beside the
  Sonnet meter; `get_usage_history` (7 days) shows today with `top_projects` `demo`; a new
  `cc_line` shows in `get_usage`'s `local_burn_since_snapshot` before the next refresh.
- [ ] Names Hidden: `get_local_burn_by_project` names the project `p-` and 10 hex digits, with
  `"names": "hidden"`, even when asked for full paths; nothing in any answer says `demo` or
  `smoketest`. Names Full paths: asked for full paths, the project is `/tmp/demo`.
- [ ] Activity Live only: `vault_id` leaves the file; burn and model still answer. Not tracked:
  `live_folder` leaves too and both answer `disabled`.
- [ ] Switch to another account with sharing Off: within a second the file's `active` flags
  follow, and `get_usage` answers `not_shared` while the shared account's activity tools still
  answer for it. Rename the shared account: its `account_ref` and `history_file` change with it.
- [ ] `mv "$F" "$F.bak"`: every tool answers `not_shared`, `ping` says `access_file: "missing"`;
  change any choice and the file comes back. `log stream --predicate 'subsystem ==
  "com.626labs.sanduhr"'` while changing choices shows no label or path.
- [ ] Clean up: Share Off, unlink and `rm -rf ~/.claude-smoketest`; `bash
  mac/integrations/install.sh --remove` if the server was registered only for this test.

## 19. The Claude Usage page (item 48)

Use the test folder from section 16 (`~/.claude-smoketest`, `cc_line` as there), linked to a test
or throwaway account, never a real folder: the page shows project names, so screenshots come from
this folder only. A second helper writes a line on an earlier day:

```sh
cc_line_at() {   # UTC timestamp, model, input, output, project
  mkdir -p ~/.claude-smoketest/projects/"$5"
  printf '{"type":"assistant","timestamp":"%s","message":{"model":"%s","usage":{"input_tokens":%s,"output_tokens":%s}},"cwd":"/tmp/%s"}\n' \
    "$1" "$2" "$3" "$4" "$5" >> ~/.claude-smoketest/projects/"$5"/past.jsonl
}
cc_line_at "$(date -u -v-3d +%Y-%m-%dT10:00:00.000Z)" claude-sonnet-4-6 4000 1000 api
cc_line_at "$(date -u -v-10d +%Y-%m-%dT10:00:00.000Z)" claude-opus-4-1 20000 0 web
```

- [ ] `smoke/smoke run usage-page` and `smoke/smoke run settings-sections` pass; the snapshots
  show each tab. Tools, Claude Usage… in the widget's menu, the menu bar menu and a Desk meter's
  two-finger menu each open Settings at Claude Usage; `state.yaml` shows `usage_page: {open: true,
  tab: overview}` and nothing else about the page.
- [ ] Activity Not tracked: Overview says to choose a folder and activity, with Data Settings…,
  which opens Accounts on that account scrolled to Data. Trends and Sessions say they come from
  the record, with the same button.
- [ ] Live only: Overview's status line says Live only; Today shows the `cc_line` tokens with sent
  and received; the strip has bars 3 and 10 days back; Projects lists `demo`, `api`, `web`.
  Trends and Sessions still ask for a record. No folder appears under `vault/`.
- [ ] Keep a record, refresh: the status line goes; the strip's old days now come from the
  record (`rm ~/.claude-smoketest/projects/api/past.jsonl`, wait a minute: the bar 3 days back
  stays). Days before the record's coverage are dotted ("no record"), never empty bars. Today
  grows with each `cc_line` within a minute.
- [ ] Trends: 4/12/26 weeks; the current week is hatched; weeks before the record began are
  dotted, never zero bars; the footer reads "History kept since <today>" and the first-day note
  shows. Top projects match Overview's.
- [ ] Sessions: 7d by default; three rows (`demo`, `api`, `web`), the `web` one shows "—" under
  7d and moves to the top under All when sorted by tokens. Today/Yesterday change the column and
  its header ("Tokens (Today) ▼"). Each header sorts, a second click flips the glyph. Clicking a
  row shows the wall-clock span, each day with its models, and `Record: .claude-smoketest`;
  a refresh keeps it open and the scroll where it was.
- [ ] Export CSV… to the Desktop: the file opens in Numbers with the header `session,root,
  project,first_seen_utc,last_seen_utc,tokens_in_scope,tokens_total,models`, the rows in the
  order shown and `.claude-smoketest` as root. Nothing is written until you click Save.
- [ ] Names Hidden, then a new `cc_line` into `projects/demo2/session.jsonl`: Overview and
  Sessions show its `p-` code and the caption explaining codes; nothing on the page or in the
  CSV reads `demo2`.
- [ ] Two accounts: the picker shows both; picking the other shows its own folder's numbers
  (or the setup note); the Records list doesn't change.
- [ ] Records on this Mac lists the test folder's record with its size, oldest day ("since
  …", 10 days back), the account and "recording". Unlink the folder (Keep It when asked): the
  row reads "Not linked to an account (<8 hex>)". Show in Finder selects `vault/<id>`. Erase…
  names it as not linked; Erase Record: the row and `vault/<id>/` are gone and stay gone after
  two refreshes. Link and record again, then Erase… on the linked row: the confirmation says the
  account switches to Live only first; after it, Data shows Live only and the folder is gone.
- [ ] Console (`log stream --predicate 'subsystem == "com.626labs.sanduhr"'`) shows no label,
  path, project or number from the page. Then `rm -rf ~/.claude-smoketest ~/Desktop/sanduhr-sessions-*.csv`
  and unlink it.

## 20. One-click MCP and statusline install (item 49)

Use test Claude Code folders, never your real `~/.claude`, `~/.claude-*` or `~/.claude.json`:
two folders cover both placements of `.claude.json` without touching the default home. Make
them, with configs that have keys of their own, and keep copies to compare with:

```sh
mkdir -p ~/.claude-smoketest/projects ~/.claude-smoketest2/projects
printf '{\n  "numStartups": 3,\n  "mcpServers": {\n    "other": {"type": "stdio", "command": "true"}\n  }\n}\n' > ~/.claude-smoketest/.claude.json
printf '{\n  "model": "opus",\n  "statusLine": {"type": "command", "command": "echo mine"}\n}\n' > ~/.claude-smoketest/settings.json
cp ~/.claude-smoketest/.claude.json /tmp/st-claude.json; cp ~/.claude-smoketest/settings.json /tmp/st-settings.json
S=~/Library/Application\ Support/Sanduhr/integrations
```

- [ ] `smoke/smoke run settings-sections` passes; Settings shows Integrations under Claude
  Usage. The page lists `~/.claude-smoketest` and `~/.claude-smoketest2` (and any real folders:
  leave those alone) with MCP server and Statusline rows, and a "Runs with Python 3.x at …"
  line. On a Mac without Command Line Tools (or with `/usr/bin/python3` only), the page says
  what is needed instead, Install buttons are off, and nothing asks to install until Install
  Command Line Tools… is clicked.
- [ ] MCP server, Install… on `~/.claude-smoketest`: the sheet lists every account with its
  Share with your agents choice ("Off: nothing" and so on), Change in Accounts… goes to Accounts,
  and it names `~/.claude-smoketest/.claude.json`. Install: the row reads Installed; `jq
  .mcpServers ~/.claude-smoketest/.claude.json` shows `other` unchanged and `sanduhr` as
  `{"type": "stdio", "command": "<python3>", "args": [".../integrations/current/sanduhr_mcp.py"]}`;
  `diff /tmp/st-claude.json ~/.claude-smoketest/.claude.json.sanduhr-backup` is empty; `ls -l
  "$S"` shows `current -> <12 hex>` and that folder holds both scripts.
- [ ] `CLAUDE_CONFIG_DIR=~/.claude-smoketest claude` then `/mcp`: sanduhr is connected; "call
  the sanduhr ping tool" answers with `0.3.0-mac`.
- [ ] Statusline, Install…: the sheet shows `echo mine` as the statusline already set, with
  Combine (default), Replace and Cancel, a `mine` chip (blue) and Sanduhr's five chips (amber;
  session, weekly and weekly reset kept, context and model struck through), the own-row/same-row
  picker and one preview; Cancel changes nothing
  (`diff /tmp/st-settings.json ~/.claude-smoketest/settings.json` is empty). Install… again,
  Replace: Installed; `"model"` is still there; the Claude Code session above shows the meters
  under the prompt after its next refresh.
- [ ] Combine (item 63): Remove the statusline (the diff is empty again), then Install…. The
  preview shows `mine` then the meters on their own row; pick On the same row and it shows
  `mine │ 5h …` on one row (meters from the widget, or `5h 23%* | wk 41%*` from the sample data
  while the widget is stopped). Combine without touching a chip: the row reads "Combined with
  your statusline"; `jq -r .statusLine.command ~/.claude-smoketest/settings.json` ends in
  `--chain-b64 ZWNobyBtaW5l --join same` (no picks); the session shows `mine │ 5h 42% | wk 18% …`
  after its next refresh.
- [ ] Segments (item 63b): Remove, then put a multi-segment line in: `printf '#!/bin/sh\nprintf
  "\\033[34m~/proj\\033[0m | ⎇ main | \\033[33m+3\\033[0m\\n"\n' > /tmp/st-line.sh; chmod +x
  /tmp/st-line.sh; printf '{\n  "statusLine": {"type": "command", "command": "/tmp/st-line.sh"}\n}\n'
  > ~/.claude-smoketest/settings.json; cp ~/.claude-smoketest/settings.json /tmp/st-seg.json`.
  Install…: three blue chips `~/proj`,
  `⎇ main`, `+3`, "Split yours on Pipe | (found)", and "Join with: Same as yours"; every control has
  a caption under it and a tooltip on hover (each of Sanduhr's chips says what it prints, as in
  "Session: the 5-hour limit, as in 5h 42%"). Click `⎇ main`, Sanduhr's weekly and weekly reset,
  and model: the preview reads `~/proj | +3` (blue and yellow kept) then `5h … | Opus`, and
  updates on each click without running the line again. Switch Split yours on to None (one
  segment): at once one chip for the whole line, and the preview shows it whole; back to Pipe, the
  picks return. Join with Bar │: the preview reads `~/proj │ +3` and Sanduhr's part `5h … │ Opus`;
  Join with Same as yours puts `|` back. Test with live data: the chips and preview refill (meters
  from the widget, model and context from the latest session in this folder) and "Tested <time>
  with live numbers" shows; with no session there, it says model and context are sample values. Try to drop every one of Sanduhr's: the last stays. Combine: the command ends in
  `--keep-theirs-b64 … --mine session,model`, and the session's statusline matches the preview.
  Outside a repository the branch is missing: `printf '{}' | python3
  "$S/current/sanduhr_statusline.py" --chain-b64 $(printf 'printf "~/x | +0"' | base64) --join
  line --keep-theirs-b64 <the payload from the command>` prints `~/x | +0` whole, then the
  meters. Update (after a Sanduhr update shows Outdated) keeps the
  picks; Remove: `diff /tmp/st-seg.json ~/.claude-smoketest/settings.json` is empty. A
  powerline-style line (a copy, in this test folder): drop a middle segment, and the arrow
  between the two now-neighbors takes their colors, with no bleed into Sanduhr's segment.
- [ ] Kinds, duplicates, styles (round 3): with `printf "⎇ main | \$1.42 | Opus | ctx 38%%\n"` as
  the line, the chips read Branch `⎇ main`, Cost `$1.42`, Model `Opus`, Context `ctx 38%`, each
  tooltip like "Branch, from your statusline: ⎇ main". Turn on Sanduhr's Context and Model: both
  pairs get orange badges ("Also shown by Sanduhr: Context" and "Also shown by yours: Context"),
  and "2 duplicates: Model, Context" shows above the preview. Keep yours on Model: Sanduhr's Model
  is struck through; Keep Sanduhr's on Context: yours is. Brush on Branch: Gradient, two stops,
  Script letters: the preview draws `⎇ 𝓂𝒶𝒾𝓃` in the gradient, and the command's
  `--keep-theirs-b64` payload decodes to a `style` entry for `⎇`. Brush on Sanduhr's Session,
  Bold: the preview's `5h` is bold. Reset to its own look returns both. The Join with and Split
  yours on menus show drawn arrows beside "Powerline arrow (needs a Nerd Font)" and "Powerline
  thin arrow (needs a Nerd Font)", never boxes; with Join with on Powerline arrow, the preview
  draws filled triangles, not boxes.
- [ ] Mods: with no mods in the folder, "From your mods" says "No mods in this folder draw status
  entries." Make one in the test folder: `mkdir -p /tmp/st-mod/hooks; printf '{"modules":
  ["./r.tsx"]}' > /tmp/st-mod/hooks/hooks.json; printf '$.ui.status("hi")\n' > /tmp/st-mod/hooks/r.tsx`,
  and add `"env": {"CLAUDE_CODE_PLUGIN_DIRS": "/tmp/st-mod"}` to `/tmp/st-seg.json`'s copy in
  `~/.claude-smoketest/settings.json`. Install…: a purple `st-mod` chip that can't be clicked, the
  caption saying Claude Code draws it beside the statusline, and a "Claude Code's status area"
  preview row reading `⚠ st-mod: …`. Cancel. Put the `echo mine` file back (`cp /tmp/st-settings.json
  ~/.claude-smoketest/settings.json`) before the next step.
- [ ] A slow and a failing line: `printf '{\n  "model": "opus",\n  "statusLine": {"type":
  "command", "command": "sleep 5; echo slow", "padding": 1, "refreshInterval": 5}\n}\n' >
  ~/.claude-smoketest/settings.json; cp ~/.claude-smoketest/settings.json /tmp/st-slow.json`,
  Install…, Combine (own row). `padding` and `refreshInterval` are still there; the session's
  statusline shows the meters alone within about 1.5 s of each refresh, and `pgrep -f "sleep 5"`
  is empty two seconds later. Repeat with `"command": "echo oops; exit 2"`: `oops` and the
  meters both show. A powerline-style line of your own (a copy, in this test folder) on the same
  row: no color bleeds into Sanduhr's segment, and in a narrow terminal it moves to its own row.
- [ ] Remove: `diff /tmp/st-slow.json ~/.claude-smoketest/settings.json` is empty (siblings
  included). Put the `echo mine` file back (`cp /tmp/st-settings.json
  ~/.claude-smoketest/settings.json`) and Install…, Replace again for the next step.
- [ ] Remove both: `diff /tmp/st-claude.json ~/.claude-smoketest/.claude.json` and `diff
  /tmp/st-settings.json ~/.claude-smoketest/settings.json` are empty (the `echo mine` statusline
  is back), the `.sanduhr-backup` files are gone, and with nothing installed anywhere `"$S"`
  holds no `current` and no stamped folder (install.sh's own copies, if any, stay).
- [ ] `~/.claude-smoketest2` has no `.claude.json` or `settings.json`: install both, then remove
  both: the folder holds only `projects` again.
- [ ] Break `~/.claude-smoketest/settings.json` (add a trailing comma): the row says it isn't
  valid JSON and offers no button; the file is unchanged. Fix it.
- [ ] Update path: install the MCP server, then `ls "$S"`, quit, build and launch a copy whose
  `sanduhr_mcp.py` differs (any edit in `mac/integrations/`, `./build.sh --debug`): after launch
  `current` points at a new stamp, the old stamp folder is still there, and
  `.claude-smoketest/.claude.json` is unchanged; the row reads Installed. Launch again: the old
  stamp folder is gone.
- [ ] `state.yaml` shows `integrations: {mcp_installed: N, statusline_installed: N}` with the
  counts, and no path. `log stream --predicate 'subsystem == "com.626labs.sanduhr"'` while
  installing shows no path or label.
- [ ] Clean up: Remove everything installed above, then `rm -rf ~/.claude-smoketest2
  /tmp/st-claude.json /tmp/st-settings.json` (and `~/.claude-smoketest` once sections 16 to 19
  are done).

## 21. Meters above Claude Code's prompt (item 50)

A Claude Code with mods (`claude plugin test --help` works). Use a test folder, never your real
`~/.claude*`. The `/tmp/other-mod` entry stands for a mod of your own already in the list:

```sh
mkdir -p ~/.claude-smoketest/projects /tmp/other-mod
printf '{\n  "model": "opus",\n  "env": {\n    "CLAUDE_CODE_PLUGIN_DIRS": "/tmp/other-mod"\n  }\n}\n' > ~/.claude-smoketest/settings.json
cp ~/.claude-smoketest/settings.json /tmp/st-meters.json
S=~/Library/Application\ Support/Sanduhr/integrations
```

- [ ] `claude plugin test mac/integrations/mods/sanduhr-meters` and `claude plugin validate
  mac/integrations/mods/sanduhr-meters` pass.
- [ ] Settings, Integrations: `~/.claude-smoketest` has a third row, Meters above the prompt, Not
  installed. Its Install… works even where the page says Python is missing.
- [ ] Install…: the sheet says the band shows the session and weekly bars, reads only
  `snapshot.json`, and that Sanduhr adds its folder to `env.CLAUDE_CODE_PLUGIN_DIRS` in
  `~/.claude-smoketest/settings.json`, keeping the folders already listed. Install: Installed;
  `jq -r '.env.CLAUDE_CODE_PLUGIN_DIRS' ~/.claude-smoketest/settings.json` prints
  `/tmp/other-mod:<…>/integrations/current/mods/sanduhr-meters`, `"model"` is still there, and
  `ls "$S/current/mods/sanduhr-meters"` shows `.claude-plugin`, `hooks`, `types` and no tests.
- [ ] `CLAUDE_CONFIG_DIR=~/.claude-smoketest claude`: above the prompt, `Session ▕…▏ N% resets
  …   Weekly ▕…▏ N% resets …` with the widget's percentages and countdowns (compare with the
  cards), the pink pace mark where the card's pace marker is, colors as on the cards. After a
  widget refresh the band changes within 30 seconds; a countdown moves each minute.
- [ ] States, each in a fresh session with `SANDUHR_SNAPSHOT=/tmp/snap.json` in front of the
  command above, writing `/tmp/snap.json` first:
  `W=$(date -u -v+2d +%FT%TZ); A=$(date -u -v-9M +%FT%TZ)` and
  `printf '{"schema_version":1,"captured_at":"%s","status":"ok","error_kind":null,"tiers":[{"key":"seven_day","utilization":93,"resets_at":"%s"}]}' "$A" "$W" > /tmp/snap.json`:
  the band is dimmed with a red ⚠ after 93% and `(9m ago)`, and a notice says the weekly limit
  is at 93%; a second session shows no notice. With `captured_at` 20 minutes back: one dim line,
  `Sanduhr: no update for 20m. Is the widget running?`. With `"status":"error",
  "error_kind":"session_expired","tiers":[]`: `Sanduhr: sign in to see your meters.`. With
  `SANDUHR_SNAPSHOT=/tmp/none.json`: no band at all.
- [ ] Session reset: write a session tier whose `resets_at` is a minute ahead
  (`date -u -v+1M +%FT%TZ`); within the next minute the bar becomes `Session reset` and a notice
  says the session limit has reset, once.
- [ ] `/config`: the mod's Meters (both, session, weekly), Style (compact, full) and Label rows;
  full shows a line per meter with `on pace` or `N% ahead` and `resets Mon 9:00 AM`; a short
  terminal folds it to one line; the label shows first.
- [ ] Remove: `diff /tmp/st-meters.json ~/.claude-smoketest/settings.json` is empty and the
  `.sanduhr-backup` is gone. Install again, add `:/tmp/mine` after Sanduhr's entry by hand,
  Remove: the list reads `/tmp/other-mod:/tmp/mine`.
- [ ] `state.yaml` shows `meters_installed` with the count and no path. Clean up: Remove, then
  `rm -rf /tmp/other-mod /tmp/st-meters.json /tmp/snap.json`.

## 22. The notch glows when Claude Code needs you (item 51)

The installed hooks post a Darwin notification (`/usr/bin/notifyutil -p com.626labs.sanduhr.claude-code.waiting` or `.done`): every running Sanduhr hears it, a dev build too, and nothing is ever launched. A `sanduhr://` link, which the steps below also use, goes to the default app for the scheme and can launch the installed copy: aim link tests at the dev build with `open -g -a <dev Sanduhr.app> 'sanduhr://claude-code?event=waiting'`.

Use a test folder, never your real `~/.claude*`. The `say` hook stands for a hook of your own:

```sh
mkdir -p ~/.claude-smoketest/projects
printf '{\n  "hooks": {\n    "Stop": [\n      {\n        "hooks": [\n          { "type": "command", "command": "say done" }\n        ]\n      }\n    ]\n  }\n}\n' > ~/.claude-smoketest/settings.json
cp ~/.claude-smoketest/settings.json /tmp/st-hooks.json
```

- [ ] The link alone, before any install: Settings, Notch, Glow for Claude Code, both switches
  off. Bring Finder to the front and run `open -g 'sanduhr://claude-code?event=waiting'` from a
  script or another Mac app (or with "Not while a terminal is in front" off): nothing glows.
  Turn "When Claude Code is waiting on you" on: the same command glows the notch once within a
  second (around the island with the notch on, around the hardware notch with it off). Again
  within 20 seconds: no glow; after 20 seconds: one glow.
- [ ] `event=done` with "When Claude Code finishes" on glows once, but not within 5 seconds of a
  waiting glow. `open -g 'sanduhr://claude-code?event=other'`,
  `open -g 'sanduhr://claude-code?event=waiting&x=1'` and `open -g 'sanduhr://claude-code'`: no
  glow, no window, no error.
- [ ] "Not while a terminal is in front" on: run the waiting command in Terminal (Terminal stays
  in front): no glow. Switch it off: it glows from Terminal too. Put it back on.
- [ ] Settings, Integrations: `~/.claude-smoketest` has a fourth row, Notch glow when Claude needs
  you, Not installed, and Install… works even where the page says Python is missing. Under the
  folders, a line says the glow is off until turned on in Notch, Glow, and Glow Settings… opens
  the Notch page.
- [ ] Install…: the sheet says Claude Code tells Sanduhr only that it is waiting or finished,
  nothing about the conversation, and that Sanduhr adds one entry to each of
  `hooks.Notification and hooks.Stop` in `~/.claude-smoketest/settings.json`; Glow Settings…
  there opens the Notch page. Install: Installed;
  `jq '.hooks.Stop | length, .[0].hooks[0].command' ~/.claude-smoketest/settings.json` prints 2
  and `"say done"`, and `jq '.hooks.Notification[0]' ~/.claude-smoketest/settings.json` shows the
  matcher `permission_prompt|idle_prompt|elicitation_dialog` and the
  `/usr/bin/notifyutil -p com.626labs.sanduhr.claude-code.waiting || true` command with
  `"async": true`. `/usr/bin/notifyutil -p com.626labs.sanduhr.claude-code.waiting` from a script
  glows like the link.
- [ ] Outdated: put an earlier version's command back,
  `jq '.hooks.Notification[0].hooks[0].command = "/usr/bin/pgrep -xq Sanduhr && /usr/bin/open -g '"'"'sanduhr://claude-code?event=waiting'"'"' || true"' ~/.claude-smoketest/settings.json > /tmp/st-old.json && mv /tmp/st-old.json ~/.claude-smoketest/settings.json`:
  the row reads Outdated. Install: Installed, and
  `grep -c 'sanduhr://' ~/.claude-smoketest/settings.json` prints 0.
- [ ] With Claude Code: both switches on, "Not while a terminal is in front" on.
  `CLAUDE_CONFIG_DIR=~/.claude-smoketest claude` in a scratch folder, ask for something that needs
  a permission (`create a file x.txt`), and switch to Finder before it asks: the notch glows
  within a second of the prompt. Answer it; when the turn finishes with Finder in front, a
  second glow (unless within 5 seconds of the first). With the terminal in front: no glow, and
  you hear "done" from your own hook every turn. Claude Code never waits on the hooks and shows
  no hook error.
- [ ] Quit Sanduhr; finish a Claude Code turn: Sanduhr does not start (`pgrep -x Sanduhr` finds
  nothing), and no `watch-stop-*` file appears in `~/Library/Application Support/Sanduhr`. With a
  dev build running and the installed copy quit, a turn glows the dev build's notch and the
  installed copy stays quit. Open it again.
- [ ] No notch (an external display alone, or a Mac without one): the waiting command glows a
  soft halo around a notch-wide spot at the top center of the main screen; `state.yaml` shows
  `glow_shape: top`.
- [ ] Remove: `diff /tmp/st-hooks.json ~/.claude-smoketest/settings.json` is empty and the
  `.sanduhr-backup` is gone; a waiting prompt no longer glows. Install with no `settings.json`
  (`rm ~/.claude-smoketest/settings.json`): it is created; Remove deletes it again.
- [ ] `state.yaml` shows `hooks_installed` with the count, `glow_claude_waiting` and
  `glow_claude_done`, and no path. Clean up: Remove, then `rm -f /tmp/st-hooks.json` (and
  `rm -rf ~/.claude-smoketest` once the other sections are done).

## 23. Now playing (items 53, 53b)

Desk on. Have YouTube Music open in Chrome (or Safari) and Music with a song queued; start and stop
playback yourself. Never put titles in a bug report: `state.yaml` carries only flags.

- [ ] `lipo -archs mac/Sanduhr.app/Contents/Frameworks/MediaRemoteAdapter.framework/Versions/A/MediaRemoteAdapter`
  and `lipo -archs mac/Sanduhr.app/Contents/Helpers/MediaRemoteAdapterTestClient` print
  `x86_64 arm64`; `codesign --verify --strict mac/Sanduhr.app` passes.
- [ ] Placed nowhere by default: Settings, Desk, Now Playing has no on/off switch and reads Source:
  Not placed anywhere, with "Arrange on the Notch…" and "Arrange on the Desk…" (each opens that
  page); `pgrep -fl mediaremote-adapter` finds nothing; `state.yaml` shows
  `now_playing: {enabled: false, placed: [], source: off, state: none}`. Settings, Notch lists Now
  playing as a choice for both wings and the strip; Settings, Layout lists Now playing as Hidden.
- [ ] Pick Now playing for the right wing (island on): that alone starts it. Source reads Checking…
  for a moment, then Adapter working; one `perl … mediaremote-adapter.pl … stream` process runs
  (`pgrep -fl mediaremote-adapter`); `state.yaml` `placed: [wing_right]`, `enabled: true`. Put Now
  playing in Layout at Bottom left too: `placed: [wing_right, desk]`.
- [ ] Play in YouTube Music: within two seconds the right wing shows "▶ Title · Artist" and the Desk
  shows the line with a position bar in its corner (under the meters when they share it), the bar
  moving about once a second. `state.yaml`: `source: adapter`, `state: playing`, no title anywhere.
- [ ] A long title (longer than the wing's 180 points): the wing grows to its limit and the text
  rests at its beginning with a soft fade at the clipped end, then after about a second scrolls
  through once, slowly (about 30 points a second, easing in and out), rests at the end, glides back
  and stays at the beginning. It never starts again for the same track; the next track (or picking
  the wing again) scrolls once more. A short title never moves. With System Settings,
  Accessibility, Display, Reduce motion on, nothing scrolls: the beginning shows, faded at the end.
  The strip (Notch, Text under the camera, Now playing) does the same when the line is too long for
  it.
- [ ] Pause in the browser: the glyph turns to a pause sign and the bar stops within two seconds; a
  Next button (⏭) appears at the wing's outer edge (the right wing's right end, the left wing's left
  end), the title still starting at its beginning. Pausing does not scroll the title. Click the
  button: the next track plays and the button goes. Pause again and click the title: it plays and
  the button goes. Skip twice: each new title shows without the wing blinking empty in between.
- [ ] With the strip on Now playing and paused, the strip shows ⏭ at its end; `state.yaml` lists a
  `now_playing_next` `strip` frame beside the `now_playing` `strip` one and `desk_frames_ok: true`;
  a click on ⏭ skips, a click on the title plays.
- [ ] Click the wing: playback pauses; click again: it plays. Two-finger click the wing: Previous,
  Pause, Next, Now Playing Settings…; Next and Previous skip; the last item opens this page.
  The rest of the island still opens Settings on a click.
- [ ] The Desk line: a click plays or pauses (pointing hand on hover); a two-finger click opens the
  same menu and the Finder's desktop menu never opens there; `state.yaml` lists a `now_playing`
  `desk` frame and `desk_frames_ok: true`. With the strip on Now playing (Notch, Text under the
  camera), the strip shows the whole title and takes the same clicks (a `now_playing` `strip` frame).
- [ ] Stop the browser tab and play in Music: the wing and the line follow Music within two seconds.
  Settings, Apps lists Chrome and Music; switch Chrome off and play in the browser: the wing shows
  its stand-in (When nothing is playing, below), never the browser's title; switch it back on.
- [ ] Hide while paused on: pausing hides the line and puts the wing's stand-in in its place
  (When nothing is playing, below); playing brings both back. With nothing playing at all, the line
  is gone and no timer ticks.
- [ ] When nothing is playing. Right wing and the strip (Text under the camera on) on Now playing,
  nothing playing. Settings, Desk, Now Playing has "When nothing is playing" reading "What that spot
  shows by default", with its caption naming the wings and the strip and saying the Desk line simply
  hides. The right wing shows the Claude meters, sized like the meters (no gap for ⏭), and the strip
  the next meeting or the meters; `state.yaml` `now_playing_idle: automatic`,
  `notch_shows: {left: …, right: meters, strip: meetingOrMeters}`, still `placed: [wing_right, strip]`
  and `enabled: true`, and `desk_frames` has no `now_playing` `strip` frame. A click on the right wing
  opens Settings (not play or pause); a two-finger click there shows no now playing menu. Settings,
  Notch shows "When nothing plays: Claude meters (its default)." under Right wing and "When nothing
  plays: Next meeting, or the Claude meters (its default)." under Under the camera; Change… opens the
  Now Playing page. Pick Time: both show the time at once and the captions read "When nothing plays:
  Time."; `now_playing_idle: time`. Play something: within two seconds both show the track again
  (`notch_shows` back to `nowPlaying`) with their clicks; pause with Hide while paused on: the time
  comes back. Pick Nothing: with nothing playing both go plain black, as before this choice. Put it
  back on "What that spot shows by default".
- [ ] Move Now playing to Hidden in Layout: the line goes, the wing stays and it keeps running. Then
  set the right wing back to Claude meters: `placed: []`, the Source reads Not placed anywhere and
  `pgrep -fl mediaremote-adapter` finds nothing within a second. Place it again, then switch Desk off:
  the same. Quit Sanduhr while it runs: no `perl` process is left behind.
- [ ] Upgrade from item 53's switch: quit, `defaults write com.626labs.sanduhr.desk nowPlaying -bool
  true`, `defaults write com.626labs.sanduhr.desk layout "message:tl clock:bl meters:bl meetings:bl"`
  and `defaults delete com.626labs.sanduhr.desk nowPlayingPlacementUpgraded`, then open Sanduhr:
  `layout` reads `message:tl clock:bl meters:bl nowPlaying:bl meetings:bl` and the line sits under
  the meters. Move it to Hidden and relaunch: it stays hidden.
- [ ] Fallback: quit, then `open -n --env SANDUHR_NOWPLAYING_TEST=fail mac/Sanduhr.app`. Source
  reads "Fallback (Music and Spotify only): the system now playing isn't available on this macOS";
  no adapter process runs; no prompt appears. Play, pause or skip in Music: the wing follows from the
  next change. The browser player shows nothing (expected). Clicks still play and pause.
- [ ] Still on the fallback, switch Ask Music and Spotify directly on with Music open: macOS asks
  once whether Sanduhr may control Music; Allow, and the current track shows without waiting for a
  change. With Music quit the switch launches nothing. Switch it off again and reset the grant with
  `tccutil reset AppleEvents com.626labs.sanduhr` if you want the prompt back.
- [ ] Sleep the Mac while now playing runs and wake it: Source settles on Adapter working again and
  the wing follows the next change.
- [ ] Settings, About: "Now playing uses mediaremote-adapter by Jonas van den Berg (BSD-3-Clause)"
  links to its GitHub page; Third-Party Notices opens the text with the adapter's and Sparkle's
  licenses. Console (`log stream --predicate 'subsystem == "com.626labs.sanduhr" AND category ==
  "nowplaying"'`) shows only on/off, test and stream lines, never a title.

## 24. Claude writes your Desk messages (item 54)

Desk on with the message in a corner. Back up the list first and put it back at the end:
`M=~/Library/Application\ Support/Desk/messages.txt; cp "$M" /tmp/messages.txt.mine`. Settings,
Alerts on (Banner) for the notification step. The probe below calls the installed server the way
Claude Code does, one stdio round trip per call (`S=~/Library/Application\
Support/Sanduhr/integrations/current/sanduhr_mcp.py`, or `mac/integrations/sanduhr_mcp.py`):

```sh
mcp() { printf '%s\n' "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"$1\",\"arguments\":${2:-{\}}}}" \
  | python3 "$S" | python3 -c 'import json,sys; print(json.dumps(json.loads(json.loads(sys.stdin.read())["result"]["content"][0]["text"]), indent=1, ensure_ascii=False))'; }
```

- [ ] `mcp get_desk_messages`: `lines` match `cat "$M"` (comments included), `today` is the line
  the Desk shows, `pinned: false`, `rotate: "daily"`. Pin a line in Settings, Message: within a
  second `jq . ~/Library/Application\ Support/Sanduhr/desk-messages-state.json` shows
  `pinned: true` with the line, and `get_desk_messages` returns it as `today`. Unpin; Every hour
  makes `rotate: "hourly"`; back to Once a day.
- [ ] A bad proposal is refused at once and writes nothing:
  `mcp propose_desk_messages '{"lines":["{blink} hi","Monday: x","13-40: y"]}'` lists three reasons
  (line 1 unknown effect, line 2 write the day as Mon, line 3 not a date); no
  `desk-messages-request.json` appears in the Sanduhr folder.
- [ ] Approve: `mcp propose_desk_messages '{"lines":["{ink:#ff2a6d,#05d9e8} {glow} {write} hello there.","{shimmer} {size:0.8} keep going."],"note":"two for the week"}'`
  answers `pending_approval` within a second. A quiet banner "Claude suggested 2 Desk messages"
  (body "two for the week", no sound) shows; clicking it opens Settings, Message. The sidebar's
  Message has a badge; the page shows the banner with the note, Dismiss, Review… and Add. Review…
  draws both lines on a dark card: the first writes itself in, left to right, in a pink-to-cyan
  gradient with a glow; the second, smaller, gets a light sweep every 8 seconds. Add: the badge
  and banner go, the editor shows the two lines at the end of the list, `messages.txt.previous`
  holds the list as it was, and `jq .result ~/Library/Application\ Support/Sanduhr/desk-messages-result.json`
  reads `applied` with `lines_added: 2`. Propose the same lines again and Add: `lines_skipped: 2`.
- [ ] Dismiss: propose one line, Dismiss: the list is unchanged and the result reads `rejected`
  with `dismissed by the user`. With unsaved edits in the editor, Add is disabled ("Save or reload
  your edits first").
- [ ] Replace: propose with `"mode":"replace"`: the banner and Review say "to replace your list";
  after Replace the comment block at the top of the file is still there, every old line is gone
  and `messages.txt.previous` has them.
- [ ] Apply directly: Settings, Message, "Let Claude change the messages directly" on. Propose a
  line: the answer is `applied`, no banner, no badge; the editor reloads with it.
- [ ] Alerts off (Settings, Alerts): a proposal shows only the badge and the page's banner, no
  notification.
- [ ] The effects on the Desk: pin each of these in turn (Pin one line): `{write} hello.` draws in
  over about a second and a half, once; `{shimmer} hi.` sweeps every 8 seconds; `{ink:#ffd08a}
  {noglow} {size:1.5} big.` is gold, larger, without glow; `{blink} {glow} odd.` shows the text
  `{blink} {glow} odd.` as written. Settings, Desk, Look, "Glow around the message" off: lines
  lose the glow except a `{glow}` line. With Reduce motion on, `{write}` shows at once and nothing
  shimmers. With a `{shimmer}` line showing and Activity Monitor on Sanduhr: CPU is 0.0 between
  sweeps and with the Desk covered by a full-screen window (the sweep pauses); a plain line costs
  nothing at any time.
- [ ] Letter styles and the sweep (item 65), pinning each in turn with the Desk in EsteFont 26:
  1. `{font:smallcaps} {sweep} showtime.` draws SHOWTIME in the handwriting with the S full height
     and the rest smaller, at about the height of lowercase letters; about half a second after it
     appears a small white light, about three letters wide, crosses it once in about a second.
  2. `{font:bold} ship it.` is in EsteFont 26 Bold; `{font:italic} ship it.` leans right, the
     handwriting itself, not another font; `{font:bold-italic} ship it.` is both.
  3. `{font:fraktur} Hello 42.` draws in fraktur (the H is ℌ), the digits plain;
     `{font:double-struck} Room 42.` has double-struck digits too; `{font:script} Bonjour.` has a
     script ℬ; `{font:mono} x` and `{font:sans} x` draw; none of them is a box or a question mark.
  4. `{sweep:5} hi.` sweeps every 5 seconds. Cover the Desk with a full-screen window for 20
     seconds and come back: it starts again, and Activity Monitor shows Sanduhr at 0.0 CPU while
     covered and between sweeps. `{sweep} hi.` sweeps once and does not sweep again after the Desk
     is covered and uncovered.
  5. Reduce Motion on: no line sweeps; the letter styles still draw.
  6. `{font:outline} hi.` and `{sweep:1} hi.` show as written, tags included.
  7. Settings, Desk, Notch, a wing or the strip on Message: `{font:smallcaps} {sweep} hi.` shows
     HI in small caps and no tags; `{font:fraktur} Hi` shows ℌ𝔦.
  8. VoiceOver on the Desk line `{font:fraktur} Hello.` reads "Hello.".
  9. Settings, Message with `{sweep} hi.` pinned: the preview has Replay, and Replay sweeps the
     line at once.
- [ ] Quit Sanduhr and propose: the answer is `queued` / `app_not_responding` after about ten
  seconds; open Sanduhr within ten minutes and the suggestion appears. One older than ten minutes
  is dropped. `log show --last 5m --predicate 'subsystem == "com.626labs.sanduhr"'` holds no
  message text. Put the list back: `cp /tmp/messages.txt.mine "$M"`.

## 25. Claude proposes themes (item 55)

The `mcp` probe from section 24. Alerts on (Banner). Note the theme in use, and keep a copy of the
themes folder: `T=~/Library/Application\ Support/Sanduhr/themes; cp -R "$T" /tmp/themes.mine`.
`G='"glass":"#14262e","glass_on_mica":"#10222a","title_bg":"#0c1c22","border":"#1f3a44","footer_bg":"#081418","bar_bg":"#12303a","text":"#e6f4f1","text_secondary":"#a9cfc8","text_dim":"#6f9a93","text_muted":"#4f7a73","accent":"#2dd4bf","pace_marker":"#fb7185","sparkline":"#2dd4bf"'`.

- [ ] A broken theme is refused at once and writes nothing:
  `mcp propose_theme '{"theme":{"name":"Broken","bg":"#fff"}}'` answers `rejected` /
  `invalid_theme` with `findings` naming `bg` (`#rrggbb`) and every missing color; no
  `theme-request.json` appears. `{"theme":{"name":"Obsidian",'"$G"',"bg":"#0b1418"}}` answers
  `reserved_name`.
- [ ] Approve: `mcp propose_theme '{"theme":{"name":"Tidepool","description":"Deep teal glass with a coral pace tick.","bg":"#0b1418",'"$G"'}}'`
  answers `pending_approval` within a second with `key: "tidepool"`. A quiet banner "Claude
  suggested a theme: Tidepool" shows (body: the description); clicking it opens Settings, Themes.
  The sidebar's Themes has a badge; the page's banner shows a teal card like the gallery's, the name,
  the description, Dismiss, Save and Save and Apply. Save and Apply: the widget turns teal, the card
  appears in the gallery marked as yours with the description in its tooltip, the badge goes, and
  `jq .result ~/Library/Application\ Support/Sanduhr/theme-result.json` reads `applied`,
  `previous_key` the theme you had and `saved_path` ending `tidepool.json`. Pick your old theme
  again in the gallery.
- [ ] Save only: propose it again with `"save_as":"tidepool-calm","apply":false` and click Save: the
  file appears, the widget keeps its theme, the result reads `saved`.
- [ ] Collision: `tidepool.json` from the step above is now one of your themes. Propose Tidepool
  with a changed `accent` (`"accent":"#38bdf8","sparkline":"#38bdf8"`): the banner says it
  is saved as `tidepool-2.json`; after Save, `tidepool.json` is unchanged and the result names
  `renamed_from: "tidepool"`. Proposing the very same theme as `tidepool` again says "You already
  have this theme" and adds no file.
- [ ] Warnings ride along: propose with `"text":"#5a5a5a"`: `pending_approval` with a `text`
  warning in `findings`; the banner shows "1 design note" with the message on hover. Dismiss: the
  result reads `rejected` / `dismissed`, no file is written.
- [ ] Apply directly: Settings, Widget, Themes, "Let Claude change themes directly" on. Propose a
  new theme: the answer is `applied` at once, no banner, no badge, the widget changes. With
  `"apply":false`: `saved` and the widget stays.
- [ ] Match Desk: with Match Desk in use, a proposal with `apply` true moves the widget off Match
  Desk to the new theme; `previous_key` reads `match-desk`, and clicking Match Desk in the gallery
  brings the Desk's ink back.
- [ ] Quit Sanduhr and propose: `queued` / `app_not_responding` after about ten seconds; open
  Sanduhr within ten minutes and the suggestion appears. `log show --last 5m --predicate
  'subsystem == "com.626labs.sanduhr"'` holds no theme name. `state.yaml` shows
  `pending_suggestions: {messages: false, theme: true}` while one waits. Put the folder back:
  `rm -rf "$T" && cp -R /tmp/themes.mine "$T"`.

## 26. Desk items move clear of the Dock (item 56)

Desk on with something in a bottom corner (the standard layout: clock, meters, meetings at `bl`;
add `nowPlaying:br` to have the right side too). Note your Dock settings in System Settings,
Desktop & Dock, and put them back at the end. After each step, `mac/smoke/smoke snap` and read
`dock:` and `desk_frames_ok` in its `state.yaml` (see `mac/smoke/README.md`).

- [ ] Dock shown, at the bottom: the bottom-left stack sits above the Dock with the usual gap,
  nothing under it. `dock: {side: bottom, autohide: false, inset: N}`, N the Dock's height (about
  60 to 90), and `desk_frames_ok: true`. Make the Dock larger and smaller with the Size slider: the
  stack follows within a second.
- [ ] Dock shown, on the left: the left column (top and bottom) moves right of the Dock, the right
  column stays; `side: left`. On the right: the right column moves; `side: right`. The message at
  the top moves with its column; nothing at the top goes under the menu bar.
- [ ] Auto-hide on (bottom): the stack rests at the screen edge margin (`inset: 0`). Move the
  pointer to the bottom edge and rest it there: the stack starts up together with the Dock, not
  after it (the Dock's own delay, `defaults read com.apple.dock autohide-delay`, 0.5 s when unset),
  and rises as fast as the Dock does (`autohide-time-modifier` scales it, about half a second when
  unset); `inset` reads the Dock's height while it shows. Move away and the stack drops in step
  with the Dock. Rest the pointer a few points above the edge, short of where the Dock reacts:
  nothing moves, or the stack comes up and drops back within about half a second and stays down.
  Meeting rows and the meters' two-finger menu work while the Dock is up; `desk_frames_ok`
  stays true during and after.
- [ ] Auto-hide on the left and on the right: the same, sideways.
- [ ] Reduce Motion on (Accessibility, Display): the stack jumps instead of gliding.
- [ ] No cost away from the Dock: with the pointer in the middle of the screen for a minute,
  Activity Monitor shows Sanduhr at 0.0 % CPU (as before this item).
- [ ] Mission Control with the pointer in the middle: the Dock shows there and the Desk is not
  visible, nothing moves; afterwards the stack is back at the edge. A full-screen app: the Desk is
  not shown there; leave it and the Desk is where it was.
- [ ] Two screens, Dock shown at the bottom of the other screen: the Desk's screen keeps
  `inset: 0`. Move the Dock to the Desk's screen (bottom edge there): the stack moves up. With
  auto-hide, the Dock coming up on the other screen moves nothing on the Desk's.
- [ ] Put your Dock settings back as they were.

## 27. EsteFont Pro and EsteFont 26, built in (items 58, 65 (e))

Use a Mac or a user account where neither font is installed. On your own account, turn the
installed copies off instead: Font Book, select EsteFont Pro, EsteFont 26 (and any older
EsteFont), Edit, Disable (or right-click, Deactivate), and turn them back on at the end. Never
delete your fonts. Quit Sanduhr first and open the build afterwards, so it starts with the fonts
switched off.

- [ ] The built app has every face: `ls Sanduhr.app/Contents/Resources/Fonts` lists
  `EsteFontPro-Regular.ttf`, `EsteFontPro-Bold.ttf`, `EsteFont26-Regular.ttf` and
  `EsteFont26-Bold.ttf`, and `codesign --verify --strict Sanduhr.app` is quiet (`build.sh` also
  fails without them).
- [ ] Fresh install (a new user account, or `defaults delete com.626labs.sanduhr` and
  `defaults delete com.626labs.sanduhr.desk` on a test account only): the Desk's clock, date,
  meters and message draw in EsteFont Pro, the time in Bold. Settings, Desk, Look shows Desk font
  EsteFont Pro.
- [ ] Font Book still shows EsteFont Pro and EsteFont 26 off (or absent): Sanduhr did not install
  them. TextEdit's font list does not have them.
- [ ] Both pickers list EsteFont Pro first and EsteFont 26 right after it, under System:
  Settings, Desk, Look (Desk font and Message font) and Settings, Widget, Look. Pick EsteFont Pro
  for the widget: the cards draw in it, the semibold lines in Bold. Pick EsteFont 26: the same,
  in its own Bold. Use System Font brings the widget back.
- [ ] Pick EsteFont 26 for the Desk: the clock's time draws in EsteFont 26 Bold, the rest in its
  Regular; quit and reopen: it stays.
- [ ] Match Desk with the Desk on EsteFont Pro: the widget draws in EsteFont Pro.
- [ ] Pick another Desk font, quit and reopen: it stays. Pick System: the Desk draws in the system
  font after a relaunch too.
- [ ] A saved font that is gone: `defaults write com.626labs.sanduhr.desk font "No Such Font"`,
  relaunch: the Desk draws in EsteFont Pro and the picker shows EsteFont Pro.
- [ ] Upgrade from 2.6.0 to 2.8.0 with no Desk font picked (the Desk drew in EsteFont 26): the Desk
  keeps EsteFont 26 (`defaults read com.626labs.sanduhr.desk font` prints `EsteFont 26`).
- [ ] Upgrade from a build before 2.6.0 with no Desk font picked: the Desk keeps the system font
  (`defaults read com.626labs.sanduhr.desk font` prints an empty line).
- [ ] Settings, About shows "Handwriting: EsteFont Pro and EsteFont 26 by Estevan Hernandez";
  Third-Party Notices opens with an EsteFont Pro and EsteFont 26 section naming all four files,
  with the copyright and license.
- [ ] Turn your EsteFont copies back on in Font Book.

## 28. What's New after an update (item 57)

Fake an older version with the switch below; never on a fresh test account's first launch (that
one records the version and shows nothing). Quit Sanduhr before each `defaults` command and open
the build afterwards.

- [ ] Update from an older build: `defaults write com.626labs.sanduhr whatsNewLastSeen 2.3.4`,
  open the build. About two seconds after the widget and Desk are up, a window "What's New in
  Sanduhr" shows the cards of every release since, newest first, at most 8 (from 2.3.4 to 2.7.0:
  three of 2.7, three of 2.6, two of 2.5, under "New in 2.5.0 – 2.7.0"; from 2.6.0: sign-in, the
  tour and now playing, under "New in 2.7.0"); no card has its own version line. Each card has its art (a symbol,
  or a live preview: the Desk message writing itself in, EsteFont 26, a notch wing, the menu bar),
  a title, a sentence or two and Show me. `smoke/smoke state` shows `whats_new:
  { last_seen: <this version>, pending: 0, open: true }`.
- [ ] Show me on each card closes the window and opens Settings at its page: sign-in at Accounts,
  the tour at About (Take the Tour… is there), Now playing at Now
  Playing, Claude's messages and themes at Message, the Dock at Layout, EsteFont 26 at Desk, Look,
  Claude Usage at Claude Usage, integrations at Integrations, accounts at Accounts, the menu bar
  and limits at General.
- [ ] Quit and open again: no window (`defaults read com.626labs.sanduhr whatsNewLastSeen` prints
  this version).
- [ ] Settings, About, What's New…: the window opens with every card up to this version under
  "New in 2.4.0 – <this version>", and again
  from What's New… in the menu bar item's menu, the widget's two-finger menu and the Desk clock's
  menu (under Check for Updates…). Done (or Return) closes it.
- [ ] Don't show after updates: tick it, quit, `defaults write com.626labs.sanduhr whatsNewLastSeen
  2.3.4`, open: no window, and `whatsNewLastSeen` is this version again; About's What's New… still
  opens it. Untick it.
- [ ] Onboarding first: on a test account with no session key and an older `whatsNewLastSeen`, the
  onboarding sheet shows and What's New does not; it shows on a later launch after signing in.
- [ ] Fresh install (test account only): `defaults delete com.626labs.sanduhr` and `defaults
  delete com.626labs.sanduhr.desk`, open: onboarding, no What's New, and `whatsNewLastSeen` is
  this version.
- [ ] `smoke/smoke run scenarios/whats-new.yaml` passes (opens it, reads the title, Show me and the
  switch, closes it).
- [ ] Put `whatsNewLastSeen` back to this version: `defaults write com.626labs.sanduhr
  whatsNewLastSeen <version>`.

## 29. Sign in to Claude inside Sanduhr (item 62)

Use a dev build: it keeps keys in `credentials.json`, which a release build copies into the
Keychain at its next launch. Back up the defaults first, and delete `credentials.json` before
the installed app starts again.

1. With no key saved, the welcome sheet shows **Sign In to Claude…** (default) and **Paste a Key
   Instead**. Paste a Key Instead opens Settings → Accounts.
2. Sign In to Claude… opens "Sign in to Claude" on claude.ai's login. Sign in with an email
   account: the window closes within two seconds of reaching claude.ai, and the widget fetches.
3. Again with a Google-created account: choosing Google (or Continue with email with its
   address) lands on Google's page, and the panel "This account signs in with Google" covers the
   blank page with the browser-and-paste steps. Open claude.ai in Browser opens the default
   browser; Paste a Key Instead ends the window; Back to Sign-In Choices returns to the login.
4. Settings → Accounts → Add Account…: with a label, Sign In to Claude… adds the account when
   the window closes; with no label, it says to name the account, and Add Account finishes.
5. An account's page: a working account shows **Replace Sign-In…** (never "Sign In Again"), which replaces its key in place; an expired one shows **Sign In Again…** and "Session expired"; after either, the page says Signed in.
6. Close the window without signing in: nothing changes. Unplug the network: the error panel
   offers Try Again and Paste a Key Instead.
7. Nothing persists: after the window closes, sign in again opens a fresh login (not signed in).

## 30. Welcome tour (item 61)

Needs a fresh state: a fresh macOS user, or a dev build with the defaults domains cleared. Use a
dev build for the second: it keeps keys in `~/Library/Application Support/Sanduhr/credentials.json`
(§29), and a Mac that already has a key or accounts never gets the tour, so move that file aside
first. A release build copies `credentials.json` into the Keychain at its next launch: delete it
before the installed app starts again. Back up both defaults domains (`defaults export
com.626labs.sanduhr` and `com.626labs.sanduhr.desk`) and import them afterwards. Quit Sanduhr
before each `defaults` command.

1. Fresh start: `defaults delete com.626labs.sanduhr`, `defaults delete com.626labs.sanduhr.desk`,
   open the build. The welcome sheet shows; no tour, no What's New. `smoke/smoke state` shows
   `tour: { open: false, pending: true, done: false }`.
2. Sign in (Sign In to Claude… or Paste a Key Instead). About a second after the widget fetches,
   "Welcome to Sanduhr" opens with "1 of 5" under it and the card "Your limits, paced" showing your
   own session and weekly bars with the pace tick and the same percents as the widget.
3. Quit before signing in on a fresh start, open again: still no tour until the first fetch. Turn
   the network off and sign in: no tour while the fetch fails; it shows once a fetch succeeds.
4. Next (or Return) steps through: 2 "On your desktop" (a Desk corner with today's date and your
   meters; Show the Desk and Widget theme: Match Desk), 3 "At a glance" (the menu bar, with Menu bar
   shows; on a notched Mac a second card "Around the notch" with your numbers on the wings; on a Mac
   without a notch the menu bar card alone), 4 "More than one account" (the chip), 5 "Claude Code,
   connected" with Finish and "You can take the tour again from About or the menus." Back returns,
   and is off on step 1. Tab reaches every control.
5. Choices are real: Show the Desk turns the Desk on or off at once (Settings, General follows);
   Match Desk switches the widget to Match Desk, and off goes back to the theme you had; Menu bar
   shows changes the menu bar at once. Back to a step shows the choice as made.
6. Show me on each card leaves the tour open: step 1 shows the widget, step 2 opens Settings,
   Layout, the menu bar card Settings, General, the notch card Settings, Notch, step 4 Settings,
   Accounts, step 5 Settings, Integrations.
7. Skip the Tour (or Escape, or the close button) on step 3: the window closes, every setting stays
   as it is, and `defaults read com.626labs.sanduhr welcomeTourState` prints `skipped`;
   `whatsNewLastSeen` is this version. Quit and open: no tour, no What's New.
8. Again from a fresh start, Finish on step 5: `welcomeTourState` is `finished`.
9. Take the Tour… in Settings, About (beside What's New…), and in the menu bar item's, the widget's
   and the Desk clock's menus (under What's New…): the tour opens at step 1 with the current
   settings. Finishing or skipping it changes neither `welcomeTourState` nor `whatsNewLastSeen`.
10. Never on update: with your own defaults back (or `defaults write com.626labs.sanduhr
    whatsNewLastSeen 2.3.4` on a Mac that has a key), open the build: What's New shows, no tour,
    and `welcomeTourState` is `notOffered`.
11. VoiceOver reads each preview ("Your limits: Session 12%, weekly 40%" and so on). With Reduce
    Motion on (System Settings, Accessibility, Display), nothing in the previews moves.
12. `smoke/smoke run scenarios/welcome-tour.yaml` passes (opens the tour, steps 1 to 5 with their
    step text and titles, closes it; records nothing).

## 31. Watchers (item 66)

Use the test folder from §22 (`~/.claude-smoketest`, never your real `~/.claude*`) and a dev build
(aim links at it as §22 says). Turn the island on (Settings, Notch) and set the right wing to
Watchers; in Settings, Desk, Layout put Watchers in the top right corner.

1. Both switches off (Settings, Integrations, Watchers): `cat ~/Library/Application\
   Support/Sanduhr/watchers.json` prints `{"agents":false,"background":false,"schema_version":1}`.
   The right wing shows its default (the meters) and no Desk row draws.
2. Agents, switch off: in `CLAUDE_CONFIG_DIR=~/.claude-smoketest claude` with the MCP server
   installed there, ask Claude to call `watch_start` with title "CI on main": it comes back
   `rejected`, `watchers_off`, and nothing named `watch-request-*` appears in Sanduhr's folder.
3. Turn **Let agents show watchers** on (`watchers.json` follows at once) and ask again with a
   link to a real https page, total 12 and short "CI": within a second the right wing shows a blue
   dot and the full line "CI on main · 0s · 0/12" (scrolling once if it doesn't fit), then
   shrinks to "CI · 0/12"; the Desk corner shows the full row; no `watch-request-*` file is left.
   A title like "PR 140 CI: combine statuslines" without `short` rests on "PR 140 · 0/12". Each
   state change (waiting, passed, failed) plays the full line again; with Reduce Motion it never
   does and the wing stays short.
4. Ask Claude to `watch_update` it with done 3 and note "lint passed", then state waiting: the
   dot turns amber and pulses, the note shows under the Desk row, and the notch glows once. With
   Reduce Motion on the dot holds still. Click the wing or the row: the link opens in the browser.
5. `watch_end` with result passed: a check, then it fades about 6 seconds later. Start another
   and end it failed: it stays, marked with a red exclamation mark in a triangle (never a red
   dot, which means the camera; §32), and VoiceOver on the wing or the row says "failed";
   two-finger click it (wing or row) and Dismiss. Start two and
   Dismiss All. Watcher Settings… opens Settings, Integrations.
6. Lost touch: start one and wait 10 minutes without an update: it greys and reads "lost touch";
   an update brings it back.
7. Background work: Settings, Integrations shows the notch glow hooks as **Outdated** for a folder
   installed before this version; Install updates it (`jq -r '.hooks.Stop[0].hooks[0].command'
   ~/.claude-smoketest/settings.json` names `watchers.json`, `osascript` and
   `notifyutil -p com.626labs.sanduhr.claude-code.done`, and no `sanduhr://`). Turn **Show Claude
   Code's background work** on. In the session ask for `sleep 120` in the background (or a
   background subagent): when the turn ends a watcher with the task's description and "background
   shell" shows; nothing named `watch-stop-*` stays in Sanduhr's folder, and
   `log show --last 5m --predicate 'process == "Sanduhr"' | grep -i sleep` finds nothing. When the
   task finishes and Claude's next turn ends, it ends as finished and fades.
8. Switch background work off: its watchers go, and the next Stop writes no file at all (watch
   the folder with `ls` during a turn). The item 51 glow still works with either switch. Switch
   it back on and quit Sanduhr; finish a Claude Code turn: Sanduhr does not start and no
   `watch-stop-*` file is written. Open Sanduhr: no old background watcher appears.
9. Work tagging: in Settings, Accounts, Data, link `~/.claude-smoketest` to an account and turn
   **Work account** on. Start a watcher from that folder: `smoke/smoke do demo on` hides it (the
   wing shows its default), `demo off` brings it back. One flagged `work: true` from any folder
   hides too.
10. `smoke/smoke do watch-test start` (then `wait`, `pass`, `fail`, `clear`) drives a made-up
    watcher whatever the switches say; `state.yaml` shows `watchers: {count, states, placements,
    agents, background}` with no title. `smoke/smoke run scenarios/watchers.yaml` passes and
    leaves no watcher behind.
11. Quit Sanduhr: watchers are gone at the next launch. Clean up: switches off, Remove the hooks.

## 32. Camera and mic indicators (item 67)

Desk on, the island on (Settings, Notch), no call running. No macOS permission prompt may appear
at any step.

1. **Show the red dot** Never and the mic switch off (Settings, Desk, Notch, Camera and mic): open
   Photo Booth and record a Voice Memo; nothing shows on the notch. `smoke/smoke state` shows
   `av_indicators: {camera: false, mic: false, shown: none}` (the monitors don't run while off).
2. **For cameras without a visible light**, on a MacBook with the lid open: Photo Booth on the
   built-in camera shows no dot (the green light is right there). Pick an iPhone (Continuity) or a
   USB camera in Photo Booth's Camera menu: within a second the dot shows. Back to the built-in
   camera: it goes. With the lid closed on an external display, the built-in camera (if the
   external setup keeps it) or any other one shows the dot. An install that had the old switch on
   opens with this choice picked.
3. **Always**: Photo Booth on the built-in camera shows a red dot on the island right of the camera
   within a second, fading in over about a quarter second, the island a little wider on that side,
   both wings unchanged. The dot breathes smoothly (dimming and brightening over about 1.6 s, never
   blinking). Turn on Reduce Motion: it holds still, and it comes and goes without a fade. Turn off
   **Pulse the dot gently**: still too. Quit Photo Booth: the dot fades out within a second and the
   island and the right wing's text go back exactly where they were before the camera came on;
   nothing stays drawn over the start of the wing (watch for a minute).
4. Turn on **Show a mic while the microphone is on**. Start a Voice Memo recording (or a FaceTime
   or browser call): an orange mic glyph shows beside the dot (or alone). Stop: it goes within a
   second. Sanduhr's log (`log show --last 5m --predicate 'process == "Sanduhr"'`) names no app.
5. Click the dot: a menu with "Camera in use" and "Microphone in use" (greyed) and Indicator
   Settings…, which opens Settings, Notch. Two-finger click: the same. Nothing mutes, the call
   keeps its camera and microphone.
6. **Beside the camera**: Left of the camera moves them to the left of the cutout.
7. Set the right wing to **Camera and mic**: they move into the right wing (at its inner end); with
   nothing in use the wing shows the meters. Set the strip under the camera to Camera and mic (with
   text under the camera on): they show there, centered; a click opens the menu.
8. Headset: with a call running on the built-in mic, connect a Bluetooth headset and make it the
   input: the glyph stays (it follows the default input); end the call: it goes.
9. Turn the island off (Extend the camera notch off): during a call the indicators show in a small
   black tab against the notch on the chosen side. On a Mac without a notch (or an external main
   display with the lid closed) they show in a small tab at the top center.
10. With a watcher running, now playing in the left wing and a call going: all three show; the
   indicators take only their own room.
11. `smoke/smoke do av-test camera on` with the dot on any setting but Never shows the dot with
    no camera (a faked camera counts as one without a visible light); `smoke/smoke do av-test
    camera off` takes it away (likewise `mic`). `smoke/smoke run scenarios/av-indicators.yaml`
    passes and puts the settings back.
12. Clean up: the dot back to Never, the mic switch off.

## 33. A live preview in every settings pane (item 68)

Desk on. Go pane by pane; each card sits at the top, about 160 points tall, and takes no clicks
(only its button does).

1. **Fresh install** (or a signed-out test account, no calendar access, nothing playing, no
   watchers): every card shows something, never blank, with a "Sample: …" label naming what is
   made up (meters, meetings, message, track, watcher, camera and mic). After the first fetch the
   meters lose the label.
2. **Notch**: the island at true proportions, both wings, the strip when there is extra height
   and text under the camera, the camera and mic indicators as their switches say. Change the
   width, the height, each wing's content and the side of the indicators: the card follows at
   once. **Test Glow** on the card (and the one in Glow) glows the card and the real notch
   together. Turn the island off: the card shows the plain notch. On a Mac without a notch the
   card shows the top tab.
3. **Layout**: the screen map shows the menu bar, the Dock on its edge and each piece in its
   corner in stacking order; moving a piece or a margin moves it on the map at once; Hidden takes
   it off.
4. **Look**: the message, clock and date, and meters in the chosen fonts, sizes, ink, shadow and
   glow; each control changes the card at once.
5. **Meters**: a warning row red with its glow; change a threshold so it no longer warns: the row
   turns back at once. Hide a temporary limit: it leaves the bars and "Hidden: …" names it.
6. **Message**: today's line with its effects; with a `{write}`, `{shimmer}` or `{sweep}` line,
   **Replay** writes it in again or sweeps it at once. Pin a line: the card shows it.
7. **Now Playing**: a wing playing, paused (with Next; with Hide while paused on, the stand-in)
   and with nothing playing (the When nothing is playing choice), and the Desk line.
8. **Widget Look** and **Pacing & Focus**: the widget's cards in the theme and font; Subtle mode
   drops the glass; Pin the pacing calculators shows them on every card.
9. **General**: the menu bar item as it will read; Rotate shows both readings ("S 42%" then
   "W 91%").
10. **Integrations**: Sanduhr's statusline in a terminal frame (sample input) and a watcher card.
    Without Python: "No preview: Python or the scripts weren't found."
11. Reduce Motion on: the message's write-in and shimmer, the camera dot's pulse and the glow
    hold still. VoiceOver reads each card as one sentence.
12. `smoke/smoke state` shows `settings_preview` (`notch`, `layout`, `look`, `meters`, `message`,
    `nowPlaying`, `widget`, `menuBar`, `integrations`, `mods`, or null). `smoke/smoke run
    scenarios/settings-sections.yaml` snaps each pane with its card.

## 34. Desk layout: more places, your order, a size per piece (item 59)

Desk on, Settings, Desk, Layout open. Note the `layout` string first (`defaults read
com.626labs.sanduhr.desk layout`) to put it back at the end.

1. **An existing layout looks identical.** Before updating, screenshot the Desk; after, compare:
   every piece sits where it sat, in the same order, at the same size. The `layout` string is
   unchanged until you change something.
2. **Eight places.** Each piece's place menu lists Top left, Top center, Top right, Middle left,
   Middle right, Bottom left, Bottom center, Bottom right and Hidden. Put the clock at each in turn:
   it moves live, and the map card shows it there. Middle left and right sit halfway down their
   side; Bottom center rests on the bottom margin.
3. **Top center and the notch.** On a notched Mac, put the message at Top center and set Top (below
   the menu bar) to 0: the message sits just below the notch. Turn the notch island on with some
   extra height: it moves below the island's strip. On a plain screen (an external display) it sits
   on the top margin like the corners. The map draws the notch (and the island) at the top.
4. **Your order.** With the clock, the meters and the meetings at Bottom left, the Order list shows
   Bottom left with the three. Drag Meetings onto Clock: the meetings move above the clock on the
   Desk and the map. Drag Clock onto Meters, below it: it lands below the meters. Drag a piece onto a piece in
   another place: it moves there, just above that piece. With VoiceOver, Move Up and Move Down on a
   row do the same. Picking a new place for one piece leaves the others' order alone.
5. **A size per piece.** Set the clock's size to 160%: the clock (time and date) grows, nothing
   else does, and the pieces stacked with it move to make room. 60% shrinks it. Set the
   message's size to 140%: it grows from its own size in Look. The map's outlines grow and shrink
   with them. Hide a piece: its size menu greys out.
6. **The Dock.** With the Dock always shown at the bottom, pieces at Bottom center sit clear of it,
   like the corners. Move the Dock to the left: Middle left moves in with Top left and Bottom left.
   An auto-hiding Dock lifts Bottom center with the corners.
7. **Clicks.** Put the meters at Middle right and the meetings at Top center: a two-finger click on
   the meters opens their menu, a meeting row with a link opens it, and clicks elsewhere still reach
   the Finder.
   **Side beside center.** Pin a long message (`defaults write com.626labs.sanduhr.desk message
   "a long line that runs well past the middle of the screen"`), put it and the meetings at Top
   left and the meters at Top center: the message wraps and shrinks to end short of the meters,
   and nothing at Top left draws under them. Put the clock and the meetings at Bottom left with a
   piece at Middle left: the middle moves up as the bottom stack grows, never drawn over. Delete
   the pinned message afterwards.
8. **A hand-edited string.** `defaults write com.626labs.sanduhr.desk layout "message:zz clock:bl:1.4
   meters:bl"`: the message shows top left (an unknown place falls back to its default, never
   blank), the clock at 140%.
9. `smoke/smoke state` shows `desk_pieces` (piece, anchor, order, scale) matching the Desk.
   `smoke/smoke run scenarios/desk-layout.yaml` reorders Bottom left, then moves pieces to Top
   center, Middle right (at 120%) and Bottom center, with `desk_frames_ok: true` each time.
10. Put the `layout` string back.
## 35. The Mods page (item 64, slice 1)

Read-only. Use a throwaway Claude Code folder for the setup (`mkdir -p ~/.claude-modtest/projects`),
never your real settings, and remove it afterwards. Copy `mac/integrations/mods/sanduhr-meters`
to `~/modtest/meters` and give `~/.claude-modtest/settings.json`
`{"env": {"CLAUDE_CODE_PLUGIN_DIRS": "~/modtest/meters:~/modtest/gone"}}`.

1. Settings, Mods (under Integrations, cube symbol) opens with a summary card: mods, plugins, on,
   folders, and missing when one is; "Sanduhr's own mod switches here; other mods are read-only." No Sample
   label. VoiceOver reads it as one sentence with the counts.
2. One box per Claude Code folder, its path on top. `~/.claude-modtest` lists **sanduhr-meters**
   (0.2.0, Mod, On, its description, "Plugin folder list (CLAUDE_CODE_PLUGIN_DIRS)" and the path),
   "Draws: band above the prompt, toasts", "Its code reads or writes files." in orange, and a
   terminal frame titled Claude Code with a cyan band right above the `>` prompt and a toast line,
   captioned "Sketch: drawn by the mod in Claude Code" (blocks only, no numbers). **gone** reads
   Missing with "(not found)", and its Check is off: "Nothing to check: its folder isn't here."
3. Add `"enabledPlugins": {"sanduhr-meters@inline": false}` to that settings.json and press Read
   Again: the row reads Off and the summary's on count drops by one.
4. A folder with installed plugins (your real one is fine, it is only read) lists each with
   "Installed from <marketplace>", Plugin or Mod, and On or Off as its `enabledPlugins` says; a
   plugin with `commands/*.md` shows "slash commands (/name)".
5. **Check** on sanduhr-meters: within a few seconds an amber card, "Medium risk · Claude Code
   would load it", "Reads files: $.fs.read", "Reads environment variables: APPDATA, HOME, OS,
   SANDUHR_BAND, SANDUHR_SNAPSHOT", the hooks and calls, and "From claude plugin validate, which read the files
   without running them." `ps` never shows a `claude plugin test`.
6. Break the copy's manifest (`"version": 3`) and Check again: a red card, "Claude Code would
   refuse it (1 error)", "Error: version: Invalid input…", with no absolute path in it.
7. Without the CLI (move it aside for a minute, or on a Mac without Claude Code): the page says
   "Check needs Claude Code's command line (claude)…", every Check is off and its tooltip says why.
8. A `~/.claude-modtest.config-backup-20261001-000000` folder holding a `projects` folder gets no
   box.
9. Nothing changed: `shasum` of every settings.json listed is the same before and after.
10. `smoke/smoke state` shows `mods_page` (`open`, `loaded`, `folders`, `mods`, `plugins`,
    `enabled`, `missing`, `checked`, `cli`): flags and counts only, never a name or a path.

## 36. A Message editor anyone can use (item 69)

Back up your list first and put it back at the end:
`cp ~/Library/Application\ Support/Desk/messages.txt ~/messages.txt.bak`. Then replace it with a
hand-written test file holding a note, a blank line, `  Mon:   one thing.`,
`Fri:{ink:#FF2A6D,#05d9e8}  {glow}   showtime.`, `{blink} unknown.`, `{write} {shimmer} two motions.`
and `keep building.` (CRLF endings in one copy of the test, no final newline in another), and note
its `shasum`.

1. Settings, Message: Claude's card (when a suggestion waits) on top, then Once a day / Every hour,
   **Edit as text…**, Save and Revert (both off), "Today: …", and one row per message line, each
   with its day ("Every day", "Mondays", "Fridays") over the line drawn as the Desk draws it, in
   the Desk's font, color, glow and effects. The note and blank line are not rows; "Notes (#) and
   blank lines in the file stay where they are." The `{blink}` and two-motion lines show as written
   in monospace with "Kept as written…", and their previews draw them as the Desk does.
2. Save stays off. Switch to Edit as text… and back: still nothing unsaved, and `shasum` is
   unchanged, CRLF and the missing final newline included.
3. **Add Line**: a new row opens its controls. Set When to Friday, Text "ship it.", Color Gradient,
   Palette sunset, Letters Script, Motion Sweep: the row's picture changes with each, and no brace
   shows anywhere. Save: the file's last line is
   `Fri: {ink:#ff7e5f,#feb47b,#ffd86f} {font:script} {sweep} ship it.` and every other line is
   unchanged (`diff` against a copy of the test file shows one added line).
4. Each control's tooltip is one line. Size slides 0.5× to 2× in 0.05 steps; Glow On, Off, As the
   Desk write `{glow}`, `{noglow}`, nothing; One color shows one well, Gradient two with Add Color up
   to four and a minus on each beyond two; When, A date shows Month and Day menus and writes `MM-DD:`.
5. Change only the Fridays row's glow to Off and Save: only that line changes in the file, rewritten
   as `Fri: {ink:#ff2a6d,#05d9e8} {noglow} showtime.`; the odd spacing of the other lines stays.
6. A row's menu: Duplicate puts a copy right below; Move Up and Move Down step past other lines
   (notes keep their place); Delete removes only that line. Drag a row by its handle onto another:
   it lands there. Each reaches the file only on Save; Revert drops them.
7. Pin on a row: "Pinned: the Desk shows … every day; special days still show above it." above the list, the Desk shows that line,
   the pin is filled; Unpin (or Pin again) gives the list back.
8. Type `#1 fan` as an Every day line's text: the row warns that the Desk reads it as a note; add a
   glow and the warning goes. `{x} hi`, and `Mon: hi` on an Every day line, warn too.
9. **Edit as text…**: the file with the tag reference beside it (every tag, the font names) and the
   pin field. Type a line there, switch back to the list: it is a row; edit a row, switch to text:
   the change is there. Unsaved edits survive the switches; Save from either view writes them.
10. With unsaved edits, have Claude suggest lines (`propose_desk_messages`): the card's Add is off;
    Save, then Add: the list reloads with Claude's lines. With "Let Claude change the messages
    directly" on and unsaved edits, "The file changed" and Reload show instead.
11. **Ask Claude**: the example prompt and Copy ("Copied"); the pasteboard holds the prompt.
12. Keyboard only (Full Keyboard Access on): Tab reaches every control, Pin, the row menu, Edit,
    Add Line and Save. VoiceOver reads each control by name (When, Text, Color, Color 1 of 3, Glow,
    Size with "1.2 times", Letters, Motion), each preview as "Preview: <text>", and offers Move Up
    and Move Down on a row. With Reduce Motion on, the row previews hold still.
13. `smoke/smoke run scenarios/message-editor.yaml` passes: it adds the smoke's own line unsaved and
    reverts it; `messages.txt` is untouched (`shasum`). `smoke/smoke state` shows `message_editor`
    (`open`, `mode`, `rows`, `styled`, `raw`, `notes`, `unsaved`, `today_special`, `added`): counts and flags, never
    one of your lines.
14. **Special days add.** Add two lines for today's date (`MM-DD: happy birthday, Sam.` and
    `MM-DD: happy birthday, Alex.`) and Save: the Desk draws both, a little smaller, stacked above
    the day's usual line, each with its own effects; the Message preview shows the same stack;
    "Today:" reads "Today: happy birthday, Sam. / happy birthday, Alex. / <usual line>". Their rows
    read "<Month day> · shows above the day's message · with 1 other that day". The notch's
    Message shows "happy birthday, Sam.". `smoke/smoke state` shows `message_editor.today_special: 2`.
    Add two more for today: three show, and the set changes on the hour. Pin a "Good vibes only"
    line: the date's lines still stack above it ("Today: … / Good vibes only"); Unpin.
15. **Weekday lines.** Rows read "Fridays · takes turns with N others" (or "· shows instead of the
    every-day lines" for one) and the every-day rows "Every day · … · steps aside on days with their
    own line". Switch on "Mix every-day lines in on days with their own line": the notes change to
    "· mixes with every-day lines" and "· mixes in on days with their own line", and
    `get_desk_messages` reports `mix_daily: true`. Switch it off again.
16. **Claude.** `get_desk_messages` returns `today` (the usual line) and `today_special` (the
    date lines). Ask Claude Code to add birthdays for the year: it proposes `MM-DD:` lines with
    mode add, one per person.
17. **On special days.** With the two birthday lines for today in place: the bar's "On special
    days" reads Stack and no timing menu shows. Pick **Take turns**: "Each line shows for" appears
    (5 s, 10 s, 30 s, 1 min, 5 min; 10 s chosen). Pick 5 s: the Desk shows one line at a time,
    Sam, then Alex, then the usual line, crossfading (about 0.4 s), the piece never changing size
    and nothing beside it moving; the notch's Message shows the same line at the same moment; the
    Message preview card does the same. A `{write}` line writes itself in each time it comes round.
    Pick **Scroll**: each line glides up out of view as the next glides up in, one visible at a
    time. Turn on Reduce Motion: both swap at once with no fade or glide. Cover the Desk with a
    window for 20 s and uncover it: the line didn't advance while covered and then jumps to the
    current one. Remove today's date lines: nothing cycles, the usual line stays. Without a date
    line today the preview card shows a sample birthday and "Sample: … special day".
    `get_desk_messages` reports `special_mode` and `special_seconds`. Set Stack again.

Put your list back: `cp ~/messages.txt.bak ~/Library/Application\ Support/Desk/messages.txt`.


## 37. Switch Sanduhr's own mod on the Mods page (item 64, slice 2)

Use throwaway Claude Code folders only (`mkdir -p ~/.claude-modtest/projects ~/.claude-modtest2/projects`)
and remove them afterwards. Give `~/.claude-modtest/settings.json` `{"model": "opus"}` and note its
`shasum`. Settings, Integrations: install **Meters above the prompt** into `~/.claude-modtest`.

1. Settings, Mods: a box at the top, "Sanduhr's mod: sanduhr-meters" and the app's version, one
   row per Claude Code folder with a switch. `~/.claude-modtest` is on: "Version …, through
   Sanduhr's current folder: it follows Sanduhr's updates (installed from Integrations)." In the
   folder list below, its sanduhr-meters row says "Sanduhr's own mod: switch it under Sanduhr's
   mod at the top of this page." Other mods carry no switch.
2. Switch it off: the row reads "… Off: enabledPlugins sets sanduhr-meters@inline to false here."
   and "Takes effect in new Claude Code sessions (or after /reload-plugins)." settings.json now has
   `"enabledPlugins": {"sanduhr-meters@inline": false}` and the plugin list entry is still there;
   the inventory row below reads Off.
3. Switch it on: settings.json is byte for byte what Integrations wrote (`shasum` as after the
   install).
4. A project override: add `"/tmp/modtest-project": {}` under `projects` in
   `~/.claude-modtest/.claude.json` (create it as `{"projects": {...}}` if missing) and
   `{"enabledPlugins": {"sanduhr-meters@inline": true}}` in
   `/tmp/modtest-project/.claude/settings.json`. Switch it off: the orange line reads "A project
   setting keeps it on in /tmp/modtest-project (.claude/settings.json)."
5. `~/.claude-modtest2` (no settings.json yet) reads "Not in this folder's plugin folder list. On
   adds it." Switch it on: a question, "Turn on sanduhr-meters in ~/.claude-modtest2?", with Turn
   On and Cancel. Cancel leaves the switch off and writes nothing. Turn On: its settings.json lists
   `…/Sanduhr/integrations/<12 hex>/mods/sanduhr-meters` and the row reads "Version …, kept by the
   Mods page: Update moves it to a new version."
6. Update (needs two builds): with a build whose mod differs installed over this one, the
   `~/.claude-modtest2` row shows "Update to <version>". If the new mod calls something the old
   didn't, Update asks "The new version of sanduhr-meters can do more" listing the new calls;
   "Keep the Version in Use" leaves settings.json alone; Update moves the entry to the new stamp.
7. Remove on both rows: each settings.json is back to its first `shasum` (`~/.claude-modtest2`'s
   is gone again), and Integrations shows the meters as not installed.
8. Integrations, Statusline, Install over an existing statusline in a folder that loads a mod
   with a status entry: under "From your mods", "Switch it on the Mods page" closes the sheet and
   opens Settings, Mods.
## 38. A look per song and themes with a title style (item 65, parts c and d)

Back up your saved looks if you have any:
`cp ~/Library/Application\ Support/Sanduhr/now-playing-looks.json ~/looks.bak` (skip when it is
missing). Place now playing on a notch wing, the strip and the Desk (Layout), and play a song in
Music.

1. Settings, Desk, Now Playing has a **Looks** section: **Style what's playing** (off), its
   caption, **Let Claude style songs directly** (off), its caption naming
   `propose_now_playing_looks`, "No saved looks" and **Clear Looks…** (off). The page's top caption
   says titles are never saved, "only the songs of looks you save from Claude, below".
2. Switch on Style what's playing: the wing, the strip and the Desk line draw the song in a
   gradient from one of the eight palettes and a letter style (small caps, italic, bold or bold
   italic) in the Desk font; the Desk line's position bar takes the gradient. The ▶ glyph stays
   plain. Skip to the next song: another look. Go back: the first song's look again. Quit and
   relaunch Sanduhr: the same song wears the same look.
3. A long title in a styled look still fits or scrolls once in its wing, with nothing clipped at
   rest and the Next button where it was while paused.
4. Ask Claude Code for looks for the song playing and two others (`propose_now_playing_looks`):
   it answers `pending_approval` with `style_on: true`. The page shows "Claude suggested looks for 3
   songs", each song drawn in its look on black with its mood, Dismiss and Save. Save: the playing
   song changes to its look at once, the row reads "3 saved looks", and
   `now-playing-looks.json` is mode 600 and holds the three songs. A second app playing the same
   song (a browser) wears the same look.
5. Have Claude suggest again and Dismiss: Claude hears `rejected` ("dismissed by the user"),
   nothing changes. Ask for a look with a dark color (`#202020`): refused at once, naming "too dark
   for the black notch", and no request file appears in `~/Library/Application Support/Sanduhr`.
6. Switch on Let Claude style songs directly and ask again: `applied` with `looks_saved`, no card.
7. Switch off Style what's playing and have Claude suggest: the result carries `style_on: false`;
   the notch and Desk draw in their own ink as before.
8. **Clear Looks…** asks "Clear every saved look?"; Clear Looks deletes the file, the row reads
   "No saved looks" and the playing song goes back to its seeded look (with the switch on).
9. VoiceOver on the card reads each song as "Title by Artist, small caps, mood …"; the notch and
   Desk still read the plain title.
10. **Theme title.** Save a theme with `"title_ink": ["#ff9ac1", "#9ff3ff"]` and
    `"title_style": "small-caps"` (Settings, Widget, Themes, or have Claude call `propose_theme`
    with them) and apply it: the widget's "Sanduhr" title draws in that gradient in small caps.
    `"title_style": "fraktur"` draws it in Unicode fraktur letters. A built-in theme's title is as
    before. A proposal with `"title_ink": ["#ff9ac1"]` is refused naming `title_ink`; one with a
    dark stop (`#303030`) goes through with a warning "title_ink stop 2 reads at … on the card".

Put your looks back: `cp ~/looks.bak ~/Library/Application\ Support/Sanduhr/now-playing-looks.json`
(or leave it cleared).
## 39. Desk layout: arrange on the desktop (item 60)

Desk on with the clock, the meters and the meetings at Bottom left and the message at Top left.
Note the `layout` string first (`defaults read com.626labs.sanduhr.desk layout`) to put it back at
the end.

1. **Ways in.** The menu bar item's menu and the widget's two-finger menu have **Arrange Desk…**
   under Settings…, and so does the Desk's clock menu in the menu bar. A two-finger click on the
   meters: the shared part under the limit's own items has it. A two-finger click on a meeting row,
   the calendar note or the account name opens the shared menu, with it. Now playing's, a watcher's
   and the camera and mic indicators' menus end with it. Settings, Desk, Layout has an **On the
   desktop** section with "Move, reorder and resize the pieces on the desktop itself. Return or Done
   keeps the new layout; Escape or Cancel puts it back as it was." and an **Arrange Desk…** button.
   With Desk off the button is off and the line reads "Turn on Desk to arrange it on the desktop.";
   every menu's Arrange Desk… is greyed with the same line under it (and as its tooltip).
2. **Arrange mode, from Settings.** Open Settings, Desk, Layout and press **Arrange Desk…**: the
   Settings window goes away. Every piece shows a dashed outline with its name above it and a round
   handle on the corner facing the middle of the screen (the inner bottom corner at the top and the
   middles, the inner top corner at the bottom).
3. **The bar floats.** A bar in the middle of the screen reads "Arrange Desk", "Drag a piece to any
   of the eight places, or up and down its stack. Drag its round handle to resize it.", "Return or
   Done keeps the new layout. Escape or Cancel puts it back.", Cancel and Done. Open another app's
   window (Safari, Finder) over the middle of the screen: the bar stays on top of it. Switch Spaces:
   the bar follows. With a second display, the bar sits on the middle of the screen the Desk is on.
4. **Clicks over the whole screen.** While arranging, a click anywhere on the desktop goes to the
   Desk, not the Finder: no icon selects, no Finder desktop menu opens, and a click on a meeting row
   does not join it. A two-finger click on the meters opens no menu.
5. **Move.** Drag the clock: it fades where it was, an outline follows the pointer, and the eight
   places light up as dots, the one it would land on bigger and in the accent color. Drop it near the top
   right: it lands at Top right with a short snap. Drop a piece near the top middle: it sits below
   the notch (and the island, when it shows), as Top center always does.
6. **Reorder.** Drag the meetings up within Bottom left and drop them above the clock: they land at
   the top of that stack, and Bottom left's dot stays lit the whole way up the stack (Middle left
   does not take it). Drag the clock down below the meters: it lands under them.
7. **Resize.** Drag the clock's handle away from the clock: it grows in steps of 10%, up to 160%;
   back toward it, it shrinks down to 60%. The pieces stacked with it move to make room.
8. **The Dock.** With the Dock always shown at the bottom, the bottom places' dots sit above it and
   a piece dropped at Bottom center clears it. Auto-hide on: the Dock still lifts the bottom pieces
   while arranging.
9. **Nothing saved until Done.** While arranging, `defaults read com.626labs.sanduhr.desk layout`
   still shows the old string. Press **Cancel**: the bar goes, every piece goes back exactly where it
   was, at its size, the string is unchanged, and Settings comes back in front, its map showing the
   old layout.
10. **Escape cancels.** Arrange again, move a piece, press **Escape** without clicking anything:
    the same as Cancel, nothing kept. Arrange again, move a piece, click another piece (the Desk takes
    the click), then press Escape: still Cancel.
11. **Return and Done keep.** Arrange again, move and resize a piece, press **Return**: the Desk
    keeps it and the string now holds it (the same `piece:anchor:size` words as Layout writes).
    Arrange again from the menu bar item's menu with Sanduhr in the background, move a piece and
    press **Done**: kept the same way. Keypad Enter does what Return does.
12. **Clicks back to normal.** After Done, Return, Escape or Cancel, the outlines and the bar go,
    clicks on the desktop reach the Finder again everywhere except on the meters, the meeting rows,
    the account name, now playing and the watchers, which work as before. Started from a menu with
    Settings closed, Settings stays closed.
13. **Reduce Motion.** With Reduce Motion on, a drop moves the piece without the snap.
14. **VoiceOver.** While arranging, each piece offers Move Up, Move Down, Bigger and Smaller, and the
    bar reads as "Arrange Desk" with Cancel and Done.
15. `smoke/smoke run scenarios/desk-arrange.yaml` passes: it opens Settings, Desk, Layout first;
    while arranging `desk_arrange.click_through` is `whole`, `desk_arrange.bar_visible` is true and
    `settings_open` is false; after, `drawn`, false and true again. The smoke's edit (the clock to
    Top right at 120%) is only in `desk_arrange.working` until Done, Cancel leaves `layout` as it
    was, and Done writes it.
16. Put the `layout` string back.
## 40. The animated band and watchers above the prompt (items 65f, 66)

Use a throwaway Claude Code folder (`mkdir -p ~/.claude-bandtest/projects`), never your real
settings, and remove it afterwards. Install the meters mod and the statusline for it in Settings,
Integrations. For the Combine steps give it a statusline of its own first
(`{"statusLine": {"type": "command", "command": "echo mine"}}` in its `settings.json`).

1. Nothing set: `ls ~/Library/Application\ Support/Sanduhr/band.json` finds no file. Settings,
   Integrations, Watchers shows **Show watchers above the prompt**, off, under the Notch… and Desk
   Layout… row, with its caption about band.json.
2. Install the statusline: the sheet offers Combine. Open a Sanduhr chip's **Style…**: the caption
   ends "the meters mod's band above the prompt draws Sanduhr's segments in this look and moves it
   (sweep, shimmer, glow)". Give Session a two-stop gradient, Script letters and Bold.
3. Under Join with, tick **Show Sanduhr's meters above the prompt instead (animated)**: its caption
   says your segments stay and Sanduhr's move to the band, and the preview shows only `mine`.
   Combine. `jq -r .statusLine.command ~/.claude-bandtest/settings.json` ends in `--band`;
   `band.json` now exists, `stat -f %Lp` prints 600, and it holds `meters.styles.session` with the
   ink, `"font":"script"` and `"bold":true`, and no `watchers` key.
4. `CLAUDE_CONFIG_DIR=~/.claude-bandtest claude`: the statusline reads `mine` only; above the
   prompt the band reads `𝒮ℯ𝓈𝓈𝒾ℴ𝓃` in the gradient, bold, then the bar, percent and reset. Nothing
   moves while the numbers sit still.
5. Sweep: when a limit crosses 50, 75 or 90% (keep working, or use an account near a line), within
   30 seconds of the widget's next fetch a white light runs across that bar and its percent once,
   about a second, then the band rests.
6. Glow and shimmer: a limit at 90% or more with a day left (weekly) shows ⚠ and its percent glows
   every 4 seconds; a session within ten minutes of its reset shimmers its reset words every 4
   seconds. Turn on Reduce Motion (System Settings, Accessibility, Display): `band.json` says
   `"reduce_motion":true` within a second and the band stops moving; off again, it moves. The
   mod's Motion option set to off (Claude Code's `/config`) keeps it still too.
7. Turn **Show watchers above the prompt** on and **Let agents show watchers** on, then
   `smoke/smoke do watch-test start`: within 2 seconds a row under the meters reads `● Smoke
   watcher 0s 0/10`, the time counting each second. `watch-test wait`: `◉`, amber, pulsing every 4
   seconds. `watch-test pass`: a green ✓ that fades toward grey, gone within 6 seconds.
   `watch-test fail` (after a new start): a red ⚠ and a red title that stays.
8. `band.json` holds the agent watcher's title, short, state, done, total and times only; with
   **Show Claude Code's background work** on and a background shell running in a session, its row
   reads `● background shell` and its entry is `{"kind":"shell","source":"automatic","state":
   "running"}`: `grep -c` for the shell's description in `band.json` is 0.
9. A narrow terminal (under 60 columns) shows the short title (`smoke`); a short band folds extra
   watchers into "+N more".
10. Quit Sanduhr: `band.json` keeps the looks and `"watchers":[]`; the rows go within 2 seconds.
    Turn the watchers switch off and Replace the statusline: `band.json` is deleted.
11. Junk: write `{` into `band.json` while Sanduhr is quit: the band draws the meters as before,
    no rows, nothing logged.
12. Clean up: Remove both integrations, delete `~/.claude-bandtest`.

## 41. The clock, the message and the claude line take clicks

Standard layout (message top left; clock, claude line and meters bottom left), Desk on, a message
showing today. Put a desktop icon (a scratch file) right under the clock first, and another on the
desktop well away from every piece.

1. Settings, Desk, Look ends with **Clicks**: **Clock and message take clicks**, on, captioned "On:
   two-finger click them for Sanduhr's menu. Desktop icons right beneath them can't be clicked
   there while it's on." `smoke/smoke state` shows `desk_piece_clicks: true` and `desk_frames`
   lists `clock`, `message` and `claude_line` with non-empty frames; `desk_frames_ok: true`.
2. Close Settings. Two-finger click (or Control-click) the time, the date, the gap between the
   hour and the minutes, and the space between two words of the message: each time Sanduhr's menu
   opens (Arrange Desk…, Settings…, Quit…), never the Finder's (no New Folder, no Change Desktop
   Background). The clock's menu starts with **Desk Settings…**, the message's with **Message…**;
   each opens Settings at that page. Two-finger click the claude line: the shared menu, no extra
   item. With two accounts, a plain click on the account name still switches accounts.
3. Plain click the clock: Settings opens at Desk, Look. Plain click the message: Settings, Desk,
   Message. Plain click the claude line (not the account name): nothing happens, no Finder
   selection box starts.
4. The same with a special day's stack (`10-31: test` in messages.txt with the Mac's date on
   10-31, or a date line for today), and with On special days set to Take turns and to Scroll: a
   two-finger click anywhere on the stack or the line taking its turn opens the menu.
5. Desktop icons: the icon under the clock can't be clicked through it (the caption says so); the
   icon away from the pieces clicks, double-clicks and drags as always. Move the pointer slowly
   from the icon onto the clock and two-finger click without stopping: Sanduhr's menu.
6. File drags: drag the far icon slowly across the clock, then the message, and drop it on an
   empty part of the desktop beyond them: it lands there. Drag it onto a Finder window's sidebar
   folder that sits over the clock: it files. Drop it right on top of the clock: Finder places it
   there (under the clock), it never springs back. Record what you see in the PR; the drag
   behaviour has not been checked against a live Finder drag before this step. Then drag from the
   clock itself: nothing moves (the press is Desk's), and the next click elsewhere works.
7. Arrange Desk…: the whole screen takes clicks as before, the clock and message drag to new
   places, Done keeps them; afterwards steps 2 and 3 work at the new places.
8. Switch **Clock and message take clicks** off: `desk_piece_clicks: false`, no `clock`,
   `message` or `claude_line` in `desk_frames`, `desk_frames_ok: true`. A two-finger click on the
   clock or message now opens the Finder's desktop menu, a plain click does nothing to Sanduhr,
   and the icon under the clock clicks again. Switch it back on.
9. `smoke/smoke run smoke/scenarios/desk-layout.yaml` (from `mac/`) passes (it flips the switch and checks the clock's frame).
