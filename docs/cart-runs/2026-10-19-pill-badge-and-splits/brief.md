<!-- Written once at intake, before any item is planned. Keep the Ask
     verbatim. Every Inferred line is also a ledger entry in decisions.md. -->

# Run Brief: pill-badge-and-splits

## Ask
"start it unattended and send the things we need to old mac", where "it" is the pick offered a
moment earlier: "the #6 camera and mic badge fix plus the two file splits as one small run, with
the backlog headings tidied in the same PR, then the measurements on maccy-lives through the Local
sync session."

## Source
- **Kind:** a backlog item's open list, a previous run's "found along the way", and stale headings
- **Location:** `docs/BACKLOG.md` #6 (the camera and mic badge bullet), the polish-leftovers run's
  report ("the next two files over 500"), `docs/BACKLOG.md` #3 and #6 headings

## Intent
The one #6 item that is code and not a measurement is fixed, so the next check on the no-notch Mac
can verify it; the last two Mods files over 500 lines are split; and the backlog stops saying the
tour is only spec'd and that the pill has never been seen on real hardware.

## Done when
- With the pill up and no wings window, the camera and mic indicators draw on the pill (beside its
  text, or in a chosen place), not in the top-center tab; with neither up they keep their tab. A
  pure rule pins it.
- `ModsCatalogView.swift` and `ModsSeatSections.swift` are split into files under 500 lines each
  with no behavior change.
- `docs/BACKLOG.md` #3 reads shipped, and #6 records the pill's live confirmation on the old Mac and
  the badge fix.
- `cd mac && ./build.sh --debug`, `./test.sh` and `ruby smoke/selftest.rb` pass on the integration branch.

## Out of scope
- The four #6 measurements (menu collision, the 24-pt text floor, CPU and memory, the Touch Bar
  spike): briefed to the old Mac's session in parallel; their findings land as a note from there.
- A release: these ride with the next minor.
- `ModsCatalogView` or seat section behavior, copy or layout.

## Inferred
Each line is something the builder did not say. Say where it came from.
- The island counts as up when either the wings window or the pill window exists; the tab stays
  the fallback when neither does (from: `AVIndicatorViews.update`, `DeskController.pillWindow`, D2).
- The Settings Notch preview asks the same rule, so it follows the same change where it passes
  `islandUp` (from: the `spot(_:islandUp:in:)` doc comment, D2).
- The split boundaries follow the files' own section order and keep each file's header comment
  honest, as the ModsSettings split did (from: the polish-leftovers run, item 6, D3).
- #3's shipped version is the one the welcome tour's What's New card names (from:
  `WhatsNewCards.swift`, D4).

## Mode
- **Mode:** Unattended
- **Triggered by:** The ask said so ("start it unattended").
