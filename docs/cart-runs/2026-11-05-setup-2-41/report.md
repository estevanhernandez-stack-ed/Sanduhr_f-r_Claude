# Run Report: setup-2-41

10 merged, 0 held, 0 parked, 0 stopped. Built 2026-11-05 between 08:15 and 10:05. The integration branch passes 3052 tests in 337 suites and 1063 selftest checks.

**Built, not shipped.** The owner's call 1 holds 2.41.0 until 2.40.0 (Drawers) has run on real Desktops for a few days (D4).

## Ready to push
- **Integration branch:** `cart/setup-2-41`, based on `main` at `d1686977`. Worktree: `~/.cwt/sanduhr-f-r-clau-setup-2-41/int`.
- **Commits** (each item merged locally):
  - 1 `2b22ff5e`: the stored setup, its migration and the launch rules
  - 2 `7756df52`: usage off with the account kept
  - 3 `6f150b30`: A nicer desktop off leaves only the drawers
  - 4 `172b050f`: the setup service applies a change
  - 5 `76871497`: the Set up Sanduhr window, where it opens, and the 2.41.0 card. It also fixes a bug in item 4 (D9).
  - 6 `553420f3`: the AI chat prompt and the pasted-answer reader
  - 7 `51c12a16`: Design it with an AI chat
  - 8 `721e84d1`: the `state.yaml` `setup` block and `scenarios/setup.yaml`
  - 9 `95dce5df`: PRIVACY, README, smoke section 74 (with 45 rewritten), CHANGELOG, and the setup guide
  - 10 `92c55668`: whole-branch review fixes, from D6, D7, D10 and D12 plus item 9's findings
- **Checks:**
  - **Per item:** `cd mac && ./test.sh`, and from item 5 on also `ruby smoke/selftest.rb`. Every item passed its first check, and I re-ran each check at merge.
  - **Re-checked by me at merge:**
    - Item 2's diff around sign-out, launch and account switch. With usage off, no credential is deleted and nothing is fetched or written.
    - Item 4's order against Windows.
    - Item 5's fix to the usage switch.
    - Item 7's pasted-review guards: no result file and no notification.
    - Every `ClaudeAPI` caller is gated on usage on: the fetch, the follower, the seat meters and the folder lookup.
  - **Close-out gates:** vibe-test and vibe-sec were not run, because their gates are Node-oriented. The repo's own suite and selftest stand in for them, as on the Drawers run.
- **Not checked by anyone yet: the live app.** Everything below is tested only in unit tests and the selftest:
  - the window;
  - first run on a clean Mac;
  - the upgrade paths;
  - the menu bar with usage off;
  - the AI chat window;
  - the Desk with desktop off.

  `docs/mac-smoke-test.md` section 74 is the walk. A dev build runs signed out, so the "upgrade with an account" case needs care. It is safest to check it on the release build after it ships.

To push (when you say so):
```bash
cd ~/.cwt/sanduhr-f-r-clau-setup-2-41/int
git push -u origin cart/setup-2-41
gh pr create --base main --head cart/setup-2-41 --title "feat(mac): Set up Sanduhr (2.41.0)"
```
Commit this run folder with the push, as earlier runs were.

## Waiting on you
- **The live walk** (smoke section 74), on a dev build with your settings backed up.
- **Two calls:**
  1. **Per-seat snapshots.** Usage off leaves the per-seat `snapshot.<account_ref>.json` files in place; they just stop refreshing. PRIVACY says so. Delete them too?
  2. **Ship timing.** When has Drawers soaked long enough?

## Parked
None.

## Stopped
None.

## Decisions to skim
The full ledger is in `decisions.md` (D1 to D12). The highest stakes:
- **D1:** no `hold` items. Usage off stops fetching and deletes `snapshot.json`, but never touches the Keychain.
- **D6 and item 10:**
  - an upgrader with an account can never come out usage-off;
  - a sign-in, an added account or a saved key turns usage on;
  - a Save that only clears the key does not.
- **D9:** the usage switch bug that item 5 caught in item 4. A fake host that didn't read storage hid it.
- **D11:** the chat answer's vocabulary works on both platforms (`shortcuts` maps to `links`). The prompt takes no state.
- **D12:** a pasted design rides `propose_desk`'s review with a neutral heading, in memory only.
- **Item 9's finding:** PRIVACY is written to what the code does, not to the brief. Item 10 then made the stronger claim true: with usage off, nothing goes to claude.ai unless you start a sign-in.

## Left untouched
- **Your tree:** `main`, clean at run start. The run folder (`docs/cart-runs/2026-11-05-setup-2-41/`) is untracked there.
- **Worktrees kept:** the integration worktree only. The item worktrees were removed as each merged.

## Live walk (2026-11-05, after the run)
Smoke section 74 on a dev build, with settings backed up and restored identical:
- **Passed:** steps 1, 3, 6, 7, 8 and 9. Step 9's What's New card needed the bundle stamped 2.41.0.
- **Not walkable in a dev build:** steps 2, 4 and 5 need a saved key. Check them on the release build after ship.
- **Fixed from the walk, in one commit:**
  - Settings, Desk says the pieces are hidden while A nicer desktop is off, offers Set up Sanduhr…, and dims its preview.
  - Use these drawer rules keeps "Saved N drawer rules into …" under its button, with a link to Settings, Drawers. The shared status line had been overwriting it.
  - Smoke step 9 notes the card's version.
