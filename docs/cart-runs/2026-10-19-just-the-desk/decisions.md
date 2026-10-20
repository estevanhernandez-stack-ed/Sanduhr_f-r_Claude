<!-- The ledger. Every inference, default, substitution and mid-run class
     upgrade lands here, numbered D1, D2, and so on. Append only. -->

# Decisions: just-the-desk

## D1
- **Decision:** The run is unattended, inferred from the session rather than said in the ask.
- **Why:** "take it" answered an offer; every Cart list this session ran unattended on the owner's say-so, the owner is not watching, and an attended run would build in the working tree instead of on a branch for the PR review the owner expects.
- **Alternative not taken:** Waiting for a mode confirmation (the reference's rule for an ambiguous ask) with nothing built.
- **Item:** run
- **How to reverse:** Say so; the branches delete and the items rerun attended.

## D2
- **Decision:** The open question "does the tour get a Desk-only variant" is yes: three steps (the desktop, the menu bar with the notch, Sign in any time), offered once after Just the Desk is chosen.
- **Why:** A Just the Desk user never sees the tour today (it waits for a successful fetch) and Take the Tour… shows them the signed-out tour, whose cards are about meters, accounts and Claude Code they do not have.
- **Alternative not taken:** Reusing the signed-out tour with cards hidden; its first card still asks to sign in.
- **Item:** 1
- **How to reverse:** Drop `deskOnly` and the offer in `chooseJustTheDesk`; the signed-out tour comes back for them.

## D3
- **Decision:** The open question "skip the Keychain prompt path for Just the Desk" is answered with no change.
- **Why:** The launch's `KeychainStore.exists` is one lookup of the app's own item and a signed release build gets no prompt for it; the prompt the question had in mind is the dev build's partition list (CLAUDE.md, Gotchas, Mac).
- **Alternative not taken:** A code path that skips the Keychain while Just the Desk is on, which would also skip the sign-in that ends it.
- **Item:** 4
- **How to reverse:** Reopen the question if a release build ever prompts.

## D4
- **Decision:** Landing copy means `docs/index.html` and `README.md`; the hub page, the Store copy and the cask's `desc` are out.
- **Why:** The hub page is built on the owner's other machine, the Store copy is the Windows agent's, and the cask's one-line `desc` is under `brew audit` rules for small gain.
- **Alternative not taken:** Editing the cask's `desc`.
- **Item:** 3
- **How to reverse:** A later run touches those surfaces.

## D5
- **Decision:** The ship commit (2.29.0, the What's New card, the CHANGELOG entries) is not an item.
- **Why:** As in the last three runs, the ship commit follows the owner's review and "ship", so the run's PR carries features only.
- **Alternative not taken:** A fifth item that bumps the version on the integration branch.
- **Item:** run
- **How to reverse:** Not needed.

## D6
- **Decision:** Cart's session, friction and wins logs are not written; commits carry the session attribution lines.
- **Why:** The plugin's data path resolves to the work seat on this Mac (previous runs).
- **Alternative not taken:** Writing to the work seat.
- **Item:** run
- **How to reverse:** Point the plugin's data path at the personal seat.

## D7
- **Decision:** `WelcomeTour.deskOnly` is computed from `table` (the desk, menu-bar and notch cards copied to their new steps by a private `moved(_:to:body:)`), not retyped.
- **Why:** `TourCard.step` is a `let` and the desk card is step 2 in the full tour; copying keeps every other field the table's, so one edit to a card reaches both tours.
- **Alternative not taken:** Retyping the three cards in a second literal table.
- **Item:** 1
- **How to reverse:** Inline the literals.

## D8
- **Decision:** `steps(features:table:signedIn:justTheDesk:)` takes `table` as an optional (`nil` means the default), and an explicit table always wins over `justTheDesk`.
- **Why:** With `table` defaulted to `WelcomeTour.table` the function could not tell "not passed" from "passed the default"; tests that pass their own table keep working unchanged.
- **Alternative not taken:** A second function for the Desk-only tour.
- **Item:** 1
- **How to reverse:** Restore the non-optional default and branch on `justTheDesk` first.

## D9
- **Decision:** Take the Tour… shows the Desk-only variant for as long as Just the Desk is on, not only the once-offered tour.
- **Why:** The window reads `JustTheDesk.isOn()` at open; a person with no account retaking the tour still has no meters, accounts or Claude Code to tour.
- **Alternative not taken:** The full tour on a retake.
- **Item:** 1
- **How to reverse:** Pass `justTheDesk` only from the offer in `chooseJustTheDesk`.

## D10
- **Decision:** The Desk-only tour is three steps on every Mac (the notch card shares step 2 with the menu bar), so `just-the-desk.yaml` pins "1 of 3" and steps to 3 everywhere; `tour-step` clamps past the end.
- **Why:** The list allowed for a two-step tour without a notch; the model has none.
- **Alternative not taken:** A looser `/^1 of [23]$/`.
- **Item:** 2
- **How to reverse:** Loosen the pin if a card ever gets its own step behind a feature.

## D11
- **Decision:** The scenario waits 1.5 s after `just-the-desk on` before closing the tour.
- **Why:** The offer opens the tour a second after the choice and `WelcomeTourWindowController.show()` resets the step to 1, so a close sent at once is followed by a reset in the middle of the scenario's stepping. Caught in review; the worker's version closed at once.
- **Alternative not taken:** Not offering the tour from the hook, which would make the hook differ from the button.
- **Item:** 2
- **How to reverse:** Drop the wait if the offer ever checks for an open window.

## D12
- **Decision:** The smoke harness's restore plan ends Just the Desk when a run turned it on (`smoke_core.rb`), with the selftest pinning it.
- **Why:** `on` changes the shared defaults (the Desk on, the reminder off), and the README promises a run leaves them as it found them even when a scenario stops early.
- **Alternative not taken:** Relying on the scenario's own `off` step.
- **Item:** 2
- **How to reverse:** Remove the line; the scenario's last step still ends it on a full run.
