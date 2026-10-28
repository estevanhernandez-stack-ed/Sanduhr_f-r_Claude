# Run Report: widgets-2-35

**8 merged, 0 held, 0 parked, 0 stopped.** Base 8fce24db, integration branch `cart/widgets-2-35` (24 commits, 276 files, mostly the shared fixture corpus).

## Ready to push

`cart/widgets-2-35`, checked on its tip: `cd mac && ./test.sh` 2226 tests passed, `ruby smoke/selftest.rb` 1033 checks, `python3 -m pytest -q integrations/tests` 227 passed, `./build.sh --debug` built.

| # | Item | Merge |
|---|---|---|
| 1 | PackValidator.swift, byte-equal on the 50 shared fixtures; validate.py vendored at 61b4247 | b1cea8b5 |
| 2 | WidgetZip (stored only) and the pixel-sheet rasterizer | 2a756f63 |
| 3 | WidgetCatalog (Key A, rollback, `PackFormat.supported = "3.8.0"`), the download client, two PackInflate fixes | 35366816, 9b9e1538, c10e5031 |
| 4 | WidgetStore, Reset's "Your widgets", Settings ▸ Widgets, pack themes and palettes | 3f87c40e, 700ee362, 6f2d19cf |
| 5 | IdleClock and OLED care | f6ac6f2c, 8f8149d7 |
| 6 | The companion's rules | 1eda0c0c, be415eed |
| 7 | The companion piece on the Desk | 95ddb9db |
| 8 | Install notifies the companion; PRIVACY, README, smoke sections 54-56, What's New 2.35.0, CHANGELOG | bbc4ef78, 57de9229, + README fix |

Push and PR:

```
git push -u origin cart/widgets-2-35
gh pr create --base main --head cart/widgets-2-35 --title "feat(mac): widgets, the companion and OLED care (2.35.0)"
```

Then the live checks, then the ship commit (Info.plist 2.35.0, build 47).

## Live checks before the ship commit (owner)

Dev build, signed out, the usual backup and restore. Smoke sections 54-56 in `docs/mac-smoke-test.md`.
- Settings ▸ Widgets fetches the real signed catalog (the first live Key A verification on a Mac) and installs `hourglass`.
- The companion on the Desk: stage, moods, a trigger, roaming, and that the sprite does **not clip** while roaming outside its piece (D15).
- OLED care: drift, dim after 5 minutes, the white cap; off means nothing runs. An idle-CPU measurement with the companion showing.

## Waiting on you, Parked, Stopped

None.

## Decisions to skim

`decisions.md`, D1-D19. The ones that matter most:
- **D7/D8:** a differential fuzz of the hand-written inflater against Python zlib (36,000 streams, no crash or hang); fixes kept; the macOS libz wording emulation reverted to stock zlib.
- **D10:** pack palettes join the message editor, not the seeded now-playing looks.
- **D15:** the companion roams outside its piece view; the clip check above decides whether it moves to an overlay.
- **D18:** `.sig2` on every refresh and size-based timeouts, the same as Windows; a PRIVACY wording note for the Windows agent.

Builder's tree at start: clean, on `main`.
