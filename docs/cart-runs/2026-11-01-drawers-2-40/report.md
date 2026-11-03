# Run Report: drawers-2-40

8 merged, 1 held, 0 parked, 0 stopped. Update 2026-11-02 23:33: the owner walked item 8 ("everything read well") and it is merged into the integration branch (`1b185dce`), with the walk fix D17 (`f2781cfc`). The combined branch passes 2925 tests in 327 suites and 1048 selftest checks.

## Ready to push
- **Integration branch:** `cart/drawers-2-40`, based on `e0402e7b`: the spec branch `docs/mac-2-40-spec` (PR #413) with `origin/main` merged in locally (D1).
- **Commits** (each item merged locally):
  - 1 `cfb0b344`: rules, plans and free names (pure)
  - 2 `511e673f`: journal, Undo and the return (pure)
  - 3 `cc1d65b8`: the mover, the journal store and the file-safety suite
  - 4 `cb8943c9`: the drawer service, its settings and Reset
  - 5 `524142cb`: the drawer panels, looks, placement and review bars
  - 6 `2094c066`: Settings, Drawers
  - 7 `24e19a17` + `f9939734`: state.yaml `drawers` and the smoke scenario, plus my guard on the debug action (D15)
  - 9 `eb37192e`: PRIVACY, README, smoke doc, the 2.40.0 What's New card and CHANGELOG
- **Checks:**
  - **Per item:** `cd mac && ./test.sh`, from item 6 on also `ruby smoke/selftest.rb`. Every item passed on its first check, and I re-ran each at merge.
  - **On the integration branch at close:** 2913 tests in 326 suites pass, and the selftest passes 1048 checks.
  - **Also re-checked by me at merge:**
    - Item 3's file calls: every move goes through `renamex_np(RENAME_EXCL)`. The only removals are `rmdir` and a lone `.DS_Store` in `removeIfEmpty`.
    - Item 4's launch path and its single `removeItem`, which removes only the journal on Reset.
    - Item 5's rename and trash guards.
    - Item 6's custom-drawer creation.
    - Item 8's Info.plist diff, which adds 2 lines.
  - **Close-out gates:** vibe-test and vibe-sec were not run, since their gates are Node-project oriented. The repo's own suite and selftest stand in for them.
- **Not checked by anyone yet: the live app.** Drawing, the looks, drag in and out, Quick Look from the panel, the review and result bars, and the smoke scenario (`mac/smoke/scenarios/drawers.yaml`, which needs a debug build with a stand-in Desktop). `docs/mac-smoke-test.md` section 73 is the walkthrough.

**The push needs one hand step: the CHANGELOG** (D4). Main renamed `## Unreleased (mac)` to `## v2.39.2-mac — 2026-11-02` (#417). A plain merge of main is clean, but it leaves the Drawers bullet under the 2.39.2 heading. Fix it while merging: add a fresh `## Unreleased (mac)` above `## v2.39.2-mac` and move the Drawers bullet under it.

To push, after #413 merges (or with it, since this branch contains it):
```bash
cd ~/.cwt/sanduhr-2-40-spe-drawers-2-40/int
git merge origin/main        # then fix the CHANGELOG heading as above, test, commit
git push -u origin cart/drawers-2-40
gh pr create --base main --head cart/drawers-2-40 --title "feat(mac): Drawers (2.40.0)"
```
Pushing waits for your say-so. Say "push it", or name a subset. Item 8 is already in the branch.

## Waiting on you
- **Item 08: the Desktop Folder permission** (`cart/drawers-2-40--08-desktop-permission`; merges cleanly onto the integration branch)
  - **Look at:**
    - `mac/Info.plist`: one new key, `NSDesktopFolderUsageDescription`, with the text "Sanduhr looks at the files on your Desktop to sort them into drawers, only when you ask it to."
    - The denied state: an EPERM or EACCES read gives "Sanduhr can't see your Desktop." with **Open Files and Folders…** in Settings, Drawers, and the same line on an empty drawer panel. The state is kept in memory only.
  - **Live walk:**
    1. Switch Drawers on. Turn-on probes the Desktop's names, so the macOS prompt appears then (D16). Check its text.
    2. Deny once and see the row and the line.
    3. Allow it in Files and Folders, then press Sort.
  - **Held because:** it touches a permission. It is the only item that changes Info.plist.
  - **Docs already describe it.** PRIVACY carries an `<!-- item 8 (held) -->` comment on that sentence, and the README, CHANGELOG and smoke steps 1 and 7 assume it. If you drop item 8, those lines change too.

## Parked
None.

## Stopped
None.

## Decisions to skim
Full ledger: `decisions.md` (D1 to D16). Highest stakes:
- D8: the mover's safety rules.
  - The journal is written before any move. The inode is recorded before each move, for crash recovery.
  - There is no copy fallback on `EXDEV` or `ENOTSUP`.
  - A view drawer is never filled.
  - If the journal can't be written, nothing moves.
- D15: I guarded item 7's `drawers` debug action. Smoke hooks answer in release builds, which ignore the stand-in Desktop override, so the action would have acted on real Desktop files. It now runs only in a debug build with `SANDUHR_DRAWERS_DESKTOP` set.
- D16: the macOS prompt comes at turn-on, not only at Sort.
- D6: the journal keeps Windows' `written` key (false, an ISO date or true), so the file stays readable by Windows.
- D14: custom rules create their named drawer under the root on the first move.
- D12 and D13: follow-ups.
  - White panel text may be hard to read on a light wallpaper with Outline or Bare (live check).
  - Solid uses the default theme's background, not the chosen theme.
- D10: item 4's first worker died when the Mac slept and the session restarted. A fresh worker finished it from the partial files.
- D9: ten leftover `sanduhr-drawers-test-*` folders sit in the OS temp folder, from an aborted test run. They hold no user data. The safety check refused their removal, so they're left for you or for macOS.

## Left untouched
- **Your tree:** nothing in the builder branch (`docs/mac-2-40-spec`) was uncommitted at run start. Its only local change is the merge of `origin/main` (D1), not pushed.
- **This run folder** (`docs/cart-runs/2026-11-01-drawers-2-40/`) is untracked in the spec worktree. Commit it with the push if you want it in the repo, as earlier runs were.
- **Worktrees kept:** `~/.cwt/sanduhr-2-40-spe-drawers-2-40/int` (integration) and `.../08` (the held item). The other item worktrees were removed when they merged.
