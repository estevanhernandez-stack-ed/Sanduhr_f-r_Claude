# Report: desk-2-37

**7 merged, 0 held, 0 parked, 0 stopped.**

## Ready to push

Integration branch `cart/desk-2-37` (base `e66f051c`), worktree `~/.cwt/sanduhr-f-r-clau-desk-2-37/int`.

| Item | Commits |
|---|---|
| 1. Layout words gain tilt and style | e5c74c7b |
| 2. The fold | c57b914e |
| 3. Meter styles | 4dabaf4a |
| 4. Font packs | 01ceaf13 |
| 5. get_desk and propose_desk | 51f2f7b7, 0c2fcdd0 |
| 6. The app's Desk design review | bb76e422, 80238338 (Put my Desk back) |
| 7. Docs, What's New, CHANGELOG | 41d3aec6, 9603ced8 |

Close-out on the integration branch (session-run):
- `./test.sh`: 2403 tests in 281 suites passed.
- `ruby smoke/selftest.rb`: 1033 checks passed.
- `python3 -m pytest -q integrations/tests`: 293 passed, 167 subtests.
- `./build.sh --debug`: built.

To push and open the PR:

```
git push -u origin cart/desk-2-37
gh pr create --base main --head cart/desk-2-37 --title "feat(mac): 2.37.0 the Desk, shaped by hand or by Claude"
```

## Waiting on you

No held items. Live checks only a running Desk can confirm (dev build, signed out, Application Support backed up first):
1. Slant a stack in Settings ▸ Desk ▸ Slant: pieces and plates lean together, clicks land on the slant, Arrange Desk drags a slanted stack.
2. Meter styles (Slim, Rings, Ticks) on the Claude line and the meters: pace mark, the nearly-full warning, proportions, and on a slanted stack.
3. A font pack (the fixture pack, installed by hand per smoke section 64): it joins the font pickers and draws; uninstall takes it out.
4. `propose_desk` from Claude Code: the banner, Preview on the Desk (layout, slant, style, font, ink across notch and message, plate, shadow), Dismiss, Apply, then Put my Desk back and Keep it.
5. Idle CPU with a slant, a ring style and the desk-state exporter running.

## Parked

None.

## Stopped

None.

## Decisions to skim

`decisions.md`, D1 to D11. The ones that matter most:
- **D9 / D2:** no app export carried the Desk settings, so the app now writes `desk-state.json` (0600, only when it changes) for `get_desk`; PRIVACY covers it (dashboard AsQeKGBqvbGBqsktyJyU).
- **D11:** Apply first had no undo; Put my Desk back (one snapshot, `deskDesignBefore`) closes that gap. Windows' saved-designs library is not ported.
- **D10:** `propose_desk` refuses messages, looks and theme (pointing at their own tools), Windows-only fields and the 2.38 pieces, with reasons.
- **D7:** the font fixture is a generated OFL font, not a relabelled copy of EsteFont.
- **D5:** the fold is one static shear per slanted stack; plates stay per piece.

Builder's tree at start: on `main`, no uncommitted files. Untouched by the run apart from this folder.
