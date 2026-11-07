# Run List: widget-from-file-2-42

Every item:
- The spec is `docs/mac-widgets-spec.md` section 20. The Windows reference is `windows-dotnet/src/Sanduhr.Core/WidgetFileInstall.cs`, `windows-dotnet/src/Sanduhr.App/ViewModels/ModsPageViewModel.cs` (the file-install path) and `windows-dotnet/tests/Sanduhr.App.Tests/WidgetFromFilePageTests.cs`. Read them; never edit `windows-dotnet/`.
- Never sign in, never read or write the Keychain, never touch `~/Library/Application Support/Sanduhr`. Tests use temp folders, in-memory defaults and the fixtures under `mac/Tests`.
- Keep SwiftUI bodies small for the macos-15 type-checker. Never `sed -i` a tracked file. No em dash in user-facing copy. Sentences end with a period.
- Copy follows the spec's sentences word for word, with "Mac" where Windows says "PC".
- Each item commits on its own branch with a conventional-commit subject and the session's attribution trailer.

- [ ] **1. WidgetFileInstall (pure): read, version, validate, install, and the sentences**
  Spec ref: `docs/mac-widgets-spec.md > 20` (Read, Version, Validate, Install, companion lines); Windows `WidgetFileInstall.cs`
  What to build: `mac/Sources/Sanduhr/Widgets/WidgetFileInstall.swift`, no UI:
  - `WidgetFileInstall.install(zip: Data, store: WidgetStore, supported: String = WidgetCatalog.supported) -> Outcome`, where `Outcome` is `installed(manifest, wasUpdate: Bool)` or `refused(String)`. Order: `WidgetZip.read` (its error's sentence on refusal), the manifest's `sanduhr_min` through `WidgetCatalog.meetsMinimum` with the catalog's sentence ("<Name> needs a newer Sanduhr (it asks for widget format <v>)."), `PackValidator.validate(files, folderName: manifest.id, strict: true)` (on failure the first three sentences, then "and N more."), then `store.install(files, manifest, source: .local, replaceMadeHere: true)`; the store's refusal sentence passes through, so a catalog pack of the same id is never replaced. `wasUpdate` is true when the same id was installed as `.local` before.
  - `WidgetFileInstall.line(for:manifest, wasUpdate:, isSprite:, deskOn:, companionShown:) -> String`: the spec's sentences. A sprite: "<Name> is on your Desk now. It came from your file and stays on this Mac." / Desk off: "<Name> is your companion now. Turn the Desk on in Settings, Desk to see it." / piece hidden: "<Name> is your companion now. It is hidden; tick Companion in Settings, Desk to show it." Not a sprite: "<Name> is installed from your file. It stays on this Mac.", with "updated" in place of "installed" on an update.
  - Byte cap: refuse a zip over the catalog download's cap before reading, with one sentence.
  Size M.
  Acceptance: Done when `mac/Tests/SanduhrTests/WidgetFileInstallTests.swift` (zips built the way `WidgetZipTests` builds `ZipSpec`s, from the `WidgetPackFixtures` good and bad packs, store on a temp folder) proves:
  - a good pack installs, lists as made here with source `local`, and its files on disk match the zip;
  - a bad pack (a missing `LICENSE`; a blocked word in the name; a sheet over 4096 px) installs nothing and the outcome carries the validator's sentence;
  - the same id installed twice ends as one pack holding the second zip's files, `wasUpdate` true;
  - a zip whose id matches an installed catalog pack is refused and the catalog pack's files are unchanged;
  - a `sanduhr_min` above `supported` is refused before validation, with the catalog's sentence;
  - a zip `WidgetZip` refuses (a link, an oversize entry) installs nothing;
  - every `line(for:)` case, and no em dash in any sentence.
  The suite passes.
  Verify: `cd mac && ./test.sh --filter WidgetFileInstall` then `./test.sh`
  Stop class: go

- [ ] **2. The Settings entry, the model, and the companion**
  Spec ref: `docs/mac-widgets-spec.md > 20` (the entry point, the status line, the companion); Windows `ModsPageViewModel.cs` file-install path, `WidgetFromFilePageTests.cs`; #440
  Depends on: 1
  What to build:
  - `WidgetsModel`: `fileInstallStatus: String?` beside `ownArtStatus`, cleared when the panel opens; `installFromFile(data: Data?)` (nil means the file couldn't be read: "The file couldn't be read, so nothing was installed."). On `installed` with a sprite pack (a `sprite.json` in the files, the same test own art uses), write only the companion choice through `CompanionSettings` (`pack` = the id, saved to the Desk suite) and leave the layout alone; then set the status line from `WidgetFileInstall.line(for:)` using the Desk's enabled flag (`DeskController.enabledKey`) and whether the Companion piece is shown in the current layout. A palette or effects pack leaves the companion as it was. Post the same pack-change notification the own-art path posts so the Desk reloads.
  - `WidgetsSettings.swift`: **Install widget from file…** beside **New sprite from an image…**, an `NSOpenPanel` for one `.zip` (allowed content type zip, no multiple selection, no directories), read with the same cap item 1 enforces, handed to `installFromFile`. The status line shows under the buttons like the own-art line. Accessibility label "Install widget from file".
  - The seat report and `get_widgets` need no change if they already report `source` for local packs; confirm by reading `SeatReport.swift` and the MCP's widgets read, and say so in the report.
  Size M, hard: the companion state lives in the Desk's layout and the model must not touch the Desk's windows.
  Acceptance: Done when `WidgetsModelTests` (or a new `WidgetsModelFileInstallTests`) with fakes proves:
  - a good sprite zip installs and the companion choice becomes its id, with the three Desk-state lines for Desk on and shown, Desk off, and piece hidden;
  - a palette zip installs and the companion choice is unchanged;
  - a refused zip leaves the companion unchanged and the status line carries the refusal;
  - a nil read gives the read sentence and installs nothing;
  - replacing an earlier companion pick works and an update keeps the pick.
  The button exists in the Widgets settings view with its accessibility label (a source scan test like `ClaudeSignInTests` uses is enough). `./test.sh` passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **3. Smoke section 75: a debug hook, a scenario and state.yaml**
  Spec ref: `docs/mac-widgets-spec.md > 20` (Release: "Smoke section 75 with a scenario"); `mac/smoke/README.md`; `docs/mac-smoke-test.md` sections 72 to 74 for the shape
  Depends on: 2
  What to build:
  - A debug hook the harness can call to install a pack from a zip path (the same way the existing hooks drive own art or setup; read `mac/smoke/README.md` and the Debug sources first), plus `widgets.fileInstallStatus` (or the harness's naming) in `state.yaml`.
  - `mac/smoke/scenarios/widget-from-file.yaml`: builds or ships a small good zip fixture (a palette or the fixtures' sprite; no real art, invented names), installs it, asserts the pack is listed as made here and the status line, then installs a broken zip and asserts nothing changed and the refusal line. Ruby selftest covers the scenario the way the other scenarios are covered.
  - `docs/mac-smoke-test.md`: section 75, "Install a widget pack from a file (2.42.0)", in the shape of 72 to 74: the steps, what to see, the scenario name.
  Size M.
  Acceptance: Done when `ruby smoke/selftest.rb` passes with the new scenario included, the scenario file validates, and section 75 is in the smoke doc.
  Verify: `cd mac && ruby smoke/selftest.rb && ./test.sh --filter Debug`
  Stop class: go

- [ ] **4. Release lockstep, the What's New card, CHANGELOG, PRIVACY and README**
  Spec ref: `docs/mac-widgets-spec.md > 20` (Privacy, Release); `CLAUDE.md` Gotchas, Mac (version lockstep, the card, the CHANGELOG heading)
  Depends on: 2
  What to build:
  - `mac/Info.plist`: `CFBundleShortVersionString` 2.42.0, `CFBundleVersion` 57.
  - `WhatsNewCards.swift`: a card `version: "2.42.0", id: "widget-from-file"`, title "Install a widget from a file", body in the cards' voice (two sentences: what it does, where it is), `art: .symbol("shippingbox")` or a fitting symbol, `destination: .widgets`. `WhatsNewTests`: the table count, the release set and the per-version ids and destinations updated so CI stays green.
  - `CHANGELOG.md`: under `## Unreleased (mac)` (create the heading above the 2.41.0 section if absent; keep that exact heading), a **New** bullet in the Windows entry's voice with Mac words: what installs, the checks, made here, same id updates, a catalog id is never replaced, the file is only read, a sprite becomes the companion.
  - `docs/PRIVACY.md`: the Mac paragraph that starts "On the Mac from 2.36.0" gains a sentence for 2.42.0 (the zip you pick is read once, copied into the pack's folder, never sent anywhere or shown to Claude); the On the Mac table's own-art row gains the zip.
  - `mac/README.md`: the Widgets feature's rules gain "Install widget from file" in one or two lines.
  - `docs/BACKLOG.md` #8 or the issue list: nothing to edit unless a line there says the Mac has no file install; if one does, correct it.
  Size S.
  Acceptance: Done when `./test.sh --filter WhatsNew` passes, `plutil -lint mac/Info.plist` is OK, the CHANGELOG heading reads exactly `## Unreleased (mac)`, and `rg -n "2\.42\.0" mac/Info.plist CHANGELOG.md mac/Sources/Sanduhr/Models/WhatsNewCards.swift docs/PRIVACY.md` finds each file.
  Verify: `cd mac && plutil -lint Info.plist && ./test.sh --filter WhatsNew`
  Stop class: go

- [ ] **5. Whole-branch review fixes**
  Spec ref: `docs/mac-widgets-spec.md > 20`, every line; the integration branch's diff against the base
  Depends on: 1, 2, 3, 4
  What to build: read the whole integration diff against the spec: every sentence matches the spec word for word; no em dash in copy; no home path, person or employer in any committed file (the run's `state.json` and fixtures included); the file picker reads once and nothing else; `WidgetFileInstall` never writes outside the store; tests use no real folders. Fix what is off, in place, and list each fix in the commit body. Run the full suite, the integrations tests and the band-fixture check.
  Size S.
  Acceptance: Done when `./test.sh`, `python3 -m pytest -q integrations/tests` and `node --disable-warning=MODULE_TYPELESS_PACKAGE_JSON --import ./register.mjs generate.ts --check` (in `mac/integrations/band-fixtures`) all pass, and `rg -n "/Users/" docs/cart-runs/2026-11-07-widget-from-file-2-42 mac/Tests mac/smoke` finds nothing.
  Verify: `cd mac && ./test.sh && python3 -m pytest -q integrations/tests`
  Stop class: go

## Not this run

- Drag and drop a zip onto the Widgets page (`#408`, "nice to have"): a second entry point with its own drop-target and accessibility work; the spec keeps it out of 2.42.0.
