# Mac smoke test

By hand, on the build from `cd mac && SIGN_IDENTITY=- ./build.sh`. Run before every Mac release;
M0 is the first pass. Each line is a check: do the action, see the result.

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

## 1. Fresh defaults: Desk stays off

- [ ] `for d in com.626labs.sanduhr com.626labs.sanduhr.desk com.626labs.sanduhrdesk com.estevan.desk; do defaults delete $d 2>/dev/null; done`
  (credentials and history are files under `~/Library/Application Support/Sanduhr/`, not touched).
- [ ] `open mac/Sanduhr.app`. Widget appears top right; no clock, meetings or message on the desktop;
  no black island around the notch; no Calendar permission prompt.
- [ ] `defaults read com.626labs.sanduhr.desk` shows `migrated = 1` and no `deskEnabled`.
- [ ] Quit and relaunch: still no Desk.

## 2. Widget

- [ ] Cards fill in after the first fetch (sign in via Credentials if the file is gone).
- [ ] Left-click the menu bar hourglass hides the widget; again shows it. Hidden survives a relaunch.
- [ ] Right-click opens the status menu: Show/Hide, Refresh Now, Credentials, Desk Settings, Check for Updates, Quit.
- [ ] Refresh Now updates the footer time; `~/Library/Application Support/Sanduhr/snapshot.json` has a new `captured_at`.
- [ ] Settings: switch a theme, toggle compact (panel resizes, top edge stays put), open the focus timer and close it.

## 3. Alerts

- [ ] Settings, Alerts: turn alerts on; macOS asks for notification permission once. Allow.
- [ ] Set the session line below the current 5-hour percent, Refresh Now: one banner.
- [ ] Refresh Now again: no second banner (once per tier per reset window).
- [ ] Put the line back where it was.

## 4. Desk on

- [ ] Status menu, Desk Settings. Window titled "Sanduhr Desk" opens with Layout, Look, Message, Notch, General.
- [ ] General, "Show Desk": clock, date, Claude line and message appear on the desktop at once. The notch stays plain (it has its own switch, off).
- [ ] Calendar prompt appears (meetings default on). Allow: today's remaining timed meetings show.
- [ ] Layout: move Clock to Top right; it moves live. Hide Meetings; they go. Put both back.
- [ ] Look: ink `9ad7ff, 012089` (space after the comma) on the message draws blue to navy, no gray.
- [ ] Message: `~/Library/Application Support/Desk/messages.txt` exists with the starter lines; add `10-01: smoke test` (today's MM-DD), and within a minute the desktop shows it.
- [ ] Meeting rows with a Teams/Zoom/Meet link: pointer turns into a hand; click opens it. Clicking empty desktop still reaches Finder.
- [ ] Turn "Show Desk" off: desktop layer (and notch, if on) go. On again: they come back (repeat twice; no doubled refreshes in Console).

## 5. Notch (Mac with a notch only)

- [ ] Notch tab shows "Extend the camera notch" off. Turn it on: black wings extend the notch left and right; left wing shows the time or the next meeting within the hour, right wing shows `5h N%  wk N%`.
- [ ] Strip under the notch draws when Notch, "Extra height below" is above 0; its text appears with "Under the camera too".
- [ ] Click the island: Desk Settings opens.
- [ ] Quit and relaunch: the notch is still on (the switch saved). Turn it off: wings and strip go, Desk layer stays.
- [ ] Full-screen an app: wings stay above it. Switch Spaces: wings stay.
- [ ] External display as main: no island drawn.

## 6. Links and keys

- [ ] `open sanduhr://settings` and `open estedesk://settings` both open Desk Settings.
- [ ] `open estedesk://join-next` opens the next meeting with a link, or beeps when there is none.
- [ ] Remove or comment out any skhd binding for alt - j and alt - s first (`skhd --reload`), or skhd answers before Sanduhr and this proves nothing.
- [ ] With Desk on, from any app: Option+J joins the next meeting (or beeps), Option+S opens Desk Settings. No Accessibility prompt.
- [ ] In a text field, Option+S does not type ß while Desk is on (expected cost). General, Shortcuts off: Option+S types ß again, Option+J types ∆. Back on: shortcuts work again.
- [ ] Desk off: Option+S types ß; the shortcuts are gone.

## 7. Migration from Sanduhr Desk, then restore

- [ ] Quit Sanduhr. `defaults delete com.626labs.sanduhr.desk`, then
  `defaults import com.626labs.sanduhrdesk ~/sanduhr-smoke/com.626labs.sanduhrdesk.plist`.
- [ ] Launch: Desk comes on by itself with the old layout, colors and font (EsteFont if none was set).
  A running Sanduhr Desk app is quit.
- [ ] The notch is on (the old apps defaulted it on), unless it was switched off there.
- [ ] Restore: quit Sanduhr, then
  `for f in ~/sanduhr-smoke/*.plist; do defaults import "$(basename "$f" .plist)" "$f"; done`.

## 8. Intel slice

- [ ] On an Intel Mac, or `arch -x86_64 mac/Sanduhr.app/Contents/MacOS/Sanduhr` under Rosetta:
  launches, widget fetches. Quit.
