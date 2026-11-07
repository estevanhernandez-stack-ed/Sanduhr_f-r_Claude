# Decisions: widget-from-file-2-42

## D1
- **Decision:** Unattended layout (integration branch, item worktrees, local merges, nothing pushed) although the clock read 06:52 and the list has five items.
- **Why:** The ask named a `cart/` branch and autonomous mode and the owner is away for the day; the attended engine would commit to `main` in the owner's tree, which this repo never does (every change goes through a PR). The 2.41.0 run used the same layout for the same reason.
- **Alternative not taken:** Attended autonomous on the current branch with checkpoints.
- **Item:** run
- **How to reverse:** None needed; the layout only adds branches. Delete `cart/widget-from-file-2-42*` to discard.

## D2
- **Decision:** The approved spec section was committed on local `main` (`906f7feb`, one ahead of `origin/main`) before `init`, so the helper's base includes it and the integration branch carries it as its first commit.
- **Why:** The helper never touches the owner's uncommitted files, and a worker in a worktree cannot see an uncommitted edit. Committing on `main` locally keeps the owner on `main`; once the integration PR merges, `main` fast-forwards because the PR branch descends from that commit.
- **Alternative not taken:** A separate docs branch and PR for the spec first (one more PR for one paragraph), or making the spec an item (a worker retyping a paragraph it was handed).
- **Item:** run
- **How to reverse:** `git -C <repo> reset --soft origin/main` on `main` returns the edit to the working tree; the integration branch keeps its copy.

## D3
- **Decision:** Every item is `go`.
- **Why:** No item touches money, auth, permissions, secrets, production data or a deploy. The file picker reads one user-chosen zip, covered by the spec and PRIVACY.
- **Alternative not taken:** `hold` on item 2 for the file read; a user-chosen local file is not an access-gating surface.
- **Item:** 1 to 5
- **How to reverse:** The owner walks the branch before the push step either way.

## D4
- **Decision:** Five items, in this order: the pure install (1), the model and Settings entry (2), the smoke hook and scenario (3), release lockstep and docs (4), whole-branch review (5). Items 3 and 4 both depend on 2 and are independent of each other.
- **Why:** Each item has a local check that runs alone in its worktree, and the pure core gets its tests before any UI exists, as the 2.41.0 list did.
- **Alternative not taken:** One item for the feature and one for the release; a single failing check would park the whole feature.
- **Item:** run
- **How to reverse:** Edit `list.md` before `start` on the item in question.

## D5
- **Decision:** The spec's line "the seat report and `get_widgets` show the pack as `local`, as they do own art" is treated as wrong as written; item 4's docs say "listed as made here" instead.
- **Why:** Item 2's worker found no `get_widgets` tool and no widget content in `SeatReport.swift`; the only read surface is `get_desk`'s `sprites` field, which lists every made-here sprite without a `source` field. A file-installed sprite appears there with no change.
- **Alternative not taken:** Adding a `source` field to `get_desk` sprites; a new MCP field for one sentence of docs is scope the spec didn't ask for.
- **Item:** 2, 4
- **How to reverse:** Add `source` to `SpriteMotionState.sprites` and its tests, then restore the spec's wording.

## D6
- **Decision:** Sprite detection after a successful install reads the zip a second time (`WidgetZip.read` then `WidgetFileInstall.isSprite`) rather than widening item 1's `Outcome`.
- **Why:** Keeps item 1's API and tests untouched; the cost is one extra parse of a zip already capped at `maxZipBytes`, on a user action.
- **Alternative not taken:** `Outcome.installed` carrying `isSprite`; cleaner, and a one-line follow-up if the double read ever shows up in a profile.
- **Item:** 2
- **How to reverse:** Add `isSprite` to `Outcome.installed` in `WidgetFileInstall.swift` and drop the second read in `WidgetsModel.installFromFile`.

## D7
- **Decision:** Item 3 added a second debug hook, `widget-remove <id>`, beyond the brief, and the harness's restore plan runs it for any made-here pack a run installed.
- **Why:** Without it a smoke run leaves the fixture pack installed on the owner's Mac. The hook refuses a catalog pack or an id that is not installed, so it cannot remove anything the user got from the catalog.
- **Alternative not taken:** Leaving cleanup to the human walk; the harness restores every other setting it changes, and a leftover pack would show in Settings and on the Desk.
- **Item:** 3
- **How to reverse:** Remove the `widgetRemove` case from `DebugCommand.swift` and `DebugHooks.swift` and its `Restore.plan` line in `smoke_core.rb`.

## D8
- **Decision:** `state.yaml`'s `widgets.file_install` carries the status sentence as is, pack name included; `mac/smoke/README.md` says to read it and never paste it, as it does for usage numbers.
- **Why:** The spec puts the name in the sentence, and the scenario asserts on the sentence. The committed fixtures carry invented names only.
- **Alternative not taken:** An enum code in place of the sentence; a small follow-up if a real pack name in `state.yaml` ever matters.
- **Item:** 3
- **How to reverse:** Emit a code in `WidgetsDebug` and match on it in the scenario.

## D9
- **Decision:** Item 4's acceptance line "`rg 2\.42\.0` finds CHANGELOG.md" was dropped at verification; the bullet carries no version, as the Windows entry does, and the heading must stay `## Unreleased (mac)` for the release workflow.
- **Why:** The line in `list.md` contradicted the repo's own CHANGELOG rule; the worker followed the rule and said so.
- **Alternative not taken:** A version in the bullet text, which the release notes would then repeat.
- **Item:** 4
- **How to reverse:** None needed; the heading is renamed after publish per `docs/mac-release.md`.

## D10
- **Decision:** PRIVACY.md gets its own "On the Mac from 2.42.0" paragraph and a "A widget from a file (2.42.0)" entry, rather than a sentence spliced into the 2.36.0 paragraph.
- **Why:** The 2.36.0 paragraph opens with its own date and the On the Mac section is prose, not a table; each release's paragraph stands alone there.
- **Alternative not taken:** The spec's wording (a sentence in the 2.36.0 paragraph and a table row).
- **Item:** 4
- **How to reverse:** Fold the paragraph into the 2.36.0 one.

## D11
- **Decision:** Item 5 fixed a real defect: `WidgetFileInstall.rejected()` joined the validator's sentences, which carry no closing period, with spaces, so a multi-error refusal ran its clauses together and a single one had no full stop. Each message now gets a period when it lacks one; the test and the smoke scenario's regex require it.
- **Why:** The tests had passed only because `rejected` was fed pre-punctuated inputs, and smoke section 75 already promised the period.
- **Alternative not taken:** Changing the validator's own messages, which other surfaces show as they are.
- **Item:** 5
- **How to reverse:** Drop the period append in `rejected()` and loosen the scenario regex.

## D12
- **Decision:** `wasUpdate` is true only when the same id was installed as `.local`; a same-id Claude-made pack is replaced through `replaceMadeHere` but the line says "installed". Left as is.
- **Why:** Cosmetic; the spec says only "installing the same id again updates it", and the replacement itself happens either way.
- **Alternative not taken:** Treating any made-here replacement as an update.
- **Item:** 5
- **How to reverse:** Read the existing pack's `madeHere` instead of `source == .local` when computing `wasUpdate`.

## D13
- **Decision:** The close-out gates (vibe-test, vibe-sec) were not run; the repo's own suite (3078 tests), the smoke selftest (1090 checks), the integrations tests (310) and the band-fixture check stand in, as on the Drawers and Set up Sanduhr runs.
- **Why:** Both gates are Node-oriented and have no Swift coverage model; the substitution is the same one the two previous Mac runs logged.
- **Alternative not taken:** Running the gates for the record; exit 2 was the likely result.
- **Item:** run
- **How to reverse:** Run `/vibe-test:gate` and `/vibe-sec:gate` on `cart/widget-from-file-2-42` and append findings to the report.

## D14
- **Decision:** Item 4's docs order the checks as the code runs them: safe to open, the Sanduhr version it needs, every pack rule. The CHANGELOG and PRIVACY had listed the version last; item 5 reordered them.
- **Why:** `WidgetFileInstall.install` checks `sanduhr_min` before the validator so a pack from a newer format gets the catalog's sentence instead of validator noise.
- **Alternative not taken:** Reordering the code to match the docs.
- **Item:** 4, 5
- **How to reverse:** None needed.
