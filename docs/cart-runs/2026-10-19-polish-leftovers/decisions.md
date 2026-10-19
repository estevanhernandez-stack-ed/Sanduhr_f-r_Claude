<!-- The ledger. Every inference, default, substitution and mid-run class
     upgrade lands here, numbered D1, D2, and so on. Append only. -->

# Decisions: polish-leftovers

## D1
- **Decision:** The six items are the run 2 report's leftovers 1, 3, 4, 5 and 6, with leftover 1 split in two (the Order caption, the At column); the spec's old shadow label (leftover 2) stays left.
- **Why:** The owner named six; the report itself counted the shadow label as "left as the dated record".
- **Alternative not taken:** Touching `docs/settings-v2-spec.md`'s two old labels.
- **Item:** run
- **How to reverse:** Add a seventh item later.

## D2
- **Decision:** Three-digit hex shorthand is accepted and expanded on commit, not refused with a clearer hint.
- **Why:** Run 2's D8 recorded that a saved "fff" still renders; refusing it only when retyped is the inconsistency the report flagged.
- **Alternative not taken:** Refusing shorthand everywhere, which would break saved values on their next edit.
- **Item:** 4
- **How to reverse:** Drop the expansion and change the hint.

## D3
- **Decision:** The `ModsSettings.swift` split is the last item.
- **Why:** Each item's branch cuts from the integration tip; a split merged early would make every later touch of that file a conflict.
- **Alternative not taken:** Running it first.
- **Item:** 6
- **How to reverse:** Not needed.

## D4
- **Decision:** Cart's session, friction and wins logs are not written; commits carry the session attribution lines.
- **Why:** The plugin's data path resolves to the work seat on this Mac (previous runs, D5).
- **Alternative not taken:** Writing to the work seat.
- **Item:** run
- **How to reverse:** Point the plugin's data path at the personal seat.

## D5
- **Decision:** The Arrange scenario pins exact hidden keys after hide and show instead of an empty list after show: `desk_arrange.hidden` is every known widget not in the working layout, so with the scenario's four-piece layout three pieces are hidden throughout; `[]` holds only after cancel.
- **Why:** The brief's "empty after show" assumed `hidden` meant "hidden by the user"; the model defines it as "not placed", and the Swift test on the same layout proves the exact values.
- **Alternative not taken:** A second snapshot key for user-hidden pieces only.
- **Item:** 5
- **How to reverse:** Add `hidden_by_user` beside `hidden` if the distinction is ever needed.

## D6
- **Decision:** `ModsPageModel` split at its "By Seat" mark into the class file (489 lines) and an extension file for the By Seat and own-mod methods, with that section's eight stored properties hoisted, unchanged, to the end of the class body.
- **Why:** An `@Observable` extension cannot hold stored properties, and no single mark cut left the base under 500 lines without one; the hoist is the one reorder in an otherwise move-only split.
- **Alternative not taken:** Leaving the model whole at 700 lines.
- **Item:** 6
- **How to reverse:** Move the eight declarations back to their mark; nothing else depends on their position.

## D7
- **Decision:** Close-out gates replaced by the repo's own checks on the integration tip plus a grep over the run's diff for secret shapes and home paths.
- **Why:** The vibe-test and vibe-sec gates are not runnable on this Mac (2026-10-17 run 1, D12).
- **Alternative not taken:** Skipping the secret check.
- **Item:** run (close-out)
- **How to reverse:** Run the gates where they work before the PR merges.
