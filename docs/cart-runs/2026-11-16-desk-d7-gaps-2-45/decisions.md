<!-- The ledger. Every inference, default, substitution and mid-run class
     upgrade lands here, numbered D1, D2, and so on. Append only. -->

# Decisions: desk-d7-gaps-2-45

## D1
- **Decision:** The companion roams the display it is placed on; there is still one companion, and All displays (or
  a display not connected) puts it on the main display.
- **Why:** The owner's answer ("Roam its display") to the gap-4 question. Two companions would need two drivers and
  two moods; one that lives where it is put closes the gap.
- **Alternative not taken:** Pin the companion to main and offer no display for it (size S).
- **Item:** 4
- **How to reverse:** Restore `DeskWindowPlan.mainOnly` and drop the companion's display choices.

## D2
- **Decision:** These fixes ship as 2.45.0 build 67 with a What's New card, not 2.44.1.
- **Why:** The companion on any display is new behaviour; CLAUDE.md asks a card of a minor, and a patch would hide it.
- **Alternative not taken:** 2.44.1 with only CHANGELOG Fixes.
- **Item:** 5
- **How to reverse:** Info.plist 2.44.1 and drop the card and its test lines.

## D3
- **Decision:** Items 2, 3 and 4 go to judgment-tier workers (the session model); items 1 and 5 to the bulk tier at
  high effort.
- **Why:** 2 and 3 reshape how every click on the Desk is judged (one frame per element becomes one per window); a
  wrong call breaks the main Desk's clicks for every user. 4 rewires the companion's one driver. Same call as D5 of
  the 2.44 run.
- **Alternative not taken:** Bulk tier for all five.
- **Item:** 2, 3, 4
- **How to reverse:** None needed; the tier changes cost, not the output contract.

## D4
- **Decision:** On a display besides the main one, the Desk clears the always-shown Dock (its share of that screen's
  frame against its visible frame) and that screen's notch; the auto-hiding Dock's slide stays on the main display.
- **Why:** `DockFollower` is built around one screen (its window-list extent, pointer watch and slide state), so
  following the slide on every display is not a small change. An auto-hiding Dock reserves nothing, as on a
  one-display Mac, so pieces rest at the edge there.
- **Alternative not taken:** A DockFollower per display.
- **Item:** 1
- **How to reverse:** `DeskScreenGeometry.extra` back to `of(..., notch: nil)`; drop `refreshExtraGeometry`.

## D5
- **Decision:** Click frames are kept per Desk window (`DeskModel.windowFrames: [DeskDisplay: DeskFrames]`, `.main`
  plus `.id(display)` for each other window), all still in the main window's coordinates; the old per-field
  properties read and write `.main`. A click is judged against the elements of the window under the pointer.
- **Why:** One-display identity holds by construction (only `.main` exists, and a test compares its elements with
  2.44.0's path), and the pointer reading stays one coordinate space. Folds extend the same store (item 3).
- **Alternative not taken:** Each window's frames in its own local coordinates, with the pointer translated per
  window.
- **Item:** 2
- **How to reverse:** Revert item 2's commit; `DeskView.extraPiece` turning reporting off for `.all` is the 2.44 rule.

## D6
- **Decision:** Item 2's smaller calls, accepted at collect: the strip under the camera stays main-only; state.yaml's
  `desk_frames` lists the main window's elements only (a piece on another display drops out of it instead of showing
  an off-window frame); row frame changes now fire `onHitAreasChange`; Show Widget sits beside the copy the menu was
  opened from.
- **Why:** Each keeps one display unchanged and fixes a 2.44 rough edge on two (a stale meters frame, rows that moved
  under a still pointer).
- **Alternative not taken:** Listing every window's elements in `desk_frames`.
- **Item:** 2
- **How to reverse:** The worker's report lists each; all live in `DeskModel`, `DeskController` and `DeskFrames.swift`.

## D7
- **Decision:** Folds live inside each window's `DeskFrames`; `DeskFoldStack` reports on every window, moved by the
  window's offset, and each window folds its elements over its own pieces. Arrange's folds stay main-window-only.
- **Why:** One store means a gone window's folds go with its frames. Arrange keys folds by anchor alone, so a drop
  onto a slanted stack on an extra window still lands by its bounding box; that is a drop-target nicety, not a click,
  and outside item 3's done-when.
- **Alternative not taken:** Per-window folds in Arrange too (Arrange keyed by display and anchor).
- **Item:** 3
- **How to reverse:** Revert item 3's commit; the 2.44 rule is folds reported by the main window only.

## D8
- **Decision:** `mainOnly` becomes `drawnOnce` (the companion): it draws in the one window its display resolves to,
  `.all` reading as main. The companion re-seats only when its window changes; new geometry on the same window keeps
  its place. The picker shows a hand-written `.all` companion as Main without rewriting it.
- **Why:** D1's rule. Re-seating only on a window change keeps a one-display Mac exactly as 2.44.0 (no re-seat when
  Arrange rebuilds the view), and not rewriting `.all` keeps a 2.44 string byte for byte.
- **Alternative not taken:** Refusing `monitor: "all"` on the companion in `propose_desk`.
- **Item:** 4
- **How to reverse:** Revert item 4's commit; `DeskWindowPlan.mainOnly` is the 2.44 rule.

## D9
- **Decision:** Item 6 added mid-run, after the live pass: the auto-hiding Dock's slide is followed on every display,
  reversing D4's main-only slide.
- **Why:** On the owner's second display the Dock rose over the pieces ("the text does not move up when the bar
  does"); the owner confirmed it was the Dock and asked for the fix in this run.
- **Alternative not taken:** Shipping 2.45.0 with the gap documented.
- **Item:** 6
- **How to reverse:** Revert item 6's commit; D4's rule returns.

## D10
- **Decision:** One shared Dock slide that follows the display under the pointer (`DockTarget`), not a follower per
  display; main keeps `apply` → `model.dockInsets`, extras slide through `model.extraDockInsets` without rebuilding.
  With separate Spaces off, only main slides ahead of the window list. `DockGeometry.shownExtent` now tests
  `!isEmpty` instead of `!isNull`.
- **Why:** Only one Dock exists; a handover on crossing is simpler than two state machines. The `isNull` test counted a
  zero-width touch at a shared edge as "over main" and would have slid main's pieces for a Dock on the left display (a
  gotcha the new tests found). The companion's roam area and OLED drift on an extra still read the resting geometry.
- **Alternative not taken:** A DockFollower per display.
- **Item:** 6
- **How to reverse:** Revert item 6's commit (D4's main-only slide returns).
