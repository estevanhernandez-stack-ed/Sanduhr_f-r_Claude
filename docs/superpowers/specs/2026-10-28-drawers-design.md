# Drawers: design

Windows, 5.0 spec 1 of 4. Brainstormed with the owner on 2026-10-28, after 4.0.0.0 certified. Dashboard
decisions: `01yLlImy20i5fysTrRwV` (the scope add), `DdHaiULvJrqPo2QVfDxy` (real folders first),
`H9y2hds0NGJ49qc4otNr` (one plan, three sources), `eGlcLsW6z9J5vEBvQ8qW` (per-drawer homes),
`W3Auw3Izq6iBL53855Ml` (a Desk piece, three front doors), `U0L7tncv569RecYc2Vlb` (naming),
`mMKMhDGVZkBD4bLWJ5X2` (the brief), `KZASgYSLxidUCIDKuuEJ` (approach 1), `N8N5mSuXOqdguxoGOKr0` (turning
off returns everything). The early decisions say "fences"; the feature is **Drawers**, and Stardock's
Fences is a competitor's name only, never ours, in the UI, the listing or code.

## The 5.0 frame

The app becomes **Sanduhr**; the desktop setup (clock, widgets, meters, drawers) is **Sanduhr Desk**.
5.0 is for people coding with any agent and for people who want a living desktop, Claude account or
not. It ships as four specs, in order:

1. **Drawers stage 1 and the three front doors** (this spec).
2. The agent layer, with Claude and Codex on it.
3. Gemini CLI and local-model sources, after a Gemini usage spike.
4. The rename to Sanduhr and Sanduhr Desk, and the Store listing.

Windows only; the Mac follows in its own cycle. Drawers stage 2 (frames under the real desktop icons,
Fences-style) waits for its own spike on Explorer's icon view and is not in this spec.

## The goal

Sanduhr Desk sorts the desktop into drawers: themed panels on the Desk, each showing a real folder, that
open, fold and hide. Built-in rules, an agent, or the user's own hand fill them. Nothing moves without
review (the user's own drags excepted), every move can be undone, and turning drawers off puts every
file back on the Desktop.

**Success:** someone with no agent installs Sanduhr, picks Just drawers, accepts the first rule plan and
has a clean desktop in under two minutes. They never see a sign-in screen or a meter.

## Out of scope

- Frames around the real desktop icons (stage 2).
- Deleting, overwriting or renaming files by rule or by agent. Moves are the only plan action.
- Any network destination. Drawers are local.
- Drawers on the Mac.

## 0. The spike (blocks everything else)

A throwaway project at `windows-dotnet/spikes/drawer-island/`, run on Windows 11 24H2 or later, with
mixed DPI, through an Explorer restart. On a Desk window that is layered and never activates
(`DesktopLayerHost`'s styles), it must prove:

1. It accepts an OLE drop from Explorer (`RegisterDragDrop`).
2. It starts an OLE drag that Explorer accepts as a move.
3. It shows Explorer's own context menu for an item (`IContextMenu` from the Shell).
4. It takes keyboard focus on a click into the drawer and gives it back on a click away.
5. It is still a drop target after Explorer restarts and the Desk re-parents.

A written verdict goes in `windows-dotnet/docs/checklist.md` as the WorkerW spike's did. On a fail, stop and
escalate with routes; do not build on a guess.

## 1. The move plan (Core, pure)

A **plan** is a list of drawers and moves, plus its source.

- A **drawer**: name, home folder, place on the Desk (a layout word).
- A **move**: source path and target drawer.
- **Source**: `rules`, `agent:<name>` or `user`.

A plan never holds a delete, an overwrite, or a path outside the allowed roots.

**Allowed roots.**

- **Sources:** the user's Desktop, the Public Desktop, and drawer homes.
- **Targets:** drawer homes only.

Core refuses the whole plan if any entry is outside them. That covers system folders, Program Files,
another user's profile, network paths, and a reparse point (link or junction) that resolves outside the
roots. The roots are checked when the plan is validated, and again by the executor just before each move,
so a link swapped in between is caught.

The Public Desktop (`C:\Users\Public\Desktop`, where installers drop shortcuts for every user) can only be
written with administrator rights, and Sanduhr never elevates. Its items are listed, but a plan skips
them, and the review names them: "3 shortcuts are shared with every user and need an administrator to
move." The rules do not match them.

**How each source makes a plan.**

- **Agents:** the `propose_drawers` MCP tool writes `drawers-request.json` to `%USERPROFILE%\.sanduhr`.
  This is the same handoff as themes and widgets (`WidgetReviewService`): the app watches the file, Core
  validates it again, and the server is never trusted.
- **Rules:** a Core function, `DrawerRules.Plan(listing, rules)`, turns the current desktop listing and the
  user's rule set into a plan.
- **The user:** no plan. A drag is one move that applies at once and goes straight to the journal.

**Review.** The plan previews on the Desk: drawers appear with their contents, and items still waiting to
move are drawn faded. The review bar (`DeskReviewBar`) reads "14 files into 3 drawers" with Apply, Edit
and Dismiss. Edit unticks single moves or renames a drawer before Apply. Only one review is open at a time,
the same `DeskBusy` rule a widget review follows.

**Undo.** Each applied plan, and each move the user makes by hand, is one journal entry. Undo puts every
file back where it came from. A file that has since moved or changed is skipped and named in the result.
Undo never overwrites.

## 2. The drawer on the Desk

**A Desk piece.** A drawer uses the existing layout grammar with its id in the widget slot:
`drawer.<id>:<place>:<scale>[:tilt]`, for example `drawer.screenshots:tl:1`. (`drawer:<name>` would have put
the name where the grammar reads the anchor.) Drawers stack and sit side by side as other pieces do. Each drawer is
its own island with `TakesClicks` set; the rest of the Desk stays click-through.

**Themed in full.** A drawer draws from the Desk look in force: `ThemeDeskLook.From(theme)` when Match the
widget theme is on, otherwise the Desk's own look.

| Part | From |
|---|---|
| Frame | the plate (color, shadow) |
| Title | the ink |
| Item labels | the ink, at body size |
| Hover and selection | the accent |
| Font | the Desk font |

Explorer's context menu is the one surface that is not ours.

**States.**

- **Open:** a grid of Shell icons and thumbnails (`IShellItemImageFactory`), each with its name, scrolling
  inside the frame when full.
- **Folded:** the title bar only, with a count ("Screenshots · 14"). A click on the title opens or folds
  the drawer.
- **Hidden:** gone from the Desk. It comes back from the Desk menu ("Show drawers") or from Settings.
  Its files stay where they are and Explorer can still reach them.

**Interaction.** Double-click opens through the Shell. Right-click shows Explorer's menu, plus a Sanduhr
section: Rename drawer, Fold, Hide, and Drawer settings… (a page name, as every Settings entry point is).
Dragging out moves the file to the drop target, dragging in moves it into the drawer, and dragging between
drawers moves it from one to the other. All three go through the journal. A drag out to Explorer is Explorer's own move (its undo
covers it): Sanduhr never learns where the file landed, so it is not journaled. A Desk design never moves, hides or drops a drawer. Renaming a file goes through the
Shell. The keyboard works only after a click into the drawer, which takes focus then and gives it back on a
click away (spike item 4).

**Live.** A `FileSystemWatcher` on each drawer's home keeps the grid current when files change outside
Sanduhr, debounced as the Desk's other watchers are. Explorer restarts follow the Desk's re-parent path.

**Accessibility.** Each item's accessible name is its file name. Each drawer is a named group:
"Screenshots drawer, 14 items". This follows the 10.1.4.4 rule that every control has an accessible name.

## 3. Rules and the front doors

**Built-in rules.** Each rule is a filter plus a target drawer:

- **By kind:**
  - Screenshots: images whose name matches the system screenshot pattern.
  - Images.
  - Documents.
  - Installers (`.exe`, `.msi`, `.msix`).
  - Archives.
  - Shortcuts.
- **By age:** "Older than 30 days" takes what no earlier rule took and has not been touched in the window.
  The window can be changed.
- **By name:** contains or starts with, for example "invoice" to Receipts.

Rules run top to bottom and the first match wins. The user can reorder them by dragging, and turn off or
delete any of them. Unmatched files stay on the Desktop; there is no Misc bucket. The rule set is plain
data in `settings.json`, and the engine is pure, so it is tested against invented listings. The MCP reads
the rule set through `get_drawers`, so an agent can build on the user's rules rather than ignore them.

**Auto-tidy.** The "Keep my desktop tidy" switch appears only after a rule plan has been accepted once. With
it on, a new file that matches a rule moves into its drawer after a ten-second settle, so a download or a
screenshot is never taken from under the app still writing it. A toast reads "Moved 1 file to Screenshots",
with Undo. Auto-tidy skips review; it is off by default.

**The three front doors.** First run asks once, before any sign-in:

- **Just drawers:** the Desk turns on with only drawers on it. The built-in rules run once, and their plan
  is shown for review. This is the success path.
- **Desk:** the Desk as it is today (clock, message, weather), with drawers available but not run.
- **Desk and usage meters:** today's full setup, sign-in included.

All three can be changed later in Settings. The widget does not open until usage meters are chosen.

**Existing users.** An upgrade skips the doors and changes nothing. A What's New card ("Drawers: let
Sanduhr Desk sort your desktop") opens Settings at the Drawers page. Nothing moves until the user asks.

## 4. Safety, errors and privacy

**One file at a time.** A plan applies file by file through `IFileOperation`, and the journal records each
move as it lands. A failure skips that file and the plan carries on. The result line names it: "13 moved,
1 skipped: report.docx is open in Word". There are no retry loops and no half-applied file.

**Name clashes.** A clash is renamed the way Explorer renames, "report (2).docx". The journal keeps the new
name so that undo restores the old one. Nothing is overwritten.

**Cloud files.** OneDrive placeholders move as placeholders, and the Shell handles that with OneDrive. With
the default root the drawer homes sit inside the Desktop, so a synced Desktop stays synced. When the user
moves the root out of a synced Desktop, the first plan after says so in one line naming the folders, before
Apply.

**Homes.** Each drawer has its own home. The default root is a folder on the Desktop itself,
`Desktop\Drawers`, so homes are `Desktop\Drawers\<name>`. This costs one folder icon on the desktop and buys
the uninstall case below: whatever happens to Sanduhr, the files sit in one visible folder on the Desktop.
The root can be changed in Settings. A drawer can instead point at the Desktop or at an existing folder
(Downloads, a project folder), and then shows that folder in place, as a view. The root folder itself is
never a plan source, so no plan can move it into a drawer. The first time drawers are turned on, one line
says where drawer files live: "Drawer files live in the Drawers folder on your Desktop."

**The journal.** It lives in Sanduhr's data folder, kept apart from the Velopack install tree. It holds
paths only, never contents: the last 50 plans, and the user's own moves for 30 days. The MCP sees a summary
(counts, drawer names), never paths.

**Turning drawers off puts everything back.** Turning drawers off asks first, with a count: "Put 42 files
back on the Desktop?" Then:

- Every Sanduhr-owned home (under the Drawers root) empties back onto the Desktop, files and folders alike,
  whether the journal moved them or the user dragged them in through Explorer. Clashes are renamed.
- An emptied home is removed, and then the Drawers root when it is empty too. A home that still holds a file
  that could not move (open or locked) is kept, and the result says why.
- Drawers pointed at existing folders are views, so their contents stay where they are.

Reset's Drawers part runs the same return first, then clears the rules, the journal and the drawer
settings. Reset never deletes a user's file.

**Uninstall.** A Setup.exe install runs the same return from Velopack's `OnBeforeUninstallFastCallback`
(`Sanduhr.App/Program.cs`), best-effort: that callback must be quick, so a locked file keeps its home. A
Store package cannot run code on removal, so its files stay in the Drawers folder on the Desktop, in plain
sight. Settings (Drawers page) and the listing say so.

**Privacy.**

- There is no new network destination; `NetworkDisclosureTests` stays green.
- Agent access is new reach: `propose_drawers` and `get_drawers` let an agent see file names on the Desktop
  and in drawer homes. It sits on the Privacy page behind its own switch, "Let agents see and sort my
  desktop". The switch is off by default and is turned on through the same consent prompt the MCP uses
  today. With it off, both tools answer: "Turned off in Settings, Privacy."
- Streamer mode shows drawer names as codenames and blurs file labels.

**Store review.** The reviewer letter gets a paragraph on drawers: moves only, never deletes, one undo log,
nothing runs until the user approves it, everything returns to the Desktop when drawers are turned off, and
agent access is off by default.

## 5. Testing and order of work

**Core** (most of the coverage):

- **Plan validation:** roots, reparse points leading outside, no delete, and a refusal of the whole plan.
- **The rule engine** on invented listings: order, first match, unmatched files left alone, and age
  windows.
- **The journal:** round trip, the 50-plan and 30-day trims, and undo skipping a changed file.
- **Turning off:** owned homes return and views stay, and clash renames.
- **Decoding:** the `drawer:` layout word, and settings, including an upgrade with no drawers key.

**App**, in a sandbox folder, never the real Desktop:

- Moves through `IFileOperation`.
- A locked file reported and skipped.
- The OneDrive warning when sync states differ.
- The review flow and `DeskBusy`.

**MCP:**

- `propose_drawers` and `get_drawers` through the request and result files.
- `TrustBoundaryTests` gains exactly one source-linked validator, changed in the same commit.
- The off-by-default answer.

**Guards that stay green:** `SettingsSourceScanTests` (each new setting is one literal path ending at a
section heading), the Settings index and search, the Privacy page tests, and streamer-mode codenames.

**RenderHarness:** a `drawers` scenario with an invented desktop, for screenshots in obsidian and aurora.
File names are made up and pass the `AssertNoUserName` guard.

**Order:**

1. The spike, with its verdict.
2. Core: plan, rules, journal, turn-off.
3. The drawer island and its theming.
4. Moves and review.
5. Turning off and Reset.
6. The three front doors and the What's New card.
7. The MCP tools and the Privacy switch.
8. Harness screenshots.

## Open questions for later specs

- Spec 4 owns the drawer word in the Store listing and the Partner Center name check for "Sanduhr" and
  "Sanduhr Desk". These depend on the dashboard Architect's naming and trademark ruling.
- Stage 2 (icon frames) gets its own spike on `SysListView32` control: position writes across processes,
  surviving a refresh and an Explorer restart, and mixed DPI.
