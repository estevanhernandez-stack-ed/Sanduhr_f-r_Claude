# Drawers and Set up Sanduhr on the Mac (parity with Windows 4.5.0)

Spec, 2026-11-01, a draft for the owner's calls at the end of this file. Dashboard task: "Mac: parity with
Windows 4.5.0 (Set up Sanduhr, Drawers)". Windows reference: 4.5.0 as built in `windows-dotnet/`
(read it, never edit it), and its runs `docs/cart-runs/2026-10-31-setup-4-5/` and
`docs/cart-runs/2026-11-01-drawers-look-4-5/`. Line references below are to `windows-dotnet/src/`.

Drawers move the user's files. Every other Desk piece only draws. That makes section 2 (what may
move, and how it comes back) the part of this spec that can lose someone's work. It is written
first, and built and tested first.

## Where the Mac starts

- **Just the Desk** (BACKLOG #4, 2.15.0): the welcome sheet's second button, a Desk-only tour, and
  Claude pieces that hide while there is no account. What's missing is Windows' **usage off with an
  account kept**: the Mac today is either signed in and fetching, or signed out.
- **The Desk** draws under the icons (`DeskLayer`). Clicks land through `DeskClickLayer`, a layer
  above the icons. Pieces are placed with `widget:anchor[:scale]` words. `get_desk` and
  `propose_desk` (2.37.0) let Claude read and shape the layout.
- **Not sandboxed, Developer ID.** Reading `~/Desktop` is guarded by TCC's Desktop Folder
  permission. Sanduhr has never touched the Desktop's files, so nobody has granted it yet.
- **No file-moving code anywhere in the Mac app.**

## Scope

Two releases. Drawers come first, because Set up's "Tidy my files" choice turns them on.

| Release | What | Windows ref |
|---|---|---|
| **2.40.0: Drawers** | Drawer panels on the Desk, each showing a real folder. Rules that sort the Desktop after a review. Undo per run. Turning drawers off puts every file back. The four looks, placement and snapping. | 4.5.0 Drawers |
| **2.41.0: Set up Sanduhr** | One screen of free choices replaces the welcome sheet, **Track my AI usage** off with the account kept, and **Copy prompt** with a paste-back block for any AI chat. | 4.5.0 Set up |

Left out: **Keep clear of icons**. Windows reads Explorer's icon positions; on the Mac that needs
AppleScript to Finder and its Automation prompt, for a nicety (owner call 3). Also left out, as on
Windows: a `propose_drawers` MCP tool, and Claude seeing any file name.

---

## Release 2.40.0: Drawers

### 1. What a drawer is

- **An owned drawer** is a folder Sanduhr made: `<root>/<Name>`. The default root is
  `~/Desktop/Drawers` (`DrawerSettings.cs:54`). This keeps every move on the Desktop's own volume,
  and inside its iCloud container when Desktop & Documents sync is on, so a move is a rename and
  never a copy. The seven default drawers are Windows' (`DrawerRules.cs:36-40`), with the Mac's
  names where they differ (section 2).
- **A view drawer** is any existing folder the user adds to look at. Its contents are never moved
  by Sanduhr, and never put back.
- **Choosing another root** is allowed only while no owned drawer exists, as on Windows. The root
  must be on the Desktop's volume, must not be the Desktop or a folder that holds it, and must not
  be `/`, a volume root or the home folder. Different volumes are refused, not copied.
- **Limits per plan:** 24 drawers and 2,000 moves (`DrawerPlanValidator.cs:77-78`).

### 2. What may move

**From the Desktop only:** the direct children of `~/Desktop`, resolved to real paths. Hand moves
may also go from one drawer to another. Nothing else is ever a source.

**Never moved by a rule:**
- folders, packages (`.app`, `.pages` bundles, `.photoslibrary`: anything `isPackage`) and volumes;
- hidden items: dotfiles, the `isHidden` flag, `.DS_Store`, `.localized` and `Icon\r`;
- symlinks;
- the Drawers root, any drawer folder, and anything inside them;
- iCloud items that are not downloaded (dataless): they are left in place and listed as "in iCloud,
  not downloaded". A same-container rename would work, but a dataless item's size and dates can be
  stale, and the Older files rule would misfire.

**The built-in rules,** in this order, first match wins, all on by default. A file no rule matches
stays where it is.

| # | Rule | Mac match | Windows |
|---|---|---|---|
| 1 | Screenshots | The screen-capture flag (`kMDItemIsScreenCapture`, set by macOS on screenshots and screen recordings in any language), or an image whose name starts with "Screenshot" or "Screen Shot" | Image named "Screenshot…" |
| 2 | Images | `.png .jpg .jpeg .gif .bmp .webp .heic .heif .tif .tiff .svg` | same, plus `.heif` |
| 3 | Documents | `.pdf .doc .docx .xls .xlsx .ppt .pptx .txt .md .rtf .odt .ods .odp .csv .pages .numbers .key` (the last three when saved flat; bundles are packages and stay) | same, plus iWork |
| 4 | Installers | `.dmg .pkg .mpkg` | `.exe .msi .msix…` |
| 5 | Archives | `.zip .7z .rar .tar .gz .tgz .bz2 .xz .iso` | same |
| 6 | Links | `.webloc .inetloc`, and Finder aliases (`isAliasFile`) | `.lnk .url .pif` |
| 7 | Older files | Not modified for more than 30 days (1 to 3,650) | same |

**Custom rules:** name contains, or name starts with, compared case-insensitively. Rules can be
turned on and off, reordered and removed, as on Windows.

**Applications on the Desktop stay.** An `.app` is a package, so no rule takes it, and the Installers
rule never reaches it.

### 3. Consent: nothing moves without a review

- **Sort my desktop now** (Settings, Drawers) builds a plan and opens a review bar on the Desk: "N
  files into M drawers", each drawer with up to three names, and **Apply**, **Edit** and **Dismiss**
  (`DrawerReview.cs:31-60`). Edit unticks files and renames new drawers.
- **Apply checks the plan again first.** Every source must still exist with the same size and
  modification date, every path must still resolve inside the Desktop or a drawer, and the root must
  still be safe. Whatever fails is dropped and named in the result line.
- **No watching the Desktop.** Sanduhr never sorts on its own. Windows' `auto_tidy` setting is
  stored but nothing reads it (verified 2026-11-01). The Mac does not add the setting.
- **Hand moves** (a file dropped onto a drawer) move at once and are journaled as `user`, as on
  Windows (`DrawerService.cs:266-276`).

### 4. Moving a file safely

- **One move = one `renamex_np(from, to, RENAME_EXCL)`.** It is atomic on the same volume, keeps
  every attribute, tag and date, and **refuses to replace an existing file at the kernel level**.
  On `EEXIST`, the next free Finder-style name is tried ("Report 2.pdf", then "Report 3.pdf"), up to
  99 tries. Then the file is skipped. Nothing is ever copied, and no file is ever overwritten or
  deleted.
- **Cross-volume moves are refused** (`EXDEV`), never turned into copy-and-delete. The root rule in
  section 1 means this only happens when a path changed under Sanduhr. In that case the file is
  skipped and named in the result line.
- **Each path is checked right before its move:** the source is resolved with symlinks followed,
  must be a direct child of the Desktop or a drawer, and must still be a regular file (for a rule
  move).
- **Open files move.** On macOS an app keeps its open file across a rename, so there is no
  locked-file case to handle. (Windows needs Restart Manager for this.)
- **Where a file ended up is read back** after the rename, by name and inode, and that is what the
  journal records.

### 5. The journal, Undo, and turning drawers off

- **The journal** is `~/Library/Application Support/Sanduhr/drawers-journal.json`, owner-only
  (0600), written atomically. It uses Windows' format, so a future config pack can carry it:
  `{"entries":[{"id","at","source":"rules|user","moves":[{"from","to","folder","written"}]}]}`.
  It keeps the last 50 runs, and user moves for 30 days.
- **Better than Windows here:** the entry is written **before** the first move, with every move
  marked `written: false`. Each move flips its flag, and the file is saved every 25 moves and at
  the end. If Sanduhr crashes mid-run, the next launch finds the open entry and offers Undo for the
  moves that happened. (Windows writes the entry once, after the run, so a crash leaves no Undo.)
- **Undo per run:** from the result bar right after Apply, and from Settings, Drawers, which lists
  the last 10 runs. Moves are reversed newest first.
  - A file that is gone is skipped: "was moved since".
  - A file whose modification date changed is skipped: "changed since".
  - An original place that is taken gets the next free name. Undo never overwrites.
- **Turning drawers off** asks "Put N files back on the Desktop?", then empties **every owned drawer
  folder** onto the Desktop. That includes files the user put there by hand, and files moved before
  any crash; it does not read the journal (`DrawerReturn.cs:15-40`). Name clashes get the next free
  name. Each folder that ends up empty is removed, and the root too when it is the default and
  empty. A folder that still holds something stays, with its drawer listed, and turning off again
  finishes the job. Rules are kept. View drawers are never emptied.
- **Reset Sanduhr…** runs the same return first, then removes the drawer settings and the journal,
  and says so if anything stayed behind.
- **Uninstall.** A Mac app has no uninstall hook, and the Homebrew cask's `zap` must not move user
  files. Settings, Drawers says: "Before you remove Sanduhr, turn Drawers off to put your files back.
  Otherwise they stay in the Drawers folder on your Desktop." Same as Windows' Store note.

### 6. The permission

- The first read of `~/Desktop` raises macOS's "access files in your Desktop folder" prompt. It
  happens only when the user turns Drawers on or presses Sort, never at launch. The purpose string,
  `NSDesktopFolderUsageDescription`: "Sanduhr looks at the files on your Desktop to sort them into
  drawers, only when you ask it to."
- **Denied:** Settings, Drawers says so, with a button to the Files and Folders pane, and the
  drawers show "Sanduhr can't see your Desktop." Nothing is retried in a loop.
- **A view drawer** under `~/Documents` or `~/Downloads` raises that folder's prompt when it is
  added, and is handled the same way.

### 7. What a drawer looks like

- **A panel per drawer,** in its own borderless window at the `DeskClickLayer` level. Each panel has
  a title (name and count) and a grid of thumbnails with names. Thumbnails come from
  `QLThumbnailGenerator`, at icon size, cached by path and modification date. Folders sort first,
  then names, as Finder sorts them (`localizedStandardCompare`). Hidden items are not shown.
  Extensions follow the Finder's "Show all filename extensions" setting.
- **Folded:** "Screenshots · 14". Clicking the title folds and opens; dragging the title moves the
  panel. The first two new drawers arrive open, the rest folded.
- **Mouse and keys:**
  - double-click or Return opens the item (`NSWorkspace.open`);
  - Space shows Quick Look;
  - Command-R reveals the item in Finder;
  - drag out to the Finder or another app, drag in from the Desktop or another drawer;
  - Command-Delete moves the item to the Trash (`FileManager.trashItem`, so the Finder's Put Back
    works), the only way a file leaves a drawer other than a drag;
  - the context menu has Open, Quick Look, Show in Finder, Rename, Move to Trash, then Rename
    drawer, Fold, Hide, Look, Tidy drawers and Drawers Settings….
- **The four looks,** one for all drawers, with per-drawer overrides as on Windows:
  - **Solid:** the theme's surface at the opacity.
  - **Glass:** `NSVisualEffectView`, `.hudWindow` material, behind-window blending.
  - **Outline:** a 0.7-alpha border.
  - **Bare:** a border on hover only.
  Opacity runs 20 to 100, default 55. Outline and Bare keep a 0.02 fill so clicks land.
- **Placement:** 360 points wide, 330 open, 42 folded (`DrawerPlacement.cs:10-14`). Positions are
  saved per screen as an offset from its visible frame. Panels snap within 12 points to other
  drawers, then to Desk pieces, then to the screen margin; Option-drag turns snapping off. **Tidy
  drawers** lines them up from a chosen corner, with Undo. A title bar never leaves the screen.
- **Idle cost:** folder changes arrive through one FSEvents stream over the drawer folders only,
  never the Desktop, and redraw only the panel whose folder changed. No timers. The idle-CPU budget
  (BACKLOG #7) is checked with Drawers on.
- **Turning Drawers on** turns the Desk on too, as on Windows.

### 8. Settings, state and privacy

- **Settings, Drawers:**
  - the switch, the root and the Sort button;
  - the rules list, the Older files days and a custom rule field;
  - the look, the opacity and the Tidy corner;
  - recent runs, each with Undo;
  - the uninstall note.
- **Preferences** live in `com.626labs.sanduhr.desk` under `drawers.*`: enabled, root, the drawer
  list (id, name, home, owned), rules, looks, places, folded, hidden.
- **`state.yaml` `drawers:`** gets the switch, the drawer count, the files per drawer as numbers, the
  permission status, and whether a review or an open journal entry is pending. **Never a file name
  or a path.**
- **`desk-state.json` and `get_desk`** report no drawers: no names, no files. The MCP gets no drawer
  tool, and its trust boundary is unchanged.
- **PRIVACY, Mac section:**
  - Sanduhr reads the names, types, sizes and dates of the Desktop's top-level items, and the
    contents of drawer folders, only to sort and show them.
  - The journal records full paths, so file names, on this Mac (owner-only, kept as above, removed
    by Reset).
  - Nothing about any file is logged, sent or shown to Claude.
  - The Desktop Folder permission, and how to revoke it.
  - Moves, never deletes. Move to Trash happens only from the user's own command.

### 9. Tests and checks

- **Pure core, Swift testing** (no AppKit). Covers:
  - the rule matcher, each rule against a table of names and flags;
  - the plan validator: the root rules, sources outside the Desktop, packages, hidden items,
    dataless items and the limits;
  - free-name choice;
  - the return plan;
  - journal round-trip, trim and crash recovery;
  - Undo's skips.
- **A temp-folder integration suite** stands in for the Desktop: real files and `renamex_np`, with
  clashes on both sides, a file edited between plan and Apply, a drawer folder deleted by hand, a
  crash simulated after N moves, and the return after a mixed rules-and-hand history.
  **Checklist line:** after any sequence, every starting file exists exactly once, at its start path
  or in a drawer, with its bytes unchanged.
- **Smoke:**
  - a scenario sorts a fixture Desktop under `SANDUHR_DRAWERS_DESKTOP=<dir>`, a debug-only
    override that is refused in release builds, and checks `state.yaml` counts;
  - `docs/mac-smoke-test.md` gets a live section on a real Desktop: sort, Undo, a hand drop, Trash,
    then off with everything back.

---

## Release 2.41.0: Set up Sanduhr

### 10. The screen

- **Choices:** **A nicer desktop** (the Desk's pieces), **Tidy my files** (Drawers),
  **Track my AI usage** (Claude). First-run defaults are desktop and drawers on, usage off, as on
  Windows (`SetupSettings.cs:48`). At least one choice must be ticked. Unticking a choice that is on
  shows what will change first.
- **When it opens:**
  - on a new install, in place of today's welcome sheet;
  - on upgrade, only for someone with no account who never chose Just the Desk;
  - from the menu bar item's **Set up Sanduhr…**, from Settings, General, and from the What's New
    card **Choose what Sanduhr shows**.
- **Continue** applies the choices in Windows' order (`SetupService.cs:47-96`):
  - drawers off first, with the put-back question;
  - then the Desk;
  - then usage;
  - then drawers on, opening a first sort review only if no rules run was ever accepted.
- **Just the Desk folds in.** The screen's "desktop on, usage off" is the same state. The
  `justTheDesk` key migrates to `setup` and is then read only for upgrades.

### 11. Usage off, with the account kept

- **Fetching stops.** The widget hides, the menu bar shows the hourglass alone, and the Desk's
  Claude pieces hide as they do while signed out.
- **The snapshot file goes.** `snapshot.json`, which the statusline, the meters mod and the MCP's
  `get_usage` read, is deleted, so those show their signed-out state. **The account and its Keychain
  item stay.** Turning usage on again fetches at once.
- **Usage, Alerts and Claude Code** say "Usage tracking is off. Turn it on in Set up Sanduhr."
  instead of the sign-in line.

### 12. Design it with an AI chat

- **Copy prompt** puts a prompt on the clipboard for any chat, written from `SetupPrompt.cs:57-103`
  with the Mac's Desk words. It asks what the person likes, and how they keep their files, then
  returns one JSON block. **The prompt never contains a file name, a path or a usage number.**
- **The paste-back block** is Windows' shape, so the same answer works on either platform:
  - `{"desk": {...}, "drawers": {"rules": [...], "look", "opacity", "arrange": {"corner", "direction"}}}`;
  - limits from `PastedSetup.cs:36-41`: 1 to 20 rules, text up to 60 characters, days 1 to 3,650,
    drawer names up to 40 characters and valid as folder names, a paste up to 200,000 characters.
- **The `desk` half** goes through the `propose_desk` validator, the same checks Claude's proposals
  pass.
- **One bad field rejects the whole block, and the line says which field.**
- **Buttons:**
  - **Preview on my Desk**;
  - **Use these drawer rules**: replaces the rules and opens a review, so nothing moves before Apply;
  - **Use these drawer settings**: look, opacity and a Tidy, with Undo.

### 13. Release, docs, privacy

Each release follows section 16 of `docs/mac-widgets-spec.md`:
- PRIVACY rows in the same release;
- `mac/README.md` and smoke sections;
- a What's New card and its `WhatsNewTests` lines;
- CHANGELOG under `## Unreleased (mac)`.

The cards:
- **2.40.0:** "Drawers for your Desktop", destination Settings, Drawers.
- **2.41.0:** "Choose what Sanduhr shows", which opens Set up Sanduhr.

---

## Owner calls (open)

1. **Two releases, Drawers first** (2.40.0, then 2.41.0)? Or one 2.40.0 with both, as Windows
   shipped them? Recommended: two. Drawers is the risky half, and it should be live on a real
   Desktop for a few days before Set up starts turning it on for new users.
2. **Default root `~/Desktop/Drawers`,** as on Windows? It keeps moves on one volume and in one
   iCloud container, and the folder is plainly visible on the Desktop. Recommended: yes.
3. **Leave out Keep clear of icons** (it needs AppleScript to Finder and an Automation prompt)?
   Recommended: yes, revisit if drawers land on top of icons in practice.
4. **Command-Delete to the Trash** inside a drawer? Windows has Delete through Explorer. Recommended:
   yes. It goes to the Trash with Put Back, and it is the only delete-like action anywhere.
5. **Crash-safe journal** (written before the moves, section 5): the Mac's own improvement, to hand to
   the Windows agent afterwards? Recommended: yes.
