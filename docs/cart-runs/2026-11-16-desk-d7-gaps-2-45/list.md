# Run List: desk-d7-gaps-2-45

Every item: a Mac with one display draws, clicks and arranges exactly as 2.44.0. Tests build screens as values
(sizes, positions, made-up UUIDs); no test needs a second display. Keep SwiftUI view bodies small for the macos-15
type-checker. Never `sed -i` a tracked file. Never touch `windows-dotnet/`. Nothing committed names a home path or a
person. Run every command from the item's worktree.

- [ ] **1. Dock clearance and the notch on every Desk window**
  Spec ref: 2.44 D7 (gap 1); `DeskController.buildExtraWindows`, `DeskScreenGeometry.of`, `DockClearance.swift`
  What to build: An extra window's `DeskScreenGeometry` gets that screen's camera notch (`NSScreen.cameraNotch`, as
  the main window reads it) and its Dock reservation (the always-shown Dock's share from `frame` against
  `visibleFrame`, the rule `DockGeometry.alwaysShown` uses), recomputed when screen parameters change (the Dock
  moves between displays, resizes, starts or stops hiding). The auto-hiding Dock's slide stays main-only unless
  DockFollower generalises for the screen the Dock is on at small cost; record which in the ledger. Pure helpers
  with tests; the AppKit read stays thin. Size M.
  Acceptance: Done when tests show an extra screen with a Dock at the bottom, left and right gets those insets,
  one without the Dock gets none, a notched screen that is not main gets its notch rect, and the main window's
  geometry is unchanged. The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **2. Every copy of a piece on All displays takes clicks**
  Spec ref: 2.44 D7 (gap 2); `DeskView.extraPiece`, `DeskFrameReporting`, `DeskModel.elements()`, `DeskHitTest`
  Depends on: 1
  What to build: Today an `.all` piece reports frames only from the main window, because the model holds one frame
  per element. Keep frames per Desk window (main plus each extra, keyed by the window's display), build elements
  per window, and judge a click against the elements of the window under the pointer (the click host already
  picks that window). The click layer plates on each window come from that window's own elements. Meetings join,
  now playing, the limit menu and piece clicks work on every copy. One display: the same single store as today.
  Size L, hard.
  Acceptance: Done when tests show an `.all` piece's click areas on the main and an extra window, a click on the
  extra copy resolving to the same element as one on the main copy, a piece on one display reporting only there,
  and a one-display Mac's elements identical to 2.44.0's for the same frames. The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **3. Slanted stacks take clicks by their shape on every window**
  Spec ref: 2.44 D7 (gap 3); `DeskFoldStack`, `DeskModel.setFold`, `DeskElements.folded`, `DeskClickLayer.slantedAreas`
  Depends on: 2
  What to build: Folds are stored per anchor for the main window only, so a slanted stack on an extra window
  hit-tests by its bounding box and gets no slanted plates. Store folds per window too (beside item 2's frames),
  fold each window's elements with its own folds, and give each click layer its slanted areas moved by its offset.
  Size M, hard.
  Acceptance: Done when tests show a slanted stack on an extra window misses a click in its bounding box's empty
  corner and takes one on its face, its click layer gets slanted areas, two windows' `tl` folds don't mix, and the
  main window's folding is unchanged. The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **4. The companion roams the display it is placed on**
  Spec ref: 2.44 D7 (gap 4); owner's answer "Roam its display"; `DeskWindowPlan.mainOnly`, `CompanionDriver`,
  `CompanionLayerView`, `DeskDisplayChoices`, `DeskArrange`
  Depends on: 1
  What to build: There is still one companion. It draws in the window of the display its placement resolves to
  (`.id` connected: that display; `.main`, `.all` or a display not connected: main), and `CompanionDriver` roams
  that window's area (`CompanionRoamMath.area` from its geometry: size, menu bar, Dock from item 1), moving when
  its display changes or goes away. `mainOnly` goes; the window plan counts the companion's display like any
  piece's, except `.all` adds no windows for it. Settings' Display picker for the companion offers Main display
  and each display, not All displays; Arrange's Show on is not offered for it; a drop on another display moves it
  there. `propose_desk` accepts `monitor` on the companion as for any piece and its guide says All draws it on the
  main display. Shared fixtures stay in step on both suites. Size M, hard.
  Acceptance: Done when tests show the window plan with the companion on an extra display (that window exists,
  main still does), `.all` keeping it on main with no extra window, the roam area from the extra window's
  geometry, the fallback to main when its display is unplugged and its return, the picker's choices for the
  companion, and a cross-display drop writing the display. The suite and the Python tests pass.
  Verify: `cd mac && ./test.sh && python3 -m pytest -q integrations/tests`
  Stop class: go

- [ ] **5. The 2.45.0 rows**
  Spec ref: CLAUDE.md Mac gotchas
  Depends on: 1, 2, 3, 4
  What to build: `mac/Info.plist` 2.45.0, build 67. A What's New card (`version: "2.45.0"`, an id like
  `desk-every-display-finished`, destination `.deskLayout`) with its `WhatsNewTests` lines. CHANGELOG
  `## Unreleased (mac)` above `## v2.44.0-mac` with New and Fixes. `mac/README.md` Desk layout section (the
  companion rule, the Dock and notch on every display, clicks on every copy). Smoke section 79 "The Desk on two
  displays, finished (2.45.0)" in `docs/mac-smoke-test.md`, the live steps for the owner, including the 2.44 checks
  not done live (clicks on the second display, now playing on All displays, the main Desk under focus changes,
  drift). `docs/PRIVACY.md` only if what is stored or reported changes. Size S.
  Acceptance: Done when `WhatsNewTests` pin the new card, the CHANGELOG heading is `## Unreleased (mac)`, and the
  suite, the selftest and the Python tests pass.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb && python3 -m pytest -q integrations/tests`
  Stop class: go

- [ ] **6. The auto-hiding Dock's slide on every display**
  Spec ref: live pass 2026-11-17 (owner: "the text does not move up when the bar does", the bar being the Dock); D4
  Depends on: 5
  What to build: `DockFollower` follows the auto-hiding Dock on whichever display it comes up on, not only the main
  one: the pointer watch at that display's Dock edge, the window-list check over that display, and the slide applied to
  that display's Desk window (main through `model.dockInsets`, an extra through its geometry). One display: unchanged.
  CHANGELOG Fixes line, README and smoke section 79 step 1 updated to match. Size M, hard.
  Acceptance: Done when tests show the slide's target display chosen from where the Dock edge is, an extra window's
  insets sliding while main's stay put (and the reverse), and a one-display Mac's follower behaving as before. The
  suite and the selftest pass.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb`
  Stop class: go

## Not this run
- The live two-display pass: needs the owner at the second display (smoke section 79).
- Drawers, the island and the camera badge on other displays.
- The release dispatch, appcast and cask.
