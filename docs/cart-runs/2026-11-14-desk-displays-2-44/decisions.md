<!-- The ledger. Every inference, default, substitution and mid-run class
     upgrade lands here, numbered D1, D2, and so on. Append only. -->

# Decisions: desk-displays-2-44

## D1
- **Decision:** A piece on a display other than main is written in the long form with the suffix last
  (`clock:tl:1:0:bar@all`); Windows' short form (`clock:tl@all`) is read too.
- **Why:** A 2.43 build splits on `:` and reads the anchor from the second field. The long form leaves `tl` intact, so a
  2.43 build that reads a 2.44 string (a downgrade, or a synced defaults file) keeps every piece where it was and only
  loses the display. The short form would hand it `tl@all`, an unknown anchor, and move the piece to its default.
- **Alternative not taken:** Windows' short form only, for identical strings on both platforms. The layout preference is
  per platform, so identical strings buy nothing.
- **Item:** 2
- **How to reverse:** Write the short form in `DeskArrangement.word`; parsing already accepts both.

## D2
- **Decision:** No suffix means the main display on the Mac (Windows: every monitor), and main is the menu bar display
  (`NSScreen.screens.first`), not `NSScreen.main`.
- **Why:** The owner's answers 1 and 2 (spec, Answered by the owner). Every 2.43 Desk drew on one screen; mirroring it on
  update would put the Desk on every external display.
- **Alternative not taken:** Windows' "all" default; keeping `NSScreen.main` (the screen with focus at build time).
- **Item:** 2, 3, 4
- **How to reverse:** `DeskDisplay`'s default and `DeskScreens.current()`'s choice of primary.

## D3
- **Decision:** The live two-display pass and the two-window idle-CPU measurement are not run items; they are listed
  for the owner in the morning report and in smoke section 78.
- **Why:** This Mac has one display connected (system_profiler: Color LCD only). The ask says the owner plugs one in;
  a simulated second screen would be a faked pass.
- **Alternative not taken:** A `stop` item, which is meant for dangerous surfaces, not missing hardware.
- **Item:** run
- **How to reverse:** Run smoke section 78 with the display plugged in before the release.

## D4
- **Decision:** The spec, uncommitted in the builder's tree, is copied onto the branch by item 1 rather than committed on
  `main`.
- **Why:** The run cuts from the last commit and never touches the builder's tree; the uncommitted file is listed in
  the report so the owner can drop their copy after merging.
- **Alternative not taken:** Committing it on `main` first.
- **Item:** 1
- **How to reverse:** Drop item 1's commit and commit the spec on `main`.

## D5
- **Decision:** Item 4 goes to a judgment-tier worker (the session model), not the bulk tier.
- **Why:** It reshapes `DeskController`'s one-window design (click layer, OLED care, Drawers, the island all key off it);
  a wrong call there breaks the main Desk for every user, and the list marks it L, hard.
- **Alternative not taken:** The bulk tier, as for items 2 and 3.
- **Item:** 4
- **How to reverse:** None needed; the tier changes cost, not the output contract.

## D6
- **Decision:** The real-hardware probe of `DeskScreens.id(of:)` ran against this Mac after item 3: every screen's id is a
  36-character UUID of hex and dashes. A second display was connected by then (1024×768, left of main).
- **Why:** Item 3's adapter has no unit test (it reads `NSScreen`); the probe is the cheap check that the id survives the
  `DeskDisplay.parse` rules (no space, `:` or `@`).
- **Alternative not taken:** Waiting for the live pass.
- **Item:** 3
- **How to reverse:** n/a (a read-only probe).

## D7
- **Decision:** Item 4 merged with four known gaps, carried to the report and the live pass rather than parked: extra
  windows get no Dock insets or notch; a piece on `.all` clicks only on the main window's copy; slanted stacks on extra
  windows hit-test by their bounding box; the clock's 1-second timeline, message effects and meter pulses animate in
  every window that draws them.
- **Why:** The checks pass and a one-display Mac draws as before; each gap shows only with two displays and none breaks
  the main Desk. Arrange across displays is item 5's job.
- **Alternative not taken:** Parking item 4 until all four were closed, which would block items 5 to 8.
- **Item:** 4
- **How to reverse:** Follow-ups after the live pass; the report lists them.

## D8
- **Decision:** Item 5 merged after a slow verify (`./test.sh` 1036 s wall, the tests themselves 7.8 s; the worker's first
  run sat 41 min at 3 s of CPU in the test helper and passed on a rerun). Treated as environmental, not as the item.
- **Why:** A build of the same tree with `-warn-long-function-bodies=500 -warn-long-expression-type-checking=500` took 37 s
  and flagged only two bodies item 5 does not touch (`ModNewSheet.body` 1.9 s, `applicationDidFinishLaunching` 0.5 s),
  so no new view strains the macos-15 type-checker. Both checks passed twice.
- **Alternative not taken:** Parking item 5 on the hang.
- **Item:** 5
- **How to reverse:** If later items' runs stay slow, profile `swift test` (the helper's launch) on the integration branch.

## D9
- **Decision:** `propose_desk` checks a monitor id against the state's `monitors` only when the state lists them; an
  older `desk-state.json` without `monitors` leaves the id check to the app, which checks against connected displays.
- **Why:** The server never guesses displays; the app is the authority, and a stale state written by 2.43 has no list.
- **Alternative not taken:** Refusing every id when the state has no `monitors`.
- **Item:** 7
- **How to reverse:** `sanduhr_mcp.py` monitor check; refuse ids when `monitors` is absent.

## D10
- **Decision:** `SetupPrompt.swift` (the prompt for pasting a design from an AI chat) gains the `monitor` field in item 8,
  not item 7.
- **Why:** Item 7's worker left it untouched as outside its scope; it's copy for the same feature and lands with the
  release rows.
- **Alternative not taken:** A separate item.
- **Item:** 7, 8
- **How to reverse:** n/a.

## D11
- **Decision:** Item 8 also carries two fixes found at its verify: the pasted-chat prompt offers only "main" and "all"
  (a plain AI chat can't call `get_desk`, so it never has a display id), and `BundledFontsTests` runs on the main actor.
- **Why:** The test run hung three times in this run (helper at ~0% CPU). A sample showed an AppKit class-init deadlock:
  `+[NSFont initialize]` off the main thread posts `UserDefaults.didChangeNotification` inside the class-init lock, a
  main-queue observer (pre-existing, e.g. `DeskModel`) made the post wait on the main thread, and the main thread sat
  in `NSApplication +initialize` on that lock. Eleven full runs pass after the fix under a 120-240 s watchdog.
- **Alternative not taken:** Parking item 8, or `.serialized` on the whole test target.
- **Item:** 8
- **How to reverse:** Drop `@MainActor` from `BundledFontsTests`; revert the `SetupPrompt` line.
