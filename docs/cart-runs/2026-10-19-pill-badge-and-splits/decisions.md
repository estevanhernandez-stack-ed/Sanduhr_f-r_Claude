<!-- The ledger. Every inference, default, substitution and mid-run class
     upgrade lands here, numbered D1, D2, and so on. Append only. -->

# Decisions: pill-badge-and-splits

## D1
- **Decision:** The run holds the one #6 item that is code (the badge under the pill), the two Mods file splits and the backlog tidy; the four measurements go to the old Mac's session in parallel.
- **Why:** The owner picked that bundle from the offered list and asked for the old-Mac work to be sent at the same time.
- **Alternative not taken:** Waiting for the measurements before touching #6.
- **Item:** run
- **How to reverse:** Not needed.

## D2
- **Decision:** The island counts as up for the indicators when the wings window or the pill window exists; the Settings Notch preview follows the same rule.
- **Why:** `AVIndicatorViews.update` reads only `wingsWindow`, and the pill view already carries the beside-wing slot; the preview asks the same `spot` and would otherwise disagree with the Desk.
- **Alternative not taken:** Moving the tab out from under the pill (a second placement rule for a case the island already covers).
- **Item:** 1
- **How to reverse:** Drop `pillUp` from the rule.

## D3
- **Decision:** Both splits follow the files' own section order; `ModsCatalogView.swift` becomes three files (view, rows, card), `ModsSeatSections.swift` two (seat sections, repo sections).
- **Why:** The ModsSettings split did the same and merged clean; cutting at section marks keeps `git diff -M` honest.
- **Alternative not taken:** One file per view type.
- **Item:** 2, 3
- **How to reverse:** Concatenate.

## D4
- **Decision:** #3 reads shipped, naming the welcome tour's release from its What's New card and the Desk-only variant from 2.29.0.
- **Why:** The heading said spec'd while the tour has shipped and grown a variant.
- **Alternative not taken:** Deleting #3.
- **Item:** 4
- **How to reverse:** Not needed.

## D5
- **Decision:** Cart's session, friction and wins logs are not written; commits carry the session attribution lines.
- **Why:** The plugin's data path resolves to the work seat on this Mac (previous runs).
- **Alternative not taken:** Writing to the work seat.
- **Item:** run
- **How to reverse:** Point the plugin's data path at the personal seat.

## D6
- **Decision:** The Settings Notch preview draws the real pill (`NotchWingsView` with `pill`) in place of the tab when the pill switch is on, instead of only passing `islandUp` through.
- **Why:** With the pill counting as the island the preview's model would say "beside" while the canvas still drew a tab; the preview now matches the screen.
- **Alternative not taken:** The one-line `islandUp` change alone, leaving the preview's tab drawn under a "beside" state.
- **Item:** 1
- **How to reverse:** Drop `pillView` from `NotchTabCanvas`; keep the `islandUp` line.

## D7
- **Decision:** `islandUp` reads `pillWindow != nil`, not the window's visibility, so while the pill hides for the pointer in the menu bar the indicators stay "beside" and vanish with it rather than jumping to the tab.
- **Why:** The same rule the wings window has had; a moment's hide should not move the indicators.
- **Alternative not taken:** Switching to the tab while the pill is hidden.
- **Item:** 1
- **How to reverse:** Pass `pillWindow?.isVisible == true`.

## D8
- **Decision:** `applyPill()` ends with an explicit `AVIndicatorController.shared.update()`.
- **Why:** The controller re-runs on Desk-defaults changes and screen changes, which covers the switch and the hook by notification order; the explicit call makes the pill's build and close re-place the indicators without relying on that order. Added in review.
- **Alternative not taken:** Relying on the defaults observer alone.
- **Item:** 1
- **How to reverse:** Remove the line.

## D9
- **Decision:** The catalog split widened four views and the top-level `highlighted(_:_:)` helper from private to internal; the helper lives in the rows file next to its first user.
- **Why:** They are used across the cut; everything used only inside its new file stays private. A line-multiset diff of the three files against the original shows only those five declarations and the two headers changed.
- **Alternative not taken:** Duplicating `highlighted` in the card file.
- **Item:** 2
- **How to reverse:** Concatenate the files and restore `private`.

## D10
- **Decision:** The seat split renamed the two private helpers to `SeatCaption` and `SeatSubHeading` (two other files have their own private `Caption`) and the byte formatter to `seatBytes`, a module-wide free function now that both files use it.
- **Why:** Widening a private `Caption` would clash; a free function named `bytes` is too generic for the module (review rename; the worker had left it `bytes`).
- **Alternative not taken:** Duplicating the helpers in the new file.
- **Item:** 3
- **How to reverse:** Concatenate and restore the private names.
