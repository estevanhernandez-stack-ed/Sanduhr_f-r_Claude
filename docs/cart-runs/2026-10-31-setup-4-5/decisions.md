<!-- The ledger. Every inference, default, substitution and mid-run class
     upgrade lands here, numbered D1, D2, and so on. Append only. -->

# Decisions: setup-4-5

## D1
- **Decision:** Every item is `go`, including item 4 (sign-in turns usage on) and item 6 (setup turns drawers off).
- **Why:** Item 4 changes what follows an added account but adds no auth, gating or permission surface: sign-in itself is untouched. Item 6 moves files only through the existing `DrawerService.TurnOff`, behind its existing confirmation. Every later item depends on 4 and 6, and a `hold` on either would hold the rest of the run.
- **Alternative not taken:** `hold` on item 4 as an auth-adjacent change.
- **Item:** 4, 6
- **How to reverse:** Review items 4 and 6's commits on `cart/setup-4-5` before pushing; revert either merge if the change is unwanted.

## D2
- **Decision:** Each item gets a fresh implementer (bulk tier) and then a fresh reviewer (judgment tier, the session model) before it merges. A reviewer's Critical or Important finding goes back to the implementer once; the item merges only when the reviewer clears it and its check passes. The run closes with one whole-branch review.
- **Why:** The owner chose subagent-driven execution under Cart. Cart's engine checks each report against its done-when lines; the per-item reviewer is added on top, as asked.
- **Alternative not taken:** Cart's report check alone.
- **Item:** all
- **How to reverse:** None needed; it adds review only.

## D3
- **Decision:** The base is `HEAD` of `docs/setup-spec` (`52a97067`), 0 behind `origin/main`.
- **Why:** The branch carries the spec and plan commits and nothing is behind.
- **Alternative not taken:** `--merge origin/main` (nothing to merge).
- **Item:** run
- **How to reverse:** None needed.

## D4
- **Decision:** Item 12 runs only the full suite; plan Task 12 Step 2 (the live check) is left for the owner.
- **Why:** The live check needs the owner at the PC: a first run on a dev profile, a sign-in, a chat paste.
- **Alternative not taken:** A scripted stand-in for the live check.
- **Item:** 12
- **How to reverse:** The owner runs the four live steps from the report.

## D5
- **Decision:** Item 1 merged on the reviewer's APPROVE, with its five Minor findings deferred to the whole-branch review.
- **Why:** None block. The ones worth doing are test rows that pin rules the code already gets right: account + Just the Desk + silenced, Just the Desk not silenced, `DeskOn` on account rows, an unreadable `usage.claude` with a true fallback, and `WidgetStartsHidden(null, …)`. One is a forward-compat note: `SetupLaunch.WidgetStartsHidden` reads any source (`UsageOn`) while the widget reads Claude only, which only matters once spec 2 adds a source.
- **Alternative not taken:** Sending item 1 back for the test rows now.
- **Item:** 1
- **How to reverse:** Add the rows in the whole-branch fix pass (planned).

## D6
- **Decision:** Item 2's reviewer Minor 1 was fixed before merge (bc66785c): `MigrateSetup` writes the Desk and reminder first and the `setup` marker last. Minors 2-4 are deferred to the whole-branch review: `LoadSetup`'s fallback assumes an account, `MigrateSetup` returns true under `ResetGuard.Done` though nothing was written, and two store-level test rows are missing.
- **Why:** `Write` swallows an IOException, and `setup` is the "already ran" marker, so writing it first could leave a Just the Desk user with the Desk off and no second try. `ChooseJustTheDesk` already wrote its marker last for the same reason.
- **Alternative not taken:** Deferring the reorder too.
- **Item:** 2
- **How to reverse:** Revert bc66785c.

## D7
- **Decision:** Item 4 went back to its implementer for four Important findings. (1) Usage off still fetched through SeatMeters and AccountFollower. (2) Stale tiers stayed on the Desk and tray after usage turned off. (3) A pre-2.2.0 legacy-key install looked like a first run. (4) Signing in before any setup left the widget hidden. Two choices were made on the reviewer's open questions: the usage snapshot (`snapshot.json`, read by the MCP and the statusline) is deleted when usage turns off, and a sign-in with no setup yet shows the widget without saving a setup, so the setup screen stays pending and the next launch's migration records usage on.
- **Why:** The spec says usage off means "fetching stops". A stale reading served as current misleads the MCP and the statusline. Saving a setup on sign-in would skip the first-run screen for someone who never saw it.
- **Alternative not taken:** Saving `WithClaude(true)` on a no-setup sign-in. Keeping the snapshot.
- **Item:** 4
- **How to reverse:** Change `UsageOnForAccount` to save `WithClaude(true)`. Drop the `Delete()` call.

## D8
- **Decision:** Item 4 merged after its re-review approved 6b7c4f99. Three new Minors are deferred to the whole-branch review: "Switching account…" stays up when usage is off, `UsageOnForAccount` should call `ShowWindow()` before reading the setup, and the account chip is blank while usage is off.
- **Why:** None blocks. Each shows only with usage off and the widget visible.
- **Alternative not taken:** A third round on item 4.
- **Item:** 4
- **How to reverse:** Fix them in the whole-branch pass.

## D9
- **Decision:** Item 5 went back for one Important finding: a drawers review with desktop off drew the whole desktop. The plan had prescribed `DrawsDesktop = _desktopShown || _preview is not null`, which ignores what kind of preview is open. `ShowPreview` now takes `full`: a design review is full, a widget review is full (it previews a look the user asked to see), and a drawers review is not. Item 5 also ends an Arrange session already open when desktop goes off, and gives the Arrange note a reason line. Two items are deferred to the whole-branch list: a review-bar line when a design is applied with desktop off, and a `desktop` flag in desk-state.json for `get_desk`.
- **Why:** The first-run path itself (drawers ticked, desktop unticked, first rule plan) would have shown the clock and weather during the review.
- **Alternative not taken:** A widget review that filters to drawers plus the companion.
- **Item:** 5
- **How to reverse:** Pass `full: false` from WidgetReviewService if a widget review should honour desktop off.

## D10
- **Decision:** Item 6's deviations are accepted. The first drawers review opens only when drawers are newly turned on, `FirstOnLine` is shown on turn-on, and `ReloadDesk` reloads the Desk only. Two Minors are fixed before merge: the setup-specific `SetupCopy.SaveFailed` line with a Desk reload after a turn-off, and refusing turn-off when there is no DrawerService. Two Minors are left: Task 7 must refresh the Settings Desk page after Apply (`_settingsWindow?.ViewModel.Desk.Reload()`) and call Apply on the UI thread, and AppSetupHost has no tests against a real DrawerService (whole-branch pass).
- **Why:** The plan's rule would have reopened a review on every Continue for a user with drawers already on.
- **Alternative not taken:** The plan's "whenever ticked and not unlocked" rule.
- **Item:** 6
- **How to reverse:** Key the review on `now.Drawers && !host.DrawersEverSorted`.

## D11
- **Decision:** Item 7 went back with one Important finding: AfterSetup ran before the setup window closed, so the modal notice sat on a disabled setup window. Also decided: (a) `FirstOnLine` is filtered out of the post-setup notice, so a default first run ends without a dialog. (b) The Desk-only tour's last card gets a title that matches its body and a Show me that opens Set up Sanduhr. (c) The tour also waits while the drawers result bar is up, and the wait timer stops on quit. (d) A first run whose user signed in while setup was open ticks usage at Continue. Item 7 keeps three choices of its own: `SetupPending` holds the Desk's sign-in line in memory only while no setup or account exists; `firstRun |= LoadSetup() is null` on a reopen; and a failed save keeps the window open with the line.
- **Why:** The default first run is the success path, and an extra modal, a tour on top of the result bar, or usage switched off on a new account all hurt it.
- **Alternative not taken:** Showing every result line in a dialog.
- **Item:** 7
- **How to reverse:** Remove the `FirstOnLine` filter in AfterSetup.

## D12
- **Decision:** Item 8 merged after a tooltip fix: the setup card's Show me now says "Opens Set up Sanduhr", through a shared `SettingsIndex.OpensSetup`. Release note: the 4.5.0 card shows only after the csproj version trio and the manifest are bumped to 4.5.0, a release-runbook step no item in this run takes.
- **Why:** The card is for 4.5.0, and the version bump belongs to the release.
- **Alternative not taken:** Bumping the version in this run.
- **Item:** 8
- **How to reverse:** None needed.

## D13
- **Decision:** Item 9 went back with one Critical and two Important findings. Critical: a duplicate JSON key crashed the reader, which in the App is an unhandled exception in the live paste check. Important: the reader took the first candidate block instead of the first good one, and distinct drawer names could collapse to one id (two non-ASCII names both became `drawer`). The reviewer's Minors folded in: tell the chat to leave drawers out to keep the user's rules (an empty rules list is still refused, so a chat can't wipe rules it can't see), refuse every invalid file-name character in rule text, and accept trailing commas and comments. Item 9's prompt choices were accepted: `meetings` left out (Windows doesn't draw it), launcher buttons, looks and theme not taught, `ink: auto` preferred.
- **Why:** A crash on paste and a mis-sorted drawer are both user-visible failures on the AI chat path.
- **Alternative not taken:** Treating an empty rules list as "clear my rules".
- **Item:** 9
- **How to reverse:** None needed.

## D14
- **Decision:** Item 9 merged after its re-review approved 7ba8777d. Two Minors are deferred to the whole-branch pass: a truncated block (no closing brace) still gets "no Sanduhr block found" instead of "did not parse", and the duplicate-key reason should also mention "a field given twice".
- **Why:** Both are reason wording, not wrong behaviour.
- **Alternative not taken:** A third round on item 9.
- **Item:** 9
- **How to reverse:** Fix them in the whole-branch pass.

## D15
- **Decision:** Item 10's pasted desk designs are limited to the fields the prompt teaches (`SetupPrompt.DeskFields`). Theme, looks, Launcher buttons and follows_theme are refused in the paste path only; the MCP is unchanged. Item 10 went back for two Important findings. (1) A chat's drawer names didn't map to an existing drawer, and rules were saved before the plan was checked, so a renamed drawer turned every later sort into a refusal with the old rules already gone. Now names resolve to existing drawers, the plan is checked before anything is written, and the write can't be partial. (2) An unnamed pasted design was saved as "Claude's design". The fallback is now "Your AI chat's design". Its own deviations were accepted: chat drawer names are kept for new drawers via `SortNow(names)`, `meetings` is refused in pastes, and any problem in either part disables both Preview and Use rules.
- **Why:** The spec says arrangement only, and the prompt tells the chat anything else is refused. Losing the user's rules to a refusal is the worst outcome on the chat path.
- **Alternative not taken:** Letting pastes carry every field an MCP design can.
- **Item:** 10
- **How to reverse:** Drop the DeskFields check in the paste validation.

## D16
- **Decision:** Item 10 merged after ddef06e6. A chat drawer name now matches only an owned drawer, never a view of an existing folder, and homes are compared normalized. Deferred: M6 (when the sort review is dismissed, the chat's drawer names are lost; a later Sort now names those drawers from their ids) and M9 (the chat window's status goes stale if an MCP proposal replaces the paste review while direct apply is on).
- **Why:** A chat can't see the user's drawer names. A name match onto a view such as Documents would move Desktop files into the user's real folder by coincidence.
- **Alternative not taken:** Matching views by name, as the Settings add-rule does.
- **Item:** 10
- **How to reverse:** Drop the `Owned` filter in `ResolveDrawers`.

## D17
- **Decision:** Item 11 (harness-only code, no app change) merged on the orchestrator's own review of the six PNGs instead of a separate reviewer agent. They were legible in obsidian and aurora, nothing clipped, the first-run defaults correct, the reopened window showing the usage-off line, and the chat window showing one finding by field with Preview disabled. The PNGs stay untracked in the scratch folder.
- **Why:** The item changes no shipped behaviour, and a screenshot review is a look at the images.
- **Alternative not taken:** A reviewer agent for the scenario code.
- **Item:** 11
- **How to reverse:** None needed.

## D18
- **Decision:** The whole-branch review returned FIX FIRST: three Important seams (I1 an upgrader with an account and the Desk off migrates to desktop off, so turning the Desk on later draws nothing, against "nothing changes"; I2 the widget is blank with usage off; I3 the setup screen names Claude Code with the trademark line collapsed). An item 13 was added for them plus M1 (doc comment misplaced), M2 (the tour card's Show me should open Set up Sanduhr), M4 (a tray click while setup is pending opens Set up Sanduhr instead of the first-run card), M6 (the usage-off line only with an account), the D9 bar line (a design applied with desktop off says so), and the D5 test rows. Left for the owner: M3 (the tour is keyed on usage only, with no drawers card) and M5 (a tour lost after Not now plus a sign-in).
- **Why:** I1 breaks the spec's upgrade promise for the most common upgrader. I2 and I3 are visible on a Store review path.
- **Alternative not taken:** Shipping with them in the report.
- **Item:** 13
- **How to reverse:** Revert item 13's merge.

## D19
- **Decision:** The vibe-test and vibe-sec close-out gates were not run. The repo's own full Release suites on the integration tip stand in: Core 4960, App 1323 and Mcp 300, all passing. Item 13 also changed the tray-click rule after the branch re-review: setup opens only while no setup exists and no account is signed in.
- **Why:** Those gates have exited 2 on win32 before (the environmental-blocker fallback). This repo's CI is its own `dotnet test` suites. The tray-click change was the reviewer's own one-line recommendation.
- **Alternative not taken:** Running the gates and logging an exit 2.
- **Item:** close-out, 13
- **How to reverse:** Run `/vibe-test:gate` and `/vibe-sec:gate` on `cart/setup-4-5` before the PR.

## D20
- **Decision:** After the run, the orchestrator drove the integration build through UI Automation on dev profiles and fixed three bugs it found (cf9d4e8f on cart/setup-4-5): the archive consent waits for a setup with usage on, there is no "Connecting…" line with usage off, and the change lines are one readable TextBlock. A no-account first run could not be tested on this PC, because Credential Manager is machine-wide.
- **Why:** These are live-only findings that no unit test covered.
- **Alternative not taken:** Leaving them for the owner's live check.
- **Item:** post-run
- **How to reverse:** Revert cf9d4e8f.
