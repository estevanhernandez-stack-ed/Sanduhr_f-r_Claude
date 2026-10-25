# One theme for the widget and the Desk ("theme match") Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A theme styles the widget and the Desk as a set. A theme file may carry `font` and a `desk` block; `ThemeDeskLook.From(theme)` turns any theme into the Desk's whole look; meters drawn with a gradient carry the usage signal as a middle stop at Warn at and Urgent at; Desk Look gains "Match the widget theme" (Follow the widget theme, Match now, and the theme's Desk look editor) so a matched look always lives in a theme; designs, undo, `get_desk`, `propose_theme` and the Theme Studio carry it.

**Architecture:** Pure Core first: `ThemeDefinition` gains `Font` and `Desk` (`ThemeDesk`), `ThemeLint` checks them, `ThemeDraft` (the canonical serializer behind designs and `get_desk`) carries them; a new pure `ThemeDeskLook.cs` holds the rules and the readability rule; `MeterSignal` (in `DeskMeters.cs`) works the signal into a gradient. The widget then draws `desk.bars` with the signal (ramp otherwise) and the theme's font. The Desk draws a theme look through new `DeskFrame` fields, resolved by an App `DeskLookResolver` from one new key, `desk_follows_theme`. Core `ThemeMatch` (names, keys, the spelled-out block, Match now's copy) and the App `ThemeMatchService` (Follow on/off, Match now, hand edits, save-first undo) drive Desk Look's new first section. Designs gain `follows_theme`; the MCP server links `ThemeDeskLook.cs` for `get_desk`'s `theme_look` and for judging followed designs. The Theme Studio reuses the Desk look editor. Last, a harness scenario renders every built-in matched.

**Tech Stack:** .NET 10, WPF, xUnit 2.9, CommunityToolkit.Mvvm 8.4, the render harness (`tools/Sanduhr.RenderHarness`), the MCP server (`src/Sanduhr.Mcp`, source-linked Core slice).

**Spec:** `docs/superpowers/specs/2026-10-25-theme-match-design.md` (approved 2026-10-25; dashboard decision `zIsGHT5TiD5lbEwP4AiQ`). Read it before starting. `docs/superpowers/plans/2026-10-24-settings-v2-slice-3.md` is the model for conventions and helpers (`AppWindowFixture`, `ScratchWidget`, `NoActivate`, `AppHost`, `Sta`, `SettingsAnchor.Find`, the Desk Look page).

All paths below are under `windows-dotnet/` unless they start with `docs/`. Run every `dotnet` command from `windows-dotnet/`.

## Spec questions decided in this plan

1. **The shadow shade and the plate tint (spec, "Open for the plan" 1).** Both are a fixed blend of the theme's `bg`, in sRGB bytes, each channel `round(c + (target - c) * t)` rounding half away from zero (`ThemeDeskLook.Mix`). **Shadow:** `bg` toward black at `t = 0.5` (toward white behind dark ink, as the Desk's shadow flips today): `ShadowBlend = 0.5`. **Plate tint:** `bg` toward the plate's own color today (`DeskPlate.ColorHex(darkInk)`: black behind light ink, white behind dark ink) at `t = 0.25`: `PlateBlend = 0.25`, drawn at `DeskPlate.Opacity` (0.30), so a plate reads as today's plate tinted by the theme. Pinned per built-in in Task 3: shadow / plate are Obsidian `070707` / `0a0a0a`, Aurora `05080d` / `080b14`, Ember `0d0505` / `140808`, Mint `050d0a` / `08140f`, Matrix `010501` / `020802`, Blueprint `0d0b13` / `14111c`; a light theme (`bg #f4f4f2`, dark ink) gives `fafaf9` / `f7f7f5`.
2. **Where Follow and Match now sit (spec, "Open for the plan" 2).** A new **first** section of Desk Look, **"Match the widget theme"**, right after the title and the jump links: anchor `theme-match` (`SettingsIndex.DeskThemeMatch`), its own index row, the first jump link, names in `SettingsNames`. It holds the Follow switch, Match now, a live status line, the plate note, and (while following) "The theme's Desk look" editor. Fonts, Sizes, Ink and color, Back plate follow it unchanged.
3. **The readability guard judges the ink's first stop.** A followed ink reads when its first stop (the theme's `text` unless `desk.ink` is set: the color the clock's digits start in) keeps `DeskDesign.MinContrast` (3:1) against the wallpaper sample behind each stack. Judging every stop would turn the plate on for every built-in (Obsidian's `#6c63ff` reads at 2.4:1 on a dark wallpaper) without making anything more readable. The design validator uses the same rule for followed designs, so the server and the widget agree.
4. **Where the signal stop goes.** Into the user's stops at index `ceil(n / 2)` (a solid color is first doubled), all stops spread evenly: exactly the middle for one, two and four stops; two thirds along for three. The colors are the widget ramp's own yellow `#facc15` and red `#f87171`.
5. **The percent text.** Past Warn at it is yellow, past Urgent at red; under Warn at it keeps today's color (the Desk: the ink; the widget with `desk.bars`: the theme's `text`; the widget without: the ramp, unchanged).
6. **Only the Claude limits carry the signal.** The `meters` piece and the `claude` piece in a gauge style. Telemetry (CPU, RAM, disk) is no usage and keeps no signal; it does take the theme's bar colors, track and tick while the Desk follows.
7. **Match now and Follow off copy what the Desk's own settings can hold.** Ink stops (Custom), shadow on, font, plate on or off, message color stops, message glow on, gradient bars on. The plate tint, the shadow shade and the glow color have no Desk setting, and this feature adds exactly one key, so after Match now they draw as today (black or white plate and shadow, the glow from the message's middle stop). Follow keeps them; the status line after Match now says the look is the Desk's own.
8. **Follow writes a spelled-out `desk` block** (ink, message, glow, plate, plate_color, shadow) into the theme it makes or updates. `bars` and `font` are written only when the theme already set them: writing `bars` would turn the widget's ramp into a gradient, which Follow must never do.
9. **Unknown `desk` fields are warnings, not errors.** A theme written by a newer Sanduhr still loads (the field is ignored and named). Shape errors in known fields are errors.
10. **"A known family or a font pack" is a warning the App adds.** `ThemeLint.Lint(..., fontAvailable)` warns about a family this PC lacks only when the caller passes the check (the paste box, the Studio, a Claude proposal on the widget). The MCP server lints the name's shape (`DeskDesign.FontFamilyProblem`): it cannot know the PC's fonts.
11. **While following, Desk Look's own look controls are disabled** (Desk font, Ink and color, Back plate) with one line saying the theme sets them; edits go through "The theme's Desk look" editor, each committed edit asking Update / Save as new / Cancel. Message font and the sizes stay the Desk's own. Now playing's bars and position bar wear the theme gradient while following (no field of their own); the Now playing page's Gradient bars switch keeps its value for when Follow goes off. That is the spec's "Now playing's bars" hand edit: it is not a Desk Look control, so it is not offered there.
12. **"Make a new theme" on Follow** (a user theme active) uses the automatic name "<Theme> + Desk"; only the hand edit's "Save as a new theme" asks for a name (spec names the person only there).
13. **Put my Desk back after "Add the Desk look to <theme>"** restores the look and switches the widget to the theme as it was; because `DesignTheme.Plan` never overwrites a theme with other colors, the old theme comes back under the next free key (`dusk-2`) and the edited `dusk` stays. Accepted; the dashboard log records it.
14. **The Theme Studio's Desk preview** reaches the Desk through `WidgetViewModel.ThemeChanged` (the controller keeps the definition it was handed), so it shows on the Desk while the Desk follows (or uses Theme ink); otherwise the Desk keeps its own look and a line under the Studio's Desk section says so.
15. **`get_desk`'s `theme_look`** is computed in the server from the state's `design.theme` with the linked `ThemeDeskLook.cs`; `follows_theme` rides inside `design`.
16. **The three-way prompt** is `ThemedDialog.ShowChoice` (a third button, Cancel), so the prompts stay themed like every other dialog.

## Global Constraints

- **CRLF working tree, LF index.** Edit tracked files with the Edit tool only; never `sed -i`; a Python rewrite must be UTF-8 and newline-preserving. New files may be created with Write.
- **Hands off the live app.** Never start, stop, kill or attach to `Sanduhr.exe`. If a build fails because a `Sanduhr.exe` holds a file, stop and ask the owner.
- **Hands off the foreground.** Every App test that shows a window shows it through `NoActivate.Show(window)` and calls `guard.Check()` after anything that moves focus. `[DesktopFact]` tests run locally, once per change; **never re-run a UI test in a loop**: a failure is diagnosed from its message, fixed, and run once more.
- **Scratch data only.** No test reads the real AppData, LocalAppData, `~\.sanduhr` or Credential Manager: App tests derive from `AppWindowFixture` and build widgets with `ScratchWidget(...)` over `ScratchPaths()`, or build stores over `new Paths(appDataBase: …, localAppDataBase: …)`. Never `DemoWidget()` in a new test.
- **RED is required evidence.** Each task's tests are written first and run to a failing build or failing assertion before the implementation; the failing output is kept in the task's report.
- **Run tests from `windows-dotnet/`.** `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~<Class>` (Core), `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~<Class>` (App), `dotnet test tests/Sanduhr.Mcp.Tests` (server).
- **Exactly one new settings key: `desk_follows_theme`** (bool, absent = off). `SettingsKeysContractTests` gains it in the task that adds it (Task 8) and changes nowhere else. No key is renamed or removed; `desk_ink.mode: theme` keeps meaning the theme's `text` color.
- **The trust boundary widens once, on purpose.** `ThemeModel.cs` and `ThemeLint.cs` (already linked) gain the new fields and checks; `ThemeDeskLook.cs` joins the `Sanduhr.Mcp.csproj` link list in Task 15 and must stay pure (no `File.`, `Directory.`, `Environment.`, network, process or credential names). `TrustBoundaryTests` changes only to admit it. The already-linked `DeskDesign.cs`, `DeskDesignValidator.cs` and `PackValidator.cs` gain pure code for `follows_theme` and the theme fields (the spec's own requirements) and nothing that names IO. `ThemeDraft.cs`, `ThemeMatch.cs` and `DeskMeters.cs` are not linked and must not become linked.
- **Nothing changes at upgrade.** Every built-in and every existing theme file renders the widget unchanged (no `desk.bars`: the ramp); a Desk that does not follow draws exactly its own settings; a design saved before this feature is the same look as one with `follows_theme: false`.
- **Names.** Every new visible or spoken string is a `SettingsNames` member or built from one; prose that names a place uses "Settings, <Page>, <Section>" and passes `SettingsNames.PathProblems`. An anchor joins `SettingsIndex.Anchors` in the same commit that marks it in XAML.
- **Own-file pages and controls** use the window's styles through `DynamicResource`, never a local style with `FocusVisualStyle="{x:Null}"`, and a page's `ScrollViewer` stays `Focusable="False"`; radio groups are named. No "Settings, " split across literals.
- **Branch.** Tasks run in numeric order on `feat/theme-match` (cut from `release/4.0` at `999baf1`, spec at `03d41ae`). Each task ends in one commit; commit messages end with the session's attribution line.

## Review Focus

1. **A theme without `desk.bars` leaves the widget exactly as it was.** Every built-in, every existing user or pack theme, and a theme whose `desk` block sets other fields: the card's bars, the percent chip and its color are pixel-identical. Pinned in Task 5 (`A_desk_block_without_bars_leaves_the_card_pixel_identical`, `A_theme_without_desk_bars_keeps_the_ramp`).
2. **A write into a theme keeps every other field of it.** "Add the Desk look to <theme>" and "Update <theme>" rewrite the file through `ThemeDraft`; `border_tint`, `inner_highlight`, `accent_bloom`, `monospace_font`, `card_corner_radius`, `font` and the dials survive. Pinned in Task 9 (`WithDesk_keeps_every_other_field_of_the_theme`) and Task 11 (`Add_writes_the_desk_into_the_user_theme_keeping_every_other_field`).
3. **Cancel anywhere writes nothing.** Follow's add-or-new prompt cancelled leaves Follow off, no theme file, no saved design; a hand edit cancelled leaves the theme file byte for byte and the editor shows the theme's values again; an empty name saves nothing. Pinned in Task 11 (`Cancel_writes_nothing_and_Follow_stays_off`) and Task 12 (`Cancel_leaves_the_theme_as_it_was`, `A_name_left_empty_saves_nothing`), Task 14 (`A_cancelled_edit_puts_the_editor_back`).
4. **Every theme change restyles a following Desk.** The strip, Settings, a Claude proposal, Put my Desk back and a Studio preview all raise `ThemeChanged`; the Desk redraws from the definition it is handed, and a theme with no `desk` block still gets the rules' look. Pinned in Task 8 (`Following_restyles_on_every_theme_change`, `A_studio_preview_reaches_a_following_desk`).
5. **Names and keys never collide.** "<Theme> + Desk" taken goes to " 2", " 3"; a 24-character name is cut so the suffix fits the strip; "Obsidian + Desk" followed again does not stack the suffix; the file key never lands on a built-in's key or an existing file. Pinned in Task 9 (`The_new_themes_name_is_the_theme_plus_Desk_made_unique`, `A_long_name_is_cut_so_the_suffix_fits_the_strip`, `The_file_key_never_lands_on_a_built_in_or_a_file_that_is_there`).

## Where this plan steps outside the spec's file mentions

- New Core files: `ThemeDeskLook.cs` (linked), `ThemeMatch.cs` (not linked). `MeterSignal` lives in `DeskMeters.cs` (not linked) beside the rows it replaces `WarningAt` in.
- New App files: `Desk/DeskLookResolver.cs`, `Services/ThemeMatchService.cs`, `Views/ThemeMatchPrompts.cs`, `ViewModels/ThemeMatchViewModel.cs`, `ViewModels/ThemeDeskEditorViewModel.cs`, `Views/ThemeDeskEditor.xaml(.cs)`.
- Touched beyond the spec's list: `Controls/UsageBar.cs` (a brush fill), `Views/TierCard.xaml`, `MainWindow.xaml` (the font resource), `Desk/DeskFonts.cs` (`Has`), `Desk/DeskMessagePiece.cs` (the glow color), `Views/ThemedDialog.xaml(.cs)` (a third button), `ViewModels/DeskContentPagesViewModels.cs` (`ReloadSettings`), `Core/PackValidator.cs` (the catalog lets `font` and `desk` through), `Core/ThemeProposals.cs` and `Services/ThemeHandoffService.cs` (the font check), `ViewModels/ThemesViewModel.cs`, `Sanduhr.Mcp/ToolLogic.Desk.cs`, `Sanduhr.Mcp/ToolCatalog.cs`.
- Harness: `tools/Sanduhr.RenderHarness/ThemeMatchScenario.cs` and `Program.cs` (one scenario).

## File Structure

| File | Responsibility |
|---|---|
| `src/Sanduhr.Core/ThemeModel.cs` (modify) | `ThemeDesk` (the `desk` block, list-wise equality, `Read`, `ToJson`); `ThemeDefinition.Font`, `.Desk`; `BuildDefinition` reads them. |
| `src/Sanduhr.Core/ThemeLint.cs` (modify) | `font` and `desk` schema errors, unknown-field and contrast warnings, the optional font check; `MaxDeskStops`, `DeskContrastMin`. |
| `src/Sanduhr.Core/ThemeDraft.cs` (modify) | `Font`, `Desk` carried by `FromDefinition` and `ToJson`; `Lint(fontAvailable)`. |
| `src/Sanduhr.Core/ThemeDeskLook.cs` (create, linked in Task 15) | `ThemeDeskLook` record: `From`, `ReadsOver`, `Guarded`, `Mix`, `ToJson`. |
| `src/Sanduhr.Core/DeskMeters.cs` (modify) | `MeterLevel`, `MeterSignal`; `DeskMeterRow.Level` replaces `Warning`; `Rows` takes the thresholds; `WarningAt` goes. |
| `src/Sanduhr.Core/ThemeMatch.cs` (create) | `ThemeOrigin`, `ThemeMatchOwn`; `Origin`, `NewThemeName`, `NewThemeKey`, `CleanName`, `WithDesk`, `SpelledOut`, `OwnSettings`. |
| `src/Sanduhr.Core/DeskDesign.cs`, `DeskDesignLibrary.cs`, `DeskDesignApply.cs`, `DeskState.cs` (modify) | `follows_theme` in designs, their canonical form, the apply and the state. |
| `src/Sanduhr.Core/DeskDesignValidator.cs` (modify) | A followed design is judged in the theme's look, with the guard. |
| `src/Sanduhr.Core/PackValidator.cs`, `ThemeProposals.cs`, `SettingsIndex.cs`, `SettingsNames.cs` (modify) | Catalog keys; the font check; Desk Look's new section, row, jump link and names. |
| `src/Sanduhr.App/Controls/UsageBar.cs`, `Views/TierCard.xaml`, `ViewModels/TierCardViewModel.cs`, `ViewModels/WidgetViewModel.cs` (modify) | The widget's bars from `desk.bars` with the signal; thresholds; `FontAvailable`. |
| `src/Sanduhr.App/Theming/ThemePalette.cs`, `MainWindow.xaml`, `Desk/DeskFonts.cs`, `App.xaml.cs` (modify) | The widget's font; wiring. |
| `src/Sanduhr.App/Desk/DeskView.cs`, `DeskView.Styles.cs`, `DeskMessagePiece.cs` (modify) | The signal on meters; the frame's theme fields (plate tint, glow, Now playing stops, meter bars, track, tick). |
| `src/Sanduhr.App/Desk/DeskLookResolver.cs` (create) | `DeskThemeDraw`; `For`, `Settings`, `Frame`. |
| `src/Sanduhr.App/Desk/DeskController.cs`, `DeskDesignService.cs` (modify) | Follow read, `ThemeChanged(ThemeDefinition)`, the guard, `PlateForced`; `SaveBeforeChange`, `follows_theme` in designs. |
| `src/Sanduhr.App/Services/SettingsStore.cs` (modify) | `LoadDeskFollowsTheme`, `SaveDeskFollowsTheme`. |
| `src/Sanduhr.App/Services/ThemeMatchService.cs`, `Views/ThemeMatchPrompts.cs` (create) | Follow on and off, Match now, hand edits; the default prompts. |
| `src/Sanduhr.App/Views/ThemedDialog.xaml(.cs)` (modify) | `ShowChoice`, the third button. |
| `src/Sanduhr.App/ViewModels/ThemeMatchViewModel.cs`, `ThemeDeskEditorViewModel.cs`, `Views/ThemeDeskEditor.xaml(.cs)` (create) | Desk Look's section; the shared editor. |
| `src/Sanduhr.App/Views/DeskLookPage.xaml`, `ViewModels/SettingsViewModel.cs`, `ViewModels/DeskContentPagesViewModels.cs` (modify) | The section; own controls disabled while following; refreshes. |
| `src/Sanduhr.App/ViewModels/ThemeStudioViewModel.cs`, `ThemesViewModel.cs`, `Views/SettingsWindow.xaml` (modify) | The Studio's Font picker and Desk section. |
| `src/Sanduhr.Mcp/Sanduhr.Mcp.csproj`, `ToolLogic.Desk.cs`, `ToolCatalog.cs` (modify) | The link; `theme_look`; schema text. |
| `tools/Sanduhr.RenderHarness/ThemeMatchScenario.cs` (create), `Program.cs` (modify) | Every built-in matched, warn and urgent, dark and light. |
| Tests | `tests/Sanduhr.Tests/ThemeDeskLookTests.cs`, `MeterSignalTests.cs`, `ThemeMatchTests.cs` (create); additions to `ThemeLintTests`, `ThemeModelTests`, `ThemeDraftTests`, `PackValidatorTests`, `ThemeProposalsTests`, `DeskMetersTests`, `DeskDesignTests`, `DeskDesignValidatorTests`, `SettingsDeskSplitIndexTests`, `SettingsKeysContractTests`; `tests/Sanduhr.App.Tests/WidgetBarsTests.cs`, `ThemeFontTests.cs`, `DeskThemeLookViewTests.cs`, `DeskFollowThemeTests.cs`, `ThemeMatchServiceTests.cs`, `ThemeMatchSettingsTests.cs`, `ThemeDeskEditorTests.cs`, `ThemeStudioDeskTests.cs` (create); additions to `DeskMeterStylesViewTests`, `DeskDesignServiceTests`; `tests/Sanduhr.Mcp.Tests/TrustBoundaryTests.cs`, `ToolLogicDeskDesignTests.cs`, `ToolLogicThemeTests.cs`. |

---

### Task 1: A theme file may carry `font` and `desk`; the lint checks them

**Files:**
- Modify: `src/Sanduhr.Core/ThemeModel.cs` (after `InnerHighlight` ~line 14; `ThemeDefinition` ~52-59; `BuildDefinition` ~332-360)
- Modify: `src/Sanduhr.Core/ThemeLint.cs` (constants ~41-50; `Lint` overloads ~67-83; schema ~145; warnings ~196-199; helpers at the end)
- Modify: `src/Sanduhr.Core/PackValidator.cs` (`ThemeOptionalKeys` ~105-109)
- Test: `tests/Sanduhr.Tests/ThemeLintTests.cs`, `tests/Sanduhr.Tests/ThemeModelTests.cs`, `tests/Sanduhr.Tests/PackValidatorTests.cs`

**Interfaces:**
- Produces: `public sealed record ThemeDesk { IReadOnlyList<string>? Ink, Bars, Message; string? Glow; bool? Plate; string? PlateColor, Shadow, Font; static IReadOnlyList<string> Fields; bool IsEmpty; JsonObject ToJson(); internal static ThemeDesk? Read(JsonNode?) }` (stops as the file has them, `#rrggbb`; equality compares lists element-wise); `ThemeDefinition.Font` (`string?`), `ThemeDefinition.Desk` (`ThemeDesk?`); `ThemeLint.Lint(string json, Func<string, bool>? fontAvailable = null)`, `ThemeLint.Lint(JsonObject data, Func<string, bool>? fontAvailable = null)`; `ThemeLint.MaxDeskStops = 4`, `ThemeLint.DeskContrastMin = DeskDesign.MinContrast`. Findings use the fields `font`, `desk`, `desk.ink`, `desk.bars`, `desk.message`, `desk.glow`, `desk.plate`, `desk.plate_color`, `desk.shadow`, `desk.font`, `desk.<unknown>`.

- [ ] **Step 1: Write the failing tests**

Append to the class in `tests/Sanduhr.Tests/ThemeLintTests.cs`:

```csharp
    // ---- theme match: font and desk (one theme for the widget and the Desk) ----

    private static JsonObject WithDesk(string deskJson)
    {
        var t = Clean();
        t["desk"] = JsonNode.Parse(deskJson);
        return t;
    }

    [Fact]
    public void Font_and_a_full_desk_block_lint_clean_and_build()
    {
        var t = WithDesk("""
            {"ink": ["#e8e4dc", "#6c63ff"], "bars": ["#6c63ff", "#e8e4dc", "#ff6b6b"], "message": ["#6c63ff"],
             "glow": "#6c63ff", "plate": false, "plate_color": "#0a0a0a", "shadow": "#070707", "font": "Consolas"}
            """);
        t["font"] = "Segoe UI";
        var r = ThemeLint.Lint(t);
        Assert.True(r.Ok);
        Assert.Empty(r.Findings);
        Assert.Equal("Segoe UI", r.Theme!.Font);
        var d = r.Theme.Desk!;
        Assert.Equal(["#e8e4dc", "#6c63ff"], d.Ink);
        Assert.Equal(3, d.Bars!.Count);
        Assert.Equal(["#6c63ff"], d.Message);
        Assert.Equal("#6c63ff", d.Glow);
        Assert.False(d.Plate);
        Assert.Equal("#0a0a0a", d.PlateColor);
        Assert.Equal("#070707", d.Shadow);
        Assert.Equal("Consolas", d.Font);
    }

    [Fact]
    public void A_theme_without_font_or_desk_builds_with_neither()
    {
        var r = ThemeLint.Lint(Clean());
        Assert.Null(r.Theme!.Font);
        Assert.Null(r.Theme.Desk);
        Assert.Null(ThemeLint.Lint(WithDesk("{}")).Theme!.Desk);   // an empty block is no block
    }

    [Theory]
    [InlineData("""{"ink": []}""", "desk.ink")]
    [InlineData("""{"ink": ["#111111", "#222222", "#333333", "#444444", "#555555"]}""", "desk.ink")]
    [InlineData("""{"bars": ["red"]}""", "desk.bars")]
    [InlineData("""{"message": "#ffffff"}""", "desk.message")]
    [InlineData("""{"glow": "#fff"}""", "desk.glow")]
    [InlineData("""{"plate_color": 3}""", "desk.plate_color")]
    [InlineData("""{"shadow": null}""", "desk.shadow")]
    [InlineData("""{"plate": "yes"}""", "desk.plate")]
    [InlineData("""{"font": "C:\\Windows\\Fonts\\x.ttf"}""", "desk.font")]
    [InlineData("""[1]""", "desk")]
    public void A_bad_desk_field_is_an_error_naming_it(string desk, string field)
    {
        var r = ThemeLint.Lint(WithDesk(desk));
        Assert.False(r.Ok);
        Assert.Contains(field, Fields(r, ThemeFindingLevel.Error));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Fira Code, Consolas")]
    [InlineData("https://example.com/font")]
    public void A_font_that_is_not_a_family_name_is_an_error(string font)
    {
        var t = Clean();
        t["font"] = font;
        Assert.Contains("font", Fields(ThemeLint.Lint(t), ThemeFindingLevel.Error));
    }

    [Fact]
    public void An_unknown_desk_field_is_a_warning_and_the_theme_still_loads()
    {
        var r = ThemeLint.Lint(WithDesk("""{"ink": ["#e8e4dc"], "sparkle": true}"""));
        Assert.True(r.Ok);
        Assert.Contains("desk.sparkle", Fields(r, ThemeFindingLevel.Warning));
        Assert.Equal(["#e8e4dc"], r.Theme!.Desk!.Ink);
    }

    [Fact]
    public void Desk_ink_and_message_stops_that_do_not_read_on_the_background_are_warnings()
    {
        var r = ThemeLint.Lint(WithDesk("""{"ink": ["#e8e4dc", "#222222"], "message": ["#1a1a1a"], "bars": ["#111111"]}"""));
        Assert.True(r.Ok);
        var warned = Fields(r, ThemeFindingLevel.Warning).ToList();
        Assert.Contains("desk.ink", warned);
        Assert.Contains("desk.message", warned);
        Assert.DoesNotContain("desk.bars", warned);   // bars sit on bar_bg, not on the wallpaper: no text contrast rule
        Assert.Contains(r.Findings, f => f.Field == "desk.ink" && f.Message.Contains("#222222"));
    }

    [Fact]
    public void A_font_this_pc_lacks_is_a_warning_only_when_the_caller_knows_the_fonts()
    {
        var t = Clean();
        t["font"] = "Nope Sans";
        t["desk"] = JsonNode.Parse("""{"font": "Also Missing"}""");
        Assert.Empty(ThemeLint.Lint(t).Findings);   // the MCP server's lint: the name's shape only
        var r = ThemeLint.Lint(t, f => f == "Consolas");
        Assert.Contains("font", Fields(r, ThemeFindingLevel.Warning));
        Assert.Contains("desk.font", Fields(r, ThemeFindingLevel.Warning));
        t["font"] = "EsteFont Pro";   // bundled: always there
        Assert.DoesNotContain("font", Fields(ThemeLint.Lint(t, _ => false), ThemeFindingLevel.Warning));
    }
```

Append to the class in `tests/Sanduhr.Tests/ThemeModelTests.cs`:

```csharp
    [Fact]
    public void A_user_theme_file_with_font_and_desk_loads_them()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sanduhr-theme-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(dir);
            var o = ThemeDraft.FromDefinition("obsidian", ThemeCatalog.BuiltIns["obsidian"]).ToJson();
            o["name"] = "Dusk";
            o["font"] = "Consolas";
            o["desk"] = JsonNode.Parse("""{"bars": ["#6c63ff", "#e8e4dc"]}""");
            File.WriteAllText(Path.Combine(dir, "dusk.json"), o.ToJsonString());
            var dusk = ThemeCatalog.LoadUserThemes(dir)["dusk"];
            Assert.Equal("Consolas", dusk.Font);
            Assert.Equal(["#6c63ff", "#e8e4dc"], dusk.Desk!.Bars);
            Assert.Null(ThemeCatalog.BuiltIns["obsidian"].Desk);   // no built-in sets one
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
```

Append to the class in `tests/Sanduhr.Tests/PackValidatorTests.cs`:

```csharp
    [Fact]
    public void Theme_font_and_desk_are_documented_fields()
    {
        var p = Good("good-theme");
        EditJson(p, "themes/fixture-dark.json", t =>
        {
            t["font"] = "EsteFont Pro";
            t["desk"] = JsonNode.Parse("""{"ink": ["#ffffff"], "plate": true}""");
        });
        Assert.Empty(Errors(p, "good-theme"));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~ThemeLintTests|FullyQualifiedName~ThemeModelTests|FullyQualifiedName~PackValidatorTests"`
Expected: build FAILS with `'ThemeDefinition' does not contain a definition for 'Font'` and `No overload for method 'Lint' takes 2 arguments`.

- [ ] **Step 3: The model**

In `src/Sanduhr.Core/ThemeModel.cs`, after the `InnerHighlight` record (`public sealed record InnerHighlight(string Color, double Alpha);`), add:

```csharp

/// <summary>
/// The Desk part of a theme (theme match, the theme JSON's <c>desk</c> object). Every field is optional; null leaves that part to
/// ThemeDeskLook's rules. Stops and colors are <c>#rrggbb</c> as the file has them (<see cref="ThemeLint"/> accepted them).
/// Equality compares the stop lists element by element, so a definition read twice equals itself.
/// </summary>
public sealed record ThemeDesk
{
    /// <summary>1 to 4 stops for the clock, the date and the Claude line.</summary>
    public IReadOnlyList<string>? Ink { get; init; }
    /// <summary>1 to 4 stops for the meter bars, on the widget and the Desk.</summary>
    public IReadOnlyList<string>? Bars { get; init; }
    /// <summary>1 to 4 stops for the Message line.</summary>
    public IReadOnlyList<string>? Message { get; init; }
    /// <summary>The message glow's color.</summary>
    public string? Glow { get; init; }
    /// <summary>Whether the back plate shows.</summary>
    public bool? Plate { get; init; }
    /// <summary>The plate's tint.</summary>
    public string? PlateColor { get; init; }
    /// <summary>The text shadow's color.</summary>
    public string? Shadow { get; init; }
    /// <summary>A family for the Desk only, when it should differ from the theme's <c>font</c>.</summary>
    public string? Font { get; init; }

    /// <summary>The fields a desk object may carry, in the order a file writes them.</summary>
    public static IReadOnlyList<string> Fields { get; } = ["ink", "bars", "message", "glow", "plate", "plate_color", "shadow", "font"];

    public bool IsEmpty =>
        Ink is null && Bars is null && Message is null && Glow is null && Plate is null && PlateColor is null && Shadow is null && Font is null;

    /// <summary>The desk object: only the fields that are set.</summary>
    public JsonObject ToJson()
    {
        var o = new JsonObject();
        if (Ink is not null) o["ink"] = Stops(Ink);
        if (Bars is not null) o["bars"] = Stops(Bars);
        if (Message is not null) o["message"] = Stops(Message);
        if (Glow is not null) o["glow"] = Glow;
        if (Plate is { } plate) o["plate"] = plate;
        if (PlateColor is not null) o["plate_color"] = PlateColor;
        if (Shadow is not null) o["shadow"] = Shadow;
        if (Font is not null) o["font"] = Font;
        return o;
    }

    /// <summary>The desk object of a theme the lint accepted; null when it is absent or sets nothing. Unknown fields are left behind
    /// (the lint names them).</summary>
    internal static ThemeDesk? Read(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        var d = new ThemeDesk
        {
            Ink = List(o["ink"]),
            Bars = List(o["bars"]),
            Message = List(o["message"]),
            Glow = Str(o["glow"]),
            Plate = o["plate"] is JsonValue v && v.TryGetValue<bool>(out var on) ? on : null,
            PlateColor = Str(o["plate_color"]),
            Shadow = Str(o["shadow"]),
            Font = Str(o["font"]),
        };
        return d.IsEmpty ? null : d;
    }

    public bool Equals(ThemeDesk? other) =>
        other is not null && Same(Ink, other.Ink) && Same(Bars, other.Bars) && Same(Message, other.Message)
        && Glow == other.Glow && Plate == other.Plate && PlateColor == other.PlateColor && Shadow == other.Shadow && Font == other.Font;

    public override int GetHashCode() =>
        HashCode.Combine(Glow, Plate, PlateColor, Shadow, Font, Ink?.Count ?? -1, Bars?.Count ?? -1, Message?.Count ?? -1);

    private static bool Same(IReadOnlyList<string>? a, IReadOnlyList<string>? b) =>
        a is null ? b is null : b is not null && a.SequenceEqual(b, StringComparer.Ordinal);

    private static JsonArray Stops(IReadOnlyList<string> stops)
    {
        var a = new JsonArray();
        // Non-generic Add: the trimmed MCP server carries no serializer metadata.
        foreach (var s in stops) a.Add((JsonNode?)s);
        return a;
    }

    private static IReadOnlyList<string>? List(JsonNode? n) =>
        n is JsonArray a ? [.. a.Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : "")] : null;

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
```

In `ThemeDefinition`, replace

```csharp
    public int? BreathPeriodMs { get; init; }
    public bool BgGrid { get; init; }
}
```

with

```csharp
    public int? BreathPeriodMs { get; init; }
    public bool BgGrid { get; init; }

    // -- one theme for the widget and the Desk (theme match) -------------------
    /// <summary>The family the widget's text is drawn in, and the Desk's while it follows the theme; null keeps Segoe UI.</summary>
    public string? Font { get; init; }
    /// <summary>The theme's Desk look; null leaves every part of it to ThemeDeskLook's rules.</summary>
    public ThemeDesk? Desk { get; init; }
}
```

In `ThemeCatalog.BuildDefinition`, replace

```csharp
            BgGrid = BoolOr(data["bg_grid"], false),
        };
```

with

```csharp
            BgGrid = BoolOr(data["bg_grid"], false),
            Font = StrOrNull(data, "font"),
            Desk = ThemeDesk.Read(data["desk"]),
        };
```

- [ ] **Step 4: The lint**

In `src/Sanduhr.Core/ThemeLint.cs`, after `public const double HueSaturationFloor = 0.15;` add:

```csharp

    /// <summary>A Desk gradient (desk.ink, desk.bars, desk.message) is one to this many colors.</summary>
    public const int MaxDeskStops = 4;
    /// <summary>desk.ink and desk.message stops against the theme's bg: the Desk's readable-contrast rule.</summary>
    public const double DeskContrastMin = DeskDesign.MinContrast;
```

Replace the two `Lint` overloads' signatures and the string overload's last line:

```csharp
    public static ThemeLintResult Lint(string json)
```
with
```csharp
    /// <param name="fontAvailable">Whether this PC can draw a family (bundled, installed or a font pack); null skips the check
    /// (the MCP server, which cannot know the PC's fonts).</param>
    public static ThemeLintResult Lint(string json, Func<string, bool>? fontAvailable = null)
```
and
```csharp
        return Lint(data);
    }

    public static ThemeLintResult Lint(JsonObject data)
```
with
```csharp
        return Lint(data, fontAvailable);
    }

    public static ThemeLintResult Lint(JsonObject data, Func<string, bool>? fontAvailable = null)
```

In the schema section, replace

```csharp
        bool optsOut = data["opts_out_of_mica"] is JsonNode oom && BoolOr(oom, false);
```

with

```csharp
        bool optsOut = data["opts_out_of_mica"] is JsonNode oom && BoolOr(oom, false);
        CheckFont(data, "font", findings);
        if (data["desk"] is JsonNode deskNode)
            CheckDesk(deskNode, findings);
```

In the design rules, replace

```csharp
        if (optsOut && glassAlpha < 1.0)
            findings.Add(Warning("glass_alpha", "opts_out_of_mica is true, so set glass_alpha to 1.0; cards render translucent on non-Mica fallbacks otherwise."));

        return new ThemeLintResult(ThemeCatalog.BuildDefinition(data), findings);
```

with

```csharp
        if (optsOut && glassAlpha < 1.0)
            findings.Add(Warning("glass_alpha", "opts_out_of_mica is true, so set glass_alpha to 1.0; cards render translucent on non-Mica fallbacks otherwise."));

        // The Desk draws ink and message stops over a plate tinted from bg (theme match): each must read on bg.
        if (data["desk"] is JsonObject desk)
        {
            double bgL = Luminance(colors["bg"]);
            foreach (var key in new[] { "ink", "message" })
            {
                if (desk[key] is not JsonArray stops) continue;
                foreach (var stop in stops)
                {
                    if (StringOrNull(stop) is not { } hex || !TryParseHex(hex, out var rgb)) continue;
                    double ratio = Contrast(Luminance(rgb), bgL);
                    if (ratio < DeskContrastMin)
                        findings.Add(Warning("desk." + key, $"desk.{key} color {hex} reads at {ratio:0.0}:1 on bg (needs {DeskContrastMin:0.0}:1); the Desk draws it over a plate tinted from bg."));
                }
            }
        }
        if (fontAvailable is not null)
        {
            foreach (var (label, family) in new[] { ("font", StringOrNull(data["font"])), ("desk.font", StringOrNull((data["desk"] as JsonObject)?["font"])) })
            {
                if (family is { Length: > 0 } f && BundledFonts.Bundled(f) is null && !fontAvailable(f))
                    findings.Add(Warning(label, $"{label} {f} is not installed on this PC or in a font pack; the widget shows Segoe UI and the Desk {DeskFont.DefaultFamily} until it is."));
            }
        }

        return new ThemeLintResult(ThemeCatalog.BuildDefinition(data), findings);
```

After `CheckOptionalHex` (the last method), add:

```csharp

    /// <summary>A font family (theme match): a family name as the font pickers show it, never a path, a link or a list.</summary>
    private static void CheckFont(JsonObject o, string key, List<ThemeFinding> findings, string? label = null)
    {
        label ??= key;
        if (o[key] is not JsonNode n)
            return;
        string? family = StringOrNull(n);
        if (family is null || family.Trim().Length == 0)
            findings.Add(Error(label, $"{label} must be a font family name, got {Describe(n)}."));
        else if (DeskDesign.FontFamilyProblem(family) is { } why)
            findings.Add(Error(label, $"{label} {why}."));
    }

    /// <summary>The desk object (theme match): its known fields' shapes are errors; an unknown field is a warning and is ignored, so a
    /// theme written by a newer Sanduhr still loads.</summary>
    private static void CheckDesk(JsonNode node, List<ThemeFinding> findings)
    {
        if (node is not JsonObject desk)
        {
            findings.Add(Error("desk", $"desk must be an object: ink, bars and message (1 to {MaxDeskStops} #rrggbb colors each), glow, plate_color and shadow (#rrggbb), plate (true or false) and font, every one optional."));
            return;
        }
        foreach (var key in new[] { "ink", "bars", "message" })
        {
            if (!desk.ContainsKey(key)) continue;
            var n = desk[key];
            if (n is not JsonArray arr || arr.Count is < 1 or > MaxDeskStops || arr.Any(s => StringOrNull(s) is not { } h || !TryParseHex(h, out _)))
                findings.Add(Error("desk." + key, $"desk.{key} must be a list of 1 to {MaxDeskStops} #rrggbb colors, got {Describe(n)}."));
        }
        foreach (var key in new[] { "glow", "plate_color", "shadow" })
        {
            if (!desk.ContainsKey(key)) continue;
            var n = desk[key];
            if (StringOrNull(n) is not { } hex || !TryParseHex(hex, out _))
                findings.Add(Error("desk." + key, $"desk.{key} must be a #rrggbb color, got {Describe(n)}."));
        }
        if (desk.ContainsKey("plate") && !(desk["plate"] is JsonValue pv && pv.TryGetValue<bool>(out _)))
            findings.Add(Error("desk.plate", $"desk.plate must be true or false, got {Describe(desk["plate"])}."));
        CheckFont(desk, "font", findings, "desk.font");
        foreach (var unknown in desk.Select(kv => kv.Key).Where(k => !ThemeDesk.Fields.Contains(k)).Order(StringComparer.Ordinal).Take(5))
            findings.Add(Warning("desk." + unknown, $"desk.{unknown} is not a Desk field; it is ignored. The fields are {string.Join(", ", ThemeDesk.Fields)}."));
    }
```

(`DeskDesign`, `BundledFonts` and `DeskFont` live in `DeskDesign.cs` and `DeskFont.cs`, both already linked into the MCP server, so the server still compiles.)

- [ ] **Step 5: The catalog lets them through**

In `src/Sanduhr.Core/PackValidator.cs`, replace

```csharp
        "breath_period_ms", "opts_out_of_mica", "bg_grid", "monospace_font", "monospace_fallback",
    };
```

with

```csharp
        "breath_period_ms", "opts_out_of_mica", "bg_grid", "monospace_font", "monospace_fallback",
        // theme match: the widget's font and the Desk look (Core's ThemeLint checks both).
        "font", "desk",
    };
```

- [ ] **Step 6: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~ThemeLintTests|FullyQualifiedName~ThemeModelTests|FullyQualifiedName~PackValidatorTests|FullyQualifiedName~ThemeDraftTests|FullyQualifiedName~ThemeProposalsTests"`
Expected: PASS (the six built-ins still lint with zero findings). Then `dotnet build src/Sanduhr.Mcp` (the server compiles the changed linked files) and `dotnet test tests/Sanduhr.Mcp.Tests --filter "FullyQualifiedName~TrustBoundaryTests|FullyQualifiedName~ToolLogicThemeTests"`: PASS, untouched.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/ThemeModel.cs windows-dotnet/src/Sanduhr.Core/ThemeLint.cs windows-dotnet/src/Sanduhr.Core/PackValidator.cs windows-dotnet/tests/Sanduhr.Tests/ThemeLintTests.cs windows-dotnet/tests/Sanduhr.Tests/ThemeModelTests.cs windows-dotnet/tests/Sanduhr.Tests/PackValidatorTests.cs
git commit -m "feat(theme-match): a theme carries font and a desk block; the lint checks both"
```

---

### Task 2: The canonical serializer carries `font` and `desk`

**Files:**
- Modify: `src/Sanduhr.Core/ThemeDraft.cs` (properties ~42-48; `FromDefinition` ~57-86; `ToJson` ~114-124; `Lint` ~128)
- Test: `tests/Sanduhr.Tests/ThemeDraftTests.cs`

**Interfaces:**
- Consumes: `ThemeDesk`, `ThemeDefinition.Font/.Desk`, `ThemeLint.Lint(JsonObject, Func<string,bool>?)` (Task 1).
- Produces: `ThemeDraft.Font` (`string?`, settable), `ThemeDraft.Desk` (`ThemeDesk?`, settable); `ToJson` writes `font` and `desk` after the two dials, only when set; `ThemeDraft.Lint(Func<string, bool>? fontAvailable = null)`. `DesignTheme.Canonical` and `DeskState.Current` (both go through the draft) now keep them.

- [ ] **Step 1: Write the failing tests**

Append to the class in `tests/Sanduhr.Tests/ThemeDraftTests.cs`:

```csharp
    // ---- theme match: the canonical serializer carries font and desk ----

    private static ThemeDefinition Matched() => ThemeCatalog.BuiltIns["obsidian"] with
    {
        Name = "Obsidian + Desk",
        Font = "Consolas",
        Desk = new ThemeDesk { Ink = ["#e8e4dc", "#6c63ff"], Plate = false, Shadow = "#070707", Font = "EsteFont Pro" },
    };

    [Fact]
    public void Font_and_desk_survive_the_draft_round_trip()
    {
        var back = ThemeLint.Lint(ThemeDraft.FromDefinition("x", Matched()).ToJson()).Theme;
        Assert.Equal(Matched(), back);
    }

    [Fact]
    public void A_theme_without_font_or_desk_writes_neither_and_keeps_its_canonical_string()
    {
        var json = ThemeDraft.FromDefinition("obsidian", ThemeCatalog.BuiltIns["obsidian"]).ToJson();
        Assert.False(json.ContainsKey("font"));
        Assert.False(json.ContainsKey("desk"));
        // Saved designs compare themes by this string: a theme from before theme match is still the same theme.
        Assert.DoesNotContain("desk", DesignTheme.Canonical(ThemeCatalog.BuiltIns["obsidian"]));
    }

    [Fact]
    public void The_draft_edits_font_and_desk_and_an_empty_one_writes_nothing()
    {
        var d = ThemeDraft.FromDefinition("obsidian", ThemeCatalog.BuiltIns["obsidian"]);
        d.Font = "Segoe UI";
        d.Desk = new ThemeDesk { Bars = ["#6c63ff"] };
        var json = d.ToJson();
        Assert.Equal("Segoe UI", (string?)json["font"]);
        Assert.Equal("#6c63ff", (string?)json["desk"]!["bars"]![0]);
        d.Desk = new ThemeDesk();
        d.Font = "";
        Assert.False(d.ToJson().ContainsKey("desk"));
        Assert.False(d.ToJson().ContainsKey("font"));
    }

    [Fact]
    public void Designs_and_get_desk_keep_a_matched_theme_whole()
    {
        // DeskState.Current and DesignTheme.Canonical both go through the draft: a field it dropped would vanish from every design.
        var canonical = DesignTheme.Canonical(ThemeDraft.FromDefinition("x", Matched()).ToJson());
        Assert.Equal("Consolas", (string?)canonical["font"]);
        Assert.False((bool)canonical["desk"]!["plate"]!);
    }

    [Fact]
    public void The_draft_lint_passes_the_font_check_through()
    {
        var d = ThemeDraft.FromDefinition("obsidian", ThemeCatalog.BuiltIns["obsidian"]);
        d.Font = "Nope Sans";
        Assert.Contains(d.Lint(_ => false).Warnings, f => f.Field == "font");
        Assert.Empty(d.Lint().Findings);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~ThemeDraftTests`
Expected: build FAILS with `'ThemeDraft' does not contain a definition for 'Font'`.

- [ ] **Step 3: Carry them**

In `src/Sanduhr.Core/ThemeDraft.cs`, replace

```csharp
    public string Name { get; set; } = "";
    public double GlassAlpha { get; set; } = 0.80;
    public double BorderAlpha { get; set; } = 0.40;
```

with

```csharp
    public string Name { get; set; } = "";
    public double GlassAlpha { get; set; } = 0.80;
    public double BorderAlpha { get; set; } = 0.40;

    /// <summary>The theme's font (theme match); null or empty writes none.</summary>
    public string? Font { get; set; }

    /// <summary>The theme's Desk look (theme match); null or empty writes none. Designs, <c>get_desk</c> and undo serialize themes through
    /// this class, so a field it did not carry would silently drop out of all of them.</summary>
    public ThemeDesk? Desk { get; set; }
```

replace

```csharp
        var d = new ThemeDraft { SourceKey = key, Name = def.Name, GlassAlpha = def.GlassAlpha, BorderAlpha = def.BorderAlpha };
```

with

```csharp
        var d = new ThemeDraft
        {
            SourceKey = key, Name = def.Name, GlassAlpha = def.GlassAlpha, BorderAlpha = def.BorderAlpha, Font = def.Font, Desk = def.Desk,
        };
```

replace

```csharp
        o["glass_alpha"] = Math.Round(GlassAlpha, 2);
        o["border_alpha"] = Math.Round(BorderAlpha, 2);
        foreach (var (k, v) in _extras)
```

with

```csharp
        o["glass_alpha"] = Math.Round(GlassAlpha, 2);
        o["border_alpha"] = Math.Round(BorderAlpha, 2);
        // Written only when set, so a theme from before theme match keeps its canonical string (designs compare by it).
        if (!string.IsNullOrEmpty(Font)) o["font"] = Font;
        if (Desk is { IsEmpty: false } desk) o["desk"] = desk.ToJson();
        foreach (var (k, v) in _extras)
```

and replace

```csharp
    public ThemeLintResult Lint() => ThemeLint.Lint(ToJson());
```

with

```csharp
    /// <param name="fontAvailable">This PC's font check (the App's); null lints the font's shape only.</param>
    public ThemeLintResult Lint(Func<string, bool>? fontAvailable = null) => ThemeLint.Lint(ToJson(), fontAvailable);
```

Update the class summary's second sentence to read: "a name, the fourteen role-named colors as the user typed them, the two dials the studio exposes, the theme's font and Desk look (theme match), and the untouched extras carried from whatever theme the draft started from …" (the rest unchanged).

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~ThemeDraftTests|FullyQualifiedName~DeskDesignApplyTests|FullyQualifiedName~DeskDesignTests|FullyQualifiedName~DeskDesignHandoffTests"`
Expected: PASS (`Every_built_in_survives_the_draft_round_trip_field_for_field` still passes: built-ins carry neither field).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/ThemeDraft.cs windows-dotnet/tests/Sanduhr.Tests/ThemeDraftTests.cs
git commit -m "feat(theme-match): ThemeDraft carries font and desk, so designs and get_desk keep them"
```

---

### Task 3: `ThemeDeskLook.From`: the rules, pinned per built-in, and the readability rule

**Files:**
- Create: `src/Sanduhr.Core/ThemeDeskLook.cs`
- Test: `tests/Sanduhr.Tests/ThemeDeskLookTests.cs` (create)

**Interfaces:**
- Consumes: `ThemeDefinition`, `ThemeDesk` (Task 1); `InkHex`, `DeskPlate`, `DeskDesign.Ratio`, `DeskDesign.MinContrast`, `ThemeLint.MaxDeskStops`.
- Produces: `public sealed record ThemeDeskLook(IReadOnlyList<string> Gradient, IReadOnlyList<string> Ink, IReadOnlyList<string> Bars, IReadOnlyList<string> Message, string Glow, bool Plate, string PlateColor, string Shadow, string? Font, string BarTrack, string PaceTick)` (every color six-digit lowercase hex, no #; lists of 1 to 4, a color equal to the one before it dropped); `const double ShadowBlend = 0.5`, `PlateBlend = 0.25`; `bool DarkInk`; `static ThemeDeskLook From(ThemeDefinition)`; `bool ReadsOver(double wallpaperLuminance)`; `ThemeDeskLook Guarded(double? wallpaperLuminance)`; `static string Mix(string hex, string toward, double amount)`; `JsonObject ToJson()` (keys `gradient, ink, bars, message, glow, plate, plate_color, shadow, font, bar_track, pace_tick`, colors `#rrggbb`).

- [ ] **Step 1: Write the failing tests**

Create `tests/Sanduhr.Tests/ThemeDeskLookTests.cs`:

```csharp
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Theme match: one function turns a theme into the Desk's whole look (spec, "The rules"). The built-ins' looks are pinned,
/// shadow and plate tint included (the plan's formulas: bg toward black at 0.5, bg toward the plate's color at 0.25).</summary>
public class ThemeDeskLookTests
{
    [Theory]
    //          key,         gradient,         ink,              glow,     plate,    shadow,   font,            track,    tick
    [InlineData("obsidian", "6c63ff,e8e4dc", "e8e4dc,6c63ff", "6c63ff", "0a0a0a", "070707", null, "2a2a2a", "ff6b6b")]
    [InlineData("aurora", "38bdf8,e2e8f0", "e2e8f0,38bdf8", "38bdf8", "080b14", "05080d", null, "1e293b", "f472b6")]
    [InlineData("ember", "f97316,f5e6e0", "f5e6e0,f97316", "f97316", "140808", "0d0505", null, "2d1a1a", "fbbf24")]
    [InlineData("mint", "34d399,e0f5ec", "e0f5ec,34d399", "34d399", "08140f", "050d0a", null, "163020", "f472b6")]
    [InlineData("matrix", "00ff41", "00ff41", "00ff41", "020802", "010501", "Cascadia Code", "0a1a0a", "ff0040")]
    [InlineData("blueprint", "4dffc4,e0e2f5", "e0e2f5,4dffc4", "4dffc4", "14111c", "0d0b13", null, "1f1a2e", "ff4d88")]
    public void Each_built_in_has_its_pinned_look(string key, string gradient, string ink, string glow, string plate, string shadow,
        string? font, string track, string tick)
    {
        var look = ThemeDeskLook.From(ThemeCatalog.BuiltIns[key]);
        Assert.Equal(gradient.Split(','), look.Gradient);
        Assert.Equal(ink.Split(','), look.Ink);
        Assert.Equal(look.Gradient, look.Bars);       // no desk.bars: the theme gradient
        Assert.Equal(look.Gradient, look.Message);
        Assert.Equal(glow, look.Glow);
        Assert.True(look.Plate);
        Assert.Equal(plate, look.PlateColor);
        Assert.Equal(shadow, look.Shadow);
        Assert.Equal(font, look.Font);
        Assert.Equal(track, look.BarTrack);
        Assert.Equal(tick, look.PaceTick);
        Assert.False(look.DarkInk);
    }

    [Fact]
    public void The_gradient_is_accent_to_sparkline_or_accent_to_text_when_they_are_one()
    {
        Assert.Equal(["6c63ff", "9d97ff"], ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"] with { Sparkline = "#9d97ff" }).Gradient);
        Assert.Equal(["6c63ff", "e8e4dc"], ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"]).Gradient);
    }

    [Fact]
    public void Every_desk_field_wins_over_its_rule()
    {
        var t = ThemeCatalog.BuiltIns["obsidian"] with
        {
            Font = "Segoe UI",
            Desk = new ThemeDesk
            {
                Ink = ["#FFFFFF", "#9AD7FF"], Bars = ["#ff0000", "#00ff00", "#0000ff", "#ffffff"], Message = ["#ffd08a"],
                Glow = "#ffd08a", Plate = false, PlateColor = "#112233", Shadow = "#000000", Font = "Consolas",
            },
        };
        var look = ThemeDeskLook.From(t);
        Assert.Equal(["ffffff", "9ad7ff"], look.Ink);
        Assert.Equal(["ff0000", "00ff00", "0000ff", "ffffff"], look.Bars);
        Assert.Equal(["ffd08a"], look.Message);
        Assert.Equal("ffd08a", look.Glow);
        Assert.False(look.Plate);
        Assert.Equal("112233", look.PlateColor);
        Assert.Equal("000000", look.Shadow);
        Assert.Equal("Consolas", look.Font);
        Assert.Equal(["6c63ff", "e8e4dc"], look.Gradient);   // the gradient itself stays the theme's
    }

    [Fact]
    public void The_font_falls_back_from_desk_font_to_font_to_monospace_to_none()
    {
        var matrix = ThemeCatalog.BuiltIns["matrix"];
        Assert.Equal("Cascadia Code", ThemeDeskLook.From(matrix).Font);
        Assert.Equal("Consolas", ThemeDeskLook.From(matrix with { Font = "Consolas" }).Font);
        Assert.Equal("EsteFont 26", ThemeDeskLook.From(matrix with { Font = "Consolas", Desk = new ThemeDesk { Font = "EsteFont 26" } }).Font);
        Assert.Null(ThemeDeskLook.From(ThemeCatalog.BuiltIns["aurora"]).Font);   // the Desk keeps its own font
    }

    [Fact]
    public void Dark_ink_turns_the_shadow_and_the_plate_toward_white()
    {
        var look = ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"] with { Bg = "#f4f4f2", Text = "#1b1f27" });
        Assert.True(look.DarkInk);
        Assert.Equal("fafaf9", look.Shadow);
        Assert.Equal("f7f7f5", look.PlateColor);
    }

    [Theory]
    [InlineData("0d0d0d", "000000", 0.5, "070707")]
    [InlineData("0d0d0d", "000000", 0.25, "0a0a0a")]
    [InlineData("#F4F4F2", "ffffff", 0.5, "fafaf9")]
    [InlineData("zzz", "000000", 0.5, "808080")]   // an unreadable color is white, InkHex's rule
    public void Mix_blends_in_srgb_bytes_and_rounds_half_away(string from, string toward, double amount, string expected) =>
        Assert.Equal(expected, ThemeDeskLook.Mix(from, toward, amount));

    [Fact]
    public void The_ink_reads_when_its_first_stop_keeps_three_to_one()
    {
        var look = ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"]);
        Assert.True(look.ReadsOver(0.05));    // #e8e4dc on a dark wallpaper: 8.3:1
        Assert.False(look.ReadsOver(0.9));    // on a bright one: 1.1:1
    }

    [Fact]
    public void The_guard_turns_the_plate_on_only_where_the_ink_does_not_read()
    {
        var bare = ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"] with { Desk = new ThemeDesk { Plate = false } });
        Assert.False(bare.Guarded(0.05).Plate);
        Assert.True(bare.Guarded(0.9).Plate);
        Assert.False(bare.Guarded(null).Plate);        // no sample: as the theme says
        Assert.False(bare.Guarded(double.NaN).Plate);
        var plated = ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"]);
        Assert.Same(plated, plated.Guarded(0.9));       // already on: nothing to do
    }

    [Fact]
    public void The_look_as_get_desk_reports_it()
    {
        var json = ThemeDeskLook.From(ThemeCatalog.BuiltIns["matrix"]).ToJson();
        Assert.Equal("#00ff41", (string?)json["ink"]![0]);
        Assert.Equal("#020802", (string?)json["plate_color"]);
        Assert.True((bool)json["plate"]!);
        Assert.Equal("Cascadia Code", (string?)json["font"]);
        Assert.Equal(["gradient", "ink", "bars", "message", "glow", "plate", "plate_color", "shadow", "font", "bar_track", "pace_tick"],
            json.Select(kv => kv.Key));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~ThemeDeskLookTests`
Expected: build FAILS with `The type or namespace name 'ThemeDeskLook' could not be found`.

- [ ] **Step 3: Write the rules**

Create `src/Sanduhr.Core/ThemeDeskLook.cs`:

```csharp
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// One theme's look for the Desk (theme match, spec "The rules"): the theme gradient, the ink for the clock, date and Claude line,
/// the meter bars, the message and its glow, the back plate and its tint, the text shadow, the Desk font, and the meter track and
/// pace tick. <see cref="From"/> turns any theme into one; a field of the theme's <c>desk</c> object wins over its rule. Colors are
/// six-digit lowercase hex without the #, the Desk's own form (<see cref="InkHex"/>); a list holds 1 to 4 stops. Pure (colors and
/// arithmetic; no IO, network, credentials or process): the MCP server links this file so get_desk and the design check compute
/// the same look as the widget.
/// </summary>
public sealed record ThemeDeskLook(
    IReadOnlyList<string> Gradient,
    IReadOnlyList<string> Ink,
    IReadOnlyList<string> Bars,
    IReadOnlyList<string> Message,
    string Glow,
    bool Plate,
    string PlateColor,
    string Shadow,
    string? Font,
    string BarTrack,
    string PaceTick)
{
    /// <summary>The text shadow: the theme's bg blended this far toward black (toward white behind dark ink, as the Desk's shadow
    /// flips today), a deep shade of the theme rather than black.</summary>
    public const double ShadowBlend = 0.5;

    /// <summary>The plate's tint: the theme's bg blended this far toward the plate's own color (black behind light ink, white behind
    /// dark ink, <see cref="DeskPlate.ColorHex"/>), drawn at <see cref="DeskPlate.Opacity"/>.</summary>
    public const double PlateBlend = 0.25;

    /// <summary>True when the ink is dark: its first stop under half luminance, as <see cref="DeskInk"/> decides for a custom ink.</summary>
    public bool DarkInk => InkHex.Luminance(Ink[0]) < 0.5;

    /// <summary>
    /// The theme's Desk look. The gradient is accent to sparkline (accent to text when they are one color). Ink: desk.ink, else text to
    /// accent. Bars and message: theirs, else the gradient. Glow: desk.glow, else accent. Plate: desk.plate, else on. Plate tint and
    /// shadow: theirs, else blends of bg. Font: desk.font, else font, else monospace_font, else none (the Desk keeps its own).
    /// Track and tick: bar_bg and pace_marker.
    /// </summary>
    public static ThemeDeskLook From(ThemeDefinition theme)
    {
        string accent = Hex(theme.Accent), sparkline = Hex(theme.Sparkline), text = Hex(theme.Text), bg = Hex(theme.Bg);
        var gradient = Stops([accent, accent == sparkline ? text : sparkline]);
        var d = theme.Desk;
        var ink = d?.Ink is { Count: > 0 } i ? Stops(i) : Stops([text, accent]);
        bool dark = InkHex.Luminance(ink[0]) < 0.5;
        return new ThemeDeskLook(
            gradient,
            ink,
            d?.Bars is { Count: > 0 } b ? Stops(b) : gradient,
            d?.Message is { Count: > 0 } m ? Stops(m) : gradient,
            d?.Glow is { } g ? Hex(g) : accent,
            d?.Plate ?? true,
            d?.PlateColor is { } pc ? Hex(pc) : Mix(bg, DeskPlate.ColorHex(dark), PlateBlend),
            d?.Shadow is { } s ? Hex(s) : Mix(bg, dark ? "ffffff" : "000000", ShadowBlend),
            d?.Font ?? theme.Font ?? theme.MonospaceFont,
            Hex(theme.BarBg),
            Hex(theme.PaceMarker));
    }

    /// <summary>
    /// The readability rule (theme match, the guard): the ink reads over a wallpaper of this luminance when its first stop, the color the
    /// clock's digits start in, keeps <see cref="DeskDesign.MinContrast"/> against it.
    /// </summary>
    public bool ReadsOver(double wallpaperLuminance) =>
        DeskDesign.Ratio(InkHex.Luminance(Ink[0]), wallpaperLuminance) >= DeskDesign.MinContrast;

    /// <summary>This look over a wallpaper of this luminance: the plate turned on when it is off and the ink would not read. No sample
    /// (null or NaN) leaves the look as the theme says.</summary>
    public ThemeDeskLook Guarded(double? wallpaperLuminance) =>
        Plate || wallpaperLuminance is not { } l || double.IsNaN(l) || ReadsOver(l) ? this : this with { Plate = true };

    /// <summary><paramref name="hex"/> blended <paramref name="amount"/> (0 to 1) of the way to <paramref name="toward"/>, per sRGB byte,
    /// rounding half away from zero. An unreadable color is white (<see cref="InkHex.Fallback"/>).</summary>
    public static string Mix(string hex, string toward, double amount)
    {
        string a = InkHex.Normalize(hex) ?? InkHex.Fallback, b = InkHex.Normalize(toward) ?? InkHex.Fallback;
        amount = Math.Clamp(amount, 0, 1);
        var sb = new StringBuilder(6);
        for (int i = 0; i < 3; i++)
        {
            int x = int.Parse(a.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int y = int.Parse(b.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int v = (int)Math.Round(x + (y - x) * amount, MidpointRounding.AwayFromZero);
            sb.Append(Math.Clamp(v, 0, 255).ToString("x2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>The look as get_desk reports it (<c>theme_look</c>): every color as <c>#rrggbb</c>.</summary>
    public JsonObject ToJson() => new()
    {
        ["gradient"] = Hashed(Gradient),
        ["ink"] = Hashed(Ink),
        ["bars"] = Hashed(Bars),
        ["message"] = Hashed(Message),
        ["glow"] = "#" + Glow,
        ["plate"] = Plate,
        ["plate_color"] = "#" + PlateColor,
        ["shadow"] = "#" + Shadow,
        ["font"] = Font,
        ["bar_track"] = "#" + BarTrack,
        ["pace_tick"] = "#" + PaceTick,
    };

    private static string Hex(string? h) => InkHex.Normalize(h) ?? InkHex.Fallback;

    /// <summary>Colors as stops: normalized, at most <see cref="ThemeLint.MaxDeskStops"/>, a color equal to the one before it dropped
    /// (a gradient of one color is that color); none usable is white.</summary>
    private static IReadOnlyList<string> Stops(IEnumerable<string> colors)
    {
        var list = new List<string>(ThemeLint.MaxDeskStops);
        foreach (var c in colors)
        {
            if (InkHex.Normalize(c) is not { } h) continue;
            if (list.Count > 0 && list[^1] == h) continue;
            if (list.Count == ThemeLint.MaxDeskStops) break;
            list.Add(h);
        }
        if (list.Count == 0) list.Add(InkHex.Fallback);
        return list;
    }

    private static JsonArray Hashed(IReadOnlyList<string> stops)
    {
        var a = new JsonArray();
        // Non-generic Add: the trimmed MCP server carries no serializer metadata.
        foreach (var s in stops) a.Add((JsonNode?)("#" + s));
        return a;
    }
}
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~ThemeDeskLookTests`
Expected: PASS. If a pinned shade differs by one, the rounding is wrong (it must be half away from zero on the unrounded blend); the values in the table are the formula's, not tuned.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/ThemeDeskLook.cs windows-dotnet/tests/Sanduhr.Tests/ThemeDeskLookTests.cs
git commit -m "feat(theme-match): ThemeDeskLook.From turns a theme into the Desk's look; shadow and plate tint pinned per built-in"
```

---

### Task 4: The usage signal: a yellow or red middle stop at Warn at and Urgent at, on the Desk's meters

**Files:**
- Modify: `src/Sanduhr.Core/DeskMeters.cs` (`DeskMeterRow` ~41-50; `WarningAt` ~55-57; `Rows` ~64-80; new types at the end)
- Modify: `src/Sanduhr.App/Desk/DeskView.cs` (`DeskFrame` ~24-57; `WarningRed` comment ~73-74; `BarRow` ~674-710; `Meters` ~715-730; `TelemetryPiece` ~749; helpers after `Ink` ~625)
- Modify: `src/Sanduhr.App/Desk/DeskView.Styles.cs` (`DeskGaugeRead` ~11; `StyledMeters` ~24-31; `StyledClaude` ~67, ~86-87; `Warn` and `Head` ~118-140; `SlimRow` ~161; `TickRow` ~205-225; `Ring` ~273, ~289)
- Modify: `src/Sanduhr.App/Views/DeskLookPage.xaml` (the ink note, ~139)
- Test: `tests/Sanduhr.Tests/MeterSignalTests.cs` (create), `tests/Sanduhr.Tests/DeskMetersTests.cs:54-60`, `tests/Sanduhr.App.Tests/DeskMeterStylesViewTests.cs:84-86` and a new fact there

**Interfaces:**
- Produces: `public enum MeterLevel { Calm, Warn, Urgent }`; `public static class MeterSignal { const int DefaultWarnAt = 80, DefaultUrgentAt = 95; const string WarnHex = "facc15", UrgentHex = "f87171"; static MeterLevel Level(double percent, int warnAt, int urgentAt); static string? Hex(MeterLevel); static IReadOnlyList<string> Stops(IReadOnlyList<string> stops, MeterLevel level) }`; `DeskMeterRow(..., MeterLevel Level)` (replaces `bool Warning`); `DeskMeters.Rows(DeskUsage, DateTimeOffset, TimeZoneInfo? zone = null, int warnAt = 80, int urgentAt = 95)`; `DeskMeters.WarningAt` is removed. App: `DeskFrame.WarnAt`, `DeskFrame.UrgentAt` (ints, default 80 and 95); `DeskGaugeRead.Level`; `DeskView.MeterFill(MeterLevel)`, `DeskView.Signal(TextBlock, MeterLevel)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Sanduhr.Tests/MeterSignalTests.cs`:

```csharp
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Theme match, "Meters": the usage signal worked into a meter's gradient at the alert thresholds.</summary>
public class MeterSignalTests
{
    [Theory]
    [InlineData(0, MeterLevel.Calm)]
    [InlineData(79.9, MeterLevel.Calm)]
    [InlineData(80, MeterLevel.Warn)]
    [InlineData(94.9, MeterLevel.Warn)]
    [InlineData(95, MeterLevel.Urgent)]
    [InlineData(130, MeterLevel.Urgent)]
    public void The_level_follows_Warn_at_and_Urgent_at(double percent, MeterLevel level) =>
        Assert.Equal(level, MeterSignal.Level(percent, MeterSignal.DefaultWarnAt, MeterSignal.DefaultUrgentAt));

    [Fact]
    public void Custom_thresholds_move_the_levels()
    {
        Assert.Equal(MeterLevel.Calm, MeterSignal.Level(59, 60, 70));
        Assert.Equal(MeterLevel.Warn, MeterSignal.Level(60, 60, 70));
        Assert.Equal(MeterLevel.Urgent, MeterSignal.Level(70, 60, 70));
    }

    [Fact]
    public void Under_Warn_at_the_gradient_is_the_users_exactly() =>
        Assert.Equal(["6c63ff", "e8e4dc"], MeterSignal.Stops(["#6C63FF", "e8e4dc"], MeterLevel.Calm));

    [Fact]
    public void A_two_stop_gradient_gets_yellow_or_red_in_the_middle()
    {
        Assert.Equal(["6c63ff", "facc15", "e8e4dc"], MeterSignal.Stops(["6c63ff", "e8e4dc"], MeterLevel.Warn));
        Assert.Equal(["6c63ff", "f87171", "e8e4dc"], MeterSignal.Stops(["6c63ff", "e8e4dc"], MeterLevel.Urgent));
    }

    [Fact]
    public void A_solid_meter_is_its_color_to_itself_with_the_same_middle_stop()
    {
        Assert.Equal(["9ad7ff", "facc15", "9ad7ff"], MeterSignal.Stops(["9ad7ff"], MeterLevel.Warn));
        Assert.Equal(["9ad7ff"], MeterSignal.Stops(["9ad7ff"], MeterLevel.Calm));
    }

    [Fact]
    public void Three_and_four_stops_take_the_signal_after_their_first_half()
    {
        Assert.Equal(["111111", "222222", "facc15", "333333"], MeterSignal.Stops(["111111", "222222", "333333"], MeterLevel.Warn));
        Assert.Equal(["111111", "222222", "f87171", "333333", "444444"],
            MeterSignal.Stops(["111111", "222222", "333333", "444444"], MeterLevel.Urgent));
    }

    [Fact]
    public void No_usable_color_is_white_as_the_ink_rule_says() =>
        Assert.Equal(["ffffff", "facc15", "ffffff"], MeterSignal.Stops(["nope"], MeterLevel.Warn));

    [Fact]
    public void Only_a_signal_has_a_text_color_and_it_is_the_ramps_own()
    {
        Assert.Null(MeterSignal.Hex(MeterLevel.Calm));
        Assert.Equal("facc15", MeterSignal.Hex(MeterLevel.Warn));
        Assert.Equal("f87171", MeterSignal.Hex(MeterLevel.Urgent));
        Assert.Equal(ThemeCatalog.UsageColor(60), "#" + MeterSignal.WarnHex);
        Assert.Equal(ThemeCatalog.UsageColor(95), "#" + MeterSignal.UrgentHex);
    }
}
```

In `tests/Sanduhr.Tests/DeskMetersTests.cs`, replace the whole fact

```csharp
    [Fact]
    public void Warning_StartsAtNinety()
    {
        var rows = DeskMeters.Rows(Usage(new DeskTier(TierModel.FiveHour, 89.9, null), new DeskTier(TierModel.SevenDay, 90, null)), Now);
        Assert.False(rows[0].Warning);
        Assert.True(rows[1].Warning);
    }
```

with

```csharp
    [Fact]
    public void Each_row_carries_its_level_at_the_alert_thresholds()
    {
        var rows = DeskMeters.Rows(Usage(new DeskTier(TierModel.FiveHour, 79.9, null), new DeskTier(TierModel.SevenDay, 80, null)), Now);
        Assert.Equal([MeterLevel.Calm, MeterLevel.Warn], rows.Select(r => r.Level));
        rows = DeskMeters.Rows(Usage(new DeskTier(TierModel.FiveHour, 94.9, null), new DeskTier(TierModel.SevenDay, 95, null)), Now);
        Assert.Equal([MeterLevel.Warn, MeterLevel.Urgent], rows.Select(r => r.Level));
        rows = DeskMeters.Rows(Usage(new DeskTier(TierModel.FiveHour, 65, null), new DeskTier(TierModel.SevenDay, 70, null)), Now,
            warnAt: 60, urgentAt: 70);
        Assert.Equal([MeterLevel.Warn, MeterLevel.Urgent], rows.Select(r => r.Level));
    }
```

In `tests/Sanduhr.App.Tests/DeskMeterStylesViewTests.cs`, replace

```csharp
        // The week is over the warning line: its value is red in every style.
        var red = All<TextBlock>(meters).Single(t => t.Text.StartsWith("93", StringComparison.Ordinal));
        Assert.Equal(Color.FromRgb(0xF8, 0x71, 0x71), ((SolidColorBrush)red.Foreground).Color);
```

with

```csharp
        // The week (93) is past Warn at (80) and under Urgent at (95): its value is yellow in every style.
        var warn = All<TextBlock>(meters).Single(t => t.Text.StartsWith("93", StringComparison.Ordinal));
        Assert.Equal(Color.FromRgb(0xFA, 0xCC, 0x15), ((SolidColorBrush)warn.Foreground).Color);
```

and append to the class:

```csharp
    // ---- theme match: the usage signal on the Desk's meters ----

    /// <summary>The bar row's fill rectangles, in tier order (pace marks are 2 px wide; tracks stretch).</summary>
    private static List<Rectangle> Fills(FrameworkElement meters) =>
        All<Rectangle>(meters).Where(r => r.HorizontalAlignment == HorizontalAlignment.Left && r.Width > 2).ToList();

    [Fact]
    public void A_meter_past_Warn_at_wears_a_yellow_middle_stop_and_under_it_draws_as_before() => Sta.Run(() =>
    {
        var view = View("meters:tr", new DeskFrame { Usage = Usage(), Now = Now });   // session 42, week 93
        var fills = Fills(Piece(view, "meters"));
        Assert.IsType<SolidColorBrush>(fills[0].Fill);   // calm: the ink itself (Light ink is one white)
        var week = Assert.IsType<LinearGradientBrush>(fills[1].Fill);
        Assert.Equal([Colors.White, Color.FromRgb(0xFA, 0xCC, 0x15), Colors.White], week.GradientStops.Select(s => s.Color));
        var calm = All<TextBlock>(Piece(view, "meters")).Single(t => t.Text == "42%");
        Assert.Equal(Colors.White, ((SolidColorBrush)calm.Foreground).Color);
        view.Dispose();
    });

    [Fact]
    public void The_alert_thresholds_in_the_frame_move_the_signal() => Sta.Run(() =>
    {
        var view = View("meters:tr", new DeskFrame { Usage = Usage(), Now = Now, WarnAt = 40, UrgentAt = 90 });
        var fills = Fills(Piece(view, "meters"));
        Assert.Equal(Color.FromRgb(0xFA, 0xCC, 0x15), Assert.IsType<LinearGradientBrush>(fills[0].Fill).GradientStops[1].Color);
        Assert.Equal(Color.FromRgb(0xF8, 0x71, 0x71), Assert.IsType<LinearGradientBrush>(fills[1].Fill).GradientStops[1].Color);
        var urgent = All<TextBlock>(Piece(view, "meters")).Single(t => t.Text == "93%");
        Assert.Equal(Color.FromRgb(0xF8, 0x71, 0x71), ((SolidColorBrush)urgent.Foreground).Color);
        view.Dispose();
    });
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~MeterSignalTests|FullyQualifiedName~DeskMetersTests"`
Expected: build FAILS with `The type or namespace name 'MeterLevel' could not be found`.

- [ ] **Step 3: The signal, in Core**

In `src/Sanduhr.Core/DeskMeters.cs`, replace

```csharp
/// <param name="Warning">Utilization at <see cref="DeskMeters.WarningAt"/> or more: the bar draws red.</param>
public sealed record DeskMeterRow(string Key, string Label, int Percent, double Fill, double? Pace, string Reset, bool Warning);
```

with

```csharp
/// <param name="Level">How far the limit is toward its end by the alert thresholds (<see cref="MeterSignal"/>): past Warn at the bar
/// takes a yellow middle stop, past Urgent at a red one.</param>
public sealed record DeskMeterRow(string Key, string Label, int Percent, double Fill, double? Pace, string Reset, MeterLevel Level);
```

replace

```csharp
    /// <summary>The utilization at which the widget card's bar turns red
    /// (<see cref="ThemeCatalog.UsageColor"/>: under 90 is orange, 90 and up is red).</summary>
    public const double WarningAt = 90;

```

with nothing (delete the three lines and the blank line after them), and replace

```csharp
    /// <summary>One row per tier, in the given order; none when a sign-in is needed.
    /// <paramref name="zone"/> converts reset times (default: local), for deterministic tests.</summary>
    public static IReadOnlyList<DeskMeterRow> Rows(DeskUsage usage, DateTimeOffset now, TimeZoneInfo? zone = null)
```

with

```csharp
    /// <summary>One row per tier, in the given order; none when a sign-in is needed.
    /// <paramref name="zone"/> converts reset times (default: local), for deterministic tests. <paramref name="warnAt"/> and
    /// <paramref name="urgentAt"/> are the alert thresholds (settings.json <c>alert_warn_pct</c>, <c>alert_urgent_pct</c>).</summary>
    public static IReadOnlyList<DeskMeterRow> Rows(DeskUsage usage, DateTimeOffset now, TimeZoneInfo? zone = null,
        int warnAt = MeterSignal.DefaultWarnAt, int urgentAt = MeterSignal.DefaultUrgentAt)
```

and in its body replace

```csharp
                t.Utilization >= WarningAt));
```

with

```csharp
                MeterSignal.Level(t.Utilization, warnAt, urgentAt)));
```

At the end of the file, add:

```csharp

/// <summary>How far a meter is toward its limit, by the alert thresholds (theme match, the usage signal).</summary>
public enum MeterLevel { Calm, Warn, Urgent }

/// <summary>
/// The usage signal worked into a meter's gradient (theme match, spec "Meters"): under Warn at the meter is its own colors; from Warn
/// at a yellow stop sits in their middle, from Urgent at a red one; a solid meter is its color to itself, so it gets the same stop.
/// The thresholds are the alerts' (<c>alert_warn_pct</c>, default 80; <c>alert_urgent_pct</c>, default 95); the colors are the widget
/// ramp's own yellow and red (<see cref="ThemeCatalog.UsageColor"/>). Only meters carry it: the clock, date, message and Now
/// playing keep their gradient exactly. This replaces the Desk's switch to solid red at 90%.
/// </summary>
public static class MeterSignal
{
    public const int DefaultWarnAt = 80;
    public const int DefaultUrgentAt = 95;
    public const string WarnHex = "facc15";
    public const string UrgentHex = "f87171";

    public static MeterLevel Level(double percent, int warnAt, int urgentAt) =>
        percent >= urgentAt ? MeterLevel.Urgent : percent >= warnAt ? MeterLevel.Warn : MeterLevel.Calm;

    /// <summary>The signal's color, six hex digits; null when calm (the meter's text keeps its own color).</summary>
    public static string? Hex(MeterLevel level) => level switch
    {
        MeterLevel.Warn => WarnHex,
        MeterLevel.Urgent => UrgentHex,
        _ => null,
    };

    /// <summary>
    /// The stops a meter at <paramref name="level"/> draws: the given ones normalized (none usable is white); past Warn at, a solid
    /// is doubled and the signal goes in at index ceil(n / 2), so with one, two or four stops it sits exactly in the middle once the
    /// stops are spread evenly.
    /// </summary>
    public static IReadOnlyList<string> Stops(IReadOnlyList<string> stops, MeterLevel level)
    {
        var list = stops.Select(InkHex.Normalize).OfType<string>().ToList();
        if (list.Count == 0) list.Add(InkHex.Fallback);
        if (Hex(level) is not { } signal) return list;
        if (list.Count == 1) list.Add(list[0]);
        list.Insert((list.Count + 1) / 2, signal);
        return list;
    }
}
```

- [ ] **Step 4: The Desk draws it**

In `src/Sanduhr.App/Desk/DeskView.cs`, in `DeskFrame`, replace

```csharp
    /// <summary>The companion piece's sprite (spec 3.8 section 4); null when no sprite is installed or the piece is not placed.</summary>
    public CompanionService? Companion { get; init; }
}
```

with

```csharp
    /// <summary>The companion piece's sprite (spec 3.8 section 4); null when no sprite is installed or the piece is not placed.</summary>
    public CompanionService? Companion { get; init; }
    /// <summary>The alert thresholds the meters' signal uses (theme match): Alerts' Warn at and Urgent at.</summary>
    public int WarnAt { get; init; } = MeterSignal.DefaultWarnAt;
    public int UrgentAt { get; init; } = MeterSignal.DefaultUrgentAt;
}
```

Replace

```csharp
    /// <summary>The widget's over-limit red (<c>ThemeCatalog.UsageColor</c> at 90% and up).</summary>
    private static readonly Color WarningRed = Color.FromRgb(0xF8, 0x71, 0x71);
```

with

```csharp
    /// <summary>The widget's over-limit red (<c>ThemeCatalog.UsageColor</c> at 90% and up): a failed watcher's title. Meters take
    /// <see cref="MeterSignal"/> instead.</summary>
    private static readonly Color WarningRed = Color.FromRgb(0xF8, 0x71, 0x71);
```

After `private Brush Ink => InkBrush(_ink.Stops);` add:

```csharp

    /// <summary>A meter's fill (theme match, the usage signal): the ink, and past Warn at or Urgent at the ink with a yellow or red stop
    /// in its middle. Calm, it is <see cref="Ink"/> itself, so a meter under Warn at draws exactly as it always has.</summary>
    private Brush MeterFill(MeterLevel level) => level == MeterLevel.Calm ? Ink : InkBrush(MeterSignal.Stops(_ink.Stops, level));

    /// <summary>A meter's value past Warn at or Urgent at takes the signal's color; under it, the ink as before.</summary>
    private static void Signal(TextBlock value, MeterLevel level)
    {
        if (MeterSignal.Hex(level) is { } hex) value.Foreground = SolidInk(hex);
    }
```

In `BarRow`, replace

```csharp
    private (StackPanel Block, TextBlock Percent, Rectangle Fill) BarRow(string label, int percent, double fill, double size, double width,
        bool warning, bool first, double? pace = null)
    {
        var block = new StackPanel { Width = width, Margin = new Thickness(0, first ? 0 : size * 0.6, 0, 0) };
        var head = new DockPanel { LastChildFill = true };
        var pct = Text($"{percent}%", size, 1);
        if (warning) pct.Foreground = new SolidColorBrush(WarningRed);
```

with

```csharp
    private (StackPanel Block, TextBlock Percent, Rectangle Fill) BarRow(string label, int percent, double fill, double size, double width,
        MeterLevel level, bool first, double? pace = null)
    {
        var block = new StackPanel { Width = width, Margin = new Thickness(0, first ? 0 : size * 0.6, 0, 0) };
        var head = new DockPanel { LastChildFill = true };
        var pct = Text($"{percent}%", size, 1);
        Signal(pct, level);
```

and

```csharp
            Fill = warning ? new SolidColorBrush(WarningRed) : Ink, Opacity = _ink.TextOpacity,
```

with

```csharp
            Fill = MeterFill(level), Opacity = _ink.TextOpacity,
```

In `Meters`, replace

```csharp
        var rows = DeskMeters.Rows(_frame.Usage, _frame.Now);
        double size = ts * 0.17;
        double width = ts * 3.2;
```

with

```csharp
        var rows = DeskMeters.Rows(_frame.Usage, _frame.Now, warnAt: _frame.WarnAt, urgentAt: _frame.UrgentAt);
        double size = ts * 0.17;
        double width = ts * 3.2;
```

and

```csharp
            var (block, _, _) = BarRow(row.Label, row.Percent, row.Fill, size, width, row.Warning, panel.Children.Count == 0, row.Pace);
```

with

```csharp
            var (block, _, _) = BarRow(row.Label, row.Percent, row.Fill, size, width, row.Level, panel.Children.Count == 0, row.Pace);
```

In `TelemetryPiece`, replace

```csharp
            var (block, pct, fill) = BarRow(row.Label, row.Percent, row.Fill, size, width, false, panel.Children.Count == 0);
```

with

```csharp
            // Telemetry is no usage: it never carries the signal.
            var (block, pct, fill) = BarRow(row.Label, row.Percent, row.Fill, size, width, MeterLevel.Calm, panel.Children.Count == 0);
```

In `src/Sanduhr.App/Desk/DeskView.Styles.cs`, replace

```csharp
/// pace mark (0 to 1, a Claude limit only) and whether it is over the warning line.</summary>
internal sealed record DeskGaugeRead(string Label, string Short, int Percent, double Fill, double? Pace = null, bool Warning = false);
```

with

```csharp
/// pace mark (0 to 1, a Claude limit only) and its level by the alert thresholds (theme match, the usage signal).</summary>
internal sealed record DeskGaugeRead(string Label, string Short, int Percent, double Fill, double? Pace = null, MeterLevel Level = MeterLevel.Calm);
```

In `StyledMeters`, replace

```csharp
        var rows = DeskMeters.Rows(_frame.Usage, _frame.Now);
        double size = ts * 0.17;
        if (rows.Count == 0)
```

with

```csharp
        var rows = DeskMeters.Rows(_frame.Usage, _frame.Now, warnAt: _frame.WarnAt, urgentAt: _frame.UrgentAt);
        double size = ts * 0.17;
        if (rows.Count == 0)
```

and

```csharp
        var reads = rows.Select(r => new DeskGaugeRead(r.Label, DeskMeters.ShortLabel(r.Key), r.Percent, r.Fill, r.Pace, r.Warning)).ToList();
```

with

```csharp
        var reads = rows.Select(r => new DeskGaugeRead(r.Label, DeskMeters.ShortLabel(r.Key), r.Percent, r.Fill, r.Pace, r.Level)).ToList();
```

In `StyledClaude`, replace

```csharp
        IReadOnlyList<DeskMeterRow> rows = style == DeskMeterStyle.Slim ? [] : DeskMeters.Rows(_frame.Usage with { Tiers = tiers }, _frame.Now);
```

with

```csharp
        IReadOnlyList<DeskMeterRow> rows = style == DeskMeterStyle.Slim ? []
            : DeskMeters.Rows(_frame.Usage with { Tiers = tiers }, _frame.Now, warnAt: _frame.WarnAt, urgentAt: _frame.UrgentAt);
```

and

```csharp
            r.Key == TierModel.FiveHour ? "session" : "week", r.Percent, r.Fill, r.Pace, r.Warning)).ToList();
```

with

```csharp
            r.Key == TierModel.FiveHour ? "session" : "week", r.Percent, r.Fill, r.Pace, r.Level)).ToList();
```

Delete the `Warn` property (the eight lines from `private Brush Warn` to its closing brace) and, in `Head`, replace

```csharp
    /// <summary>The label and the value on one line, the value at the end (red over the warning line).</summary>
    private (DockPanel Head, TextBlock Percent) Head(DeskGaugeRead r, double size)
    {
        var head = new DockPanel { LastChildFill = true };
        var pct = Text($"{r.Percent}%", size, 1);
        if (r.Warning) pct.Foreground = Warn;
```

with

```csharp
    /// <summary>The label and the value on one line, the value at the end (yellow or red past the alert thresholds).</summary>
    private (DockPanel Head, TextBlock Percent) Head(DeskGaugeRead r, double size)
    {
        var head = new DockPanel { LastChildFill = true };
        var pct = Text($"{r.Percent}%", size, 1);
        Signal(pct, r.Level);
```

In `SlimRow`, replace

```csharp
            Fill = r.Warning ? Warn : Ink, Opacity = _ink.TextOpacity,
```

with

```csharp
            Fill = MeterFill(r.Level), Opacity = _ink.TextOpacity,
```

In `TickRow`, replace

```csharp
        var lit = Ink;
        // A gradient ink runs along the row, a color per cell, so each cell is not a little copy of the whole gradient.
        var stops = _ink.Stops;
        bool spread = stops.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        var colors = Enumerable.Range(0, n).Select(i => spread ? SolidInk(NowPlayingLooks.ColorAt(stops, (double)i / (n - 1))) : lit).ToArray();
        var warn = Warn;
        void Light(DeskGaugeRead read)
        {
            int on = DeskMeters.LitTicks(read.Fill, n);
            for (int i = 0; i < n; i++)
            {
                if (i < on)
                {
                    cells[i].Fill = read.Warning ? warn : colors[i];
```

with

```csharp
        // A gradient ink runs along the row, a color per cell, so each cell is not a little copy of the whole gradient; past Warn at
        // the signal's stop runs along it too (theme match).
        Brush[] CellColors(MeterLevel level)
        {
            var stops = MeterSignal.Stops(_ink.Stops, level);
            bool spread = stops.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
            var solid = Ink;
            return [.. Enumerable.Range(0, n).Select(i => spread ? SolidInk(NowPlayingLooks.ColorAt(stops, (double)i / (n - 1))) : solid)];
        }
        var colors = CellColors(MeterLevel.Calm);
        void Light(DeskGaugeRead read)
        {
            int on = DeskMeters.LitTicks(read.Fill, n);
            var lit = read.Level == MeterLevel.Calm ? colors : CellColors(read.Level);
            for (int i = 0; i < n; i++)
            {
                if (i < on)
                {
                    cells[i].Fill = lit[i];
```

In `TickRow`'s returned updater, a reading whose level changes must light again: replace

```csharp
            int on = DeskMeters.LitTicks(next.Fill, n);
            if (on != shown) { shown = on; Light(next); }
```

with

```csharp
            int on = DeskMeters.LitTicks(next.Fill, n);
            if (on != shown || next.Level != shownLevel) { shown = on; shownLevel = next.Level; Light(next); }
```

and just above the `return (block, next =>` line, after `int shown = DeskMeters.LitTicks(r.Fill, n);`, add `var shownLevel = r.Level;`.

In `Ring`, replace

```csharp
            Data = Arc(c, radius, r.Fill), Stroke = r.Warning ? Warn : Ink, StrokeThickness = t, Opacity = _ink.TextOpacity,
```

with

```csharp
            Data = Arc(c, radius, r.Fill), Stroke = MeterFill(r.Level), StrokeThickness = t, Opacity = _ink.TextOpacity,
```

and

```csharp
        if (r.Warning) value.Foreground = Warn;
```

with

```csharp
        Signal(value, r.Level);
```

Update the class summary of `Ring` ("the arc the ink (red over the warning line)") to "the arc the ink (with the signal's middle stop past the alert thresholds)".

In `src/Sanduhr.App/Views/DeskLookPage.xaml`, in the ink note `Text="Automatic reads how bright … A meter near its limit stays red in every ink."`, replace the last sentence `A meter near its limit stays red in every ink.` with `A meter past the alert levels (warn and urgent, set under Alerts) takes a yellow or red stop in the middle of its color, in every ink.`

- [ ] **Step 5: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~MeterSignalTests|FullyQualifiedName~DeskMetersTests|FullyQualifiedName~DeskMeterStylesTests"` then `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskMeterStylesViewTests|FullyQualifiedName~DeskRideAlongsViewTests"`
Expected: PASS. `grep -rn "WarningAt\|\.Warning\b" src --include=*.cs` prints nothing outside `ThemeFindingLevel`/`ThemedDialogKind` uses.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/DeskMeters.cs windows-dotnet/src/Sanduhr.App/Desk/DeskView.cs windows-dotnet/src/Sanduhr.App/Desk/DeskView.Styles.cs windows-dotnet/src/Sanduhr.App/Views/DeskLookPage.xaml windows-dotnet/tests/Sanduhr.Tests/MeterSignalTests.cs windows-dotnet/tests/Sanduhr.Tests/DeskMetersTests.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskMeterStylesViewTests.cs
git commit -m "feat(theme-match): the usage signal is a yellow or red middle stop at the alert thresholds; the Desk's solid red at 90 goes"
```

---

### Task 5: The widget's bars draw `desk.bars` with the signal; every other theme keeps the ramp

**Files:**
- Modify: `src/Sanduhr.App/Controls/UsageBar.cs` (DPs ~34-42; `OnRender` ~56-62)
- Modify: `src/Sanduhr.App/ViewModels/TierCardViewModel.cs` (properties ~29; `Update` ~74-82; `ApplyPalette` ~111-116; new members)
- Modify: `src/Sanduhr.App/Views/TierCard.xaml` (~66-82)
- Modify: `src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (~687 the alert handler; ~1753 card creation)
- Test: `tests/Sanduhr.App.Tests/WidgetBarsTests.cs` (create)

**Interfaces:**
- Consumes: `ThemeDefinition.Desk.Bars` (Task 1); `MeterSignal` (Task 4); `DeskView.InkBrush(IReadOnlyList<string>)` (internal static, App).
- Produces: `UsageBar.FillBrush` (`Brush?`, null fills with `FillColor`); `TierCardViewModel.BarBrush` (`Brush?`, null for the ramp), `TierCardViewModel.ValueBrush` (`Brush`), `TierCardViewModel.SetThresholds(int warnAt, int urgentAt)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Sanduhr.App.Tests/WidgetBarsTests.cs`:

```csharp
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sanduhr.App.Theming;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Theme match, "Meters", on the widget: the ramp stays for every theme without <c>desk.bars</c> (no built-in sets it, so nothing
/// changes at upgrade); a theme with it fills the bars with its gradient and the usage signal, and the percent takes the signal.
/// </summary>
public sealed class WidgetBarsTests
{
    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static TierCardViewModel Card(ThemeDefinition theme, int util)
    {
        var palette = new ThemePalette("t", theme);
        var vm = new TierCardViewModel(TierModel.FiveHour, palette);
        vm.Update(util, null, null, null, [], palette, SparklineStyle.Classic, DateTimeOffset.Now);
        return vm;
    }

    [Theory]
    [InlineData("obsidian", 10)]
    [InlineData("aurora", 60)]
    [InlineData("ember", 85)]
    [InlineData("mint", 97)]
    [InlineData("matrix", 50)]
    [InlineData("blueprint", 90)]
    public void A_theme_without_desk_bars_keeps_the_ramp(string key, int util) => Sta.Run(() =>
    {
        var vm = Card(ThemeCatalog.BuiltIns[key], util);
        Assert.Null(vm.BarBrush);
        Assert.Equal(ThemePalette.UsageColor(util), vm.BarColor);
        Assert.Equal(ThemePalette.UsageColor(util), ((SolidColorBrush)vm.ValueBrush).Color);
    });

    [Fact]
    public void Desk_bars_fill_the_bar_with_the_gradient_and_the_signal() => Sta.Run(() =>
    {
        var theme = ThemeCatalog.BuiltIns["obsidian"] with { Desk = new ThemeDesk { Bars = ["#6c63ff", "#e8e4dc"] } };
        var calm = Card(theme, 40);
        Assert.Equal([Rgb(0x6c, 0x63, 0xff), Rgb(0xe8, 0xe4, 0xdc)], ((LinearGradientBrush)calm.BarBrush!).GradientStops.Select(s => s.Color));
        Assert.Equal(Rgb(0xe8, 0xe4, 0xdc), ((SolidColorBrush)calm.ValueBrush).Color);   // the theme's text under Warn at
        var warn = Card(theme, 85);
        Assert.Equal(Rgb(0xfa, 0xcc, 0x15), ((LinearGradientBrush)warn.BarBrush!).GradientStops[1].Color);
        Assert.Equal(Rgb(0xfa, 0xcc, 0x15), ((SolidColorBrush)warn.ValueBrush).Color);
        warn.SetThresholds(50, 80);   // Alerts moved: 85 is past Urgent at now
        Assert.Equal(Rgb(0xf8, 0x71, 0x71), ((LinearGradientBrush)warn.BarBrush!).GradientStops[1].Color);
        Assert.Equal(Rgb(0xf8, 0x71, 0x71), ((SolidColorBrush)warn.ValueBrush).Color);
    });

    [Fact]
    public void Switching_to_and_from_a_theme_with_desk_bars_recolors_the_card() => Sta.Run(() =>
    {
        var plain = ThemeCatalog.BuiltIns["obsidian"];
        var vm = Card(plain, 85);
        vm.ApplyPalette(new ThemePalette("x", plain with { Desk = new ThemeDesk { Bars = ["#6c63ff"] } }));
        Assert.NotNull(vm.BarBrush);
        vm.ApplyPalette(new ThemePalette("obsidian", plain));
        Assert.Null(vm.BarBrush);
        Assert.Equal(ThemePalette.UsageColor(85), ((SolidColorBrush)vm.ValueBrush).Color);
    });

    [Fact]
    public void A_desk_block_without_bars_leaves_the_card_pixel_identical() => AppHost.Run(() =>
    {
        var plain = ThemeCatalog.BuiltIns["obsidian"];
        var matched = plain with { Desk = new ThemeDesk { Ink = ["#ffffff"], Plate = false, Shadow = "#000000", Glow = "#6c63ff" } };
        Assert.Equal(Render(plain, 85), Render(matched, 85));
        Assert.NotEqual(Render(plain, 85), Render(plain with { Desk = new ThemeDesk { Bars = ["#6c63ff"] } }, 85));
    });

    private static byte[] Render(ThemeDefinition theme, int util)
    {
        var card = new TierCard { DataContext = Card(theme, util), Width = 380 };
        new ThemePalette("t", theme).Apply(card.Resources);
        card.Measure(new Size(380, double.PositiveInfinity));
        card.Arrange(new Rect(card.DesiredSize));
        card.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth), (int)Math.Ceiling(card.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(card);
        var px = new byte[rtb.PixelWidth * rtb.PixelHeight * 4];
        rtb.CopyPixels(px, rtb.PixelWidth * 4, 0);
        return px;
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~WidgetBarsTests`
Expected: build FAILS with `'TierCardViewModel' does not contain a definition for 'BarBrush'`.

- [ ] **Step 3: The bar takes a brush**

In `src/Sanduhr.App/Controls/UsageBar.cs`, after the `PaceColorProperty` registration add:

```csharp

    /// <summary>The fill as a brush (theme match: a theme's <c>desk.bars</c> gradient with the usage signal); null fills with
    /// <see cref="FillColor"/>, the ramp, as before.</summary>
    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush), typeof(Brush), typeof(UsageBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
```

after `public Color PaceColor { … }` add:

```csharp
    public Brush? FillBrush { get => (Brush?)GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
```

and in `OnRender` replace

```csharp
            var fill = new SolidColorBrush(FillColor); fill.Freeze();
```

with

```csharp
            var fill = FillBrush;
            if (fill is null)
            {
                var solid = new SolidColorBrush(FillColor);
                solid.Freeze();
                fill = solid;
            }
```

- [ ] **Step 4: The card's colors**

In `src/Sanduhr.App/ViewModels/TierCardViewModel.cs`, after `[ObservableProperty] private Color _barColor = Colors.LimeGreen;` add:

```csharp
    /// <summary>The bar's fill when the theme sets <c>desk.bars</c> (theme match): its gradient with the usage signal. Null: the ramp
    /// (<see cref="BarColor"/>), unchanged for every theme without it.</summary>
    [ObservableProperty] private Brush? _barBrush;
    /// <summary>The percent's color: the ramp's without <c>desk.bars</c>; with it, the theme's text, yellow past Warn at, red past Urgent at.</summary>
    [ObservableProperty] private Brush _valueBrush = Brushes.LimeGreen;
    private int _warnAt = MeterSignal.DefaultWarnAt;
    private int _urgentAt = MeterSignal.DefaultUrgentAt;
```

In `Update`, replace

```csharp
        BarColor = ThemePalette.UsageColor(util);
        PaceTickColor = palette.PaceMarker;
```

with

```csharp
        BarColor = ThemePalette.UsageColor(util);
        Recolor();
        PaceTickColor = palette.PaceMarker;
```

replace `ApplyPalette`'s body

```csharp
        _palette = palette;
        PaceTickColor = palette.PaceMarker;
        SparklineColor = palette.Sparkline;
    }
```

with

```csharp
        _palette = palette;
        PaceTickColor = palette.PaceMarker;
        SparklineColor = palette.Sparkline;
        Recolor();
    }

    /// <summary>The alert thresholds the bar's signal uses (theme match); the widget passes Alerts' Warn at and Urgent at.</summary>
    public void SetThresholds(int warnAt, int urgentAt)
    {
        _warnAt = warnAt;
        _urgentAt = urgentAt;
        Recolor();
    }

    /// <summary>The bar and the percent for the theme now (theme match, "Meters"): a theme with <c>desk.bars</c> fills the bar with that
    /// gradient and works the usage signal into it; any other keeps the ramp exactly.</summary>
    private void Recolor()
    {
        if (_palette.Definition.Desk?.Bars is { Count: > 0 } bars)
        {
            var level = MeterSignal.Level(_util, _warnAt, _urgentAt);
            BarBrush = Sanduhr.App.Desk.DeskView.InkBrush(MeterSignal.Stops(bars, level));
            ValueBrush = Frozen(MeterSignal.Hex(level) is { } hex ? (Color)ColorConverter.ConvertFromString("#" + hex)! : _palette.Text);
        }
        else
        {
            BarBrush = null;
            ValueBrush = Frozen(BarColor);
        }
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
```

Update `ApplyPalette`'s summary: "The bar/value color stays usage-derived (theme-independent) unless the theme sets desk.bars; the pace tick + sparkline pick up the new accent hues…".

In `src/Sanduhr.App/Views/TierCard.xaml`, replace

```xml
                               Foreground="{Binding BarColor, Converter={StaticResource ColorBrush}}" />
```

with

```xml
                               Foreground="{Binding ValueBrush}" />
```

and replace

```xml
                               FillColor="{Binding BarColor}"
```

with

```xml
                               FillColor="{Binding BarColor}"
                               FillBrush="{Binding BarBrush}"
```

In `src/Sanduhr.App/ViewModels/WidgetViewModel.cs`, replace

```csharp
        AlertSettingsChanged += () => _alertConfig = _settings.LoadAlertConfig();
```

with

```csharp
        AlertSettingsChanged += () =>
        {
            _alertConfig = _settings.LoadAlertConfig();
            // The bars' usage signal (theme match) follows Alerts' thresholds.
            foreach (var card in Tiers) card.SetThresholds(_alertConfig.WarnPct, _alertConfig.UrgentPct);
        };
```

and replace

```csharp
                vm = new TierCardViewModel(key, _palette);
                Tiers.Insert(Math.Min(i, Tiers.Count), vm);
```

with

```csharp
                vm = new TierCardViewModel(key, _palette);
                vm.SetThresholds(_alertConfig.WarnPct, _alertConfig.UrgentPct);
                Tiers.Insert(Math.Min(i, Tiers.Count), vm);
```

- [ ] **Step 5: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~WidgetBarsTests|FullyQualifiedName~AppWindowNamesTests"` and `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~TierCardNameTests`
Expected: PASS. The pixel compare is the upgrade guarantee: if it fails, a path changed for themes without `desk.bars`.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Controls/UsageBar.cs windows-dotnet/src/Sanduhr.App/ViewModels/TierCardViewModel.cs windows-dotnet/src/Sanduhr.App/Views/TierCard.xaml windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs windows-dotnet/tests/Sanduhr.App.Tests/WidgetBarsTests.cs
git commit -m "feat(theme-match): the widget's bars draw desk.bars with the usage signal; every other theme keeps the ramp"
```

---

### Task 6: The theme's font draws the widget's text; the App's lint sites check it

**Files:**
- Modify: `src/Sanduhr.App/Theming/ThemePalette.cs` (ctor ~73; `Apply` ~172-175; `ResolveValueFont` ~184-196)
- Modify: `src/Sanduhr.App/MainWindow.xaml:24`
- Modify: `src/Sanduhr.App/Desk/DeskFonts.cs` (after `PickerFamilies` ~39)
- Modify: `src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (a property near `ThemesDir` ~362)
- Modify: `src/Sanduhr.App/App.xaml.cs` (`OnStartup` ~77-80; the fields; `svm.WidgetsPage.Changed` ~728)
- Modify: `src/Sanduhr.Core/ThemeProposals.cs:21-23`, `src/Sanduhr.App/Services/ThemeHandoffService.cs:96`, `src/Sanduhr.App/ViewModels/ThemesViewModel.cs:126`, `src/Sanduhr.App/ViewModels/ThemeStudioViewModel.cs` (`Refresh` and `Save`, the two `_draft.Lint()` calls)
- Test: `tests/Sanduhr.App.Tests/ThemeFontTests.cs` (create), `tests/Sanduhr.Tests/ThemeProposalsTests.cs`

**Interfaces:**
- Consumes: `ThemeDefinition.Font` (Task 1); `ThemeLint.Lint(…, fontAvailable)`, `ThemeDraft.Lint(fontAvailable)` (Tasks 1, 2).
- Produces: `ThemePalette.DefaultUiFamily` (`"Segoe UI Variable Display, Segoe UI"`), `ThemePalette.ResolveFont` (`static Func<string, FontFamily?>?`), ctor `ThemePalette(string key, ThemeDefinition def, Func<string, FontFamily?>? resolveFont = null)`, `ThemePalette.UiFont()`; the resource `Sanduhr.Font.Ui`; `DeskFonts.Has(string family)`; `WidgetViewModel.FontAvailable` (`Func<string, bool>`, default all true); `ThemeProposals.Process(…, Func<string, bool>? fontAvailable = null)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Sanduhr.App.Tests/ThemeFontTests.cs`:

```csharp
using System.IO;
using System.Windows;
using System.Windows.Media;
using Sanduhr.App.Desk;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Theme match: a theme's <c>font</c> draws the widget's text when this PC has the family; Segoe UI otherwise; Matrix and
/// Blueprint keep their monospace values.</summary>
public sealed class ThemeFontTests
{
    private static FontFamily? Only(string family, string name) => name == family ? new FontFamily(family) : null;

    [Fact]
    public void A_theme_without_a_font_keeps_the_widget_in_Segoe_UI() => Sta.Run(() =>
    {
        var res = new ResourceDictionary();
        new ThemePalette("obsidian", ThemeCatalog.BuiltIns["obsidian"], f => Only("Consolas", f)).Apply(res);
        Assert.Equal(ThemePalette.DefaultUiFamily, ((FontFamily)res["Sanduhr.Font.Ui"]).Source);
        Assert.Equal(ThemePalette.DefaultUiFamily, ((FontFamily)res["Sanduhr.Font.Value"]).Source);
    });

    [Fact]
    public void A_themes_font_draws_the_text_and_the_values_when_the_pc_has_it() => Sta.Run(() =>
    {
        var res = new ResourceDictionary();
        new ThemePalette("t", ThemeCatalog.BuiltIns["obsidian"] with { Font = "Consolas" }, f => Only("Consolas", f)).Apply(res);
        Assert.Equal("Consolas", ((FontFamily)res["Sanduhr.Font.Ui"]).Source);
        Assert.Equal("Consolas", ((FontFamily)res["Sanduhr.Font.Value"]).Source);
        // Missing on this PC: Segoe UI, not a family the theme never named.
        new ThemePalette("t", ThemeCatalog.BuiltIns["obsidian"] with { Font = "Nope Sans" }, f => Only("Consolas", f)).Apply(res);
        Assert.Equal(ThemePalette.DefaultUiFamily, ((FontFamily)res["Sanduhr.Font.Ui"]).Source);
        // Matrix keeps its monospace values whatever its font.
        new ThemePalette("matrix", ThemeCatalog.BuiltIns["matrix"] with { Font = "Consolas" }, f => Only("Consolas", f)).Apply(res);
        Assert.Equal("Consolas", ((FontFamily)res["Sanduhr.Font.Ui"]).Source);
        Assert.Equal("Cascadia Code, Consolas", ((FontFamily)res["Sanduhr.Font.Value"]).Source);
    });

    [Fact]
    public void DeskFonts_knows_bundled_installed_and_pack_families() => Sta.Run(() =>
    {
        var fonts = new DeskFonts(["Consolas"], new Dictionary<string, Uri> { ["Pack Hand"] = new Uri("file:///C:/fonts/") });
        Assert.True(fonts.Has("EsteFont Pro"));
        Assert.True(fonts.Has("consolas"));
        Assert.True(fonts.Has("Pack Hand"));
        Assert.False(fonts.Has("Nope Sans"));
    });

    [Fact]
    public void The_widget_window_takes_its_font_from_the_theme()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "MainWindow.xaml"))) dir = dir.Parent;
        Assert.NotNull(dir);
        string xaml = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Sanduhr.App", "MainWindow.xaml"));
        Assert.Contains("FontFamily=\"{DynamicResource Sanduhr.Font.Ui}\"", xaml);
    }
}
```

Append to the class in `tests/Sanduhr.Tests/ThemeProposalsTests.cs`:

```csharp
    [Fact]
    public void A_theme_with_font_and_desk_is_saved_whole_and_a_missing_font_is_a_warning()
    {
        using var tmp = new TempDir();
        string dir = Path.Combine(tmp.Path, "themes");
        var theme = CleanTheme();
        theme["font"] = "Nope Sans";
        theme["desk"] = JsonNode.Parse("""{"bars": ["#6c63ff", "#e8e4dc"]}""");

        var o = ThemeProposals.Process(Req(theme), dir, "obsidian", Reserved, fontAvailable: _ => false);

        Assert.Equal("applied", (string?)o.Payload["status"]);
        Assert.Contains((JsonArray)o.Payload["findings"]!, f => (string?)f!["field"] == "font");
        var saved = ThemeCatalog.LoadUserThemes(dir)["nebula"];
        Assert.Equal("Nope Sans", saved.Font);
        Assert.Equal(["#6c63ff", "#e8e4dc"], saved.Desk!.Bars);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~ThemeFontTests`
Expected: build FAILS with `'ThemePalette' does not contain a constructor that takes 3 arguments` and `'DeskFonts' does not contain a definition for 'Has'`.

- [ ] **Step 3: The palette sets the widget's font**

In `src/Sanduhr.App/Theming/ThemePalette.cs`, replace

```csharp
    public ThemePalette(string key, ThemeDefinition def)
    {
        Key = key;
        Definition = def;
```

with

```csharp
    /// <summary>The widget's text family before theme match, and for a theme without a <c>font</c> this PC has.</summary>
    public const string DefaultUiFamily = "Segoe UI Variable Display, Segoe UI";

    /// <summary>Turns a theme's <c>font</c> into the family to draw, or null when this PC lacks it. App sets it from <c>DeskFonts</c>
    /// (bundled, installed and font-pack families); unset (tests, the harness) a theme's font is not drawn.</summary>
    public static Func<string, FontFamily?>? ResolveFont { get; set; }

    private readonly Func<string, FontFamily?>? _resolveFont;

    /// <param name="resolveFont">This palette's font lookup; null uses <see cref="ResolveFont"/>.</param>
    public ThemePalette(string key, ThemeDefinition def, Func<string, FontFamily?>? resolveFont = null)
    {
        Key = key;
        Definition = def;
        _resolveFont = resolveFont;
```

In `Apply`, replace

```csharp
        res["Sanduhr.Font.Value"] = ResolveValueFont();
```

with

```csharp
        res["Sanduhr.Font.Value"] = ResolveValueFont();

        // The widget's text (theme match): the theme's font when this PC has it, else Segoe UI. MainWindow binds to it.
        res["Sanduhr.Font.Ui"] = UiFont();
```

and in `ResolveValueFont` replace

```csharp
        return new FontFamily("Segoe UI Variable Display, Segoe UI");
    }
```

with

```csharp
        return UiFont();
    }

    /// <summary>The widget's text family: the theme's <c>font</c> when this PC has it, else <see cref="DefaultUiFamily"/>.</summary>
    public FontFamily UiFont() =>
        Definition.Font is { Length: > 0 } f && (_resolveFont ?? ResolveFont)?.Invoke(f) is { } family ? family : new FontFamily(DefaultUiFamily);
```

In `src/Sanduhr.App/MainWindow.xaml`, replace

```xml
        FontFamily="Segoe UI Variable Display, Segoe UI"
```

with

```xml
        FontFamily="{DynamicResource Sanduhr.Font.Ui}"
```

In `src/Sanduhr.App/Desk/DeskFonts.cs`, after `PickerFamilies()` add:

```csharp

    /// <summary>True when this PC can draw <paramref name="family"/>: bundled, installed or from a font pack (theme match's font check).</summary>
    public bool Has(string family) =>
        BundledFonts.Bundled(family) is not null || _installed.Contains(family) || _packs.ContainsKey(family);
```

- [ ] **Step 4: The lint sites know the PC's fonts**

In `src/Sanduhr.App/ViewModels/WidgetViewModel.cs`, after `public string ThemesDir => _paths.ThemesDir;` add:

```csharp

    /// <summary>Whether this PC can draw a font family (bundled, installed or a font pack), for the theme lint's font warning. App sets
    /// it; the default (tests, the harness) answers yes, so no warning appears there.</summary>
    public Func<string, bool> FontAvailable { get; set; } = _ => true;
```

In `src/Sanduhr.Core/ThemeProposals.cs`, replace

```csharp
    public static Outcome Process(ThemeHandoff.Request request, string themesDir, string activeKey, Func<string, bool> isReservedKey)
    {
        var lint = ThemeLint.Lint(request.Theme);
```

with

```csharp
    /// <param name="fontAvailable">This PC's font check (theme match: a <c>font</c> it lacks is a warning in the result); null skips it.</param>
    public static Outcome Process(ThemeHandoff.Request request, string themesDir, string activeKey, Func<string, bool> isReservedKey,
        Func<string, bool>? fontAvailable = null)
    {
        var lint = ThemeLint.Lint(request.Theme, fontAvailable);
```

In `src/Sanduhr.App/Services/ThemeHandoffService.cs`, replace

```csharp
            var outcome = ThemeProposals.Process(request, _paths.ThemesDir, _widget.ActiveThemeKey, ThemeCatalog.BuiltIns.ContainsKey);
```

with

```csharp
            var outcome = ThemeProposals.Process(request, _paths.ThemesDir, _widget.ActiveThemeKey, ThemeCatalog.BuiltIns.ContainsKey,
                _widget.FontAvailable);
```

In `src/Sanduhr.App/ViewModels/ThemesViewModel.cs`, replace `var lint = ThemeLint.Lint(raw);` with `var lint = ThemeLint.Lint(raw, _widget.FontAvailable);`. In `src/Sanduhr.App/ViewModels/ThemeStudioViewModel.cs`, replace both `var lint = _draft.Lint();` with `var lint = _draft.Lint(_widget.FontAvailable);`.

In `src/Sanduhr.App/App.xaml.cs`, add a field beside the other private fields of `App`:

```csharp
    /// <summary>This PC's font families for the theme's font (theme match): drawn when present, warned about when not. Read again when a
    /// font pack comes or goes.</summary>
    private Desk.DeskFonts _themeFonts = new([]);
```

and replace

```csharp
        _vm = new WidgetViewModel();
        // Apply the saved theme's palette to the Application resources before the
```

with

```csharp
        _vm = new WidgetViewModel();
        // A theme's font (theme match): drawn when this PC has the family, bundled, installed or from a font pack; the theme lint warns
        // about one it lacks. Set before the first ApplyThemeResources so the first paint is in the theme's font.
        _themeFonts = Desk.DeskFonts.ForThisPc(new Sanduhr.Core.Paths());
        _vm.FontAvailable = f => _themeFonts.Has(f);
        Sanduhr.App.Theming.ThemePalette.ResolveFont = f => _themeFonts.Has(f) ? _themeFonts.Families(f).Regular : null;
        // Apply the saved theme's palette to the Application resources before the
```

In `svm.WidgetsPage.Changed`, replace

```csharp
            _vm?.ReloadUserThemes();
            _desk?.ResetFonts();
```

with

```csharp
            _themeFonts = Desk.DeskFonts.ForThisPc(new Sanduhr.Core.Paths());
            _vm?.ReloadUserThemes();
            _desk?.ResetFonts();
```

- [ ] **Step 5: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~ThemeFontTests|FullyQualifiedName~PackThemeWiringTests|FullyQualifiedName~ThemeGalleryAutomationTests"` and `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~ThemeProposalsTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Theming/ThemePalette.cs windows-dotnet/src/Sanduhr.App/MainWindow.xaml windows-dotnet/src/Sanduhr.App/Desk/DeskFonts.cs windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs windows-dotnet/src/Sanduhr.App/App.xaml.cs windows-dotnet/src/Sanduhr.Core/ThemeProposals.cs windows-dotnet/src/Sanduhr.App/Services/ThemeHandoffService.cs windows-dotnet/src/Sanduhr.App/ViewModels/ThemesViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/ThemeStudioViewModel.cs windows-dotnet/tests/Sanduhr.App.Tests/ThemeFontTests.cs windows-dotnet/tests/Sanduhr.Tests/ThemeProposalsTests.cs
git commit -m "feat(theme-match): a theme's font draws the widget's text; the paste box, the Studio and Claude's proposals warn about a missing one"
```

---

### Task 7: The Desk can draw a theme look: plate tint, glow color, Now playing's gradient, the meters' bars, track and tick

**Files:**
- Modify: `src/Sanduhr.App/Desk/DeskView.cs` (`DeskFrame`; `Plated` ~522-524; `MeterFill` and new helpers; `BarRow` track and pace ~692-708; `NowPlaying` ~830; `BarsPiece` call ~870 and stops ~963)
- Modify: `src/Sanduhr.App/Desk/DeskView.Styles.cs` (`SlimRow` track ~156; `PaceMark` ~174; `TickRow` `CellColors` and pace mark; `Ring` track ~268, pace line)
- Modify: `src/Sanduhr.App/Desk/DeskMessagePiece.cs` (`DeskMessageFrame` ~16-19; `Create` ~149; `Line` signature ~256 and glow ~287-293)
- Test: `tests/Sanduhr.App.Tests/DeskThemeLookViewTests.cs` (create)

**Interfaces:**
- Consumes: `MeterFill`, `Signal`, `DeskFrame.WarnAt/UrgentAt` (Task 4).
- Produces: `DeskFrame.PlateHex`, `.NowPlayingStops`, `.MeterStops`, `.MeterTrackHex`, `.PaceHex` (all null by default: the Desk's own look, exactly as before); `DeskMessageFrame.GlowHex` (null: the message's middle stop). `DeskView.MeterStops`, `.MeterTrack`, `.MeterTrackOpacity`, `.PaceInk` (private).

- [ ] **Step 1: Write the failing tests**

Create `tests/Sanduhr.App.Tests/DeskThemeLookViewTests.cs`:

```csharp
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Sanduhr.App.Desk;
using Sanduhr.Core;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Sanduhr.App.Tests;

/// <summary>Theme match: the Desk draws a theme's look from the frame (plate tint, glow, Now playing's gradient, the meters' colors,
/// track and tick); with none of it set it draws its own look exactly as before.</summary>
public sealed class DeskThemeLookViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static DeskScreen Screen()
    {
        var bounds = new PxRect(0, 0, 1920, 1080);
        var work = new PxRect(0, 0, 1920, 1032);
        return new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0));
    }

    private static DeskView View(string layout, DeskFrame frame, bool plate = false)
    {
        var settings = (DeskSettings.Default with { Enabled = true, Layout = layout, Font = "Segoe UI", Plate = plate }).Normalized();
        var view = new DeskView(Screen(), settings, new DeskFonts(["Segoe UI"]));
        view.Update(frame, DeskInkMap.Light);
        return view;
    }

    private static IEnumerable<T> All<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T hit) yield return hit;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var found in All<T>(child))
                yield return found;
    }

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString("#" + hex)!;

    private static DeskUsage Usage() => new(
        [new DeskTier(TierModel.FiveHour, 42, null), new DeskTier(TierModel.SevenDay, 63, null)], Now.AddMinutes(-1), false, false, null);

    [Fact]
    public void The_plate_takes_the_frames_tint_and_without_one_stays_black() => Sta.Run(() =>
    {
        Border Plate(DeskView v) => All<Border>(v).Single(b => b.Effect is BlurEffect);
        var own = View("clock:bl", new DeskFrame { Now = Now }, plate: true);
        Assert.Equal(Color.FromArgb(0x4D, 0, 0, 0), ((SolidColorBrush)Plate(own).Background).Color);
        var themed = View("clock:bl", new DeskFrame { Now = Now, PlateHex = "0a0a0a" }, plate: true);
        Assert.Equal(Color.FromArgb(0x4D, 0x0a, 0x0a, 0x0a), ((SolidColorBrush)Plate(themed).Background).Color);
    });

    [Fact]
    public void The_message_glow_takes_the_frames_color() => Sta.Run(() =>
    {
        var settings = DeskMessageSettings.Default with { Glow = true, Color = "6c63ff,e8e4dc" };
        var frame = new DeskFrame { Now = Now, Message = new DeskMessageFrame(new MessageToday(usual: "keep building."), settings) { GlowHex = "38bdf8" } };
        var view = View("message:tl", frame);
        var line = All<TextBlock>(view).Single(t => t.Text == "keep building.");
        Assert.Equal(C("38bdf8"), Assert.IsType<DropShadowEffect>(line.Effect).Color);
        var own = View("message:tl", frame with { Message = frame.Message! with { GlowHex = null } });
        var ownLine = All<TextBlock>(own).Single(t => t.Text == "keep building.");
        Assert.Equal(C("e8e4dc"), Assert.IsType<DropShadowEffect>(ownLine.Effect).Color);   // the middle stop, as before
    });

    [Fact]
    public void Meters_draw_the_themes_bars_track_and_tick() => Sta.Run(() =>
    {
        var frame = new DeskFrame { Now = Now, Usage = Usage(), MeterStops = ["6c63ff", "e8e4dc"], MeterTrackHex = "2a2a2a", PaceHex = "ff6b6b" };
        var view = View("meters:tr", frame);
        var meters = All<FrameworkElement>(view).First(e => e.Tag as string == "meters");
        var rects = All<Rectangle>(meters).ToList();
        var fill = rects.First(r => r.HorizontalAlignment == HorizontalAlignment.Left && r.Width > 2);
        Assert.Equal([C("6c63ff"), C("e8e4dc")], Assert.IsType<LinearGradientBrush>(fill.Fill).GradientStops.Select(s => s.Color));
        Assert.Contains(rects, r => r.Fill is SolidColorBrush b && b.Color == C("2a2a2a"));   // the track
    });

    [Fact]
    public void Now_playings_position_bar_wears_the_frames_gradient() => Sta.Run(() =>
    {
        var track = new NowPlayingInfo { Title = "Song", Artist = "Band", Duration = 200, Elapsed = 50, ElapsedAt = Now, Playing = false };
        var view = View("nowPlaying:br", new DeskFrame { Now = Now, Track = track, NowPlayingStops = ["6c63ff", "e8e4dc"] });
        var bar = All<Rectangle>(view).First(r => r.HorizontalAlignment == HorizontalAlignment.Left && r.Fill is LinearGradientBrush);
        var g = (LinearGradientBrush)bar.Fill;
        Assert.Equal(BrushMappingMode.Absolute, g.MappingMode);
        Assert.Equal([C("6c63ff"), C("e8e4dc")], g.GradientStops.Select(s => s.Color));
    });
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~DeskThemeLookViewTests`
Expected: build FAILS with `'DeskFrame' does not contain a definition for 'PlateHex'` and `'DeskMessageFrame' does not contain a definition for 'GlowHex'`.

- [ ] **Step 3: The frame carries a look**

In `src/Sanduhr.App/Desk/DeskView.cs`, in `DeskFrame`, after the `UrgentAt` property (Task 4) add:

```csharp

    // -- theme match: what a followed theme draws. Null is the Desk's own look, exactly as before. --
    /// <summary>The plate's tint (six hex digits); null is black behind light ink, white behind dark ink.</summary>
    public string? PlateHex { get; init; }
    /// <summary>Now playing's bars and position bar when no song look shows: the theme gradient. Null: the ink (and Gradient bars).</summary>
    public IReadOnlyList<string>? NowPlayingStops { get; init; }
    /// <summary>The meters' colors (desk.bars, else the theme gradient), their track (bar_bg) and pace tick (pace_marker). Null: the ink.</summary>
    public IReadOnlyList<string>? MeterStops { get; init; }
    public string? MeterTrackHex { get; init; }
    public string? PaceHex { get; init; }
```

In `src/Sanduhr.App/Desk/DeskMessagePiece.cs`, replace

```csharp
internal sealed record DeskMessageFrame(MessageToday Today, DeskMessageSettings Settings)
{
    public ISet<string> Played { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}
```

with

```csharp
internal sealed record DeskMessageFrame(MessageToday Today, DeskMessageSettings Settings)
{
    public ISet<string> Played { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    /// <summary>The glow's color (theme match: the followed theme's desk.glow, else its accent); null glows in the line's middle stop.</summary>
    public string? GlowHex { get; init; }
}
```

replace

```csharp
        var built = lines.Select(l => Line(l, s, ink, regular, bold, baseSize, ta, align, width, live, frame.Played, gate)).ToList();
```

with

```csharp
        var built = lines.Select(l => Line(l, s, ink, regular, bold, baseSize, ta, align, width, live, frame.Played, gate, frame.GlowHex)).ToList();
```

replace

```csharp
        double baseSize, TextAlignment ta, HorizontalAlignment align, double width, List<IDisposable> live, ISet<string> played, DeskPause gate)
```

with

```csharp
        double baseSize, TextAlignment ta, HorizontalAlignment align, double width, List<IDisposable> live, ISet<string> played, DeskPause gate,
        string? glowHex = null)
```

and replace

```csharp
                Color = DeskView.Rgb(stops[stops.Count / 2]),
```

with

```csharp
                Color = DeskView.Rgb(glowHex ?? stops[stops.Count / 2]),
```

- [ ] **Step 4: The view draws it**

In `src/Sanduhr.App/Desk/DeskView.cs`, in `Plated`, replace

```csharp
        var c = Rgb(DeskPlate.ColorHex(_ink.DarkInk));
```

with

```csharp
        var c = Rgb(_frame.PlateHex ?? DeskPlate.ColorHex(_ink.DarkInk));
```

Replace the `MeterFill` helper from Task 4 with:

```csharp
    /// <summary>A meter's colors: the frame's (a followed theme's bars) or the ink.</summary>
    private IReadOnlyList<string> MeterStops => _frame.MeterStops ?? _ink.Stops;

    /// <summary>A meter's fill (theme match, the usage signal): its colors, and past Warn at or Urgent at with a yellow or red stop in
    /// their middle. Calm in the Desk's own look, it is <see cref="Ink"/> itself, so such a meter draws exactly as it always has.</summary>
    private Brush MeterFill(MeterLevel level) =>
        level == MeterLevel.Calm && _frame.MeterStops is null ? Ink : InkBrush(MeterSignal.Stops(MeterStops, level));

    /// <summary>A meter's track: a followed theme's bar_bg, else the ink (at <see cref="MeterTrackOpacity"/>).</summary>
    private Brush MeterTrack => _frame.MeterTrackHex is { } hex ? SolidInk(hex) : Ink;

    /// <summary>The ink's track is the ink at 22%; a theme's bar_bg is its own color, nearly solid.</summary>
    private double MeterTrackOpacity => _frame.MeterTrackHex is null ? 0.22 : 0.9;

    /// <summary>A meter's pace tick: a followed theme's pace_marker, else the ink.</summary>
    private Brush PaceInk => _frame.PaceHex is { } hex ? SolidInk(hex) : Ink;
```

In `BarRow`, replace

```csharp
            Height = barHeight, RadiusX = barHeight / 2, RadiusY = barHeight / 2,
            Fill = Ink, Opacity = 0.22, VerticalAlignment = VerticalAlignment.Center,
```

with

```csharp
            Height = barHeight, RadiusX = barHeight / 2, RadiusY = barHeight / 2,
            Fill = MeterTrack, Opacity = MeterTrackOpacity, VerticalAlignment = VerticalAlignment.Center,
```

and

```csharp
                Width = 2, Height = barHeight * 2, Fill = Ink, Opacity = _ink.TextOpacity,
```

with

```csharp
                Width = 2, Height = barHeight * 2, Fill = PaceInk, Opacity = _ink.TextOpacity,
```

In `NowPlaying`, replace

```csharp
        Brush barInk = look is null ? Ink : AbsoluteGradient(look.Stops, width);
```

with

```csharp
        // A followed theme's gradient (theme match) when no song look shows.
        Brush barInk = look is not null ? AbsoluteGradient(look.Stops, width)
            : _frame.NowPlayingStops is { } themed ? AbsoluteGradient(themed, width) : Ink;
```

Replace

```csharp
            var bars = BarsPiece(size, audio, look, _frame.BarsGradient);
```

with

```csharp
            var bars = BarsPiece(size, audio, look, _frame.BarsGradient || _frame.NowPlayingStops is not null);
```

and in `BarsPiece` replace

```csharp
        IReadOnlyList<string>? stops = look?.Stops ?? (gradient ? _ink.Stops : null);
```

with

```csharp
        IReadOnlyList<string>? stops = look?.Stops ?? _frame.NowPlayingStops ?? (gradient ? _ink.Stops : null);
```

In `src/Sanduhr.App/Desk/DeskView.Styles.cs`, in `SlimRow` replace

```csharp
            Height = barH, RadiusX = barH / 2, RadiusY = barH / 2, Fill = Ink, Opacity = 0.22, VerticalAlignment = VerticalAlignment.Center,
```

with

```csharp
            Height = barH, RadiusX = barH / 2, RadiusY = barH / 2, Fill = MeterTrack, Opacity = MeterTrackOpacity, VerticalAlignment = VerticalAlignment.Center,
```

in `PaceMark` replace

```csharp
        Width = 2, Height = height, Fill = Ink, Opacity = _ink.TextOpacity,
```

with

```csharp
        Width = 2, Height = height, Fill = PaceInk, Opacity = _ink.TextOpacity,
```

in `TickRow`'s `CellColors` replace

```csharp
            var stops = MeterSignal.Stops(_ink.Stops, level);
            bool spread = stops.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
            var solid = Ink;
```

with

```csharp
            var stops = MeterSignal.Stops(MeterStops, level);
            bool spread = stops.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
            var solid = _frame.MeterStops is null ? Ink : SolidInk(stops[0]);
```

and its pace mark

```csharp
            var mark = new Rectangle { Width = 2, Height = ch + 2 * over, Fill = Ink, Opacity = _ink.TextOpacity };
```

with

```csharp
            var mark = new Rectangle { Width = 2, Height = ch + 2 * over, Fill = PaceInk, Opacity = _ink.TextOpacity };
```

In `Ring`, replace the track

```csharp
            Data = Arc(c, radius, 1), Stroke = Ink, StrokeThickness = t, Opacity = 0.22,
```

with

```csharp
            Data = Arc(c, radius, 1), Stroke = MeterTrack, StrokeThickness = t, Opacity = MeterTrackOpacity,
```

and the pace line's `Stroke = Ink, StrokeThickness = 2, Opacity = _ink.TextOpacity,` with `Stroke = PaceInk, StrokeThickness = 2, Opacity = _ink.TextOpacity,`.

- [ ] **Step 5: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskThemeLookViewTests|FullyQualifiedName~DeskMeterStylesViewTests|FullyQualifiedName~DeskRideAlongsViewTests|FullyQualifiedName~NowPlayingLooksDeskTests|FullyQualifiedName~NowPlayingReflowTests"`
Expected: PASS; the existing view tests are the proof that a frame without the new fields draws as before.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Desk/DeskView.cs windows-dotnet/src/Sanduhr.App/Desk/DeskView.Styles.cs windows-dotnet/src/Sanduhr.App/Desk/DeskMessagePiece.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskThemeLookViewTests.cs
git commit -m "feat(theme-match): the Desk can draw a theme's plate tint, glow, Now playing gradient and meter colors from the frame"
```

---

### Task 8: Follow on, the Desk draws the theme's look; off, its own settings. The one new key.

**Files:**
- Create: `src/Sanduhr.App/Desk/DeskLookResolver.cs`
- Modify: `src/Sanduhr.App/Services/SettingsStore.cs` (after `SaveDeskPlate` ~793)
- Modify: `src/Sanduhr.App/Desk/DeskController.cs` (fields ~43-47; `ThemeText` ~296; `Reload` ~303-320; `ThemeChanged` ~377-381; `RenderOnce` ~686-722; `InkFor` ~746-768)
- Modify: `src/Sanduhr.App/App.xaml.cs` (~621, and next to `_desk.Reload();` ~624)
- Test: `tests/Sanduhr.Tests/SettingsKeysContractTests.cs` (the frozen list), `tests/Sanduhr.App.Tests/DeskFollowThemeTests.cs` (create)

**Interfaces:**
- Consumes: `ThemeDeskLook` (Task 3), the frame fields (Tasks 4, 7), `DeskDesignApply.FontSetting`.
- Produces: `SettingsStore.LoadDeskFollowsTheme()`, `SaveDeskFollowsTheme(bool)` (key `desk_follows_theme`, written only while on); `internal sealed record DeskThemeDraw(ThemeDeskLook Look, ResolvedInk Ink)`; `internal static class DeskLookResolver { DeskThemeDraw? For(bool follows, ThemeDefinition? theme); DeskSettings Settings(DeskSettings own, DeskThemeDraw? draw); DeskFrame Frame(DeskFrame frame, DeskThemeDraw? draw) }`; `DeskController.ThemeChanged(ThemeDefinition theme)` (replaces the parameterless one); `DeskController.PlateForced` (bool), `event Action? PlateForcedChanged`; `internal Func<DeskScreen, PxRect, double?>? WallpaperLuminance` (a test seam over the wallpaper sample).

- [ ] **Step 1: Write the failing tests**

In `tests/Sanduhr.Tests/SettingsKeysContractTests.cs`, in `Frozen`, replace

```csharp
        "desk_enabled",
        "desk_font",
```

with

```csharp
        "desk_enabled",
        "desk_follows_theme",
        "desk_font",
```

Create `tests/Sanduhr.App.Tests/DeskFollowThemeTests.cs`:

```csharp
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Theme match, "Follow the widget theme": with <c>desk_follows_theme</c> on the Desk draws the active theme's look and redraws on every
/// theme change; off, it draws its own settings untouched. The readability guard turns a plateless look's plate on where its ink would not
/// read. Over a scratch AppData, with the wallpaper sample replaced.
/// </summary>
public sealed class DeskFollowThemeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-follow-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    public DeskFollowThemeTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        _store = new SettingsStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private sealed class FakeHost : IDeskHost
    {
        public event Action? ScreensChanged { add { } remove { } }
        public event Action? CoverageChanged { add { } remove { } }
        public IReadOnlyList<DeskSurface> Shown { get; private set; } = [];
        public bool IsShowing { get; private set; }
        public IReadOnlyList<bool> Coverage => [];

        public IReadOnlyList<DeskScreen> Screens()
        {
            var bounds = new PxRect(0, 0, 1920, 1080);
            var work = new PxRect(0, 0, 1920, 1032);
            return [new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0))];
        }

        public void Show(IReadOnlyList<DeskSurface> surfaces) { Shown = surfaces; IsShowing = true; }
        public void Hide() { Shown = []; IsShowing = false; }
        public void SetDrift(int? step) { }
        public void SetDim(double opacity, TimeSpan ease) { }
        public void Dispose() { }
    }

    private (DeskController Desk, FakeHost Host) Start(bool follows, double wallpaper = 0.05)
    {
        _store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl", Font = "" });
        _store.SaveDeskFollowsTheme(follows);
        var host = new FakeHost();
        var desk = new DeskController(_store, () => host, () => DeskUsage.Empty, new NowPlayingHub(), new DeskMessageSource(_store, _paths), paths: _paths)
        {
            WallpaperLuminance = (_, _) => wallpaper,
        };
        desk.ThemeChanged(ThemeCatalog.BuiltIns["obsidian"]);
        desk.Reload();
        return (desk, host);
    }

    private static IEnumerable<T> All<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T hit) yield return hit;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var found in All<T>(child))
                yield return found;
    }

    /// <summary>The clock's digits (the bold text of the clock piece).</summary>
    private static TextBlock Clock(FakeHost host) =>
        host.Shown.SelectMany(s => s.Islands).SelectMany(i => All<TextBlock>(i.Content)).First(t => t.FontWeight == FontWeights.Bold);

    private static Color[] Stops(Brush b) => b is LinearGradientBrush g ? [.. g.GradientStops.Select(s => s.Color)] : [((SolidColorBrush)b).Color];

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString("#" + hex)!;

    [Fact]
    public void Following_draws_the_themes_ink_and_off_draws_the_desks_own() => Sta.Run(() =>
    {
        var (desk, host) = Start(follows: true);
        try
        {
            Assert.Equal([C("e8e4dc"), C("6c63ff")], Stops(Clock(host).Foreground));   // Obsidian: text to accent
            _store.SaveDeskFollowsTheme(false);
            desk.Reload();
            Assert.Equal(C("ffffff"), Stops(Clock(host).Foreground)[0]);              // its own Automatic ink on a dark wallpaper: white
        }
        finally { desk.Dispose(); }
    });

    [Fact]
    public void Following_restyles_on_every_theme_change() => Sta.Run(() =>
    {
        var (desk, host) = Start(follows: true);
        try
        {
            desk.ThemeChanged(ThemeCatalog.BuiltIns["aurora"]);
            Assert.Equal([C("e2e8f0"), C("38bdf8")], Stops(Clock(host).Foreground));
            // A theme's font comes with it (Consolas: on every Windows, so the Desk's font check never swaps it for EsteFont Pro).
            desk.ThemeChanged(ThemeCatalog.BuiltIns["aurora"] with { Font = "Consolas" });
            Assert.Equal("Consolas", Clock(host).FontFamily.Source);
        }
        finally { desk.Dispose(); }
    });

    [Fact]
    public void A_studio_preview_reaches_a_following_desk() => Sta.Run(() =>
    {
        var (desk, host) = Start(follows: true);
        try
        {
            // The Studio's preview raises ThemeChanged with a draft definition (a "studio" palette): the Desk draws it.
            desk.ThemeChanged(ThemeCatalog.BuiltIns["obsidian"] with { Desk = new ThemeDesk { Ink = ["#ffd08a"] } });
            Assert.Equal(C("ffd08a"), Stops(Clock(host).Foreground)[0]);
        }
        finally { desk.Dispose(); }
    });

    [Fact]
    public void A_desk_that_does_not_follow_ignores_theme_changes() => Sta.Run(() =>
    {
        var (desk, host) = Start(follows: false);
        try
        {
            var before = Clock(host);
            desk.ThemeChanged(ThemeCatalog.BuiltIns["aurora"]);   // Automatic ink: no redraw
            Assert.Same(before, Clock(host));
        }
        finally { desk.Dispose(); }
    });

    [Fact]
    public void The_guard_turns_a_plateless_looks_plate_on_where_the_ink_would_not_read() => Sta.Run(() =>
    {
        var plateless = ThemeCatalog.BuiltIns["obsidian"] with { Desk = new ThemeDesk { Plate = false } };
        var (desk, host) = Start(follows: true, wallpaper: 0.9);
        int raised = 0;
        desk.PlateForcedChanged += () => raised++;
        try
        {
            desk.ThemeChanged(plateless);
            Assert.True(desk.PlateForced);
            Assert.Equal(1, raised);
            desk.WallpaperLuminance = (_, _) => 0.05;
            desk.Reload();
            Assert.False(desk.PlateForced);
            Assert.Equal(2, raised);
        }
        finally { desk.Dispose(); }
    });

    [Fact]
    public void The_resolver_leaves_the_own_look_alone_and_carries_the_theme_look_when_following()
    {
        var own = DeskSettings.Default with { Font = "", Plate = false };
        var frame = new DeskFrame();
        Assert.Null(DeskLookResolver.For(false, ThemeCatalog.BuiltIns["obsidian"]));
        Assert.Null(DeskLookResolver.For(true, null));
        Assert.Same(own, DeskLookResolver.Settings(own, null));
        Assert.Same(frame, DeskLookResolver.Frame(frame, null));

        var matrix = DeskLookResolver.For(true, ThemeCatalog.BuiltIns["matrix"])!;
        Assert.Equal(["00ff41", "00ff41"], matrix.Ink.Stops);
        Assert.Equal("010501", matrix.Ink.ShadowHex);
        Assert.True(matrix.Ink.ShadowOpacity > 0);
        var s = DeskLookResolver.Settings(own, matrix);
        Assert.Equal("Cascadia Code", s.Font);
        Assert.True(s.Plate);
        Assert.Equal("", DeskLookResolver.Settings(own, DeskLookResolver.For(true, ThemeCatalog.BuiltIns["aurora"])).Font);   // no theme font: its own

        var msg = new DeskMessageFrame(new MessageToday(usual: "keep building."), DeskMessageSettings.Default with { Glow = false });
        var themed = DeskLookResolver.Frame(frame with { Message = msg }, DeskLookResolver.For(true, ThemeCatalog.BuiltIns["obsidian"]));
        Assert.Equal("0a0a0a", themed.PlateHex);
        Assert.Equal(["6c63ff", "e8e4dc"], themed.NowPlayingStops);
        Assert.Equal(["6c63ff", "e8e4dc"], themed.MeterStops);
        Assert.Equal("2a2a2a", themed.MeterTrackHex);
        Assert.Equal("ff6b6b", themed.PaceHex);
        Assert.Equal("6c63ff,e8e4dc", themed.Message!.Settings.Color);
        Assert.True(themed.Message.Settings.Glow);
        Assert.Equal("6c63ff", themed.Message.GlowHex);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~DeskFollowThemeTests`
Expected: build FAILS with `'SettingsStore' does not contain a definition for 'SaveDeskFollowsTheme'` and `The name 'DeskLookResolver' does not exist`.

- [ ] **Step 3: The key**

In `src/Sanduhr.App/Services/SettingsStore.cs`, after `SaveDeskPlate` add:

```csharp

    /// <summary>"Follow the widget theme" (theme match): <c>desk_follows_theme</c>, absent = off. While on, the Desk draws the active
    /// theme's look and leaves its own look settings untouched for when it goes off. Written only while on.</summary>
    public bool LoadDeskFollowsTheme() => ReadBool(Read(), "desk_follows_theme", false);

    public void SaveDeskFollowsTheme(bool on)
    {
        var root = Read();
        if (on) root["desk_follows_theme"] = true;
        else root.Remove("desk_follows_theme");
        Write(root);
    }
```

- [ ] **Step 4: The resolver**

Create `src/Sanduhr.App/Desk/DeskLookResolver.cs`:

```csharp
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>What the Desk draws while it follows the widget theme (theme match): the theme's look, and the ink made of it.</summary>
internal sealed record DeskThemeDraw(ThemeDeskLook Look, ResolvedInk Ink);

/// <summary>
/// Follow on: the active theme's look (<see cref="ThemeDeskLook.From"/>); off: the Desk's own settings, untouched (theme match, spec
/// "Follow the widget theme"). Pure over its inputs, so the controller, the harness and the tests draw the same look. Sizes, margins,
/// OLED care and the layout are never part of a look.
/// </summary>
internal static class DeskLookResolver
{
    /// <summary>The look to draw, or null when the Desk draws its own (not following, or no theme known yet).</summary>
    public static DeskThemeDraw? For(bool follows, ThemeDefinition? theme)
    {
        if (!follows || theme is null) return null;
        var look = ThemeDeskLook.From(theme);
        // A custom ink of the look's stops, with its shadow on in the theme's shade: DeskInk's own rules decide dark ink and opacities.
        var ink = DeskInk.Resolve(new DeskInkSettings(DeskInkMode.Custom, string.Join(",", look.Ink), true), null, null) with { ShadowHex = look.Shadow };
        return new DeskThemeDraw(look, ink);
    }

    /// <summary>The view's settings: the look's font (when it has one; else the Desk keeps its own) and plate.</summary>
    public static DeskSettings Settings(DeskSettings own, DeskThemeDraw? draw) => draw is null ? own : own with
    {
        Font = draw.Look.Font is { } f ? DeskDesignApply.FontSetting(new DeskFontChoice(f)) : own.Font,
        Plate = draw.Look.Plate,
    };

    /// <summary>The frame with the look's plate tint, Now playing gradient, meter colors, track and tick, and the message in the look's
    /// colors with its glow on in the look's glow color (a line's own <c>{ink:}</c> still wins).</summary>
    public static DeskFrame Frame(DeskFrame frame, DeskThemeDraw? draw)
    {
        if (draw is null) return frame;
        var look = draw.Look;
        return frame with
        {
            PlateHex = look.PlateColor,
            NowPlayingStops = look.Gradient,
            MeterStops = look.Bars,
            MeterTrackHex = look.BarTrack,
            PaceHex = look.PaceTick,
            Message = frame.Message is { } m
                ? m with { Settings = m.Settings with { Color = string.Join(",", look.Message), Glow = true }, GlowHex = look.Glow }
                : null,
        };
    }
}
```

- [ ] **Step 5: The controller**

In `src/Sanduhr.App/Desk/DeskController.cs`, after `private LauncherModel _launcher = LauncherModel.Empty;` add:

```csharp
    /// <summary>"Follow the widget theme" (theme match): the Desk draws <see cref="_theme"/>'s look instead of its own settings.</summary>
    private bool _follows;
    /// <summary>The widget's theme as last handed over (a switch, a proposal, a Studio preview); the look a following Desk draws.</summary>
    private ThemeDefinition? _theme;
    /// <summary>Alerts' thresholds, for the meters' usage signal.</summary>
    private int _warnAt = MeterSignal.DefaultWarnAt, _urgentAt = MeterSignal.DefaultUrgentAt;
    private bool _plateForced;
```

After `public Func<string?> ThemeText { get; set; } = () => null;` add:

```csharp

    /// <summary>True while a followed look's plate is on only because its ink would not read over this wallpaper (the readability guard);
    /// Settings, Desk Look says so.</summary>
    public bool PlateForced => _plateForced;

    /// <summary>Raised when <see cref="PlateForced"/> changes.</summary>
    public event Action? PlateForcedChanged;

    /// <summary>The wallpaper's luminance behind a region (tests replace it); null reads the wallpaper file.</summary>
    internal Func<DeskScreen, PxRect, double?>? WallpaperLuminance { get; set; }

    private double? Luminance(DeskScreen screen, PxRect px) => WallpaperLuminance is { } f ? f(screen, px) : _wallpaper.Luminance(screen, px);
```

In `Reload`, replace

```csharp
        _launcher = _store.LoadLauncher();
        _oledOn = _store.LoadOledCare();
```

with

```csharp
        _launcher = _store.LoadLauncher();
        _follows = _store.LoadDeskFollowsTheme();
        var alerts = _store.LoadAlertConfig();
        (_warnAt, _urgentAt) = (alerts.WarnPct, alerts.UrgentPct);
        _oledOn = _store.LoadOledCare();
```

Replace

```csharp
    /// <summary>The widget's theme changed: Theme ink follows it.</summary>
    public void ThemeChanged()
    {
        if (_ink.Mode == DeskInkMode.Theme) Render();
    }
```

with

```csharp
    /// <summary>The widget's theme changed (a switch, a Claude proposal, Put my Desk back, a Studio preview): Theme ink and a Desk that
    /// follows the theme redraw from <paramref name="theme"/>.</summary>
    public void ThemeChanged(ThemeDefinition theme)
    {
        _theme = theme;
        if (_follows || _ink.Mode == DeskInkMode.Theme) Render();
    }
```

In `RenderOnce`, replace

```csharp
            Launcher = LauncherNow(),
            Companion = CompanionNow(),
        };
        _fullAt = now;
        foreach (var old in _views) old.Dispose();
        _views.Clear();
        var surfaces = new List<DeskSurface>();
        foreach (var screen in _screens)
        {
            var view = new DeskView(screen, _settings with { Layout = LayoutFor(screen) }, _fonts);
            view.Update(frame, InkFor(view, screen, frame));
            _views.Add(view);
            surfaces.Add(new DeskSurface(screen, DeskClicks.Prepare(view.TakeIslands(), _piecesTakeClicks, this)));
        }
        _surfaces = surfaces;
```

with

```csharp
            Launcher = LauncherNow(),
            Companion = CompanionNow(),
            WarnAt = _warnAt,
            UrgentAt = _urgentAt,
        };
        // Following the widget theme (theme match): the theme's look in place of the Desk's own.
        var draw = DeskLookResolver.For(_follows, _theme);
        frame = DeskLookResolver.Frame(frame, draw);
        _fullAt = now;
        foreach (var old in _views) old.Dispose();
        _views.Clear();
        var surfaces = new List<DeskSurface>();
        bool forced = false;
        foreach (var screen in _screens)
        {
            var settings = DeskLookResolver.Settings(_settings, draw) with { Layout = LayoutFor(screen) };
            var view = new DeskView(screen, settings, _fonts);
            var inks = InkFor(view, screen, frame, draw);
            // The readability guard: a followed look without its plate gets it where its ink would not read over this wallpaper.
            if (draw is not null && !settings.Plate && !Reads(view, screen, frame, inks, draw.Look))
            {
                view.Dispose();
                view = new DeskView(screen, settings with { Plate = true }, _fonts);
                forced = true;
            }
            view.Update(frame, inks);
            _views.Add(view);
            surfaces.Add(new DeskSurface(screen, DeskClicks.Prepare(view.TakeIslands(), _piecesTakeClicks, this)));
        }
        if (forced != _plateForced)
        {
            _plateForced = forced;
            PlateForcedChanged?.Invoke();
        }
        _surfaces = surfaces;
```

Replace the whole `InkFor` method (from its `/// <summary>` through its closing brace) with:

```csharp
    /// <summary>
    /// The inks for one screen (spec 6.7). A followed theme's look is one ink for the screen. Every mode but Automatic is one ink too.
    /// Automatic lays the screen out once in Light ink, reads the wallpaper's brightness behind each stack (from the wallpaper file, never
    /// the screen), and the view draws each stack in the ink that reading picks.
    /// </summary>
    private DeskInkMap InkFor(DeskView view, DeskScreen screen, DeskFrame frame, DeskThemeDraw? draw)
    {
        if (draw is not null)
            return new DeskInkMap(Care(draw.Ink));
        if (_ink.Mode != DeskInkMode.Auto)
            return new DeskInkMap(Care(DeskInk.Resolve(_ink, ThemeText(), null)));
        view.Update(Scratch(frame), DeskInkMap.Light);
        var by = new Dictionary<DeskAnchor, ResolvedInk>();
        foreach (var (anchor, r) in view.StackRects())
            by[anchor] = Care(DeskInk.Resolve(_ink, ThemeText(), Luminance(screen, Px(screen, r))));
        return new DeskInkMap(Care(DeskInk.Resolve(_ink, ThemeText(), null)), by);
    }

    /// <summary>True when a followed look's ink reads over the wallpaper behind every stack on this screen; a stack with no sample reads.</summary>
    private bool Reads(DeskView view, DeskScreen screen, DeskFrame frame, DeskInkMap inks, ThemeDeskLook look)
    {
        view.Update(Scratch(frame), inks);
        foreach (var (_, r) in view.StackRects())
            if (Luminance(screen, Px(screen, r)) is { } l && !double.IsNaN(l) && !look.ReadsOver(l)) return false;
        return true;
    }

    /// <summary>A sampling pass draws nothing anyone sees: its message gets a scratch copy of the played set, so the drawn pass still
    /// writes a new line in.</summary>
    private static DeskFrame Scratch(DeskFrame frame) =>
        frame.Message is { } m ? frame with { Message = m with { Played = new HashSet<string>(m.Played, StringComparer.Ordinal) } } : frame;

    /// <summary>A stack's rect in the view's DIPs as the screen's pixels.</summary>
    private static PxRect Px(DeskScreen screen, Rect r) => new(
        screen.Bounds.X + (int)Math.Round(r.X * screen.Scale), screen.Bounds.Y + (int)Math.Round(r.Y * screen.Scale),
        Math.Max(1, (int)Math.Round(r.Width * screen.Scale)), Math.Max(1, (int)Math.Round(r.Height * screen.Scale)));
```

(`Rect` is `System.Windows.Rect`; the file already uses `System.Windows`.)

- [ ] **Step 6: Wire it**

In `src/Sanduhr.App/App.xaml.cs`, replace

```csharp
        _vm.ThemeChanged += _ => _desk?.ThemeChanged();
```

with

```csharp
        _vm.ThemeChanged += p => _desk?.ThemeChanged(p.Definition);
        // The meters' usage signal follows Alerts' thresholds.
        _vm.AlertSettingsChanged += () => _desk?.Reload();
```

and in the same method replace

```csharp
        _vm.NowPlaying = new NowPlayingLineViewModel(_nowPlaying, store);
        _desk.Reload();
```

with

```csharp
        _vm.NowPlaying = new NowPlayingLineViewModel(_nowPlaying, store);
        // The theme a following Desk draws from (theme match), before the first redraw.
        _desk.ThemeChanged(_vm.Palette.Definition);
        _desk.Reload();
```

- [ ] **Step 7: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskFollowThemeTests|FullyQualifiedName~OledCareTests|FullyQualifiedName~DeskReviewPlacementTests"` and `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsKeysContractTests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Desk/DeskLookResolver.cs windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs windows-dotnet/src/Sanduhr.App/Desk/DeskController.cs windows-dotnet/src/Sanduhr.App/App.xaml.cs windows-dotnet/tests/Sanduhr.Tests/SettingsKeysContractTests.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskFollowThemeTests.cs
git commit -m "feat(theme-match): desk_follows_theme: the Desk draws the active theme's look, restyles on every theme change, and guards readability"
```

---

### Task 9: `ThemeMatch`: who owns a theme, the names and keys Follow makes, the spelled-out block, Match now's copy

**Files:**
- Create: `src/Sanduhr.Core/ThemeMatch.cs`
- Test: `tests/Sanduhr.Tests/ThemeMatchTests.cs` (create)

**Interfaces:**
- Consumes: `ThemeDeskLook` (Task 3), `ThemeDraft.Desk` (Task 2), `ThemeHandoff.Slugify/IsValidKey`, `DeskDesignApply.FontSetting`.
- Produces: `public enum ThemeOrigin { BuiltIn, Pack, User }`; `public sealed record ThemeMatchOwn(DeskSettings Desk, DeskInkSettings Ink, DeskMessageSettings Message, NowPlayingSettings NowPlaying)`; `public static class ThemeMatch { const string DeskSuffix = " + Desk"; ThemeOrigin Origin(string key); string NewThemeName(string themeName, Func<string, bool> taken); string NewThemeKey(string name, Func<string, bool> taken); string? CleanName(string? raw); JsonObject WithDesk(ThemeDefinition def, ThemeDesk? desk, string? name = null); ThemeDesk SpelledOut(ThemeDefinition def); ThemeMatchOwn OwnSettings(ThemeDeskLook look, DeskSettings desk, DeskInkSettings ink, DeskMessageSettings message, NowPlayingSettings np) }`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Sanduhr.Tests/ThemeMatchTests.cs`:

```csharp
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Theme match's pure rules: who owns a theme, the name and key of the theme Follow makes, the look spelled out into a theme,
/// and what Match now writes into the Desk's own settings.</summary>
public class ThemeMatchTests
{
    [Theory]
    [InlineData("obsidian", ThemeOrigin.BuiltIn)]
    [InlineData("good-theme:fixture-dark", ThemeOrigin.Pack)]
    [InlineData("dusk", ThemeOrigin.User)]
    public void Origin_says_who_owns_the_theme(string key, ThemeOrigin origin) => Assert.Equal(origin, ThemeMatch.Origin(key));

    [Fact]
    public void The_new_themes_name_is_the_theme_plus_Desk_made_unique()
    {
        Assert.Equal("Obsidian + Desk", ThemeMatch.NewThemeName("Obsidian", _ => false));
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Obsidian + Desk", "Obsidian + Desk 2" };
        Assert.Equal("Obsidian + Desk 3", ThemeMatch.NewThemeName("Obsidian", taken.Contains));
        // Following again from a "+ Desk" theme does not stack the suffix.
        Assert.Equal("Obsidian + Desk 3", ThemeMatch.NewThemeName("Obsidian + Desk", taken.Contains));
        Assert.Equal("Obsidian + Desk 3", ThemeMatch.NewThemeName("Obsidian + Desk 2", taken.Contains));
    }

    [Fact]
    public void A_long_name_is_cut_so_the_suffix_fits_the_strip()
    {
        string name = ThemeMatch.NewThemeName("Midnight Aurora Borealis", _ => false);   // 24 characters, the limit
        Assert.Equal("Midnight Aurora B + Desk", name);
        Assert.True(name.Length <= ThemeLint.MaxNameLength);
        Assert.True(ThemeLint.Lint(ThemeMatch.WithDesk(ThemeCatalog.BuiltIns["obsidian"], null, name)).Ok);
    }

    [Fact]
    public void The_file_key_never_lands_on_a_built_in_or_a_file_that_is_there()
    {
        Assert.Equal("obsidian-desk", ThemeMatch.NewThemeKey("Obsidian + Desk", _ => false));
        Assert.Equal("obsidian-desk-2", ThemeMatch.NewThemeKey("Obsidian + Desk", k => k == "obsidian-desk"));
        Assert.Equal("obsidian-2", ThemeMatch.NewThemeKey("Obsidian", _ => false));   // a built-in's key is never taken over
    }

    [Theory]
    [InlineData("  Dusk  ", "Dusk")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData("A name far longer than the strip allows", "A name far longer than t")]
    public void CleanName_trims_and_cuts(string? raw, string? expected) => Assert.Equal(expected, ThemeMatch.CleanName(raw));

    [Fact]
    public void Follow_spells_the_look_out_but_leaves_bars_and_font_as_the_theme_had_them()
    {
        var obsidian = ThemeCatalog.BuiltIns["obsidian"];
        var desk = ThemeMatch.SpelledOut(obsidian);
        Assert.Equal(["#e8e4dc", "#6c63ff"], desk.Ink);
        Assert.Equal(["#6c63ff", "#e8e4dc"], desk.Message);
        Assert.Equal("#6c63ff", desk.Glow);
        Assert.True(desk.Plate);
        Assert.Equal("#0a0a0a", desk.PlateColor);
        Assert.Equal("#070707", desk.Shadow);
        Assert.Null(desk.Bars);   // writing bars would turn the widget's ramp into a gradient
        Assert.Null(desk.Font);
        // Spelled out, it is the same look.
        Assert.Equal(ThemeDeskLook.From(obsidian).ToJson().ToJsonString(), ThemeDeskLook.From(obsidian with { Desk = desk }).ToJson().ToJsonString());
        // A theme that set bars and a desk font keeps them.
        var own = ThemeMatch.SpelledOut(obsidian with { Desk = new ThemeDesk { Bars = ["#ff0000"], Font = "Consolas" } });
        Assert.Equal(["#ff0000"], own.Bars);
        Assert.Equal("Consolas", own.Font);
    }

    [Fact]
    public void WithDesk_keeps_every_other_field_of_the_theme()
    {
        var aurora = ThemeCatalog.BuiltIns["aurora"] with { Font = "Segoe UI" };
        var back = ThemeLint.Lint(ThemeMatch.WithDesk(aurora, ThemeMatch.SpelledOut(aurora), "Aurora + Desk")).Theme!;
        Assert.Equal(aurora with { Name = "Aurora + Desk", Desk = back.Desk }, back);
        Assert.Equal("#38bdf8", back.InnerHighlight!.Color);
        Assert.NotNull(back.Desk);
    }

    [Fact]
    public void Match_now_writes_what_the_desks_own_settings_hold()
    {
        var look = ThemeDeskLook.From(ThemeCatalog.BuiltIns["matrix"]);
        var own = ThemeMatch.OwnSettings(look, DeskSettings.Default with { Plate = false, Font = "" }, DeskInkSettings.Default with { Shadow = false },
            DeskMessageSettings.Default with { Glow = false, Size = 70 }, NowPlayingSettings.Default);
        Assert.Equal(DeskInkMode.Custom, own.Ink.Mode);
        Assert.Equal("00ff41", own.Ink.Color);
        Assert.True(own.Ink.Shadow);
        Assert.Equal("Cascadia Code", own.Desk.Font);
        Assert.True(own.Desk.Plate);
        Assert.Equal("00ff41", own.Message.Color);
        Assert.True(own.Message.Glow);
        Assert.Equal(70, own.Message.Size);   // sizes are no part of a look
        Assert.True(own.NowPlaying.BarsGradient);
    }

    [Fact]
    public void A_theme_without_a_font_leaves_the_desks_own_font()
    {
        var own = ThemeMatch.OwnSettings(ThemeDeskLook.From(ThemeCatalog.BuiltIns["aurora"]), DeskSettings.Default with { Font = "Consolas" },
            DeskInkSettings.Default, DeskMessageSettings.Default, NowPlayingSettings.Default);
        Assert.Equal("Consolas", own.Desk.Font);
        Assert.Equal("e2e8f0,38bdf8", own.Ink.Color);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~ThemeMatchTests`
Expected: build FAILS with `The name 'ThemeMatch' does not exist in the current context`.

- [ ] **Step 3: Write the rules**

Create `src/Sanduhr.Core/ThemeMatch.cs`:

```csharp
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>Where the active theme came from, which decides whether theme match may write into it.</summary>
public enum ThemeOrigin { BuiltIn, Pack, User }

/// <summary>The Desk's own settings that Match now and Follow off write (theme match).</summary>
public sealed record ThemeMatchOwn(DeskSettings Desk, DeskInkSettings Ink, DeskMessageSettings Message, NowPlayingSettings NowPlaying);

/// <summary>
/// The pure part of "one theme for the widget and the Desk": who owns a theme, the name and file key of a theme Follow or "Save as a
/// new theme" makes, a theme carrying a Desk block as its canonical document, the matched look spelled out, and the Desk's own settings
/// Match now writes. No IO: the App's ThemeMatchService writes. Not linked into the MCP server.
/// </summary>
public static partial class ThemeMatch
{
    public const string DeskSuffix = " + Desk";
    private const int MaxTries = 99;

    [GeneratedRegex(@" \+ Desk( \d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex DeskSuffixRe();

    /// <summary>A built-in (even when a user file of the same name shadows it) and a pack's theme (<c>pack:stem</c>) are never edited.</summary>
    public static ThemeOrigin Origin(string key) =>
        ThemeCatalog.BuiltIns.ContainsKey(key) ? ThemeOrigin.BuiltIn : key.Contains(':') ? ThemeOrigin.Pack : ThemeOrigin.User;

    /// <summary>"&lt;Theme&gt; + Desk", then " + Desk 2", " + Desk 3"… while <paramref name="taken"/>; the theme's own name is cut so the
    /// whole fits <see cref="ThemeLint.MaxNameLength"/>, and a name that already ends in "+ Desk" is not suffixed twice.</summary>
    public static string NewThemeName(string themeName, Func<string, bool> taken)
    {
        string bare = DeskSuffixRe().Replace(CleanName(themeName) ?? "Theme", "");
        if (bare.Length == 0) bare = "Theme";
        string name = "";
        for (int n = 1; n <= MaxTries; n++)
        {
            string suffix = n == 1 ? DeskSuffix : $"{DeskSuffix} {n}";
            string head = bare.Length + suffix.Length > ThemeLint.MaxNameLength ? bare[..(ThemeLint.MaxNameLength - suffix.Length)].TrimEnd() : bare;
            name = head + suffix;
            if (!taken(name)) return name;
        }
        return name;
    }

    /// <summary>The file key for a new theme: the name slugged, then -2, -3… until it is free; never a built-in's key.</summary>
    public static string NewThemeKey(string name, Func<string, bool> taken)
    {
        string slug = ThemeHandoff.Slugify(name);
        for (int n = 1; n <= MaxTries; n++)
        {
            string suffix = n == 1 ? "" : "-" + n;
            string key = (slug.Length + suffix.Length > 40 ? slug[..(40 - suffix.Length)].TrimEnd('-') : slug) + suffix;
            if (ThemeHandoff.IsValidKey(key) && !ThemeCatalog.BuiltIns.ContainsKey(key) && !taken(key)) return key;
        }
        return "theme-" + Guid.NewGuid().ToString("N")[..8];
    }

    /// <summary>A name a person typed, as a theme name: control characters dropped, trimmed, cut to the strip's limit; null when empty.</summary>
    public static string? CleanName(string? raw)
    {
        if (raw is null) return null;
        var clean = new string(raw.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length > ThemeLint.MaxNameLength) clean = clean[..ThemeLint.MaxNameLength].TrimEnd();
        return clean.Length == 0 ? null : clean;
    }

    /// <summary><paramref name="def"/> carrying <paramref name="desk"/> (null or empty: none), renamed when <paramref name="name"/> is
    /// given, as its canonical document (<see cref="ThemeDraft"/>), so every other field of the theme is kept.</summary>
    public static JsonObject WithDesk(ThemeDefinition def, ThemeDesk? desk, string? name = null)
    {
        var draft = ThemeDraft.FromDefinition("theme", def);
        draft.Desk = desk is { IsEmpty: false } ? desk : null;
        if (name is not null) draft.Name = name;
        return draft.ToJson();
    }

    /// <summary>
    /// The theme's matched look as a desk block, every part spelled out (what Follow writes): ink, message, glow, plate, plate tint and
    /// shadow. <c>bars</c> and <c>font</c> only when the theme set them: writing bars would turn the widget's ramp into a gradient.
    /// </summary>
    public static ThemeDesk SpelledOut(ThemeDefinition def)
    {
        var look = ThemeDeskLook.From(def);
        static IReadOnlyList<string> Hashed(IReadOnlyList<string> stops) => [.. stops.Select(s => "#" + s)];
        return new ThemeDesk
        {
            Ink = Hashed(look.Ink),
            Message = Hashed(look.Message),
            Glow = "#" + look.Glow,
            Plate = look.Plate,
            PlateColor = "#" + look.PlateColor,
            Shadow = "#" + look.Shadow,
            Bars = def.Desk?.Bars,
            Font = def.Desk?.Font,
        };
    }

    /// <summary>
    /// What Match now (and Follow off) write into the Desk's own settings: the ink's stops as a Custom ink with its shadow on, the font
    /// (the Desk keeps its own when the look has none), the plate, the message's colors with its glow on, and gradient bars. The plate
    /// tint, shadow shade and glow color have no Desk setting, so they draw as before once the Desk has its own look again.
    /// </summary>
    public static ThemeMatchOwn OwnSettings(ThemeDeskLook look, DeskSettings desk, DeskInkSettings ink, DeskMessageSettings message, NowPlayingSettings np) => new(
        desk with
        {
            Font = look.Font is { } f ? DeskDesignApply.FontSetting(new DeskFontChoice(f)) : desk.Font,
            Plate = look.Plate,
        },
        ink with { Mode = DeskInkMode.Custom, Color = string.Join(",", look.Ink), Shadow = true },
        message with { Color = string.Join(",", look.Message), Glow = true },
        np with { BarsGradient = true });
}
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~ThemeMatchTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/ThemeMatch.cs windows-dotnet/tests/Sanduhr.Tests/ThemeMatchTests.cs
git commit -m "feat(theme-match): ThemeMatch names the theme Follow makes, spells the look out and says what Match now copies"
```

---

### Task 10: Designs and undo carry `follows_theme`; Put my Desk back returns the Desk and the theme together

**Files:**
- Modify: `src/Sanduhr.Core/DeskDesign.cs` (`Fields` ~106-107; properties ~104; `Parse` ~138-158; `ToJson` ~600; `Merge` ~639-653; `Diff` ~755-756)
- Modify: `src/Sanduhr.Core/DeskDesignLibrary.cs` (`Canonical` ~121-128)
- Modify: `src/Sanduhr.Core/DeskDesignApply.cs` (`DeskDesignSettings` ~7; `Settings` ~25)
- Modify: `src/Sanduhr.Core/DeskState.cs` (`Current` ~20-35)
- Modify: `src/Sanduhr.App/Desk/DeskDesignService.cs` (`CurrentDesign` ~243-244; `WriteDesign` ~486-498; new `SaveBeforeChange` after `SaveCurrentFirst` ~466)
- Modify: `src/Sanduhr.App/Desk/DeskController.cs` (`Reload`'s preview block ~315-322)
- Test: `tests/Sanduhr.Tests/DeskDesignTests.cs`, `tests/Sanduhr.App.Tests/DeskDesignServiceTests.cs`

**Interfaces:**
- Consumes: `SettingsStore.Load/SaveDeskFollowsTheme` (Task 8); `ThemeMatch.WithDesk/SpelledOut` (Task 9, test only).
- Produces: `DeskDesign.FollowsTheme` (`bool?`; JSON `follows_theme`); `DeskDesignSettings(…, string? WeatherNickname = null, bool? FollowsTheme = null)`; `DeskState.Current(…, string? weatherNickname = null, bool followsTheme = false)`; `DeskDesignService.SaveBeforeChange(string incomingName)` → `bool`.

- [ ] **Step 1: Write the failing tests**

Append to the class in `tests/Sanduhr.Tests/DeskDesignTests.cs`:

```csharp
    // ---- theme match: a design carries "follows the widget theme" ----

    [Fact]
    public void Follows_theme_is_read_written_merged_and_described()
    {
        var parsed = DeskDesign.Parse(JsonNode.Parse("""{"follows_theme": true}"""));
        Assert.True(parsed.Design!.FollowsTheme);
        Assert.True((bool)parsed.Design.ToJson()["follows_theme"]!);
        Assert.Contains(DeskDesign.Parse(JsonNode.Parse("""{"follows_theme": "yes"}""")).Reasons, r => r.Field == "follows_theme");
        Assert.True(DeskDesign.Merge(new DeskDesign { FollowsTheme = true }, new DeskDesign { Plate = true }).FollowsTheme);
        Assert.False(DeskDesign.Merge(new DeskDesign { FollowsTheme = true }, new DeskDesign { FollowsTheme = false }).FollowsTheme);
        Assert.Contains("Desk follows the widget theme: on", DeskDesign.Diff(new DeskDesign(), new DeskDesign { FollowsTheme = true }));
        Assert.Empty(DeskDesign.Diff(new DeskDesign { FollowsTheme = false }, new DeskDesign { FollowsTheme = false }));
    }

    [Fact]
    public void A_design_saved_before_theme_match_is_the_same_look_as_one_that_does_not_follow()
    {
        var before = new DeskDesign { Plate = true };
        Assert.Equal(DeskDesigns.Canonical(before), DeskDesigns.Canonical(before with { FollowsTheme = false }));
        Assert.NotEqual(DeskDesigns.Canonical(before), DeskDesigns.Canonical(before with { FollowsTheme = true }));
    }

    [Fact]
    public void The_state_and_the_apply_carry_it()
    {
        var current = DeskState.Current(DeskSettings.Default, DeskInkSettings.Default, LauncherModel.Empty, null, followsTheme: true);
        Assert.True(current.FollowsTheme);
        Assert.True((bool)current.ToJson()["follows_theme"]!);
        Assert.False(DeskState.Current(DeskSettings.Default, DeskInkSettings.Default, LauncherModel.Empty, null).FollowsTheme);
        Assert.True(DeskDesignApply.Settings(DeskSettings.Default, DeskInkSettings.Default, LauncherModel.Empty, new DeskDesign { FollowsTheme = true }).FollowsTheme);
        Assert.Null(DeskDesignApply.Settings(DeskSettings.Default, DeskInkSettings.Default, LauncherModel.Empty, new DeskDesign()).FollowsTheme);
    }
```

Append to the class in `tests/Sanduhr.App.Tests/DeskDesignServiceTests.cs`:

```csharp
    // ---- theme match: undo ----

    [Fact]
    public void The_current_design_says_whether_the_desk_follows()
    {
        _store.SaveDeskFollowsTheme(true);
        WithService(s => Assert.True(s.CurrentDesign().FollowsTheme));
    }

    [Fact]
    public void Put_my_Desk_back_after_Follow_returns_the_desk_and_the_widget_theme_together()
    {
        _store.SaveTheme("obsidian");
        WithService(s =>
        {
            Assert.True(s.SaveBeforeChange("Obsidian + Desk"));
            Assert.True(s.CanPutBack);
            // What Follow does next: a new user theme, made active, and the switch on.
            var obsidian = ThemeCatalog.BuiltIns["obsidian"];
            Directory.CreateDirectory(_paths.ThemesDir);
            File.WriteAllText(Path.Combine(_paths.ThemesDir, "obsidian-desk.json"),
                ThemeMatch.WithDesk(obsidian, ThemeMatch.SpelledOut(obsidian), "Obsidian + Desk").ToJsonString());
            _store.SaveTheme("obsidian-desk");
            _store.SaveDeskFollowsTheme(true);

            Assert.True(s.PutBack().Ok);
            Assert.Equal("obsidian", _store.LoadTheme());
            Assert.False(_store.LoadDeskFollowsTheme());
        });
    }

    [Fact]
    public void Saving_before_a_change_twice_keeps_one_design()
    {
        WithService(s =>
        {
            Assert.True(s.SaveBeforeChange("matching Obsidian"));
            Assert.True(s.SaveBeforeChange("matching Obsidian"));   // the same look: not saved twice
            Assert.Single(s.Designs());
            Assert.Equal("Before matching Obsidian", s.Designs()[0].Name);
        });
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~DeskDesignTests`
Expected: build FAILS with `'DeskDesign' does not contain a definition for 'FollowsTheme'`.

- [ ] **Step 3: The design format**

In `src/Sanduhr.Core/DeskDesign.cs`, after the `Weather` property add:

```csharp
    /// <summary>"Follow the widget theme" (theme match): true, the Desk draws the theme's look; null leaves it alone.</summary>
    public bool? FollowsTheme { get; init; }
```

replace

```csharp
        ["name", "note", "layout", "weather", "font", "ink", "plate", "shadow", "launcher", "messages", "looks", "theme"];
```

with

```csharp
        ["name", "note", "layout", "weather", "font", "ink", "plate", "shadow", "launcher", "messages", "looks", "theme", "follows_theme"];
```

In `Parse`, replace

```csharp
        bool? shadow = Bool(o, "shadow", r);
```

with

```csharp
        bool? shadow = Bool(o, "shadow", r);
        bool? followsTheme = Bool(o, "follows_theme", r);
```

and

```csharp
            Launcher = launcher, Messages = messages, Theme = theme, Looks = looks, Weather = weather,
        }, [], warnings);
```

with

```csharp
            Launcher = launcher, Messages = messages, Theme = theme, Looks = looks, Weather = weather, FollowsTheme = followsTheme,
        }, [], warnings);
```

In `ToJson`, replace

```csharp
        if (Theme is not null) o["theme"] = Theme.DeepClone();
        return o;
```

with

```csharp
        if (Theme is not null) o["theme"] = Theme.DeepClone();
        if (FollowsTheme is { } follows) o["follows_theme"] = follows;
        return o;
```

In `Merge`, replace

```csharp
            Weather = proposal.Weather ?? current?.Weather,
        };
```

with

```csharp
            Weather = proposal.Weather ?? current?.Weather,
            FollowsTheme = proposal.FollowsTheme ?? current?.FollowsTheme,
        };
```

In `Diff`, replace

```csharp
        if (b.Shadow is { } shadow && a.Shadow != shadow) lines.Add($"Text shadow: {(shadow ? "on" : "off")}");
```

with

```csharp
        if (b.Shadow is { } shadow && a.Shadow != shadow) lines.Add($"Text shadow: {(shadow ? "on" : "off")}");
        if (b.FollowsTheme is { } follows && (a.FollowsTheme ?? false) != follows)
            lines.Add($"Desk follows the widget theme: {(follows ? "on" : "off")}");
```

Add `follows_theme` to the class summary's field list ("… and <c>theme</c> (a widget theme object), and <c>follows_theme</c> (true or false: the Desk draws the theme's look)").

In `src/Sanduhr.Core/DeskDesignLibrary.cs`, in `Canonical`, replace

```csharp
        // No nickname is the same look as a design from before nicknames (#315).
        if (design.Weather is { Nickname.Length: 0 }) o.Remove("weather");
```

with

```csharp
        // No nickname is the same look as a design from before nicknames (#315).
        if (design.Weather is { Nickname.Length: 0 }) o.Remove("weather");
        // Not following is the same look as a design from before theme match.
        if (design.FollowsTheme != true) o.Remove("follows_theme");
```

In `src/Sanduhr.Core/DeskDesignApply.cs`, replace

```csharp
public sealed record DeskDesignSettings(DeskSettings Desk, DeskInkSettings Ink, LauncherModel Launcher, string? WeatherNickname = null);
```

with

```csharp
/// <param name="FollowsTheme">"Follow the widget theme" as the design sets it (theme match); null leaves it alone.</param>
public sealed record DeskDesignSettings(DeskSettings Desk, DeskInkSettings Ink, LauncherModel Launcher, string? WeatherNickname = null,
    bool? FollowsTheme = null);
```

and

```csharp
        return new DeskDesignSettings(nextDesk.Normalized(), Ink(ink, design), design.Launcher ?? launcher, design.Weather?.Nickname);
```

with

```csharp
        return new DeskDesignSettings(nextDesk.Normalized(), Ink(ink, design), design.Launcher ?? launcher, design.Weather?.Nickname,
            design.FollowsTheme);
```

In `src/Sanduhr.Core/DeskState.cs`, replace

```csharp
    public static DeskDesign Current(DeskSettings desk, DeskInkSettings ink, LauncherModel launcher, JsonObject? theme, string? weatherNickname = null)
    {
```

with

```csharp
    /// <param name="followsTheme">"Follow the widget theme" (theme match), reported as <c>follows_theme</c>.</param>
    public static DeskDesign Current(DeskSettings desk, DeskInkSettings ink, LauncherModel launcher, JsonObject? theme, string? weatherNickname = null,
        bool followsTheme = false)
    {
```

and

```csharp
            Weather = new DeskWeatherChange(weatherNickname ?? ""),
        };
```

with

```csharp
            Weather = new DeskWeatherChange(weatherNickname ?? ""),
            FollowsTheme = followsTheme,
        };
```

- [ ] **Step 4: The widget's half**

In `src/Sanduhr.App/Desk/DeskDesignService.cs`, replace

```csharp
        DeskState.Current(_store.LoadDesk(), _store.LoadDeskInk(), _store.LoadLauncher(), ActiveTheme(), _store.LoadDeskWeather().Nickname);
```

with

```csharp
        DeskState.Current(_store.LoadDesk(), _store.LoadDeskInk(), _store.LoadLauncher(), ActiveTheme(), _store.LoadDeskWeather().Nickname,
            _store.LoadDeskFollowsTheme());
```

In `WriteDesign`, replace

```csharp
        if (next.Launcher.ToJson().ToJsonString() != launcher.ToJson().ToJsonString()) _store.SaveLauncher(next.Launcher);
```

with

```csharp
        if (next.Launcher.ToJson().ToJsonString() != launcher.ToJson().ToJsonString()) _store.SaveLauncher(next.Launcher);
        // Theme match: a design that says whether the Desk follows the theme sets it (Put my Desk back restores both sides).
        if (next.FollowsTheme is { } follows && follows != _store.LoadDeskFollowsTheme()) _store.SaveDeskFollowsTheme(follows);
```

After the `SaveCurrentFirst` method add:

```csharp

    /// <summary>
    /// Saves the Desk as it is before a change Settings makes to it (theme match: Follow on, Match now), unless a saved design already is
    /// that look, and points "Put my Desk back" at it, as applying a design does. False when it could not be saved.
    /// </summary>
    public bool SaveBeforeChange(string incomingName)
    {
        try
        {
            if (SaveCurrentFirst(CurrentDesign(), incomingName) is not { } saved) return false;
            _store.SaveDeskPutBack(saved.Id);
            AfterChange();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log("save before", e);
            return false;
        }
    }
```

In `src/Sanduhr.App/Desk/DeskController.cs`, in `Reload`'s preview block, replace

```csharp
            _launcher = preview.Launcher;
```

with

```csharp
            _launcher = preview.Launcher;
            if (preview.FollowsTheme is { } follows) _follows = follows;
```

- [ ] **Step 5: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DeskDesignTests|FullyQualifiedName~DeskDesignValidatorTests|FullyQualifiedName~DeskDesignApplyTests|FullyQualifiedName~DeskDesignHandoffTests|FullyQualifiedName~WallpaperSummaryTests"` then `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskDesignServiceTests|FullyQualifiedName~DeskDesignReviewTests|FullyQualifiedName~DesignsSettingsTests"` and `dotnet build src/Sanduhr.Mcp`
Expected: PASS; the server compiles the widened `DeskDesign.cs`.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/DeskDesign.cs windows-dotnet/src/Sanduhr.Core/DeskDesignLibrary.cs windows-dotnet/src/Sanduhr.Core/DeskDesignApply.cs windows-dotnet/src/Sanduhr.Core/DeskState.cs windows-dotnet/src/Sanduhr.App/Desk/DeskDesignService.cs windows-dotnet/src/Sanduhr.App/Desk/DeskController.cs windows-dotnet/tests/Sanduhr.Tests/DeskDesignTests.cs windows-dotnet/tests/Sanduhr.App.Tests/DeskDesignServiceTests.cs
git commit -m "feat(theme-match): designs and get_desk carry follows_theme; Put my Desk back returns the Desk and the theme together"
```

---

### Task 11: Follow on and off, and Match now, with the save-first undo

**Files:**
- Create: `src/Sanduhr.App/Services/ThemeMatchService.cs`, `src/Sanduhr.App/Views/ThemeMatchPrompts.cs`
- Modify: `src/Sanduhr.Core/SettingsNames.cs` (a new block after `DropShadow` ~370)
- Modify: `src/Sanduhr.App/Views/ThemedDialog.xaml` (~52), `src/Sanduhr.App/Views/ThemedDialog.xaml.cs` (after `Show`)
- Test: `tests/Sanduhr.App.Tests/ThemeMatchServiceTests.cs` (create)

**Interfaces:**
- Consumes: `ThemeMatch` (Task 9), `DeskDesignService.SaveBeforeChange` (Task 10), `SettingsStore.Load/SaveDeskFollowsTheme` (Task 8), `WidgetViewModel.ActiveThemeKey/GetThemeDefinition/ThemesDir/Themes/HasTheme/ReloadUserThemes/ApplyThemeByKey`.
- Produces: `internal enum ThemeAddChoice { AddToTheme, NewTheme, Cancel }`, `internal enum ThemeEditChoice { UpdateTheme, NewTheme, Cancel }`, `internal sealed record ThemeMatchResult(bool Ok, string Message)`; `internal sealed class ThemeMatchService(SettingsStore store, WidgetViewModel widget, Func<DeskDesignService?>? designs = null)` with `AskAddOrNew` (`Func<string, ThemeAddChoice>`; Task 12 adds `AskWhere` and `AskName`), `bool Following`, `FollowOn()`, `FollowOff()`, `MatchNow()` (each `ThemeMatchResult`); `ThemedDialog.ShowChoice(Window? owner, string title, string message, string primaryLabel, string secondaryLabel)` (Yes, No or Cancel); `ThemeMatchPrompts.AddOrNew/Where/Name`. SettingsNames: `ThemeMatchSection`, `FollowThemeSwitch`, `FollowingTheme(string)`, `KeptAsOwnLook`, `MatchingTheme(string)`, `MatchedTheme(string)`, `CouldNotSaveDeskFirst`, `ThemeNotWritten(string)`, `AddOrNewQuestion(string)`, `AddDeskLookTo(string)`, `MakeANewTheme`, `ThemeMatchCancel`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Sanduhr.App.Tests/ThemeMatchServiceTests.cs`:

```csharp
using System.IO;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Theme match, "Follow the widget theme" and "Match now", over a scratch widget and its own themes folder: a built-in or a pack theme is
/// never edited; a theme of the person's asks add-or-new; Cancel writes nothing; Follow off and Match now leave the look as the Desk's own;
/// the Desk is saved first so Put my Desk back returns both sides. The prompts are replaced; one that should not be asked throws.
/// </summary>
public sealed class ThemeMatchServiceTests : AppWindowFixture
{
    private sealed record Rig(ThemeMatchService Service, WidgetViewModel Widget, SettingsStore Store, DeskDesignService Designs, Paths Paths) : IDisposable
    {
        public void Dispose() => Designs.Dispose();
    }

    private Rig Make()
    {
        var paths = ScratchPaths();
        var (widget, _) = ScratchWidget("Personal");
        var store = new SettingsStore(paths);
        var designs = new DeskDesignService(paths, store);
        var service = new ThemeMatchService(store, widget, () => designs)
        {
            AskAddOrNew = _ => throw new Xunit.Sdk.XunitException("asked add-or-new"),
        };
        return new Rig(service, widget, store, designs, paths);
    }

    /// <summary>A theme of the person's, "Dusk": Aurora's colors (border tint, inner highlight, bloom) under another name, made active.</summary>
    private static string AddDusk(Rig r)
    {
        var dusk = ThemeCatalog.BuiltIns["aurora"] with { Name = "Dusk", Font = "Segoe UI" };
        Directory.CreateDirectory(r.Paths.ThemesDir);
        string path = Path.Combine(r.Paths.ThemesDir, "dusk.json");
        File.WriteAllText(path, ThemeDraft.FromDefinition("dusk", dusk).ToJsonText());
        r.Widget.ReloadUserThemes();
        r.Widget.ApplyThemeByKey("dusk");
        return path;
    }

    [Fact]
    public void Follow_on_a_built_in_makes_Theme_plus_Desk_and_follows_it() => AppHost.Run(() =>
    {
        using var r = Make();
        var result = r.Service.FollowOn();
        Assert.True(result.Ok);
        Assert.Equal(SettingsNames.FollowingTheme("Obsidian + Desk"), result.Message);
        Assert.Equal("obsidian-desk", r.Widget.ActiveThemeKey);
        var made = r.Widget.GetThemeDefinition("obsidian-desk")!;
        Assert.Equal("Obsidian + Desk", made.Name);
        Assert.Equal(["#e8e4dc", "#6c63ff"], made.Desk!.Ink);
        Assert.Null(made.Desk.Bars);   // the widget keeps its ramp
        Assert.True(r.Store.LoadDeskFollowsTheme());
        Assert.True(r.Designs.CanPutBack);
        Assert.False(File.Exists(Path.Combine(r.Paths.ThemesDir, "obsidian.json")));   // the built-in is never edited
    });

    [Fact]
    public void Follow_on_never_edits_a_pack_theme() => AppHost.Run(() =>
    {
        using var r = Make();
        InstalledPackKit.Install(r.Paths, "good-theme");
        r.Widget.ReloadUserThemes();
        r.Widget.ApplyThemeByKey("good-theme:fixture-dark");
        Assert.True(r.Service.FollowOn().Ok);   // no question: a pack's theme is not the person's to edit
        Assert.DoesNotContain(':', r.Widget.ActiveThemeKey);
        Assert.True(File.Exists(Path.Combine(r.Paths.ThemesDir, r.Widget.ActiveThemeKey + ".json")));
    });

    [Fact]
    public void Add_writes_the_desk_into_the_user_theme_keeping_every_other_field() => AppHost.Run(() =>
    {
        using var r = Make();
        AddDusk(r);
        var before = r.Widget.GetThemeDefinition("dusk")!;
        string? asked = null;
        r.Service.AskAddOrNew = name => { asked = name; return ThemeAddChoice.AddToTheme; };

        Assert.True(r.Service.FollowOn().Ok);

        Assert.Equal("Dusk", asked);
        Assert.Equal("dusk", r.Widget.ActiveThemeKey);
        var after = r.Widget.GetThemeDefinition("dusk")!;
        Assert.NotNull(after.Desk);
        Assert.Equal(before with { Desk = after.Desk }, after);
        Assert.True(r.Store.LoadDeskFollowsTheme());
    });

    [Fact]
    public void Make_a_new_theme_leaves_the_user_theme_alone() => AppHost.Run(() =>
    {
        using var r = Make();
        string path = AddDusk(r);
        byte[] bytes = File.ReadAllBytes(path);
        r.Service.AskAddOrNew = _ => ThemeAddChoice.NewTheme;

        Assert.True(r.Service.FollowOn().Ok);

        Assert.Equal("dusk-desk", r.Widget.ActiveThemeKey);
        Assert.Equal("Dusk + Desk", r.Widget.GetThemeDefinition("dusk-desk")!.Name);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    });

    [Fact]
    public void Cancel_writes_nothing_and_Follow_stays_off() => AppHost.Run(() =>
    {
        using var r = Make();
        AddDusk(r);
        r.Service.AskAddOrNew = _ => ThemeAddChoice.Cancel;

        var result = r.Service.FollowOn();

        Assert.False(result.Ok);
        Assert.False(r.Store.LoadDeskFollowsTheme());
        Assert.Equal(["dusk.json"], Directory.GetFiles(r.Paths.ThemesDir).Select(f => Path.GetFileName(f)));
        Assert.False(r.Designs.CanPutBack);
        Assert.Equal("dusk", r.Widget.ActiveThemeKey);
    });

    [Fact]
    public void Follow_off_keeps_the_look_as_the_desks_own() => AppHost.Run(() =>
    {
        using var r = Make();
        r.Service.FollowOn();
        var result = r.Service.FollowOff();
        Assert.True(result.Ok);
        Assert.Equal(SettingsNames.KeptAsOwnLook, result.Message);
        Assert.False(r.Store.LoadDeskFollowsTheme());
        Assert.Equal(new DeskInkSettings(DeskInkMode.Custom, "e8e4dc,6c63ff", true), r.Store.LoadDeskInk());
        Assert.True(r.Store.LoadDeskPlate());
        Assert.Equal("6c63ff,e8e4dc", r.Store.LoadDeskMessage().Color);
        Assert.True(r.Store.LoadNowPlaying().BarsGradient);
        Assert.Equal("obsidian-desk", r.Widget.ActiveThemeKey);   // the theme stays where it is
    });

    [Fact]
    public void Match_now_copies_once_saves_first_and_never_follows() => AppHost.Run(() =>
    {
        using var r = Make();
        var result = r.Service.MatchNow();
        Assert.True(result.Ok);
        Assert.Equal(SettingsNames.MatchedTheme("Obsidian"), result.Message);
        Assert.False(r.Store.LoadDeskFollowsTheme());
        Assert.Equal(DeskInkMode.Custom, r.Store.LoadDeskInk().Mode);
        Assert.Equal("obsidian", r.Widget.ActiveThemeKey);
        Assert.False(Directory.Exists(r.Paths.ThemesDir) && Directory.GetFiles(r.Paths.ThemesDir).Length > 0);   // no theme is made
        Assert.Equal("Before matching Obsidian", Assert.Single(r.Designs.Designs()).Name);
    });

    [Fact]
    public void Put_my_Desk_back_after_Follow_returns_both_sides() => AppHost.Run(() =>
    {
        using var r = Make();
        r.Service.FollowOn();
        Assert.True(r.Designs.PutBack().Ok);
        Assert.Equal("obsidian", r.Store.LoadTheme());
        Assert.False(r.Store.LoadDeskFollowsTheme());
    });
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~ThemeMatchServiceTests`
Expected: build FAILS with `The type or namespace name 'ThemeMatchService' could not be found`.

- [ ] **Step 3: The names**

In `src/Sanduhr.Core/SettingsNames.cs`, after `public const string DropShadow = "Drop shadow under the text";` add:

```csharp
    // ---- One theme for the widget and the Desk (theme match) ----
    /// <summary>Desk Look's first section (theme match).</summary>
    public const string ThemeMatchSection = "Match the widget theme";
    public const string FollowThemeSwitch = "Follow the widget theme";
    /// <summary>The status line after Follow on, an update or a new theme.</summary>
    public static string FollowingTheme(string theme) => $"The Desk follows {theme}.";
    public const string KeptAsOwnLook = "The Desk keeps this look as its own.";
    /// <summary>The incoming name of the design Match now saves first: "Before matching Obsidian".</summary>
    public static string MatchingTheme(string theme) => $"matching {theme}";
    public static string MatchedTheme(string theme) => $"The Desk now matches {theme}. Its look is its own, so it stays when the theme changes.";
    public const string CouldNotSaveDeskFirst = "The Desk could not be saved first, so nothing was changed.";
    public static string ThemeNotWritten(string why) => $"The theme could not be written ({why}), so nothing was changed.";
    public static string AddOrNewQuestion(string theme) =>
        $"{theme} is one of your themes. The Desk's look can go into it, or into a new theme made from it.";
    public static string AddDeskLookTo(string theme) => $"Add the Desk look to {theme}";
    public const string MakeANewTheme = "Make a new theme";
    public const string ThemeMatchCancel = "Cancel";
```

- [ ] **Step 4: The three-way dialog**

In `src/Sanduhr.App/Views/ThemedDialog.xaml`, replace

```xml
                <Button x:Name="SecondaryButton" Content="No" Click="OnSecondaryClick"
```

with

```xml
                <Button x:Name="TertiaryButton" Content="Cancel" Click="OnTertiaryClick"
                        Visibility="Collapsed" Cursor="Hand"
                        Padding="18,7" Margin="0,0,8,0" BorderThickness="1"
                        Background="{DynamicResource Sanduhr.Brush.Glass}"
                        Foreground="{DynamicResource Sanduhr.Brush.Text}"
                        BorderBrush="{DynamicResource Sanduhr.Brush.Border}" />
                <Button x:Name="SecondaryButton" Content="No" Click="OnSecondaryClick"
```

In `src/Sanduhr.App/Views/ThemedDialog.xaml.cs`, after the `Show` method add:

```csharp

    private void OnTertiaryClick(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Cancel;
        DialogResult = false;
        Close();
    }

    /// <summary>A three-way question (theme match: "Update Dusk", "Save as a new theme", Cancel). Yes for the primary, No for the
    /// secondary, Cancel for the Cancel button, Escape or closing the window: both labelled buttons say what they do.</summary>
    public static MessageBoxResult ShowChoice(Window? owner, string title, string message, string primaryLabel, string secondaryLabel)
    {
        var dlg = new ThemedDialog(title, message, MessageBoxButton.YesNo, ThemedDialogKind.Info, playSound: true);
        dlg.PrimaryButton.Content = primaryLabel;
        dlg.SecondaryButton.Content = secondaryLabel;
        dlg.SecondaryButton.IsCancel = false;
        dlg.TertiaryButton.Content = Sanduhr.Core.SettingsNames.ThemeMatchCancel;
        dlg.TertiaryButton.Visibility = Visibility.Visible;
        dlg.TertiaryButton.IsCancel = true;
        dlg.Result = MessageBoxResult.Cancel;
        if (owner is not null && owner.IsLoaded)
            dlg.Owner = owner;
        else
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
        return dlg.Result;
    }
```

- [ ] **Step 5: The service and its prompts**

Create `src/Sanduhr.App/Views/ThemeMatchPrompts.cs`:

```csharp
using System.Windows;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>The questions theme match asks, as themed dialogs over Settings. Tests replace them on <see cref="ThemeMatchService"/>.</summary>
internal static class ThemeMatchPrompts
{
    private static Window? Owner => Application.Current?.Windows.OfType<SettingsWindow>().FirstOrDefault(w => w.IsLoaded);

    /// <summary>Follow on while a theme of the person's is active: its name in, where the look goes out.</summary>
    public static ThemeAddChoice AddOrNew(string theme) =>
        ThemedDialog.ShowChoice(Owner, SettingsNames.FollowThemeSwitch, SettingsNames.AddOrNewQuestion(theme),
            SettingsNames.AddDeskLookTo(theme), SettingsNames.MakeANewTheme) switch
        {
            MessageBoxResult.Yes => ThemeAddChoice.AddToTheme,
            MessageBoxResult.No => ThemeAddChoice.NewTheme,
            _ => ThemeAddChoice.Cancel,
        };
}
```

Create `src/Sanduhr.App/Services/ThemeMatchService.cs`:

```csharp
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sanduhr.App.Desk;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>Follow on while a theme of the person's is active: where the Desk's look goes.</summary>
internal enum ThemeAddChoice { AddToTheme, NewTheme, Cancel }

/// <summary>A hand edit while following: where the change goes.</summary>
internal enum ThemeEditChoice { UpdateTheme, NewTheme, Cancel }

/// <summary>What a theme-match action did: whether it went ahead, and the line Settings shows ("" for none).</summary>
internal sealed record ThemeMatchResult(bool Ok, string Message);

/// <summary>
/// "One theme for the widget and the Desk", the writing half (spec, "Follow the widget theme, and Match now"). Follow on keeps the matched
/// look in a theme: a new "&lt;Theme&gt; + Desk" for a built-in or a pack's theme (never edited), or, for a theme of the person's, the
/// theme itself or a new one as they choose. Follow off and Match now copy the look into the Desk's own settings. Turning Follow on and
/// Match now save the Desk first, as an applied design does, so Put my Desk back returns the Desk and the widget theme together. The
/// pure rules are Core's <see cref="ThemeMatch"/>; the questions are seams the tests replace.
/// </summary>
internal sealed class ThemeMatchService
{
    private readonly SettingsStore _store;
    private readonly WidgetViewModel _widget;
    private readonly Func<DeskDesignService?> _designs;
    private static readonly ThemeMatchResult CouldNotSave = new(false, SettingsNames.CouldNotSaveDeskFirst);

    /// <param name="designs">Where the Desk is saved first; default the running one. None (the harness): nothing to save into, so it goes ahead.</param>
    public ThemeMatchService(SettingsStore store, WidgetViewModel widget, Func<DeskDesignService?>? designs = null)
    {
        _store = store;
        _widget = widget;
        _designs = designs ?? (() => DeskDesignService.Current);
    }

    public Func<string, ThemeAddChoice> AskAddOrNew { get; set; } = ThemeMatchPrompts.AddOrNew;

    public bool Following => _store.LoadDeskFollowsTheme();

    private string ActiveKey => _widget.ActiveThemeKey;
    private ThemeDefinition Active => _widget.GetThemeDefinition(ActiveKey) ?? ThemeCatalog.BuiltIns["obsidian"];

    /// <summary>Follow the widget theme: on.</summary>
    public ThemeMatchResult FollowOn()
    {
        if (Following) return new(true, "");
        string key = ActiveKey;
        var def = Active;
        if (ThemeMatch.Origin(key) == ThemeOrigin.User)
        {
            switch (AskAddOrNew(def.Name))
            {
                case ThemeAddChoice.Cancel:
                    return new(false, "");
                case ThemeAddChoice.AddToTheme:
                    if (!SaveBefore(def.Name)) return CouldNotSave;
                    if (Write(key, ThemeMatch.WithDesk(def, ThemeMatch.SpelledOut(def))) is { } failed) return failed;
                    return Follow(key, def.Name);
            }
        }
        string name = ThemeMatch.NewThemeName(def.Name, NameTaken);
        string newKey = ThemeMatch.NewThemeKey(name, KeyTaken);
        if (!SaveBefore(name)) return CouldNotSave;
        if (Write(newKey, ThemeMatch.WithDesk(def, ThemeMatch.SpelledOut(def), name)) is { } bad) return bad;
        return Follow(newKey, name);
    }

    private ThemeMatchResult Follow(string key, string name)
    {
        _widget.ReloadUserThemes();
        _store.SaveDeskFollowsTheme(true);
        _widget.ApplyThemeByKey(key);   // raises ThemeChanged: the Desk draws the theme's look
        return new(true, SettingsNames.FollowingTheme(name));
    }

    /// <summary>Follow the widget theme: off. The look stays, as the Desk's own settings, so nothing jumps.</summary>
    public ThemeMatchResult FollowOff()
    {
        if (!Following) return new(true, "");
        WriteOwn(ThemeDeskLook.From(Active));
        _store.SaveDeskFollowsTheme(false);
        return new(true, SettingsNames.KeptAsOwnLook);
    }

    /// <summary>Match now: the theme's look copied into the Desk's own settings once, the Desk saved first. Follow stays off; no theme is made.</summary>
    public ThemeMatchResult MatchNow()
    {
        var def = Active;
        if (!SaveBefore(SettingsNames.MatchingTheme(def.Name))) return CouldNotSave;
        WriteOwn(ThemeDeskLook.From(def));
        return new(true, SettingsNames.MatchedTheme(def.Name));
    }

    private void WriteOwn(ThemeDeskLook look)
    {
        var own = ThemeMatch.OwnSettings(look, _store.LoadDesk(), _store.LoadDeskInk(), _store.LoadDeskMessage(), _store.LoadNowPlaying());
        _store.SaveDesk(own.Desk);
        _store.SaveDeskPlate(own.Desk.Plate);
        _store.SaveDeskInk(own.Ink);
        _store.SaveDeskMessage(own.Message);
        _store.SaveNowPlaying(own.NowPlaying);
    }

    /// <summary>Saves the Desk as it is first (Put my Desk back). No design service: nothing to save into, so it goes ahead.</summary>
    private bool SaveBefore(string incoming) => _designs() is not { } designs || designs.SaveBeforeChange(incoming);

    /// <summary>Writes a theme under the themes folder, linted first; null when it was written, else the result to show.</summary>
    private ThemeMatchResult? Write(string key, JsonObject theme)
    {
        var lint = ThemeLint.Lint(theme);
        if (!lint.Ok) return new(false, lint.Errors.First().Message);
        try
        {
            Directory.CreateDirectory(_widget.ThemesDir);
            string path = Path.Combine(_widget.ThemesDir, key + ".json");
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, theme.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, path, overwrite: true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(false, SettingsNames.ThemeNotWritten(e.GetType().Name));
        }
    }

    private bool NameTaken(string name) => _widget.Themes.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    private bool KeyTaken(string key) =>
        _widget.HasTheme(key) || File.Exists(Path.Combine(_widget.ThemesDir, key + ".json"));
}
```

- [ ] **Step 6: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~ThemeMatchServiceTests|FullyQualifiedName~PackThemeWiringTests"`
Expected: PASS. (The dialog itself is checked on screen in Task 17's owner list: it is modal and the tests never show it.)

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Services/ThemeMatchService.cs windows-dotnet/src/Sanduhr.App/Views/ThemeMatchPrompts.cs windows-dotnet/src/Sanduhr.App/Views/ThemedDialog.xaml windows-dotnet/src/Sanduhr.App/Views/ThemedDialog.xaml.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.App.Tests/ThemeMatchServiceTests.cs
git commit -m "feat(theme-match): Follow keeps the matched look in a theme, never a built-in's; Match now copies it once; both save the Desk first"
```

---

### Task 12: A hand edit while following asks where it goes: Update, Save as a new theme, Cancel

**Files:**
- Modify: `src/Sanduhr.App/Services/ThemeMatchService.cs` (the seams; `Edit`)
- Modify: `src/Sanduhr.App/Views/ThemeMatchPrompts.cs` (`Where`, `Name`)
- Modify: `src/Sanduhr.Core/SettingsNames.cs` (the theme-match block)
- Test: `tests/Sanduhr.App.Tests/ThemeMatchServiceTests.cs`

**Interfaces:**
- Consumes: Task 11's service.
- Produces: `ThemeMatchService.AskWhere` (`Func<string, bool, ThemeEditChoice>`: theme name, whether Update is offered), `ThemeMatchService.AskName` (`Func<string, string?>`: the suggested name in, the typed one or null out), `ThemeMatchService.Edit(ThemeDesk next)` → `ThemeMatchResult`; SettingsNames `WhereQuestion(string, bool)`, `UpdateTheme(string)`, `SaveAsNewTheme`, `NewThemeNamePrompt`, `ThemeUpdated(string)`.

- [ ] **Step 1: Write the failing tests**

In `ThemeMatchServiceTests.Make()`, replace

```csharp
            AskAddOrNew = _ => throw new Xunit.Sdk.XunitException("asked add-or-new"),
        };
```

with

```csharp
            AskAddOrNew = _ => throw new Xunit.Sdk.XunitException("asked add-or-new"),
            AskWhere = (_, _) => throw new Xunit.Sdk.XunitException("asked where"),
            AskName = _ => throw new Xunit.Sdk.XunitException("asked a name"),
        };
```

and append to the class:

```csharp
    // ---- a hand edit while following ----

    private static ThemeDesk Edited(ThemeDefinition def) => ThemeMatch.SpelledOut(def) with { Ink = ["#ffffff"] };

    [Fact]
    public void A_hand_edit_on_a_built_in_follow_offers_only_a_new_theme() => AppHost.Run(() =>
    {
        using var r = Make();
        r.Store.SaveDeskFollowsTheme(true);   // following while the built-in itself is active (a Desk from a design, or an older build)
        bool? offered = null;
        r.Service.AskWhere = (_, canUpdate) => { offered = canUpdate; return ThemeEditChoice.NewTheme; };
        r.Service.AskName = suggested => { Assert.Equal("Obsidian + Desk", suggested); return "Obsidian Neon"; };

        var result = r.Service.Edit(Edited(ThemeCatalog.BuiltIns["obsidian"]));

        Assert.True(result.Ok);
        Assert.False(offered);
        Assert.Equal("obsidian-neon", r.Widget.ActiveThemeKey);
        Assert.Equal(["#ffffff"], r.Widget.GetThemeDefinition("obsidian-neon")!.Desk!.Ink);
        Assert.False(File.Exists(Path.Combine(r.Paths.ThemesDir, "obsidian.json")));
    });

    [Fact]
    public void Update_writes_the_edit_into_the_active_user_theme() => AppHost.Run(() =>
    {
        using var r = Make();
        r.Service.FollowOn();   // obsidian-desk, a theme of the person's now
        var before = r.Widget.GetThemeDefinition("obsidian-desk")!;
        bool? offered = null;
        r.Service.AskWhere = (name, canUpdate) => { Assert.Equal("Obsidian + Desk", name); offered = canUpdate; return ThemeEditChoice.UpdateTheme; };

        var result = r.Service.Edit(Edited(before));

        Assert.True(result.Ok);
        Assert.True(offered);
        Assert.Equal(SettingsNames.ThemeUpdated("Obsidian + Desk"), result.Message);
        var after = r.Widget.GetThemeDefinition("obsidian-desk")!;
        Assert.Equal(["#ffffff"], after.Desk!.Ink);
        Assert.Equal(before with { Desk = after.Desk }, after);
    });

    [Fact]
    public void Cancel_leaves_the_theme_as_it_was() => AppHost.Run(() =>
    {
        using var r = Make();
        r.Service.FollowOn();
        string path = Path.Combine(r.Paths.ThemesDir, "obsidian-desk.json");
        byte[] bytes = File.ReadAllBytes(path);
        r.Service.AskWhere = (_, _) => ThemeEditChoice.Cancel;

        Assert.False(r.Service.Edit(Edited(ThemeCatalog.BuiltIns["obsidian"])).Ok);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    });

    [Fact]
    public void A_name_left_empty_saves_nothing() => AppHost.Run(() =>
    {
        using var r = Make();
        r.Service.FollowOn();
        int files = Directory.GetFiles(r.Paths.ThemesDir).Length;
        r.Service.AskWhere = (_, _) => ThemeEditChoice.NewTheme;
        r.Service.AskName = _ => "   ";

        Assert.False(r.Service.Edit(Edited(ThemeCatalog.BuiltIns["obsidian"])).Ok);
        Assert.Equal(files, Directory.GetFiles(r.Paths.ThemesDir).Length);
        Assert.Equal("obsidian-desk", r.Widget.ActiveThemeKey);
    });

    [Fact]
    public void Not_following_an_edit_asks_nothing_and_does_nothing() => AppHost.Run(() =>
    {
        using var r = Make();
        Assert.False(r.Service.Edit(Edited(ThemeCatalog.BuiltIns["obsidian"])).Ok);   // AskWhere would throw
    });
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~ThemeMatchServiceTests`
Expected: build FAILS with `'ThemeMatchService' does not contain a definition for 'AskWhere'` (the rig sets it).

- [ ] **Step 3: The names**

In `src/Sanduhr.Core/SettingsNames.cs`, after `public const string ThemeMatchCancel = "Cancel";` add:

```csharp
    public static string WhereQuestion(string theme, bool canUpdate) => canUpdate
        ? $"The Desk follows {theme}. Save this change into it, or into a new theme?"
        : $"The Desk follows {theme}, which ships with Sanduhr and stays as it is. Save this change as a new theme?";
    public static string UpdateTheme(string theme) => $"Update {theme}";
    public const string SaveAsNewTheme = "Save as a new theme";
    public const string NewThemeNamePrompt = "Name of the new theme";
    public static string ThemeUpdated(string theme) => $"Updated {theme}.";
```

- [ ] **Step 4: The prompts and the edit**

In `src/Sanduhr.App/Views/ThemeMatchPrompts.cs`, before the class's closing brace add:

```csharp

    /// <summary>A hand edit while following: Update (only for a theme of the person's), Save as a new theme, or Cancel.</summary>
    public static ThemeEditChoice Where(string theme, bool canUpdate)
    {
        if (canUpdate)
            return ThemedDialog.ShowChoice(Owner, SettingsNames.ThemeMatchSection, SettingsNames.WhereQuestion(theme, true),
                SettingsNames.UpdateTheme(theme), SettingsNames.SaveAsNewTheme) switch
            {
                MessageBoxResult.Yes => ThemeEditChoice.UpdateTheme,
                MessageBoxResult.No => ThemeEditChoice.NewTheme,
                _ => ThemeEditChoice.Cancel,
            };
        return ThemedDialog.Show(Owner, SettingsNames.ThemeMatchSection, SettingsNames.WhereQuestion(theme, false), MessageBoxButton.YesNo,
            ThemedDialogKind.Info, primaryLabel: SettingsNames.SaveAsNewTheme, secondaryLabel: SettingsNames.ThemeMatchCancel) == MessageBoxResult.Yes
            ? ThemeEditChoice.NewTheme
            : ThemeEditChoice.Cancel;
    }

    /// <summary>"Save as a new theme": the name, the suggestion filled in; null when cancelled.</summary>
    public static string? Name(string suggested)
    {
        var window = new TextPromptWindow(SettingsNames.SaveAsNewTheme, SettingsNames.NewThemeNamePrompt, suggested);
        if (Owner is { } owner) window.Owner = owner;
        return window.ShowDialog() == true ? window.Value : null;
    }
```

In `src/Sanduhr.App/Services/ThemeMatchService.cs`, after `public Func<string, ThemeAddChoice> AskAddOrNew { get; set; } = ThemeMatchPrompts.AddOrNew;` add:

```csharp
    public Func<string, bool, ThemeEditChoice> AskWhere { get; set; } = ThemeMatchPrompts.Where;
    public Func<string, string?> AskName { get; set; } = ThemeMatchPrompts.Name;
```

and after `MatchNow` add:

```csharp

    /// <summary>
    /// A hand edit while following (spec: asked once per edit, never per tick): <paramref name="next"/> is the whole desk block with the
    /// change. Update writes it into the active theme of the person's (never offered for a built-in or a pack's theme); Save as a new
    /// theme makes one, named by the person, and makes it active; Cancel leaves everything as it was (the caller shows the theme's
    /// values again). Not following: nothing.
    /// </summary>
    public ThemeMatchResult Edit(ThemeDesk next)
    {
        if (!Following) return new(false, "");
        string key = ActiveKey;
        var def = Active;
        bool canUpdate = ThemeMatch.Origin(key) == ThemeOrigin.User;
        switch (AskWhere(def.Name, canUpdate))
        {
            case ThemeEditChoice.UpdateTheme when canUpdate:
                if (Write(key, ThemeMatch.WithDesk(def, next)) is { } failed) return failed;
                _widget.ReloadUserThemes();
                _widget.ApplyThemeByKey(key);
                return new(true, SettingsNames.ThemeUpdated(def.Name));
            case ThemeEditChoice.NewTheme:
                if (ThemeMatch.CleanName(AskName(ThemeMatch.NewThemeName(def.Name, NameTaken))) is not { } name) return new(false, "");
                string newKey = ThemeMatch.NewThemeKey(name, KeyTaken);
                if (Write(newKey, ThemeMatch.WithDesk(def, next, name)) is { } bad) return bad;
                _widget.ReloadUserThemes();
                _widget.ApplyThemeByKey(newKey);
                return new(true, SettingsNames.FollowingTheme(name));
            default:
                return new(false, "");
        }
    }
```

- [ ] **Step 5: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~ThemeMatchServiceTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Services/ThemeMatchService.cs windows-dotnet/src/Sanduhr.App/Views/ThemeMatchPrompts.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.App.Tests/ThemeMatchServiceTests.cs
git commit -m "feat(theme-match): a hand edit while following asks Update or Save as a new theme; a built-in is never offered Update"
```

---

### Task 13: Desk Look opens on "Match the widget theme": Follow, Match now, and the own look greyed while following

**Files:**
- Create: `src/Sanduhr.App/ViewModels/ThemeMatchViewModel.cs`
- Modify: `src/Sanduhr.Core/SettingsIndex.cs` (a constant after `DeskSizes` ~101; a row before the Fonts row ~233; `NameAnchors` ~329; `JumpLinks` ~728)
- Modify: `src/Sanduhr.Core/SettingsNames.cs` (the theme-match block; `Table` after `(MessageColor, SettingsPage.DeskLook),`)
- Modify: `src/Sanduhr.App/Views/DeskLookPage.xaml` (after the jump links ~25; the Desk font grid ~32; Ink and color ~100-104 and ~141; Back plate ~177)
- Modify: `src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (a property near `WeatherPage` ~49; the constructor after `WeatherPage = …` ~166)
- Modify: `src/Sanduhr.App/ViewModels/DeskContentPagesViewModels.cs` (`NowPlayingPageViewModel`, after `Refresh` ~165)
- Modify: `src/Sanduhr.App/App.xaml.cs` (`svm.Desk.Changed = …` ~721; `_settingsWindow.Closed += …` ~744)
- Test: `tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs` (~189-203 and a new fact), `tests/Sanduhr.App.Tests/ThemeMatchSettingsTests.cs` (create)

**Interfaces:**
- Consumes: `ThemeMatchService` (Tasks 11, 12), `DeskController.PlateForced`/`PlateForcedChanged` (Task 8).
- Produces: `SettingsIndex.DeskThemeMatch = "theme-match"` (first entry and first jump link of Desk Look); SettingsNames `MatchNowButton`, `FollowThemeCaption`, `MatchNowCaption`, `ThemeMatchPlateNote`, `ThemeLookSetsThis`; `public sealed partial class ThemeMatchViewModel` (internal ctor `(ThemeMatchService service)`; `Follow` (bool, two-way), `NotFollowing`, `Status`, `PlateNote`, `MatchNowCommand`, `Changed` (`Action?`), `PlateForced` (`Func<bool>`), `Reload()`, internal `Service`); `SettingsViewModel.ThemeMatch`; `NowPlayingPageViewModel.ReloadSettings()`.

- [ ] **Step 1: Write the failing tests**

In `tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs`, replace

```csharp
    public void Desk_Look_holds_four_sections_and_the_old_links_land_there()
    {
        Assert.Equal([SettingsIndex.DeskLook, SettingsIndex.DeskSizes, SettingsIndex.DeskInk, SettingsIndex.DeskPlate],
            SettingsIndex.EntriesOn(SettingsPage.DeskLook).Select(e => e.Anchor));
        Assert.Equal([SettingsNames.FontsSection, SettingsNames.SizesSection, SettingsNames.InkAndColor, SettingsNames.BackPlate],
            SettingsIndex.EntriesOn(SettingsPage.DeskLook).Select(e => e.Title));
```

with

```csharp
    public void Desk_Look_holds_five_sections_and_the_old_links_land_there()
    {
        // Theme match put "Match the widget theme" first.
        Assert.Equal([SettingsIndex.DeskThemeMatch, SettingsIndex.DeskLook, SettingsIndex.DeskSizes, SettingsIndex.DeskInk, SettingsIndex.DeskPlate],
            SettingsIndex.EntriesOn(SettingsPage.DeskLook).Select(e => e.Anchor));
        Assert.Equal([SettingsNames.ThemeMatchSection, SettingsNames.FontsSection, SettingsNames.SizesSection, SettingsNames.InkAndColor, SettingsNames.BackPlate],
            SettingsIndex.EntriesOn(SettingsPage.DeskLook).Select(e => e.Title));
```

and append to the class:

```csharp
    // ---- theme match: Desk Look's first section ----

    [Fact]
    public void Match_the_widget_theme_is_Desk_Looks_first_section_and_first_jump_link()
    {
        Assert.Equal((SettingsNames.ThemeMatchSection, SettingsIndex.DeskThemeMatch), SettingsIndex.JumpLinks(SettingsPage.DeskLook)[0]);
        Assert.Contains(SettingsIndex.Search("follow the widget theme"), h => h.Entry.Page == SettingsPage.DeskLook && h.Entry.Anchor == SettingsIndex.DeskThemeMatch);
        Assert.Contains(SettingsIndex.Search("match now"), h => h.Entry.Anchor == SettingsIndex.DeskThemeMatch);
        Assert.Empty(SettingsNames.PathProblems("Settings, Desk Look, Match the widget theme"));
        foreach (var name in new[] { SettingsNames.FollowThemeSwitch, SettingsNames.MatchNowButton })
            Assert.Equal(SettingsPage.DeskLook, SettingsNames.Table.Single(t => t.Name == name).Page);
    }
```

Create `tests/Sanduhr.App.Tests/ThemeMatchSettingsTests.cs`:

```csharp
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Theme match on a real SettingsWindow over scratch folders: Desk Look's first section, Follow and Match now, and the Desk's own
/// look greyed while it follows. Every window is shown through <see cref="NoActivate"/>.</summary>
[Collection("SettingsWindow")]
public sealed class ThemeMatchSettingsTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Svm, NoActivate Guard, WidgetViewModel Widget) Open()
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        svm.ThemeMatch.Service.AskAddOrNew = _ => throw new Xunit.Sdk.XunitException("asked add-or-new");
        var window = new SettingsWindow(svm);
        return (window, svm, NoActivate.Show(window), widget);
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
    public void Desk_Look_opens_on_Match_the_widget_theme_with_its_switch_and_button() => AppHost.Run(() =>
    {
        var (window, _, guard, _) = Open();
        try
        {
            window.SelectPage(SettingsPage.DeskLook, SettingsIndex.DeskThemeMatch);
            Settle(window, guard);
            var section = Marked(window, SettingsPage.DeskLook, SettingsIndex.DeskThemeMatch);
            Assert.Contains(SettingsNames.FollowThemeSwitch, Below<CheckBox>(section).Select(AutomationProperties.GetName));
            Assert.Contains(SettingsNames.MatchNowButton, Below<Button>(section).Select(AutomationProperties.GetName));
            Assert.Equal(new SettingsLink(SettingsPage.DeskLook, SettingsIndex.DeskThemeMatch), window.Place);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Following_greys_the_desks_own_ink_and_plate_and_Match_now() => AppHost.Run(() =>
    {
        var (window, svm, guard, widget) = Open();
        try
        {
            window.SelectPage(SettingsPage.DeskLook);
            Settle(window, guard);
            svm.ThemeMatch.Follow = true;   // Obsidian is a built-in: a new theme is made, nothing asked
            Settle(window, guard);
            Assert.Equal("obsidian-desk", widget.ActiveThemeKey);
            Assert.Equal(SettingsNames.FollowingTheme("Obsidian + Desk"), svm.ThemeMatch.Status);
            Assert.False(svm.ThemeMatch.MatchNowCommand.CanExecute(null));
            var ink = Marked(window, SettingsPage.DeskLook, SettingsIndex.DeskInk);
            Assert.All(Below<ComboBox>(ink), c => Assert.False(c.IsEnabled));
            Assert.Contains(Below<TextBlock>(ink), t => t.Text == SettingsNames.ThemeLookSetsThis && t.IsVisible);
            var plate = Marked(window, SettingsPage.DeskLook, SettingsIndex.DeskPlate);
            Assert.All(Below<CheckBox>(plate), c => Assert.False(c.IsEnabled));

            svm.ThemeMatch.Follow = false;   // the look stays, as the Desk's own: the controls read it again
            Settle(window, guard);
            Assert.All(Below<ComboBox>(ink), c => Assert.True(c.IsEnabled));
            Assert.Equal(DeskInkMode.Custom, svm.Desk.InkChoice.Mode);
            Assert.True(svm.ThemeMatch.MatchNowCommand.CanExecute(null));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Cancel_on_a_theme_of_yours_puts_the_switch_back() => AppHost.Run(() =>
    {
        var (window, svm, guard, widget) = Open();
        try
        {
            var dusk = ThemeCatalog.BuiltIns["aurora"] with { Name = "Dusk" };
            System.IO.Directory.CreateDirectory(widget.ThemesDir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(widget.ThemesDir, "dusk.json"), ThemeDraft.FromDefinition("dusk", dusk).ToJsonText());
            widget.ReloadUserThemes();
            widget.ApplyThemeByKey("dusk");
            svm.ThemeMatch.Service.AskAddOrNew = _ => ThemeAddChoice.Cancel;
            window.SelectPage(SettingsPage.DeskLook);
            Settle(window, guard);

            svm.ThemeMatch.Follow = true;
            Settle(window, guard);

            Assert.False(svm.ThemeMatch.Follow);
            var box = Below<CheckBox>(Marked(window, SettingsPage.DeskLook, SettingsIndex.DeskThemeMatch))
                .Single(c => AutomationProperties.GetName(c) == SettingsNames.FollowThemeSwitch);
            Assert.False(box.IsChecked);
        }
        finally { window.Close(); }
    });
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsDeskSplitIndexTests`
Expected: build FAILS with `'SettingsIndex' does not contain a definition for 'DeskThemeMatch'`.

- [ ] **Step 3: The index and the names**

In `src/Sanduhr.Core/SettingsIndex.cs`, replace

```csharp
    /// <summary>Desk Look, Sizes (slice 3).</summary>
    public const string DeskSizes = "sizes";
```

with

```csharp
    /// <summary>Desk Look, Sizes (slice 3).</summary>
    public const string DeskSizes = "sizes";
    /// <summary>Desk Look, Match the widget theme (theme match): the page's first section.</summary>
    public const string DeskThemeMatch = "theme-match";
```

replace

```csharp
        E(SettingsNames.FontsSection, SettingsPage.DeskLook, DeskLook,
```

with

```csharp
        E(SettingsNames.ThemeMatchSection, SettingsPage.DeskLook, DeskThemeMatch,
            [SettingsNames.FollowThemeSwitch, SettingsNames.MatchNowButton, "widget theme", "match", "matching", "follow",
             "same look", "one theme", "plate color", "shadow color", "glow color"]),
        E(SettingsNames.FontsSection, SettingsPage.DeskLook, DeskLook,
```

replace

```csharp
        [SettingsNames.MessageColor] = DeskInk,
```

with

```csharp
        [SettingsNames.MessageColor] = DeskInk,
        [SettingsNames.FollowThemeSwitch] = DeskThemeMatch,
        [SettingsNames.MatchNowButton] = DeskThemeMatch,
```

and

```csharp
        SettingsPage.DeskLook => Links(page, DeskLook, DeskSizes, DeskInk, DeskPlate),
```

with

```csharp
        SettingsPage.DeskLook => Links(page, DeskThemeMatch, DeskLook, DeskSizes, DeskInk, DeskPlate),
```

In `src/Sanduhr.Core/SettingsNames.cs`, after `public const string FollowThemeSwitch = "Follow the widget theme";` add:

```csharp
    public const string FollowThemeCaption =
        "The Desk draws its colors, gradients, font, plate and shadow from the widget's theme, and changes with it. The look is kept in a theme of yours, so it travels with the theme.";
    public const string MatchNowButton = "Match now";
    public const string MatchNowCaption =
        "Copies the widget theme's look into the Desk's own settings once. The Desk keeps it when the theme changes.";
    /// <summary>The readability guard's line (spec, "The rules").</summary>
    public const string ThemeMatchPlateNote = "The plate is on so the clock reads over this wallpaper.";
    /// <summary>Under Desk Look's own look controls while the Desk follows the theme.</summary>
    public const string ThemeLookSetsThis = "The widget theme sets this while the Desk follows it. Change it under Match the widget theme.";
```

and in `Table`, replace

```csharp
        (MessageColor, SettingsPage.DeskLook),
```

with

```csharp
        (MessageColor, SettingsPage.DeskLook),
        (FollowThemeSwitch, SettingsPage.DeskLook),
        (MatchNowButton, SettingsPage.DeskLook),
```

- [ ] **Step 4: The view model**

Create `src/Sanduhr.App/ViewModels/ThemeMatchViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Settings, Desk Look, Match the widget theme (theme match): the Follow switch, Match now, a status line, and the plate note while the
/// readability guard holds the plate on. A cancelled Follow puts the switch back; Match now is off while following (Follow already keeps
/// the look).
/// </summary>
public sealed partial class ThemeMatchViewModel : ObservableObject
{
    private readonly ThemeMatchService _service;
    private bool _loading;

    internal ThemeMatchViewModel(ThemeMatchService service)
    {
        _service = service;
        Reload();
    }

    /// <summary>The service (tests replace its questions).</summary>
    internal ThemeMatchService Service => _service;

    /// <summary>Called after an action went ahead (SettingsViewModel reads the Desk's own look again and the Desk redraws).</summary>
    public Action? Changed { get; set; }

    /// <summary>True while the running Desk holds the plate on for readability (App sets it).</summary>
    public Func<bool> PlateForced { get; set; } = () => false;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(NotFollowing), nameof(PlateNote)), NotifyCanExecuteChangedFor(nameof(MatchNowCommand))]
    private bool _follow;

    [ObservableProperty] private string _status = "";

    public bool NotFollowing => !Follow;

    public string PlateNote => Follow && PlateForced() ? SettingsNames.ThemeMatchPlateNote : "";

    /// <summary>Reads the switch again (and the plate note) without acting on it.</summary>
    public void Reload()
    {
        _loading = true;
        Follow = _service.Following;
        _loading = false;
        OnPropertyChanged(nameof(PlateNote));
    }

    partial void OnFollowChanged(bool value)
    {
        if (_loading) return;
        var result = value ? _service.FollowOn() : _service.FollowOff();
        Status = result.Message;
        Reload();   // a Cancel, or a theme that could not be written, puts the switch back
        if (result.Ok) Changed?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(NotFollowing))]
    private void MatchNow()
    {
        var result = _service.MatchNow();
        Status = result.Message;
        if (result.Ok) Changed?.Invoke();
    }
}
```

In `src/Sanduhr.App/ViewModels/DeskContentPagesViewModels.cs`, after `NowPlayingPageViewModel.Refresh()`'s closing brace add:

```csharp

    /// <summary>The settings changed behind the page (theme match's Follow off or Match now turned Gradient bars on): read them again.</summary>
    public void ReloadSettings()
    {
        _saved = _store.LoadNowPlaying();
        Load();
    }
```

In `src/Sanduhr.App/ViewModels/SettingsViewModel.cs`, after the `WeatherPage` property add:

```csharp

    /// <summary>Settings, Desk Look, Match the widget theme (theme match).</summary>
    public ThemeMatchViewModel ThemeMatch { get; }
```

and after `WeatherPage = new WeatherPageViewModel(new SettingsStore(new Paths()));` add:

```csharp
        ThemeMatch = new ThemeMatchViewModel(new ThemeMatchService(new SettingsStore(new Paths()), widget));
        // Follow on and off and Match now change the Desk's switch or its own look: the pages that show them read them again, and the
        // running Desk follows (App sets Desk.Changed).
        ThemeMatch.Changed = () =>
        {
            Desk.RefreshFromStore();
            MessagePage.RefreshSettings();
            NowPlayingPage.ReloadSettings();
            Desk.Changed?.Invoke();
        };
```

- [ ] **Step 5: The page**

In `src/Sanduhr.App/Views/DeskLookPage.xaml`, replace

```xml
                <views:SectionJumpLinks Page="DeskLook" />
```

with

```xml
                <views:SectionJumpLinks Page="DeskLook" />

                <!-- Match the widget theme (theme match): Follow keeps the Desk in the active theme's look, kept in a theme of yours;
                     Match now copies the look into the Desk's own settings once. -->
                <StackPanel x:Name="ThemeMatchSection" views:SettingsAnchor.Id="theme-match" DataContext="{Binding ThemeMatch}">
                    <TextBlock Text="{x:Static core:SettingsNames.ThemeMatchSection}" FontSize="13" FontWeight="SemiBold"
                               AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                    <CheckBox Style="{DynamicResource ThemedCheckBox}"
                              Content="{x:Static core:SettingsNames.FollowThemeSwitch}"
                              IsChecked="{Binding Follow, Mode=TwoWay}"
                              AutomationProperties.Name="{x:Static core:SettingsNames.FollowThemeSwitch}"
                              AutomationProperties.HelpText="{x:Static core:SettingsNames.FollowThemeCaption}" />
                    <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,6,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                               Text="{x:Static core:SettingsNames.FollowThemeCaption}" />
                    <Button Style="{DynamicResource FlatButton}" HorizontalAlignment="Left" Margin="0,10,0,0"
                            Content="{x:Static core:SettingsNames.MatchNowButton}" Command="{Binding MatchNowCommand}"
                            AutomationProperties.Name="{x:Static core:SettingsNames.MatchNowButton}"
                            AutomationProperties.HelpText="{x:Static core:SettingsNames.MatchNowCaption}" />
                    <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,6,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                               Text="{x:Static core:SettingsNames.MatchNowCaption}" />
                    <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,8,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                               Text="{Binding Status}" Visibility="{Binding Status, Converter={StaticResource StrVis}}"
                               AutomationProperties.LiveSetting="Polite" />
                    <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,4,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                               Text="{Binding PlateNote}" Visibility="{Binding PlateNote, Converter={StaticResource StrVis}}"
                               AutomationProperties.LiveSetting="Polite" />
                </StackPanel>

                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />
```

Replace

```xml
                    <Grid DataContext="{Binding Desk}">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="110" />
                            <ColumnDefinition Width="230" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Text="{x:Static core:SettingsNames.DeskFont}"
```

with

```xml
                    <Grid DataContext="{Binding Desk}"
                          IsEnabled="{Binding DataContext.ThemeMatch.NotFollowing, ElementName=PageBody}">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="110" />
                            <ColumnDefinition Width="230" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Text="{x:Static core:SettingsNames.DeskFont}"
```

Replace

```xml
                    <TextBlock Text="{x:Static core:SettingsNames.InkAndColor}" FontSize="13" FontWeight="SemiBold"
                               AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                    <StackPanel DataContext="{Binding Desk}">
```

with

```xml
                    <TextBlock Text="{x:Static core:SettingsNames.InkAndColor}" FontSize="13" FontWeight="SemiBold"
                               AutomationProperties.HeadingLevel="Level2"
                               Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,8" />
                    <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,0,0,8"
                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                               Text="{x:Static core:SettingsNames.ThemeLookSetsThis}"
                               Visibility="{Binding DataContext.ThemeMatch.Follow, ElementName=PageBody, Converter={StaticResource BoolVis}}" />
                    <StackPanel DataContext="{Binding Desk}"
                                IsEnabled="{Binding DataContext.ThemeMatch.NotFollowing, ElementName=PageBody}">
```

Replace

```xml
                    <StackPanel Margin="0,14,0,0" DataContext="{Binding MessagePage}">
```

with

```xml
                    <StackPanel Margin="0,14,0,0" DataContext="{Binding MessagePage}"
                                IsEnabled="{Binding DataContext.ThemeMatch.NotFollowing, ElementName=PageBody}">
```

and replace

```xml
                    <CheckBox Style="{DynamicResource ThemedCheckBox}"
                              Content="{x:Static core:SettingsNames.DeskPlateSwitch}"
```

with

```xml
                    <CheckBox Style="{DynamicResource ThemedCheckBox}"
                              IsEnabled="{Binding DataContext.ThemeMatch.NotFollowing, ElementName=PageBody}"
                              Content="{x:Static core:SettingsNames.DeskPlateSwitch}"
```

- [ ] **Step 6: The app tells the section about the guard**

In `src/Sanduhr.App/App.xaml.cs`, replace

```csharp
        svm.Desk.Changed = () => _desk?.Reload();
```

with

```csharp
        svm.Desk.Changed = () => _desk?.Reload();
        // Desk Look, Match the widget theme: the plate note follows the running Desk's readability guard.
        svm.ThemeMatch.PlateForced = () => _desk?.PlateForced == true;
        Action plateShown = () => svm.ThemeMatch.Reload();
        if (_desk is not null) _desk.PlateForcedChanged += plateShown;
```

and

```csharp
            _settingsWindow.Closed += (_, _) => RunnerStatusShown -= showRunnerStatus;
```

with

```csharp
            _settingsWindow.Closed += (_, _) =>
            {
                RunnerStatusShown -= showRunnerStatus;
                if (_desk is not null) _desk.PlateForcedChanged -= plateShown;
            };
```

- [ ] **Step 7: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsDeskSplitIndexTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsReachIndexTests"` then `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~ThemeMatchSettingsTests|FullyQualifiedName~DeskSplitPagesTests|FullyQualifiedName~SettingsWindowNamesTests|FullyQualifiedName~RowNamesTests"`
Expected: PASS: the anchor scans see `theme-match` in the XAML and the index together; the own-file page scans (focus rings, `Focusable="False"` ScrollViewer, no split "Settings, " literals) stay green. `[DesktopFact]` tests in these classes run once.

- [ ] **Step 8: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.App/ViewModels/ThemeMatchViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/DeskContentPagesViewModels.cs windows-dotnet/src/Sanduhr.App/Views/DeskLookPage.xaml windows-dotnet/src/Sanduhr.App/App.xaml.cs windows-dotnet/tests/Sanduhr.Tests/SettingsDeskSplitIndexTests.cs windows-dotnet/tests/Sanduhr.App.Tests/ThemeMatchSettingsTests.cs
git commit -m "feat(theme-match): Desk Look opens on Match the widget theme: Follow, Match now, and the Desk's own look greyed while it follows"
```

---

### Task 14: "The theme's Desk look" editor; on Desk Look each committed edit asks where it goes

**Files:**
- Create: `src/Sanduhr.App/ViewModels/ThemeDeskEditorViewModel.cs`, `src/Sanduhr.App/Views/ThemeDeskEditor.xaml`, `src/Sanduhr.App/Views/ThemeDeskEditor.xaml.cs`
- Modify: `src/Sanduhr.App/Converters/StringToVisibilityConverter.cs` (a `ConverterParameter=empty`)
- Modify: `src/Sanduhr.App/ViewModels/ThemeMatchViewModel.cs` (the ctor, `Editor`, `Reload`)
- Modify: `src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (the `ThemeMatch` construction)
- Modify: `src/Sanduhr.App/Views/DeskLookPage.xaml` (the editor in the section)
- Modify: `src/Sanduhr.App/App.xaml.cs` (the section reloads on a theme change while Settings is open)
- Modify: `src/Sanduhr.Core/SettingsNames.cs` (the editor's names)
- Test: `tests/Sanduhr.App.Tests/ThemeDeskEditorTests.cs` (create), `tests/Sanduhr.App.Tests/ThemeMatchSettingsTests.cs`

**Interfaces:**
- Consumes: `ThemeDeskLook` (Task 3), `ThemeMatchService.Edit` and its seams (Task 12).
- Produces: `public sealed partial class ThemeDeskEditorViewModel(IReadOnlyList<string> fontFamilies)` with `FontChoices`, `Committed` (`Action<ThemeDesk>?`), two-way `Font`, `Ink`, `Bars`, `Message`, `Glow`, `Plate`, `PlateColor`, `Shadow`, the greyed `FontDefault`, `InkDefault`, `BarsDefault`, `MessageDefault`, `GlowDefault`, `PlateColorDefault`, `ShadowDefault`, `Problem`, `Desk` (the block now), `Load(ThemeDefinition)`, `ShowRules(ThemeDefinition)`, `internal static ParseStops(string)`; `ThemeDeskEditor` (UserControl, DataContext the editor); `ThemeMatchViewModel(ThemeMatchService, Func<ThemeDefinition> activeTheme, IReadOnlyList<string> fontFamilies)` and `ThemeMatchViewModel.Editor`. SettingsNames `ThemeDeskLookHeading`, `ThemeDeskFont`, `ThemeInkColors`, `ThemeBarColors`, `ThemeMessageColors`, `ThemeGlowColor`, `ThemePlateSwitch`, `ThemePlateColor`, `ThemeShadowColor`, `ThemeDeskFromTheme`, `DesksOwnFont`, `ThemeColorsProblem`, `ThemeColorProblem`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Sanduhr.App.Tests/ThemeDeskEditorTests.cs`:

```csharp
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Theme match, the theme's Desk look editor: rows start empty with the rules' values greyed, a row commits the whole block
/// with its change, a row that does not parse commits nothing and says why, and clearing a row gives the part back to its rule.</summary>
public sealed class ThemeDeskEditorTests
{
    private static (ThemeDeskEditorViewModel Editor, List<ThemeDesk> Commits) Editor(ThemeDefinition theme)
    {
        var commits = new List<ThemeDesk>();
        var e = new ThemeDeskEditorViewModel(["Consolas", "Segoe UI"]) { Committed = commits.Add };
        e.Load(theme);
        return (e, commits);
    }

    [Fact]
    public void Rows_start_empty_with_the_rules_greyed() => Sta.Run(() =>
    {
        var (e, commits) = Editor(ThemeCatalog.BuiltIns["obsidian"]);
        Assert.Equal("", e.Ink);
        Assert.Equal("#e8e4dc, #6c63ff", e.InkDefault);
        Assert.Equal("#6c63ff, #e8e4dc", e.BarsDefault);
        Assert.Equal("#6c63ff", e.GlowDefault);
        Assert.Equal("#0a0a0a", e.PlateColorDefault);
        Assert.Equal("#070707", e.ShadowDefault);
        Assert.True(e.Plate);
        Assert.Equal(SettingsNames.ThemeDeskFromTheme, e.Font);
        Assert.Equal(SettingsNames.DesksOwnFont, e.FontDefault);
        Assert.Empty(commits);   // showing a theme is no edit
    });

    [Fact]
    public void A_row_commits_the_whole_block_with_its_change() => Sta.Run(() =>
    {
        var (e, commits) = Editor(ThemeCatalog.BuiltIns["obsidian"]);
        e.Ink = "#FFF, 9ad7ff";
        Assert.Equal(["#ffffff", "#9ad7ff"], commits[^1].Ink);
        e.Plate = false;
        Assert.False(commits[^1].Plate);
        Assert.Equal(["#ffffff", "#9ad7ff"], commits[^1].Ink);   // the earlier change rides along
        e.Font = "Consolas";
        Assert.Equal("Consolas", commits[^1].Font);
        e.Glow = "#38bdf8";
        Assert.Equal("#38bdf8", commits[^1].Glow);
        Assert.Null(e.Problem);
    });

    [Fact]
    public void A_row_that_does_not_parse_commits_nothing_and_says_why() => Sta.Run(() =>
    {
        var (e, commits) = Editor(ThemeCatalog.BuiltIns["obsidian"]);
        e.Bars = "red";
        Assert.Empty(commits);
        Assert.Equal(SettingsNames.ThemeColorsProblem, e.Problem);
        e.Bars = "#111111, #222222, #333333, #444444, #555555";
        Assert.Empty(commits);
        e.Glow = "#6c63ff, #ffffff";
        Assert.Equal(SettingsNames.ThemeColorProblem, e.Problem);
        Assert.Empty(commits);
        e.Bars = "#111111";
        Assert.Null(e.Problem);
        Assert.Equal(["#111111"], Assert.Single(commits).Bars);
    });

    [Fact]
    public void A_theme_with_a_desk_block_shows_it_and_clearing_a_row_gives_it_back_to_the_rule() => Sta.Run(() =>
    {
        var (e, commits) = Editor(ThemeCatalog.BuiltIns["obsidian"] with { Desk = new ThemeDesk { Ink = ["#ffffff"], Plate = false, Font = "Consolas" } });
        Assert.Equal("#ffffff", e.Ink);
        Assert.False(e.Plate);
        Assert.Equal("Consolas", e.Font);
        e.Ink = "";
        Assert.Null(commits[^1].Ink);
        Assert.False(commits[^1].Plate);
    });
}
```

Append to `ThemeMatchSettingsTests`:

```csharp
    [Fact]
    public void The_editor_shows_only_while_following_and_an_edit_updates_the_theme() => AppHost.Run(() =>
    {
        var (window, svm, guard, widget) = Open();
        try
        {
            window.SelectPage(SettingsPage.DeskLook);
            Settle(window, guard);
            var section = Marked(window, SettingsPage.DeskLook, SettingsIndex.DeskThemeMatch);
            Assert.False(Below<ThemeDeskEditor>(section).Single().IsVisible);

            svm.ThemeMatch.Follow = true;
            Settle(window, guard);
            Assert.True(Below<ThemeDeskEditor>(section).Single().IsVisible);
            Assert.Equal("#e8e4dc, #6c63ff", svm.ThemeMatch.Editor.Ink);   // Follow spelled the look out into Obsidian + Desk

            svm.ThemeMatch.Service.AskWhere = (_, canUpdate) => { Assert.True(canUpdate); return ThemeEditChoice.UpdateTheme; };
            svm.ThemeMatch.Editor.Ink = "#ffffff";
            Settle(window, guard);
            Assert.Equal(["#ffffff"], widget.GetThemeDefinition("obsidian-desk")!.Desk!.Ink);
            Assert.Equal(SettingsNames.ThemeUpdated("Obsidian + Desk"), svm.ThemeMatch.Status);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_cancelled_edit_puts_the_editor_back() => AppHost.Run(() =>
    {
        var (window, svm, guard, widget) = Open();
        try
        {
            svm.ThemeMatch.Follow = true;
            svm.ThemeMatch.Service.AskWhere = (_, _) => ThemeEditChoice.Cancel;
            svm.ThemeMatch.Editor.Ink = "#ffffff";
            Settle(window, guard);
            Assert.Equal("#e8e4dc, #6c63ff", svm.ThemeMatch.Editor.Ink);
            Assert.Equal(["#e8e4dc", "#6c63ff"], widget.GetThemeDefinition("obsidian-desk")!.Desk!.Ink);
        }
        finally { window.Close(); }
    });
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~ThemeDeskEditorTests|FullyQualifiedName~ThemeMatchSettingsTests"`
Expected: build FAILS with `The type or namespace name 'ThemeDeskEditorViewModel' could not be found` and `'ThemeMatchViewModel' does not contain a definition for 'Editor'`.

- [ ] **Step 3: The names**

In `src/Sanduhr.Core/SettingsNames.cs`, after `public static string ThemeUpdated(string theme) => $"Updated {theme}.";` add:

```csharp
    /// <summary>The theme's Desk look editor (Desk Look while following; the Theme Studio).</summary>
    public const string ThemeDeskLookHeading = "The theme's Desk look";
    public const string ThemeDeskFont = "Desk font in this theme";
    public const string ThemeInkColors = "Ink colors";
    public const string ThemeBarColors = "Meter bar colors";
    public const string ThemeMessageColors = "Message colors";
    public const string ThemeGlowColor = "Message glow color";
    public const string ThemePlateSwitch = "Plate behind the text in this theme";
    public const string ThemePlateColor = "Plate color";
    public const string ThemeShadowColor = "Shadow color";
    public const string ThemeDeskFromTheme = "From the theme";
    public const string DesksOwnFont = "The Desk's own font";
    public const string ThemeColorsProblem = "Use one to four hex colors, like #e8e4dc, #6c63ff.";
    public const string ThemeColorProblem = "Use one hex color, like #6c63ff.";
```

- [ ] **Step 4: The editor**

Create `src/Sanduhr.App/ViewModels/ThemeDeskEditorViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// "The theme's Desk look" (theme match): one row per part of a theme's desk block (the Desk font, the ink, the meter bars, the message,
/// the glow, the plate and its tint, the shadow). A row is empty until set and shows the rule's value greyed meanwhile. Desk Look (while
/// the Desk follows) and the Theme Studio both use it. A row commits when it is left (a text box losing focus, the checkbox, a font
/// picked), never per keystroke; a row that does not parse commits nothing and says why.
/// </summary>
public sealed partial class ThemeDeskEditorViewModel : ObservableObject
{
    private bool _loading;
    private ThemeDesk _desk = new();

    public ThemeDeskEditorViewModel(IReadOnlyList<string> fontFamilies)
    {
        FontChoices = [SettingsNames.ThemeDeskFromTheme, .. fontFamilies];
    }

    /// <summary>The Desk font row's choices: "From the theme" (no desk.font), then this PC's families.</summary>
    public IReadOnlyList<string> FontChoices { get; }

    /// <summary>A committed edit: the whole desk block with the change.</summary>
    public Action<ThemeDesk>? Committed { get; set; }

    /// <summary>The desk block as the rows hold it now.</summary>
    public ThemeDesk Desk => _desk;

    [ObservableProperty] private string _font = SettingsNames.ThemeDeskFromTheme;
    [ObservableProperty] private string _ink = "";
    [ObservableProperty] private string _bars = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _glow = "";
    [ObservableProperty] private bool _plate = true;
    [ObservableProperty] private string _plateColor = "";
    [ObservableProperty] private string _shadow = "";

    [ObservableProperty] private string _fontDefault = "";
    [ObservableProperty] private string _inkDefault = "";
    [ObservableProperty] private string _barsDefault = "";
    [ObservableProperty] private string _messageDefault = "";
    [ObservableProperty] private string _glowDefault = "";
    [ObservableProperty] private string _plateColorDefault = "";
    [ObservableProperty] private string _shadowDefault = "";

    /// <summary>Why the last edit was not committed; null when it was.</summary>
    [ObservableProperty] private string? _problem;

    /// <summary>Shows <paramref name="theme"/>'s desk block and its rules' values. Not an edit.</summary>
    public void Load(ThemeDefinition theme)
    {
        _loading = true;
        _desk = theme.Desk ?? new ThemeDesk();
        ShowRules(theme);
        Font = _desk.Font ?? SettingsNames.ThemeDeskFromTheme;
        Ink = Join(_desk.Ink);
        Bars = Join(_desk.Bars);
        Message = Join(_desk.Message);
        Glow = _desk.Glow ?? "";
        PlateColor = _desk.PlateColor ?? "";
        Shadow = _desk.Shadow ?? "";
        Plate = _desk.Plate ?? true;
        Problem = null;
        _loading = false;
    }

    /// <summary>The greyed values: <paramref name="theme"/>'s rules, its desk block left out (the Studio calls it as colors change).</summary>
    public void ShowRules(ThemeDefinition theme)
    {
        var rules = ThemeDeskLook.From(theme with { Desk = null });
        FontDefault = rules.Font ?? SettingsNames.DesksOwnFont;
        InkDefault = Join(rules.Ink);
        BarsDefault = Join(rules.Bars);
        MessageDefault = Join(rules.Message);
        GlowDefault = "#" + rules.Glow;
        PlateColorDefault = "#" + rules.PlateColor;
        ShadowDefault = "#" + rules.Shadow;
    }

    partial void OnFontChanged(string value) =>
        Commit(_desk with { Font = value is { Length: > 0 } f && f != SettingsNames.ThemeDeskFromTheme ? f : null });

    partial void OnInkChanged(string value) => CommitStops(value, s => _desk with { Ink = s });
    partial void OnBarsChanged(string value) => CommitStops(value, s => _desk with { Bars = s });
    partial void OnMessageChanged(string value) => CommitStops(value, s => _desk with { Message = s });
    partial void OnGlowChanged(string value) => CommitColor(value, c => _desk with { Glow = c });
    partial void OnPlateColorChanged(string value) => CommitColor(value, c => _desk with { PlateColor = c });
    partial void OnShadowChanged(string value) => CommitColor(value, c => _desk with { Shadow = c });
    partial void OnPlateChanged(bool value) => Commit(_desk with { Plate = value });

    private void CommitStops(string text, Func<IReadOnlyList<string>?, ThemeDesk> edit)
    {
        if (_loading) return;
        if (ParseStops(text) is not { } stops)
        {
            Problem = SettingsNames.ThemeColorsProblem;
            return;
        }
        Commit(edit(stops.Count == 0 ? null : stops));
    }

    private void CommitColor(string text, Func<string?, ThemeDesk> edit)
    {
        if (_loading) return;
        var t = (text ?? "").Trim();
        if (t.Length == 0)
        {
            Commit(edit(null));
            return;
        }
        if (t.Contains(',') || InkHex.Normalize(t) is not { } hex)
        {
            Problem = SettingsNames.ThemeColorProblem;
            return;
        }
        Commit(edit("#" + hex));
    }

    private void Commit(ThemeDesk next)
    {
        if (_loading) return;
        Problem = null;
        _desk = next;
        Committed?.Invoke(next);
    }

    /// <summary>"#e8e4dc, 6C63FF" is ["#e8e4dc", "#6c63ff"]; empty is []; a bad color or more than four is null.</summary>
    internal static IReadOnlyList<string>? ParseStops(string text)
    {
        var parts = (text ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > ThemeLint.MaxDeskStops) return null;
        var list = new List<string>(parts.Length);
        foreach (var p in parts)
        {
            if (InkHex.Normalize(p) is not { } hex) return null;
            list.Add("#" + hex);
        }
        return list;
    }

    private static string Join(IReadOnlyList<string>? stops) =>
        stops is null ? "" : string.Join(", ", stops.Select(s => "#" + s.TrimStart('#').ToLowerInvariant()));
}
```

In `src/Sanduhr.App/Converters/StringToVisibilityConverter.cs`, replace

```csharp
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
```

with

```csharp
    /// <remarks><c>ConverterParameter=empty</c> turns it around: visible only while the string is empty (theme match's greyed
    /// defaults show under an empty row).</remarks>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool empty = string.IsNullOrWhiteSpace(value as string);
        bool whenEmpty = string.Equals(parameter as string, "empty", StringComparison.Ordinal);
        return empty == whenEmpty ? Visibility.Visible : Visibility.Collapsed;
    }
```

Create `src/Sanduhr.App/Views/ThemeDeskEditor.xaml`:

```xml
<UserControl x:Class="Sanduhr.App.Views.ThemeDeskEditor"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:core="clr-namespace:Sanduhr.Core;assembly=Sanduhr.Core"
             xmlns:conv="clr-namespace:Sanduhr.App.Converters"
             Focusable="False">
    <!-- The theme's Desk look (theme match). DataContext: a ThemeDeskEditorViewModel. Each text row is empty until set and shows the
         rule's value greyed under it meanwhile; it commits when it loses focus. Hosted by Desk Look and the Theme Studio, whose styles
         it finds with DynamicResource. -->
    <UserControl.Resources>
        <conv:StringToVisibilityConverter x:Key="StrVis" />
    </UserControl.Resources>
    <StackPanel>
        <TextBlock Text="{x:Static core:SettingsNames.ThemeDeskLookHeading}" FontSize="12" FontWeight="SemiBold"
                   AutomationProperties.HeadingLevel="Level3"
                   Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,2" />

        <Grid Margin="0,6,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="160" />
                <ColumnDefinition Width="230" />
            </Grid.ColumnDefinitions>
            <TextBlock Text="{x:Static core:SettingsNames.ThemeDeskFont}" FontSize="12" VerticalAlignment="Center" TextWrapping="Wrap"
                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
            <ComboBox Grid.Column="1" Style="{DynamicResource ThemedComboBox}"
                      ItemsSource="{Binding FontChoices}" SelectedItem="{Binding Font, Mode=TwoWay}"
                      AutomationProperties.Name="{x:Static core:SettingsNames.ThemeDeskFont}"
                      AutomationProperties.HelpText="{Binding FontDefault}" />
        </Grid>

        <Grid Margin="0,6,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="160" />
                <ColumnDefinition Width="230" />
            </Grid.ColumnDefinitions>
            <TextBlock Text="{x:Static core:SettingsNames.ThemeInkColors}" FontSize="12" VerticalAlignment="Center"
                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
            <TextBox Grid.Column="1" Style="{DynamicResource Input}" FontFamily="Cascadia Code, Consolas"
                     Text="{Binding Ink, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                     AutomationProperties.Name="{x:Static core:SettingsNames.ThemeInkColors}"
                     AutomationProperties.HelpText="{Binding InkDefault}" />
            <TextBlock Grid.Column="1" Margin="8,0,0,0" VerticalAlignment="Center" IsHitTestVisible="False"
                       FontFamily="Cascadia Code, Consolas" FontSize="11" Foreground="{DynamicResource Sanduhr.Brush.TextMuted}"
                       Text="{Binding InkDefault}" Visibility="{Binding Ink, Converter={StaticResource StrVis}, ConverterParameter=empty}" />
        </Grid>

        <Grid Margin="0,6,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="160" />
                <ColumnDefinition Width="230" />
            </Grid.ColumnDefinitions>
            <TextBlock Text="{x:Static core:SettingsNames.ThemeBarColors}" FontSize="12" VerticalAlignment="Center"
                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
            <TextBox Grid.Column="1" Style="{DynamicResource Input}" FontFamily="Cascadia Code, Consolas"
                     Text="{Binding Bars, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                     AutomationProperties.Name="{x:Static core:SettingsNames.ThemeBarColors}"
                     AutomationProperties.HelpText="{Binding BarsDefault}" />
            <TextBlock Grid.Column="1" Margin="8,0,0,0" VerticalAlignment="Center" IsHitTestVisible="False"
                       FontFamily="Cascadia Code, Consolas" FontSize="11" Foreground="{DynamicResource Sanduhr.Brush.TextMuted}"
                       Text="{Binding BarsDefault}" Visibility="{Binding Bars, Converter={StaticResource StrVis}, ConverterParameter=empty}" />
        </Grid>

        <Grid Margin="0,6,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="160" />
                <ColumnDefinition Width="230" />
            </Grid.ColumnDefinitions>
            <TextBlock Text="{x:Static core:SettingsNames.ThemeMessageColors}" FontSize="12" VerticalAlignment="Center"
                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
            <TextBox Grid.Column="1" Style="{DynamicResource Input}" FontFamily="Cascadia Code, Consolas"
                     Text="{Binding Message, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                     AutomationProperties.Name="{x:Static core:SettingsNames.ThemeMessageColors}"
                     AutomationProperties.HelpText="{Binding MessageDefault}" />
            <TextBlock Grid.Column="1" Margin="8,0,0,0" VerticalAlignment="Center" IsHitTestVisible="False"
                       FontFamily="Cascadia Code, Consolas" FontSize="11" Foreground="{DynamicResource Sanduhr.Brush.TextMuted}"
                       Text="{Binding MessageDefault}" Visibility="{Binding Message, Converter={StaticResource StrVis}, ConverterParameter=empty}" />
        </Grid>

        <Grid Margin="0,6,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="160" />
                <ColumnDefinition Width="230" />
            </Grid.ColumnDefinitions>
            <TextBlock Text="{x:Static core:SettingsNames.ThemeGlowColor}" FontSize="12" VerticalAlignment="Center"
                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
            <TextBox Grid.Column="1" Style="{DynamicResource Input}" FontFamily="Cascadia Code, Consolas"
                     Text="{Binding Glow, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                     AutomationProperties.Name="{x:Static core:SettingsNames.ThemeGlowColor}"
                     AutomationProperties.HelpText="{Binding GlowDefault}" />
            <TextBlock Grid.Column="1" Margin="8,0,0,0" VerticalAlignment="Center" IsHitTestVisible="False"
                       FontFamily="Cascadia Code, Consolas" FontSize="11" Foreground="{DynamicResource Sanduhr.Brush.TextMuted}"
                       Text="{Binding GlowDefault}" Visibility="{Binding Glow, Converter={StaticResource StrVis}, ConverterParameter=empty}" />
        </Grid>

        <CheckBox Style="{DynamicResource ThemedCheckBox}" Margin="0,10,0,0"
                  Content="{x:Static core:SettingsNames.ThemePlateSwitch}" IsChecked="{Binding Plate, Mode=TwoWay}"
                  AutomationProperties.Name="{x:Static core:SettingsNames.ThemePlateSwitch}" />

        <Grid Margin="0,6,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="160" />
                <ColumnDefinition Width="230" />
            </Grid.ColumnDefinitions>
            <TextBlock Text="{x:Static core:SettingsNames.ThemePlateColor}" FontSize="12" VerticalAlignment="Center"
                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
            <TextBox Grid.Column="1" Style="{DynamicResource Input}" FontFamily="Cascadia Code, Consolas"
                     Text="{Binding PlateColor, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                     AutomationProperties.Name="{x:Static core:SettingsNames.ThemePlateColor}"
                     AutomationProperties.HelpText="{Binding PlateColorDefault}" />
            <TextBlock Grid.Column="1" Margin="8,0,0,0" VerticalAlignment="Center" IsHitTestVisible="False"
                       FontFamily="Cascadia Code, Consolas" FontSize="11" Foreground="{DynamicResource Sanduhr.Brush.TextMuted}"
                       Text="{Binding PlateColorDefault}" Visibility="{Binding PlateColor, Converter={StaticResource StrVis}, ConverterParameter=empty}" />
        </Grid>

        <Grid Margin="0,6,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="160" />
                <ColumnDefinition Width="230" />
            </Grid.ColumnDefinitions>
            <TextBlock Text="{x:Static core:SettingsNames.ThemeShadowColor}" FontSize="12" VerticalAlignment="Center"
                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
            <TextBox Grid.Column="1" Style="{DynamicResource Input}" FontFamily="Cascadia Code, Consolas"
                     Text="{Binding Shadow, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                     AutomationProperties.Name="{x:Static core:SettingsNames.ThemeShadowColor}"
                     AutomationProperties.HelpText="{Binding ShadowDefault}" />
            <TextBlock Grid.Column="1" Margin="8,0,0,0" VerticalAlignment="Center" IsHitTestVisible="False"
                       FontFamily="Cascadia Code, Consolas" FontSize="11" Foreground="{DynamicResource Sanduhr.Brush.TextMuted}"
                       Text="{Binding ShadowDefault}" Visibility="{Binding Shadow, Converter={StaticResource StrVis}, ConverterParameter=empty}" />
        </Grid>

        <TextBlock FontSize="11" TextWrapping="Wrap" Margin="160,4,0,0"
                   Foreground="{DynamicResource Sanduhr.Brush.PaceMarker}"
                   Text="{Binding Problem}" Visibility="{Binding Problem, Converter={StaticResource StrVis}}"
                   AutomationProperties.LiveSetting="Assertive" />
    </StackPanel>
</UserControl>
```

Create `src/Sanduhr.App/Views/ThemeDeskEditor.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace Sanduhr.App.Views;

/// <summary>The theme's Desk look editor (theme match): Desk Look while following, and the Theme Studio.</summary>
public partial class ThemeDeskEditor : UserControl
{
    public ThemeDeskEditor() => InitializeComponent();
}
```

- [ ] **Step 5: Desk Look edits ask where they go**

In `src/Sanduhr.App/ViewModels/ThemeMatchViewModel.cs`, replace

```csharp
    private readonly ThemeMatchService _service;
    private bool _loading;

    internal ThemeMatchViewModel(ThemeMatchService service)
    {
        _service = service;
        Reload();
    }
```

with

```csharp
    private readonly ThemeMatchService _service;
    private readonly Func<ThemeDefinition> _activeTheme;
    private bool _loading;

    /// <param name="activeTheme">The widget's saved theme (never a Studio preview): what the editor shows.</param>
    internal ThemeMatchViewModel(ThemeMatchService service, Func<ThemeDefinition> activeTheme, IReadOnlyList<string> fontFamilies)
    {
        _service = service;
        _activeTheme = activeTheme;
        Editor = new ThemeDeskEditorViewModel(fontFamilies) { Committed = OnEdited };
        Reload();
    }

    /// <summary>The theme's Desk look, shown while following; each committed edit asks where it goes.</summary>
    public ThemeDeskEditorViewModel Editor { get; }

    private void OnEdited(ThemeDesk next)
    {
        var result = _service.Edit(next);
        Status = result.Message;
        Reload();   // Update or a new theme: the theme now; Cancel: the theme as it was
    }
```

and in `Reload`, replace

```csharp
        Follow = _service.Following;
        _loading = false;
```

with

```csharp
        Follow = _service.Following;
        Editor.Load(_activeTheme());
        _loading = false;
```

In `src/Sanduhr.App/ViewModels/SettingsViewModel.cs`, replace

```csharp
        ThemeMatch = new ThemeMatchViewModel(new ThemeMatchService(new SettingsStore(new Paths()), widget));
```

with

```csharp
        ThemeMatch = new ThemeMatchViewModel(new ThemeMatchService(new SettingsStore(new Paths()), widget),
            () => widget.Palette.Definition, Sanduhr.App.Desk.DeskFonts.ForThisPc(new Paths()).PickerFamilies());
```

In `src/Sanduhr.App/Views/DeskLookPage.xaml`, in the section, replace

```xml
                               Text="{Binding PlateNote}" Visibility="{Binding PlateNote, Converter={StaticResource StrVis}}"
                               AutomationProperties.LiveSetting="Polite" />
                </StackPanel>
```

with

```xml
                               Text="{Binding PlateNote}" Visibility="{Binding PlateNote, Converter={StaticResource StrVis}}"
                               AutomationProperties.LiveSetting="Polite" />
                    <views:ThemeDeskEditor Margin="0,12,0,0" DataContext="{Binding Editor}"
                                           Visibility="{Binding DataContext.Follow, ElementName=ThemeMatchSection, Converter={StaticResource BoolVis}}" />
                </StackPanel>
```

In `src/Sanduhr.App/App.xaml.cs`, replace

```csharp
        Action plateShown = () => svm.ThemeMatch.Reload();
        if (_desk is not null) _desk.PlateForcedChanged += plateShown;
```

with

```csharp
        Action plateShown = () => svm.ThemeMatch.Reload();
        if (_desk is not null) _desk.PlateForcedChanged += plateShown;
        // The editor shows the theme now: a switch from the strip or a Claude proposal while Settings is open reloads it.
        Action<Sanduhr.App.Theming.ThemePalette> themeShown = _ => svm.ThemeMatch.Reload();
        _vm.ThemeChanged += themeShown;
```

and in the window's `Closed` handler add `_vm.ThemeChanged -= themeShown;` after `RunnerStatusShown -= showRunnerStatus;`.

- [ ] **Step 6: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~ThemeDeskEditorTests|FullyQualifiedName~ThemeMatchSettingsTests|FullyQualifiedName~DeskSplitPagesTests|FullyQualifiedName~SettingsWindowNamesTests"` and `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsSourceScanTests`
Expected: PASS (the focus-ring scan reads the new control: it uses the window's styles, no local `FocusVisualStyle`).

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/ThemeDeskEditorViewModel.cs windows-dotnet/src/Sanduhr.App/Views/ThemeDeskEditor.xaml windows-dotnet/src/Sanduhr.App/Views/ThemeDeskEditor.xaml.cs windows-dotnet/src/Sanduhr.App/Converters/StringToVisibilityConverter.cs windows-dotnet/src/Sanduhr.App/ViewModels/ThemeMatchViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.App/Views/DeskLookPage.xaml windows-dotnet/src/Sanduhr.App/App.xaml.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.App.Tests/ThemeDeskEditorTests.cs windows-dotnet/tests/Sanduhr.App.Tests/ThemeMatchSettingsTests.cs
git commit -m "feat(theme-match): the theme's Desk look editor; on Desk Look each committed edit asks Update, Save as new or Cancel"
```

---

### Task 15: The server: `ThemeDeskLook.cs` linked, `get_desk` reports `theme_look`, followed designs judged in the theme's look, the schemas

**Files:**
- Modify: `src/Sanduhr.Mcp/Sanduhr.Mcp.csproj` (after the `ThemeLint.cs` link ~55)
- Modify: `tests/Sanduhr.Mcp.Tests/TrustBoundaryTests.cs` (the list ~83; the pure-file theory ~120)
- Modify: `src/Sanduhr.Mcp/ToolLogic.Desk.cs` (`BuildGetDesk` ~53-56)
- Modify: `src/Sanduhr.Core/DeskDesignValidator.cs` (`CheckContrast` ~166-227)
- Modify: `src/Sanduhr.Mcp/ToolCatalog.cs` (propose_theme ~167-182; propose_desk ~303-308; `DeskDesignGuideRead` ~607; `DeskDesignGuidePropose` ~644)
- Test: `tests/Sanduhr.Mcp.Tests/ToolLogicDeskDesignTests.cs`, `tests/Sanduhr.Mcp.Tests/ToolLogicThemeTests.cs`, `tests/Sanduhr.Tests/DeskDesignValidatorTests.cs`

**Interfaces:**
- Consumes: `ThemeDeskLook` (Task 3), `DeskDesign.FollowsTheme` (Task 10), `ThemeLint` (Task 1).
- Produces: `get_desk` answers `theme_look` (`ThemeDeskLook.ToJson()` of `design.theme`) when the state's design carries a theme that lints; a design with `follows_theme: true` and a theme is judged with the theme's ink (its first stop, Decision 3) and plate, the guard applied, a failure's field `follows_theme`; the tool descriptions name `font`, `desk`, `follows_theme` and `theme_look`.

- [ ] **Step 1: Write the failing tests**

In `tests/Sanduhr.Mcp.Tests/TrustBoundaryTests.cs`, in `Source_link_list_is_exactly_the_allowlisted_slice`, replace

```csharp
                // propose_theme: the lint and the model it builds, both pure. The
                // widget owns every write under the themes folder.
                "ThemeLint.cs", "ThemeModel.cs", "TierModel.cs",
```

with

```csharp
                // theme match (2026-10-25, decision zIsGHT5TiD5lbEwP4AiQ): ThemeDeskLook turns a theme into the Desk's look, so
                // get_desk's theme_look and the check of a design that follows the theme compute what the widget draws. Pure
                // (colors and arithmetic; no IO, network, credentials or process).
                "ThemeDeskLook.cs",
                // propose_theme: the lint and the model it builds, both pure. The
                // widget owns every write under the themes folder.
                "ThemeLint.cs", "ThemeModel.cs", "TierModel.cs",
```

and in `Files_linked_for_watchers_desk_messages_and_desk_designs_are_pure`, after `[InlineData("WidgetProposal.cs")]` add `[InlineData("ThemeDeskLook.cs")]`.

Append to the class in `tests/Sanduhr.Mcp.Tests/ToolLogicDeskDesignTests.cs`:

```csharp
    // ---- theme match ----

    private static JsonNode Obsidian() => JsonNode.Parse("""
        {"name": "Obsidian", "bg": "#0d0d0d", "glass": "#1c1c1c", "glass_on_mica": "#1a1a1c", "title_bg": "#161616",
         "border": "#333333", "footer_bg": "#111111", "bar_bg": "#2a2a2a", "text": "#e8e4dc", "text_secondary": "#b8b4ac",
         "text_dim": "#777777", "text_muted": "#555555", "accent": "#6c63ff", "pace_marker": "#ff6b6b", "sparkline": "#6c63ff"}
        """)!;

    [Fact]
    public void Get_reports_the_theme_look_the_widget_would_draw_and_whether_the_desk_follows()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        WriteState(cfg, s =>
        {
            s["design"]!["theme"] = Obsidian();
            s["design"]!["follows_theme"] = true;
        });

        var r = new ToolLogic(cfg, () => Now).BuildGetDesk();

        Assert.True((bool)r["design"]!["follows_theme"]!);
        Assert.Equal("#e8e4dc", (string?)r["theme_look"]!["ink"]![0]);
        Assert.Equal("#0a0a0a", (string?)r["theme_look"]!["plate_color"]);
        Assert.Equal("#070707", (string?)r["theme_look"]!["shadow"]);
    }

    [Fact]
    public void Get_reports_no_theme_look_without_a_theme()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        WriteState(cfg);
        Assert.Null(new ToolLogic(cfg, () => Now).BuildGetDesk()["theme_look"]);
    }
```

Append to the class in `tests/Sanduhr.Mcp.Tests/ToolLogicThemeTests.cs`:

```csharp
    [Fact]
    public void A_theme_with_font_and_desk_is_handed_over_and_a_bad_desk_is_refused()
    {
        using var tmp = new TempDir();
        var cfg = ThemeConfig(tmp, TimeSpan.FromMilliseconds(50));
        var bad = CleanTheme();
        bad["desk"] = JsonNode.Parse("""{"ink": "white"}""");

        var refused = new ToolLogic(cfg, () => Now).BuildProposeTheme(bad, null, true);

        Assert.Equal("rejected", (string?)refused["status"]);
        Assert.Contains(((JsonArray)refused["findings"]!).Select(f => (string?)f!["field"]), f => f == "desk.ink");
        Assert.False(File.Exists(cfg.ThemeRequestPath));

        var good = CleanTheme();
        good["font"] = "Segoe UI";
        good["desk"] = JsonNode.Parse("""{"bars": ["#6c63ff", "#e8e4dc"], "plate": false}""");
        var queued = new ToolLogic(cfg, () => Now).BuildProposeTheme(good, null, true);

        Assert.Equal("queued", (string?)queued["status"]);   // no widget answers here
        var request = JsonNode.Parse(File.ReadAllText(cfg.ThemeRequestPath!))!;
        Assert.Equal("Segoe UI", (string?)request["theme"]!["font"]);
        Assert.False((bool)request["theme"]!["desk"]!["plate"]!);
    }
```

Append to the class in `tests/Sanduhr.Tests/DeskDesignValidatorTests.cs`:

```csharp
    // ---- theme match: a design that follows the widget theme is judged in the theme's look ----

    private static JsonObject ObsidianTheme(bool? plate = null)
    {
        var t = ThemeDraft.FromDefinition("obsidian", ThemeCatalog.BuiltIns["obsidian"]).ToJson();
        if (plate is { } p) t["desk"] = new JsonObject { ["plate"] = p };
        return t;
    }

    private static JsonObject Follow(JsonObject theme) => new() { ["follows_theme"] = true, ["theme"] = theme };

    [Fact]
    public void A_followed_design_is_judged_by_the_themes_ink_not_the_desks_own()
    {
        var ctx = new DeskDesignContext(Wallpaper: Flat(0.05), Current: Current("""{"layout":{"clock":{"anchor":"bl"}},"ink":"dark","plate":false}"""));
        Assert.False(DeskDesign.Validate(Obj("""{"plate":false}"""), ctx).Ok);   // the Desk's own dark ink on a dark wallpaper
        Assert.True(DeskDesign.Validate(Follow(ObsidianTheme()), ctx).Ok);         // #e8e4dc reads at 8:1
    }

    [Fact]
    public void The_guard_turns_the_plate_on_before_a_followed_look_is_refused()
    {
        // At 0.3 #e8e4dc reads at 2.4:1 bare but 4.4:1 over the plate: the Desk turns the plate on, so the design is fine.
        var ctx = new DeskDesignContext(Wallpaper: Flat(0.3), Current: Current("""{"layout":{"clock":{"anchor":"bl"}}}"""));
        Assert.True(DeskDesign.Validate(Follow(ObsidianTheme(plate: false)), ctx).Ok);
    }

    [Fact]
    public void A_followed_look_that_cannot_read_even_on_its_plate_is_refused_naming_follows_theme()
    {
        var ctx = new DeskDesignContext(Wallpaper: Flat(0.9), Current: Current("""{"layout":{"clock":{"anchor":"bl"}},"ink":"dark"}"""));
        var v = DeskDesign.Validate(Follow(ObsidianTheme()), ctx);
        Assert.Equal("follows_theme", Fields(v));
        Assert.Contains("with the plate on", v.Reasons[0].Message);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.Mcp.Tests --filter "FullyQualifiedName~TrustBoundaryTests|FullyQualifiedName~ToolLogicDeskDesignTests|FullyQualifiedName~ToolLogicThemeTests"` and `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~DeskDesignValidatorTests`
Expected: `Source_link_list_is_exactly_the_allowlisted_slice` FAILS (`ThemeDeskLook.cs` missing from the link list); `Get_reports_the_theme_look…` FAILS (`theme_look` is null); the followed-design facts FAIL (judged by the Desk's own ink, or not judged). `A_theme_with_font_and_desk…` passes already (Task 1): it pins the server's half.

- [ ] **Step 3: Link it**

In `src/Sanduhr.Mcp/Sanduhr.Mcp.csproj`, replace

```xml
    <Compile Include="..\Sanduhr.Core\ThemeLint.cs" Link="Core\ThemeLint.cs" />
```

with

```xml
    <Compile Include="..\Sanduhr.Core\ThemeLint.cs" Link="Core\ThemeLint.cs" />
    <!-- Theme match (2026-10-25, decision zIsGHT5TiD5lbEwP4AiQ): one more file, pure (colors and arithmetic; no IO, no network, no
         credentials, no process). ThemeDeskLook turns a theme into the Desk's look, so get_desk's theme_look and the check of a design
         that follows the theme compute exactly what the widget draws. -->
    <Compile Include="..\Sanduhr.Core\ThemeDeskLook.cs" Link="Core\ThemeDeskLook.cs" />
```

- [ ] **Step 4: get_desk reports the look**

In `src/Sanduhr.Mcp/ToolLogic.Desk.cs`, replace

```csharp
        if (state!["writer_version"] is JsonValue wv && wv.TryGetValue<string>(out var writer)) answer["writer_version"] = writer;
```

with

```csharp
        if (state!["writer_version"] is JsonValue wv && wv.TryGetValue<string>(out var writer)) answer["writer_version"] = writer;
        // Theme match: the look the theme gives the Desk (what it draws while design.follows_theme is true), computed with the widget's code.
        if (state["design"]?["theme"] is JsonObject theme && ThemeLint.Lint(theme).Theme is { } def)
            answer["theme_look"] = ThemeDeskLook.From(def).ToJson();
```

- [ ] **Step 5: Followed designs are judged in the theme's look**

In `src/Sanduhr.Core/DeskDesignValidator.cs`, replace the whole `CheckContrast` method (from `private static void CheckContrast(DeskDesign proposal, DeskDesignContext ctx, List<DeskReason> reasons)` to its closing brace, just before `/// <summary>The wallpaper monitors a piece is judged against`) with:

```csharp
    private static void CheckContrast(DeskDesign proposal, DeskDesignContext ctx, List<DeskReason> reasons)
    {
        if (ctx.Wallpaper is not { Monitors.Count: > 0 } wall) return;
        var merged = Merge(ctx.Current, proposal);
        // Following the widget theme (theme match): the Desk draws the theme's look, whatever its own ink says.
        var look = merged.FollowsTheme == true && merged.Theme is { } followed && ThemeLint.Lint(followed).Theme is { } fdef
            ? ThemeDeskLook.From(fdef) : null;
        var choice = merged.Ink;
        if (look is null && (choice is null || choice.Kind == DeskInkKind.Auto)) return;

        bool inkChanged = look is null && proposal.Ink is not null;
        bool plateChanged = look is null && proposal.Plate is not null;
        bool followChanged = look is not null && proposal.FollowsTheme == true;
        bool themeChanged = proposal.Theme is not null && (look is not null || choice!.Kind == DeskInkKind.Theme);
        bool layoutChanged = proposal.Layout is not null;
        if (!(inkChanged || plateChanged || followChanged || themeChanged || layoutChanged)) return;

        ResolvedInk ink;
        bool plate;
        double[] stopLum;
        if (look is not null)
        {
            ink = DeskInk.Resolve(new DeskInkSettings(DeskInkMode.Custom, string.Join(",", look.Ink), true), null, null);
            plate = look.Plate;
            // The readability rule judges a followed ink by its first stop (ThemeDeskLook.ReadsOver), as the Desk does.
            stopLum = [InkHex.Luminance(look.Ink[0])];
        }
        else
        {
            string? themeText = merged.Theme?["text"] is JsonValue tv && tv.TryGetValue<string>(out var text) ? text : null;
            if (choice!.Kind == DeskInkKind.Theme && InkHex.Normalize(themeText) is null) return;
            var settings = new DeskInkSettings(
                choice.Kind switch
                {
                    DeskInkKind.Light => DeskInkMode.Light,
                    DeskInkKind.Dark => DeskInkMode.Dark,
                    DeskInkKind.Theme => DeskInkMode.Theme,
                    _ => DeskInkMode.Custom,
                },
                string.Join(",", choice.Colors), merged.Shadow ?? true);
            ink = DeskInk.Resolve(settings, themeText, null);
            plate = merged.Plate ?? false;
            stopLum = ink.Stops.Select(h => InkHex.Luminance(h)).ToArray();
        }
        // A followed look without its plate gets it where its ink would not read (the guard): judged again on the plate first.
        bool guard = look is not null && !plate;

        // A change of ink, plate, theme or following touches every placed piece; a layout change only the pieces it names.
        var named = proposal.Layout?.Select(p => p.Piece).ToHashSet(StringComparer.Ordinal);
        bool everything = inkChanged || plateChanged || followChanged || themeChanged;
        bool launcherLabelled = merged.Launcher?.Buttons.Any(b => b.Label is not null) == true;
        string from = inkChanged ? "ink" : plateChanged ? "plate" : followChanged ? "follows_theme" : themeChanged ? "theme" : "layout";

        int found = 0;
        foreach (var p in merged.Layout ?? [])
        {
            // The message has its own color; the glow is no text; the companion is the pack's art, not the ink.
            if (p.IsHidden || p.Piece is "message" or "glow" or "companion") continue;
            if (p.Piece == LauncherModel.Widget && !launcherLabelled) continue;   // icons do not take the ink
            if (!everything && named?.Contains(p.Piece) != true) continue;
            if (found >= MaxContrastReasons) break;

            var anchor = p.Anchor ?? DeskArrangement.DefaultAnchor(p.Piece);
            (double Worst, double Bg) Judge(bool onPlate)
            {
                double worst = double.MaxValue, worstBg = 0;
                foreach (var m in MonitorsOf(wall, p.Monitor))
                {
                    double under = m.Summary.At(anchor)?.Brightness ?? m.Summary.Brightness;
                    if (onPlate) under = UnderPlate(under, ink.DarkInk);
                    foreach (var l in stopLum)
                    {
                        double ratio = Ratio(l, under);
                        if (ratio < worst) { worst = ratio; worstBg = under; }
                    }
                }
                return (worst, worstBg);
            }
            bool plated = plate;
            var (worstRatio, worstBack) = Judge(plate);
            if (worstRatio < MinContrast && guard)
            {
                (worstRatio, worstBack) = Judge(true);
                plated = true;
            }
            if (worstRatio >= MinContrast) continue;

            string field = from == "layout" ? $"layout.{p.Piece}" : from;
            // Black beats white as the ground gets brighter than about 0.18 (where (L + .05)^2 = 1.05 * .05).
            string fix = worstBack > 0.179 ? "a darker ink" : "a lighter ink";
            reasons.Add(new DeskReason(field,
                $"the {p.Piece} piece at {DeskAnchors.Word(anchor)} ({DeskAnchors.Name(anchor).ToLowerInvariant()}): the ink would read at "
                + $"{worstRatio.ToString("0.0", CultureInfo.InvariantCulture)}:1 against the wallpaper there "
                + $"(brightness {worstBack.ToString("0.00", CultureInfo.InvariantCulture)}{(plated ? ", with the plate on" : "")}); "
                + $"the Desk needs {MinContrast.ToString("0", CultureInfo.InvariantCulture)}:1. Choose {fix}, {(plated ? "" : "turn the plate on, ")}or move the piece."));
            found++;
        }
    }
```

- [ ] **Step 6: The tool descriptions**

In `src/Sanduhr.Mcp/ToolCatalog.cs`, replace

```csharp
            "complementary hue works). The server lints first: status rejected with findings means " +
```

with

```csharp
            "complementary hue works). Optional: font (a font family the widget's text, and the Desk while it follows the " +
            "theme, is drawn in) and desk {ink, bars, message: 1 to 4 #rrggbb each; glow, plate_color, shadow: #rrggbb; " +
            "plate: true or false; font}: the Desk's look for this theme, every field optional with a default worked out from " +
            "the colors; bars also fills the widget's bars, with a yellow or red middle stop at the user's alert levels. " +
            "The server lints first: status rejected with findings means " +
```

replace

```csharp
                        ["description"] = "The palette JSON: name, the fourteen #rrggbb colors, optional dials.",
```

with

```csharp
                        ["description"] = "The palette JSON: name, the fourteen #rrggbb colors, optional dials, optional font and desk.",
```

replace

```csharp
                                ["description"] = "A widget palette as propose_theme takes it: name and the fourteen #rrggbb colors.",
                            },
                        },
                        ["additionalProperties"] = false,
```

with

```csharp
                                ["description"] = "A widget palette as propose_theme takes it: name and the fourteen #rrggbb colors, optional font and desk.",
                            },
                            ["follows_theme"] = new JsonObject
                            {
                                ["type"] = "boolean",
                                ["description"] = "True draws the Desk in the widget theme's own look (get_desk theme_look) instead of its font, ink and plate.",
                            },
                        },
                        ["additionalProperties"] = false,
```

replace

```csharp
        "instead of the place's, \"\" for none), font, ink, plate, shadow, launcher, theme), pieces and anchors (the allowed words), fonts (installed " +
```

with

```csharp
        "instead of the place's, \"\" for none), font, ink, plate, shadow, launcher, theme, follows_theme: true while the Desk draws the " +
        "theme's look instead of its own), theme_look (that look worked out from the theme: gradient, ink, bars, message, glow, plate, " +
        "plate_color, shadow, font, bar_track, pace_tick), pieces and anchors (the allowed words), fonts (installed " +
```

and

```csharp
        "palette: name and the fourteen #rrggbb colors, per docs/themes/AGENT_PROMPT.md, as propose_theme takes it). " +
```

with

```csharp
        "palette: name and the fourteen #rrggbb colors, per docs/themes/AGENT_PROMPT.md, as propose_theme takes it, with its optional " +
        "font and desk look); follows_theme (true draws the Desk in the theme's look; its ink is judged by its first stop, and the " +
        "Desk turns its plate on where that would not read). " +
```

- [ ] **Step 7: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.Mcp.Tests` and `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DeskDesignValidatorTests|FullyQualifiedName~DeskDesignTests"`
Expected: PASS, the whole server suite included (catalog and schema tests read the new text). `git diff -- windows-dotnet/src/Sanduhr.Mcp/Sanduhr.Mcp.csproj` shows the one `Compile Include` and its comment, nothing else.

- [ ] **Step 8: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Mcp/Sanduhr.Mcp.csproj windows-dotnet/tests/Sanduhr.Mcp.Tests/TrustBoundaryTests.cs windows-dotnet/src/Sanduhr.Mcp/ToolLogic.Desk.cs windows-dotnet/src/Sanduhr.Core/DeskDesignValidator.cs windows-dotnet/src/Sanduhr.Mcp/ToolCatalog.cs windows-dotnet/tests/Sanduhr.Mcp.Tests/ToolLogicDeskDesignTests.cs windows-dotnet/tests/Sanduhr.Mcp.Tests/ToolLogicThemeTests.cs windows-dotnet/tests/Sanduhr.Tests/DeskDesignValidatorTests.cs
git commit -m "feat(theme-match): the server links ThemeDeskLook: get_desk reports theme_look and a followed design is judged in the theme's look"
```

---

### Task 16: The Theme Studio makes a theme's font and Desk look

**Files:**
- Modify: `src/Sanduhr.App/ViewModels/ThemeStudioViewModel.cs` (ctor ~104-112; properties ~92-100; `LoadFrom` ~139-153; `Refresh` ~205-213; a font handler)
- Modify: `src/Sanduhr.App/ViewModels/ThemesViewModel.cs:81`
- Modify: `src/Sanduhr.App/Views/SettingsWindow.xaml` (the Studio, before its `Summary` line ~2504)
- Modify: `src/Sanduhr.Core/SettingsNames.cs` (the theme-match block)
- Test: `tests/Sanduhr.App.Tests/ThemeStudioDeskTests.cs` (create)

**Interfaces:**
- Consumes: `ThemeDraft.Font/.Desk` (Task 2), `ThemeDeskEditorViewModel` and `ThemeDeskEditor` (Task 14).
- Produces: `ThemeStudioViewModel(WidgetViewModel widget, Action<string> error, Action<string> info, Action saved, IReadOnlyList<string>? fontFamilies = null)`; `FontChoices`, `Font` (two-way; `SettingsNames.ThemeDefaultFont` is none), `DeskEditor`; SettingsNames `ThemeFontPicker`, `ThemeDefaultFont`, `ThemeStudioDeskNote`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Sanduhr.App.Tests/ThemeStudioDeskTests.cs`:

```csharp
using System.IO;
using Sanduhr.App.Theming;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Theme match in the Theme Studio: a Font picker and the Desk look section, saved into the theme and previewed live (the Desk
/// redraws on the preview's ThemeChanged while it follows).</summary>
public sealed class ThemeStudioDeskTests : AppWindowFixture
{
    [Fact]
    public void The_studio_saves_the_font_and_the_desk_look_into_the_theme() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var errors = new List<string>();
        var studio = new ThemeStudioViewModel(widget, errors.Add, _ => { }, () => { }, ["Consolas", "Segoe UI"]);
        studio.Activate();   // starts from the active theme, Obsidian
        Assert.Equal(SettingsNames.ThemeDefaultFont, studio.Font);
        Assert.Equal("#6c63ff, #e8e4dc", studio.DeskEditor.BarsDefault);

        studio.Font = "Consolas";
        studio.DeskEditor.Bars = "#6c63ff, #e8e4dc";
        studio.FileName = "studio-desk";
        studio.SaveCommand.Execute(null);

        Assert.Empty(errors);
        var saved = widget.GetThemeDefinition("studio-desk")!;
        Assert.Equal("Consolas", saved.Font);
        Assert.Equal(["#6c63ff", "#e8e4dc"], saved.Desk!.Bars);
    });

    [Fact]
    public void Starting_from_a_theme_with_a_desk_look_shows_it_and_the_rules_greyed() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var dusk = ThemeCatalog.BuiltIns["aurora"] with { Name = "Dusk", Font = "Consolas", Desk = new ThemeDesk { Ink = ["#ffffff"] } };
        Directory.CreateDirectory(widget.ThemesDir);
        File.WriteAllText(Path.Combine(widget.ThemesDir, "dusk.json"), ThemeDraft.FromDefinition("dusk", dusk).ToJsonText());
        widget.ReloadUserThemes();
        var studio = new ThemeStudioViewModel(widget, _ => { }, _ => { }, () => { }, ["Consolas"]);

        studio.SelectedStart = widget.Themes.First(t => t.Key == "dusk");

        Assert.Equal("Consolas", studio.Font);
        Assert.Equal("#ffffff", studio.DeskEditor.Ink);
        Assert.Equal("", studio.DeskEditor.Bars);
        Assert.Equal("#38bdf8, #e2e8f0", studio.DeskEditor.BarsDefault);
    });

    [Fact]
    public void A_desk_edit_previews_through_ThemeChanged() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        ThemeDefinition? seen = null;
        widget.ThemeChanged += p => seen = p.Definition;
        var studio = new ThemeStudioViewModel(widget, _ => { }, _ => { }, () => { }, []);
        studio.Activate();

        studio.DeskEditor.Ink = "#ffffff";

        Assert.Equal(["#ffffff"], seen!.Desk!.Ink);
        Assert.Equal("obsidian", widget.ActiveThemeKey);   // a preview, nothing saved
        studio.Deactivate();
    });
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~ThemeStudioDeskTests`
Expected: build FAILS with `'ThemeStudioViewModel' does not contain a constructor that takes 5 arguments`.

- [ ] **Step 3: The names**

In `src/Sanduhr.Core/SettingsNames.cs`, after `public const string ThemeColorProblem = "Use one hex color, like #6c63ff.";` add:

```csharp
    /// <summary>The Theme Studio's font picker (theme match) and its first choice, no font (Segoe UI).</summary>
    public const string ThemeFontPicker = "Font";
    public const string ThemeDefaultFont = "Segoe UI (the default)";
    public const string ThemeStudioDeskNote =
        "The Desk shows these while it follows the widget theme (Settings, Desk Look, Match the widget theme).";
```

- [ ] **Step 4: The Studio**

In `src/Sanduhr.App/ViewModels/ThemeStudioViewModel.cs`, after `[ObservableProperty] private bool _isPreviewing;` add:

```csharp
    /// <summary>The theme's font (theme match): the widget's text, and the Desk's while it follows.</summary>
    [ObservableProperty] private string _font = SettingsNames.ThemeDefaultFont;

    /// <summary>The Font picker's choices: no font (Segoe UI), then this PC's families.</summary>
    public IReadOnlyList<string> FontChoices { get; }

    /// <summary>The theme's Desk look (theme match); each committed row updates the draft and the preview.</summary>
    public ThemeDeskEditorViewModel DeskEditor { get; }
```

Replace

```csharp
    public ThemeStudioViewModel(WidgetViewModel widget, Action<string> error, Action<string> info, Action saved)
    {
        _widget = widget;
        _error = error;
        _info = info;
        _saved = saved;
```

with

```csharp
    /// <param name="fontFamilies">This PC's families for the Font picker and the Desk font row; null offers none beyond the default.</param>
    public ThemeStudioViewModel(WidgetViewModel widget, Action<string> error, Action<string> info, Action saved,
        IReadOnlyList<string>? fontFamilies = null)
    {
        _widget = widget;
        _error = error;
        _info = info;
        _saved = saved;
        FontChoices = [SettingsNames.ThemeDefaultFont, .. fontFamilies ?? []];
        DeskEditor = new ThemeDeskEditorViewModel(fontFamilies ?? []) { Committed = OnDeskEdited };
```

In `LoadFrom`, replace

```csharp
        BorderAlpha = _draft.BorderAlpha;
        FileName = SuggestedFileName(key);
```

with

```csharp
        BorderAlpha = _draft.BorderAlpha;
        Font = _draft.Font ?? SettingsNames.ThemeDefaultFont;
        DeskEditor.Load(def);
        FileName = SuggestedFileName(key);
```

After `OnBorderAlphaChanged` add:

```csharp

    partial void OnFontChanged(string value)
    {
        if (_loading) return;
        _draft.Font = value == SettingsNames.ThemeDefaultFont || value.Length == 0 ? null : value;
        Refresh(preview: true);
    }

    private void OnDeskEdited(ThemeDesk desk)
    {
        if (_loading) return;
        _draft.Desk = desk.IsEmpty ? null : desk;
        Refresh(preview: true);
    }
```

In `Refresh`, replace

```csharp
        if (_draft.PreviewDefinition() is { } def)
        {
            _widget.PreviewTheme(def);
            IsPreviewing = true;
        }
```

with

```csharp
        if (_draft.PreviewDefinition() is { } def)
        {
            // The greyed Desk defaults follow the colors being edited.
            DeskEditor.ShowRules(def);
            _widget.PreviewTheme(def);   // ThemeChanged: a Desk that follows the theme redraws from the draft too
            IsPreviewing = true;
        }
```

In `src/Sanduhr.App/ViewModels/ThemesViewModel.cs`, replace

```csharp
        Studio = new ThemeStudioViewModel(widget, Error, Info, RefreshList);
```

with

```csharp
        Studio = new ThemeStudioViewModel(widget, Error, Info, RefreshList,
            Sanduhr.App.Desk.DeskFonts.ForThisPc(new Sanduhr.Core.Paths()).PickerFamilies());
```

In `src/Sanduhr.App/Views/SettingsWindow.xaml`, in the Studio, replace

```xml
                                    <TextBlock FontSize="10.5" Margin="0,6,0,0" Text="{Binding Summary}"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
```

with

```xml
                                    <!-- Theme match: the theme's font (the widget's text; the Desk's while it follows) and its Desk look. -->
                                    <Grid Margin="0,8,0,0">
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="92" />
                                            <ColumnDefinition Width="230" />
                                        </Grid.ColumnDefinitions>
                                        <TextBlock Text="{x:Static core:SettingsNames.ThemeFontPicker}" FontSize="11" VerticalAlignment="Center"
                                                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                        <ComboBox Grid.Column="1" Style="{StaticResource ThemedComboBox}"
                                                  ItemsSource="{Binding FontChoices}" SelectedItem="{Binding Font, Mode=TwoWay}"
                                                  AutomationProperties.Name="{x:Static core:SettingsNames.ThemeFontPicker}" />
                                    </Grid>
                                    <views:ThemeDeskEditor Margin="0,10,0,0" DataContext="{Binding DeskEditor}" />
                                    <TextBlock FontSize="10.5" TextWrapping="Wrap" Margin="0,4,0,0"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                               Text="{x:Static core:SettingsNames.ThemeStudioDeskNote}" />
                                    <TextBlock FontSize="10.5" Margin="0,6,0,0" Text="{Binding Summary}"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
```

- [ ] **Step 5: Run them to see them pass**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~ThemeStudioDeskTests|FullyQualifiedName~PackThemeWiringTests|FullyQualifiedName~ThemeGalleryAutomationTests|FullyQualifiedName~SettingsWindowNamesTests"` and `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests"`
Expected: PASS ("Settings, Desk Look, Match the widget theme" passes `PathProblems`).

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/ThemeStudioViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/ThemesViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.App.Tests/ThemeStudioDeskTests.cs
git commit -m "feat(theme-match): the Theme Studio picks a theme's font and makes its Desk look, previewed live"
```

---

### Task 17: End to end: every built-in matched in the harness, warn and urgent, dark and light; the whole suite; the owner's checks

**Files:**
- Create: `tools/Sanduhr.RenderHarness/ThemeMatchScenario.cs`
- Modify: `tools/Sanduhr.RenderHarness/Program.cs` (the header ~23; `known` ~109; the dispatch ~372-373)
- Modify: `docs/decisions/_pending-dashboard-log.md` only if the dashboard MCP is not connected (Step 6)

**Interfaces:**
- Consumes: everything above (`DeskLookResolver`, `ThemeDeskLook.Guarded`, `TierCardViewModel`, `ThemePalette`).
- Produces: the `theme-match` harness scenario: `theme-match-<key>-<warn|urgent>-<dark|light>.png` (24) and `theme-match-widget-<key>-<ramp|bars>.png` (12), with one log line per theme (its look) and per render (plate on or off, the guard).

- [ ] **Step 1: The scenario**

Create `tools/Sanduhr.RenderHarness/ThemeMatchScenario.cs`:

```csharp
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Desk;
using Sanduhr.App.Theming;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

// The theme match scenario: every built-in theme's matched Desk (the clock, the Claude line, the limits as bars, telemetry as rings and a
// message) at the warn level (session 85%) and the urgent level (week 97%), over a dark and a light wallpaper, with the readability guard
// as the Desk applies it; and each theme's widget cards at 41, 85 and 97%, as it ships (the ramp) and with desk.bars set to its gradient.
// Sample data only: no Desk windows, no network, no real usage.
public static partial class Program
{
    private static async Task RenderThemeMatch(WidgetViewModel vm, Assembly asm)
    {
        var now = new DateTimeOffset(2026, 10, 25, 21, 9, 0, TimeSpan.Zero);
        var screen = new DeskScreen("DISPLAY1", new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1032), 1.0, true,
            TaskbarClearance.Insets(new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1032), [], 1.0));
        var fonts = new DeskFonts();
        var telemetry = new TelemetrySource();
        telemetry.Preview(new TelemetrySample(23, 61, 48, now));
        DeskUsage Usage(double session, double week) => new(
            [new DeskTier(TierModel.FiveHour, session, Iso(now + TimeSpan.FromHours(2.5))), new DeskTier(TierModel.SevenDay, week, Iso(now + TimeSpan.FromDays(3)))],
            now, false, false, null);
        var levels = new[] { ("warn", Usage(85, 63)), ("urgent", Usage(41, 97)) };
        // The backgrounds stand in for wallpapers; the luminance is what the Desk's sample would read behind each stack.
        var walls = new[] { ("dark", Color.FromRgb(0x10, 0x14, 0x1c), 0.006), ("light", Color.FromRgb(0xe9, 0xe6, 0xdf), 0.79) };
        const string layout = "clock:bl claude:bl meters:bl telemetry:tr:1:0:ring message:tl";
        var message = new DeskMessageFrame(new MessageToday(usual: "keep building."), DeskMessageSettings.Default);

        foreach (var key in ThemeCatalog.BuiltInOrder)
        {
            var theme = ThemeCatalog.BuiltIns[key];
            var draw = DeskLookResolver.For(true, theme)!;
            Log.Add($"theme-match {key}: {draw.Look.ToJson().ToJsonString()}");
            foreach (var (wallName, wallColor, wallLum) in walls)
            {
                // The guard as the controller applies it: the plate goes on where the ink's first stop would not read.
                var guarded = draw.Look.Guarded(wallLum);
                var settings = (DeskLookResolver.Settings(DeskSettings.Default with { Enabled = true, Layout = layout }, draw) with { Plate = guarded.Plate }).Normalized();
                foreach (var (levelName, usage) in levels)
                {
                    var view = new DeskView(screen, settings, fonts);
                    var frame = DeskLookResolver.Frame(new DeskFrame { Now = now, Usage = usage, Telemetry = telemetry, Message = message }, draw);
                    view.Update(frame, new DeskInkMap(draw.Ink));
                    var islands = view.TakeIslands();
                    Log.Add($"theme-match {key}-{levelName}-{wallName}: plate {(guarded.Plate ? "on" : "off")}"
                        + $"{(guarded.Plate && !draw.Look.Plate ? " (the guard)" : "")}, reads {draw.Look.ReadsOver(wallLum)}, islands {islands.Count}");
                    SaveScaled(Composite(islands, wallColor), $"theme-match-{key}-{levelName}-{wallName}", 0.5);
                    await Settle();
                    view.Dispose();
                }
            }

            // The widget: the ramp as the theme ships, and desk.bars set to its gradient (the gradient and the signal).
            var withBars = theme with { Desk = new ThemeDesk { Bars = [.. draw.Look.Gradient.Select(s => "#" + s)] } };
            foreach (var (variant, def) in new[] { ("ramp", theme), ("bars", withBars) })
            {
                var palette = new ThemePalette(key, def);
                var stack = new StackPanel { Width = 396, Background = new SolidColorBrush(palette.Bg) };
                palette.Apply(stack.Resources);
                foreach (var util in new[] { 41, 85, 97 })
                {
                    var card = new TierCardViewModel(TierModel.FiveHour, palette);
                    card.Update(util, Iso(now + TimeSpan.FromHours(2.5)), null, null, [], palette, SparklineStyle.Classic, now);
                    stack.Children.Add(new TierCard { DataContext = card, Margin = new Thickness(8) });
                }
                stack.Measure(new Size(396, double.PositiveInfinity));
                stack.Arrange(new Rect(stack.DesiredSize));
                stack.UpdateLayout();
                Save(stack, $"theme-match-widget-{key}-{variant}");
                await Settle();
            }
        }
    }
}
```

In `tools/Sanduhr.RenderHarness/Program.cs`, replace

```csharp
//   --scenario <name>    widget | whatsnew | tour | settings | glow | reset | desk | arrange | launcher | review | companion | ridealongs | propose-widget | meterstyles | all; repeatable (default all)
```

with

```csharp
//   --scenario <name>    widget | whatsnew | tour | settings | glow | reset | desk | arrange | launcher | review | companion | ridealongs | propose-widget | meterstyles | theme-match | all; repeatable (default all)
```

replace

```csharp
        string[] known = { "widget", "whatsnew", "tour", "settings", "glow", "reset", "desk", "arrange", "launcher", "review", "companion", "ridealongs", "propose-widget", "meterstyles" };
```

with

```csharp
        string[] known = { "widget", "whatsnew", "tour", "settings", "glow", "reset", "desk", "arrange", "launcher", "review", "companion", "ridealongs", "propose-widget", "meterstyles", "theme-match" };
```

and

```csharp
            if (scenarios.Contains("meterstyles"))
                await RenderMeterStyles(vm, asm);
```

with

```csharp
            if (scenarios.Contains("meterstyles"))
                await RenderMeterStyles(vm, asm);
            if (scenarios.Contains("theme-match"))
                await RenderThemeMatch(vm, asm);
```

- [ ] **Step 2: Build the harness**

Run: `dotnet build tools/Sanduhr.RenderHarness`
Expected: build succeeds (it exercises `DeskLookResolver`, `TierCard` and the frame fields from outside the App: the `InternalsVisibleTo("Sanduhr.RenderHarness")` the App already grants).

- [ ] **Step 3: Render**

Run (PowerShell, from `windows-dotnet/`):

```powershell
$env:SANDUHR_DEV_PROFILE = "$env:TEMP\sanduhr-harness-theme-match"
dotnet run --project tools/Sanduhr.RenderHarness -- --scenario theme-match --out "$env:TEMP\sanduhr-theme-match"
```

Expected: 36 PNGs and a `log.txt` with six look lines (each matching Task 3's pinned values) and 24 render lines. Open the PNGs with the Read tool and check, for each built-in:

- The clock, date and Claude line wear text to accent (Matrix one green); the message wears accent to text with an accent glow; the meters wear the gradient on a `bar_bg` track with the `pace_marker` tick; telemetry's rings take the same colors and no signal.
- **Warn** renders: the session meter (85%) has a yellow middle stop and a yellow "85%"; the week (63%) is calm. **Urgent** renders: the week (97%) has a red middle stop and a red "97"/"97%"; the session (41%) is calm.
- **Light** renders: every stack sits on its plate, tinted from the theme's bg; the log says `plate on`, and `reads False` (pale ink on a bright wallpaper; the plate is the only help, as the spec's guard intends). **Dark** renders: `reads True`.
- `theme-match-widget-<key>-ramp`: the three cards are green-ish, orange, red, exactly as before this feature. `-bars`: the theme's gradient, with a yellow stop at 85 and a red one at 97, percent chips calm in the theme's text, then yellow, then red.

A render that does not match is a bug to fix in its task, then this step runs once more. The PNGs under `%TEMP%` are evidence, never committed.

- [ ] **Step 4: The whole suite**

Run: `dotnet test Sanduhr.slnx` (from `windows-dotnet/`)
Expected: PASS across `Sanduhr.Tests`, `Sanduhr.App.Tests` and `Sanduhr.Mcp.Tests`, every `[DesktopFact]` included (run once).

- [ ] **Step 5: The contracts changed only as admitted**

Run: `git diff release/4.0 -- windows-dotnet/src/Sanduhr.Mcp/Sanduhr.Mcp.csproj windows-dotnet/tests/Sanduhr.Mcp.Tests/TrustBoundaryTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsKeysContractTests.cs`
Expected: the csproj gains one `Compile Include` (ThemeDeskLook.cs) and its comment; `TrustBoundaryTests` gains `"ThemeDeskLook.cs"` with its comment and one `[InlineData]`; `SettingsKeysContractTests` gains `"desk_follows_theme"`. Nothing else. `grep -rn "desk_follows_theme" windows-dotnet/src --include=*.cs` names only `SettingsStore.cs`.

- [ ] **Step 6: Log the decisions**

Log to the 626 Labs dashboard (project **Sanduhr für Claude (Windows)**, `manage_decisions` `log`) the plan's decisions 1, 3, 4, 7, 8 and 13 (the shadow and plate formulas; the guard judges the first stop; where the signal stop goes; what Match now cannot copy; why Follow never writes `bars`; Put my Desk back after "Add the Desk look"), each a sentence of what and a sentence of why. When the dashboard MCP is not connected, append them to `docs/decisions/_pending-dashboard-log.md` and commit that file alone:

```bash
git add docs/decisions/_pending-dashboard-log.md
git commit -m "docs(theme-match): decisions pending the dashboard log"
```

- [ ] **Step 7: Hand the owner's checks over**

Not code and not for the agent (they need the real app on real screens, which this plan never launches). Post them to the owner as the feature's open checks:

- **Follow on, on a built-in:** a new "Obsidian + Desk" appears in the strip and is active; the Desk restyles at once; Put my Desk back returns both.
- **Follow on, on a theme of yours:** the three-button question (Add the Desk look to <theme>, Make a new theme, Cancel) reads well, light and dark; Escape cancels.
- **A hand edit while following:** a text row commits on leaving it, not per keystroke; Update / Save as a new theme / Cancel; Cancel puts the row back; the new theme's name prompt.
- **The readability guard:** a theme with `desk.plate: false` over a bright wallpaper shows the plate and the note "The plate is on so the clock reads over this wallpaper."
- **The widget's font:** a theme with `font` set to a font pack's family draws the widget in it; uninstalling the pack falls back to Segoe UI.
- **The usage signal** on real usage crossing Warn at and Urgent at, widget (a theme with `desk.bars`) and Desk; and with Alerts' thresholds moved.
- Narrator: the section's heading, the switch, Match now, the status line and the editor rows read by name.

---

## Carried, not in this plan

- **The catalog's own validator.** `sanduhr-widgets/tools/validate.py` (the catalog CI) must learn `font` and `desk` as `PackValidator` now does, or a catalog theme pack using them fails CI there. A hand-off to that repo.
- **`docs/themes/AGENT_PROMPT.md`** documents the fourteen colors for agents; it gains `font` and `desk` (the tool descriptions already say them) in the docs pass.
- **The Mac.** Its themes have no `desk` block; parity is the Mac agent's call, handed over with this plan's decisions.
- **What's New and the Store listing copy** name the feature in the 4.0 release work (slice 7 and the Store Listing Console), not here.
- **A separate Now playing bars color.** While following, Now playing's bars and position bar wear the theme gradient (Decision 11); a `desk` field of their own is a later spec's.

## Spec coverage

| Spec section or requirement | Task |
| --- | --- |
| The goal: one theme styles the widget and the Desk; a matched look kept in a theme travels | 3, 8, 11, 17 |
| A theme may carry `font`; built-ins set none; Matrix and Blueprint keep `monospace_font` for the values | 1, 6 |
| `desk` object: `ink`, `bars`, `message`, `glow`, `plate`, `plate_color`, `shadow`, `font`, all optional; existing files stay valid | 1, 2 |
| The rules (`ThemeDeskLook.From`): gradient accent to sparkline (else accent to text), ink text to accent, bars and message the gradient, glow accent, plate on tinted from bg at 0.30, shadow a deep shade of bg, Desk font fallback chain; a `desk` field wins | 3 |
| Now playing's bars and position bar take the gradient unless a song's look shows | 7, 8 |
| Readability guard: judged against the wallpaper sample; the plate turns on; one line says so | 3, 8, 13, 15 |
| Meters: the widget keeps its ramp unless the theme sets `desk.bars` | 5 |
| Any gradient meter carries the signal: middle stop yellow at Warn at, red at Urgent at, alert thresholds, live, gone below Warn at; a solid meter likewise | 4, 5 |
| Replaces `DeskMeters.WarningAt`/`WarningRed` for meters; the percent text takes the signal | 4, 5 |
| Only meters carry the signal | 4 (clock, message and Now playing untouched; Decision 6) |
| Follow and Match now at the top of Desk Look | 13 (Decision 2) |
| Follow on with a built-in or pack theme: a new "<Theme> + Desk" user theme, unique, made active; built-ins and packs never edited | 9, 11 |
| Follow on with a user theme: "Add the Desk look to <theme>" or "Make a new theme" | 11 |
| Switching the widget theme while following restyles the Desk | 8 |
| Match now: a one-time copy into the Desk's own settings; Follow stays off; no theme made | 9, 11, 13 (Decision 7) |
| A hand edit while following asks once per edit: Update <theme> (not for a built-in or pack), Save as a new theme (named), Cancel | 12, 14 |
| Follow off: the Desk's own look, edits save silently; turning it off keeps the current look | 9, 11, 13 |
| Undo: Follow on and Match now save the Desk first; Put my Desk back returns the Desk and the theme together | 10, 11 |
| One new key `desk_follows_theme` in `SettingsKeysContractTests`, `DeskState` (get_desk), the design format (`follows_theme`) and `DeskDesignApply` | 8, 10, 15 |
| `desk_ink.mode: theme` unchanged; nothing changes look at upgrade | 5, 8 (Global Constraints) |
| Theme files: read by `BuildDefinition`, written by `ThemeDraft`, linted (hex stops, contrast, known font, unknown `desk` fields); packs and pasted JSON accept them; `propose_theme` accepts them | 1, 2, 6, 15 (Decisions 9, 10) |
| Theme Studio: a Font picker; a Desk section with the three gradients, glow, plate and tint, shadow, each showing the rule's default greyed; live preview of the widget and the Desk | 14, 16 (Decision 14) |
| Trust boundary: `ThemeModel.cs`/`ThemeLint.cs` gain the fields and checks; `ThemeDeskLook.cs` joins the link list, pure; `TrustBoundaryTests` changes only to admit it | 1, 3, 15, 17 Step 5 |
| What does not change: sizes, margins, OLED care, layout; the Claude Code glow; the widget's ramp without `desk.bars`; every Desk setting's meaning | 5, 8 (the resolver touches no size, margin or layout), Global Constraints |
| Testing, Core: `ThemeDeskLook.From` per built-in, overrides, the fallback; signal stops for gradient and solid, custom thresholds; catalog and draft round trips; lint findings | 1, 2, 3, 4, 9 |
| Testing, App: Follow on a built-in makes "<Theme> + Desk"; a user theme asks; hand edits ask and a built-in never offers Update; Follow off silent and keeps the look; Put back restores both; the widget's bars unchanged without `desk.bars` (render compare) | 5, 11, 12, 13, 14 |
| Testing, MCP: `propose_theme` accepts and lints `font` and `desk`; `get_desk` reports `follows_theme`; `TrustBoundaryTests` with the widened list | 15 |
| Testing, Harness: each built-in matched, dark and light wallpapers, warn and urgent | 17 |
| Open for the plan: the shadow shade and plate tint; where Follow and Match now sit | Decisions 1, 2; Tasks 3, 13 |
