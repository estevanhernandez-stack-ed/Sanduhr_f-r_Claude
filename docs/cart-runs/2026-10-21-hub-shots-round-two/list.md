<!-- Same per-item field labels as checklist-template.md. Stop class is the only added field. -->

# Run List: hub-shots-round-two

- [x] **1. The session-start card sits above the Mods tabs (#291)**
  Spec ref: GitHub issue #291
  What to build: `SessionLoadPanel` ("What a new session starts with", `mac/Sources/Sanduhr/Views/ModsCatalogView.swift` around lines 94 and 164) draws inside the Catalog tab's content, after the tab bar, so at the default Settings height it is cut mid-card. Move it to page level in `mac/Sources/Sanduhr/Views/ModsSettings.swift`: right above `ModsTabBar`, after `ModsSummaryCard`, shown on every tab, with the same `if model.loaded` condition and the same `model.shownLoads(demo:)` input. It must no longer draw in the Catalog tab. Give it a settings anchor (`SettingsAnchor.sessionLoad`, following how `metersMod` and `inventory` are declared and used) so a `settings-link` can scroll to it. Keep its body unchanged; it becomes internal if it is private. Update the README's Mods & Config description where it says where the card sits (`rg -n -i "new session starts" mac/README.md`), and any test or smoke fixture that pins its place. Look at the anchors test (`rg -n "metersMod|inventory" mac/Tests`) and add the new anchor where they are listed.
  Acceptance: the card is drawn once, above the tab bar, on every tab; the anchor exists and is pinned; the build, suite and selftest pass.
  Verify: `cd mac && ./build.sh --debug && ./test.sh && ruby smoke/selftest.rb`.
  Stop class: go

- [x] **2. The log walks sort paths by bytes, not Unicode comparison (#7 leftover)**
  Spec ref: `docs/BACKLOG.md > #7 > Still worth a look`
  What to build: `CCLogBulkWalk` users sort with Swift `String` `<`, which is Unicode-correct and slow; a 60 s sample of 2.30.0 spent about 0.2 percent of a core in `_stableSortImpl` and the merge it calls. In `mac/Sources/Sanduhr/Services/CCLogReader.swift` (`LocalCCLogFileSystem.logFiles(under:)`, `.sorted { $0.path < $1.path }`) and `mac/Sources/Sanduhr/Services/VaultIngester.swift` (`walk`, sorted by `(mtimeTicks, path)`), compare paths by `utf8.lexicographicallyPrecedes`. Nothing depends on Unicode collation order: the reader keys its cache by path and the vault sorts for a stable ingest order. Update `CCLogWalkTests.bulkWalkMatchesTheFileByFileWalk` if it asserts `sorted()` order (make it assert the byte order), and add a test with two paths that sort differently by bytes than by Unicode comparison (an accented name against its decomposed form, or "Z" against "a") proving the walk's order is the byte order.
  Acceptance: no `String <` comparison of paths left in the two walks; the tests pass; the build, suite and selftest pass.
  Verify: `cd mac && ./build.sh --debug && ./test.sh && ruby smoke/selftest.rb`.
  Stop class: go

- [x] **3. Debug hooks open the Export Config sheet and Compare on import with made-up data (#292)**
  Spec ref: GitHub issue #292
  What to build: The hub wants shots of the Export Config sheet (its checkboxes and the CARRY.md line) and of Compare on import (a checkbox per difference). Find both (`rg -n -i "export config|CARRY.md|compare on import|ConfigImport|ConfigPack" mac/Sources/Sanduhr --glob '*.swift' | head`) and how each is opened today. Extend the `mods-config` hook in `mac/Sources/Sanduhr/Debug/DebugCommand.swift` (around line 293) with `export` (opens the Export Config sheet as its button does) and `import-sample` (opens Compare on import over a made-up pack). The sample pack must be made-up data only: invent it in code (or a fixture under `mac/smoke/fixtures/`) with plausible but fake mod names, a fake folder name, no home paths and no person or employer names, and with at least three differences so the checkboxes show. It must never write to a real Claude Code folder: the hook opens the sheet only; applying from it in a debug session is the user's own act, but prefer a sheet whose Apply is disabled or targets nothing when opened from the sample, if the code allows it without new UI (say what you did). Apply them through `DebugHooks.swift` the way `.modsConfig` is applied. Pin the parse in `DebugTests.swift` (both new words and a bad one), update the `mods-config` usage text in `DebugCommand.swift` and `mac/smoke/smoke`, the hook list in `mac/smoke/README.md`, and the selftest's fake app if it validates `mods-config` args (read `mac/smoke/selftest.rb`). Snapshot: if `state.yaml` has a key for open sheets, make the new ones show there; if not, do not add one.
  Acceptance: both words parse and open their sheets; the sample pack has no real data; the build, suite and selftest pass.
  Verify: `cd mac && ./build.sh --debug && ./test.sh && ruby smoke/selftest.rb`.
  Stop class: go

- [x] **4. The three #292 screenshots, taken from the integration build in demo mode**
  Spec ref: GitHub issue #292
  What to build: Done by the orchestrator at close, not a worker: on the integration branch's dev build, with the release app quit and the support folder backed up, `smoke/smoke enable`, `do demo on`, then the Export Config sheet (`do mods-config export`), Compare on import (`do mods-config import-sample`) and Settings, Accounts, each captured from `screen-settings.png` (or the sheet's window capture) in a `smoke/smoke snap`. Save as `docs/screenshots/mac/settings-export-config.png`, `settings-import-compare.png` and `settings-accounts.png`, check each for home paths with a username, real account names, employer or person names, then demo off, hooks off, restore the folder, relaunch the release app.
  Acceptance: three PNGs in `docs/screenshots/mac/`, each read by eye for made-up data only.
  Verify: the files exist; each was looked at.
  Stop class: go

- [x] **5. The notchless Notch preview with the pill off draws no empty bar (#290)**
  Spec ref: GitHub issue #290; the old Mac's recheck on 2.31.0 (pill on passes, pill off fails), `docs/screenshots/mac/old-mac-2.31/290-preview-pill-off.png` on branch `shots/old-mac-290`
  What to build: In `mac/Sources/Sanduhr/Desk/DeskPreviews.swift`, `NotchTabCanvas` always draws `MenuBarStrip(height:)`; with the pill off and no camera or mic in use it is an empty dark bar over the caption "No notch on this Mac's screens: the island stays off." Draw the strip only when something sits on it: the pill (pill on) or the camera and mic tab (`shown.any`), which is what the screen itself shows. With neither, draw no strip and keep the caption, and add the way back to the caption's sentence: "No notch on this Mac's screens: the island stays off. Turn on Top-center island below to draw it as a pill." (check the switch's exact label in `SettingsView.swift`, `rg -n "Top-center island" mac/Sources`, and quote it as it reads). Put the rule in a small pure helper on the canvas or `NotchPill` (for example `static func previewDrawsStrip(pill: Bool, indicators: Bool) -> Bool`) and pin it in `NotchPillTests.swift`: pill on draws, indicators draw, neither does not. Keep the canvas height steady between the states if the card would otherwise jump (a clear spacer of the strip's height is fine). Nothing changes on a notched Mac's preview.
  Acceptance: the helper and its test; no strip with neither; the build, suite and selftest pass.
  Verify: `cd mac && ./build.sh --debug && ./test.sh && ruby smoke/selftest.rb`.
  Stop class: go

- [x] **6. Export, import and Accounts show only invented data in demo mode (#292)**
  Spec ref: GitHub issue #292; the first capture pass of item 4 (no shot kept)
  What to build: Three gaps found when the shots were taken from the integration build, demo mode on:
  (a) `mods-config export` opens Export Config over the real shown seat: real agent and skill names, real "looks like a secret" paths. Add an export sample the way item 3 added the import sample: `mods-config export-sample` opens the Export Config sheet over invented rows (several agents, skills, a plugin, an MCP server with a `${NAME}`, an env var, two rows that cannot travel with their reasons) and an invented folder name, built in code from the sheet's own row types. Its Export button writes nothing (a hidden `sample` flag, as item 3 did for import). Keep the plain `export` word as it is.
  (b) `mods-config import-sample` picks the shown seat for its Into picker, which shows the real folder, its git-clone warning and, behind the sheet, the real seats. Make the sample's Into list a single invented folder (for example "Sample ~/.claude-sample") and no real ones, with no git-clone note, regardless of what is shown behind it.
  (c) Settings, Accounts in demo mode collapses to a short sample list with no names under the accounts and no "Follow the account I'm using" switch, so it cannot show what #292 asks. In demo mode, render the real Accounts layout over the sample accounts (Work and Personal, with made-up names under them where the real page shows a name, and the Follow switch as the real page draws it with two accounts), with every action that would change accounts inert while demo is on. Keep the demo caption.
  Pin: the export sample has no "/Users/" or "/home/" in any string and at least two "cannot travel" rows; the import sample's Into list is the invented folder only; the demo Accounts model yields two accounts and shows the Follow switch. Update `DebugTests.swift`, the `mods-config` usage text in `DebugCommand.swift` and `mac/smoke/smoke`, `mac/smoke/README.md`, and the README's demo-mode section with one sentence on Accounts.
  Acceptance: the three behaviors and their tests; the build, suite and selftest pass.
  Verify: `cd mac && ./build.sh --debug && ./test.sh && ruby smoke/selftest.rb`.
  Stop class: go

<!-- Dependencies: 4 needs 3's and 6's hooks and runs last (after 5 and 6). 1 and 2 are independent. 4 runs from the integration tip after 1 to 3 merge. -->
