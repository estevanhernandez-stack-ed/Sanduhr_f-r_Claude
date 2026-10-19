<!-- Same per-item field labels as checklist-template.md, so /build can read
     either file. Acceptance holds the item's done-when lines: observable and
     checkable by the item's own local checks. Spec ref points at the source.
     Stop class is the only added field. See skills/guide/references/unattended-run.md. -->

# Run List: polish-leftovers

- [ ] **1. The Desk page's Order caption is two sentences with a Learn more link**
  Spec ref: `docs/cart-runs/2026-10-17-polish-ux/report.md > Found along the way, item 1`
  What to build: In `mac/Sources/Sanduhr/Desk/SettingsView.swift` (around line 46) the Order caption reads "Pieces in the same place stack top to bottom in this order. Drag a piece up or down to reorder it, or onto a piece in another place to move it there. The top, middle and bottom of a side share a column, and a side keeps clear of the centers, so places never overlap." Cut it to two sentences (the stacking rule and the drag), and move the column-and-centers sentence to `docs/setup-guide.md` under the Desk layout heading, reached from a Learn more link in the caption the way run 2 item 14 did it (find that pattern: `rg -n 'Learn more' mac/Sources/Sanduhr/Desk/SettingsView.swift`, and the matching anchors in `docs/setup-guide.md`). If a test pins the caption text, update it.
  Acceptance: the caption has two sentences and a Learn more link whose target exists in `docs/setup-guide.md`; the moved sentence is there; the build, suite and selftest pass.
  Verify: `cd mac && ./build.sh --debug && ./test.sh && ruby smoke/selftest.rb`; `rg -n 'keeps clear of the centers' ../docs/setup-guide.md` hits and the same grep over `Sources/` does not.
  Stop class: go

- [ ] **2. The meter-warning card's At column uses the shared value width**
  Spec ref: `docs/cart-runs/2026-10-17-polish-ux/report.md > Found along the way, item 1 (second half)`
  What to build: In `mac/Sources/Sanduhr/Desk/SettingsView.swift` around line 589, `Text("At")` sits in the meter-warning card with the old fixed 48 pt width; run 2 item 14 put Brightness on the shared value width (find how: `rg -n 'valueWidth|labelWidth|frame\(width: 48' mac/Sources/Sanduhr/Desk/SettingsView.swift`). Put the At column on the same shared width so the card's values line up with the rest of the page. Nothing else on the card changes.
  Acceptance: no `48` literal remains for that column; the build, suite and selftest pass.
  Verify: `cd mac && ./build.sh --debug && ./test.sh && ruby smoke/selftest.rb`.
  Stop class: go

- [ ] **3. With the Desk off, the Notch page says "Needs the Desk." once**
  Spec ref: `docs/cart-runs/2026-10-17-polish-ux/report.md > Found along the way, item 3`
  What to build: On the Notch page (`SettingsView.swift` around lines 800 to 810) `NeedsDeskRow()` shows the alert row when the Desk is off, and the island caption begins with `NeedsDeskRow.text` ("Needs the Desk.") in every state, so with the Desk off the page says it twice. Keep the row; make the caption drop its leading sentence while the row shows (read the Desk switch the way the row does), so the sentence appears exactly once in either state. Check the other two `NeedsDeskRow()` pages (lines 374 and 629) for the same doubling and fix them the same way if they have it. If a test pins the caption, update it; add a test on the pure rule if you write a helper (for example `NeedsDeskRow.caption(_ base: String, deskOn: Bool)`).
  Acceptance: with the Desk off, "Needs the Desk." appears once per page; with it on, once (the caption's sentence); the build, suite and selftest pass.
  Verify: `cd mac && ./build.sh --debug && ./test.sh && ruby smoke/selftest.rb`.
  Stop class: go

- [ ] **4. Three-digit hex shorthand is accepted on commit**
  Spec ref: `docs/cart-runs/2026-10-17-polish-ux/decisions.md > D8`
  What to build: `mac/Sources/Sanduhr/Desk/DeskHex.swift` validates the Desk Look hex fields on commit and refuses "fff" though a saved "fff" still renders. Accept three-digit shorthand by expanding each digit ("fff" to "ffffff", "9ad" to "99aadd"), for single values and for each entry of a gradient comma list, before the six-digit check; keep every other refusal. Update `hint` and `gradientHint` if they promise six digits only ("Three or six hex digits, like 9ad7ff"). Pin in `DeskHexTests.swift` (or the suite that covers DeskHex): shorthand expands, mixed-case expands, a four or five digit value is still refused, a gradient list with a shorthand entry expands that entry.
  Acceptance: the tests above pass with the rest of the suite; the build and selftest pass.
  Verify: `cd mac && ./build.sh --debug && ./test.sh && ruby smoke/selftest.rb`.
  Stop class: go

- [ ] **5. Arrange mode's Hide and Show have debug hook steps and show in the snapshot**
  Spec ref: `docs/cart-runs/2026-10-17-polish-ux/report.md > Found along the way, item 4`
  What to build: `DebugAction.DeskArrangeStep` (`mac/Sources/Sanduhr/Debug/DebugCommand.swift` line 95) has `start, test, done, cancel`. Add `hide` and `show`: `desk-arrange hide` hides the first visible piece of the working layout the way the Hide button does (`DeskArrange` and `DeskArrangeViews.swift`; the Hidden strip is `DeskArrangeCopy.hiddenStrip`), `desk-arrange show` shows the first hidden one the way the strip's button does; both only while arranging, unsaved until done. Snapshot: `desk_arrange` gains `hidden` (the list of hidden piece keys in the working layout, empty outside Arrange). Pin in `DebugTests.swift`, the fixture `smoke/fixtures/snapshot/state.yaml`, `smoke/selftest.rb`, the README key list and the `do` usage text in `mac/smoke/smoke`; extend `smoke/scenarios/desk-arrange.yaml` with a hide, an expect on `desk_arrange.hidden`, a show, and the cancel it already does (read the scenario first and keep its shape).
  Acceptance: the two steps exist, the snapshot key is pinned, the scenario parses and passes the selftest's fixture checks; the build, suite and selftest pass.
  Verify: `cd mac && ./build.sh --debug && ./test.sh && ruby smoke/selftest.rb && ruby -ryaml -e 'YAML.load_file("smoke/scenarios/desk-arrange.yaml")'`.
  Stop class: go

- [ ] **6. ModsSettings.swift is split by section, no behavior change**
  Spec ref: `docs/cart-runs/2026-10-17-polish-ux/report.md > Found along the way, item 5`
  What to build: `mac/Sources/Sanduhr/Views/ModsSettings.swift` is 1,282 lines: `ModsPageModel` (line 11 to about 700), the read and tab types (703 to 790), the `ModsSettings` view and its sections (790 to 1,270), `FolderNotes` (1,272). Move the pieces into sibling files with no edits to their bodies: `ModsPageModel.swift` (the model), `ModsPageTypes.swift` (`ModsPageRead`, `ModsTab`, `ModsFocus`, `ModSketch`, `FolderNotes`), `ModsRows.swift` (the `ModRow*` and `ModSketchView`, `ModCheckRow`, `ModRiskCard`, `ModReportView` private views), leaving `ModsSettings.swift` with the page view, `ModsMore`, `ModsFolderActions`, `ModsIntro`, `ModsSeatMods`, `ModsFromClaudeRow`, `ModsSummaryCard`, `ModsTile`. Private types that cross files become `internal` (drop `private`), nothing else changes. Keep each file's header comment honest. Every file under 500 lines.
  Acceptance: `git diff --stat` shows moves only (no logic change: compare with `git diff -M --stat`); every new file is under 500 lines; the build, suite and selftest pass; `rg -c 'struct|final class|enum' ` over the four files sums to the original count.
  Verify: `cd mac && ./build.sh --debug && ./test.sh && ruby smoke/selftest.rb`; `wc -l Sources/Sanduhr/Views/Mods*.swift`.
  Stop class: go

<!-- Dependencies: none. Order matters for 6 (last) so no later item touches ModsSettings.swift. -->
