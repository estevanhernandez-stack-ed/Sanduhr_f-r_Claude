# Run List: setup-2-41

Every item:
- Read the Windows reference named in its Spec ref, but never edit `windows-dotnet/`.
- Never sign in, read or write the Keychain, or read or move anything in the real `~/Desktop`. Tests use in-memory defaults stores (`DefaultsStore`), temp folders and fakes.
- Keep SwiftUI bodies small for the macos-15 type-checker. Never `sed -i` a tracked file. No em dash in user-facing copy.
- Copy follows Windows' `SetupCopy.cs`, with Mac words: menu bar, not tray; Settings, General; the Desk's pieces.

- [ ] **1. The stored setup, its migration and the launch rules (pure)**
  Spec ref: spec 10 ("When it opens", "Just the Desk folds in"); Windows `SetupSettings.cs`, `SetupCopy.cs` (`SetupChange`), the setup run's D-entries and review fix I1
  What to build: `mac/Sources/Sanduhr/Models/Setup.swift` (or a `Setup/` folder):
  - **`SetupSettings`:** `desktop` plus a `usage` map with `claude`. It is stored in standard defaults under `setup` as a dictionary in Windows' shape. Decoding is per entry: a missing or wrong-typed entry keeps the fallback, so damage never reads as nothing ticked.
  - **`SetupChoices`:**
    - the three ticks (desktop, drawers, Claude usage), with first-run defaults desktop and drawers on, usage off;
    - `anyTicked`;
    - `deskRuns` (desktop or drawers);
    - `of(settings, drawersOn, deskEnabled)`, where desktop reads as ticked only while the Desk runs;
    - `toSettings(before)`, which keeps unknown usage sources.
  - **`SetupMigration.from(hasAccount, justTheDesk, silencedReminder, deskEnabled)`:**
    - an account gives desktop on and usage on, with the Desk switch untouched, and the reminder back only if Just the Desk silenced it;
    - Just the Desk alone gives desktop on and usage off, with the Desk on;
    - neither gives nil, which means the screen opens.
    - It runs once. After that, `justTheDesk` is read only for this migration.
  - **`SetupLaunch`:**
    - `opensSetup(setup)`;
    - the widget starts hidden when usage is off and the Desk runs;
    - the menu bar click before any setup, with no account, opens setup.
  - **`SetupCopy`:** every string, from Windows with Mac words. Includes "Usage tracking is off. Turn it on in Set up Sanduhr."
  - **`SetupChange.lines(before, after, drawerFiles, addOnsInstalled, hasAccount)`:** what unticking will change, said first. Includes "Pick at least one…" when nothing is ticked.
  Size M.
  Acceptance: Done when tests cover:
  - encode and decode round-trip, and decoding damaged entries;
  - every migration case, including that an account never comes out usage-off;
  - each launch rule;
  - every SetupChange line in both directions, the blocked line, and copy free of em dashes.
  The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **2. Usage off with the account kept**
  Spec ref: spec 11; Windows `WidgetViewModel` UsageOn/SetUsageOn (setup run item 4, review fix I2)
  Depends on: 1
  What to build: in `UsageViewModel` and the places that read its status:
  - **`usageOn`** reads the stored setup (absent means on, for anyone upgraded before the migration runs).
  - **`setUsageOn(false)`:**
    - stops fetching (`stopFetching`) and sets a `usageOff` status, distinct from `signedOut`;
    - **deletes `snapshot.json`** (`SnapshotWriter.delete()`);
    - keeps the account list, the Keychain item, history and settings.
  - **`setUsageOn(true)`:** connects and fetches at once when a key exists. Without a key it is signed out as today.
  - **With usage off:**
    - the launch path never fetches and never writes the snapshot;
    - the widget hides, and the menu bar shows the hourglass alone, with no percentage;
    - the Desk's and notch's Claude pieces hide as they do when signed out, and the sign-in line is not shown.
  - **Settings:** Usage, Alerts and the Claude Code page show `SetupCopy`'s usage-off line in place of the sign-in line.
  - **Saving a key** (a sign-in) turns usage on, as Windows does.
  - The migration from item 1 is called once at launch, before the first fetch.
  Size L.
  Acceptance: Done when tests (with fakes for the API and snapshot writer) prove:
  - usage off stops the timers and calls delete, and leaves the accounts and key untouched;
  - usage on fetches;
  - launch with usage off never fetches;
  - a sign-in turns usage on;
  - each status-dependent surface reads `usageOff`.
  The suite passes, and a grep shows no Keychain delete on the usage-off path.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **3. A nicer desktop off leaves only the drawers**
  Spec ref: spec 10 (A nicer desktop); Windows `SetupDesk.cs`, setup run item 5
  Depends on: 1
  What to build: with desktop off and drawers on, the Desk runs but draws none of its pieces (clock, message, weather, meters, now playing, watchers, and the rest), while the drawer panels keep showing.
  - The saved layout is never rewritten. Ticking desktop again puts every piece back where it was.
  - Desk Layout's Arrange and a Desk design's Apply say `SetupCopy`'s desktop-off lines: "Turn on A nicer desktop…", and Apply saves the design for later.
  - The notch is not a Desk piece and is untouched.
  Size M.
  Acceptance: Done when tests prove:
  - the pieces filter out while desktop is off, and the stored layout is unchanged;
  - turning desktop back on restores the same pieces;
  - the Arrange and design lines appear only while desktop is off.
  The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **4. The setup service applies a change**
  Spec ref: spec 10 (Continue); Windows `SetupService.cs`
  Depends on: 1, 2, 3
  What to build: a `SetupService` over a host protocol (drawersOn, drawersEverSorted, drawerFiles, addOnsInstalled, usageOn, hasAccount, turnOffDrawers, turnOnDrawers, sortNow, setUsage, reloadDesk). `apply(after)` runs in this order:
  - **refuse** a blocked choice (nothing ticked);
  - **drawers off first**, with the put-back question. A no, or a failed return, keeps drawers ticked while the rest still applies;
  - **save the setup**, and the Desk switch to `deskRuns`. A failed save stops here with `SaveFailed`;
  - **usage;**
  - **drawers on**, with its first-on line;
  - **one Desk reload;**
  - **the first sort review**, only when drawers were just turned on and no rules run was ever accepted.

  The app host wires these to `DrawersSettingsModel`/`DrawerService` (the same turn-off and turn-on the Drawers page uses), `UsageViewModel`, and `DeskController`. It returns the result lines and whether a review opened.
  Size M.
  Acceptance: Done when tests with a fake host cover:
  - the order;
  - a refused turn-off keeping drawers on while the rest applies;
  - the save failure;
  - a review only on the first turn-on;
  - no review when one was accepted before;
  - the blocked choice.
  The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **5. The Set up Sanduhr window, where it opens, and the 2.41.0 card**
  Spec ref: spec 10 (The screen, When it opens), 13 (the card); Windows `SetupViewModel.cs`, `SetupWindow.xaml(.cs)`, setup run items 7 and 8
  Depends on: 4
  What to build:
  - **The window:**
    - the title, an intro line, and three choices, each with its body text. Claude is named under Track my AI usage, with the trademark line wherever Claude is named;
    - the What changes list, updated as ticks change;
    - **Continue**, which applies through item 4 and shows What happened when there is anything to say;
    - **Not now**, and the close button: neither saves anything;
    - a **Design it with an AI chat** button and its caption, which opens item 7's window (a stub until item 7 lands).
    - Accessible names on every control.
  - **Where it opens:**
    - it replaces the welcome sheet on a new install (no setup, no account);
    - on upgrade, it opens only when the migration returned nil;
    - from the menu bar menu's **Set up Sanduhr…** (`SanduhrMenu`);
    - from a **Set up Sanduhr…** button on Settings, General (with its `SettingsIndex` entry);
    - from the What's New card.
  - **The welcome tour:** keep it, following Windows: the tour for whatever the screen turned on. Remove the Just the Desk button and its sheet path. Leave `JustTheDesk` only as the migration's reader.
  - **The 2.41.0 card:** "Choose what Sanduhr shows" in `WhatsNewCards.swift` (`release2410`, id `choose-what-shows`), with a destination that opens the setup window. Add its `WhatsNewTests` lines (count, ids per version, destination).
  Size L.
  Acceptance: Done when tests cover:
  - the window model: ticks from the current state, lines on untick, blocked Continue, Not now saving nothing;
  - the open rules for new install, upgrade with account, upgrade with Just the Desk, and neither;
  - the menu item and the Settings index entry;
  - the card table;
  - no remaining caller of the removed Just the Desk button path.
  The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **6. The AI chat prompt and the pasted-answer reader (pure)**
  Spec ref: spec 12; Windows `SetupPrompt.cs`, `PastedSetup.cs`
  Depends on: 1
  What to build:
  - **`SetupPrompt`:** the Mac's prompt, written from `SetupPrompt.cs` with the Mac Desk's words (its widget names and anchors from the `propose_desk` vocabulary). It asks what the person likes and how they keep files, and returns one JSON block. **It never contains a file name, a path or a usage number.** Build it only from constants and the Desk vocabulary, never from the user's state.
  - **`PastedSetup.read(text)`:**
    - scans balanced JSON objects (braces inside strings don't count) and takes the first one holding `desk` or `drawers`, counting how many there were;
    - refuses a paste over 200,000 characters;
    - reads `drawers` with `rules` (1 to 20; kinds as in `DrawerRules`; text up to 60 characters; days 1 to 3650; drawer names up to 40 characters and valid folder names), `look`, `opacity` and `arrange {corner, direction}`, and no other key;
    - every problem is a finding naming its field, and **one error rejects the whole block**;
    - the `desk` half is kept raw for item 7, which runs the `propose_desk` validator on it.
  Size M.
  Acceptance: Done when tests cover:
  - the prompt holds no path, file name or digit-percent usage pattern;
  - the block scan, including braces in strings, fences, several blocks and the size limit;
  - each limit at and over its edge;
  - unknown keys;
  - one bad field rejecting everything, with the field named;
  - a Windows-shaped sample block reading cleanly.
  The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **7. Design it with an AI chat in the app**
  Spec ref: spec 12 (Buttons); Windows `ChatDesign` window (setup run item 10), `DeskDesignService.ProposePasted`
  Depends on: 5, 6
  What to build: a window opened from the setup screen and from Settings, Drawers (under its own button). It has Copy prompt (to the pasteboard, with the copied line), a paste box, the findings list, and three buttons:
  - **Preview on my Desk:** runs the `desk` half through the same validator and Desk review `propose_desk` uses, with a neutral heading, writing no result file. While desktop is off, Apply saves the design for later.
  - **Use these drawer rules:** says first what it will do, then replaces the rules, turns drawers on if they were off, and opens a review. Nothing moves before Apply.
  - **Use these drawer settings:** applies look, opacity and a Tidy, with Undo.

  Buttons enable only for the parts the paste held and passed. Copy names no product. A paste is parsed locally and never logged or sent.
  Size L.
  Acceptance: Done when tests cover:
  - the window model: button enablement per part, the copied and fix-count lines;
  - preview handing the desk half to the validator, with a bad desk half naming its field;
  - rules replaced and a review opened, with no move;
  - settings applied, with Undo restoring them.
  The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **8. state.yaml and the smoke scenario**
  Spec ref: spec 13 (smoke); `mac/smoke/README.md`; the Drawers run's item 7
  Depends on: 5
  What to build:
  - **`state.yaml`** gets a `setup:` block: `desktop`, `drawers`, `usage`, `migrated`, and `pending` (whether the screen would open). No name, path or number.
  - **The debug hook** gets a `setup` action that applies a set of ticks through the service, with drawers left out (`setup desktop=on usage=off` and the like). The `drawers` action already guards real-Desktop moves.
  - **The scenario:** `mac/smoke/scenarios/setup.yaml` turns usage off and on, and desktop off and on, and checks `state.yaml`, putting everything back as it was. Quote on/off in the args.
  - Selftest fixtures follow.
  Size S.
  Acceptance: Done when the DebugSnapshot tests cover the block, `ruby smoke/selftest.rb` passes with the new fixtures, and the scenario parses.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb`
  Stop class: go

- [ ] **9. PRIVACY, README, smoke doc and CHANGELOG**
  Spec ref: spec 13; `docs/mac-widgets-spec.md` section 16
  Depends on: 2, 5, 7
  What to build:
  - **`docs/PRIVACY.md`, exactly true to the code:**
    - usage off stops every request to claude.ai and deletes `snapshot.json`, while the account and Keychain item stay;
    - the prompt holds no file name, path or usage number;
    - a paste is read locally and never stored, logged or sent;
    - the Last updated date.
  - **`mac/README.md`:** a Set up Sanduhr section.
  - **`docs/mac-smoke-test.md`:** a new section 74, Set up Sanduhr, as a live walk: new install, upgrade cases, usage off with statusline and `get_usage` signed-out, desktop off with drawers, Copy prompt, a paste with one bad field, the three buttons.
  - **`CHANGELOG.md`:** one bullet under `## Unreleased (mac)`, which is a fresh heading above `## v2.40.0-mac`.
  Size S.
  Acceptance: Done when every doc claim maps to code in items 1 to 8, the README and smoke doc name real controls, and the suite still passes.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb`
  Stop class: go

- [ ] **10. Whole-branch review fixes**
  Spec ref: `decisions.md > D6, D7, D10, D12` and item 9's findings; the same close as the Windows run's item 13
  Depends on: 9
  What to build:
  - **(a)** Saving credentials turns usage on only when a non-empty session key is saved (D6).
  - **(b)** While A nicer desktop is off, the weather and telemetry timers stop. They key off the filtered layout, so no weather request goes out with nothing drawn (D7).
  - **(c)** After Continue, with usage ticked and no key, the sign-in sheet opens at once (D10).
  - **(d)** "Use these drawer rules" is refused, with a line, while a drawers review is pending (D12).
  - **(e)** `suggestedFolder`'s `/organizations` request is guarded on usage on, so usage off sends nothing to claude.ai unless you start a sign-in. PRIVACY's exception sentence is updated to match.
  - **(f)** `mac/smoke/scenarios/just-the-desk.yaml` expects the new tour card.
  - **(g)** `UsageSwitch`'s "signed-out state" comment is corrected.
  - **(h)** PRIVACY and README updated wherever (a), (b) and (e) change what they say.
  Size M.
  Acceptance: Done when each fix has a test where testable, PRIVACY stays exactly true, and the suite and selftest pass.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb`
  Stop class: go
