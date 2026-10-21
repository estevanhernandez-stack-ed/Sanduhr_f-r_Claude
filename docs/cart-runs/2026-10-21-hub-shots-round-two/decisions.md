<!-- The ledger. Append only. -->

# Decisions: hub-shots-round-two

## D1
- **Decision:** #290 is not an item; the old Mac's session checks it on 2.31.0, since the preview has drawn the pill since 2.30.0.
- **Why:** The fix likely shipped already; only a notchless screen can confirm it.
- **Alternative not taken:** A speculative code change here.
- **Item:** run
- **How to reverse:** Add an item if the check fails.

## D2
- **Decision:** #291 moves the session-start card above the tabs instead of making the window taller.
- **Why:** The card describes the session, not the catalog; a taller default changes every page and still cuts wherever the content ends.
- **Alternative not taken:** `SettingsPreviewFold.defaultWindowHeight` raised.
- **Item:** 1
- **How to reverse:** Move the panel back into the Catalog content.

## D3
- **Decision:** "Compare on import" is the import sheet with a checkbox per difference, opened over an invented pack by a new hook.
- **Why:** The Compare tab is a different view; the import sheet needs a pack, and a real one would carry real names.
- **Alternative not taken:** Capturing an import of a real pack.
- **Item:** 3
- **How to reverse:** Drop the `import-sample` word.

## D4
- **Decision:** The screenshots are taken by the orchestrator from the integration build at close.
- **Why:** A worker in a worktree cannot drive the screen, and the shots should show item 1's layout.
- **Alternative not taken:** Shots from the 2.31.0 release app, which has neither the new layout nor the hooks.
- **Item:** 4
- **How to reverse:** Not needed.

## D5
- **Decision:** Cart's session, friction and wins logs are not written; commits carry the session attribution lines.
- **Why:** The plugin's data path resolves to the work seat on this Mac (previous runs).
- **Alternative not taken:** Writing to the work seat.
- **Item:** run
- **How to reverse:** Point the plugin's data path at the personal seat.

## D6
- **Decision:** Items 1 and 2 build in parallel worktrees and merge in list order, against the reference's one-at-a-time rule.
- **Why:** They touch disjoint files (the Mods views against the two walkers), each cuts from the same integration tip, and merging in order keeps the history the rule protects.
- **Alternative not taken:** Strictly serial building.
- **Item:** 1, 2
- **How to reverse:** Not needed; item 2 rebuilds on the tip if its merge conflicts.

## D7
- **Decision:** #290 becomes item 5: on the old Mac, 2.31.0's Notch preview passes with the pill on and still draws the empty bar with the pill off; the fix draws the menu bar strip only when the pill or the camera tab sits on it, and the caption names the switch that brings the pill.
- **Why:** The old Mac's session checked both states on real hardware (branch `shots/old-mac-290`); D1 assumed 2.30.0 had covered it, and only half was.
- **Alternative not taken:** A placeholder pill drawn while the switch is off, which would show something the screen does not.
- **Item:** 5
- **How to reverse:** Draw the strip unconditionally again.

## D8
- **Decision:** Item 2's test pins the byte order with a decomposed "e" plus combining acute against "f"; `subdirectories(of:)` also sorts by bytes now.
- **Why:** The list's precomposed pair was wrong: Swift `String <` compares canonically normalized scalars, so a precomposed "é" sorts after "f" both ways; the decomposed form normalizes differently from its bytes, which is the case the change exists for. Caught by the worker's first failing test.
- **Alternative not taken:** The list's pair.
- **Item:** 2
- **How to reverse:** Not needed.

## D9
- **Decision:** The import sample is guarded by an invisible `sample` flag on `ConfigImportState`: `finishConfigImport` returns before any write and `plan()` leaves the invented rows alone; the Import button and its confirm stay as they look in the real sheet.
- **Why:** The shot must look like the real sheet, and nothing opened from a debug hook may write to a real Claude Code folder.
- **Alternative not taken:** A pack fixture under `mac/smoke/fixtures/` run through the real import, which would plan against a real folder.
- **Item:** 3
- **How to reverse:** Remove the flag and the hook word.

## D10
- **Decision:** The Export Config sheet's rows come from the shown seat, so its shot is taken in demo mode and read by eye before saving.
- **Why:** A fully invented export would need a fake scan, more than the item asked for.
- **Alternative not taken:** A fake seat scan.
- **Item:** 3, 4
- **How to reverse:** Add a sample scan if the shot ever shows real names.

## D11
- **Decision:** Item 4's first capture pass kept nothing, and item 6 is added: an invented export sample, an import sample whose Into list is an invented folder only, and a demo Accounts page that draws the real layout over sample accounts.
- **Why:** Read by eye before saving: Export Config showed this Mac's real seat (agent and skill names, paths flagged as secrets); the import sample's Into picker showed the real folder, a git-clone note and a real backup seat behind the sheet; demo Accounts drops the names and the Follow switch #292 asks for. The repo's rule is made-up data only in screenshots; D10 named the export risk and it held.
- **Alternative not taken:** Cropping the real-data parts out of the shots.
- **Item:** 4, 6
- **How to reverse:** Drop the sample words; take the shots on a scratch user instead.

## D12
- **Decision:** Item 4 lands only `settings-accounts.png`; the export shot taken here is reverted to the one already on main, and the import shot is dropped.
- **Why:** Another session captured Export Config and Compare on import from throwaway sample folders on 2026-10-20 (0fac5a0, `settings-export-config.png` and `settings-compare-on-import.png`), and they are live on the hub; #292's last comment leaves only the Accounts pane, which needed item 6's demo layout. The run's first commit overwrote the live export shot by name; caught before the PR.
- **Alternative not taken:** Replacing the live pair with this run's cropped captures.
- **Item:** 4
- **How to reverse:** Re-take from `mods-config export-sample` and `import-sample`; the hooks stay for that.
