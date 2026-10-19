<!-- Written once at intake, before any item is planned. Keep the Ask
     verbatim. Every Inferred line is also a ledger entry in decisions.md. -->

# Run Brief: no-notch-island

## Ask
"start 1 unattended", where 1 is the first item of the list Cart offered after the 2.27.0 ship:
"An island for screens without a notch (issue #119). Mac minis, iMacs, external displays,
clamshell MacBooks: none of them get the notch wings, the glow or the camera strip today. A
top-center pill at the menu bar is the spec'd shape, the Touch Bar spike the stretch." The same
offer folded in the two reflection debts: "a one-page Mac scope so the next scan has a user to aim
at, and the icon-aware-layer note in the spec's Mac section so the Accessibility route isn't
rediscovered."

## Source
- **Kind:** issue plus two documentation debts from `docs/reflection.md`
- **Location:** GitHub issue #119 (Proposal A, the acceptance list); `docs/reflection.md` sections
  "Scope & Idea Clarity" and "Technical Decisions"

## Intent
A Mac without a notch gets the same at-a-glance line at the top of the screen that a notched
MacBook gets from the island: the wing content, the click to Settings, the two-finger menu, the
glow and the camera light on one frame. Off by default; nothing changes on a notched Mac or with
the switch off. The Mac app gets a scope page of its own, and the spec records why the Desk's
click layer is not icon-aware.

## Done when
- `docs/mac-scope.md` names the Mac app's user, what the Desk, the island and the widget are for,
  and the cuts; `docs/spec.md`'s Mac section points at it and carries the icon-aware-layer note.
- A pure `NotchPill` rule set decides the pill's frame and visibility, pinned by tests: main
  screen only, never on a screen with a notch, hidden while the menu bar is hidden (full screen,
  auto-hide) and while the pointer is in the menu bar, width capped against app menus.
- With the new switch on and no notch, the Desk builds a top-center pill window at the menu bar
  with the same wing content, click and two-finger behavior as the island; the glow and the camera
  light draw on its frame. A debug override lets a notched Mac show the pill for the smoke tools.
- Settings, Desk, Notch carries the switch and says what the pill does; the debug snapshot reports
  the pill; the smoke fixture, selftest and README know the new keys; a smoke scenario exists for
  the override.
- `cd mac && ./build.sh --debug`, `./test.sh` and `ruby smoke/selftest.rb` pass on the
  integration branch.

## Out of scope
- Proposal B, the Touch Bar spike: needs the Touch Bar Mac and private APIs; a findings note, not
  app code. Listed under "Not this run".
- The CPU and memory measurement on the old Mac: needs that Mac.
- Deciding the menu-collision rule on real no-notch hardware: this Mac has a notch, so the rule
  ships conservative (a width cap and hide-under-pointer) and the issue keeps the hardware check.
- Running the smoke scenario against a live dev build: a dev build shares the release app's
  support folder and settings on this Mac and runs signed out, which the owner backs up by hand
  around every dev session. The scenario is written and fixture-checked; the live run waits for the
  morning.
- Windows parity.

## Inferred
Each line is something the builder did not say. Say where it came from.
- "1" means the no-notch island, the first item of the list Cart offered after 2.27.0, with the two
  reflection debts folded in as Cart proposed in that message (from: the offer and the reply "start 1 unattended").
- The pill reuses the wings window pattern (`DeskController.buildWingsWindow`, `.popUpMenu`
  level, `FirstClickHostingView`) and `NotchWingsView`'s content, rather than a new view family
  (from: issue #119 "drawing the same wing content" and the code).
- The switch lives in the `.desk` defaults suite as `notchPill`, off, with a `DeskLayer`-style enum
  (key, defaultOn, title, caption, isOn, set), since every Desk option of 2.26 and 2.27 took that
  shape (from: `DeskLayer`, `DeskPlate`, `DeskPieceClicks`).
- Main screen only in this cut; a later item can add every notch-less screen (from: issue #119's
  open question, and the Desk window being main-screen only today).
- The debug override is a hook action, `notch-pill on|off|force`, in the style of `desk on|off`
  (from: `mac/smoke/smoke` usage and `DebugHooks.swift`).
- Items 2 to 4 are checked by the full Mac build, test suite and selftest, the way the 2026-10-17
  runs were (from: those runs' `list.md`).

## Mode
- **Mode:** Unattended
- **Triggered by:** The ask said so ("start 1 unattended"); the local clock also read 21:56.
