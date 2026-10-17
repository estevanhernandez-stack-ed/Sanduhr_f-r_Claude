# Run Brief: polish-ux

## Ask
"I want to make sure we are ready for any user. I am concerned that the text/items above files will make it difficult for some users with full desktops and that the settings still need a bit of attention/organization mostly in the mods and configs area since it is so new." Then, on the scan's recommendation (the Mods & Config first-run header and a Desk readability plate first, the rest as a batch): "I am happy with your recommendations. We can use vibe cart to run both sections with take knocking down that list at the end." Mode: "Yes unattended".

## Source
- **Kind:** plugin report, `/vibe-iterate:ux-polish` scan of 2026-10-17
- **Location:** `docs/vibe-iterate/ux-polish-2026-10-17.md` (37 rough patches with file:line, trust and effort scores)

## Intent
A first-time user with a full desktop and no interest in mods should (1) read the Desk's text over their files, and (2) open Mods & Config and find "Meters above the prompt" with its switch at once, with the mods-and-packs machinery out of the way until it applies. Existing users keep every choice they made.

## Done when
- Item 1 ships the header; item 2 ships the plate; items 3–14 close the catalog's remaining rows or record why not.
- Every item: `cd mac && ./build.sh --debug && ./test.sh` green in its worktree and again before merge; string changes carry their smoke fixture updates; no existing user preference changes meaning.
- `report.md` with the push commands and anything held or parked.

## Out of scope
- The "keep clear of desktop items" route (Finder automation, a permission prompt): the owner chose the plate; B can come later on top of it.
- The "quiet on a busy desktop" route (Desktop-folder access prompt).
- `windows-dotnet/`, the Windows agent's.
- Anything not in the scan's catalog.

## Inferred
- Ptolemy's posture governs the work (regression-aware, user-trust-aware, small-diff-preferred) with Cart as the engine (from: the owner choosing Cart's /take for execution after the vibe-iterate scan).
- The plate defaults on for new installs (first run writes the key) and off for existing installs (absent key reads as off), so no current Desk changes look without the user's say (from: posture.md § User-trust-aware).
- The new header lives above the tabs and reuses the existing per-folder switch row and consent sheet rather than new consent logic (from: `ModsOwnSettings.swift`, `ModStudio.swift:267`).
- Cart's own session/friction/wins logs are not written (they resolve to the work seat's folder) (from: run 1, D3).
- Commits carry the session's attribution lines (from: the session's git rules).

## Mode
- **Mode:** Unattended
- **Triggered by:** builder confirmed the recommendation ("Yes unattended"): 14 items.
