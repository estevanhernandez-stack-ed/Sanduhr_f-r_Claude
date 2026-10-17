# Decisions: polish-ux

## D1
- **Decision:** The Desk plate defaults on for new installs and off for existing ones (absent key = off).
- **Why:** Ptolemy's user-trust posture: no current Desk changes its look without the user's say; a new user with a full desktop gets the readable default.
- **Alternative not taken:** Default on for everyone (a visible change on update) or off for everyone (the first-timer never finds it).
- **Item:** 2
- **How to reverse:** Flip the absent-key reading in `DeskView` and the first-run write.

## D2
- **Decision:** "Clock and message take clicks" defaults off for new installs; existing values are kept.
- **Why:** On a full desktop the on-default reads as broken Finder clicks (catalog #32); the feature stays one toggle away.
- **Alternative not taken:** Keep the on-default and reword the caption only.
- **Item:** 11
- **How to reverse:** Remove the first-run write.

## D3
- **Decision:** The vocabulary change (item 5) renames user-facing strings only; `ModsTab` raw values, defaults keys and the smoke hook's tab names stay.
- **Why:** Saved preferences and the smoke harness's `showTab` must keep working; the user never sees identifiers.
- **Alternative not taken:** Rename identifiers too (touches 300+ sites, run 1's "not this run").
- **Item:** 5
- **How to reverse:** Not needed.

## D4
- **Decision:** Progressive disclosure thresholds: more than 10 cards or more than one folder shows the Catalog toolbar; two or more folders shows the folder pickers and Compare.
- **Why:** The scan's numbers; a single-folder, stock-mod user needs none of them. The thresholds are constants in one place so they can move.
- **Alternative not taken:** A Simple/Advanced mode (settings-v2-spec rejects it).
- **Item:** 6
- **How to reverse:** Change the two constants.

## D5
- **Decision:** Cart's session, friction and wins logs are not written (work-seat path); commits carry the session attribution lines; `windows-dotnet/` untouched.
- **Why:** As run 1 (D1, D3, D4 there).
- **Alternative not taken:** None.
- **Item:** run-wide
- **How to reverse:** Not needed.

## D6
- **Decision:** Item 5 renamed "By Seat" to "By Folder" in the three older What's New card bodies (2.16, 2.18, 2.19) and the MCP server's remedy string, not only on the page.
- **Why:** The names contract: a control has one name everywhere, and the retired-names test scans every string literal, so the old name would have failed it. A reader of an old card is pointed at the tab as it is called now.
- **Alternative not taken:** An exception in the test for historical card copy.
- **Item:** 5
- **How to reverse:** Add the exception and restore the three bodies (commit f284dac).

## D7
- **Decision:** Item 5's first merge check was a false fail: my grep for the literal `"By Seat"` hit the retired-names table (`SettingsNames.retired`, which must hold the old name) and a doc comment. Re-checked and merged.
- **Why:** The check tested the wrong thing; the tests and selftest had passed.
- **Alternative not taken:** Sending the worker back to remove a string the names contract requires.
- **Item:** 5
- **How to reverse:** Not needed.

## D8
- **Decision:** The Desk Look hex fields now save on commit (Return or focus loss) instead of on every keystroke, so a half-typed value never reaches the desktop; three-digit shorthand ("fff") is refused when retyped, though an existing saved one still renders.
- **Why:** Validation on commit is what keeps a bad value off the desktop (catalog #18); live-as-you-type was the thing that painted it gray. The preset picker still applies at once.
- **Alternative not taken:** Validate live and keep saving every keystroke that happens to be valid.
- **Item:** 9
- **How to reverse:** Save the draft on `onChange` when `DeskHex.problem` is nil (commit fb02da3).

## D9
- **Decision:** Item 13 shipped the caption half only. The scan's premise for its first half (Glow and Camera-and-mic switches "do nothing" with the island off) was wrong: Glow glows the plain hardware notch (`NotchGlowTests.swift:233`, smoke §5 item 27) and the indicators show in a small tab (`AVIndicatorTests.swift:226`, smoke §24 step 9). Disabling them would have regressed pinned behaviour.
- **Why:** Regression-aware posture; the worker checked the claim against the tests before acting (doctrine: verify the scare).
- **Alternative not taken:** Disabling the groups as the catalog said.
- **Item:** 13
- **How to reverse:** Not needed; the catalog row #33 was half wrong.
