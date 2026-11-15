# Run Report: desk-displays-2-44

8 merged, 0 held, 0 parked, 0 stopped.

## Ready to push
- **Integration branch:** `cart/desk-displays-2-44` (base `83e5509b`, `main`), worktree
  `~/.cwt/sanduhr-f-r-clau-desk-displays-2-44/int`
- **Commits:** 18 (one or more per item, each merged locally):
  - `e0fc8a9a` docs(mac): the 2.44.0 Desk per display spec
  - `48ef7751` feat(mac): a display per Desk piece in the layout string
  - `21b7c6be` feat(mac): Desk displays, their names and where a piece draws
  - `ba9ed4c8` feat(mac): a Desk window per display
  - `d095828c` feat(mac): Arrange Desk across displays
  - `0bd31457` feat(mac): a Display picker for each Desk piece
  - `53d7af13` feat(mac): Claude places Desk pieces per display
  - `4476ec9e` chore(mac): 2.44.0, the Desk on every display
  - `f93c1605` fix(mac): the pasted-chat prompt offers main and all, not display ids
  - `48beec91` test(mac): the bundled font tests run on the main actor
  - plus eight `merge: item NN` commits
- **Checks on the integration branch (2026-11-15):** `./test.sh` 3243 tests in 361 suites passed; `ruby smoke/selftest.rb`
  1090 checks passed; `python3 -m pytest -q integrations/tests` 319 passed, 209 subtests; `./build.sh --debug` built.
  Every item was also checked on its own branch by the orchestrator, not only by its worker.
- **Close-out gates:** vibe-test and vibe-sec not run; the repo's own checks above stand in (Swift and Python suites,
  smoke selftest, debug build). CI (`swift test` on macos-15, the docs link check) runs on the PR.

To push and open the PR:
```bash
git -C ~/.cwt/sanduhr-f-r-clau-desk-displays-2-44/int push -u origin cart/desk-displays-2-44
gh pr create --base main --head cart/desk-displays-2-44 --title "Mac 2.44.0: the Desk on every display" --body-file <body>
```
Pushing waits for your say-so. After the PR merges: the live pass (below), then `gh workflow run mac-release.yml --ref main
-f version=2.44.0`.

## Live pass, 2026-11-15 (dev build of the integration branch, signed out, file store)
Built-in display plus a 1920×1080 external display to its left. Smoke section 78:
- **Passed:** 1 and 2 (plug in: nothing moves; `desk-state.json` lists both displays, every piece `main`); 3 (clock set to
  the second display in Settings: a window appears there and draws the clock at its anchor; the layout writes the long form
  `clock:br:1:0:bar@<id>`); 5 (Arrange: the drag tracks across displays, Show on works, Cancel restores the layout string
  exactly); 6 (`get_desk` lists both displays and each piece's monitor; an unknown id is refused on `layout.meters.monitor`
  and writes nothing; a proposal moving the message to the second display previews there after Preview on the Desk); 7
  (unplug: layout not rewritten, `get_desk` lists one display; replug: the clock returns); sleep and wake (the second
  window comes back); 8, OLED care's away dimming on the second window after 352 s idle; 9, idle CPU **0.48% of a core**
  over 60 s with two windows (cputime delta).
- **Not checked live:** clicks on the second display (no meeting today), now playing on All displays (nothing playing),
  the main Desk staying on the menu bar display while another app has focus on the second, the drift step, and the meters
  (hidden while signed out). Where the clock drew while unplugged was not confirmed by eye.
- Settings and data were backed up first and restored after; both changed defaults domains match the backup.

## Waiting on you
- **The rest of the live pass** (see above for what passed), smoke section 78 (`docs/mac-smoke-test.md`), before the release. A second display is
  connected now (1024×768, left of main). Use a dev build signed out on the file store (CLAUDE.md), or the release build
  after the section 45 backup. What only a live pass can tell (D7 and item 5's notes):
  - A drag that leaves its window keeps updating on the other display (item 5's key unknown).
  - A drop lands at the right anchor on a display offset or a different size; an empty display gets a window when Arrange
    starts and loses it on Cancel; the bar follows the pointer; Return and Escape after clicking on the second display.
  - Clicks on the second display (meetings join, now playing, the limit menu), with and without the under-icons layer.
  - Unplug: pieces fall back to main; replug: they return. Sleep and wake. OLED care dims both windows.
  - The main Desk stays on the menu bar display while another app has focus on the second one (D2 changes this).
  - **Idle CPU with two windows**, per backlog #7 (cputime deltas, under 1 percent of a core): the clock's 1-second
    timeline, message effects and meter pulses animate in every window that draws them.
- **Known gaps on extra displays, by design for 2.44.0 (D7):** no Dock insets or notch; a piece on All displays clicks
  only on the main copy; slanted stacks hit-test by their bounding box; the companion stays on main and does not move
  when dropped on another display.
- **sanduhr-mods desk-setup 0.2.1** (not this run): switch on `monitors` and `wallpaper` separately so a two-display Mac
  gets the screen question; `hold-for-release` until 2.44.0 is live.

## Parked
None.

## Stopped
None.

## Decisions to skim
`decisions.md`, D1 to D11. The ones with stakes:
- **D1:** a piece off the main display writes the long form `clock:tl:1:0:bar@all`, so a 2.43 build reading it keeps the
  anchor, size and tilt (it loses only the display, and a non-bar style).
- **D2:** no suffix means main, and main is the menu bar display, not `NSScreen.main`. Owner-approved.
- **D7:** item 4 merged with four gaps on extra displays (above), left for after the live pass.
- **D11:** an intermittent hang of the whole test run, traced by a sample to an AppKit class-init deadlock in the bundled
  font tests (pre-existing, not 2.44 code), fixed with `@MainActor` on that suite; 11 full runs pass after.

## Your tree
Untouched; you're on `main`. Untracked at run start: `docs/superpowers/specs/2026-11-14-mac-desk-per-monitor-design.md`
(item 1 copied it onto the branch byte for byte, so your copy can go once the PR merges). This run folder,
`docs/cart-runs/2026-11-14-desk-displays-2-44/`, is untracked in your tree too; commit it with the PR or on its own.
