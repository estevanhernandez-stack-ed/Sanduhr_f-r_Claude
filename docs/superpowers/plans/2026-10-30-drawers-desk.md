# Drawers, plan 2 of 3: drawers on the Desk Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Drawers work on the real Desk: themed panels showing real folders that take drops, drags, Explorer's menu and the
keyboard, filled by the built-in rules through a review with Edit and Undo, with every file going back to the Desktop when drawers
are turned off, on Reset and on a Setup.exe uninstall. All of it is reached from a Settings Drawers page.

**Architecture:** Plan 1 built the pure rules (`Sanduhr.Core`: plan check, rule engine, journal, undo, return, settings, layout
word). This plan adds the App side in three layers. (1) Shell interop (`Interop/Shell*.cs`), ported from the spike's proven code:
`IFileOperation` moves, Explorer's `IContextMenu3`, thumbnails, Restart Manager and final paths. (2) `DrawerMover` runs checked moves
one file at a time with a final-path root check before each, and `DrawerService` holds every drawer's live state (items, fold,
watchers, icons) across Desk redraws, the way `CompanionService` holds the companion. (3) The piece: `DeskView` draws a `drawer.<id>`
word as a `DrawerPiece`, an island holding one takes clicks and focus, and the review reuses `DeskReviewWindow` with an Edit button.
Plan 3 adds the three front doors, the What's New card, `propose_drawers` / `get_drawers` with the Privacy switch, auto-tidy, the
RenderHarness scenario and the reviewer letter.

**Tech Stack:** .NET 10, C# 13, WPF, xUnit (`Sanduhr.Tests` for Core, `Sanduhr.App.Tests` with the `Sta.Run` helper for WPF and COM),
hand-written COM interop (`[ComImport]`, `DllImport`), as `DeskNative` and the spike do.

**Spec:** `docs/superpowers/specs/2026-10-28-drawers-design.md`. Also read: plan 1 (`docs/superpowers/plans/2026-10-29-drawers-core.md`)
for the Core API, and the spike's verdict (`windows-dotnet/spikes/drawer-island/VERDICT.md`) for the four Win32 rules Task 8 carries.

## Global Constraints

- Moves are the only thing a plan does: no delete, no overwrite. A clash takes the Explorer-style name `name (2).ext` (`DrawerNames.Free`), and the Shell call also carries `FOF_RENAMEONCOLLISION` as a second guard. The user's own Delete key and Explorer's menu are the Shell's and are not plans.
- Allowed sources: the user's Desktop and drawer homes. The Public Desktop is listed but never moved. Allowed targets: drawer homes; the Desktop as well for undo and return only. Every move checks the **final** paths (junctions, links and 8.3 names resolved) just before it runs (`DrawerPaths.MoveAllowed`).
- One file per Shell operation. A failure skips that file, the run carries on, and the result line names it: "13 moved, 1 skipped: report.docx is open in Word."
- No new network destination. `NetworkDisclosureTests` stays green; a new `Process.Start` that opens a local file carries the marker the test asks for (the Launcher's launch line in `DeskController.LaunchButton` is the precedent).
- **Streamer reach:** every new App line that reads a path, a file name (`Path.GetFileName`), `_paths.*`, an exception's `.Message`, or builds a spoken name from a non-literal carries a `// streamer: <id> - <why>` marker on the line or within the three lines above (ids: masked, key, export, demo, cant, notname). After each App task, run `StreamerReachTests` and add the new file's `(file, id, count)` rows to `Known` in `tests/Sanduhr.Tests/StreamerReachTests.cs` exactly as the failure lists them. Drawn drawer names go through `StreamerMode.Mask.Name(CodenameCategory.Folder, name)` (marker `masked`); drawn file labels blur while streamer mode is on (marker `masked`); a path used only for IO is `notname` or `key`.
- Every Core type lives in `namespace Sanduhr.Core;`, every App type in its folder's namespace (`Sanduhr.App.Desk` for `Desk/` and `Desk/Drawers/`, `Sanduhr.App.Interop` for `Interop/`), file-scoped, with `/// <summary>` docs in the house style (what and why, one or two sentences).
- User-facing strings: no em dash, sentences end with a period, Settings entry points are page names ending in "…" (`SettingsNames.LinkTitle`). Feature name is **Drawers**; never "fence" or "Fences".
- Core stays IO-free in the drawer files: no `File.`, `Directory.`, watchers, network or process calls; the file system arrives as delegates.
- App tests touch only sandboxes under `Path.GetTempPath()`, never the real Desktop. A test that needs a junction uses `[SkippableFact]` and skips when `mklink /J` fails.
- The working tree is CRLF with an LF index: edit with the Edit tool or a Python rewrite, never `sed -i`.
- Build and test from `windows-dotnet/` with `--disable-build-servers -nodeReuse:false`. Quit any running `Sanduhr.exe` before building the App.
- Every commit message ends with the line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **A file in a reviewed plan is gone or renamed before Apply** (the user tidied by hand while the bar was up). Expected: that file is skipped with "was moved since." and the rest move. Pinned in Task 5.
2. **A drawer's folder is deleted or renamed in Explorer while the drawer shows.** A `FileSystemWatcher` on a missing folder throws at creation. Expected: the drawer shows "This drawer's folder is gone." and nothing crashes, at start and while running. Pinned in Task 6.
3. **The five-minute full redraw lands while the user is mid-interaction** (an inline rename typed, Explorer's menu open, a drag in flight). Expected: the redraw waits until the interaction ends; nothing typed is lost. Pinned in Task 7.
4. **A drop that goes nowhere:** a file dragged out of a drawer and dropped back on the same drawer, or dropped on the drawer whose folder already holds it. Expected: nothing moves and no "a (2).txt" copy appears. Pinned in Tasks 6 and 8.
5. **Turning off while a drawer file is open in Word.** Expected: every other file returns, the locked file's home and the Drawers root stay, the drawer stays listed so turning off again retries, and the result names the file and the app. Pinned in Task 10.

## File map

| File | Task | Responsibility |
|---|---|---|
| `src/Sanduhr.Core/DeskArrangement.cs`, `DeskDesign.cs`, `DeskDesignApply.cs` | 1 | Designs carry drawer pieces through and never move, hide or drop one |
| `src/Sanduhr.Core/DrawerModel.cs`, `DrawerPieces.cs`, `DrawerPlanValidator.cs`, `DrawerJournal.cs`, `DrawerSettings.cs` | 2 | Plan 1's deferred hardening, the final-path rule, skip reasons, folded drawers |
| `src/Sanduhr.Core/DrawerReview.cs` (new), `DeskReview.cs` | 3 | Review and result copy, plan edits, the OneDrive line, where new drawers arrive |
| `src/Sanduhr.App/Interop/ShellNative.cs`, `ShellFiles.cs`, `ShellMenu.cs`, `DarkMenus.cs` (new) | 4 | The Shell: moves, renames, final paths, the app holding a file, thumbnails, Explorer's menu, dark menus |
| `src/Sanduhr.App/Desk/Drawers/DrawerMover.cs`, `DrawerJournalStore.cs` (new) | 5 | Runs moves, undo and return file by file; the journal on disk |
| `src/Sanduhr.App/Desk/Drawers/DrawerState.cs`, `DrawerIcons.cs`, `DrawerService.cs`, `DrawerPlaces.cs` (new); `Services/SettingsStore.cs` | 6 | Live drawer state across redraws, listings, watchers, icons, hand moves, busy holds |
| `src/Sanduhr.App/Desk/Drawers/DrawerPiece.xaml(.cs)` (new); `DeskView.cs`, `DeskClicks.cs`, `IDeskHost.cs`, `DeskController.cs`, `DeskArrangeMode.cs`, `DeskLookResolver.cs`, `App.xaml.cs` | 7 | The piece, themed, in stacks and in Arrange; redraws wait while a drawer is busy |
| `src/Sanduhr.App/Desk/DeskFocus.cs` (new); `DesktopLayerHost.cs`, `DrawerPiece.xaml.cs` | 8 | Focus on click, drops, drags, Explorer's menu, keys, inline rename; the live gate |
| `src/Sanduhr.App/Desk/Drawers/DrawerReviewService.cs`, `DrawerPlanEditWindow.xaml(.cs)` (new); `DeskReviewBar.xaml(.cs)`, `DeskReviewWindow.xaml.cs`, `WidgetReviewService.cs`, `DeskDesignService.cs` | 9 | Review with Edit, Apply's result with Undo, one review at a time |
| `src/Sanduhr.App/Desk/Drawers/DrawerUninstall.cs` (new); `DrawerService.cs`, `Services/ResetService.cs`, `Sanduhr.Core/ResetPlan.cs`, `Program.cs` | 10 | Turning off, Reset's part, the Setup.exe uninstall |
| `src/Sanduhr.App/Views/DrawersPage.xaml(.cs)`, `ViewModels/DrawersSettingsViewModel.cs` (new); `Sanduhr.Core/WhatsNew.cs`, `SettingsNames.cs`, `SettingsIndex.cs`; `Views/SettingsWindow.xaml`, `ViewModels/SettingsViewModel.cs`, `DeskClicks.cs`, `App.xaml.cs` | 11 | The Drawers page, the Desk menu's Show drawers and Drawer settings… |

---

### Task 1: Designs keep drawer pieces

Plan 1's final review (#8): `DeskDesign.ToArrangement` and `DeskDesignApply.Layout` keep only `DeskArrangement.Known` words, so the
first design applied after this plan places drawers would silently drop every drawer from the layout. The rule this task pins: **a
Desk design never moves, hides or drops a drawer.** `propose_desk` still refuses a drawer word (agents get `propose_drawers` in
plan 3).

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/DeskArrangement.cs` (next to `Known`)
- Modify: `windows-dotnet/src/Sanduhr.Core/DeskDesign.cs:662-668` (`ToArrangement`)
- Modify: `windows-dotnet/src/Sanduhr.Core/DeskDesignApply.cs` (`Settings`, `Layout`)
- Modify: `docs/superpowers/specs/2026-10-28-drawers-design.md` (section 2, Interaction)
- Test: `windows-dotnet/tests/Sanduhr.Tests/DeskDesignDrawerTests.cs` (new)

**Interfaces:**
- Consumes: `DrawerPieces.IsWord(string?)` (plan 1).
- Produces: `DeskArrangement.IsPiece(string widget) : bool`; `DeskDesignApply.WithDrawers(string layout, string current) : string`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>A Desk design never moves, hides or drops a drawer (plan 1's final review, #8).</summary>
public sealed class DeskDesignDrawerTests
{
    private const string WithDrawer = "clock:bl claude:bl drawer.screenshots:tl:1";

    private static DeskDesign D(string json) => DeskDesign.Parse(JsonNode.Parse(json)).Design!;

    private static DeskDesign Current(string layout) =>
        new() { Layout = DeskDesign.FromArrangement(new DeskArrangement(layout)) };

    [Fact]
    public void A_layout_word_can_be_a_drawer()
    {
        Assert.True(DeskArrangement.IsPiece("clock"));
        Assert.True(DeskArrangement.IsPiece("drawer.screenshots"));
        Assert.False(DeskArrangement.IsPiece("drawer."));
        Assert.False(DeskArrangement.IsPiece("folder"));
    }

    [Fact]
    public void Merging_a_proposal_keeps_the_drawers_the_Desk_has()
    {
        var merged = DeskDesign.Merge(Current(WithDrawer), D("""{"layout":{"clock":{"anchor":"tr"}}}"""));
        var s = DeskDesignApply.Settings(DeskSettings.Default with { Layout = WithDrawer }, DeskInkSettings.Default, LauncherModel.Empty, merged);
        var layout = new DeskArrangement(s.Desk.Layout);
        Assert.Equal(DeskAnchor.TR, layout.Placement("clock")!.Anchor);
        Assert.Equal(new DeskPlacement("drawer.screenshots", DeskAnchor.TL, 1), layout.Placement("drawer.screenshots"));
    }

    [Fact]
    public void A_saved_design_from_before_drawers_keeps_them_where_they_are()
    {
        // A library design saved before drawers existed names no drawer at all.
        var saved = D("""{"layout":{"clock":{"anchor":"tc"},"claude":{"hidden":true}}}""");
        var s = DeskDesignApply.Settings(DeskSettings.Default with { Layout = WithDrawer }, DeskInkSettings.Default, LauncherModel.Empty, saved);
        Assert.Equal(DeskAnchor.TL, new DeskArrangement(s.Desk.Layout).Placement("drawer.screenshots")!.Anchor);
    }

    [Fact]
    public void A_design_cannot_move_a_drawer_even_when_its_layout_names_one()
    {
        var moved = new DeskDesign { Layout = DeskDesign.FromArrangement(new DeskArrangement("clock:bl drawer.screenshots:br:1.4")) };
        Assert.Equal("clock:bl drawer.screenshots:tl:1",
            DeskDesignApply.WithDrawers(DeskDesignApply.Layout(moved.Layout!), WithDrawer));
    }

    [Fact]
    public void The_review_lists_no_change_for_a_drawer_the_design_leaves_alone()
    {
        var current = Current(WithDrawer);
        var lines = DeskDesign.Diff(current, DeskDesign.Merge(current, D("""{"plate":true}""")));
        Assert.DoesNotContain(lines, l => l.Contains("drawer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Propose_desk_still_refuses_a_drawer_word()
    {
        var parsed = DeskDesign.Parse(JsonNode.Parse("""{"layout":{"drawer.screenshots":{"anchor":"tl"}}}"""));
        Assert.Contains(parsed.Reasons, r => r.Field == "layout.drawer.screenshots");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DeskDesignDrawerTests" --disable-build-servers -nodeReuse:false`
Expected: build FAIL with "'DeskArrangement' does not contain a definition for 'IsPiece'" and "'DeskDesignApply' does not contain a definition for 'WithDrawers'".
If `DeskDesign.Parse(...).Reasons` or `DeskReason.Field` are named differently, read `DeskDesign.Parse` and use its names; the assertion stays "a reason names the drawer field".

- [ ] **Step 3: Implement**

In `DeskArrangement.cs`, beside `Known`:

```csharp
    /// <summary>A word the layout can hold: a built-in piece or a drawer (<c>drawer.&lt;id&gt;</c>, 5.0 drawers spec, section 2).</summary>
    public static bool IsPiece(string widget) => Known.Contains(widget, StringComparer.Ordinal) || DrawerPieces.IsWord(widget);
```

In `DeskDesign.ToArrangement`, replace `DeskArrangement.Known.Contains(p.Piece, StringComparer.Ordinal)` with
`DeskArrangement.IsPiece(p.Piece)`. Leave `ParseLayout` and `FromArrangement`'s hidden loop on `Known`: agents still cannot name a
drawer, and `get_desk` lists hidden built-in pieces only.

In `DeskDesignApply`, make `Layout` use `DeskArrangement.IsPiece(p.Piece)` the same way, change the layout line in `Settings` to

```csharp
            Layout = design.Layout is null ? desk.Layout : WithDrawers(Layout(design.Layout), desk.Layout),
```

and add:

```csharp
    /// <summary>A design never moves, hides or drops a drawer: <paramref name="layout"/>'s drawer words are replaced by the ones placed in
    /// <paramref name="current"/>, where they were. Drawers belong to the user's folders, not to a look.</summary>
    public static string WithDrawers(string layout, string current)
    {
        var kept = new DeskArrangement(current).Pieces.Where(p => DrawerPieces.IsWord(p.Widget));
        var rest = new DeskArrangement(layout).Pieces.Where(p => !DrawerPieces.IsWord(p.Widget));
        return new DeskArrangement(rest.Concat(kept)).Layout;
    }
```

In the spec, section 2, after "All three go through the journal.", add: "A drag out to Explorer is Explorer's own move (its undo
covers it): Sanduhr never learns where the file landed, so it is not journaled. A Desk design never moves, hides or drops a drawer."

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DeskDesign" --disable-build-servers -nodeReuse:false`
Expected: PASS, the existing `DeskDesign*` suites included.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/DeskArrangement.cs windows-dotnet/src/Sanduhr.Core/DeskDesign.cs windows-dotnet/src/Sanduhr.Core/DeskDesignApply.cs windows-dotnet/tests/Sanduhr.Tests/DeskDesignDrawerTests.cs docs/superpowers/specs/2026-10-28-drawers-design.md
git commit -m "fix(drawers): a Desk design never moves, hides or drops a drawer

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Core hardening, the final-path rule and skip reasons

Plan 1's deferred minors that this plan's App code walks into, plus three small Core additions the App needs: the final-path rule
each move checks, the skip reasons every result line uses, and the folded set.

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/DrawerModel.cs` (`DrawerNames`; new `DrawerSkip`, `DrawerSkipped`)
- Modify: `windows-dotnet/src/Sanduhr.Core/DrawerPieces.cs` (`DrawerIds.Unique`)
- Modify: `windows-dotnet/src/Sanduhr.Core/DrawerPlanValidator.cs` (null guards, `DrawerPaths.MoveAllowed`)
- Modify: `windows-dotnet/src/Sanduhr.Core/DrawerJournal.cs` (`Without`, `DrawerUndo` constants)
- Modify: `windows-dotnet/src/Sanduhr.Core/DrawerSettings.cs` (`Folded`)
- Test: `windows-dotnet/tests/Sanduhr.Tests/DrawerHardeningTests.cs` (new)

**Interfaces:**
- Consumes: plan 1's `DrawerNames`, `DrawerIds`, `DrawerPaths`, `DrawerPlanValidator`, `DrawerJournal`, `DrawerUndo`, `DrawerSettings`.
- Produces:
  - `DrawerSkip.MovedSince`, `ChangedSince`, `Outside`, `CouldNotMove`, `NotInTime`, `Shared` (const string) and `DrawerSkip.OpenIn(string app) : string`. Reasons are predicates without a subject: the line reads "report.docx is open in Word."
  - `record DrawerSkipped(string Name, string Reason)`.
  - `DrawerIds.Unique(string name, IReadOnlyCollection<string> taken) : string`.
  - `DrawerPaths.MoveAllowed(string finalSource, string finalTargetDir, string finalDesktop, IReadOnlyList<string> finalHomes, bool toDesktop) : string?` (null allows; otherwise a `DrawerSkip` reason).
  - `DrawerJournal.Without(string id) : DrawerJournal`.
  - `DrawerSettings.Folded : IReadOnlyList<string>` (init, default empty; JSON `folded`).
  - `DrawerUndo.MovedSince` and `DrawerUndo.ChangedSince` now equal `DrawerSkip.MovedSince` and `DrawerSkip.ChangedSince`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Plan 1's deferred minors and the Core pieces plan 2's App code needs (drawers plan 2, Task 2).</summary>
public sealed class DrawerHardeningTests
{
    private const string Desk = @"C:\Users\Ada\Desktop";
    private const string Home = @"C:\Users\Ada\Desktop\Drawers\Images";

    [Theory]
    [InlineData("COM¹")]
    [InlineData("LPT³.txt")]
    [InlineData("CONIN$")]
    [InlineData("conout$")]
    public void Device_names_with_superscripts_and_console_names_are_refused(string name) =>
        Assert.False(DrawerNames.IsValid(name));

    [Fact]
    public void A_dotfile_clash_is_numbered_after_its_whole_name()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(Desk, ".gitignore") };
        Assert.Equal(Path.Combine(Desk, ".gitignore (2)"), DrawerNames.Free(Desk, ".gitignore", false, taken.Contains));
    }

    [Fact]
    public void Free_gives_up_instead_of_looping_forever() =>
        Assert.Throws<InvalidOperationException>(() => DrawerNames.Free(Desk, "a.txt", false, _ => true));

    [Fact]
    public void Two_names_that_both_slug_to_drawer_get_different_ids()
    {
        Assert.Equal("drawer", DrawerIds.Unique("Фото", []));
        Assert.Equal("drawer-2", DrawerIds.Unique("写真", ["drawer"]));
        Assert.Equal("images-3", DrawerIds.Unique("Images", ["images", "images-2"]));
    }

    [Fact]
    public void A_unique_id_stays_within_forty_characters()
    {
        var name = new string('a', 40);
        var id = DrawerIds.Unique(name, [name]);
        Assert.True(DrawerIds.IsValid(id));
        Assert.EndsWith("-2", id);
    }

    [Fact]
    public void A_plan_with_null_lists_or_entries_is_refused_not_thrown()
    {
        var roots = new DrawerRoots(Desk, @"C:\Users\Public\Desktop", Path.Combine(Desk, "Drawers"));
        Assert.False(DrawerPlanValidator.Check(new DrawerPlan(DrawerPlanSource.Rules, null, null!, null!), roots, []).Ok);
        Assert.False(DrawerPlanValidator.Check(new DrawerPlan(DrawerPlanSource.Rules, null, [null!], []), roots, []).Ok);
        Assert.False(DrawerPlanValidator.Check(new DrawerPlan(DrawerPlanSource.Rules, null, [], [null!]), roots, []).Ok);
    }

    [Fact]
    public void A_move_from_the_Desktop_into_a_home_is_allowed() =>
        Assert.Null(DrawerPaths.MoveAllowed(Desk + @"\a.png", Home, Desk, [Home], toDesktop: false));

    [Fact]
    public void A_source_whose_final_path_is_elsewhere_is_refused() =>
        // A Desktop junction or an 8.3 name that resolves to System32 arrives here as its final path.
        Assert.Equal(DrawerSkip.Outside, DrawerPaths.MoveAllowed(@"C:\Windows\System32\drivers", Home, Desk, [Home], toDesktop: false));

    [Fact]
    public void The_Desktop_is_a_target_only_for_undo_and_return()
    {
        Assert.Equal(DrawerSkip.Outside, DrawerPaths.MoveAllowed(Home + @"\a.png", Desk, Desk, [Home], toDesktop: false));
        Assert.Null(DrawerPaths.MoveAllowed(Home + @"\a.png", Desk, Desk, [Home], toDesktop: true));
    }

    [Fact]
    public void A_target_outside_the_homes_is_refused() =>
        Assert.Equal(DrawerSkip.Outside, DrawerPaths.MoveAllowed(Desk + @"\a.png", @"C:\Temp", Desk, [Home], toDesktop: true));

    [Fact]
    public void A_journal_entry_can_be_dropped_by_id()
    {
        var now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        var j = new DrawerJournal()
            .Add(new JournalEntry("a", now, DrawerPlanSource.Rules, null, [new JournalMove(Desk + @"\x.png", Home + @"\x.png", false, now)]), now)
            .Add(new JournalEntry("b", now, DrawerPlanSource.User, null, [new JournalMove(Desk + @"\y.png", Home + @"\y.png", false, now)]), now);
        Assert.Equal(["b"], j.Without("a").Entries.Select(e => e.Id));
        Assert.Equal(2, j.Without("zzz").Entries.Count);
    }

    [Fact]
    public void Undo_reasons_read_as_predicates()
    {
        Assert.Equal("was moved since.", DrawerUndo.MovedSince);
        Assert.Equal("changed since.", DrawerUndo.ChangedSince);
        Assert.Equal("is open in Word.", DrawerSkip.OpenIn("Word"));
    }

    [Fact]
    public void Folded_drawers_round_trip_and_bad_ids_drop()
    {
        var s = DrawerSettings.Default with { Folded = ["images", "documents"] };
        Assert.Equal(["images", "documents"], DrawerSettings.Decode(JsonNode.Parse(s.Encode().ToJsonString())).Folded);
        var bad = JsonNode.Parse("""{"folded":["images","Not An Id",3]}""");
        Assert.Equal(["images"], DrawerSettings.Decode(bad).Folded);
        Assert.Empty(DrawerSettings.Decode(JsonNode.Parse("{}")).Folded);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DrawerHardeningTests" --disable-build-servers -nodeReuse:false`
Expected: build FAIL on the missing `DrawerSkip`, `DrawerIds.Unique`, `DrawerPaths.MoveAllowed`, `DrawerJournal.Without` and `DrawerSettings.Folded`.

- [ ] **Step 3: Implement**

`DrawerModel.cs`. Add to the `Reserved` set: `"COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³", "CONIN$", "CONOUT$"`. Replace `Free`'s body:

```csharp
    /// <summary>The path <paramref name="name"/> can take in <paramref name="dir"/>: itself when free, otherwise "name (2).ext", "(3)" and on,
    /// as Explorer names a clash. A folder, and a dotfile such as ".gitignore", is numbered after its whole name. Never returns a taken path,
    /// so nothing is overwritten; gives up after 9,999 tries rather than loop on a <paramref name="taken"/> that never says no.</summary>
    public static string Free(string dir, string name, bool isFolder, Func<string, bool> taken)
    {
        var first = Path.Combine(dir, name);
        if (!taken(first)) return first;
        var ext = isFolder ? "" : Path.GetExtension(name);
        var stem = name[..^ext.Length];
        if (stem.Length == 0) { stem = name; ext = ""; }
        for (var n = 2; n < 10_000; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!taken(candidate)) return candidate;
        }
        throw new InvalidOperationException("No free name for " + name + ".");
    }
```

Below `DrawerNames`, add:

```csharp
/// <summary>Why a file stayed where it was, as the end of a sentence that starts with its name: "report.docx is open in Word."</summary>
public static class DrawerSkip
{
    public const string MovedSince = "was moved since.";
    public const string ChangedSince = "changed since.";
    public const string Outside = "is outside the Desktop and the drawers.";
    public const string CouldNotMove = "could not be moved.";
    public const string NotInTime = "was not moved in time.";
    public const string Shared = "is shared with every user and needs an administrator to move.";

    public static string OpenIn(string app) => $"is open in {app}.";
}

/// <summary>A file a run left where it was: its name (never its path) and why.</summary>
public sealed record DrawerSkipped(string Name, string Reason);
```

`DrawerPieces.cs`, in `DrawerIds`:

```csharp
    /// <summary>An id for <paramref name="name"/> that <paramref name="taken"/> does not hold: <see cref="From"/>, then "-2", "-3" and on,
    /// cut so the whole stays within 40. Two names with no ASCII letters ("Фото", "写真") get "drawer" and "drawer-2".</summary>
    public static string Unique(string name, IReadOnlyCollection<string> taken)
    {
        var id = From(name);
        if (!taken.Contains(id)) return id;
        for (var n = 2; ; n++)
        {
            var tail = "-" + n;
            var candidate = id[..Math.Min(id.Length, MaxLength - tail.Length)].TrimEnd('-') + tail;
            if (!taken.Contains(candidate)) return candidate;
        }
    }
```

`DrawerPlanValidator.cs`. At the top of `Check`, before the agent test:

```csharp
        // A decoder bug or a hand-built plan must refuse, never throw (plan 1's final review, deferred minor).
        if (plan.Drawers is null || plan.Moves is null || plan.Drawers.Any(d => d is null) || plan.Moves.Any(m => m is null))
            return Refuse("A plan entry is empty.");
```

In `DrawerPaths`, add:

```csharp
    /// <summary>
    /// The check each move makes just before it runs, on <b>final</b> paths (junctions, links and 8.3 names resolved by the App), so a
    /// link swapped in after the plan was checked is caught (spec section 1). The source must sit directly on the Desktop or in a home;
    /// the target must be a home, or the Desktop when <paramref name="toDesktop"/> (undo and turning off). Null allows the move.
    /// </summary>
    public static string? MoveAllowed(string finalSource, string finalTargetDir, string finalDesktop, IReadOnlyList<string> finalHomes, bool toDesktop)
    {
        var source = Normalize(finalSource);
        var target = Normalize(finalTargetDir);
        var desktop = Normalize(finalDesktop);
        if (source is null || target is null || desktop is null) return DrawerSkip.Outside;
        var homes = finalHomes.Select(Normalize).OfType<string>().ToArray();
        bool fromOk = IsChildOf(source, desktop) || homes.Any(h => IsChildOf(source, h));
        bool toOk = homes.Any(h => string.Equals(h, target, StringComparison.OrdinalIgnoreCase))
            || (toDesktop && string.Equals(desktop, target, StringComparison.OrdinalIgnoreCase));
        // A source that is or holds a home would swallow a drawer.
        bool swallows = homes.Any(h => IsInside(h, source));
        return fromOk && toOk && !swallows ? null : DrawerSkip.Outside;
    }
```

`DrawerJournal.cs`. In `DrawerJournal`:

```csharp
    /// <summary>The journal without one entry (undone, or its files all gone). An unknown id changes nothing.</summary>
    public DrawerJournal Without(string id) => new(Entries.Where(e => e.Id != id).ToArray());
```

In `DrawerUndo`, change the two constants to `public const string MovedSince = DrawerSkip.MovedSince;` and
`public const string ChangedSince = DrawerSkip.ChangedSince;`.

`DrawerSettings.cs`. Add to the record body:

```csharp
    /// <summary>The drawers shown folded (title bar and count only), by id.</summary>
    public IReadOnlyList<string> Folded { get; init; } = [];
```

In `Decode`, return `new DrawerSettings(...) { Folded = o["folded"] is JsonArray fs ? [.. fs.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var s) ? s : null).Where(DrawerIds.IsValid).OfType<string>().Distinct()] : [] }`.
In `Encode`, add `["folded"] = new JsonArray([.. Folded.Select(f => (JsonNode?)f)]),`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~Drawer" --disable-build-servers -nodeReuse:false`
Expected: PASS, plan 1's drawer suites included (they reference `DrawerUndo.MovedSince` by name, so the new wording carries through).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/DrawerModel.cs windows-dotnet/src/Sanduhr.Core/DrawerPieces.cs windows-dotnet/src/Sanduhr.Core/DrawerPlanValidator.cs windows-dotnet/src/Sanduhr.Core/DrawerJournal.cs windows-dotnet/src/Sanduhr.Core/DrawerSettings.cs windows-dotnet/tests/Sanduhr.Tests/DrawerHardeningTests.cs
git commit -m "feat(drawers): final-path move rule, skip reasons, unique ids, folded drawers and plan 1's deferred minors

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Review copy, plan edits, the OneDrive line and where new drawers arrive

Everything the review bar says, pure and tested here so the App only draws it. `DeskReviewModel` gains three optional fields (Edit,
and relabelled Apply and Dismiss for the result state) that default to today's bar, so the design and widget reviews are unchanged.

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/DrawerReview.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/DeskReview.cs` (`DeskReviewCopy.Edit`, three init properties on `DeskReviewModel`)
- Test: `windows-dotnet/tests/Sanduhr.Tests/DrawerReviewTests.cs` (new)

**Interfaces:**
- Consumes: `DrawerPlan`, `DrawerPlanCheck`, `DrawerDef`, `DrawerSkipped`, `DrawerNames.IsValid`, `DeskArrangement`, `DrawerPieces.Word`.
- Produces:
  - `DeskReviewModel.CanEdit : bool`, `ApplyLabel : string?`, `DismissLabel : string?`, `ApplyOff : bool` (init; defaults false, null, null, false; `CanApply` also needs `!ApplyOff`). `DeskReviewCopy.Edit = "Edit"`.
  - `DrawerReview.Title(int files, int drawers) : string`
  - `DrawerReview.Model(DrawerPlan plan, DrawerPlanCheck check, IReadOnlyCollection<string> existingIds, string? note, IEnumerable<string> warnings) : DeskReviewModel`
  - `DrawerReview.SharedLine(IReadOnlyList<string> shared) : string?`
  - `DrawerReview.ResultLine(int moved, IReadOnlyList<DrawerSkipped> skipped) : string`
  - `DrawerReview.Result(string line, bool canUndo) : DeskReviewModel`
  - consts `DrawerReview.BarName`, `ResultBarName`, `NothingToSort`, `FirstOnLine`, `Busy`, `Undo`, `Done`
  - `DrawerPlanEdit.Apply(DrawerPlan plan, IReadOnlySet<string> unticked, IReadOnlyDictionary<string, string> renames, IReadOnlyCollection<string> existingIds, string drawersRoot) : DrawerPlan`
  - `DrawerSync.Warning(string desktop, string root, IReadOnlyList<string> synced) : string?`
  - `DrawerArrival.Place(string layout, IReadOnlyList<DrawerDef> created) : string` and `DrawerArrival.Folded(IReadOnlyList<DrawerDef> created) : IReadOnlyList<string>`

- [ ] **Step 1: Write the failing tests**

```csharp
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>What the drawers review says and how Edit changes a plan (5.0 drawers spec, sections 1 and 4).</summary>
public sealed class DrawerReviewTests
{
    private const string Desk = @"C:\Users\Ada\Desktop";
    private static readonly string Root = Path.Combine(Desk, "Drawers");

    private static DrawerDef Def(string name, bool owned = true) => new(DrawerIds.From(name), name, Path.Combine(Root, name), owned);

    private static (DrawerPlan Plan, DrawerPlanCheck Check) Plan(params (string File, string Drawer)[] moves)
    {
        var drawers = moves.Select(m => m.Drawer).Distinct().Select(n => Def(n)).ToArray();
        var plan = new DrawerPlan(DrawerPlanSource.Rules, null, drawers,
            moves.Select(m => new DrawerMove(Path.Combine(Desk, m.File), DrawerIds.From(m.Drawer))).ToArray());
        var check = DrawerPlanValidator.Check(plan, new DrawerRoots(Desk, @"C:\Users\Public\Desktop", Root), []);
        Assert.True(check.Ok, check.Error);
        return (plan, check);
    }

    [Theory]
    [InlineData(14, 3, "14 files into 3 drawers")]
    [InlineData(1, 1, "1 file into 1 drawer")]
    [InlineData(2, 1, "2 files into 1 drawer")]
    public void The_title_counts_files_and_drawers(int files, int drawers, string expected) =>
        Assert.Equal(expected, DrawerReview.Title(files, drawers));

    [Fact]
    public void The_model_names_each_drawer_with_its_count_and_marks_new_ones()
    {
        var (plan, check) = Plan(("a.png", "Images"), ("b.png", "Images"), ("c.pdf", "Documents"));
        var m = DrawerReview.Model(plan, check, ["documents"], note: null, warnings: []);
        Assert.Equal("3 files into 2 drawers", m.Title);
        Assert.Equal("Sanduhr suggests drawers", m.Heading);
        Assert.Equal(DrawerReview.BarName, m.BarName);
        Assert.True(m.CanEdit);
        Assert.True(m.CanApply);
        Assert.Contains("Images (new): 2 files", m.Changes);
        Assert.Contains("Documents: 1 file", m.Changes);
    }

    [Fact]
    public void An_agent_plan_is_headed_with_the_agents_name()
    {
        var (plan, check) = Plan(("a.png", "Images"));
        var m = DrawerReview.Model(plan with { Source = DrawerPlanSource.Agent, Agent = "Codex" }, check, [], null, []);
        Assert.Equal("Codex suggests drawers", m.Heading);
    }

    [Fact]
    public void Shared_items_are_named_as_shortcuts_when_they_all_are()
    {
        Assert.Equal("3 shortcuts are shared with every user and need an administrator to move.",
            DrawerReview.SharedLine([@"C:\Users\Public\Desktop\A.lnk", @"C:\Users\Public\Desktop\B.lnk", @"C:\Users\Public\Desktop\C.url"]));
        Assert.Equal("1 item is shared with every user and needs an administrator to move.",
            DrawerReview.SharedLine([@"C:\Users\Public\Desktop\notes.txt"]));
        Assert.Null(DrawerReview.SharedLine([]));
    }

    [Fact]
    public void The_result_line_names_skipped_files_and_caps_at_three()
    {
        Assert.Equal("13 moved.", DrawerReview.ResultLine(13, []));
        Assert.Equal("13 moved, 1 skipped: report.docx is open in Word.",
            DrawerReview.ResultLine(13, [new("report.docx", DrawerSkip.OpenIn("Word"))]));
        var five = Enumerable.Range(1, 5).Select(i => new DrawerSkipped($"f{i}.txt", DrawerSkip.MovedSince)).ToArray();
        Assert.Equal("0 moved, 5 skipped: f1.txt was moved since. f2.txt was moved since. f3.txt was moved since. And 2 more.",
            DrawerReview.ResultLine(0, five));
    }

    [Fact]
    public void The_result_model_offers_Undo_and_Done()
    {
        var r = DrawerReview.Result("13 moved.", canUndo: true);
        Assert.Equal("13 moved.", r.Title);
        Assert.Equal(DrawerReview.Undo, r.ApplyLabel);
        Assert.Equal(DrawerReview.Done, r.DismissLabel);
        Assert.False(r.CanEdit);
        Assert.True(r.CanApply);
        Assert.False(DrawerReview.Result("0 moved.", canUndo: false).CanApply);
    }

    [Fact]
    public void Edit_drops_unticked_moves_and_drawers_left_empty()
    {
        var (plan, _) = Plan(("a.png", "Images"), ("c.pdf", "Documents"));
        var edited = DrawerPlanEdit.Apply(plan, new HashSet<string> { Path.Combine(Desk, "c.pdf") }, new Dictionary<string, string>(), [], Root);
        Assert.Equal([Path.Combine(Desk, "a.png")], edited.Moves.Select(m => m.Source));
        Assert.Equal(["images"], edited.Drawers.Select(d => d.Id));
    }

    [Fact]
    public void Edit_renames_a_new_drawer_and_its_folder_but_keeps_its_id()
    {
        var (plan, _) = Plan(("a.png", "Images"));
        var edited = DrawerPlanEdit.Apply(plan, new HashSet<string>(), new Dictionary<string, string> { ["images"] = "Photos" }, [], Root);
        var d = Assert.Single(edited.Drawers);
        Assert.Equal(("images", "Photos", Path.Combine(Root, "Photos")), (d.Id, d.Name, d.Home));
        Assert.Equal("images", edited.Moves[0].DrawerId);
    }

    [Fact]
    public void Edit_never_renames_an_existing_drawer_or_takes_a_bad_name()
    {
        var (plan, _) = Plan(("a.png", "Images"), ("c.pdf", "Documents"));
        var edited = DrawerPlanEdit.Apply(plan, new HashSet<string>(),
            new Dictionary<string, string> { ["documents"] = "Papers", ["images"] = "CON" }, ["documents"], Root);
        Assert.Equal(["Images", "Documents"], edited.Drawers.Select(d => d.Name));
    }

    [Fact]
    public void The_OneDrive_line_names_the_folders_when_sync_differs()
    {
        const string od = @"C:\Users\Ada\OneDrive";
        Assert.Null(DrawerSync.Warning(od + @"\Desktop", od + @"\Desktop\Drawers", [od]));
        Assert.Null(DrawerSync.Warning(Desk, @"D:\Drawers", [od]));
        Assert.Equal(@"Your Desktop syncs with OneDrive and D:\Drawers does not. Files moved into drawers there stop syncing.",
            DrawerSync.Warning(od + @"\Desktop", @"D:\Drawers", [od]));
        Assert.Equal(@"C:\Users\Ada\OneDrive\Drawers syncs with OneDrive and your Desktop does not.",
            DrawerSync.Warning(Desk, od + @"\Drawers", [od]));
    }

    [Fact]
    public void New_drawers_arrive_top_left_and_all_but_the_first_two_folded()
    {
        var created = new[] { Def("Images"), Def("Documents"), Def("Archives") };
        var layout = new DeskArrangement(DrawerArrival.Place("clock:bl", created));
        Assert.All(created, d => Assert.Equal(DeskAnchor.TL, layout.Placement(DrawerPieces.Word(d.Id))!.Anchor));
        Assert.Equal(["archives"], DrawerArrival.Folded(created));
    }

    [Fact]
    public void A_drawer_already_placed_keeps_its_place()
    {
        var layout = new DeskArrangement(DrawerArrival.Place("drawer.images:br:1", [Def("Images")]));
        Assert.Equal(DeskAnchor.BR, layout.Placement("drawer.images")!.Anchor);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DrawerReviewTests" --disable-build-servers -nodeReuse:false`
Expected: build FAIL on the missing `DrawerReview`, `DrawerPlanEdit`, `DrawerSync`, `DrawerArrival` and `DeskReviewModel.CanEdit`.

- [ ] **Step 3: Implement**

In `DeskReview.cs`, add `public const string Edit = "Edit";` to `DeskReviewCopy`, and to `DeskReviewModel`:

```csharp
    /// <summary>The bar shows an Edit button (the drawers review: untick moves, rename a new drawer).</summary>
    public bool CanEdit { get; init; }

    /// <summary>Apply's and Dismiss's words when not "Apply" and "Dismiss" (the drawers result: "Undo" and "Done"); null keeps them.</summary>
    public string? ApplyLabel { get; init; }
    public string? DismissLabel { get; init; }

    /// <summary>Apply is off for a reason other than a missing font (a result with nothing to undo).</summary>
    public bool ApplyOff { get; init; }
```

and change `CanApply` to `public bool CanApply => MissingFonts.Count == 0 && !ApplyOff;`.

Create `DrawerReview.cs`:

```csharp
namespace Sanduhr.Core;

/// <summary>
/// The drawers review bar's words (5.0 drawers spec, section 1): "14 files into 3 drawers", a line per drawer, the shared-items and
/// OneDrive lines, and after Apply the result line with Undo. Pure: the App's <c>DeskReviewBar</c> draws the model.
/// </summary>
public static class DrawerReview
{
    public const string BarName = "Review drawers";
    public const string ResultBarName = "Drawers result";
    public const string ChangesTitle = "Where files go";
    public const string NothingToSort = "Nothing on your Desktop matches a rule.";
    public const string FirstOnLine = "Drawer files live in the Drawers folder on your Desktop.";
    public const string Busy = "Finish the review on your Desk first.";
    public const string Undo = "Undo";
    public const string Done = "Done";
    private const int NamedSkips = 3;

    public static string Title(int files, int drawers) =>
        $"{files} {(files == 1 ? "file" : "files")} into {drawers} {(drawers == 1 ? "drawer" : "drawers")}";

    /// <summary>The bar for a checked plan. <paramref name="existingIds"/> are the drawers the user already has (no "(new)").</summary>
    public static DeskReviewModel Model(DrawerPlan plan, DrawerPlanCheck check, IReadOnlyCollection<string> existingIds, string? note,
        IEnumerable<string> warnings)
    {
        var names = plan.Drawers.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
        var lines = check.Moves.GroupBy(m => m.DrawerId)
            .Select(g =>
            {
                var name = names.TryGetValue(g.Key, out var n) ? n : g.Key;
                var count = g.Count();
                return $"{name}{(existingIds.Contains(g.Key) ? "" : " (new)")}: {count} {(count == 1 ? "file" : "files")}";
            })
            .ToList();
        var all = new List<string>();
        if (SharedLine(check.Shared) is { } shared) all.Add(shared);
        all.AddRange(warnings);
        return new DeskReviewModel(Title(check.Moves.Count, lines.Count), note, lines, [], DeskOff: false, all)
        {
            Heading = plan.Source == DrawerPlanSource.Agent && !string.IsNullOrWhiteSpace(plan.Agent)
                ? $"{plan.Agent} suggests drawers" : "Sanduhr suggests drawers",
            ChangesTitle = ChangesTitle,
            BarName = BarName,
            CanEdit = true,
        };
    }

    /// <summary>The Public Desktop line (spec section 1): shortcuts when every item is one, items otherwise; null when there are none.</summary>
    public static string? SharedLine(IReadOnlyList<string> shared)
    {
        if (shared.Count == 0) return null;
        bool shortcuts = shared.All(p => Path.GetExtension(p).ToLowerInvariant() is ".lnk" or ".url");
        string noun = shortcuts ? (shared.Count == 1 ? "shortcut" : "shortcuts") : (shared.Count == 1 ? "item" : "items");
        return shared.Count == 1
            ? $"1 {noun} is shared with every user and needs an administrator to move."
            : $"{shared.Count} {noun} are shared with every user and need an administrator to move.";
    }

    /// <summary>"13 moved, 1 skipped: report.docx is open in Word." Names the first three skipped files, then counts the rest.</summary>
    public static string ResultLine(int moved, IReadOnlyList<DrawerSkipped> skipped)
    {
        if (skipped.Count == 0) return $"{moved} moved.";
        var named = string.Join(" ", skipped.Take(NamedSkips).Select(s => $"{s.Name} {s.Reason}"));
        var more = skipped.Count > NamedSkips ? $" And {skipped.Count - NamedSkips} more." : "";
        return $"{moved} moved, {skipped.Count} skipped: {named}{more}";
    }

    /// <summary>The bar after Apply: the result line, Undo (when anything moved) and Done.</summary>
    public static DeskReviewModel Result(string line, bool canUndo) =>
        new(line, null, [], [], DeskOff: false, [])
        {
            Heading = "Drawers",
            ChangesTitle = "",
            BarName = ResultBarName,
            ApplyLabel = Undo,
            DismissLabel = Done,
            ApplyOff = !canUndo,
        };
}

/// <summary>What Edit does to a plan before Apply (spec section 1): unticked moves drop, a new drawer can take another name (and the folder
/// of that name; its id stays, so its layout word does not change), and a drawer left with no moves drops.</summary>
public static class DrawerPlanEdit
{
    public static DrawerPlan Apply(DrawerPlan plan, IReadOnlySet<string> unticked, IReadOnlyDictionary<string, string> renames,
        IReadOnlyCollection<string> existingIds, string drawersRoot)
    {
        var moves = plan.Moves.Where(m => !unticked.Contains(m.Source)).ToArray();
        var used = moves.Select(m => m.DrawerId).ToHashSet(StringComparer.Ordinal);
        var drawers = plan.Drawers
            .Where(d => used.Contains(d.Id))
            .Select(d => !existingIds.Contains(d.Id) && renames.TryGetValue(d.Id, out var name) && DrawerNames.IsValid(name)
                ? d with { Name = name, Home = Path.Combine(drawersRoot, name) }
                : d)
            .ToArray();
        return plan with { Drawers = drawers, Moves = moves };
    }
}

/// <summary>The one OneDrive line (spec section 4): said when the Desktop and the Drawers root differ in whether they sync, naming both.</summary>
public static class DrawerSync
{
    public static string? Warning(string desktop, string root, IReadOnlyList<string> synced)
    {
        bool Synced(string path) => DrawerPaths.Normalize(path) is { } p
            && synced.Select(DrawerPaths.Normalize).OfType<string>().Any(s => DrawerPaths.IsInside(p, s));
        bool d = Synced(desktop), r = Synced(root);
        if (d == r) return null;
        return d
            ? $"Your Desktop syncs with OneDrive and {root} does not. Files moved into drawers there stop syncing."
            : $"{root} syncs with OneDrive and your Desktop does not.";
    }
}

/// <summary>Where drawers a plan creates appear: the top-left stack, the first two open and the rest folded so a six-drawer plan fits a
/// 1080-pixel screen. A drawer already placed keeps its place.</summary>
public static class DrawerArrival
{
    public const int OpenOnArrival = 2;

    public static string Place(string layout, IReadOnlyList<DrawerDef> created)
    {
        var arr = new DeskArrangement(layout);
        foreach (var d in created)
        {
            var word = DrawerPieces.Word(d.Id);
            if (arr.Placement(word) is null) arr.Place(word, DeskAnchor.TL);
        }
        return arr.Layout;
    }

    public static IReadOnlyList<string> Folded(IReadOnlyList<DrawerDef> created) => [.. created.Skip(OpenOnArrival).Select(d => d.Id)];
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DrawerReviewTests|FullyQualifiedName~DeskDesignApplyTests" --disable-build-servers -nodeReuse:false`
Expected: PASS (the design review's existing model tests prove the defaults kept today's bar).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/DrawerReview.cs windows-dotnet/src/Sanduhr.Core/DeskReview.cs windows-dotnet/tests/Sanduhr.Tests/DrawerReviewTests.cs
git commit -m "feat(drawers): review and result copy, plan edits, the OneDrive line and where new drawers arrive

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Shell interop

The spike's proven COM code, made production: one file of interface definitions, then small static helpers. Every helper returns a
result instead of throwing on a Shell failure, because a move that fails must skip one file and let the run carry on.

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/Interop/ShellNative.cs`
- Create: `windows-dotnet/src/Sanduhr.App/Interop/ShellFiles.cs`
- Create: `windows-dotnet/src/Sanduhr.App/Interop/ShellMenu.cs`
- Create: `windows-dotnet/src/Sanduhr.App/Interop/DarkMenus.cs`
- Create: `windows-dotnet/tests/Sanduhr.App.Tests/DrawerSandbox.cs` (shared by Tasks 4 to 6 and 10)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/ShellFilesTests.cs` (new)

**Interfaces:**
- Produces (all `internal`, namespace `Sanduhr.App.Interop`):
  - `record ShellResult(bool Ok, string? NewPath, int HResult)`
  - `ShellFiles.Move(string source, string targetDir, string newName) : ShellResult`
  - `ShellFiles.Rename(string path, string newName) : ShellResult`
  - `ShellFiles.FinalPath(string path) : string?` (junctions, links and 8.3 names resolved; null when it cannot be opened)
  - `ShellFiles.Holder(string path) : string?` (the app holding the file open, from Restart Manager)
  - `ShellFiles.Thumbnail(string path, int pixels) : BitmapSource?` (frozen)
  - `ShellFiles.Open(string path) : bool`
  - `record ShellMenuItem(int Id, string Text)`; `record ShellMenuChoice(string? Verb, int? Own)`
  - `ShellMenu.Show(IntPtr owner, string? path, int x, int y, IReadOnlyList<ShellMenuItem> extras, Func<string, bool> takeVerb) : ShellMenuChoice?`
  - `ShellMenu.InvokeVerb(IntPtr owner, string path, string verb) : bool`
  - `ShellMenu.Forward(int msg, IntPtr wParam, IntPtr lParam, out IntPtr result) : bool`
  - `ShellMenu.OwnFirst = 0x8000` (extras' ids start here)
  - `DarkMenus.Enable() : bool`
  - Test helper `DrawerSandbox` (namespace `Sanduhr.App.Tests`): `Root`, `Desktop`, `Public`, `Drawers`, `Roots`, `File(rel, text)`, `Dir(rel)`, `Junction(link, target) : bool`.

- [ ] **Step 1: Write the sandbox and the failing tests**

`DrawerSandbox.cs`:

```csharp
using System.Diagnostics;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>A throwaway Desktop for drawer tests under the temp folder (never the real Desktop): Desktop, Public and Desktop\Drawers.</summary>
internal sealed class DrawerSandbox : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "sanduhr-drawers-" + Guid.NewGuid().ToString("N")[..8]);
    public string Desktop => Path.Combine(Root, "Desktop");
    public string Public => Path.Combine(Root, "Public");
    public string Drawers => Path.Combine(Desktop, "Drawers");
    public DrawerRoots Roots => new(Desktop, Public, Drawers);

    public DrawerSandbox()
    {
        Directory.CreateDirectory(Desktop);
        Directory.CreateDirectory(Public);
    }

    public string File(string relative, string text = "x")
    {
        var p = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllText(p, text);
        return p;
    }

    public string Dir(string relative) => Directory.CreateDirectory(Path.Combine(Root, relative)).FullName;

    /// <summary>A junction (no administrator needed), or false where mklink is not available.</summary>
    public bool Junction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        p.WaitForExit(10_000);
        return p.ExitCode == 0 && Directory.Exists(link);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* a locked test file; the temp folder is the OS's */ }
    }
}
```

`ShellFilesTests.cs`:

```csharp
using Sanduhr.App.Interop;

namespace Sanduhr.App.Tests;

/// <summary>The Shell calls drawers make, against a sandbox (drawers plan 2, Task 4).</summary>
public sealed class ShellFilesTests
{
    [Fact]
    public void Move_moves_a_file_and_reports_where_it_landed() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var a = box.File(@"Desktop\a.txt", "hello");
        var home = box.Dir(@"Desktop\Drawers\Docs");
        var r = ShellFiles.Move(a, home, "a.txt");
        Assert.True(r.Ok, $"0x{r.HResult:X8}");
        Assert.False(File.Exists(a));
        Assert.Equal(Path.Combine(home, "a.txt"), r.NewPath, ignoreCase: true);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(home, "a.txt")));
    });

    [Fact]
    public void Move_never_overwrites_a_file_already_there() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var a = box.File(@"Desktop\a.txt", "new");
        var home = box.Dir(@"Desktop\Drawers\Docs");
        File.WriteAllText(Path.Combine(home, "a.txt"), "old");
        var r = ShellFiles.Move(a, home, "a.txt");
        Assert.True(r.Ok);
        Assert.Equal("old", File.ReadAllText(Path.Combine(home, "a.txt")));
        Assert.Equal(2, Directory.GetFiles(home).Length);
    });

    [Fact]
    public void Move_of_a_locked_file_fails_and_leaves_it_in_place() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var a = box.File(@"Desktop\locked.txt");
        var home = box.Dir(@"Desktop\Drawers\Docs");
        using (new FileStream(a, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(ShellFiles.Move(a, home, "locked.txt").Ok);
            Assert.False(string.IsNullOrEmpty(ShellFiles.Holder(a)));
        }
        Assert.True(File.Exists(a));
    });

    [Fact]
    public void Rename_renames_through_the_Shell() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var a = box.File(@"Desktop\a.txt");
        Assert.True(ShellFiles.Rename(a, "b.txt").Ok);
        Assert.True(File.Exists(Path.Combine(box.Desktop, "b.txt")));
    });

    [SkippableFact]
    public void FinalPath_sees_through_a_junction() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var real = box.Dir("Elsewhere");
        box.File(@"Elsewhere\secret.txt");
        var link = Path.Combine(box.Desktop, "link");
        Skip.IfNot(box.Junction(link, real), "mklink /J is not available here.");
        Assert.Equal(Path.Combine(real, "secret.txt"), ShellFiles.FinalPath(Path.Combine(link, "secret.txt")), ignoreCase: true);
        Assert.Equal(real, ShellFiles.FinalPath(link), ignoreCase: true);
    });

    [Fact]
    public void FinalPath_of_a_missing_file_is_null()
    {
        using var box = new DrawerSandbox();
        Assert.Null(ShellFiles.FinalPath(Path.Combine(box.Desktop, "nope.txt")));
    }

    [Fact]
    public void Holder_of_a_file_nobody_holds_is_null()
    {
        using var box = new DrawerSandbox();
        Assert.Null(ShellFiles.Holder(box.File(@"Desktop\free.txt")));
    }

    [Fact]
    public void Thumbnail_gives_a_frozen_image_for_a_text_file() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var img = ShellFiles.Thumbnail(box.File(@"Desktop\a.txt"), 64);
        Assert.NotNull(img);
        Assert.True(img!.IsFrozen);
        Assert.True(img.PixelWidth > 0);
    });

    [Fact]
    public void Dark_menus_turn_on_on_this_Windows() => Assert.True(DarkMenus.Enable());
}
```

`Skip.IfNot` throws a skip exception that `Sta.Run` rethrows on the test thread, so `[SkippableFact]` reports Skipped as usual.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~ShellFilesTests" --disable-build-servers -nodeReuse:false`
Expected: build FAIL: "The type or namespace name 'ShellFiles' could not be found".

- [ ] **Step 3: Write `ShellNative.cs`**

```csharp
using System.Runtime.InteropServices;
using System.Text;

namespace Sanduhr.App.Interop;

/// <summary>
/// The Shell's COM surface drawers use (5.0 drawers spec; ported from the drawer-island spike, VERDICT.md): items, file operations,
/// Explorer's menu and thumbnails, plus the Win32 calls for final paths, Restart Manager and bitmaps. Hand-written, as
/// <see cref="DeskNative"/> is. The helpers that use these are <see cref="ShellFiles"/> and <see cref="ShellMenu"/>.
/// </summary>
internal static class ShellNative
{
    public static readonly Guid BHID_SFUIObject = new("3981e225-f559-11d3-8e3a-00c04f6837d5");
    public const uint SIGDN_FILESYSPATH = 0x80058000;

    // FOF_NO_UI (silent, no confirmation, no error UI, no mkdir confirmation) | FOF_RENAMEONCOLLISION | FOFX_EARLYFAILURE.
    public const uint MoveFlags = 0x0614 | 0x0008 | 0x00100000;

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdn, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOperationProgressSink
    {
        void StartOperations();
        void FinishOperations(int hrResult);
        void PreRenameItem(uint dwFlags, IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);
        void PostRenameItem(uint dwFlags, IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, int hrRename, IShellItem? psiNewlyCreated);
        void PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);
        void PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, int hrMove, IShellItem? psiNewlyCreated);
        void PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);
        void PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, int hrCopy, IShellItem? psiNewlyCreated);
        void PreDeleteItem(uint dwFlags, IShellItem psiItem);
        void PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem? psiNewlyCreated);
        void PreNewItem(uint dwFlags, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);
        void PostNewItem(uint dwFlags, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, uint dwFileAttributes, int hrNew, IShellItem? psiNewItem);
        void UpdateProgress(uint iWorkTotal, uint iWorkSoFar);
        void ResetTimer();
        void PauseTimer();
        void ResumeTimer();
    }

    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOperation
    {
        uint Advise(IFileOperationProgressSink pfops);
        void Unadvise(uint dwCookie);
        void SetOperationFlags(uint dwOperationFlags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
        void SetProgressDialog(IntPtr popd);
        void SetProperties(IntPtr pproparray);
        void SetOwnerWindow(IntPtr hwndOwner);
        void ApplyPropertiesToItem(IShellItem psiItem);
        void ApplyPropertiesToItems(IntPtr punkItems);
        void RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IFileOperationProgressSink? pfopsItem);
        void RenameItems(IntPtr pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
        void MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, IFileOperationProgressSink? pfopsItem);
        void MoveItems(IntPtr punkItems, IShellItem psiDestinationFolder);
        void CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszCopyName, IFileOperationProgressSink? pfopsItem);
        void CopyItems(IntPtr punkItems, IShellItem psiDestinationFolder);
        void DeleteItem(IShellItem psiItem, IFileOperationProgressSink? pfopsItem);
        void DeleteItems(IntPtr punkItems);
        void NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, IFileOperationProgressSink? pfopsItem);
        void PerformOperations();
        [return: MarshalAs(UnmanagedType.Bool)] bool GetAnyOperationsAborted();
    }

    [ComImport, Guid("3ad05575-8857-4850-9277-11b85bdb8e09")]
    public class FileOperation { }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(UIntPtr idcmd, uint uflags, IntPtr reserved, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder commandstring, int cch);
    }

    [ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(UIntPtr idcmd, uint uflags, IntPtr reserved, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder commandstring, int cch);
        [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize; public uint fMask; public IntPtr hwnd; public IntPtr lpVerb;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpDirectory;
        public int nShow; public uint dwHotKey; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpTitle;
        public IntPtr lpVerbW; public string? lpParametersW; public string? lpDirectoryW; public string? lpTitleW;
        public POINT ptInvoke;
    }

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemImageFactory
    {
        void GetImage(SIZE size, uint flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItem item);

    /// <summary>The Shell item for a path (throws a COMException or FileNotFoundException when there is none).</summary>
    public static IShellItem Item(string path)
    {
        var iid = typeof(IShellItem).GUID;
        SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item);
        return item;
    }

    /// <summary>An item's file system path, or null.</summary>
    public static string? PathOf(IShellItem? item)
    {
        if (item is null) return null;
        item.GetDisplayName(SIGDN_FILESYSPATH, out var p);
        try { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    /// <summary>Records the one item a file operation ran: its result and where it landed (FOF_RENAMEONCOLLISION may pick another name).</summary>
    [ComVisible(true)]
    public sealed class Sink : IFileOperationProgressSink
    {
        public int Hr { get; private set; } = unchecked((int)0x80004005);   // E_FAIL until the Shell reports
        public string? NewPath { get; private set; }

        public void PostMoveItem(uint f, IShellItem i, IShellItem d, string? n, int hr, IShellItem? created) { Hr = hr; NewPath = PathOf(created); }
        public void PostRenameItem(uint f, IShellItem i, string? n, int hr, IShellItem? created) { Hr = hr; NewPath = PathOf(created); }
        public void StartOperations() { }
        public void FinishOperations(int hrResult) { }
        public void PreRenameItem(uint f, IShellItem i, string? n) { }
        public void PreMoveItem(uint f, IShellItem i, IShellItem d, string? n) { }
        public void PreCopyItem(uint f, IShellItem i, IShellItem d, string? n) { }
        public void PostCopyItem(uint f, IShellItem i, IShellItem d, string? n, int hr, IShellItem? c) { }
        public void PreDeleteItem(uint f, IShellItem i) { }
        public void PostDeleteItem(uint f, IShellItem i, int hr, IShellItem? c) { }
        public void PreNewItem(uint f, IShellItem d, string? n) { }
        public void PostNewItem(uint f, IShellItem d, string? n, string? t, uint a, int hr, IShellItem? c) { }
        public void UpdateProgress(uint total, uint soFar) { }
        public void ResetTimer() { }
        public void PauseTimer() { }
        public void ResumeTimer() { }
    }

    // ---- Win32 ----

    public const uint FILE_SHARE_ALL = 0x1 | 0x2 | 0x4;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint GetFinalPathNameByHandleW(Microsoft.Win32.SafeHandles.SafeFileHandle h, StringBuilder path, uint length, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct RM_UNIQUE_PROCESS { public int dwProcessId; public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
        public int ApplicationType; public uint AppStatus; public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] public static extern int RmStartSession(out uint session, int flags, StringBuilder key);
    [DllImport("rstrtmgr.dll")] public static extern int RmEndSession(uint session);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    public static extern int RmRegisterResources(uint session, uint nFiles, string[] files, uint nApps, IntPtr apps, uint nServices, IntPtr services);
    [DllImport("rstrtmgr.dll")]
    public static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] RM_PROCESS_INFO[]? info, out uint reasons);

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }

    [DllImport("gdi32.dll")] public static extern int GetObject(IntPtr h, int size, out BITMAP bmp);
    [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenuW(IntPtr h, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] public static extern uint TrackPopupMenuEx(IntPtr h, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetMenuItemCount(IntPtr h);
}
```

- [ ] **Step 4: Write `ShellFiles.cs`, `ShellMenu.cs` and `DarkMenus.cs`**

`ShellFiles.cs`:

```csharp
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sanduhr.App.Interop;

/// <summary>One Shell file operation's outcome: whether it ran, where the item landed, and the HRESULT for the log.</summary>
internal sealed record ShellResult(bool Ok, string? NewPath, int HResult);

/// <summary>
/// The Shell calls drawers make (5.0 drawers spec, section 4): one item per <c>IFileOperation</c>, with no UI and
/// <c>FOF_RENAMEONCOLLISION</c> so a race can never overwrite; final paths for the root check; the app holding a file for the result
/// line; thumbnails; opening an item. Nothing here throws on a Shell failure: the caller skips that one file.
/// </summary>
internal static class ShellFiles
{
    public static ShellResult Move(string source, string targetDir, string newName) =>
        Run(op => op.MoveItem(ShellNative.Item(source), ShellNative.Item(targetDir), newName, null));

    public static ShellResult Rename(string path, string newName) =>
        Run(op => op.RenameItem(ShellNative.Item(path), newName, null));

    private static ShellResult Run(Action<ShellNative.IFileOperation> queue)
    {
        ShellNative.IFileOperation? op = null;
        try
        {
            op = (ShellNative.IFileOperation)new ShellNative.FileOperation();
            var sink = new ShellNative.Sink();
            uint cookie = op.Advise(sink);
            op.SetOperationFlags(ShellNative.MoveFlags);
            queue(op);
            op.PerformOperations();
            bool aborted = op.GetAnyOperationsAborted();
            op.Unadvise(cookie);
            return new ShellResult(sink.Hr >= 0 && !aborted, sink.NewPath, sink.Hr);
        }
        catch (Exception e) when (e is COMException or FileNotFoundException or DirectoryNotFoundException or ArgumentException
            or UnauthorizedAccessException or InvalidCastException)
        {
            return new ShellResult(false, null, e.HResult);
        }
        finally
        {
            if (op is not null) Marshal.FinalReleaseComObject(op);
        }
    }

    /// <summary>The path after every junction, link and 8.3 name is resolved, without the <c>\\?\</c> prefix; null when it cannot be
    /// opened, or resolves to a network share. Opening with no access rights works on a file another app holds.</summary>
    public static string? FinalPath(string path)
    {
        using var h = ShellNative.CreateFileW(path, 0, ShellNative.FILE_SHARE_ALL, IntPtr.Zero, ShellNative.OPEN_EXISTING,
            ShellNative.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (h.IsInvalid) return null;
        var sb = new StringBuilder(1024);
        uint n = ShellNative.GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
        if (n == 0 || n >= sb.Capacity) return null;
        var p = sb.ToString();
        if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return null;
        return p.StartsWith(@"\\?\", StringComparison.Ordinal) ? p[4..] : p;
    }

    /// <summary>The app holding <paramref name="path"/> open ("Microsoft Word"), from Restart Manager; null when none or unknown.</summary>
    public static string? Holder(string path)
    {
        if (ShellNative.RmStartSession(out var session, 0, new StringBuilder(33)) != 0) return null;
        try
        {
            if (ShellNative.RmRegisterResources(session, 1, [path], 0, IntPtr.Zero, 0, IntPtr.Zero) != 0) return null;
            uint count = 0;
            int r = ShellNative.RmGetList(session, out var needed, ref count, null, out _);
            if (r != 234 || needed == 0) return null;   // ERROR_MORE_DATA: someone holds it
            var info = new ShellNative.RM_PROCESS_INFO[needed];
            count = needed;
            if (ShellNative.RmGetList(session, out _, ref count, info, out _) != 0 || count == 0) return null;
            var name = info[0].strAppName;   // streamer: notname - an app's name ("Microsoft Word"), never a user's
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        finally { ShellNative.RmEndSession(session); }
    }

    /// <summary>The Shell's thumbnail or icon for an item at <paramref name="pixels"/> square, frozen so any thread may hand it on.</summary>
    public static BitmapSource? Thumbnail(string path, int pixels)
    {
        IntPtr hbm = IntPtr.Zero;
        try
        {
            var factory = (ShellNative.IShellItemImageFactory)ShellNative.Item(path);
            factory.GetImage(new ShellNative.SIZE { cx = pixels, cy = pixels }, 0, out hbm);
            if (hbm == IntPtr.Zero || ShellNative.GetObject(hbm, Marshal.SizeOf<ShellNative.BITMAP>(), out var bmp) == 0) return null;
            int w = bmp.bmWidth, h = Math.Abs(bmp.bmHeight);
            var bits = new byte[w * h * 4];
            var header = new ShellNative.BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            var dc = ShellNative.GetDC(IntPtr.Zero);
            try { if (ShellNative.GetDIBits(dc, hbm, 0, (uint)h, bits, ref header, 0) == 0) return null; }
            finally { ShellNative.ReleaseDC(IntPtr.Zero, dc); }
            // The Shell's images carry premultiplied alpha; read as plain BGRA their transparent corners turn black.
            var image = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, bits, w * 4);
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is COMException or FileNotFoundException or InvalidCastException or ArgumentException) { return null; }
        finally { if (hbm != IntPtr.Zero) ShellNative.DeleteObject(hbm); }
    }

    /// <summary>Opens an item as Explorer's double-click does. False when there is nothing to open it with.</summary>
    public static bool Open(string path)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException) { return false; }
    }
}
```

`ShellMenu.cs`:

```csharp
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Sanduhr.App.Interop;

/// <summary>One of Sanduhr's own items under Explorer's menu (Rename drawer, Fold, Hide, Drawer settings…).</summary>
internal sealed record ShellMenuItem(int Id, string Text);

/// <summary>What was picked: the Shell's canonical verb ("open", "rename", "delete"; null for one without), or one of ours.</summary>
internal sealed record ShellMenuChoice(string? Verb, int? Own);

/// <summary>
/// Explorer's own context menu for an item (spec section 2), with Sanduhr's items below a separator, or ours alone on empty space.
/// The spike's three rules (VERDICT.md): the owner goes to the foreground before <c>TrackPopupMenuEx</c> and gets <c>WM_NULL</c>
/// after, or every pick is dismissed; and <c>WM_INITMENUPOPUP</c>, <c>WM_DRAWITEM</c>, <c>WM_MEASUREITEM</c> and <c>WM_MENUCHAR</c>
/// go to <c>IContextMenu3</c> (<see cref="Forward"/>, from the Desk window's hook), or Open with and Send to stay empty.
/// </summary>
internal static class ShellMenu
{
    public const int OwnFirst = 0x8000;
    private const uint First = 1, Last = 0x7FFF;
    private const uint CMF_NORMAL = 0, CMF_CANRENAME = 0x10, TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2;
    private const uint CMIC_MASK_UNICODE = 0x4000, CMIC_MASK_PTINVOKE = 0x20000000, GCS_VERBW = 4, MF_STRING = 0, MF_SEPARATOR = 0x800;

    [ThreadStatic] private static ShellNative.IContextMenu3? t_open;

    /// <summary>Hands a menu message to the open Shell menu. True when it took it (the hook then marks it handled).</summary>
    public static bool Forward(int msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (t_open is null || msg is not (0x117 or 0x2B or 0x2C or 0x120)) return false;
        return t_open.HandleMenuMsg2((uint)msg, wParam, lParam, out result) == 0;
    }

    /// <summary>
    /// Shows the menu at a screen point and runs the pick. A Shell verb <paramref name="takeVerb"/> claims (true) is handed back
    /// instead of run (the drawer renames inline). Null when dismissed or when there is nothing to show.
    /// </summary>
    public static ShellMenuChoice? Show(IntPtr owner, string? path, int x, int y, IReadOnlyList<ShellMenuItem> extras, Func<string, bool> takeVerb)
    {
        ShellNative.IContextMenu? menu = null;
        var h = ShellNative.CreatePopupMenu();
        try
        {
            if (path is not null)
            {
                try
                {
                    var bhid = ShellNative.BHID_SFUIObject;
                    var iid = typeof(ShellNative.IContextMenu).GUID;
                    ShellNative.Item(path).BindToHandler(IntPtr.Zero, ref bhid, ref iid, out var o);
                    menu = (ShellNative.IContextMenu)o;
                    t_open = o as ShellNative.IContextMenu3;
                    menu.QueryContextMenu(h, 0, First, Last, CMF_NORMAL | CMF_CANRENAME);
                }
                catch (Exception e) when (e is COMException or FileNotFoundException or InvalidCastException) { menu = null; }
            }
            if (extras.Count > 0 && ShellNative.GetMenuItemCount(h) > 0) ShellNative.AppendMenuW(h, MF_SEPARATOR, UIntPtr.Zero, null);
            foreach (var item in extras) ShellNative.AppendMenuW(h, MF_STRING, (UIntPtr)(uint)item.Id, item.Text);
            if (ShellNative.GetMenuItemCount(h) == 0) return null;

            DeskNative.SetForegroundWindow(owner);
            uint cmd = ShellNative.TrackPopupMenuEx(h, TPM_RETURNCMD | TPM_RIGHTBUTTON, x, y, owner, IntPtr.Zero);
            ShellNative.PostMessageW(owner, 0, IntPtr.Zero, IntPtr.Zero);
            if (cmd == 0) return null;
            if (cmd >= OwnFirst) return new ShellMenuChoice(null, (int)cmd);
            if (menu is null) return null;

            var verb = Verb(menu, cmd - First);
            if (verb is not null && takeVerb(verb)) return new ShellMenuChoice(verb, null);
            Invoke(menu, owner, (IntPtr)(cmd - First), (IntPtr)(cmd - First), x, y);
            return new ShellMenuChoice(verb, null);
        }
        finally
        {
            t_open = null;
            ShellNative.DestroyMenu(h);
            if (menu is not null) Marshal.FinalReleaseComObject(menu);
        }
    }

    /// <summary>Runs one verb on an item without showing a menu (the Delete key: Explorer's own delete, to the Recycle Bin).</summary>
    public static bool InvokeVerb(IntPtr owner, string path, string verb)
    {
        ShellNative.IContextMenu? menu = null;
        var h = ShellNative.CreatePopupMenu();
        IntPtr ansi = Marshal.StringToHGlobalAnsi(verb), wide = Marshal.StringToHGlobalUni(verb);
        try
        {
            var bhid = ShellNative.BHID_SFUIObject;
            var iid = typeof(ShellNative.IContextMenu).GUID;
            ShellNative.Item(path).BindToHandler(IntPtr.Zero, ref bhid, ref iid, out var o);
            menu = (ShellNative.IContextMenu)o;
            menu.QueryContextMenu(h, 0, First, Last, CMF_NORMAL);
            return Invoke(menu, owner, ansi, wide, 0, 0) >= 0;
        }
        catch (Exception e) when (e is COMException or FileNotFoundException or InvalidCastException) { return false; }
        finally
        {
            Marshal.FreeHGlobal(ansi);
            Marshal.FreeHGlobal(wide);
            ShellNative.DestroyMenu(h);
            if (menu is not null) Marshal.FinalReleaseComObject(menu);
        }
    }

    private static string? Verb(ShellNative.IContextMenu menu, uint offset)
    {
        var sb = new StringBuilder(64);
        return menu.GetCommandString((UIntPtr)offset, GCS_VERBW, IntPtr.Zero, sb, sb.Capacity) == 0 && sb.Length > 0
            ? sb.ToString().ToLowerInvariant() : null;
    }

    private static int Invoke(ShellNative.IContextMenu menu, IntPtr owner, IntPtr verb, IntPtr verbW, int x, int y)
    {
        var info = new ShellNative.CMINVOKECOMMANDINFOEX
        {
            cbSize = Marshal.SizeOf<ShellNative.CMINVOKECOMMANDINFOEX>(),
            fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
            hwnd = owner,
            lpVerb = verb,
            lpVerbW = verbW,
            nShow = 1,
            ptInvoke = new ShellNative.POINT { X = x, Y = y },
        };
        return menu.InvokeCommand(ref info);
    }
}
```

`DarkMenus.cs`:

```csharp
using System.Runtime.InteropServices;

namespace Sanduhr.App.Interop;

/// <summary>
/// Explorer's menus in dark mode when Windows is (VERDICT.md, change 4): uxtheme's undocumented ordinals 135
/// (<c>SetPreferredAppMode</c>, 1 = follow the system) and 136 (<c>FlushMenuThemes</c>), once at start. Guarded: a Windows without them
/// keeps light menus. Only Win32 menus follow it; Sanduhr's own WPF menus are themed already.
/// </summary>
internal static class DarkMenus
{
    [DllImport("uxtheme.dll", EntryPoint = "#135")] private static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint = "#136")] private static extern void FlushMenuThemes();

    public static bool Enable()
    {
        if (Environment.OSVersion.Version.Build < 17763) return false;
        try
        {
            SetPreferredAppMode(1);
            FlushMenuThemes();
            return true;
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException) { return false; }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~ShellFilesTests" --disable-build-servers -nodeReuse:false`
Expected: PASS (the junction test may report Skipped where mklink is unavailable).

- [ ] **Step 6: Streamer reach and the full App suite**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~StreamerReachTests|FullyQualifiedName~NetworkDisclosure" --disable-build-servers -nodeReuse:false`
Expected first: FAIL naming the new files with their counts. Check every flagged line carries its marker, add the rows the failure
lists to `Known` in `StreamerReachTests.cs`, and if `NetworkDisclosureTests` names `ShellFiles.Open`'s `Process.Start`, give it the
marker that test asks for local launches. Run again: PASS. Then `dotnet test tests/Sanduhr.App.Tests --disable-build-servers -nodeReuse:false`: PASS.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Interop/ShellNative.cs windows-dotnet/src/Sanduhr.App/Interop/ShellFiles.cs windows-dotnet/src/Sanduhr.App/Interop/ShellMenu.cs windows-dotnet/src/Sanduhr.App/Interop/DarkMenus.cs windows-dotnet/tests/Sanduhr.App.Tests/DrawerSandbox.cs windows-dotnet/tests/Sanduhr.App.Tests/ShellFilesTests.cs windows-dotnet/tests/Sanduhr.Tests/StreamerReachTests.cs
git commit -m "feat(drawers): Shell interop for moves, renames, final paths, holders, thumbnails, Explorer's menu and dark menus

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: The mover and the journal on disk

`DrawerMover` is the only code that moves a user's file. It runs a list of steps (a source and the full path it should land at) one
file at a time, checks final paths before each, takes a fresh clash name if the planned one was taken since, verifies on disk, and
reports what moved and what stayed, with why.

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerMover.cs`
- Create: `windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerJournalStore.cs`
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DrawerMoverTests.cs` (new)

**Interfaces:**
- Consumes: `ShellFiles.Move`, `FinalPath`, `Holder` (Task 4); `DrawerPaths.MoveAllowed`, `DrawerSkip`, `DrawerSkipped`, `DrawerReview.ResultLine`, `DrawerJournal.Without` (Tasks 2 and 3); `DrawerNames.Free`, `UndoStep`, `UndoPlan`, `ReturnPlan`, `DrawerPlanCheck`, `JournalMove`, `JournalEntry` (plan 1); `ResetGuard.Done` (`Sanduhr.App.Services`).
- Produces (namespace `Sanduhr.App.Desk`):
  - `record DrawerRun(IReadOnlyList<JournalMove> Moved, IReadOnlyList<DrawerSkipped> Skipped)` with `string Line` and `static DrawerRun Empty`.
  - `record DrawerReturnRun(DrawerRun Run, IReadOnlyList<string> KeptHomes)`.
  - `DrawerMover(string desktop)`; `Run(IEnumerable<UndoStep> steps, IReadOnlyList<string> homes, bool toDesktop, Func<bool>? stop = null) : DrawerRun`; `Apply(DrawerPlanCheck check, IReadOnlyList<DrawerDef> drawers) : DrawerRun`; `Undo(UndoPlan plan, IReadOnlyList<string> homes) : DrawerRun`; `Return(ReturnPlan plan, IReadOnlyList<string> homes, Func<bool>? stop = null) : DrawerReturnRun`; `static Exists(string path) : bool`.
  - `DrawerJournalStore(string file)`; `Load() : DrawerJournal`; `Add(DrawerPlanSource source, string? agent, IReadOnlyList<JournalMove> moves, DateTime nowUtc) : JournalEntry`; `Remove(string id)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Sanduhr.App.Desk;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Moves, undo and return, file by file in a sandbox (5.0 drawers spec, section 4; drawers plan 2, Task 5).</summary>
public sealed class DrawerMoverTests
{
    private static DrawerDef Def(DrawerSandbox box, string name) => new(DrawerIds.From(name), name, Path.Combine(box.Drawers, name), true);

    private static DrawerPlanCheck Check(DrawerSandbox box, IReadOnlyList<DrawerDef> existing, params (string File, DrawerDef Drawer)[] moves)
    {
        var plan = new DrawerPlan(DrawerPlanSource.Rules, null, moves.Select(m => m.Drawer).Distinct().ToArray(),
            moves.Select(m => new DrawerMove(m.File, m.Drawer.Id)).ToArray());
        var check = DrawerPlanValidator.Check(plan, box.Roots, existing);
        Assert.True(check.Ok, check.Error);
        return check;
    }

    [Fact]
    public void Apply_creates_new_homes_moves_files_and_records_each_move() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var a = box.File(@"Desktop\a.png");
        var images = Def(box, "Images");
        var run = new DrawerMover(box.Desktop).Apply(Check(box, [], (a, images)), []);
        var m = Assert.Single(run.Moved);
        Assert.Equal(a, m.From);
        Assert.Equal(Path.Combine(images.Home, "a.png"), m.To, ignoreCase: true);
        Assert.NotNull(m.WrittenUtc);
        Assert.Equal("1 moved.", run.Line);
    });

    [Fact]
    public void A_name_taken_in_the_drawer_gets_a_clash_name_and_nothing_is_overwritten() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var images = Def(box, "Images");
        box.File(@"Desktop\Drawers\Images\a.png", "old");
        var a = box.File(@"Desktop\a.png", "new");
        var run = new DrawerMover(box.Desktop).Apply(Check(box, [images], (a, images)), [images]);
        Assert.Equal(Path.Combine(images.Home, "a (2).png"), Assert.Single(run.Moved).To, ignoreCase: true);
        Assert.Equal("old", File.ReadAllText(Path.Combine(images.Home, "a.png")));
    });

    [Fact]
    public void A_file_gone_before_Apply_is_skipped_and_the_rest_move() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var images = Def(box, "Images");
        var a = box.File(@"Desktop\a.png");
        var b = box.File(@"Desktop\b.png");
        var check = Check(box, [], (a, images), (b, images));
        File.Delete(a);   // the user tidied by hand while the bar was up
        var run = new DrawerMover(box.Desktop).Apply(check, []);
        Assert.Single(run.Moved);
        Assert.Equal("1 moved, 1 skipped: a.png was moved since.", run.Line);
    });

    [Fact]
    public void A_locked_file_is_skipped_with_the_app_holding_it_and_the_run_carries_on() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var docs = Def(box, "Documents");
        var locked = box.File(@"Desktop\report.docx");
        var free = box.File(@"Desktop\notes.txt");
        var check = Check(box, [], (locked, docs), (free, docs));
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var run = new DrawerMover(box.Desktop).Apply(check, []);
        Assert.Equal(free, Assert.Single(run.Moved).From);
        var skip = Assert.Single(run.Skipped);
        Assert.Equal("report.docx", skip.Name);
        Assert.StartsWith("is open in ", skip.Reason);
        Assert.True(File.Exists(locked));
    });

    [SkippableFact]
    public void A_Desktop_junction_leading_outside_is_never_moved() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var outside = box.Dir("Elsewhere");
        var link = Path.Combine(box.Desktop, "link");
        Skip.IfNot(box.Junction(link, outside), "mklink /J is not available here.");
        var junk = Def(box, "Junk");
        Directory.CreateDirectory(junk.Home);
        var run = new DrawerMover(box.Desktop).Run([new UndoStep(link, Path.Combine(junk.Home, "link"))], [junk.Home], toDesktop: false);
        Assert.Equal(DrawerSkip.Outside, Assert.Single(run.Skipped).Reason);
        Assert.True(Directory.Exists(link));
    });

    [Fact]
    public void Undo_puts_files_back_and_skips_one_changed_since() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var images = Def(box, "Images");
        var a = box.File(@"Desktop\a.png");
        var b = box.File(@"Desktop\b.png");
        var mover = new DrawerMover(box.Desktop);
        var run = mover.Apply(Check(box, [], (a, images), (b, images)), []);
        var entry = new JournalEntry("e1", DateTime.UtcNow, DrawerPlanSource.Rules, null, run.Moved);
        File.SetLastWriteTimeUtc(Path.Combine(images.Home, "b.png"), DateTime.UtcNow.AddMinutes(5));   // edited since
        var undo = DrawerUndo.Plan(entry, p => DrawerMover.Exists(p) ? File.GetLastWriteTimeUtc(p) : null, DrawerMover.Exists);
        var back = mover.Undo(undo, [images.Home]);
        Assert.True(File.Exists(a));
        Assert.Equal("1 moved, 1 skipped: b.png changed since.", back.Line);
    });

    [Fact]
    public void Return_empties_owned_homes_and_removes_them_but_keeps_one_holding_a_locked_file() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var images = Def(box, "Images");
        var docs = Def(box, "Documents");
        box.File(@"Desktop\Drawers\Images\a.png");
        var locked = box.File(@"Desktop\Drawers\Documents\report.docx");
        box.File(@"Desktop\a.png");   // a clash on the way back
        var plan = DrawerReturn.Plan([images, docs], box.Drawers, box.Desktop,
            dir => Directory.EnumerateFileSystemEntries(dir).Select(p => new DesktopItem(p, Directory.Exists(p), DateTime.UtcNow)),
            DrawerMover.Exists);
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = new DrawerMover(box.Desktop).Return(plan, [images.Home, docs.Home]);
        Assert.True(File.Exists(Path.Combine(box.Desktop, "a (2).png")));
        Assert.False(Directory.Exists(images.Home));
        Assert.Equal([docs.Home, box.Drawers], result.KeptHomes);
        Assert.StartsWith("1 moved, 1 skipped: report.docx is open in ", result.Run.Line);
    });

    [Fact]
    public void A_home_holding_only_Windows_housekeeping_files_counts_as_empty() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var images = Def(box, "Images");
        var ini = box.File(@"Desktop\Drawers\Images\desktop.ini", "[.ShellClassInfo]");
        File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
        var plan = new ReturnPlan([], [images.Home, box.Drawers]);
        Assert.Empty(new DrawerMover(box.Desktop).Return(plan, [images.Home]).KeptHomes);
        Assert.False(Directory.Exists(box.Drawers));
    });

    [Fact]
    public void The_journal_store_adds_reads_and_removes_entries()
    {
        using var box = new DrawerSandbox();
        var store = new DrawerJournalStore(Path.Combine(box.Root, "app", "drawers-journal.json"));
        var e = store.Add(DrawerPlanSource.User, null, [new JournalMove(@"C:\a", @"C:\b", false, DateTime.UtcNow)], DateTime.UtcNow);
        Assert.Equal(e.Id, Assert.Single(store.Load().Entries).Id);
        store.Remove(e.Id);
        Assert.Empty(store.Load().Entries);
    }

    [Fact]
    public void A_damaged_journal_file_reads_as_empty()
    {
        using var box = new DrawerSandbox();
        var file = box.File(@"app\drawers-journal.json", "{ not json");
        Assert.Empty(new DrawerJournalStore(file).Load().Entries);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DrawerMoverTests" --disable-build-servers -nodeReuse:false`
Expected: build FAIL: "The type or namespace name 'DrawerMover' could not be found".

- [ ] **Step 3: Implement**

`DrawerMover.cs`:

```csharp
using System.IO;
using Sanduhr.App.Interop;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>What one run did: the moves that landed (journal-ready) and the files that stayed, by name, with why.</summary>
internal sealed record DrawerRun(IReadOnlyList<JournalMove> Moved, IReadOnlyList<DrawerSkipped> Skipped)
{
    public static DrawerRun Empty { get; } = new([], []);

    /// <summary>"13 moved, 1 skipped: report.docx is open in Word."</summary>
    public string Line => DrawerReview.ResultLine(Moved.Count, Skipped);
}

/// <summary>Turning drawers off: the run, and the homes (and root) kept because something in them could not move.</summary>
internal sealed record DrawerReturnRun(DrawerRun Run, IReadOnlyList<string> KeptHomes);

/// <summary>
/// The only code that moves a user's file (5.0 drawers spec, section 4). One item per Shell operation; before each, the final paths
/// (junctions, links and 8.3 names resolved) must pass <see cref="DrawerPaths.MoveAllowed"/>, so a link swapped in since the plan was
/// checked is caught. A name taken since the plan gets a fresh clash name; the result is checked on disk; a failure skips that one file.
/// </summary>
internal sealed class DrawerMover
{
    private static readonly string[] Housekeeping = ["desktop.ini", "Thumbs.db"];
    private readonly string _desktop;

    public DrawerMover(string desktop) => _desktop = desktop;

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>Applies a checked plan: makes the homes it creates, then moves each file into its drawer.</summary>
    public DrawerRun Apply(DrawerPlanCheck check, IReadOnlyList<DrawerDef> drawers)
    {
        foreach (var d in check.Drawers) Directory.CreateDirectory(d.Home);
        var homes = drawers.Concat(check.Drawers).GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First().Home, StringComparer.Ordinal);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steps = new List<UndoStep>();
        foreach (var m in check.Moves)
        {
            var dir = homes[m.DrawerId];
            var to = DrawerNames.Free(dir, Path.GetFileName(m.Source), Directory.Exists(m.Source), p => Exists(p) || claimed.Contains(p));   // streamer: key - a file's leaf names its new place, never drawn
            claimed.Add(to);
            steps.Add(new UndoStep(m.Source, to));
        }
        return Run(steps, [.. homes.Values], toDesktop: false);
    }

    /// <summary>Undo for one journal entry: Core's plan, run, with the files Core already skipped added to the result.</summary>
    public DrawerRun Undo(UndoPlan plan, IReadOnlyList<string> homes)
    {
        var run = Run(plan.Steps, homes, toDesktop: true);
        var skipped = plan.Skipped.Select(s => new DrawerSkipped(Path.GetFileName(s.Path), s.Reason)).Concat(run.Skipped).ToArray();   // streamer: key - a file's leaf for the result line
        return run with { Skipped = skipped };
    }

    /// <summary>Turning drawers off: every item back on the Desktop, then each listed folder removed once it is empty.</summary>
    public DrawerReturnRun Return(ReturnPlan plan, IReadOnlyList<string> homes, Func<bool>? stop = null)
    {
        var run = Run(plan.Moves, homes, toDesktop: true, stop);
        var kept = new List<string>();
        foreach (var dir in plan.HomesToRemove)
            if (Directory.Exists(dir) && !RemoveIfEmpty(dir)) kept.Add(dir);
        return new DrawerReturnRun(run, kept);
    }

    public DrawerRun Run(IEnumerable<UndoStep> steps, IReadOnlyList<string> homes, bool toDesktop, Func<bool>? stop = null)
    {
        var moved = new List<JournalMove>();
        var skipped = new List<DrawerSkipped>();
        var finalDesktop = ShellFiles.FinalPath(_desktop) ?? _desktop;
        var finalHomes = homes.Select(h => ShellFiles.FinalPath(h) ?? h).ToArray();
        foreach (var step in steps)
        {
            var name = Path.GetFileName(step.From);   // streamer: key - a file's leaf for the result line, which its reader masks
            if (stop?.Invoke() == true) { skipped.Add(new(name, DrawerSkip.NotInTime)); continue; }
            bool isFolder = Directory.Exists(step.From);
            if (!isFolder && !File.Exists(step.From)) { skipped.Add(new(name, DrawerSkip.MovedSince)); continue; }
            var targetDir = Path.GetDirectoryName(step.To)!;   // streamer: key - the folder a file goes to, never drawn
            var finalSource = ShellFiles.FinalPath(step.From);
            var finalTarget = ShellFiles.FinalPath(targetDir);
            var refused = finalSource is null || finalTarget is null
                ? DrawerSkip.Outside
                : DrawerPaths.MoveAllowed(finalSource, finalTarget, finalDesktop, finalHomes, toDesktop);
            if (refused is not null) { skipped.Add(new(name, refused)); continue; }

            // The name chosen when the plan was made may have been taken since.
            var to = DrawerNames.Free(targetDir, Path.GetFileName(step.To), isFolder, Exists);   // streamer: key - a file's leaf names its new place, never drawn
            DateTime? written = isFolder ? null : File.GetLastWriteTimeUtc(step.From);
            var result = ShellFiles.Move(step.From, targetDir, Path.GetFileName(to));   // streamer: key - a file's leaf names its new place, never drawn
            if (result.Ok && !Exists(step.From)) moved.Add(new JournalMove(step.From, result.NewPath ?? to, isFolder, written));
            else skipped.Add(new(name, Reason(step.From)));
        }
        return new DrawerRun(moved, skipped);
    }

    private static string Reason(string path) => ShellFiles.Holder(path) is { } app ? DrawerSkip.OpenIn(app) : DrawerSkip.CouldNotMove;

    /// <summary>Removes a folder that holds nothing but Windows' own housekeeping files (desktop.ini, Thumbs.db), which Windows writes into
    /// folders it shows; they are never a user's. False when anything else is in it or it cannot be removed.</summary>
    private static bool RemoveIfEmpty(string dir)
    {
        try
        {
            var entries = Directory.EnumerateFileSystemEntries(dir).ToArray();
            if (entries.Any(e => !Housekeeping.Contains(Path.GetFileName(e), StringComparer.OrdinalIgnoreCase))) return false;   // streamer: key - compared with fixed names, never drawn
            foreach (var e in entries)
            {
                File.SetAttributes(e, FileAttributes.Normal);
                File.Delete(e);
            }
            Directory.Delete(dir, recursive: false);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
```

`DrawerJournalStore.cs`:

```csharp
using System.IO;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>The move journal on disk (5.0 drawers spec, section 4): paths only, in Sanduhr's data folder, apart from the Velopack install
/// tree. Written through a temp file so a crash never leaves half a journal; nothing is written after Reset.</summary>
internal sealed class DrawerJournalStore
{
    private readonly string _file;

    public DrawerJournalStore(string file) => _file = file;

    public DrawerJournal Load()
    {
        try { return File.Exists(_file) ? DrawerJournal.Decode(File.ReadAllText(_file)) : new DrawerJournal(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new DrawerJournal(); }
    }

    public JournalEntry Add(DrawerPlanSource source, string? agent, IReadOnlyList<JournalMove> moves, DateTime nowUtc)
    {
        var entry = new JournalEntry(Guid.NewGuid().ToString("N"), nowUtc, source, agent, moves);
        Save(Load().Add(entry, nowUtc));
        return entry;
    }

    public void Remove(string id) => Save(Load().Without(id));

    private void Save(DrawerJournal journal)
    {
        if (ResetGuard.Done) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);   // streamer: notname - Sanduhr's own data folder, used for IO
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, journal.Encode());
            File.Move(tmp, _file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the journal is best-effort; the moves stand */ }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DrawerMoverTests" --disable-build-servers -nodeReuse:false`
Expected: PASS (the junction test may report Skipped).

- [ ] **Step 5: Streamer reach, then commit**

Run `StreamerReachTests`; check each flagged line in the two new files carries its marker; add the rows the failure lists to `Known`; run again: PASS.

```bash
git add windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerMover.cs windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerJournalStore.cs windows-dotnet/tests/Sanduhr.App.Tests/DrawerMoverTests.cs windows-dotnet/tests/Sanduhr.Tests/StreamerReachTests.cs
git commit -m "feat(drawers): the mover runs moves, undo and return file by file with final-path checks; the journal on disk

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: The drawer service

The long-lived home of every drawer's state, so a Desk redraw rebuilds only the visuals (the companion's pattern). It owns the
listings, a debounced watcher per home, thumbnails on a background STA thread, the user's hand moves (journaled), fold, hide,
rename, view drawers, the preview's pending items, and the **busy holds** a redraw waits on.

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerPlaces.cs`
- Create: `windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerState.cs`
- Create: `windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerIcons.cs`
- Create: `windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerService.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs` (`LoadDrawers`, `SaveDrawers`, beside `LoadCompanion`)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DrawerServiceTests.cs` (new)

**Interfaces:**
- Consumes: Task 5's `DrawerMover`, `DrawerRun`, `DrawerJournalStore`; Task 4's `ShellFiles.Thumbnail`, `Rename`; Core's `DrawerSettings` (with `Folded`), `DrawerPlanValidator`, `DrawerUndo`, `DrawerArrival`, `DrawerIds.Unique`, `DeskArrangement`, `DrawerPieces`, `StreamerMode.Mask`.
- Produces (namespace `Sanduhr.App.Desk`):
  - `record DrawerPlaces(string Desktop, string PublicDesktop, IReadOnlyList<string> Synced)` with `static ForThisUser()`.
  - `DrawerItem` (ObservableObject): `Path`, `Name`, `IsFolder`, `Pending`, `Icon : ImageSource?`, `Editing : bool`, `SpokenName`.
  - `DrawerState` (ObservableObject): `Def`, `Items : ObservableCollection<DrawerItem>`, `Folded`, `Missing`, `Title`, `FoldedTitle`, `SpokenName`, `Word`, `const MissingLine`.
  - `DrawerIcons(Dispatcher ui)`: `Request(DrawerItem)`, `Dispose()`.
  - `DrawerService(SettingsStore store, Paths paths, DrawerPlaces places, Dispatcher? ui = null)`; `static Current`; `Settings`; `Places`; `Roots`; `Homes`; `Journal`; `Mover`; `Drawers`; `Get(string id) : DrawerState?`; `event Changed`; `Busy`; `event BusyEnded`; `Hold() : IDisposable`; `Reload()`; `Save(DrawerSettings next)`; `Accepts(IReadOnlyList<string> paths, string id) : bool`; `MoveIn(IReadOnlyList<string> paths, string id) : DrawerRun`; `Undo(string entryId) : DrawerRun`; `SetFolded(string id, bool folded)`; `Hide(string id)`; `ShowAll()`; `RenameDrawer(string id, string name) : string?`; `RenameItem(string path, string newName) : string?`; `AddView(string name, string folder) : string?`; `SetPending(DrawerPlanCheck check)`; `ClearPending()`; `DesktopListing() : IReadOnlyList<DesktopItem>`.
  - `SettingsStore.LoadDrawers() : DrawerSettings`, `SaveDrawers(DrawerSettings)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Drawer state across redraws, listings, watchers, hand moves and holds, in a sandbox (drawers plan 2, Task 6).</summary>
public sealed class DrawerServiceTests
{
    private static (DrawerService Service, SettingsStore Store) Make(DrawerSandbox box, params string[] drawers)
    {
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"));
        var store = new SettingsStore(paths);
        var defs = drawers.Select(n => new DrawerDef(DrawerIds.From(n), n, Path.Combine(box.Drawers, n), true)).ToArray();
        foreach (var d in defs) Directory.CreateDirectory(d.Home);
        store.SaveDrawers(DrawerSettings.Default with { Enabled = true, Drawers = defs });
        return (new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, []), Dispatcher.CurrentDispatcher), store);
    }

    /// <summary>Lets the dispatcher run queued work and timers for a while (the watcher's debounce).</summary>
    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    [Fact]
    public void Each_drawer_lists_its_folder_folders_first_and_skips_housekeeping_files() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        box.File(@"Desktop\Drawers\Images\b.png");
        box.File(@"Desktop\Drawers\Images\a.png");
        box.Dir(@"Desktop\Drawers\Images\zfolder");
        box.File(@"Desktop\Drawers\Images\desktop.ini");
        var (s, _) = Make(box, "Images");
        Assert.Equal(["zfolder", "a.png", "b.png"], s.Get("images")!.Items.Select(i => i.Name));
        Assert.Equal("Images drawer, 3 items", s.Get("images")!.SpokenName);
        s.Dispose();
    });

    [Fact]
    public void A_drawer_whose_folder_is_gone_says_so_instead_of_crashing() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _) = Make(box, "Images");
        Directory.Delete(Path.Combine(box.Drawers, "Images"));
        s.Reload();
        Assert.True(s.Get("images")!.Missing);
        Assert.Empty(s.Get("images")!.Items);
        s.Dispose();
    });

    [Fact]
    public void A_folder_deleted_while_watched_turns_the_drawer_missing() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        box.File(@"Desktop\Drawers\Images\a.png");
        var (s, _) = Make(box, "Images");
        Directory.Delete(Path.Combine(box.Drawers, "Images"), recursive: true);
        Pump(1500);
        Assert.True(s.Get("images")!.Missing);
        s.Dispose();
    });

    [Fact]
    public void A_file_added_outside_Sanduhr_appears_after_the_debounce() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _) = Make(box, "Images");
        box.File(@"Desktop\Drawers\Images\new.png");
        Pump(1500);
        Assert.Contains(s.Get("images")!.Items, i => i.Name == "new.png");
        s.Dispose();
    });

    [Fact]
    public void A_hand_move_lands_in_the_drawer_and_is_one_journal_entry() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _) = Make(box, "Images");
        var run = s.MoveIn([box.File(@"Desktop\a.png"), box.File(@"Desktop\b.png")], "images");
        Assert.Equal(2, run.Moved.Count);
        var entry = Assert.Single(s.Journal.Load().Entries);
        Assert.Equal(DrawerPlanSource.User, entry.Source);
        Assert.Equal(2, entry.Moves.Count);
        s.Dispose();
    });

    [Fact]
    public void Dropping_a_file_on_the_drawer_already_holding_it_does_nothing() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var a = box.File(@"Desktop\Drawers\Images\a.png");
        var (s, _) = Make(box, "Images");
        Assert.False(s.Accepts([a], "images"));
        var run = s.MoveIn([a], "images");
        Assert.Empty(run.Moved);
        Assert.Single(Directory.GetFiles(Path.Combine(box.Drawers, "Images")));
        Assert.Empty(s.Journal.Load().Entries);
        s.Dispose();
    });

    [Fact]
    public void A_file_from_outside_the_Desktop_and_the_drawers_is_not_accepted() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _) = Make(box, "Images");
        Assert.False(s.Accepts([box.File(@"Downloads\a.png")], "images"));
        Assert.True(s.Accepts([box.File(@"Desktop\a.png")], "images"));
        s.Dispose();
    });

    [Fact]
    public void Undo_of_a_hand_move_puts_the_file_back_and_drops_the_entry() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _) = Make(box, "Images");
        var a = box.File(@"Desktop\a.png");
        s.MoveIn([a], "images");
        var run = s.Undo(s.Journal.Load().Entries[0].Id);
        Assert.Single(run.Moved);
        Assert.True(File.Exists(a));
        Assert.Empty(s.Journal.Load().Entries);
        s.Dispose();
    });

    [Fact]
    public void Holds_make_the_service_busy_until_the_last_is_released() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _) = Make(box, "Images");
        int ended = 0;
        s.BusyEnded += () => ended++;
        var a = s.Hold();
        var b = s.Hold();
        Assert.True(s.Busy);
        a.Dispose();
        Assert.Equal(0, ended);
        b.Dispose();
        b.Dispose();   // releasing twice is harmless
        Assert.False(s.Busy);
        Assert.Equal(1, ended);
        s.Dispose();
    });

    [Fact]
    public void Fold_and_hide_are_saved() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store) = Make(box, "Images");
        store.SaveDesk(store.LoadDesk() with { Layout = "clock:bl drawer.images:tl:1" });
        s.SetFolded("images", true);
        Assert.True(s.Get("images")!.Folded);
        Assert.Equal(["images"], store.LoadDrawers().Folded);
        s.Hide("images");
        Assert.Null(new DeskArrangement(store.LoadDesk().Layout).Placement("drawer.images"));
        s.ShowAll();
        Assert.Equal(DeskAnchor.TL, new DeskArrangement(store.LoadDesk().Layout).Placement("drawer.images")!.Anchor);
        s.Dispose();
    });

    [Fact]
    public void Renaming_an_owned_drawer_renames_its_folder_and_keeps_its_id() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        box.File(@"Desktop\Drawers\Images\a.png");
        var (s, store) = Make(box, "Images");
        Assert.Null(s.RenameDrawer("images", "Photos"));
        var d = Assert.Single(store.LoadDrawers().Drawers);
        Assert.Equal(("images", "Photos", Path.Combine(box.Drawers, "Photos")), (d.Id, d.Name, d.Home));
        Assert.True(File.Exists(Path.Combine(box.Drawers, "Photos", "a.png")));
        Assert.NotNull(s.RenameDrawer("images", "CON"));
        s.Dispose();
    });

    [Fact]
    public void Renaming_a_file_goes_through_the_Shell_and_refuses_a_taken_name() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var a = box.File(@"Desktop\Drawers\Images\a.png");
        box.File(@"Desktop\Drawers\Images\b.png");
        var (s, _) = Make(box, "Images");
        Assert.NotNull(s.RenameItem(a, "b.png"));
        Assert.Null(s.RenameItem(a, "c.png"));
        Assert.True(File.Exists(Path.Combine(box.Drawers, "Images", "c.png")));
        s.Dispose();
    });

    [Fact]
    public void Pending_items_show_faded_in_their_drawer_and_new_drawers_preview() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _) = Make(box, "Images");
        var a = box.File(@"Desktop\a.png");
        var c = box.File(@"Desktop\c.pdf");
        var docs = new DrawerDef("documents", "Documents", Path.Combine(box.Drawers, "Documents"), true);
        var plan = new DrawerPlan(DrawerPlanSource.Rules, null, [docs], [new DrawerMove(a, "images"), new DrawerMove(c, "documents")]);
        s.SetPending(DrawerPlanValidator.Check(plan, box.Roots, s.Settings.Drawers));
        Assert.True(s.Get("images")!.Items.Single(i => i.Path == a).Pending);
        Assert.True(s.Get("documents")!.Items.Single().Pending);
        s.ClearPending();
        Assert.Empty(s.Get("images")!.Items);
        Assert.Null(s.Get("documents"));
        s.Dispose();
    });

    [Fact]
    public void The_Desktop_listing_flags_hidden_items_and_leaves_out_the_Public_Desktop() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _) = Make(box);
        var h = box.File(@"Desktop\hidden.txt");
        File.SetAttributes(h, FileAttributes.Hidden);
        box.File(@"Desktop\shown.txt");
        box.File(@"Public\shared.lnk");
        var list = s.DesktopListing();
        Assert.Contains(list, i => i.Path == h && i.Hidden);
        Assert.Contains(list, i => i.Path.EndsWith("shown.txt") && !i.Hidden);
        Assert.DoesNotContain(list, i => i.Path.EndsWith("shared.lnk"));
        s.Dispose();
    });

    [Fact]
    public void A_view_drawer_shows_an_existing_folder_and_two_names_get_different_ids() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store) = Make(box);
        Assert.Null(s.AddView("Фото", box.Dir("Downloads")));
        Assert.Null(s.AddView("写真", box.Dir("Pictures")));
        var ids = store.LoadDrawers().Drawers.Select(d => (d.Id, d.Owned)).ToArray();
        Assert.Equal([("drawer", false), ("drawer-2", false)], ids);
        s.Dispose();
    });

    [Fact]
    public void Turned_off_drawers_are_not_drawn() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store) = Make(box, "Images");
        store.SaveDrawers(store.LoadDrawers() with { Enabled = false });
        s.Reload();
        Assert.Null(s.Get("images"));
        s.Dispose();
    });
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DrawerServiceTests" --disable-build-servers -nodeReuse:false`
Expected: build FAIL on the missing `DrawerService`, `DrawerPlaces` and `SettingsStore.SaveDrawers`.

- [ ] **Step 3: Implement**

`SettingsStore.cs`, beside `LoadCompanion`:

```csharp
    /// <summary>Drawers (5.0 drawers spec): <c>drawers</c>, absent = off with the built-in rules, so an upgrade changes nothing.</summary>
    public DrawerSettings LoadDrawers() => DrawerSettings.Decode(Read()[DrawerSettings.Key]);

    public void SaveDrawers(DrawerSettings drawers)
    {
        var root = Read();
        root[DrawerSettings.Key] = drawers.Encode();
        Write(root);
    }
```

`DrawerPlaces.cs`:

```csharp
namespace Sanduhr.App.Desk;

/// <summary>Where drawers work on this PC: the user's Desktop, the Public Desktop, and the OneDrive roots (for the sync line). Tests pass
/// a sandbox.</summary>
internal sealed record DrawerPlaces(string Desktop, string PublicDesktop, IReadOnlyList<string> Synced)
{
    public static DrawerPlaces ForThisUser() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }
            .Select(Environment.GetEnvironmentVariable).OfType<string>().Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
}
```

`DrawerState.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>One file or folder in a drawer. <see cref="Pending"/> marks one a plan under review would move here (drawn faded).</summary>
internal sealed partial class DrawerItem : ObservableObject
{
    public DrawerItem(string path, bool isFolder, bool pending)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);   // streamer: masked - the label blurs and the spoken name is SpokenName while streamer mode is on
        IsFolder = isFolder;
        Pending = pending;
    }

    public string Path { get; }
    public string Name { get; }
    public bool IsFolder { get; }
    public bool Pending { get; }

    [ObservableProperty] private ImageSource? _icon;
    [ObservableProperty] private bool _editing;

    /// <summary>The item's accessible name: its file name (spec section 2), or "File" while streamer mode is on.</summary>
    public string SpokenName => StreamerMode.Mask.Omit(Name) ?? (IsFolder ? "Folder" : "File");
}

/// <summary>One drawer's live state, kept across Desk redraws (the piece is rebuilt, this is not).</summary>
internal sealed partial class DrawerState : ObservableObject
{
    public const string MissingLine = "This drawer's folder is gone.";

    public DrawerState(DrawerDef def) => Def = def;

    public DrawerDef Def { get; internal set; }
    public ObservableCollection<DrawerItem> Items { get; } = [];
    public string Word => DrawerPieces.Word(Def.Id);

    [ObservableProperty] private bool _folded;
    [ObservableProperty] private bool _missing;

    /// <summary>The drawer's name as drawn: its codename while streamer mode is on.</summary>
    public string Title => StreamerMode.Mask.Name(CodenameCategory.Folder, Def.Name);   // streamer: masked - Mask.Name gives the codename while on

    private int Count => Items.Count(i => !i.Pending);

    /// <summary>The folded title bar: "Screenshots · 14".</summary>
    public string FoldedTitle => $"{Title} · {Count}";

    /// <summary>The drawer's group name (spec section 2): "Screenshots drawer, 14 items".</summary>
    public string SpokenName => $"{Title} drawer, {Count} {(Count == 1 ? "item" : "items")}";

    /// <summary>The item list changed: the titles that count follow.</summary>
    internal void CountChanged()
    {
        OnPropertyChanged(nameof(FoldedTitle));
        OnPropertyChanged(nameof(SpokenName));
    }
}
```

`DrawerIcons.cs`:

```csharp
using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using Sanduhr.App.Interop;

namespace Sanduhr.App.Desk;

/// <summary>Thumbnails and icons for drawer items, made on one background STA thread (the Shell's image factory can take a while on a big
/// image) and cached by path and write time. A frozen image is handed to the UI thread; nothing blocks a redraw.</summary>
internal sealed class DrawerIcons : IDisposable
{
    public const int Pixels = 64;
    private const int MaxCached = 2000;
    private readonly BlockingCollection<DrawerItem> _queue = new();
    private readonly ConcurrentDictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dispatcher _ui;

    public DrawerIcons(Dispatcher ui)
    {
        _ui = ui;
        var worker = new Thread(Work) { IsBackground = true, Name = "Drawer icons" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    public void Request(DrawerItem item)
    {
        if (_cache.TryGetValue(Key(item.Path), out var hit)) { item.Icon = hit; return; }
        if (!_queue.IsAddingCompleted) _queue.TryAdd(item);
    }

    private void Work()
    {
        foreach (var item in _queue.GetConsumingEnumerable())
        {
            if (ShellFiles.Thumbnail(item.Path, Pixels) is not { } image) continue;
            if (_cache.Count < MaxCached) _cache[Key(item.Path)] = image;
            _ui.BeginInvoke(() => item.Icon = image);
        }
    }

    private static string Key(string path) => path + "|" + (File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0);

    public void Dispose() => _queue.CompleteAdding();
}
```

`DrawerService.cs`:

```csharp
using System.IO;
using System.Windows.Threading;
using Sanduhr.App.Interop;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// Drawers' live side (5.0 drawers spec, section 2): every drawer's state, kept across Desk redraws so a redraw rebuilds visuals only
/// (the companion's pattern). Lists each home, watches it (debounced, as the Desk's other watchers are), hands out thumbnails, runs the
/// user's own moves through the journal, and keeps fold, hide and names in settings. A drawer mid-interaction holds the service busy
/// (<see cref="Hold"/>), and the Desk waits to redraw until the last hold is released.
/// </summary>
internal sealed class DrawerService : IDisposable
{
    public static DrawerService? Current { get; set; }

    private static readonly string[] Housekeeping = ["desktop.ini", "Thumbs.db"];
    private readonly SettingsStore _store;
    private readonly Dispatcher _ui;
    private readonly Dictionary<string, DrawerState> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DrawerState> _preview = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dirty = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _debounce;
    private readonly DrawerIcons _icons;
    private readonly List<object> _holds = [];
    private ILookup<string, string> _pending = Array.Empty<string>().ToLookup(s => s);
    private bool _disposed;

    public DrawerService(SettingsStore store, Paths paths, DrawerPlaces places, Dispatcher? ui = null)
    {
        _store = store;
        Places = places;
        _ui = ui ?? Dispatcher.CurrentDispatcher;
        Journal = new DrawerJournalStore(paths.DrawerJournalFile);
        Mover = new DrawerMover(places.Desktop);
        _icons = new DrawerIcons(_ui);
        _debounce = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) => Flush();
        Reload();
    }

    public DrawerSettings Settings { get; private set; } = DrawerSettings.Default;
    public DrawerPlaces Places { get; }
    public DrawerJournalStore Journal { get; }
    public DrawerMover Mover { get; }

    public DrawerRoots Roots => new(Places.Desktop, Places.PublicDesktop, Settings.RootOr(Places.Desktop));
    public IReadOnlyList<string> Homes => [.. Settings.Drawers.Select(d => d.Home)];
    public IReadOnlyList<DrawerState> Drawers => [.. _states.Values];

    /// <summary>A drawer to draw: a saved one while drawers are on, or one a plan under review would create.</summary>
    public DrawerState? Get(string id) =>
        _preview.TryGetValue(id, out var p) ? p : Settings.Enabled && _states.TryGetValue(id, out var s) ? s : null;

    /// <summary>Drawers were added, removed, renamed, folded or hidden, or one's folder came or went: the Desk lays out again.</summary>
    public event Action? Changed;

    // ---- busy ----

    public bool Busy => _holds.Count > 0;

    /// <summary>The last hold was released: a redraw that waited can run.</summary>
    public event Action? BusyEnded;

    /// <summary>Holds the Desk's redraws off while a drawer is mid-interaction (focus, a drag, a menu, an inline rename). Dispose to release;
    /// releasing twice is harmless.</summary>
    public IDisposable Hold()
    {
        var token = new object();
        _holds.Add(token);
        return new Release(() =>
        {
            if (_holds.Remove(token) && _holds.Count == 0) BusyEnded?.Invoke();
        });
    }

    private sealed class Release(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() { _release?.Invoke(); _release = null; }
    }

    // ---- state ----

    /// <summary>Reads the settings again and brings every drawer's state, listing and watcher up to date.</summary>
    public void Reload()
    {
        Settings = _store.LoadDrawers();
        var ids = Settings.Drawers.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var gone in _states.Keys.Where(k => !ids.Contains(k)).ToArray())
        {
            Unwatch(gone);
            _states.Remove(gone);
        }
        foreach (var def in Settings.Drawers)
        {
            if (!_states.TryGetValue(def.Id, out var state)) _states[def.Id] = state = new DrawerState(def);
            bool moved = !string.Equals(state.Def.Home, def.Home, StringComparison.OrdinalIgnoreCase);
            state.Def = def;
            state.Folded = Settings.Folded.Contains(def.Id);
            if (moved) state.Items.Clear();
            Refresh(state);
            if (Settings.Enabled) Watch(state); else Unwatch(def.Id);
        }
    }

    /// <summary>Saves drawers' settings and follows them.</summary>
    public void Save(DrawerSettings next)
    {
        _store.SaveDrawers(next);
        Reload();
        Changed?.Invoke();
    }

    private void Refresh(DrawerState state)
    {
        bool wasMissing = state.Missing;
        var listed = new List<DrawerItem>();
        if (Directory.Exists(state.Def.Home))
        {
            state.Missing = false;
            try
            {
                foreach (var info in new DirectoryInfo(state.Def.Home).EnumerateFileSystemInfos())
                {
                    if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    if (Housekeeping.Contains(info.Name, StringComparer.OrdinalIgnoreCase)) continue;   // streamer: key - compared with fixed names
                    listed.Add(new DrawerItem(info.FullName, info is DirectoryInfo, pending: false));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* listed as far as it could be */ }
        }
        else state.Missing = !_preview.ContainsKey(state.Def.Id);
        foreach (var path in _pending[state.Def.Id]) listed.Add(new DrawerItem(path, Directory.Exists(path), pending: true));
        Sync(state, [.. listed.OrderBy(i => i.Pending).ThenBy(i => !i.IsFolder).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)]);
        if (wasMissing != state.Missing) Changed?.Invoke();
    }

    /// <summary>Brings the list to <paramref name="next"/> without rebuilding it, so a selection survives a file arriving.</summary>
    private void Sync(DrawerState state, List<DrawerItem> next)
    {
        var items = state.Items;
        var keep = next.Select(n => (n.Path, n.Pending)).ToHashSet();
        for (int i = items.Count - 1; i >= 0; i--)
            if (!keep.Contains((items[i].Path, items[i].Pending))) items.RemoveAt(i);
        for (int i = 0; i < next.Count; i++)
        {
            int at = -1;
            for (int j = i; j < items.Count; j++)
                if (items[j].Path == next[i].Path && items[j].Pending == next[i].Pending) { at = j; break; }
            if (at == i) continue;
            if (at > i) items.Move(at, i);
            else
            {
                items.Insert(i, next[i]);
                _icons.Request(next[i]);
            }
        }
        state.CountChanged();
    }

    private void Watch(DrawerState state)
    {
        Unwatch(state.Def.Id);
        if (!Directory.Exists(state.Def.Home)) return;
        try
        {
            var id = state.Def.Id;
            var w = new FileSystemWatcher(state.Def.Home)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
            };
            w.Created += (_, _) => MarkDirty(id);
            w.Deleted += (_, _) => MarkDirty(id);
            w.Renamed += (_, _) => MarkDirty(id);
            w.Changed += (_, _) => MarkDirty(id);
            w.Error += (_, _) => MarkDirty(id);   // the folder itself was deleted or renamed, or the buffer overflowed
            w.EnableRaisingEvents = true;
            _watchers[id] = w;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Gone between the check and the watcher: shown missing; Reload tries again.
            state.Missing = true;
        }
    }

    private void Unwatch(string id)
    {
        if (_watchers.Remove(id, out var w)) w.Dispose();
    }

    private void MarkDirty(string id)
    {
        if (_disposed) return;
        _ui.BeginInvoke(() =>
        {
            if (_disposed) return;
            _dirty.Add(id);
            _debounce.Stop();
            _debounce.Start();
        });
    }

    private void Flush()
    {
        _debounce.Stop();
        foreach (var id in _dirty.ToArray())
            if (_states.TryGetValue(id, out var s))
            {
                Refresh(s);
                if (s.Missing) Unwatch(id);
            }
        _dirty.Clear();
    }

    // ---- the user's own moves ----

    /// <summary>A drop the drawer takes: every item on the Desktop or in another drawer, and at least one not already in this one.</summary>
    public bool Accepts(IReadOnlyList<string> paths, string id)
    {
        if (Get(id) is not { Missing: false } state || paths.Count == 0) return false;
        var home = DrawerPaths.Normalize(state.Def.Home);
        var desktop = DrawerPaths.Normalize(Places.Desktop);
        var homes = Homes.Select(DrawerPaths.Normalize).OfType<string>().ToArray();
        bool any = false;
        foreach (var raw in paths)
        {
            if (DrawerPaths.Normalize(raw) is not { } p) return false;
            bool inside = (desktop is not null && DrawerPaths.IsChildOf(p, desktop)) || homes.Any(h => DrawerPaths.IsChildOf(p, h));
            if (!inside) return false;
            if (home is null || !DrawerPaths.IsChildOf(p, home)) any = true;
        }
        return any;
    }

    /// <summary>The user's drop (spec section 1): no review, one journal entry. Items already in the drawer are left alone; a Public
    /// Desktop item is named and skipped.</summary>
    public DrawerRun MoveIn(IReadOnlyList<string> paths, string id)
    {
        if (Get(id) is not { Missing: false }) return DrawerRun.Empty;
        var plan = new DrawerPlan(DrawerPlanSource.User, null, [], [.. paths.Select(p => new DrawerMove(p, id))]);
        var check = DrawerPlanValidator.Check(plan, Roots, Settings.Drawers);
        if (!check.Ok) return new DrawerRun([], [.. paths.Select(p => new DrawerSkipped(Path.GetFileName(p), DrawerSkip.Outside))]);   // streamer: key - a file's leaf for the result line
        var run = Mover.Apply(check, Settings.Drawers);
        var shared = check.Shared.Select(p => new DrawerSkipped(Path.GetFileName(p), DrawerSkip.Shared));   // streamer: key - a file's leaf for the result line
        if (run.Moved.Count > 0) Journal.Add(DrawerPlanSource.User, null, run.Moved, DateTime.UtcNow);
        return run with { Skipped = [.. shared, .. run.Skipped] };
    }

    /// <summary>Undoes one journal entry and drops it from the journal.</summary>
    public DrawerRun Undo(string entryId)
    {
        if (Journal.Load().Entries.FirstOrDefault(e => e.Id == entryId) is not { } entry) return DrawerRun.Empty;
        var run = Mover.Undo(DrawerUndo.Plan(entry, Written, DrawerMover.Exists), Homes);
        Journal.Remove(entryId);
        return run;
    }

    private static DateTime? Written(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path) : Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : null;

    // ---- fold, hide, names ----

    public void SetFolded(string id, bool folded)
    {
        var set = Settings.Folded.Where(f => f != id).ToList();
        if (folded) set.Add(id);
        Save(Settings with { Folded = set });
    }

    /// <summary>Hidden: gone from the Desk, files where they are (spec section 2). The layout word is removed; Show drawers puts it back.</summary>
    public void Hide(string id)
    {
        var desk = _store.LoadDesk();
        var layout = new DeskArrangement(desk.Layout);
        layout.Hide(DrawerPieces.Word(id));
        _store.SaveDesk(desk with { Layout = layout.Layout });
        Changed?.Invoke();
    }

    /// <summary>"Show drawers": every drawer not on the Desk comes back, top left.</summary>
    public void ShowAll()
    {
        var desk = _store.LoadDesk();
        var layout = DrawerArrival.Place(desk.Layout, Settings.Drawers);
        if (layout == desk.Layout) return;
        _store.SaveDesk(desk with { Layout = layout });
        Changed?.Invoke();
    }

    /// <summary>Renames a drawer; an owned drawer's folder takes the name too (its id, and so its layout word, stays). Null on success,
    /// otherwise the line to show.</summary>
    public string? RenameDrawer(string id, string name)
    {
        name = name.Trim();
        if (Settings.Drawers.FirstOrDefault(d => d.Id == id) is not { } def) return "That drawer is gone.";
        if (!DrawerNames.IsValid(name)) return "That name can't be a folder name.";
        var next = def with { Name = name };
        if (def.Owned)
        {
            var home = Path.Combine(Path.GetDirectoryName(def.Home)!, name);   // streamer: key - the renamed folder's path, used for IO
            if (!string.Equals(home, def.Home, StringComparison.OrdinalIgnoreCase) && DrawerMover.Exists(home))
                return "A folder of that name is already in the Drawers folder.";
            if (!ShellFiles.Rename(def.Home, name).Ok) return "The drawer's folder could not be renamed.";
            next = next with { Home = home };
        }
        Save(Settings with { Drawers = [.. Settings.Drawers.Select(d => d.Id == id ? next : d)] });
        return null;
    }

    /// <summary>Renames a file in a drawer through the Shell (spec section 2). Null on success, otherwise the line to show.</summary>
    public string? RenameItem(string path, string newName)
    {
        newName = newName.Trim();
        if (newName.Length is 0 or > 255 || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || newName.EndsWith('.') || newName.EndsWith(' '))
            return "That name can't be a file name.";
        var target = Path.Combine(Path.GetDirectoryName(path)!, newName);   // streamer: key - the renamed file's path, used for IO
        if (!string.Equals(target, path, StringComparison.OrdinalIgnoreCase) && DrawerMover.Exists(target))
            return "A file of that name is already in this drawer.";
        return ShellFiles.Rename(path, newName).Ok ? null : "The file could not be renamed.";
    }

    /// <summary>Adds a drawer that shows an existing folder in place, as a view (spec section 4): its contents never move when drawers are
    /// turned off. Null on success.</summary>
    public string? AddView(string name, string folder)
    {
        name = name.Trim();
        if (!DrawerNames.IsValid(name)) return "That name can't be a folder name.";
        if (!Directory.Exists(folder)) return "That folder is gone.";
        if (Settings.Drawers.Any(d => string.Equals(DrawerPaths.Normalize(d.Home), DrawerPaths.Normalize(folder), StringComparison.OrdinalIgnoreCase)))
            return "A drawer already shows that folder.";
        var id = DrawerIds.Unique(name, [.. Settings.Drawers.Select(d => d.Id)]);
        Save(Settings with { Drawers = [.. Settings.Drawers, new DrawerDef(id, name, folder, Owned: false)] });
        ShowAll();
        return null;
    }

    // ---- the review's preview ----

    /// <summary>Shows a checked plan's moves as faded items in their drawers, with the drawers it would create.</summary>
    public void SetPending(DrawerPlanCheck check)
    {
        ClearPending(refresh: false);
        foreach (var d in check.Drawers) _preview[d.Id] = new DrawerState(d);
        _pending = check.Moves.ToLookup(m => m.DrawerId, m => m.Source, StringComparer.Ordinal);
        foreach (var s in _states.Values.Concat(_preview.Values)) Refresh(s);
    }

    public void ClearPending() => ClearPending(refresh: true);

    private void ClearPending(bool refresh)
    {
        _preview.Clear();
        _pending = Array.Empty<string>().ToLookup(s => s);
        if (refresh) foreach (var s in _states.Values) Refresh(s);
    }

    // ---- the Desktop ----

    /// <summary>The user's Desktop as the rules see it (the Public Desktop is never listed for them: its items need an administrator).</summary>
    public IReadOnlyList<DesktopItem> DesktopListing()
    {
        var list = new List<DesktopItem>();
        try
        {
            foreach (var info in new DirectoryInfo(Places.Desktop).EnumerateFileSystemInfos())
                list.Add(new DesktopItem(info.FullName, info is DirectoryInfo, info.LastWriteTimeUtc,
                    (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* an unreadable Desktop sorts nothing */ }
        return list;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounce.Stop();
        foreach (var w in _watchers.Values) w.Dispose();
        _watchers.Clear();
        _icons.Dispose();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DrawerServiceTests" --disable-build-servers -nodeReuse:false`
Expected: PASS. If the two watcher tests are slow under load, raise `Pump` to 3000 ms; never weaken the assertion.

- [ ] **Step 5: Streamer reach, then commit**

Run `StreamerReachTests`, add the new files' rows to `Known`, run again: PASS.

```bash
git add windows-dotnet/src/Sanduhr.App/Desk/Drawers/ windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs windows-dotnet/tests/Sanduhr.App.Tests/DrawerServiceTests.cs windows-dotnet/tests/Sanduhr.Tests/StreamerReachTests.cs
git commit -m "feat(drawers): the drawer service keeps live state across redraws, with watchers, icons, hand moves and busy holds

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: The drawer piece on the Desk

`DeskView` draws a placed `drawer.<id>` word as a `DrawerPiece`, themed from the Desk look in force (spec section 2's table), in the
same anchor stacks as every other piece. An island holding a drawer takes clicks and may take focus (Task 8 uses that). The
controller holds a full redraw while the drawer service is busy and runs it when the last hold is released. Arrange mode lists placed
drawers by name.

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerPiece.xaml`, `DrawerPiece.xaml.cs`, `DrawerLook.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerState.cs` (`Selected`)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/IDeskHost.cs` (`DeskIsland.TakesFocus`)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskView.cs` (`DeskFrame.Drawers`, `DeskFrame.AccentHex`, the piece switch, drawn words, `DrawnFor`, `PieceName`)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskLookResolver.cs` (`AccentHex` from a followed theme)
- Modify: `windows-dotnet/src/Sanduhr.Core/ThemeDeskLook.cs` (make its hex helper `public static string Hex(string color)` if it is private)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskClicks.cs` (`Prepare`: a drawer island takes clicks and focus)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskController.cs` (constructor `drawers`, the frame, the busy hold)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskArrangeMode.cs` (`DeskView.DrawnFor`, `DeskView.PieceName`)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (make the service, hand it to the controller)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DrawerPieceTests.cs` (new)

**Interfaces:**
- Consumes: Task 6's `DrawerService`, `DrawerState`, `DrawerItem`; `DeskView.Rgb(string)`, `DeskView.Shadow(ResolvedInk)`; `ResolvedInk`; `DeskPlate.Opacity`; `StreamerMode`.
- Produces:
  - `record DrawerLook(Color Plate, Color Ink, Color Accent, Effect? Shadow, FontFamily Font)` with `const MinPlateOpacity = 0.55` and `static From(ResolvedInk ink, string? plateHex, double? plateOpacity, string? accentHex, FontFamily font)`.
  - `DrawerPiece(DrawerState state, DrawerService service, DrawerLook look, double scale)` (UserControl, IDisposable): `State`, consts `FrameWidth`, `ListHeight`; named parts `Frame`, `TitleBar`, `TitleText`, `List`, `MissingText`, `Status`.
  - `DrawerState.Selected : IReadOnlyList<string>` (paths kept across a rebuild).
  - `DeskIsland.TakesFocus : bool` (init).
  - `DeskFrame.Drawers : DrawerService?`, `DeskFrame.AccentHex : string?` (init).
  - `DeskView.DrawnFor(DeskArrangement) : IReadOnlyList<string>`, `DeskView.PieceName(string widget) : string`.
  - `DeskController(..., DrawerService? drawers = null)` (new last optional parameter).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>The drawer piece, themed and named, in the Desk's stacks; redraws wait while a drawer is busy (drawers plan 2, Task 7).</summary>
public sealed class DrawerPieceTests
{
    private static (DrawerService Service, SettingsStore Store, Paths Paths) Make(DrawerSandbox box, params string[] files)
    {
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"), localAppDataBase: Path.Combine(box.Root, "local"));
        var store = new SettingsStore(paths);
        var home = box.Dir(@"Desktop\Drawers\Images");
        foreach (var f in files) box.File(Path.Combine(@"Desktop\Drawers\Images", f));
        store.SaveDrawers(DrawerSettings.Default with { Enabled = true, Drawers = [new DrawerDef("images", "Images", home, true)] });
        store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl drawer.images:tl:1", Font = "" });
        return (new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, []), Dispatcher.CurrentDispatcher), store, paths);
    }

    private static DrawerLook Look() => DrawerLook.From(DeskInkMap.Light.Default, null, null, null, new FontFamily("Segoe UI"));

    private static DrawerPiece Piece(DrawerService s)
    {
        var piece = new DrawerPiece(s.Get("images")!, s, Look(), 1);
        piece.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        piece.Arrange(new Rect(piece.DesiredSize));
        piece.UpdateLayout();
        return piece;
    }

    private static DeskScreen Screen()
    {
        var bounds = new PxRect(0, 0, 1920, 1080);
        var work = new PxRect(0, 0, 1920, 1032);
        return new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0));
    }

    [Fact]
    public void The_drawer_is_a_named_group_and_each_item_is_named_by_its_file() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = Make(box, "a.png", "b.png");
        var piece = Piece(s);
        Assert.Equal("Images drawer, 2 items", AutomationProperties.GetName(piece));
        Assert.Equal(System.Windows.Automation.Peers.AutomationControlType.Group,
            System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(piece).GetAutomationControlType());
        var item = (ListBoxItem)piece.List.ItemContainerGenerator.ContainerFromIndex(0);
        Assert.Equal("a.png", AutomationProperties.GetName(item));
        Assert.Equal("drawer.images", piece.Tag);
        s.Dispose();
    });

    [Fact]
    public void Folded_shows_the_title_with_its_count_and_no_list() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = Make(box, "a.png");
        s.SetFolded("images", true);
        var piece = Piece(s);
        Assert.Equal("Images · 1", piece.TitleText.Text);
        Assert.Equal(Visibility.Collapsed, piece.List.Visibility);
        s.Dispose();
    });

    [Fact]
    public void A_missing_folder_shows_its_line() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = Make(box);
        Directory.Delete(Path.Combine(box.Drawers, "Images"));
        s.Reload();
        var piece = Piece(s);
        Assert.Equal(Visibility.Visible, piece.MissingText.Visibility);
        Assert.Equal(DrawerState.MissingLine, piece.MissingText.Text);
        Assert.Equal(Visibility.Collapsed, piece.List.Visibility);
        s.Dispose();
    });

    [Fact]
    public void Pending_items_are_drawn_faded() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = Make(box, "a.png");
        var b = box.File(@"Desktop\b.png");
        var plan = new DrawerPlan(DrawerPlanSource.Rules, null, [], [new DrawerMove(b, "images")]);
        s.SetPending(DrawerPlanValidator.Check(plan, box.Roots, s.Settings.Drawers));
        var piece = Piece(s);
        var pending = (ListBoxItem)piece.List.ItemContainerGenerator.ContainerFromIndex(1);
        Assert.Equal(0.45, pending.Opacity, 3);
        Assert.Equal(1.0, ((ListBoxItem)piece.List.ItemContainerGenerator.ContainerFromIndex(0)).Opacity, 3);
        s.Dispose();
    });

    [Fact]
    public void The_frame_takes_the_plate_at_a_readable_strength_and_the_title_the_ink() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = Make(box);
        var piece = Piece(s);
        var plate = ((SolidColorBrush)piece.Frame.Background).Color;
        Assert.Equal((byte)Math.Round(DrawerLook.MinPlateOpacity * 255), plate.A);
        Assert.Equal((0, 0, 0), (plate.R, plate.G, plate.B));   // light ink: a dark plate
        Assert.Equal(Look().Ink, ((SolidColorBrush)piece.TitleText.Foreground).Color);
        var themed = DrawerLook.From(DeskInkMap.Light.Default, "123456", 0.8, "ff8800", new FontFamily("Segoe UI"));
        Assert.Equal(Color.FromArgb(204, 0x12, 0x34, 0x56), themed.Plate);
        Assert.Equal(Color.FromRgb(0xff, 0x88, 0x00), themed.Accent);
        s.Dispose();
    });

    [Fact]
    public void Streamer_mode_codenames_the_drawer_and_blurs_the_labels() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = Make(box, "salary-2026.xlsx");
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), box.Root));
        try
        {
            var piece = Piece(s);
            Assert.DoesNotContain("Images", piece.TitleText.Text);
            Assert.DoesNotContain("Images", AutomationProperties.GetName(piece));
            Assert.IsType<BlurEffect>(piece.Resources["Drawer.LabelEffect"]);
            var item = (ListBoxItem)piece.List.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.DoesNotContain("salary", AutomationProperties.GetName(item));
        }
        finally { StreamerMode.Reset(); s.Dispose(); }
    });

    [Fact]
    public void The_Desk_draws_a_placed_drawer_and_its_island_takes_clicks_and_focus() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store, _) = Make(box, "a.png");
        var view = new DeskView(Screen(), store.LoadDesk(), new DeskFonts(["Segoe UI"]));
        view.Update(new DeskFrame { Drawers = s }, DeskInkMap.Light);
        var islands = DeskClicks.Prepare(view.TakeIslands(), false, new NoClicks());
        var island = islands.Single(i => i.Widgets.Contains("drawer.images"));
        Assert.True(island.TakesClicks);
        Assert.True(island.TakesFocus);
        Assert.False(islands.Single(i => i.Widgets.Contains("clock")).TakesFocus);
        view.Dispose();
        s.Dispose();
    });

    [Fact]
    public void A_drawer_word_with_no_drawer_draws_nothing() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store, _) = Make(box);
        store.SaveDesk(store.LoadDesk() with { Layout = "clock:bl drawer.ghost:tl:1" });
        var view = new DeskView(Screen(), store.LoadDesk(), new DeskFonts(["Segoe UI"]));
        view.Update(new DeskFrame { Drawers = s }, DeskInkMap.Light);
        Assert.DoesNotContain(view.TakeIslands(), i => i.Widgets.Contains("drawer.ghost"));
        view.Dispose();
        s.Dispose();
    });

    [Fact]
    public void A_redraw_waits_while_a_drawer_is_busy_and_runs_when_it_is_released() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store, paths) = Make(box, "a.png");
        var host = new CountingHost();
        var desk = new DeskController(store, () => host, () => DeskUsage.Empty, new NowPlayingHub(), new DeskMessageSource(store, paths),
            paths: paths, drawers: s);
        desk.Reload();
        int before = host.Shows;
        var hold = s.Hold();
        desk.Reload();
        Assert.Equal(before, host.Shows);
        hold.Dispose();
        Assert.Equal(before + 1, host.Shows);
        desk.Dispose();
        s.Dispose();
    });

    [Fact]
    public void Arrange_mode_names_a_drawer_by_its_name() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, _) = Make(box);
        DrawerService.Current = s;
        try
        {
            Assert.Equal("Images drawer", DeskView.PieceName("drawer.images"));
            Assert.Equal("Drawer", DeskView.PieceName("drawer.ghost"));
            Assert.Equal(SettingsNames.DeskPieceName("clock"), DeskView.PieceName("clock"));
            Assert.Contains("drawer.images", DeskView.DrawnFor(new DeskArrangement("clock:bl drawer.images:tl:1")));
        }
        finally { DrawerService.Current = null; s.Dispose(); }
    });

    private sealed class NoClicks : IDeskClickActions
    {
        public void PlayPause() { }
        public void OpenWeatherCredit() { }
        public void Menu(DeskMenuAction action) { }
        public void LaunchButton(int index) { }
    }

    private sealed class CountingHost : IDeskHost
    {
        public int Shows { get; private set; }
        public event Action? ScreensChanged { add { } remove { } }
        public event Action? CoverageChanged { add { } remove { } }
        public bool IsShowing { get; private set; }
        public IReadOnlyList<bool> Coverage => [];

        public IReadOnlyList<DeskScreen> Screens()
        {
            var bounds = new PxRect(0, 0, 1920, 1080);
            var work = new PxRect(0, 0, 1920, 1032);
            return [new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0))];
        }

        public void Show(IReadOnlyList<DeskSurface> surfaces) { Shows++; IsShowing = true; }
        public void Hide() => IsShowing = false;
        public void SetDrift(int? step) { }
        public void SetDim(double opacity, TimeSpan ease) { }
        public void Dispose() { }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DrawerPieceTests" --disable-build-servers -nodeReuse:false`
Expected: build FAIL on the missing `DrawerPiece`, `DrawerLook`, `DeskFrame.Drawers`, `DeskIsland.TakesFocus` and the controller's `drawers` parameter.

- [ ] **Step 3: Write `DrawerLook.cs` and the piece**

`DrawerLook.cs`:

```csharp
using System.Windows.Media;
using System.Windows.Media.Effects;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// What a drawer draws from (5.0 drawers spec, section 2): the frame is the plate (its color, or black behind light ink and white behind
/// dark ink, at no less than <see cref="MinPlateOpacity"/> so labels read over any wallpaper and the frame takes the mouse), the title
/// and labels are the ink, hover and selection the accent (a followed theme's, else the ink's last color), and the Desk font.
/// </summary>
internal sealed record DrawerLook(Color Plate, Color Ink, Color Accent, Effect? Shadow, FontFamily Font)
{
    public const double MinPlateOpacity = 0.55;

    public static DrawerLook From(ResolvedInk ink, string? plateHex, double? plateOpacity, string? accentHex, FontFamily font)
    {
        var rgb = plateHex is { } h ? DeskView.Rgb(h) : ink.DarkInk ? Colors.White : Colors.Black;
        double a = Math.Max(plateOpacity ?? DeskPlate.Opacity, MinPlateOpacity);
        var plate = Color.FromArgb((byte)Math.Round(a * 255), rgb.R, rgb.G, rgb.B);
        var accent = accentHex is { } x ? DeskView.Rgb(x) : DeskView.Rgb(ink.Stops[^1]);
        Effect? shadow = null;
        if (ink.ShadowOpacity > 0)
        {
            var s = DeskView.Shadow(ink);
            s.Freeze();
            shadow = s;
        }
        return new DrawerLook(plate, DeskView.Rgb(ink.Stops[0]), accent, shadow, font);
    }
}
```

`DrawerPiece.xaml`:

```xml
<UserControl x:Class="Sanduhr.App.Desk.DrawerPiece" x:ClassModifier="internal"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Focusable="False">
    <!-- A drawer on the Desk (5.0 drawers spec, section 2). Brushes are set per piece from DrawerLook; nothing here reads the widget theme. -->
    <UserControl.Resources>
        <Style x:Key="DrawerItem" TargetType="ListBoxItem">
            <Setter Property="Margin" Value="2" />
            <Setter Property="Padding" Value="0" />
            <Setter Property="FocusVisualStyle" Value="{x:Null}" />
            <Setter Property="AutomationProperties.Name" Value="{Binding SpokenName}" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ListBoxItem">
                        <Border x:Name="Chip" CornerRadius="6" BorderThickness="1" Background="Transparent" BorderBrush="Transparent">
                            <ContentPresenter />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Chip" Property="Background" Value="{DynamicResource Drawer.Hover}" />
                            </Trigger>
                            <Trigger Property="IsSelected" Value="True">
                                <Setter TargetName="Chip" Property="Background" Value="{DynamicResource Drawer.Selected}" />
                            </Trigger>
                            <Trigger Property="IsKeyboardFocused" Value="True">
                                <Setter TargetName="Chip" Property="BorderBrush" Value="{DynamicResource Drawer.Accent}" />
                            </Trigger>
                            <DataTrigger Binding="{Binding Pending}" Value="True">
                                <Setter Property="Opacity" Value="0.45" />
                            </DataTrigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <DataTemplate x:Key="DrawerTile">
            <StackPanel Width="72" Margin="2,6,2,4">
                <Image Source="{Binding Icon}" Width="40" Height="40" HorizontalAlignment="Center" />
                <TextBlock x:Name="Label" Text="{Binding Name}" FontSize="11.5" TextAlignment="Center" TextWrapping="Wrap"
                           TextTrimming="CharacterEllipsis" MaxHeight="32" Margin="0,4,0,0"
                           Foreground="{DynamicResource Drawer.Ink}" Effect="{DynamicResource Drawer.LabelEffect}" />
                <TextBox x:Name="Editor" Visibility="Collapsed" FontSize="11.5" Margin="0,4,0,0" TextWrapping="Wrap"
                         AutomationProperties.Name="New name"
                         IsVisibleChanged="Editor_IsVisibleChanged" KeyDown="Editor_KeyDown" LostKeyboardFocus="Editor_LostKeyboardFocus" />
            </StackPanel>
            <DataTemplate.Triggers>
                <DataTrigger Binding="{Binding Editing}" Value="True">
                    <Setter TargetName="Label" Property="Visibility" Value="Collapsed" />
                    <Setter TargetName="Editor" Property="Visibility" Value="Visible" />
                </DataTrigger>
            </DataTemplate.Triggers>
        </DataTemplate>
    </UserControl.Resources>

    <Border x:Name="Frame" CornerRadius="10" Padding="8" Background="{DynamicResource Drawer.Plate}" AllowDrop="True">
        <DockPanel>
            <Grid x:Name="TitleBar" DockPanel.Dock="Top" Height="26" Background="Transparent" Cursor="Hand">
                <TextBlock x:Name="TitleText" FontSize="13" FontWeight="SemiBold" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"
                           Margin="4,0" Foreground="{DynamicResource Drawer.Ink}" />
            </Grid>
            <TextBlock x:Name="Status" DockPanel.Dock="Bottom" FontSize="11" TextWrapping="Wrap" Margin="4,4,4,0" Visibility="Collapsed"
                       Foreground="{DynamicResource Drawer.Ink}" AutomationProperties.LiveSetting="Polite" />
            <TextBlock x:Name="MissingText" FontSize="12" TextWrapping="Wrap" Margin="4,8" Visibility="Collapsed"
                       Foreground="{DynamicResource Drawer.Ink}" />
            <ListBox x:Name="List" Background="Transparent" BorderThickness="0" SelectionMode="Extended"
                     ItemContainerStyle="{StaticResource DrawerItem}" ItemTemplate="{StaticResource DrawerTile}"
                     ScrollViewer.HorizontalScrollBarVisibility="Disabled" ScrollViewer.VerticalScrollBarVisibility="Hidden">
                <ListBox.ItemsPanel>
                    <ItemsPanelTemplate>
                        <WrapPanel />
                    </ItemsPanelTemplate>
                </ListBox.ItemsPanel>
            </ListBox>
        </DockPanel>
    </Border>
</UserControl>
```

`DrawerPiece.xaml.cs` (Task 8 adds the interaction to this class; leave the three `Editor_*` handlers empty for now):

```csharp
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Sanduhr.App.Services;

namespace Sanduhr.App.Desk;

/// <summary>
/// A drawer on the Desk (5.0 drawers spec, section 2): a themed frame with the drawer's name, and open, a grid of Shell icons and names
/// that scrolls inside it; folded, the title and a count; for a folder that is gone, one line. Its state lives in
/// <see cref="DrawerService"/>, so a redraw builds a new piece over the same <see cref="DrawerState"/>, selection included.
/// </summary>
internal sealed partial class DrawerPiece : UserControl, IDisposable
{
    public const double FrameWidth = 344;
    public const double ListHeight = 264;
    private readonly DrawerState _state;
    private readonly DrawerService _service;

    public DrawerPiece(DrawerState state, DrawerService service, DrawerLook look, double scale)
    {
        InitializeComponent();
        _state = state;
        _service = service;
        Tag = state.Word;
        FontFamily = look.Font;
        Resources["Drawer.Plate"] = Brush(look.Plate);
        Resources["Drawer.Ink"] = Brush(look.Ink);
        Resources["Drawer.Accent"] = Brush(look.Accent);
        Resources["Drawer.Hover"] = Brush(Color.FromArgb(46, look.Accent.R, look.Accent.G, look.Accent.B));
        Resources["Drawer.Selected"] = Brush(Color.FromArgb(90, look.Accent.R, look.Accent.G, look.Accent.B));
        Resources["Drawer.LabelEffect"] = StreamerMode.On ? Blur() : look.Shadow;   // streamer: masked - file labels blur while streamer mode is on
        Frame.Width = FrameWidth;
        List.Height = ListHeight;
        if (Math.Abs(scale - 1) > 0.001) LayoutTransform = new ScaleTransform(scale, scale);
        List.ItemsSource = state.Items;
        foreach (var path in state.Selected)
            if (state.Items.FirstOrDefault(i => i.Path == path) is { } item) List.SelectedItems.Add(item);
        List.SelectionChanged += (_, _) => _state.Selected = [.. List.SelectedItems.OfType<DrawerItem>().Select(i => i.Path)];
        _state.PropertyChanged += OnStateChanged;
        _state.Items.CollectionChanged += OnItemsChanged;
        Show();
        Wire();
    }

    public DrawerState State => _state;

    private void Show()
    {
        TitleText.Text = _state.Folded ? _state.FoldedTitle : _state.Title;
        AutomationProperties.SetName(TitleText, TitleText.Text);   // streamer: masked - Title and FoldedTitle carry the codename while on
        AutomationProperties.SetName(this, _state.SpokenName);   // streamer: masked - SpokenName carries the codename while on
        MissingText.Text = DrawerState.MissingLine;
        bool open = !_state.Folded;
        MissingText.Visibility = open && _state.Missing ? Visibility.Visible : Visibility.Collapsed;
        List.Visibility = open && !_state.Missing ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) => Show();

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Show();

    protected override AutomationPeer OnCreateAutomationPeer() => new GroupPeer(this);

    /// <summary>A drawer is a named group (spec section 2, the 10.1.4.4 rule): "Screenshots drawer, 14 items".</summary>
    private sealed class GroupPeer(DrawerPiece owner) : UserControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    }

    private static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static BlurEffect Blur()
    {
        var b = new BlurEffect { Radius = 6 };
        b.Freeze();
        return b;
    }

    /// <summary>The interaction (Task 8).</summary>
    private void Wire() { }

    private void Editor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) { }
    private void Editor_KeyDown(object sender, KeyEventArgs e) { }
    private void Editor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) { }

    public void Dispose()
    {
        _state.PropertyChanged -= OnStateChanged;
        _state.Items.CollectionChanged -= OnItemsChanged;
    }
}
```

In `DrawerState.cs`, add:

```csharp
    /// <summary>The selected items' paths, so a redraw that builds a new piece keeps the selection.</summary>
    public IReadOnlyList<string> Selected { get; set; } = [];
```

- [ ] **Step 4: Wire the piece into the Desk**

`IDeskHost.cs`, in `DeskIsland` after `TakesClicks`:

```csharp
    /// <summary>The island holds a drawer (5.0 drawers spec, section 2): a click into it may make its window active so the keyboard
    /// reaches the drawer (<see cref="DeskFocus"/>), and activation is given back on a click away. False never activates, as before.</summary>
    public bool TakesFocus { get; init; }
```

`DeskView.cs`. In `DeskFrame` add:

```csharp
    /// <summary>Drawers' live state (5.0 drawers spec); null draws no drawer.</summary>
    public DrawerService? Drawers { get; init; }

    /// <summary>The followed theme's accent (six hex digits), for a drawer's hover and selection; null is the ink's last color.</summary>
    public string? AccentHex { get; init; }
```

In `DeskView`, beside `Drawn`:

```csharp
    /// <summary>The pieces this build draws on <paramref name="arrangement"/>: the built-in ones and every drawer placed in it.</summary>
    internal static IReadOnlyList<string> DrawnFor(DeskArrangement arrangement) =>
        [.. Drawn, .. arrangement.Pieces.Select(p => p.Widget).Where(DrawerPieces.IsWord)];

    /// <summary>A piece's name in Arrange and on the Desk menu: a drawer's own name (its codename while streamer mode is on), else the
    /// built-in piece's name.</summary>
    internal static string PieceName(string widget) =>
        DrawerPieces.IdOf(widget) is { } id
            ? DrawerService.Current?.Get(id) is { } state ? state.Title + " drawer" : "Drawer"   // streamer: masked - Title is the codename while on
            : SettingsNames.DeskPieceName(widget);
```

Where the stacks are built (`var stacks = arrangement.Stacks(roams ? Drawn.Where(...).ToList() : Drawn);`), use
`var drawn = DrawnFor(arrangement);` and `arrangement.Stacks(roams ? drawn.Where(w => w != CompanionSettings.Widget).ToList() : drawn)`.

In `Piece`, add a guarded arm before `_ => null`:

```csharp
            var w when DrawerPieces.IdOf(w) is { } id => DrawerPieceFor(id, p.Scale),
```

change the shadow line's condition to also skip drawers (`&& !DrawerPieces.IsWord(p.Widget)`), and add:

```csharp
    /// <summary>A drawer piece (5.0 drawers spec, section 2), or null when there is no such drawer to draw. Its subscriptions end with
    /// the view.</summary>
    private FrameworkElement? DrawerPieceFor(string id, double scale)
    {
        if (_frame.Drawers is not { } service || service.Get(id) is not { } state) return null;
        var piece = new DrawerPiece(state, service, DrawerLook.From(_ink, _frame.PlateHex, _frame.PlateOpacity, _frame.AccentHex, _family), scale);
        _live.Add(piece);
        return piece;
    }
```

`DeskLookResolver.Frame`: where the followed look is applied (`draw` is not null and `theme` is given), set
`AccentHex = theme is null ? null : ThemeDeskLook.Hex(theme.Accent)` on the frame it returns. If `ThemeDeskLook`'s hex helper is
private, make it `public static string Hex(string color)` (it is pure; the MCP links that file, which stays IO-free).

`DeskClicks.Prepare`:

```csharp
        foreach (var island in islands)
        {
            bool drawer = HasDrawer(island.Content);
            var found = Find(island.Content);
            var kinds = Kinds(found, island.Widgets, piecesTakeClicks);
            if (kinds.Count == 0)
            {
                result.Add(drawer ? island with { TakesClicks = true, TakesFocus = true } : island);
                continue;
            }
            var targets = found.Where(f => kinds.Contains(f.Kind)).ToList();
            foreach (var (_, element) in targets) SetPlate(element);
            Wire(island.Content, targets, actions);
            result.Add(island with { TakesClicks = true, TakesFocus = drawer });
        }
```

with

```csharp
    /// <summary>The island holds a drawer, which takes its own clicks (Task 8) and may take focus.</summary>
    private static bool HasDrawer(DependencyObject node)
    {
        if (node is DrawerPiece) return true;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (HasDrawer(VisualTreeHelper.GetChild(node, i))) return true;
        return node is ContentControl { Content: DependencyObject c } && HasDrawer(c)
            || node is Decorator { Child: { } d } && HasDrawer(d)
            || node is Panel p && p.Children.OfType<DependencyObject>().Any(HasDrawer);
    }
```

(The island's content is laid out but not yet in a window, so the walk also follows logical children.)

`DeskController.cs`. Add `DrawerService? drawers = null` as the constructor's last parameter, store it in `_drawers`, and in the
constructor:

```csharp
        _drawers = drawers;
        if (_drawers is not null)
        {
            _drawers.Changed += () => { if (!_stopped) Reload(); };
            _drawers.BusyEnded += () => { if (_renderWanted) Render(); };
        }
```

with a field `private bool _renderWanted;`. At the top of `Render()`, after the existing guard line:

```csharp
        // A drawer is mid-interaction (a drag, Explorer's menu, an inline rename): rebuilding it now would cut that short. The redraw
        // runs when the last hold is released (Review Focus 3).
        if (_drawers?.Busy == true) { _renderWanted = true; return; }
        _renderWanted = false;
```

In both `DeskFrame` builds (`RenderOnce` and the arrange frame near line 538), add `Drawers = _drawers,`.

`DeskArrangeMode.cs`: replace `DeskView.Drawn` with `DeskView.DrawnFor(_working)` (lines 181 and 184), and each
`SettingsNames.DeskPieceName(widget)` with `DeskView.PieceName(widget)` (lines 242, 301, 339). Those three lines' markers become
`// streamer: masked - DeskView.PieceName gives a drawer's codename while streamer mode is on`; update their rows in `Known` from
`notname` to `masked`.

`App.xaml.cs`, before `_desk = new Desk.DeskController(...)`:

```csharp
        _drawers = new Desk.DrawerService(store, paths, Desk.DrawerPlaces.ForThisUser());
        Desk.DrawerService.Current = _drawers;
```

pass `drawers: _drawers` as the controller's last argument, add the field `private Desk.DrawerService? _drawers;`, and dispose it
(then set `Current` to null) where `_widgetReview` is disposed.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DrawerPieceTests|FullyQualifiedName~CompanionPieceTests|FullyQualifiedName~DeskFollowThemeTests|FullyQualifiedName~DeskArrange" --disable-build-servers -nodeReuse:false`
Expected: PASS, the existing Desk suites included.

- [ ] **Step 6: Streamer reach and the full suites**

Run `StreamerReachTests`, add the new rows and move the three `DeskArrangeMode.cs` rows; run again: PASS. Then the full App and Core
suites: PASS.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Desk windows-dotnet/src/Sanduhr.App/App.xaml.cs windows-dotnet/src/Sanduhr.Core/ThemeDeskLook.cs windows-dotnet/tests/Sanduhr.App.Tests/DrawerPieceTests.cs windows-dotnet/tests/Sanduhr.Tests/StreamerReachTests.cs
git commit -m "feat(drawers): the drawer piece on the Desk, themed and named; redraws wait while a drawer is busy

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Drawer interaction on the real host, and the live gate

The spike's four Win32 rules (VERDICT.md) on the shipped host: a click into a drawer makes its window active without raising it; a
click away gives it back; Explorer's menu works and is dark; and the drag-and-drop rules, including never acting on a drag-out's
result. Then a gate only a person can run: the five spike checks again, on the App, before Task 9 builds review on top.

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/Desk/DeskFocus.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Interop/DeskNative.cs` (`WM_ACTIVATE`, `WA_INACTIVE`)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DesktopLayerHost.cs` (`DeskHook`: menu forwarding, give focus back)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerPiece.xaml.cs` (`Wire` and the handlers)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerService.cs` (`SettingsRequested`, `RequestSettings`)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (`DarkMenus.Enable()` at start; `SettingsRequested` opens Settings)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DrawerInteractionTests.cs` (new)

**Interfaces:**
- Consumes: Task 4's `ShellMenu`, `ShellFiles.Open`, `DarkMenus`; Task 6's `DrawerService.Accepts`, `MoveIn`, `Hold`, `SetFolded`, `Hide`, `RenameDrawer`, `RenameItem`; Task 7's `DrawerPiece`; `TextPromptWindow(string title, string prompt, string initial)` with `Value`; `ThemedDialog.Show`.
- Produces:
  - `DeskFocus.Take(UIElement target) : bool`; `DeskFocus.GaveBack(IntPtr hwnd)`; `event Action<IntPtr> DeskFocus.Lost`.
  - `DrawerPieceCopy` constants: `RenameDrawer = "Rename drawer"`, `Fold = "Fold"`, `Open = "Open"`, `Hide = "Hide"`, `Settings = "Drawers settings…"` (Task 11 replaces the literal with `SettingsNames.LinkTitle(SettingsPage.Drawers)`).
  - `DrawerService.SettingsRequested` (event) and `RequestSettings()`.
  - `DrawerPiece` testable seams: `internal static string[]? FilesOf(IDataObject data)`, `internal bool Accepts(IDataObject data)`, `internal void BeginRename(DrawerItem item)`, `internal void CommitRename(DrawerItem item, string text)`, `internal void Say(string line)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>The drawer's drop rules and inline rename, without a live Desk window (drawers plan 2, Task 8). The window side is the live
/// gate in Step 6.</summary>
public sealed class DrawerInteractionTests
{
    private static (DrawerService Service, DrawerPiece Piece) Make(DrawerSandbox box)
    {
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"));
        var store = new SettingsStore(paths);
        var home = box.Dir(@"Desktop\Drawers\Images");
        store.SaveDrawers(DrawerSettings.Default with { Enabled = true, Drawers = [new DrawerDef("images", "Images", home, true)] });
        var s = new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, []), Dispatcher.CurrentDispatcher);
        var look = DrawerLook.From(DeskInkMap.Light.Default, null, null, null, new FontFamily("Segoe UI"));
        return (s, new DrawerPiece(s.Get("images")!, s, look, 1));
    }

    private static DataObject Files(params string[] paths) => new(DataFormats.FileDrop, paths);

    [Fact]
    public void A_drop_from_the_Desktop_is_accepted_and_one_from_its_own_drawer_is_not() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, piece) = Make(box);
        var own = box.File(@"Desktop\Drawers\Images\a.png");
        Assert.True(piece.Accepts(Files(box.File(@"Desktop\b.png"))));
        Assert.False(piece.Accepts(Files(own)));
        Assert.False(piece.Accepts(new DataObject(DataFormats.Text, "not files")));
        s.Dispose();
    });

    [Fact]
    public void Inline_rename_commits_through_the_Shell_and_holds_redraws_until_it_ends() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, piece) = Make(box);
        box.File(@"Desktop\Drawers\Images\a.png");
        s.Reload();
        var item = s.Get("images")!.Items.Single();
        piece.BeginRename(item);
        Assert.True(item.Editing);
        Assert.True(s.Busy);
        piece.CommitRename(item, "b.png");
        Assert.False(item.Editing);
        Assert.False(s.Busy);
        Assert.True(File.Exists(Path.Combine(box.Drawers, "Images", "b.png")));
        s.Dispose();
    });

    [Fact]
    public void A_rename_to_a_taken_name_says_why_and_changes_nothing() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, piece) = Make(box);
        box.File(@"Desktop\Drawers\Images\a.png");
        box.File(@"Desktop\Drawers\Images\b.png");
        s.Reload();
        var a = s.Get("images")!.Items.First(i => i.Name == "a.png");
        piece.BeginRename(a);
        piece.CommitRename(a, "b.png");
        Assert.Equal("A file of that name is already in this drawer.", piece.Status.Text);
        Assert.Equal(Visibility.Visible, piece.Status.Visibility);
        Assert.True(File.Exists(Path.Combine(box.Drawers, "Images", "a.png")));
        s.Dispose();
    });

    [Fact]
    public void The_drawer_menu_items_use_their_copy()
    {
        Assert.Equal("Rename drawer", DrawerPieceCopy.RenameDrawer);
        Assert.EndsWith("settings…", DrawerPieceCopy.Settings);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DrawerInteractionTests" --disable-build-servers -nodeReuse:false`
Expected: build FAIL on the missing `DrawerPiece.Accepts`, `BeginRename`, `CommitRename` and `DrawerPieceCopy`.

- [ ] **Step 3: Focus, the host and the service**

`DeskNative.cs`: add `public const int WM_ACTIVATE = 0x0006;` and `public const int WA_INACTIVE = 0;`.

`DeskFocus.cs`:

```csharp
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Sanduhr.App.Desk;

/// <summary>
/// Keyboard focus for a drawer (5.0 drawers spec, section 2; VERDICT.md change 1). Desk windows never activate
/// (<c>WS_EX_NOACTIVATE</c>), so the keyboard could never reach a drawer. A click into one clears the style for the visit and makes the
/// window the foreground one; the host refuses the raise that activation asks for (<c>KeepZOrder</c>, the spike's z-order pin), so the
/// drawer stays under the apps as desktop icons do. A click away gives the style back (<see cref="GaveBack"/>, from the host's hook).
/// </summary>
internal static class DeskFocus
{
    /// <summary>A Desk window gave activation back (the window's handle).</summary>
    public static event Action<IntPtr>? Lost;

    public static bool Take(UIElement target)
    {
        if (PresentationSource.FromVisual(target) is not HwndSource src) return false;
        var h = src.Handle;
        if (DeskNative.GetForegroundWindow() != h)
        {
            int style = DeskNative.GetWindowLong(h, DeskNative.GWL_EXSTYLE);
            DeskNative.SetWindowLongPtr(h, DeskNative.GWL_EXSTYLE, (IntPtr)(style & ~DeskNative.WS_EX_NOACTIVATE));
            DeskNative.SetForegroundWindow(h);
        }
        Keyboard.Focus(target);
        return DeskNative.GetForegroundWindow() == h;
    }

    /// <summary>The window lost activation (or is going away): it never activates again until the next click into a drawer.</summary>
    public static void GaveBack(IntPtr h)
    {
        int style = DeskNative.GetWindowLong(h, DeskNative.GWL_EXSTYLE);
        DeskNative.SetWindowLongPtr(h, DeskNative.GWL_EXSTYLE, (IntPtr)(style | DeskNative.WS_EX_NOACTIVATE));
        Lost?.Invoke(h);
    }
}
```

`DesktopLayerHost.DeskHook`: before the `switch`, forward Explorer's menu messages:

```csharp
        // Open with and Send to fill in lazily: the open Shell menu needs these messages (VERDICT.md change 3).
        if (ShellMenu.Forward(msg, wParam, lParam, out var menuResult))
        {
            handled = true;
            return menuResult;
        }
```

and add to the `switch`:

```csharp
            case DeskNative.WM_ACTIVATE when (wParam.ToInt64() & 0xFFFF) == DeskNative.WA_INACTIVE:
                DeskFocus.GaveBack(hwnd);
                break;
```

and in the existing `WM_DESTROY` case, call `DeskFocus.GaveBack(hwnd);` first. Leave `WM_MOUSEACTIVATE` returning `MA_NOACTIVATE`:
`DeskFocus.Take` activates explicitly, which is what the spike proved.

`DrawerService.cs`:

```csharp
    /// <summary>A drawer's "Drawers settings…" was chosen: the app opens Settings at the Drawers page.</summary>
    public event Action? SettingsRequested;

    public void RequestSettings() => SettingsRequested?.Invoke();
```

`App.xaml.cs`: call `Interop.DarkMenus.Enable();` once at start, before the Desk starts; subscribe
`_drawers.SettingsRequested += () => _ = ShowSettingsAsync(SettingsPage.Desk, null);` (Task 11 changes it to the Drawers page).

- [ ] **Step 4: The piece's interaction**

In `DrawerPiece.xaml.cs`, add `using System.IO; using System.Runtime.InteropServices; using System.Windows.Interop; using
System.Windows.Threading; using Sanduhr.App.Interop; using Sanduhr.App.Views; using Sanduhr.Core;`, the copy class, and replace
`Wire` and the three `Editor_*` handlers:

```csharp
/// <summary>The drawer's own menu items under Explorer's (spec section 2).</summary>
internal static class DrawerPieceCopy
{
    public const string RenameDrawer = "Rename drawer";
    public const string Fold = "Fold";
    public const string Open = "Open";
    public const string Hide = "Hide";
    public const string Settings = "Drawers settings…";
}
```

```csharp
    private const int RenameId = ShellMenu.OwnFirst, FoldId = ShellMenu.OwnFirst + 1, HideId = ShellMenu.OwnFirst + 2, SettingsId = ShellMenu.OwnFirst + 3;
    private Point _dragStart;
    private IDisposable? _renameHold;
    private DispatcherTimer? _statusTimer;

    private void Wire()
    {
        PreviewMouseDown += (_, _) => DeskFocus.Take(List);
        TitleBar.MouseLeftButtonUp += (_, e) => { _service.SetFolded(_state.Def.Id, !_state.Folded); e.Handled = true; };
        Frame.DragEnter += OnDragOver;
        Frame.DragOver += OnDragOver;
        Frame.Drop += OnDrop;
        List.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(this);
        List.PreviewMouseMove += OnMaybeDrag;
        List.MouseDoubleClick += (_, e) => { if (ItemAt(e.OriginalSource) is { Pending: false } item) ShellFiles.Open(item.Path); };
        List.KeyDown += OnKey;
        MouseRightButtonUp += OnRightClick;
    }

    // ---- drops ----

    internal static string[]? FilesOf(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) ? data.GetData(DataFormats.FileDrop) as string[] : null;

    /// <summary>A drop this drawer takes: files from the Desktop or another drawer, not all already here (Review Focus 4).</summary>
    internal bool Accepts(IDataObject data) => FilesOf(data) is { Length: > 0 } files && _service.Accepts(files, _state.Def.Id);

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Accepts(e.Data) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!Accepts(e.Data) || FilesOf(e.Data) is not { } files) { e.Effects = DragDropEffects.None; return; }
        var run = _service.MoveIn(files, _state.Def.Id);
        if (run.Skipped.Count > 0) Say(run);
        // An optimized move (Shell, "Handling Optimized Move Operations"): this drawer moved the files itself, so the source is told
        // the logical effect was a move and the performed effect nothing, and never deletes anything of its own.
        e.Effects = DragDropEffects.None;
        try
        {
            e.Data.SetData("Logical Performed DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Move)));
            e.Data.SetData("Performed DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.None)));
        }
        catch (Exception x) when (x is COMException or NotSupportedException or InvalidOperationException) { /* a source that takes no answer */ }
    }

    // ---- drags out ----

    private void OnMaybeDrag(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var d = e.GetPosition(this) - _dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var paths = List.SelectedItems.OfType<DrawerItem>().Where(i => !i.Pending && !i.Editing).Select(i => i.Path).ToArray();
        if (paths.Length == 0) return;
        var data = new DataObject(DataFormats.FileDrop, paths);
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Move)));
        using (_service.Hold())
            // The result is never acted on (VERDICT.md): the target moved the files, or the drop landed back here and did nothing.
            DragDrop.DoDragDrop(List, data, DragDropEffects.Move | DragDropEffects.Copy);
    }

    // ---- keys ----

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (List.SelectedItem is not DrawerItem { Pending: false, Editing: false } item) return;
        switch (e.Key)
        {
            case Key.Enter: ShellFiles.Open(item.Path); e.Handled = true; break;
            case Key.F2: BeginRename(item); e.Handled = true; break;
            case Key.Delete: ShellMenu.InvokeVerb(Handle, item.Path, "delete"); e.Handled = true; break;   // Explorer's own delete: the Recycle Bin, its confirmation
        }
    }

    // ---- the menu ----

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var item = ItemAt(e.OriginalSource);
        if (item is { Pending: false } && !List.SelectedItems.Contains(item))
        {
            List.SelectedItems.Clear();
            List.SelectedItem = item;
        }
        var at = PointToScreen(e.GetPosition(this));
        ShellMenuItem[] extras =
        [
            new(RenameId, DrawerPieceCopy.RenameDrawer),
            new(FoldId, _state.Folded ? DrawerPieceCopy.Open : DrawerPieceCopy.Fold),
            new(HideId, DrawerPieceCopy.Hide),
            new(SettingsId, DrawerPieceCopy.Settings),
        ];
        ShellMenuChoice? choice;
        using (_service.Hold())
            choice = ShellMenu.Show(Handle, item is { Pending: false } ? item.Path : null, (int)at.X, (int)at.Y, extras, verb => verb == "rename");
        if (choice is { Verb: "rename" } && item is not null) BeginRename(item);
        switch (choice?.Own)
        {
            case RenameId: RenameDrawer(); break;
            case FoldId: _service.SetFolded(_state.Def.Id, !_state.Folded); break;
            case HideId: _service.Hide(_state.Def.Id); break;
            case SettingsId: _service.RequestSettings(); break;
        }
    }

    private void RenameDrawer()
    {
        var prompt = new TextPromptWindow(DrawerPieceCopy.RenameDrawer, "Name", _state.Def.Name);   // streamer: masked - the user's own drawer name, in a prompt they opened
        if (prompt.ShowDialog() != true) return;
        if (_service.RenameDrawer(_state.Def.Id, prompt.Value) is { } error) Say(error);
    }

    // ---- inline rename ----

    internal void BeginRename(DrawerItem item)
    {
        if (item.Pending || item.Editing) return;
        _renameHold ??= _service.Hold();
        item.Editing = true;
    }

    internal void CommitRename(DrawerItem item, string text)
    {
        if (!item.Editing) return;
        item.Editing = false;
        if (text.Trim() != item.Name && _service.RenameItem(item.Path, text) is { } error) Say(error);
        EndRename();
    }

    private void CancelRename(DrawerItem item)
    {
        item.Editing = false;
        EndRename();
    }

    private void EndRename()
    {
        _renameHold?.Dispose();
        _renameHold = null;
    }

    private void Editor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox box || e.NewValue is not true || box.DataContext is not DrawerItem item) return;
        box.Text = item.Name;
        // Explorer selects the name without its extension.
        int stem = item.IsFolder ? item.Name.Length : Path.GetFileNameWithoutExtension(item.Name).Length;   // streamer: key - a length, never drawn
        box.Dispatcher.BeginInvoke(() => { box.Focus(); box.Select(0, stem); }, DispatcherPriority.Input);
    }

    private void Editor_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: DrawerItem item } box) return;
        if (e.Key == Key.Enter) { CommitRename(item, box.Text); List.Focus(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelRename(item); List.Focus(); e.Handled = true; }
    }

    private void Editor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: DrawerItem item } box) CommitRename(item, box.Text);
    }

    // ---- helpers ----

    private IntPtr Handle => PresentationSource.FromVisual(this) is HwndSource src ? src.Handle : IntPtr.Zero;

    private static DrawerItem? ItemAt(object source) =>
        (source as DependencyObject) is { } d && ItemsControl.ContainerFromElement(null, d) is ListBoxItem { DataContext: DrawerItem item } ? item
        : (source as FrameworkElement)?.DataContext as DrawerItem;

    /// <summary>A drop's result: the named line, or only the counts while streamer mode is on (the line names files).</summary>
    private void Say(DrawerRun run) =>
        Say(StreamerMode.On ? $"{run.Moved.Count} moved, {run.Skipped.Count} skipped." : run.Line);   // streamer: masked - counts only while on

    /// <summary>One line at the drawer's foot for six seconds: what a drop skipped, or why a rename did not happen.</summary>
    internal void Say(string line)
    {
        Status.Text = line;
        AutomationProperties.SetName(Status, line);   // streamer: masked - names a file only while streamer mode is off (Say(DrawerRun))
        Status.Visibility = Visibility.Visible;
        _statusTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _statusTimer.Stop();
        _statusTimer.Tick += Hide;
        _statusTimer.Start();

        void Hide(object? s, EventArgs a)
        {
            _statusTimer!.Tick -= Hide;
            _statusTimer.Stop();
            Status.Visibility = Visibility.Collapsed;
        }
    }
```

Add `EndRename();` and `_statusTimer?.Stop();` to `Dispose`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DrawerInteractionTests|FullyQualifiedName~DrawerPieceTests|FullyQualifiedName~DesktopLayerHost" --disable-build-servers -nodeReuse:false`
Expected: PASS. Then `StreamerReachTests` (add rows), and the full App suite: PASS.

- [ ] **Step 6: The live gate (a person runs this; do not skip it)**

Build a debug App (`dotnet build src/Sanduhr.App`), quit the installed Sanduhr, and run the debug build. In a scratch copy of the
settings (back up `%APPDATA%\Sanduhr\settings.json` first), turn the Desk on and give `settings.json` a `drawers` object with
`"enabled": true` and one owned drawer whose home is a new, empty folder `Desktop\Drawers\Spike`; put `drawer.spike:tl:1` in
`desk_layout`. Then check, writing each result in `.superpowers/sdd/2026-10-30-drawers-desk/progress.md`:

1. Drag a junk file from Explorer onto the drawer: it moves in, shows in the grid, and Explorer shows no error or copy.
2. Drag it back out to an Explorer window: Explorer moves it; the drawer empties; nothing is left behind or duplicated.
3. Right-click it in the drawer: Explorer's menu shows, dark when Windows is; Open with and Send to fill in; Properties opens; the
   Sanduhr items show below a separator and work (Fold, Hide, Rename drawer, Drawers settings…).
4. Click the drawer, then use the arrow keys, F2 (rename inline, Enter commits), Enter (opens) and Delete (Explorer's delete
   confirmation). With an app window over part of the drawer, the drawer stays behind it while it has the keyboard. Click away: the
   next key goes to the app clicked.
5. Restart Explorer (Task Manager, Windows Explorer, Restart): the Desk comes back and a drop onto the drawer still works.
6. Drop the file back onto the drawer it came from: nothing happens.

A failure stops the plan here, as the spike's did: write the failing step and what the log says, and escalate before Task 9.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App windows-dotnet/tests/Sanduhr.App.Tests/DrawerInteractionTests.cs windows-dotnet/tests/Sanduhr.Tests/StreamerReachTests.cs
git commit -m "feat(drawers): focus on click, drops, drags, Explorer's menu, keys and inline rename on the Desk host

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Review with Edit, Apply's result with Undo, one review at a time

The rules' plan previews on the Desk (new drawers placed, waiting files faded) under the existing review window, now with an Edit
button. Apply moves file by file and turns the bar into the result with Undo and Done. Only one review is open at a time across Desk
designs, widgets and drawers (spec section 1, the `DeskBusy` rule).

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerReviewService.cs` (service, `IDrawerReviewHost`, `DrawerReviewHost`)
- Create: `windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerPlanEditWindow.xaml`, `DrawerPlanEditWindow.xaml.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskReviewBar.xaml` (an Edit button between Dismiss and Apply), `DeskReviewBar.xaml.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskReviewWindow.xaml.cs` (`EditRequested`)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/WidgetReviewService.cs` (`WidgetReviewHost.DeskBusy` counts a drawers review)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskDesignService.cs` (`OnProposalCore` answers busy during a drawers review)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (make the review service and its host)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DrawerReviewServiceTests.cs`, `DrawerPlanEditWindowTests.cs` (new)

**Interfaces:**
- Consumes: Task 3's `DrawerReview`, `DrawerPlanEdit`, `DrawerSync`, `DrawerArrival`, `DeskReviewModel.CanEdit/ApplyLabel/DismissLabel/ApplyOff`; Task 6's `DrawerService`; `DrawerRules.Plan`, `DrawerRules.DefaultDrawers`, `DrawerPlanValidator.Check`; `DeskController.ShowPreview`, `EndPreview`, `ReviewScreen`; `DeskReviewWindow`.
- Produces (namespace `Sanduhr.App.Desk`):
  - `record DrawerPlanEdits(IReadOnlySet<string> Unticked, IReadOnlyDictionary<string, string> Renames)`
  - `interface IDrawerReviewHost { bool OtherReviewOpen { get; } void Preview(string layout); void EndPreview(); void Bar(DeskReviewModel model, Action apply, Action? edit, Action dismiss); void CloseBar(); DrawerPlanEdits? Edit(DrawerPlan plan, IReadOnlyCollection<string> existingIds); }`
  - `DrawerReviewService(DrawerService drawers, SettingsStore store, IDrawerReviewHost host)`: `static Current`; `Reviewing`; `SortNow() : string?`; `Open(DrawerPlan plan) : string?`; `Dismiss()`. (Plan 3's `propose_drawers` calls `Open`.)
  - `DrawerReviewHost(DeskController desk, SettingsStore store)` : `IDrawerReviewHost`, `IDisposable`.
  - `DeskReviewBar.EditRequested`, `DeskReviewBar.Edit : Button`; `DeskReviewWindow.EditRequested`.
  - `DrawerPlanEditWindow(DrawerPlan plan, IReadOnlyCollection<string> existingIds)`: `Untick(string path)`, `Rename(string id, string name)`, `TryResult(out string? error) : DrawerPlanEdits?`, `Result : DrawerPlanEdits?`.

- [ ] **Step 1: Write the failing tests**

`DrawerReviewServiceTests.cs`:

```csharp
using System.IO;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>The drawers review from the rules' plan to Apply, Edit, Undo and Dismiss, with a fake bar (drawers plan 2, Task 9).</summary>
public sealed class DrawerReviewServiceTests
{
    private sealed class FakeHost : IDrawerReviewHost
    {
        public bool OtherReviewOpen { get; set; }
        public string? Previewing { get; private set; }
        public DeskReviewModel? Model { get; private set; }
        public Action? Apply, Edit_, Dismiss;
        public DrawerPlanEdits? NextEdit { get; set; }

        public void Preview(string layout) => Previewing = layout;
        public void EndPreview() => Previewing = null;
        public void Bar(DeskReviewModel model, Action apply, Action? edit, Action dismiss) { Model = model; Apply = apply; Edit_ = edit; Dismiss = dismiss; }
        public void CloseBar() => Model = null;
        public DrawerPlanEdits? Edit(DrawerPlan plan, IReadOnlyCollection<string> existingIds) => NextEdit;
    }

    private static (DrawerReviewService Review, DrawerService Drawers, SettingsStore Store, FakeHost Host) Make(DrawerSandbox box)
    {
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"));
        var store = new SettingsStore(paths);
        store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl" });
        store.SaveDrawers(DrawerSettings.Default with { Enabled = true });
        var drawers = new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, []), Dispatcher.CurrentDispatcher);
        var host = new FakeHost();
        return (new DrawerReviewService(drawers, store, host), drawers, store, host);
    }

    [Fact]
    public void Sort_now_previews_the_rules_plan_with_new_drawers_placed_and_files_faded() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, _, host) = Make(box);
        var shot = box.File(@"Desktop\Screenshot 2026-10-30 101500.png");
        box.File(@"Desktop\notes.pdf");
        Assert.Null(review.SortNow());
        Assert.True(review.Reviewing);
        Assert.Equal("2 files into 2 drawers", host.Model!.Title);
        Assert.Equal(DrawerReview.FirstOnLine, host.Model.Note);
        Assert.NotNull(host.Edit_);
        Assert.Contains("drawer.screenshots", host.Previewing);
        Assert.True(drawers.Get("screenshots")!.Items.Single().Pending);
        Assert.True(File.Exists(shot));   // nothing moves until Apply
        drawers.Dispose();
    });

    [Fact]
    public void Apply_moves_saves_the_drawers_and_shows_the_result_with_Undo() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, store, host) = Make(box);
        var pdf = box.File(@"Desktop\notes.pdf");
        review.SortNow();
        host.Apply!();
        Assert.False(review.Reviewing);
        Assert.Null(host.Previewing);
        Assert.False(File.Exists(pdf));
        Assert.True(File.Exists(Path.Combine(box.Drawers, "Documents", "notes.pdf")));
        Assert.Equal("1 moved.", host.Model!.Title);
        Assert.Equal(DrawerReview.Undo, host.Model.ApplyLabel);
        var s = store.LoadDrawers();
        Assert.Equal(["documents"], s.Drawers.Select(d => d.Id));
        Assert.True(s.AutoTidyUnlocked);
        Assert.Equal(DeskAnchor.TL, new DeskArrangement(store.LoadDesk().Layout).Placement("drawer.documents")!.Anchor);
        Assert.Equal(DrawerPlanSource.Rules, Assert.Single(drawers.Journal.Load().Entries).Source);
        drawers.Dispose();
    });

    [Fact]
    public void Undo_from_the_result_puts_the_files_back() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, _, host) = Make(box);
        var pdf = box.File(@"Desktop\notes.pdf");
        review.SortNow();
        host.Apply!();
        host.Apply!();   // the result's Undo
        Assert.True(File.Exists(pdf));
        Assert.Empty(drawers.Journal.Load().Entries);
        Assert.False(host.Model!.CanApply);   // nothing left to undo
        drawers.Dispose();
    });

    [Fact]
    public void Dismiss_moves_nothing_and_puts_the_Desk_back() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, store, host) = Make(box);
        var pdf = box.File(@"Desktop\notes.pdf");
        review.SortNow();
        host.Dismiss!();
        Assert.False(review.Reviewing);
        Assert.Null(host.Model);
        Assert.Null(host.Previewing);
        Assert.True(File.Exists(pdf));
        Assert.Empty(store.LoadDrawers().Drawers);
        Assert.Null(drawers.Get("documents"));
        drawers.Dispose();
    });

    [Fact]
    public void Edit_unticks_a_file_and_renames_a_new_drawer_before_Apply() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, _, host) = Make(box);
        var keep = box.File(@"Desktop\keep.pdf");
        box.File(@"Desktop\notes.pdf");
        review.SortNow();
        host.NextEdit = new DrawerPlanEdits(new HashSet<string> { keep }, new Dictionary<string, string> { ["documents"] = "Papers" });
        host.Edit_!();
        Assert.Equal("1 file into 1 drawer", host.Model!.Title);
        host.Apply!();
        Assert.True(File.Exists(keep));
        Assert.True(File.Exists(Path.Combine(box.Drawers, "Papers", "notes.pdf")));
        drawers.Dispose();
    });

    [Fact]
    public void A_name_rule_for_a_drawer_not_made_yet_makes_it() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, store, host) = Make(box);
        store.SaveDrawers(store.LoadDrawers() with { Rules = [new DrawerRule(DrawerRuleKind.NameContains, "receipts", Text: "invoice")] });
        drawers.Reload();
        box.File(@"Desktop\invoice-0042.txt");
        Assert.Null(review.SortNow());
        Assert.Contains("Receipts (new): 1 file", host.Model!.Changes);
        drawers.Dispose();
    });

    [Fact]
    public void The_review_names_both_folders_when_only_the_Desktop_syncs() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"));
        var store = new SettingsStore(paths);
        var elsewhere = box.Dir("Elsewhere");
        store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl" });
        store.SaveDrawers(DrawerSettings.Default with { Enabled = true, Root = elsewhere });
        // The sandbox Desktop stands in for a OneDrive-synced one; the moved Drawers folder is outside it.
        var drawers = new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, [box.Desktop]), Dispatcher.CurrentDispatcher);
        var host = new FakeHost();
        box.File(@"Desktop
otes.pdf");
        Assert.Null(new DrawerReviewService(drawers, store, host).SortNow());
        Assert.Contains(host.Model!.Warnings, w => w == DrawerSync.Warning(box.Desktop, elsewhere, [box.Desktop]));
        drawers.Dispose();
    });

    [Fact]
    public void A_Desktop_with_nothing_to_sort_opens_no_review() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, _, host) = Make(box);
        box.File(@"Desktop\project.code-workspace");
        Assert.Equal(DrawerReview.NothingToSort, review.SortNow());
        Assert.Null(host.Model);
        drawers.Dispose();
    });

    [Fact]
    public void Only_one_review_is_open_at_a_time() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (review, drawers, _, host) = Make(box);
        box.File(@"Desktop\notes.pdf");
        host.OtherReviewOpen = true;
        Assert.Equal(DrawerReview.Busy, review.SortNow());
        host.OtherReviewOpen = false;
        Assert.Null(review.SortNow());
        Assert.Equal(DrawerReview.Busy, review.SortNow());
        drawers.Dispose();
    });
}
```

`DrawerPlanEditWindowTests.cs`:

```csharp
using Sanduhr.App.Desk;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

public sealed class DrawerPlanEditWindowTests
{
    private static readonly DrawerPlan Plan = new(DrawerPlanSource.Rules, null,
        [new("images", "Images", @"C:\D\Drawers\Images", true), new("documents", "Documents", @"C:\D\Drawers\Documents", true)],
        [new(@"C:\D\a.png", "images"), new(@"C:\D\b.pdf", "documents")]);

    [Fact]
    public void Edits_come_back_as_unticked_paths_and_renames() => Sta.Run(() =>
    {
        var w = new DrawerPlanEditWindow(Plan, ["documents"]);
        w.Untick(@"C:\D\b.pdf");
        w.Rename("images", "Photos");
        var r = w.TryResult(out var error);
        Assert.Null(error);
        Assert.Equal([@"C:\D\b.pdf"], r!.Unticked);
        Assert.Equal("Photos", r.Renames["images"]);
        Assert.False(w.CanRename("documents"));   // an existing drawer is renamed from its own menu, not here
    });

    [Fact]
    public void A_bad_name_is_refused_with_a_line() => Sta.Run(() =>
    {
        var w = new DrawerPlanEditWindow(Plan, []);
        w.Rename("images", "CON");
        Assert.Null(w.TryResult(out var error));
        Assert.Equal("\"CON\" can't be a folder name.", error);
    });
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DrawerReviewServiceTests|FullyQualifiedName~DrawerPlanEditWindowTests" --disable-build-servers -nodeReuse:false`
Expected: build FAIL on the missing `DrawerReviewService`, `IDrawerReviewHost`, `DrawerPlanEdits` and `DrawerPlanEditWindow`.

- [ ] **Step 3: Implement the service and its host**

`DrawerReviewService.cs`:

```csharp
using System.IO;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>What Edit hands back: the files left out and new drawers' new names, by id.</summary>
internal sealed record DrawerPlanEdits(IReadOnlySet<string> Unticked, IReadOnlyDictionary<string, string> Renames);

/// <summary>The Desk and bar side of a drawers review; the App's is <see cref="DrawerReviewHost"/>, tests use a fake.</summary>
internal interface IDrawerReviewHost
{
    /// <summary>A Desk design or a widget is under review (spec section 1: one review at a time).</summary>
    bool OtherReviewOpen { get; }

    /// <summary>The Desk draws <paramref name="layout"/> (the plan's new drawers placed) until <see cref="EndPreview"/>.</summary>
    void Preview(string layout);
    void EndPreview();

    /// <summary>Shows or updates the bar. A null <paramref name="edit"/> hides Edit.</summary>
    void Bar(DeskReviewModel model, Action apply, Action? edit, Action dismiss);
    void CloseBar();

    /// <summary>Edit: the user's changes, or null when they cancelled.</summary>
    DrawerPlanEdits? Edit(DrawerPlan plan, IReadOnlyCollection<string> existingIds);
}

/// <summary>
/// The drawers review (5.0 drawers spec, section 1): a plan previews on the Desk, the bar reads "14 files into 3 drawers" with Apply,
/// Edit and Dismiss, nothing moves before Apply, and Apply's result keeps the bar up with Undo. Plans come from the rules here and from
/// agents in plan 3 (<see cref="Open"/>).
/// </summary>
internal sealed class DrawerReviewService
{
    public static DrawerReviewService? Current { get; set; }

    private readonly DrawerService _drawers;
    private readonly SettingsStore _store;
    private readonly IDrawerReviewHost _host;
    private DrawerPlan? _plan;
    private DrawerPlanCheck? _check;
    private string? _undoId;

    public DrawerReviewService(DrawerService drawers, SettingsStore store, IDrawerReviewHost host)
    {
        _drawers = drawers;
        _store = store;
        _host = host;
    }

    /// <summary>A plan is on the Desk waiting for Apply, Edit or Dismiss.</summary>
    public bool Reviewing => _plan is not null;

    /// <summary>The built-in rules over the Desktop, opened for review. Null when the bar opened; otherwise the line to show instead.</summary>
    public string? SortNow()
    {
        var s = _drawers.Settings;
        var root = s.RootOr(_drawers.Places.Desktop);
        var known = s.Drawers.Concat(DrawerRules.DefaultDrawers(root).Where(d => s.Drawers.All(x => x.Id != d.Id))).ToList();
        // A rule the user added for a drawer that does not exist yet ("invoice" to Receipts) gets that drawer when it first matches.
        foreach (var id in s.Rules.Select(r => r.DrawerId).Distinct().Where(id => known.All(d => d.Id != id)).ToArray())
            known.Add(new DrawerDef(id, NameFromId(id), Path.Combine(root, NameFromId(id)), Owned: true));
        return Open(DrawerRules.Plan(_drawers.DesktopListing(), s.Rules, known, root, DateTime.UtcNow));
    }

    /// <summary>The name a drawer a rule names gets before it exists: its id in words ("older-files" is "Older files").</summary>
    internal static string NameFromId(string id) =>
        DrawerRules.DefaultDrawers("").FirstOrDefault(d => d.Id == id)?.Name
        ?? (id.Replace('-', ' ') is var words && words.Length > 0 ? char.ToUpperInvariant(words[0]) + words[1..] : id);

    /// <summary>Checks a plan and opens it for review. Null when the bar opened; otherwise the line to show instead.</summary>
    public string? Open(DrawerPlan plan)
    {
        if (Reviewing || _host.OtherReviewOpen) return DrawerReview.Busy;
        var check = DrawerPlanValidator.Check(plan, _drawers.Roots, _drawers.Settings.Drawers);
        if (!check.Ok) return check.Error;
        if (check.Moves.Count == 0) return DrawerReview.NothingToSort;
        Show(plan, check);
        return null;
    }

    private void Show(DrawerPlan plan, DrawerPlanCheck check)
    {
        _plan = plan;
        _check = check;
        _undoId = null;
        _drawers.SetPending(check);
        _host.Preview(DrawerArrival.Place(_store.LoadDesk().Layout, check.Drawers));
        _host.Bar(Model(plan, check), Apply, Edit, Dismiss);
    }

    private DeskReviewModel Model(DrawerPlan plan, DrawerPlanCheck check)
    {
        var s = _drawers.Settings;
        var places = _drawers.Places;
        string? note = s.Drawers.Count == 0 && string.IsNullOrWhiteSpace(s.Root) ? DrawerReview.FirstOnLine : null;
        var sync = DrawerSync.Warning(places.Desktop, s.RootOr(places.Desktop), places.Synced);
        return DrawerReview.Model(plan, check, [.. s.Drawers.Select(d => d.Id)], note, sync is null ? [] : [sync]);
    }

    private void Apply()
    {
        if (_plan is not { } plan || _check is not { } check) return;
        var run = _drawers.Mover.Apply(check, _drawers.Settings.Drawers);
        _drawers.ClearPending();
        var s = _drawers.Settings;
        _drawers.Save(s with
        {
            Drawers = [.. s.Drawers, .. check.Drawers],
            Folded = [.. s.Folded, .. DrawerArrival.Folded(check.Drawers)],
            AutoTidyUnlocked = s.AutoTidyUnlocked || plan.Source == DrawerPlanSource.Rules,
        });
        var desk = _store.LoadDesk();
        _store.SaveDesk(desk with { Layout = DrawerArrival.Place(desk.Layout, check.Drawers) });
        _undoId = run.Moved.Count > 0 ? _drawers.Journal.Add(plan.Source, plan.Agent, run.Moved, DateTime.UtcNow).Id : null;
        _plan = null;
        _check = null;
        _host.EndPreview();
        _host.Bar(DrawerReview.Result(run.Line, _undoId is not null), Undo, null, Done);
    }

    private void Undo()
    {
        if (_undoId is not { } id) return;
        _undoId = null;
        var run = _drawers.Undo(id);
        _host.Bar(DrawerReview.Result(run.Line, canUndo: false), Undo, null, Done);
    }

    private void Done()
    {
        _undoId = null;
        _host.CloseBar();
    }

    private void Edit()
    {
        if (_plan is not { } plan) return;
        var existing = _drawers.Settings.Drawers.Select(d => d.Id).ToArray();
        if (_host.Edit(plan, existing) is not { } edits) return;
        var next = DrawerPlanEdit.Apply(plan, edits.Unticked, edits.Renames, existing, _drawers.Settings.RootOr(_drawers.Places.Desktop));
        var check = DrawerPlanValidator.Check(next, _drawers.Roots, _drawers.Settings.Drawers);
        if (!check.Ok || check.Moves.Count == 0) { Dismiss(); return; }
        Show(next, check);
    }

    /// <summary>Dismiss (the button, Escape, or the bar closed): nothing moves and the Desk is drawn as it was.</summary>
    public void Dismiss()
    {
        if (_plan is null) { Done(); return; }
        _plan = null;
        _check = null;
        _drawers.ClearPending();
        _host.EndPreview();
        _host.CloseBar();
    }
}

/// <summary>The running app's side of a drawers review: the Desk previews through <see cref="DeskController.ShowPreview"/> and the bar
/// is the shared <see cref="DeskReviewWindow"/>.</summary>
internal sealed class DrawerReviewHost : IDrawerReviewHost, IDisposable
{
    private readonly DeskController _desk;
    private readonly SettingsStore _store;
    private DeskReviewWindow? _window;
    private Action _apply = () => { }, _dismiss = () => { };
    private Action? _edit;
    private DeskDesignSettings? _preview;

    public DrawerReviewHost(DeskController desk, SettingsStore store)
    {
        _desk = desk;
        _store = store;
    }

    public bool OtherReviewOpen => DeskDesignService.Current?.Reviewing == true || WidgetReviewService.Current?.Reviewing == true;

    public void Preview(string layout)
    {
        _preview = new DeskDesignSettings(_store.LoadDesk() with { Layout = layout }, _store.LoadDeskInk(), _store.LoadLauncher());
        _desk.ShowPreview(_preview);
    }

    public void EndPreview()
    {
        _preview = null;
        _desk.EndPreview();
    }

    public void Bar(DeskReviewModel model, Action apply, Action? edit, Action dismiss)
    {
        (_apply, _edit, _dismiss) = (apply, edit, dismiss);
        if (_window is null)
        {
            _window = new DeskReviewWindow();
            _window.ApplyRequested += () => _apply();
            _window.EditRequested += () => _edit?.Invoke();
            _window.DismissRequested += () => _dismiss();
            _window.Closed += (s, _) =>
            {
                if (!ReferenceEquals(_window, s)) return;
                _window = null;
                _dismiss();
            };
            _window.SetModel(model);
            if (_preview is { } p) _window.Target = _desk.ReviewScreen(p);
            _window.Show();
            _window.Activate();
        }
        else _window.SetModel(model);
    }

    public void CloseBar()
    {
        var w = _window;
        _window = null;
        try { w?.Close(); }
        catch (InvalidOperationException) { /* already closing */ }
    }

    public DrawerPlanEdits? Edit(DrawerPlan plan, IReadOnlyCollection<string> existingIds)
    {
        var w = new DrawerPlanEditWindow(plan, existingIds) { Owner = _window };
        return w.ShowDialog() == true ? w.Result : null;
    }

    public void Dispose() => CloseBar();
}
```

- [ ] **Step 4: The Edit window, the bar's Edit button and the shared busy rule**

`DrawerPlanEditWindow.xaml`:

```xml
<Window x:Class="Sanduhr.App.Desk.DrawerPlanEditWindow" x:ClassModifier="internal"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Edit drawers" Width="440" SizeToContent="Height" MaxHeight="640" ResizeMode="NoResize" WindowStartupLocation="CenterOwner"
        ShowInTaskbar="False" Background="{DynamicResource Sanduhr.Brush.Glass}" AutomationProperties.Name="Edit drawers">
    <DockPanel Margin="20">
        <TextBlock DockPanel.Dock="Top" Text="Untick files to leave on the Desktop. A new drawer can take another name." TextWrapping="Wrap"
                   FontSize="12.5" Margin="0,0,0,12" Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
        <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,12,0,0">
            <TextBlock x:Name="Error" Foreground="{DynamicResource Sanduhr.Brush.Text}" VerticalAlignment="Center" Margin="0,0,12,0"
                       AutomationProperties.LiveSetting="Assertive" />
            <Button x:Name="CancelButton" Content="Cancel" IsCancel="True" MinWidth="80" Margin="0,0,8,0" />
            <Button x:Name="OkButton" Content="Done" IsDefault="True" MinWidth="80" />
        </StackPanel>
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <StackPanel x:Name="Groups" />
        </ScrollViewer>
    </DockPanel>
</Window>
```

`DrawerPlanEditWindow.xaml.cs`:

```csharp
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>Edit for a drawers review (spec section 1): every move as a tick box under its drawer, and a name box for each drawer the plan
/// creates. Existing drawers are renamed from their own menu, not here.</summary>
internal sealed partial class DrawerPlanEditWindow : Window
{
    private readonly Dictionary<string, CheckBox> _moves = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBox> _names = new(StringComparer.Ordinal);

    public DrawerPlanEditWindow(DrawerPlan plan, IReadOnlyCollection<string> existingIds)
    {
        InitializeComponent();
        foreach (var d in plan.Drawers)
        {
            var group = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            if (existingIds.Contains(d.Id))
                group.Children.Add(Heading(StreamerMode.Mask.Name(CodenameCategory.Folder, d.Name)));   // streamer: masked - Mask.Name gives the codename while on
            else
            {
                var box = new TextBox { Text = d.Name, FontSize = 13, Margin = new Thickness(0, 0, 0, 6) };   // streamer: masked - the user's own new drawer's name, editable here
                AutomationProperties.SetName(box, "New drawer's name");
                _names[d.Id] = box;
                group.Children.Add(box);
            }
            foreach (var m in plan.Moves.Where(m => m.DrawerId == d.Id))
            {
                var name = StreamerMode.Mask.Omit(Path.GetFileName(m.Source)) ?? "File";   // streamer: masked - Omit hides the file name while on
                var tick = new CheckBox { Content = name, IsChecked = true, Margin = new Thickness(4, 2, 0, 2) };
                tick.SetResourceReference(ForegroundProperty, "Sanduhr.Brush.Text");
                AutomationProperties.SetName(tick, name);   // streamer: masked - the same masked name
                _moves[m.Source] = tick;
                group.Children.Add(tick);
            }
            Groups.Children.Add(group);
        }
        OkButton.Click += (_, _) =>
        {
            Result = TryResult(out var error);
            Error.Text = error ?? "";
            if (Result is not null) DialogResult = true;
        };
    }

    public DrawerPlanEdits? Result { get; private set; }

    public bool CanRename(string id) => _names.ContainsKey(id);
    public void Untick(string path) => _moves[path].IsChecked = false;
    public void Rename(string id, string name) => _names[id].Text = name;

    public DrawerPlanEdits? TryResult(out string? error)
    {
        foreach (var box in _names.Values)
            if (!DrawerNames.IsValid(box.Text.Trim()))
            {
                error = $"\"{box.Text.Trim()}\" can't be a folder name.";
                return null;
            }
        error = null;
        return new DrawerPlanEdits(
            _moves.Where(kv => kv.Value.IsChecked != true).Select(kv => kv.Key).ToHashSet(StringComparer.OrdinalIgnoreCase),
            _names.ToDictionary(kv => kv.Key, kv => kv.Value.Text.Trim(), StringComparer.Ordinal));
    }

    private static TextBlock Heading(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        t.SetResourceReference(ForegroundProperty, "Sanduhr.Brush.Text");
        return t;
    }
}
```

`DeskReviewBar.xaml`: between `DismissButton` and `ApplyButton`, add
`<Button x:Name="EditButton" Style="{StaticResource BarButton}" Content="Edit" Visibility="Collapsed" />` (match the neighbours'
margin). `DeskReviewBar.xaml.cs`: add `public event Action? EditRequested;` and `public Button Edit => EditButton;`; in the
constructor, `AutomationProperties.SetName(EditButton, DeskReviewCopy.Edit);` and `EditButton.Click += (_, _) => EditRequested?.Invoke();`;
in `SetModel`:

```csharp
        EditButton.Visibility = model.CanEdit ? Visibility.Visible : Visibility.Collapsed;
        ApplyButton.Content = model.ApplyLabel ?? DeskReviewCopy.Apply;
        AutomationProperties.SetName(ApplyButton, (string)ApplyButton.Content);   // streamer: notname - fixed copy, Apply or Undo
        DismissButton.Content = model.DismissLabel ?? DeskReviewCopy.Dismiss;
        AutomationProperties.SetName(DismissButton, (string)DismissButton.Content);   // streamer: notname - fixed copy, Dismiss or Done
```

`DeskReviewWindow.xaml.cs`: `public event Action? EditRequested;` and in the constructor `Bar.EditRequested += () => EditRequested?.Invoke();`.

`WidgetReviewService.cs`, `WidgetReviewHost.DeskBusy`:
`DeskDesignService.Current?.Reviewing == true || DrawerReviewService.Current?.Reviewing == true`, and the busy reason's text becomes
"a Desk design or a drawers plan is waiting for the user's review; ask them to Apply or Dismiss it, then propose the widget again".

`DeskDesignService.OnProposalCore`, after the `ResetGuard` line:

```csharp
        if (DrawerReviewService.Current?.Reviewing == true)
            return new DeskAnswer(DeskAnswer.Rejected, "busy",
                [new DeskReason("design", "a drawers plan is waiting for the user's review; ask them to Apply or Dismiss it, then propose the design again")]);
```

`App.xaml.cs`, after the widget review is set up:

```csharp
        _drawerReview = new Desk.DrawerReviewService(_drawers, store, new Desk.DrawerReviewHost(_desk, store));
        Desk.DrawerReviewService.Current = _drawerReview;
```

(dispose the host and clear `Current` beside `_widgetReview`'s).

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DrawerReview|FullyQualifiedName~DrawerPlanEdit|FullyQualifiedName~DeskReview|FullyQualifiedName~WidgetReview|FullyQualifiedName~DeskDesign" --disable-build-servers -nodeReuse:false`
Expected: PASS, the existing review suites included. Then `StreamerReachTests` (add rows) and the full App suite: PASS.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App windows-dotnet/tests/Sanduhr.App.Tests/DrawerReviewServiceTests.cs windows-dotnet/tests/Sanduhr.App.Tests/DrawerPlanEditWindowTests.cs windows-dotnet/tests/Sanduhr.Tests/StreamerReachTests.cs
git commit -m "feat(drawers): review the rules' plan on the Desk with Edit; Apply's result offers Undo; one review at a time

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Turning off, Reset and the Setup.exe uninstall

All three run the same return (spec section 4): every owned home empties onto the Desktop, emptied homes and then the root go, views
stay, and a home holding a file that could not move is kept and named. Turning off asks first with a count; Reset and uninstall do not.

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/DrawerReview.cs` (`KeptLine`)
- Modify: `windows-dotnet/src/Sanduhr.Core/ResetPlan.cs` (the journal goes with History)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerService.cs` (`ChildrenOf`, `ReturnPlan`, `TurnOff`, `Remove`)
- Create: `windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerUninstall.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Services/ResetService.cs` (`returnDrawers` runs first in the Settings part)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs:1087` (pass `returnDrawers`)
- Modify: `windows-dotnet/src/Sanduhr.App/Program.cs` (`OnBeforeUninstallFastCallback`)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DrawerTurnOffTests.cs` (new); `windows-dotnet/tests/Sanduhr.Tests/DrawerReviewTests.cs` and `ResetPlanTests.cs` (add one test each)

**Interfaces:**
- Consumes: Task 5's `DrawerMover.Return`, `DrawerReturnRun`; Task 6's `DrawerService`; plan 1's `DrawerReturn.Plan`, `DrawerReturn.Prompt`.
- Produces:
  - `DrawerReview.KeptLine(int kept) : string?` (Core).
  - `DrawerService.ChildrenOf(string dir) : IEnumerable<DesktopItem>` (internal static); `ReturnPlan() : ReturnPlan`; `TurnOff(Func<bool>? stop = null) : DrawerReturnRun`; `Remove(string id) : DrawerReturnRun`.
  - `DrawerUninstall.Run(Paths paths, TimeSpan budget)` and `Run(Paths paths, DrawerPlaces places, TimeSpan budget) : DrawerReturnRun?`.
  - `ResetService(..., Action? returnDrawers = null)` (new last optional parameter).

- [ ] **Step 1: Write the failing tests**

Add to `tests/Sanduhr.Tests/DrawerReviewTests.cs`:

```csharp
    [Fact]
    public void The_kept_line_says_what_stays_and_how_to_finish()
    {
        Assert.Null(DrawerReview.KeptLine(0));
        Assert.Equal("1 drawer folder stays on your Desktop until its files can move. Turn drawers off again to finish.", DrawerReview.KeptLine(1));
        Assert.Equal("2 drawer folders stay on your Desktop until their files can move. Turn drawers off again to finish.", DrawerReview.KeptLine(2));
    }
```

Add to `tests/Sanduhr.Tests/ResetPlanTests.cs`:

```csharp
    [Fact]
    public void The_drawer_journal_goes_with_History()
    {
        var paths = new Paths(appDataBase: Path.Combine(Path.GetTempPath(), "inv-" + Guid.NewGuid().ToString("N")[..6]));
        Directory.CreateDirectory(paths.AppDataDir);
        File.WriteAllText(paths.DrawerJournalFile, "{}");
        try { Assert.Contains(ResetPlan.Inventory(paths)[ResetPart.History], i => i.Path == paths.DrawerJournalFile); }
        finally { Directory.Delete(paths.AppDataDir, true); }
    }
```

`DrawerTurnOffTests.cs`:

```csharp
using System.IO;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Turning drawers off, removing one drawer, and the uninstall's return, in a sandbox (drawers plan 2, Task 10).</summary>
public sealed class DrawerTurnOffTests
{
    private static (DrawerService S, SettingsStore Store, Paths Paths) Make(DrawerSandbox box, params DrawerDef[] defs)
    {
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"));
        var store = new SettingsStore(paths);
        foreach (var d in defs) Directory.CreateDirectory(d.Home);
        store.SaveDrawers(DrawerSettings.Default with { Enabled = true, Drawers = defs });
        store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl " + string.Join(' ', defs.Select(d => $"drawer.{d.Id}:tl:1")) });
        return (new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, []), Dispatcher.CurrentDispatcher), store, paths);
    }

    private static DrawerDef Owned(DrawerSandbox box, string name) => new(DrawerIds.From(name), name, Path.Combine(box.Drawers, name), true);

    [Fact]
    public void Turning_off_puts_every_file_back_removes_the_folders_and_clears_the_drawers() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store, _) = Make(box, Owned(box, "Images"), Owned(box, "Documents"));
        box.File(@"Desktop\Drawers\Images\a.png");
        box.Dir(@"Desktop\Drawers\Documents\project");
        Assert.Equal("Put 2 files back on the Desktop?", DrawerReturn.Prompt(s.ReturnPlan().Moves.Count));
        var result = s.TurnOff();
        Assert.Equal("2 moved.", result.Run.Line);
        Assert.True(File.Exists(Path.Combine(box.Desktop, "a.png")));
        Assert.True(Directory.Exists(Path.Combine(box.Desktop, "project")));
        Assert.False(Directory.Exists(box.Drawers));
        var after = store.LoadDrawers();
        Assert.False(after.Enabled);
        Assert.Empty(after.Drawers);
        Assert.DoesNotContain("drawer.", store.LoadDesk().Layout);
        s.Dispose();
    });

    [Fact]
    public void A_locked_file_keeps_its_drawer_listed_so_turning_off_again_finishes() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var docs = Owned(box, "Documents");
        var (s, store, _) = Make(box, Owned(box, "Images"), docs);
        box.File(@"Desktop\Drawers\Images\a.png");
        var locked = box.File(@"Desktop\Drawers\Documents\report.docx");
        DrawerReturnRun first;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            first = s.TurnOff();
        Assert.StartsWith("1 moved, 1 skipped: report.docx is open in ", first.Run.Line);
        Assert.Equal(2, first.KeptHomes.Count);   // the home and the root
        Assert.Equal(["documents"], store.LoadDrawers().Drawers.Select(d => d.Id));
        var second = s.TurnOff();
        Assert.Equal("1 moved.", second.Run.Line);
        Assert.False(Directory.Exists(box.Drawers));
        Assert.Empty(store.LoadDrawers().Drawers);
        s.Dispose();
    });

    [Fact]
    public void A_view_drawer_keeps_its_files_where_they_are() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var downloads = box.Dir("Downloads");
        var file = box.File(@"Downloads\setup.exe");
        var (s, _, _) = Make(box, new DrawerDef("downloads", "Downloads", downloads, Owned: false));
        s.TurnOff();
        Assert.True(File.Exists(file));
        Assert.True(Directory.Exists(downloads));
        s.Dispose();
    });

    [Fact]
    public void Removing_one_owned_drawer_returns_its_files_and_leaves_the_others() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, store, _) = Make(box, Owned(box, "Images"), Owned(box, "Documents"));
        box.File(@"Desktop\Drawers\Images\a.png");
        box.File(@"Desktop\Drawers\Documents\b.pdf");
        s.Remove("images");
        Assert.True(File.Exists(Path.Combine(box.Desktop, "a.png")));
        Assert.True(File.Exists(Path.Combine(box.Drawers, "Documents", "b.pdf")));
        Assert.Equal(["documents"], store.LoadDrawers().Drawers.Select(d => d.Id));
        Assert.DoesNotContain("drawer.images", store.LoadDesk().Layout);
        s.Dispose();
    });

    [Fact]
    public void The_uninstall_return_puts_files_back_and_stops_when_its_time_is_up() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, _, paths) = Make(box, Owned(box, "Images"));
        box.File(@"Desktop\Drawers\Images\a.png");
        box.File(@"Desktop\Drawers\Images\b.png");
        s.Dispose();
        var places = new DrawerPlaces(box.Desktop, box.Public, []);
        var none = DrawerUninstall.Run(paths, places, TimeSpan.Zero)!;
        Assert.Equal(2, none.Run.Skipped.Count(k => k.Reason == DrawerSkip.NotInTime));
        var all = DrawerUninstall.Run(paths, places, TimeSpan.FromSeconds(15))!;
        Assert.Equal(2, all.Run.Moved.Count);
        Assert.False(Directory.Exists(box.Drawers));
    });
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DrawerTurnOffTests" --disable-build-servers -nodeReuse:false`
and `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DrawerReviewTests|FullyQualifiedName~drawer_journal" --disable-build-servers -nodeReuse:false`
Expected: build FAIL on `TurnOff`, `Remove`, `ReturnPlan`, `DrawerUninstall` and `KeptLine`.

- [ ] **Step 3: Implement**

`DrawerReview.cs`:

```csharp
    /// <summary>What turning off left behind: the folders kept because a file in them could not move (the root counts as one).</summary>
    public static string? KeptLine(int kept) => kept switch
    {
        0 => null,
        1 => "1 drawer folder stays on your Desktop until its files can move. Turn drawers off again to finish.",
        _ => $"{kept} drawer folders stay on your Desktop until their files can move. Turn drawers off again to finish.",
    };
```

`ResetPlan.Inventory`, in the History list: `AddFile(history, paths.DrawerJournalFile);`.

`DrawerService.cs`:

```csharp
    /// <summary>A home's items for the return: everything but Windows' own housekeeping files, hidden or not (a hidden file a user put in a
    /// drawer is still theirs).</summary>
    internal static IEnumerable<DesktopItem> ChildrenOf(string dir)
    {
        if (!Directory.Exists(dir)) yield break;
        foreach (var info in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            if (!Housekeeping.Contains(info.Name, StringComparer.OrdinalIgnoreCase))   // streamer: key - compared with fixed names
                yield return new DesktopItem(info.FullName, info is DirectoryInfo, info.LastWriteTimeUtc);
    }

    /// <summary>What turning off would move (the confirmation counts these).</summary>
    public ReturnPlan ReturnPlan() =>
        DrawerReturn.Plan(Settings.Drawers, Settings.RootOr(Places.Desktop), Places.Desktop, ChildrenOf, DrawerMover.Exists);

    /// <summary>
    /// Turning drawers off (spec section 4): every owned home empties onto the Desktop and goes, then the root; views stay as they are.
    /// A drawer whose home had to stay (a file open in another app) stays listed, so turning off again finishes the job. Drawers go off,
    /// their layout words go, and auto-tidy goes off; the rules stay.
    /// </summary>
    public DrawerReturnRun TurnOff(Func<bool>? stop = null)
    {
        var result = Mover.Return(ReturnPlan(), Homes, stop);
        var kept = result.KeptHomes.Select(DrawerPaths.Normalize).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stay = Settings.Drawers.Where(d => d.Owned && DrawerPaths.Normalize(d.Home) is { } h && kept.Contains(h)).ToArray();
        Save(Settings with { Enabled = false, AutoTidy = false, Drawers = stay, Folded = [] });
        var desk = _store.LoadDesk();
        var layout = new DeskArrangement(desk.Layout);
        foreach (var word in layout.Pieces.Select(p => p.Widget).Where(DrawerPieces.IsWord).ToArray()) layout.Hide(word);
        _store.SaveDesk(desk with { Layout = layout.Layout });
        Changed?.Invoke();
        return result;
    }

    /// <summary>Removes one drawer: an owned one returns its files to the Desktop first and keeps its listing if a file could not move;
    /// a view just goes (its folder is the user's).</summary>
    public DrawerReturnRun Remove(string id)
    {
        if (Settings.Drawers.FirstOrDefault(d => d.Id == id) is not { } def) return new DrawerReturnRun(DrawerRun.Empty, []);
        var result = new DrawerReturnRun(DrawerRun.Empty, []);
        if (def.Owned)
        {
            var plan = DrawerReturn.Plan([def], Settings.RootOr(Places.Desktop), Places.Desktop, ChildrenOf, DrawerMover.Exists);
            // Only this drawer's home: the root stays for the other drawers.
            plan = plan with { HomesToRemove = [.. plan.HomesToRemove.Where(h => !string.Equals(DrawerPaths.Normalize(h), DrawerPaths.Normalize(Settings.RootOr(Places.Desktop)), StringComparison.OrdinalIgnoreCase))] };
            result = Mover.Return(plan, Homes);
            if (result.KeptHomes.Count > 0) return result;
        }
        Save(Settings with { Drawers = [.. Settings.Drawers.Where(d => d.Id != id)], Folded = [.. Settings.Folded.Where(f => f != id)] });
        Hide(id);
        return result;
    }
```

`DrawerUninstall.cs`:

```csharp
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// The Setup.exe uninstall's return (spec section 4), from Velopack's <c>OnBeforeUninstallFastCallback</c>: the same return as turning off,
/// best-effort and inside a time budget, because that callback must be quick. A file still locked keeps its home, in plain sight on the
/// Desktop. Never throws: an uninstall must not fail on drawers. A Store package cannot run this; Settings and the listing say so.
/// </summary>
internal static class DrawerUninstall
{
    public static void Run(Paths paths, TimeSpan budget) => Run(paths, DrawerPlaces.ForThisUser(), budget);

    public static DrawerReturnRun? Run(Paths paths, DrawerPlaces places, TimeSpan budget)
    {
        try
        {
            var s = new SettingsStore(paths).LoadDrawers();
            if (s.Drawers.Count == 0) return null;
            var plan = DrawerReturn.Plan(s.Drawers, s.RootOr(places.Desktop), places.Desktop, DrawerService.ChildrenOf, DrawerMover.Exists);
            var until = DateTime.UtcNow + budget;
            return new DrawerMover(places.Desktop).Return(plan, [.. s.Drawers.Select(d => d.Home)], () => DateTime.UtcNow >= until);
        }
        catch (Exception) { return null; }
    }
}
```

`ResetService.cs`: add `Action? returnDrawers = null` as the constructor's last parameter, stored in `_returnDrawers`, and in
`RunAsync`'s dispatch:

```csharp
                ResetPart.Settings => await SettingsAsync(),
```

with

```csharp
    /// <summary>Reset's drawers part (5.0 drawers spec, section 4) runs inside Settings, first: every drawer file goes back to the Desktop
    /// while the drawer settings that say where they are still exist. Reset never deletes a user's file.</summary>
    private async Task<ResetStepResult> SettingsAsync()
    {
        try { _returnDrawers?.Invoke(); } catch (Exception) { /* the files stay in plain sight; the reset carries on */ }
        return await DeleteInventoryAsync(ResetPart.Settings);
    }
```

`App.xaml.cs:1087`: pass `returnDrawers: () => _drawers?.TurnOff()`.

`Program.cs`, inside `OnBeforeUninstallFastCallback`, after the toast cleanup:

```csharp
                // Drawers go back to the Desktop (5.0 drawers spec, section 4), best-effort within the fast callback's time.
                try { Desk.DrawerUninstall.Run(new Sanduhr.Core.Paths(), TimeSpan.FromSeconds(15)); }
                catch { /* best-effort: uninstall must never fail on drawers */ }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run the two commands from Step 2, then the Reset suites (`--filter "FullyQualifiedName~Reset"` in both test projects).
Expected: PASS. Then `StreamerReachTests` (add rows) and the full suites: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src windows-dotnet/tests
git commit -m "feat(drawers): turning off, removing a drawer, Reset and the Setup.exe uninstall all return files to the Desktop

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: The Drawers page and the Desk menu

One Settings page in the Desktop group, reached from every drawer's menu ("Drawers settings…") and the Desk menu's "Show drawers". It
turns drawers on (and the Desk with them) and off (with the count), shows where drawer files live and moves that folder while no owned
drawer exists, runs "Sort my desktop now", lists the rules (on or off, up or down, delete, add a name rule), the drawers (rename,
show, remove, add a folder as a drawer), and the last ten moves with Undo. The Store build says what happens on removal.

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/WhatsNew.cs` (`SettingsPage.Drawers`, at the end of the enum)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (sidebar, title, copy, `DeskPiecePage`)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (anchors, entries, page words, `MenuAnchor`, `StreamerPages`, `JumpLinks`)
- Create: `windows-dotnet/src/Sanduhr.App/Views/DrawersPage.xaml`, `DrawersPage.xaml.cs`
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/DrawersSettingsViewModel.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (`Drawers`), `Views/SettingsWindow.xaml` (the tab)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskClicks.cs` (`DeskMenuAction.ShowDrawers`, the menu item), `Desk/Drawers/DrawerPiece.xaml.cs` (`DrawerPieceCopy.Settings`), `Desk/Drawers/DrawerService.cs` (`HasHidden`), `App.xaml.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/SettingsDrawersPageTests.cs`, `windows-dotnet/tests/Sanduhr.App.Tests/DrawersSettingsViewModelTests.cs` (new)

**Interfaces:**
- Consumes: Tasks 6, 9 and 10's `DrawerService` (`Save`, `TurnOff`, `ReturnPlan`, `Remove`, `RenameDrawer`, `AddView`, `ShowAll`, `Undo`, `Journal`), `DrawerReviewService.SortNow`; `DrawerReturn.Prompt`; `DrawerPaths.IsSafeRoot`; `Startup.IsPackaged()`.
- Produces:
  - `SettingsPage.Drawers`; `SettingsNames.DrawersTitle = "Drawers"` and the page copy below; `SettingsNames.LinkTitle(SettingsPage.Drawers) == "Drawers settings…"`; `SettingsNames.DeskPiecePage("drawer.<id>") == SettingsPage.Drawers`.
  - `SettingsIndex` anchors `Drawers = "drawers"`, `DrawersFolder = "drawers-folder"`, `DrawersSort = "drawers-sort"`, `DrawersRules = "drawers-rules"`, `DrawersList = "drawers-list"`, `DrawersRecent = "drawers-recent"`.
  - `DrawersSettingsViewModel(DrawerService? drawers, DrawerReviewService? review, SettingsStore store)` with `Enabled`, `RootText`, `Status`, `CanChangeRoot`, `Rules`, `DrawerRows`, `Recent`, `StoreNoteVisible`, commands `SortNow`, `ChangeRoot`, `AddNameRule`, `ShowDrawers`, `AddFolder`, and seams `Confirm : Func<string, bool>`, `PickFolder : Func<string?>`.
  - `DeskMenuAction.ShowDrawers`; `DrawerService.HasHidden : bool`.

- [ ] **Step 1: Write the failing tests**

`SettingsDrawersPageTests.cs` (Core):

```csharp
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>The Drawers page's place in Settings (drawers plan 2, Task 11).</summary>
public sealed class SettingsDrawersPageTests
{
    [Fact]
    public void Drawers_is_a_Desktop_page_after_the_Launcher()
    {
        var desktop = SettingsNames.Sidebar.Single(g => g.Pages.Contains(SettingsPage.Desk));
        var pages = desktop.Pages.ToList();
        Assert.Equal(pages.IndexOf(SettingsPage.Launcher) + 1, pages.IndexOf(SettingsPage.Drawers));
        Assert.Equal("Drawers", SettingsNames.PageTitle(SettingsPage.Drawers));
        Assert.Equal("Drawers settings…", SettingsNames.LinkTitle(SettingsPage.Drawers));
    }

    [Fact]
    public void A_drawer_piece_opens_the_Drawers_page() =>
        Assert.Equal(SettingsPage.Drawers, SettingsNames.DeskPiecePage("drawer.screenshots"));

    [Fact]
    public void Search_finds_the_page_and_its_sections()
    {
        Assert.Contains(SettingsIndex.Search("sort my desktop"), h => h.Page == SettingsPage.Drawers && h.Anchor == SettingsIndex.DrawersSort);
        Assert.Contains(SettingsIndex.Search("tidy"), h => h.Page == SettingsPage.Drawers);
        Assert.Equal(SettingsIndex.DrawersList, SettingsIndex.MenuAnchor(SettingsPage.Drawers));
        Assert.Contains(SettingsPage.Drawers, SettingsIndex.StreamerPages);
    }
}
```

(If `SettingsIndex.Search`'s hit type names its members differently, use the names `SettingsIndexTests` uses.)

`DrawersSettingsViewModelTests.cs` (App):

```csharp
using System.IO;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

public sealed class DrawersSettingsViewModelTests
{
    private static (DrawersSettingsViewModel Vm, DrawerService S, SettingsStore Store) Make(DrawerSandbox box, bool enabled = false)
    {
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"));
        var store = new SettingsStore(paths);
        store.SaveDesk(DeskSettings.Default with { Enabled = false, Layout = "clock:bl" });
        store.SaveDrawers(DrawerSettings.Default with { Enabled = enabled });
        var s = new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, []), Dispatcher.CurrentDispatcher);
        return (new DrawersSettingsViewModel(s, null, store) { Confirm = _ => true, PickFolder = () => null }, s, store);
    }

    [Fact]
    public void Turning_drawers_on_turns_the_Desk_on_and_says_where_files_live() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, store) = Make(box);
        vm.Enabled = true;
        Assert.True(store.LoadDrawers().Enabled);
        Assert.True(store.LoadDesk().Enabled);
        Assert.Equal(DrawerReview.FirstOnLine, vm.Status);
        s.Dispose();
    });

    [Fact]
    public void Turning_off_asks_with_the_count_and_a_no_keeps_drawers_on() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, store) = Make(box, enabled: true);
        var home = box.Dir(@"Desktop\Drawers\Images");
        box.File(@"Desktop\Drawers\Images.png");
        s.Save(s.Settings with { Drawers = [new DrawerDef("images", "Images", home, true)] });
        string? asked = null;
        vm.Confirm = q => { asked = q; return false; };
        vm.Enabled = false;
        Assert.Equal(DrawerReturn.Prompt(1), asked);
        Assert.True(vm.Enabled);
        Assert.True(store.LoadDrawers().Enabled);
        s.Dispose();
    });

    [Fact]
    public void The_folder_moves_only_while_no_owned_drawer_exists() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, store) = Make(box, enabled: true);
        var elsewhere = box.Dir("Elsewhere");
        vm.PickFolder = () => elsewhere;
        vm.ChangeRootCommand.Execute(null);
        Assert.Equal(elsewhere, store.LoadDrawers().Root);
        s.Save(s.Settings with { Drawers = [new DrawerDef("images", "Images", Path.Combine(elsewhere, "Images"), true)] });
        Assert.False(vm.CanChangeRoot);
        s.Dispose();
    });

    [Fact]
    public void Rules_move_up_and_down_turn_off_and_a_name_rule_is_added() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, store) = Make(box, enabled: true);
        vm.Rules[1].MoveUpCommand.Execute(null);
        Assert.Equal(DrawerRuleKind.Images, store.LoadDrawers().Rules[0].Kind);
        vm.Rules[0].On = false;
        Assert.False(store.LoadDrawers().Rules[0].On);
        vm.NewRuleText = "invoice";
        vm.NewRuleDrawer = "Receipts";
        vm.AddNameRuleCommand.Execute(null);
        var added = store.LoadDrawers().Rules[^1];
        Assert.Equal((DrawerRuleKind.NameContains, "invoice", "receipts"), (added.Kind, added.Text, added.DrawerId));
        s.Dispose();
    });

    [Fact]
    public void Recent_moves_list_the_journal_and_Undo_puts_files_back() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, store) = Make(box, enabled: true);
        var home = box.Dir(@"Desktop\Drawers\Images");
        s.Save(s.Settings with { Drawers = [new DrawerDef("images", "Images", home, true)] });
        var a = box.File(@"Desktop\a.png");
        s.MoveIn([a], "images");
        vm.Refresh();
        var row = Assert.Single(vm.Recent);
        Assert.Contains("1 file", row.Line);
        row.UndoCommand.Execute(null);
        Assert.True(File.Exists(a));
        Assert.Empty(vm.Recent);
        s.Dispose();
    });

    [Fact]
    public void Without_drawers_the_page_still_opens() => Sta.Run(() =>
    {
        var paths = new Paths(appDataBase: Path.Combine(Path.GetTempPath(), "dvm-" + Guid.NewGuid().ToString("N")[..6]));
        var vm = new DrawersSettingsViewModel(null, null, new SettingsStore(paths));
        Assert.False(vm.Enabled);
        Assert.Empty(vm.DrawerRows);
    });
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsDrawersPageTests" --disable-build-servers -nodeReuse:false`
and `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DrawersSettingsViewModelTests" --disable-build-servers -nodeReuse:false`
Expected: build FAIL on the missing `SettingsPage.Drawers` and `DrawersSettingsViewModel`.

- [ ] **Step 3: Core: the page, its names and its index**

`WhatsNew.cs`: append `Drawers` to the end of `SettingsPage`.

`SettingsNames.cs`: in the Desktop group, put `SettingsPage.Drawers` after `SettingsPage.Launcher`; add `SettingsPage.Drawers => DrawersTitle,`
to the page-title switch; add `_ when DrawerPieces.IsWord(widget) => SettingsPage.Drawers,` to `DeskPiecePage` before `_ => null`; and
add the copy:

```csharp
    // ---- Drawers (5.0 drawers spec) ----
    public const string DrawersTitle = "Drawers";
    public const string DrawersSwitch = "Use drawers";
    public const string DrawersLine = "Drawers sort your desktop into folders on the Desk. Nothing moves until you say so, and turning drawers off puts every file back on the Desktop.";
    public const string DrawersSwitchCaption = "Drawers live on the Desk, so turning them on turns the Desk on.";
    public const string DrawersFolder = "Where drawer files live";
    public const string DrawersChangeFolder = "Change folder…";
    public const string DrawersFolderLocked = "Turn drawers off first to move the Drawers folder.";
    public const string DrawersSort = "Sort my desktop";
    public const string DrawersSortButton = "Sort my desktop now";
    public const string DrawersSortCaption = "The rules below make a plan. It shows on your Desk for review, and nothing moves until you choose Apply.";
    public const string DrawersRules = "Rules";
    public const string DrawersRulesCaption = "Rules run top to bottom and the first match wins. Files no rule takes stay on the Desktop.";
    public const string DrawersAddRule = "Add rule";
    public const string DrawersList = "Your drawers";
    public const string DrawersShow = "Show drawers";
    public const string DrawersAddFolder = "Show a folder as a drawer…";
    public const string DrawersRecent = "Recent moves";
    public const string DrawersNothingRecent = "Nothing has moved yet.";
    public const string DrawersStoreNote = "If you remove Sanduhr from the Microsoft Store, drawer files stay in the Drawers folder on your Desktop.";
    public const string DrawersTurnOffTitle = "Turn off drawers";
```

`SettingsIndex.cs`: add the six anchor constants beside the Launcher's; the entries:

```csharp
        E(SettingsNames.DrawersSwitch, SettingsPage.Drawers, Drawers,
            ["drawers", "drawer", "tidy", "organize", "organise", "clean up", "desktop icons", "turn off", "put back"]),
        E(SettingsNames.DrawersFolder, SettingsPage.Drawers, DrawersFolder, ["folder", "Drawers folder", "OneDrive", "location", "where"]),
        E(SettingsNames.DrawersSort, SettingsPage.Drawers, DrawersSort,
            [SettingsNames.DrawersSortButton, "sort", "sort my desktop", "plan", "review", "apply", "screenshots", "installers"]),
        E(SettingsNames.DrawersRules, SettingsPage.Drawers, DrawersRules,
            ["rules", "by kind", "by age", "by name", "older than", "first match", "order", SettingsNames.DrawersAddRule]),
        E(SettingsNames.DrawersList, SettingsPage.Drawers, DrawersList,
            ["rename", "hide", "show", SettingsNames.DrawersShow, "remove", "view", "existing folder", "Downloads"]),
        E(SettingsNames.DrawersRecent, SettingsPage.Drawers, DrawersRecent, ["undo", "history", "journal", "moved"]),
```

`[SettingsPage.Drawers] = ["tidy", "sort", "folders", "organize"],` in the page words; `SettingsPage.Drawers => DrawersList,` in
`MenuAnchor`; `SettingsPage.Drawers` in `StreamerPages`; `SettingsPage.Drawers => Links(page, DrawersSort, DrawersRules, DrawersList, DrawersRecent),`
in `JumpLinks`.

- [ ] **Step 4: The view model**

`DrawersSettingsViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>Settings, Drawers (5.0 drawers spec): on and off, where files live, Sort my desktop now, the rules, the drawers and the recent
/// moves with Undo. Works with no drawer service (tests, the harness): the page opens empty.</summary>
internal sealed partial class DrawersSettingsViewModel : ObservableObject
{
    private readonly DrawerService? _drawers;
    private readonly DrawerReviewService? _review;
    private readonly SettingsStore _store;
    private bool _loading;

    public DrawersSettingsViewModel(DrawerService? drawers, DrawerReviewService? review, SettingsStore store)
    {
        _drawers = drawers;
        _review = review;
        _store = store;
        if (_drawers is not null) _drawers.Changed += Refresh;
        Refresh();
    }

    /// <summary>Asks a yes or no question (the turn-off count); the page sets a themed dialog.</summary>
    public Func<string, bool> Confirm { get; set; } = _ => false;

    /// <summary>Picks a folder; the page sets the system folder picker.</summary>
    public Func<string?> PickFolder { get; set; } = () => null;

    public ObservableCollection<DrawerRuleRow> Rules { get; } = [];
    public ObservableCollection<DrawerRow> DrawerRows { get; } = [];
    public ObservableCollection<DrawerRecentRow> Recent { get; } = [];
    public bool StoreNoteVisible { get; } = Startup.IsPackaged();

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _rootText = "";
    [ObservableProperty] private bool _canChangeRoot;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _newRuleText = "";
    [ObservableProperty] private string _newRuleDrawer = "";

    public void Refresh()
    {
        _loading = true;
        try
        {
            if (_drawers is null) return;
            var s = _drawers.Settings;
            Enabled = s.Enabled;
            RootText = StreamerMode.Mask.Paths(s.RootOr(_drawers.Places.Desktop));   // streamer: masked - Mask.Paths hides the profile folder while on
            CanChangeRoot = !s.Drawers.Any(d => d.Owned);
            Rules.Clear();
            for (int i = 0; i < s.Rules.Count; i++) Rules.Add(new DrawerRuleRow(this, i, s.Rules[i], NameOf(s.Rules[i].DrawerId)));
            DrawerRows.Clear();
            foreach (var d in s.Drawers) DrawerRows.Add(new DrawerRow(this, d));
            Recent.Clear();
            foreach (var e in _drawers.Journal.Load().Entries.OrderByDescending(e => e.AtUtc).Take(10)) Recent.Add(new DrawerRecentRow(this, e));
        }
        finally { _loading = false; }
    }

    private string NameOf(string id) =>
        _drawers?.Settings.Drawers.FirstOrDefault(d => d.Id == id) is { } d
            ? StreamerMode.Mask.Name(CodenameCategory.Folder, d.Name)   // streamer: masked - Mask.Name gives the codename while on
            : DrawerReviewService.NameFromId(id);

    partial void OnEnabledChanged(bool value)
    {
        if (_loading || _drawers is null) return;
        var s = _drawers.Settings;
        if (value)
        {
            var desk = _store.LoadDesk();
            if (!desk.Enabled) _store.SaveDesk(desk with { Enabled = true });
            _drawers.Save(s with { Enabled = true });
            _drawers.ShowAll();
            Status = DrawerReview.FirstOnLine;
            return;
        }
        int count = _drawers.ReturnPlan().Moves.Count;
        if (count > 0 && !Confirm(DrawerReturn.Prompt(count)))
        {
            _loading = true;
            Enabled = true;
            _loading = false;
            return;
        }
        var result = _drawers.TurnOff();
        Status = string.Join(" ", new[] { result.Run.Line, DrawerReview.KeptLine(result.KeptHomes.Count) }.OfType<string>());
        Refresh();
    }

    [RelayCommand]
    private void SortNow() => Status = _review?.SortNow() ?? "";

    [RelayCommand]
    private void ChangeRoot()
    {
        if (_drawers is null || !CanChangeRoot) { Status = SettingsNames.DrawersFolderLocked; return; }
        if (PickFolder() is not { } folder) return;
        var p = _drawers.Places;
        if (!DrawerPaths.IsSafeRoot(folder, p.Desktop, p.PublicDesktop)) { Status = "The Drawers folder can't be the Desktop or a folder that holds it."; return; }
        _drawers.Save(_drawers.Settings with { Root = folder });
        Refresh();
    }

    [RelayCommand]
    private void AddNameRule()
    {
        if (_drawers is null || string.IsNullOrWhiteSpace(NewRuleText) || !DrawerNames.IsValid(NewRuleDrawer.Trim())) return;
        var s = _drawers.Settings;
        var id = s.Drawers.FirstOrDefault(d => string.Equals(d.Name, NewRuleDrawer.Trim(), StringComparison.OrdinalIgnoreCase))?.Id
            ?? DrawerIds.From(NewRuleDrawer.Trim());
        var rule = new DrawerRule(DrawerRuleKind.NameContains, id, Text: NewRuleText.Trim());
        _drawers.Save(s with { Rules = [.. s.Rules, rule] });
        NewRuleText = "";
        NewRuleDrawer = "";
        Refresh();
    }

    [RelayCommand]
    private void ShowDrawers() => _drawers?.ShowAll();

    [RelayCommand]
    private void AddFolder()
    {
        if (_drawers is null || PickFolder() is not { } folder) return;
        var name = new DirectoryInfo(folder).Name;   // streamer: masked - the user's own folder name; the row draws it through Mask.Name
        Status = _drawers.AddView(name, folder) ?? "";
        Refresh();
    }

    // ---- rows ----

    internal void SaveRule(int index, Func<DrawerRule, DrawerRule> change)
    {
        if (_loading || _drawers is null) return;
        var rules = _drawers.Settings.Rules.ToList();
        rules[index] = change(rules[index]);
        _drawers.Save(_drawers.Settings with { Rules = rules });
    }

    internal void MoveRule(int index, int by)
    {
        if (_drawers is null) return;
        var rules = _drawers.Settings.Rules.ToList();
        int to = index + by;
        if (to < 0 || to >= rules.Count) return;
        (rules[index], rules[to]) = (rules[to], rules[index]);
        _drawers.Save(_drawers.Settings with { Rules = rules });
        Refresh();
    }

    internal void DeleteRule(int index)
    {
        if (_drawers is null) return;
        var rules = _drawers.Settings.Rules.ToList();
        rules.RemoveAt(index);
        _drawers.Save(_drawers.Settings with { Rules = rules });
        Refresh();
    }

    internal void Rename(string id, string name) { Status = _drawers?.RenameDrawer(id, name) ?? ""; Refresh(); }
    internal void Remove(string id)
    {
        if (_drawers is null) return;
        var r = _drawers.Remove(id);
        Status = string.Join(" ", new[] { r.Run.Moved.Count + r.Run.Skipped.Count > 0 ? r.Run.Line : null, DrawerReview.KeptLine(r.KeptHomes.Count) }.OfType<string>());
        Refresh();
    }
    internal void Undo(string entryId) { Status = _drawers?.Undo(entryId).Line ?? ""; Refresh(); }
}

/// <summary>One rule's row: what it matches, the drawer it fills, on or off, and its place in the order.</summary>
internal sealed partial class DrawerRuleRow : ObservableObject
{
    private readonly DrawersSettingsViewModel _page;
    private readonly int _index;

    public DrawerRuleRow(DrawersSettingsViewModel page, int index, DrawerRule rule, string drawerName)
    {
        _page = page;
        _index = index;
        _on = rule.On;
        _days = rule.Days;
        Kind = rule.Kind;
        Label = rule.Kind switch
        {
            DrawerRuleKind.Screenshots => "Screenshots",
            DrawerRuleKind.Images => "Images",
            DrawerRuleKind.Documents => "Documents",
            DrawerRuleKind.Installers => "Installers",
            DrawerRuleKind.Archives => "Archives",
            DrawerRuleKind.Shortcuts => "Shortcuts",
            DrawerRuleKind.OlderThan => "Not touched in",
            DrawerRuleKind.NameContains => $"Name contains \"{rule.Text}\"",
            _ => $"Name starts with \"{rule.Text}\"",
        } + $" to {drawerName}";
    }

    public DrawerRuleKind Kind { get; }
    public string Label { get; }
    public bool HasDays => Kind == DrawerRuleKind.OlderThan;

    [ObservableProperty] private bool _on;
    [ObservableProperty] private int _days;

    partial void OnOnChanged(bool value) => _page.SaveRule(_index, r => r with { On = value });
    partial void OnDaysChanged(int value) => _page.SaveRule(_index, r => r with { Days = Math.Clamp(value, 1, 3650) });

    [RelayCommand] private void MoveUp() => _page.MoveRule(_index, -1);
    [RelayCommand] private void MoveDown() => _page.MoveRule(_index, +1);
    [RelayCommand] private void Delete() => _page.DeleteRule(_index);
}

/// <summary>One drawer's row: its name, what it is, and Rename and Remove.</summary>
internal sealed partial class DrawerRow : ObservableObject
{
    private readonly DrawersSettingsViewModel _page;
    private readonly string _id;

    public DrawerRow(DrawersSettingsViewModel page, DrawerDef def)
    {
        _page = page;
        _id = def.Id;
        Name = StreamerMode.Mask.Name(CodenameCategory.Folder, def.Name);   // streamer: masked - Mask.Name gives the codename while on
        Kind = def.Owned ? "In the Drawers folder" : "Shows a folder in place";
        _newName = StreamerMode.On ? "" : def.Name;   // streamer: masked - the real name only while streamer mode is off
    }

    public string Name { get; }
    public string Kind { get; }

    [ObservableProperty] private string _newName;

    [RelayCommand] private void Rename() => _page.Rename(_id, NewName);
    [RelayCommand] private void Remove() => _page.Remove(_id);
}

/// <summary>One journal entry: "Today 10:42 · Rules · 14 files", with Undo.</summary>
internal sealed partial class DrawerRecentRow : ObservableObject
{
    private readonly DrawersSettingsViewModel _page;
    private readonly string _id;

    public DrawerRecentRow(DrawersSettingsViewModel page, JournalEntry e)
    {
        _page = page;
        _id = e.Id;
        var local = e.AtUtc.ToLocalTime();
        var when = local.Date == DateTime.Today ? "Today " + local.ToString("t", CultureInfo.CurrentCulture) : local.ToString("g", CultureInfo.CurrentCulture);
        var who = e.Source switch { DrawerPlanSource.Rules => "Rules", DrawerPlanSource.Agent => e.Agent ?? "Agent", _ => "You" };
        Line = $"{when} · {who} · {e.Moves.Count} {(e.Moves.Count == 1 ? "file" : "files")}";
    }

    public string Line { get; }

    [RelayCommand] private void Undo() => _page.Undo(_id);
}
```

- [ ] **Step 5: The page, the tab, the menus**

`DrawersPage.xaml` follows `WatchersPage.xaml`'s shape (glass border, slim scroll bar, `PageBody` stack, title, `StreamerBanner`,
the page line, sections divided by 1-pixel borders, each section a `StackPanel` with `views:SettingsAnchor.Id`):

```xml
<UserControl x:Class="Sanduhr.App.Views.DrawersPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:views="clr-namespace:Sanduhr.App.Views"
             xmlns:core="clr-namespace:Sanduhr.Core;assembly=Sanduhr.Core"
             Focusable="False">
    <!-- Settings, Drawers (5.0 drawers spec). DataContext: the DrawersSettingsViewModel. -->
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BoolVis" />
    </UserControl.Resources>
    <Border Background="{DynamicResource Sanduhr.Brush.Glass}" CornerRadius="8">
        <ScrollViewer VerticalScrollBarVisibility="Auto" Focusable="False">
            <ScrollViewer.Resources>
                <Style TargetType="ScrollBar" BasedOn="{StaticResource SlimThemeScrollBar}" />
            </ScrollViewer.Resources>
            <StackPanel x:Name="PageBody" Margin="20">
                <TextBlock Text="{x:Static core:SettingsNames.DrawersTitle}" FontSize="14" FontWeight="SemiBold" AutomationProperties.HeadingLevel="Level1"
                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,12" />
                <views:StreamerBanner />
                <TextBlock Text="{x:Static core:SettingsNames.DrawersLine}" FontSize="11" TextWrapping="Wrap"
                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,-8,0,12" />

                <StackPanel views:SettingsAnchor.Id="drawers">
                    <CheckBox Style="{DynamicResource ThemedCheckBox}" Content="{x:Static core:SettingsNames.DrawersSwitch}"
                              IsChecked="{Binding Enabled, Mode=TwoWay}" AutomationProperties.Name="{x:Static core:SettingsNames.DrawersSwitch}" />
                    <TextBlock Text="{x:Static core:SettingsNames.DrawersSwitchCaption}" FontSize="11" TextWrapping="Wrap" Margin="0,8,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                    <TextBlock Text="{Binding Status}" FontSize="11.5" TextWrapping="Wrap" Margin="0,6,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" AutomationProperties.LiveSetting="Polite" />
                    <TextBlock Text="{x:Static core:SettingsNames.DrawersStoreNote}" FontSize="11" TextWrapping="Wrap" Margin="0,6,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                               Visibility="{Binding StoreNoteVisible, Converter={StaticResource BoolVis}}" />
                </StackPanel>

                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />
                <StackPanel views:SettingsAnchor.Id="drawers-folder">
                    <TextBlock Text="{x:Static core:SettingsNames.DrawersFolder}" FontSize="13" FontWeight="SemiBold" AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                    <TextBlock Text="{Binding RootText}" FontSize="12" TextWrapping="Wrap" Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                    <Button Content="{x:Static core:SettingsNames.DrawersChangeFolder}" Command="{Binding ChangeRootCommand}" HorizontalAlignment="Left"
                            Margin="0,8,0,0" IsEnabled="{Binding CanChangeRoot}" AutomationProperties.Name="Change the Drawers folder" />
                </StackPanel>

                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />
                <StackPanel views:SettingsAnchor.Id="drawers-sort">
                    <TextBlock Text="{x:Static core:SettingsNames.DrawersSort}" FontSize="13" FontWeight="SemiBold" AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                    <TextBlock Text="{x:Static core:SettingsNames.DrawersSortCaption}" FontSize="11" TextWrapping="Wrap"
                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                    <Button Content="{x:Static core:SettingsNames.DrawersSortButton}" Command="{Binding SortNowCommand}" HorizontalAlignment="Left"
                            Margin="0,8,0,0" IsEnabled="{Binding Enabled}" AutomationProperties.Name="{x:Static core:SettingsNames.DrawersSortButton}" />
                </StackPanel>

                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />
                <StackPanel views:SettingsAnchor.Id="drawers-rules">
                    <TextBlock Text="{x:Static core:SettingsNames.DrawersRules}" FontSize="13" FontWeight="SemiBold" AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                    <TextBlock Text="{x:Static core:SettingsNames.DrawersRulesCaption}" FontSize="11" TextWrapping="Wrap" Margin="0,0,0,8"
                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                    <ItemsControl ItemsSource="{Binding Rules}" Focusable="False">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <DockPanel Margin="0,2">
                                    <Button DockPanel.Dock="Right" Content="Delete" Command="{Binding DeleteCommand}" Margin="4,0,0,0" AutomationProperties.Name="Delete rule" />
                                    <Button DockPanel.Dock="Right" Content="Down" Command="{Binding MoveDownCommand}" Margin="4,0,0,0" AutomationProperties.Name="Move rule down" />
                                    <Button DockPanel.Dock="Right" Content="Up" Command="{Binding MoveUpCommand}" Margin="4,0,0,0" AutomationProperties.Name="Move rule up" />
                                    <TextBox DockPanel.Dock="Right" Width="48" Text="{Binding Days, UpdateSourceTrigger=LostFocus}" Margin="4,0,0,0"
                                             Visibility="{Binding HasDays, Converter={StaticResource BoolVis}}" AutomationProperties.Name="Days" />
                                    <CheckBox Style="{DynamicResource ThemedCheckBox}" Content="{Binding Label}" IsChecked="{Binding On, Mode=TwoWay}"
                                              AutomationProperties.Name="{Binding Label}" VerticalAlignment="Center" />
                                </DockPanel>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <DockPanel Margin="0,8,0,0">
                        <Button DockPanel.Dock="Right" Content="{x:Static core:SettingsNames.DrawersAddRule}" Command="{Binding AddNameRuleCommand}" Margin="6,0,0,0"
                                AutomationProperties.Name="{x:Static core:SettingsNames.DrawersAddRule}" />
                        <TextBox DockPanel.Dock="Right" Width="120" Text="{Binding NewRuleDrawer, UpdateSourceTrigger=PropertyChanged}" Margin="6,0,0,0"
                                 AutomationProperties.Name="Drawer for the new rule" />
                        <TextBox Text="{Binding NewRuleText, UpdateSourceTrigger=PropertyChanged}" AutomationProperties.Name="Name contains" />
                    </DockPanel>
                </StackPanel>

                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />
                <StackPanel views:SettingsAnchor.Id="drawers-list">
                    <TextBlock Text="{x:Static core:SettingsNames.DrawersList}" FontSize="13" FontWeight="SemiBold" AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                    <ItemsControl ItemsSource="{Binding DrawerRows}" Focusable="False">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <DockPanel Margin="0,3">
                                    <Button DockPanel.Dock="Right" Content="Remove" Command="{Binding RemoveCommand}" Margin="4,0,0,0" AutomationProperties.Name="Remove drawer" />
                                    <Button DockPanel.Dock="Right" Content="Rename" Command="{Binding RenameCommand}" Margin="4,0,0,0" AutomationProperties.Name="Rename drawer" />
                                    <TextBox DockPanel.Dock="Right" Width="140" Text="{Binding NewName, UpdateSourceTrigger=PropertyChanged}" Margin="4,0,0,0"
                                             AutomationProperties.Name="New name" />
                                    <StackPanel>
                                        <TextBlock Text="{Binding Name}" FontSize="12.5" Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                        <TextBlock Text="{Binding Kind}" FontSize="11" Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                                    </StackPanel>
                                </DockPanel>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
                        <Button Content="{x:Static core:SettingsNames.DrawersShow}" Command="{Binding ShowDrawersCommand}" AutomationProperties.Name="{x:Static core:SettingsNames.DrawersShow}" />
                        <Button Content="{x:Static core:SettingsNames.DrawersAddFolder}" Command="{Binding AddFolderCommand}" Margin="8,0,0,0"
                                AutomationProperties.Name="Show a folder as a drawer" />
                    </StackPanel>
                </StackPanel>

                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />
                <StackPanel views:SettingsAnchor.Id="drawers-recent">
                    <TextBlock Text="{x:Static core:SettingsNames.DrawersRecent}" FontSize="13" FontWeight="SemiBold" AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                    <ItemsControl ItemsSource="{Binding Recent}" Focusable="False">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <DockPanel Margin="0,2">
                                    <Button DockPanel.Dock="Right" Content="Undo" Command="{Binding UndoCommand}" AutomationProperties.Name="Undo these moves" />
                                    <TextBlock Text="{Binding Line}" FontSize="12" VerticalAlignment="Center" Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                </DockPanel>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </StackPanel>
        </ScrollViewer>
    </Border>
</UserControl>
```

Use the house button style the neighbouring pages use (read `LauncherPage.xaml` for its `Style=` on buttons and text boxes) in
place of the bare `Button` and `TextBox` above; the structure and bindings stay.

`DrawersPage.xaml.cs`: `InitializeComponent()`, and on `DataContextChanged` set the view model's seams:

```csharp
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not ViewModels.DrawersSettingsViewModel vm) return;
            vm.Confirm = q => ThemedDialog.ShowConfirm(Window.GetWindow(this), SettingsNames.DrawersTurnOffTitle, q, "Put them back", "Cancel");
            vm.PickFolder = () =>
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog { Title = SettingsNames.DrawersFolder };
                return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FolderName : null;   // streamer: notname - a folder the user picked, used for IO
            };
            vm.Refresh();
        };
```

`SettingsViewModel.cs`: `public DrawersSettingsViewModel Drawers { get; }` and, beside `Desk = ...`,
`Drawers = new DrawersSettingsViewModel(Sanduhr.App.Desk.DrawerService.Current, Sanduhr.App.Desk.DrawerReviewService.Current, new SettingsStore(new Paths()));`.

`SettingsWindow.xaml`, after the Launcher tab:

```xml
                <TabItem Header="Drawers" Tag="Drawers" AutomationProperties.Name="Drawers" DataContext="{Binding Drawers}">
                    <views:DrawersPage />
                </TabItem>
```

`DeskClicks.cs`: add `ShowDrawers` to `DeskMenuAction`; `public const string ShowDrawers = SettingsNames.DrawersShow;`; in
`OpenMenu`, before the separator: `if (DrawerService.Current?.HasHidden == true) Add(ShowDrawers, DeskMenuAction.ShowDrawers);`.

`DrawerService.cs`:

```csharp
    /// <summary>A drawer exists that is not on the Desk (the Desk menu then offers Show drawers).</summary>
    public bool HasHidden => Settings.Enabled
        && Settings.Drawers.Any(d => new DeskArrangement(_store.LoadDesk().Layout).Placement(DrawerPieces.Word(d.Id)) is null);
```

`DrawerPiece.xaml.cs`: `public static readonly string Settings = SettingsNames.LinkTitle(SettingsPage.Drawers);` in place of the literal
(it is no longer a `const`; the menu code reads it the same way).

`App.xaml.cs`: in the Desk menu switch, `case Desk.DeskMenuAction.ShowDrawers: _drawers?.ShowAll(); break;`; and change the drawer
service's `SettingsRequested` handler to `_ = ShowSettingsAsync(SettingsPage.Drawers, SettingsIndex.MenuAnchor(SettingsPage.Drawers));`.

- [ ] **Step 6: Run the tests and the Settings guards**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --disable-build-servers -nodeReuse:false` and
`dotnet test tests/Sanduhr.App.Tests --disable-build-servers -nodeReuse:false`.
Expected: the new tests PASS. The Settings guard suites (`SettingsIndexTests`, `SettingsNamesTests`, `SettingsLinkTests`,
`SettingsSourceScanTests`, the page-anchor and jump-link tests, `StreamerReachTests`, the streamer sweep) will name what a new page
must also carry. Add each the way commit `202e47b6` added the Launcher page (`git show 202e47b6` lists every file and test it
touched); never loosen a guard to pass. Run both suites again: PASS.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src windows-dotnet/tests
git commit -m "feat(drawers): the Drawers page, Drawers settings… from every drawer, and Show drawers on the Desk menu

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## After the last task

- Full suites from `windows-dotnet/`: Core, App and Mcp, all green, with the counts in the ledger.
- Run the live gate from Task 8, Step 6 once more on the finished branch, then on the second monitor: turn drawers on from Settings,
  Sort my desktop now on a junk Desktop folder copy, Edit, Apply, Undo, turn off with a file open in Notepad, turn off again.
- The checklist (`windows-dotnet/docs/checklist.md`) gets one line per task under 5.0, as plan 1's did.

## What plan 3 covers (written after plan 2 lands)

The three front doors on first run, on top of `JustTheDesk` (Just drawers runs `DrawerReviewService.SortNow` as its success path); the
What's New card that opens the Drawers page; `propose_drawers` and `get_drawers` through `drawers-request.json`, the Privacy page's "Let
agents see and sort my desktop" switch (off by default, the MCP's consent prompt), sanitized agent names and refusal text, and the
single new `TrustBoundaryTests` entry; auto-tidy with its ten-second settle and the "Moved 1 file to Screenshots" toast with Undo;
drag to reorder rules (plan 2 ships Up and Down); the RenderHarness `drawers` scenario in obsidian and aurora; the reviewer letter's
drawers paragraph and the listing note for the Store build.
