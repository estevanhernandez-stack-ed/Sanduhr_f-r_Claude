# Settings v2 for Windows, Slice 3 ("the Desk split") Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Desk page stops holding fourteen sections. Every piece is switched on and placed in one place, Desk, Pieces, with Show, Order and a link to its page; fonts, sizes and colors move to a new Desk Look page; Watchers and Launcher get pages of their own; Message, Now playing, Weather, Watchers, Launcher and the Claude Code glow say where their piece sits and can place it; turning a feature on never leaves its piece invisible; and the keyboard can reorder a stack, in Settings and in Arrange Desk.

**Architecture:** Core first. `DeskArrangement` gains one table of default places (`DefaultPlaceFor`), `PlaceAtDefault` and `StackPosition`; `DeskArrange` gains the pure arrow-key rules; `SettingsNames` gains the new names; `SettingsIndex` learns to fall back to a retired page's row, which sections start collapsed, and (per page task) the new anchors. Then three empty pages join the sidebar (`DeskLook`, `Watchers`, `Launcher`), so every later link has a target. App: `DeskSettingsViewModel` owns the piece rows (Show, Order, Settings…, a place remembered for the window's life), a `DeskPieceStatusViewModel` per piece for the feature pages, and the companion (moved from `WidgetsPageViewModel`). A routed `SettingsLinkTo.Link` attached property lets links inside page controls open Settings places. The pages then move one at a time (Desk Look, Watchers, Launcher), the Desk page is rewritten in the spec's order with collapsed Designs and Advanced, the map folds on short windows, section headings become headings and four pages get jump links. Arrange Desk takes the arrow keys. Last, the old cards and links are checked against the new pages and the harness reports where every new anchor lands.

**Tech Stack:** .NET 10, WPF, xUnit 2.9, CommunityToolkit.Mvvm 8.4 (two-argument `On…Changed` hooks), the render harness (`tools/Sanduhr.RenderHarness`).

**Spec:** `docs/settings-v2-windows/spec.md`, "Shippable slices, Slice 3: the Desk split" (the authority on scope), with the page sections "Desk (`desk`): the split", "Desk Look", "Message", "Now playing, Weather", "Watchers", "Launcher", and "Principles", "Sidebar", "Search", "Deep links and entry points", "Keyboard and screen reader", "Short windows", "Mapping" and "Settings keys and migrations". Findings WF5, WF8, WF11, WF12, WF22, WF26, WF32 in `docs/settings-v2-windows/findings.md`. Read them before starting. The two executed plans, `docs/superpowers/plans/2026-10-24-settings-v2-slice-1.md` and `…-slice-2.md`, are the models for conventions and helpers (`AppWindowFixture`, `ScratchWidget`, `NoActivate`, `Pump`, `SettingsAnchor.Find`).

## Spec questions decided in this plan

1. **`DefaultPlaceFor` is a new table; `DefaultAnchor` stays.** `DefaultAnchor` is the layout and design formats' fallback for an unknown anchor, and `DeskArrangement.cs`, `DeskDesign.cs` and `DeskDesignValidator.cs` are linked into the MCP server, so changing it would change what `get_desk` and `propose_desk` report. Every place Settings or Arrange *shows* a piece (the Show box, "Place it on the Desk", a feature's on-action, Arrange's Hidden strip, the companion install, the glow) uses `DefaultPlaceFor`. "Watchers right" is the middle of the right edge (`MR`). **The values are the spec's proposals, used as written and owner-confirmable: each is one line in `DefaultPlaceFor` (Task 1).**
2. **Show again in the same window returns to the last place.** Unchecking Show remembers the place for the window's life (spec, Pieces); checking it again, or "Place it on the Desk" in the same window, uses that place, otherwise `DefaultPlaceFor`.
3. **What "turning on" means where a page has no on switch.** Weather: picking a place from the results (the moment a city is set). Now playing: turning on "Bars beside it on the Desk" or "Style what's playing" (the two things it draws on the Desk; "Show what's playing in the widget" is the widget's). Message: Add line. Watchers: either switch. Launcher: the first button (as today). Claude Code glow: choosing the Desk as the target (as today). Each places the piece only when it is Hidden; a placed piece never moves.
4. **The sidebar's group order is not changed in this slice.** The spec's table puts Desktop right after Usage; today it sits after Extend and Widget. Moving it means moving every Desktop TabItem block above Claude Code in `SettingsWindow.xaml`, which collides with slices 4 and 5 running in parallel. The new pages are slotted inside Desktop in the spec's order. Carried to slice 8.
5. **The companion's empty line names today's page.** The spec's "No companion yet. Get one in Mods & Widgets…" names a page slice 4 creates. Slice 3 reads "Get one in Widgets…" and links to `widgets#widgets-catalog`, which the alias table carries to `mods#catalog` when slice 4 lands. The "filtered to sprites" part needs slice 4's filters and is carried there.
6. **Designs and Advanced are collapsed sections.** The `desk-direct` anchor goes (its alias to `desk-designs` goes live). Names in the names table whose anchor is a collapsed section get its name as `CollapsedIn` through one map, `SettingsIndex.CollapsedSections`, so "Let Claude change the Desk directly" reads "(in Designs)". Landing on a collapsed section's own anchor opens it (slice 2's `RevealInside` already starts at the target).
7. **Jump links go on the four slice-3 pages with four or more sections:** Desk, Desk Look, Now playing (Message has three once Look leaves). Other pages with four or more sections get theirs in their own slices.
8. **The status line names the Desk being off.** A piece placed while the Desk is off would read "On the Desk: bottom right." and show nothing, so the line adds "The Desk is off: turn it on in Settings, Desk."
9. **Place keeps its Hidden entry.** Show and Place's Hidden write the same value; the harness and existing keyboard paths use Place, and removing Hidden from the list would leave a hidden piece's Place combo blank.

## Global Constraints

- **CRLF working tree, LF index.** Edit tracked files with the Edit tool only. Never `sed -i`, never rewrite a tracked file wholesale with Write. New files may be created with Write.
- **Keys.** No `settings.json` key is renamed, removed or added in this slice (spec, "Settings keys and migrations": the only two additions, `update_check` and `streamer_mode`, belong to slices 6 and 7). `SettingsKeysContractTests` is not touched. Every moved control keeps its key: `desk_layout`, `desk_font`, `desk_time_size`, `desk_plate`, `desk_ink`, `desk_margins`, `desk_oled_care`, `desk_direct`, `desk_pieces_take_clicks`, `desk_sign_in_reminder`, `watchers_agents`, `watchers_ci_runners`, `desk_launcher`, `desk_companion.pack`, `desk_companion.roam`, `desk_message.size`, `.color`, `.glow`, `.font`. The remembered place, the map's fold and the Order buttons write nothing new.
- **Trust boundary.** No file is added to `Sanduhr.Mcp.csproj`'s `Compile Include` list. `DeskArrangement.cs` and `DeskDesign.cs` are already linked: what this slice adds to `DeskArrangement.cs` is pure (no IO, network or process names) and names nothing outside the linked set. `SettingsIndex.cs`, `SettingsNames.cs`, `WindowFit.cs`, `DeskArrange.cs` are not linked and must not become linked. `TrustBoundaryTests` stays green untouched. The Mcp edits are two strings (`ToolLogic.Watchers.cs`, `ToolCatalog.cs`).
- **Hands off the live app.** Never start, stop, kill or attach to `Sanduhr.exe`. If a build fails because a `Sanduhr.exe` holds a file, stop and ask the owner. No test reads the real AppData, LocalAppData, `~\.sanduhr` or Credential Manager: App tests derive from `AppWindowFixture` (scratch `SANDUHR_DEV_PROFILE`) and build widgets with `ScratchWidget(...)`, or build stores over a scratch `Paths(appDataBase: …, localAppDataBase: …)`. Never `DemoWidget()` in a new test.
- **Hands off the foreground.** Every App test that shows a window shows it through `NoActivate.Show(window)` and calls `guard.Check()` after anything that moves focus. Keyboard focus is routed with `guard.RouteKeyboard()` (WM_SETFOCUS only). `[DesktopFact]` tests now run locally (the owner lifted the input rule) and must pass before the slice is done. **Never re-run a UI test in a loop**: a `[DesktopFact]` that fails is diagnosed from its message, fixed, and run once more.
- **Run tests from `windows-dotnet/`.** `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~<Class>` for Core, `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~<Class>` for App, `dotnet test tests/Sanduhr.Mcp.Tests` for the server.
- **Names.** Every new visible or spoken string is a `SettingsNames` member or built from one; buttons that open Settings read "<Page> settings…" (`SettingsNames.LinkTitle`); prose that names a place uses "Settings, <Page>, <Section>" with a section title from `SettingsIndex` (`PathProblems` checks it). A control's spoken name starts with its visible text.
- **Index with the XAML.** An anchor joins `SettingsIndex.Anchors` in the same commit that marks it in XAML, and leaves it in the commit that removes the mark (two scan tests check both ways). When an anchor leaves, its alias target must be live in the same commit (`WhatsNewTests.Every_Settings_card_names_an_anchor_in_the_index` resolves every card through the aliases).
- **Own-file pages.** New pages are `UserControl`s named `<Name>Page` in `Views/` (the anchor scans read `<views:\w+Page>` files). They use the window's styles through `DynamicResource` (`FlatButton`, `AccentButton`, `SettingsLinkButton`, `ThemedCheckBox`, `ThemedComboBox`, `ThemedSlider`, `Input`), never a local style with `FocusVisualStyle="{x:Null}"` and no keyboard-focus trigger (P25), and their `ScrollViewer` is `Focusable="False"` (slice 2 P5).
- **Default places are owner-confirmable** (spec, Desk: "proposals for the owner to confirm in slice 3"). They live in `DeskArrangement.DefaultPlaceFor` only; a change is one line there plus its test row. The owner pre-approved executing slice 3, so there is no plan-review stop.
- **Branch.** Tasks run in numeric order on `feat/4.0-slice-3`, cut from `release/4.0` after slice 2 merges. Slices 4 and 5 run in parallel: each slice owns its pages' XAML blocks; rebase on `SettingsWindow.xaml` conflicts (see "Merge surface").

## Review Focus

1. **A feature's on-action never moves a placed piece and never writes when nothing changes.** Picking a second city, turning Bars off and on, adding a second message line: the piece stays where the person put it, and `desk_layout` is not rewritten. Pinned in Task 10 (`An_on_action_leaves_a_placed_piece_where_it_is`) and Task 7 (`PlaceIfHidden_does_nothing_to_a_placed_piece`).
2. **While Arrange mode runs, nothing in Settings writes `desk_layout`.** Arrange's Done writes its working layout over the saved one, so a Show, an Order button, a "Place it on the Desk" or a feature's on-action in that window would be lost or would fight it. The Pieces rows are already disabled (`NotArranging`); `PlaceIfHidden` returns null and the status line's button is disabled while `Arranging`. Pinned in Task 7 (`Nothing_places_while_Arrange_runs`).
3. **A status line follows the layout wherever it changes.** Arrange's Done, the Pieces rows, a design applied by Claude, Reset and another page's on-action all change `desk_layout`; each feature page's line must say the new place without reopening Settings. The line listens to `DeskSettingsViewModel.Layout`, which `Reload()` (App calls it after Arrange, a design and a companion install) and every save raise. Pinned in Task 7 (`The_status_line_follows_a_reload`).
4. **Old links land on the new pages.** Cards, tour cards, agent prose and `--settings` links written before 4.0 name `desk#look`, `desk#ink`, `desk#desk-plate`, `desk#desk-watchers`, `desk#desk-launcher`, `desk#desk-direct`, `deskmessage#message-look`, `alerts#signin-reminders` and `widgets#widgets-companion`; each must land on its new home, and an alias whose target is a later slice's page must fall back to the retired page's own row. Pinned in Task 4 (`An_anchor_row_whose_target_is_missing_falls_back_to_the_page_row`) and Task 19 (`Every_slice_3_alias_lands_on_its_new_home`, and the window-level `Old_desk_links_land_on_the_new_pages`).
5. **A short window and a collapsed section never hide a landing.** Below 600 DIP the map folds; a search hit or a link to `map`, `margins`, `desk-oled`, `desk-direct`'s new home or a Designs control opens what it sits in. Pinned in Task 14 (`A_hit_inside_Advanced_opens_it`) and Task 15 (`A_link_to_the_folded_map_opens_it`).

## Where this plan steps outside the spec's slice 3 file list

The spec's list is the core; these extra files are needed to deliver its own bullets:

- New page controls `Views/DeskLookPage.xaml(.cs)`, `Views/WatchersPage.xaml(.cs)`, `Views/LauncherPage.xaml(.cs)` (own files, so slices 4 and 5 don't fight over `SettingsWindow.xaml` lines), the shared `Views/DeskPieceStatusPanel.xaml(.cs)` and `Views/SectionJumpLinks.cs`.
- New view models `ViewModels/DeskPieceStatusViewModel.cs`, `ViewModels/DeskCompanionViewModel.cs` (the companion moves out of `WidgetsPageViewModel`).
- `Views/SettingsReach.cs` (the `SettingsLinkTo` attached property), `Views/WidgetsPage.xaml` (the companion block leaves), `Views/DeskMessagePage.xaml` (Look leaves; its status line; heading level), `ViewModels/DeskMessagePageViewModel.cs`, `ViewModels/DeskContentPagesViewModels.cs` (Now playing and Weather status and on-actions), `ViewModels/LauncherSettingsViewModel.cs` (its notes), `ViewModels/DesignsSettingsViewModel.cs` (the summary), `ViewModels/SettingsViewModel.cs` (wiring; the Quit strings move to the names table), `Services/SettingsStore.cs` (exposes its `Paths`), `Desk/DeskClicks.cs` and `App.xaml.cs` (the Launcher menu), `Desk/WidgetReviewService.cs` (its two companion placements use the table), `Desk/DeskArrangeBar.xaml.cs` is unchanged (it reads `DeskArrangeCopy`).
- Core: `WindowFit.cs` (the map fold rule), `WhatsNewCards.cs` and `WelcomeTour.cs` (bodies name exact places), `DeskArrange.cs` (arrow rules and copy).
- Mcp: `ToolLogic.Watchers.cs`, `ToolCatalog.cs` (one string each, WF18).
- Harness: `tools/Sanduhr.RenderHarness/SettingsPassScenario.cs` (menu entries; `RouteKeyboard` drops `WM_ACTIVATE`).
- Tests: new `tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs`; new `tests/Sanduhr.App.Tests/DeskPiecesSettingsTests.cs`, `DeskSplitPagesTests.cs`; additions to the files named in each task.

Not in this slice, though the spec mentions it nearby: the sidebar group order (decision 4), the companion catalog filter (decision 5), jump links on other pages (decision 7), What's New 4.0 cards (slice 7), Mods & Widgets and the Claude Code folder cards (slice 4).

## Merge surface with slices 4 and 5

| File | Slice 3 touches | Slice 4 or 5 likely touches | Risk |
| --- | --- | --- | --- |
| `Views/SettingsWindow.xaml` | resources (one `SectionExpander` style), the Alerts sign-in block (deleted), the Desk TabItem (rewritten), three new TabItems, the Now playing and Weather TabItems (status line, prose, heading levels), one `DeskPieceStatus` in the Claude Code glow-where section | Claude Code and Mods TabItems (slice 4), Alerts, Accounts, History, Claude Usage (slice 5) | Medium: the Alerts block (slice 5) and the glow-where section (slice 4). Each edit is a whole block; rebase keeps the other slice's block. |
| `Core/SettingsIndex.cs` | the Desk, Message, Alerts sign-in, Widgets companion and new pages' rows of `Anchors`, `NameAnchors`, `PageSynonyms`, `MenuAnchor`, `Resolve`, two new members | slice 4 (Claude Code, Mods rows), slice 5 (Alerts `each-limit`, History rows) | Medium: rows are line-per-entry, so conflicts are local. |
| `Core/SettingsNames.cs` | `Sidebar` (Desktop group), `PageTitle`, `TitlesToCome`, `Table` rows for Desk pieces, new names | slice 4 (`Mods` title, Extend group), slice 5 (Widget page into Widget group, Alerts rows) | Medium: `Sidebar` and `TitlesToCome` are shared lines; keep both sides' entries. |
| `ViewModels/WidgetsPageViewModel.cs`, `Views/WidgetsPage.xaml` | the companion block leaves | slice 4 merges Widgets into Mods & Widgets | Medium: land slice 3's removal first or rebase slice 4 on it. |
| `ViewModels/ClaudeCodeViewModel.cs` | `PlaceGlowSpot` (one line) | slice 4 (folder cards) | Low |

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Sanduhr.Core/DeskArrangement.cs` (modify) | `DefaultPlaceFor`, `PlaceAtDefault`, `StackPosition`; `Show` uses the table. |
| `src/Sanduhr.Core/DeskArrange.cs` (modify) | `DeskArrangeArrow`, `ArrowKey`, `Step`; the bar's key line and the Settings note. |
| `src/Sanduhr.Core/SettingsNames.cs` (modify) | Slice 3 names; the three pages in `Sidebar` and `PageTitle`; `DeskPiecePage`; `Table` rows and `Retired` entries as controls move; Quit strings. |
| `src/Sanduhr.Core/SettingsIndex.cs` (modify) | New anchor constants and rows per page; `Resolve`'s page-row fallback; `CollapsedSections`; `MenuAnchor(Launcher)`; `JumpLinks`; `E(…, collapsedIn)`. |
| `src/Sanduhr.Core/WhatsNew.cs` (modify) | `SettingsPage.DeskLook`, `Watchers`, `Launcher`. |
| `src/Sanduhr.Core/WindowFit.cs` (modify) | `MapFoldsBelow`, `MapFolds`. |
| `src/Sanduhr.Core/WhatsNewCards.cs`, `WelcomeTour.cs` (modify) | Bodies name the exact place. |
| `src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs` (modify) | Piece rows (Show, Order, Settings…, Companion), `PlaceIfHidden`, the remembered place, `PieceStatus`, ink presets shown without Custom, watchers on-actions. |
| `src/Sanduhr.App/ViewModels/DeskPieceStatusViewModel.cs` (create) | A feature page's piece line. |
| `src/Sanduhr.App/ViewModels/DeskCompanionViewModel.cs` (create) | The companion's sprite, roaming and status (from `WidgetsPageViewModel`). |
| `src/Sanduhr.App/Views/DeskPiecesPanel.xaml` (modify) | Wrapping rows with Show, Place, Size, Monitor, Style or link or companion, Order. |
| `src/Sanduhr.App/Views/DeskPieceStatusPanel.xaml(.cs)` (create) | The status line control. |
| `src/Sanduhr.App/Views/DeskLookPage.xaml(.cs)`, `WatchersPage.xaml(.cs)`, `LauncherPage.xaml(.cs)` (create) | The new pages. |
| `src/Sanduhr.App/Views/SectionJumpLinks.cs` (create) | A page's jump links. |
| `src/Sanduhr.App/Views/SettingsReach.cs` (modify) | `SettingsLinkTo.Link`. |
| `src/Sanduhr.App/Views/SettingsWindow.xaml(.cs)` (modify) | TabItems, Desk rewrite, link routing, map fold. |
| `src/Sanduhr.App/Desk/DeskArrangeMode.cs` (modify) | Arrow keys on the focused piece. |
| `tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs` (create) | Core index, names and alias tests for this slice. |
| `tests/Sanduhr.App.Tests/DeskPiecesSettingsTests.cs` (create) | Piece rows and status VMs. |
| `tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs` (create) | The pages in a real window: landings, folding, jump links, tab stops, the Done criteria. |

---

### Task 1: One table of default places

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/DeskArrangement.cs` (after `DefaultAnchor`, ~line 362; `Show`, ~line 507)
- Test: `windows-dotnet/tests/Sanduhr.Tests/DeskArrangementTests.cs`, `windows-dotnet/tests/Sanduhr.Tests/DeskArrangeTests.cs`

**Interfaces:**
- Produces: `public static DeskAnchor DeskArrangement.DefaultPlaceFor(string widget)`; `public DeskAnchor? PlaceAtDefault(string widget, DeskAnchor? remembered = null)` (null when already placed or unknown); `public (int Index, int Count) StackPosition(string widget)` ((-1, 0) when hidden). `Show(widget)` now seats at `DefaultPlaceFor`.

- [ ] **Step 1: Write the failing tests**

Append to the class in `windows-dotnet/tests/Sanduhr.Tests/DeskArrangementTests.cs`:

```csharp
    // ---- Settings v2, slice 3: default places (spec, Desk: the split). Owner-confirmable: one line each in DefaultPlaceFor. ----

    [Theory]
    [InlineData("message", DeskAnchor.BL)]
    [InlineData("nowPlaying", DeskAnchor.BR)]
    [InlineData("weather", DeskAnchor.TR)]
    [InlineData("watchers", DeskAnchor.MR)]
    [InlineData("launcher", DeskAnchor.TL)]
    [InlineData("companion", DeskAnchor.BC)]
    [InlineData("telemetry", DeskAnchor.TR)]
    [InlineData("clock", DeskAnchor.BL)]
    [InlineData("claude", DeskAnchor.BL)]
    [InlineData("meters", DeskAnchor.BL)]
    [InlineData("glow", DeskAnchor.TC)]
    public void DefaultPlaceFor_is_the_specs_table(string widget, DeskAnchor expected) =>
        Assert.Equal(expected, DeskArrangement.DefaultPlaceFor(widget));

    [Fact]
    public void The_layout_formats_fallback_is_unchanged()
    {
        // DefaultAnchor is what a layout or a design falls back to for an anchor it can't read; the MCP server reports it.
        Assert.Equal(DeskAnchor.TL, DeskArrangement.DefaultAnchor("message"));
        Assert.Equal(DeskAnchor.MR, DeskArrangement.DefaultAnchor("weather"));
        Assert.Equal(DeskAnchor.BR, DeskArrangement.DefaultAnchor("companion"));
        Assert.Equal(DeskAnchor.TL, new DeskArrangement("message:zz").Placement("message")!.Anchor);
    }

    [Fact]
    public void PlaceAtDefault_places_a_hidden_piece_and_says_where()
    {
        var a = new DeskArrangement(DeskArrangement.Standard);
        Assert.Equal(DeskAnchor.TR, a.PlaceAtDefault("weather"));
        Assert.Equal("clock:bl claude:bl weather:tr", a.Layout);
        Assert.Equal(DeskAnchor.BC, a.PlaceAtDefault("companion", DeskAnchor.BC));
        Assert.Equal(DeskAnchor.TL, a.PlaceAtDefault("launcher", remembered: DeskAnchor.TL));
    }

    [Fact]
    public void PlaceAtDefault_uses_a_remembered_place_first()
    {
        var a = new DeskArrangement(DeskArrangement.Standard);
        Assert.Equal(DeskAnchor.BL, a.PlaceAtDefault("weather", DeskAnchor.BL));
        Assert.Equal(DeskAnchor.BL, a.Placement("weather")!.Anchor);
    }

    [Fact]
    public void PlaceAtDefault_leaves_a_placed_piece_and_an_unknown_word_alone()
    {
        var a = new DeskArrangement("clock:bl weather:bl:1.2");
        Assert.Null(a.PlaceAtDefault("weather"));
        Assert.Null(a.PlaceAtDefault("nope"));
        Assert.Equal("clock:bl weather:bl:1.2", a.Layout);
    }

    [Fact]
    public void StackPosition_counts_the_pieces_that_share_a_place()
    {
        var a = new DeskArrangement("clock:bl claude:bl weather:tr");
        Assert.Equal((0, 2), a.StackPosition("clock"));
        Assert.Equal((1, 2), a.StackPosition("claude"));
        Assert.Equal((0, 1), a.StackPosition("weather"));
        Assert.Equal((-1, 0), a.StackPosition("message"));
    }
```

In `windows-dotnet/tests/Sanduhr.Tests/DeskArrangeTests.cs`, in `ShowSeatsAHiddenPieceAtItsStandardAnchorAtSizeOne`, replace

```csharp
        a.Show("message");
        Assert.Equal(DeskAnchor.TL, a.Placement("message")!.Anchor);
        a.Show("weather");
        Assert.Equal(DeskAnchor.MR, a.Placement("weather")!.Anchor);
```

with

```csharp
        a.Show("message");
        Assert.Equal(DeskAnchor.BL, a.Placement("message")!.Anchor);   // DefaultPlaceFor (slice 3)
        a.Show("weather");
        Assert.Equal(DeskAnchor.TR, a.Placement("weather")!.Anchor);
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DeskArrangementTests|FullyQualifiedName~DeskArrangeTests"`
Expected: build FAILS with `'DeskArrangement' does not contain a definition for 'DefaultPlaceFor'`.

- [ ] **Step 3: Add the table**

In `windows-dotnet/src/Sanduhr.Core/DeskArrangement.cs`, after the `DefaultAnchor` switch (its closing `};`), add:

```csharp

    /// <summary>
    /// Where Settings and Arrange put a piece they show (Settings v2, Principle 4): Desk, Pieces' Show box, "Place it on the
    /// Desk", a feature's own on-action, Arrange's Hidden strip, a companion install and the glow. One table, so a place is one
    /// line to change. The rows marked "proposal" are the spec's proposals for the owner to confirm (settings-v2 spec, Desk);
    /// the clock, the Claude meters and the glow keep the place they had before v2. <see cref="DefaultAnchor"/> stays the
    /// layout and design formats' fallback for an anchor this build can't read.
    /// </summary>
    public static DeskAnchor DefaultPlaceFor(string widget) => widget switch
    {
        "message" => DeskAnchor.BL,      // proposal: bottom left
        "nowPlaying" => DeskAnchor.BR,   // proposal: bottom right
        "weather" => DeskAnchor.TR,      // proposal: top right
        "watchers" => DeskAnchor.MR,     // proposal: right (the middle of the right edge)
        "launcher" => DeskAnchor.TL,     // proposal: top left
        "companion" => DeskAnchor.BC,    // proposal: bottom center
        "telemetry" => DeskAnchor.TR,    // proposal: top right
        _ => DefaultAnchor(widget),      // clock, claude, meters, meetings: bottom left; glow: top center (as before v2)
    };

    /// <summary>Places a hidden <paramref name="widget"/> at <paramref name="remembered"/> or else <see cref="DefaultPlaceFor"/>,
    /// seated by Settings order, and returns where. Null, with nothing changed, when it is already placed or this build doesn't
    /// know the word.</summary>
    public DeskAnchor? PlaceAtDefault(string widget, DeskAnchor? remembered = null)
    {
        if (Placement(widget) is not null || !Known.Contains(widget, StringComparer.Ordinal)) return null;
        var at = remembered ?? DefaultPlaceFor(widget);
        Place(widget, at);
        return at;
    }

    /// <summary>Where <paramref name="widget"/> sits in its place's stack, top first, and how many pieces share the place;
    /// (-1, 0) for a hidden piece. Desk, Pieces' Order buttons read it.</summary>
    public (int Index, int Count) StackPosition(string widget)
    {
        if (Placement(widget) is not { } p) return (-1, 0);
        var stack = Stack(p.Anchor);
        return (stack.ToList().FindIndex(x => x.Widget == widget), stack.Count);
    }
```

and change `Show`:

```csharp
    /// <summary>
    /// Puts a hidden <paramref name="widget"/> back at its <see cref="DefaultPlaceFor"/> place at size 1, seated by Settings
    /// order. A placed piece stays where it is; a word this build does not know is ignored.
    /// </summary>
    public void Show(string widget) => PlaceAtDefault(widget);
```

(replacing the old `Show` summary and body, which called `Place(widget, DefaultAnchor(widget))`).

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DeskArrangementTests|FullyQualifiedName~DeskArrangeTests|FullyQualifiedName~DeskRideAlongsTests|FullyQualifiedName~DeskMonitorsTests"`
Expected: PASS. Then `dotnet test tests/Sanduhr.Mcp.Tests --filter FullyQualifiedName~TrustBoundaryTests`: PASS (the file stays pure).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/DeskArrangement.cs windows-dotnet/tests/Sanduhr.Tests/DeskArrangementTests.cs windows-dotnet/tests/Sanduhr.Tests/DeskArrangeTests.cs
git commit -m "feat(desk): one table of default places, the spec's proposals, owner-confirmable"
```

---

### Task 2: Arrange Desk's arrow keys, the pure rules

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/DeskArrange.cs` (after `EndKey`; `DeskArrangeCopy.SettingsNote` and `BarKeys`)
- Test: `windows-dotnet/tests/Sanduhr.Tests/DeskArrangeTests.cs`

**Interfaces:**
- Produces: `public enum DeskArrangeArrowKind { Place, Order }`; `public sealed record DeskArrangeArrow(DeskArrangeArrowKind Kind, int Dx, int Dy)`; `public static DeskArrangeArrow? DeskArrange.ArrowKey(string? keyName, bool shift)`; `public static DeskAnchor? DeskArrange.Step(DeskAnchor from, int dx, int dy)`. Key names are WPF's `Key` names: "Left", "Right", "Up", "Down".

- [ ] **Step 1: Write the failing tests**

In `windows-dotnet/tests/Sanduhr.Tests/DeskArrangeTests.cs`, replace the two copy assertions

```csharp
        Assert.Equal("Return or Done keeps the new layout. Escape or Cancel puts it back.", DeskArrangeCopy.BarKeys);
        Assert.Contains("Return or Done keeps the new layout; Escape or Cancel puts it back", DeskArrangeCopy.SettingsNote);
```

with

```csharp
        Assert.Equal("Tab picks a piece; the arrow keys move it to the next place, and Shift+Up or Shift+Down moves it in its stack. "
            + "Return or Done keeps the new layout. Escape or Cancel puts it back.", DeskArrangeCopy.BarKeys);
        Assert.Contains("Return or Done keeps the new layout; Escape or Cancel puts it back", DeskArrangeCopy.SettingsNote);
        Assert.EndsWith("Or use the Order buttons below.", DeskArrangeCopy.SettingsNote);
```

and append to the class:

```csharp
    // ---- Settings v2, slice 3: the arrow keys (spec, Keyboard and screen reader) ----

    [Theory]
    [InlineData("Left", false, DeskArrangeArrowKind.Place, -1, 0)]
    [InlineData("Right", false, DeskArrangeArrowKind.Place, 1, 0)]
    [InlineData("Up", false, DeskArrangeArrowKind.Place, 0, -1)]
    [InlineData("Down", false, DeskArrangeArrowKind.Place, 0, 1)]
    [InlineData("Up", true, DeskArrangeArrowKind.Order, 0, -1)]
    [InlineData("Down", true, DeskArrangeArrowKind.Order, 0, 1)]
    public void Arrows_move_a_piece_and_Shift_arrows_reorder_it(string key, bool shift, DeskArrangeArrowKind kind, int dx, int dy) =>
        Assert.Equal(new DeskArrangeArrow(kind, dx, dy), DeskArrange.ArrowKey(key, shift));

    [Theory]
    [InlineData("Left", true)]
    [InlineData("Right", true)]
    [InlineData("Tab", false)]
    [InlineData("Return", false)]
    [InlineData(null, false)]
    public void Other_keys_are_not_arrows(string? key, bool shift) => Assert.Null(DeskArrange.ArrowKey(key, shift));

    [Theory]
    [InlineData(DeskAnchor.TL, 1, 0, DeskAnchor.TC)]
    [InlineData(DeskAnchor.TC, 1, 0, DeskAnchor.TR)]
    [InlineData(DeskAnchor.TC, 0, 1, DeskAnchor.BC)]   // the open middle of the screen is stepped over
    [InlineData(DeskAnchor.ML, 1, 0, DeskAnchor.MR)]
    [InlineData(DeskAnchor.TL, 0, 1, DeskAnchor.ML)]
    [InlineData(DeskAnchor.BR, -1, 0, DeskAnchor.BC)]
    public void Step_goes_to_the_next_place_that_way(DeskAnchor from, int dx, int dy, DeskAnchor to) =>
        Assert.Equal(to, DeskArrange.Step(from, dx, dy));

    [Theory]
    [InlineData(DeskAnchor.TL, -1, 0)]
    [InlineData(DeskAnchor.TL, 0, -1)]
    [InlineData(DeskAnchor.BR, 1, 0)]
    [InlineData(DeskAnchor.BR, 0, 1)]
    public void Step_stops_at_the_edge(DeskAnchor from, int dx, int dy) => Assert.Null(DeskArrange.Step(from, dx, dy));
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~DeskArrangeTests`
Expected: build FAILS: `DeskArrangeArrow` and `DeskArrange.ArrowKey` don't exist.

- [ ] **Step 3: Write the rules and the copy**

In `windows-dotnet/src/Sanduhr.Core/DeskArrange.cs`, after `public enum DeskArrangeEnd { Done, Cancel }` add:

```csharp

/// <summary>What an arrow key does in Arrange mode (spec, Keyboard and screen reader): move the chosen piece to the next place
/// (<see cref="Place"/>), or up or down its stack (<see cref="Order"/>).</summary>
public enum DeskArrangeArrowKind { Place, Order }

/// <summary>An arrow key read: its kind and its direction (dx: -1 left, +1 right; dy: -1 up, +1 down).</summary>
public sealed record DeskArrangeArrow(DeskArrangeArrowKind Kind, int Dx, int Dy);
```

Inside `public static class DeskArrange`, after `EndKey`, add:

```csharp

    /// <summary>
    /// What an arrow key does to the piece the keyboard is on: a plain arrow moves it to the next place that way, Shift+Up and
    /// Shift+Down move it up or down its stack; null for any other key (Shift+Left and Shift+Right included). The caller maps
    /// its key to "Left", "Right", "Up" and "Down".
    /// </summary>
    public static DeskArrangeArrow? ArrowKey(string? keyName, bool shift) => (keyName, shift) switch
    {
        ("Up", true) => new(DeskArrangeArrowKind.Order, 0, -1),
        ("Down", true) => new(DeskArrangeArrowKind.Order, 0, 1),
        ("Left", false) => new(DeskArrangeArrowKind.Place, -1, 0),
        ("Right", false) => new(DeskArrangeArrowKind.Place, 1, 0),
        ("Up", false) => new(DeskArrangeArrowKind.Place, 0, -1),
        ("Down", false) => new(DeskArrangeArrowKind.Place, 0, 1),
        _ => null,
    };

    /// <summary>The place one step from <paramref name="from"/> that way, stepping over the open middle of the screen; null at
    /// the screen's edge.</summary>
    public static DeskAnchor? Step(DeskAnchor from, int dx, int dy)
    {
        int col = (int)DeskAnchors.Column(from) + dx, row = (int)DeskAnchors.Row(from) + dy;
        while (col is >= 0 and <= 2 && row is >= 0 and <= 2 && (dx != 0 || dy != 0))
        {
            if (DeskAnchors.At((DeskColumn)col, (DeskRow)row) is { } at) return at;
            col += dx;
            row += dy;
        }
        return null;
    }
```

In `DeskArrangeCopy`, replace

```csharp
    public const string SettingsNote = "Move and reorder the pieces on the desktop itself. Return or Done keeps the new layout; Escape or Cancel puts it back as it was.";
```

with

```csharp
    public const string SettingsNote = "Move and reorder the pieces on the desktop itself. Return or Done keeps the new layout; Escape or Cancel puts it back as it was. Or use the Order buttons below.";
```

and

```csharp
    public const string BarKeys = "Return or Done keeps the new layout. Escape or Cancel puts it back.";
```

with

```csharp
    public const string BarKeys = "Tab picks a piece; the arrow keys move it to the next place, and Shift+Up or Shift+Down moves it in its stack. "
        + "Return or Done keeps the new layout. Escape or Cancel puts it back.";
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~DeskArrangeTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/DeskArrange.cs windows-dotnet/tests/Sanduhr.Tests/DeskArrangeTests.cs
git commit -m "feat(desk): Arrange Desk's arrow-key rules and the bar's key line"
```

---
### Task 3: The slice's names, and the Quit strings into the table

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (a new block before `// ---- Core words (WF21)`)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs:126-140`
- Test: `windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs` (create)

**Interfaces:**
- Produces (all on `SettingsNames`): consts `PiecesSection` ("Pieces"), `DeskBehavior` ("Behavior"), `DeskAdvanced` ("Advanced"), `DeskLayoutMap` ("Layout map"), `DeskMapCaption`, `MoveUp`, `MoveDown`, `PlaceOnDesk`, `NotOnDesk`, `DeskIsOff`, `FontsSection`, `SizesSection`, `InkAndColor`, `BackPlate`, `ClockSize`, `MessageSize`, `GlowAroundMessage`, `DropShadow`, `WatchersPlace`, `LauncherPlace`, `LauncherButtons`, `CiRunnersCaption`, `WatchersLine`, `CompanionLine`, `NoCompanionYet`, `QuitQuestion`, `QuitConfirm`, `QuitCancel`; methods `MoveUpIn(string piece, string place)`, `MoveDownIn(string piece, string place)`, `PieceShownSpoken(string piece)`, `PieceStatus(string? place, bool deskOn)`, `DesignsSummary(int saved, bool canPutBack)`; property `GetACompanion`. None is used by the app yet except the Quit strings; no `Table` or `Retired` row changes here (they move with their controls).

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 3 (spec, "Desk: the split" and the pages it creates): the names, the index rows, the aliases that
/// go live and the jump links.</summary>
public class SettingsDeskSplitIndexTests
{
    [Fact]
    public void A_feature_page_says_where_its_piece_sits()
    {
        Assert.Equal("On the Desk: bottom right.", SettingsNames.PieceStatus("Bottom right", deskOn: true));
        Assert.Equal("On the Desk: top right. The Desk is off: turn it on in Settings, Desk.", SettingsNames.PieceStatus("Top right", deskOn: false));
        Assert.Equal("Not on the Desk.", SettingsNames.PieceStatus(null, deskOn: true));
        Assert.Equal("Place it on the Desk", SettingsNames.PlaceOnDesk);
        Assert.Empty(SettingsNames.PathProblems(SettingsNames.DeskIsOff));
    }

    [Fact]
    public void The_Order_buttons_say_which_piece_and_where()
    {
        Assert.Equal("Move up", SettingsNames.MoveUp);
        Assert.Equal("Move down", SettingsNames.MoveDown);
        Assert.Equal("Move Weather up in bottom left", SettingsNames.MoveUpIn("Weather", "Bottom left"));
        Assert.Equal("Move Clock and date down in top right", SettingsNames.MoveDownIn("Clock and date", "Top right"));
        Assert.Equal("Weather, on the Desk", SettingsNames.PieceShownSpoken("Weather"));
    }

    [Fact]
    public void The_new_pages_open_with_their_core_words()
    {
        Assert.Equal("Watchers are " + SettingsNames.DefineWatchers + ".", SettingsNames.WatchersLine);
        Assert.Equal("The companion is " + SettingsNames.DefineCompanion + ".", SettingsNames.CompanionLine);
        Assert.Equal("Get one in Widgets…", SettingsNames.GetACompanion);   // follows PageTitle; slice 4 moves the catalog
        Assert.Equal("Where each piece sits now. Move pieces with the rows below or Arrange Desk…", SettingsNames.DeskMapCaption);
    }

    [Fact]
    public void The_Designs_summary_counts_and_says_when_put_back_is_ready()
    {
        Assert.Equal("None saved yet.", SettingsNames.DesignsSummary(0, canPutBack: false));
        Assert.Equal("1 saved.", SettingsNames.DesignsSummary(1, canPutBack: false));
        Assert.Equal("3 saved. Put my Desk back is ready.", SettingsNames.DesignsSummary(3, canPutBack: true));
    }

    [Fact]
    public void Quit_asks_in_the_tables_words() =>
        Assert.Equal(("Quit Sanduhr? The widget and the Desk close.", "Quit", "Cancel"),
            (SettingsNames.QuitQuestion, SettingsNames.QuitConfirm, SettingsNames.QuitCancel));
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsDeskSplitIndexTests`
Expected: build FAILS: `SettingsNames` has no `PieceStatus`.

- [ ] **Step 3: Add the names**

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, insert before the line `    // ---- Core words (WF21): each is defined once, used as its home page's first line and in the tour.`:

```csharp
    // ---- Settings v2, slice 3: the Desk split ----

    /// <summary>Desk: the piece rows' section (was "Where each piece sits").</summary>
    public const string PiecesSection = "Pieces";
    /// <summary>Desk: the clicks switch and the sign-in reminders.</summary>
    public const string DeskBehavior = "Behavior";
    /// <summary>Desk: the collapsed section holding Margins and OLED care.</summary>
    public const string DeskAdvanced = "Advanced";
    /// <summary>Desk: the map at the top of the page, and its fold's name below 600 DIP.</summary>
    public const string DeskLayoutMap = "Layout map";
    /// <summary>Desk, Layout map: it is a picture, not a control (WF5).</summary>
    public const string DeskMapCaption = "Where each piece sits now. Move pieces with the rows below or Arrange Desk…";
    /// <summary>Desk, Pieces: Order (WF5), live when another piece shares the place.</summary>
    public const string MoveUp = "Move up";
    public const string MoveDown = "Move down";
    /// <summary>An Order button spoken: "Move Weather up in bottom left".</summary>
    public static string MoveUpIn(string piece, string place) => $"Move {piece} up in {place.ToLowerInvariant()}";
    public static string MoveDownIn(string piece, string place) => $"Move {piece} down in {place.ToLowerInvariant()}";
    /// <summary>A row's Show box spoken: its visible label is the piece's name.</summary>
    public static string PieceShownSpoken(string piece) => $"{piece}, on the Desk";
    /// <summary>A feature page's button when its piece is hidden (Principle 1); it places the piece where Desk, Pieces would.</summary>
    public const string PlaceOnDesk = "Place it on the Desk";
    public const string NotOnDesk = "Not on the Desk.";
    /// <summary>Added to a placed piece's line while the Desk itself is off.</summary>
    public const string DeskIsOff = "The Desk is off: turn it on in Settings, Desk.";
    /// <summary>A feature page's line about its piece: "On the Desk: bottom right." or "Not on the Desk."</summary>
    public static string PieceStatus(string? place, bool deskOn) =>
        place is null ? NotOnDesk : $"On the Desk: {place.ToLowerInvariant()}." + (deskOn ? "" : " " + DeskIsOff);
    /// <summary>Desk Look's sections (spec, Desk Look).</summary>
    public const string FontsSection = "Fonts";
    public const string SizesSection = "Sizes";
    public const string InkAndColor = "Ink and color";
    public const string BackPlate = "Back plate";
    /// <summary>Desk Look's controls whose labels were literals.</summary>
    public const string ClockSize = "Clock size";
    public const string MessageSize = "Message size";
    public const string GlowAroundMessage = "Glow around the message";
    public const string DropShadow = "Drop shadow under the text";
    /// <summary>Watchers and Launcher: the section that holds the piece's status line.</summary>
    public const string WatchersPlace = "Where they show";
    public const string LauncherPlace = "Where it shows";
    /// <summary>Launcher: the buttons, their pictures, Add app… and Add link….</summary>
    public const string LauncherButtons = "Buttons";
    /// <summary>Watchers: under "Show my CI runners' jobs" (WF8).</summary>
    public const string CiRunnersCaption = "Independent of the switch above.";
    /// <summary>The Watchers page's first line, and the Companion row's (WF21).</summary>
    public const string WatchersLine = "Watchers are " + DefineWatchers + ".";
    public const string CompanionLine = "The companion is " + DefineCompanion + ".";
    /// <summary>Desk, Pieces, the Companion row with no sprite installed. The link names the page that holds the catalog today.</summary>
    public const string NoCompanionYet = "No companion yet.";
    public static string GetACompanion => $"Get one in {PageTitle(SettingsPage.Widgets)}…";
    /// <summary>Desk, Designs: the collapsed section's one-line summary.</summary>
    public static string DesignsSummary(int saved, bool canPutBack) =>
        saved == 0 ? "None saved yet." : $"{saved} saved." + (canPutBack ? " " + PutMyDeskBack + " is ready." : "");
    /// <summary>The sidebar's Quit asks first (WF33): the question and its two buttons.</summary>
    public const string QuitQuestion = "Quit Sanduhr? The widget and the Desk close.";
    public const string QuitConfirm = "Quit";
    public const string QuitCancel = "Cancel";

```

In `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`, replace

```csharp
    public const string QuitQuestion = "Quit Sanduhr? The widget and the Desk close.";
```

with

```csharp
    public const string QuitQuestion = SettingsNames.QuitQuestion;
```

and

```csharp
              primaryLabel: "Quit", secondaryLabel: "Cancel") == System.Windows.MessageBoxResult.Yes;
```

with

```csharp
              primaryLabel: SettingsNames.QuitConfirm, secondaryLabel: SettingsNames.QuitCancel) == System.Windows.MessageBoxResult.Yes;
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsDeskSplitIndexTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests"`
Expected: PASS. Then `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~SettingsReachTests`: PASS (its Quit assertion reads the same words; its `[DesktopFact]` runs locally).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs
git commit -m "feat(settings-v2): the Desk split's names; the Quit question joins the names table"
```

---

### Task 4: `Resolve` falls back to a retired page's row; `PageFromId` documented

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (`PageFromId`, the `Resolve` overload that takes anchors and pages)
- Test: `windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs`

**Interfaces:**
- Produces: unchanged signatures. New behavior: when an anchor's own alias row targets a place that isn't live, `Resolve` tries the page's row (`FromAnchor` null) once the page has left the sidebar. `PageFromId` keeps reading retired enum names on purpose (documented, pinned). Slice 2 final review, T1.

- [ ] **Step 1: Write the failing tests**

Append to `SettingsDeskSplitIndexTests`:

```csharp
    [Fact]
    public void An_anchor_row_whose_target_is_missing_falls_back_to_the_page_row()
    {
        // A later build without Widgets in the sidebar, where Mods has a catalog but no "mods" section yet: the installed-widgets
        // link can't land on its own target, so it lands where the Widgets page as a whole went.
        var anchors = SettingsIndex.Anchors
            .Where(e => !(e.Page == SettingsPage.Mods && e.Anchor == SettingsIndex.ModList))
            .Append(new SettingsEntry("Catalog", SettingsPage.Mods, "catalog", []))
            .ToList();
        var pages = SettingsNames.Pages.Where(p => p != SettingsPage.Widgets).ToList();
        Assert.Equal(new SettingsLink(SettingsPage.Mods, "catalog"),
            SettingsIndex.Resolve(SettingsPage.Widgets, SettingsIndex.WidgetsInstalled, anchors, pages));
    }

    [Fact]
    public void A_live_page_keeps_an_anchor_no_alias_carries()
    {
        // While the old page is in the sidebar there is no page row to fall back to: the link stays, and Check drops the dead
        // anchor to the page top.
        Assert.Equal(new SettingsLink(SettingsPage.Widgets, "gone"),
            SettingsIndex.Resolve(SettingsPage.Widgets, "gone", SettingsIndex.Anchors, SettingsNames.Pages));
        Assert.Equal(new SettingsLink(SettingsPage.Widgets, null), SettingsIndex.Check(SettingsPage.Widgets, "gone"));
    }

    [Fact]
    public void Retired_page_ids_still_read_so_their_aliases_can_carry_old_links()
    {
        Assert.Equal(SettingsPage.History, SettingsIndex.PageFromId("history"));
        Assert.Equal(SettingsPage.Widgets, SettingsIndex.PageFromId("Widgets"));
        Assert.Null(SettingsIndex.PageFromId("history2"));
    }
```

- [ ] **Step 2: Run them to see the first fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsDeskSplitIndexTests`
Expected: `An_anchor_row_whose_target_is_missing_falls_back_to_the_page_row` FAILS (it returns `Widgets/widgets-installed`); the other two PASS (they pin today's behavior).

- [ ] **Step 3: Write the fallback**

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`, in the `Resolve` overload with `anchors` and `pages`, replace

```csharp
        var row = (anchor is null ? null : Aliases.FirstOrDefault(a => a.FromPage == page && a.FromAnchor == anchor))
            ?? (pages.Contains(page) ? null : Aliases.FirstOrDefault(a => a.FromPage == page && a.FromAnchor is null));
        if (row is not null && PageFromId(row.ToPageId) is { } to && Live(to, row.ToAnchor))
            return new(to, row.ToAnchor);
        return new(page, anchor);
```

with

```csharp
        // The anchor's own row first. When its target is a later slice's (not live yet), the page's row, once the page itself has
        // left the sidebar: the old link then lands where the page as a whole went rather than on a page that no longer shows.
        var tries = new[]
        {
            anchor is null ? null : Aliases.FirstOrDefault(a => a.FromPage == page && a.FromAnchor == anchor),
            pages.Contains(page) ? null : Aliases.FirstOrDefault(a => a.FromPage == page && a.FromAnchor is null),
        };
        foreach (var row in tries)
            if (row is not null && PageFromId(row.ToPageId) is { } to && Live(to, row.ToAnchor))
                return new(to, row.ToAnchor);
        return new(page, anchor);
```

and replace the summary of `PageFromId`

```csharp
    /// <summary>The page an id names, in any case; null for anything else, numbers and spaces included.</summary>
```

with

```csharp
    /// <summary>
    /// The page an id names, in any case; null for anything else, numbers and spaces included. Retired pages (History, Widgets)
    /// still read on purpose: their aliases carry old links (cards, agent text, <c>--settings history</c>) to the new homes, and
    /// <see cref="ParseLink"/> opens General when nothing carries them.
    /// </summary>
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsDeskSplitIndexTests|FullyQualifiedName~SettingsReachIndexTests|FullyQualifiedName~SettingsLinkTests|FullyQualifiedName~WhatsNewTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs
git commit -m "fix(settings-v2): an alias whose target is a later page falls back to its retired page's row"
```

---

### Task 5: Scans that guard the split: split literals, radio groups, own-file focus rings

**Files:**
- Modify: `windows-dotnet/tests/Sanduhr.Tests/SettingsSourceScanTests.cs`
- Modify: `windows-dotnet/src/Sanduhr.Mcp/ToolCatalog.cs:686-687` (join the split literal; its words change in Task 12)

**Interfaces:**
- Produces: three scans every later task must pass: `No_literal_ends_inside_a_Settings_path`, `Every_radio_in_Settings_sits_in_a_named_group`, `Own_file_pages_draw_a_focus_ring_and_add_no_scroll_stop`. (Slice 1 review items 14 and "Split literals"; slice 2 P25 and P5.)

- [ ] **Step 1: Write the scans**

Append to the class in `windows-dotnet/tests/Sanduhr.Tests/SettingsSourceScanTests.cs`:

```csharp
    [Fact]
    public void No_literal_ends_inside_a_Settings_path()
    {
        // "(Settings, " + "Desk);" escapes PathProblems: the page is in the next literal (slice 1 review, split literals). Keep a
        // place in one literal; wrap the prose around it instead.
        var split = CsStrings().Concat(CoreAndMcpStrings())
            .Where(x => x.Text.TrimEnd().EndsWith("Settings,", StringComparison.Ordinal))
            .Select(x => $"{x.Where}: \"{x.Text}\"")
            .ToList();
        Assert.True(split.Count == 0, "A Settings path split across literals: " + string.Join(", ", split));
    }

    [Fact]
    public void Every_radio_in_Settings_sits_in_a_named_group()
    {
        // Slice 1's scan read DeskMessagePage only; the Desk split adds pages. A radio outside a named RadioGroupPanel is read
        // as a lone choice by Narrator.
        var bad = new List<string>();
        foreach (var file in SettingsXamlFiles())
        {
            var src = XmlComment.Replace(File.ReadAllText(file), "");
            int radios = Regex.Matches(src, "<RadioButton\\b").Count;
            if (radios == 0) continue;
            var groups = RadioGroups(src);
            foreach (var g in groups.Where(g => !Regex.IsMatch(g.Open, "AutomationProperties\\.Name=\"[^\"]+\"")))
                bad.Add($"{Path.GetFileName(file)}: a RadioGroupPanel with no AutomationProperties.Name");
            int inside = groups.Sum(g => Regex.Matches(g.Body, "<RadioButton\\b").Count);
            if (inside != radios)
                bad.Add($"{Path.GetFileName(file)}: {radios - inside} radio(s) outside a RadioGroupPanel");
        }
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
    }

    [Fact]
    public void Own_file_pages_draw_a_focus_ring_and_add_no_scroll_stop()
    {
        // P25: the window's styles ring keyboard focus, but a page in its own file can hide the focus rectangle in a local style
        // and draw nothing instead. P5: a page's ScrollViewer must not be an extra, invisible Tab stop.
        var bad = new List<string>();
        foreach (var file in SettingsXamlFiles().Skip(1))   // SettingsWindow.xaml's own styles are pinned by slice 2's tests
        {
            var src = XmlComment.Replace(File.ReadAllText(file), "");
            foreach (Match style in Regex.Matches(src, "<Style\\b.*?</Style>", RegexOptions.Singleline))
                if (Regex.IsMatch(style.Value, "Property=\"FocusVisualStyle\"\\s+Value=\"\\{x:Null\\}\"")
                    && !style.Value.Contains("IsKeyboardFocused", StringComparison.Ordinal))
                    bad.Add($"{Path.GetFileName(file)}: {Regex.Match(style.Value, "<Style\\b[^>]*>").Value} hides the focus rectangle and draws no ring");
            foreach (Match sv in Regex.Matches(src, "<ScrollViewer\\b[^>]*>"))
                if (!sv.Value.Contains("Focusable=\"False\"", StringComparison.Ordinal))
                    bad.Add($"{Path.GetFileName(file)}: {sv.Value} is a Tab stop");
        }
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
    }
```

- [ ] **Step 2: Run them**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsSourceScanTests`
Expected: `No_literal_ends_inside_a_Settings_path` FAILS naming `Mcp/ToolCatalog.cs:686`. The other two PASS. If `Own_file_pages_draw_a_focus_ring_and_add_no_scroll_stop` names an existing file, that is P25's case: give that style's template a ring the way `SettingsLinkButton` does (the outer `Border` gets `BorderThickness="1" BorderBrush="Transparent"`, and `ControlTemplate.Triggers` gets `<Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="<that Border>" Property="BorderBrush" Value="{DynamicResource Sanduhr.Brush.Accent}" /></Trigger>`), or set the named ScrollViewer's `Focusable="False"`, and run the scan once more.

- [ ] **Step 3: Join the split literal**

In `windows-dotnet/src/Sanduhr.Mcp/ToolCatalog.cs`, replace

```csharp
        "watchers_off when the user has not turned on 'Let Claude show watchers' in Sanduhr (Settings, " +
        "Desk); then do not retry. It shows where the user has placed the Watchers piece on the Desk.";
```

with

```csharp
        "watchers_off when the user has not turned on 'Let Claude show watchers' in Sanduhr " +
        "(Settings, Desk); then do not retry. It shows where the user has placed the Watchers piece on the Desk.";
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsSourceScanTests` and `dotnet test tests/Sanduhr.Mcp.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/tests/Sanduhr.Tests/SettingsSourceScanTests.cs windows-dotnet/src/Sanduhr.Mcp/ToolCatalog.cs
git commit -m "test(settings-v2): scans for split Settings paths, unnamed radio groups and ringless own-file pages"
```

(Add any file Step 2 fixed to the `git add`.)

---

### Task 6: Desk Look, Watchers and Launcher join the sidebar, empty

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/WhatsNew.cs:7` (the enum)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (`Sidebar`, the titles block, `PageTitle`; add `DeskPiecePage`)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (`PageSynonyms`)
- Create: `windows-dotnet/src/Sanduhr.App/Views/DeskLookPage.xaml(.cs)`, `WatchersPage.xaml(.cs)`, `LauncherPage.xaml(.cs)`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (three TabItems)
- Test: `windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs`, `SettingsDeskSplitIndexTests.cs`; `windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs` (create)

**Interfaces:**
- Produces: `SettingsPage.DeskLook`, `SettingsPage.Watchers`, `SettingsPage.Launcher` (appended to the enum); page ids `desklook`, `watchers`, `launcher`; `SettingsNames.DeskPiecePage(string widget)` → `SettingsPage?`; the three page controls, each with a `PageBody` StackPanel that later tasks fill.

- [ ] **Step 1: Write the failing tests**

In `windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs`, add three rows to `PageTitle_is_pinned`:

```csharp
    [InlineData(SettingsPage.DeskLook, "Desk Look")]
    [InlineData(SettingsPage.Watchers, "Watchers")]
    [InlineData(SettingsPage.Launcher, "Launcher")]
```

and in `Titles_to_come_are_pinned_and_open_no_page_yet` replace

```csharp
        Assert.Equal(["Privacy", "Desk Look", "Watchers", "Launcher", "Mods & Widgets"], SettingsNames.TitlesToCome);
```

with

```csharp
        Assert.Equal(["Privacy", "Mods & Widgets"], SettingsNames.TitlesToCome);   // Desk Look, Watchers, Launcher landed in slice 3
```

Append to `SettingsDeskSplitIndexTests`:

```csharp
    [Fact]
    public void The_Desktop_group_holds_the_split_pages_in_the_specs_order()
    {
        var desktop = SettingsNames.Sidebar.Single(g => g.Title == "Desktop").Pages;
        Assert.Equal([SettingsPage.Desk, SettingsPage.DeskLook, SettingsPage.DeskMessage, SettingsPage.NowPlaying, SettingsPage.Weather,
            SettingsPage.Watchers, SettingsPage.Launcher, SettingsPage.Widgets], desktop);
        Assert.Equal(["desklook", "watchers", "launcher"],
            new[] { SettingsPage.DeskLook, SettingsPage.Watchers, SettingsPage.Launcher }.Select(SettingsIndex.PageId));
    }

    [Theory]
    [InlineData("message", SettingsPage.DeskMessage)]
    [InlineData("nowPlaying", SettingsPage.NowPlaying)]
    [InlineData("weather", SettingsPage.Weather)]
    [InlineData("watchers", SettingsPage.Watchers)]
    [InlineData("launcher", SettingsPage.Launcher)]
    [InlineData("glow", SettingsPage.ClaudeCode)]
    public void A_piece_with_a_page_names_it(string widget, SettingsPage page) =>
        Assert.Equal(page, SettingsNames.DeskPiecePage(widget));

    [Theory]
    [InlineData("clock")]
    [InlineData("claude")]
    [InlineData("meters")]
    [InlineData("telemetry")]
    [InlineData("companion")]
    public void A_piece_without_a_page_names_none(string widget) => Assert.Null(SettingsNames.DeskPiecePage(widget));
```

Create `windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 3 ("the Desk split") on a real SettingsWindow over scratch folders: the new pages, where links land, the
/// collapsed sections, the folding map, the jump links and the Done criteria. Every window is shown through <see cref="NoActivate"/>.
/// </summary>
[Collection("SettingsWindow")]
public sealed class DeskSplitPagesTests : AppWindowFixture
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

    private static void Settle(SettingsWindow window, NoActivate guard)
    {
        window.UpdateLayout();
        Pump();
        guard.Check();
    }

    private static FrameworkElement Marked(SettingsWindow window, SettingsPage page, string anchor) =>
        SettingsAnchor.Find((DependencyObject)TabOf(window, page).Content, anchor)
            ?? throw new Xunit.Sdk.XunitException($"{page}/{anchor} is not marked");

    private static IEnumerable<T> Below<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Below<T>(child)) yield return deeper;
        }
    }

    [Fact]
    public void The_three_new_pages_open_from_the_sidebar() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            foreach (var page in new[] { SettingsPage.DeskLook, SettingsPage.Watchers, SettingsPage.Launcher })
            {
                window.SelectPage(page);
                Settle(window, guard);
                Assert.Equal(new SettingsLink(page, null), window.Place);
                Assert.True(TabOf(window, page).IsSelected);
            }
        }
        finally { window.Close(); }
    });
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsDeskSplitIndexTests"`
Expected: build FAILS: `SettingsPage` has no `DeskLook`.

- [ ] **Step 3: Add the enum values, the sidebar and the titles**

In `windows-dotnet/src/Sanduhr.Core/WhatsNew.cs`, replace

```csharp
public enum SettingsPage { Accounts, Themes, Alerts, History, ClaudeUsage, Publish, General, Widget, About, ClaudeCode, Mods, Desk, DeskMessage, NowPlaying, Weather, Widgets }
```

with

```csharp
public enum SettingsPage { Accounts, Themes, Alerts, History, ClaudeUsage, Publish, General, Widget, About, ClaudeCode, Mods, Desk, DeskMessage, NowPlaying, Weather, Widgets, DeskLook, Watchers, Launcher }
```

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, replace the Desktop row of `Sidebar`

```csharp
        new("Desktop", [SettingsPage.Desk, SettingsPage.DeskMessage, SettingsPage.NowPlaying, SettingsPage.Weather, SettingsPage.Widgets]),
```

with

```csharp
        new("Desktop", [SettingsPage.Desk, SettingsPage.DeskLook, SettingsPage.DeskMessage, SettingsPage.NowPlaying, SettingsPage.Weather,
            SettingsPage.Watchers, SettingsPage.Launcher, SettingsPage.Widgets]),
```

replace

```csharp
    // ---- Page titles still to come (Settings v2). Unused until their slice adds the page: Privacy
    // (slice 6), Desk Look, Watchers and Launcher (slice 3), Mods & Widgets (slice 4, the Mods page's new title).
```

with

```csharp
    // ---- Page titles added by Settings v2. Desk Look, Watchers and Launcher are pages since slice 3; Privacy (slice 6) and
    // Mods & Widgets (slice 4, the Mods page's new title) are still to come.
```

replace

```csharp
    /// <summary>The titles above, in sidebar order of their future pages.</summary>
    public static IReadOnlyList<string> TitlesToCome { get; } =
        [PrivacyTitle, DeskLookTitle, WatchersTitle, LauncherTitle, ModsAndWidgetsTitle];
```

with

```csharp
    /// <summary>The titles of pages still to come, in sidebar order.</summary>
    public static IReadOnlyList<string> TitlesToCome { get; } = [PrivacyTitle, ModsAndWidgetsTitle];
```

in `PageTitle`, after `        SettingsPage.Weather => "Weather",` add

```csharp
        SettingsPage.DeskLook => DeskLookTitle,
        SettingsPage.Watchers => WatchersTitle,
        SettingsPage.Launcher => LauncherTitle,
```

and after the `DeskPieceName` switch add

```csharp

    /// <summary>The page a Desk piece's own settings live on (Desk, Pieces' "&lt;Page&gt; settings…"); null for a piece that has
    /// none (the clock, the Claude meters, System, and the companion, whose controls sit in its row).</summary>
    public static SettingsPage? DeskPiecePage(string widget) => widget switch
    {
        "message" => SettingsPage.DeskMessage,
        "nowPlaying" => SettingsPage.NowPlaying,
        "weather" => SettingsPage.Weather,
        "watchers" => SettingsPage.Watchers,
        "launcher" => SettingsPage.Launcher,
        "glow" => SettingsPage.ClaudeCode,
        _ => null,
    };
```

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`, in `PageSynonyms`, after the `[SettingsPage.Weather]` row add

```csharp
        [SettingsPage.DeskLook] = ["look", "fonts", "size", "ink", "color", "colour", "plate"],
        [SettingsPage.Watchers] = ["watcher", "cards", "CI", "progress"],
        [SettingsPage.Launcher] = ["buttons", "apps", "links", "shortcuts"],
```

(Not "font": the "font" boost lands on Desk Look, Fonts in Task 11; not "runner": its boost lands on CI runners in Task 12.)

- [ ] **Step 4: Create the three empty pages**

Create `windows-dotnet/src/Sanduhr.App/Views/DeskLookPage.xaml`:

```xml
<UserControl x:Class="Sanduhr.App.Views.DeskLookPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:views="clr-namespace:Sanduhr.App.Views"
             xmlns:vm="clr-namespace:Sanduhr.App.ViewModels"
             xmlns:core="clr-namespace:Sanduhr.Core;assembly=Sanduhr.Core"
             Focusable="False">
    <!-- Settings, Desk Look (spec, Desk Look): the Desk's fonts, sizes, ink and color, and the back plate, in one place (WF12).
         Hosted in SettingsWindow, whose styles it finds with DynamicResource. DataContext: the SettingsViewModel; each section
         sets its own (Desk, or MessagePage for the message's look). -->
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BoolVis" />
    </UserControl.Resources>
    <Border Background="{DynamicResource Sanduhr.Brush.Glass}" CornerRadius="8">
        <ScrollViewer VerticalScrollBarVisibility="Auto" Focusable="False">
            <ScrollViewer.Resources>
                <Style TargetType="ScrollBar" BasedOn="{StaticResource SlimThemeScrollBar}" />
            </ScrollViewer.Resources>
            <StackPanel x:Name="PageBody" Margin="20">
                <TextBlock Text="{x:Static core:SettingsNames.DeskLookTitle}" FontSize="14" FontWeight="SemiBold"
                           AutomationProperties.HeadingLevel="Level1"
                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,12" />
            </StackPanel>
        </ScrollViewer>
    </Border>
</UserControl>
```

Create `windows-dotnet/src/Sanduhr.App/Views/DeskLookPage.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace Sanduhr.App.Views;

/// <summary>Settings, Desk Look (Settings v2, slice 3): fonts, sizes, ink and color, the back plate.</summary>
public partial class DeskLookPage : UserControl
{
    public DeskLookPage() => InitializeComponent();
}
```

Create `WatchersPage.xaml` with the same skeleton, `x:Class="Sanduhr.App.Views.WatchersPage"`, the comment `<!-- Settings, Watchers (spec, Watchers): Claude's watchers, the CI runners' jobs, and where the piece shows (WF8, WF22). Hosted in SettingsWindow. DataContext: the DeskSettingsViewModel. -->`, the title `Text="{x:Static core:SettingsNames.WatchersTitle}"`, and under the title its first line (WF21):

```xml
                <TextBlock Text="{x:Static core:SettingsNames.WatchersLine}" FontSize="11" TextWrapping="Wrap"
                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,-8,0,12" />
```

Create `LauncherPage.xaml` with the same skeleton, `x:Class="Sanduhr.App.Views.LauncherPage"`, the comment `<!-- Settings, Launcher (spec, Launcher): up to eight buttons on the Desk, and where the piece shows (WF22). Hosted in SettingsWindow. DataContext: the DeskSettingsViewModel. -->` and the title `Text="{x:Static core:SettingsNames.LauncherTitle}"`.

Create `WatchersPage.xaml.cs` and `LauncherPage.xaml.cs` like `DeskLookPage.xaml.cs`, with their class names and the summaries `Settings, Watchers (Settings v2, slice 3).` and `Settings, Launcher (Settings v2, slice 3).`.

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`, after the Desk TabItem's closing `</TabItem>` (the line before `<!-- Settings, Message (spec 6.2): …`) add

```xml

                <!-- Settings, Desk Look (Settings v2, slice 3): its own control; its sections pick their view models. -->
                <TabItem Header="Desk Look" Tag="DeskLook" AutomationProperties.Name="Desk Look">
                    <views:DeskLookPage />
                </TabItem>
```

and after the Weather TabItem's closing `</TabItem>` (the line before `<!-- Settings, Widgets (spec 3.8): …`) add

```xml

                <!-- Settings, Watchers and Launcher (Settings v2, slice 3): features that were sections of the Desk page (WF22). -->
                <TabItem Header="Watchers" Tag="Watchers" AutomationProperties.Name="Watchers" DataContext="{Binding Desk}">
                    <views:WatchersPage />
                </TabItem>

                <TabItem Header="Launcher" Tag="Launcher" AutomationProperties.Name="Launcher" DataContext="{Binding Desk}">
                    <views:LauncherPage />
                </TabItem>
```

- [ ] **Step 5: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsDeskSplitIndexTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsReachIndexTests|FullyQualifiedName~SettingsIndexTests"`
Expected: PASS (the sidebar scan finds the three TabItems in the names table's order).
Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskSplitPagesTests|FullyQualifiedName~SettingsWindowNamesTests|FullyQualifiedName~SidebarTabControlAutomationTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/WhatsNew.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.App/Views/DeskLookPage.xaml windows-dotnet/src/Sanduhr.App/Views/DeskLookPage.xaml.cs windows-dotnet/src/Sanduhr.App/Views/WatchersPage.xaml windows-dotnet/src/Sanduhr.App/Views/WatchersPage.xaml.cs windows-dotnet/src/Sanduhr.App/Views/LauncherPage.xaml windows-dotnet/src/Sanduhr.App/Views/LauncherPage.xaml.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs
git commit -m "feat(settings-v2): Desk Look, Watchers and Launcher join the Desktop group, empty"
```

---

### Task 7: The piece rows' Show, Order and Settings…; the feature pages' status

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs` (the `DeskPieceRow` class; `DeskSettingsViewModel.Load`, `Edit`, `PlaceLauncherIfHidden`; new members)
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/DeskPieceStatusViewModel.cs`
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DeskPiecesSettingsTests.cs` (create); `windows-dotnet/tests/Sanduhr.App.Tests/LauncherSettingsTests.cs:198-204`

**Interfaces:**
- Consumes: `DeskArrangement.PlaceAtDefault`, `StackPosition`, `Move(string, int)` (Task 1); the names (Task 3); `SettingsNames.DeskPiecePage` (Task 6).
- Produces on `DeskPieceRow`: `bool Shown { get; set; }`, `bool CanMoveUp`, `bool CanMoveDown`, `IRelayCommand MoveUpCommand`, `MoveDownCommand`, `string ShownName`, `MoveUpName`, `MoveDownName`, `SettingsPage? PiecePage`, `bool HasSettingsPage`, `string SettingsTitle`, `string SettingsLink`; `internal void Load(DeskPlacement? p, int stackIndex, int stackCount)`.
- Produces on `DeskSettingsViewModel`: `public DeskAnchor? PlaceIfHidden(string widget)` (null while arranging, when placed, or for an unknown word), `internal void Remember(string widget, DeskAnchor at)`, `public DeskPieceStatusViewModel PieceStatus(string widget)`, `WatchersPiece`, `LauncherPiece`, `GlowPiece`.
- Produces: `public sealed partial class DeskPieceStatusViewModel` with `Widget`, `IsPlaced`, `IsHidden`, `Text`, `CanPlace`, `PlaceTitle`, `DeskSettingsTitle`, `DeskSettingsLink` (`"desk#pieces"`), `PlaceCommand`, `DeskAnchor? PlaceIfHidden()`, `Refresh()`.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/DeskPiecesSettingsTests.cs`:

```csharp
using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 3: Desk, Pieces' rows (Show, Order, Settings…) and the feature pages' status lines, over a scratch AppData.
/// Every change goes to desk_layout through DeskArrangement; nothing else is written.
/// </summary>
public sealed class DeskPiecesSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-pieces-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    public DeskPiecesSettingsTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        _store = new SettingsStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private DeskSettingsViewModel Desk(string? layout = null, bool enabled = true)
    {
        _store.SaveDesk((DeskSettings.Default with { Enabled = enabled, Layout = layout ?? DeskArrangement.Standard }).Normalized());
        return new DeskSettingsViewModel(_store, [], () => []);
    }

    private string Layout => _store.LoadDesk().Layout;

    private static DeskPieceRow Row(DeskSettingsViewModel desk, string widget) => desk.Pieces.Single(p => p.Widget == widget);

    [Fact]
    public void Show_places_a_piece_at_its_default_and_off_hides_it()
    {
        var desk = Desk();
        var weather = Row(desk, "weather");
        Assert.False(weather.Shown);
        weather.Shown = true;
        Assert.Equal("clock:bl claude:bl weather:tr", Layout);
        Assert.True(weather.Shown);
        Assert.Equal(DeskAnchor.TR, weather.Place.Anchor);
        weather.Shown = false;
        Assert.Equal(DeskArrangement.Standard, Layout);
        Assert.False(weather.Shown);
    }

    [Fact]
    public void Show_again_in_the_same_window_returns_to_the_last_place()
    {
        var desk = Desk("clock:bl claude:bl weather:bl");
        var weather = Row(desk, "weather");
        weather.Shown = false;
        weather.Shown = true;
        Assert.Equal(DeskAnchor.BL, new DeskArrangement(Layout).Placement("weather")!.Anchor);

        weather.Place = DeskSettingsViewModel.Hidden;       // hidden through Place: remembered too
        Assert.Equal(DeskAnchor.BL, desk.PlaceIfHidden("weather"));

        weather.Shown = false;
        Assert.Equal(DeskAnchor.TR, Desk(Layout).PlaceIfHidden("weather"));   // a new window: the default place again
    }

    [Fact]
    public void Order_moves_a_piece_within_its_place_and_is_live_only_where_it_can_move()
    {
        var desk = Desk("clock:bl claude:bl weather:tr");
        var clock = Row(desk, "clock");
        var claude = Row(desk, "claude");
        Assert.False(clock.CanMoveUp);
        Assert.True(clock.CanMoveDown);
        Assert.True(claude.CanMoveUp);
        Assert.False(claude.CanMoveDown);
        Assert.False(Row(desk, "weather").CanMoveUp || Row(desk, "weather").CanMoveDown);   // alone in its place
        Assert.False(Row(desk, "message").CanMoveUp || Row(desk, "message").CanMoveDown);   // hidden
        Assert.Equal("Move Clock and date down in bottom left", clock.MoveDownName);

        clock.MoveDownCommand.Execute(null);
        Assert.Equal("claude:bl clock:bl weather:tr", Layout);
        Assert.True(Row(desk, "clock").CanMoveUp);
        Assert.False(Row(desk, "clock").MoveDownCommand.CanExecute(null));
    }

    [Fact]
    public void A_piece_with_a_page_links_to_it()
    {
        var desk = Desk();
        var weather = Row(desk, "weather");
        Assert.True(weather.HasSettingsPage);
        Assert.Equal("Weather settings…", weather.SettingsTitle);
        Assert.Equal("weather", weather.SettingsLink);
        Assert.Equal("claudecode#glow-where", Row(desk, "glow").SettingsLink);
        Assert.Equal("launcher", Row(desk, "launcher").SettingsLink);
        Assert.False(Row(desk, "clock").HasSettingsPage);
        Assert.Equal("", Row(desk, "clock").SettingsTitle);
        Assert.Equal("Weather, on the Desk", weather.ShownName);
    }

    [Fact]
    public void A_feature_pages_line_says_where_and_places_a_hidden_piece()
    {
        var desk = Desk();
        var status = desk.PieceStatus("weather");
        Assert.Same(status, desk.PieceStatus("weather"));
        Assert.False(status.IsPlaced);
        Assert.Equal("Not on the Desk.", status.Text);
        Assert.True(status.PlaceCommand.CanExecute(null));

        status.PlaceCommand.Execute(null);
        Assert.Equal("clock:bl claude:bl weather:tr", Layout);
        Assert.True(status.IsPlaced);
        Assert.Equal("On the Desk: top right.", status.Text);
        Assert.False(status.PlaceCommand.CanExecute(null));
        Assert.Equal("desk#pieces", status.DeskSettingsLink);
        Assert.Equal("Desk settings…", status.DeskSettingsTitle);
    }

    [Fact]
    public void The_line_says_when_the_Desk_itself_is_off()
    {
        var desk = Desk("clock:bl weather:br", enabled: false);
        Assert.Equal("On the Desk: bottom right. The Desk is off: turn it on in Settings, Desk.", desk.PieceStatus("weather").Text);
        desk.Enabled = true;
        Assert.Equal("On the Desk: bottom right.", desk.PieceStatus("weather").Text);
    }

    [Fact]
    public void The_status_line_follows_a_reload()
    {
        var desk = Desk();
        var status = desk.PieceStatus("weather");
        _store.SaveDesk(_store.LoadDesk() with { Layout = "clock:bl weather:bl" });   // Arrange's Done, a design, another window
        desk.Reload();
        Assert.Equal("On the Desk: bottom left.", status.Text);
    }

    [Fact]
    public void PlaceIfHidden_does_nothing_to_a_placed_piece()
    {
        var desk = Desk("clock:bl weather:bl:1.2");
        var before = File.ReadAllText(_paths.SettingsFile);
        var stamp = File.GetLastWriteTimeUtc(_paths.SettingsFile);
        Assert.Null(desk.PlaceIfHidden("weather"));
        Assert.Null(desk.PlaceIfHidden("nope"));
        Assert.Equal(before, File.ReadAllText(_paths.SettingsFile));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(_paths.SettingsFile));
    }

    [Fact]
    public void Nothing_places_while_Arrange_runs()
    {
        var desk = Desk();
        var status = desk.PieceStatus("weather");
        desk.Arranging = true;
        Assert.Null(desk.PlaceIfHidden("weather"));
        Assert.False(status.CanPlace);
        Assert.False(status.PlaceCommand.CanExecute(null));
        Assert.Equal(DeskArrangement.Standard, Layout);
        desk.Arranging = false;
        Assert.True(status.PlaceCommand.CanExecute(null));
    }
}
```

In `windows-dotnet/tests/Sanduhr.App.Tests/LauncherSettingsTests.cs`, replace

```csharp
    [Fact]
    public void TheDeskSettingsPicksTheFirstFreeCornerAndSavesIt()
    {
        var desk = new DeskSettingsViewModel(_store, []);
        Assert.Equal("bottom right", desk.PlaceLauncherIfHidden());   // the standard layout fills bottom left
        Assert.Equal(DeskAnchor.BR, new DeskArrangement(_store.LoadDesk().Layout).Placement("launcher")!.Anchor);
        Assert.Null(desk.PlaceLauncherIfHidden());                    // already placed: nothing moves
    }
```

with

```csharp
    [Fact]
    public void TheDeskSettingsPlacesTheLauncherAtItsDefaultPlaceAndSavesIt()
    {
        var desk = new DeskSettingsViewModel(_store, [], () => []);
        Assert.Equal("top left", desk.PlaceLauncherIfHidden());       // DeskArrangement.DefaultPlaceFor("launcher")
        Assert.Equal(DeskAnchor.TL, new DeskArrangement(_store.LoadDesk().Layout).Placement("launcher")!.Anchor);
        Assert.Null(desk.PlaceLauncherIfHidden());                    // already placed: nothing moves
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskPiecesSettingsTests|FullyQualifiedName~LauncherSettingsTests"`
Expected: build FAILS: `DeskPieceRow` has no `Shown`.

- [ ] **Step 3: Write the status view model**

Create `windows-dotnet/src/Sanduhr.App/ViewModels/DeskPieceStatusViewModel.cs`:

```csharp
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// A feature page's line about its Desk piece (Settings v2, Principle 1): "On the Desk: bottom right." with "Desk settings…", or
/// "Not on the Desk." with "Place it on the Desk", which places it the way Desk, Pieces' Show box would. A status, never a second
/// control: it reads and writes desk_layout through <see cref="DeskSettingsViewModel"/>, so the two can't disagree. It follows
/// every change to the layout (a save, a reload after Arrange or a design), the Desk switch and Arrange mode.
/// </summary>
public sealed partial class DeskPieceStatusViewModel : ObservableObject
{
    private readonly DeskSettingsViewModel _desk;

    internal DeskPieceStatusViewModel(DeskSettingsViewModel desk, string widget)
    {
        _desk = desk;
        Widget = widget;
        desk.PropertyChanged += OnDeskChanged;
        Refresh();
    }

    public string Widget { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsHidden), nameof(CanPlace))] private bool _isPlaced;
    [ObservableProperty] private string _text = "";

    public bool IsHidden => !IsPlaced;

    /// <summary>"Place it on the Desk" is live while the piece is hidden and no Arrange session runs (Done would overwrite it).</summary>
    public bool CanPlace => !IsPlaced && !_desk.Arranging;

    public string PlaceTitle => SettingsNames.PlaceOnDesk;
    public string DeskSettingsTitle => SettingsNames.LinkTitle(SettingsPage.Desk);
    public string DeskSettingsLink => SettingsIndex.PageId(SettingsPage.Desk) + "#" + SettingsIndex.DeskPieces;

    /// <summary>A feature's own on-action (Principle 4): places the piece if it is hidden; null when nothing moved.</summary>
    public DeskAnchor? PlaceIfHidden() => _desk.PlaceIfHidden(Widget);

    [RelayCommand(CanExecute = nameof(CanPlace))]
    private void Place() => PlaceIfHidden();

    /// <summary>Reads the piece's place again.</summary>
    public void Refresh()
    {
        var placed = new DeskArrangement(_desk.Layout).Placement(Widget);
        IsPlaced = placed is not null;
        Text = SettingsNames.PieceStatus(placed is null ? null : DeskAnchors.Name(placed.Anchor), _desk.Enabled);
        OnPropertyChanged(nameof(CanPlace));
        PlaceCommand.NotifyCanExecuteChanged();
    }

    private void OnDeskChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DeskSettingsViewModel.Layout) or nameof(DeskSettingsViewModel.Enabled)
            or nameof(DeskSettingsViewModel.Arranging))
            Refresh();
    }
}
```

- [ ] **Step 4: Give the rows Show, Order and their page; give the page `PlaceIfHidden`**

In `windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs`, add `using CommunityToolkit.Mvvm.Input;` to the usings, and replace the whole `DeskPieceRow` class (from `/// <summary>One row of "Where each piece sits": a piece, its place and its size (60% to 160%).</summary>` through its closing `}` before `/// <summary>A meter style as the Style picker lists it (#336).</summary>`) with:

```csharp
/// <summary>One row of Desk, Pieces: the piece's Show box, its place, size, monitor and style, its order among the pieces that
/// share its place, and the link to its own page.</summary>
public sealed partial class DeskPieceRow : ObservableObject
{
    private readonly DeskSettingsViewModel _owner;
    private bool _loading;

    public DeskPieceRow(DeskSettingsViewModel owner, string widget, string name)
    {
        _owner = owner;
        Widget = widget;
        Name = name;
    }

    public string Widget { get; }
    public string Name { get; }

    public override string ToString() => Name;

    [ObservableProperty] private DeskPlaceOption _place = DeskSettingsViewModel.Hidden;
    [ObservableProperty] private double _size = 1;

    /// <summary>What the Monitor dropdown offers for this piece: All monitors, Main monitor, each connected monitor, and (when the piece is
    /// on one that is gone) that monitor as "not connected".</summary>
    [ObservableProperty] private IReadOnlyList<DeskMonitorChoice> _monitorChoices = [];

    /// <summary>The monitor the piece is drawn on.</summary>
    [ObservableProperty] private DeskMonitorChoice _monitor = new(null, DeskMonitorNames.AllMonitors);

    /// <summary>The meter style (#336), for the pieces that have one (<see cref="HasStyle"/>).</summary>
    [ObservableProperty] private DeskStyleOption _style = DeskSettingsViewModel.StyleOptions[0];

    /// <summary>Order (WF5): live when another piece shares the place and this one isn't at that end of its stack.</summary>
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(MoveUpCommand))] private bool _canMoveUp;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(MoveDownCommand))] private bool _canMoveDown;

    /// <summary>"120%", for the size picker.</summary>
    public string SizeText => $"{Math.Round(Size * 100)}%";

    public bool IsPlaced => Place.Anchor is not null;

    /// <summary>True for the Claude line, the Claude limits and telemetry: their row shows a Style picker.</summary>
    public bool HasStyle => DeskMeterStyles.Supports(Widget);

    /// <summary>Show (spec, Pieces): on means Place is not Hidden. On places the piece where it last was in this window, else at its
    /// default place (<see cref="DeskArrangement.DefaultPlaceFor"/>); off hides it and remembers where it was.</summary>
    public bool Shown
    {
        get => IsPlaced;
        set
        {
            if (_loading || value == IsPlaced) return;
            if (value) _owner.PlaceIfHidden(Widget);
            else Place = DeskSettingsViewModel.Hidden;
        }
    }

    /// <summary>The Show box spoken; its visible label is <see cref="Name"/>.</summary>
    public string ShownName => SettingsNames.PieceShownSpoken(Name);
    public string MoveUpName => SettingsNames.MoveUpIn(Name, Place.Name);
    public string MoveDownName => SettingsNames.MoveDownIn(Name, Place.Name);

    /// <summary>The page with this piece's own settings; null for a piece that has none.</summary>
    public SettingsPage? PiecePage => SettingsNames.DeskPiecePage(Widget);

    /// <summary>True when that page is in this build's sidebar: the row then shows "&lt;Page&gt; settings…".</summary>
    public bool HasSettingsPage => PiecePage is { } page && SettingsNames.Pages.Contains(page);

    public string SettingsTitle => PiecePage is { } page ? SettingsNames.LinkTitle(page) : "";

    /// <summary>The link the button opens: the page, and for the glow the section where its place is chosen.</summary>
    public string SettingsLink => PiecePage switch
    {
        null => "",
        SettingsPage.ClaudeCode => SettingsIndex.PageId(SettingsPage.ClaudeCode) + "#" + SettingsIndex.GlowWhere,
        { } page => SettingsIndex.PageId(page),
    };

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => _owner.Edit(a => a.Move(Widget, -1));

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => _owner.Edit(a => a.Move(Widget, 1));

    internal void Load(DeskPlacement? p, int stackIndex, int stackCount)
    {
        _loading = true;
        Place = p is null ? DeskSettingsViewModel.Hidden : DeskSettingsViewModel.PlaceFor(p.Anchor);
        Size = p?.Scale ?? 1;
        Style = DeskSettingsViewModel.StyleFor(p?.Style ?? DeskMeterStyle.Bar);
        MonitorChoices = DeskMonitorNames.Choices(_owner.Monitors, p?.Monitor);
        Monitor = DeskMonitorNames.Select(MonitorChoices, p?.Monitor);
        CanMoveUp = stackIndex > 0;
        CanMoveDown = stackIndex >= 0 && stackIndex < stackCount - 1;
        _loading = false;
    }

    partial void OnPlaceChanged(DeskPlaceOption value)
    {
        OnPropertyChanged(nameof(IsPlaced));
        OnPropertyChanged(nameof(Shown));
        OnPropertyChanged(nameof(MoveUpName));
        OnPropertyChanged(nameof(MoveDownName));
        if (!_loading) _owner.Edit(a => a.Place(Widget, value.Anchor));
    }

    /// <summary>Hidden by hand (Show or Place): the place is remembered for this window's life.</summary>
    partial void OnPlaceChanged(DeskPlaceOption? oldValue, DeskPlaceOption newValue)
    {
        if (!_loading && newValue.Anchor is null && oldValue?.Anchor is { } was) _owner.Remember(Widget, was);
    }

    partial void OnMonitorChanged(DeskMonitorChoice value)
    {
        if (!_loading && value is not null) _owner.Edit(a => a.SetMonitor(Widget, value.Value));
    }

    partial void OnSizeChanged(double value)
    {
        OnPropertyChanged(nameof(SizeText));
        if (!_loading) _owner.Edit(a => a.SetScale(Widget, value));
    }

    partial void OnStyleChanged(DeskStyleOption value)
    {
        if (!_loading && value is not null && HasStyle) _owner.Edit(a => a.SetStyle(Widget, value.Style));
    }
}
```

In `DeskSettingsViewModel`, replace both occurrences (in `Load` and in `Edit`) of

```csharp
        foreach (var row in Pieces) row.Load(a.Placement(row.Widget));
```

with

```csharp
        LoadRows(a);
```

replace `PlaceLauncherIfHidden` (its summary and body) with

```csharp
    /// <summary>The Launcher's first button was added while the piece has no place: puts it at its default place (or where it last
    /// was in this window) and returns that place's name in lower case ("top left"), or null when it already has a place.</summary>
    internal string? PlaceLauncherIfHidden() =>
        PlaceIfHidden(LauncherModel.Widget) is { } at ? DeskAnchors.Name(at).ToLowerInvariant() : null;
```

and add, after `PlaceLauncherIfHidden`:

```csharp

    private readonly Dictionary<string, DeskAnchor> _lastPlace = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeskPieceStatusViewModel> _statuses = new(StringComparer.Ordinal);

    /// <summary>A piece hidden by hand: where it was, for the life of this Settings window (spec, Pieces: Show).</summary>
    internal void Remember(string widget, DeskAnchor at) => _lastPlace[widget] = at;

    /// <summary>
    /// Shows a hidden piece (Principle 4): where it last was in this window, else at <see cref="DeskArrangement.DefaultPlaceFor"/>;
    /// saves, and the running Desk follows. Returns where, or null when nothing changed: the piece is already placed, the word is
    /// unknown, or an Arrange session runs (its Done would overwrite the layout). Nothing is written then.
    /// </summary>
    public DeskAnchor? PlaceIfHidden(string widget)
    {
        if (Arranging || new DeskArrangement(_saved.Layout).Placement(widget) is not null
            || !DeskArrangement.Known.Contains(widget, StringComparer.Ordinal))
            return null;
        DeskAnchor? remembered = _lastPlace.TryGetValue(widget, out var at) ? at : null;
        DeskAnchor? placed = null;
        Edit(a => placed = a.PlaceAtDefault(widget, remembered));
        return placed;
    }

    /// <summary>The line a feature page shows about <paramref name="widget"/>'s piece; one per piece for this window.</summary>
    public DeskPieceStatusViewModel PieceStatus(string widget) =>
        _statuses.TryGetValue(widget, out var status) ? status : _statuses[widget] = new DeskPieceStatusViewModel(this, widget);

    /// <summary>Watchers, Launcher and Claude Code bind to these (a binding can't call <see cref="PieceStatus"/>).</summary>
    public DeskPieceStatusViewModel WatchersPiece => PieceStatus("watchers");
    public DeskPieceStatusViewModel LauncherPiece => PieceStatus(LauncherModel.Widget);
    public DeskPieceStatusViewModel GlowPiece => PieceStatus("glow");

    private void LoadRows(DeskArrangement a)
    {
        foreach (var row in Pieces)
        {
            var (index, count) = a.StackPosition(row.Widget);
            row.Load(a.Placement(row.Widget), index, count);
        }
    }
```

(`LauncherModel.Widget` is `"launcher"`, the constant `DeskDesign.cs:636` already uses.)

- [ ] **Step 5: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskPiecesSettingsTests|FullyQualifiedName~LauncherSettingsTests|FullyQualifiedName~DeskMonitorTests|FullyQualifiedName~DeskMeterStylesViewTests|FullyQualifiedName~OledCareTests|FullyQualifiedName~DeskDirectSwitchTests|FullyQualifiedName~CiRunnersSwitchTests"`
Expected: PASS. (`LauncherSettingsTests.TheFirstButtonPlacesAHiddenLauncherAndSaysWhere` stubs the placer, so its "bottom right" text is unchanged until Task 8.)

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/DeskPieceStatusViewModel.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskPiecesSettingsTests.cs windows-dotnet/tests/Sanduhr.App.Tests/LauncherSettingsTests.cs
git commit -m "feat(desk): piece rows get Show, Order and their page; feature pages get a status that can place the piece"
```

---

### Task 8: Desk, Pieces: wrapping rows, and links that open from any page control

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Views/DeskPiecesPanel.xaml` (whole file), `DeskPiecesPanel.xaml.cs` (summary)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsReach.cs` (add `SettingsLinkTo`)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs` (constructor; a click handler)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (the `pieces` section's heading and caption)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (the `pieces` row), `SettingsNames.cs` (`Retired`)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/LauncherSettingsViewModel.cs:125, 169, 367` (notes name the section)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DeskPiecesSettingsTests.cs`, `DeskSplitPagesTests.cs`, `LauncherSettingsTests.cs:174, 229`; `windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs`

**Interfaces:**
- Consumes: the row members of Task 7.
- Produces: `public static class SettingsLinkTo` with attached `Link` (string, "page#anchor"); `SettingsWindow` opens any clicked button's `Link`. The panel names: the ItemsControl `PieceRows`; each row a `WrapPanel` tagged `PieceRow` (`Tag="PieceRow"`) whose children are the 490 DIP place block and the 230 DIP Order block. The section title is `SettingsNames.PiecesSection`.

- [ ] **Step 1: Write the failing tests**

Append to `DeskPiecesSettingsTests`:

```csharp
    private static System.Windows.Controls.WrapPanel FirstRow(System.Windows.DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.WrapPanel { Tag: "PieceRow" } row) return row;
            if (FirstRowOrNull(child) is { } found) return found;
        }
        throw new Xunit.Sdk.XunitException("no piece row");
    }

    private static System.Windows.Controls.WrapPanel? FirstRowOrNull(System.Windows.DependencyObject root)
    {
        try { return FirstRow(root); } catch (Xunit.Sdk.XunitException) { return null; }
    }

    [Fact]
    public void A_row_is_one_line_at_760_of_page_width_and_wraps_below() => Sta.Run(() =>
    {
        var panel = new Sanduhr.App.Views.DeskPiecesPanel { DataContext = Desk("clock:bl claude:bl") };
        double OrderTop(double pageWidth)
        {
            double inner = pageWidth - 40;   // the page's 20 DIP margins
            panel.Measure(new System.Windows.Size(inner, double.PositiveInfinity));
            panel.Arrange(new System.Windows.Rect(0, 0, inner, panel.DesiredSize.Height));
            panel.UpdateLayout();
            var row = FirstRow(panel);
            return ((System.Windows.FrameworkElement)row.Children[1]).TranslatePoint(new System.Windows.Point(0, 0), row).Y;
        }
        Assert.Equal(0, OrderTop(760), 1);
        Assert.True(OrderTop(759) > 20, "the Order buttons stay on the first line below 760 DIP");
    });
```

Append to `DeskSplitPagesTests`:

```csharp
    [Fact]
    public void A_pieces_row_is_named_and_its_page_link_opens_the_page() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk, SettingsIndex.DeskPieces);
            Settle(window, guard);
            var pieces = Marked(window, SettingsPage.Desk, SettingsIndex.DeskPieces);
            var show = Below<CheckBox>(pieces).Single(c => (string)c.Content == SettingsNames.DeskWeather);
            Assert.Equal("Weather, on the Desk", System.Windows.Automation.AutomationProperties.GetName(show));
            var link = Below<Button>(pieces).Single(b => (b.Content as string) == "Weather settings…");
            Assert.Equal("weather", SettingsLinkTo.GetLink(link));
            link.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Weather, null), window.Place);
        }
        finally { window.Close(); }
    });
```

Append to `SettingsDeskSplitIndexTests`:

```csharp
    [Fact]
    public void The_pieces_section_is_called_Pieces_and_still_found_by_its_old_name()
    {
        var entry = SettingsIndex.Find(SettingsPage.Desk, SettingsIndex.DeskPieces)!;
        Assert.Equal(SettingsNames.PiecesSection, entry.Title);
        Assert.Contains(SettingsIndex.Search("where each piece sits"), h => h.Entry == entry);
        Assert.Contains(SettingsIndex.Search("move up"), h => h.Entry == entry);
        Assert.Equal(["Where each piece sits"], SettingsNames.RetiredNames("Where each piece sits"));
        Assert.Empty(SettingsNames.RetiredNames(SettingsNames.DeskMapCaption));   // a whole-name retirement: the caption may say it
        Assert.Empty(SettingsNames.PathProblems("Move it in Settings, Desk, Pieces."));
    }
```

In `windows-dotnet/tests/Sanduhr.App.Tests/LauncherSettingsTests.cs`, replace

```csharp
            Assert.Equal("Placed at bottom right. Move it under Where each piece sits.", vm.PlacementNote);
```

with

```csharp
            Assert.Equal("Placed at bottom right. Move it in Settings, Desk, Pieces.", vm.PlacementNote);
```

and

```csharp
        Assert.Contains("Where each piece sits", hidden.PlacementNote);
```

with

```csharp
        Assert.Contains("Settings, Desk, Pieces", hidden.PlacementNote);
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskPiecesSettingsTests|FullyQualifiedName~DeskSplitPagesTests|FullyQualifiedName~LauncherSettingsTests"`
Expected: build FAILS: `SettingsLinkTo` doesn't exist.

- [ ] **Step 3: Add the routed link**

In `windows-dotnet/src/Sanduhr.App/Views/SettingsReach.cs`, after the `SettingsSidebar` class add:

```csharp

/// <summary>
/// A link to a Settings place on a button anywhere in the Settings window, page controls in their own files included:
/// <c>views:SettingsLinkTo.Link="desk#pieces"</c>. The window opens it when the button is clicked (<c>SettingsWindow</c>
/// listens for every Click), reading the text with <c>SettingsIndex.ParseLink</c>, so aliases and unknown anchors land as any
/// link does.
/// </summary>
public static class SettingsLinkTo
{
    public static readonly DependencyProperty LinkProperty = DependencyProperty.RegisterAttached(
        "Link", typeof(string), typeof(SettingsLinkTo), new PropertyMetadata(null));

    public static string? GetLink(DependencyObject d) => (string?)d.GetValue(LinkProperty);
    public static void SetLink(DependencyObject d, string? value) => d.SetValue(LinkProperty, value);
}
```

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs`, in the constructor, after `InitializeComponent();` add

```csharp
        // A button carrying views:SettingsLinkTo.Link opens that place, wherever it sits (Desk, Pieces' rows, a page's status line).
        AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler(OnSettingsLinkClick));
```

and after `OpenSettingsLink_Click` add

```csharp

    /// <summary>Opens the place a clicked button's <see cref="SettingsLinkTo.Link"/> names.</summary>
    private void OnSettingsLinkClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && SettingsLinkTo.GetLink(d) is { Length: > 0 } text
            && SettingsIndex.ParseLink(text) is { } link && link.Page != SettingsPage.Widget)
        {
            SelectPage(link.Page, link.Anchor);
            e.Handled = true;
        }
    }
```

- [ ] **Step 4: Rewrite the rows**

Replace the whole of `windows-dotnet/src/Sanduhr.App/Views/DeskPiecesPanel.xaml` with:

```xml
<UserControl x:Class="Sanduhr.App.Views.DeskPiecesPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:Sanduhr.App.ViewModels"
             xmlns:views="clr-namespace:Sanduhr.App.Views"
             xmlns:core="clr-namespace:Sanduhr.Core;assembly=Sanduhr.Core"
             Focusable="False">
    <!-- Settings, Desk, Pieces (spec, Desk: the split): one row per piece. Show is the piece's name; then Place, Size and Monitor,
         and on a second line its Style (the meters) or the link to its own page; then Order, live when another piece shares the
         place (WF5). Each row is two blocks in a WrapPanel, 490 + 230 DIP, so it is one line from 760 DIP of page width (720
         inside the page's margins) and wraps to two below (WF26). The Settings window supplies the styles (found at run time)
         and the theme brushes. DataContext: the Desk page. -->
    <UserControl.Resources>
        <DataTemplate x:Key="StylePicker">
            <ComboBox Width="138" Margin="0,6,0,0" HorizontalAlignment="Left" Style="{DynamicResource ThemedComboBox}"
                      ItemsSource="{x:Static vm:DeskSettingsViewModel.StyleOptions}"
                      SelectedItem="{Binding Style, Mode=TwoWay}"
                      IsEnabled="{Binding IsPlaced}"
                      AutomationProperties.Name="{Binding Name, StringFormat='{}{0}, style'}"
                      AutomationProperties.HelpText="Bars, Slim (one line over a thin bar), Rings (small gauges in a row) or Ticks (ten cells, kind to OLED screens)" />
        </DataTemplate>
        <DataTemplate x:Key="PageLink">
            <Button Style="{DynamicResource SettingsLinkButton}" HorizontalAlignment="Left" Margin="0,6,0,0"
                    Content="{Binding SettingsTitle}" views:SettingsLinkTo.Link="{Binding SettingsLink}"
                    AutomationProperties.Name="{Binding SettingsTitle}" />
        </DataTemplate>
    </UserControl.Resources>
    <ItemsControl x:Name="PieceRows" ItemsSource="{Binding Pieces}" Focusable="False"
                  AutomationProperties.Name="{x:Static core:SettingsNames.PiecesSection}">
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <WrapPanel Tag="PieceRow" Margin="0,0,0,8">
                    <Grid Width="490">
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="Auto" />
                        </Grid.RowDefinitions>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" MinWidth="118" />
                            <ColumnDefinition Width="138" />
                            <ColumnDefinition Width="8" />
                            <ColumnDefinition Width="72" />
                            <ColumnDefinition Width="8" />
                            <ColumnDefinition Width="146" />
                        </Grid.ColumnDefinitions>
                        <CheckBox Grid.Column="0" Style="{DynamicResource ThemedCheckBox}" VerticalAlignment="Center" Margin="0,0,6,0"
                                  Content="{Binding Name}" IsChecked="{Binding Shown, Mode=TwoWay}"
                                  AutomationProperties.Name="{Binding ShownName}" />
                        <ComboBox Grid.Column="1" Style="{DynamicResource ThemedComboBox}"
                                  ItemsSource="{x:Static vm:DeskSettingsViewModel.PlaceOptions}"
                                  SelectedItem="{Binding Place, Mode=TwoWay}"
                                  AutomationProperties.Name="{Binding Name, StringFormat='{}{0}, place'}" />
                        <ComboBox Grid.Column="3" Style="{DynamicResource ThemedComboBox}"
                                  ItemsSource="{x:Static vm:DeskSettingsViewModel.SizeSteps}"
                                  SelectedItem="{Binding Size, Mode=TwoWay}"
                                  IsEnabled="{Binding IsPlaced}"
                                  AutomationProperties.Name="{Binding Name, StringFormat='{}{0}, size'}">
                            <ComboBox.ItemTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding StringFormat='{}{0:0%}'}" />
                                </DataTemplate>
                            </ComboBox.ItemTemplate>
                        </ComboBox>
                        <ComboBox Grid.Column="5" Style="{DynamicResource ThemedComboBox}"
                                  ItemsSource="{Binding MonitorChoices}"
                                  SelectedItem="{Binding Monitor, Mode=TwoWay}"
                                  IsEnabled="{Binding IsPlaced}"
                                  AutomationProperties.Name="{Binding Name, StringFormat='{}{0}, monitor'}" />
                        <!-- The second line: the meter style (#336) on the Claude line, the Claude limits and telemetry, or the link to the
                             piece's own page. Made only for those rows (not merely hidden), so a screen reader never meets it elsewhere. -->
                        <ContentControl Grid.Row="1" Grid.Column="1" Grid.ColumnSpan="5" Focusable="False" IsTabStop="False">
                            <ContentControl.Style>
                                <Style TargetType="ContentControl">
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding HasStyle}" Value="True">
                                            <Setter Property="Content" Value="{Binding}" />
                                            <Setter Property="ContentTemplate" Value="{StaticResource StylePicker}" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding HasSettingsPage}" Value="True">
                                            <Setter Property="Content" Value="{Binding}" />
                                            <Setter Property="ContentTemplate" Value="{StaticResource PageLink}" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </ContentControl.Style>
                        </ContentControl>
                    </Grid>
                    <!-- Order (WF5): live when another piece shares the place. Spoken "Move <piece> up in <place>". -->
                    <StackPanel Width="230" Orientation="Horizontal" VerticalAlignment="Top" Margin="0">
                        <Button Style="{DynamicResource FlatButton}" Padding="8,4" Margin="8,0,4,0"
                                Content="{x:Static core:SettingsNames.MoveUp}" Command="{Binding MoveUpCommand}"
                                AutomationProperties.Name="{Binding MoveUpName}" />
                        <Button Style="{DynamicResource FlatButton}" Padding="8,4" Margin="0"
                                Content="{x:Static core:SettingsNames.MoveDown}" Command="{Binding MoveDownCommand}"
                                AutomationProperties.Name="{Binding MoveDownName}" />
                    </StackPanel>
                </WrapPanel>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</UserControl>
```

In `DeskPiecesPanel.xaml.cs`, replace the summary with `/// <summary>Settings, Desk, Pieces (spec, Desk: the split): each piece's Show, place, size, monitor, style or page link, and order. Its DataContext is the <see cref="ViewModels.DeskSettingsViewModel"/>.</summary>`.

(The second line's `Content` is set only by a trigger, as the style picker's was, so a row with neither a style nor a page has no content and no stop.)

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`, in the `pieces` section, replace

```xml
                                <TextBlock Text="Where each piece sits" FontSize="13" FontWeight="SemiBold"
                                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
```

with

```xml
                                <TextBlock Text="{x:Static core:SettingsNames.PiecesSection}" FontSize="13" FontWeight="SemiBold"
                                           AutomationProperties.HeadingLevel="Level2"
                                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
```

and its caption's `Text="Eight places: the four corners, … until it is back."` with

```xml
                                           Text="Tick a piece to show it at its usual place. Eight places: the four corners, the top and bottom centers, and the middle of each side. Size scales a piece from 60% to 160%. Move up and Move down change the order of the pieces that share a place. Monitor puts a piece on every monitor, on the main one, or on one monitor; a piece on a monitor that is not connected shows on the main one until it is back." />
```

- [ ] **Step 5: Rename the section in the index and retire the old title**

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`, in the `DeskPieces` row, replace `E("Where each piece sits", SettingsPage.Desk, DeskPieces,` with `E(SettingsNames.PiecesSection, SettingsPage.Desk, DeskPieces,` and add to the start of its synonym list `"where each piece sits", "show", "hide", "order", "reorder", "move up", "move down", "stack",`.

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, add to `Retired` (before its closing `];`):

```csharp
        ("Where each piece sits", PiecesSection, true),
```

- [ ] **Step 6: The Launcher's notes name the section the new way**

In `windows-dotnet/src/Sanduhr.App/ViewModels/LauncherSettingsViewModel.cs`, replace `/// itself is placed under Where each piece sits, like any other.` with `/// itself is placed in Settings, Desk, Pieces, like any other.`; replace

```csharp
        : !_isPlaced() ? "The Launcher is hidden. Pick a place for it under Where each piece sits."
```

with

```csharp
        : !_isPlaced() ? "The Launcher is hidden. Show it in Settings, Desk, Pieces."
```

and

```csharp
            _autoNote = $"Placed at {where}. Move it under Where each piece sits.";
```

with

```csharp
            _autoNote = $"Placed at {where}. Move it in Settings, Desk, Pieces.";
```

- [ ] **Step 7: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskPiecesSettingsTests|FullyQualifiedName~DeskSplitPagesTests|FullyQualifiedName~LauncherSettingsTests|FullyQualifiedName~DeskMonitorTests|FullyQualifiedName~DeskMeterStylesViewTests|FullyQualifiedName~SettingsWindowNamesTests|FullyQualifiedName~SettingsReachTests"`
Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsDeskSplitIndexTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsNamesTests"`
Expected: PASS. The slice 2 `[DesktopFact]` Tab walk runs here too and must still end every page at the search box.

- [ ] **Step 8: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Views/DeskPiecesPanel.xaml windows-dotnet/src/Sanduhr.App/Views/DeskPiecesPanel.xaml.cs windows-dotnet/src/Sanduhr.App/Views/SettingsReach.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.App/ViewModels/LauncherSettingsViewModel.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskPiecesSettingsTests.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs windows-dotnet/tests/Sanduhr.App.Tests/LauncherSettingsTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs
git commit -m "feat(desk): Pieces rows wrap, carry Show, Order and their page's link; links open from any page control"
```

---

### Task 9: The Companion row: the companion moves from Widgets into Desk, Pieces

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/DeskCompanionViewModel.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetsPageViewModel.cs` (the companion leaves: the `CompanionChoice` record, the companion members, `ReloadCompanions`, `StatusFor`, two calls)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/WidgetsPage.xaml` (the `widgets-companion` block and the separator after it go)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs` (constructor parameter `widgets`; `Companion`; `DeskPieceRow.Companion`, `IsCompanion`; reloads)
- Modify: `windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs` (`Paths`)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/DeskPiecesPanel.xaml` (the companion's second line)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (the `widgets-companion` row leaves; the `pieces` row and `NameAnchors`), `SettingsNames.cs` (`Table`, `Retired`, `WidgetCompanionShow` goes)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/CompanionPieceTests.cs:299-348`; `windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs`, `SettingsLinkTests.cs:59-72`

**Interfaces:**
- Consumes: `DeskPieceRow.Shown` (Task 7), the `PageLink` pattern and `SettingsLinkTo` (Task 8).
- Produces: `public sealed partial class DeskCompanionViewModel` (`Companions`, `SelectedCompanion`, `Roams`, `HasCompanions`, `Status`, `Line`, `PickName`, `RoamName`, `NoneYet`, `GetOneTitle`, `GetOneLink`, `Reload()`, `RefreshStatus()`, `Changed`); `CompanionChoice` moves into that file unchanged; `DeskSettingsViewModel(…, Func<WidgetStore>? widgets = null)`, `DeskSettingsViewModel.Companion`; `DeskPieceRow.Companion` (non-null on the companion row), `IsCompanion`; `SettingsStore.Paths`.

- [ ] **Step 1: Write the failing tests**

In `windows-dotnet/tests/Sanduhr.App.Tests/CompanionPieceTests.cs`, replace the two tests `Settings_widgets_picks_the_sprite_shows_or_hides_the_piece_and_lets_it_roam` and `The_companion_controls_carry_their_names_for_a_screen_reader` with:

```csharp
    [Fact]
    public void Desk_pieces_companion_row_picks_the_sprite_shows_or_hides_it_and_lets_it_roam()
    {
        Install("clock:bl companion:bl", roam: false);
        var svc = new WidgetCatalogService(_paths, new FakeGitHub(), WidgetKit.V380, []);
        var desk = new DeskSettingsViewModel(_store, [], () => [], widgets: () => svc.Store);
        int changed = 0;
        desk.Changed = () => changed++;
        var row = desk.Pieces.Single(p => p.Widget == CompanionSettings.Widget);
        Assert.True(row.IsCompanion);
        var companion = Assert.IsType<DeskCompanionViewModel>(row.Companion);
        Assert.Null(desk.Pieces.Single(p => p.Widget == "clock").Companion);

        var choice = Assert.Single(companion.Companions);
        Assert.Equal((PackId, "Tiny sprite"), (choice.Id, choice.Name));
        Assert.True(companion.HasCompanions);
        Assert.Equal(choice, companion.SelectedCompanion);
        Assert.True(row.Shown);
        Assert.False(companion.Roams);
        Assert.Equal("Stage 1 of 2, Small. 0 coding days so far.", companion.Status);
        Assert.Equal(0, changed);

        companion.Roams = true;
        Assert.True(_store.LoadCompanion().Roam);
        row.Shown = false;
        Assert.Equal("clock:bl", _store.LoadDesk().Layout);
        Assert.Contains("hidden", companion.Status);
        row.Shown = true;
        Assert.Equal("clock:bl companion:bl", _store.LoadDesk().Layout);   // back where it was in this window
        Assert.Equal(3, changed);

        // Gone from disk: the row lists no sprite and says how to get one.
        Assert.True(svc.Store.Uninstall(PackId).Ok);
        desk.Reload();
        Assert.False(companion.HasCompanions);
        Assert.Null(companion.SelectedCompanion);
        Assert.Equal("widgets#widgets-catalog", companion.GetOneLink);
    }

    [Fact]
    public void A_fresh_companion_is_shown_at_its_default_place()
    {
        Install("clock:bl", roam: false);
        var svc = new WidgetCatalogService(_paths, new FakeGitHub(), WidgetKit.V380, []);
        var desk = new DeskSettingsViewModel(_store, [], () => [], widgets: () => svc.Store);
        desk.Pieces.Single(p => p.Widget == CompanionSettings.Widget).Shown = true;
        Assert.Equal("clock:bl companion:bc", _store.LoadDesk().Layout);   // DeskArrangement.DefaultPlaceFor("companion")
    }

    [Fact]
    public void The_companion_controls_carry_their_names_and_live_on_the_Desk_page()
    {
        var panel = File.ReadAllText(Path.Combine(AppDir(), "Views", "DeskPiecesPanel.xaml"));
        Assert.Contains("AutomationProperties.Name=\"{x:Static core:SettingsNames.WidgetCompanionPick}\"", panel);
        Assert.Contains("AutomationProperties.Name=\"{x:Static core:SettingsNames.WidgetCompanionRoam}\"", panel);
        Assert.Contains("views:SettingsLinkTo.Link=\"{Binding GetOneLink}\"", panel);
        Assert.DoesNotContain("widgets-companion", File.ReadAllText(Path.Combine(AppDir(), "Views", "WidgetsPage.xaml")));
        Assert.Contains((SettingsNames.WidgetCompanionPick, SettingsPage.Desk), SettingsNames.Table);
        Assert.Contains((SettingsNames.WidgetCompanionRoam, SettingsPage.Desk), SettingsNames.Table);
    }

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Sanduhr.App");
    }
```

In `windows-dotnet/tests/Sanduhr.Tests/SettingsLinkTests.cs`, in `Check_sends_an_old_place_to_where_it_lives_now`, replace

```csharp
        // With it, the place is live and stays.
        Assert.Equal(new SettingsLink(SettingsPage.Widgets, SettingsIndex.WidgetsCompanion),
            SettingsIndex.Check(SettingsPage.Widgets, SettingsIndex.WidgetsCompanion));
```

with

```csharp
        // With it too, now that the companion left Widgets (slice 3): the old section is gone from the page, so the alias carries it.
        Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskPieces),
            SettingsIndex.Check(SettingsPage.Widgets, SettingsIndex.WidgetsCompanion));
```

Append to `SettingsDeskSplitIndexTests`:

```csharp
    [Fact]
    public void The_companion_is_found_on_Desk_Pieces_and_its_old_link_lands_there()
    {
        Assert.Null(SettingsIndex.Find(SettingsPage.Widgets, SettingsIndex.WidgetsCompanion));
        Assert.Contains(SettingsIndex.Search("companion"), h => h.Entry.Page == SettingsPage.Desk && h.Entry.Anchor == SettingsIndex.DeskPieces);
        Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskPieces),
            SettingsIndex.Resolve(SettingsPage.Widgets, SettingsIndex.WidgetsCompanion));
        Assert.Equal(["Show the companion on the Desk"], SettingsNames.RetiredNames("Show the companion on the Desk"));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~CompanionPieceTests`
Expected: build FAILS: no `DeskCompanionViewModel`, no `widgets` parameter.

- [ ] **Step 3: Move the companion's view model**

Create `windows-dotnet/src/Sanduhr.App/ViewModels/DeskCompanionViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>An installed sprite pack the companion can be: its id and its name.</summary>
public sealed record CompanionChoice(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Desk, Pieces, the Companion row (spec, Desk: the split; it was Settings, Widgets, Desk companion): which installed sprite is the
/// companion, whether it roams, and how it is growing. Whether it shows is the row's Show box (desk_layout), not a switch of its
/// own. The keys are unchanged: desk_companion.pack and desk_companion.roam.
/// </summary>
public sealed partial class DeskCompanionViewModel : ObservableObject
{
    private readonly SettingsStore _prefs;
    private readonly Func<WidgetStore> _widgets;
    private bool _loading;

    internal DeskCompanionViewModel(SettingsStore prefs, Func<WidgetStore> widgets)
    {
        _prefs = prefs;
        _widgets = widgets;
    }

    /// <summary>Called after the sprite or roaming changes (the Desk reloads).</summary>
    public Action? Changed { get; set; }

    /// <summary>The installed sprite packs, the companion's choices.</summary>
    public ObservableCollection<CompanionChoice> Companions { get; } = [];

    [ObservableProperty] private CompanionChoice? _selectedCompanion;
    [ObservableProperty] private bool _roams;
    [ObservableProperty] private bool _hasCompanions;
    [ObservableProperty] private string _status = "";

    /// <summary>The row's first line (WF21).</summary>
    public string Line => SettingsNames.CompanionLine;
    public string PickName => SettingsNames.WidgetCompanionPick;
    public string RoamName => SettingsNames.WidgetCompanionRoam;
    public string NoneYet => SettingsNames.NoCompanionYet;
    public string GetOneTitle => SettingsNames.GetACompanion;
    /// <summary>The catalog, where a sprite is installed (the alias table carries it to Mods &amp; Widgets in slice 4).</summary>
    public string GetOneLink => SettingsIndex.PageId(SettingsPage.Widgets) + "#" + SettingsIndex.WidgetsCatalog;

    partial void OnSelectedCompanionChanged(CompanionChoice? value)
    {
        if (_loading || value is null) return;
        _prefs.SaveCompanion(_prefs.LoadCompanion() with { Pack = value.Id });
        Status = StatusFor(value.Id);
        Changed?.Invoke();
    }

    partial void OnRoamsChanged(bool value)
    {
        if (_loading) return;
        _prefs.SaveCompanion(_prefs.LoadCompanion() with { Roam = value });
        Changed?.Invoke();
    }

    /// <summary>Lists the installed sprites and reads the companion's choices. A pick that is no longer installed falls back to the
    /// first sprite, as the Desk does.</summary>
    public void Reload()
    {
        _loading = true;
        try
        {
            Companions.Clear();
            var store = _widgets();
            WidgetRegistry reg;
            try { reg = store.Load(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { reg = WidgetRegistry.Empty; }
            foreach (var row in reg.Packs)
                if (store.ReadInfo(row.Id) is { Kind: "sprite" } info)
                    Companions.Add(new CompanionChoice(row.Id, info.Name));
            HasCompanions = Companions.Count > 0;
            if (!HasCompanions)
            {
                SelectedCompanion = null;
                Status = "";
                return;
            }
            var settings = _prefs.LoadCompanion();
            var active = settings.ActivePack(reg, id => store.ReadInfo(id)?.Kind);
            SelectedCompanion = Companions.FirstOrDefault(c => c.Id == active);
            Roams = settings.Roam;
            Status = active is null ? "" : StatusFor(active);
        }
        finally { _loading = false; }
    }

    /// <summary>The growth line again: the piece was shown or hidden, or the Desk turned on or off.</summary>
    public void RefreshStatus()
    {
        if (SelectedCompanion is { } c) Status = StatusFor(c.Id);
    }

    /// <summary>"Stage 2 of 4, Sprout. 5 coding days so far." and, when the Desk is off or the piece hidden, how to see it.</summary>
    private string StatusFor(string packId)
    {
        string line = "";
        try
        {
            var store = _widgets();
            var growth = CompanionGrowthState.Load(Path.Combine(store.Root, CompanionGrowthState.FileName));
            var file = Path.Combine(store.PackDir(packId), "sprite.json");
            if (File.Exists(file) && CompanionSprite.Parse(File.ReadAllBytes(file)) is { } sprite)
            {
                var stage = sprite.StageAt(growth.Growth);
                int at = sprite.Stages.ToList().IndexOf(stage) + 1;
                string days = growth.Days.Count == 1 ? "1 coding day" : $"{growth.Days.Count} coding days";
                line = sprite.Stages.Count > 1
                    ? $"Stage {at} of {sprite.Stages.Count}, {stage.Name}. {days} so far."
                    : $"{days} so far.";
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        var desk = _prefs.LoadDesk();
        if (!desk.Enabled) line = (line + " Turn the Desk on in Settings, Desk to see it.").Trim();
        else if (new DeskArrangement(desk.Layout).Placement(CompanionSettings.Widget) is null)
            line = (line + " It is hidden; tick " + SettingsNames.DeskCompanion + " to show it.").Trim();
        return line;
    }
}
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetsPageViewModel.cs`:
- delete the `CompanionChoice` record and its summary (it now lives in `DeskCompanionViewModel.cs`);
- in the class summary, replace `the Desk companion (which installed sprite, shown or not, roaming or\n/// not), the verified catalog` with `the verified catalog` (one sentence: "Settings ▸ Widgets (spec 3.8, sections 2 and 3): the verified catalog with Install and Update, …"; the companion moved to Settings, Desk, Pieces in Settings v2);
- delete every companion member: from `/// <summary>The installed sprite packs, the companion's choices.</summary>` through the end of `StatusFor` (the `Companions` collection, the five companion `[ObservableProperty]` fields, `CompanionPickName`, `CompanionShowName`, `CompanionRoamName`, the three `partial void OnCompanion…Changed`/`OnSelectedCompanionChanged` methods, `ReloadCompanions` and `StatusFor`);
- delete the line `        ReloadCompanions();` in `Activate` and in `Rebuild`;
- keep `_settings`, `Prefs` and the `store` constructor parameter only if something else still uses them; otherwise delete `Prefs` and `_settings` but keep the parameter (tests pass it), with the comment `// settings.json is no longer read here; the parameter stays for callers.`

In `windows-dotnet/src/Sanduhr.App/Views/WidgetsPage.xaml`, delete the block from `<!-- The Desk companion (spec 3.8 section 4): one sprite at a time, shown or hidden, roaming or sitting. -->` through the `</StackPanel>` that closes `views:SettingsAnchor.Id="widgets-companion"`, and the `<Border Height="1" … Margin="0,8,0,16" />` right after it.

In `windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs`, in the constructor add `Paths = paths;` and add the property

```csharp
    /// <summary>The folders this store reads and writes under (a scratch profile in tests).</summary>
    public Paths Paths { get; }
```

- [ ] **Step 4: The Desk owns the companion; the row shows it**

In `windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs`:

Change the constructor signature to

```csharp
    public DeskSettingsViewModel(SettingsStore store, IReadOnlyList<string> fontFamilies, Func<IReadOnlyList<DeskMonitorInfo>>? monitors = null,
        Func<IReadOnlyList<(string Name, string Value)>>? inkPresets = null, Func<WidgetStore>? widgets = null)
```

with `/// <param name="widgets">The installed packs (the companion's sprites). Default: this store's own widgets folder.</param>` added to its doc, and before `Pieces =` add

```csharp
        Companion = new DeskCompanionViewModel(store,
            widgets ?? (() => new WidgetStore(store.Paths.WidgetsDir, Sanduhr.App.Services.WidgetCatalogService.EncodePng)))
        {
            Changed = () => Changed?.Invoke(),
        };
```

Add the property after `Designs`:

```csharp
    /// <summary>Desk, Pieces, the Companion row's sprite and roaming (moved from Settings, Widgets).</summary>
    public DeskCompanionViewModel Companion { get; }
```

In `Load()`, after `LoadRows(a);` add `Companion.Reload();`; in `Edit`, after `LoadRows(a);` add `Companion.RefreshStatus();`; in `OnEnabledChanged`, after the `Save` line add `Companion.RefreshStatus();`.

In `DeskPieceRow`, after `HasStyle` add

```csharp
    /// <summary>The companion's row carries its sprite and roaming inline (spec, Pieces).</summary>
    public bool IsCompanion => Widget == CompanionSettings.Widget;
    public DeskCompanionViewModel? Companion => IsCompanion ? _owner.Companion : null;
```

In `windows-dotnet/src/Sanduhr.App/Views/DeskPiecesPanel.xaml`, add to `UserControl.Resources`:

```xml
        <BooleanToVisibilityConverter x:Key="BoolVis" />
        <DataTemplate x:Key="CompanionExtras">
            <StackPanel Margin="0,6,0,0" DataContext="{Binding Companion}">
                <TextBlock Text="{Binding Line}" FontSize="11" TextWrapping="Wrap"
                           Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                <WrapPanel Margin="0,6,0,0" Visibility="{Binding HasCompanions, Converter={StaticResource BoolVis}}">
                    <ComboBox Width="190" Style="{DynamicResource ThemedComboBox}" Margin="0,0,8,6"
                              ItemsSource="{Binding Companions}" DisplayMemberPath="Name"
                              SelectedItem="{Binding SelectedCompanion, Mode=TwoWay}"
                              AutomationProperties.Name="{x:Static core:SettingsNames.WidgetCompanionPick}" />
                    <CheckBox Style="{DynamicResource ThemedCheckBox}" VerticalAlignment="Center" Margin="0,0,0,6"
                              Content="{x:Static core:SettingsNames.WidgetCompanionRoam}"
                              IsChecked="{Binding Roams, Mode=TwoWay}"
                              AutomationProperties.Name="{x:Static core:SettingsNames.WidgetCompanionRoam}"
                              AutomationProperties.HelpText="It walks the edges of its monitor. With Windows' animations off it stays put." />
                </WrapPanel>
                <TextBlock Text="{Binding Status}" FontSize="11" TextWrapping="Wrap"
                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                           AutomationProperties.Name="Companion status" AutomationProperties.LiveSetting="Polite"
                           Visibility="{Binding HasCompanions, Converter={StaticResource BoolVis}}" />
                <WrapPanel Visibility="{Binding HasCompanions, Converter={x:Static views:WidgetsPage.NotVisible}}">
                    <TextBlock Text="{Binding NoneYet}" FontSize="11" VerticalAlignment="Center" Margin="0,0,6,0"
                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                    <Button Style="{DynamicResource SettingsLinkButton}" Content="{Binding GetOneTitle}"
                            views:SettingsLinkTo.Link="{Binding GetOneLink}"
                            AutomationProperties.Name="{Binding GetOneTitle}" />
                </WrapPanel>
            </StackPanel>
        </DataTemplate>
```

and in the row's second-line `ContentControl` style triggers add

```xml
                                        <DataTrigger Binding="{Binding IsCompanion}" Value="True">
                                            <Setter Property="Content" Value="{Binding}" />
                                            <Setter Property="ContentTemplate" Value="{StaticResource CompanionExtras}" />
                                        </DataTrigger>
```

(`WidgetsPage.NotVisible` is the existing static converter `WidgetsPage.xaml` used for the same empty state.)

- [ ] **Step 5: The index and the names follow the companion**

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`:
- delete the row `E("Desk companion", SettingsPage.Widgets, WidgetsCompanion, …),` (both lines);
- add to the `DeskPieces` row's synonyms `"companion", "pet", "sprite", "hourglass", "roam", "walk", "grow", "growth", "stage", "mood", SettingsNames.WidgetCompanionPick, SettingsNames.WidgetCompanionRoam`;
- in `NameAnchors`, replace `[SettingsNames.WidgetCompanionShow] = WidgetsCompanion,` and `[SettingsNames.WidgetCompanionRoam] = WidgetsCompanion,` with `[SettingsNames.WidgetCompanionPick] = DeskPieces,` and `[SettingsNames.WidgetCompanionRoam] = DeskPieces,`;
- in `PageSynonyms[SettingsPage.Widgets]`, remove `"companion"` and `"pet"` (the catalog row keeps them: that is where a sprite is installed).

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`:
- delete `WidgetCompanionShow` and its summary;
- change the summaries of `WidgetCompanionPick` and `WidgetCompanionRoam` to `/// <summary>Desk, Pieces, the Companion row: the installed sprite that is the companion (one at a time).</summary>` and `/// <summary>Desk, Pieces, the Companion row: it walks the screen's edges (never with Windows' animations off).</summary>`;
- in `Table`, replace `(WidgetCompanionShow, SettingsPage.Widgets),` and `(WidgetCompanionRoam, SettingsPage.Widgets),` with `(WidgetCompanionPick, SettingsPage.Desk),` and `(WidgetCompanionRoam, SettingsPage.Desk),`;
- add to `Retired`: `("Show the companion on the Desk", DeskCompanion, false),`.

Then `git grep -n "WidgetCompanionShow\|ReloadCompanions\|CompanionShown" windows-dotnet` must print nothing.

- [ ] **Step 6: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~CompanionPieceTests|FullyQualifiedName~WidgetsPageNamesTests|FullyQualifiedName~WidgetCatalogServiceTests|FullyQualifiedName~WidgetReviewServiceTests|FullyQualifiedName~DeskPiecesSettingsTests|FullyQualifiedName~DeskSplitPagesTests|FullyQualifiedName~SettingsWindowNamesTests"`
Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsDeskSplitIndexTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~WhatsNewTests|FullyQualifiedName~SettingsReachIndexTests"`
Expected: PASS (the What's New "companion" card resolves through the now-live alias to `desk#pieces`).

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/DeskCompanionViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/WidgetsPageViewModel.cs windows-dotnet/src/Sanduhr.App/Views/WidgetsPage.xaml windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs windows-dotnet/src/Sanduhr.App/Views/DeskPiecesPanel.xaml windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.App.Tests/CompanionPieceTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsLinkTests.cs
git commit -m "feat(desk): the companion's sprite and roaming move into its Pieces row; its Show is the row's"
```

---

### Task 10: Message, Now playing and Weather say where their piece sits, and turning them on places it

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/Views/DeskPieceStatusPanel.xaml(.cs)`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/DeskContentPagesViewModels.cs` (`NowPlayingPageViewModel`, `WeatherPageViewModel`)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/DeskMessagePageViewModel.cs` (`Piece`, `AddLine`)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (wiring)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (Now playing and Weather: the status line; the two "Desk page" sentences go)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/DeskMessagePage.xaml` (the status line)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs`

**Interfaces:**
- Consumes: `DeskSettingsViewModel.PieceStatus` and `DeskPieceStatusViewModel` (Task 7), `SettingsLinkTo` (Task 8).
- Produces: `DeskPieceStatusPanel` (DataContext: a `DeskPieceStatusViewModel`; parts named `StatusText`, `DeskLink`, `PlaceButton`); `public DeskPieceStatusViewModel? Piece { get; internal set; }` on `NowPlayingPageViewModel`, `WeatherPageViewModel` and `DeskMessagePageViewModel`, set by `SettingsViewModel`.

- [ ] **Step 1: Write the failing tests**

Append to `DeskSplitPagesTests`:

```csharp
    private static Sanduhr.App.Services.SettingsStore Store() => new(new Paths());   // the fixture's scratch profile

    [Fact]
    public void Turning_a_feature_on_from_its_page_places_its_piece() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        string Layout() => Store().LoadDesk().Layout;
        Assert.DoesNotContain("nowPlaying", Layout());

        svm.NowPlayingPage.Bars = true;
        Assert.Contains("nowPlaying:br", Layout());
        svm.WeatherPage.Picked = WeatherPlace.Create("Fort Worth", "Texas", "United States", 32.75, -97.33);
        Assert.Contains("weather:tr", Layout());
        svm.MessagePage.AddLineCommand.Execute(null);
        Assert.Contains("message:bl", Layout());

        Assert.Equal(SettingsNames.PieceStatus("Top right", svm.Desk.Enabled), svm.WeatherPage.Piece!.Text);
        Assert.True(svm.NowPlayingPage.Piece!.IsPlaced);
        Assert.True(svm.MessagePage.Piece!.IsPlaced);
    });

    [Fact]
    public void An_on_action_leaves_a_placed_piece_where_it_is() => AppHost.Run(() =>
    {
        var store = Store();
        store.SaveDesk(store.LoadDesk() with { Layout = "clock:bl weather:bl nowPlaying:tl message:tc" });
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);

        svm.WeatherPage.Picked = WeatherPlace.Create("Fort Worth", "Texas", "United States", 32.75, -97.33);
        svm.NowPlayingPage.Bars = true;
        svm.NowPlayingPage.Bars = false;
        svm.NowPlayingPage.Style = true;
        svm.MessagePage.AddLineCommand.Execute(null);
        Assert.Equal("clock:bl weather:bl nowPlaying:tl message:tc", Store().LoadDesk().Layout);
    });

    [Fact]
    public void Now_playing_and_Weather_show_their_line_at_the_top() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            foreach (var page in new[] { SettingsPage.NowPlaying, SettingsPage.Weather, SettingsPage.DeskMessage })
            {
                window.SelectPage(page);
                Settle(window, guard);
                var status = Below<DeskPieceStatusPanel>((DependencyObject)TabOf(window, page).Content).Single();
                var place = (Button)status.FindName("PlaceButton");
                Assert.True(place.IsVisible);
                Assert.Equal(SettingsNames.PlaceOnDesk, System.Windows.Automation.AutomationProperties.GetName(place));
                place.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Settle(window, guard);
                Assert.False(place.IsVisible);
                var link = (Button)status.FindName("DeskLink");
                Assert.True(link.IsVisible);
                Assert.Equal("desk#pieces", SettingsLinkTo.GetLink(link));
            }
        }
        finally { window.Close(); }
    });
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~DeskSplitPagesTests`
Expected: build FAILS: `WeatherPageViewModel` has no `Piece`.

- [ ] **Step 3: The status control**

Create `windows-dotnet/src/Sanduhr.App/Views/DeskPieceStatusPanel.xaml`:

```xml
<UserControl x:Class="Sanduhr.App.Views.DeskPieceStatusPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:views="clr-namespace:Sanduhr.App.Views"
             Focusable="False">
    <!-- A feature page's line about its Desk piece (Settings v2, Principle 1): "On the Desk: bottom right." and "Desk settings…",
         or "Not on the Desk." and "Place it on the Desk". A status, never a second control. DataContext: a DeskPieceStatusViewModel.
         The Settings window supplies SettingsLinkButton and FlatButton. -->
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BoolVis" />
    </UserControl.Resources>
    <WrapPanel>
        <TextBlock x:Name="StatusText" Text="{Binding Text}" FontSize="11" TextWrapping="Wrap" VerticalAlignment="Center" Margin="0,0,8,4"
                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" AutomationProperties.LiveSetting="Polite" />
        <Button x:Name="DeskLink" Style="{DynamicResource SettingsLinkButton}" Margin="0,0,0,4"
                Content="{Binding DeskSettingsTitle}" views:SettingsLinkTo.Link="{Binding DeskSettingsLink}"
                AutomationProperties.Name="{Binding DeskSettingsTitle}"
                Visibility="{Binding IsPlaced, Converter={StaticResource BoolVis}}" />
        <Button x:Name="PlaceButton" Style="{DynamicResource FlatButton}" Padding="10,4" Margin="0,0,0,4"
                Content="{Binding PlaceTitle}" Command="{Binding PlaceCommand}"
                AutomationProperties.Name="{Binding PlaceTitle}"
                Visibility="{Binding IsHidden, Converter={StaticResource BoolVis}}" />
    </WrapPanel>
</UserControl>
```

Create `DeskPieceStatusPanel.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace Sanduhr.App.Views;

/// <summary>A feature page's line about its Desk piece (Settings v2, slice 3). Its DataContext is a
/// <see cref="ViewModels.DeskPieceStatusViewModel"/>.</summary>
public partial class DeskPieceStatusPanel : UserControl
{
    public DeskPieceStatusPanel() => InitializeComponent();
}
```

- [ ] **Step 4: The pages' on-actions**

In `windows-dotnet/src/Sanduhr.App/ViewModels/DeskContentPagesViewModels.cs`, in `NowPlayingPageViewModel`, after `public Action? Changed { get; set; }` add

```csharp

    /// <summary>The Now playing piece's line at the page top (set by <see cref="SettingsViewModel"/>).</summary>
    public DeskPieceStatusViewModel? Piece { get; internal set; }
```

and replace

```csharp
    partial void OnBarsChanged(bool value) { if (!_loading) Save(_saved with { Bars = value }); }
```

and

```csharp
    partial void OnStyleChanged(bool value) { if (!_loading) Save(_saved with { Style = value }); }
```

with

```csharp
    // The bars and the song's look draw on the Desk: turning either on shows a hidden piece (Principle 4).
    partial void OnBarsChanged(bool value)
    {
        if (_loading) return;
        Save(_saved with { Bars = value });
        if (value) Piece?.PlaceIfHidden();
    }
```

and

```csharp
    partial void OnStyleChanged(bool value)
    {
        if (_loading) return;
        Save(_saved with { Style = value });
        if (value) Piece?.PlaceIfHidden();
    }
```

In `WeatherPageViewModel`, change the class summary's last sentence `The piece itself is placed on the Desk page.` to `Picking a place shows a hidden Weather piece (Principle 4); where it sits is Settings, Desk, Pieces.`, add after `public Action? Changed { get; set; }`

```csharp

    /// <summary>The Weather piece's line at the page top (set by <see cref="SettingsViewModel"/>).</summary>
    public DeskPieceStatusViewModel? Piece { get; internal set; }
```

and in `OnPickedChanged`, after `City = "";` add `        Piece?.PlaceIfHidden();`.

In `windows-dotnet/src/Sanduhr.App/ViewModels/DeskMessagePageViewModel.cs`, add near the page's other public properties

```csharp
    /// <summary>The Message piece's line at the page top (set by <see cref="SettingsViewModel"/>).</summary>
    public DeskPieceStatusViewModel? Piece { get; internal set; }
```

and in `AddLine`, after `OnPropertyChanged(nameof(EditorText));` add

```csharp
        // Writing a line is turning the message on: a hidden piece is shown (Principle 4).
        Piece?.PlaceIfHidden();
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`, after `WeatherPage = new WeatherPageViewModel(new SettingsStore(new Paths()));` add

```csharp
        // Each feature page says where its Desk piece sits and can place it (Settings v2, Principle 1): one line per piece,
        // reading and writing the same desk_layout as Desk, Pieces.
        MessagePage.Piece = Desk.PieceStatus("message");
        NowPlayingPage.Piece = Desk.PieceStatus("nowPlaying");
        WeatherPage.Piece = Desk.PieceStatus("weather");
```

- [ ] **Step 5: The lines on the pages**

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`, in the Now playing TabItem, replace

```xml
                            <TextBlock Text="Now playing" FontSize="14" FontWeight="SemiBold"
                                       Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,12" />
```

with

```xml
                            <TextBlock Text="Now playing" FontSize="14" FontWeight="SemiBold" AutomationProperties.HeadingLevel="Level1"
                                       Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                            <views:DeskPieceStatusPanel DataContext="{Binding Piece}" Margin="0,0,0,12" />
```

and at the end of the `np-widget` caption delete the sentence ` To show it on the Desk, place Now playing on the Desk page.` (the text ends `Sanduhr never saves or logs it.`).

In the Weather TabItem, replace

```xml
                            <TextBlock Text="Weather" FontSize="14" FontWeight="SemiBold"
                                       Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,12" />
```

with

```xml
                            <TextBlock Text="Weather" FontSize="14" FontWeight="SemiBold" AutomationProperties.HeadingLevel="Level1"
                                       Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                            <views:DeskPieceStatusPanel DataContext="{Binding Piece}" Margin="0,0,0,12" />
```

and at the end of its privacy caption delete the sentence ` Place Weather on the Desk page.` (the text ends `and stays in memory.`).

In `windows-dotnet/src/Sanduhr.App/Views/DeskMessagePage.xaml`, replace

```xml
                <TextBlock Text="Message" FontSize="14" FontWeight="SemiBold"
                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,12" />
```

with

```xml
                <TextBlock Text="Message" FontSize="14" FontWeight="SemiBold" AutomationProperties.HeadingLevel="Level1"
                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                <views:DeskPieceStatusPanel DataContext="{Binding Piece}" Margin="0,0,0,12" />
```

- [ ] **Step 6: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskSplitPagesTests|FullyQualifiedName~NowPlayingLooksSettingsPageTests|FullyQualifiedName~MessageModeSegmentTests|FullyQualifiedName~SettingsWindowNamesTests|FullyQualifiedName~DeskPiecesSettingsTests"`
Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsSourceScanTests`
Expected: PASS (the new panel matches `*Panel.xaml`, so the label scans read it).

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Views/DeskPieceStatusPanel.xaml windows-dotnet/src/Sanduhr.App/Views/DeskPieceStatusPanel.xaml.cs windows-dotnet/src/Sanduhr.App/ViewModels/DeskContentPagesViewModels.cs windows-dotnet/src/Sanduhr.App/ViewModels/DeskMessagePageViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/Views/DeskMessagePage.xaml windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs
git commit -m "feat(desk): Message, Now playing and Weather say where their piece sits, and turning them on places it"
```

---

### Task 11: Desk Look: fonts, sizes, ink and color, and the back plate in one page

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Views/DeskLookPage.xaml` (the sections)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (Desk: `look`, `desk-plate`, `ink` and their separators go)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/DeskMessagePage.xaml` (`message-look` and its separator go)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs` (presets shown without Custom)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (rows, `NameAnchors`, `PageSynonyms[Desk]`, a `DeskSizes` constant), `SettingsNames.cs` (`Table`; `DeskInk` goes)
- Test: `windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs`, `SettingsNamesTests.cs:214-230`, `SettingsSourceScanTests.cs` (the footer count), `DeskPlateTests.cs:131`, `SettingsReachIndexTests.cs:110-116`; `windows-dotnet/tests/Sanduhr.App.Tests/DeskInkPackPresetsTests.cs`, `DeskSplitPagesTests.cs`

**Interfaces:**
- Consumes: the page stub (Task 6), the names (Task 3).
- Produces: anchors on `DeskLook`: `look` ("Fonts"), `sizes` ("Sizes", new constant `SettingsIndex.DeskSizes`), `ink` ("Ink and color"), `desk-plate` ("Back plate"); `DeskSettingsViewModel.ShowsInkColors` (true unless Ink is Automatic); choosing a preset sets Ink to Custom. Aliases `desk#look`, `desk#ink`, `desk#desk-plate` and `deskmessage#message-look` go live.

- [ ] **Step 1: Write the failing tests**

Append to `SettingsDeskSplitIndexTests`:

```csharp
    [Fact]
    public void Desk_Look_holds_four_sections_and_the_old_links_land_there()
    {
        Assert.Equal([SettingsIndex.DeskLook, SettingsIndex.DeskSizes, SettingsIndex.DeskInk, SettingsIndex.DeskPlate],
            SettingsIndex.EntriesOn(SettingsPage.DeskLook).Select(e => e.Anchor));
        Assert.Equal([SettingsNames.FontsSection, SettingsNames.SizesSection, SettingsNames.InkAndColor, SettingsNames.BackPlate],
            SettingsIndex.EntriesOn(SettingsPage.DeskLook).Select(e => e.Title));
        foreach (var (from, anchor, to) in new[]
        {
            (SettingsPage.Desk, SettingsIndex.DeskLook, SettingsIndex.DeskLook),
            (SettingsPage.Desk, SettingsIndex.DeskInk, SettingsIndex.DeskInk),
            (SettingsPage.Desk, SettingsIndex.DeskPlate, SettingsIndex.DeskPlate),
            (SettingsPage.DeskMessage, SettingsIndex.MessageLook, SettingsIndex.DeskSizes),
        })
            Assert.Equal(new SettingsLink(SettingsPage.DeskLook, to), SettingsIndex.Resolve(from, anchor));
        Assert.Null(SettingsIndex.Find(SettingsPage.DeskMessage, SettingsIndex.MessageLook));
    }

    [Fact]
    public void Font_lands_on_Desk_Look_Fonts_first()
    {
        // Slice 2's boost, live now that its page is (slice 2 final review: pin "font" once Desk Look lands).
        var top = SettingsIndex.Search("font")[0];
        Assert.Equal((SettingsPage.DeskLook, SettingsIndex.DeskLook, -1), (top.Entry.Page, top.Entry.Anchor, top.Rank));
        Assert.Contains(SettingsIndex.Search("message color"), h => h.Entry.Page == SettingsPage.DeskLook && h.Entry.Anchor == SettingsIndex.DeskInk);
        Assert.Contains(SettingsIndex.Search("clock size"), h => h.Entry.Page == SettingsPage.DeskLook && h.Entry.Anchor == SettingsIndex.DeskSizes);
    }

    [Fact]
    public void The_moved_names_sit_on_Desk_Look()
    {
        foreach (var name in new[] { SettingsNames.DeskFont, SettingsNames.MessageFont, SettingsNames.MessageColor, SettingsNames.DeskPlateSwitch, SettingsNames.DeskInkPicker })
            Assert.Equal(SettingsPage.DeskLook, SettingsNames.Table.Single(t => t.Name == name).Page);
        Assert.Empty(SettingsNames.PathProblems("Settings, Desk Look, Ink and color"));
        Assert.Empty(SettingsNames.PathProblems("Settings, Desk Look, Back plate"));
    }
```

In `windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs`, replace the bodies of `PathProblems_flags_a_longer_unknown_title` and `PathProblems_flags_a_longer_unknown_section_title` with:

```csharp
    [Fact]
    public void PathProblems_flags_a_longer_unknown_title()
    {
        // "Desk Look" is a page since slice 3: the longer title wins over "Desk", and "Font" is none of its sections.
        Assert.Contains("no section \"Font\" on Desk Look", SettingsNames.PathProblems("Settings, Desk Look, Font"));
        Assert.Empty(SettingsNames.PathProblems("Settings, Desk Look, Fonts"));
        // A capitalised word after a known title is still a longer, unknown title.
        Assert.Contains("no page \"Desk Looks\" after \"Settings, \"", SettingsNames.PathProblems("Settings, Desk Looks"));
        // Lowercase prose after a page and a comma is not a title.
        Assert.Empty(SettingsNames.PathProblems("Settings, About, or the tray menu"));
    }

    [Fact]
    public void PathProblems_flags_a_longer_unknown_section_title()
    {
        // The page rule, applied to sections (slice 1 review: section prefix-match): "Ink and color" is a section of Desk Look;
        // "Ink" alone, "Ink and colors" and "Back plate Extra" are not.
        Assert.Contains("no section \"Ink\" on Desk Look", SettingsNames.PathProblems("(Settings, Desk Look, Ink)"));
        Assert.Contains("no section \"Ink and colors\" on Desk Look", SettingsNames.PathProblems("Settings, Desk Look, Ink and colors"));
        Assert.Contains("no section \"Back plate Extra\" on Desk Look", SettingsNames.PathProblems("Settings, Desk Look, Back plate Extra"));
        Assert.Empty(SettingsNames.PathProblems("(Settings, Desk Look, Ink and color)"));
        Assert.Empty(SettingsNames.PathProblems("Settings, Claude Code, Folders to share"));
    }
```

In `windows-dotnet/tests/Sanduhr.Tests/SettingsSourceScanTests.cs`, in `The_trademark_footer_is_one_control_on_six_pages`, rename it `The_trademark_footer_is_one_control_on_seven_pages` and after `Assert.Equal(1, Count("WidgetsPage.xaml"));` add `Assert.Equal(1, Count("DeskLookPage.xaml"));`.

In `windows-dotnet/tests/Sanduhr.Tests/DeskPlateTests.cs`, in `TheSwitchIsFoundFromSettingsSearch`, replace `h.Entry.Page == SettingsPage.Desk` with `h.Entry.Page == SettingsPage.DeskLook`.

In `windows-dotnet/tests/Sanduhr.Tests/SettingsReachIndexTests.cs`, slice 2's "inert" pin stops being true the moment the first boost's page lands (slice 2 final review, T4: "each slice adds its boost's anchor assert"). Replace

```csharp
    [Fact]
    public void Boosts_change_no_search_today()
    {
        foreach (var boost in SettingsIndex.Boosts)
            foreach (var q in boost.Queries)
                Assert.DoesNotContain(SettingsIndex.Search(q), h => h.Rank < 0);
    }
```

with

```csharp
    [Fact]
    public void A_boost_ranks_first_only_once_its_section_is_live()
    {
        foreach (var boost in SettingsIndex.Boosts)
        {
            bool live = SettingsIndex.PageFromId(boost.PageId) is { } page && SettingsIndex.Find(page, boost.Anchor) is not null;
            foreach (var q in boost.Queries)
                Assert.True(live == SettingsIndex.Search(q).Any(h => h.Rank < 0), $"\"{q}\" (live: {live})");
        }
    }
```

Append to `windows-dotnet/tests/Sanduhr.App.Tests/DeskInkPackPresetsTests.cs`:

```csharp
    [Fact]
    public void The_presets_show_whenever_the_ink_is_not_Automatic_and_a_preset_sets_Custom()
    {
        var page = Page();
        Assert.False(page.ShowsInkColors);                    // Automatic
        page.InkChoice = DeskSettingsViewModel.InkChoices.First(c => c.Mode == DeskInkMode.Light);
        Assert.True(page.ShowsInkColors);
        page.InkPreset = "Sky";
        Assert.Equal(DeskInkMode.Custom, page.InkChoice.Mode);
        Assert.Equal(DeskInkMode.Custom, _store.LoadDeskInk().Mode);
        Assert.Equal("9ad7ff", _store.LoadDeskInk().Color);
    }
```

Append to `DeskSplitPagesTests`:

```csharp
    [Fact]
    public void Desk_Look_holds_both_fonts_both_sizes_and_both_colors() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.DeskLook, SettingsIndex.DeskLook);
            Settle(window, guard);
            string Names<T>(string anchor) where T : FrameworkElement => string.Join("|",
                Below<T>(Marked(window, SettingsPage.DeskLook, anchor)).Select(System.Windows.Automation.AutomationProperties.GetName));
            Assert.Equal($"{SettingsNames.DeskFont}|{SettingsNames.MessageFont}", Names<ComboBox>(SettingsIndex.DeskLook));
            Assert.Equal($"{SettingsNames.ClockSize}|{SettingsNames.MessageSize}", Names<Slider>(SettingsIndex.DeskSizes));
            Assert.Contains(SettingsNames.MessageColor, Names<ComboBox>(SettingsIndex.DeskInk));
            Assert.Contains(SettingsNames.DeskInkPicker, Names<ComboBox>(SettingsIndex.DeskInk));

            // Old links land here.
            window.SelectPage(SettingsPage.Desk, SettingsIndex.DeskInk);
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.DeskLook, SettingsIndex.DeskInk), window.Place);
            window.SelectPage(SettingsPage.DeskMessage, SettingsIndex.MessageLook);
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.DeskLook, SettingsIndex.DeskSizes), window.Place);
        }
        finally { window.Close(); }
    });
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsDeskSplitIndexTests|FullyQualifiedName~SettingsNamesTests"`
Expected: build FAILS: `SettingsIndex.DeskSizes` doesn't exist.

- [ ] **Step 3: Fill the page**

In `windows-dotnet/src/Sanduhr.App/Views/DeskLookPage.xaml`, add `xmlns:conv="clr-namespace:Sanduhr.App.Converters"` to the root, add `<conv:StringToVisibilityConverter x:Key="StrVis" />` to `UserControl.Resources`, and after the title `TextBlock` inside `PageBody` add:

```xml

                <!-- Fonts: the Desk's and the message's, the two "Font"s apart (WF12). Font packs join both lists. -->
                <StackPanel views:SettingsAnchor.Id="look">
                    <TextBlock Text="{x:Static core:SettingsNames.FontsSection}" FontSize="13" FontWeight="SemiBold"
                               AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                    <Grid DataContext="{Binding Desk}">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="110" />
                            <ColumnDefinition Width="230" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Text="{x:Static core:SettingsNames.DeskFont}" FontSize="12" VerticalAlignment="Center"
                                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                        <ComboBox Grid.Column="1" Style="{DynamicResource ThemedComboBox}"
                                  ItemsSource="{Binding FontChoices}" SelectedItem="{Binding Font, Mode=TwoWay}"
                                  AutomationProperties.Name="{x:Static core:SettingsNames.DeskFont}" />
                    </Grid>
                    <Grid Margin="0,10,0,0" DataContext="{Binding MessagePage}">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="110" />
                            <ColumnDefinition Width="230" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Text="{x:Static core:SettingsNames.MessageFont}" FontSize="12" VerticalAlignment="Center"
                                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                        <ComboBox Grid.Column="1" Style="{DynamicResource ThemedComboBox}"
                                  ItemsSource="{Binding FontOptions}" SelectedItem="{Binding Font, Mode=TwoWay}"
                                  AutomationProperties.Name="{x:Static core:SettingsNames.MessageFont}" />
                    </Grid>
                </StackPanel>

                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />

                <!-- Sizes: the clock's and the message's. -->
                <StackPanel views:SettingsAnchor.Id="sizes">
                    <TextBlock Text="{x:Static core:SettingsNames.SizesSection}" FontSize="13" FontWeight="SemiBold"
                               AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                    <Grid DataContext="{Binding Desk}">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="110" />
                            <ColumnDefinition Width="230" />
                            <ColumnDefinition Width="44" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Text="{x:Static core:SettingsNames.ClockSize}" FontSize="12" VerticalAlignment="Center"
                                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                        <Slider Grid.Column="1" x:Name="TimeSizeSlider" Style="{DynamicResource ThemedSlider}"
                                Minimum="48" Maximum="200" TickFrequency="4" IsSnapToTickEnabled="True"
                                SmallChange="4" LargeChange="16" Value="{Binding TimeSize, Mode=TwoWay}"
                                AutomationProperties.Name="{x:Static core:SettingsNames.ClockSize}" />
                        <TextBlock Grid.Column="2" Text="{Binding Value, ElementName=TimeSizeSlider, StringFormat='{}{0:0}'}"
                                   FontSize="11" VerticalAlignment="Center" HorizontalAlignment="Right"
                                   Foreground="{DynamicResource Sanduhr.Brush.TextDim}" FontFamily="Cascadia Code, Consolas" />
                    </Grid>
                    <Grid Margin="0,10,0,0" DataContext="{Binding MessagePage}">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="110" />
                            <ColumnDefinition Width="230" />
                            <ColumnDefinition Width="44" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Text="{x:Static core:SettingsNames.MessageSize}" FontSize="12" VerticalAlignment="Center"
                                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                        <Slider Grid.Column="1" x:Name="MessageSizeSlider" Style="{DynamicResource ThemedSlider}"
                                Minimum="24" Maximum="180" TickFrequency="1" IsSnapToTickEnabled="True"
                                SmallChange="1" LargeChange="10" Value="{Binding Size, Mode=TwoWay}"
                                AutomationProperties.Name="{x:Static core:SettingsNames.MessageSize}" />
                        <TextBlock Grid.Column="2" Text="{Binding Value, ElementName=MessageSizeSlider, StringFormat='{}{0:0}'}"
                                   FontSize="11" VerticalAlignment="Center" HorizontalAlignment="Right"
                                   Foreground="{DynamicResource Sanduhr.Brush.TextDim}" FontFamily="Cascadia Code, Consolas" />
                    </Grid>
                </StackPanel>

                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />

                <!-- Ink and color: the Desk's ink (its presets show whenever it is not Automatic, WF3) and the message's color. -->
                <StackPanel views:SettingsAnchor.Id="ink">
                    <TextBlock Text="{x:Static core:SettingsNames.InkAndColor}" FontSize="13" FontWeight="SemiBold"
                               AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                    <StackPanel DataContext="{Binding Desk}">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="110" />
                                <ColumnDefinition Width="230" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="{x:Static core:SettingsNames.DeskInkPicker}" FontSize="12" VerticalAlignment="Center" TextWrapping="Wrap"
                                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                            <ComboBox Grid.Column="1" Style="{DynamicResource ThemedComboBox}"
                                      ItemsSource="{x:Static vm:DeskSettingsViewModel.InkChoices}"
                                      SelectedItem="{Binding InkChoice, Mode=TwoWay}"
                                      AutomationProperties.Name="{x:Static core:SettingsNames.DeskInkPicker}" />
                        </Grid>
                        <Grid Margin="0,10,0,0" Visibility="{Binding ShowsInkColors, Converter={StaticResource BoolVis}}">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="110" />
                                <ColumnDefinition Width="150" />
                                <ColumnDefinition Width="8" />
                                <ColumnDefinition Width="200" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="Colors" FontSize="12" VerticalAlignment="Center"
                                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                            <ComboBox Grid.Column="1" Style="{DynamicResource ThemedComboBox}"
                                      ItemsSource="{Binding InkPresetNames}"
                                      SelectedItem="{Binding InkPreset, Mode=TwoWay}"
                                      AutomationProperties.Name="Colors, ink preset" />
                            <TextBox Grid.Column="3" Style="{DynamicResource Input}" FontFamily="Cascadia Code, Consolas"
                                     Text="{Binding InkColors, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                                     AutomationProperties.Name="Ink hex colors" />
                        </Grid>
                        <CheckBox Style="{DynamicResource ThemedCheckBox}" Margin="0,10,0,0"
                                  Content="{x:Static core:SettingsNames.DropShadow}" IsChecked="{Binding InkShadow, Mode=TwoWay}"
                                  AutomationProperties.Name="{x:Static core:SettingsNames.DropShadow}" />
                        <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,8,0,0"
                                   Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                   Text="Automatic reads how bright your wallpaper image is behind each piece and picks light or dark ink, so it stays readable. It reads the wallpaper file only, never your screen. Picking one of the Colors switches the ink to Custom, which takes one hex color, or two to four with commas for a gradient, such as 8f5bd6,3a63e0,33fdff. A meter near its limit stays red in every ink." />
                    </StackPanel>
                    <StackPanel Margin="0,14,0,0" DataContext="{Binding MessagePage}">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="110" />
                                <ColumnDefinition Width="230" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="{x:Static core:SettingsNames.MessageColor}" FontSize="12" VerticalAlignment="Center"
                                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                            <ComboBox Grid.Column="1" Style="{DynamicResource ThemedComboBox}"
                                      ItemsSource="{Binding ColorOptions}" SelectedItem="{Binding Color, Mode=TwoWay}"
                                      AutomationProperties.Name="{x:Static core:SettingsNames.MessageColor}" />
                            <TextBox Grid.Column="2" Style="{DynamicResource Input}" Width="170" Height="30" Margin="8,0,0,0"
                                     VerticalContentAlignment="Center" FontFamily="Cascadia Code, Consolas"
                                     Visibility="{Binding IsCustomColor, Converter={StaticResource BoolVis}}"
                                     Text="{Binding CustomColorText, UpdateSourceTrigger=PropertyChanged}"
                                     AutomationProperties.Name="Custom message color, hex"
                                     ToolTip="Hex colors like 9ad7ff. Two or more, separated by commas, make a gradient." />
                        </Grid>
                        <TextBlock FontSize="11" TextWrapping="Wrap" Margin="110,4,0,0"
                                   Foreground="{DynamicResource Sanduhr.Brush.PaceMarker}"
                                   Text="{Binding CustomColorProblem}"
                                   Visibility="{Binding CustomColorProblem, Converter={StaticResource StrVis}}" />
                        <CheckBox Style="{DynamicResource ThemedCheckBox}" Margin="0,10,0,0"
                                  Content="{x:Static core:SettingsNames.GlowAroundMessage}" IsChecked="{Binding Glow, Mode=TwoWay}"
                                  AutomationProperties.Name="{x:Static core:SettingsNames.GlowAroundMessage}" />
                    </StackPanel>
                </StackPanel>

                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />

                <!-- Back plate: a soft plate behind each piece. -->
                <StackPanel views:SettingsAnchor.Id="desk-plate" DataContext="{Binding Desk}">
                    <TextBlock Text="{x:Static core:SettingsNames.BackPlate}" FontSize="13" FontWeight="SemiBold"
                               AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                    <CheckBox Style="{DynamicResource ThemedCheckBox}"
                              Content="{x:Static core:SettingsNames.DeskPlateSwitch}"
                              IsChecked="{Binding Plate, Mode=TwoWay}"
                              AutomationProperties.Name="{x:Static core:SettingsNames.DeskPlateSwitch}"
                              AutomationProperties.HelpText="{x:Static core:DeskPlate.Caption}" />
                    <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,8,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                               Text="{x:Static core:DeskPlate.Caption}" />
                </StackPanel>

                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />

                <views:TrademarkFooter />
```

- [ ] **Step 4: Take Look off the Desk and Message pages**

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`, in the Desk TabItem, delete from `<StackPanel views:SettingsAnchor.Id="look">` through the `</StackPanel>` that closes `views:SettingsAnchor.Id="ink"`, together with the separator `<Border Height="1" … Margin="0,16,0,16" />` that follows the `ink` block (so the `map` block's separator is followed by the `margins` block). Three section blocks and three separators go.

In `windows-dotnet/src/Sanduhr.App/Views/DeskMessagePage.xaml`, delete from `<!-- Look -->` through the `</StackPanel>` that closes `views:SettingsAnchor.Id="message-look"`, and the separator `<Border Height="1" … />` right after it (the `special` block's separator is then followed by the trademark footer).

- [ ] **Step 5: The presets show without Custom**

In `windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs`, after `public bool IsCustomInk => InkChoice.Mode == DeskInkMode.Custom;` add

```csharp

    /// <summary>The Colors presets show whenever the ink is not Automatic (WF3: a palette pack is no longer hidden behind Custom).</summary>
    public bool ShowsInkColors => InkChoice.Mode != DeskInkMode.Auto;
```

replace

```csharp
    partial void OnInkChoiceChanged(DeskInkChoice value)
    {
        OnPropertyChanged(nameof(IsCustomInk));
        SaveInk(_ink with { Mode = value.Mode });
    }
```

with

```csharp
    partial void OnInkChoiceChanged(DeskInkChoice value)
    {
        OnPropertyChanged(nameof(IsCustomInk));
        OnPropertyChanged(nameof(ShowsInkColors));
        SaveInk(_ink with { Mode = value.Mode });
    }
```

and in `OnInkPresetChanged` replace

```csharp
        _loadingInk = true;
        InkColors = preset.Value;
        _loadingInk = false;
        SaveInk(_ink with { Color = preset.Value });
```

with

```csharp
        // Choosing a preset sets Custom (spec, Desk Look): the colors are the ink's own from now on.
        _loadingInk = true;
        InkColors = preset.Value;
        if (InkChoice.Mode != DeskInkMode.Custom) InkChoice = InkChoices.First(c => c.Mode == DeskInkMode.Custom);
        _loadingInk = false;
        OnPropertyChanged(nameof(IsCustomInk));
        OnPropertyChanged(nameof(ShowsInkColors));
        SaveInk(_ink with { Color = preset.Value, Mode = DeskInkMode.Custom });
```

- [ ] **Step 6: The index and the names**

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`:
- after `public const string DeskDesigns = "desk-designs";` add `    /// <summary>Desk Look, Sizes (slice 3).</summary>\n    public const string DeskSizes = "sizes";` (two lines);
- delete the three rows `E("Look", SettingsPage.Desk, DeskLook, …)`, `E(SettingsNames.DeskPlateSwitch, SettingsPage.Desk, DeskPlate, …)` and `E(SettingsNames.DeskInk, SettingsPage.Desk, DeskInk, …)`, and the row `E("Message look", SettingsPage.DeskMessage, MessageLook, …)`;
- after the last Desk row (`E("Layout map", …)`) add the Desk Look rows:

```csharp

        E(SettingsNames.FontsSection, SettingsPage.DeskLook, DeskLook,
            ["font", SettingsNames.DeskFont, SettingsNames.MessageFont, "EsteFont", "EsteFont Pro", "EsteFont 26", "typeface", "Segoe UI", "font pack"]),
        E(SettingsNames.SizesSection, SettingsPage.DeskLook, DeskSizes,
            [SettingsNames.ClockSize, SettingsNames.MessageSize, "time size", "size", "bigger", "smaller"]),
        E(SettingsNames.InkAndColor, SettingsPage.DeskLook, DeskInk,
            ["ink", SettingsNames.DeskInkPicker, "text color", "automatic", "light", "dark", "wallpaper", "readable", SettingsNames.DropShadow,
             "shadow", "gradient", "hex", "palette", "presets", SettingsNames.MessageColor, SettingsNames.GlowAroundMessage]),
        E(SettingsNames.BackPlate, SettingsPage.DeskLook, DeskPlate,
            [SettingsNames.DeskPlateSwitch, "plate", "backdrop", "background", "contrast", "readable", "desktop icons", "files", "dark plate", "behind the text"]),
```

- in `NameAnchors`: delete `[SettingsNames.DeskInk] = DeskInk,`; add `[SettingsNames.DeskInkPicker] = DeskInk,`; change `[SettingsNames.MessageFont] = MessageLook,` to `= DeskLook,` and `[SettingsNames.MessageColor] = MessageLook,` to `= DeskInk,`;
- in `PageSynonyms[SettingsPage.Desk]`, remove `"font"` and `"EsteFont"`.

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, in `Table`: replace `(DeskInk, SettingsPage.Desk),` with `(DeskInkPicker, SettingsPage.DeskLook),`; change the pages of `DeskPlateSwitch`, `DeskFont`, `MessageFont` and `MessageColor` to `SettingsPage.DeskLook`. Then `git grep -n "SettingsNames.DeskInk\b" windows-dotnet` must print nothing; if so, delete the `DeskInk` constant and its summary (the section is `InkAndColor` now). Update the summaries of `DeskInkPicker`, `DeskFont`, `MessageFont`, `MessageColor` and `DeskPlateSwitch` to start `Desk Look, …`.

- [ ] **Step 7: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsDeskSplitIndexTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsReachIndexTests|FullyQualifiedName~DeskPlateTests|FullyQualifiedName~WhatsNewTests"`
Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskSplitPagesTests|FullyQualifiedName~DeskInkPackPresetsTests|FullyQualifiedName~SettingsWindowNamesTests|FullyQualifiedName~MessageModeSegmentTests|FullyQualifiedName~DeskFontsPackTests"`
Expected: PASS (the "desk-plate" What's New card resolves to Desk Look).

- [ ] **Step 8: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Views/DeskLookPage.xaml windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/Views/DeskMessagePage.xaml windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsSourceScanTests.cs windows-dotnet/tests/Sanduhr.Tests/DeskPlateTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsReachIndexTests.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskInkPackPresetsTests.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs
git commit -m "feat(settings-v2): Desk Look holds both fonts, both sizes, ink and color and the plate; presets show without Custom"
```

---

### Task 12: Watchers: its own page; either switch shows the piece; the agents' text names it

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Views/WatchersPage.xaml` (the sections)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (Desk: `desk-watchers` and its separator go)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs` (`WatchersAgents`, `WatchersCiRunners` setters)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`, `SettingsNames.cs` (`Table`)
- Modify: `windows-dotnet/src/Sanduhr.Mcp/ToolLogic.Watchers.cs:114`, `ToolCatalog.cs:686-687`
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/CiRunnersSwitchTests.cs:63-68`, `DeskPiecesSettingsTests.cs`, `DeskSplitPagesTests.cs`; `windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs`

**Interfaces:**
- Produces: anchors on `Watchers`: `watchers` (constant `SettingsIndex.Watchers`), `ci-runners` (`SettingsIndex.CiRunners`), `watchers-place` (`SettingsIndex.WatchersPlace`). `desk#desk-watchers` resolves to `watchers#watchers`. Turning either switch on places a hidden Watchers piece at middle right.

- [ ] **Step 1: Write the failing tests**

Append to `DeskPiecesSettingsTests`:

```csharp
    [Fact]
    public void Either_watchers_switch_shows_a_hidden_Watchers_piece()
    {
        var desk = Desk();
        desk.WatchersAgents = true;
        Assert.Contains("watchers:mr", Layout);
        desk.Pieces.Single(p => p.Widget == "watchers").Shown = false;
        desk.WatchersAgents = false;
        Assert.DoesNotContain("watchers", Layout);       // turning a switch off never hides the piece, and never shows it
        desk.WatchersCiRunners = true;
        Assert.Contains("watchers:mr", Layout);           // back where it was
        Assert.Equal("On the Desk: middle right.", desk.WatchersPiece.Text);
    }
```

In `windows-dotnet/tests/Sanduhr.App.Tests/CiRunnersSwitchTests.cs`, replace the body of `TheSwitchIsSearchableAndSitsOnTheDeskPage` and rename it:

```csharp
    [Fact]
    public void TheSwitchIsSearchableAndSitsOnTheWatchersPage()
    {
        foreach (var query in new[] { "show my CI runners' jobs", "runner", "GitHub Actions", "Docker" })
            Assert.Contains(SettingsIndex.Search(query), h => h.Entry.Page == SettingsPage.Watchers && h.Entry.Anchor == SettingsIndex.CiRunners);
        Assert.Equal(SettingsPage.Watchers, SettingsNames.Table.Single(t => t.Name == SettingsNames.CiRunnersSwitch).Page);
    }
```

Append to `SettingsDeskSplitIndexTests`:

```csharp
    [Fact]
    public void Watchers_holds_its_switches_and_where_they_show()
    {
        Assert.Equal([SettingsIndex.Watchers, SettingsIndex.CiRunners, SettingsIndex.WatchersPlace],
            SettingsIndex.EntriesOn(SettingsPage.Watchers).Select(e => e.Anchor));
        Assert.Equal(new SettingsLink(SettingsPage.Watchers, SettingsIndex.Watchers),
            SettingsIndex.Resolve(SettingsPage.Desk, SettingsIndex.DeskWatchers));
        // Slice 2's boost, live now: "docker" and "runner" land on the CI runners first.
        foreach (var q in new[] { "docker", "runner", "runners" })
            Assert.Equal((SettingsPage.Watchers, SettingsIndex.CiRunners, -1),
                (SettingsIndex.Search(q)[0].Entry.Page, SettingsIndex.Search(q)[0].Entry.Anchor, SettingsIndex.Search(q)[0].Rank));
        Assert.Equal(SettingsPage.Watchers, SettingsNames.Table.Single(t => t.Name == SettingsNames.WatchersSwitch).Page);
    }
```

Append to `DeskSplitPagesTests`:

```csharp
    [Fact]
    public void The_Watchers_page_holds_both_switches_and_the_pieces_line() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk, SettingsIndex.DeskWatchers);   // an old link
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Watchers, SettingsIndex.Watchers), window.Place);
            var ci = Below<CheckBox>(Marked(window, SettingsPage.Watchers, SettingsIndex.CiRunners)).Single();
            Assert.Equal(SettingsNames.CiRunnersSwitch, System.Windows.Automation.AutomationProperties.GetName(ci));
            Assert.Single(Below<DeskPieceStatusPanel>(Marked(window, SettingsPage.Watchers, SettingsIndex.WatchersPlace)));
            Assert.Null(SettingsAnchor.Find((DependencyObject)TabOf(window, SettingsPage.Desk).Content, SettingsIndex.DeskWatchers));
        }
        finally { window.Close(); }
    });
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsDeskSplitIndexTests`
Expected: build FAILS: `SettingsIndex.CiRunners` doesn't exist.

- [ ] **Step 3: Fill the page**

In `windows-dotnet/src/Sanduhr.App/Views/WatchersPage.xaml`, after the `WatchersLine` TextBlock add:

```xml

                <!-- Claude's watchers (WF10's one word for agents: "Claude"). -->
                <StackPanel views:SettingsAnchor.Id="watchers">
                    <CheckBox Style="{DynamicResource ThemedCheckBox}"
                              Content="{x:Static core:SettingsNames.WatchersSwitch}"
                              IsChecked="{Binding WatchersAgents, Mode=TwoWay}"
                              AutomationProperties.Name="{x:Static core:SettingsNames.WatchersSwitch}" />
                    <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,8,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                               Text="Off by default. With the Sanduhr MCP server installed, Claude Code can put a live card on the Desk for work it is watching (a CI run, a deploy): a title, progress and a one-line note. Cards live in memory only, fade or stay as the work ends, and are gone when you quit. Turning this on shows the Watchers piece if it is hidden." />
                </StackPanel>

                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />

                <!-- The CI runners' jobs, a switch of its own (WF8). -->
                <StackPanel views:SettingsAnchor.Id="ci-runners">
                    <CheckBox Style="{DynamicResource ThemedCheckBox}"
                              Content="{x:Static core:SettingsNames.CiRunnersSwitch}"
                              IsChecked="{Binding WatchersCiRunners, Mode=TwoWay}"
                              AutomationProperties.Name="{x:Static core:SettingsNames.CiRunnersSwitch}" />
                    <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,8,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                               Text="{x:Static core:SettingsNames.CiRunnersCaption}" />
                    <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,4,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                               Text="Off by default. Shows a card for each job a GitHub Actions runner container on this PC runs, however it was started. Sanduhr reads only the runners' log output through Docker's local pipe: no GitHub account, no network, and never a container's settings. Cards live in memory only." />
                    <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,6,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}"
                               Text="{Binding CiRunnersStatus}"
                               AutomationProperties.LiveSetting="Polite" />
                </StackPanel>

                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />

                <!-- Where they show: the Watchers piece's line (Principle 1). -->
                <StackPanel views:SettingsAnchor.Id="watchers-place">
                    <TextBlock Text="{x:Static core:SettingsNames.WatchersPlace}" FontSize="13" FontWeight="SemiBold"
                               AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                    <views:DeskPieceStatusPanel DataContext="{Binding WatchersPiece}" />
                </StackPanel>
```

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`, in the Desk TabItem, delete the `<StackPanel views:SettingsAnchor.Id="desk-watchers">` block through its closing `</StackPanel>`, and the separator `<Border Height="1" … />` right after it.

- [ ] **Step 4: Either switch shows the piece**

In `windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs`, in the `WatchersAgents` setter replace

```csharp
            _store.SaveWatchersAgents(value);
            OnPropertyChanged();
            WatchersChanged?.Invoke();
```

with

```csharp
            _store.SaveWatchersAgents(value);
            OnPropertyChanged();
            WatchersChanged?.Invoke();
            if (value) PlaceIfHidden("watchers");   // Principle 4: turning watchers on never leaves them invisible
```

and in the `WatchersCiRunners` setter replace

```csharp
            _store.SaveWatchersCiRunners(value);
            OnPropertyChanged();
            CiRunnersChanged?.Invoke();
```

with

```csharp
            _store.SaveWatchersCiRunners(value);
            OnPropertyChanged();
            CiRunnersChanged?.Invoke();
            if (value) PlaceIfHidden("watchers");
```

Change the two setters' summaries from `"Let Claude show watchers" (spec 6.9)` / `"Show my CI runners' jobs"` to start `Settings, Watchers: …` and end `Turning it on shows a hidden Watchers piece.`

- [ ] **Step 5: The index, the names and the agents' text**

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`:
- after the `DeskSizes` constant add

```csharp
    // Watchers (slice 3)
    public const string Watchers = "watchers";
    public const string CiRunners = "ci-runners";
    public const string WatchersPlace = "watchers-place";
```

- delete the row `E(SettingsNames.WatchersSwitch, SettingsPage.Desk, DeskWatchers, …)` (all three of its lines);
- after the Weather rows (`E("Weather data by Open-Meteo.com", …)`) add

```csharp

        E(SettingsNames.WatchersSwitch, SettingsPage.Watchers, Watchers,
            ["watcher", "watchers", "agent", "agents", "MCP", "watch_start", "deploy", "progress", "card", "claude code", "off by default"]),
        E(SettingsNames.CiRunnersSwitch, SettingsPage.Watchers, CiRunners,
            ["CI", "runner", "runners", "GitHub Actions", "Docker", "jobs", "containers", "off by default"]),
        E(SettingsNames.WatchersPlace, SettingsPage.Watchers, WatchersPlace,
            ["place", "where", "corner", "on the Desk", "piece", "hidden"]),
```

- in `NameAnchors`, change `[SettingsNames.WatchersSwitch] = DeskWatchers,` to `= Watchers,` and `[SettingsNames.CiRunnersSwitch] = DeskWatchers,` to `= CiRunners,`.

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, in `Table`, change the pages of `WatchersSwitch` and `CiRunnersSwitch` to `SettingsPage.Watchers`, and their summaries to start `Watchers: …`.

In `windows-dotnet/src/Sanduhr.Mcp/ToolLogic.Watchers.cs`, replace `"The user has not turned on 'Let Claude show watchers' in Sanduhr (Settings, Desk). "` with `"The user has not turned on 'Let Claude show watchers' in Sanduhr (Settings, Watchers). "`.

In `windows-dotnet/src/Sanduhr.Mcp/ToolCatalog.cs`, replace `"(Settings, Desk); then do not retry. It shows where the user has placed the Watchers piece on the Desk.";` with `"(Settings, Watchers); then do not retry. It shows where the user has placed the Watchers piece on the Desk.";` (WF18).

- [ ] **Step 6: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsDeskSplitIndexTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsReachIndexTests|FullyQualifiedName~WhatsNewTests|FullyQualifiedName~WatcherTests"`
Run: `dotnet test tests/Sanduhr.Mcp.Tests`
Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskPiecesSettingsTests|FullyQualifiedName~CiRunnersSwitchTests|FullyQualifiedName~DeskSplitPagesTests|FullyQualifiedName~SettingsWindowNamesTests"`
Expected: PASS (`The_Core_and_Mcp_scan_sees_known_strings` still finds `WatchersSwitch` in `ToolLogic.Watchers.cs`; `PathProblems` reads "Settings, Watchers" as a real page). If a Mcp test pins the old sentence, change it to the new one.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Views/WatchersPage.xaml windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.Mcp/ToolLogic.Watchers.cs windows-dotnet/src/Sanduhr.Mcp/ToolCatalog.cs windows-dotnet/tests/Sanduhr.App.Tests/CiRunnersSwitchTests.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskPiecesSettingsTests.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs
git commit -m "feat(settings-v2): Watchers is its own page; either switch shows the piece; agents read Settings, Watchers"
```

---

### Task 13: Launcher: its own page; the Desk menu's "Launcher settings…" and the missing-app click land on its buttons

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Views/LauncherPage.xaml` (the sections)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (Desk: `desk-launcher` and its separator go)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/LauncherSettingsViewModel.cs` (`PlacementNote`)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskClicks.cs:53`, `windows-dotnet/src/Sanduhr.App/App.xaml.cs:492, 510-513`
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (rows, `NameAnchors`, `MenuAnchor`), `SettingsNames.cs` (`Table`, `Retired`)
- Test: `windows-dotnet/tests/Sanduhr.Tests/LauncherSettingsTests.cs`, `SettingsLinkTests.cs:106-119`, `SettingsEntryPointScanTests.cs:25-33`, `SettingsDeskSplitIndexTests.cs`; `windows-dotnet/tests/Sanduhr.App.Tests/LauncherSettingsTests.cs` (`ThePlacementNoteShowsOnlyWhileTheButtonsHaveNoPlace`), `DeskSplitPagesTests.cs`

**Interfaces:**
- Produces: anchors on `Launcher`: `launcher` (`SettingsIndex.LauncherPlace`, "Where it shows") and `launcher-buttons` (`SettingsIndex.LauncherButtons`, "Buttons"); `SettingsIndex.MenuAnchor(SettingsPage.Launcher)` = `launcher-buttons`; `DeskClicks.LauncherSettings` = "Launcher settings…" (a `static readonly` now). `desk#desk-launcher` resolves to `launcher#launcher`.

- [ ] **Step 1: Write the failing tests**

In `windows-dotnet/tests/Sanduhr.Tests/LauncherSettingsTests.cs`, replace the three tests at the top of the class with:

```csharp
    [Theory]
    [InlineData("launcher")]
    [InlineData("button")]
    [InlineData("shortcut")]
    [InlineData("app")]
    [InlineData("Add app")]
    [InlineData("Add link")]
    public void SearchFindsTheLauncherButtons(string query) =>
        Assert.Contains(SettingsIndex.Search(query),
            h => h.Entry.Page == SettingsPage.Launcher && h.Entry.Anchor == SettingsIndex.LauncherButtons);

    [Fact]
    public void LauncherIsTheTopHitForItsOwnName()
    {
        var top = SettingsIndex.Search("launcher")[0];
        Assert.Equal((SettingsPage.Launcher, (string?)null), (top.Entry.Page, top.Entry.Anchor));   // the page itself
    }

    [Fact]
    public void TheAddButtonsAreInTheNamesTableOnTheLauncherPage()
    {
        Assert.Contains((SettingsNames.LauncherAddApp, SettingsPage.Launcher), SettingsNames.Table);
        Assert.Contains((SettingsNames.LauncherAddLink, SettingsPage.Launcher), SettingsNames.Table);
        Assert.Equal("Add app…", SettingsNames.LauncherAddApp);
        Assert.Equal("Add link…", SettingsNames.LauncherAddLink);
    }
```

In `windows-dotnet/tests/Sanduhr.Tests/SettingsLinkTests.cs`, add to `Menu_links_land_on_their_section`:

```csharp
    [InlineData(SettingsPage.Launcher, SettingsIndex.LauncherButtons)]
```

and in `Check_sends_an_old_place_to_where_it_lives_now` replace

```csharp
        // An alias whose target page isn't there yet does nothing: the place is kept (Desk's Launcher until slice 3).
        Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskLauncher),
            SettingsIndex.Check(SettingsPage.Desk, SettingsIndex.DeskLauncher));
```

with

```csharp
        // Desk's Launcher became a page in slice 3: the old section lands at the page's place line.
        Assert.Equal(new SettingsLink(SettingsPage.Launcher, SettingsIndex.LauncherPlace),
            SettingsIndex.Check(SettingsPage.Desk, SettingsIndex.DeskLauncher));
        // An alias whose target isn't there yet does nothing: Claude Code's meters wait for Mods & Widgets (slice 4), so a build
        // without the old section sends the link to the page's top.
        var withoutMeters = SettingsIndex.Anchors.Where(e => !(e.Page == SettingsPage.ClaudeCode && e.Anchor == SettingsIndex.Meters)).ToList();
        Assert.Equal(new SettingsLink(SettingsPage.ClaudeCode, null),
            SettingsIndex.Check(SettingsPage.ClaudeCode, SettingsIndex.Meters, withoutMeters, SettingsNames.Pages));
```

In `windows-dotnet/tests/Sanduhr.Tests/SettingsEntryPointScanTests.cs`, in `Every_App_call_that_names_a_page_also_names_an_anchor`, add

```csharp
        Assert.Contains("SettingsIndex.MenuAnchor(SettingsPage.Launcher)", app);
        Assert.DoesNotContain("SettingsIndex.DeskLauncher", app);
```

Append to `SettingsDeskSplitIndexTests`:

```csharp
    [Fact]
    public void Launcher_holds_where_it_shows_and_its_buttons()
    {
        Assert.Equal([SettingsIndex.LauncherPlace, SettingsIndex.LauncherButtons],
            SettingsIndex.EntriesOn(SettingsPage.Launcher).Select(e => e.Anchor));
        Assert.Equal(new SettingsLink(SettingsPage.Launcher, SettingsIndex.LauncherPlace),
            SettingsIndex.Resolve(SettingsPage.Desk, SettingsIndex.DeskLauncher));
        Assert.Equal(["Edit buttons…"], SettingsNames.RetiredNames("Edit buttons…"));
        Assert.Equal("Launcher settings…", SettingsNames.LinkTitle(SettingsPage.Launcher));
    }
```

In `windows-dotnet/tests/Sanduhr.App.Tests/LauncherSettingsTests.cs`, replace `ThePlacementNoteShowsOnlyWhileTheButtonsHaveNoPlace` with

```csharp
    [Fact]
    public void AHiddenLauncherIsSaidByThePagesStatusLineNotANote()
    {
        Assert.False(Open(placed: false).HasPlacementNote);
        Assert.False(Open(new LauncherModel(LauncherOrientation.Row, [App("A")]), placed: false).HasPlacementNote);
        Assert.False(Open(new LauncherModel(LauncherOrientation.Row, [App("A")]), placed: true).HasPlacementNote);
    }
```

Append to `DeskSplitPagesTests`:

```csharp
    [Fact]
    public void The_Launcher_page_holds_its_buttons_and_old_links_land_there() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Launcher, SettingsIndex.MenuAnchor(SettingsPage.Launcher));
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Launcher, SettingsIndex.LauncherButtons), window.Place);
            Assert.Contains(Below<Button>(Marked(window, SettingsPage.Launcher, SettingsIndex.LauncherButtons)),
                b => (b.Content as string) == SettingsNames.LauncherAddApp);
            Assert.Single(Below<DeskPieceStatusPanel>(Marked(window, SettingsPage.Launcher, SettingsIndex.LauncherPlace)));

            window.SelectPage(SettingsPage.Desk, SettingsIndex.DeskLauncher);
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Launcher, SettingsIndex.LauncherPlace), window.Place);
        }
        finally { window.Close(); }
    });
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~LauncherSettingsTests|FullyQualifiedName~SettingsDeskSplitIndexTests"`
Expected: build FAILS: `SettingsIndex.LauncherButtons` doesn't exist.

- [ ] **Step 3: Move the Launcher's block to its page**

In `windows-dotnet/src/Sanduhr.App/Views/LauncherPage.xaml`, after the title TextBlock add:

```xml

                <!-- Where it shows: the Launcher piece's line (Principle 1). -->
                <StackPanel views:SettingsAnchor.Id="launcher">
                    <TextBlock Text="{x:Static core:SettingsNames.LauncherPlace}" FontSize="13" FontWeight="SemiBold"
                               AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                    <views:DeskPieceStatusPanel DataContext="{Binding LauncherPiece}" />
                </StackPanel>

                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />
```

Then cut, from `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`'s Desk TabItem, the whole block from `<StackPanel views:SettingsAnchor.Id="desk-launcher" DataContext="{Binding Launcher}">` through its closing `</StackPanel>` (it ends after the "Images are PNG files up to 1 MB…" caption), and delete the separator `<Border Height="1" … />` that followed it. Paste the block into `LauncherPage.xaml` right after the separator just added, with exactly these changes and nothing else:
- the opening tag becomes `<StackPanel views:SettingsAnchor.Id="launcher-buttons" DataContext="{Binding Launcher}">`;
- its first TextBlock (`Text="Launcher" FontSize="13" …`) becomes `<TextBlock Text="{x:Static core:SettingsNames.LauncherButtons}" FontSize="13" FontWeight="SemiBold" AutomationProperties.HeadingLevel="Level2" Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />`;
- the caption under it drops the sentence ` Place the Launcher under Where each piece sits.` (it reads "Up to eight buttons on the Desk, each opening an app from your Start menu or an https link. The Desk sits below your windows, so a button works where the Desk shows, or after Show desktop.");
- every `Style="{StaticResource FlatButton}"`, `Style="{StaticResource ThemedComboBox}"` and `Style="{StaticResource Input}"` becomes `Style="{DynamicResource …}"` with the same key (a page in its own file finds the window's styles at run time); `Converter={StaticResource BoolVis}` stays (the page declares `BoolVis`).

- [ ] **Step 4: The note, the menu and the missing-app click**

In `windows-dotnet/src/Sanduhr.App/ViewModels/LauncherSettingsViewModel.cs`, replace

```csharp
    /// <summary>Shown when there are buttons but the piece has no place on the Desk.</summary>
    public string PlacementNote => !HasButtons ? ""
        : !_isPlaced() ? "The Launcher is hidden. Show it in Settings, Desk, Pieces."
        : _autoNote;
```

with

```csharp
    /// <summary>Where the first button put the piece ("Placed at top left. …"). A hidden Launcher is said by the page's status line
    /// at its top (Principle 1), not here.</summary>
    public string PlacementNote => HasButtons && _isPlaced() ? _autoNote : "";
```

In `windows-dotnet/src/Sanduhr.App/Desk/DeskClicks.cs`, replace

```csharp
    public const string LauncherSettings = "Edit buttons…";
```

with

```csharp
    /// <summary>A Launcher button's menu item: "Launcher settings…" (WF17, WF19), the page's own name like every entry point.</summary>
    public static readonly string LauncherSettings = SettingsNames.LinkTitle(SettingsPage.Launcher);
```

In `windows-dotnet/src/Sanduhr.App/App.xaml.cs`, replace

```csharp
            case Desk.DeskMenuAction.LauncherSettings: _ = ShowSettingsAsync(SettingsPage.Desk, SettingsIndex.DeskLauncher); break;
```

with

```csharp
            case Desk.DeskMenuAction.LauncherSettings: _ = ShowSettingsAsync(SettingsPage.Launcher, SettingsIndex.MenuAnchor(SettingsPage.Launcher)); break;
```

and in `OnLauncherMissing` replace its summary's `Settings, Desk, Launcher opens at that button.` with `Settings, Launcher opens at its buttons, that one selected.` and

```csharp
        _ = ShowSettingsAsync(SettingsPage.Desk, SettingsIndex.DeskLauncher);
```

with

```csharp
        _ = ShowSettingsAsync(SettingsPage.Launcher, SettingsIndex.MenuAnchor(SettingsPage.Launcher));
```

- [ ] **Step 5: The index and the names**

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`:
- after the Watchers constants add

```csharp
    // Launcher (slice 3)
    public const string LauncherPlace = "launcher";
    public const string LauncherButtons = "launcher-buttons";
```

- delete the row `E(SettingsNames.DeskLauncher, SettingsPage.Desk, DeskLauncher, …)` (all its lines);
- after the Watchers rows add

```csharp

        E(SettingsNames.LauncherPlace, SettingsPage.Launcher, LauncherPlace,
            ["place", "where", "corner", "on the Desk", "piece", "hidden"]),
        E(SettingsNames.LauncherButtons, SettingsPage.Launcher, LauncherButtons,
            ["launcher", "button", "buttons", "shortcut", "shortcuts", "app", "apps", "link", "TouchButton", "Rainmeter",
             SettingsNames.LauncherAddApp, SettingsNames.LauncherAddLink, "image", "label", "PNG", "Chrome app", "PWA", "row", "column"]),
```

- in `NameAnchors`, change `[SettingsNames.LauncherAddApp] = DeskLauncher,` and `[SettingsNames.LauncherAddLink] = DeskLauncher,` to `= LauncherButtons,`;
- in `MenuAnchor`, before `_ => null,` add `        SettingsPage.Launcher => LauncherButtons,` and change its summary's `Slices 3 and 5 move Alerts to <c>each-limit</c> and add the Launcher here` to `Slice 5 moves Alerts to <c>each-limit</c>`.

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, in `Table`, change the pages of `LauncherAddApp` and `LauncherAddLink` to `SettingsPage.Launcher` (summaries `Launcher, Buttons: …`), and add to `Retired`: `("Edit buttons…", LinkTitle(SettingsPage.Launcher), false),`.

- [ ] **Step 6: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~LauncherSettingsTests|FullyQualifiedName~SettingsLinkTests|FullyQualifiedName~SettingsEntryPointScanTests|FullyQualifiedName~SettingsDeskSplitIndexTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~WhatsNewTests"`
Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~LauncherSettingsTests|FullyQualifiedName~LauncherPieceTests|FullyQualifiedName~LauncherLabelTests|FullyQualifiedName~DeskSplitPagesTests|FullyQualifiedName~SettingsWindowNamesTests|FullyQualifiedName~RowControlNamesTests"`
Expected: PASS. (The "launcher" What's New card resolves to `launcher#launcher`.)

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Views/LauncherPage.xaml windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/ViewModels/LauncherSettingsViewModel.cs windows-dotnet/src/Sanduhr.App/Desk/DeskClicks.cs windows-dotnet/src/Sanduhr.App/App.xaml.cs windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.Tests/LauncherSettingsTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsLinkTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsEntryPointScanTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs windows-dotnet/tests/Sanduhr.App.Tests/LauncherSettingsTests.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs
git commit -m "feat(settings-v2): Launcher is its own page; Launcher settings… and the missing-app click land on its buttons"
```

---

### Task 14: The Desk page in the spec's order: map on top, Pieces, Behavior, collapsed Designs and Advanced

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (resources: `SectionExpander`; the Desk TabItem, replaced whole; the Alerts sign-in block goes)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/DesignsSettingsPanel.xaml` (its own heading goes; the Expander carries it)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/DesignsSettingsViewModel.cs` (`Summary`)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (the Desk rows, `E(…, collapsedIn)`, `CollapsedSections`, `Build`, `DeskAdvanced`), `SettingsNames.cs` (`Table`)
- Test: `windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs`, `SettingsReachIndexTests.cs:153-159`; `windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs`, `SettingsReachTests.cs:281`, `DeskDirectSwitchTests.cs:44-51`

**Interfaces:**
- Consumes: everything that left the Desk page (Tasks 9, 11, 12, 13); `SettingsWindow.RevealInside` (slice 2) opens an Expander that is, or holds, a landing.
- Produces: the `SectionExpander` style (`Header` drawn in a 44 DIP toggle with a disclosure arrow, content below); `x:Name`s `DeskDesignsSection`, `DeskAdvancedSection`; anchors in order `desk`, `arrange`, `map`, `pieces`, `desk-clicks` (Behavior, holding `signin-reminders`), `desk-designs`, `desk-advanced` (`SettingsIndex.DeskAdvanced`), `margins`, `desk-oled`; `SettingsIndex.CollapsedSections`; `DesignsSettingsViewModel.Summary`. `desk#desk-direct` and `alerts#signin-reminders` go live as aliases.

- [ ] **Step 1: Write the failing tests**

Add to the top of `SettingsDeskSplitIndexTests` (after the class's opening brace) the helpers, and `using System.Text.RegularExpressions;` to the file:

```csharp
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Sanduhr.App");
    }

    private static string TabRange(string tag)
    {
        var xaml = File.ReadAllText(Path.Combine(AppDir(), "Views", "SettingsWindow.xaml"));
        var m = Regex.Match(xaml, "<TabItem\\b[^>]*?\\bTag=\"" + tag + "\"[^>]*>");
        Assert.True(m.Success, "no TabItem with Tag=" + tag);
        return xaml[m.Index..xaml.IndexOf("</TabItem>", m.Index, StringComparison.Ordinal)];
    }
```

and append:

```csharp
    [Fact]
    public void The_Desk_page_runs_in_the_specs_order()
    {
        var marks = Regex.Matches(TabRange("Desk"), "views:SettingsAnchor\\.Id=\"([\\w-]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(["desk", "arrange", "map", "pieces", "desk-clicks", "signin-reminders", "desk-designs", "desk-advanced", "margins", "desk-oled"], marks);
        Assert.DoesNotContain("signin-reminders", TabRange("Alerts"));
    }

    [Fact]
    public void What_sits_in_Designs_and_Advanced_says_so()
    {
        Assert.Equal(SettingsNames.DeskAdvanced, SettingsIndex.Search("margins")[0].Entry.CollapsedIn);
        Assert.Contains(SettingsIndex.Search("OLED care"),
            h => h.Entry.Anchor == SettingsIndex.DeskOled && h.Entry.CollapsedIn == SettingsNames.DeskAdvanced);
        Assert.Contains(SettingsIndex.Search("let Claude change the Desk directly"),
            h => h.Entry.Anchor == SettingsIndex.DeskDesigns && h.Entry.CollapsedIn == SettingsNames.DeskDesigns);
        Assert.Contains(SettingsIndex.Search("put my desk back"),
            h => h.Entry.Anchor == SettingsIndex.DeskDesigns && h.Entry.CollapsedIn == SettingsNames.DeskDesigns);
        Assert.Null(SettingsIndex.Find(SettingsPage.Desk, SettingsIndex.DeskDesigns)!.CollapsedIn);    // the section's own row
        Assert.Null(SettingsIndex.Find(SettingsPage.Desk, SettingsIndex.DeskAdvanced)!.CollapsedIn);
    }

    [Fact]
    public void The_moved_Desk_links_land_in_their_new_sections()
    {
        Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskDesigns), SettingsIndex.Resolve(SettingsPage.Desk, SettingsIndex.DeskDirect));
        Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.SignInReminders), SettingsIndex.Resolve(SettingsPage.Alerts, SettingsIndex.SignInReminders));
        Assert.Equal(SettingsPage.Desk, SettingsNames.Table.Single(t => t.Name == SettingsNames.SignInReminders).Page);
        Assert.Empty(SettingsNames.PathProblems("Settings, Desk, Advanced"));
        Assert.Empty(SettingsNames.PathProblems("Settings, Desk, Designs"));
    }
```

In `windows-dotnet/tests/Sanduhr.Tests/SettingsReachIndexTests.cs`, in `A_hit_in_a_collapsed_section_carries_the_section_name`, replace

```csharp
        // Nothing is collapsed until slice 3 folds Designs and Advanced.
        Assert.All(SettingsIndex.All, e => Assert.Null(e.CollapsedIn));
```

with

```csharp
        // Slice 3 folds Designs and Advanced on the Desk page; nothing else is collapsed.
        Assert.All(SettingsIndex.All.Where(e => e.CollapsedIn is not null), e => Assert.Equal(SettingsPage.Desk, e.Page));
        Assert.Contains(SettingsIndex.All, e => e.CollapsedIn == SettingsNames.DeskAdvanced);
```

In `windows-dotnet/tests/Sanduhr.App.Tests/DeskDirectSwitchTests.cs`, replace the body of `TheSwitchIsSearchableByWhatAPersonWouldTypeAndSitsOnTheDeskPage`:

```csharp
        foreach (var query in new[] { "let Claude change the Desk directly", "propose_desk", "agents" })
            Assert.Contains(SettingsIndex.Search(query), h => h.Entry.Page == SettingsPage.Desk && h.Entry.Anchor == SettingsIndex.DeskDesigns);
        Assert.Equal(SettingsPage.Desk, SettingsNames.Table.Single(t => t.Name == SettingsNames.DeskDirectSwitch).Page);
```

In `windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs`, in `Alerts_off_dims_everything_under_the_switch_and_says_why`, delete the line

```csharp
            Assert.True(SettingsAnchor.Find(content, SettingsIndex.SignInReminders)!.IsEnabled);
```

(the reminder now lives on Desk, Behavior).

Append to `DeskSplitPagesTests`:

```csharp
    [Fact]
    public void Designs_and_Advanced_start_closed_and_a_hit_inside_opens_them() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk);
            Settle(window, guard);
            var designs = (Expander)window.FindName("DeskDesignsSection");
            var advanced = (Expander)window.FindName("DeskAdvancedSection");
            Assert.False(designs.IsExpanded);
            Assert.False(advanced.IsExpanded);
            Assert.Contains(Below<TextBlock>(designs), t => t.Text == svm.Desk.Designs.Summary);

            window.SelectPage(SettingsPage.Desk, SettingsIndex.DeskMargins);
            Settle(window, guard);
            Assert.True(advanced.IsExpanded);
            Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskMargins), window.Place);

            window.SelectPage(SettingsPage.Desk, SettingsIndex.DeskDirect);   // an old link
            Settle(window, guard);
            Assert.True(designs.IsExpanded);
            Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskDesigns), window.Place);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_hit_inside_Advanced_opens_it() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk, SettingsIndex.DeskOled);
            Settle(window, guard);
            Assert.True(((Expander)window.FindName("DeskAdvancedSection")).IsExpanded);
            Assert.True(Marked(window, SettingsPage.Desk, SettingsIndex.DeskOled).IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Sign_in_reminders_live_on_Desk_Behavior() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Alerts, SettingsIndex.SignInReminders);   // an old link
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.SignInReminders), window.Place);
            var box = Below<CheckBox>(Marked(window, SettingsPage.Desk, SettingsIndex.SignInReminders)).Single();
            Assert.Equal(SettingsNames.SignInReminders, box.Content);
            Assert.Equal(SettingsNames.SignInReminders, System.Windows.Automation.AutomationProperties.GetName(box));
            Assert.True(box.IsChecked);
            box.IsChecked = false;
            Assert.False(svm.Alerts.DeskSignInReminder);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_Designs_summary_reads_the_kept_designs()
    {
        var designs = new DesignsSettingsViewModel(() => null);
        Assert.Equal(SettingsNames.DesignsSummary(0, canPutBack: false), designs.Summary);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsDeskSplitIndexTests`
Expected: build FAILS: `SettingsIndex.DeskAdvanced` doesn't exist.

- [ ] **Step 3: The index: Desk's rows in page order, the collapsed sections**

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`:

After `public const string DeskDesigns = "desk-designs";` add

```csharp
    /// <summary>Desk, Advanced (slice 3): collapsed; holds Margins and OLED care.</summary>
    public const string DeskAdvanced = "desk-advanced";
```

Replace the helper

```csharp
    private static SettingsEntry E(string title, SettingsPage page, string anchor, string[]? synonyms = null,
        string? subsection = null) => new(title, page, anchor, synonyms ?? [], subsection);
```

with

```csharp
    private static SettingsEntry E(string title, SettingsPage page, string anchor, string[]? synonyms = null,
        string? subsection = null, string? collapsedIn = null) => new(title, page, anchor, synonyms ?? [], subsection, collapsedIn);
```

Delete `E(SettingsNames.SignInReminders, SettingsPage.Alerts, SignInReminders, …)` from the Alerts rows (both lines). Replace every remaining Desk row (`DeskOn`, `DeskArrange`, `DeskClicks`, `DeskDirect`, `DeskOled`, `DeskDesigns`, `DeskPieces`, `DeskMargins`, `DeskMap`: the rows from `E(SettingsNames.DeskSwitch, SettingsPage.Desk, DeskOn,` through the end of `E("Layout map", SettingsPage.Desk, DeskMap, …),`) with this block, in the page's order:

```csharp
        E(SettingsNames.DeskSwitch, SettingsPage.Desk, DeskOn,
            ["desktop", "wallpaper", "clock", "meters", "Rainmeter", "show the Desk", "on the desktop", "off by default", "monitors"]),
        E(SettingsNames.ArrangeDesk, SettingsPage.Desk, DeskArrange,
            ["drag", "move", "rearrange", "arrange", "reorder", "on the desktop", "layout", "arrow keys", "keyboard"]),
        E(SettingsNames.DeskLayoutMap, SettingsPage.Desk, DeskMap,
            ["map", "preview", "layout", "taskbar", "screen", "monitor", "where"]),
        E(SettingsNames.PiecesSection, SettingsPage.Desk, DeskPieces,
            ["where each piece sits", "show", "hide", "order", "reorder", "move up", "move down", "stack",
             "layout", "place", "corner", "position", "size", "hidden", "clock", "meters", "Claude line", "taskbar", "anchor", "monitor", "monitors",
             "screen", "display", "main monitor", "all monitors", "second monitor", "per monitor",
             "style", "meter style", "slim", "ring", "rings", "gauge", "ticks", "segments", "Roundline", "compact",
             "companion", "pet", "sprite", "hourglass", "roam", "walk", "grow", "growth", "stage", "mood",
             SettingsNames.WidgetCompanionPick, SettingsNames.WidgetCompanionRoam]),
        E(SettingsNames.DeskPieceClicksSwitch, SettingsPage.Desk, DeskClicks,
            [SettingsNames.DeskBehavior, "click", "click-through", "right-click", "menu", "desktop icons", "clock", "message"]),
        E(SettingsNames.SignInReminders, SettingsPage.Desk, SignInReminders,
            ["sign in", "signed out", "without Claude", "reminder", "Just the Desk", "session expired"]),
        E(SettingsNames.DeskDesigns, SettingsPage.Desk, DeskDesigns,
            ["design", "designs", "undo", "put back", SettingsNames.PutMyDeskBack, "restore", "switch", "switch to", "rename", "pin", "delete",
             "saved looks", "Before", "history", "Claude", SettingsNames.DeskDirectSwitch, "agent", "agents", "MCP", "propose_desk", "review",
             "apply", "without asking", "wallpaper", "off by default"]),
        E(SettingsNames.DeskAdvanced, SettingsPage.Desk, DeskAdvanced, ["advanced", "more"]),
        E("Margins", SettingsPage.Desk, DeskMargins,
            ["margin", "edge", "padding", "taskbar", "inset", "spacing"], collapsedIn: SettingsNames.DeskAdvanced),
        E(SettingsNames.OledCareSwitch, SettingsPage.Desk, DeskOled,
            ["oled", "burn-in", "burn in", "burnin", "drift", "dim", "idle", "away", "screen", "pixels", "white", "image retention", "off by default"],
            collapsedIn: SettingsNames.DeskAdvanced),
```

(If Tasks 8 and 9 left the `DeskPieces` synonyms in a different order, keep the union shown here.)

In `NameAnchors`, change `[SettingsNames.DeskDirectSwitch] = DeskDirect,` to `= DeskDesigns,`.

Right after the `NameAnchors` dictionary (before `PageSynonyms`; it must be initialized before `All`, which `Build()` fills) add:

```csharp

    /// <summary>
    /// The sections that start collapsed (spec, Desk: Designs and Advanced), by page and anchor, with the name a search row gives
    /// what sits in them: "Let Claude change the Desk directly (in Designs)". A name in <see cref="SettingsNames.Table"/> whose
    /// anchor is one of these is inside it; the section's own row is not. Declared before <see cref="All"/>, which reads it.
    /// </summary>
    public static IReadOnlyDictionary<(SettingsPage Page, string Anchor), string> CollapsedSections { get; } =
        new Dictionary<(SettingsPage Page, string Anchor), string>
        {
            [(SettingsPage.Desk, DeskDesigns)] = SettingsNames.DeskDesigns,
            [(SettingsPage.Desk, DeskAdvanced)] = SettingsNames.DeskAdvanced,
        };
```

In `Build()`, replace

```csharp
                var at = Anchors.FirstOrDefault(e => e.Page == page && e.Anchor == anchor);
                all.Add(new SettingsEntry(name, page, anchor, [], at?.Subsection, at?.CollapsedIn));
```

with

```csharp
                var at = Anchors.FirstOrDefault(e => e.Page == page && e.Anchor == anchor);
                var inside = at?.CollapsedIn
                    ?? (anchor is not null && CollapsedSections.TryGetValue((page, anchor), out var section) ? section : null);
                all.Add(new SettingsEntry(name, page, anchor, [], at?.Subsection, inside));
```

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, in `Table`, change `(SignInReminders, SettingsPage.Alerts),` to `(SignInReminders, SettingsPage.Desk),` and its summary to `/// <summary>Desk, Behavior: the Desk's sign-in reminder switch (settings.json "desk_sign_in_reminder", on by default; WF32).</summary>`.

- [ ] **Step 4: The section style and the Designs summary**

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`, in `Window.Resources` after the `SettingsLinkButton` style, add:

```xml

        <!-- A collapsible section (Settings v2, slice 3: Desk's Designs and Advanced, the Layout map's fold): a 44 DIP header with a
             disclosure arrow that takes the keyboard and rings on focus (WF38), the content below. Search hits and links inside open
             it (SettingsWindow.RevealInside). -->
        <Style x:Key="SectionExpander" TargetType="Expander">
            <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.Text}" />
            <Setter Property="Focusable" Value="False" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Expander">
                        <StackPanel>
                            <ToggleButton x:Name="Toggle" MinHeight="44" Cursor="Hand" FocusVisualStyle="{x:Null}"
                                          HorizontalContentAlignment="Stretch"
                                          IsChecked="{Binding IsExpanded, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"
                                          Content="{TemplateBinding Header}"
                                          AutomationProperties.Name="{TemplateBinding AutomationProperties.Name}">
                                <ToggleButton.Template>
                                    <ControlTemplate TargetType="ToggleButton">
                                        <Border x:Name="ring" Background="Transparent" BorderThickness="1" BorderBrush="Transparent"
                                                CornerRadius="4" Padding="2,4">
                                            <DockPanel>
                                                <TextBlock x:Name="arrow" DockPanel.Dock="Left" Text="&#xE76C;" FontSize="10" Margin="0,0,8,0"
                                                           FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets" VerticalAlignment="Center"
                                                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                                                           AutomationProperties.AccessibilityView="Raw" />
                                                <ContentPresenter VerticalAlignment="Center" />
                                            </DockPanel>
                                        </Border>
                                        <ControlTemplate.Triggers>
                                            <Trigger Property="IsChecked" Value="True">
                                                <Setter TargetName="arrow" Property="Text" Value="&#xE70D;" />
                                            </Trigger>
                                            <Trigger Property="IsKeyboardFocused" Value="True">
                                                <Setter TargetName="ring" Property="BorderBrush" Value="{DynamicResource Sanduhr.Brush.Text}" />
                                            </Trigger>
                                        </ControlTemplate.Triggers>
                                    </ControlTemplate>
                                </ToggleButton.Template>
                            </ToggleButton>
                            <ContentPresenter x:Name="Body" Margin="18,8,0,0" Visibility="Collapsed" />
                        </StackPanel>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsExpanded" Value="True">
                                <Setter TargetName="Body" Property="Visibility" Value="Visible" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/DesignsSettingsViewModel.cs`, after `CountText` add

```csharp

    /// <summary>Desk, Designs' one line while the section is collapsed: "3 saved. Put my Desk back is ready."</summary>
    public string Summary => SettingsNames.DesignsSummary(Rows.Count, CanPutBack);
```

and in `Reload()`, after `OnPropertyChanged(nameof(CanPutBack));` add `        OnPropertyChanged(nameof(Summary));`.

In `windows-dotnet/src/Sanduhr.App/Views/DesignsSettingsPanel.xaml`, delete the first child of its `StackPanel`, the heading

```xml
            <TextBlock Text="{x:Static core:SettingsNames.DeskDesigns}" FontSize="13" FontWeight="SemiBold"
                       Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
```

(the Expander's header carries it now), and change its comment's first words to `Settings, Desk, Designs (collapsed; spec, the agent-designed Desk): …`.

- [ ] **Step 5: Rewrite the Desk page**

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`, in the Alerts TabItem delete the block from `<!-- Sign-in reminders (spec 7.5): the Desk's "sign in to Sanduhr" line. -->` through its `</StackPanel>` (the reminder moves to Desk, Behavior; the duplicate spoken heading "Sign-in reminders" goes with it).

Replace the whole Desk TabItem, from the comment `<!-- Desk (Stage 5): the clock and Claude meters on the desktop. Off by default; every` through the TabItem's closing `</TabItem>`, with (indentation re-done, slice 2 T18's leftover):

```xml
                <!-- Desk (Settings v2, slice 3: the split, WF22, WF8): the switch, Arrange, the map on top, every piece (Show, place, order),
                     Behavior, and the collapsed Designs and Advanced. Fonts, sizes and colors are on Desk Look; Watchers and the Launcher
                     have pages of their own. Every change saves at once and the running Desk follows. -->
                <TabItem Header="Desk" Tag="Desk" AutomationProperties.Name="Desk"
                         DataContext="{Binding Desk}">
                    <Border Background="{DynamicResource Sanduhr.Brush.Glass}" CornerRadius="8">
                        <!-- WF26: the page is as wide as the viewport, so text wraps and the piece rows wrap to two lines below 760 DIP;
                             it is never narrower than a row's first block (490, plus the 2 x 20 margin), and below that it scrolls
                             sideways instead of clipping the Monitor combo. -->
                        <ScrollViewer x:Name="DeskPageScroller" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto" Focusable="False">
                            <ScrollViewer.Resources>
                                <Style TargetType="ScrollBar" BasedOn="{StaticResource SlimThemeScrollBar}" />
                            </ScrollViewer.Resources>
                            <Border x:Name="DeskPageContent" MinWidth="530" HorizontalAlignment="Left"
                                    Width="{Binding ViewportWidth, ElementName=DeskPageScroller}">
                                <StackPanel Margin="20">
                                    <TextBlock Text="Desk" FontSize="14" FontWeight="SemiBold" AutomationProperties.HeadingLevel="Level1"
                                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,4" />
                                    <TextBlock Text="{x:Static core:SettingsNames.DeskLine}" FontSize="11" TextWrapping="Wrap"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,0,0,12" />

                                    <StackPanel views:SettingsAnchor.Id="desk">
                                        <CheckBox Style="{StaticResource ThemedCheckBox}"
                                                  Content="{x:Static core:SettingsNames.DeskSwitch}"
                                                  IsChecked="{Binding Enabled, Mode=TwoWay}"
                                                  AutomationProperties.Name="{x:Static core:SettingsNames.DeskSwitch}" />
                                        <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,8,0,0"
                                                   Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                                   Text="Off by default. Draws on the desktop behind your windows, on every monitor, and keeps clear of the taskbar. Turn it off here at any time." />
                                    </StackPanel>

                                    <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />

                                    <StackPanel views:SettingsAnchor.Id="arrange">
                                        <Button Style="{StaticResource FlatButton}" HorizontalAlignment="Left" Padding="12,6"
                                                Content="{x:Static core:SettingsNames.ArrangeDesk}"
                                                Command="{Binding ArrangeCommand}"
                                                IsEnabled="{Binding CanArrange}"
                                                AutomationProperties.Name="{x:Static core:SettingsNames.ArrangeDesk}" />
                                        <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,8,0,0"
                                                   AutomationProperties.LiveSetting="Polite"
                                                   Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                                   Text="{Binding ArrangeNote}" />
                                    </StackPanel>

                                    <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />

                                    <!-- The Layout map, on top (spec, Desk): a picture of where each piece sits, not a control (WF5). -->
                                    <StackPanel views:SettingsAnchor.Id="map">
                                        <TextBlock Text="{x:Static core:SettingsNames.DeskLayoutMap}" FontSize="13" FontWeight="SemiBold"
                                                   AutomationProperties.HeadingLevel="Level2"
                                                   Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                                        <views:DeskLayoutMap HorizontalAlignment="Left" />
                                        <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,8,0,0"
                                                   Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                                   Text="{x:Static core:SettingsNames.DeskMapCaption}" />
                                    </StackPanel>

                                    <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />

                                    <StackPanel views:SettingsAnchor.Id="pieces" IsEnabled="{Binding NotArranging}">
                                        <TextBlock Text="{x:Static core:SettingsNames.PiecesSection}" FontSize="13" FontWeight="SemiBold"
                                                   AutomationProperties.HeadingLevel="Level2"
                                                   Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                                        <views:DeskPiecesPanel />
                                        <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,2,0,0"
                                                   Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                                   Text="Tick a piece to show it at its usual place. Eight places: the four corners, the top and bottom centers, and the middle of each side. Size scales a piece from 60% to 160%. Move up and Move down change the order of the pieces that share a place. Monitor puts a piece on every monitor, on the main one, or on one monitor; a piece on a monitor that is not connected shows on the main one until it is back." />
                                    </StackPanel>

                                    <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />

                                    <!-- Behavior: clicks, and the sign-in reminder (moved from Alerts: a Desk behavior, WF32). -->
                                    <StackPanel views:SettingsAnchor.Id="desk-clicks">
                                        <TextBlock Text="{x:Static core:SettingsNames.DeskBehavior}" FontSize="13" FontWeight="SemiBold"
                                                   AutomationProperties.HeadingLevel="Level2"
                                                   Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                                        <CheckBox Style="{StaticResource ThemedCheckBox}"
                                                  Content="{x:Static core:SettingsNames.DeskPieceClicksSwitch}"
                                                  IsChecked="{Binding PiecesTakeClicks, Mode=TwoWay}"
                                                  AutomationProperties.Name="{x:Static core:SettingsNames.DeskPieceClicksSwitch}"
                                                  AutomationProperties.HelpText="{x:Static core:DeskPieceClicks.Caption}" />
                                        <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,8,0,0"
                                                   Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                                   Text="{x:Static core:DeskPieceClicks.Caption}" />
                                        <StackPanel views:SettingsAnchor.Id="signin-reminders" Margin="0,12,0,0"
                                                    DataContext="{Binding DataContext.Alerts, RelativeSource={RelativeSource AncestorType=Window}}">
                                            <CheckBox Style="{StaticResource ThemedCheckBox}"
                                                      Content="{x:Static core:SettingsNames.SignInReminders}"
                                                      IsChecked="{Binding DeskSignInReminder, Mode=TwoWay}"
                                                      AutomationProperties.Name="{x:Static core:SettingsNames.SignInReminders}" />
                                            <TextBlock Text="{x:Static core:SignInReminder.Caption}" TextWrapping="Wrap" FontSize="11" Margin="0,8,0,0"
                                                       Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                                        </StackPanel>
                                    </StackPanel>

                                    <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />

                                    <!-- Designs (collapsed): the kept designs, Put my Desk back, and the switch whose changes they undo. -->
                                    <Expander x:Name="DeskDesignsSection" Style="{StaticResource SectionExpander}" views:SettingsAnchor.Id="desk-designs"
                                              AutomationProperties.Name="{x:Static core:SettingsNames.DeskDesigns}">
                                        <Expander.Header>
                                            <StackPanel>
                                                <TextBlock Text="{x:Static core:SettingsNames.DeskDesigns}" FontSize="13" FontWeight="SemiBold"
                                                           AutomationProperties.HeadingLevel="Level2"
                                                           Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                                <TextBlock Text="{Binding Designs.Summary}" FontSize="11" Margin="0,2,0,0"
                                                           Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                                            </StackPanel>
                                        </Expander.Header>
                                        <StackPanel>
                                            <views:DesignsSettingsPanel DataContext="{Binding Designs}" />
                                            <CheckBox Style="{StaticResource ThemedCheckBox}" Margin="0,14,0,0"
                                                      Content="{x:Static core:SettingsNames.DeskDirectSwitch}"
                                                      IsChecked="{Binding DirectDesign, Mode=TwoWay}"
                                                      AutomationProperties.Name="{x:Static core:SettingsNames.DeskDirectSwitch}" />
                                            <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,8,0,0"
                                                       Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                                       Text="Off by default. With the Sanduhr MCP server installed, Claude Code can propose a whole look for the Desk and the widget theme. Sanduhr shows it on the desktop first, with Apply and Dismiss, and nothing is saved until you choose Apply. Turn this on and a design is applied at once instead. Either way the Desk it replaces is saved first, and Put my Desk back undoes the change." />
                                        </StackPanel>
                                    </Expander>

                                    <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />

                                    <!-- Advanced (collapsed): Margins and OLED care. -->
                                    <Expander x:Name="DeskAdvancedSection" Style="{StaticResource SectionExpander}" views:SettingsAnchor.Id="desk-advanced"
                                              AutomationProperties.Name="{x:Static core:SettingsNames.DeskAdvanced}">
                                        <Expander.Header>
                                            <TextBlock Text="{x:Static core:SettingsNames.DeskAdvanced}" FontSize="13" FontWeight="SemiBold"
                                                       AutomationProperties.HeadingLevel="Level2"
                                                       Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                        </Expander.Header>
                                        <StackPanel>
                                            <StackPanel views:SettingsAnchor.Id="margins" IsEnabled="{Binding NotArranging}">
                                                <TextBlock Text="Margins" FontSize="13" FontWeight="SemiBold"
                                                           AutomationProperties.HeadingLevel="Level3"
                                                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                                                <Grid>
                                                    <Grid.ColumnDefinitions>
                                                        <ColumnDefinition Width="90" />
                                                        <ColumnDefinition Width="230" />
                                                        <ColumnDefinition Width="44" />
                                                    </Grid.ColumnDefinitions>
                                                    <Grid.RowDefinitions>
                                                        <RowDefinition Height="Auto" />
                                                        <RowDefinition Height="Auto" />
                                                        <RowDefinition Height="Auto" />
                                                        <RowDefinition Height="Auto" />
                                                    </Grid.RowDefinitions>
                                                    <TextBlock Grid.Row="0" Text="Left" FontSize="12" VerticalAlignment="Center" Margin="0,0,0,6"
                                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                                    <Slider Grid.Row="0" Grid.Column="1" x:Name="MarginLeftSlider" Style="{StaticResource ThemedSlider}" Margin="0,0,0,6"
                                                            Minimum="0" Maximum="400" TickFrequency="4" IsSnapToTickEnabled="True" SmallChange="4" LargeChange="20"
                                                            Value="{Binding MarginLeft, Mode=TwoWay}" AutomationProperties.Name="Left margin" />
                                                    <TextBlock Grid.Row="0" Grid.Column="2" Text="{Binding Value, ElementName=MarginLeftSlider, StringFormat='{}{0:0}'}"
                                                               FontSize="11" VerticalAlignment="Center" HorizontalAlignment="Right" Margin="0,0,0,6"
                                                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}" FontFamily="Cascadia Code, Consolas" />
                                                    <TextBlock Grid.Row="1" Text="Right" FontSize="12" VerticalAlignment="Center" Margin="0,0,0,6"
                                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                                    <Slider Grid.Row="1" Grid.Column="1" x:Name="MarginRightSlider" Style="{StaticResource ThemedSlider}" Margin="0,0,0,6"
                                                            Minimum="0" Maximum="400" TickFrequency="4" IsSnapToTickEnabled="True" SmallChange="4" LargeChange="20"
                                                            Value="{Binding MarginRight, Mode=TwoWay}" AutomationProperties.Name="Right margin" />
                                                    <TextBlock Grid.Row="1" Grid.Column="2" Text="{Binding Value, ElementName=MarginRightSlider, StringFormat='{}{0:0}'}"
                                                               FontSize="11" VerticalAlignment="Center" HorizontalAlignment="Right" Margin="0,0,0,6"
                                                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}" FontFamily="Cascadia Code, Consolas" />
                                                    <TextBlock Grid.Row="2" Text="Top" FontSize="12" VerticalAlignment="Center" Margin="0,0,0,6"
                                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                                    <Slider Grid.Row="2" Grid.Column="1" x:Name="MarginTopSlider" Style="{StaticResource ThemedSlider}" Margin="0,0,0,6"
                                                            Minimum="0" Maximum="400" TickFrequency="4" IsSnapToTickEnabled="True" SmallChange="4" LargeChange="20"
                                                            Value="{Binding MarginTop, Mode=TwoWay}" AutomationProperties.Name="Top margin" />
                                                    <TextBlock Grid.Row="2" Grid.Column="2" Text="{Binding Value, ElementName=MarginTopSlider, StringFormat='{}{0:0}'}"
                                                               FontSize="11" VerticalAlignment="Center" HorizontalAlignment="Right" Margin="0,0,0,6"
                                                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}" FontFamily="Cascadia Code, Consolas" />
                                                    <TextBlock Grid.Row="3" Text="Bottom" FontSize="12" VerticalAlignment="Center"
                                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                                    <Slider Grid.Row="3" Grid.Column="1" x:Name="MarginBottomSlider" Style="{StaticResource ThemedSlider}"
                                                            Minimum="0" Maximum="400" TickFrequency="4" IsSnapToTickEnabled="True" SmallChange="4" LargeChange="20"
                                                            Value="{Binding MarginBottom, Mode=TwoWay}" AutomationProperties.Name="Bottom margin" />
                                                    <TextBlock Grid.Row="3" Grid.Column="2" Text="{Binding Value, ElementName=MarginBottomSlider, StringFormat='{}{0:0}'}"
                                                               FontSize="11" VerticalAlignment="Center" HorizontalAlignment="Right"
                                                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}" FontFamily="Cascadia Code, Consolas" />
                                                </Grid>
                                                <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,8,0,0"
                                                           Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                                           Text="Measured from the screen's edge, inside the taskbar." />
                                            </StackPanel>

                                            <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />

                                            <views:OledCarePanel views:SettingsAnchor.Id="desk-oled" />
                                        </StackPanel>
                                    </Expander>

                                    <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />

                                    <views:TrademarkFooter />
                                </StackPanel>
                            </Border>
                        </ScrollViewer>
                    </Border>
                </TabItem>
```

**Slice 1 review item 7 (the Desk defined twice in a row)**, decided here: the definition line (`SettingsNames.DeskLine`, WF21: a core word's definition is its home page's first line) and the switch's label (`SettingsNames.DeskSwitch`, WF16, which the tour quotes) are both the spec's words and stay word for word. They stop reading as one doubled line by layout: Task 16 puts the page's jump links between them. Nothing to do in this step; record it in the task report so the owner sees the call.

- [ ] **Step 6: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsDeskSplitIndexTests|FullyQualifiedName~SettingsReachIndexTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~WhatsNewTests|FullyQualifiedName~OledCareSettingsTests"`
Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskSplitPagesTests|FullyQualifiedName~SettingsReachTests|FullyQualifiedName~DeskDirectSwitchTests|FullyQualifiedName~DesignsPanelAutomationTests|FullyQualifiedName~DesignsSettingsTests|FullyQualifiedName~OledCareTests|FullyQualifiedName~SettingsWindowNamesTests|FullyQualifiedName~DeskPiecesSettingsTests"`
Expected: PASS, the slice 2 `[DesktopFact]` Tab walk included (the Expander toggles are stops; Tab from the last one still reaches the search box). The "oled-care", "desk-design" and "saved-designs" cards land in Advanced and Designs, opened.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/Views/DesignsSettingsPanel.xaml windows-dotnet/src/Sanduhr.App/ViewModels/DesignsSettingsViewModel.cs windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsReachIndexTests.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs windows-dotnet/tests/Sanduhr.App.Tests/SettingsReachTests.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskDirectSwitchTests.cs
git commit -m "feat(desk): the Desk page in the spec's order: map on top, Pieces, Behavior with sign-in reminders, collapsed Designs and Advanced"
```

---

### Task 15: The Layout map folds below 600 DIP and remembers the person's choice

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/WindowFit.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (the `map` StackPanel becomes an Expander)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs` (fold on size; remember a choice)
- Test: `windows-dotnet/tests/Sanduhr.Tests/WindowFitTests.cs`; `windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs`

**Interfaces:**
- Produces: `WindowFit.MapFoldsBelow` (600), `WindowFit.MapFolds(double windowHeight, bool? chosenOpen)`; the Expander `DeskMapFold` (anchor `map`).

- [ ] **Step 1: Write the failing tests**

Append to the class in `windows-dotnet/tests/Sanduhr.Tests/WindowFitTests.cs`:

```csharp
    [Theory]
    [InlineData(599, null, true)]
    [InlineData(600, null, false)]
    [InlineData(800, false, true)]    // folded by the person: stays folded on a tall window
    [InlineData(500, true, false)]    // opened by the person: stays open on a short one
    public void The_map_folds_below_600_unless_the_person_chose(double height, bool? chosenOpen, bool folds) =>
        Assert.Equal(folds, WindowFit.MapFolds(height, chosenOpen));
```

Append to `DeskSplitPagesTests`:

```csharp
    [Fact]
    public void The_map_folds_below_600_and_a_choice_holds_for_the_window() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk);
            window.Height = 560;
            Settle(window, guard);
            var fold = (Expander)window.FindName("DeskMapFold");
            Assert.False(fold.IsExpanded);
            Assert.InRange(Below<ToggleButton>(fold).First().ActualHeight, 43.5, 46);   // a 44 DIP strip
            window.Height = 800;
            Settle(window, guard);
            Assert.True(fold.IsExpanded);
            fold.IsExpanded = false;                                                       // the person folds it
            window.Height = 820;
            Settle(window, guard);
            Assert.False(fold.IsExpanded);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_link_to_the_folded_map_opens_it() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk);
            window.Height = 560;
            Settle(window, guard);
            var fold = (Expander)window.FindName("DeskMapFold");
            Assert.False(fold.IsExpanded);
            window.SelectPage(SettingsPage.Desk, SettingsIndex.DeskMap);
            Settle(window, guard);
            Assert.True(fold.IsExpanded);
            Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskMap), window.Place);
        }
        finally { window.Close(); }
    });
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~WindowFitTests`
Expected: build FAILS: `WindowFit.MapFolds` doesn't exist.

- [ ] **Step 3: The rule**

In `windows-dotnet/src/Sanduhr.Core/WindowFit.cs`, inside `public static class WindowFit`, add:

```csharp

    /// <summary>Below this window height, in DIPs, the Desk's Layout map folds to its 44 DIP header (spec, Short windows).</summary>
    public const double MapFoldsBelow = 600;

    /// <summary>Whether the Layout map is folded: the person's own choice this window's life when they made one, else folded below
    /// <see cref="MapFoldsBelow"/> (the Mac's rule).</summary>
    public static bool MapFolds(double windowHeight, bool? chosenOpen) =>
        chosenOpen is { } open ? !open : windowHeight < MapFoldsBelow;
```

- [ ] **Step 4: The fold**

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`, in the Desk TabItem replace the map block (from `<!-- The Layout map, on top (spec, Desk): …` through its `</StackPanel>`) with:

```xml
                                    <!-- The Layout map, on top (spec, Desk): a picture of where each piece sits, not a control (WF5). Below 600 DIP of
                                         window height it folds to its 44 DIP header; a fold or unfold by the person holds for the window's life. -->
                                    <Expander x:Name="DeskMapFold" Style="{StaticResource SectionExpander}" views:SettingsAnchor.Id="map" IsExpanded="True"
                                              AutomationProperties.Name="{x:Static core:SettingsNames.DeskLayoutMap}"
                                              Expanded="DeskMapFold_Toggled" Collapsed="DeskMapFold_Toggled">
                                        <Expander.Header>
                                            <TextBlock Text="{x:Static core:SettingsNames.DeskLayoutMap}" FontSize="13" FontWeight="SemiBold"
                                                       AutomationProperties.HeadingLevel="Level2"
                                                       Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                        </Expander.Header>
                                        <StackPanel>
                                            <views:DeskLayoutMap HorizontalAlignment="Left" />
                                            <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,8,0,0"
                                                       Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                                       Text="{x:Static core:SettingsNames.DeskMapCaption}" />
                                        </StackPanel>
                                    </Expander>
```

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs`, in the constructor after `InitializeComponent();` add

```csharp
        SizeChanged += (_, _) => FoldMapForHeight();
```

and add these members (after `OnSettingsLinkClick`):

```csharp

    // The Desk's Layout map folds below 600 DIP of window height (spec, Short windows). A fold or unfold by the person, or a link that
    // opens it, holds for this window's life, as on the Mac.
    private bool? _mapChosenOpen;
    private bool _foldingMap;

    private void FoldMapForHeight()
    {
        if (!IsLoaded || ActualHeight <= 0) return;
        bool open = !WindowFit.MapFolds(ActualHeight, _mapChosenOpen);
        if (DeskMapFold.IsExpanded == open) return;
        _foldingMap = true;
        DeskMapFold.IsExpanded = open;
        _foldingMap = false;
    }

    private void DeskMapFold_Toggled(object sender, RoutedEventArgs e)
    {
        // XAML's own IsExpanded="True" raises Expanded while loading: that is not a choice.
        if (IsLoaded && !_foldingMap && ReferenceEquals(e.OriginalSource, sender))
            _mapChosenOpen = DeskMapFold.IsExpanded;
    }
```

(`WindowFit` is in `Sanduhr.Core`, already imported by the file for slice 2's `FitToWorkArea`.)

- [ ] **Step 5: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~WindowFitTests`
Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskSplitPagesTests|FullyQualifiedName~SettingsReachTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/WindowFit.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs windows-dotnet/tests/Sanduhr.Tests/WindowFitTests.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs
git commit -m "feat(desk): the Layout map folds below 600 DIP and remembers the person's choice for the window"
```

---

### Task 16: Headings are stops; jump links on Desk, Desk Look and Now playing; Pieces within 10 Tab stops

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/Views/SectionJumpLinks.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (`JumpLinks`), `SettingsNames.cs` (`JumpLinksName`, `NowPlayingJumpWidget`)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (Desk and Now playing: the links; Now playing and Weather: heading levels), `DeskLookPage.xaml` (the links), `DeskMessagePage.xaml` (the `Heading` style)
- Test: `windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs`; `windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs`

**Interfaces:**
- Produces: `SettingsIndex.JumpLinks(SettingsPage page)` → `IReadOnlyList<(string Title, string Anchor)>`; `SectionJumpLinks` (a `WrapPanel` with a `Page` dependency property; its buttons carry `SettingsLinkTo.Link`); `SettingsNames.JumpLinksName` ("Sections on this page"), `NowPlayingJumpWidget` ("In the widget").

- [ ] **Step 1: Write the failing tests**

Append to `SettingsDeskSplitIndexTests`:

```csharp
    [Theory]
    [InlineData(SettingsPage.Desk)]
    [InlineData(SettingsPage.DeskLook)]
    [InlineData(SettingsPage.NowPlaying)]
    public void A_page_with_four_or_more_sections_has_a_jump_link_to_each(SettingsPage page)
    {
        var links = SettingsIndex.JumpLinks(page);
        Assert.True(links.Count >= 4);
        Assert.All(links, l => Assert.NotNull(SettingsIndex.Find(page, l.Anchor)));
        Assert.Equal(links.Count, links.Select(l => l.Anchor).Distinct().Count());
    }

    [Fact]
    public void The_Desks_jump_links_read_as_the_spec_writes_them()
    {
        Assert.Equal(["Pieces", "Behavior", "Designs", "Advanced"], SettingsIndex.JumpLinks(SettingsPage.Desk).Select(l => l.Title));
        Assert.Empty(SettingsIndex.JumpLinks(SettingsPage.Weather));
    }

    [Fact]
    public void The_Desk_split_pages_mark_their_headings()
    {
        var sources = new[] { TabRange("Desk"), TabRange("NowPlaying"), TabRange("Weather") }
            .Concat(new[] { "DeskLookPage", "WatchersPage", "LauncherPage" }
                .Select(p => File.ReadAllText(Path.Combine(AppDir(), "Views", p + ".xaml"))));
        var bad = new List<string>();
        foreach (var src in sources)
            foreach (Match t in Regex.Matches(src, "<TextBlock\\b[^>]*FontSize=\"1[34]\"[^>]*FontWeight=\"SemiBold\"[^>]*>"))
                if (!t.Value.Contains("AutomationProperties.HeadingLevel=\"Level", StringComparison.Ordinal))
                    bad.Add(t.Value);
        Assert.True(bad.Count == 0, "Headings not marked:" + Environment.NewLine + string.Join(Environment.NewLine, bad));
        Assert.Contains("<Setter Property=\"AutomationProperties.HeadingLevel\" Value=\"Level2\" />",
            File.ReadAllText(Path.Combine(AppDir(), "Views", "DeskMessagePage.xaml")));
    }
```

Append to `DeskSplitPagesTests`:

```csharp
    [Fact]
    public void A_jump_link_lands_on_its_section_and_opens_it() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk);
            Settle(window, guard);
            var links = Below<SectionJumpLinks>((DependencyObject)TabOf(window, SettingsPage.Desk).Content).Single();
            Assert.Equal(SettingsNames.JumpLinksName, System.Windows.Automation.AutomationProperties.GetName(links));
            var advanced = links.Children.OfType<Button>().Single(b => (string)b.Content == SettingsNames.DeskAdvanced);
            advanced.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Desk, SettingsIndex.DeskAdvanced), window.Place);
            Assert.True(((Expander)window.FindName("DeskAdvancedSection")).IsExpanded);
        }
        finally { window.Close(); }
    });

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

    /// <summary>Done (spec, slice 3): the Desk page reaches Pieces in under 10 Tab stops. Keyboard focus is routed inside the
    /// never-activating window, as slice 2's Tab walk does.</summary>
    [DesktopFact]
    public void From_the_Desk_pages_first_stop_Tab_reaches_Pieces_in_under_10() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk);
            Settle(window, guard);
            guard.RouteKeyboard();
            var content = (DependencyObject)TabOf(window, SettingsPage.Desk).Content;
            var pieces = Marked(window, SettingsPage.Desk, SettingsIndex.DeskPieces);
            var first = Below<Control>(content).First(c => c is { Focusable: true, IsTabStop: true, IsVisible: true, IsEnabled: true });
            Keyboard.Focus(first);
            guard.Check();
            Assert.True(ReferenceEquals(Keyboard.FocusedElement, first), "keyboard focus could not be routed to the Desk page");
            int stops = 1;
            IInputElement? at = first;
            while (stops < 20 && at is not null && !Inside(at, pieces))
            {
                Next(at);
                Pump();
                guard.Check();
                at = Keyboard.FocusedElement;
                stops++;
            }
            Assert.True(Inside(at, pieces), $"Tab never reached Pieces; it stopped on {at?.GetType().Name ?? "nothing"}");
            Assert.True(stops < 10, $"Pieces is Tab stop {stops} from the page's first stop");
        }
        finally { window.Close(); }
    });
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsDeskSplitIndexTests`
Expected: build FAILS: `SettingsIndex.JumpLinks` doesn't exist.

- [ ] **Step 3: The table and the names**

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, in the slice 3 block add:

```csharp
    /// <summary>A page's row of jump links, spoken (spec, Keyboard and screen reader).</summary>
    public const string JumpLinksName = "Sections on this page";
    /// <summary>Now playing's first jump link (its section is "Show what's playing in the widget").</summary>
    public const string NowPlayingJumpWidget = "In the widget";
```

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`, after `Landing` add:

```csharp

    /// <summary>
    /// A page's jump links (spec, Keyboard and screen reader: pages with four or more sections), in page order: a short label and
    /// the section's anchor. Slice 3 gives them to its pages; other pages get theirs in their own slices. Empty for a page without.
    /// </summary>
    public static IReadOnlyList<(string Title, string Anchor)> JumpLinks(SettingsPage page) => page switch
    {
        SettingsPage.Desk => [(SettingsNames.PiecesSection, DeskPieces), (SettingsNames.DeskBehavior, DeskClicks),
            (SettingsNames.DeskDesigns, DeskDesigns), (SettingsNames.DeskAdvanced, DeskAdvanced)],
        SettingsPage.DeskLook => [(SettingsNames.FontsSection, DeskLook), (SettingsNames.SizesSection, DeskSizes),
            (SettingsNames.InkAndColor, DeskInk), (SettingsNames.BackPlate, DeskPlate)],
        SettingsPage.NowPlaying => [(SettingsNames.NowPlayingJumpWidget, NowPlayingWidget), ("Apps", NowPlayingApps),
            ("Bars", NowPlayingBars), ("Looks", NowPlayingLooks)],
        _ => [],
    };
```

- [ ] **Step 4: The control and the pages**

Create `windows-dotnet/src/Sanduhr.App/Views/SectionJumpLinks.cs`:

```csharp
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// A page's row of jump links, "Pieces · Behavior · Designs · Advanced" (spec, Keyboard and screen reader: pages with four or more
/// sections, WF22). Each is a <see cref="SettingsLinkTo"/> link to its section, so the window scrolls there, opens a collapsed
/// section and puts the keyboard on it. The links come from <see cref="SettingsIndex.JumpLinks"/>.
/// </summary>
public sealed class SectionJumpLinks : WrapPanel
{
    public static readonly DependencyProperty PageProperty = DependencyProperty.Register(
        nameof(Page), typeof(SettingsPage), typeof(SectionJumpLinks),
        new PropertyMetadata(SettingsPage.General, (d, _) => ((SectionJumpLinks)d).Build()));

    public SettingsPage Page
    {
        get => (SettingsPage)GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    public SectionJumpLinks()
    {
        Margin = new Thickness(0, 0, 0, 12);
        AutomationProperties.SetName(this, SettingsNames.JumpLinksName);
    }

    private void Build()
    {
        Children.Clear();
        var links = SettingsIndex.JumpLinks(Page);
        for (int i = 0; i < links.Count; i++)
        {
            if (i > 0)
            {
                var dot = new TextBlock { Text = "·", FontSize = 11, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
                dot.SetResourceReference(TextBlock.ForegroundProperty, "Sanduhr.Brush.TextDim");
                AutomationProperties.SetAccessibilityView(dot, AccessibilityView.Raw);
                Children.Add(dot);
            }
            var link = new Button { Content = links[i].Title };
            link.SetResourceReference(StyleProperty, "SettingsLinkButton");
            SettingsLinkTo.SetLink(link, SettingsIndex.PageId(Page) + "#" + links[i].Anchor);
            AutomationProperties.SetName(link, links[i].Title);
            Children.Add(link);
        }
    }
}
```

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`:
- in the Desk TabItem, right after the `DeskLine` TextBlock, add `<views:SectionJumpLinks Page="Desk" />` (this row now separates the Desk's definition from its switch, slice 1 review item 7);
- in the Now playing TabItem, right after `<views:DeskPieceStatusPanel DataContext="{Binding Piece}" Margin="0,0,0,12" />`, add `<views:SectionJumpLinks Page="NowPlaying" />`;
- in the Now playing TabItem, add `AutomationProperties.HeadingLevel="Level2"` to the `Text="Apps" FontSize="13" FontWeight="SemiBold"` TextBlock; in the Weather TabItem, to the `Text="City" FontSize="13" FontWeight="SemiBold"` TextBlock. Then run Step 5's heading scan: every other heading it names in those two TabItems gets the same attribute.

In `windows-dotnet/src/Sanduhr.App/Views/DeskLookPage.xaml`, right after the title TextBlock add `<views:SectionJumpLinks Page="DeskLook" />`.

In `windows-dotnet/src/Sanduhr.App/Views/DeskMessagePage.xaml`, in the `Heading` style add, after its `Foreground` setter:

```xml
            <Setter Property="AutomationProperties.HeadingLevel" Value="Level2" />
```

- [ ] **Step 5: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsDeskSplitIndexTests|FullyQualifiedName~SettingsSourceScanTests"`
Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskSplitPagesTests|FullyQualifiedName~SettingsReachTests|FullyQualifiedName~SettingsWindowNamesTests"`
Expected: PASS, both `[DesktopFact]`s included (slice 2's Tab walk and the Pieces count: link, link, link, link, the Desk switch, Arrange Desk… when the Desk is on, the map's toggle, then the first row's Show box: 8 at most). If the count fails, the message names the stop; do not loop the run.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Views/SectionJumpLinks.cs windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/Views/DeskLookPage.xaml windows-dotnet/src/Sanduhr.App/Views/DeskMessagePage.xaml windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs
git commit -m "feat(settings-v2): section headings are headings; jump links on Desk, Desk Look and Now playing"
```

---

### Task 17: Arrange Desk takes the arrow keys

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskArrangeMode.cs` (`DeskArrangeContent`: `HandleArrow`, `OnPreviewKeyDown`, `PieceWithFocus`)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DeskArrangeKeysTests.cs` (create)

**Interfaces:**
- Consumes: `DeskArrange.ArrowKey`, `DeskArrange.Step` (Task 2); `DeskArrangement.Put`, `Move(string, int)`.
- Produces: `public bool DeskArrangeContent.HandleArrow(string widget, string keyName, bool shift)`; the arrows act on the piece whose Hide (or Show on) button holds the keyboard; a change raises `Toggled(HideKey(widget))`, so every surface lays out again and the keyboard stays on that piece.

- [ ] **Step 1: Write the failing test**

Create `windows-dotnet/tests/Sanduhr.App.Tests/DeskArrangeKeysTests.cs`:

```csharp
using Sanduhr.App.Desk;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 3 (spec, Keyboard and screen reader): Arrange Desk without a mouse. Tab picks a piece (its Hide
/// button), the arrows move it between places, Shift+Up and Shift+Down move it in its stack.</summary>
public sealed class DeskArrangeKeysTests
{
    private static DeskScreen Screen() =>
        new("DISPLAY1", new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1032), 1.0, true,
            TaskbarClearance.Insets(new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1032), [], 1.0), "main");

    private static readonly DeskFrame Frame = new() { Now = new DateTimeOffset(2026, 10, 25, 10, 9, 0, TimeSpan.Zero) };

    [Fact]
    public void Arrows_move_the_piece_and_Shift_arrows_reorder_its_stack() => Sta.Run(() =>
    {
        var settings = (DeskSettings.Default with { Enabled = true, Layout = "clock:bl claude:bl" }).Normalized();
        var working = new DeskArrangement(settings.Layout);
        var content = new DeskArrangeContent(Screen(), settings, new DeskFonts(), Frame, working, primary: true);
        var focusAfter = new List<string>();
        content.Toggled += focusAfter.Add;

        Assert.True(content.HandleArrow("claude", "Up", shift: true));
        Assert.Equal("claude:bl clock:bl", working.Layout);
        Assert.False(content.HandleArrow("claude", "Up", shift: true));      // already on top: nothing changes
        Assert.True(content.HandleArrow("clock", "Right", shift: false));
        Assert.Equal(DeskAnchor.BC, working.Placement("clock")!.Anchor);
        Assert.False(content.HandleArrow("clock", "Down", shift: false));    // the screen's edge
        Assert.False(content.HandleArrow("clock", "Tab", shift: false));     // not an arrow
        Assert.False(content.HandleArrow("message", "Left", shift: false));  // not placed
        Assert.Equal([DeskArrangeContent.HideKey("claude"), DeskArrangeContent.HideKey("clock")], focusAfter);
        content.Dispose();
    });
}
```

(The `Screen` helper is `DeskMonitorTests.cs:172`'s with one screen. Take the file's `using` lines from `DeskMonitorTests.cs` too, so `DeskScreen`, `PxRect`, `TaskbarClearance`, `DeskFrame` and `DeskFonts` resolve wherever they live.)

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~DeskArrangeKeysTests`
Expected: build FAILS: `DeskArrangeContent` has no `HandleArrow`.

- [ ] **Step 3: Write the keys**

In `windows-dotnet/src/Sanduhr.App/Desk/DeskArrangeMode.cs`, in `DeskArrangeContent`, after `OnKeyDown` add:

```csharp

    /// <summary>
    /// The arrow keys (spec, Keyboard and screen reader). Tab picks a piece: its Hide or Show on button holds the keyboard. Then a
    /// plain arrow moves it to the next place that way and Shift+Up or Shift+Down moves it up or down its stack. Seen before the
    /// buttons see them, so the arrows never move focus instead.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled || _drag is not null) return;
        var key = e.Key.ToString();
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (DeskArrange.ArrowKey(key, shift) is null || PieceWithFocus() is not { } widget) return;
        if (HandleArrow(widget, key, shift)) e.Handled = true;
    }

    /// <summary>The piece whose Hide or Show on button holds the keyboard, null when none does.</summary>
    private string? PieceWithFocus()
    {
        foreach (var (key, button) in _buttons)
            if (button.IsKeyboardFocused && (key.StartsWith("hide:", StringComparison.Ordinal) || key.StartsWith("showon:", StringComparison.Ordinal)))
                return key[(key.IndexOf(':') + 1)..];
        return null;
    }

    /// <summary>
    /// An arrow on <paramref name="widget"/>: to the next place that way (<see cref="DeskArrange.Step"/>, joining the bottom of that
    /// stack), or up or down its own stack. True when the working layout changed; then every surface lays out again and the
    /// keyboard stays on the piece's Hide button (<see cref="Toggled"/>).
    /// </summary>
    public bool HandleArrow(string widget, string keyName, bool shift)
    {
        if (DeskArrange.ArrowKey(keyName, shift) is not { } arrow || _working.Placement(widget) is not { } piece) return false;
        var before = _working.Layout;
        if (arrow.Kind == DeskArrangeArrowKind.Order)
            _working.Move(widget, arrow.Dy);
        else if (DeskArrange.Step(piece.Anchor, arrow.Dx, arrow.Dy) is { } to)
            _working.Put(widget, to, before: null);
        if (_working.Layout == before) return false;
        Toggled?.Invoke(HideKey(widget));
        return true;
    }
```

(`_buttons`, `_drag`, `_working`, `Toggled` and `HideKey` are the class's own; `DeskArrangeMode.OnToggled` lays every surface out again and puts the keyboard on that key's button.)

- [ ] **Step 4: Run it to see it pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskArrangeKeysTests|FullyQualifiedName~DeskMonitorTests|FullyQualifiedName~DeskMeterStylesViewTests"`
Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DeskArrangeTests|FullyQualifiedName~DeskArrangeGestureTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Desk/DeskArrangeMode.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskArrangeKeysTests.cs
git commit -m "feat(desk): Arrange Desk moves the chosen piece with the arrows and reorders its stack with Shift+Up and Shift+Down"
```

---

### Task 18: The Claude Code glow and the companion install use the table; the glow's page says where it sits

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs` (`PlaceGlowSpot`)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/WidgetReviewService.cs:233-238, 357-362`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (the Claude Code page reloads the Desk page by default)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (Claude Code, `glow-where`: the glow's line)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs`

**Interfaces:**
- Consumes: `DeskArrangement.PlaceAtDefault` (Task 1), `DeskSettingsViewModel.GlowPiece` (Task 7), `DeskPieceStatusPanel` (Task 10).
- Produces: no new API. After this task no code outside `DeskArrangement.cs` and the design formats calls `DeskArrangement.DefaultAnchor` to show a piece.

- [ ] **Step 1: Write the failing test**

Append to `DeskSplitPagesTests`:

```csharp
    [Fact]
    public void Choosing_the_Desk_for_the_glow_places_its_spot_and_its_line_says_where() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.ClaudeCode, SettingsIndex.GlowWhere);
            Settle(window, guard);
            svm.ClaudeCodePage.GlowTarget = ClaudeCodeViewModel.GlowTargets.First(t => t.Kind == GlowTargetKind.Desk);
            Settle(window, guard);
            Assert.Contains("glow:tc", Store().LoadDesk().Layout);
            Assert.True(svm.Desk.GlowPiece.IsPlaced);                     // the Desk page heard it without a visit
            var line = Below<DeskPieceStatusPanel>(Marked(window, SettingsPage.ClaudeCode, SettingsIndex.GlowWhere)).Single();
            Assert.True(line.IsVisible);
            Assert.Equal(svm.Desk.GlowPiece.Text, ((TextBlock)line.FindName("StatusText")).Text);
        }
        finally { window.Close(); }
    });
```

(`GlowTargetKind` is in `Sanduhr.Core`; `ClaudeCodeViewModel.GlowTargets` is the static list the picker shows.)

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~DeskSplitPagesTests`
Expected: FAIL: `svm.Desk.GlowPiece.IsPlaced` is false (nothing reloads the Desk page in a test; App's own `DeskReload` is not wired here), and no `DeskPieceStatusPanel` is marked under `glow-where`.

- [ ] **Step 3: One table, and the Desk page hears the glow**

In `windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs`, in `PlaceGlowSpot`, replace

```csharp
            if (layout.Placement("glow") is not null) return;
            layout.Place("glow", DeskArrangement.DefaultAnchor("glow"));
```

with

```csharp
            // DeskArrangement.DefaultPlaceFor: the one table of default places (Settings v2, Principle 4).
            if (layout.PlaceAtDefault("glow") is null) return;
```

In `windows-dotnet/src/Sanduhr.App/Desk/WidgetReviewService.cs`, replace

```csharp
            if (layout.Placement(CompanionSettings.Widget) is null)
            {
                layout.Place(CompanionSettings.Widget, DeskArrangement.DefaultAnchor(CompanionSettings.Widget));
                _store.SaveDesk(desk with { Layout = layout.Layout });
            }
```

with

```csharp
            if (layout.PlaceAtDefault(CompanionSettings.Widget) is not null)
                _store.SaveDesk(desk with { Layout = layout.Layout });
```

and

```csharp
            if (layout.Placement(CompanionSettings.Widget) is null)
            {
                layout.Place(CompanionSettings.Widget, DeskArrangement.DefaultAnchor(CompanionSettings.Widget));
                desk = desk with { Layout = layout.Layout };
            }
```

with

```csharp
            if (layout.PlaceAtDefault(CompanionSettings.Widget) is not null)
                desk = desk with { Layout = layout.Layout };
```

Then `git grep -n "DefaultAnchor(" windows-dotnet/src` must list only `DeskArrangement.cs`, `DeskDesign.cs`, `DeskDesignApply.cs`, `DeskDesignLibrary.cs`, `DeskDesignValidator.cs` and `NowPlayingLook.cs` (the layout and design formats' fallback, decision 1).

In `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`, after the three `….Piece = Desk.PieceStatus(…)` lines (Task 10) add

```csharp
        // The glow's spot is placed by the Claude Code page through its own store: the Desk page reads it again (App replaces this
        // with one that also reloads the running Desk).
        ClaudeCodePage.DeskReload = Desk.Reload;
```

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`, in the Claude Code TabItem's `glow-where` section, after the TextBlock with `Text="{x:Static core:GlowTargetSettings.DeskCaption}"` add

```xml
                                                <!-- The glow's spot on the Desk: its line (Principle 1), while the Desk is the target. -->
                                                <Border Margin="0,6,0,0" Visibility="{Binding GlowOnDesk, Converter={StaticResource BoolVis}}">
                                                    <views:DeskPieceStatusPanel
                                                        DataContext="{Binding DataContext.Desk.GlowPiece, RelativeSource={RelativeSource AncestorType=Window}}" />
                                                </Border>
```

- [ ] **Step 4: Run it to see it pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskSplitPagesTests|FullyQualifiedName~WidgetReviewServiceTests|FullyQualifiedName~ClaudeCodeLinkTests|FullyQualifiedName~ClaudeCodeFolderChoiceTests|FullyQualifiedName~SettingsWindowNamesTests"`
Expected: PASS. If a `WidgetReviewServiceTests` case pins the companion at `br`, it now reads `bc` (`DefaultPlaceFor`); change that expectation and nothing else.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs windows-dotnet/src/Sanduhr.App/Desk/WidgetReviewService.cs windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs
git commit -m "feat(desk): the glow and a companion install place their piece from the one table; the glow's page says where it sits"
```

---

### Task 19: Old links land on the new pages: cards and tour name the exact place, aliases pinned row by row

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/WhatsNewCards.cs` (eight bodies), `WelcomeTour.cs` (the Desk card's body)
- Test: `windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs`, `SettingsLinkTests.cs:74-82`; `windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs`

**Interfaces:**
- Consumes: every page and anchor of Tasks 6 to 16.
- Produces: no new API. Cards keep their destination and anchor (slice 2's design: the aliases carry them); only their words change.

- [ ] **Step 1: Write the failing tests**

Append to `SettingsDeskSplitIndexTests`:

```csharp
    [Theory]
    [InlineData("ci-runners", "Settings, Watchers")]
    [InlineData("companion", "Settings, Desk, Pieces")]
    [InlineData("meter-styles", "Settings, Desk, Pieces")]
    [InlineData("launcher", "Settings, Launcher")]
    [InlineData("oled-care", "Settings, Desk, Advanced")]
    [InlineData("saved-designs", "Settings, Desk, Designs")]
    [InlineData("watchers", "Settings, Watchers")]
    [InlineData("desk-plate", "Settings, Desk Look, Back plate")]
    public void A_Desk_card_names_the_exact_place(string id, string place)
    {
        var body = WhatsNewCards.Table.Single(c => c.Id == id).Body;
        Assert.Contains(place, body);
        Assert.Empty(SettingsNames.PathProblems(body));
    }

    [Fact]
    public void The_tours_Desk_card_says_where_pieces_are_arranged() =>
        Assert.Contains("Settings, Desk, Pieces", WelcomeTour.Table.Single(t => t.Id == "desk").Card.Body);

    [Theory]
    [InlineData(SettingsPage.Desk, "look", SettingsPage.DeskLook, "look")]
    [InlineData(SettingsPage.Desk, "ink", SettingsPage.DeskLook, "ink")]
    [InlineData(SettingsPage.Desk, "desk-plate", SettingsPage.DeskLook, "desk-plate")]
    [InlineData(SettingsPage.Desk, "desk-watchers", SettingsPage.Watchers, "watchers")]
    [InlineData(SettingsPage.Desk, "desk-launcher", SettingsPage.Launcher, "launcher")]
    [InlineData(SettingsPage.Desk, "desk-direct", SettingsPage.Desk, "desk-designs")]
    [InlineData(SettingsPage.DeskMessage, "message-look", SettingsPage.DeskLook, "sizes")]
    [InlineData(SettingsPage.Alerts, "signin-reminders", SettingsPage.Desk, "signin-reminders")]
    [InlineData(SettingsPage.Widgets, "widgets-companion", SettingsPage.Desk, "pieces")]
    public void Every_slice_3_alias_lands_on_its_new_home(SettingsPage from, string anchor, SettingsPage to, string toAnchor)
    {
        Assert.Equal(new SettingsLink(to, toAnchor), SettingsIndex.Resolve(from, anchor));
        Assert.Equal(new SettingsLink(to, toAnchor), SettingsIndex.ParseLink(SettingsIndex.PageId(from) + "#" + anchor));
    }
```

In `windows-dotnet/tests/Sanduhr.Tests/SettingsLinkTests.cs`, replace the near-tautological `A_link_is_checked_by_the_same_rule_as_Check` (slice 2 final review, T11) with expected places:

```csharp
    [Theory]
    [InlineData("desk#pieces", SettingsPage.Desk, "pieces")]
    [InlineData("desk#nowhere", SettingsPage.Desk, null)]
    [InlineData("desk", SettingsPage.Desk, null)]
    [InlineData("history#chart", SettingsPage.History, "chart")]            // History is a page until slice 5
    [InlineData("desk#desk-launcher", SettingsPage.Launcher, "launcher")]   // an alias live since slice 3
    [InlineData("deskmessage#message-look", SettingsPage.DeskLook, "sizes")]
    [InlineData("DESKLOOK#INK", SettingsPage.DeskLook, "ink")]
    public void A_link_lands_where_the_rule_says(string text, SettingsPage page, string? anchor) =>
        Assert.Equal(new SettingsLink(page, anchor), SettingsIndex.ParseLink(text));
```

Append to `DeskSplitPagesTests`:

```csharp
    [Fact]
    public void Old_desk_links_land_on_the_new_pages() => AppHost.Run(() =>
    {
        var (window, _, guard) = Open("Personal");
        try
        {
            foreach (var (from, anchor, to, toAnchor) in new[]
            {
                (SettingsPage.Desk, SettingsIndex.DeskLook, SettingsPage.DeskLook, SettingsIndex.DeskLook),
                (SettingsPage.Desk, SettingsIndex.DeskInk, SettingsPage.DeskLook, SettingsIndex.DeskInk),
                (SettingsPage.Desk, SettingsIndex.DeskPlate, SettingsPage.DeskLook, SettingsIndex.DeskPlate),
                (SettingsPage.Desk, SettingsIndex.DeskWatchers, SettingsPage.Watchers, SettingsIndex.Watchers),
                (SettingsPage.Desk, SettingsIndex.DeskLauncher, SettingsPage.Launcher, SettingsIndex.LauncherPlace),
                (SettingsPage.Desk, SettingsIndex.DeskDirect, SettingsPage.Desk, SettingsIndex.DeskDesigns),
                (SettingsPage.DeskMessage, SettingsIndex.MessageLook, SettingsPage.DeskLook, SettingsIndex.DeskSizes),
                (SettingsPage.Alerts, SettingsIndex.SignInReminders, SettingsPage.Desk, SettingsIndex.SignInReminders),
                (SettingsPage.Widgets, SettingsIndex.WidgetsCompanion, SettingsPage.Desk, SettingsIndex.DeskPieces),
            })
            {
                window.SelectPage(from, anchor);
                Settle(window, guard);
                Assert.Equal(new SettingsLink(to, toAnchor), window.Place);
                Assert.True(Marked(window, to, toAnchor).IsVisible, $"{from}#{anchor} landed on {to}#{toAnchor}, hidden");
            }
        }
        finally { window.Close(); }
    });
```

- [ ] **Step 2: Run them to see the card tests fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsDeskSplitIndexTests|FullyQualifiedName~SettingsLinkTests"`
Expected: `A_Desk_card_names_the_exact_place` and `The_tours_Desk_card_says_where_pieces_are_arranged` FAIL; the alias and link tests PASS (they pin what Tasks 4 to 14 built).

- [ ] **Step 3: The bodies**

In `windows-dotnet/src/Sanduhr.Core/WhatsNewCards.cs`, change exactly these words in the card bodies (slice 1 hand-off: "Desk card bodies name the exact section"):

| Card id | Replace | With |
| --- | --- | --- |
| `ci-runners` | `Show my CI runners' jobs, in Settings, Desk, puts a card` | `Show my CI runners' jobs, in Settings, Watchers, puts a card` |
| `companion` | `Show it and let it roam in Settings, Widgets.` | `Show it and let it roam in Settings, Desk, Pieces.` |
| `meter-styles` | `Set them in Settings, Desk.` | `Set them in Settings, Desk, Pieces.` |
| `launcher` | `Add them in Settings, Desk.` | `Add them in Settings, Launcher.` |
| `oled-care` | `Turn it on in Settings, Desk.` | `Turn it on in Settings, Desk, Advanced.` |
| `saved-designs` | `Put my Desk back, in Settings and the tray menu, returns` | `Put my Desk back, in Settings, Desk, Designs and in the tray menu, returns` |
| `watchers` | `Turn it on in Settings, Desk, then place Watchers.` | `Turn it on in Settings, Watchers.` |
| `desk-plate` | `Turn it on in Settings, Desk.` | `Turn it on in Settings, Desk Look, Back plate.` |

(`oled-care` and `desk-plate` share the old sentence: edit each inside its own card.)

In `windows-dotnet/src/Sanduhr.Core/WelcomeTour.cs`, in the `desk` card's body replace `and you arrange each from Settings, Desk.` with `and you arrange each in Settings, Desk, Pieces.` If `DeskOnly` carries the same sentence, change it the same way.

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsDeskSplitIndexTests|FullyQualifiedName~SettingsLinkTests|FullyQualifiedName~WhatsNewTests|FullyQualifiedName~WhatsNewCardsTests|FullyQualifiedName~WelcomeTourTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests"`
Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskSplitPagesTests|FullyQualifiedName~SettingsReachTests"`
Expected: PASS. Slice 2's `An_old_link_and_an_unknown_anchor_land_without_throwing` now takes its "page this build shows" branch for the nine rows above and its "place unchanged" branch for the rows whose targets are later slices' (History, Mods, Claude Code), so both branches run. If a `WelcomeTourTests` or `WhatsNewCardsTests` case pins one of the old sentences, change it to the new words.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/WhatsNewCards.cs windows-dotnet/src/Sanduhr.Core/WelcomeTour.cs windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsLinkTests.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs
git commit -m "feat(settings-v2): Desk cards name the exact place; every slice 3 alias is pinned to its new home"
```

---

### Task 20: End to end: the harness probes the new pages; the Done criteria; the whole suite

**Files:**
- Modify: `windows-dotnet/tools/Sanduhr.RenderHarness/SettingsPassScenario.cs` (`RouteKeyboard`; the menu entries)
- Modify: `windows-dotnet/tests/Sanduhr.App.Tests/NoActivate.cs` (its summary)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `anchors-landing.txt` lines for every new anchor at 800 and 600; the Done criteria as tests.

- [ ] **Step 1: The Done criteria as tests**

Append to `DeskSplitPagesTests`:

```csharp
    /// <summary>Done (spec, slice 3): turning on Weather, Watchers or Now playing from their pages shows the piece, with no visit
    /// to Desk.</summary>
    [Fact]
    public void Turning_on_watchers_now_playing_and_weather_from_their_pages_shows_each_piece() => AppHost.Run(() =>
    {
        var (window, svm, guard) = Open("Personal");
        var visited = new List<SettingsPage>();
        try
        {
            void Visit(SettingsPage page)
            {
                window.SelectPage(page);
                Settle(window, guard);
                visited.Add(window.Place.Page);
            }

            Visit(SettingsPage.Watchers);
            Below<CheckBox>(Marked(window, SettingsPage.Watchers, SettingsIndex.Watchers)).Single().IsChecked = true;
            Visit(SettingsPage.NowPlaying);
            Below<CheckBox>(Marked(window, SettingsPage.NowPlaying, SettingsIndex.NowPlayingBars))
                .Single(c => (string)c.Content == "Bars beside it on the Desk").IsChecked = true;
            Visit(SettingsPage.Weather);
            svm.WeatherPage.Picked = WeatherPlace.Create("Fort Worth", "Texas", "United States", 32.75, -97.33);
            Settle(window, guard);

            var layout = Store().LoadDesk().Layout;
            Assert.Contains("watchers:mr", layout);
            Assert.Contains("nowPlaying:br", layout);
            Assert.Contains("weather:tr", layout);
            Assert.DoesNotContain(SettingsPage.Desk, visited);
            var weatherLine = Below<DeskPieceStatusPanel>((DependencyObject)TabOf(window, SettingsPage.Weather).Content).Single();
            Assert.StartsWith("On the Desk: top right.", ((TextBlock)weatherLine.FindName("StatusText")).Text);
        }
        finally { window.Close(); }
    });

    /// <summary>Done (spec, slice 3): two pieces in one corner reorder by keyboard (WF5). Keyboard focus is routed inside the
    /// never-activating window; Space on the focused Move down button presses it.</summary>
    [DesktopFact]
    public void Two_pieces_in_one_place_reorder_from_the_keyboard() => AppHost.Run(() =>
    {
        var store = Store();
        store.SaveDesk(store.LoadDesk() with { Layout = "clock:bl claude:bl" });
        var (window, _, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Desk, SettingsIndex.DeskPieces);
            Settle(window, guard);
            guard.RouteKeyboard();
            var down = Below<Button>(Marked(window, SettingsPage.Desk, SettingsIndex.DeskPieces)).First(b =>
                System.Windows.Automation.AutomationProperties.GetName(b) == SettingsNames.MoveDownIn(SettingsNames.DeskClock, "Bottom left"));
            Keyboard.Focus(down);
            guard.Check();
            Assert.True(down.IsKeyboardFocused, "keyboard focus could not be routed to Move down");
            var source = PresentationSource.FromVisual(down)!;
            down.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
            down.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
            Settle(window, guard);
            Assert.Equal("claude:bl clock:bl", Store().LoadDesk().Layout);
        }
        finally { window.Close(); }
    });
```

(The third criterion, "Desk reaches Pieces in under 10 Tab stops", is Task 16's `From_the_Desk_pages_first_stop_Tab_reaches_Pieces_in_under_10`.)

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~DeskSplitPagesTests`
Expected: PASS, the `[DesktopFact]`s included. A failure names what it found; fix it, run once more, never in a loop.

- [ ] **Step 2: The harness: no WM_ACTIVATE, and the moved menu entries**

In `windows-dotnet/tools/Sanduhr.RenderHarness/SettingsPassScenario.cs`, replace `RouteKeyboard` (slice 2 final review M5):

```csharp
    static void RouteKeyboard(PassCtx c)
    {
        var hwnd = new WindowInteropHelper(c.Sw).Handle;
        const int WM_ACTIVATE = 0x0006, WM_SETFOCUS = 0x0007;
        SendMessage(hwnd, WM_ACTIVATE, (IntPtr)1, IntPtr.Zero);
        SendMessage(hwnd, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
    }
```

with

```csharp
    /// <summary>Lets WPF route keyboard focus inside the off-screen window: WM_SETFOCUS only. WM_ACTIVATE is left out: in the test
    /// host it made the window the foreground window (measured in 6af89bf), and focus routes without it. NoActivate (App.Tests)
    /// follows the same rule.</summary>
    static void RouteKeyboard(PassCtx c)
    {
        var hwnd = new WindowInteropHelper(c.Sw).Handle;
        const int WM_SETFOCUS = 0x0007;
        SendMessage(hwnd, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
    }
```

In the entries list of `PassLinks`, replace

```csharp
            ("Launcher piece: add buttons / empty state (App.xaml.cs:508)", SettingsPage.Desk, SettingsIndex.DeskLauncher),
```

with

```csharp
            ("Desk right-click on a Launcher button: Launcher settings… (and the missing-app click)", SettingsPage.Launcher,
                SettingsIndex.MenuAnchor(SettingsPage.Launcher)),
```

and point any other entry in that list that names `SettingsIndex.DeskWatchers`, `DeskLook`, `DeskInk`, `DeskPlate`, `DeskDirect`, `MessageLook` or `WidgetsCompanion` on its old page at its new page and anchor (`SettingsIndex.Resolve(oldPage, oldAnchor)` gives both).

In `windows-dotnet/tests/Sanduhr.App.Tests/NoActivate.cs`, replace the class summary's first sentence `The render harness's foreground rule for tests (SettingsPassScenario: NeverActivate, RouteKeyboard, GuardForeground).` with `The foreground rule for tests, the same one the render harness follows (SettingsPassScenario: NeverActivate, RouteKeyboard with WM_SETFOCUS only, GuardForeground).`

- [ ] **Step 3: Build and run the harness's links part off screen, against a scratch profile**

Run: `dotnet build tools/Sanduhr.RenderHarness` (from `windows-dotnet/`)
Expected: Build succeeded, 0 errors, no new warnings.

From the repo root, in PowerShell:

```powershell
$env:SANDUHR_DEV_PROFILE = Join-Path $env:TEMP "sanduhr-slice3-profile"
New-Item -ItemType Directory -Force $env:SANDUHR_DEV_PROFILE | Out-Null
dotnet run --project windows-dotnet/tools/Sanduhr.RenderHarness -- --scenario settings-pass --parts links --out (Join-Path $env:TEMP "sanduhr-slice3-out")
```

Expected: the run ends normally and its `settings-pass: foreground window before X, after X` line shows the same handle twice.

- [ ] **Step 4: Check every new landing**

```powershell
$out = Join-Path $env:TEMP "sanduhr-slice3-out\settings-pass"
Select-String -Path (Join-Path $out "anchors-landing.txt") -Pattern "^(Desk Look|Watchers|Launcher|Desk) / .* \| 800 \|" |
    Where-Object { $_.Line -notmatch "\| (fully in view|top in view)[^|]* \|[^|]*\| ok$" }
Select-String -Path (Join-Path $out "deeplinks.txt") -Pattern "(CARD|TOUR)-LANDING" |
    Where-Object { $_.Line -notmatch "at 800: (fully in view|top in view)[^;]*; ok" }
```

Expected: no output. Every Desk, Desk Look, Watchers and Launcher anchor lands in view at 800 high after its jump (the "after SelectPage" column; `margins`, `desk-oled` and `desk-designs` open their section first), and every What's New and tour card still lands in view. A printed line names the anchor or card and why; fix the landing (most likely a section that didn't open, or a page whose anchor isn't marked) and run Steps 3 and 4 once more. The evidence under `%TEMP%` is never committed.

- [ ] **Step 5: The whole suite**

Run: `dotnet test Sanduhr.slnx` (from `windows-dotnet/`)
Expected: PASS across `Sanduhr.Tests`, `Sanduhr.App.Tests` and `Sanduhr.Mcp.Tests`, every `[DesktopFact]` included (this is a desktop session). `TrustBoundaryTests` and `SettingsKeysContractTests` are green and untouched: `git diff release/4.0 --stat -- windows-dotnet/tests/Sanduhr.Mcp.Tests/TrustBoundaryTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsKeysContractTests.cs windows-dotnet/src/Sanduhr.Mcp/Sanduhr.Mcp.csproj` prints nothing.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/tools/Sanduhr.RenderHarness/SettingsPassScenario.cs windows-dotnet/tests/Sanduhr.App.Tests/NoActivate.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskSplitPagesTests.cs
git commit -m "test(settings-v2): the Desk split's Done criteria; the harness routes keys without WM_ACTIVATE and probes the new pages"
```

- [ ] **Step 7: Hand the Tier 2 checks and the owner's call to the owner**

Not code, and not for the agent: they need the real app on real screens, which this plan never launches. Post them to the owner as the slice's open checks:

- **The default places (owner-confirmable):** Message bottom left, Now playing bottom right, Weather top right, Watchers middle right, Launcher top left, Companion bottom center, System top right. Each is one line in `DeskArrangement.DefaultPlaceFor`.
- **Item 4 (WF5):** with Narrator on, reorder two pieces in one corner without a mouse, from Desk, Pieces (Move up, Move down) and in Arrange Desk (Tab to a piece's Hide button, Shift+Up and Shift+Down, then the arrows); listen to the Desk page from its top to the piece rows, its headings by H, and the jump links.
- **Item 5 (WF41):** a 1366x768 screen at 100% and 1080p at 125% and 150%: the Layout map folds below 600 DIP of window height, the piece rows wrap to two lines below 760 DIP of page width and never clip at 680, and Quit stays on screen.
- **Item 11 (WF17):** each Desk card's Show me at 125% and 150% lands on its new place (Desk Look, Watchers, Launcher, Pieces, Designs and Advanced opened).
- From slice 2's Tier 2 list, still open: the focus rings on screen (now including the section toggles, the jump links and the Order buttons), light and dark.

---

## Hand-offs into this slice, and where each went

Every item of `slice3-handoffs.md`, with its source, and the slice 1 plan's "Slice 3" hand-offs:

| # | Hand-off (source) | Where |
| --- | --- | --- |
| 1 | First live aliases: `Resolve` doesn't fall back to the page row; `PageFromId` reads retired names; the "place unchanged" branch; the near-tautological Check-vs-ParseLink test (slice 2 final review T1, T11) | Task 4 (fallback, `PageFromId` documented and pinned); Tasks 9 and 13 (the Check pins that flip as aliases go live); Task 19 (every slice 3 alias pinned row by row, `A_link_lands_where_the_rule_says` replaces the tautology, both window branches now run) |
| 2 | Pin "font" → Desk Look once the page lands (T19) | Task 11 (`Font_lands_on_Desk_Look_Fonts_first`); the per-slice boost assert becomes `A_boost_ranks_first_only_once_its_section_is_live` (T4), which also covers "docker"/"runner" in Task 12 |
| 3 | Harness `RouteKeyboard` still sends `WM_ACTIVATE`; `NoActivate`'s summary claims the harness rule (M5, T20) | Task 20 Step 2 |
| 4 | Section titles prefix-match (slice 1 review, T1 "Should fix") | Already fixed in `PathProblems` (its "capital after the matched title" rule covers sections, `PathProblems_flags_a_longer_unknown_section_title`); Task 11 re-pins it on the first real multi-word section ("Ink and color", "Ink", "Ink and colors", "Back plate Extra") |
| 5 | The radio-group scan is lazy and DeskMessagePage-only (slice 1 review 14, T8 "Should fix") | Nesting was fixed in slice 2 (`RadioGroups`, `The_radio_group_scan_reads_nested_and_self_closing_panels`); Task 5 runs it over every Settings page and panel file |
| 6 | Split literals escape the scans; `ToolCatalog.cs:686` (slice 1 review) | Task 5 (`No_literal_ends_inside_a_Settings_path`, the literal joined); Task 12 ("Settings, Watchers", WF18) |
| 7 | The Desk page defines the Desk twice in a row (slice 1 review 7) | Task 14 (decided: both strings are the spec's and stay); Task 16 (the jump links sit between them) |
| 8 | Quit question and labels inline in `SettingsViewModel` (T12) | Task 3 |
| 9 | Desk wrapper not re-indented; rows wrap below 760 (T18) | Task 14 (rewritten, indented); Task 8 (rows wrap) |
| 10 | Default places are proposals for the owner | Task 1 (one table, marked), Global Constraints, Task 20 Step 7 |
| 11 | Focus rings on own-file pages (P25) | Task 5 (scan); new pages use the window's styles through `DynamicResource`; Task 13 converts the moved Launcher block's `StaticResource` styles |
| 12 | Page ScrollViewers `Focusable="False"` (P5) | Task 5 (scan); the page skeletons (Task 6) |
| 13 | `[DesktopFact]` tests run locally and must pass; `NoActivate.Show` + `Check()`; never touch `Sanduhr.exe` | Global Constraints; Tasks 8, 14, 16, 20 run them |
| S1 | "Edit buttons…" → "Launcher settings…" (slice 1 plan) | Task 13 |
| S1 | Watchers MCP text and `ToolCatalog.cs:686` → "Settings, Watchers" | Task 12 |
| S1 | The duplicate spoken "Sign-in reminders" heading goes | Task 14 |
| S1 | `DefineWatchers`, `DefineCompanion` become first lines | Task 6 (Watchers), Task 9 (Companion row) |
| S1 | Desk card bodies name the exact section | Task 19 |
| T3 | `MenuAnchor` null case pinned only for Themes; add the Launcher | Task 13 |
| P17 | "Claude's suggestion" hit lands on the page entry while no suggestion waits | Carried (below) |

## Carried, not in this slice

- **The Desktop group's place in the sidebar** (spec, Sidebar: after Usage). Moving it means moving every Desktop TabItem block above Claude Code, which collides with slices 4 and 5 in parallel. Slice 8, when nothing else edits those blocks.
- **The companion catalog filtered to sprites, and "Get one in Mods & Widgets…".** Filters arrive with Mods & Widgets in slice 4; until then the row links to `widgets#widgets-catalog` (an alias to `mods#catalog` already) and `GetACompanion` reads the catalog page's title. Slice 4 points `GetOneLink` and `GetACompanion` at the Mods page.
- **Jump links on General, Alerts, Claude Usage, Publish usage, Claude Code, Mods & Widgets, Themes and About.** Each gains them in the slice that rebuilds it (4, 5, 6, 8); `SettingsIndex.JumpLinks` is the one table they join.
- **P17, the suggestion hit while no suggestion waits.** The section is hidden, not collapsed, so "(in …)" doesn't apply; landing on the page is honest. Slice 8 (polish).
- **A Desk Look preview** (spec, "Not in 4.0").
- **The 4.0 What's New cards** ("Settings, rearranged" and streamer mode): slice 7.
- **Alerts' `each-limit` and its `MenuAnchor`:** slice 5.
- **The Tier 2 checks** (Task 20 Step 7): the owner, on real screens.

---

## Spec coverage (slice 3)

| Slice 3 bullet or Done item (spec) | Task |
| --- | --- |
| New pages Desk Look, Watchers and Launcher: enum values, sidebar, names, index | 6, 11, 12, 13 |
| The Desk page in its new order with the map on top (sections 1 to 7) | 14 |
| Pieces rows with Show, Order and Settings… (names in `SettingsNames`; spoken "Move <piece> up in <place>") | 7, 8 |
| Rows wrap to two lines below 760 DIP; nothing clips at 680 (WF26) | 8, 14 |
| `DeskArrangement.DefaultPlaceFor` and auto-place from Weather, Now playing, Message, Watchers and Launcher; the three call sites move to the table | 1, 7, 10, 12, 13, 18 |
| Feature pages show the piece's status or "Place it on the Desk" (Principle 1; Message, Now playing, Weather, Watchers, Launcher, Claude Code glow) | 7, 10, 12, 13, 18 |
| The Companion row (sprite, Let it roam, "No companion yet…"; "Show the companion on the Desk" retired) | 9 |
| Message Look moved (Desk Look: Desk font, Message font, Sizes, Message color, Glow) | 11 |
| Sign-in reminders moved to Desk, Behavior (WF32) | 14 |
| Palette presets shown without Custom (WF3, WF12) | 11 |
| Arrange arrow keys; its bar hint names them; Arrange's caption "Or use the Order buttons below." | 2, 17 |
| Heading stops and jump links | 16 |
| Map folding below 600 DIP (Short windows) | 15 |
| Designs collapsed with its summary and "Let Claude change the Desk directly"; Advanced collapsed with Margins and OLED care; "(in Advanced)" search rows live | 14 |
| Search: `font` → Desk Look, Fonts; `docker`, `runner` → Watchers, CI runners | 11, 12 |
| Watchers: "Let Claude show watchers", "Show my CI runners' jobs" with "Independent of the switch above.", Where they show; agent text "Settings, Watchers" (WF18) | 12 |
| Launcher: moved as is; "Launcher settings…" and the missing-app click land on `launcher#launcher-buttons` (WF17, WF19) | 13 |
| Weather: the "Place Weather on the Desk page." prose goes; Now playing likewise | 10 |
| Aliases for the moved anchors resolve (Deep links) | 4, 9, 11, 12, 13, 14, 19 |
| Tests named by the spec: `DeskArrangementTests` (default places, order moves), `DeskSettingsTests` (Show writes Hidden or the default: the App-side `DeskPiecesSettingsTests`, since the view model is App's), `DeskArrangeTests` (arrow keys), `SettingsIndexTests` (the new rows: `SettingsDeskSplitIndexTests`) | 1, 2, 7, 11 to 14, 16 |
| Done: turning on Weather, Watchers or Now playing from their pages shows the piece with no visit to Desk | 10, 12, 20 |
| Done: two pieces in one corner reorder by keyboard | 7, 17, 20 |
| Done: Desk reaches Pieces in under 10 Tab stops | 16 |
| Tier 2 items 4, 5 and 11 | 20 Step 7 |
| No settings key renamed, removed or added; no MCP link list change | Global Constraints, 20 Step 5 |
