# Run List: drawers-2-40

Every item: no file under the real `~/Desktop` is read, moved or listed, and the Desktop Folder prompt never appears. Tests use temp folders made in the test and removed after. Keep view bodies small for the macos-15 type-checker. Never `sed -i` a tracked file. Never touch `windows-dotnet/`.

- [ ] **1. Drawer rules and plans (pure)**
  Spec ref: `docs/mac-drawers-setup-spec.md > 1, 2, 3`; Windows `DrawerRules.cs`, `DrawerPlanValidator.cs`, `DrawerNames`
  What to build: Pure Swift types in `mac/Sources/Sanduhr/Desk/Drawers/`, no AppKit, no file system:
  - `DrawerItemFacts`: name, extension, isDirectory, isPackage, isHidden, isSymlink, isAlias, isDataless, isScreenCapture, size, modified.
  - The seven built-in rules from spec section 2, in order, first match wins, plus the custom rules (name contains, name starts with, case-insensitive). The Older files days are 1 to 3650.
  - The never-moved list: folders, packages, hidden items (dotfiles, `.DS_Store`, `.localized`, `Icon\r`), symlinks, dataless items, the root and drawer folders.
  - The default drawer names.
  - A plan builder: facts in, moves per drawer out.
  - A validator: root safety (not the Desktop or a folder holding it, not `/`, a volume root or home, same volume as the Desktop); sources only direct children of the Desktop or of a drawer (hand moves); the limits of 24 drawers and 2000 moves.
  - Drawer folder name checks.
  - Finder-style free names ("Report 2.pdf", "Report 3.pdf"; folders "Name 2"), up to 99 tries.
  Size M.
  Acceptance: Done when tests cover each rule against a table of names and flags, the never-moved list, first-match order, custom rules, every root refusal, the source rules and limits, and free names with and without extensions. The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **2. Journal, Undo and the return (pure)**
  Spec ref: `docs/mac-drawers-setup-spec.md > 5`; Windows `DrawerJournal.cs`, `DrawerReturn.cs`
  Depends on: 1
  What to build: Pure types:
  - **The journal model** in Windows' JSON shape (`{"entries":[{"id","at","source":"rules|user","moves":[{"from","to","folder","written"}]}]}`), with Codable round-trip. Trim keeps the last 50 runs, and user moves for 30 days.
  - **An open entry** (any `written: false` after a crash) is found on load and offered as Undo for its written moves.
  - **The Undo plan:** newest first. Skip "was moved since" when the file is gone and "changed since" when its modification date changed. A taken original place gets the next free name.
  - **The return plan:** empty every owned drawer that sits directly in the root onto the Desktop, with free names, and remove folders only once empty (the root last, only when it is the default). View drawers are never emptied. A folder that keeps something stays listed.
  - **Prompt strings:** "Put N files back on the Desktop?" and its singular.
  Size M.
  Acceptance: Done when tests cover journal round-trip, trim, crash recovery of an open entry, each Undo skip, name clashes in both directions, the return with mixed rules and hand history, view drawers left alone, and a non-default root kept. The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **3. The mover and the file-safety suite**
  Spec ref: `docs/mac-drawers-setup-spec.md > 4, 5, 9`
  Depends on: 1, 2
  What to build: A `DrawerMover` that executes a plan, an Undo or a return against real folders.
  - **One move** is `renamex_np(from, to, RENAME_EXCL)`. On `EEXIST` it tries the next free name, up to 99 times, then skips. On `EXDEV` it refuses and never copies.
  - **Before each move**, it checks again: the source resolves inside the Desktop or a drawer, and for a rule move it is still a regular file with the same size and date.
  - **After each move**, it reads back where the file landed, by name and inode.
  - **The journal** is written atomically with 0600 permissions, at `~/Library/Application Support/Sanduhr/drawers-journal.json` or an injected path:
    - the entry goes in before the first move, every move marked `written: false`;
    - each move flips its flag;
    - the file is saved every 25 moves and at the end.
  - A result lists what moved and each skip with its reason, for the result bar.
  - **Facts** are read with URL resource values: `isPackage`, `isHidden`, `isSymbolicLink`, `isAliasFile`, the iCloud downloading status for dataless items, and the `kMDItemIsScreenCapture` xattr.
  - **The integration suite runs in a temp folder** standing in for the Desktop, with real files:
    - clashes on both sides;
    - a file edited between plan and Apply;
    - a drawer folder deleted by hand;
    - a simulated crash after N moves, then recovery and Undo;
    - hand moves;
    - a return after mixed history;
    - packages, hidden files and symlinks left alone.
  - **Every scenario ends with the invariant check:** every starting file exists exactly once, at its start path or in a drawer, with its bytes unchanged (compared by SHA-256). No code path deletes or overwrites.
  Size L, hard.
  Acceptance: Done when the integration suite passes every scenario with the invariant check, a test proves an existing destination is never replaced, and a grep of the drawer sources finds no `removeItem` or `trashItem` except the empty-folder removal after the return, which checks emptiness right before. The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **4. Drawer settings and the service**
  Spec ref: `docs/mac-drawers-setup-spec.md > 1, 3, 5, 7, 8`
  Depends on: 1, 2, 3
  What to build:
  - **Preferences** in `UserDefaults.desk` under `drawers.*`: enabled, root, the drawer list (id, name, home, owned), rules, look, opacity, per-drawer looks, places, folded, hidden, and the tidy corner and direction. They load with defaults and round-trip.
  - **A `DrawerService` (MainActor) that:**
    - turns Drawers on (which turns the Desk on) and off (asks for the return, runs it, keeps the rules);
    - creates the default root and owned drawers on the first sort;
    - adds and removes view drawers;
    - allows choosing a root only while no owned drawer exists, with the validator's checks;
    - builds a sort plan and holds it as the pending review;
    - applies it (re-validated);
    - keeps the last 10 runs for Undo;
    - takes hand moves (journaled `user`);
    - renames a drawer, which renames its folder and updates the journal paths.
  - **One FSEvents stream** watches the drawer folders only, never the Desktop, and publishes per-drawer contents: folders first, then names in Finder order, hidden items left out.
  - **The Desktop's location is injected.** The default is `FileManager`'s desktop directory; debug builds also accept `SANDUHR_DRAWERS_DESKTOP`, which release builds ignore (`#if DEBUG`).
  - **Reset Sanduhr…** runs the return first, then removes the drawer settings and the journal, and says so if anything stayed behind.
  - No UI.
  Size L.
  Acceptance: Done when service tests run in temp folders: on, sort, apply, Undo, a hand move, rename, a view drawer untouched by the return, off with everything back, Reset, and the root rules. The FSEvents watcher is tested to report a change in a drawer folder. The suite passes, and nothing reads the real Desktop.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **5. The drawer panels, looks and placement**
  Spec ref: `docs/mac-drawers-setup-spec.md > 7`; Windows `DrawerPlacement.cs`, `DrawerSnap.cs`, `DrawerTidyPlan.cs`, `DrawerLook.cs`
  Depends on: 4
  What to build:
  - **A borderless panel window per drawer** at the `DeskClickLayer` level. Each shows a title (name and count) and a grid of `QLThumbnailGenerator` thumbnails, cached by path and date. Extensions follow the Finder's setting.
  - **Folding:** "Screenshots · 14" when folded; clicking the title folds and opens it, and dragging the title moves the panel.
  - **Mouse and keys:**
    - double-click or Return opens the item;
    - Space shows Quick Look;
    - Command-R reveals it in Finder;
    - drag out to the Finder (file URLs), drag in from the Desktop or another drawer (through the service);
    - Command-Delete moves it to the Trash (`FileManager.trashItem`, from the user's command only).
  - **The context menu** spec section 7 lists.
  - **The four looks,** Solid, Glass (`NSVisualEffectView`, `.hudWindow`, behind-window), Outline and Bare, with opacity 20 to 100 (default 55) and per-drawer overrides.
  - **Placement:** 360 points wide, 330 open, 42 folded. Positions are saved per screen as an offset from its visible frame.
  - **Snapping** to other drawers, then Desk pieces, then the margin, within 12 points; Option-drag turns it off.
  - **Tidy** from a corner as a column or row, with Undo. Title bars stay on screen.
  - **The review bar** (N files into M drawers, up to 3 names each, Apply, Edit, Dismiss, with Edit unticking files and renaming new drawers) and **the result bar** (Undo, and the skips).
  - **No timers.** Redraws come only from the service's changes.
  - The placement, snap and tidy maths stay pure and tested.
  Size L.
  Acceptance: Done when the pure placement, snap, tidy, look and review-model tests pass and the suite passes. Drawing is a live check for the owner.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **6. Settings, Drawers**
  Spec ref: `docs/mac-drawers-setup-spec.md > 8`
  Depends on: 4
  What to build: A Settings page in the Desktop group, with a `SettingsIndex` entry and anchors. It has:
  - the switch, the root (with Choose… only while no owned drawer exists) and **Sort my desktop now**;
  - the rules list (on and off, reorder, remove), the Older files days, and an add field for custom rules;
  - the look, the opacity and the tidy corner;
  - recent runs, each with Undo;
  - the uninstall note, verbatim from spec section 5.
  The page binds to item 4's service. The view models are tested.
  Size M.
  Acceptance: Done when the view-model tests pass and the suite passes.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb`
  Stop class: go

- [ ] **7. state.yaml and the smoke scenario**
  Spec ref: `docs/mac-drawers-setup-spec.md > 8, 9`
  Depends on: 4
  What to build:
  - **`state.yaml` `drawers:`** gets the switch, the drawer count, the files per drawer as numbers, the permission status (a stub `unknown` until item 8), and `review_pending` and `journal_open`. **Never a name or a path.**
  - **The smoke fixture and `smoke/selftest.rb`** are updated to the new keys.
  - **A smoke scenario** sorts a fixture Desktop under `SANDUHR_DRAWERS_DESKTOP` (a temp folder made by the scenario) and checks the counts.
  - **A debug hook** applies the pending review, for the scenario.
  Size S.
  Acceptance: Done when the DebugTests cover the keys and that no name leaks (a fixture file named like a secret never appears in the YAML), and the selftest passes.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb`
  Stop class: go

- [ ] **8. The Desktop Folder permission**
  Spec ref: `docs/mac-drawers-setup-spec.md > 6`
  Depends on: 4, 6
  What to build:
  - **`NSDesktopFolderUsageDescription`** in `mac/Info.plist`, with the spec's sentence. This item only.
  - **Detecting a denial** without private TCC calls: an `EPERM` or `EACCES` reading the Desktop's top level.
  - **Settings, Drawers:** a row saying so, with a button that opens Privacy & Security, Files and Folders. The panels say "Sanduhr can't see your Desktop." No retry loop.
  - **`state.yaml` `drawers.permission`:** `granted`, `denied` or `unknown`.
  - **First read:** the first real Desktop read happens only when the user turns Drawers on or presses Sort, never at launch, and that is tested with a fake.
  - **Never raise the prompt in tests or during the run.**
  Size S.
  Acceptance: Done when the tests prove launch does not read the Desktop, a fake `EPERM` gives the denied state and its strings, the Info.plist key is present with the spec's text, and the suite and selftest pass.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb`
  Stop class: hold

- [ ] **9. Docs, the 2.40.0 What's New card, CHANGELOG**
  Spec ref: `docs/mac-drawers-setup-spec.md > 8, 13`; `docs/mac-widgets-spec.md > 16`
  Depends on: 1, 2, 3, 4, 5, 6, 7
  What to build:
  - **PRIVACY, Mac section,** with the spec section 8 rows: what is read and why; the journal holding full paths on this Mac (0600, kept as specified, removed by Reset); nothing logged, sent or shown to Claude; moves, never deletes; Move to Trash only from the user's command. Name the Desktop Folder permission as item 8 ships it.
  - **`mac/README.md`,** a Drawers section.
  - **`docs/mac-smoke-test.md`,** a live section on a real Desktop: sort, Undo, a hand drop, Trash, then off with everything back.
  - **What's New:** a 2.40.0 card, "Drawers for your Desktop", destination Settings, Drawers, with its `WhatsNewTests` lines.
  - **CHANGELOG:** an entry under the existing `## Unreleased (mac)` heading.
  - No Info.plist. Every claim true to the code.
  Size S.
  Acceptance: Done when the WhatsNew tests pass and the docs describe only what was built.
  Verify: `cd mac && ./test.sh`
  Stop class: go

## Not this run

- Set up Sanduhr, usage off, Copy prompt (`docs/mac-drawers-setup-spec.md > 10-12`): this is 2.41.0, by the owner's call 1.
