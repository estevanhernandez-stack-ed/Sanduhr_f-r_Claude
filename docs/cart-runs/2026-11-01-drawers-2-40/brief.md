# Run Brief: drawers-2-40

## Ask
"2.39.2 first, then start the Drawers run", after the owner's calls on the spec: "1 ok. 2 yes 3 4 yes 5 yes".

## Source
- **Kind:** spec
- **Location:** `docs/mac-drawers-setup-spec.md` (PR #413, branch `docs/mac-2-40-spec`). It covers sections 1 to 9 and the 2.40.0 rows of section 13, with the owner's decisions at its end.
- **Windows reference, read only:** 4.5.0 in `windows-dotnet/src/`:
  - `Sanduhr.Core/`: `DrawerRules.cs`, `DrawerPlanValidator.cs`, `DrawerJournal.cs`, `DrawerReturn.cs`, `DrawerReview.cs`, `DrawerSettings.cs`, `DrawerSnap.cs`, `DrawerTidyPlan.cs`;
  - `Sanduhr.App/Desk/Drawers/`.
- **House style:** the Mac's Desk code in `mac/Sources/Sanduhr/Desk/` (`DeskClickLayer`, `DeskController`, `DeskHitTest`, the `*Settings.swift` pages), the review patterns in `Widgets/`, `SettingsIndex`, and `Debug/` for `state.yaml`.

## Intent
Drawers on the Mac Desk, each showing a real folder.
- Rules sort the Desktop into them, but only after a review.
- Every move can be undone, and turning Drawers off puts every file back.
- Sanduhr never overwrites, copies across volumes or deletes a file.
- No file name is ever logged, sent or shown to Claude.

## Done when
- Items 1 to 7 and 9 are merged on `cart/drawers-2-40`.
- Item 8, the Desktop Folder permission, is built and checked on its own branch, and held.
- `cd mac && ./test.sh` and `ruby smoke/selftest.rb` pass on the integration branch.
- The temp-folder suite proves the file-safety invariant: after any sequence, every starting file exists exactly once, at its start path or in a drawer, with its bytes unchanged.

## Out of scope
- The release (Info.plist version, appcast, cask), any push or PR, and anything under `windows-dotnet/`.
- Set up Sanduhr (2.41.0), Keep clear of icons (decided out), a `propose_drawers` tool, and any MCP change.
- **Reading or moving anything in the real `~/Desktop` during the run,** and raising the Desktop Folder prompt. Every test works in a temp folder.
- Live network in tests.

## Inferred
- The run is based on the spec branch with `origin/main` merged in locally (`e0402e7b`). That gives workers the spec and the 2.39.2 lock-line fix; the push step merges #413 first. (from: #413 unmerged at run start)
- Item 8 is `hold` because it touches a permission. Only item 8 adds `NSDesktopFolderUsageDescription` to `mac/Info.plist` and the denied-state UI. Items 4 to 7 take the Desktop's location by injection, so they never need the prompt. (from: stop-class defaults; the 2.39.0 run held its permission item the same way)
- The mover (item 3) is `go`. It moves user files but never deletes, overwrites or copies them, and in this run it touches only temp folders. Destructive data operations (deletes, overwrites) are what item 3 is built to make impossible. (from: spec section 4)
- `SANDUHR_DRAWERS_DESKTOP` (spec section 9) overrides the Desktop's path in debug builds only, for the smoke scenario. Release builds ignore it. (from: spec section 9)

## Mode
- **Mode:** Unattended
- **Triggered by:** The ask chained the run after a release handoff at 22:02, inside the late window. It follows the 2.39.0 run, which the owner ran unattended, and auto mode is on (permission preflight passed).
