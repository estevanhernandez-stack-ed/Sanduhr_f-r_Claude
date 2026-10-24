# Settings v2 for Windows, Slice 2 ("reach") Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every way into Settings lands on its section: What's New and tour cards, menus, search, a second launch with `--settings <page>[#<anchor>]`, and "Settings…" itself, which reopens where it was left this session. Along the way: Quit leaves the Tab path and asks first, empty lists say they are empty, Alerts dim when off, focus is visible, and the window fits a short screen.

**Architecture:** Core first. `SettingsIndex` gains the alias table and `Resolve`, page ids, `SettingsLink` parsing for `--settings`, `MenuAnchor` and `Landing`, the `suggestion` anchor, an inert boost table and a `CollapsedIn` field. `WhatsNewCard` gains `Anchor`, filled by id maps kept apart from the card text in `WhatsNewCards` and `WelcomeTour`. A new pure `WindowFit` sizes the window to a work area. App: `SingleInstance` hands a second launch's link to the first through a named shared-memory mailbox; `Program` passes `--settings`; `App` routes Show me, menus, second launches and its own start-up link through `ShowSettingsAsync(page, anchor)` and keeps the last place in memory; `SettingsWindow` resolves aliases, records `Place`, opens collapsed groups, fits the work area and reorders Tab; `SettingsViewModel` confirms Quit. XAML: Alerts dim, ScrollViewers on Alerts and Publish usage, empty-state lines, focus rings, a last-resort horizontal scroll on Desk. The render harness reports where every card lands.

**Tech Stack:** .NET 10, WPF, xUnit 2.9, CommunityToolkit.Mvvm, `System.IO.MemoryMappedFiles`, user32 through the existing `Interop/GlowNative`.

**Spec:** `docs/settings-v2-windows/spec.md`, "Slice 2: reach" (the authority on scope), with "Search", "Deep links and entry points", "Keyboard and screen reader" and "Short windows". Findings WF17, WF23, WF25, WF26, WF33, WF35, WF36, WF38, WF41, WF45 in `docs/settings-v2-windows/findings.md`. Read them before starting. **No page moves in this slice.** Synonyms (boosts) and the "(in Advanced)" suffix are built and stay inert until the pages and collapsed sections they name land in slices 3 to 7.

**The spec's open question, answered from the code:** `SingleInstance` forwards nothing today. `Claim` sets a named auto-reset event (`<name>.Show`) and waits for `<name>.Shown`; `Listen(Action onShow)` takes no argument (`Services/SingleInstance.cs:53-97`); `Program.Main` passes only the data folder and the 15 s handoff (`Program.cs:38`). Task 7 adds the hand-off.

## Global Constraints

- **CRLF working tree, LF index.** Edit tracked files with the Edit tool only. Never `sed -i`, never rewrite a tracked file wholesale with Write. New files may be created with Write.
- **Keys.** No `settings.json` key is renamed, removed or added in this slice; `SettingsKeysContractTests` is not touched. "Reopen where it was left" is memory only, by spec.
- **Trust boundary.** The MCP link list is unchanged. None of the Core files this slice edits or adds (`SettingsIndex.cs`, `WhatsNew.cs`, `WhatsNewCards.cs`, `WelcomeTour.cs`, `WindowFit.cs`) is in `Sanduhr.Mcp.csproj`'s `Compile Include` list, and none may be added there. `TrustBoundaryTests` stays green untouched.
- **Hands off the live app.** Never start, stop, kill or attach to `Sanduhr.exe`. If a build fails because a `Sanduhr.exe` holds a file, stop and ask the owner. No test reads the real AppData, LocalAppData or Credential Manager: App tests derive from `AppWindowFixture` (scratch `SANDUHR_DEV_PROFILE`, scratch Claude Code profile) and build widgets with `ScratchWidget(...)` (in-memory credentials), never `DemoWidget()`.
- **Hands off the foreground.** Every App test that shows a window shows it through `NoActivate.Show` (Task 8): off screen, `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` set before the first `Show`, `ShowActivated = false`, and `Check()` after anything that moves focus, which hands the foreground back and fails the test if the window ever took it. This is the render harness's pattern (`SettingsPassScenario.cs`, `NeverActivate`, `RouteKeyboard`, `GuardForeground`).
- **The harness** (`tools/Sanduhr.RenderHarness`) refuses to run without `SANDUHR_DEV_PROFILE`; point it at a scratch folder and write its `--out` under `%TEMP%`. Its evidence is never committed.
- **Merge order: slice 1 lands first, then slice 2 rebases on it.** Tasks 1 to 15, 17 and 18 can be built while slice 1 is still open; their edits avoid slice 1's lines. **Rebase this branch on `release/4.0` after slice 1 merges and before Task 16**, because Task 16's edit targets are the list elements as slice 1 leaves them (with their new `AutomationProperties.Name`). Task 19 (slice 1's hand-offs) depends on slice 1 being merged: it uses the `SettingsNames` constants slice 1 adds. Task 20 runs last, on the rebased branch.
- **Slice 1 runs in parallel.** Slice 1 owns label and automation-name strings (`SettingsNames.cs` strings, `AutomationProperties.Name` values, card bodies). Slice 2 owns anchors, entry points, focus, layout and `SettingsIndex.cs`. This slice changes no existing visible string. It adds only these new lines, each required by its scope: the empty-state lines (Task 15, Task 16), the Quit confirmation (Task 12), the Alerts-off line "Alerts are off. Nothing below sends." (spec, Alerts; Task 14), and the search title "Claude's suggestion" for the newly indexed anchor (WF45; Task 4). List names (WF35 "every list has a name") and "Close Settings" are names, so they are slice 1's.
- **Run tests from `windows-dotnet/`.** `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~<Class>` for Core, `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~<Class>` for App. `[DesktopFact]` tests skip on GitHub's hosted runner; run them on a desktop session before the slice is called done.

## Review Focus

1. **The second-launch hand-off must never stop Sanduhr from starting.** Every launch goes through `SingleInstance.Claim`. The mailbox is best effort: a failed create, open, read or write leaves the old behavior (show the widget). A message too long is dropped, never cut, and a stale message is never read as the next launch's. Pinned in Task 7 (`A_second_launch_hands_its_settings_link_to_the_first`, `A_plain_launch_after_a_link_hands_over_nothing`, `A_link_too_long_for_the_mailbox_is_dropped_not_cut`) and the four existing `SingleInstanceTests`.
2. **Aliases and boosts change nothing today.** A live anchor always resolves to itself, a boost needs its target in the index, and rows naming pages this build lacks (`desklook`, `watchers`, `launcher`, `privacy`) are inert. Pinned in Task 1 (`Every_live_page_and_anchor_resolves_to_itself`) and Task 4 (`Boosts_change_no_search_today`), with every existing `SettingsIndexTests` case still green.
3. **Tab order.** Search, page list, Quit, then the page; Tab past the page's last control goes to the search box; the title-bar close button is no longer a tab stop (Alt+F4 still closes; Narrator still finds and invokes it). WPF keyboard navigation in a re-templated `TabControl` is the least predictable part of this slice: pinned in Task 13 by a scan test (CI) and `Tab_from_the_end_of_every_page_goes_to_the_search_box_never_to_Quit` (`[DesktopFact]`, local only).
4. **Work-area math.** `FitToWorkArea` reads the owner's monitor with `MonitorFromWindow` and `GetMonitorInfoW` and divides by the window's DPI scale. The process is system-DPI aware, so this is right on the widget's monitor at the system DPI and approximate on a second monitor at a different scale. Pure sizing pinned in Task 6 (`WindowFitTests`); live screens are Tier 2 item 5.
5. **Card anchors are kept apart from card text.** `WhatsNewCards.AnchorFor(id)` and `WelcomeTour.AnchorFor(destination)` sit below the tables so slice 1 can rewrite bodies without touching the same lines. Pinned in Task 5 (`Every_Settings_card_names_an_anchor_in_the_index`).
6. **The Desk page must not scroll sideways at the default 780 width.** The 530 DIP floor comes from the piece row's columns (118 + 138 + 8 + 72 + 8 + 146 = 490, plus the page's 2 x 20 margin). Pinned in Task 18 (`The_Desk_page_scrolls_sideways_only_below_its_rows_width`).

## Where this plan steps outside the spec's slice 2 file list

The spec's list is the core; these extra files are needed to deliver its own bullets, and none of them is a page move:

- `Sanduhr.Core/WindowFit.cs` (new): the work-area clamp's pure math, kept out of `SettingsIndex.cs`.
- `Sanduhr.App/Views/WhatsNewWindow.xaml.cs`: Show me must pass the card's anchor.
- `Sanduhr.App/ViewModels/SettingsSearchViewModel.cs`: the "(in Advanced)" suffix lives on the search row.
- `Sanduhr.App/MainWindow.xaml.cs`: one argument, `includeManage: true`, so the widget's account menu shows "Accounts settings…" (spec, Menus table).
- `tools/Sanduhr.RenderHarness/SettingsPassScenario.cs`: print each card's landing for the end-to-end check.
- Tests: new `SettingsReachIndexTests.cs`, `SettingsLinkTests.cs`, `WindowFitTests.cs`, `SettingsEntryPointScanTests.cs` (Sanduhr.Tests); new `NoActivate.cs`, `SettingsReachTests.cs` (Sanduhr.App.Tests); additions to `WhatsNewTests.cs`, `SingleInstanceTests.cs`; two lambdas in `AppWindowNamesTests.cs`. New test files instead of growing `SettingsIndexTests.cs` and `SettingsSourceScanTests.cs`, which slice 1 also extends.

Not in this slice, though the spec mentions them nearby: the comment fixes at `SettingsNames.cs:80` and `WidgetViewModel.cs:311` (slice 1, WF47), the Desk card bodies' "in Settings, Desk" wording (slice 1), the Layout map fold below 600 DIP and the wrapping piece rows (slice 3), the `each-limit` and `launcher#launcher-buttons` targets (slices 5 and 3; `MenuAnchor` and the alias table carry them forward).

## Merge surface with slice 1

| File | Slice 2 touches | Slice 1 likely touches | Risk |
| --- | --- | --- | --- |
| `Sanduhr.Core/WhatsNewCards.cs` | the `Table` line (line 10) and a new block after `Release350()` | card bodies and titles | Low: no shared lines |
| `Sanduhr.Core/WelcomeTour.cs` | `Table` and `DeskOnly` declaration lines and their closing `]`, the first and last line of `SignedOut`, a new block at the end | card bodies, `AgainLine`, `SkipTitle` | Low to medium: `SignedOut`'s body string sits between our two lines |
| `Sanduhr.App/App.xaml.cs` | lines 95, 114, 222-223, 290-297, 323-329, 430, 485-486, 665-745, a field after line 50 | tray menu labels at 382-447 (`"Quit Sanduhr"` at 447, "Deep Work (Focus)", "Cooldown Snake") | Medium: line 430 is close to the tray block |
| `Sanduhr.App/Views/SettingsWindow.xaml` | styles (FlatButton, AccentButton, ThemedComboBox, ThemedCheckBox, CloseButton, three new styles), the sidebar template's TabIndex attributes, Alerts and Publish wrappers, the History chart, the list empty states, the shortcut-reset and "Project page" link templates, the Desk scroller | labels and automation names, the shortcut checkbox at 862-864, the trademark footer on Desk, Alerts' "100% plays the !" checkbox, possibly the QuitButton's `Content` | Medium: the QuitButton edit (Task 13) and the shortcut link (Task 17) sit two lines from slice 1's; the Desk closing edit (Task 18) sits under the trademark line slice 1 replaces |
| `Sanduhr.App/MainWindow.xaml.cs` | line 100 (`includeManage`) | widget menu labels | Low |

Also from slice 1's plan (`plan-slice-1.md`, "Hand-offs to other slices"): slice 1 adds `AutomationProperties.Name` to the Projects, Skills, vault Roots and Trends `ItemsControl` lines (Task 16 is written against that text), sets the close button's name on the element (Task 13 sets `IsTabStop` in the style, not on the element), changes the Alerts "100%" checkbox's `Content` line (Task 14 wraps it in a panel and never edits that element, so the two edits share no line), and touches one assertion in `SettingsIndexTests.cs` and replaces its "csv" test (this plan adds no lines to `SettingsIndexTests.cs`; its index tests live in the new `SettingsReachIndexTests.cs`).

Slice 1 lands first; rebase on it before Task 16. Slice 1's strings win wherever both changed a line.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Sanduhr.Core/SettingsIndex.cs` (modify) | `SettingsLink`; `CollapsedIn`; `MessageSuggestion` anchor; `Alias`, `Aliases`, `Resolve`; `PageId`, `PageFromId`, `ParseLink`, `LinkArgument`, `LinkFromArgs`; `MenuAnchor`, `Landing`; `Boost`, `Boosts`, boosted ranking in `Search`. |
| `src/Sanduhr.Core/WhatsNew.cs` (modify) | `WhatsNewCard.Anchor`. |
| `src/Sanduhr.Core/WhatsNewCards.cs` (modify) | `Anchored`, `AnchorFor(id)`. |
| `src/Sanduhr.Core/WelcomeTour.cs` (modify) | `Anchored`, `WithAnchor`, `AnchorFor(destination)`. |
| `src/Sanduhr.Core/WindowFit.cs` (create) | `WindowFit.Area`, `WindowFit.Fit`. |
| `src/Sanduhr.App/Services/SingleInstance.cs` (modify) | The mailbox: `Claim(..., message)`, `Listen(Action<string?>)`, `MaxMessageBytes`. |
| `src/Sanduhr.App/Program.cs` (modify) | Passes `--settings` to `Claim`. |
| `src/Sanduhr.App/App.xaml.cs` (modify) | `ShowMe`, `OnSecondLaunch`, `OpenSettingsLink`, start-up link, menu anchors, account menu item, `_lastSettings`, `FitToWorkArea` call. |
| `src/Sanduhr.App/MainWindow.xaml.cs` (modify) | Widget account menu shows "Accounts settings…". |
| `src/Sanduhr.App/Views/WhatsNewWindow.xaml.cs` (modify) | Show me passes page and anchor. |
| `src/Sanduhr.App/Views/SettingsWindow.xaml.cs` (modify) | `Resolve` in `SelectPage`, `Place`, `RevealInside`, `OpenSettingsLink_Click`, `FitToWorkArea`, `AttachOwner` for Quit. |
| `src/Sanduhr.App/Views/SettingsWindow.xaml` (modify) | Tab order, Alerts dim and scroll, Publish scroll, empty states, focus and disabled styles, search row title, Desk sideways fallback. |
| `src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (modify) | Quit confirmation; `ClaudeCodeSettingsLink`. |
| `src/Sanduhr.App/ViewModels/SettingsSearchViewModel.cs` (modify) | `SettingsSearchRow.CollapsedIn`, `DisplayTitle`. |
| `src/Sanduhr.App/ViewModels/HistoryTabViewModel.cs` (modify) | `EmptyText`, `HasEmptyText`. |
| `tests/Sanduhr.Tests/SettingsSourceScanTests.cs` (modify, Task 19) | `SettingsIndex.cs` leaves `NotScanned` (slice 1 hand-off). |
| `src/Sanduhr.Core/SettingsNames.cs` (modify, Task 19 Step 7 only) | Drop the `ExportCsv` alias, as a one-line commit slice 1's owner reviews. |
| `tools/Sanduhr.RenderHarness/SettingsPassScenario.cs` (modify) | `CARD-LANDING` and `TOUR-LANDING` lines; menu entries with their anchors. |
| `tests/Sanduhr.Tests/SettingsReachIndexTests.cs`, `SettingsLinkTests.cs`, `WindowFitTests.cs`, `SettingsEntryPointScanTests.cs` (create); `WhatsNewTests.cs` (modify) | Core and source-scan tests. |
| `tests/Sanduhr.App.Tests/NoActivate.cs`, `SettingsReachTests.cs` (create); `SingleInstanceTests.cs`, `AppWindowNamesTests.cs` (modify) | App tests. |

---

### Task 1: The alias table and `SettingsIndex.Resolve`

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/SettingsReachIndexTests.cs` (create)

**Interfaces:**
- Produces: `public sealed record SettingsLink(SettingsPage Page, string? Anchor)`; `SettingsIndex.Alias(SettingsPage FromPage, string? FromAnchor, string ToPageId, string ToAnchor)`; `IReadOnlyList<Alias> SettingsIndex.Aliases`; `SettingsLink SettingsIndex.Resolve(SettingsPage page, string? anchor)` and the overload `Resolve(page, anchor, IReadOnlyList<SettingsEntry> anchors, IReadOnlyList<SettingsPage> pages)`; `SettingsPage? SettingsIndex.PageFromId(string? id)`.
- Consumes: `SettingsIndex.Anchors`, `SettingsNames.Pages`.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/SettingsReachIndexTests.cs`:

```csharp
using System.Text.RegularExpressions;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 2 (spec, "Deep links and entry points" and "Search"): the alias table, the boosts, the
/// collapsed-section field and the index's agreement with the XAML.</summary>
public class SettingsReachIndexTests
{
    /// <summary>The v2 page ids (spec, the sidebar table), lower case.</summary>
    private static readonly string[] V2PageIds =
    [
        "general", "accounts", "alerts", "privacy", "claudeusage", "publish", "desk", "desklook", "deskmessage",
        "nowplaying", "weather", "watchers", "launcher", "claudecode", "mods", "widget", "themes", "about",
    ];

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Sanduhr.App");
    }

    [Fact]
    public void Every_live_page_and_anchor_resolves_to_itself()
    {
        foreach (var e in SettingsIndex.Anchors)
            Assert.Equal(new SettingsLink(e.Page, e.Anchor), SettingsIndex.Resolve(e.Page, e.Anchor));
        foreach (var page in SettingsNames.Pages)
            Assert.Equal(new SettingsLink(page, null), SettingsIndex.Resolve(page, null));
        Assert.Equal(new SettingsLink(SettingsPage.Widget, null), SettingsIndex.Resolve(SettingsPage.Widget, null));
    }

    [Fact]
    public void Every_alias_whose_target_page_exists_lands_there_once_its_source_is_retired()
    {
        var rows = SettingsIndex.Aliases.Where(a => SettingsIndex.PageFromId(a.ToPageId) is not null).ToList();
        Assert.NotEmpty(rows);
        // A later build: every source gone, every target present.
        var anchors = SettingsIndex.Anchors
            .Where(e => !SettingsIndex.Aliases.Any(a => a.FromPage == e.Page && a.FromAnchor == e.Anchor))
            .Concat(rows.Select(a => new SettingsEntry(a.ToAnchor, SettingsIndex.PageFromId(a.ToPageId)!.Value, a.ToAnchor, [])))
            .DistinctBy(e => (e.Page, e.Anchor))
            .ToList();
        var pages = SettingsNames.Pages.Where(p => p is not SettingsPage.History and not SettingsPage.Widgets).ToList();
        foreach (var a in rows)
        {
            var to = SettingsIndex.PageFromId(a.ToPageId)!.Value;
            Assert.Equal(new SettingsLink(to, a.ToAnchor), SettingsIndex.Resolve(a.FromPage, a.FromAnchor, anchors, pages));
        }
        // A retired page with an anchor it never had still lands on the page's new home.
        Assert.Equal(new SettingsLink(SettingsPage.ClaudeUsage, "meters"),
            SettingsIndex.Resolve(SettingsPage.History, "nowhere", anchors, pages));
    }

    [Fact]
    public void A_row_naming_a_page_this_build_lacks_changes_nothing()
    {
        // Desk's Look moves to Desk Look in slice 3; until that page exists the old link stays put.
        var withoutLook = SettingsIndex.Anchors.Where(e => e.Anchor != SettingsIndex.DeskLook).ToList();
        Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskLook),
            SettingsIndex.Resolve(SettingsPage.Desk, SettingsIndex.DeskLook, withoutLook, SettingsNames.Pages));
    }

    [Fact]
    public void The_alias_table_has_one_row_per_old_place_and_names_v2_pages_only()
    {
        var sources = SettingsIndex.Aliases.Select(a => (a.FromPage, a.FromAnchor)).ToList();
        Assert.Equal(sources.Count, sources.Distinct().Count());
        Assert.All(SettingsIndex.Aliases, a => Assert.Contains(a.ToPageId, V2PageIds));
        Assert.Equal(22, SettingsIndex.Aliases.Count);
    }
}
```

The `AppDir` helper and the `Regex` using are for Task 4's tests in this file.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsReachIndexTests`
Expected: build FAILS with `The type or namespace name 'SettingsLink' could not be found`.

- [ ] **Step 3: Add `SettingsLink` and the alias table**

In `SettingsIndex.cs`, after the `SettingsEntry` record (it ends at line 16, `    string? Subsection = null);`), add:

```csharp

/// <summary>A place in Settings: a page and, optionally, an anchor on it (spec, "Deep links and entry points").</summary>
public sealed record SettingsLink(SettingsPage Page, string? Anchor);
```

In the `SettingsIndex` class, before `    private static int IndexOf(IReadOnlyList<SettingsPage> list, SettingsPage p)`, add:

```csharp
    // ---- reach (Settings v2, slice 2): aliases ----

    /// <summary>
    /// An old page or anchor and where it lives in v2. <paramref name="ToPageId"/> is the target page's lower-case id
    /// (<see cref="PageId"/>), so a row can name a page this build doesn't have yet: such a row does nothing until it
    /// does. A null <paramref name="FromAnchor"/> stands for the whole page once the page leaves the sidebar.
    /// </summary>
    public sealed record Alias(SettingsPage FromPage, string? FromAnchor, string ToPageId, string ToAnchor);

    /// <summary>Every old place that moves in v2 (spec, "Deep links and entry points", Aliases), anchor rows before page rows.</summary>
    public static IReadOnlyList<Alias> Aliases { get; } =
    [
        new(SettingsPage.History, Chart, "claudeusage", "meters"),
        new(SettingsPage.History, HistoryExport, "claudeusage", "meters-export"),
        new(SettingsPage.Widgets, WidgetsCatalog, "mods", "catalog"),
        new(SettingsPage.Widgets, WidgetsInstalled, "mods", ModList),
        new(SettingsPage.Widgets, WidgetsCompanion, "desk", DeskPieces),
        new(SettingsPage.Desk, DeskLook, "desklook", "look"),
        new(SettingsPage.Desk, DeskInk, "desklook", "ink"),
        new(SettingsPage.Desk, DeskPlate, "desklook", "desk-plate"),
        new(SettingsPage.Desk, DeskWatchers, "watchers", "watchers"),
        new(SettingsPage.Desk, DeskLauncher, "launcher", "launcher"),
        new(SettingsPage.Desk, DeskDirect, "desk", DeskDesigns),
        new(SettingsPage.DeskMessage, MessageLook, "desklook", "sizes"),
        new(SettingsPage.Alerts, SignInReminders, "desk", "signin-reminders"),
        new(SettingsPage.ClaudeCode, Meters, "mods", "meters"),
        new(SettingsPage.ClaudeCode, Statusline, "claudecode", Folders),
        new(SettingsPage.ClaudeCode, Mcp, "claudecode", Folders),
        new(SettingsPage.Mods, ConfigFiles, "claudecode", "folder-config"),
        new(SettingsPage.Mods, McpServers, "claudecode", "folder-config"),
        new(SettingsPage.Mods, ClaudeMd, "claudecode", "folder-config"),
        new(SettingsPage.Mods, Retention, "claudecode", "folder-config"),
        new(SettingsPage.History, null, "claudeusage", "meters"),
        new(SettingsPage.Widgets, null, "mods", "catalog"),
    ];

    /// <summary>The page an id names, in any case; null for anything else, numbers and spaces included.</summary>
    public static SettingsPage? PageFromId(string? id) =>
        !string.IsNullOrEmpty(id) && id.All(char.IsAsciiLetter) && Enum.TryParse<SettingsPage>(id, ignoreCase: true, out var page)
            ? page
            : null;

    /// <summary>
    /// Where (<paramref name="page"/>, <paramref name="anchor"/>) lives now: itself while it is still in the index; else
    /// its alias's target once that target is in the index; else itself, unchanged. Cards, tests and agent text written
    /// before 4.0 land through this.
    /// </summary>
    public static SettingsLink Resolve(SettingsPage page, string? anchor) =>
        Resolve(page, anchor, Anchors, SettingsNames.Pages);

    /// <summary><see cref="Resolve(SettingsPage, string?)"/> over a given index and sidebar, so tests can stand in for later slices.</summary>
    public static SettingsLink Resolve(SettingsPage page, string? anchor, IReadOnlyList<SettingsEntry> anchors,
        IReadOnlyList<SettingsPage> pages)
    {
        bool Live(SettingsPage p, string? a) =>
            pages.Contains(p) && (a is null || anchors.Any(e => e.Page == p && e.Anchor == a));
        if (Live(page, anchor))
            return new(page, anchor);
        var row = (anchor is null ? null : Aliases.FirstOrDefault(a => a.FromPage == page && a.FromAnchor == anchor))
            ?? (pages.Contains(page) ? null : Aliases.FirstOrDefault(a => a.FromPage == page && a.FromAnchor is null));
        if (row is not null && PageFromId(row.ToPageId) is { } to && Live(to, row.ToAnchor))
            return new(to, row.ToAnchor);
        return new(page, anchor);
    }

```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsReachIndexTests|FullyQualifiedName~SettingsIndexTests"`
Expected: PASS (all, including every existing `SettingsIndexTests` case).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/tests/Sanduhr.Tests/SettingsReachIndexTests.cs
git commit -m "feat(settings-v2): alias table and SettingsIndex.Resolve, inert until targets land"
```

---

### Task 2: Page ids, `ParseLink` and the `--settings` switch

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/SettingsLinkTests.cs` (create)

**Interfaces:**
- Produces: `string SettingsIndex.PageId(SettingsPage)`; `const string SettingsIndex.SettingsSwitch = "--settings"`; `SettingsLink? SettingsIndex.ParseLink(string? text)`; `string? SettingsIndex.LinkArgument(IEnumerable<string> args)` (never returns `""`: a bare switch reads `"general"`); `SettingsLink? SettingsIndex.LinkFromArgs(IEnumerable<string> args)`.
- Consumes: `PageFromId`, `Resolve`, `Find` (Task 1).

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/SettingsLinkTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2 reach (spec, "Deep links and entry points"): page ids, links, the --settings switch, the
/// section each menu opens and where an opening Settings lands.</summary>
public class SettingsLinkTests
{
    [Fact]
    public void Every_page_has_a_lowercase_id_that_reads_back()
    {
        foreach (var page in Enum.GetValues<SettingsPage>())
        {
            var id = SettingsIndex.PageId(page);
            Assert.Equal(id.ToLowerInvariant(), id);
            Assert.Equal(page, SettingsIndex.PageFromId(id));
            Assert.Equal(page, SettingsIndex.PageFromId(id.ToUpperInvariant()));
        }
        Assert.Equal("claudeusage", SettingsIndex.PageId(SettingsPage.ClaudeUsage));
        Assert.Null(SettingsIndex.PageFromId("3"));
        Assert.Null(SettingsIndex.PageFromId("desk look"));
        Assert.Null(SettingsIndex.PageFromId(""));
        Assert.Null(SettingsIndex.PageFromId(null));
        Assert.Null(SettingsIndex.PageFromId("desklook")); // slice 3's page: unknown until then
    }

    [Theory]
    [InlineData("desk#pieces", SettingsPage.Desk, "pieces")]
    [InlineData("DESK#Pieces", SettingsPage.Desk, "pieces")]
    [InlineData("  claudeusage#sessions  ", SettingsPage.ClaudeUsage, "sessions")]
    [InlineData("desk", SettingsPage.Desk, null)]
    [InlineData("desk#", SettingsPage.Desk, null)]
    [InlineData("desk#nowhere", SettingsPage.Desk, null)]
    [InlineData("about#version", SettingsPage.About, "version")]
    [InlineData("history#chart", SettingsPage.History, "chart")]
    [InlineData("widget", SettingsPage.Widget, null)]
    [InlineData("widget#tray", SettingsPage.Widget, null)]
    [InlineData("", SettingsPage.General, null)]
    [InlineData("nonsense#tray", SettingsPage.General, null)]
    [InlineData("3", SettingsPage.General, null)]
    public void A_link_reads_as_a_page_and_an_anchor_on_it(string text, SettingsPage page, string? anchor) =>
        Assert.Equal(new SettingsLink(page, anchor), SettingsIndex.ParseLink(text));

    [Fact]
    public void No_text_is_no_link() => Assert.Null(SettingsIndex.ParseLink(null));

    [Theory]
    [InlineData(new[] { "--settings", "desk#pieces" }, "desk#pieces")]
    [InlineData(new[] { "--settings=about#version" }, "about#version")]
    [InlineData(new[] { "--SETTINGS", "alerts" }, "alerts")]
    [InlineData(new[] { "--demo", "--settings" }, "general")]
    [InlineData(new[] { "--settings", "--demo" }, "general")]
    [InlineData(new[] { "--settings=" }, "general")]
    [InlineData(new[] { "--demo" }, null)]
    [InlineData(new string[0], null)]
    public void The_settings_switch_reads_its_value(string[] args, string? expected) =>
        Assert.Equal(expected, SettingsIndex.LinkArgument(args));

    [Fact]
    public void A_launch_with_the_switch_opens_that_place()
    {
        Assert.Equal(new SettingsLink(SettingsPage.ClaudeUsage, SettingsIndex.Sessions),
            SettingsIndex.LinkFromArgs(["--settings", "claudeusage#sessions"]));
        Assert.Null(SettingsIndex.LinkFromArgs(["--demo"]));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsLinkTests`
Expected: build FAILS with `'SettingsIndex' does not contain a definition for 'PageId'`.

- [ ] **Step 3: Implement**

In `SettingsIndex.cs`, after the `Resolve` overload from Task 1, add:

```csharp
    // ---- reach: links and the --settings switch ----

    /// <summary>A page's id in links and <c>--settings</c>: its enum name in lower case ("claudeusage").</summary>
    public static string PageId(SettingsPage page) => page.ToString().ToLowerInvariant();

    /// <summary>The command-line switch that opens Settings: <c>Sanduhr.exe --settings &lt;page&gt;[#&lt;anchor&gt;]</c>.</summary>
    public const string SettingsSwitch = "--settings";

    /// <summary>
    /// A link's text, "desk#pieces", as a place: the page by id (any case), the anchor after "#" resolved through the
    /// aliases and dropped when the page has no such anchor. Text that names no Settings page opens General; the widget
    /// is kept, so the caller can show it. Null text is no link.
    /// </summary>
    public static SettingsLink? ParseLink(string? text)
    {
        if (text is null)
            return null;
        var trimmed = text.Trim();
        int hash = trimmed.IndexOf('#');
        var pageText = (hash < 0 ? trimmed : trimmed[..hash]).Trim();
        var anchorText = hash < 0 ? "" : trimmed[(hash + 1)..].Trim().ToLowerInvariant();
        if (PageFromId(pageText) is not { } page)
            return new(SettingsPage.General, null);
        var link = Resolve(page, anchorText.Length == 0 ? null : anchorText);
        if (link.Page != SettingsPage.Widget && !SettingsNames.Pages.Contains(link.Page))
            return new(SettingsPage.General, null);
        return link.Anchor is not null && Find(link.Page, link.Anchor) is null ? link with { Anchor = null } : link;
    }

    /// <summary>
    /// The value of <see cref="SettingsSwitch"/> in <paramref name="args"/>: "desk#pieces" for "--settings desk#pieces" or
    /// "--settings=desk#pieces"; "general" when the switch has no value; null without the switch.
    /// </summary>
    public static string? LinkArgument(IEnumerable<string> args)
    {
        var list = args as IReadOnlyList<string> ?? args.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var arg = list[i];
            if (arg.StartsWith(SettingsSwitch + "=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg[(SettingsSwitch.Length + 1)..];
                return value.Length == 0 ? PageId(SettingsPage.General) : value;
            }
            if (string.Equals(arg, SettingsSwitch, StringComparison.OrdinalIgnoreCase))
                return i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal)
                    ? list[i + 1]
                    : PageId(SettingsPage.General);
        }
        return null;
    }

    /// <summary>The place a launch's <see cref="SettingsSwitch"/> asks for, null without the switch.</summary>
    public static SettingsLink? LinkFromArgs(IEnumerable<string> args) => ParseLink(LinkArgument(args));

```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsLinkTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/tests/Sanduhr.Tests/SettingsLinkTests.cs
git commit -m "feat(settings-v2): page ids, link parsing and the --settings switch"
```

---

### Task 3: `MenuAnchor` and `Landing`

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/SettingsLinkTests.cs`

**Interfaces:**
- Produces: `string? SettingsIndex.MenuAnchor(SettingsPage page)` (Accounts to `accounts`, Alerts to `notifications`, Desk to `pieces`, DeskMessage to `lines`; null elsewhere); `SettingsLink SettingsIndex.Landing(SettingsPage? page, string? anchor, SettingsLink? last)`.
- Consumes: nothing new.

- [ ] **Step 1: Write the failing tests**

Append inside `SettingsLinkTests` (before its closing `}`):

```csharp
    [Theory]
    [InlineData(SettingsPage.Accounts, SettingsIndex.AccountList)]
    [InlineData(SettingsPage.Alerts, SettingsIndex.Notifications)]
    [InlineData(SettingsPage.Desk, SettingsIndex.DeskPieces)]
    [InlineData(SettingsPage.DeskMessage, SettingsIndex.MessageLines)]
    public void Menu_links_land_on_their_section(SettingsPage page, string anchor)
    {
        Assert.Equal(anchor, SettingsIndex.MenuAnchor(page));
        Assert.NotNull(SettingsIndex.Find(page, anchor));
    }

    [Fact]
    public void A_menu_with_no_section_opens_the_page_top() => Assert.Null(SettingsIndex.MenuAnchor(SettingsPage.Themes));

    [Fact]
    public void Settings_reopens_where_it_was_left_and_a_named_place_wins()
    {
        var last = new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskPieces);
        Assert.Equal(new SettingsLink(SettingsPage.General, null), SettingsIndex.Landing(null, null, null));
        Assert.Equal(last, SettingsIndex.Landing(null, null, last));
        Assert.Equal(new SettingsLink(SettingsPage.Alerts, SettingsIndex.Notifications),
            SettingsIndex.Landing(SettingsPage.Alerts, SettingsIndex.Notifications, last));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsLinkTests`
Expected: build FAILS with `'SettingsIndex' does not contain a definition for 'MenuAnchor'`.

- [ ] **Step 3: Implement**

In `SettingsIndex.cs`, after `LinkFromArgs`, add:

```csharp
    // ---- reach: menus and reopening ----

    /// <summary>
    /// The section a menu's "&lt;Page&gt; settings…" opens at (spec, Menus table). Slices 3 and 5 move Alerts to
    /// <c>each-limit</c> and add the Launcher here; null opens the page top.
    /// </summary>
    public static string? MenuAnchor(SettingsPage page) => page switch
    {
        SettingsPage.Accounts => AccountList,
        SettingsPage.Alerts => Notifications,
        SettingsPage.Desk => DeskPieces,
        SettingsPage.DeskMessage => MessageLines,
        _ => null,
    };

    /// <summary>Where an opening Settings lands: the place asked for; else where it was left this session; else General.</summary>
    public static SettingsLink Landing(SettingsPage? page, string? anchor, SettingsLink? last) =>
        page is { } p ? new(p, anchor) : last ?? new(SettingsPage.General, null);

```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsLinkTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/tests/Sanduhr.Tests/SettingsLinkTests.cs
git commit -m "feat(settings-v2): menu anchors and reopen-where-left landing"
```

---

### Task 4: Search: the `suggestion` anchor, `CollapsedIn` and inert boosts

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/SettingsReachIndexTests.cs`

**Interfaces:**
- Produces: `SettingsEntry(..., string? Subsection = null, string? CollapsedIn = null)`; `const string SettingsIndex.MessageSuggestion = "suggestion"`; `SettingsIndex.Boost(IReadOnlyList<string> Queries, string PageId, string Anchor)`; `IReadOnlyList<Boost> SettingsIndex.Boosts`; `Hit.Rank` of -1 for a boosted hit.
- Consumes: `PageFromId` (Task 1).

- [ ] **Step 1: Write the failing tests**

Append inside `SettingsReachIndexTests`:

```csharp
    [Fact]
    public void Claudes_suggestion_on_Message_is_indexed()
    {
        Assert.NotNull(SettingsIndex.Find(SettingsPage.DeskMessage, SettingsIndex.MessageSuggestion));
        var top = SettingsIndex.Search("suggestion")[0];
        Assert.Equal((SettingsPage.DeskMessage, SettingsIndex.MessageSuggestion), (top.Entry.Page, top.Entry.Anchor));
    }

    [Fact]
    public void Every_anchor_marked_in_XAML_is_in_the_index()
    {
        var views = Path.Combine(AppDir(), "Views");
        var xaml = File.ReadAllText(Path.Combine(views, "SettingsWindow.xaml"));
        var missing = new List<string>();
        foreach (Match tab in Regex.Matches(xaml, "<TabItem\\b[^>]*?\\bTag=\"(\\w+)\"[^>]*>"))
        {
            var page = Enum.Parse<SettingsPage>(tab.Groups[1].Value);
            int end = xaml.IndexOf("</TabItem>", tab.Index, StringComparison.Ordinal);
            var range = xaml[tab.Index..(end < 0 ? xaml.Length : end)];
            var texts = new List<string> { range };
            foreach (Match c in Regex.Matches(range, "<views:(\\w+Page)\\b"))
            {
                var file = Path.Combine(views, c.Groups[1].Value + ".xaml");
                if (File.Exists(file)) texts.Add(File.ReadAllText(file));
            }
            foreach (var text in texts)
                foreach (Match m in Regex.Matches(text, "views:SettingsAnchor\\.Id=\"([\\w-]+)\""))
                    if (SettingsIndex.Find(page, m.Groups[1].Value) is null)
                        missing.Add($"{page}/{m.Groups[1].Value}");
        }
        Assert.True(missing.Count == 0, "Marked in XAML but not in the index: " + string.Join(", ", missing));
    }

    [Fact]
    public void Boosts_change_no_search_today()
    {
        foreach (var boost in SettingsIndex.Boosts)
            foreach (var q in boost.Queries)
                Assert.DoesNotContain(SettingsIndex.Search(q), h => h.Rank < 0);
    }

    [Fact]
    public void A_boost_waits_for_its_section_then_ranks_first()
    {
        var later = SettingsIndex.All.Append(new SettingsEntry("Updates", SettingsPage.About, "updates", [])).ToList();
        var top = SettingsIndex.Search("update", later)[0];
        Assert.Equal((SettingsPage.About, "updates", -1), (top.Entry.Page, top.Entry.Anchor, top.Rank));
        Assert.Equal("updates", SettingsIndex.Search("Updates", later)[0].Entry.Anchor);
        // Accounts' "update sign-in" is still found, below it.
        Assert.Contains(SettingsIndex.Search("update", later), h => h.Entry.Page == SettingsPage.Accounts);
        // Typing part of the word is not the word.
        Assert.DoesNotContain(SettingsIndex.Search("upd", later), h => h.Rank < 0);
    }

    [Fact]
    public void Pack_words_land_on_the_catalog_once_Mods_and_Widgets_has_one()
    {
        var later = SettingsIndex.All.Append(new SettingsEntry("Catalog", SettingsPage.Mods, "catalog", [])).ToList();
        foreach (var q in new[] { "widgets", "packs", "pack" })
        {
            var top = SettingsIndex.Search(q, later)[0];
            Assert.Equal((SettingsPage.Mods, "catalog"), (top.Entry.Page, top.Entry.Anchor));
        }
    }

    [Fact]
    public void Every_boost_names_a_v2_page_and_the_spec_words()
    {
        Assert.All(SettingsIndex.Boosts, b => Assert.Contains(b.PageId, V2PageIds));
        var words = SettingsIndex.Boosts.SelectMany(b => b.Queries).ToList();
        foreach (var w in new[] { "font", "update", "updates", "docker", "runner", "privacy", "offline", "network", "send",
                     "stream", "streamer", "screen share", "hide names", "obs", "demo", "codename", "widgets", "packs", "pack" })
            Assert.Contains(w, words);
    }

    [Fact]
    public void A_hit_in_a_collapsed_section_carries_the_section_name()
    {
        var margins = new SettingsEntry("Margins", SettingsPage.Desk, SettingsIndex.DeskMargins, ["edge"], CollapsedIn: "Advanced");
        Assert.Equal("Advanced", SettingsIndex.Search("edge", [margins]).Single().Entry.CollapsedIn);
        // Nothing is collapsed until slice 3 folds Designs and Advanced.
        Assert.All(SettingsIndex.All, e => Assert.Null(e.CollapsedIn));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsReachIndexTests`
Expected: build FAILS with `'SettingsIndex' does not contain a definition for 'MessageSuggestion'` (and `Boosts`, `CollapsedIn`).

- [ ] **Step 3: Add `CollapsedIn` and the suggestion anchor**

Replace the `SettingsEntry` record (lines 6-16) with:

```csharp
/// <summary>
/// One searchable place in Settings: a page (no anchor), a section or a control. The anchor is
/// page-local and marks the element Settings scrolls to. <paramref name="Subsection"/> names the
/// Claude Usage view (Overview, Trends, Sessions) the anchor sits in, null elsewhere.
/// <paramref name="CollapsedIn"/> names the collapsed section it sits in ("Advanced"), so a search
/// result can say so and opening it can unfold the section; null while it is always shown.
/// </summary>
public sealed record SettingsEntry(
    string Title,
    SettingsPage Page,
    string? Anchor,
    IReadOnlyList<string> Synonyms,
    string? Subsection = null,
    string? CollapsedIn = null);
```

After `    public const string MessageLook = "message-look";` add:

```csharp
    public const string MessageSuggestion = "suggestion";
```

In `Anchors`, before `        E("Your lines", SettingsPage.DeskMessage, MessageLines,` add:

```csharp
        E("Claude's suggestion", SettingsPage.DeskMessage, MessageSuggestion,
            ["suggestion", "suggested", "Claude", "agent", "review", "dismiss", "propose"]),
```

In `Build()`, replace

```csharp
                var sub = Anchors.FirstOrDefault(e => e.Page == page && e.Anchor == anchor)?.Subsection;
                all.Add(new SettingsEntry(name, page, anchor, [], sub));
```

with

```csharp
                var at = Anchors.FirstOrDefault(e => e.Page == page && e.Anchor == anchor);
                all.Add(new SettingsEntry(name, page, anchor, [], at?.Subsection, at?.CollapsedIn));
```

- [ ] **Step 4: Add the boosts and rank them first**

Replace the `Hit` record and `Search` method (lines 352-381) with:

```csharp
    /// <summary>A match: the entry, and 0 when its title matched, 1 when only a synonym did, -1 when a boost named it.</summary>
    public sealed record Hit(SettingsEntry Entry, int Rank);

    /// <summary>
    /// The entries <paramref name="query"/> finds, best first: boosted sections, then title matches, then synonym
    /// matches, then in sidebar order, then in index order. Empty for a blank query.
    /// </summary>
    public static IReadOnlyList<Hit> Search(string? query, IReadOnlyList<SettingsEntry>? index = null)
    {
        var q = Words(query);
        if (q.Count == 0)
            return [];
        index ??= All;
        var order = SettingsNames.Pages;
        var boosted = Boosted(q, index);
        var found = new List<(Hit Hit, int I)>();
        for (int i = 0; i < index.Count; i++)
        {
            var e = index[i];
            if (boosted.Any(b => ReferenceEquals(b, e)))
                continue;
            if (Matches(q, e.Title))
                found.Add((new Hit(e, 0), i));
            else if (e.Synonyms.Any(s => Matches(q, s)))
                found.Add((new Hit(e, 1), i));
        }
        int PageOrder(SettingsPage p) { var at = IndexOf(order, p); return at < 0 ? int.MaxValue : at; }
        return [.. boosted.Select(e => new Hit(e, -1)),
            .. found
            .OrderBy(f => f.Hit.Rank)
            .ThenBy(f => PageOrder(f.Hit.Entry.Page))
            .ThenBy(f => f.I)
            .Select(f => f.Hit)];
    }

    /// <summary>
    /// A word that lands on one section above every title match (spec, Search, Synonyms). Like an alias it names its page
    /// by id, so it does nothing until that page and anchor are in the index: the rows below wait for slices 3 to 7.
    /// </summary>
    public sealed record Boost(IReadOnlyList<string> Queries, string PageId, string Anchor);

    /// <summary>The spec's synonyms, each the whole query (any case), never a prefix of it.</summary>
    public static IReadOnlyList<Boost> Boosts { get; } =
    [
        new(["font", "fonts"], "desklook", "look"),
        new(["update", "updates"], "about", "updates"),
        new(["docker", "runner", "runners"], "watchers", "ci-runners"),
        new(["privacy", "offline", "network", "send"], "privacy", "network"),
        new(["stream", "streamer", "screen share", "hide names", "obs"], "privacy", "streamer"),
        new(["demo"], "privacy", "streamer"),
        new(["codename", "codenames"], "privacy", "streamer-allowlist"),
        new(["widgets", "packs", "pack"], "mods", "catalog"),
    ];

    /// <summary>The entries a boost names for exactly this query, in boost order, when they are in <paramref name="index"/>.</summary>
    private static List<SettingsEntry> Boosted(IReadOnlyList<string> query, IReadOnlyList<SettingsEntry> index)
    {
        var phrase = string.Join(' ', query);
        var hits = new List<SettingsEntry>();
        foreach (var boost in Boosts)
        {
            if (!boost.Queries.Any(w => string.Join(' ', Words(w)) == phrase) || PageFromId(boost.PageId) is not { } page)
                continue;
            if (index.FirstOrDefault(e => e.Page == page && e.Anchor == boost.Anchor) is { } entry
                && !hits.Any(h => ReferenceEquals(h, entry)))
                hits.Add(entry);
        }
        return hits;
    }
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsReachIndexTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsNamesTests"`
Expected: PASS (all). `Every_search_anchor_is_marked_in_its_pages_TabItem` finds `suggestion` in `DeskMessagePage.xaml:220`.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/tests/Sanduhr.Tests/SettingsReachIndexTests.cs
git commit -m "feat(settings-v2): index Claude's suggestion, inert synonym boosts, collapsed-section field"
```

---

### Task 5: Anchors on What's New and tour cards

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/WhatsNew.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/WhatsNewCards.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/WelcomeTour.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/WhatsNewTests.cs`

**Interfaces:**
- Produces: `string? WhatsNewCard.Anchor { get; init; }`; every card in `WhatsNewCards.Table`, `WelcomeTour.Table`, `WelcomeTour.DeskOnly` and `WelcomeTour.SignedOut(...)` carries one when its destination is a Settings page.
- Consumes: `SettingsIndex` constants, `SettingsIndex.Resolve` and `Find`.

Card landings (all exist in today's index; slices 3 to 5 re-point them through the aliases): `ci-runners` and `watchers` to `desk-watchers`; `widgets` to `widgets-catalog`; `companion` to `widgets-companion`; `claude-widgets` to `widgets-installed` (where packs made here are listed); `restart-to-update` and `whats-new` to `version`; `desk-fold` and `saved-designs` to `desk-designs`; `weather-nickname` to `weather-place`; `livelier-bars` to `np-bars`; `meter-styles` and `per-monitor` to `pieces`; `desk-design` to `desk-direct`; `launcher` to `desk-launcher`; `song-looks` to `np-looks`; `oled-care` to `desk-oled`; `now-playing-steps-aside` to `np-widget`; `suggested-messages` to `lines` (the suggestion section shows only while one waits, directly above Your lines); `desk-plate` to `desk-plate`; `arrange-hide` to `arrange`; `statusline-label` to `statusline`; `desk` to `desk`; `settings-redesigned` and `tray-shows` to `tray` (General's first section); `claude-code-page` to `folders`; `shortcut-keys` to `shortcuts`; `every-folder` to `vault`. `limits-come-and-go` and `steady-sparklines` show the widget and carry none. Tour: Accounts to `accounts`, Desk to `desk`, General to `tray`, Claude Code to `folders`, the widget none.

- [ ] **Step 1: Write the failing tests**

Append inside `WhatsNewTests` (before its closing `}`):

```csharp
    /// <summary>Settings v2 (WF17): every card that opens Settings names the section it lands on, and that section is in
    /// the index after any alias; a card that shows the widget names none.</summary>
    [Fact]
    public void Every_Settings_card_names_an_anchor_in_the_index()
    {
        var cards = WhatsNewCards.Table
            .Concat(WelcomeTour.Table.Select(t => t.Card))
            .Concat(WelcomeTour.DeskOnly.Select(t => t.Card))
            .Concat(WelcomeTour.Table.Select(WelcomeTour.SignedOut).Select(t => t.Card));
        foreach (var card in cards)
        {
            if (card.Destination == SettingsPage.Widget)
            {
                Assert.Null(card.Anchor);
                continue;
            }
            Assert.False(card.Anchor is null, $"{card.Id} opens {card.Destination} with no anchor");
            var link = SettingsIndex.Resolve(card.Destination, card.Anchor);
            Assert.True(SettingsIndex.Find(link.Page, link.Anchor) is not null,
                $"{card.Id}: {card.Destination}#{card.Anchor} is not in the index");
        }
    }

    /// <summary>The cards the harness found landing below the fold at 800 high (WF17) name their section.</summary>
    [Theory]
    [InlineData("launcher", SettingsPage.Desk, SettingsIndex.DeskLauncher)]
    [InlineData("per-monitor", SettingsPage.Desk, SettingsIndex.DeskPieces)]
    [InlineData("meter-styles", SettingsPage.Desk, SettingsIndex.DeskPieces)]
    [InlineData("saved-designs", SettingsPage.Desk, SettingsIndex.DeskDesigns)]
    [InlineData("desk-fold", SettingsPage.Desk, SettingsIndex.DeskDesigns)]
    [InlineData("desk-plate", SettingsPage.Desk, SettingsIndex.DeskPlate)]
    [InlineData("ci-runners", SettingsPage.Desk, SettingsIndex.DeskWatchers)]
    [InlineData("statusline-label", SettingsPage.ClaudeCode, SettingsIndex.Statusline)]
    public void Cards_that_landed_below_the_fold_name_their_section(string id, SettingsPage page, string anchor)
    {
        var card = WhatsNewCards.Table.Single(c => c.Id == id);
        Assert.Equal(page, card.Destination);
        Assert.Equal(anchor, card.Anchor);
    }

    [Fact]
    public void Signed_out_the_first_tour_card_lands_on_the_account_list()
    {
        var paced = WelcomeTour.SignedOut(WelcomeTour.Table.Single(t => t.Id == "paced"));
        Assert.Equal((SettingsPage.Accounts, SettingsIndex.AccountList), (paced.Card.Destination, paced.Card.Anchor));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~WhatsNewTests`
Expected: build FAILS with `'WhatsNewCard' does not contain a definition for 'Anchor'`.

- [ ] **Step 3: Add `Anchor` to the card**

In `WhatsNew.cs`, after `    public SettingsPage Destination { get; }` add:

```csharp
    /// <summary>
    /// The section Show me lands on: an anchor of <see cref="SettingsIndex"/> on <see cref="Destination"/> (Settings v2,
    /// WF17). Null when the card shows the widget. Set from the id maps in <see cref="WhatsNewCards"/> and
    /// <see cref="WelcomeTour"/>, apart from the card's words.
    /// </summary>
    public string? Anchor { get; init; }
```

- [ ] **Step 4: Fill the What's New anchors**

In `WhatsNewCards.cs`, replace

```csharp
    public static IReadOnlyList<WhatsNewCard> Table { get; } = [.. Release390(), .. Release380(), .. Release371(), .. Release360(), .. Release350()];
```

with

```csharp
    public static IReadOnlyList<WhatsNewCard> Table { get; } = Anchored([.. Release390(), .. Release380(), .. Release371(), .. Release360(), .. Release350()]);
```

and after the `Release350()` method (before the class's closing `}`) add:

```csharp

    /// <summary>Each card with the section its Show me lands on (Settings v2, WF17).</summary>
    private static WhatsNewCard[] Anchored(WhatsNewCard[] cards) =>
        [.. cards.Select(c => AnchorFor(c.Id) is { } anchor ? c with { Anchor = anchor } : c)];

    /// <summary>
    /// Where each card lands, by id. Kept apart from the cards so their words and their landings change independently.
    /// Today's anchors; the aliases in <see cref="SettingsIndex.Resolve(SettingsPage, string?)"/> carry them to v2's pages.
    /// Cards that show the widget have none.
    /// </summary>
    private static string? AnchorFor(string id) => id switch
    {
        "ci-runners" => SettingsIndex.DeskWatchers,
        "widgets" => SettingsIndex.WidgetsCatalog,
        "companion" => SettingsIndex.WidgetsCompanion,
        "claude-widgets" => SettingsIndex.WidgetsInstalled,
        "restart-to-update" => SettingsIndex.Version,
        "desk-fold" => SettingsIndex.DeskDesigns,
        "weather-nickname" => SettingsIndex.WeatherPlace,
        "livelier-bars" => SettingsIndex.NowPlayingBars,
        "meter-styles" => SettingsIndex.DeskPieces,
        "desk-design" => SettingsIndex.DeskDirect,
        "launcher" => SettingsIndex.DeskLauncher,
        "per-monitor" => SettingsIndex.DeskPieces,
        "song-looks" => SettingsIndex.NowPlayingLooks,
        "oled-care" => SettingsIndex.DeskOled,
        "saved-designs" => SettingsIndex.DeskDesigns,
        "now-playing-steps-aside" => SettingsIndex.NowPlayingWidget,
        "watchers" => SettingsIndex.DeskWatchers,
        "suggested-messages" => SettingsIndex.MessageLines,
        "desk-plate" => SettingsIndex.DeskPlate,
        "arrange-hide" => SettingsIndex.DeskArrange,
        "statusline-label" => SettingsIndex.Statusline,
        "desk" => SettingsIndex.DeskOn,
        "settings-redesigned" => SettingsIndex.Tray,
        "claude-code-page" => SettingsIndex.Folders,
        "shortcut-keys" => SettingsIndex.Shortcuts,
        "tray-shows" => SettingsIndex.Tray,
        "every-folder" => SettingsIndex.Vault,
        "whats-new" => SettingsIndex.Version,
        _ => null,
    };
```

- [ ] **Step 5: Fill the tour anchors**

In `WelcomeTour.cs`:

Replace `    public static IReadOnlyList<TourCard> Table { get; } =` and the `[` on the next line with:

```csharp
    public static IReadOnlyList<TourCard> Table { get; } = Anchored(
    [
```

and the `    ];` that closes `Table` (the one directly before the `DeskOnly` doc comment `    /// <summary>\n    /// The tour for someone who chose Just the Desk`) with `    ]);`.

Replace `    public static IReadOnlyList<TourCard> DeskOnly { get; } =` and its `[` with:

```csharp
    public static IReadOnlyList<TourCard> DeskOnly { get; } = Anchored(
    [
```

and the `    ];` that closes `DeskOnly` (directly before `    private static TourCard ById(string id)`) with `    ]);`.

Replace

```csharp
    public static TourCard SignedOut(TourCard card) =>
        card.Step != 1
```

with

```csharp
    public static TourCard SignedOut(TourCard card) => WithAnchor(
        card.Step != 1
```

and, at the end of the same method,

```csharp
                ShowMeTitle = SettingsNames.SignIn,
            };
```

with

```csharp
                ShowMeTitle = SettingsNames.SignIn,
            });
```

Before the class's closing `}` (after `End`) add:

```csharp

    /// <summary>The cards with the section each Show me lands on (Settings v2, WF17).</summary>
    private static TourCard[] Anchored(TourCard[] cards) => [.. cards.Select(WithAnchor)];

    /// <summary><paramref name="card"/> with the anchor its destination calls for; the same card when it has it already.</summary>
    private static TourCard WithAnchor(TourCard card)
    {
        var anchor = AnchorFor(card.Card.Destination);
        return card.Card.Anchor == anchor ? card : card with { Card = card.Card with { Anchor = anchor } };
    }

    /// <summary>Where a tour card lands, by its destination. Kept apart from the cards' words. The widget has none.</summary>
    private static string? AnchorFor(SettingsPage destination) => destination switch
    {
        SettingsPage.Accounts => SettingsIndex.AccountList,
        SettingsPage.Desk => SettingsIndex.DeskOn,
        SettingsPage.General => SettingsIndex.Tray,
        SettingsPage.ClaudeCode => SettingsIndex.Folders,
        _ => null,
    };
```

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~WhatsNewTests|FullyQualifiedName~WhatsNewCardsTests|FullyQualifiedName~WelcomeTourTests"`
Expected: PASS (all; `DeskOnly_ReusesTheDeskAndTrayCardsAndRewritesTheTrayBody` and `SignedOut_ChangesOnlyStepOne` still pass because a card already carrying its anchor is returned as is).

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/WhatsNew.cs windows-dotnet/src/Sanduhr.Core/WhatsNewCards.cs windows-dotnet/src/Sanduhr.Core/WelcomeTour.cs windows-dotnet/tests/Sanduhr.Tests/WhatsNewTests.cs
git commit -m "feat(settings-v2): every What's New and tour card names the section Show me lands on"
```

---

### Task 6: `WindowFit`, the work-area clamp's math

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/WindowFit.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/WindowFitTests.cs` (create)

**Interfaces:**
- Produces: `readonly record struct WindowFit.Area(double Left, double Top, double Width, double Height)`; `WindowFit.Area WindowFit.Fit(Area work, double width, double height, double minWidth, double minHeight)`.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/WindowFitTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings opens inside its monitor's work area (spec, "Short windows"; WF41), in DIPs.</summary>
public class WindowFitTests
{
    private static WindowFit.Area Fit(double left, double top, double width, double height) =>
        WindowFit.Fit(new(left, top, width, height), width: 780, height: 800, minWidth: 680, minHeight: 520);

    [Fact]
    public void A_roomy_screen_gets_the_designed_size_centred() =>
        Assert.Equal(new WindowFit.Area(570, 120, 780, 800), Fit(0, 0, 1920, 1040));

    [Fact]
    public void A_1366x768_laptop_gets_the_whole_work_height() =>
        Assert.Equal(new WindowFit.Area(293, 0, 780, 728), Fit(0, 0, 1366, 728));

    [Fact]
    public void A_1080p_screen_at_150_percent_fits_its_693_DIP_work_area()
    {
        var fit = Fit(0, 0, 1280, 1040 / 1.5);
        Assert.Equal(1040 / 1.5, fit.Height, 3);
        Assert.Equal(0, fit.Top);
        Assert.Equal(250, fit.Left);
    }

    [Fact]
    public void Below_the_minimum_the_top_stays_on_screen() =>
        Assert.Equal(new WindowFit.Area(0, 0, 680, 520), Fit(0, 0, 640, 480));

    [Fact]
    public void A_monitor_left_of_the_main_one_keeps_its_offset() =>
        Assert.Equal(new WindowFit.Area(-1350, 120, 780, 800), Fit(-1920, 0, 1920, 1040));
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~WindowFitTests`
Expected: build FAILS with `The type or namespace name 'WindowFit' could not be found`.

- [ ] **Step 3: Implement**

Create `windows-dotnet/src/Sanduhr.Core/WindowFit.cs`:

```csharp
namespace Sanduhr.Core;

/// <summary>
/// Where a window opens so it fits its monitor's work area (Settings v2, "Short windows"; WF41). Pure, in DIPs: at most
/// the designed size, at least the minimum, centred, and never above or left of the work area, so the window's foot and
/// Quit stay on screen whenever the minimum fits.
/// </summary>
public static class WindowFit
{
    /// <summary>A rectangle in DIPs.</summary>
    public readonly record struct Area(double Left, double Top, double Width, double Height);

    public static Area Fit(Area work, double width, double height, double minWidth, double minHeight)
    {
        double w = Math.Max(minWidth, Math.Min(width, work.Width));
        double h = Math.Max(minHeight, Math.Min(height, work.Height));
        double left = work.Left + Math.Max(0, (work.Width - w) / 2);
        double top = work.Top + Math.Max(0, (work.Height - h) / 2);
        return new Area(left, top, w, h);
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~WindowFitTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/WindowFit.cs windows-dotnet/tests/Sanduhr.Tests/WindowFitTests.cs
git commit -m "feat(settings-v2): WindowFit, the work-area clamp for Settings"
```

---

### Task 7: `SingleInstance` hands a second launch's link to the first

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Services/SingleInstance.cs`
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/SingleInstanceTests.cs`

**Interfaces:**
- Produces: `SingleInstance.Claim(string dataDir, TimeSpan handoff, string? message = null)`; `void Listen(Action<string?> onShow)` (and `Listen(Action)` kept, ignoring the message); `const int SingleInstance.MaxMessageBytes` (1020).
- Kernel objects added, per data folder and session: `<name>.Message` (a 1024-byte memory-mapped mailbox: a 32-bit length, then UTF-8) and `<name>.Post` (a mutex so one second launch writes at a time).

- [ ] **Step 1: Write the failing tests**

In `SingleInstanceTests.cs`, replace

```csharp
    private static (Thread Thread, Task<SingleInstance> Claimed) HoldOnThread(string dir, ManualResetEventSlim release, Action? onShow = null, TimeSpan? handoff = null)
    {
        var claimed = new TaskCompletionSource<SingleInstance>();
        var thread = new Thread(() =>
        {
            var instance = SingleInstance.Claim(dir, handoff ?? TimeSpan.FromSeconds(5));
            if (onShow is not null) instance.Listen(onShow);
```

with

```csharp
    private static (Thread Thread, Task<SingleInstance> Claimed) HoldOnThread(string dir, ManualResetEventSlim release, Action? onShow = null, TimeSpan? handoff = null,
        Action<string?>? onMessage = null)
    {
        var claimed = new TaskCompletionSource<SingleInstance>();
        var thread = new Thread(() =>
        {
            var instance = SingleInstance.Claim(dir, handoff ?? TimeSpan.FromSeconds(5));
            if (onMessage is not null) instance.Listen(onMessage);
            else if (onShow is not null) instance.Listen(onShow);
```

and append inside the class:

```csharp
    [Fact]
    public async Task A_second_launch_hands_its_settings_link_to_the_first()
    {
        var dir = Folder();
        using var release = new ManualResetEventSlim();
        var got = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (thread, claimed) = HoldOnThread(dir, release, onMessage: m => got.TrySetResult(m));
        Assert.True((await claimed).IsPrimary);

        using var second = SingleInstance.Claim(dir, TimeSpan.FromSeconds(5), "desk#pieces");

        Assert.False(second.IsPrimary);
        Assert.Equal("desk#pieces", await got.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        release.Set();
        thread.Join();
    }

    [Fact]
    public async Task A_plain_launch_after_a_link_hands_over_nothing()
    {
        var dir = Folder();
        using var release = new ManualResetEventSlim();
        using var got = new System.Collections.Concurrent.BlockingCollection<string>();
        var (thread, claimed) = HoldOnThread(dir, release, onMessage: m => got.Add(m ?? "(none)"));
        Assert.True((await claimed).IsPrimary);

        using (SingleInstance.Claim(dir, TimeSpan.FromSeconds(5), "about#version")) { }
        using (SingleInstance.Claim(dir, TimeSpan.FromSeconds(5))) { }

        Assert.True(got.TryTake(out var first, 2000));
        Assert.Equal("about#version", first);
        Assert.True(got.TryTake(out var second, 2000));
        Assert.Equal("(none)", second);
        release.Set();
        thread.Join();
    }

    [Fact]
    public async Task A_link_too_long_for_the_mailbox_is_dropped_not_cut()
    {
        var dir = Folder();
        using var release = new ManualResetEventSlim();
        var got = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (thread, claimed) = HoldOnThread(dir, release, onMessage: m => got.TrySetResult(m));
        Assert.True((await claimed).IsPrimary);

        using var second = SingleInstance.Claim(dir, TimeSpan.FromSeconds(5), new string('a', SingleInstance.MaxMessageBytes + 1));

        Assert.False(second.IsPrimary);
        Assert.Null(await got.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        release.Set();
        thread.Join();
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SingleInstanceTests`
Expected: build FAILS with `No overload for method 'Claim' takes 3 arguments` (and `Listen` with `Action<string?>`).

- [ ] **Step 3: Add the mailbox fields and constructor parameter**

In `SingleInstance.cs`:

Replace `using System.Security.Cryptography;` with:

```csharp
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
```

In the class summary, after the paragraph ending `that is the\n/// restart after an update.\n/// </para>`, add a paragraph:

```csharp
/// <para>
/// A second launch with <c>--settings &lt;page&gt;[#&lt;anchor&gt;]</c> hands that link over before it asks: it writes
/// the link into a small named mailbox (<c>.Message</c>, shared memory, one writer at a time under <c>.Post</c>) and the
/// first instance reads and clears it when it answers. Best effort: without a mailbox the first just shows its widget.
/// </para>
```

Replace

```csharp
    private readonly EventWaitHandle _ack;
    private RegisteredWaitHandle? _listener;
```

with

```csharp
    private readonly EventWaitHandle _ack;
    private readonly MemoryMappedFile? _mailbox;
    private RegisteredWaitHandle? _listener;

    /// <summary>The mailbox's size: a 32-bit length, then that many UTF-8 bytes.</summary>
    private const int MailboxBytes = 1024;

    /// <summary>The longest link a second launch hands over; a longer one is dropped, never cut.</summary>
    public const int MaxMessageBytes = MailboxBytes - sizeof(int);
```

Replace the constructor

```csharp
    private SingleInstance(Mutex mutex, EventWaitHandle show, EventWaitHandle ack, bool isPrimary)
    {
        _mutex = mutex;
        _show = show;
        _ack = ack;
        IsPrimary = isPrimary;
    }
```

with

```csharp
    private SingleInstance(Mutex mutex, EventWaitHandle show, EventWaitHandle ack, bool isPrimary, MemoryMappedFile? mailbox)
    {
        _mutex = mutex;
        _show = show;
        _ack = ack;
        IsPrimary = isPrimary;
        _mailbox = mailbox;
    }
```

- [ ] **Step 4: Post on claim, read on listen**

Replace the whole `Claim` method (its doc comment through its closing `}`) with:

```csharp
    /// <summary>
    /// Claims the data folder. When another instance holds it, hands it <paramref name="message"/> (a settings link from
    /// <c>--settings</c>, or null), asks it to show itself and waits up to <paramref name="handoff"/>: it answers (this
    /// launch is the duplicate, <see cref="IsPrimary"/> false) or it exits (this launch takes over).
    /// </summary>
    public static SingleInstance Claim(string dataDir, TimeSpan handoff, string? message = null)
    {
        var name = NameFor(dataDir);
        var mutex = new Mutex(initiallyOwned: false, name + ".Lock");
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Show");
        var ack = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Shown");

        bool owns = Take(mutex, TimeSpan.Zero);
        if (!owns)
        {
            // One second launch at a time writes the mailbox, and keeps it until the first instance has answered.
            using var post = new Mutex(initiallyOwned: false, name + ".Post");
            bool posting = Take(post, handoff);
            try
            {
                Post(name, message);
                show.Set();
                try
                {
                    owns = WaitHandle.WaitAny([ack, mutex], handoff) == 1;
                }
                catch (AbandonedMutexException e) when (e.MutexIndex == 1)
                {
                    owns = true; // the first instance died holding it: ours now
                }
                if (owns)
                    show.Reset(); // nobody is left to answer our own request
            }
            finally
            {
                if (posting) post.ReleaseMutex();
            }
        }
        MemoryMappedFile? mailbox = null;
        if (owns)
        {
            mailbox = OpenMailbox(name);
            Clear(mailbox); // what this launch posted to an instance that then quit is not a request to itself
        }
        return new SingleInstance(mutex, show, ack, owns, mailbox);
    }

    private static MemoryMappedFile? OpenMailbox(string name)
    {
        try { return MemoryMappedFile.CreateOrOpen(name + ".Message", MailboxBytes); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>A second launch's write; a first instance without a mailbox simply shows its widget.</summary>
    private static void Post(string name, string? message)
    {
        try
        {
            using var mailbox = MemoryMappedFile.OpenExisting(name + ".Message", MemoryMappedFileRights.ReadWrite);
            Write(mailbox, message);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static void Write(MemoryMappedFile? mailbox, string? message)
    {
        if (mailbox is null)
            return;
        byte[] bytes = message is null ? [] : Encoding.UTF8.GetBytes(message);
        if (bytes.Length > MaxMessageBytes)
            bytes = []; // dropped, never cut: half a link could open some other page
        using var view = mailbox.CreateViewAccessor(0, MailboxBytes);
        view.WriteArray(sizeof(int), bytes, 0, bytes.Length);
        view.Write(0, bytes.Length);
    }

    private static void Clear(MemoryMappedFile? mailbox)
    {
        try { Write(mailbox, null); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>On the primary: the link the asking launch left, cleared as it is read; null when it left none.</summary>
    private string? TakeMessage()
    {
        if (_mailbox is null)
            return null;
        try
        {
            using var view = _mailbox.CreateViewAccessor(0, MailboxBytes);
            int length = view.ReadInt32(0);
            string? message = null;
            if (length > 0 && length <= MaxMessageBytes)
            {
                var bytes = new byte[length];
                view.ReadArray(sizeof(int), bytes, 0, length);
                message = Encoding.UTF8.GetString(bytes);
            }
            view.Write(0, 0);
            return message;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            return null;
        }
    }
```

Replace the `Listen` method (doc comment through closing `}`) with:

```csharp
    /// <summary>
    /// On the primary: runs <paramref name="onShow"/> (on a pool thread) whenever a second launch asks, with the settings
    /// link it handed over, or null for a plain launch.
    /// </summary>
    public void Listen(Action<string?> onShow)
    {
        if (!IsPrimary || _listener is not null)
            return;
        _listener = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) =>
        {
            if (!_accepting)
                return;
            try { onShow(TakeMessage()); }
            catch { /* a failed show still answers, so the duplicate exits */ }
            _ack.Set();
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>On the primary: runs <paramref name="onShow"/> whenever a second launch asks; any link is ignored.</summary>
    public void Listen(Action onShow) => Listen(_ => onShow());
```

In `Dispose`, replace

```csharp
        _show.Dispose();
        _ack.Dispose();
```

with

```csharp
        _show.Dispose();
        _ack.Dispose();
        _mailbox?.Dispose();
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SingleInstanceTests`
Expected: PASS (the three new tests and the five existing ones).

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Services/SingleInstance.cs windows-dotnet/tests/Sanduhr.App.Tests/SingleInstanceTests.cs
git commit -m "feat(settings-v2): a second launch hands its --settings link to the running Sanduhr"
```

---

### Task 8: `NoActivate` for tests; `SettingsWindow` resolves links and knows its place

**Files:**
- Create: `windows-dotnet/tests/Sanduhr.App.Tests/NoActivate.cs`
- Create: `windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs`

**Interfaces:**
- Produces: `SettingsLink? SettingsWindow.Place { get; }` (set on every page change, with the anchor `SelectPage` opened at); `SelectPage` resolves through `SettingsIndex.Resolve` first; test helper `NoActivate.Show(Window)`, `RouteKeyboard()`, `Check()`.
- Consumes: `SettingsIndex.Resolve` (Task 1).

- [ ] **Step 1: Write the foreground guard for tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/NoActivate.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Sanduhr.App.Tests;

/// <summary>
/// The render harness's foreground rule for tests (SettingsPassScenario: NeverActivate, RouteKeyboard, GuardForeground).
/// A window shown through it is off screen and can never become the foreground window; <see cref="Check"/> hands the
/// foreground back and fails the test if it ever did.
/// </summary>
internal sealed class NoActivate
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000L, WS_EX_TOOLWINDOW = 0x80L;

    private readonly IntPtr _before = GetForegroundWindow();
    private readonly IntPtr _hwnd;

    private NoActivate(IntPtr hwnd) => _hwnd = hwnd;

    /// <summary>Off screen, never activating: the extended styles go on before the first Show.</summary>
    public static NoActivate Show(Window window)
    {
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)(ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        var guard = new NoActivate(hwnd);
        window.Show();
        guard.Check();
        return guard;
    }

    /// <summary>Lets WPF route keyboard focus inside the window: WM_ACTIVATE and WM_SETFOCUS sent to it, never SetForegroundWindow.</summary>
    public void RouteKeyboard()
    {
        const int WM_ACTIVATE = 0x0006, WM_SETFOCUS = 0x0007;
        SendMessage(_hwnd, WM_ACTIVATE, (IntPtr)1, IntPtr.Zero);
        SendMessage(_hwnd, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
        Check();
    }

    /// <summary>Fails the test, after handing the foreground back, if the window became the foreground window.</summary>
    public void Check()
    {
        if (GetForegroundWindow() != _hwnd)
            return;
        SetForegroundWindow(_before);
        Assert.Fail("The test window became the foreground window; the foreground was handed back.");
    }
}
```

- [ ] **Step 2: Write the failing test**

Create `windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs`:

```csharp
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 2 ("reach"), on a real SettingsWindow over scratch folders and an in-memory account registry:
/// where links land, what the window remembers, and the keyboard and short-window fixes. Every window is shown through
/// <see cref="NoActivate"/>, so no test takes the foreground.
/// </summary>
[Collection("SettingsWindow")]
public sealed class SettingsReachTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Svm, NoActivate Guard) Open(params string[] accounts)
    {
        var (widget, _) = ScratchWidget(accounts);
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        return (window, svm, NoActivate.Show(window));
    }

    private static TabItem TabOf(SettingsWindow window, SettingsPage page) =>
        ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == page.ToString());

    /// <summary>Opens <paramref name="page"/> at <paramref name="anchor"/>, lets layout and the landing run, and returns the marked element.</summary>
    private static FrameworkElement Land(SettingsWindow window, NoActivate guard, SettingsPage page, string anchor)
    {
        window.SelectPage(page, anchor);
        window.UpdateLayout();
        Pump();
        guard.Check();
        return SettingsAnchor.Find((DependencyObject)TabOf(window, page).Content, anchor)
            ?? throw new Xunit.Sdk.XunitException($"{page}/{anchor} is not marked");
    }

    [Fact]
    public void The_window_remembers_the_place_it_last_opened() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            Land(window, guard, SettingsPage.Desk, SettingsIndex.DeskPieces);
            Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskPieces), window.Place);

            // A sidebar click is a page with no section.
            var tabs = (TabControl)window.FindName("Tabs");
            tabs.SelectedItem = TabOf(window, SettingsPage.Weather);
            Assert.Equal(new SettingsLink(SettingsPage.Weather, null), window.Place);
        }
        finally { window.Close(); }
    });
}
```

- [ ] **Step 3: Run it to see it fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SettingsReachTests`
Expected: build FAILS with `'SettingsWindow' does not contain a definition for 'Place'`.

- [ ] **Step 4: Implement `Place` and the resolve in `SelectPage`**

In `SettingsWindow.xaml.cs`:

After `    private readonly DispatcherTimer _localCcTimer;` add:

```csharp

    /// <summary>
    /// Where the window is now: the page, and the anchor <see cref="SelectPage"/> last opened it at (null after a sidebar
    /// click). App keeps it when the window closes, to reopen there this session (spec, Reopen where it was left).
    /// </summary>
    public SettingsLink? Place { get; private set; }
```

In `Tabs_SelectionChanged`, replace

```csharp
        var page = PageOf(Tabs.SelectedItem as TabItem);
        ViewModel.ClaudeCode.IsTabActive = page == SettingsPage.ClaudeUsage;
```

with

```csharp
        var page = PageOf(Tabs.SelectedItem as TabItem);
        if (page is { } shown)
            Place = new SettingsLink(shown, null);
        ViewModel.ClaudeCode.IsTabActive = page == SettingsPage.ClaudeUsage;
```

In `SelectPage`, replace

```csharp
    public void SelectPage(SettingsPage page, string? anchor = null)
    {
        var tab = Tabs.Items.OfType<TabItem>().FirstOrDefault(t => PageOf(t) == page);
```

with

```csharp
    public void SelectPage(SettingsPage page, string? anchor = null)
    {
        // An old page or anchor (a card, a menu or agent text from before 4.0) lands where it lives now.
        (page, anchor) = SettingsIndex.Resolve(page, anchor);
        var tab = Tabs.Items.OfType<TabItem>().FirstOrDefault(t => PageOf(t) == page);
```

and replace

```csharp
        Tabs.SelectedItem = tab;
        if (entry is null)
            return;
```

with

```csharp
        Tabs.SelectedItem = tab;
        Place = new SettingsLink(page, entry?.Anchor);
        if (entry is null)
            return;
```

- [ ] **Step 5: Run the test to see it pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SettingsReachTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/tests/Sanduhr.App.Tests/NoActivate.cs windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs
git commit -m "feat(settings-v2): SettingsWindow resolves old links and records its place"
```

---

### Task 9: Collapsed sections: the "(in Advanced)" suffix and opening the section

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsSearchViewModel.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs`

**Interfaces:**
- Produces: `SettingsSearchRow(string Title, SettingsPage Page, string? Anchor, bool IsPage, string? CollapsedIn = null)` with `DisplayTitle`; `internal static bool SettingsWindow.RevealInside(DependencyObject target)`.
- Consumes: `SettingsEntry.CollapsedIn` (Task 4).

Inert until slice 3 sets `CollapsedIn` on Designs and Advanced entries and wraps those sections in Expanders.

- [ ] **Step 1: Write the failing tests**

Append inside `SettingsReachTests`:

```csharp
    [Fact]
    public void A_search_hit_in_a_collapsed_section_says_so()
    {
        var row = new SettingsSearchRow("Margins", SettingsPage.Desk, SettingsIndex.DeskMargins, IsPage: false, CollapsedIn: "Advanced");
        Assert.Equal("Margins (in Advanced)", row.DisplayTitle);
        Assert.Equal("Margins (in Advanced), on Desk", row.AutomationName);
        Assert.Equal("Margins", new SettingsSearchRow("Margins", SettingsPage.Desk, SettingsIndex.DeskMargins, false).DisplayTitle);
    }

    [Fact]
    public void Opening_a_hit_inside_a_collapsed_section_unfolds_it() => AppHost.Run(() =>
    {
        var target = new CheckBox();
        var inner = new StackPanel();
        inner.Children.Add(target);
        var expander = new Expander { IsExpanded = false, Content = inner };
        var page = new StackPanel();
        page.Children.Add(expander);

        Assert.True(SettingsWindow.RevealInside(target));
        Assert.True(expander.IsExpanded);
        Assert.False(SettingsWindow.RevealInside(target)); // already open: nothing to do
    });
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SettingsReachTests`
Expected: build FAILS with `'SettingsSearchRow' does not contain a definition for 'DisplayTitle'`.

- [ ] **Step 3: The search row**

In `SettingsSearchViewModel.cs`, replace

```csharp
/// <summary>One row of the sidebar's search results: a page, or a section or control under it.</summary>
public sealed record SettingsSearchRow(string Title, SettingsPage Page, string? Anchor, bool IsPage)
{
    /// <summary>What a screen reader says: the page alone, or "Tray shows, on General".</summary>
    public string AutomationName => IsPage ? $"{Title} page" : $"{Title}, on {SettingsNames.PageTitle(Page)}";
}
```

with

```csharp
/// <summary>
/// One row of the sidebar's search results: a page, or a section or control under it. <paramref name="CollapsedIn"/>
/// names the collapsed section it sits in, so the row says "(in Advanced)" and opening it unfolds the section.
/// </summary>
public sealed record SettingsSearchRow(string Title, SettingsPage Page, string? Anchor, bool IsPage, string? CollapsedIn = null)
{
    /// <summary>The row's words: the title, with "(in Advanced)" when it sits in a collapsed section.</summary>
    public string DisplayTitle => CollapsedIn is null ? Title : $"{Title} (in {CollapsedIn})";

    /// <summary>What a screen reader says: the page alone, or "Tray shows, on General".</summary>
    public string AutomationName => IsPage ? $"{Title} page" : $"{DisplayTitle}, on {SettingsNames.PageTitle(Page)}";
}
```

and replace

```csharp
                var row = new SettingsSearchRow(e.Title, page, e.Anchor, IsPage: false);
```

with

```csharp
                var row = new SettingsSearchRow(e.Title, page, e.Anchor, IsPage: false, e.CollapsedIn);
```

In `SettingsWindow.xaml`, replace

```xml
                                                        <TextBlock x:Name="t" Text="{Binding Title}" FontSize="12" TextWrapping="Wrap"
```

with

```xml
                                                        <TextBlock x:Name="t" Text="{Binding DisplayTitle}" FontSize="12" TextWrapping="Wrap"
```

- [ ] **Step 4: Unfold on landing**

In `SettingsWindow.xaml.cs`, in `SelectPage`'s dispatched landing, replace

```csharp
            if (tab.Content is not DependencyObject content || SettingsAnchor.Find(content, entry.Anchor!) is not { } target
                || !target.IsVisible)
            {
                tab.Focus();   // never leave keyboard focus nowhere
                return;
            }
```

with

```csharp
            if (tab.Content is not DependencyObject content || SettingsAnchor.Find(content, entry.Anchor!) is not { } target)
            {
                tab.Focus();   // never leave keyboard focus nowhere
                return;
            }
            // A section inside a collapsed group (a search hit "(in Advanced)") opens its group first.
            if (RevealInside(target))
                target.UpdateLayout();
            if (!target.IsVisible)
            {
                tab.Focus();
                return;
            }
```

and after the `IsInside` method add:

```csharp

    /// <summary>
    /// Opens every collapsed Expander between <paramref name="target"/> and the page, so a section inside one can be
    /// shown and focused. True when one was opened.
    /// </summary>
    internal static bool RevealInside(DependencyObject target)
    {
        bool opened = false;
        for (var d = target; d is not null; d = LogicalTreeHelper.GetParent(d) ?? (d is Visual ? VisualTreeHelper.GetParent(d) : null))
        {
            if (d is Expander { IsExpanded: false } expander)
            {
                expander.IsExpanded = true;
                opened = true;
            }
        }
        return opened;
    }
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SettingsReachTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/SettingsSearchViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs
git commit -m "feat(settings-v2): search rows say (in Advanced) and landing unfolds the section"
```

---

### Task 10: `SettingsWindow.FitToWorkArea`

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs`
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs`

**Interfaces:**
- Produces: `internal void SettingsWindow.FitToWorkArea(Window? near)`, called by App before the first `Show` (Task 11). It never runs from the window itself, so test windows keep their off-screen position.
- Consumes: `WindowFit` (Task 6); `Interop.GlowNative.MonitorFromWindow`, `GetMonitorInfoW`, `MONITORINFO`.

- [ ] **Step 1: Write the failing test**

Append inside `SettingsReachTests`:

```csharp
    [Fact]
    public void Settings_opens_inside_the_work_area() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var window = new SettingsWindow(new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask));
        try
        {
            window.FitToWorkArea(null); // no owner: the primary monitor's work area; the window is never shown
            var work = SystemParameters.WorkArea;
            Assert.Equal(WindowStartupLocation.Manual, window.WindowStartupLocation);
            Assert.InRange(window.Height, window.MinHeight, Math.Max(window.MinHeight, work.Height));
            Assert.InRange(window.Width, window.MinWidth, Math.Max(window.MinWidth, 780));
            Assert.True(window.Top >= work.Top - 0.5, $"top {window.Top} is above the work area's {work.Top}");
            Assert.True(window.Height == window.MinHeight || window.Top + window.Height <= work.Bottom + 0.5,
                $"the foot at {window.Top + window.Height} is below the work area's {work.Bottom}");
        }
        finally { window.Close(); }
    });
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SettingsReachTests`
Expected: build FAILS with `'SettingsWindow' does not contain a definition for 'FitToWorkArea'`.

- [ ] **Step 3: Implement**

In `SettingsWindow.xaml.cs`, add `using System.Runtime.InteropServices;` after `using System.Windows.Threading;`, and after `OnSourceInitialized` add:

```csharp

    /// <summary>
    /// Before the first Show: sizes and places the window inside the work area of the monitor <paramref name="near"/> is
    /// on (the primary monitor without one), at most its designed 780 x 800 and at least MinWidth x MinHeight, centred,
    /// so its foot and Quit never sit off screen (spec, "Short windows"; WF41).
    /// </summary>
    internal void FitToWorkArea(Window? near)
    {
        var fit = WindowFit.Fit(WorkAreaNear(near), Width, Height, MinWidth, MinHeight);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = fit.Width;
        Height = fit.Height;
        Left = fit.Left;
        Top = fit.Top;
    }

    /// <summary>The work area, in DIPs, of the monitor <paramref name="near"/> is on; the primary's when it has no handle.</summary>
    private static WindowFit.Area WorkAreaNear(Window? near)
    {
        var primary = SystemParameters.WorkArea;
        var fallback = new WindowFit.Area(primary.Left, primary.Top, primary.Width, primary.Height);
        var hwnd = near is null ? IntPtr.Zero : new System.Windows.Interop.WindowInteropHelper(near).Handle;
        if (hwnd == IntPtr.Zero)
            return fallback;
        const uint MONITOR_DEFAULTTONEAREST = 2;
        var monitor = GlowNative.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new GlowNative.MONITORINFO { cbSize = Marshal.SizeOf<GlowNative.MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GlowNative.GetMonitorInfoW(monitor, ref info))
            return fallback;
        // The process is system-DPI aware: the rectangle is in system-scaled pixels, and the owner draws at that scale.
        double scale = VisualTreeHelper.GetDpi(near!).DpiScaleX;
        var r = info.rcWork;
        return new WindowFit.Area(r.Left / scale, r.Top / scale, (r.Right - r.Left) / scale, (r.Bottom - r.Top) / scale);
    }
```

- [ ] **Step 4: Run the test to see it pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SettingsReachTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs
git commit -m "feat(settings-v2): Settings fits the work area of the widget's monitor"
```

---

### Task 11: App wiring: Show me, menus, reopen, `--settings`

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Views/WhatsNewWindow.xaml.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/MainWindow.xaml.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Program.cs`
- Modify: `windows-dotnet/tests/Sanduhr.App.Tests/AppWindowNamesTests.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/SettingsEntryPointScanTests.cs` (create)

**Interfaces:**
- Produces: `WhatsNewWindow.Show(..., Action<SettingsPage, string?> showMe)` and `ShowTour(model, Action<SettingsPage, string?> showMe, Action<bool> ended)`; App's `ShowMe`, `OnSecondLaunch`, `OpenSettingsLink`, `_lastSettings`.
- Consumes: `WhatsNewCard.Anchor` (Task 5), `SettingsIndex.MenuAnchor`, `Landing`, `ParseLink`, `LinkArgument`, `LinkFromArgs` (Tasks 2, 3), `SingleInstance.Listen(Action<string?>)` and `Claim(..., message)` (Task 7), `SettingsWindow.Place` (Task 8), `FitToWorkArea` (Task 10).

`App` cannot run in a test (it owns the tray, the Desk and the fetch), so this task is pinned by the Core tests behind it and by source scans, in the style of `SettingsSourceScanTests`.

- [ ] **Step 1: Write the failing scan tests**

Create `windows-dotnet/tests/Sanduhr.Tests/SettingsEntryPointScanTests.cs`:

```csharp
using System.Text.RegularExpressions;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Settings v2, slice 2: every entry point into Settings passes a page and an anchor (spec, "One landing rule"), the
/// --settings switch reaches the running Sanduhr, and Settings opens fitted and where it was left. App cannot run in a
/// test, so its wiring is read from source, like <c>SettingsSourceScanTests</c>.
/// </summary>
public class SettingsEntryPointScanTests
{
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Sanduhr.App");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([AppDir(), .. parts]));

    [Fact]
    public void Every_App_call_that_names_a_page_also_names_an_anchor()
    {
        var app = Read("App.xaml.cs");
        var pageOnly = Regex.Matches(app, @"ShowSettingsAsync\([^,()]+\)").Select(m => m.Value).ToList();
        Assert.True(pageOnly.Count == 0, "page without an anchor: " + string.Join(", ", pageOnly));
        Assert.Contains("SettingsIndex.MenuAnchor(page)", app);
        Assert.Contains("SettingsIndex.MenuAnchor(SettingsPage.Desk)", app);
        Assert.Contains("SettingsIndex.MenuAnchor(SettingsPage.DeskMessage)", app);
    }

    [Fact]
    public void Show_me_passes_the_cards_anchor()
    {
        var window = Read("Views", "WhatsNewWindow.xaml.cs");
        Assert.Contains("Action<SettingsPage, string?> showMe", window);
        Assert.Contains("callback(destination, anchor);", window);
        Assert.Contains("ShowMe, EndTour", Read("App.xaml.cs"));
    }

    [Fact]
    public void Both_account_menus_offer_Accounts_settings()
    {
        foreach (var file in new[] { "App.xaml.cs", "MainWindow.xaml.cs" })
        {
            var text = Read(file);
            Assert.Contains("PopulateSwitchList(", text);
            Assert.DoesNotContain("includeManage: false", text);
        }
    }

    [Fact]
    public void The_settings_switch_reaches_the_running_Sanduhr_and_a_first_launch()
    {
        Assert.Contains("SettingsIndex.LinkArgument(args)", Read("Program.cs"));
        var app = Read("App.xaml.cs");
        Assert.Contains("SettingsIndex.LinkFromArgs(", app);
        Assert.Contains("Instance?.Listen(message =>", app);
    }

    [Fact]
    public void Settings_opens_fitted_and_where_it_was_left()
    {
        var app = Read("App.xaml.cs");
        int fit = app.IndexOf("_settingsWindow.FitToWorkArea(_window);", StringComparison.Ordinal);
        int show = app.IndexOf("_settingsWindow.Show();", StringComparison.Ordinal);
        Assert.True(fit >= 0 && show > fit, "FitToWorkArea must run before the first Show");
        Assert.Contains("SettingsIndex.Landing(page, anchor, _lastSettings)", app);
        Assert.Contains("_lastSettings = opened.Place;", app);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsEntryPointScanTests`
Expected: FAIL (every test: `ShowSettingsAsync(SettingsPage.DeskMessage)` is found, the strings are missing, `includeManage: false` is present).

- [ ] **Step 3: Show me carries the anchor**

In `WhatsNewWindow.xaml.cs`:

- `    private Action<SettingsPage> _showMe;` becomes `    private Action<SettingsPage, string?> _showMe;`
- `    private WhatsNewWindow(Action<SettingsPage> showMe)` becomes `    private WhatsNewWindow(Action<SettingsPage, string?> showMe)`
- `    public static void ShowTour(WelcomeTourViewModel model, Action<SettingsPage> showMe, Action<bool> ended)` becomes `    public static void ShowTour(WelcomeTourViewModel model, Action<SettingsPage, string?> showMe, Action<bool> ended)`
- Replace

```csharp
    /// <paramref name="showMe"/> runs after the window closes, with the card's destination.</summary>
    public static void Show(IReadOnlyList<WhatsNewCard> cards, string header, SettingsStore store,
        Action<SettingsPage> showMe)
```

with

```csharp
    /// <paramref name="showMe"/> runs after the window closes, with the card's destination and anchor.</summary>
    public static void Show(IReadOnlyList<WhatsNewCard> cards, string header, SettingsStore store,
        Action<SettingsPage, string?> showMe)
```

- In `ShowMe_Click`, replace

```csharp
        var destination = item.Card.Destination;
        var callback = _showMe;
```

with

```csharp
        var destination = item.Card.Destination;
        var anchor = item.Card.Anchor;
        var callback = _showMe;
```

and `        callback(destination);` with `        callback(destination, anchor);`.

In `AppWindowNamesTests.cs`, replace `WhatsNewWindow.Show(cards, "What's new in 3.7.1", store, _ => { });` with `WhatsNewWindow.Show(cards, "What's new in 3.7.1", store, (_, _) => { });` and `WhatsNewWindow.ShowTour(tour, _ => { }, _ => { });` with `WhatsNewWindow.ShowTour(tour, (_, _) => { }, _ => { });`.

- [ ] **Step 4: App: Show me, menus and the account menu**

In `App.xaml.cs`:

Replace

```csharp
    private void OpenWhatsNew(IReadOnlyList<WhatsNewCard> cards, string header) =>
        WhatsNewWindow.Show(cards, header, new SettingsStore(new Sanduhr.Core.Paths()), page =>
        {
            if (page == SettingsPage.Widget)
                ShowWindow();
            else
                _ = ShowSettingsAsync(page);
        });
```

with

```csharp
    private void OpenWhatsNew(IReadOnlyList<WhatsNewCard> cards, string header) =>
        WhatsNewWindow.Show(cards, header, new SettingsStore(new Sanduhr.Core.Paths()), ShowMe);

    /// <summary>A card's Show me: the widget, or Settings at the card's page and section (WF17).</summary>
    private void ShowMe(SettingsPage page, string? anchor)
    {
        if (page == SettingsPage.Widget)
            ShowWindow();
        else
            _ = ShowSettingsAsync(page, anchor);
    }
```

Replace

```csharp
            WhatsNewWindow.ShowTour(new WelcomeTourViewModel(steps, _vm), page =>
            {
                if (page == SettingsPage.Widget)
                    ShowWindow();
                else
                    _ = ShowSettingsAsync(page);
            }, EndTour);
```

with

```csharp
            WhatsNewWindow.ShowTour(new WelcomeTourViewModel(steps, _vm), ShowMe, EndTour);
```

Replace `        _vm.SettingsPageRequested += page => ShowSettingsAsync(page);` with

```csharp
        // "<Page> settings…" from the tier card and the account menus lands on its section (spec, Menus table).
        _vm.SettingsPageRequested += page => ShowSettingsAsync(page, Sanduhr.Core.SettingsIndex.MenuAnchor(page));
```

Replace

```csharp
            case Desk.DeskMenuAction.EditMessages: _ = ShowSettingsAsync(SettingsPage.DeskMessage); break;
            case Desk.DeskMenuAction.DeskSettings: _ = ShowSettingsAsync(SettingsPage.Desk); break;
```

with

```csharp
            case Desk.DeskMenuAction.EditMessages: _ = ShowSettingsAsync(SettingsPage.DeskMessage, Sanduhr.Core.SettingsIndex.MenuAnchor(SettingsPage.DeskMessage)); break;
            case Desk.DeskMenuAction.DeskSettings: _ = ShowSettingsAsync(SettingsPage.Desk, Sanduhr.Core.SettingsIndex.MenuAnchor(SettingsPage.Desk)); break;
```

Replace `                AccountMenuBuilder.PopulateSwitchList(accounts.Items, _vm, includeAdd: false, includeManage: false);` with `                AccountMenuBuilder.PopulateSwitchList(accounts.Items, _vm, includeAdd: false, includeManage: true);`.

In `MainWindow.xaml.cs`, replace `                AccountMenuBuilder.PopulateSwitchList(accounts.Items, Vm, includeAdd: true, includeManage: false);` with `                AccountMenuBuilder.PopulateSwitchList(accounts.Items, Vm, includeAdd: true, includeManage: true);`.

- [ ] **Step 5: App: reopen where left, fit, second launch and start-up link**

In `App.xaml.cs`:

After `    private bool _quitting;` add:

```csharp

    /// <summary>Where Settings was when it last closed, for the app's life (spec, Reopen where it was left). Never saved.</summary>
    private SettingsLink? _lastSettings;
```

Replace `        Instance?.Listen(() => Dispatcher.BeginInvoke(() => { if (!_quitting) ShowWindow(); }));` with

```csharp
        Instance?.Listen(message => Dispatcher.BeginInvoke(() => { if (!_quitting) OnSecondLaunch(message); }));
```

Replace

```csharp
        ScheduleWhatsNewAtLaunch();
    }

    /// <summary>The app version as Major.Minor.Build, the same shape Settings shows.</summary>
```

with

```csharp
        ScheduleWhatsNewAtLaunch();

        // `Sanduhr.exe --settings <page>[#<anchor>]` as the first launch: Settings opens there once the widget is up.
        if (Sanduhr.Core.SettingsIndex.LinkFromArgs(Environment.GetCommandLineArgs().Skip(1)) is { } startLink)
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => OpenSettingsLink(startLink)));
    }

    /// <summary>The app version as Major.Minor.Build, the same shape Settings shows.</summary>
```

Replace the head of `ShowSettingsAsync`

```csharp
    /// <summary>
    /// Open the Settings window (on General, or on <paramref name="page"/>) — single-instance and
    /// non-modal so a live account switch from here reflects in the widget at once.
    /// Re-invoking focuses the existing window and refreshes its account list.
    /// </summary>
    private Task ShowSettingsAsync(SettingsPage? page = null, string? anchor = null)
```

with

```csharp
    /// <summary>
    /// A second launch asked: <c>--settings desk#pieces</c> opens Settings there; a plain launch shows the widget.
    /// </summary>
    private void OnSecondLaunch(string? message)
    {
        if (Sanduhr.Core.SettingsIndex.ParseLink(message) is { } link)
            OpenSettingsLink(link);
        else
            ShowWindow();
    }

    /// <summary>A link from outside the app: the widget, or Settings at that page and section.</summary>
    private void OpenSettingsLink(SettingsLink link)
    {
        if (link.Page == SettingsPage.Widget)
            ShowWindow();
        else
            _ = ShowSettingsAsync(link.Page, link.Anchor);
    }

    /// <summary>
    /// Open the Settings window at <paramref name="page"/> and <paramref name="anchor"/>, or, with no page, where it was
    /// left this session (General the first time); single-instance and non-modal so a live account switch from here
    /// reflects in the widget at once. Re-invoking focuses the existing window, refreshes its account list and, with a
    /// page, moves it there.
    /// </summary>
    private Task ShowSettingsAsync(SettingsPage? page = null, string? anchor = null)
```

Replace

```csharp
        var svm = new SettingsViewModel(_vm, () => RunSignInAsync(embedded: true), RunUpdateSignInAsync, _hotKeys,
```

with

```csharp
        var landing = Sanduhr.Core.SettingsIndex.Landing(page, anchor, _lastSettings);
        var svm = new SettingsViewModel(_vm, () => RunSignInAsync(embedded: true), RunUpdateSignInAsync, _hotKeys,
```

Replace

```csharp
        if (_window is { IsLoaded: true })
            _settingsWindow.Owner = _window;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        if (page is { } first)
            _settingsWindow.SelectPage(first, anchor);
        _settingsWindow.Activate();
        return Task.CompletedTask;
```

with

```csharp
        if (_window is { IsLoaded: true })
            _settingsWindow.Owner = _window;
        // On the widget's monitor, inside its work area, so the foot and Quit never sit off screen (WF41).
        _settingsWindow.FitToWorkArea(_window);
        var opened = _settingsWindow;
        _settingsWindow.Closed += (_, _) =>
        {
            _lastSettings = opened.Place; // reopen here this session (spec, Reopen where it was left)
            _settingsWindow = null;
        };
        _settingsWindow.Show();
        _settingsWindow.SelectPage(landing.Page, landing.Anchor);
        _settingsWindow.Activate();
        return Task.CompletedTask;
```

- [ ] **Step 6: Program passes the switch**

In `Program.cs`, replace

```csharp
        // One Sanduhr per data folder: a second launch shows the running widget and exits. A launch that
        // meets an instance mid-quit waits for it (up to the handoff) and then starts as usual.
        using var instance = Services.SingleInstance.Claim(new Sanduhr.Core.Paths().AppDataDir, TimeSpan.FromSeconds(15));
```

with

```csharp
        // One Sanduhr per data folder: a second launch shows the running widget, or opens its Settings where
        // `--settings <page>[#<anchor>]` says, and exits. A launch that meets an instance mid-quit waits for it
        // (up to the handoff) and then starts as usual.
        using var instance = Services.SingleInstance.Claim(new Sanduhr.Core.Paths().AppDataDir, TimeSpan.FromSeconds(15),
            Sanduhr.Core.SettingsIndex.LinkArgument(args));
```

- [ ] **Step 7: Build and run the tests**

Run: `dotnet build src/Sanduhr.App`
Expected: Build succeeded, 0 errors.

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsEntryPointScanTests`
Expected: PASS.

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~AppWindowNamesTests|FullyQualifiedName~SingleInstanceTests"`
Expected: PASS (desktop-only cases skip on CI).

- [ ] **Step 8: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Views/WhatsNewWindow.xaml.cs windows-dotnet/src/Sanduhr.App/App.xaml.cs windows-dotnet/src/Sanduhr.App/MainWindow.xaml.cs windows-dotnet/src/Sanduhr.App/Program.cs windows-dotnet/tests/Sanduhr.App.Tests/AppWindowNamesTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsEntryPointScanTests.cs
git commit -m "feat(settings-v2): Show me, menus and --settings land on sections; Settings reopens where it was left"
```

---

### Task 12: Quit from Settings asks first

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs`
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs`

**Interfaces:**
- Produces: `const string SettingsViewModel.QuitQuestion = "Quit Sanduhr? The widget and the Desk close."`; `internal Func<bool>? SettingsViewModel.ConfirmQuitHook`; `void SettingsViewModel.AttachOwner(Window)`. Buttons "Quit" and "Cancel". The tray's Quit stays immediate.

- [ ] **Step 1: Write the failing test**

Append inside `SettingsReachTests`:

```csharp
    [Fact]
    public void Quit_from_Settings_asks_first_and_Cancel_keeps_Sanduhr_running() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        int quits = 0, asked = 0;
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask)
        {
            QuitRequested = () => quits++,
            ConfirmQuitHook = () => { asked++; return false; },
        };

        svm.QuitCommand.Execute(null);
        Assert.Equal((1, 0), (asked, quits));

        svm.ConfirmQuitHook = () => { asked++; return true; };
        svm.QuitCommand.Execute(null);
        Assert.Equal((2, 1), (asked, quits));
        Assert.Equal("Quit Sanduhr? The widget and the Desk close.", SettingsViewModel.QuitQuestion);
    });
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SettingsReachTests`
Expected: build FAILS with `'SettingsViewModel' does not contain a definition for 'ConfirmQuitHook'`.

- [ ] **Step 3: Implement**

In `SettingsViewModel.cs`, replace

```csharp
    /// <summary>The sidebar foot's "Quit Sanduhr": runs the tray's quit path.</summary>
    public ICommand QuitCommand { get; }

    /// <summary>Connected by the orchestrator: the same quit the tray's Quit runs. Null does nothing.</summary>
    public Action? QuitRequested { get; set; }
```

with

```csharp
    /// <summary>The sidebar foot's "Quit Sanduhr": asks first (WF33), then runs the tray's quit path.</summary>
    public ICommand QuitCommand { get; }

    /// <summary>Connected by the orchestrator: the same quit the tray's Quit runs. Null does nothing.</summary>
    public Action? QuitRequested { get; set; }

    /// <summary>What the sidebar's Quit asks first (spec, General). The tray's Quit stays immediate: using the tray is an explicit act.</summary>
    public const string QuitQuestion = "Quit Sanduhr? The widget and the Desk close.";

    /// <summary>Tests answer the Quit question here instead of showing it.</summary>
    internal Func<bool>? ConfirmQuitHook { get; set; }

    private System.Windows.Window? _owner;

    /// <summary>The Quit question parents to the Settings window.</summary>
    public void AttachOwner(System.Windows.Window owner) => _owner = owner;

    private bool ConfirmQuit() => ConfirmQuitHook is { } hook
        ? hook()
        : Sanduhr.App.Views.ThemedDialog.Show(_owner, SettingsNames.QuitSanduhr, QuitQuestion,
              System.Windows.MessageBoxButton.YesNo, Sanduhr.App.Views.ThemedDialogKind.Warning,
              primaryLabel: "Quit", secondaryLabel: "Cancel") == System.Windows.MessageBoxResult.Yes;
```

Replace `        QuitCommand = new RelayCommand(() => QuitRequested?.Invoke());` with

```csharp
        QuitCommand = new RelayCommand(() =>
        {
            if (ConfirmQuit())
                QuitRequested?.Invoke();
        });
```

In `SettingsWindow.xaml.cs`, replace `        ViewModel.Accounts.AttachOwner(this);` with

```csharp
        ViewModel.AttachOwner(this);
        ViewModel.Accounts.AttachOwner(this);
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SettingsReachTests`
Expected: PASS.

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsSourceScanTests`
Expected: PASS (`Quit_sits_at_the_sidebar_foot_named_and_wired_to_the_quit_command` unchanged).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs
git commit -m "feat(settings-v2): Quit from Settings asks first; the tray's Quit stays immediate"
```

---

### Task 13: Tab order: Quit after the page list, the page wraps to search

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`
- Test: `windows-dotnet/tests/Sanduhr.Tests/SettingsEntryPointScanTests.cs`
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs`

**Interfaces:**
- Produces: inside the sidebar template, one local tab group ordered search box (0), page list (1), Quit (2), page (3); the title-bar close button is not a tab stop (set in its style, so slice 1's name on the element doesn't collide).

- [ ] **Step 1: Write the failing tests**

Append inside `SettingsEntryPointScanTests`:

```csharp
    [Fact]
    public void Quit_comes_after_the_page_list_and_the_close_button_is_not_a_tab_stop()
    {
        var xaml = Read("Views", "SettingsWindow.xaml");
        Assert.Matches(@"<Grid DockPanel\.Dock=""Top""[^>]*KeyboardNavigation\.TabIndex=""0""", xaml);
        Assert.Matches(@"<Grid KeyboardNavigation\.TabIndex=""1""", xaml);
        Assert.Matches(@"x:Name=""QuitButton""[^>]*KeyboardNavigation\.TabIndex=""2""", xaml);
        Assert.Matches(@"x:Name=""PART_SelectedContentHost""[^>]*KeyboardNavigation\.TabIndex=""3""", xaml);
        var close = Regex.Match(xaml, @"x:Key=""CloseButton""[\s\S]*?</Style>");
        Assert.True(close.Success, "no CloseButton style");
        Assert.Contains(@"<Setter Property=""IsTabStop"" Value=""False"" />", close.Value);
    }
```

Append inside `SettingsReachTests`:

```csharp
    private static Control? FirstTabStop(DependencyObject root)
    {
        if (root is Control { Focusable: true, IsTabStop: true, IsVisible: true, IsEnabled: true } control)
            return control;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FirstTabStop(VisualTreeHelper.GetChild(root, i)) is { } found)
                return found;
        return null;
    }

    private static bool Inside(object? element, DependencyObject root)
    {
        for (var d = element as DependencyObject; d is not null; d = (d is Visual ? VisualTreeHelper.GetParent(d) : null) ?? LogicalTreeHelper.GetParent(d))
            if (ReferenceEquals(d, root))
                return true;
        return false;
    }

    private static void Next(IInputElement element)
    {
        var request = new TraversalRequest(FocusNavigationDirection.Next);
        if (element is UIElement ui) ui.MoveFocus(request);
        else if (element is ContentElement content) content.MoveFocus(request);
    }

    /// <summary>WF33: one Tab past a page's last control used to land on Quit. Keyboard focus is routed inside the
    /// never-activating window, as the harness does; on GitHub's runner this is skipped.</summary>
    [DesktopFact]
    public void Tab_from_the_end_of_every_page_goes_to_the_search_box_never_to_Quit() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        var bad = new List<string>();
        try
        {
            guard.RouteKeyboard();
            var tabs = (TabControl)window.FindName("Tabs");
            var host = (ContentPresenter)tabs.Template.FindName("PART_SelectedContentHost", tabs);
            var search = (TextBox)tabs.Template.FindName("SearchBox", tabs);
            var quit = (Button)tabs.Template.FindName("QuitButton", tabs);
            foreach (var item in tabs.Items.OfType<TabItem>())
            {
                tabs.SelectedItem = item;
                window.UpdateLayout();
                Pump();
                if (FirstTabStop(host) is not { } first)
                    continue;
                Keyboard.Focus(first);
                guard.Check();
                Assert.True(ReferenceEquals(Keyboard.FocusedElement, first), $"keyboard focus could not be routed to [{item.Tag}]");
                IInputElement? at = first;
                for (int step = 0; step < 400 && at is not null && Inside(at, host); step++)
                {
                    Next(at);
                    Pump();
                    guard.Check();
                    at = Keyboard.FocusedElement;
                }
                if (ReferenceEquals(at, quit))
                    bad.Add($"[{item.Tag}] Tab from the page reached Quit");
                else if (!ReferenceEquals(at, search))
                    bad.Add($"[{item.Tag}] Tab from the page reached {at?.GetType().Name ?? "nothing"}, not the search box");
            }
        }
        finally { window.Close(); }
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
    });
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsEntryPointScanTests`
Expected: FAIL in `Quit_comes_after_the_page_list_and_the_close_button_is_not_a_tab_stop` (the list grid has `TabIndex="0"`, Quit `"1"`).

Run (desktop session): `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SettingsReachTests.Tab_from_the_end`
Expected: FAIL listing pages whose Tab reached Quit.

- [ ] **Step 3: Reorder the sidebar template**

In `SettingsWindow.xaml`:

In the `CloseButton` style, replace

```xml
            <Setter Property="VerticalAlignment" Value="Stretch" />
            <Setter Property="shell:WindowChrome.IsHitTestVisibleInChrome" Value="True" />
```

with

```xml
            <Setter Property="VerticalAlignment" Value="Stretch" />
            <Setter Property="shell:WindowChrome.IsHitTestVisibleInChrome" Value="True" />
            <!-- A caption button, as in any Windows title bar: clicked, invoked by a screen reader, or Alt+F4; never a
                 Tab stop, so Tab from the page wraps to the search box (WF33). -->
            <Setter Property="IsTabStop" Value="False" />
```

Replace

```xml
                    <ControlTemplate TargetType="TabControl">
                        <Grid>
```

with

```xml
                    <ControlTemplate TargetType="TabControl">
                        <!-- One tab group: search (0), the page list (1), Quit (2), the page (3). Tab past the page's
                             last control wraps to search; Quit is never the next stop after page content (WF33). -->
                        <Grid KeyboardNavigation.TabNavigation="Local">
```

Replace

```xml
                                            Margin="0,8,0,0" Padding="10,5" HorizontalAlignment="Stretch"
                                            KeyboardNavigation.TabIndex="1"
```

with

```xml
                                            Margin="0,8,0,0" Padding="10,5" HorizontalAlignment="Stretch"
                                            KeyboardNavigation.TabIndex="2"
```

Replace `                                    <Grid DockPanel.Dock="Top" Margin="0,0,0,8" KeyboardNavigation.TabIndex="0">` with

```xml
                                    <Grid DockPanel.Dock="Top" Margin="0,0,0,8" KeyboardNavigation.TabIndex="0" KeyboardNavigation.TabNavigation="Local">
```

Replace `                                    <Grid KeyboardNavigation.TabIndex="0">` with

```xml
                                    <Grid KeyboardNavigation.TabIndex="1" KeyboardNavigation.TabNavigation="Local">
```

Replace

```xml
                            <ContentPresenter x:Name="PART_SelectedContentHost" Grid.Column="1"
                                              ContentSource="SelectedContent" Margin="16,12,16,16" />
```

with

```xml
                            <ContentPresenter x:Name="PART_SelectedContentHost" Grid.Column="1"
                                              ContentSource="SelectedContent" Margin="16,12,16,16"
                                              KeyboardNavigation.TabIndex="3" KeyboardNavigation.TabNavigation="Local" />
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsEntryPointScanTests|FullyQualifiedName~SettingsSourceScanTests"`
Expected: PASS.

Run (desktop session): `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~SettingsReachTests|FullyQualifiedName~SidebarTabControlAutomationTests"`
Expected: PASS. If `Tab_from_the_end...` still reports pages, read which element it names: a page-level `ScrollViewer` that takes focus gets `Focusable="False"` (the Claude Code page already has it), and nothing else changes.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/tests/Sanduhr.Tests/SettingsEntryPointScanTests.cs windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs
git commit -m "fix(settings-v2): Quit leaves the Tab path; Tab from a page wraps to search"
```

---

### Task 14: Alerts dim when off; ScrollViewers on Alerts and Publish usage

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs`

**Interfaces:**
- Slice 1 changes the "100%" checkbox's `Content` line. This task never edits that element: the `IsEnabled` binding lives on the new `AlertsDependents` panel around it, so the two slices share no line. The Alerts page title and the "%" suffixes are Task 19's.
- Produces: `AlertsOffLine` (TextBlock, "Alerts are off. Nothing below sends.") and `AlertsDependents` (the panel bound to `AlertsEnabled`, dimmed to 0.45 when disabled), holding the thresholds, projection, reset, sound, scoped chime, snake sound and Send test alert; Sign-in reminders stays outside (it is the Desk's, moving in slice 3). Both pages scroll vertically.

- [ ] **Step 1: Write the failing tests**

Append inside `SettingsReachTests`:

```csharp
    private static ScrollViewer? ScrollerAbove(DependencyObject element)
    {
        for (var p = VisualTreeHelper.GetParent(element); p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is ScrollViewer scroller)
                return scroller;
        return null;
    }

    [Fact]
    public void Alerts_off_dims_everything_under_the_switch_and_says_why() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            Land(window, guard, SettingsPage.Alerts, SettingsIndex.Notifications);
            var content = (DependencyObject)TabOf(window, SettingsPage.Alerts).Content;
            var off = (FrameworkElement)window.FindName("AlertsOffLine");

            svm.Alerts.AlertsEnabled = false;
            window.UpdateLayout();
            Pump();
            Assert.False(SettingsAnchor.Find(content, SettingsIndex.Thresholds)!.IsEnabled);
            Assert.False(SettingsAnchor.Find(content, SettingsIndex.Sound)!.IsEnabled);
            Assert.False(SettingsAnchor.Find(content, SettingsIndex.TestAlert)!.IsEnabled);
            Assert.True(SettingsAnchor.Find(content, SettingsIndex.SignInReminders)!.IsEnabled);
            Assert.True(off.IsVisible);
            Assert.Equal(0.45, ((FrameworkElement)window.FindName("AlertsDependents")).Opacity, 2);

            svm.Alerts.AlertsEnabled = true;
            window.UpdateLayout();
            Pump();
            Assert.True(SettingsAnchor.Find(content, SettingsIndex.Sound)!.IsEnabled);
            Assert.False(off.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Alerts_and_Publish_usage_scroll_on_a_short_window() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.Height = 520;
            foreach (var (page, anchor) in new[] { (SettingsPage.Alerts, SettingsIndex.TestAlert), (SettingsPage.Publish, SettingsIndex.Schedule) })
            {
                var scroller = ScrollerAbove(Land(window, guard, page, anchor));
                Assert.True(scroller is { VerticalScrollBarVisibility: ScrollBarVisibility.Auto, Focusable: false },
                    $"{page} has no page scroll viewer");
            }
        }
        finally { window.Close(); }
    });
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SettingsReachTests`
Expected: FAIL: `window.FindName("AlertsOffLine")` is null (a cast `NullReferenceException`), and "Alerts has no page scroll viewer".

- [ ] **Step 3: Alerts: scroller, off line and the dimmed panel**

In `SettingsWindow.xaml`, replace

```xml
                <TabItem Header="Alerts" Tag="Alerts" AutomationProperties.Name="Alerts">
                    <Border Background="{DynamicResource Sanduhr.Brush.Glass}" CornerRadius="8" Padding="16">
                        <StackPanel DataContext="{Binding Alerts}" views:SettingsAnchor.Id="notifications">
```

with

```xml
                <TabItem Header="Alerts" Tag="Alerts" AutomationProperties.Name="Alerts">
                    <Border Background="{DynamicResource Sanduhr.Brush.Glass}" CornerRadius="8">
                        <ScrollViewer VerticalScrollBarVisibility="Auto" Focusable="False">
                            <ScrollViewer.Resources>
                                <Style TargetType="ScrollBar" BasedOn="{StaticResource SlimThemeScrollBar}" />
                            </ScrollViewer.Resources>
                        <StackPanel Margin="16" DataContext="{Binding Alerts}" views:SettingsAnchor.Id="notifications">
```

Replace

```xml
                            <CheckBox Content="Enable alerts" IsChecked="{Binding AlertsEnabled}"
                                      Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,10" />

                            <Grid Margin="0,0,0,10" views:SettingsAnchor.Id="thresholds">
```

with

```xml
                            <CheckBox Content="Enable alerts" IsChecked="{Binding AlertsEnabled}"
                                      Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,10" />

                            <!-- WF36: with alerts off, everything that only matters for alerts dims and says so. -->
                            <TextBlock x:Name="AlertsOffLine" Text="Alerts are off. Nothing below sends." FontSize="11"
                                       TextWrapping="Wrap" Margin="0,0,0,10" AutomationProperties.LiveSetting="Polite"
                                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}">
                                <TextBlock.Style>
                                    <Style TargetType="TextBlock">
                                        <Setter Property="Visibility" Value="Collapsed" />
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding AlertsEnabled}" Value="False">
                                                <Setter Property="Visibility" Value="Visible" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </TextBlock.Style>
                            </TextBlock>

                            <StackPanel x:Name="AlertsDependents" IsEnabled="{Binding AlertsEnabled}">
                                <StackPanel.Style>
                                    <Style TargetType="StackPanel">
                                        <Style.Triggers>
                                            <Trigger Property="IsEnabled" Value="False">
                                                <Setter Property="Opacity" Value="0.45" />
                                            </Trigger>
                                        </Style.Triggers>
                                    </Style>
                                </StackPanel.Style>

                            <Grid Margin="0,0,0,10" views:SettingsAnchor.Id="thresholds">
```

Replace

```xml
                            <Button Style="{StaticResource FlatButton}" Content="{x:Static core:SettingsNames.SendTestAlert}"
                                    views:SettingsAnchor.Id="test"
                                    Command="{Binding TestAlertCommand}" HorizontalAlignment="Left" />
```

with

```xml
                            <Button Style="{StaticResource FlatButton}" Content="{x:Static core:SettingsNames.SendTestAlert}"
                                    views:SettingsAnchor.Id="test"
                                    Command="{Binding TestAlertCommand}" HorizontalAlignment="Left" />
                            </StackPanel>
```

Replace

```xml
                            </StackPanel>
                        </StackPanel>
                    </Border>
                </TabItem>

                <!-- History tab (item 9) — 30-day usage chart + CSV export. -->
```

with

```xml
                            </StackPanel>
                        </StackPanel>
                        </ScrollViewer>
                    </Border>
                </TabItem>

                <!-- History tab (item 9) — 30-day usage chart + CSV export. -->
```

- [ ] **Step 4: Publish usage: scroller**

Replace

```xml
                <TabItem Header="Publish usage" Tag="Publish" AutomationProperties.Name="Publish usage" DataContext="{Binding Publish}">
                    <Border Background="{DynamicResource Sanduhr.Brush.Glass}" CornerRadius="8" Padding="20">
                        <StackPanel>
```

with

```xml
                <TabItem Header="Publish usage" Tag="Publish" AutomationProperties.Name="Publish usage" DataContext="{Binding Publish}">
                    <Border Background="{DynamicResource Sanduhr.Brush.Glass}" CornerRadius="8">
                        <ScrollViewer VerticalScrollBarVisibility="Auto" Focusable="False">
                            <ScrollViewer.Resources>
                                <Style TargetType="ScrollBar" BasedOn="{StaticResource SlimThemeScrollBar}" />
                            </ScrollViewer.Resources>
                        <StackPanel Margin="20">
```

Replace

```xml
                        </StackPanel>
                    </Border>
                </TabItem>

                <!-- Claude Code page (Stage 4.2): the folders Sanduhr knows and Sanduhr's pieces in each.
```

with

```xml
                        </StackPanel>
                        </ScrollViewer>
                    </Border>
                </TabItem>

                <!-- Claude Code page (Stage 4.2): the folders Sanduhr knows and Sanduhr's pieces in each.
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SettingsReachTests`
Expected: PASS.

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsReachIndexTests"`
Expected: PASS (every anchor still marked inside its TabItem).

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs
git commit -m "fix(settings-v2): Alerts dim and say so when off; Alerts and Publish usage scroll"
```

---

### Task 15: The meter history says why it is empty

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/HistoryTabViewModel.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs`

**Interfaces:**
- Produces: `string HistoryTabViewModel.EmptyText` (observable), `bool HasEmptyText`, `internal const string SignedOutLine = "Sign in to start recording your meters. Sanduhr keeps 30 days on this PC."`, `internal const string NoHistoryLine = "No history yet. Data accumulates as you use Sanduhr."` (the words the chart already draws, now readable), `internal static string EmptyLine(bool signedIn, bool hasRows)`; `BuildRows()` sets `EmptyText`. XAML `HistoryEmptyText` shows in place of the chart, which hides.

- [ ] **Step 1: Write the failing tests**

Append inside `SettingsReachTests`:

```csharp
    [Fact]
    public void The_meter_history_says_why_it_is_empty() => AppHost.Run(() =>
    {
        var (signedOut, _) = ScratchWidget();
        var tab = new HistoryTabViewModel(signedOut);
        tab.BuildRows();
        Assert.Equal(HistoryTabViewModel.SignedOutLine, tab.EmptyText);
        Assert.True(tab.HasEmptyText);

        var (work, _) = ScratchWidget("Work");
        var workTab = new HistoryTabViewModel(work);
        workTab.BuildRows();
        Assert.Equal(HistoryTabViewModel.NoHistoryLine, workTab.EmptyText);

        work.History.AppendForAccount("five_hour", 41, "Work");
        Assert.NotEmpty(workTab.BuildRows());
        Assert.Equal("", workTab.EmptyText);
        Assert.False(workTab.HasEmptyText);
    });

    [Fact]
    public void Signed_out_the_History_page_reads_its_empty_line() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open();
        try
        {
            Land(window, guard, SettingsPage.History, SettingsIndex.Chart);
            var line = (TextBlock)window.FindName("HistoryEmptyText");
            Assert.True(line.IsVisible);
            Assert.Equal(HistoryTabViewModel.SignedOutLine, line.Text);
            Assert.Equal(Visibility.Hidden, ((FrameworkElement)window.FindName("Chart")).Visibility);
        }
        finally { window.Close(); }
    });
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SettingsReachTests`
Expected: build FAILS with `'HistoryTabViewModel' does not contain a definition for 'EmptyText'`.

- [ ] **Step 3: The view model**

In `HistoryTabViewModel.cs`, replace

```csharp
    /// <summary>
    /// Assemble the chart rows from the shared history store. Single-account mode
    /// loads one window per tier (accent line); all-accounts mode overlays one
    /// per-account series per tier (registry order → stable colors). A tier with
    /// no data in the window is dropped (parity with the Python <c>if agg:</c>).
    /// </summary>
    public IReadOnlyList<ChartRow> BuildRows()
    {
```

with

```csharp
    /// <summary>
    /// What the meter history says when it draws nothing (WF32, WF35): signed out, how to start it; signed in with nothing
    /// recorded yet, that it fills in. Empty while the chart has rows. A screen reader reads it; the chart is a drawing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmptyText))]
    private string _emptyText = "";

    /// <summary>The empty-state line shows in place of the chart.</summary>
    public bool HasEmptyText => EmptyText.Length > 0;

    internal const string SignedOutLine = "Sign in to start recording your meters. Sanduhr keeps 30 days on this PC.";
    internal const string NoHistoryLine = "No history yet. Data accumulates as you use Sanduhr.";

    /// <summary>The empty-state line for a chart with or without rows.</summary>
    internal static string EmptyLine(bool signedIn, bool hasRows) => hasRows ? "" : signedIn ? NoHistoryLine : SignedOutLine;

    /// <summary>The chart rows (see <see cref="Rows"/>), and the empty-state line that goes with them.</summary>
    public IReadOnlyList<ChartRow> BuildRows()
    {
        var rows = Rows();
        EmptyText = EmptyLine(_widget.ActiveAccount is not null || _widget.IsDemo, rows.Count > 0);
        return rows;
    }

    /// <summary>
    /// Assemble the chart rows from the shared history store. Single-account mode
    /// loads one window per tier (accent line); all-accounts mode overlays one
    /// per-account series per tier (registry order → stable colors). A tier with
    /// no data in the window is dropped (parity with the Python <c>if agg:</c>).
    /// </summary>
    private IReadOnlyList<ChartRow> Rows()
    {
```

- [ ] **Step 4: The XAML**

In `SettingsWindow.xaml`, replace

```xml
                            <!-- The chart (custom-render control; data pushed from code-behind). -->
                            <Border Grid.Row="3" Background="{DynamicResource Sanduhr.Brush.Bg}" CornerRadius="6"
                                    BorderBrush="{DynamicResource Sanduhr.Brush.Border}" BorderThickness="1"
                                    Padding="8" Margin="0,4,0,0">
                                <views:HistoryChart x:Name="Chart" />
                            </Border>
```

with

```xml
                            <!-- The chart (custom-render control; data pushed from code-behind). With nothing to draw it
                                 gives way to a line a screen reader can read (WF32, WF35). -->
                            <Border Grid.Row="3" Background="{DynamicResource Sanduhr.Brush.Bg}" CornerRadius="6"
                                    BorderBrush="{DynamicResource Sanduhr.Brush.Border}" BorderThickness="1"
                                    Padding="8" Margin="0,4,0,0">
                                <Grid>
                                    <views:HistoryChart x:Name="Chart">
                                        <views:HistoryChart.Style>
                                            <Style TargetType="views:HistoryChart">
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding HasEmptyText}" Value="True">
                                                        <Setter Property="Visibility" Value="Hidden" />
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </views:HistoryChart.Style>
                                    </views:HistoryChart>
                                    <TextBlock x:Name="HistoryEmptyText" Text="{Binding EmptyText}" TextWrapping="Wrap" FontSize="12"
                                               HorizontalAlignment="Center" VerticalAlignment="Center" TextAlignment="Center" Margin="12"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                                               AutomationProperties.LiveSetting="Polite"
                                               Visibility="{Binding HasEmptyText, Converter={StaticResource BoolVis}}" />
                                </Grid>
                            </Border>
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~SettingsReachTests|FullyQualifiedName~HistoryClearTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/HistoryTabViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs
git commit -m "fix(settings-v2): the meter history says why it is empty, signed out or not yet recorded"
```

---

### Task 16: Empty lists say so and are no longer empty tab stops

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs`

**Interfaces:**
- Produces: styles `EmptyLine` (TextBlock) and `EmptyLinkLine` (WrapPanel), each visible only while its `Tag` (bound to a list's `HasItems`) is false; style `SettingsLinkButton` (accent text link with a focus ring); `SettingsViewModel.ClaudeCodeSettingsLink` (`SettingsNames.LinkTitle(SettingsPage.ClaudeCode)`, the existing "<Page> settings…" form); `SettingsWindow.OpenSettingsLink_Click` (its `Tag` is a link, "claudecode#folders"). Named lists and lines: `PublishHomesList`/`PublishHomesEmpty`, `VaultRootsList`/`VaultRootsEmpty`, `OverviewProjectsList`/`OverviewProjectsEmpty`, `OverviewSkillsList`/`OverviewSkillsEmpty`, `TrendsProjectsList`/`TrendsProjectsEmpty`, `NowPlayingAppsList`/`NowPlayingAppsEmpty`. Every one of these lists is `Focusable="False"`; their check boxes stay tab stops.

**Precondition: slice 1 is merged and this branch is rebased on it.** The old strings below are the list elements as slice 1 leaves them (its Task 4, Step 6 adds `AutomationProperties.Name` to the Projects, Skills, vault Roots and Trends lists); the new strings keep those names.

New lines: "No Claude Code folders found. Add one in" + the link "Claude Code settings…" (Homes to share, History vault; spec, Overview); "No projects in the last 30 days."; "No skills in the last 30 days."; "No projects in this window."; "Nothing has played since Sanduhr started."

- [ ] **Step 1: Write the failing test**

Append inside `SettingsReachTests`:

```csharp
    [Fact]
    public void Empty_lists_say_so_and_are_not_tab_stops() => AppHost.Run(() =>
    {
        Directory.Delete(Path.Combine(Profile, ".claude"), recursive: true); // no Claude Code folder at all
        var (window, svm, guard) = Open("Personal");
        try
        {
            void Shows(string line, string list)
            {
                window.UpdateLayout();
                Pump();
                Assert.True(((FrameworkElement)window.FindName(line)).IsVisible, $"{line} is not shown");
                Assert.False(((Control)window.FindName(list)).Focusable, $"{list} is a tab stop");
            }

            Land(window, guard, SettingsPage.Publish, SettingsIndex.Homes);
            Shows("PublishHomesEmpty", "PublishHomesList");

            Land(window, guard, SettingsPage.ClaudeUsage, SettingsIndex.Vault);
            Shows("VaultRootsEmpty", "VaultRootsList");
            svm.ClaudeCode.Overview.ShowBreakdowns = true;
            Shows("OverviewProjectsEmpty", "OverviewProjectsList");
            Shows("OverviewSkillsEmpty", "OverviewSkillsList");

            Land(window, guard, SettingsPage.ClaudeUsage, SettingsIndex.Trends);
            Shows("TrendsProjectsEmpty", "TrendsProjectsList");

            Land(window, guard, SettingsPage.NowPlaying, SettingsIndex.NowPlayingApps);
            Shows("NowPlayingAppsEmpty", "NowPlayingAppsList");

            // The link in the folder line lands on the Claude Code folders.
            Land(window, guard, SettingsPage.Publish, SettingsIndex.Homes);
            var link = ((Panel)window.FindName("PublishHomesEmpty")).Children.OfType<Button>().Single();
            Assert.Equal(SettingsNames.LinkTitle(SettingsPage.ClaudeCode), link.Content);
            link.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump();
            guard.Check();
            Assert.Equal(new SettingsLink(SettingsPage.ClaudeCode, SettingsIndex.Folders), window.Place);
        }
        finally { window.Close(); }
    });
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SettingsReachTests.Empty_lists`
Expected: FAIL with a `NullReferenceException` at `window.FindName("PublishHomesEmpty")`.

- [ ] **Step 3: The styles**

In `SettingsWindow.xaml`, replace

```xml
        <!-- Title-bar close button — themed text, square red hover (matches the widget). -->
```

with

```xml
        <!-- An empty list's line (WF35): shown only while its Tag, bound to the list's HasItems, is false. -->
        <Style x:Key="EmptyLine" TargetType="TextBlock">
            <Setter Property="FontSize" Value="11" />
            <Setter Property="TextWrapping" Value="Wrap" />
            <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.TextDim}" />
            <Setter Property="AutomationProperties.LiveSetting" Value="Polite" />
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="False">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>

        <!-- The same for a line that carries a link. -->
        <Style x:Key="EmptyLinkLine" TargetType="WrapPanel">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="False">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>

        <!-- A link inside Settings ("Claude Code settings…"): accent text, underlined on hover, ringed on keyboard focus. -->
        <Style x:Key="SettingsLinkButton" TargetType="Button">
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="FocusVisualStyle" Value="{x:Null}" />
            <Setter Property="VerticalAlignment" Value="Center" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="ring" BorderThickness="1" BorderBrush="Transparent" CornerRadius="3" Padding="2,0">
                            <TextBlock x:Name="t" Text="{TemplateBinding Content}" FontSize="11"
                                       Foreground="{DynamicResource Sanduhr.Brush.Accent}" />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="t" Property="TextDecorations" Value="Underline" />
                            </Trigger>
                            <Trigger Property="IsKeyboardFocused" Value="True">
                                <Setter TargetName="ring" Property="BorderBrush" Value="{DynamicResource Sanduhr.Brush.Accent}" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- Title-bar close button — themed text, square red hover (matches the widget). -->
```

- [ ] **Step 4: The link's text and handler**

In `SettingsViewModel.cs`, after `    public string ResetSanduhrTitle => SettingsNames.ResetSanduhr;` add:

```csharp
    /// <summary>The empty folder lists' link to the Claude Code page, in the names table's "&lt;Page&gt; settings…" form.</summary>
    public string ClaudeCodeSettingsLink => SettingsNames.LinkTitle(SettingsPage.ClaudeCode);
```

In `SettingsWindow.xaml.cs`, after `CloseButton_Click` add:

```csharp

    /// <summary>A link inside Settings (an empty list's "Claude Code settings…"): its Tag is a link, "claudecode#folders".</summary>
    private void OpenSettingsLink_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string text && SettingsIndex.ParseLink(text) is { } link
            && link.Page != SettingsPage.Widget)
            SelectPage(link.Page, link.Anchor);
    }
```

- [ ] **Step 5: Homes to share and the History vault**

In `SettingsWindow.xaml`, replace

```xml
                            <ItemsControl ItemsSource="{Binding Roots}" Margin="0,0,0,10">
```

with

```xml
                            <WrapPanel x:Name="PublishHomesEmpty" Style="{StaticResource EmptyLinkLine}" Margin="0,0,0,10"
                                       Tag="{Binding HasItems, ElementName=PublishHomesList}">
                                <TextBlock Text="No Claude Code folders found. Add one in " FontSize="11" VerticalAlignment="Center"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                                <Button Style="{StaticResource SettingsLinkButton}" Tag="claudecode#folders" Click="OpenSettingsLink_Click"
                                        Content="{Binding DataContext.ClaudeCodeSettingsLink, RelativeSource={RelativeSource AncestorType=Window}}" />
                            </WrapPanel>
                            <ItemsControl x:Name="PublishHomesList" ItemsSource="{Binding Roots}" Margin="0,0,0,10" Focusable="False">
```

and replace

```xml
                                    <ItemsControl ItemsSource="{Binding Roots}" AutomationProperties.Name="Session archive folders">
```

with

```xml
                                    <WrapPanel x:Name="VaultRootsEmpty" Style="{StaticResource EmptyLinkLine}" Margin="0,0,0,4"
                                               Tag="{Binding HasItems, ElementName=VaultRootsList}">
                                        <TextBlock Text="No Claude Code folders found. Add one in " FontSize="11" VerticalAlignment="Center"
                                                   Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                                        <Button Style="{StaticResource SettingsLinkButton}" Tag="claudecode#folders" Click="OpenSettingsLink_Click"
                                                Content="{Binding DataContext.ClaudeCodeSettingsLink, RelativeSource={RelativeSource AncestorType=Window}}" />
                                    </WrapPanel>
                                    <ItemsControl x:Name="VaultRootsList" ItemsSource="{Binding Roots}" AutomationProperties.Name="Session archive folders" Focusable="False">
```

- [ ] **Step 6: Overview's tables, Trends and Apps**

Replace

```xml
                                    <TextBlock DockPanel.Dock="Top" Text="Top projects (last 30 days)" FontSize="9"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,0,0,4" />
```

with

```xml
                                    <TextBlock DockPanel.Dock="Top" Text="Top projects (last 30 days)" FontSize="9"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,0,0,4" />
                                    <TextBlock x:Name="OverviewProjectsEmpty" DockPanel.Dock="Top" Style="{StaticResource EmptyLine}"
                                               Margin="0,0,0,4" Text="No projects in the last 30 days."
                                               Tag="{Binding HasItems, ElementName=OverviewProjectsList}" />
```

Replace

```xml
                                        <ScrollViewer VerticalScrollBarVisibility="Auto">
                                            <ScrollViewer.Resources>
                                                <Style TargetType="ScrollBar" BasedOn="{StaticResource SlimThemeScrollBar}" />
                                            </ScrollViewer.Resources>
                                            <ItemsControl ItemsSource="{Binding Projects}" Margin="4" AutomationProperties.Name="Top projects (last 30 days)">
```

with

```xml
                                        <ScrollViewer VerticalScrollBarVisibility="Auto" Focusable="False">
                                            <ScrollViewer.Resources>
                                                <Style TargetType="ScrollBar" BasedOn="{StaticResource SlimThemeScrollBar}" />
                                            </ScrollViewer.Resources>
                                            <ItemsControl x:Name="OverviewProjectsList" ItemsSource="{Binding Projects}" Margin="4" AutomationProperties.Name="Top projects (last 30 days)" Focusable="False">
```

Replace

```xml
                                    <TextBlock DockPanel.Dock="Top" Text="Top skills (last 30 days)" FontSize="9"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,0,0,4" />
```

with

```xml
                                    <TextBlock DockPanel.Dock="Top" Text="Top skills (last 30 days)" FontSize="9"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,0,0,4" />
                                    <TextBlock x:Name="OverviewSkillsEmpty" DockPanel.Dock="Top" Style="{StaticResource EmptyLine}"
                                               Margin="0,0,0,4" Text="No skills in the last 30 days."
                                               Tag="{Binding HasItems, ElementName=OverviewSkillsList}" />
```

Replace

```xml
                                        <ScrollViewer VerticalScrollBarVisibility="Auto">
                                            <ScrollViewer.Resources>
                                                <Style TargetType="ScrollBar" BasedOn="{StaticResource SlimThemeScrollBar}" />
                                            </ScrollViewer.Resources>
                                            <ItemsControl ItemsSource="{Binding Skills}" Margin="4" AutomationProperties.Name="Top skills (last 30 days)">
```

with

```xml
                                        <ScrollViewer VerticalScrollBarVisibility="Auto" Focusable="False">
                                            <ScrollViewer.Resources>
                                                <Style TargetType="ScrollBar" BasedOn="{StaticResource SlimThemeScrollBar}" />
                                            </ScrollViewer.Resources>
                                            <ItemsControl x:Name="OverviewSkillsList" ItemsSource="{Binding Skills}" Margin="4" AutomationProperties.Name="Top skills (last 30 days)" Focusable="False">
```

Replace

```xml
                                    <ItemsControl ItemsSource="{Binding TopProjects}" AutomationProperties.Name="Top projects (window)">
```

with

```xml
                                    <TextBlock x:Name="TrendsProjectsEmpty" DockPanel.Dock="Top" Style="{StaticResource EmptyLine}"
                                               Text="No projects in this window."
                                               Tag="{Binding HasItems, ElementName=TrendsProjectsList}" />
                                    <ItemsControl x:Name="TrendsProjectsList" ItemsSource="{Binding TopProjects}" AutomationProperties.Name="Top projects (window)" Focusable="False">
```

Replace

```xml
                                <ItemsControl ItemsSource="{Binding Apps}" Focusable="False">
```

with

```xml
                                <TextBlock x:Name="NowPlayingAppsEmpty" Style="{StaticResource EmptyLine}" Margin="0,0,0,6"
                                           Text="Nothing has played since Sanduhr started."
                                           Tag="{Binding HasItems, ElementName=NowPlayingAppsList}" />
                                <ItemsControl x:Name="NowPlayingAppsList" ItemsSource="{Binding Apps}" Focusable="False">
```

- [ ] **Step 7: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SettingsReachTests`
Expected: PASS.

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsNamesTests"`
Expected: PASS (the scans accept the new lines; none names a page that doesn't exist).

- [ ] **Step 8: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs
git commit -m "fix(settings-v2): empty lists say so, link to Claude Code, and stop being empty tab stops"
```

---

### Task 17: Focus rings on buttons, pills, check boxes and links; disabled controls draw disabled

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs`

**Interfaces:**
- Produces: an `IsKeyboardFocused` template trigger on `FlatButton` (and so every pill `BasedOn` it), `AccentButton`, `ThemedCheckBox`, the shortcut-reset link and the About "Project page" links; an `IsEnabled=False` trigger on `FlatButton` (existing), `ThemedComboBox` and `ThemedCheckBox`. The ring is the Text brush at 2 px, so it reads on a glass button and on an accent-filled pill alike.

- [ ] **Step 1: Write the failing test**

Append inside `SettingsReachTests`:

```csharp
    [Fact]
    public void Buttons_pills_links_and_boxes_show_focus_and_draw_disabled() => AppHost.Run(() =>
    {
        var (window, _, _) = Open("Personal");
        try
        {
            IEnumerable<Trigger> TemplateTriggers(string key) =>
                ((Style)window.FindResource(key)).Setters.OfType<Setter>()
                    .Where(s => s.Property == Control.TemplateProperty)
                    .SelectMany(s => ((ControlTemplate)s.Value).Triggers.OfType<Trigger>());
            bool Shows(string key, DependencyProperty property, bool value) =>
                TemplateTriggers(key).Any(t => t.Property == property && Equals(t.Value, value));

            foreach (var key in new[] { "FlatButton", "AccentButton", "SettingsLinkButton", "ThemedCheckBox" })
                Assert.True(Shows(key, UIElement.IsKeyboardFocusedProperty, true), $"{key} draws no focus ring");
            foreach (var key in new[] { "FlatButton", "ThemedComboBox", "ThemedCheckBox" })
                Assert.True(Shows(key, UIElement.IsEnabledProperty, false), $"{key} does not draw disabled");
            // The History and Claude Usage pills are FlatButtons with the same template.
            Assert.Same(window.FindResource("FlatButton"), ((Style)window.FindResource("RangePillWeek")).BasedOn);
            Assert.Same(window.FindResource("FlatButton"), ((Style)window.FindResource("CcNavOverview")).BasedOn);
        }
        finally { window.Close(); }
    });
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SettingsReachTests.Buttons_pills`
Expected: FAIL with "FlatButton draws no focus ring".

- [ ] **Step 3: FlatButton and AccentButton**

In `SettingsWindow.xaml`, replace

```xml
        <Style x:Key="FlatButton" TargetType="Button">
            <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.Text}" />
```

with

```xml
        <Style x:Key="FlatButton" TargetType="Button">
            <Setter Property="FocusVisualStyle" Value="{x:Null}" />
            <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.Text}" />
```

Replace

```xml
                            <Trigger Property="IsEnabled" Value="False">
                                <Setter Property="Opacity" Value="0.4" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- The sidebar foot's Quit: a FlatButton that shows keyboard focus. -->
```

with

```xml
                            <Trigger Property="IsEnabled" Value="False">
                                <Setter Property="Opacity" Value="0.4" />
                            </Trigger>
                            <!-- WF38: a ring in the text ink, which reads on glass and on an accent-filled pill alike. -->
                            <Trigger Property="IsKeyboardFocused" Value="True">
                                <Setter TargetName="bg" Property="BorderBrush" Value="{DynamicResource Sanduhr.Brush.Text}" />
                                <Setter TargetName="bg" Property="BorderThickness" Value="2" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- The sidebar foot's Quit: a FlatButton that shows keyboard focus. -->
```

Replace

```xml
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="bg" Property="Opacity" Value="0.88" />
                            </Trigger>
```

with

```xml
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="bg" Property="Opacity" Value="0.88" />
                            </Trigger>
                            <Trigger Property="IsKeyboardFocused" Value="True">
                                <Setter TargetName="bg" Property="BorderBrush" Value="{DynamicResource Sanduhr.Brush.Text}" />
                                <Setter TargetName="bg" Property="BorderThickness" Value="2" />
                            </Trigger>
```

- [ ] **Step 4: ThemedComboBox and ThemedCheckBox**

Replace

```xml
                            <Trigger Property="IsKeyboardFocusWithin" Value="True">
                                <Setter TargetName="bd" Property="BorderBrush" Value="{DynamicResource Sanduhr.Brush.Accent}" />
                            </Trigger>
```

with

```xml
                            <Trigger Property="IsKeyboardFocusWithin" Value="True">
                                <Setter TargetName="bd" Property="BorderBrush" Value="{DynamicResource Sanduhr.Brush.Accent}" />
                            </Trigger>
                            <!-- WF38: a disabled combo (demo mode's Linked account) looks disabled. -->
                            <Trigger Property="IsEnabled" Value="False">
                                <Setter Property="Opacity" Value="0.45" />
                            </Trigger>
```

Replace

```xml
        <Style x:Key="ThemedCheckBox" TargetType="CheckBox">
            <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.Text}" />
```

with

```xml
        <Style x:Key="ThemedCheckBox" TargetType="CheckBox">
            <Setter Property="FocusVisualStyle" Value="{x:Null}" />
            <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.Text}" />
```

Replace

```xml
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="box" Property="BorderBrush" Value="{DynamicResource Sanduhr.Brush.Accent}" />
                            </Trigger>
```

with

```xml
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="box" Property="BorderBrush" Value="{DynamicResource Sanduhr.Brush.Accent}" />
                            </Trigger>
                            <Trigger Property="IsKeyboardFocused" Value="True">
                                <Setter TargetName="box" Property="BorderBrush" Value="{DynamicResource Sanduhr.Brush.Accent}" />
                                <Setter TargetName="box" Property="BorderThickness" Value="2" />
                            </Trigger>
                            <Trigger Property="IsEnabled" Value="False">
                                <Setter Property="Opacity" Value="0.45" />
                            </Trigger>
```

- [ ] **Step 5: The link-style buttons**

Replace (the shortcut reset link's template; the anchor is `Shortcuts.Note` below it)

```xml
                                        <ControlTemplate TargetType="Button">
                                            <TextBlock x:Name="t" Text="{TemplateBinding Content}" FontSize="11"
                                                       Foreground="{DynamicResource Sanduhr.Brush.Accent}" />
                                            <ControlTemplate.Triggers>
                                                <Trigger Property="IsMouseOver" Value="True">
                                                    <Setter TargetName="t" Property="TextDecorations" Value="Underline" />
                                                </Trigger>
                                            </ControlTemplate.Triggers>
                                        </ControlTemplate>
                                    </Button.Template>
                                </Button>
                                <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,6,0,0"
                                           Text="{Binding Shortcuts.Note}"
```

with

```xml
                                        <ControlTemplate TargetType="Button">
                                            <Border x:Name="ring" BorderThickness="1" BorderBrush="Transparent" CornerRadius="3" Padding="2,0">
                                                <TextBlock x:Name="t" Text="{TemplateBinding Content}" FontSize="11"
                                                           Foreground="{DynamicResource Sanduhr.Brush.Accent}" />
                                            </Border>
                                            <ControlTemplate.Triggers>
                                                <Trigger Property="IsMouseOver" Value="True">
                                                    <Setter TargetName="t" Property="TextDecorations" Value="Underline" />
                                                </Trigger>
                                                <Trigger Property="IsKeyboardFocused" Value="True">
                                                    <Setter TargetName="ring" Property="BorderBrush" Value="{DynamicResource Sanduhr.Brush.Accent}" />
                                                </Trigger>
                                            </ControlTemplate.Triggers>
                                        </ControlTemplate>
                                    </Button.Template>
                                </Button>
                                <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,6,0,0"
                                           Text="{Binding Shortcuts.Note}"
```

With the Edit tool's `replace_all: true`, replace both About "Project page" templates (identical blocks)

```xml
                                                        <ControlTemplate TargetType="Button">
                                                            <TextBlock x:Name="t" Text="{TemplateBinding Content}" FontSize="11"
                                                                       Foreground="{DynamicResource Sanduhr.Brush.Accent}" />
                                                            <ControlTemplate.Triggers>
                                                                <Trigger Property="IsMouseOver" Value="True">
                                                                    <Setter TargetName="t" Property="TextDecorations" Value="Underline" />
                                                                </Trigger>
                                                                <Trigger Property="IsKeyboardFocused" Value="True">
                                                                    <Setter TargetName="t" Property="TextDecorations" Value="Underline" />
                                                                </Trigger>
                                                            </ControlTemplate.Triggers>
                                                        </ControlTemplate>
```

with

```xml
                                                        <ControlTemplate TargetType="Button">
                                                            <Border x:Name="ring" BorderThickness="1" BorderBrush="Transparent" CornerRadius="3" Padding="2,0">
                                                                <TextBlock x:Name="t" Text="{TemplateBinding Content}" FontSize="11"
                                                                           Foreground="{DynamicResource Sanduhr.Brush.Accent}" />
                                                            </Border>
                                                            <ControlTemplate.Triggers>
                                                                <Trigger Property="IsMouseOver" Value="True">
                                                                    <Setter TargetName="t" Property="TextDecorations" Value="Underline" />
                                                                </Trigger>
                                                                <Trigger Property="IsKeyboardFocused" Value="True">
                                                                    <Setter TargetName="ring" Property="BorderBrush" Value="{DynamicResource Sanduhr.Brush.Accent}" />
                                                                </Trigger>
                                                            </ControlTemplate.Triggers>
                                                        </ControlTemplate>
```

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~SettingsReachTests|FullyQualifiedName~SettingsWindowNamesTests|FullyQualifiedName~ThemeGalleryAutomationTests"`
Expected: PASS (desktop-only cases skip on CI).

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs
git commit -m "fix(settings-v2): focus rings on buttons, pills, boxes and links; disabled combos and boxes dim"
```

---

### Task 18: The Desk page scrolls sideways only below its rows' width

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs`

**Interfaces:**
- Produces: `DeskPageScroller` (`HorizontalScrollBarVisibility="Auto"`) and `DeskPageContent` (a Border as wide as the viewport, never under 530 DIP), so text still wraps at the viewport above 530 and the page scrolls sideways below it (WF26's last resort; slice 3 makes the rows wrap).

- [ ] **Step 1: Write the failing test**

Append inside `SettingsReachTests`:

```csharp
    [Fact]
    public void The_Desk_page_scrolls_sideways_only_below_its_rows_width() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.Width = 680;
            window.Height = 800;
            Land(window, guard, SettingsPage.Desk, SettingsIndex.DeskPieces);
            window.UpdateLayout();
            Pump();
            var scroller = (ScrollViewer)window.FindName("DeskPageScroller");
            Assert.True(scroller.ScrollableWidth > 0,
                $"at 680 wide the Desk page clips instead of scrolling (extent {scroller.ExtentWidth:0}, viewport {scroller.ViewportWidth:0})");

            window.Width = 780;
            window.UpdateLayout();
            Pump();
            Assert.True(scroller.ScrollableWidth < 1,
                $"at the default 780 wide the Desk page scrolls sideways (extent {scroller.ExtentWidth:0}, viewport {scroller.ViewportWidth:0})");
        }
        finally { window.Close(); }
    });
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SettingsReachTests.The_Desk_page`
Expected: FAIL with an `InvalidCastException` or `NullReferenceException` at `window.FindName("DeskPageScroller")`.

- [ ] **Step 3: Implement**

In `SettingsWindow.xaml`, replace

```xml
                    <Border Background="{DynamicResource Sanduhr.Brush.Glass}" CornerRadius="8">
                        <ScrollViewer VerticalScrollBarVisibility="Auto">
                            <ScrollViewer.Resources>
                                <Style TargetType="ScrollBar" BasedOn="{StaticResource SlimThemeScrollBar}" />
                            </ScrollViewer.Resources>
                        <StackPanel Margin="20">
                            <TextBlock Text="Desk" FontSize="14" FontWeight="SemiBold"
```

with

```xml
                    <Border Background="{DynamicResource Sanduhr.Brush.Glass}" CornerRadius="8">
                        <!-- WF26: the page is as wide as the viewport, so text wraps, but never narrower than a piece row
                             (118 + 138 + 8 + 72 + 8 + 146 = 490, plus the 2 x 20 margin): below that it scrolls sideways
                             instead of clipping the Monitor combo. -->
                        <ScrollViewer x:Name="DeskPageScroller" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto">
                            <ScrollViewer.Resources>
                                <Style TargetType="ScrollBar" BasedOn="{StaticResource SlimThemeScrollBar}" />
                            </ScrollViewer.Resources>
                        <Border x:Name="DeskPageContent" MinWidth="530" HorizontalAlignment="Left"
                                Width="{Binding ViewportWidth, ElementName=DeskPageScroller}">
                        <StackPanel Margin="20">
                            <TextBlock Text="Desk" FontSize="14" FontWeight="SemiBold"
```

and replace

```xml
                        </StackPanel>
                        </ScrollViewer>
                    </Border>
                </TabItem>

                <!-- Settings, Message (spec 6.2): its own control, which carries its glass, scroller and anchors. -->
```

with

```xml
                        </StackPanel>
                        </Border>
                        </ScrollViewer>
                    </Border>
                </TabItem>

                <!-- Settings, Message (spec 6.2): its own control, which carries its glass, scroller and anchors. -->
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~SettingsReachTests|FullyQualifiedName~DeskMeterStylesViewTests|FullyQualifiedName~DesignsPanelAutomationTests"`
Expected: PASS.

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsSourceScanTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs
git commit -m "fix(settings-v2): the Desk page scrolls sideways at 680 instead of clipping its rows"
```

---

### Task 19: Slice 1's hand-offs: index titles from the names table, the Alerts title and "%"

**Depends on slice 1 being merged** and this branch rebased on it: it uses `SettingsNames.SessionArchive`, `ClaudeCodeGlowHook`, `TranscriptsKept`, `ExportMeterHistory` and the `NotScanned` list in `SettingsSourceScanTests`, all added by slice 1 (`plan-slice-1.md`, "Hand-offs to other slices").

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`
- Modify: `windows-dotnet/tests/Sanduhr.Tests/SettingsSourceScanTests.cs` (the `NotScanned` line only)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (Alerts block)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs`

**Interfaces:**
- Produces: the index titles "Session archive", the Claude Code glow hook's name, "Claude Code keeps transcripts for" and "Folders to share", each keeping its old words as synonyms; `SettingsIndex.cs` scanned like every other Core file; Alerts with its page title and "%" after Warn at and Urgent at; `SettingsViewModel.TrademarkNotice` gone (unused since slice 1's `TrademarkFooter`).
- Consumes: slice 1's constants. The `ExportCsv` alias in `SettingsNames.cs` is deleted in a separate one-line commit that slice 1's owner reviews (Step 7).

- [ ] **Step 1: Let the scan read `SettingsIndex.cs` (the failing test)**

In `SettingsSourceScanTests.cs`, replace

```csharp
    private static readonly string[] NotScanned = ["SettingsNames.cs", "SettingsIndex.cs"];
```

with

```csharp
    private static readonly string[] NotScanned = ["SettingsNames.cs"];
```

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsSourceScanTests`
Expected: FAIL, naming the retired titles still in `SettingsIndex.cs` ("History vault", "History kept", "Homes to share", "Glow when Claude needs you" or whichever of them slice 1 retired).

- [ ] **Step 2: Point the titles at the names table**

In `SettingsIndex.cs`:

Replace

```csharp
        E(SettingsNames.ExportCsv, SettingsPage.History, HistoryExport, ["csv", "export", "clear history", "spreadsheet"]),
```

with

```csharp
        E(SettingsNames.ExportMeterHistory, SettingsPage.History, HistoryExport, ["csv", "export", "export csv", "clear history", "spreadsheet"]),
```

Replace

```csharp
        E("History vault", SettingsPage.ClaudeUsage, Vault,
            ["vault", "archive", "erase archive", "open vault folder", "homes", ".claude"], OverviewView),
```

with

```csharp
        E(SettingsNames.SessionArchive, SettingsPage.ClaudeUsage, Vault,
            ["history vault", "usage vault", "vault", "archive", "erase archive", "open vault folder", "homes", ".claude"], OverviewView),
```

Replace

```csharp
        E("Glow when Claude needs you", SettingsPage.ClaudeCode, Glow,
```

with

```csharp
        E(SettingsNames.ClaudeCodeGlowHook, SettingsPage.ClaudeCode, Glow,
```

and, in the same entry's synonym list, replace its first item `["glow", ` with `["glow when Claude needs you", "glow", `.

Replace

```csharp
        E("History kept", SettingsPage.Mods, Retention,
            ["retention", "cleanupPeriodDays", "transcripts", "30 days", "delete", "keep"]),
```

with

```csharp
        E(SettingsNames.TranscriptsKept, SettingsPage.Mods, Retention,
            ["history kept", "retention", "cleanupPeriodDays", "transcripts", "30 days", "delete", "keep"]),
```

Replace

```csharp
        E("Homes to share", SettingsPage.Publish, Homes, ["homes", "share", ".claude"]),
```

with

```csharp
        E("Folders to share", SettingsPage.Publish, Homes, ["homes to share", "homes", "folders", "share", ".claude"]),
```

Replace

```csharp
        [SettingsNames.ExportCsv] = HistoryExport,
```

with

```csharp
        [SettingsNames.ExportMeterHistory] = HistoryExport,
```

- [ ] **Step 3: Run the index and scan tests**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsReachIndexTests"`
Expected: PASS, including slice 1's `Old_words_still_find_the_renamed_controls` and its replacement "csv" test.

- [ ] **Step 4: Write the failing Alerts test**

Append inside `SettingsReachTests`:

```csharp
    [Fact]
    public void Alerts_has_its_page_title_and_percent_after_the_thresholds() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            var thresholds = Land(window, guard, SettingsPage.Alerts, SettingsIndex.Thresholds);
            var page = (Panel)SettingsAnchor.Find((DependencyObject)TabOf(window, SettingsPage.Alerts).Content, SettingsIndex.Notifications)!;
            Assert.Equal("Alerts", ((TextBlock)page.Children[0]).Text);
            var grid = (Grid)thresholds;
            Assert.Equal(2, grid.Children.OfType<TextBlock>().Count(t => t.Text == "%"));
        }
        finally { window.Close(); }
    });
```

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SettingsReachTests.Alerts_has`
Expected: FAIL (the first child is the intro text, and the grid has no "%").

- [ ] **Step 5: The title and the suffixes**

In `SettingsWindow.xaml`, replace

```xml
                        <StackPanel Margin="16" DataContext="{Binding Alerts}" views:SettingsAnchor.Id="notifications">
```

with

```xml
                        <StackPanel Margin="16" DataContext="{Binding Alerts}" views:SettingsAnchor.Id="notifications">
                            <TextBlock Text="Alerts" FontSize="14" FontWeight="SemiBold"
                                       Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,12" />
```

Replace

```xml
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="70" />
                                    <ColumnDefinition Width="24" />
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="70" />
                                </Grid.ColumnDefinitions>
```

with

```xml
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="70" />
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="24" />
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="70" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <TextBlock Grid.Column="2" Text="%" VerticalAlignment="Center" FontSize="12" Margin="4,0,0,0"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                <TextBlock Grid.Column="6" Text="%" VerticalAlignment="Center" FontSize="12" Margin="4,0,0,0"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
```

Replace `                                <TextBlock Grid.Column="3" Text="Urgent at" VerticalAlignment="Center" FontSize="12"` with `                                <TextBlock Grid.Column="4" Text="Urgent at" VerticalAlignment="Center" FontSize="12"`, and `                                <TextBox Grid.Column="4" Text="{Binding UrgentPct, UpdateSourceTrigger=LostFocus}"` with `                                <TextBox Grid.Column="5" Text="{Binding UrgentPct, UpdateSourceTrigger=LostFocus}"`.

In `SettingsViewModel.cs`, delete the line

```csharp
    public string TrademarkNotice => SettingsNames.TrademarkNotice;
```

then confirm nothing binds it: `git grep -n "Binding TrademarkNotice" -- windows-dotnet/src` prints nothing.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~SettingsReachTests|FullyQualifiedName~SettingsWindowNamesTests"`
Expected: PASS.

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsReachIndexTests"`
Expected: PASS.

Commit:

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/tests/Sanduhr.Tests/SettingsSourceScanTests.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs
git commit -m "feat(settings-v2): index titles from the names table, SettingsIndex scanned, Alerts title and % suffixes"
```

- [ ] **Step 7: Retire the `ExportCsv` alias (one-line follow-up for slice 1's owner)**

Confirm nothing uses it: `git grep -n "SettingsNames.ExportCsv" -- windows-dotnet` prints nothing. In `SettingsNames.cs`, delete the `ExportCsv` alias line and the doc comment above it ("The old identifier, kept because SettingsIndex.cs (slice 2's file) refers to it. Same text."). Run `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests"`; expected PASS. Commit on its own and ask slice 1's owner to review it:

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsNames.cs
git commit -m "chore(settings-v2): drop the ExportCsv alias now that SettingsIndex uses ExportMeterHistory"
```

---

### Task 20: End to end: every card lands in view at 800 high; the whole suite

**Files:**
- Modify: `windows-dotnet/tools/Sanduhr.RenderHarness/SettingsPassScenario.cs`

**Interfaces:**
- Produces: `deeplinks.txt` lines `CARD-LANDING <id> at 800: <after>; <found>` and `TOUR-LANDING <set> <id> at 800: <after>; <found>`, from `Probe` after `SettingsIndex.Resolve`; the menu entries probed at their `MenuAnchor`.

- [ ] **Step 1: Report each card's landing**

In `SettingsPassScenario.cs`, in `PassLinks`, replace

```csharp
        sb.AppendLine();
        sb.AppendLine("## What's New cards (Show me opens Settings at the card's page, no anchor)");
        foreach (var card in WhatsNewCards.Table)
        {
            sb.AppendLine($"- {card.Version} {card.Id}: \"{card.Title}\" -> {(card.Destination == SettingsPage.Widget ? "the widget (not Settings)" : SettingsNames.PageTitle(card.Destination))}");
            sb.AppendLine($"  body: {card.Body}");
            if (card.Destination != SettingsPage.Widget)
            {
                sb.AppendLine(await TopView(card.Destination, 800));
                sb.AppendLine(await TopView(card.Destination, 600));
            }
        }
        sb.AppendLine();
        sb.AppendLine("## Welcome tour cards (Show me)");
        foreach (var (set, cards) in new[] { ("tour", WelcomeTour.Table), ("tour, Just the Desk", WelcomeTour.DeskOnly) })
            foreach (var t in cards)
            {
                sb.AppendLine($"- {set} step {t.Step} {t.Id}: \"{t.Card.Title}\" [{t.ShowMeTitle}] -> {(t.Card.Destination == SettingsPage.Widget ? "the widget (not Settings)" : SettingsNames.PageTitle(t.Card.Destination))}");
                sb.AppendLine($"  body: {t.Card.Body}");
            }
```

with

```csharp
        sb.AppendLine();
        sb.AppendLine("## What's New cards (Show me opens Settings at the card's page and anchor; CARD-LANDING is the target after the jump)");
        foreach (var card in WhatsNewCards.Table)
        {
            sb.AppendLine($"- {card.Version} {card.Id}: \"{card.Title}\" -> {(card.Destination == SettingsPage.Widget ? "the widget (not Settings)" : SettingsNames.PageTitle(card.Destination) + " / " + card.Anchor)}");
            sb.AppendLine($"  body: {card.Body}");
            if (card.Destination != SettingsPage.Widget)
            {
                var link = SettingsIndex.Resolve(card.Destination, card.Anchor);
                var l8 = await Probe(c, link.Page, link.Anchor, 800);
                var l6 = await Probe(c, link.Page, link.Anchor, 600);
                sb.AppendLine($"  CARD-LANDING {card.Id} at 800: {l8.After}; {l8.Found}");
                sb.AppendLine($"  at 600: {l6.After}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("## Welcome tour cards (Show me; TOUR-LANDING is the target after the jump)");
        foreach (var (set, cards) in new[] { ("tour", WelcomeTour.Table), ("tour, Just the Desk", WelcomeTour.DeskOnly) })
            foreach (var t in cards)
            {
                sb.AppendLine($"- {set} step {t.Step} {t.Id}: \"{t.Card.Title}\" [{t.ShowMeTitle}] -> {(t.Card.Destination == SettingsPage.Widget ? "the widget (not Settings)" : SettingsNames.PageTitle(t.Card.Destination) + " / " + t.Card.Anchor)}");
                sb.AppendLine($"  body: {t.Card.Body}");
                if (t.Card.Destination != SettingsPage.Widget)
                {
                    var link = SettingsIndex.Resolve(t.Card.Destination, t.Card.Anchor);
                    var l8 = await Probe(c, link.Page, link.Anchor, 800);
                    sb.AppendLine($"  TOUR-LANDING {set} {t.Id} at 800: {l8.After}; {l8.Found}");
                }
            }
```

and replace the menu entries

```csharp
            ("Tray menu / widget menu: Open Settings (default page)", SettingsPage.General, null),
            ("Meter card right-click: Alerts link (TierCard)", SettingsPage.Alerts, null),
            ("Account menu (widget chip, tray): the Accounts link", SettingsPage.Accounts, null),
            ("Desk right-click: Edit messages", SettingsPage.DeskMessage, null),
            ("Desk right-click: Desk settings", SettingsPage.Desk, null),
```

with

```csharp
            ("Tray menu / widget menu: Open Settings (first open this session)", SettingsPage.General, null),
            ("Meter card right-click: Alerts link (TierCard)", SettingsPage.Alerts, SettingsIndex.MenuAnchor(SettingsPage.Alerts)),
            ("Account menu (widget chip, tray): the Accounts link", SettingsPage.Accounts, SettingsIndex.MenuAnchor(SettingsPage.Accounts)),
            ("Desk right-click: Edit messages", SettingsPage.DeskMessage, SettingsIndex.MenuAnchor(SettingsPage.DeskMessage)),
            ("Desk right-click: Desk settings", SettingsPage.Desk, SettingsIndex.MenuAnchor(SettingsPage.Desk)),
```

and, so `TopView` keeps describing page-only links, replace

```csharp
            sb.AppendLine($"- {where} -> {SettingsNames.PageTitle(page)}{(anchor is null ? "" : " / " + anchor)}");
```

with

```csharp
            sb.AppendLine($"- {where} -> {SettingsNames.PageTitle(page)}{(anchor is null ? "" : " / " + anchor)}");
            if (anchor is null)
                sb.AppendLine(await TopView(page, 800));
```

- [ ] **Step 2: Build the harness**

Run: `dotnet build tools/Sanduhr.RenderHarness`
Expected: Build succeeded, 0 errors, no new warnings.

- [ ] **Step 3: Run the links part off screen, against a scratch profile**

From the repo root, in PowerShell:

```powershell
$env:SANDUHR_DEV_PROFILE = Join-Path $env:TEMP "sanduhr-slice2-profile"
New-Item -ItemType Directory -Force $env:SANDUHR_DEV_PROFILE | Out-Null
dotnet run --project windows-dotnet/tools/Sanduhr.RenderHarness -- --scenario settings-pass --parts links --out (Join-Path $env:TEMP "sanduhr-slice2-out")
```

Expected: the run ends normally and its log line `settings-pass: foreground window before X, after X` shows the same handle twice. The `links` part moves no keyboard focus (the harness's own note), so it never needs the foreground.

- [ ] **Step 4: Check every landing**

```powershell
Select-String -Path (Join-Path $env:TEMP "sanduhr-slice2-out\settings-pass\deeplinks.txt") -Pattern "(CARD|TOUR)-LANDING" |
    Where-Object { $_.Line -notmatch "at 800: (fully in view|top in view)[^;]*; ok" }
```

Expected: no output. Every What's New card with a page, and every tour card, lands with its target in view at 800 high (spec, slice 2 "Done"). A line here names the card and why ("below the fold", "no element marked", "anchor not in the index"); fix its anchor in `WhatsNewCards.AnchorFor` or `WelcomeTour.AnchorFor` and run Steps 3 and 4 again. The evidence under `%TEMP%` is never committed.

- [ ] **Step 5: The whole suite**

Run: `dotnet test Sanduhr.slnx`
Expected: PASS across `Sanduhr.Tests`, `Sanduhr.App.Tests` and `Sanduhr.Mcp.Tests` (`TrustBoundaryTests`, `SettingsKeysContractTests` untouched and green). On a desktop session the `[DesktopFact]` cases run too, including `Tab_from_the_end_of_every_page_goes_to_the_search_box_never_to_Quit`.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/tools/Sanduhr.RenderHarness/SettingsPassScenario.cs
git commit -m "chore(harness): settings-pass reports where every card and menu link lands"
```

- [ ] **Step 7: Hand the Tier 2 checks to the owner**

Not code, and not for the agent: they need the real app on real screens, which this plan never launches. Post them to the owner as the slice's open checks:

- **Item 5 (WF41):** a 1366x768 screen at 100%, 1080p at 125% and 150%: Settings opens inside the work area with Quit on screen, on the widget's monitor.
- **Item 9 (WF33):** Tab past a page's last control goes to the search box; Quit asks "Quit Sanduhr? The widget and the Desk close."; Cancel keeps everything running; the tray's Quit still quits at once.
- **Item 11 (WF17):** each Desk card's Show me at 125% and 150% lands on its section.
- `Sanduhr.exe --settings desk#pieces` with Sanduhr running opens Settings at Pieces; with Sanduhr not running, it starts and then opens there.

---

## Spec coverage (slice 2)

| Slice 2 bullet (spec) | Task |
| --- | --- |
| Anchors on What's New and tour cards; `WhatsNewTests` anchor check | 5, 11 (Show me passes it), 20 (lands in view) |
| Menu entry points landing on anchors (tier card, account menus shown, Desk menu) | 3, 11 |
| The alias table and `SettingsIndex.Resolve`; `SettingsIndexTests` every alias | 1, 8 (`SelectPage` resolves) |
| Synonyms, inert until slice 3; "synonyms rank as specified" | 4 |
| The "(in Advanced)" suffix, inert until slice 3 | 4, 9 |
| `suggestion` joins the index; an anchor marked but not indexed fails (WF45, WF23) | 4 |
| Reopening on the last page | 3, 8, 11 |
| `--settings` and the SingleInstance argument hand-off; an argument-parse test | 2, 7, 11 |
| Quit moved and confirming (WF33) | 12, 13 |
| Empty-state lines (WF35, WF32 signed-out meters) | 15, 16 |
| Alerts dimming (WF36) | 14 |
| Focus rings, disabled combo (WF38 focus) | 17 |
| Work-area clamp (WF41) | 6, 10, 11 |
| ScrollViewers on Alerts and Publish usage (WF41) | 14 |
| WF26 (scroll) | 18 |
| Done: every card with a page lands in view at 800; Tab never reaches Quit from page content | 20, 13 |
| Tier 2 items 5, 9, 11 | 20 Step 7 |
| Slice 1 hand-offs: index titles, `ExportMeterHistory`, `SettingsIndex.cs` scanned, Alerts title and "%", `TrademarkNotice` deleted | 19 |
