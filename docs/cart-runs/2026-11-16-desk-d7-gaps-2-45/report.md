# Run Report: desk-d7-gaps-2-45

6 merged, 0 held, 0 parked, 0 stopped (item 6 added after the live pass, D9).

## Ready to push
- **Integration branch:** `cart/desk-d7-gaps-2-45` (base `ee34fb3d`, `main`), worktree
  `~/.cwt/sanduhr-f-r-clau-desk-d7-gaps-2-45/int`
- **Commits:** 12 (one per item plus six `merge: item NN` commits), nothing in `windows-dotnet/`:
  - `756e474c` feat(mac): Dock clearance and the notch on every Desk window
  - `216e57b8` feat(mac): every copy of a Desk piece on All displays takes clicks
  - `f7f50cbf` feat(mac): slanted Desk stacks take clicks by their shape on every display
  - `d9ea34e0` feat(mac): the Desk companion roams the display it is placed on
  - `3c21e783` chore(mac): 2.45.0, the Desk on every display, finished
  - `bf98fab8` fix(mac): the auto-hiding Dock's slide on every display
- **Checks on the integration branch (2026-11-16, item 6 on 2026-11-17):** `./test.sh` 3284 tests in 367 suites passed after
  item 6 (3271 before); `ruby smoke/selftest.rb` 1090 checks passed; `python3 -m pytest -q integrations/tests` 320 passed,
  210 subtests; `./build.sh --debug` built with item 6; a
  `-warn-long-function-bodies=500` build flagged nothing (incremental). Every item was checked again on its own branch by
  the orchestrator, not only by its worker.
- **Close-out gates:** vibe-test and vibe-sec not run; the repo's own checks above stand in. CI (`swift test` on macos-15,
  docs) runs on the PR.

To push and open the PR:
```bash
git -C ~/.cwt/sanduhr-f-r-clau-desk-d7-gaps-2-45/int push -u origin cart/desk-d7-gaps-2-45
gh pr create --base main --head cart/desk-d7-gaps-2-45 --title "Mac 2.45.0: the Desk on every display, finished" --body-file <body>
```
After the PR merges: smoke section 79 live, then `gh workflow run mac-release.yml --ref main -f version=2.45.0`.

## Live pass, 2026-11-17 (dev build of the integration branch, signed out, demo, file store)
Built-in display plus a 1920×1080 display to its left, separate Spaces, auto-hiding Dock at the bottom.
- **Passed:** a design previewed through the 2.45 server's `propose_desk` draws per display: the All-displays meters,
  message, claude line and calendar note on both, the clock only on the second at -15°. Under the icons, the small
  pieces on the second display take the owner's real clicks; the meters (a menu-only piece) let them through to the
  Finder, as on main by design (`DeskClickLayer.kinds`). A loose cable unplugged and replugged the second display: its
  window came back with the layout. After item 6, the owner confirmed the auto-hiding Dock slides the second display's
  pieces and hands back to main.
- **Found and fixed:** the Dock rose over the second display's pieces (D4's main-only slide); item 6 (D9, D10).
- **Not checked live:** the companion (no sprite in the dev profile), now playing and watchers (none in demo then),
  OLED dimming, the main display's clicks (an app window covered them), Arrange across displays, a notched non-main
  display, the Dock moving between displays always-shown.
- A synthetic first click of mine landed on another app's window before a check was added; every click after it was
  sent only when the window list showed Sanduhr at that point. Settings and data were backed up first, restored after
  and compared equal; the release app 2.44.0 was relaunched.

## Waiting on you
- **The rest of smoke section 79** (see Live pass above for what ran) (`docs/mac-smoke-test.md`), before the release. Dev build signed out on
  the file store, settings backed up first (CLAUDE.md). It carries each item's live notes and the 2.44 checks never done
  live (clicks on the second display, now playing on All displays, the main Desk under focus changes, drift).
- **The idle CPU with two windows** again after item 2 (more frame reports per window): cputime deltas, under 1 percent.

## Parked
None.

## Stopped
None.

## Decisions to skim
`decisions.md`, D1 to D8. The ones with stakes:
- **D5:** click frames per Desk window, all in the main window's coordinates; a click judged against the window under
  the pointer. One display: only `.main` exists, pinned equal to 2.44.0's elements by a test.
- **D4:** the always-shown Dock and the notch are cleared on every display; the auto-hiding Dock's slide stays main-only.
- **D8:** one companion, in the window its display resolves to (`.all` means main), re-seated only when that window
  changes.
- **D7:** Arrange still knows slanted stacks on the main window only, so a drop onto a slanted stack on another display
  lands by its bounding box. Clicks are not affected. A small follow-up if it shows live.

## Your tree
Untouched; you're on `main`, clean at run start. This run folder is untracked in your tree; commit it with the PR.
