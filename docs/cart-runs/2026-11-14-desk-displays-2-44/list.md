# Run List: desk-displays-2-44

Every item: a Mac with one display and every 2.43 layout must look exactly as today. Display ids are
`CGDisplayCreateUUIDFromDisplayID` strings; nothing about a display leaves the Mac except through the local MCP's
`get_desk`. Keep SwiftUI view bodies small for the macos-15 type-checker. Never `sed -i` a tracked file. Never touch
`windows-dotnet/` (read it for parity). Spec: `docs/superpowers/specs/2026-11-14-mac-desk-per-monitor-design.md`.
Tests build screens as values (sizes, positions, made-up UUIDs); no test needs a second display.

- [ ] **1. The spec on the branch, and the README pointer**
  Spec ref: `docs/superpowers/specs/2026-11-14-mac-desk-per-monitor-design.md`
  What to build: Copy the approved spec from the builder's tree (read the file at
  `~/Projects/Sanduhr_f-r_Claude/docs/superpowers/specs/2026-11-14-mac-desk-per-monitor-design.md`, write it at
  the same repo path in the worktree, unchanged). Add a one-line pointer to it in `mac/README.md`'s Desk layout section and
  in `docs/spec.md`'s Mac section beside the other per-item specs. Size S.
  Acceptance: Done when the spec is committed on the branch byte for byte and both pointers resolve.
  Verify: `git diff --check HEAD~1`
  Stop class: go

- [ ] **2. A display per placed piece, in the layout string**
  Spec ref: spec > Layout storage, Behaviour
  Depends on: 1
  What to build: `DeskPlacement.monitor: DeskDisplay` (`.main`, `.all`, `.id(String)`; default `.main`). The layout
  word gains Windows' `@<monitor>` suffix (`all` or the UUID; none means main). Write it so a 2.43 build reading the word
  keeps the anchor, size and tilt: when the display is not main, write all five fields and put the suffix last
  (`clock:tl:1:0:bar@all`). Parse both that and Windows' short form (`clock:tl@all`). Stacks, `stack(_:)`, `stackTilt`,
  `setTilt`, `tiltConflicts` and `place` group by (display, anchor), so `tl` on two displays are two stacks. `place` and
  `setScale`/`setStyle` keep a piece's display; a new `setDisplay(_:_:)` moves a piece and seats it in the target stack by
  Settings' order. Pure, in `DeskArrangement.swift` or a new `DeskDisplay.swift`. Size M.
  Acceptance: Done when tests show every 2.43 fixture string round-trips unchanged, both suffix forms parse, the long
  form read by the 2.43 parser keeps anchor, size and tilt (call the old parse rules on it in the test), stacks and tilt
  conflicts split by display, and `setDisplay` keeps size, tilt and style. The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **3. Displays: identity, names, where a piece draws**
  Spec ref: spec > Behaviour; Windows `DeskMonitorNames.cs`, `DeskMonitors.ShowsOn`
  Depends on: 2
  What to build: `DeskScreens` (pure): `DeskScreenInfo {id, frame, primary}` values; names as Windows gives them
  ("3024×1964, main", "2560×1440, right of main", left, above, below, "at X,Y" for overlap, " (2)" for a twin);
  `showsOn(_ display: DeskDisplay, screen:, connected:)` with the fallback (an id not connected draws on main); and
  `screensWithPieces(arrangement, screens)` (main always, plus each screen some piece resolves to). A thin adapter
  `DeskScreens.current()` reads `NSScreen.screens` (primary is `screens.first`, the menu bar display) and the UUID through
  `CGDisplayCreateUUIDFromDisplayID` and `CFUUIDCreateString`. Size M.
  Acceptance: Done when tests cover each name case, the fallback and its return when the display reconnects, main as
  the first screen, and the windows a layout needs for one, two and three screens. The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **4. One Desk window per display that has pieces**
  Spec ref: spec > Drawing
  Depends on: 3
  What to build: `DeskController` keeps a window per screen from `screensWithPieces` instead of one on `NSScreen.main`;
  it rebuilds on a screen-parameters change and on a layout change that adds or empties a display. The per-screen values
  (`deskSize`, `topInset`, `notchRect`, Dock insets) move into a per-window `DeskScreenGeometry` the window's `DeskView`
  reads, while the shared data (usage, meetings, now playing, weather) stays on `DeskModel`. Each `DeskView` draws only
  the stacks `showsOn` gives its screen. The main display is the menu bar display. The click layer, mouse-through and hit
  areas run per window; OLED care drifts and dims every Desk window. Drawers, the companion's roam, the camera badge, the
  wings and the pill stay on the main display. With one screen there is exactly one window, built as today. Split the
  view bodies small. Size L, hard.
  Acceptance: Done when tests show the window plan for one, two and three screens and after a display goes away (its
  pieces move to main without a rewrite), geometry comes from each window's own screen, a one-screen Mac builds one window
  with today's geometry, and pieces never draw on two windows unless placed on all. The suite and the selftest pass.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb`
  Stop class: go

- [ ] **5. Arrange Desk across displays**
  Spec ref: spec > Drawing (arrange)
  Depends on: 4
  What to build: Arrange mode runs on every Desk window; the bar shows on the display under the pointer. Dropping a piece
  on another display's window moves it there (`setDisplay` to that display's id, or `.main` when it is the main one),
  at the anchor nearest the drop. A piece's arrange chrome gains **Show on**: This display / All displays. Done writes once,
  Cancel drops everything, as today. Size M, hard.
  Acceptance: Done when tests on the arrange model show a cross-display drop writes the target display and anchor, Show
  on toggles `.all` and back to the display it is on, Cancel restores the layout string byte for byte, and one display
  behaves as today. The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **6. Settings: a Display picker per piece**
  Spec ref: spec > Settings
  Depends on: 3
  What to build: In Settings ▸ Desk ▸ Layout, a **Display** picker beside each placed piece: Main display, All displays,
  then each connected display by name, plus "A display that is not connected (on the main display for now)" when a piece
  is on one. Shown only with more than one display connected or a piece on a display that is not. A `SettingsIndex` entry
  (display, monitor, screen, second screen). Size M.
  Acceptance: Done when tests on the picker's model show its choices for one and two screens and a disconnected id, that
  a pick writes the layout through `setDisplay`, and the index finds it. The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **7. The designed Desk: monitors in get_desk and propose_desk**
  Spec ref: spec > The agent-designed Desk
  Depends on: 2, 3
  What to build: `DeskStateExport` writes `monitors` (`{id, name, width, height, primary, left, top}`, Windows' keys and
  point sizes) and every placed piece's `monitor` (`"main"`, `"all"` or an id). `DeskDesign.swift` and `sanduhr_mcp.py`
  accept `layout.<piece>.monitor`, refusing an id that is not in the state's `monitors` with Windows' wording;
  `DeskDesignApply` writes it through `setDisplay`; the diff lines name the display. The Python guide text drops "monitor
  is Windows only" and says the Mac now places by display (keep every other Windows-only refusal). Replace the
  `windows-only-monitor.json` shared fixture with `monitor-main.json`, `monitor-all.json`, `monitor-id.json` and
  `monitor-unknown.json`, held by both test suites. `DESIGN_STATE_KEYS` gains `monitors`. Size M, hard.
  Acceptance: Done when both suites pass the shared fixtures, `get_desk` from a state with two monitors returns them and
  each piece's monitor, an unknown id is refused naming the field, and a one-display state still reports `monitors` with
  one entry. The suite and the Python tests pass.
  Verify: `cd mac && ./test.sh && python3 -m pytest -q integrations/tests`
  Stop class: go

- [ ] **8. The 2.44.0 rows**
  Spec ref: spec; CLAUDE.md Mac gotchas
  Depends on: 4, 5, 6, 7
  What to build: `mac/Info.plist` 2.44.0, build 66. A What's New card (`version: "2.44.0"`, id `desk-per-display`,
  destination `.deskLayout`) with its `WhatsNewTests` lines. CHANGELOG `## Unreleased (mac)` ▸ New. `mac/README.md` Desk
  layout section (the display rules, the main-display default, the fallback). `docs/PRIVACY.md` (Last updated 2026-11-14):
  display ids and sizes are stored on the Mac and reported to Claude only through the local `get_desk`. Smoke section 78
  "The Desk on two displays (2.44.0)" in `docs/mac-smoke-test.md`, the live steps for the owner. Size M.
  Acceptance: Done when `WhatsNewTests` pin the new card, the CHANGELOG heading is `## Unreleased (mac)`, PRIVACY matches
  the code, and the suite, the selftest and the Python tests pass.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb && python3 -m pytest -q integrations/tests`
  Stop class: go

## Not this run
- The live two-display pass and the two-window idle-CPU measurement: needs the owner's external display (spec, Testing).
- sanduhr-mods desk-setup 0.2.1 (switch on `monitors` and `wallpaper` separately, a two-display Mac fixture): another
  repo, `hold-for-release` until 2.44.0 is live.
- A Mac wallpaper summary (the 3:1 check on a Mac): its own item.
