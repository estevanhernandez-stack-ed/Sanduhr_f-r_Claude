# Run Report: polish-ux

14 merged, 0 held, 0 parked, 0 stopped.

## Ready to push
- **Integration branch:** `cart/polish-ux` (base `e53377c`, tip `2126d41`), worktree `~/.cwt/sanduhr-f-r-clau-polish-ux/int`
- **Commits:** 15 (14 items plus one orchestrator fix), each merged with a `merge: item NN <slug>` commit:
  - `e477999` feat(mac): Mods & Config opens on Meters above the prompt
  - `7c5e0d9` feat(mac): a readability plate behind every Desk piece
  - `9bec186` feat(mac): destructive actions on Mods & Config ask first
  - `47a3f0e` fix(mac): Mods & Config notes clear, and the Export sheet says why it failed
  - `f284dac` fix(mac): one word per concept on Mods & Config
  - `603be43` fix(mac): Mods & Config controls appear when they apply
  - `86800c8` fix(mac): Let agents propose mods on the Mods & Config page, in plain words
  - `6a64771` fix(mac): the From Claude caption says what the Build prompt does
  - `3bf9e13` fix(mac): the Catalog intro tells the truth, and Export… shows it is writing
  - `fb02da3` fix(mac): the Desk hex fields validate on commit and keep the last good value
  - `06bd122` fix(mac): a denied Automation permission shows on the Now Playing page
  - `2a20612` fix(mac): Desk first run and Desk Look say what they do
  - `deb9b6d` feat(mac): Arrange Desk hides and shows pieces, and the Desk page waits while arranging
  - `49fa8ab` feat(mac): the Notch page leads with its rule, and Camera and mic's caption shrinks to two sentences
  - `1a01edf` feat(mac): two Desk captions shrink to two sentences, Brightness lines up, and Message is a grouped form
- **Checks:** every item ran `./build.sh --debug`, the full `./test.sh` and `ruby smoke/selftest.rb` in its worktree and again before its merge (1,737 → 1,769 tests as helpers and rules gained tests). Close-out on the integration tip: build ✓, 1,769 tests passed, selftest 939 checks passed, pytest 223 passed, vibe-sec's in-house secret scan over 9,239 files found only the two known fixtures. The vibe-test and vibe-sec gates themselves cannot run here (run 1, D12).
- **Not run:** the real-app smoke scenarios and any by-hand look. Three items reason about a surface no test renders: the Arrange strip's placement above the Dock and the Hide button's hit area (item 12), the grouped Message page with the editor's drag-reorder inside a Form (item 14), and the plate's look on a real desktop (item 2). Worth one pass on your Mac before the release.

To push and open the PR:
```bash
git push -u origin cart/polish-ux
gh pr create --base main --head cart/polish-ux --title "feat(mac): polish round for any user: the Desk on a full desktop, Mods & Config for a first-timer" --body-file docs/cart-runs/2026-10-17-polish-ux/report.md
```
Pushing waits for your say-so. Tell Cart "push it", or name a subset.

## Waiting on you
None.

## Parked
None.

## Stopped
None.

## Decisions to skim
Full ledger: `decisions.md`. Highest stakes:
- D1: the Desk plate is on for new installs, off (absent key) for existing ones.
- D2: "Clock and message take clicks" is off for new installs; existing values kept.
- D6: "By Seat" → "By Folder" also in three older What's New cards (the names contract).
- D8: the Desk hex fields save on commit, not per keystroke.
- D9: item 13's "disable Glow and Camera-and-mic with the island off" was declined with evidence: both work without the island (test-pinned); the scan's row #33 was half wrong.

## What a first-timer now sees
Mods & Config opens on **Meters above the prompt**: one row per folder with its state and switch, Studio…, and everything else under **More: mods and config**, which opens itself once there is a kept mod, a waiting request or a second folder. Filters, sort, Expand/Collapse, the folder pickers and Compare appear with two folders or more than ten cards. One word per concept: folder, mod and plugin, On / Off / Not installed, "Work or personal". Remove, Dismiss, Undo Import and Forget Folder ask first. Notes belong to their folder and clear. The Desk draws a soft plate behind every piece on a new install; Arrange can hide and show pieces; a bad hex value never reaches the desktop; a denied Automation permission is said on the Now Playing page.

## Found along the way, not fixed (for your call)
- The Desk page's Order caption (55 words, 3 sentences) is a fourth caption wall the scan didn't list; the meter-warning card's "At" column also uses the old 48 pt width (item 14 named Brightness only).
- `docs/settings-v2-spec.md` still quotes the old shadow label twice (lines 179 and 338), left as the dated record.
- With the Desk off, the Notch page says "Needs the Desk." twice (the alert row and the new caption's first sentence); drop the caption's sentence if one mention is enough.
- The new Hide and Show in Arrange have no debug hook, so `desk-arrange.yaml` can't drive them; the string writes are unit-tested.
- Items 1 and 6 grew `ModsSettings.swift` further (now the largest view file); a split is a refactor round.
- Three-digit hex shorthand ("fff") still renders if already saved but is refused when retyped (D8).

## Left untouched
Uncommitted files in your tree at run start. The run cut from `e53377c` and did not stash, reset or check out over them.
- `docs/vibe-iterate/` (untracked: `ux-polish-2026-10-17.md`, the scan this run executed)
- `docs/cart-runs/2026-10-17-polish-ux/` (this run folder, untracked)

## After the run: the Desk under the icons (two commits on the tip)
The scan's "full desktop" concern turned out to be layer order, not readability: files should sit on top of the Desk, not under it. Two commits on `cart/polish-ux` after the run's close-out, both tried on a real desktop with files over the pieces:
- `4151850` **"Keep the Desk under desktop icons"** (`DeskLayer`, key `underIcons`, off by default and for new installs): the Desk window moves to desktop+1, under Finder's icon window. Settings, Desk, Clicks; search finds "under desktop icons", "bottom", "behind files"; `desk_under_icons` in the smoke snapshot.
- `187ad02` **A click layer** (`DeskClickLayer`): under the icons nothing on the Desk window can be clicked (Finder's window covers the screen), so a second transparent window at the Desk's old level holds one faint plate per click area of the small pieces a click acts on (meeting rows with a link, the calendar note, the account name, watchers, now playing, the camera strip). Same monitors, same hit test, same actions, on the layer's own element list. The big pieces (meters, clock, message, claude line) get no plate, so the files under them stay the Finder's: the first cut covered them too and made a full desktop's files immovable. Arrange raises the Desk window and rests the layer. Close-out on the tip: 1,779 tests, selftest 940.

Cost named in the caption: a file under a small piece yields that spot to the piece, and the meters', clock's and message's menus wait until the option is off.
