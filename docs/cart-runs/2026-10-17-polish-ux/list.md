# Run List: polish-ux

Source: `docs/vibe-iterate/ux-polish-2026-10-17.md` (catalog row numbers below are its `#`). Paths are under `mac/Sources/Sanduhr/` unless stated. Repo rules every item follows: never `sed -i` a tracked file (Edit tool or Python); stage by explicit paths; commit messages end with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>` and `Claude-Session: https://claude.ai/code/session_01Jfz21BmnQKRbuExYPtRcWC`; no home-folder paths, employer or person names; `windows-dotnet/` is never touched. Posture: smallest diff that delivers the row; no refactors beside it; an existing user's saved choices keep their meaning. Swift check: `cd mac && ./build.sh --debug && ./test.sh`. When CI's slower compiler would fail to type-check a SwiftUI `body`, split it into smaller properties. Strings that appear in the names table (`Models/SettingsNames.swift`) or in `mac/smoke/scenarios/settings-names.yaml` / `settings-sections.yaml` must be updated in all three places.

- [ ] **1. Mods & Config opens on "Meters above the prompt" with its switch**
  Spec ref: `docs/vibe-iterate/ux-polish-2026-10-17.md` > #1, #2, #3, #4, #6, #34; `docs/settings-v2-spec.md` > "### Mods & Config" ("Sanduhr's own mod at the top")
  What to build (M): in `Views/ModsSettings.swift` (the page body near the `ModsTabBar` at ~:746), add a header section above the tabs titled **Meters above the prompt** (the exact name in `Models/SettingsNames.swift:29-31`): one row per Claude Code folder the page lists, each with the mod's state (On / Off / Not installed) and its switch, reusing the row the Studio's Folders tab already draws (`Views/ModsOwnSettings.swift` ~:197, the `Toggle("On")` row with its Update/Remove) and the existing consent sheet (`MetersConsentSheet`, `Views/ModStudio.swift:267`) for a first install; plus a **Studio…** button. Below it, put the existing summary card, tab bar and tab content under a `DisclosureGroup("More: mods and config")` whose expanded state starts true when the user has any kept mod, any waiting proposal or install request, or more than one folder, else false; the user's toggle of it is remembered in defaults (`modsMoreExpanded`). Fix the stale copy at `ModsSettings.swift:817` and `:1102` (the page has the switch again; say so truthfully). Make the names table's anchor for "Meters above the prompt" (`SettingsNames.swift:59`, `mods#meters`) land on the new header. Delete the dead `requestSwitch`/`confirmOn` path (`ModsSettings.swift:518-524`, `ModsOwnSettings.swift:122-130`) since the consent sheet is the one path. Keep the Studio's Folders tab as it is (the same rows, now in two places is fine for this item).
  Acceptance: on a fresh profile with one folder and the mod not installed, the first control under the page title is the header with that folder's row saying Not installed and a switch; flipping it opens the consent sheet; with the mod installed the row says On and the switch turns it off through the existing receipt path. The disclosure is collapsed on that fresh profile and expanded when a kept mod exists. `rg -n 'switches per folder above|switches here, per folder' mac/Sources` is empty. `settings-names.yaml` still passes (`ruby smoke/selftest.rb`).
  Verify: `cd mac && ./build.sh --debug && ./test.sh && ruby smoke/selftest.rb`; swift-testing for the disclosure's default (fresh / kept mod / two folders) and for the header listing the same folders as the Studio tab.
  Stop class: go

- [ ] **2. A readability plate behind every Desk piece**
  Spec ref: `docs/vibe-iterate/ux-polish-2026-10-17.md` > "The Desk on a full desktop", fix A
  What to build (S/M): a `DeskPlate` view modifier in `Desk/DeskView.swift` applied once in `DeskPiece.body` (:236-240) around every piece (the message included, which has no `deskInk`): a `RoundedRectangle` (corner 18) filled black at 0.30 opacity with a 10 pt blur and 14 pt padding, behind the piece, following the piece's own opacity, pattern from `deskPulse` (:453-462); reduce transparency on `accessibilityReduceTransparency` means a solid 0.55 fill and no blur. Store the switch in the `.desk` defaults as `plate` (Bool); `DeskFirstRun.run` (`Desk/DeskSupport.swift:104-107`) writes `plate = true` for a new install; an absent key reads as false so existing Desks look exactly as before. A toggle **Plate behind the text** in Desk Look beside the shadow toggle (`Desk/SettingsView.swift:370-378`), with a one-line caption ("A soft dark plate behind each piece, for desktops with files under it."); add it to `Models/SettingsNames.swift` and the smoke fixtures. The Desk preview in Settings shows it because it renders the same pieces. `state.yaml` gains `desk_plate` (Bool) in the smoke snapshot (follow how `desk_piece_clicks` is reported in `Debug/`).
  Acceptance: with the key absent nothing draws differently (a test renders a piece with and without and compares that the absent case equals today's); with it on, every piece has the plate; the toggle round-trips; first run writes it true.
  Verify: `cd mac && ./build.sh --debug && ./test.sh && ruby smoke/selftest.rb`; swift-testing for the key's default and first-run write.
  Stop class: go

- [ ] **3. Destructive actions ask first and look destructive**
  Spec ref: catalog #5, #10, #17
  What to build (S/M): confirms for `Remove` (`Views/ModsOwnSettings.swift:242-245`: "Remove the meters mod from <folder>? Its settings.json entry comes out; the receipt keeps the undo."), `Dismiss` on a proposal (`Views/ModNewSheet.swift:777-780`: it deletes the staged files), `Undo Import` (`Views/ModsBySeatView.swift:142-147,167`: it rewrites the folder; add a spinner while it runs, as the Open sheet has), and `Forget Folder` (`ModsBySeatView.swift:249`). Use `.confirmationDialog` for all four, as Forget… already does (`ModNewSheet.swift:507`), with `role: .destructive` on the confirming button. Leave the Keep/Try/Turn On alerts and the TypedNameConfirm sheets alone.
  Acceptance: each of the four actions shows a confirm naming what it does before anything runs; cancelling runs nothing; the confirming buttons are destructive-styled; `./test.sh` green.
  Verify: `cd mac && ./build.sh --debug && ./test.sh`.
  Stop class: go

- [ ] **4. Notes that clear, and a sheet that says when it failed**
  Spec ref: catalog #7, #8, #9, #21
  What to build (S): `configImportNote` and `configExportNote` (`Views/ModsSettings.swift:229-230`, shown at `Views/ModsBySeatView.swift:261-266`) become per-seat (`[String: String]` keyed by the normalized seat path) and are cleared when the seat picker changes, when the Settings window closes, and when the next import/export starts. The Export Config sheet shows its own failure under its footer (`Views/ConfigExportSheet.swift:60-66` sets the note; render it in the sheet at the footer). `keptNote` (`Views/ModNewSheet.swift:647-649`) draws orange only for failures: give it a `(text, isFailure)` shape, and clear it on close and in `onAppear` (:445). The Finder-open branch at `Views/ConfigImportSheet.swift:81` switches to By Seat so its note is seen.
  Acceptance: an export note under seat A never shows under seat B; a failed export is readable inside the sheet; a success note in New Mod isn't orange; reopening New Mod shows no old note.
  Verify: `cd mac && ./build.sh --debug && ./test.sh`.
  Stop class: go

- [ ] **5. One word per concept on the page**
  Spec ref: catalog #11, #12, #13, #23
  What to build (M): in user-facing strings under `Views/Mods*.swift`, `Views/ModNewSheet.swift`, `Views/Config*Sheet.swift`, `Views/ModStudio*.swift`, `Services/ModInventory.swift:149-152`: "folder" replaces "seat" (the picker "Seat" → "Folder", "All seats" → "All folders", the tab "By Seat" → "By Folder", the By Seat section titled "Seat" → "Folder", `ModsCatalogView.swift:61` reworded); code identifiers stay. "Extensions" section → "Mods and plugins"; the summary tile "enabled plugins" → "on in a folder" (or the count's true meaning); badges stay Mod / Plugin. Sanduhr's own mod says On / Off / Not installed everywhere (`ModsCatalogView.swift:559,585`: no green "Available", no "Installed in no folder yet."; the filter labels keep "Installed"/"Available" for third-party plugins). `Picker("Side")` (`ModsCatalogView.swift:673`) shows its label as **Work or personal**, and the three spellings (:655, :658, `ConfigImportSheet.swift:350`) use "marked work" consistently. Update `Models/SettingsNames.swift`, `mac/smoke/scenarios/settings-names.yaml`, `settings-sections.yaml`, `mods-config.yaml` and `docs/mac-smoke-test.md` where they name these strings; the `ModsTab` raw values and `showTab` keys stay as they are so the smoke hook keeps working.
  Acceptance: `rg -n -i '\bseat' mac/Sources/Sanduhr/Views/Mods*.swift mac/Sources/Sanduhr/Views/ModNewSheet.swift mac/Sources/Sanduhr/Views/Config*.swift` matches only identifiers and comments, no string literals; `rg -n '"Extensions"' mac/Sources` is empty; `./test.sh` and `ruby smoke/selftest.rb` green.
  Verify: `cd mac && ./build.sh --debug && ./test.sh && ruby smoke/selftest.rb`; the rg lines above.
  Stop class: go

- [ ] **6. Controls appear when they apply**
  Spec ref: catalog #14, #15, #16, #25, #26
  What to build (M): on Catalog (`Views/ModsCatalogView.swift:85-112`) show the filter row (Kind, Folder, Show), the sort and Expand All / Collapse All only when the catalog has more than 10 cards or more than one folder; Search stays. The Folder filter and the By Folder chip picker (`Views/ModsBySeatView.swift:106-121`) show only with two or more folders. The Compare tab (`Views/ModsSettings.swift:647-656`, `Views/ModsCompareView.swift:33`) is offered only with two or more folders (the `ModsTab` case stays; the segment is hidden). The `claude`-missing note (`ModsSettings.swift:739-744`) moves from the page top to the Catalog's Check context (next to the first Check button, or inside the "More" disclosure). Expand All / Collapse All / Folders… / Studio… take the same `.bordered` small style as New Mod… (:91-95); link style stays only for in-caption links.
  Acceptance: a fresh profile with one folder and the stock mod shows no filter row, no sort, no chip picker and no Compare segment; adding a second folder shows them; the `showTab` smoke hook can still open each tab.
  Verify: `cd mac && ./build.sh --debug && ./test.sh && ruby smoke/selftest.rb`; swift-testing for the visibility rule (cards count, folder count).
  Stop class: go

- [ ] **7. "Let agents propose mods" at page level, in plain words**
  Spec ref: catalog #20
  What to build (S): move the toggle (`Views/ModNewSheet.swift:634-641`) into the "More: mods and config" area of the page (item 1's disclosure, a "From Claude" row) and keep a mirror in New Mod › From Claude if that sheet's layout needs it; its caption in plain language: "Claude can hand you mods and config to review here. Off: Claude's requests are refused and the Build prompt tells it so." Keep `canPropose`'s effect on the copied prompt (:429) but mention it in that caption.
  Acceptance: the switch is reachable without opening New Mod; the caption has no tool names; `./test.sh` green.
  Verify: `cd mac && ./build.sh --debug && ./test.sh`.
  Stop class: go

- [ ] **8. The Catalog intro tells the truth; Export… shows it is working**
  Spec ref: catalog #22, #24
  What to build (S): `Views/ModsCatalogView.swift:61` intro: "Read from files only…" becomes true by scoping it ("The cards are read from files; nothing is fetched, run or changed until you use a button."). `Views/ModNewSheet.swift:226-246` `exportKept` runs `ModPack.write` off the main actor (`Task.detached`, as `finishConfigExport` does) with a "Writing the pack…" line and the Export button disabled meanwhile.
  Acceptance: the intro sentence no longer claims nothing changes; Export… shows progress and the UI stays responsive; `./test.sh` green.
  Verify: `cd mac && ./build.sh --debug && ./test.sh`.
  Stop class: go

- [ ] **9. Desk Look hex fields validate**
  Spec ref: catalog #18
  What to build (S): `HexRow` (`Desk/SettingsView.swift:424-436`) validates on commit: six hex digits (optionally `#`), or a comma list of them for a gradient; a bad value keeps the last good one and shows a one-line red caption "Six hex digits, like 9ad7ff" under the field; `Color.hex`'s `.gray` fallback (`Models/Theme.swift:284`) stays as the last line of defence.
  Acceptance: typing "blue" leaves the saved value unchanged and shows the caption; a valid value saves; `./test.sh` green with a test for the validator.
  Verify: `cd mac && ./build.sh --debug && ./test.sh`.
  Stop class: go

- [ ] **10. A denied Automation permission shows in Settings**
  Spec ref: catalog #19
  What to build (S): `Services/NowPlayingController.swift:340-356` captures stderr (drop `nullDevice` at :352) and recognizes the Automation denial (`osascript` exit -1743 / "Not authorized to send Apple events"); it publishes a `automationDenied` flag the Now Playing page reads to show a warning row with an "Open System Settings…" button to Privacy & Security › Automation (`x-apple.systempreferences:com.apple.preference.security?Privacy_Automation`), in the same style as the Accessibility warning at `Desk/SettingsView.swift:1101-1110`. Nothing from the script's output is logged.
  Acceptance: with the flag set the warning shows and the button opens System Settings; with it clear nothing shows; `./test.sh` green with a test for the denial detection on fixture stderr.
  Verify: `cd mac && ./build.sh --debug && ./test.sh`.
  Stop class: go

- [ ] **11. Desk first run and Desk Look say what they do**
  Spec ref: catalog #27, #28, #32
  What to build (S): `DeskFirstRun.run` (`Desk/DeskSupport.swift:70,106`) stops placing `meetings` in the layout string when `showMeetings` is false, so the Order list and place picker don't show a piece that never draws (an existing saved layout is read as before). The toggle label at `Desk/SettingsView.swift:375` becomes "Drop shadow under the text" with the caption naming that it covers every piece but the message. "Clock and message take clicks" (`Desk/DeskHitTest.swift:321-333`) defaults to off for new installs (first run writes it; an existing value is kept) and its caption leads with the cost: "Desktop icons under the clock or message can't be clicked while this is on."
  Acceptance: a fresh profile's Order list has no meetings row; the shadow label and caption read as above; a fresh profile has clicks off and an existing profile keeps its value (tests for both).
  Verify: `cd mac && ./build.sh --debug && ./test.sh && ruby smoke/selftest.rb` (update `settings-names.yaml` for the label).
  Stop class: go

- [ ] **12. Arrange Desk can hide and show pieces**
  Spec ref: catalog #29, #30, #31
  What to build (M): in Arrange mode (`Desk/DeskArrangeViews.swift:163-169`) add **Hide** to a piece's controls and a small "Hidden: clock, meters…" strip with a Show button per hidden piece (hiding removes the piece from the layout string exactly as Settings does; Show puts it back at its standard anchor). While arranging, disable Margins (Advanced) and "Read today's meetings" (`Desk/SettingsView.swift:47,81-94,106`) as Place and Order already are. Disable Arrange Desk… when nothing is placed (`SettingsView.swift:153-167,225-226`) with the caption "Place a piece first."
  Acceptance: a piece hidden in Arrange is gone from the Desk and comes back with Show; the three controls are disabled while arranging; Arrange is disabled with an empty layout; `./test.sh` green.
  Verify: `cd mac && ./build.sh --debug && ./test.sh && ruby smoke/selftest.rb` (`desk-arrange.yaml` may need the new control).
  Stop class: go

- [ ] **13. The Notch page's disabled states**
  Spec ref: catalog #33
  What to build (S): `Desk/SettingsView.swift:739-741`: Glow and Camera and mic are disabled when the island is off, as Notch Text already is; the "Needs the Desk." rule moves to the first sentence of a short caption under the island switch, and the ~100-word caption at :891 is split into the two sentences that matter plus a "Learn more" link to the guide section.
  Acceptance: with the island off all three groups are disabled; the caption's first sentence says what needs the Desk; `./test.sh` green.
  Verify: `cd mac && ./build.sh --debug && ./test.sh && ruby smoke/selftest.rb`.
  Stop class: go

- [ ] **14. Captions trimmed, spacing aligned**
  Spec ref: catalog #35, #36, #37
  What to build (S): the captions at `Desk/SettingsView.swift:957` and `:290` cut to two sentences each with the rest moved to the guide (`docs/setup-guide.md`, one new paragraph each, linked); the Brightness row (:745-750) uses the shared `slider()` (:1118-1126); the Message page (:561-587) adopts `.formStyle(.grouped)` like the other Desk pages, keeping its editor's scroll behaviour.
  Acceptance: the two captions are two sentences; Brightness's value column matches the other sliders; the Message page's margins match Desk Look's; `./test.sh` green.
  Verify: `cd mac && ./build.sh --debug && ./test.sh && ruby smoke/selftest.rb`.
  Stop class: go

## Not this run

- Keep clear of desktop items (fix B, Finder automation) and quiet-on-a-busy-desktop (fix C): the owner chose the plate; both need a permission prompt and their own design.
- Splitting `ModsSettings.swift` (1,132 lines) or `Desk/SettingsView.swift` (1,127): item 1 and 6 add to them; a split is a refactor round.
