# Ready-made Desk Looks and the Neon Glow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sanduhr Windows ships six complete Desk looks, applied in one click from Settings and from setup and undone by Put my Desk back, plus a neon glow on the Desk's ink that a look or the user can turn on.

**Architecture:** The glow is two values inside the existing `desk_ink` setting and one new design field, `glow`; the Desk draws it as a blurred copy of each piece's ink. A look is an embedded JSON file wrapping a design in the format `propose_desk` already takes. Pure Core code parses the files (`DeskLook`, `DeskLookCatalog`) and fits a look to this PC's fonts and wallpaper (`DeskLookFit`), reusing the validator's own contrast judgement. `DeskDesignService.ApplyLook` writes a fitted look through the existing `WriteDesign` under two rules that keep browsing from filling the saved designs. The gallery is a new section on the Desk page and a row in setup.

**Tech Stack:** .NET 10, C#, WPF, xunit. No new package.

**Spec:** `docs/superpowers/specs/2026-11-20-desk-looks-design.md` (umbrella: `2026-11-17-windows-5-0-umbrella.md`; builds on `2026-10-21-desk-design-design.md`).

## Global Constraints

- Work on branch `feat/windows-5-0-desk-looks` (it holds the spec at `71c4bdfa`). Never commit to `main`. Push and merge are the owner's call.
- Windows code only: `windows-dotnet/`. Never edit `mac/` or `vscode/`.
- CRLF working tree, LF index. Never `sed -i` a tracked file; use the Edit tool.
- No new package, no network call, no new capability, no new file on disk, no new settings key. `Sanduhr.Mcp.csproj` and `TrustBoundaryTests` do not change. The MCP server source-links `DeskInk.cs`, `DeskDesign.cs` and `DeskDesignValidator.cs`, so nothing those three files use may live in a new Core file: the glow's helpers go inside `DeskInk.cs` and `DeskDesign.cs`. `DeskLook.cs` and `DeskLookFit.cs` are new files the server does not link, and none of the three linked files may reference them.
- `Updates/UpdateChecker.cs`, the Velopack pack commands and the packId do not change.
- **Never launch a locally built Sanduhr on this PC.** A dev build shares its data folders with the installed app. Everything here is proven by `dotnet test`; the hand checks in Task 13 run on an installed test build, and the owner starts them.
- Never kill the owner's running Sanduhr. If a build hits a locked file, stop and ask them to quit it.
- Fonts: a look names only a font that ships with every supported Windows (build 17763 and later) or one of the two bundled families (`EsteFont Pro`, `EsteFont 26`). No font is added to the package; `ThirdPartyNoticesTests` does not change.
- Constants, verbatim from the spec: glow strength 0.2 to 1.0, default 0.6; blur radius 6 to 22 DIPs; the copy's opacity 0.45 to 0.9; island padding the larger of 16 DIPs and the blur radius plus 4; under-icons budget 8 ms for one capture of the clock's island at strength 1.0; contrast 3:1 (`DeskDesign.MinContrast`); six looks with ids `quiet-corner`, `ice`, `marquee`, `terminal`, `editorial`, `dashboard`.
- Copy, verbatim from the spec: "<name> is on your Desk.", "The Desk was switched on.", "<name> is your Desk already.", "The Desk could not be saved first, so nothing was changed.", "Your Desk is already as it was.", "Uses <font> on this PC.", "Uses its plate on your wallpaper.", "Uses automatic ink on your wallpaper.", "Weather needs a place.", "The Launcher needs buttons.", "Watchers needs a watcher.", "On your Desk", "Ready-made looks", "Start with a look", "Neon glow", "Glow strength", "Covered by Neon glow.", "Under the desktop icons the glow is one color."
- Every string the App draws that could hold a file path or a name follows the streamer convention already in the code: a line that only handles a path for IO carries `// streamer: notname - …`; a line that draws a name masks it through `StreamerMode.Mask` and carries `// streamer: masked - …`. The App test suite lints this. A look's name and blurb are fixed copy.
- Run every suite before each commit: `cd windows-dotnet && dotnet test` (three suites: `Sanduhr.Tests`, `Sanduhr.Mcp.Tests`, `Sanduhr.App.Tests`). Record the three passing counts once, before Task 1, as the baseline; no later commit may lower one. One known flaky test may fail alone and pass on a rerun: `CodexUsageServiceTests.After_reset_a_callback_does_no_io` (dashboard task `task-1795056433164-bgle06qsf`).
- Commit messages end with `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`.

## Rulings the code map forced (the spec is amended to match in Task 13)

1. **A look's layout is written in the file's own order, not through `DeskDesign.Merge`.** `Merge` places a piece into a stack by Settings order and leaves a piece already at its anchor where the user last moved it, so the same look would stack differently on two Desks. `ApplyLook` writes the look's pieces in file order, each keeping the monitor the user gave it (`DeskLookFit.LayoutOver`). `propose_desk` still merges, so an audition of a look whose stack is not in Settings order shows that stack reordered; the gallery is the truth.
2. **Fitting has a third font step.** The spec says font, then `font_fallback`. When this PC has neither, the look takes the system font (`""`), so a fitted look always validates as `ok` and never as `needs_font`. Note: "Uses the system font on this PC."
3. **"<name> is your Desk already." needs the Desk on.** When the Desk already is the fitted look but is switched off, the click switches it on and answers "<name> is on your Desk. The Desk was switched on." One click always produces a Desk.
4. **"Off" means two switches.** The Desk is off when `desk_enabled` is false or when setup's "A nicer desktop" is unticked (`SetupSettings.Desktop`, which leaves only drawers drawing). `ApplyLook` turns both on.
5. **A drawers review blocks a look**, as it blocks a pasted design: "Finish the drawers review on your Desk first." A design under review is dismissed, as `SwitchTo` does.
6. **The glow switch covers a followed widget theme too.** While the Desk follows the widget theme its ink comes from the theme, not from `desk_ink`. The glow is one switch for the whole Desk, so the controller applies it to the theme's ink as well (`DeskInk.Glowing`), and the theme's shadow shade is not drawn while it is on.
7. **Drawer labels lose their shadow while the glow is on and gain no glow.** Drawers do not glow (spec section 7), and the shadow is off Desk-wide while the glow is on.
8. **`desk_ink` is written as before while the glow is off.** `glow` is written only when true and `glow_strength` only when it is not 0.6, so an install that never touches the glow keeps a byte-identical setting.
9. **An agent's design applied over an untouched look still saves "Before <name>".** Rule A covers looks and `SwitchTo`, as the spec says; `ApplyDesignCore` is unchanged.
10. **The every-look-file test lives in the App test project.** The files are embedded in the App, so the test that loads all six runs there (`DeskLookFilesTests`); Core's tests cover the parser and the fitter with inline looks.
11. **The padding test.** Spec section 10 says "radius plus 4 at each end of the slider"; section 7's rule governs: the larger of 16 and the radius plus 4. That is 16 at strength 0.2 (radius 6) and 26 at strength 1.0 (radius 22).
12. **Two fonts changed.** WPF lists "Segoe UI" as one family and no "Segoe UI Light" or "Segoe UI Semibold" (checked on the owner's PC, 2026-11-20), and the spec says a name that list does not hold is replaced. Quiet corner takes Corbel, Dashboard takes Segoe UI. A look's font and fallback must be in `DeskLook.WindowsFonts` or bundled, so "nothing that exists only on Windows 11" is a test.
13. **Launcher labels glow in one color.** The buttons' geometry is fixed and a wrapper would move them, so a label takes the glow through the effect slot it already has, in the ink's middle color. Icons do not glow, as the spec says.
14. **On a card, the look's name stands where the look puts its clock.** The spec asks for the name and the piece map over the wallpaper; drawn separately they would collide. Every other shown piece is a short bar.
15. **The Looks section sits under the Desk switch**, above Arrange, the map and Pieces. The switch stays the page's first control; the jump link for Looks is first, as the spec says.
16. **What a shown piece needs is a link under the card, outside the button.** A button inside a button takes no keyboard.
17. **The `glow` piece and the companion are hidden in all six looks.** A look decides which pieces show; these two are the owner's to bring back at the audition.
18. **Copy this plan adds, for the owner's eye:** the Neon glow caption (`SettingsNames.NeonGlowCaption`), the Looks caption (`SettingsNames.LooksCaption`), setup's row caption (`SetupCopy.StartWithALookCaption`), the five blurbs besides Marquee's, the What's New card ("Six looks for your Desk"), "That look is gone.", "The look could not be written (…)." and "Uses the system font on this PC."

## Review Focus

1. **This PC has neither the look's font nor its fallback** (a stripped Windows, or the font list could not be read). The look applies in the system font and says so; it never waits on a font. (Task 8.)
2. **The wallpaper changes after a look is applied** (a slideshow, a new picture). The Desk still counts as that look, the next look saves nothing new, and clicking the same look again re-fits it instead of answering "already". (Tasks 8 and 10.)
3. **The Desk is off, or "A nicer desktop" is unticked, when a card is clicked**, including when the Desk already is that look. It comes on drawing the look. (Task 10.)
4. **A piece sits on a monitor that is unplugged right now.** The look keeps the piece's monitor and applies; the validator's connected-monitor rule never refuses a built-in look. (Tasks 8 and 10.)
5. **A drawers review or a design review is open when a card is clicked.** A drawers review refuses the click with its line and changes nothing; a design review is dismissed and its caller is told. (Task 10.)

## File Structure

| File | Responsibility |
|---|---|
| `src/Sanduhr.Core/DeskInk.cs` | `DeskGlow` (strength to radius, opacity and padding), `glow` and `glow_strength` in `DeskInkSettings`, `ResolvedInk.Glow`, `DeskInk.Glowing` |
| `src/Sanduhr.Core/DeskDesign.cs` | `DeskGlowChoice`, the `glow` field: parse, write, merge, diff |
| `src/Sanduhr.Core/DeskDesignValidator.cs` | `ContrastReasons`, the contrast judgement made callable |
| `src/Sanduhr.Core/DeskDesignApply.cs`, `DeskDesignLibrary.cs`, `DeskState.cs`, `ThemeMatch.cs` | The glow in the settings a design writes, in the canonical form, in `get_desk`, and Match now |
| `src/Sanduhr.Core/DeskLook.cs` (new) | `DeskLook`, `DeskLookCatalog`: a look file, its rules, the list |
| `src/Sanduhr.Core/DeskLookFit.cs` (new) | `DeskLookFit`: fit to fonts and wallpaper, "is the Desk this look", the layout to write, what a shown piece needs |
| `src/Sanduhr.Core/SetupPrompt.cs`, `SetupSettings.cs`, `SettingsNames.cs`, `SettingsIndex.cs`, `WhatsNewCards.cs` | The prompt's `glow`, setup's picked look, the names, the Looks section's index entry and jump link, the What's New card |
| `src/Sanduhr.Mcp/ToolCatalog.cs` | `glow` in the `propose_desk` schema and both guides |
| `src/Sanduhr.App/Desk/Looks/*.json` (new, six) | The looks, embedded |
| `src/Sanduhr.App/Desk/DeskLookFiles.cs` (new) | Reads the embedded files into a `DeskLookCatalog` |
| `src/Sanduhr.App/Desk/DeskGlowLayer.cs` (new) | The blurred copy behind a piece, and the one-color stand-in |
| `src/Sanduhr.App/Desk/DeskView.cs`, `DeskMessagePiece.cs`, `DeskLauncherPiece.cs` | Where the glow is hung |
| `src/Sanduhr.App/Desk/DesktopLayerHost.cs`, `IDeskHost.cs`, `DeskController.cs`, `UnderIcons/UnderIconsHost.cs` | The island padding, the followed theme's glow, the under-icons path |
| `src/Sanduhr.App/Desk/DeskDesignService.cs` | `ApplyLook`, the gallery's snapshot, rule A in `SwitchTo`, the second press of Put my Desk back |
| `src/Sanduhr.App/Desk/WallpaperInk.cs` | `Picture()`: the wallpaper as the monitor shows it, small, in memory, for the cards |
| `src/Sanduhr.App/ViewModels/DeskLooksViewModel.cs` (new), `Views/DeskLookCard.cs` (new), `Views/DeskLooksPanel.xaml(.cs)` (new) | The gallery: the cards' data, a card's picture, the row of buttons |
| `src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs`, `Views/DeskLookPage.xaml`, `Views/SettingsWindow.xaml` | The glow's switch and slider; the Looks section on the Desk page |
| `src/Sanduhr.App/ViewModels/SetupViewModel.cs`, `Views/SetupWindow.xaml`, `Services/SetupService.cs` | Start with a look |
| `CHANGELOG.md`, `docs/BACKLOG.md`, the spec, the umbrella | The record |

Tests sit beside their project: `tests/Sanduhr.Tests/` for Core, `tests/Sanduhr.Mcp.Tests/` for the server, `tests/Sanduhr.App.Tests/` for the App. Core tests may call `DeskDesignValidatorTests.Flat(brightness, overrides)` (an `internal static` wallpaper fixture in the same assembly).

---

### Task 1: The glow in the ink setting

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/DeskInk.cs` (the `DeskInkSettings` record at lines 15-59, `ResolvedInk` at 169-170, `DeskInk.Resolve` at 191-210)
- Modify: `windows-dotnet/src/Sanduhr.Core/ThemeMatch.cs:109`
- Test: `windows-dotnet/tests/Sanduhr.Tests/DeskGlowTests.cs` (new)

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `static class DeskGlow` with `MinStrength = 0.2`, `MaxStrength = 1.0`, `DefaultStrength = 0.6`, `MinRadius = 6`, `MaxRadius = 22`, `MinOpacity = 0.45`, `MaxOpacity = 0.9`, `BasePad = 16`, and `double Clamp(double)`, `double Radius(double strength)`, `double Opacity(double strength)`, `double Pad(double strength)` (strength 0 means off).
  - `DeskInkSettings(DeskInkMode Mode, string Color, bool Shadow, bool Glow = false, double GlowStrength = 0.6)` with `WithShadow(bool)`, `WithGlow(bool)`, `WithGlowStrength(double)`.
  - `ResolvedInk.Glow` (`double`, init-only): the strength, 0 when off. `ResolvedInk.GlowOneColor` (`bool`, init-only): a drawing hint only the App sets.
  - `DeskInk.Glowing(ResolvedInk ink, DeskInkSettings s)`.

- [ ] **Step 1: Record the baseline**

Run: `cd windows-dotnet && dotnet test`
Expected: all three suites pass. Write the three counts into the commit message of this task ("baseline Core N, MCP N, App N").

- [ ] **Step 2: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/DeskGlowTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The neon glow in the ink setting (5.0 spec 5, section 7): the numbers, the JSON, and glow and shadow as either-or.</summary>
public class DeskGlowTests
{
    private static JsonNode Node(string json) => JsonNode.Parse(json)!;

    [Fact]
    public void TheDefaultIsOffAtSixty()
    {
        Assert.False(DeskInkSettings.Default.Glow);
        Assert.Equal(0.6, DeskInkSettings.Default.GlowStrength);
        Assert.Equal(DeskInkSettings.Default, DeskInkSettings.Decode(Node("""{"mode":"auto","color":"ffffff","shadow":true}""")));
    }

    [Theory]
    [InlineData(0.2, 6, 0.45)]
    [InlineData(0.6, 14, 0.675)]
    [InlineData(1.0, 22, 0.9)]
    public void StrengthGivesTheRadiusAndTheOpacity(double strength, double radius, double opacity)
    {
        Assert.Equal(radius, DeskGlow.Radius(strength), 6);
        Assert.Equal(opacity, DeskGlow.Opacity(strength), 6);
    }

    [Theory]
    [InlineData(0, 16)]     // off
    [InlineData(0.2, 16)]   // radius 6 plus 4 is under the shadow's room
    [InlineData(0.6, 18)]
    [InlineData(1.0, 26)]
    public void ThePaddingIsTheLargerOfSixteenAndTheRadiusPlusFour(double strength, double pad) =>
        Assert.Equal(pad, DeskGlow.Pad(strength), 6);

    [Theory]
    [InlineData(5, 1.0)]
    [InlineData(0.01, 0.2)]
    [InlineData(double.NaN, 0.6)]
    [InlineData(0.456, 0.46)]
    public void AStrengthIsClampedAndRounded(double raw, double want) => Assert.Equal(want, DeskGlow.Clamp(raw));

    [Fact]
    public void DecodeReadsTheGlowAndItsStrength()
    {
        var s = DeskInkSettings.Decode(Node("""{"mode":"custom","color":"ffd08a","shadow":false,"glow":true,"glow_strength":0.8}"""));
        Assert.True(s.Glow);
        Assert.Equal(0.8, s.GlowStrength);
        Assert.False(s.Shadow);
    }

    [Fact]
    public void AFileWithBothOnKeepsTheGlow()
    {
        var s = DeskInkSettings.Decode(Node("""{"shadow":true,"glow":true}"""));
        Assert.True(s.Glow);
        Assert.False(s.Shadow);
    }

    [Theory]
    [InlineData("""{"glow":"yes"}""")]
    [InlineData("""{"glow":1}""")]
    [InlineData("""{"glow_strength":"high"}""")]
    public void AnOddMemberReadsAsItsDefault(string json)
    {
        var s = DeskInkSettings.Decode(Node(json));
        Assert.False(s.Glow);
        Assert.Equal(0.6, s.GlowStrength);
    }

    [Fact]
    public void DecodeClampsTheStrength() =>
        Assert.Equal(1.0, DeskInkSettings.Decode(Node("""{"glow_strength":7}""")).GlowStrength);

    [Fact]
    public void EncodeLeavesAnInstallWithoutGlowAsItWas() =>
        Assert.Equal("""{"mode":"auto","color":"ffffff","shadow":true}""", DeskInkSettings.Default.Encode().ToJsonString());

    [Fact]
    public void EncodeWritesTheGlowAndRoundTrips()
    {
        var s = new DeskInkSettings(DeskInkMode.Custom, "ffd08a", false, true, 0.8);
        var o = s.Encode();
        Assert.True((bool)o["glow"]!);
        Assert.Equal(0.8, (double)o["glow_strength"]!);
        Assert.Equal(s, DeskInkSettings.Decode(Node(o.ToJsonString())));
    }

    [Fact]
    public void AStrengthSetWhileTheGlowIsOffIsKept()
    {
        var s = DeskInkSettings.Default.WithGlowStrength(0.9);
        Assert.Null(s.Encode()["glow"]);
        Assert.Equal(s, DeskInkSettings.Decode(Node(s.Encode().ToJsonString())));
    }

    [Fact]
    public void GlowAndShadowAreEitherOr()
    {
        var glowing = DeskInkSettings.Default.WithGlow(true);
        Assert.True(glowing.Glow);
        Assert.False(glowing.Shadow);
        var shadowed = glowing.WithShadow(true);
        Assert.True(shadowed.Shadow);
        Assert.False(shadowed.Glow);
        // Turning one off never turns the other on.
        Assert.False(shadowed.WithShadow(false).Glow);
        Assert.False(glowing.WithGlow(false).Shadow);
    }

    [Fact]
    public void ResolveCarriesTheGlowAndDropsTheShadow()
    {
        var ink = DeskInk.Resolve(new DeskInkSettings(DeskInkMode.Custom, "ffd08a", false, true, 0.8), null, null);
        Assert.Equal(0.8, ink.Glow);
        Assert.Equal(0, ink.ShadowOpacity);

        var plain = DeskInk.Resolve(DeskInkSettings.Default, null, 0.1);
        Assert.Equal(0, plain.Glow);
        Assert.True(plain.ShadowOpacity > 0);
    }

    [Fact]
    public void AutomaticInkGlowsToo()
    {
        var ink = DeskInk.Resolve(DeskInkSettings.Default.WithGlow(true), null, 0.9);
        Assert.True(ink.DarkInk);
        Assert.Equal(0.6, ink.Glow);
    }

    [Fact]
    public void GlowingPutsTheSwitchOnAnInkMadeElsewhere()
    {
        var followed = DeskInk.Resolve(new DeskInkSettings(DeskInkMode.Custom, "33fdff", true), null, null);
        var on = DeskInk.Glowing(followed, DeskInkSettings.Default.WithGlow(true).WithGlowStrength(1));
        Assert.Equal(1.0, on.Glow);
        Assert.Equal(0, on.ShadowOpacity);
        Assert.Same(followed, DeskInk.Glowing(followed, DeskInkSettings.Default));
    }

    [Fact]
    public void OledCareKeepsTheGlow() =>
        Assert.Equal(0.6, DeskInk.CapWhite(DeskInk.Resolve(DeskInkSettings.Default.WithGlow(true), null, null)).Glow);

    [Fact]
    public void MatchNowTurnsTheShadowOnAndTheGlowOff()
    {
        var look = ThemeDeskLook.From(ThemeCatalog.BuiltIns["obsidian"]);
        var own = ThemeMatch.OwnSettings(look, DeskSettings.Default, DeskInkSettings.Default.WithGlow(true),
            DeskMessageSettings.Default, NowPlayingSettings.Default);
        Assert.True(own.Ink.Shadow);
        Assert.False(own.Ink.Glow);
    }
}
```

- [ ] **Step 3: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DeskGlowTests"`
Expected: the build fails with "The name 'DeskGlow' does not exist" and "'DeskInkSettings' does not contain a definition for 'Glow'".

- [ ] **Step 4: Add `DeskGlow` and the two members**

In `windows-dotnet/src/Sanduhr.Core/DeskInk.cs`, replace the `DeskInkSettings` record (its doc comment through its closing brace) with:

```csharp
/// <summary>
/// The neon glow's numbers (5.0 spec 5, section 7). A strength from 0.2 to 1.0 gives the blur radius of the copy drawn behind
/// the ink (6 to 22 DIPs) and that copy's opacity (0.45 to 0.9). Kept in this file because the MCP server links it.
/// </summary>
public static class DeskGlow
{
    public const double MinStrength = 0.2;
    public const double MaxStrength = 1.0;
    public const double DefaultStrength = 0.6;
    public const double MinRadius = 6;
    public const double MaxRadius = 22;
    public const double MinOpacity = 0.45;
    public const double MaxOpacity = 0.9;

    /// <summary>The room around an island for the ink's shadow, in DIPs: what the Desk has always left.</summary>
    public const double BasePad = 16;

    /// <summary>The room the glow needs beyond its blur radius, in DIPs.</summary>
    public const double PadBeyondRadius = 4;

    /// <summary>A strength held to the range and rounded to two places; not a number reads as the default.</summary>
    public static double Clamp(double strength) =>
        double.IsFinite(strength) ? Math.Round(Math.Clamp(strength, MinStrength, MaxStrength), 2) : DefaultStrength;

    private static double Along(double strength) => (Clamp(strength) - MinStrength) / (MaxStrength - MinStrength);

    /// <summary>The blur radius of the glowing copy, in DIPs.</summary>
    public static double Radius(double strength) => MinRadius + Along(strength) * (MaxRadius - MinRadius);

    /// <summary>The opacity of the glowing copy.</summary>
    public static double Opacity(double strength) => MinOpacity + Along(strength) * (MaxOpacity - MinOpacity);

    /// <summary>The room around an island: <see cref="BasePad"/> with the glow off (a strength of 0 or less), otherwise the larger of
    /// that and the blur radius plus <see cref="PadBeyondRadius"/>, so the glow is never clipped by its window.</summary>
    public static double Pad(double strength) => strength <= 0 ? BasePad : Math.Max(BasePad, Radius(strength) + PadBeyondRadius);
}

/// <summary>
/// The Desk ink setting (settings.json <c>desk_ink</c> <c>{mode, color, shadow, glow, glow_strength}</c>). <paramref name="Color"/>
/// is the Mac's hex spec (one to four colors, comma separated) and only matters in Custom mode;
/// <paramref name="Shadow"/> is "Drop shadow under the text"; <paramref name="Glow"/> is "Neon glow" (5.0 spec 5) at
/// <paramref name="GlowStrength"/>. Glow and shadow are either-or: use <see cref="WithShadow"/> and <see cref="WithGlow"/> to change
/// one. A missing or odd member reads as its default and an unknown mode as Automatic, so an existing install is unchanged.
/// </summary>
public sealed record DeskInkSettings(DeskInkMode Mode, string Color, bool Shadow, bool Glow = false, double GlowStrength = DeskGlow.DefaultStrength)
{
    public static DeskInkSettings Default { get; } = new(DeskInkMode.Auto, "ffffff", true);

    /// <summary>The shadow switched: on turns the glow off, off leaves the glow as it is.</summary>
    public DeskInkSettings WithShadow(bool on) => this with { Shadow = on, Glow = !on && Glow };

    /// <summary>The glow switched: on turns the shadow off, off leaves the shadow as it is.</summary>
    public DeskInkSettings WithGlow(bool on) => this with { Glow = on, Shadow = !on && Shadow };

    /// <summary>The glow's strength, clamped. It is kept while the glow is off.</summary>
    public DeskInkSettings WithGlowStrength(double strength) => this with { GlowStrength = DeskGlow.Clamp(strength) };

    /// <summary>The settings word for <paramref name="mode"/>: "auto", "light", "dark", "theme" or "custom".</summary>
    public static string ModeWord(DeskInkMode mode) => mode switch
    {
        DeskInkMode.Light => "light",
        DeskInkMode.Dark => "dark",
        DeskInkMode.Theme => "theme",
        DeskInkMode.Custom => "custom",
        _ => "auto",
    };

    /// <summary>The mode for a settings word (case-insensitive); null, empty or unknown is Automatic.</summary>
    public static DeskInkMode ParseMode(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "light" => DeskInkMode.Light,
        "dark" => DeskInkMode.Dark,
        "theme" => DeskInkMode.Theme,
        "custom" => DeskInkMode.Custom,
        _ => DeskInkMode.Auto,
    };

    /// <summary>Reads the <c>desk_ink</c> node. Anything that is not an object, and any member of the wrong type, is the default.
    /// A node with both the shadow and the glow on keeps the glow, the newer choice.</summary>
    public static DeskInkSettings Decode(JsonNode? node)
    {
        if (node is not JsonObject o) return Default;
        var mode = DeskInkMode.Auto;
        var color = Default.Color;
        var shadow = Default.Shadow;
        bool glow = false;
        double strength = DeskGlow.DefaultStrength;
        if (o["mode"] is JsonValue m && m.TryGetValue<string>(out var word)) mode = ParseMode(word);
        if (o["color"] is JsonValue c && c.TryGetValue<string>(out var spec)) color = spec;
        if (o["shadow"] is JsonValue s && s.TryGetValue<bool>(out var on)) shadow = on;
        if (o["glow"] is JsonValue g && g.TryGetValue<bool>(out var lit)) glow = lit;
        if (o["glow_strength"] is JsonValue gs)
        {
            if (gs.TryGetValue<double>(out var d)) strength = DeskGlow.Clamp(d);
            else if (gs.TryGetValue<int>(out var i)) strength = DeskGlow.Clamp(i);
        }
        return new DeskInkSettings(mode, color, shadow && !glow, glow, strength);
    }

    /// <summary>The <c>desk_ink</c> object. <c>glow</c> is written only when on and <c>glow_strength</c> only when it is not the
    /// default, so an install that never used the glow keeps the three members it always had.</summary>
    public JsonObject Encode()
    {
        var o = new JsonObject
        {
            ["mode"] = ModeWord(Mode),
            ["color"] = Color,
            ["shadow"] = Shadow,
        };
        if (Glow) o["glow"] = true;
        if (GlowStrength != DeskGlow.DefaultStrength) o["glow_strength"] = GlowStrength;
        return o;
    }
}
```

- [ ] **Step 5: Carry the glow on the resolved ink**

In the same file, replace the `ResolvedInk` record with:

```csharp
/// <summary>
/// The ink the Desk draws with: gradient stops (two or more, six-digit hex), whether the ink is dark (so
/// the shadow and the message's default color flip), the shadow color and opacity (0 when the shadow is off),
/// and the opacity the text is drawn at.
/// </summary>
public sealed record ResolvedInk(
    IReadOnlyList<string> Stops, bool DarkInk, string ShadowHex, double ShadowOpacity, double TextOpacity)
{
    /// <summary>The neon glow's strength (0.2 to 1.0), 0 when the glow is off. A glowing ink has no shadow.</summary>
    public double Glow { get; init; }

    /// <summary>Draw the glow in one color, the ink's middle stop, instead of a blurred copy of the ink. The Desk sets it for
    /// a surface where the copy costs too much (under the desktop icons, when the measurement said so); nothing in Core does.</summary>
    public bool GlowOneColor { get; init; }
}
```

In `DeskInk`, rename the existing `Resolve` method to `private static ResolvedInk Plain(...)` (same parameters and body, and change its two inner recursive calls from `Resolve(` to `Plain(`), then add above it:

```csharp
    /// <summary>
    /// The ink for <paramref name="s"/>. <paramref name="themeTextHex"/> is the widget theme's text color
    /// (Theme mode; null or invalid reads as Light). <paramref name="wallpaperLuminance"/> is the sampled
    /// brightness behind the island (Automatic; null or NaN is unknown, so Light). Theme and Custom inks are
    /// the user's or the theme's own color, drawn at full opacity. With the glow on, the ink carries its strength
    /// and no shadow (<see cref="Glowing"/>).
    /// </summary>
    public static ResolvedInk Resolve(DeskInkSettings s, string? themeTextHex, double? wallpaperLuminance) =>
        Glowing(Plain(s, themeTextHex, wallpaperLuminance), s);

    /// <summary>The glow switch of <paramref name="s"/> put on an ink, whoever made it (the Desk's own, or a followed widget
    /// theme's): on gives the ink the strength and takes its shadow away, since the Desk draws one or the other. Off returns
    /// <paramref name="ink"/> itself.</summary>
    public static ResolvedInk Glowing(ResolvedInk ink, DeskInkSettings s) =>
        s.Glow ? ink with { Glow = DeskGlow.Clamp(s.GlowStrength), ShadowOpacity = 0 } : ink;
```

Move the old doc comment of `Resolve` away with the rename: `Plain` keeps a one-line comment, `/// <summary>The ink without the glow: the six rules of spec 6.7.</summary>`.

- [ ] **Step 6: Match now keeps the two switches either-or**

In `windows-dotnet/src/Sanduhr.Core/ThemeMatch.cs`, in `OwnSettings`, replace

```csharp
        ink with { Mode = DeskInkMode.Custom, Color = string.Join(",", look.Ink), Shadow = true },
```

with

```csharp
        (ink with { Mode = DeskInkMode.Custom, Color = string.Join(",", look.Ink) }).WithShadow(true),
```

- [ ] **Step 7: Run the tests**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DeskGlowTests|FullyQualifiedName~DeskInkTests|FullyQualifiedName~ThemeMatchTests"`
Expected: PASS. `DeskInkTests.EncodeWritesAllThreeAndRoundTrips` passes unchanged.

- [ ] **Step 8: Run every suite and commit**

```bash
cd windows-dotnet && dotnet test
git add src/Sanduhr.Core/DeskInk.cs src/Sanduhr.Core/ThemeMatch.cs tests/Sanduhr.Tests/DeskGlowTests.cs
git commit -m "feat(windows): the neon glow in the Desk's ink setting

desk_ink gains glow and glow_strength; glow and shadow are either-or.
Baseline before this branch: Core N, MCP N, App N.

Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>"
```

---

### Task 2: `glow` in the design format

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/DeskDesign.cs` (the records at the top, `Fields` at 109-110, `Parse` at 126-166, `ToJson` at 568-624, `Merge` at 635-663, `Diff` at 710-786)
- Modify: `windows-dotnet/src/Sanduhr.Core/DeskDesignApply.cs:65-79` (`Ink`)
- Modify: `windows-dotnet/src/Sanduhr.Core/DeskDesignLibrary.cs:121-158` (`DeskDesigns.Canonical`)
- Modify: `windows-dotnet/src/Sanduhr.Core/DeskState.cs:21-38` (`Current`)
- Test: `windows-dotnet/tests/Sanduhr.Tests/DeskDesignGlowTests.cs` (new)

**Interfaces:**
- Consumes: `DeskGlow`, `DeskInkSettings.WithShadow`, `WithGlow`, `WithGlowStrength` from Task 1.
- Produces:
  - `sealed record DeskGlowChoice(bool On, double? Strength = null)` with `static DeskGlowChoice Off` and `string Describe()` ("off", "on", "on at 80%").
  - `DeskDesign.Glow` (`DeskGlowChoice?`, init-only). JSON: `false`, `true`, or a number from 0.2 to 1.0.
  - `"glow"` in `DeskDesign.FieldNames`, right after `"shadow"`.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/DeskDesignGlowTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The design format's <c>glow</c> field (5.0 spec 5, section 7): its three forms, the either-or with <c>shadow</c>, and
/// how it merges, compares and reaches the settings.</summary>
public class DeskDesignGlowTests
{
    private static JsonObject Obj(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static DeskDesign Design(string json) => DeskDesign.Parse(Obj(json)).Design!;

    [Fact]
    public void GlowIsAFieldRightAfterShadow()
    {
        var fields = DeskDesign.FieldNames.ToList();
        Assert.Equal(fields.IndexOf("shadow") + 1, fields.IndexOf("glow"));
    }

    [Fact]
    public void TheThreeFormsParse()
    {
        Assert.Equal(new DeskGlowChoice(false), Design("""{"glow":false}""").Glow);
        Assert.Equal(new DeskGlowChoice(true), Design("""{"glow":true}""").Glow);
        Assert.Equal(new DeskGlowChoice(true, 0.8), Design("""{"glow":0.8}""").Glow);
        Assert.Equal(new DeskGlowChoice(true, 0.2), Design("""{"glow":0.2}""").Glow);
        Assert.Equal(new DeskGlowChoice(true, 1.0), Design("""{"glow":1}""").Glow);
        Assert.Null(Design("""{"plate":true}""").Glow);
        Assert.Null(Design("""{"glow":null}""").Glow);
    }

    [Theory]
    [InlineData("""{"glow":0.1}""")]
    [InlineData("""{"glow":1.5}""")]
    [InlineData("""{"glow":0}""")]
    [InlineData("""{"glow":-1}""")]
    [InlineData("""{"glow":"on"}""")]
    [InlineData("""{"glow":[]}""")]
    [InlineData("""{"glow":{"strength":0.5}}""")]
    public void AnythingElseIsRefusedAtGlow(string json)
    {
        var parsed = DeskDesign.Parse(Obj(json));
        Assert.Null(parsed.Design);
        Assert.Contains(parsed.Reasons, r => r.Field == "glow");
    }

    [Theory]
    [InlineData("""{"glow":true,"shadow":true}""")]
    [InlineData("""{"glow":0.5,"shadow":true}""")]
    public void GlowOnWithShadowOnIsRefusedNamingBoth(string json)
    {
        var parsed = DeskDesign.Parse(Obj(json));
        Assert.Null(parsed.Design);
        var reason = Assert.Single(parsed.Reasons, r => r.Field == "glow");
        Assert.Contains("shadow", reason.Message);
    }

    [Theory]
    [InlineData("""{"glow":false,"shadow":true}""")]
    [InlineData("""{"glow":0.5,"shadow":false}""")]
    [InlineData("""{"glow":false,"shadow":false}""")]
    public void TheOtherPairsAreFine(string json) => Assert.NotNull(DeskDesign.Parse(Obj(json)).Design);

    [Theory]
    [InlineData("""{"glow":false}""")]
    [InlineData("""{"glow":true}""")]
    [InlineData("""{"glow":0.8}""")]
    public void TheDesignWritesBackWhatItRead(string json) =>
        Assert.Equal(json, Design(json).ToJson().ToJsonString());

    [Fact]
    public void AProposalThatTurnsTheGlowOnTurnsTheShadowOff()
    {
        var merged = DeskDesign.Merge(Design("""{"shadow":true,"glow":false}"""), Design("""{"glow":0.8}"""));
        Assert.Equal(new DeskGlowChoice(true, 0.8), merged.Glow);
        Assert.False(merged.Shadow);
    }

    [Fact]
    public void AProposalThatTurnsTheShadowOnTurnsTheGlowOff()
    {
        var merged = DeskDesign.Merge(Design("""{"shadow":false,"glow":0.8}"""), Design("""{"shadow":true}"""));
        Assert.True(merged.Shadow);
        Assert.Equal(DeskGlowChoice.Off, merged.Glow);
    }

    [Fact]
    public void GlowTrueKeepsTheStrengthAlreadySet()
    {
        var merged = DeskDesign.Merge(Design("""{"shadow":false,"glow":0.8}"""), Design("""{"glow":true}"""));
        Assert.Equal(new DeskGlowChoice(true, 0.8), merged.Glow);
    }

    [Fact]
    public void AProposalThatSaysNeitherLeavesBoth()
    {
        var current = Design("""{"shadow":false,"glow":0.8}""");
        var merged = DeskDesign.Merge(current, Design("""{"plate":true}"""));
        Assert.Equal(current.Glow, merged.Glow);
        Assert.False(merged.Shadow);
    }

    [Fact]
    public void TurningTheShadowOffLeavesTheGlowAlone()
    {
        var merged = DeskDesign.Merge(Design("""{"shadow":true,"glow":false}"""), Design("""{"shadow":false}"""));
        Assert.Equal(DeskGlowChoice.Off, merged.Glow);
        Assert.False(merged.Shadow);
    }

    [Fact]
    public void TheDiffNamesTheGlow()
    {
        var a = Design("""{"shadow":true,"glow":false}""");
        Assert.Contains("Neon glow: on at 80%", DeskDesign.Diff(a, DeskDesign.Merge(a, Design("""{"glow":0.8}"""))));
        Assert.Contains("Text shadow: off", DeskDesign.Diff(a, DeskDesign.Merge(a, Design("""{"glow":0.8}"""))));

        var lit = Design("""{"shadow":false,"glow":0.8}""");
        Assert.Contains("Neon glow: off", DeskDesign.Diff(lit, DeskDesign.Merge(lit, Design("""{"glow":false}"""))));
        Assert.DoesNotContain(DeskDesign.Diff(lit, DeskDesign.Merge(lit, Design("""{"plate":true}"""))), l => l.StartsWith("Neon glow"));
        // A Desk that never said anything about the glow is a Desk with it off.
        Assert.DoesNotContain(DeskDesign.Diff(Design("""{"shadow":true}"""), Design("""{"glow":false}""")), l => l.StartsWith("Neon glow"));
    }

    [Fact]
    public void ADesignFromBeforeTheGlowIsTheSameLookAsOneWithItOff()
    {
        Assert.Equal(DeskDesigns.Canonical(Design("""{"shadow":true}""")), DeskDesigns.Canonical(Design("""{"shadow":true,"glow":false}""")));
        Assert.NotEqual(DeskDesigns.Canonical(Design("""{"shadow":false}""")), DeskDesigns.Canonical(Design("""{"shadow":false,"glow":0.8}""")));
        Assert.NotEqual(DeskDesigns.Canonical(Design("""{"shadow":false,"glow":0.5}""")), DeskDesigns.Canonical(Design("""{"shadow":false,"glow":0.8}""")));
    }

    [Fact]
    public void TheSettingsADesignWritesCarryTheGlow()
    {
        var was = new DeskInkSettings(DeskInkMode.Auto, "ffffff", true, false, 0.4);

        var on = DeskDesignApply.Ink(was, Design("""{"glow":0.8,"shadow":false}"""));
        Assert.True(on.Glow);
        Assert.Equal(0.8, on.GlowStrength);
        Assert.False(on.Shadow);

        var keeps = DeskDesignApply.Ink(was, Design("""{"glow":true}"""));
        Assert.True(keeps.Glow);
        Assert.Equal(0.4, keeps.GlowStrength);
        Assert.False(keeps.Shadow);

        var off = DeskDesignApply.Ink(on, Design("""{"glow":false}"""));
        Assert.False(off.Glow);
        Assert.Equal(0.8, off.GlowStrength);

        var shadowed = DeskDesignApply.Ink(on, Design("""{"shadow":true}"""));
        Assert.True(shadowed.Shadow);
        Assert.False(shadowed.Glow);

        Assert.Equal(was, DeskDesignApply.Ink(was, Design("""{"plate":true}""")));
    }

    [Fact]
    public void GetDeskReportsTheGlow()
    {
        var off = DeskState.Current(DeskSettings.Default, DeskInkSettings.Default, LauncherModel.Empty, null);
        Assert.Equal(DeskGlowChoice.Off, off.Glow);
        Assert.False((bool)off.ToJson()["glow"]!);

        var on = DeskState.Current(DeskSettings.Default, DeskInkSettings.Default.WithGlow(true).WithGlowStrength(0.8), LauncherModel.Empty, null);
        Assert.Equal(0.8, (double)on.ToJson()["glow"]!);
        Assert.False((bool)on.ToJson()["shadow"]!);
    }

    [Fact]
    public void WhatGetDeskReportsRoundTripsThroughAProposal()
    {
        var ink = DeskInkSettings.Default.WithGlow(true).WithGlowStrength(0.8);
        var current = DeskState.Current(DeskSettings.Default, ink, LauncherModel.Empty, null);
        Assert.True(DeskDesign.Validate(current.ToJson(), DeskDesignContext.Unknown).Ok);
        Assert.Equal(ink, DeskDesignApply.Ink(DeskInkSettings.Default, DeskDesign.Merge(null, current)));
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DeskDesignGlowTests"`
Expected: the build fails with "The type or namespace name 'DeskGlowChoice' could not be found".

- [ ] **Step 3: The choice record, the field and the parser**

In `windows-dotnet/src/Sanduhr.Core/DeskDesign.cs`, after the `DeskFontChoice` record, add:

```csharp
/// <summary>A design's neon glow (5.0 spec 5): off, on at the strength the Desk already has (<see cref="Strength"/> null), or on
/// at a strength from 0.2 to 1.0. In JSON: <c>false</c>, <c>true</c>, or the number.</summary>
public sealed record DeskGlowChoice(bool On, double? Strength = null)
{
    public static DeskGlowChoice Off { get; } = new(false);

    /// <summary>The words the "what changed" list uses.</summary>
    public string Describe() => !On ? "off"
        : Strength is { } s ? "on at " + Math.Round(s * 100).ToString("0", CultureInfo.InvariantCulture) + "%"
        : "on";
}
```

In the class doc comment of `DeskDesign`, after `<c>shadow</c>,` add `<c>glow</c> (false, true, or a strength from 0.2 to 1; on with <c>shadow</c> true is refused),`.

After the `Shadow` property add:

```csharp
    /// <summary>The neon glow; null leaves it alone. Glow on and <see cref="Shadow"/> true cannot both be asked for.</summary>
    public DeskGlowChoice? Glow { get; init; }
```

Replace the `Fields` array with:

```csharp
    private static readonly string[] Fields =
        ["name", "note", "layout", "weather", "font", "ink", "plate", "shadow", "glow", "launcher", "messages", "looks", "theme", "follows_theme"];
```

In `Parse`, after `bool? shadow = Bool(o, "shadow", r);` add:

```csharp
        var glow = ParseGlow(o["glow"], r);
        if (glow is { On: true } && shadow == true)
            r.Add(new DeskReason("glow", "cannot be on while shadow is true: the Desk draws one or the other. Send shadow: false with it, or leave shadow out."));
```

and in the object initializer at the end of `Parse` add `Glow = glow,` after `Shadow = shadow,`.

After the `Bool` helper add:

```csharp
    private static DeskGlowChoice? ParseGlow(JsonNode? node, List<DeskReason> r)
    {
        if (node is null) return null;
        if (node is JsonValue v && v.TryGetValue<bool>(out var on)) return new DeskGlowChoice(on);
        if (TryNumber(node, out var s) && s >= DeskGlow.MinStrength - 1e-9 && s <= DeskGlow.MaxStrength + 1e-9)
            return new DeskGlowChoice(true, DeskGlow.Clamp(s));
        r.Add(new DeskReason("glow", $"must be true, false, or a strength from {DeskGlow.MinStrength.ToString(CultureInfo.InvariantCulture)} to {DeskGlow.MaxStrength.ToString("0", CultureInfo.InvariantCulture)}"));
        return null;
    }
```

- [ ] **Step 4: Writing, merging and comparing**

In `ToJson`, after the `shadow` line add:

```csharp
        if (Glow is { } glow)
            o["glow"] = !glow.On ? JsonValue.Create(false) : glow.Strength is { } strength ? JsonValue.Create(strength) : JsonValue.Create(true);
```

In `Merge`, replace the body from `return new DeskDesign` to the end of the method with:

```csharp
        // Glow and shadow are either-or: what the proposal turns on turns the other off. "glow: true" keeps the strength there is.
        var glow = proposal.Glow ?? current?.Glow;
        if (proposal.Glow is { On: true, Strength: null } && current?.Glow is { On: true, Strength: { } kept })
            glow = new DeskGlowChoice(true, kept);
        var shadow = proposal.Shadow ?? current?.Shadow;
        if (proposal.Glow is { On: true }) shadow = false;
        else if (proposal.Shadow == true && glow is { On: true }) glow = DeskGlowChoice.Off;

        return new DeskDesign
        {
            Name = proposal.Name ?? current?.Name,
            Note = proposal.Note ?? current?.Note,
            Layout = layout,
            Font = proposal.Font ?? current?.Font,
            Ink = proposal.Ink ?? current?.Ink,
            Plate = proposal.Plate ?? current?.Plate,
            Shadow = shadow,
            Glow = glow,
            Launcher = launcher,
            Messages = proposal.Messages ?? current?.Messages,
            Theme = proposal.Theme ?? current?.Theme,
            Looks = proposal.Looks ?? current?.Looks,
            Weather = proposal.Weather ?? current?.Weather,
            FollowsTheme = proposal.FollowsTheme ?? current?.FollowsTheme,
        };
```

In `Diff`, after the `Text shadow` line add:

```csharp
        if (b.Glow is { } neon && (a.Glow ?? DeskGlowChoice.Off).Describe() != neon.Describe())
            lines.Add($"Neon glow: {neon.Describe()}");
```

- [ ] **Step 5: The settings, the canonical form and `get_desk`**

In `windows-dotnet/src/Sanduhr.Core/DeskDesignApply.cs`, in `Ink`, replace

```csharp
        var next = current with { Shadow = design.Shadow ?? current.Shadow };
```

with

```csharp
        var next = current;
        if (design.Shadow is { } shadow) next = next.WithShadow(shadow);
        if (design.Glow is { } glow)
        {
            next = next.WithGlow(glow.On);
            if (glow is { On: true, Strength: { } strength }) next = next.WithGlowStrength(strength);
        }
```

In `windows-dotnet/src/Sanduhr.Core/DeskDesignLibrary.cs`, in `DeskDesigns.Canonical`, after the `follows_theme` line add:

```csharp
        // No glow is the same look as a design from before the glow (5.0 spec 5).
        if (design.Glow is not { On: true }) o.Remove("glow");
```

In `windows-dotnet/src/Sanduhr.Core/DeskState.cs`, in `Current`, after `Shadow = ink.Shadow,` add:

```csharp
            Glow = ink.Glow ? new DeskGlowChoice(true, DeskGlow.Clamp(ink.GlowStrength)) : DeskGlowChoice.Off,
```

and in that method's doc comment add `The neon glow is reported as <c>glow</c>: false, or its strength.`

- [ ] **Step 6: Run the tests**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DeskDesign"`
Expected: PASS for `DeskDesignGlowTests`. Some older tests may now fail because they pin text that lists the design's fields or the exact JSON of the current Desk. For each, the fix is to add `glow` where the pinned text lists the fields (after `shadow`) or `"glow":false` where it pins the current Desk's JSON (after `"shadow"`). Change nothing else in those tests.

- [ ] **Step 7: Run every suite and commit**

```bash
cd windows-dotnet && dotnet test
git add src/Sanduhr.Core tests/Sanduhr.Tests
git commit -m "feat(windows): the design format gains glow

false, true, or a strength from 0.2 to 1. Glow on with shadow true is
refused; a merge turns the other one off.

Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>"
```

---

### Task 3: The server's schema, the guides and the chat prompt

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Mcp/ToolCatalog.cs` (the `propose_desk` schema near line 291, `DeskDesignGuideRead` near 618, `DeskDesignGuidePropose` near 649)
- Modify: `windows-dotnet/src/Sanduhr.Core/SetupPrompt.cs:25-26` and the `plate` line near 83
- Test: `windows-dotnet/tests/Sanduhr.Mcp.Tests/ToolLogicDeskDesignTests.cs`, `windows-dotnet/tests/Sanduhr.Tests/SetupPromptTests.cs`

**Interfaces:**
- Consumes: `DeskDesign.Glow` and its parse rules from Task 2.
- Produces: `"glow"` in `SetupPrompt.DeskFields` (after `"shadow"`); `glow` in the `propose_desk` input schema. No new tool, no new file linked into the server.

- [ ] **Step 1: Write the failing tests**

In `windows-dotnet/tests/Sanduhr.Mcp.Tests/ToolLogicDeskDesignTests.cs`, add these `InlineData` rows to `A_bad_design_is_refused_with_its_field_and_leaves_no_request_file` (above the method, with the other rows):

```csharp
    [InlineData("""{"glow":2}""", "glow")]
    [InlineData("""{"glow":"neon"}""", "glow")]
    [InlineData("""{"glow":true,"shadow":true}""", "glow")]
```

and add these tests to the class:

```csharp
    [Theory]
    [InlineData("""{"glow":true}""")]
    [InlineData("""{"glow":false}""")]
    [InlineData("""{"glow":0.8,"shadow":false}""")]
    public void A_glow_in_any_of_its_forms_is_sent_to_the_widget(string design)
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        var r = Propose(cfg, design);

        Assert.Equal("queued", (string?)r["status"]);
        Assert.True(File.Exists(cfg.DesignRequestPath!));
    }

    [Fact]
    public void Get_reports_the_glow_the_widget_wrote()
    {
        using var tmp = new TempDir();
        var cfg = DesignConfig(tmp);
        WriteState(cfg, s => s["design"]!["glow"] = 0.8);

        var r = new ToolLogic(cfg, () => Now).BuildGetDesk();

        Assert.Equal(0.8, (double)r["design"]!["glow"]!);
    }

    [Fact]
    public void The_schema_and_both_guides_teach_the_glow()
    {
        var tools = ToolCatalog.Build();
        var propose = tools.Single(t => (string?)t!["name"] == "propose_desk")!;
        Assert.NotNull(propose["inputSchema"]!["properties"]!["design"]!["properties"]!["glow"]);
        Assert.Contains("glow (", (string?)propose["description"]);
        Assert.Contains("shadow, glow,", (string?)tools.Single(t => (string?)t!["name"] == "get_desk")!["description"]);
    }
```

`ToolCatalog.Build()` returns the tools as a `JsonArray`, the one `McpServerTests` reads `inputSchema` from.

In `windows-dotnet/tests/Sanduhr.Tests/SetupPromptTests.cs`, add:

```csharp
    [Fact]
    public void The_prompt_teaches_the_glow_and_a_pasted_design_may_carry_it()
    {
        Assert.Contains("glow", SetupPrompt.DeskFields);
        Assert.Contains("glow: true or false, or a strength from 0.2 to 1", Prompt);
        var verdict = PastedSetup.RefuseUndrawn(DeskDesign.Validate(
            (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse("""{"glow":0.7,"shadow":false}""")!, DeskDesignContext.Unknown));
        Assert.True(verdict.Ok);
    }
```

`Prompt` is the field that test class already reads the built prompt from.

- [ ] **Step 2: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Mcp.Tests --filter "FullyQualifiedName~ToolLogicDeskDesignTests" ; dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SetupPromptTests"`
Expected: `The_schema_and_both_guides_teach_the_glow` and `The_prompt_teaches_the_glow_and_a_pasted_design_may_carry_it` FAIL. The other new server tests already pass, because the server links `DeskDesign.cs` and Task 2 taught it the field; they stay as the record that the server accepts it.

- [ ] **Step 3: The schema and the guides**

In `windows-dotnet/src/Sanduhr.Mcp/ToolCatalog.cs`, in the `propose_desk` schema, after

```csharp
                            ["shadow"] = new JsonObject { ["type"] = "boolean" },
```

add

```csharp
                            ["glow"] = new JsonObject
                            {
                                ["description"] = "false, true (on at the strength the user has set), or a strength from 0.2 to 1: a neon glow behind the ink, in the ink's own colors. On with shadow true is refused.",
                            },
```

In `DeskDesignGuideRead`, replace `font, ink, plate, shadow, launcher, theme, follows_theme: true while the Desk draws the ` with `font, ink, plate, shadow, glow, launcher, theme, follows_theme: true while the Desk draws the `.

In `DeskDesignGuidePropose`, replace

```csharp
        "left-to-right gradient); plate and shadow (true or false; the plate is a soft panel behind each stack of pieces); " +
```

with

```csharp
        "left-to-right gradient); plate and shadow (true or false; the plate is a soft panel behind each stack of pieces); " +
        "glow (false, true for the strength the user has set, or a strength from 0.2 to 1: a neon glow behind the ink, in the ink's own " +
        "colors, so a gradient ink glows as a gradient; it replaces the shadow, so glow on with shadow true is refused, and it does not " +
        "count toward the contrast rule); " +
```

The first test's `"shadow, glow,"` matches the read guide after this edit.

- [ ] **Step 4: The chat prompt**

In `windows-dotnet/src/Sanduhr.Core/SetupPrompt.cs`, replace the `DeskFields` list with:

```csharp
    public static readonly IReadOnlyList<string> DeskFields =
        ["name", "note", "layout", "weather", "font", "ink", "plate", "shadow", "glow", "messages"];
```

and replace

```csharp
        Line("- plate: true or false, a soft panel behind each stack of pieces. shadow: true or false.");
```

with

```csharp
        Line("- plate: true or false, a soft panel behind each stack of pieces. shadow: true or false.");
        Line($"- glow: true or false, or a strength from {DeskGlow.MinStrength.ToString(System.Globalization.CultureInfo.InvariantCulture)} to {DeskGlow.MaxStrength.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}, a neon glow behind the text in its own colors. It replaces the shadow, so never send glow on with shadow true.");
```

- [ ] **Step 5: Run the tests**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Mcp.Tests ; dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SetupPrompt|FullyQualifiedName~PastedSetup"`
Expected: PASS. `TrustBoundaryTests` passes with no change to it or to `Sanduhr.Mcp.csproj`: run `git status --short src/Sanduhr.Mcp/Sanduhr.Mcp.csproj tests/Sanduhr.Mcp.Tests/TrustBoundaryTests.cs` and expect no output.

- [ ] **Step 6: Run every suite and commit**

```bash
cd windows-dotnet && dotnet test
git add src/Sanduhr.Mcp/ToolCatalog.cs src/Sanduhr.Core/SetupPrompt.cs tests
git commit -m "feat(windows): propose_desk and the chat prompt teach glow

Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>"
```

---

### Task 4: The glow layer, and what it costs under the desktop icons

The spec asks for the under-icons measurement first. It needs the thing it measures, so this task builds the glow's one small
drawing helper and measures it before anything is wired to it. The measurement sets one switch, `DeskGlowLayer.UnderIconsOneColor`;
both ways of drawing exist either way, so no later task depends on which way it falls.

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/Desk/DeskGlowLayer.cs`
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DeskGlowLayerTests.cs` (new), `windows-dotnet/tests/Sanduhr.App.Tests/DeskGlowCostTests.cs` (new)

**Interfaces:**
- Consumes: `DeskGlow.Radius`, `DeskGlow.Opacity`, `DeskGlow.Pad`, `ResolvedInk.Glow`, `ResolvedInk.GlowOneColor` (Task 1); `DeskView.Rgb(string hex)`.
- Produces (all in namespace `Sanduhr.App.Desk`):
  - `internal sealed class GlowHost : Grid` — a piece with its glow behind it. Child 0 is the copy, child 1 the piece.
  - `internal static class DeskGlowLayer` with
    - `static readonly bool UnderIconsOneColor`
    - `static FrameworkElement On(FrameworkElement piece, ResolvedInk ink)` — the element to put where the piece was: the piece itself (no glow, or the one-color glow as its `Effect`), or a `GlowHost`.
    - `static GlowHost Wrap(FrameworkElement piece, double strength)`
    - `static Rectangle Copy(FrameworkElement source, double strength)`
    - `static DropShadowEffect OneColor(string hex, double strength)`
    - `static string Middle(IReadOnlyList<string> stops)`

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/DeskGlowLayerTests.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.Core;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Sanduhr.App.Tests;

/// <summary>The neon glow's drawing helper (5.0 spec 5, section 7): a blurred copy of a piece behind it, or one color where the copy
/// costs too much.</summary>
public sealed class DeskGlowLayerTests
{
    private static ResolvedInk Ink(double glow, bool oneColor = false) =>
        DeskInk.Resolve(new DeskInkSettings(DeskInkMode.Custom, "8f5bd6,3a63e0,33fdff", false, glow > 0, glow > 0 ? glow : 0.6), null, null)
            with { GlowOneColor = oneColor };

    private static TextBlock Piece() => new()
    {
        Text = "12:34", FontSize = 100, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Segoe UI"),
        Foreground = DeskView.InkBrush(["8f5bd6", "33fdff"]), HorizontalAlignment = HorizontalAlignment.Right, Tag = "clock",
    };

    /// <summary>The element as the under-icons host copies it: laid out in a padded root, drawn through a brush into a bitmap.</summary>
    internal static (byte[] Px, int W, int H) Render(FrameworkElement content, double pad)
    {
        var root = new Border { Child = content, Padding = new Thickness(pad), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, () => { });
        int w = (int)Math.Ceiling(root.ActualWidth), h = (int)Math.Ceiling(root.ActualHeight);
        var brush = new VisualBrush(root) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, root.ActualWidth, root.ActualHeight) };
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var px = new byte[w * h * 4];
        rtb.CopyPixels(px, w * 4, 0);
        return (px, w, h);
    }

    [Fact]
    public void No_glow_leaves_the_piece_alone() => Sta.Run(() =>
    {
        var piece = Piece();
        Assert.Same(piece, DeskGlowLayer.On(piece, Ink(0)));
        Assert.Null(piece.Effect);
        Assert.Equal("clock", piece.Tag);
    });

    [Fact]
    public void The_wrapper_takes_the_pieces_tag_and_alignment_and_puts_the_copy_under_it() => Sta.Run(() =>
    {
        var piece = Piece();
        var host = Assert.IsType<GlowHost>(DeskGlowLayer.On(piece, Ink(0.8)));

        Assert.Equal("clock", host.Tag);
        Assert.Null(piece.Tag);   // one tagged element, so the Desk's clicks find one clock
        Assert.Equal(HorizontalAlignment.Right, host.HorizontalAlignment);
        Assert.Equal(2, host.Children.Count);
        var copy = Assert.IsType<Rectangle>(host.Children[0]);
        Assert.Same(piece, host.Children[1]);
        Assert.Null(piece.Effect);   // the glow replaces the shadow
        Assert.False(copy.IsHitTestVisible);
        Assert.Equal(DeskGlow.Opacity(0.8), copy.Opacity, 6);
        Assert.Equal(DeskGlow.Radius(0.8), Assert.IsType<BlurEffect>(copy.Effect).Radius, 6);
        Assert.Same(piece, Assert.IsType<VisualBrush>(copy.Fill).Visual);
    });

    [Fact]
    public void The_copy_follows_the_pieces_size() => Sta.Run(() =>
    {
        var piece = Piece();
        var host = DeskGlowLayer.Wrap(piece, 0.6);
        Render(host, 20);
        var brush = (VisualBrush)((Rectangle)host.Children[0]).Fill;
        Assert.Equal(new Rect(0, 0, piece.ActualWidth, piece.ActualHeight), brush.Viewbox);
        Assert.Equal(piece.ActualWidth, ((Rectangle)host.Children[0]).ActualWidth, 1);

        piece.Text = "1:02";
        Render(host, 20);
        Assert.Equal(new Rect(0, 0, piece.ActualWidth, piece.ActualHeight), brush.Viewbox);
    });

    [Fact]
    public void One_color_is_an_effect_on_the_piece_in_the_inks_middle_color() => Sta.Run(() =>
    {
        var piece = Piece();
        Assert.Same(piece, DeskGlowLayer.On(piece, Ink(0.8, oneColor: true)));
        var glow = Assert.IsType<DropShadowEffect>(piece.Effect);
        Assert.Equal(0, glow.ShadowDepth);
        Assert.Equal(DeskView.Rgb("3a63e0"), glow.Color);
        Assert.Equal(DeskGlow.Radius(0.8), glow.BlurRadius, 6);
        Assert.Equal(DeskGlow.Opacity(0.8), glow.Opacity, 6);
        Assert.Equal("clock", piece.Tag);
    });

    [Theory]
    [InlineData(new[] { "aaaaaa" }, "aaaaaa")]
    [InlineData(new[] { "aaaaaa", "bbbbbb" }, "bbbbbb")]
    [InlineData(new[] { "aaaaaa", "bbbbbb", "cccccc" }, "bbbbbb")]
    [InlineData(new string[0], "ffffff")]
    public void The_middle_color_of_an_ink(string[] stops, string want) => Assert.Equal(want, DeskGlowLayer.Middle(stops));

    [Fact]
    public void The_glow_spills_past_the_ink_and_a_gradient_glows_as_a_gradient() => Sta.Run(() =>
    {
        double pad = DeskGlow.Pad(1.0);
        var plain = Render(Piece(), pad);
        var lit = Render(DeskGlowLayer.Wrap(Piece(), 1.0), pad);
        Assert.Equal(plain.W, lit.W);
        Assert.Equal(plain.H, lit.H);

        // The pixels the glow adds: lit with it, clear without it. Premultiplied BGRA.
        long leftR = 0, leftG = 0, leftA = 0, rightR = 0, rightG = 0, rightA = 0;
        int added = 0, inked = 0;
        for (int y = 0; y < lit.H; y++)
            for (int x = 0; x < lit.W; x++)
            {
                int i = (y * lit.W + x) * 4;
                if (plain.Px[i + 3] > 8) { inked++; continue; }
                if (lit.Px[i + 3] <= 8) continue;
                added++;
                if (x < lit.W / 3) { leftR += lit.Px[i + 2]; leftG += lit.Px[i + 1]; leftA += lit.Px[i + 3]; }
                else if (x > lit.W * 2 / 3) { rightR += lit.Px[i + 2]; rightG += lit.Px[i + 1]; rightA += lit.Px[i + 3]; }
            }
        Assert.True(added > inked / 2, $"the glow added {added} pixels around {inked} of ink");
        Assert.True(leftA > 0 && rightA > 0, "the glow reaches both ends");
        // Violet (8f5bd6) on the left, cyan (33fdff) on the right: more red left, more green right.
        Assert.True((double)leftR / leftA > (double)rightR / rightA + 0.15, "the left of the glow is not redder than the right");
        Assert.True((double)rightG / rightA > (double)leftG / leftA + 0.15, "the right of the glow is not greener than the left");
    });

    [Fact]
    public void The_padding_holds_the_whole_glow() => Sta.Run(() =>
    {
        var (px, w, h) = Render(DeskGlowLayer.Wrap(Piece(), 1.0), DeskGlow.Pad(1.0));
        // Nothing reaches the bitmap's outermost row or column: the glow fades out inside its room.
        for (int x = 0; x < w; x++)
        {
            Assert.True(px[x * 4 + 3] <= 2, $"top edge lit at {x}");
            Assert.True(px[((h - 1) * w + x) * 4 + 3] <= 2, $"bottom edge lit at {x}");
        }
        for (int y = 0; y < h; y++)
        {
            Assert.True(px[y * w * 4 + 3] <= 2, $"left edge lit at {y}");
            Assert.True(px[(y * w + w - 1) * 4 + 3] <= 2, $"right edge lit at {y}");
        }
    });
}
```

Create `windows-dotnet/tests/Sanduhr.App.Tests/DeskGlowCostTests.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Sanduhr.App.Desk;
using Sanduhr.Core;
using Xunit.Abstractions;

namespace Sanduhr.App.Tests;

/// <summary>
/// What one copy of the clock's island costs under the desktop icons (5.0 spec 5, section 7): the under-icons host draws each island in
/// software into a bitmap, and a clock redraws every minute. The spec's budget is 8 ms at strength 1.0, measured on the owner's PC.
/// This test asserts nothing about time (another machine would give another answer); it prints the numbers, and the person running it
/// sets <see cref="DeskGlowLayer.UnderIconsOneColor"/> from them. Run it alone with a detailed logger to read them.
/// </summary>
public sealed class DeskGlowCostTests(ITestOutputHelper output)
{
    private const int Warmups = 3;
    private const int Runs = 21;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    private static DeskScreen Screen()
    {
        var bounds = new PxRect(0, 0, 1920, 1080);
        var work = new PxRect(0, 0, 1920, 1032);
        return new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0));
    }

    /// <summary>The clock's island as the Desk builds it at its standard size, with <paramref name="glow"/> applied to the clock.</summary>
    private static FrameworkElement Island(Func<FrameworkElement, FrameworkElement>? glow, double scale)
    {
        var settings = (DeskSettings.Default with { Enabled = true, Layout = "clock:bl", Font = "Segoe UI" }).Normalized();
        var view = new DeskView(Screen(), settings, new DeskFonts(["Segoe UI"]));
        var ink = DeskInk.Resolve(new DeskInkSettings(DeskInkMode.Custom, "8f5bd6,3a63e0,33fdff", false), null, null);
        view.Update(new DeskFrame { Now = new DateTimeOffset(2026, 11, 20, 20, 0, 0, TimeSpan.Zero) }, new DeskInkMap(ink));
        var stack = (Panel)view.TakeIslands().Single().Content;
        if (glow is not null)
        {
            var clock = (FrameworkElement)stack.Children[0];
            stack.Children.RemoveAt(0);
            stack.Children.Insert(0, glow(clock));
        }
        // The host scales the island's root to the monitor: the same transform here, so the bitmap has the monitor's pixels.
        stack.LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale);
        return stack;
    }

    private static double Median(Func<FrameworkElement> build, double pad)
    {
        var times = new List<double>();
        for (int i = 0; i < Warmups + Runs; i++)
        {
            var content = build();
            long t0 = Stopwatch.GetTimestamp();
            DeskGlowLayerTests.Render(content, pad);
            double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            if (i >= Warmups) times.Add(ms);
        }
        times.Sort();
        return times[times.Count / 2];
    }

    [Fact]
    public void The_clocks_island_is_timed_with_and_without_the_glow() => Sta.Run(() =>
    {
        double scale = GetDpiForSystem() / 96.0;
        double pad = DeskGlow.Pad(1.0) * scale;
        double none = Median(() => Island(null, scale), DeskGlow.BasePad * scale);
        double copy = Median(() => Island(c => DeskGlowLayer.Wrap(c, 1.0), scale), pad);
        double one = Median(() => Island(c => { c.Effect = DeskGlowLayer.OneColor("3a63e0", 1.0); return c; }, scale), pad);

        output.WriteLine($"GLOW COST at scale {scale:F2}, median of {Runs}: no glow {none:F2} ms, blurred copy {copy:F2} ms, one color {one:F2} ms.");
        output.WriteLine($"GLOW COST budget 8.00 ms: UnderIconsOneColor should be {(copy >= 8.0 ? "true" : "false")}; it is {DeskGlowLayer.UnderIconsOneColor}.");
        Assert.True(none > 0 && copy > 0 && one > 0);
    });
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskGlowLayerTests|FullyQualifiedName~DeskGlowCostTests"`
Expected: the build fails with "The name 'DeskGlowLayer' does not exist".

- [ ] **Step 3: Write the layer**

Create `windows-dotnet/src/Sanduhr.App/Desk/DeskGlowLayer.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>A Desk piece with its neon glow behind it (5.0 spec 5, section 7): child 0 is the blurred copy, child 1 the piece. It is as
/// big as the piece, carries the piece's tag (so the Desk's clicks find one piece, not two) and its alignment in the stack.</summary>
internal sealed class GlowHost : Grid;

/// <summary>
/// Draws the neon glow. The glow is a copy of the piece's own ink, blurred, under the piece: a rectangle the size of the piece, painted
/// with the piece through a <see cref="VisualBrush"/>, under a <see cref="BlurEffect"/>. So a gradient ink glows as a gradient, and
/// whatever the piece draws (a meter past its alert level, a song's look) glows in its own colors. A <see cref="DropShadowEffect"/>
/// would glow in one color only; that is kept as the cheaper stand-in (<see cref="OneColor"/>).
/// </summary>
internal static class DeskGlowLayer
{
    /// <summary>
    /// Under the desktop icons each island is drawn in software into a bitmap, and the blurred copy costs more there. True draws the
    /// glow in one color under the icons; the Desk above the icons always draws the copy. Set from the measurement in
    /// <c>DeskGlowCostTests</c> on the owner's PC against the spec's 8 ms budget. Measured: see the commit that last changed this line.
    /// </summary>
    public static readonly bool UnderIconsOneColor = true;

    private static bool _failed;

    /// <summary>The element to put where <paramref name="piece"/> was: the piece itself when the ink does not glow, the piece with a
    /// one-color glow as its effect, or the piece wrapped with its blurred copy. A failure to build the glow draws the piece without
    /// it and is written to the debug log once: a glow is never worth a missing piece.</summary>
    public static FrameworkElement On(FrameworkElement piece, ResolvedInk ink)
    {
        if (ink.Glow <= 0) return piece;
        try
        {
            if (ink.GlowOneColor)
            {
                piece.Effect = OneColor(Middle(ink.Stops), ink.Glow);
                return piece;
            }
            return Wrap(piece, ink.Glow);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            Failed(e);
            return piece;
        }
    }

    /// <summary>The piece with its blurred copy under it.</summary>
    public static GlowHost Wrap(FrameworkElement piece, double strength)
    {
        var host = new GlowHost { HorizontalAlignment = piece.HorizontalAlignment, Tag = piece.Tag };
        piece.Tag = null;
        piece.Effect = null;
        host.Children.Add(Copy(piece, strength));
        host.Children.Add(piece);
        return host;
    }

    /// <summary>
    /// The blurred copy of <paramref name="source"/>, to sit under it in a panel that gives both the same frame (a <see cref="Grid"/>
    /// sized by the source). The brush shows exactly the source's layout box, kept in step as the source changes size (the clock's
    /// minute, a meter's number), so the copy never drifts off the ink.
    /// </summary>
    public static Rectangle Copy(FrameworkElement source, double strength)
    {
        var brush = new VisualBrush(source)
        {
            Stretch = Stretch.Fill,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, source.ActualWidth, source.ActualHeight),
        };
        source.SizeChanged += (_, e) => brush.Viewbox = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
        return new Rectangle
        {
            Fill = brush,
            Opacity = DeskGlow.Opacity(strength),
            Effect = new BlurEffect { Radius = DeskGlow.Radius(strength), KernelType = KernelType.Gaussian },
            IsHitTestVisible = false,
        };
    }

    /// <summary>The one-color glow: a shadow with no offset in <paramref name="hex"/>, at the strength's radius and opacity.</summary>
    public static DropShadowEffect OneColor(string hex, double strength) => new()
    {
        Color = DeskView.Rgb(hex),
        ShadowDepth = 0,
        BlurRadius = DeskGlow.Radius(strength),
        Opacity = DeskGlow.Opacity(strength),
    };

    /// <summary>The ink's middle stop (the second of two, the second of three), white for an ink with none.</summary>
    public static string Middle(IReadOnlyList<string> stops) => stops.Count == 0 ? InkHex.Fallback : stops[stops.Count / 2];

    /// <summary>Records a glow that could not be built, once per run. The type only, never what the piece shows.</summary>
    public static void Failed(Exception e)
    {
        if (_failed) return;
        _failed = true;
        System.Diagnostics.Debug.WriteLine($"[Sanduhr] Desk glow failed: {e.GetType().Name}");
    }
}
```

- [ ] **Step 4: Run the layer's tests**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskGlowLayerTests"`
Expected: PASS. If `The_glow_spills_past_the_ink_and_a_gradient_glows_as_a_gradient` fails on the color comparison, print the six sums and check the gradient's direction before changing the test; the ink brush runs left to right.

- [ ] **Step 5: Measure**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskGlowCostTests" --logger "console;verbosity=detailed"`
Expected: PASS, with two lines starting `GLOW COST` in the output. Run it three times and take the middle "blurred copy" number.

- [ ] **Step 6: Set the switch from the measurement**

- Blurred copy under 8.00 ms: set `UnderIconsOneColor = false`.
- 8.00 ms or more: leave it `true`.

Either way, replace the last sentence of the field's doc comment with the numbers, for example: `Measured 2026-11-21 on the owner's PC at scale 1.50: no glow 1.9 ms, blurred copy 11.4 ms, one color 4.2 ms.` If the one-color number is itself over 8 ms, keep `true` and say so in the commit message; the spec names no third path, and the hand check in Task 13 decides whether the glow under the icons is acceptable.

Record the ruling in the ledger: `Ruling: UnderIconsOneColor = <value> — measured <numbers> against the spec's 8 ms — if wrong, the Desk under the icons either costs more per redraw than budgeted or glows in one color when it could have glowed as a gradient; one line to flip.`

- [ ] **Step 7: Run every suite and commit**

```bash
cd windows-dotnet && dotnet test
git add src/Sanduhr.App/Desk/DeskGlowLayer.cs tests/Sanduhr.App.Tests/DeskGlowLayerTests.cs tests/Sanduhr.App.Tests/DeskGlowCostTests.cs
git commit -m "feat(windows): the glow layer, measured under the desktop icons

A blurred copy of a piece's ink behind it, and a one-color stand-in.
Clock island capture at scale S: no glow A ms, blurred copy B ms,
one color C ms. UnderIconsOneColor = V.

Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>"
```

---

### Task 5: The Desk draws the glow

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskView.cs` (`TakeIslands` near line 268, `Piece` at 519-523, `NowPlaying` at 908-941, `LauncherPiece` at 1101-1105)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskMessagePiece.cs:298-310` (`Line`)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/IDeskHost.cs` (`DeskIsland`)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DesktopLayerHost.cs` (`Pad` at 44-48, `Place` at 251, `Follow` at 346, `Drifted` at 407-414, `SetDrift` at 427-443, `CloseWindows` at 462-480, and where `Fit` calls `Drifted` near 382)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskController.cs` (`Care` at 181)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DeskGlowViewTests.cs` (new)

**Interfaces:**
- Consumes: `DeskGlowLayer.On`, `Copy`, `OneColor`, `Middle`, `UnderIconsOneColor`, `GlowHost` (Task 4); `ResolvedInk.Glow`, `GlowOneColor`, `DeskInk.Glowing`, `DeskGlow.Pad` (Task 1).
- Produces:
  - `DeskIsland.Pad` (`double`, init-only, default `DeskGlow.BasePad`): the room the host leaves around the island.
  - `internal static ResolvedInk DeskController.Lit(ResolvedInk ink, DeskInkSettings own, bool oneColor)`.

What glows, and how:

| Piece | How |
|---|---|
| clock, claude, meters, telemetry, weather, watchers, and a piece's name in Arrange Desk | `DeskGlowLayer.On` around the piece |
| now playing | `DeskGlowLayer.On` around its line and position bar, inside its `ReflowHost`. The audio bars beside it stay out, as they stay out of the shadow: they redraw twenty times a second |
| message | Each line gets `DeskGlowLayer.Copy` under it, in the line's own colors. A `{noglow}` line gets nothing |
| launcher | Labels get `DeskGlowLayer.OneColor` in the ink's middle color through the effect slot they already have. The buttons' geometry is fixed, and a wrapper would move them. Icons do not glow |
| glow spot, companion, drawers | Nothing |

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/DeskGlowViewTests.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Sanduhr.App.Desk;
using Sanduhr.Core;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Sanduhr.App.Tests;

/// <summary>The neon glow on the Desk itself (5.0 spec 5, section 7): which pieces glow and how, the room an island leaves for it, and
/// that the Desk without it draws exactly as before.</summary>
public sealed class DeskGlowViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static DeskScreen Screen()
    {
        var bounds = new PxRect(0, 0, 1920, 1080);
        var work = new PxRect(0, 0, 1920, 1032);
        return new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0));
    }

    private static ResolvedInk Glowing(double strength = 0.8) =>
        DeskInk.Resolve(new DeskInkSettings(DeskInkMode.Custom, "8f5bd6,3a63e0,33fdff", false, true, strength), null, null);

    private static DeskView View(string layout, DeskFrame frame, ResolvedInk ink)
    {
        var settings = (DeskSettings.Default with { Enabled = true, Layout = layout, Font = "Segoe UI" }).Normalized();
        var view = new DeskView(Screen(), settings, new DeskFonts(["Segoe UI"]));
        view.Update(frame, new DeskInkMap(ink));
        return view;
    }

    private static IEnumerable<T> All<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T hit) yield return hit;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var found in All<T>(child))
                yield return found;
    }

    private static DeskUsage Usage()
    {
        string iso = Now.AddHours(2).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", System.Globalization.CultureInfo.InvariantCulture);
        return new DeskUsage([new DeskTier(TierModel.FiveHour, 42, iso)], Now.AddMinutes(-1), false, false, null);
    }

    private static bool IsShadow(Effect? e) => e is DropShadowEffect { ShadowDepth: > 0 };

    [Fact]
    public void A_glowing_ink_puts_a_blurred_copy_behind_each_piece_and_draws_no_shadow() => Sta.Run(() =>
    {
        var view = View("clock:bl claude:bl meters:tr", new DeskFrame { Usage = Usage(), Now = Now }, Glowing());
        var hosts = All<GlowHost>(view).ToList();

        Assert.Equal(["claude", "clock", "meters"], hosts.Select(h => (string)h.Tag).Order());
        foreach (var host in hosts)
        {
            var copy = Assert.IsType<Rectangle>(host.Children[0]);
            Assert.Equal(DeskGlow.Radius(0.8), Assert.IsType<BlurEffect>(copy.Effect).Radius, 6);
            Assert.Equal(DeskGlow.Opacity(0.8), copy.Opacity, 6);
        }
        Assert.DoesNotContain(All<FrameworkElement>(view), e => IsShadow(e.Effect));
        // The frames the Desk reports are the wrappers', which are the pieces' size.
        Assert.Equal(3, view.PieceFrames().Count);
        view.Dispose();
    });

    [Fact]
    public void Without_the_glow_the_Desk_draws_as_it_always_has() => Sta.Run(() =>
    {
        var view = View("clock:bl claude:bl", new DeskFrame { Usage = Usage(), Now = Now }, DeskInkMap.Light.Default);
        Assert.Empty(All<GlowHost>(view));
        Assert.Empty(All<Rectangle>(view).Where(r => r.Fill is VisualBrush));
        var clock = All<FrameworkElement>(view).Single(e => (e.Tag as string) == "clock");
        Assert.True(IsShadow(clock.Effect));
        Assert.Equal(DeskGlow.BasePad, view.TakeIslands().Single().Pad);
        view.Dispose();
    });

    [Fact]
    public void The_one_color_hint_draws_one_color_and_no_copy() => Sta.Run(() =>
    {
        var view = View("clock:bl", new DeskFrame { Now = Now }, Glowing() with { GlowOneColor = true });
        Assert.Empty(All<GlowHost>(view));
        var clock = All<FrameworkElement>(view).Single(e => (e.Tag as string) == "clock");
        var glow = Assert.IsType<DropShadowEffect>(clock.Effect);
        Assert.Equal(0, glow.ShadowDepth);
        Assert.Equal(DeskView.Rgb("3a63e0"), glow.Color);
        view.Dispose();
    });

    [Theory]
    [InlineData(0.2)]
    [InlineData(0.6)]
    [InlineData(1.0)]
    public void An_island_leaves_room_for_the_glow(double strength) => Sta.Run(() =>
    {
        var view = View("clock:bl meters:tr", new DeskFrame { Usage = Usage(), Now = Now }, Glowing(strength));
        Assert.All(view.TakeIslands(), i => Assert.Equal(DeskGlow.Pad(strength), i.Pad, 6));
        view.Dispose();
    });

    [Fact]
    public void There_is_one_clock_for_the_Desks_clicks_to_find() => Sta.Run(() =>
    {
        var view = View("clock:bl", new DeskFrame { Now = Now }, Glowing());
        Assert.IsType<GlowHost>(All<FrameworkElement>(view).Single(e => (e.Tag as string) == "clock"));
        view.Dispose();
    });

    [Fact]
    public void A_message_line_glows_in_its_own_copy_and_a_noglow_line_does_not() => Sta.Run(() =>
    {
        var settings = DeskMessageSettings.Default with { Glow = false };
        var lit = View("message:bl", new DeskFrame { Now = Now, Message = new DeskMessageFrame(new MessageToday(usual: "keep building."), settings) }, Glowing());
        var copy = Assert.Single(All<Rectangle>(lit).Where(r => r.Fill is VisualBrush));
        Assert.IsType<BlurEffect>(copy.Effect);
        Assert.DoesNotContain(All<TextBlock>(lit), t => t.Effect is DropShadowEffect);
        lit.Dispose();

        var plain = View("message:bl", new DeskFrame { Now = Now, Message = new DeskMessageFrame(new MessageToday(usual: "{noglow}keep building."), settings) }, Glowing());
        Assert.Empty(All<Rectangle>(plain).Where(r => r.Fill is VisualBrush));
        Assert.DoesNotContain(All<TextBlock>(plain), t => t.Effect is not null);
        plain.Dispose();
    });

    [Fact]
    public void A_message_under_the_icons_glows_in_one_color() => Sta.Run(() =>
    {
        var settings = DeskMessageSettings.Default with { Glow = false };
        var view = View("message:bl", new DeskFrame { Now = Now, Message = new DeskMessageFrame(new MessageToday(usual: "keep building."), settings) },
            Glowing() with { GlowOneColor = true });
        Assert.Empty(All<Rectangle>(view).Where(r => r.Fill is VisualBrush));
        var glow = Assert.IsType<DropShadowEffect>(All<TextBlock>(view).Single(t => t.Text == "keep building.").Effect);
        Assert.Equal(0, glow.ShadowDepth);
        Assert.Equal(DeskGlow.Radius(0.8), glow.BlurRadius, 6);
        view.Dispose();
    });

    [Fact]
    public void Now_playing_glows_inside_its_reflow_host() => Sta.Run(() =>
    {
        var frame = new DeskFrame { Now = Now, Track = new NowPlayingInfo { Title = "Song", Artist = "Band" } };
        var view = View("nowPlaying:bl", frame, Glowing());
        var host = Assert.Single(All<DeskView.ReflowHost>(view));
        Assert.Single(All<GlowHost>(host));
        // The stack still holds the reflow host itself, so the piece can fade and give its height back.
        Assert.IsType<StackPanel>(host.Parent);
        view.Dispose();
    });

    [Fact]
    public void A_launcher_label_glows_in_one_color_and_its_icon_does_not() => Sta.Run(() =>
    {
        var model = new LauncherModel(LauncherOrientation.Row, [LauncherButton.Create(LauncherKind.Link, "https://example.com", label: "Home")!]);
        var launcher = new DeskLauncherFrame(model, new HashSet<string>(), new Dictionary<string, ImageSource>(), new Dictionary<string, string>(), new Dictionary<string, ImageSource>());
        var view = View("launcher:br", new DeskFrame { Now = Now, Launcher = launcher }, Glowing());
        Assert.Empty(All<GlowHost>(view));
        var label = All<TextBlock>(view).Single(t => t.Effect is DropShadowEffect);
        var glow = (DropShadowEffect)label.Effect;
        Assert.Equal(0, glow.ShadowDepth);
        Assert.Equal(DeskView.Rgb("3a63e0"), glow.Color);
        view.Dispose();
    });

    [Fact]
    public void The_switch_reaches_an_ink_the_Desk_did_not_make_and_marks_it_for_under_the_icons()
    {
        var own = DeskInkSettings.Default.WithGlow(true).WithGlowStrength(0.8);
        var followed = DeskInk.Resolve(new DeskInkSettings(DeskInkMode.Custom, "33fdff", true), null, null);

        var lit = DeskController.Lit(followed, own, oneColor: false);
        Assert.Equal(0.8, lit.Glow);
        Assert.Equal(0, lit.ShadowOpacity);
        Assert.False(lit.GlowOneColor);

        Assert.True(DeskController.Lit(followed, own, oneColor: true).GlowOneColor);
        // No glow, no hint: an ink without the glow is the ink it was.
        Assert.Same(followed, DeskController.Lit(followed, DeskInkSettings.Default, oneColor: true));
    }
}
```

`All<DeskView.ReflowHost>` needs `ReflowHost` to be reachable from the test assembly; it is already `internal sealed class ReflowHost` inside `DeskView`, and the App's `InternalsVisibleTo` already covers the test project.

- [ ] **Step 2: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskGlowViewTests"`
Expected: the build fails with "'DeskIsland' does not contain a definition for 'Pad'" and "'DeskController' does not contain a definition for 'Lit'".

- [ ] **Step 3: The island's room**

In `windows-dotnet/src/Sanduhr.App/Desk/IDeskHost.cs`, add to `DeskIsland`, after `Widgets`:

```csharp
    /// <summary>The room the host leaves around the island, in DIPs: for the ink's shadow, and more while the neon glow is on
    /// (<see cref="DeskGlow.Pad"/>), so the glow is not clipped by the island's window. A Glass drawer has none whatever this says.</summary>
    public double Pad { get; init; } = DeskGlow.BasePad;
```

In `windows-dotnet/src/Sanduhr.App/Desk/DeskView.cs`, in `TakeIslands`, replace

```csharp
            islands.Add(new DeskIsland(content, new Point(x, y), h, v) { Pause = pause, Widgets = widgets });
```

with

```csharp
            // The glow is one switch for the whole Desk, so every ink of this view carries the same strength.
            islands.Add(new DeskIsland(content, new Point(x, y), h, v) { Pause = pause, Widgets = widgets, Pad = DeskGlow.Pad(_inks.Default.Glow) });
```

The roaming companion's island a few lines below keeps the default: the companion does not glow.

In `windows-dotnet/src/Sanduhr.App/Desk/DesktopLayerHost.cs`:

1. Replace the `Pad` constant and `PadOf` with:

```csharp
    /// <summary>The room around an island for the ink's shadow or glow (<see cref="DeskIsland.Pad"/>); none for a Glass drawer, whose blur
    /// fills the whole window and so must be the panel's size.</summary>
    private static double PadOf(DeskIsland island) => island is { Glass: true, Drawer: true } ? 0 : island.Pad;

    /// <summary>The room each window was laid out with, for OLED care's drift, which holds the content (the window less its room).</summary>
    private readonly Dictionary<HwndSource, double> _pads = [];
```

2. In `Place`, after `double pad = PadOf(island);` add `_pads[src] = pad;`.
3. Change `Drifted` to take the room:

```csharp
    private PxRect Drifted(PxRect home, DeskScreen screen, double padDips)
    {
        if (_drift is not { } step) return home;
        int pad = (int)Math.Round(padDips * screen.Scale);
```

   and leave the rest of its body as it is.
4. Its two callers pass the window's own room. In `Fit`: `var at = IsDrawer(src) ? home : Drifted(home, screen, _pads.GetValueOrDefault(src, DeskGlow.BasePad));`. In `SetDrift`: `var at = Drifted(h.Home, h.Screen, _pads.GetValueOrDefault(w, DeskGlow.BasePad));`.
5. In `CloseWindows`, beside `_homes.Clear();` add `_pads.Clear();`.
6. Update the doc comment on `Drifted`: "the window less its shadow room" becomes "the window less the room around it".

- [ ] **Step 4: The pieces**

In `DeskView.Piece`, replace

```csharp
        // The message carries its own glow (spec 6.2) and the glow's spot is clear at rest; every other
        // piece takes the ink's shadow.
        if (p.Widget is not ("message" or "nowPlaying" or "glow" or "launcher" or "companion") && _ink.ShadowOpacity > 0)
            piece.Effect = Shadow(_ink);
        return piece;
```

with

```csharp
        // The message carries its own glow (spec 6.2) and the glow's spot is clear at rest; every other piece takes the
        // neon glow (5.0 spec 5) or, without it, the ink's shadow. The Desk draws one or the other.
        if (p.Widget is not ("message" or "nowPlaying" or "glow" or "launcher" or "companion"))
        {
            if (_ink.Glow > 0) piece = DeskGlowLayer.On(piece, _ink);
            else if (_ink.ShadowOpacity > 0) piece.Effect = Shadow(_ink);
        }
        return piece;
```

In `DeskView.NowPlaying`, replace

```csharp
        if (_ink.ShadowOpacity > 0) column.Effect = Shadow(_ink);
```

with

```csharp
        // The neon glow takes the same part: the line and its bar.
        FrameworkElement lit = column;
        if (_ink.Glow > 0) lit = DeskGlowLayer.On(column, _ink);
        else if (_ink.ShadowOpacity > 0) column.Effect = Shadow(_ink);
```

and, further down in the same method, use `lit` wherever `column` is placed into the piece: `FrameworkElement content = lit;`, and in the bars branch `row.Children.Add(lit)` in both arms (twice) where it now adds `column`. `column` keeps its `Width`; the wrapper sizes to it.

In `DeskView.LauncherPiece`, replace `_ink.ShadowOpacity > 0 ? Shadow(_ink) : null` with

```csharp
                _ink.Glow > 0 ? DeskGlowLayer.OneColor(DeskGlowLayer.Middle(_ink.Stops), _ink.Glow) : _ink.ShadowOpacity > 0 ? Shadow(_ink) : null
```

and extend that method's doc comment: "The labels take the ink's shadow, or the neon glow in one color; the images neither."

- [ ] **Step 5: The message**

In `windows-dotnet/src/Sanduhr.App/Desk/DeskMessagePiece.cs`, in `Line`, replace

```csharp
        if (MessageMotion.Glows(e, s.Glow))
            block.Effect = new DropShadowEffect
            {
                Color = DeskView.Rgb(glowHex ?? stops[stops.Count / 2]),
                ShadowDepth = 0,
                BlurRadius = DeskMessageLook.GlowBlur,
                Opacity = DeskMessageLook.GlowOpacity,
            };
        else if (ink.ShadowOpacity > 0)
            block.Effect = DeskView.Shadow(ink);

        var host = new Grid { HorizontalAlignment = align };
        host.Children.Add(block);
```

with

```csharp
        // Neon glow (5.0 spec 5) covers the message: each line glows in its own colors, drawn as every piece's glow is.
        // A line that says {noglow} still wins, and then it has no shadow either, since the Desk draws no shadow while glowing.
        bool neon = ink.Glow > 0 && e.Glow != false;
        Rectangle? copy = null;
        if (neon)
        {
            try
            {
                if (ink.GlowOneColor) block.Effect = DeskGlowLayer.OneColor(glowHex ?? stops[stops.Count / 2], ink.Glow);
                else copy = DeskGlowLayer.Copy(block, ink.Glow);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
            {
                DeskGlowLayer.Failed(ex);
            }
        }
        else if (ink.Glow <= 0 && MessageMotion.Glows(e, s.Glow))
            block.Effect = new DropShadowEffect
            {
                Color = DeskView.Rgb(glowHex ?? stops[stops.Count / 2]),
                ShadowDepth = 0,
                BlurRadius = DeskMessageLook.GlowBlur,
                Opacity = DeskMessageLook.GlowOpacity,
            };
        else if (ink.ShadowOpacity > 0)
            block.Effect = DeskView.Shadow(ink);

        var host = new Grid { HorizontalAlignment = align };
        if (copy is not null) host.Children.Add(copy);
        host.Children.Add(block);
```

Add `using System.Windows.Shapes;` to the file if `Rectangle` does not resolve, or write `System.Windows.Shapes.Rectangle? copy`. If a later line in this method addresses `host.Children[0]` as the text block, change it to use `block` directly.

- [ ] **Step 6: The controller**

In `windows-dotnet/src/Sanduhr.App/Desk/DeskController.cs`, replace

```csharp
    private ResolvedInk Care(ResolvedInk ink) => _oledOn ? DeskInk.CapWhite(ink) : ink;
```

with

```csharp
    /// <summary>Every ink the Desk draws passes through here: the neon glow switch (so it reaches a followed widget theme's ink too),
    /// the one-color hint for the Desk under the icons, and OLED care's white cap.</summary>
    private ResolvedInk Care(ResolvedInk ink)
    {
        ink = Lit(ink, _ink, GlowOneColorNow);
        return _oledOn ? DeskInk.CapWhite(ink) : ink;
    }

    /// <summary>The glow switch of the Desk's own ink setting put on <paramref name="ink"/>, and, when it glows and
    /// <paramref name="oneColor"/> says so, the hint to draw that glow in one color.</summary>
    internal static ResolvedInk Lit(ResolvedInk ink, DeskInkSettings own, bool oneColor)
    {
        var lit = DeskInk.Glowing(ink, own);
        return lit.Glow > 0 && oneColor ? lit with { GlowOneColor = true } : lit;
    }

    /// <summary>The Desk draws under the desktop icons and the measurement said the blurred copy costs too much there.</summary>
    private bool GlowOneColorNow =>
        DeskGlowLayer.UnderIconsOneColor && _hostUnderIcons && UnderIcons.UnderIconsHost.Unavailable is null;
```

`Care` keeps its place (line 181); if the compiler reports the old expression-bodied member is still referenced elsewhere, there is only the one definition to replace.

- [ ] **Step 7: Run the tests**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskGlow|FullyQualifiedName~DeskRideAlongs|FullyQualifiedName~DeskThemeLook|FullyQualifiedName~DeskMessage|FullyQualifiedName~Launcher|FullyQualifiedName~NowPlaying|FullyQualifiedName~UnderIcons|FullyQualifiedName~Oled"`
Expected: PASS. The older suites prove the Desk without the glow is unchanged.

- [ ] **Step 8: Run every suite and commit**

```bash
cd windows-dotnet && dotnet test
git add src/Sanduhr.App/Desk tests/Sanduhr.App.Tests/DeskGlowViewTests.cs
git commit -m "feat(windows): the Desk draws the neon glow

Each piece that takes the ink gets a blurred copy of it behind; the
island's window grows to hold it; the switch covers a followed theme.

Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>"
```

---

### Task 6: Desk Look gets Neon glow and its strength

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (beside `DropShadow`, line 1169)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs:324-326` (the `InkAndColor` entry's words)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs` (the ink members at 381-390, `LoadInk` at 501-509, `OnInkShadowChanged` at 568)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/DeskMessagePageViewModel.cs` (`RefreshSettings` at 456, `Glow` at 814-818, the notify list near 870)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (after line 283)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/DeskLookPage.xaml` (after line 182, and the check box at 216-218)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DeskGlowSettingsTests.cs` (new)

**Interfaces:**
- Consumes: `DeskInkSettings.WithShadow`, `WithGlow`, `WithGlowStrength` (Task 1); `DeskGlowLayer.UnderIconsOneColor` (Task 4).
- Produces:
  - `SettingsNames.NeonGlow = "Neon glow"`, `GlowStrength = "Glow strength"`, `NeonGlowCaption`, `CoveredByNeonGlow = "Covered by Neon glow."`, `GlowUnderIconsOneColor = "Under the desktop icons the glow is one color."`
  - `DeskSettingsViewModel.InkGlow` (`bool`), `InkGlowPercent` (`double`, 20 to 100), `InkGlowPercentText` (`string`, "60%"), `GlowUnderIconsNote` (`string?`), `event Action? InkGlowChanged`.
  - `DeskMessagePageViewModel.SetNeonGlow(bool)`, `GlowShown` (`bool`), `GlowEnabled` (`bool`), `GlowCoveredNote` (`string?`).

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/DeskGlowSettingsTests.cs`:

```csharp
using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings, Desk Look, Ink and color (5.0 spec 5, section 7): Neon glow and the drop shadow exclude each other, the strength
/// is a percentage, and the message's own glow shows as covered while Neon glow is on.</summary>
public sealed class DeskGlowSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-glow-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    public DeskGlowSettingsTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        _store = new SettingsStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private DeskSettingsViewModel Desk() => new(_store, ["Segoe UI"]);

    [Fact]
    public void A_fresh_Desk_has_the_shadow_and_no_glow() => Sta.Run(() =>
    {
        var vm = Desk();
        Assert.True(vm.InkShadow);
        Assert.False(vm.InkGlow);
        Assert.Equal(60, vm.InkGlowPercent);
        Assert.Equal("60%", vm.InkGlowPercentText);
    });

    [Fact]
    public void Turning_one_on_turns_the_other_off_and_saves_it() => Sta.Run(() =>
    {
        var vm = Desk();
        int changes = 0;
        vm.Changed = () => changes++;

        vm.InkGlow = true;
        Assert.False(vm.InkShadow);
        Assert.True(_store.LoadDeskInk().Glow);
        Assert.False(_store.LoadDeskInk().Shadow);

        vm.InkShadow = true;
        Assert.False(vm.InkGlow);
        Assert.False(_store.LoadDeskInk().Glow);
        Assert.True(_store.LoadDeskInk().Shadow);
        Assert.Equal(2, changes);
    });

    [Fact]
    public void Turning_one_off_leaves_the_other_off() => Sta.Run(() =>
    {
        var vm = Desk();
        vm.InkShadow = false;
        Assert.False(vm.InkGlow);
        vm.InkGlow = true;
        vm.InkGlow = false;
        Assert.False(vm.InkShadow);
    });

    [Fact]
    public void The_strength_is_a_percentage_and_is_kept() => Sta.Run(() =>
    {
        var vm = Desk();
        vm.InkGlow = true;
        vm.InkGlowPercent = 85;
        Assert.Equal(0.85, _store.LoadDeskInk().GlowStrength);
        Assert.Equal("85%", vm.InkGlowPercentText);

        var again = Desk();
        Assert.True(again.InkGlow);
        Assert.Equal(85, again.InkGlowPercent);
    });

    [Fact]
    public void The_glow_tells_whoever_listens() => Sta.Run(() =>
    {
        var vm = Desk();
        var heard = new List<bool>();
        vm.InkGlowChanged += () => heard.Add(vm.InkGlow);
        vm.InkGlow = true;
        vm.InkShadow = true;   // which turns the glow off
        Assert.Equal([true, false], heard);
    });

    [Fact]
    public void The_one_color_note_shows_only_when_it_is_true()
    {
        Sta.Run(() =>
        {
            _store.SaveDeskUnderIcons(false);
            var vm = Desk();
            vm.InkGlow = true;
            Assert.Null(vm.GlowUnderIconsNote);   // the Desk is above the icons
        });
        Sta.Run(() =>
        {
            _store.SaveDeskUnderIcons(true);
            var vm = Desk();
            Assert.Null(vm.GlowUnderIconsNote);   // no glow, nothing to say
            vm.InkGlow = true;
            Assert.Equal(Sanduhr.App.Desk.DeskGlowLayer.UnderIconsOneColor ? SettingsNames.GlowUnderIconsOneColor : null, vm.GlowUnderIconsNote);
        });
    }

    [Fact]
    public void While_neon_glow_is_on_the_messages_own_glow_shows_on_and_cannot_be_changed() => Sta.Run(() =>
    {
        var page = new DeskMessagePageViewModel(_store, Path.Combine(_paths.AppDataDir, "messages.txt"), () => true, () => { }, ["Segoe UI"]);
        page.Glow = false;
        Assert.False(page.GlowShown);
        Assert.True(page.GlowEnabled);
        Assert.Null(page.GlowCoveredNote);

        var raised = new List<string?>();
        page.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        page.SetNeonGlow(true);
        Assert.True(page.GlowShown);
        Assert.False(page.GlowEnabled);
        Assert.Equal(SettingsNames.CoveredByNeonGlow, page.GlowCoveredNote);
        Assert.Contains(nameof(page.GlowShown), raised);
        Assert.Contains(nameof(page.GlowEnabled), raised);
        Assert.Contains(nameof(page.GlowCoveredNote), raised);

        page.GlowShown = false;   // a stray write while covered changes nothing
        Assert.False(page.Glow);
        Assert.True(page.GlowShown);

        page.SetNeonGlow(false);
        Assert.False(page.GlowShown);   // the message's own setting was never touched
        page.Dispose();
    });

    [Fact]
    public void The_message_page_reads_the_glow_when_it_refreshes() => Sta.Run(() =>
    {
        var page = new DeskMessagePageViewModel(_store, Path.Combine(_paths.AppDataDir, "messages.txt"), () => true, () => { }, ["Segoe UI"]);
        _store.SaveDeskInk(DeskInkSettings.Default.WithGlow(true));
        page.RefreshSettings();
        Assert.Equal(SettingsNames.CoveredByNeonGlow, page.GlowCoveredNote);
        page.Dispose();
    });

    [Fact]
    public void The_page_has_the_switch_the_slider_and_the_covered_line()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Views", "DeskLookPage.xaml")))
            dir = dir.Parent;
        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Sanduhr.App", "Views", "DeskLookPage.xaml"));

        Assert.Contains("IsChecked=\"{Binding InkGlow, Mode=TwoWay}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"{x:Static core:SettingsNames.NeonGlow}\"", xaml);
        Assert.Contains("Value=\"{Binding InkGlowPercent, Mode=TwoWay}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"{x:Static core:SettingsNames.GlowStrength}\"", xaml);
        Assert.Contains("IsChecked=\"{Binding GlowShown, Mode=TwoWay}\"", xaml);
        Assert.Contains("IsEnabled=\"{Binding GlowEnabled}\"", xaml);
        Assert.Contains("{Binding GlowCoveredNote}", xaml);
        Assert.Contains("{Binding GlowUnderIconsNote}", xaml);
        // The glow is one switch for the whole Desk: it is not inside the block that greys out while the Desk follows the theme.
        int glow = xaml.IndexOf("{Binding InkGlow, Mode=TwoWay}", StringComparison.Ordinal);
        int shadow = xaml.IndexOf("{Binding InkShadow, Mode=TwoWay}", StringComparison.Ordinal);
        int closeOfInkBlock = xaml.IndexOf("</StackPanel>", shadow, StringComparison.Ordinal);
        Assert.True(glow > closeOfInkBlock, "Neon glow sits inside the block that is disabled while following the theme");
    }

    [Fact]
    public void Settings_search_finds_the_glow()
    {
        Assert.Contains(SettingsIndex.Search("neon glow"), h => h.Entry.Page == SettingsPage.DeskLook);
        Assert.Contains(SettingsIndex.Search("glow strength"), h => h.Entry.Page == SettingsPage.DeskLook);
    }
}
```

`SettingsIndex.Search(string?)` returns `IReadOnlyList<SettingsIndex.Hit>`, and a `Hit` holds its `Entry`. `DeskSettingsViewModel.Changed` is a settable `Action?` property.

- [ ] **Step 2: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskGlowSettingsTests"`
Expected: the build fails with "'DeskSettingsViewModel' does not contain a definition for 'InkGlow'".

- [ ] **Step 3: The names and the index**

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, after `DropShadow`, add:

```csharp
    public const string NeonGlow = "Neon glow";
    public const string GlowStrength = "Glow strength";
    public const string NeonGlowCaption = "A soft light behind the Desk's text and meters, in the ink's own colors. It takes the place of the drop shadow, and it stays on while the Desk follows the widget theme.";
    public const string CoveredByNeonGlow = "Covered by Neon glow.";
    public const string GlowUnderIconsOneColor = "Under the desktop icons the glow is one color.";
```

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`, in the `SettingsNames.InkAndColor` entry's word list, after `SettingsNames.DropShadow,` add `SettingsNames.NeonGlow, SettingsNames.GlowStrength, "neon", "glow",`.

- [ ] **Step 4: The Desk view model**

In `windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs`, after `[ObservableProperty] private bool _inkShadow = true;` add:

```csharp
    [ObservableProperty] private bool _inkGlow;

    /// <summary>The glow's strength as a percentage, 20 to 100: what the slider moves and what a screen reader says.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InkGlowPercentText))]
    private double _inkGlowPercent = DeskGlow.DefaultStrength * 100;

    public string InkGlowPercentText => Math.Round(InkGlowPercent).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";

    /// <summary>Neon glow was switched (by its own box, or off by the shadow's): the message's switch follows.</summary>
    public event Action? InkGlowChanged;

    /// <summary>The line beside the switch when the Desk under the desktop icons draws the glow in one color; null otherwise.</summary>
    public string? GlowUnderIconsNote =>
        InkGlow && UnderIcons && Sanduhr.App.Desk.DeskGlowLayer.UnderIconsOneColor
        && Sanduhr.App.Desk.UnderIcons.UnderIconsHost.Unavailable is null
            ? SettingsNames.GlowUnderIconsOneColor : null;
```

In `LoadInk`, after `InkShadow = _ink.Shadow;` add:

```csharp
        InkGlow = _ink.Glow;
        InkGlowPercent = Math.Round(_ink.GlowStrength * 100);
```

Replace `partial void OnInkShadowChanged(bool value) => SaveInk(_ink with { Shadow = value });` with:

```csharp
    partial void OnInkShadowChanged(bool value)
    {
        if (_loadingInk) return;
        SaveInk(_ink.WithShadow(value));
        SyncInkSwitches();
    }

    partial void OnInkGlowChanged(bool value)
    {
        if (_loadingInk) return;
        SaveInk(_ink.WithGlow(value));
        SyncInkSwitches();
    }

    partial void OnInkGlowPercentChanged(double value)
    {
        if (_loadingInk) return;
        SaveInk(_ink.WithGlowStrength(value / 100));
    }

    /// <summary>Glow and shadow are either-or: after one is switched, both boxes show what was saved.</summary>
    private void SyncInkSwitches()
    {
        _loadingInk = true;
        InkShadow = _ink.Shadow;
        InkGlow = _ink.Glow;
        _loadingInk = false;
        OnPropertyChanged(nameof(GlowUnderIconsNote));
        if (_glowTold == _ink.Glow) return;
        _glowTold = _ink.Glow;
        InkGlowChanged?.Invoke();
    }

    /// <summary>The glow's value the listeners last heard, so a switch is told once.</summary>
    private bool _glowTold;
```

`SyncInkSwitches` raises `InkGlowChanged` whenever the saved glow differs from the last value told, which covers both a tick of the glow's own box and the shadow's box switching the glow off. In `LoadInk`, after the two lines added above, add `_glowTold = _ink.Glow;`. In the `UnderIcons` setter (line 484-495), after `OnPropertyChanged(nameof(UnderIconsNote));` add `OnPropertyChanged(nameof(GlowUnderIconsNote));`.

- [ ] **Step 5: The message page**

In `windows-dotnet/src/Sanduhr.App/ViewModels/DeskMessagePageViewModel.cs`, after the `Glow` property (line 814-818), add:

```csharp
    private bool _neonGlow;

    /// <summary>Neon glow (Desk Look) is on: it covers the message, so the message's own switch shows on and cannot be changed.
    /// The message's own setting is not touched, and shows again when Neon glow goes off.</summary>
    public void SetNeonGlow(bool on)
    {
        if (_neonGlow == on) return;
        _neonGlow = on;
        OnPropertyChanged(nameof(GlowShown));
        OnPropertyChanged(nameof(GlowEnabled));
        OnPropertyChanged(nameof(GlowCoveredNote));
    }

    /// <summary>What the "Glow around the message" box shows: on while Neon glow covers it, else the message's own setting.</summary>
    public bool GlowShown
    {
        get => _neonGlow || Glow;
        set
        {
            if (!_neonGlow) Glow = value;
        }
    }

    public bool GlowEnabled => OwnColorsEnabled && !_neonGlow;

    public string? GlowCoveredNote => _neonGlow ? SettingsNames.CoveredByNeonGlow : null;
```

At the start of `RefreshSettings`, add `_neonGlow = _store.LoadDeskInk().Glow;`. In the list of property names this class raises together (near line 870, the one that holds `nameof(Glow)` and `nameof(OwnColorsEnabled)`), add `nameof(GlowShown), nameof(GlowEnabled), nameof(GlowCoveredNote)`.

In `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`, after `MessagePage.Piece = Desk.PieceStatus("message");` add:

```csharp
        // Neon glow covers the message's own glow (5.0 spec 5): the message's box on Desk Look follows the switch.
        Desk.InkGlowChanged += () => MessagePage.SetNeonGlow(Desk.InkGlow);
        MessagePage.SetNeonGlow(Desk.InkGlow);
```

- [ ] **Step 6: The page**

In `windows-dotnet/src/Sanduhr.App/Views/DeskLookPage.xaml`, right after the closing `</StackPanel>` of the block that holds the Ink picker, the Colors row and the Drop shadow box (the one whose `IsEnabled` binds `ThemeMatch.NotFollowing`, closing at line 182), add:

```xml
                    <!-- Neon glow (5.0 spec 5): one switch for the whole Desk, so it is not greyed out while the Desk follows the widget theme. -->
                    <StackPanel Margin="0,10,0,0" DataContext="{Binding Desk}">
                        <CheckBox Style="{DynamicResource ThemedCheckBox}"
                                  Content="{x:Static core:SettingsNames.NeonGlow}" IsChecked="{Binding InkGlow, Mode=TwoWay}"
                                  AutomationProperties.Name="{x:Static core:SettingsNames.NeonGlow}"
                                  AutomationProperties.HelpText="{x:Static core:SettingsNames.NeonGlowCaption}" />
                        <Grid Margin="0,10,0,0" Visibility="{Binding InkGlow, Converter={StaticResource BoolVis}}">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="110" />
                                <ColumnDefinition Width="230" />
                                <ColumnDefinition Width="44" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="{x:Static core:SettingsNames.GlowStrength}" FontSize="12" VerticalAlignment="Center"
                                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                            <Slider Grid.Column="1" x:Name="GlowStrengthSlider" Style="{DynamicResource ThemedSlider}"
                                    Minimum="20" Maximum="100" TickFrequency="5" IsSnapToTickEnabled="True" SmallChange="5" LargeChange="20"
                                    Value="{Binding InkGlowPercent, Mode=TwoWay}"
                                    AutomationProperties.Name="{x:Static core:SettingsNames.GlowStrength}"
                                    AutomationProperties.ItemStatus="{Binding InkGlowPercentText}" />
                            <TextBlock Grid.Column="2" Text="{Binding InkGlowPercentText}"
                                       FontSize="11" VerticalAlignment="Center" HorizontalAlignment="Right"
                                       Foreground="{DynamicResource Sanduhr.Brush.TextDim}" FontFamily="Cascadia Code, Consolas" />
                        </Grid>
                        <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,8,0,0"
                                   Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                   Text="{x:Static core:SettingsNames.NeonGlowCaption}" />
                        <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,4,0,0"
                                   Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                   Text="{Binding GlowUnderIconsNote}"
                                   Visibility="{Binding GlowUnderIconsNote, Converter={StaticResource StrVis}}" />
                    </StackPanel>
```

The slider's range is the percentage itself, so UI Automation's value for it is the percentage.

Then replace the message's check box (lines 216-218)

```xml
                        <CheckBox Style="{DynamicResource ThemedCheckBox}" Margin="0,10,0,0" IsEnabled="{Binding OwnColorsEnabled}"
                                  Content="{x:Static core:SettingsNames.GlowAroundMessage}" IsChecked="{Binding Glow, Mode=TwoWay}"
                                  AutomationProperties.Name="{x:Static core:SettingsNames.GlowAroundMessage}" />
```

with

```xml
                        <CheckBox Style="{DynamicResource ThemedCheckBox}" Margin="0,10,0,0" IsEnabled="{Binding GlowEnabled}"
                                  Content="{x:Static core:SettingsNames.GlowAroundMessage}" IsChecked="{Binding GlowShown, Mode=TwoWay}"
                                  AutomationProperties.Name="{x:Static core:SettingsNames.GlowAroundMessage}"
                                  AutomationProperties.HelpText="{Binding GlowCoveredNote}" />
                        <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,4,0,0"
                                   Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                   Text="{Binding GlowCoveredNote}"
                                   Visibility="{Binding GlowCoveredNote, Converter={StaticResource StrVis}}" />
```

- [ ] **Step 7: Run the tests**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskGlowSettingsTests|FullyQualifiedName~Settings|FullyQualifiedName~DeskSplitPages|FullyQualifiedName~ThemeMatchSettings|FullyQualifiedName~DeskMessage"` and `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsIndex|FullyQualifiedName~SettingsNames"`
Expected: PASS. The Settings guard tests (`SettingsWindowNamesTests`, `SettingsContrastTests`, `SettingsControlStatesTests`, `SettingsReachTests`, the source scans) hold every control to a name, a contrast and a reach; if one names the new slider or box, give it what the test asks in the way its neighbours on this page have it. Do not loosen a guard test.

- [ ] **Step 8: Run every suite and commit**

```bash
cd windows-dotnet && dotnet test
git add src tests
git commit -m "feat(windows): Neon glow and its strength on Desk Look

Either-or with the drop shadow; the message's own glow shows as
covered while it is on.

Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>"
```

---

### Task 7: A look file and the catalog

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/DeskLook.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/DeskLookTests.cs` (new)

**Interfaces:**
- Consumes: `DeskDesign.Parse`, `DeskDesign.Glow` (Task 2), `SetupPrompt.Pieces` (the pieces Windows draws: every word in `DeskArrangement.Known` but `meetings`), `BundledFonts.Bundled`.
- Produces (namespace `Sanduhr.Core`; this file is not linked into the MCP server):
  - `sealed record DeskLook(string Id, string Name, string Blurb, string FontFallback, DeskDesign Design)` with `const int SchemaVersion = 1`, `NameMax = 24`, `BlurbMax = 60`, `static IReadOnlyList<string> WindowsFonts`, `static bool ShipsWithWindows(string family)`, `static bool IsValidId(string?)`, `static DeskLookParse Parse(JsonNode?)`, `static IReadOnlyList<string> Rules(DeskDesign)`, and `bool Shows(string piece)`.
  - `sealed record DeskLookParse(DeskLook? Look, IReadOnlyList<string> Problems)`.
  - `sealed record DeskLookCatalog(IReadOnlyList<DeskLook> Looks, IReadOnlyList<string> Skipped)` with `static IReadOnlyList<string> Order`, `static DeskLookCatalog Empty`, `DeskLook? Find(string? id)`, `static DeskLookCatalog Load(IEnumerable<(string Name, string Json)> files)`.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/DeskLookTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>A ready-made look's file (5.0 spec 5, section 3): the wrapper, what its design must and must not carry, and the catalog
/// that skips a bad file and keeps the rest.</summary>
public class DeskLookTests
{
    /// <summary>A look that breaks no rule. The tests change one thing at a time.</summary>
    internal const string Good = """
        {
          "schema_version": 1,
          "id": "test-look",
          "name": "Test look",
          "blurb": "A look for the tests.",
          "font_fallback": "Segoe UI",
          "design": {
            "layout": {
              "clock": { "anchor": "tc", "size": 1.3 },
              "message": { "anchor": "tc", "size": 1 },
              "claude": { "anchor": "bl", "size": 1 },
              "meters": { "hidden": true },
              "nowPlaying": { "anchor": "bc", "size": 1 },
              "telemetry": { "hidden": true },
              "weather": { "hidden": true },
              "watchers": { "hidden": true },
              "launcher": { "anchor": "br", "size": 1 },
              "glow": { "hidden": true },
              "companion": { "hidden": true }
            },
            "font": "Bahnschrift",
            "ink": "#ffd08a",
            "plate": false,
            "shadow": false,
            "glow": 0.7,
            "follows_theme": false
          }
        }
        """;

    internal static JsonObject Doc(Action<JsonObject>? edit = null)
    {
        var o = (JsonObject)JsonNode.Parse(Good)!;
        edit?.Invoke(o);
        return o;
    }

    internal static DeskLook Look(Action<JsonObject>? edit = null)
    {
        var parsed = DeskLook.Parse(Doc(edit));
        Assert.True(parsed.Look is not null, string.Join("; ", parsed.Problems));
        return parsed.Look!;
    }

    private static string Problems(Action<JsonObject> edit) => string.Join("; ", DeskLook.Parse(Doc(edit)).Problems);

    private static JsonObject Design(JsonObject o) => (JsonObject)o["design"]!;

    [Fact]
    public void AGoodLookParses()
    {
        var look = Look();
        Assert.Equal("test-look", look.Id);
        Assert.Equal("Test look", look.Name);
        Assert.Equal("A look for the tests.", look.Blurb);
        Assert.Equal("Segoe UI", look.FontFallback);
        Assert.Equal("Bahnschrift", look.Design.Font!.Family);
        Assert.Equal(new DeskGlowChoice(true, 0.7), look.Design.Glow);
        Assert.True(look.Shows("clock"));
        Assert.False(look.Shows("meters"));
    }

    [Fact]
    public void TheLayoutKeepsTheFilesOrder()
    {
        // clock is written before message, so the clock stacks above it, whatever Settings order says.
        var shown = Look().Design.Layout!.Where(p => !p.IsHidden).Select(p => p.Piece).ToList();
        Assert.Equal(["clock", "message", "claude", "nowPlaying", "launcher"], shown);
    }

    [Fact]
    public void AnUnknownWrapperFieldIsIgnored_SoAWallpaperCanComeLater() =>
        Assert.NotNull(DeskLook.Parse(Doc(o => o["wallpaper"] = "ice.jpg")).Look);

    [Fact]
    public void AnUnknownDesignFieldIsRefused() =>
        Assert.Contains("unknown field", Problems(o => Design(o)["wallpaper"] = "ice.jpg"));

    [Theory]
    [InlineData("Marquee")]
    [InlineData("quiet_corner")]
    [InlineData("-ice")]
    [InlineData("ice-")]
    [InlineData("ice2")]
    [InlineData("")]
    public void AnIdIsLowercaseLettersAndHyphens(string id) => Assert.Contains("id", Problems(o => o["id"] = id));

    [Fact]
    public void TheWrapperIsChecked()
    {
        Assert.Contains("schema_version", Problems(o => o["schema_version"] = 2));
        Assert.Contains("name", Problems(o => o["name"] = ""));
        Assert.Contains("name", Problems(o => o["name"] = new string('x', DeskLook.NameMax + 1)));
        Assert.Contains("blurb", Problems(o => o.Remove("blurb")));
        Assert.Contains("blurb", Problems(o => o["blurb"] = new string('x', DeskLook.BlurbMax + 1)));
        Assert.Contains("font_fallback", Problems(o => o["font_fallback"] = "Segoe UI Variable Text"));   // Windows 11 only
        Assert.Contains("font_fallback", Problems(o => o.Remove("font_fallback")));
        Assert.Contains("must be an object", string.Join("; ", DeskLook.Parse(JsonValue.Create(3)).Problems));
    }

    [Fact]
    public void ABundledFontIsAFineFallbackAndAFineFont()
    {
        Assert.NotNull(DeskLook.Parse(Doc(o => o["font_fallback"] = "EsteFont Pro")).Look);
        Assert.NotNull(DeskLook.Parse(Doc(o => Design(o)["font"] = "EsteFont 26")).Look);
    }

    [Theory]
    [InlineData("layout")]
    [InlineData("font")]
    [InlineData("ink")]
    [InlineData("plate")]
    [InlineData("shadow")]
    [InlineData("glow")]
    [InlineData("follows_theme")]
    public void ALookMustCarry(string field) => Assert.Contains("design." + field, Problems(o => Design(o).Remove(field)));

    [Fact]
    public void ALookMustSwitchFollowingOff() =>
        Assert.Contains("follows_theme", Problems(o => Design(o)["follows_theme"] = true));

    [Theory]
    [InlineData("launcher", """{"orientation":"row","buttons":[]}""")]
    [InlineData("messages", """{"lines":["hello"],"mode":"add"}""")]
    [InlineData("weather", """{"nickname":"Home"}""")]
    [InlineData("name", "\"Mine\"")]
    [InlineData("note", "\"A note.\"")]
    public void ALookMustNotCarryTheUsersContent(string field, string json) =>
        Assert.Contains($"design.{field} is the user's", Problems(o => Design(o)[field] = JsonNode.Parse(json)));

    [Fact]
    public void EveryPieceWindowsDrawsIsNamed()
    {
        Assert.Contains("must name weather", Problems(o => ((JsonObject)Design(o)["layout"]!).Remove("weather")));
        foreach (var piece in SetupPrompt.Pieces) Assert.Contains(Look().Design.Layout!, p => p.Piece == piece);
    }

    [Fact]
    public void ALookNamesNoPieceWindowsDoesNotDrawAndNoMonitor()
    {
        Assert.Contains("layout.meetings", Problems(o => ((JsonObject)Design(o)["layout"]!)["meetings"] = JsonNode.Parse("""{"hidden":true}""")));
        Assert.Contains("layout.clock must not name a monitor", Problems(o => ((JsonObject)Design(o)["layout"]!)["clock"] = JsonNode.Parse("""{"anchor":"tc","size":1.3,"monitor":"main"}""")));
    }

    [Fact]
    public void AShownPieceGivesItsAnchorAndItsSize()
    {
        Assert.Contains("layout.clock must give its anchor and size", Problems(o => ((JsonObject)Design(o)["layout"]!)["clock"] = JsonNode.Parse("""{"anchor":"tc"}""")));
        Assert.Contains("layout.clock must give its anchor and size", Problems(o => ((JsonObject)Design(o)["layout"]!)["clock"] = JsonNode.Parse("""{"hidden":false,"size":1}""")));
    }

    [Fact]
    public void ALookShowsSomething()
    {
        var doc = Doc(o =>
        {
            var layout = (JsonObject)Design(o)["layout"]!;
            foreach (var key in layout.Select(kv => kv.Key).ToList()) layout[key] = JsonNode.Parse("""{"hidden":true}""");
        });
        Assert.Contains("shows nothing", string.Join("; ", DeskLook.Parse(doc).Problems));
    }

    [Fact]
    public void ALooksFontShipsWithEverySupportedWindowsAndBringsNoLink()
    {
        Assert.Contains("design.font", Problems(o => Design(o)["font"] = "Segoe UI Variable Display"));
        Assert.Contains("design.font", Problems(o => Design(o)["font"] = JsonNode.Parse("""{"family":"Georgia","link":"https://example.com/font"}""")));
        Assert.Contains("design.font", Problems(o => Design(o)["font"] = ""));
        Assert.All(DeskLook.WindowsFonts, f => Assert.True(DeskLook.ShipsWithWindows(f.ToUpperInvariant())));
    }

    [Fact]
    public void TheDesignsOwnRulesStillApply()
    {
        Assert.Contains("design.glow", Problems(o => Design(o)["shadow"] = true));   // glow on with shadow true
        Assert.Contains("design.ink", Problems(o => Design(o)["ink"] = "amber"));
    }

    // ---- the catalog ----

    private static string Named(string id) => Doc(o => o["id"] = id).ToJsonString();

    [Fact]
    public void TheCatalogListsTheLooksInTheGallerysOrder()
    {
        var catalog = DeskLookCatalog.Load([("terminal", Named("terminal")), ("zebra", Named("zebra")), ("ice", Named("ice")), ("quiet-corner", Named("quiet-corner"))]);
        Assert.Equal(["quiet-corner", "ice", "terminal", "zebra"], catalog.Looks.Select(l => l.Id));
        Assert.Empty(catalog.Skipped);
        Assert.Equal("ice", catalog.Find("ice")!.Id);
        Assert.Null(catalog.Find("nope"));
        Assert.Null(catalog.Find(null));
    }

    [Fact]
    public void ABadFileIsSkippedAndTheOthersStay()
    {
        var catalog = DeskLookCatalog.Load([
            ("ice", Named("ice")),
            ("broken", "{ not json"),
            ("marquee", Doc(o => { o["id"] = "marquee"; Design(o)["follows_theme"] = true; }).ToJsonString()),
            ("terminal", Named("editorial")),   // the id is not the file's name
        ]);
        Assert.Equal(["ice"], catalog.Looks.Select(l => l.Id));
        Assert.Equal(3, catalog.Skipped.Count);
        Assert.Contains(catalog.Skipped, s => s.StartsWith("broken: "));
        Assert.Contains(catalog.Skipped, s => s.StartsWith("marquee: ") && s.Contains("follows_theme"));
        Assert.Contains(catalog.Skipped, s => s.StartsWith("terminal: ") && s.Contains("editorial"));
    }

    [Fact]
    public void TheGalleryHasSixNames() =>
        Assert.Equal(["quiet-corner", "ice", "marquee", "terminal", "editorial", "dashboard"], DeskLookCatalog.Order);
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DeskLookTests"`
Expected: the build fails with "The type or namespace name 'DeskLook' could not be found".

- [ ] **Step 3: Write the look and the catalog**

Create `windows-dotnet/src/Sanduhr.Core/DeskLook.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>What <see cref="DeskLook.Parse"/> made of a look file: the look when nothing was wrong, else every problem found.</summary>
public sealed record DeskLookParse(DeskLook? Look, IReadOnlyList<string> Problems);

/// <summary>
/// A ready-made Desk look (5.0 spec 5, section 3): a design in exactly the format <c>propose_desk</c> takes, wrapped with an id, a
/// name, one line of blurb and the font to use when this PC lacks the design's. The looks ship inside the app; nothing is downloaded.
/// JSON: <c>{schema_version, id, name, blurb, font_fallback, design}</c>. An unknown wrapper field is ignored, which is where a
/// wallpaper goes after 5.0; an unknown field inside <c>design</c> is refused, as it is for any design.
///
/// A look owns the arrangement and stays out of the user's content: its design names every piece Windows draws as shown or hidden,
/// and carries font, ink, plate, shadow, glow and <c>follows_theme: false</c>; it carries no launcher, messages, song looks, theme,
/// weather, name or note, and no monitor on any piece. The pieces at one anchor stack in the order the file lists them.
/// Pure. Not linked into the MCP server: an agent does not list or apply built-in looks.
/// </summary>
public sealed partial record DeskLook(string Id, string Name, string Blurb, string FontFallback, DeskDesign Design)
{
    public const int SchemaVersion = 1;
    public const int NameMax = 24;
    public const int BlurbMax = 60;

    /// <summary>
    /// The font families a look or its fallback may name, besides the bundled ones: families that ship with every Windows the package
    /// supports (build 17763 and later), under the names WPF lists them by. Weights are not families here: WPF lists "Segoe UI", never
    /// "Segoe UI Light" or "Segoe UI Semibold" (checked 2026-11-20). Nothing that exists only on Windows 11.
    /// </summary>
    public static readonly IReadOnlyList<string> WindowsFonts =
    [
        "Arial", "Bahnschrift", "Calibri", "Cambria", "Candara", "Comic Sans MS", "Consolas", "Constantia", "Corbel", "Courier New",
        "Gabriola", "Georgia", "Impact", "Ink Free", "Lucida Console", "Palatino Linotype", "Segoe Print", "Segoe Script", "Segoe UI",
        "Tahoma", "Times New Roman", "Trebuchet MS", "Verdana",
    ];

    [GeneratedRegex("^[a-z]+(-[a-z]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    /// <summary>An id is lowercase letters and single hyphens between them, at most 32 characters. It is the file's name.</summary>
    public static bool IsValidId(string? id) => id is { Length: > 0 and <= 32 } && IdPattern().IsMatch(id);

    /// <summary>The family is bundled with Sanduhr or is one of <see cref="WindowsFonts"/> (by any case).</summary>
    public static bool ShipsWithWindows(string family) =>
        BundledFonts.Bundled(family) is not null || WindowsFonts.Contains(family, StringComparer.OrdinalIgnoreCase);

    /// <summary>The look shows this piece.</summary>
    public bool Shows(string piece) => Design.Layout?.Any(p => p.Piece == piece && !p.IsHidden) == true;

    private static string Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) && s is not null ? s.Trim() : "";

    /// <summary>Reads a look file. Every problem is listed, so a broken look is fixed in one pass.</summary>
    public static DeskLookParse Parse(JsonNode? node)
    {
        if (node is not JsonObject o) return new DeskLookParse(null, ["must be an object"]);
        var problems = new List<string>();
        if (!(o["schema_version"] is JsonValue sv && sv.TryGetValue<int>(out int version) && version == SchemaVersion))
            problems.Add($"schema_version must be {SchemaVersion}");
        string id = Str(o, "id"), name = Str(o, "name"), blurb = Str(o, "blurb"), fallback = Str(o, "font_fallback");
        if (!IsValidId(id)) problems.Add("id must be lowercase letters and hyphens, at most 32 characters");
        if (name.Length is 0 or > NameMax) problems.Add($"name must be 1 to {NameMax} characters");
        if (blurb.Length is 0 or > BlurbMax) problems.Add($"blurb must be 1 to {BlurbMax} characters");
        if (fallback.Length == 0 || !ShipsWithWindows(fallback))
            problems.Add("font_fallback must be a font that ships with every supported Windows, or a bundled one");

        var parsed = DeskDesign.Parse(o["design"]);
        if (parsed.Design is not { } design)
        {
            problems.AddRange(parsed.Reasons.Select(r => "design." + r));
            return new DeskLookParse(null, problems);
        }
        problems.AddRange(Rules(design));
        return problems.Count > 0
            ? new DeskLookParse(null, problems)
            : new DeskLookParse(new DeskLook(id, name, blurb, fallback, design), []);
    }

    /// <summary>What a look's design must and must not carry (spec section 3), as problems; empty when it keeps every rule.</summary>
    public static IReadOnlyList<string> Rules(DeskDesign d)
    {
        var p = new List<string>();
        if (d.Layout is not { } layout) p.Add("design.layout is missing");
        else
        {
            foreach (var piece in SetupPrompt.Pieces)
                if (layout.All(l => l.Piece != piece)) p.Add($"design.layout must name {piece}, shown or hidden");
            foreach (var l in layout)
            {
                if (!SetupPrompt.Pieces.Contains(l.Piece, StringComparer.Ordinal)) p.Add($"design.layout.{l.Piece} is not a piece Windows draws");
                if (l.Monitor is not null) p.Add($"design.layout.{l.Piece} must not name a monitor: a piece stays on the screen the user put it on");
                if (!l.IsHidden && (l.Anchor is null || l.Size is null)) p.Add($"design.layout.{l.Piece} must give its anchor and size");
            }
            if (layout.All(l => l.IsHidden)) p.Add("design.layout shows nothing");
        }
        if (d.Font is not { Family.Length: > 0 } font) p.Add("design.font is missing");
        else
        {
            if (font.Link is not null) p.Add("design.font must not bring a link: nothing is downloaded for a look");
            if (!ShipsWithWindows(font.Family)) p.Add("design.font must ship with every supported Windows, or be a bundled one");
        }
        if (d.Ink is null) p.Add("design.ink is missing");
        if (d.Plate is null) p.Add("design.plate is missing");
        if (d.Shadow is null) p.Add("design.shadow is missing");
        if (d.Glow is null) p.Add("design.glow is missing");
        if (d.FollowsTheme != false) p.Add("design.follows_theme must be false: a followed widget theme would override the look's ink");
        foreach (var (field, set) in new (string Field, bool Set)[]
                 {
                     ("launcher", d.Launcher is not null), ("messages", d.Messages is not null), ("looks", d.Looks is not null),
                     ("theme", d.Theme is not null), ("weather", d.Weather is not null), ("name", d.Name is not null), ("note", d.Note is not null),
                 })
            if (set) p.Add($"design.{field} is the user's, not a look's");
        return p;
    }
}

/// <summary>The ready-made looks, in the gallery's order, and the files that could not be used (each as "name: why").</summary>
public sealed record DeskLookCatalog(IReadOnlyList<DeskLook> Looks, IReadOnlyList<string> Skipped)
{
    /// <summary>The gallery's order. A look not named here comes after these, by id.</summary>
    public static readonly IReadOnlyList<string> Order = ["quiet-corner", "ice", "marquee", "terminal", "editorial", "dashboard"];

    public static DeskLookCatalog Empty { get; } = new([], []);

    public DeskLook? Find(string? id) => Looks.FirstOrDefault(l => l.Id == id);

    /// <summary>Reads the look files, each as its name (without the extension) and its text. A file that is not JSON, breaks a rule,
    /// carries an id that is not its name, or repeats an id is skipped and named in <see cref="Skipped"/>; the others load.</summary>
    public static DeskLookCatalog Load(IEnumerable<(string Name, string Json)> files)
    {
        var looks = new List<DeskLook>();
        var skipped = new List<string>();
        foreach (var (name, json) in files.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            JsonNode? node;
            try { node = JsonNode.Parse(json); }
            catch (JsonException) { skipped.Add($"{name}: is not JSON"); continue; }
            var parsed = DeskLook.Parse(node);
            if (parsed.Look is not { } look) { skipped.Add($"{name}: {string.Join("; ", parsed.Problems)}"); continue; }
            if (look.Id != name) { skipped.Add($"{name}: its id is {look.Id}, not the file's name"); continue; }
            if (looks.Any(l => l.Id == look.Id)) { skipped.Add($"{name}: a second look with this id"); continue; }
            looks.Add(look);
        }
        int Rank(DeskLook l)
        {
            for (int i = 0; i < Order.Count; i++)
                if (Order[i] == l.Id) return i;
            return int.MaxValue;
        }
        return new DeskLookCatalog([.. looks.OrderBy(Rank).ThenBy(l => l.Id, StringComparer.Ordinal)], skipped);
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DeskLookTests"`
Expected: PASS.

- [ ] **Step 5: Run every suite and commit**

```bash
cd windows-dotnet && dotnet test
git add src/Sanduhr.Core/DeskLook.cs tests/Sanduhr.Tests/DeskLookTests.cs
git commit -m "feat(windows): a ready-made look's file and its catalog

Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>"
```

---

### Task 8: Fitting a look to this PC

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/DeskDesignValidator.cs` (add `ContrastReasons` above `CheckContrast`, line 168)
- Create: `windows-dotnet/src/Sanduhr.Core/DeskLookFit.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/DeskLookFitTests.cs` (new)

**Interfaces:**
- Consumes: `DeskLook` (Task 7); `DeskDesignContext`, `DeskDesign.Validate`, `DeskDesign.Merge`; `DeskDesignApply.Layout`; `DeskInkChoice.Describe`; `DeskGlowChoice`.
- Produces:
  - `internal static IReadOnlyList<DeskReason> DeskDesign.ContrastReasons(DeskDesign proposal, DeskDesignContext ctx)` — the validator's contrast judgement alone.
  - `sealed record DeskLookFitResult(DeskDesign Design, IReadOnlyList<string> Notes)`.
  - `sealed record DeskLookNeed(string Piece, string Line)`.
  - `static class DeskLookFit` with
    - `const string PlateNote = "Uses its plate on your wallpaper."`, `AutoInkNote = "Uses automatic ink on your wallpaper."`, `static string FontNote(string family)`
    - `static DeskLookFitResult Fit(DeskLook look, DeskDesignContext context)`
    - `static bool IsLook(DeskLook look, DeskDesign current, IReadOnlyCollection<string>? installedFonts)` — the Desk is this look, untouched
    - `static bool IsExactly(DeskLook look, DeskLookFitResult fit, DeskDesign current)` — the Desk is this fit of the look
    - `static DeskLook? Untouched(IEnumerable<DeskLook> looks, DeskDesign current, IReadOnlyCollection<string>? installedFonts)`
    - `static IReadOnlyList<DeskPieceLayout> LayoutOver(DeskLook look, DeskDesign? current)` — the layout to write
    - `static IReadOnlyList<DeskLookNeed> Needs(DeskLook look, bool weatherHasPlace, bool launcherHasButtons, bool hasWatcher)`

The fit, as the spec orders it:

1. **Font.** Installed or bundled: kept. Otherwise `font_fallback`; when that is missing too, the system font (`""`). Unknown fonts (`InstalledFonts` null): kept.
2. **Ink.** `auto` is never touched. With no wallpaper summary the look is used as designed. Otherwise: as designed when the validator finds no contrast reason; else the plate on, when that clears every reason; else ink `auto` with the plate as designed.

The context's `Current` is the user's Desk: the validator judges the fitted design merged over it, so the user's labelled Launcher buttons and each piece's monitor count, as they will once the look is applied.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/DeskLookFitTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Fitting a ready-made look to a PC (5.0 spec 5, sections 4 and 5): the font steps, the ink ladder, that a fitted look
/// always passes the validator, and when the Desk still counts as an untouched look.</summary>
public class DeskLookFitTests
{
    private static readonly string[] Fonts = ["Bahnschrift", "Consolas", "Georgia", "Segoe UI"];

    private static WallpaperOverview Flat(double b, Dictionary<DeskAnchor, double>? overrides = null) => DeskDesignValidatorTests.Flat(b, overrides);

    private static JsonObject Design(JsonObject o) => (JsonObject)o["design"]!;

    /// <summary>A Desk as get_desk reports it: the default layout with the user's own labelled Launcher button.</summary>
    private static readonly LauncherModel UsersLauncher =
        new(LauncherOrientation.Row, [LauncherButton.Create(LauncherKind.Link, "https://example.com", label: "Home")!]);

    private static DeskDesign Desk(DeskSettings? desk = null, DeskInkSettings? ink = null, bool follows = false) =>
        DeskState.Current(desk ?? DeskSettings.Default, ink ?? DeskInkSettings.Default, UsersLauncher, null, followsTheme: follows);

    private static DeskDesignContext Ctx(WallpaperOverview? wall, DeskDesign? current = null, IReadOnlyCollection<string>? fonts = null) =>
        new(fonts ?? Fonts, wall, null, current ?? Desk());

    /// <summary>The Desk after the look is applied over <paramref name="desk"/>, the way the App writes it: the fitted design merged,
    /// the layout in the file's order, through the same settings a design writes, read back as get_desk would.</summary>
    private static DeskDesign Applied(DeskLook look, DeskLookFitResult fit, DeskSettings? desk = null, DeskInkSettings? ink = null)
    {
        desk ??= DeskSettings.Default;
        ink ??= DeskInkSettings.Default;
        var before = DeskState.Current(desk, ink, UsersLauncher, null);
        var merged = DeskDesign.Merge(before, fit.Design) with { Layout = [.. DeskLookFit.LayoutOver(look, before)] };
        var next = DeskDesignApply.Settings(desk, ink, UsersLauncher, merged);
        return DeskState.Current(next.Desk, next.Ink, next.Launcher, null, followsTheme: next.FollowsTheme ?? false);
    }

    // ---- the ink ladder ----

    [Fact]
    public void OnAWallpaperItReadsOverTheLookIsUsedAsDesigned()
    {
        var look = DeskLookTests.Look();
        var fit = DeskLookFit.Fit(look, Ctx(Flat(0.03)));
        Assert.Empty(fit.Notes);
        Assert.Equal(look.Design.ToJson().ToJsonString(), fit.Design.ToJson().ToJsonString());
    }

    [Fact]
    public void TheThreeStepsEachHappenSomewhereAndEveryFitPassesTheValidator()
    {
        var look = DeskLookTests.Look();
        var seen = new HashSet<string>();
        for (int i = 1; i < 50; i++)
        {
            double b = i / 50.0;
            var ctx = Ctx(Flat(b));
            var fit = DeskLookFit.Fit(look, ctx);
            var verdict = DeskDesign.Validate(fit.Design, ctx);
            Assert.True(verdict.Ok, $"brightness {b}: {string.Join("; ", verdict.Reasons)}");

            if (fit.Notes.Count == 0)
            {
                seen.Add("as designed");
                Assert.Equal(false, fit.Design.Plate);
                Assert.Equal(DeskInkKind.Custom, fit.Design.Ink!.Kind);
            }
            else if (fit.Notes.SequenceEqual([DeskLookFit.PlateNote]))
            {
                seen.Add("plate");
                Assert.Equal(true, fit.Design.Plate);
                Assert.Equal(DeskInkKind.Custom, fit.Design.Ink!.Kind);
            }
            else
            {
                Assert.Equal([DeskLookFit.AutoInkNote], fit.Notes);
                seen.Add("auto");
                Assert.Equal(DeskInkKind.Auto, fit.Design.Ink!.Kind);
                Assert.Equal(false, fit.Design.Plate);   // the plate stays as the look designed it
            }
        }
        Assert.Equal(["as designed", "auto", "plate"], seen.Order());
    }

    [Fact]
    public void OnePieceOverABrightPatchIsEnoughToFit()
    {
        var look = DeskLookTests.Look();   // the clock is at tc
        var ctx = Ctx(Flat(0.03, new() { [DeskAnchor.TC] = 0.95 }));
        var fit = DeskLookFit.Fit(look, ctx);
        Assert.NotEmpty(fit.Notes);
        Assert.True(DeskDesign.Validate(fit.Design, ctx).Ok);
    }

    [Fact]
    public void TwoMonitorsOfOppositeBrightnessAreJudgedOnTheWorse()
    {
        var look = DeskLookTests.Look();
        var dark = Flat(0.03).Monitors[0];
        var bright = Flat(0.95).Monitors[0] with { Index = 1, Primary = false };
        var ctx = Ctx(new WallpaperOverview([], 0.5, [dark, bright]));
        var fit = DeskLookFit.Fit(look, ctx);
        Assert.NotEmpty(fit.Notes);
        Assert.True(DeskDesign.Validate(fit.Design, ctx).Ok);
    }

    [Fact]
    public void ALookWhoseInkIsAutomaticIsNeverAdjusted()
    {
        var look = DeskLookTests.Look(o => { Design(o)["ink"] = "auto"; });
        foreach (double b in new[] { 0.02, 0.3, 0.5, 0.7, 0.98 })
        {
            var fit = DeskLookFit.Fit(look, Ctx(Flat(b)));
            Assert.Empty(fit.Notes);
            Assert.Equal(DeskInkKind.Auto, fit.Design.Ink!.Kind);
            Assert.Equal(false, fit.Design.Plate);
        }
    }

    [Fact]
    public void ALookThatAlreadyHasItsPlateGoesStraightToAutomaticInk()
    {
        var look = DeskLookTests.Look(o => Design(o)["plate"] = true);
        var ctx = Ctx(Flat(0.98));
        var fit = DeskLookFit.Fit(look, ctx);
        Assert.Equal([DeskLookFit.AutoInkNote], fit.Notes);
        Assert.Equal(true, fit.Design.Plate);
        Assert.True(DeskDesign.Validate(fit.Design, ctx).Ok);
    }

    [Fact]
    public void WithNoWallpaperSummaryTheLookIsUsedAsDesigned()
    {
        var look = DeskLookTests.Look();
        Assert.Empty(DeskLookFit.Fit(look, Ctx(null)).Notes);
        Assert.Empty(DeskLookFit.Fit(look, Ctx(new WallpaperOverview([], 0.9, []))).Notes);
    }

    // ---- the font ----

    [Fact]
    public void AnInstalledFontIsKept() => Assert.Equal("Bahnschrift", DeskLookFit.Fit(DeskLookTests.Look(), Ctx(null)).Design.Font!.Family);

    [Fact]
    public void AMissingFontTakesTheFallbackAndSaysSo()
    {
        var fit = DeskLookFit.Fit(DeskLookTests.Look(), Ctx(null, fonts: ["Segoe UI", "Georgia"]));
        Assert.Equal("Segoe UI", fit.Design.Font!.Family);
        Assert.Equal(["Uses Segoe UI on this PC."], fit.Notes);
    }

    [Fact]
    public void WithNeitherFontTheLookTakesTheSystemFontAndNeverWaitsOnOne()
    {
        var ctx = Ctx(Flat(0.03), fonts: ["Wingdings"]);
        var fit = DeskLookFit.Fit(DeskLookTests.Look(), ctx);
        Assert.Equal("", fit.Design.Font!.Family);
        Assert.Equal(["Uses the system font on this PC."], fit.Notes);
        Assert.Equal(DeskDesignStatus.Ok, DeskDesign.Validate(fit.Design, ctx).Status);
    }

    [Fact]
    public void ABundledFontIsNeverReplaced()
    {
        var look = DeskLookTests.Look(o => Design(o)["font"] = "EsteFont Pro");
        var fit = DeskLookFit.Fit(look, Ctx(null, fonts: ["Wingdings"]));
        Assert.Equal("EsteFont Pro", fit.Design.Font!.Family);
        Assert.Empty(fit.Notes);
    }

    [Fact]
    public void WhenTheFontListIsUnknownTheFontIsKept()
    {
        var fit = DeskLookFit.Fit(DeskLookTests.Look(), new DeskDesignContext(null, null, null, Desk()));
        Assert.Equal("Bahnschrift", fit.Design.Font!.Family);
        Assert.Empty(fit.Notes);
    }

    [Fact]
    public void TheFontNoteComesBeforeTheInkNote()
    {
        var fit = DeskLookFit.Fit(DeskLookTests.Look(), Ctx(Flat(0.98), fonts: ["Segoe UI"]));
        Assert.Equal(["Uses Segoe UI on this PC.", DeskLookFit.AutoInkNote], fit.Notes);
    }

    // ---- the monitor a piece is on ----

    [Fact]
    public void APieceOnAnUnpluggedMonitorKeepsItAndTheLookStillValidates()
    {
        var look = DeskLookTests.Look();
        var desk = DeskSettings.Default with { Layout = new DeskArrangement([new DeskPlacement("clock", DeskAnchor.BL, 1, @"\\?\DISPLAY#GONE#1")]).Layout };
        var current = DeskState.Current(desk, DeskInkSettings.Default, UsersLauncher, null);
        var connected = new[] { new DeskMonitorInfo(@"\\?\DISPLAY#HERE#1", "1920×1080, main", 1920, 1080, true, 0, 0) };
        var ctx = new DeskDesignContext(Fonts, Flat(0.03), null, current, connected);

        var fit = DeskLookFit.Fit(look, ctx);
        Assert.True(DeskDesign.Validate(fit.Design, ctx).Ok);   // the fitted design names no monitor, so nothing is refused

        var layout = DeskLookFit.LayoutOver(look, current);
        Assert.True(DeskMonitors.Same(@"\\?\DISPLAY#GONE#1", layout.Single(p => p.Piece == "clock").Monitor));
        Assert.Null(layout.Single(p => p.Piece == "message").Monitor);   // hidden before: it comes up on every monitor
    }

    [Fact]
    public void TheLayoutToWriteIsTheFilesOrderThenWhateverTheLookDoesNotName()
    {
        var look = DeskLookTests.Look();
        var current = Desk() with { Layout = [.. Desk().Layout!.Where(p => p.Piece != "meetings"), new DeskPieceLayout("meetings", DeskAnchor.TR, 1, false, "all")] };
        var layout = DeskLookFit.LayoutOver(look, current);
        Assert.Equal(look.Design.Layout!.Select(p => p.Piece), layout.Take(look.Design.Layout!.Count).Select(p => p.Piece));
        Assert.Equal("meetings", layout[^1].Piece);
        Assert.Equal(DeskAnchor.TR, layout[^1].Anchor);
        Assert.Equal(look.Design.Layout!.Count, DeskLookFit.LayoutOver(look, null).Count);
    }

    // ---- is the Desk this look ----

    [Theory]
    [InlineData(0.03)]   // as designed
    [InlineData(0.25)]   // one of the fitted steps
    [InlineData(0.98)]   // automatic ink
    public void AnAppliedLookIsRecognisedWhateverTheFitWas(double brightness)
    {
        var look = DeskLookTests.Look();
        var fit = DeskLookFit.Fit(look, Ctx(Flat(brightness)));
        var now = Applied(look, fit);
        Assert.True(DeskLookFit.IsLook(look, now, Fonts));
        Assert.True(DeskLookFit.IsExactly(look, fit, now));
        Assert.Same(look, DeskLookFit.Untouched([DeskLookTests.Look(o => { o["id"] = "other"; Design(o)["glow"] = false; Design(o)["shadow"] = true; }), look], now, Fonts));
    }

    [Fact]
    public void AfterTheWallpaperChangesTheDeskIsStillTheLookButNoLongerExactlyItsNewFit()
    {
        var look = DeskLookTests.Look();
        var now = Applied(look, DeskLookFit.Fit(look, Ctx(Flat(0.03))));
        var refit = DeskLookFit.Fit(look, Ctx(Flat(0.98), now));
        Assert.True(DeskLookFit.IsLook(look, now, Fonts));
        Assert.False(DeskLookFit.IsExactly(look, refit, now));
    }

    [Fact]
    public void TheFallbackFontStillCountsAndSoDoesTheSystemFontWhenBothAreMissing()
    {
        var look = DeskLookTests.Look();
        string[] some = ["Segoe UI"], none = ["Wingdings"];
        Assert.True(DeskLookFit.IsLook(look, Applied(look, DeskLookFit.Fit(look, Ctx(null, fonts: some))), some));
        Assert.True(DeskLookFit.IsLook(look, Applied(look, DeskLookFit.Fit(look, Ctx(null, fonts: none))), none));
        // The system font is the user's own choice on a PC that has the look's font.
        Assert.False(DeskLookFit.IsLook(look, Applied(look, DeskLookFit.Fit(look, Ctx(null, fonts: none))), Fonts));
    }

    [Fact]
    public void TheUsersLauncherMessagesAndSourcesNeverCount()
    {
        var look = DeskLookTests.Look();
        var now = Applied(look, DeskLookFit.Fit(look, Ctx(Flat(0.03))));
        var others = now with
        {
            Launcher = LauncherModel.Empty,
            Messages = new DeskMessagesChange(["hello"], MessageProposalMode.Add),
            Weather = new DeskWeatherChange("Home"),
            Layout = [.. now.Layout!.Select(p => p.IsHidden ? p : p with { Monitor = "main" })],
        };
        Assert.True(DeskLookFit.IsLook(look, others, Fonts));
    }

    [Fact]
    public void AnyChangeToWhatALookCarriesMakesItTheUsersOwnDesk()
    {
        var look = DeskLookTests.Look();
        var now = Applied(look, DeskLookFit.Fit(look, Ctx(Flat(0.03))));
        DeskDesign With(Func<DeskPieceLayout, DeskPieceLayout> edit, string piece) =>
            now with { Layout = [.. now.Layout!.Select(p => p.Piece == piece ? edit(p) : p)] };

        Assert.False(DeskLookFit.IsLook(look, With(p => p with { Anchor = DeskAnchor.TL }, "clock"), Fonts));       // moved
        Assert.False(DeskLookFit.IsLook(look, With(p => p with { Size = 1.5 }, "clock"), Fonts));                    // resized
        Assert.False(DeskLookFit.IsLook(look, With(p => p with { Tilt = 10 }, "claude"), Fonts));                    // slanted
        Assert.False(DeskLookFit.IsLook(look, With(p => new DeskPieceLayout(p.Piece, null, null, true), "claude"), Fonts));   // hidden
        Assert.False(DeskLookFit.IsLook(look, With(p => new DeskPieceLayout(p.Piece, DeskAnchor.TR, 1, false), "weather"), Fonts));   // shown
        Assert.False(DeskLookFit.IsLook(look, now with { Ink = new DeskInkChoice(DeskInkKind.Custom, ["#ff0000"]) }, Fonts));
        Assert.False(DeskLookFit.IsLook(look, now with { Ink = DeskInkChoice.Of(DeskInkKind.Light) }, Fonts));
        Assert.False(DeskLookFit.IsLook(look, now with { Font = new DeskFontChoice("Georgia") }, Fonts));
        Assert.False(DeskLookFit.IsLook(look, now with { Glow = new DeskGlowChoice(true, 0.9) }, Fonts));
        Assert.False(DeskLookFit.IsLook(look, now with { Glow = DeskGlowChoice.Off }, Fonts));
        Assert.False(DeskLookFit.IsLook(look, now with { Glow = DeskGlowChoice.Off, Shadow = true }, Fonts));
        Assert.False(DeskLookFit.IsLook(look, now with { FollowsTheme = true }, Fonts));
        Assert.True(DeskLookFit.IsLook(look, now with { Plate = true }, Fonts));    // the plate is the fit's to change
        Assert.True(DeskLookFit.IsLook(look, now with { Ink = DeskInkChoice.Of(DeskInkKind.Auto) }, Fonts));
    }

    [Fact]
    public void AStackTheUserReorderedIsTheirOwnDesk()
    {
        var look = DeskLookTests.Look();   // clock above message at tc
        var now = Applied(look, DeskLookFit.Fit(look, Ctx(Flat(0.03))));
        var clock = now.Layout!.Single(p => p.Piece == "clock");
        var message = now.Layout!.Single(p => p.Piece == "message");
        var swapped = now with { Layout = [.. now.Layout!.Select(p => p.Piece == "clock" ? message : p.Piece == "message" ? clock : p)] };
        Assert.False(DeskLookFit.IsLook(look, swapped, Fonts));
    }

    [Fact]
    public void ADeskThatIsNoLookIsNone()
    {
        Assert.Null(DeskLookFit.Untouched([DeskLookTests.Look()], Desk(), Fonts));
        Assert.False(DeskLookFit.IsLook(DeskLookTests.Look(), new DeskDesign(), Fonts));
    }

    // ---- what a shown piece needs ----

    [Fact]
    public void AShownPieceWithNothingToDrawSaysWhatItNeeds()
    {
        var look = DeskLookTests.Look(o =>
        {
            var layout = (JsonObject)Design(o)["layout"]!;
            layout["weather"] = JsonNode.Parse("""{"anchor":"tr","size":1}""");
            layout["watchers"] = JsonNode.Parse("""{"anchor":"mr","size":1}""");
        });
        var needs = DeskLookFit.Needs(look, weatherHasPlace: false, launcherHasButtons: false, hasWatcher: false);
        Assert.Equal([new DeskLookNeed("weather", "Weather needs a place."), new DeskLookNeed("launcher", "The Launcher needs buttons."), new DeskLookNeed("watchers", "Watchers needs a watcher.")], needs);
        Assert.Empty(DeskLookFit.Needs(look, true, true, true));
        // A piece the look hides needs nothing.
        Assert.Equal(["launcher"], DeskLookFit.Needs(DeskLookTests.Look(), false, false, false).Select(n => n.Piece));
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DeskLookFitTests"`
Expected: the build fails with "The name 'DeskLookFit' does not exist".

- [ ] **Step 3: Make the contrast judgement callable**

In `windows-dotnet/src/Sanduhr.Core/DeskDesignValidator.cs`, directly above `private static void CheckContrast(`, add:

```csharp
    /// <summary>
    /// The contrast rule alone, for a caller that fits a design to a wallpaper before it is proposed (a ready-made look,
    /// <c>DeskLookFit</c>): the reasons <see cref="Validate(JsonNode?, DeskDesignContext)"/> would give for
    /// <paramref name="proposal"/>'s ink over <paramref name="ctx"/>'s wallpaper, judged as it would look merged over
    /// <see cref="DeskDesignContext.Current"/>. Empty when every piece reads, when the ink is automatic, and when there is no
    /// wallpaper summary. One implementation: this is <see cref="CheckContrast"/> itself.
    /// </summary>
    internal static IReadOnlyList<DeskReason> ContrastReasons(DeskDesign proposal, DeskDesignContext ctx)
    {
        var reasons = new List<DeskReason>();
        CheckContrast(proposal, ctx, reasons);
        return reasons;
    }
```

Nothing else in that file changes. It names nothing from `DeskLook.cs` or `DeskLookFit.cs`, so the MCP server's link list holds.

- [ ] **Step 4: Write the fitter**

Create `windows-dotnet/src/Sanduhr.Core/DeskLookFit.cs`:

```csharp
namespace Sanduhr.Core;

/// <summary>A look fitted to this PC: the design to draw and apply, and what the fit changed, one sentence each, font first.</summary>
public sealed record DeskLookFitResult(DeskDesign Design, IReadOnlyList<string> Notes);

/// <summary>A piece a look shows that has nothing to draw yet, and the line that says so.</summary>
public sealed record DeskLookNeed(string Piece, string Line);

/// <summary>
/// Fits a ready-made look to this PC (5.0 spec 5, sections 4 and 5), and tells whether the Desk still is a look. Pure.
/// A look is ours and is applied in one click with no review, so it must always pass the rules a design is held to: the fit
/// swaps a missing font and steps the ink down a ladder (as designed, then the plate on, then automatic ink) until the validator's
/// own contrast judgement is satisfied. A fitted look always passes <see cref="DeskDesign.Validate(DeskDesign, DeskDesignContext)"/>
/// over the same context.
/// </summary>
public static class DeskLookFit
{
    public const string PlateNote = "Uses its plate on your wallpaper.";
    public const string AutoInkNote = "Uses automatic ink on your wallpaper.";

    /// <summary>The note for a font the fit swapped in: the family, or the system font for "".</summary>
    public static string FontNote(string family) =>
        family.Length == 0 ? "Uses the system font on this PC." : $"Uses {family} on this PC.";

    private static bool Has(IReadOnlyCollection<string> installed, string family) =>
        family.Length == 0 || BundledFonts.Bundled(family) is not null || installed.Contains(family, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The look as this PC can draw it. <paramref name="context"/> gives the installed fonts (null: unknown, the font is kept), the
    /// wallpaper (null or empty: the look is used as designed, as the validator does without one) and the Desk as it is now, which
    /// the contrast is judged over: the user's labelled Launcher buttons and each piece's monitor count.
    /// </summary>
    public static DeskLookFitResult Fit(DeskLook look, DeskDesignContext context)
    {
        var design = look.Design;
        var notes = new List<string>();

        if (context.InstalledFonts is { } installed && design.Font is { } font && !Has(installed, font.Family))
        {
            string stand = Has(installed, look.FontFallback) ? look.FontFallback : "";
            design = design with { Font = new DeskFontChoice(stand) };
            notes.Add(FontNote(stand));
        }

        // Automatic ink follows the wallpaper by definition, so it is never adjusted.
        if (design.Ink is { Kind: not DeskInkKind.Auto } && context.Wallpaper is { Monitors.Count: > 0 }
            && DeskDesign.ContrastReasons(design, context).Count > 0)
        {
            var plated = design with { Plate = true };
            if (design.Plate != true && DeskDesign.ContrastReasons(plated, context).Count == 0)
            {
                design = plated;
                notes.Add(PlateNote);
            }
            else
            {
                design = design with { Ink = DeskInkChoice.Of(DeskInkKind.Auto) };
                notes.Add(AutoInkNote);
            }
        }
        return new DeskLookFitResult(design, notes);
    }

    /// <summary>
    /// The layout to write for the look over the Desk as it is: the look's pieces in the file's order (which is their stacking
    /// order), each shown piece on the monitor the user gave it (a piece that was hidden comes up on every monitor), then any piece
    /// the look does not name, as it is. <see cref="DeskDesign.Merge"/> is not used for this: it places a piece into a stack by
    /// Settings order and leaves a piece already at its anchor where it was, so one look would stack differently on two Desks.
    /// </summary>
    public static IReadOnlyList<DeskPieceLayout> LayoutOver(DeskLook look, DeskDesign? current)
    {
        var now = current?.Layout ?? [];
        var named = Named(look);
        var list = (look.Design.Layout ?? [])
            .Select(p => p.IsHidden ? p : p with { Monitor = now.FirstOrDefault(c => c.Piece == p.Piece && !c.IsHidden)?.Monitor })
            .ToList();
        list.AddRange(now.Where(c => !named.Contains(c.Piece)));
        return list;
    }

    private static HashSet<string> Named(DeskLook look) =>
        (look.Design.Layout ?? []).Select(p => p.Piece).ToHashSet(StringComparer.Ordinal);

    /// <summary>The look's pieces of a layout as one comparable string: the placed ones in order, each with its anchor, size, tilt
    /// and style, and no monitor (a piece's monitor is the user's).</summary>
    private static string Words(DeskLook look, IEnumerable<DeskPieceLayout> layout)
    {
        var named = Named(look);
        return DeskDesignApply.Layout([.. layout.Where(p => named.Contains(p.Piece)).Select(p => p with { Monitor = null })]);
    }

    private static bool SameArrangement(DeskLook look, DeskDesign current) =>
        current.Layout is { } now && Words(look, now) == Words(look, look.Design.Layout ?? [])
        && (current.Shadow ?? true) == (look.Design.Shadow ?? true)
        && SameGlow(look.Design.Glow, current.Glow)
        && current.FollowsTheme != true;

    /// <summary>Off matches off; on at a strength matches that strength; plain on matches any strength.</summary>
    private static bool SameGlow(DeskGlowChoice? look, DeskGlowChoice? current)
    {
        bool want = look is { On: true }, has = current is { On: true };
        if (want != has) return false;
        return !want || look!.Strength is not { } s || (current!.Strength is { } c && Math.Abs(c - s) < 0.005);
    }

    private static bool SameFont(string? a, string? b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

    private static bool SameInk(DeskInkChoice? a, DeskInkChoice? b) =>
        a is not null && b is not null && a.Kind == b.Kind && string.Equals(a.Describe(), b.Describe(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The Desk is this look, untouched (spec section 5, "Untouched"). Judged on what a look carries and nothing else: the layout
    /// of the pieces it names (order, anchor, size, tilt, style), the shadow, the glow and not following the theme are the look's;
    /// the font is the look's or its fallback (or the system font, on a PC with neither); the ink is the look's or automatic; the
    /// plate may be either. Those last three are exactly what fitting may change, so a look applied over one wallpaper is still
    /// recognised after the wallpaper changes. The user's Launcher, messages, sources and monitors never count.
    /// </summary>
    public static bool IsLook(DeskLook look, DeskDesign current, IReadOnlyCollection<string>? installedFonts)
    {
        if (!SameArrangement(look, current)) return false;
        string? font = current.Font?.Family;
        bool fontOk = SameFont(font, look.Design.Font?.Family) || SameFont(font, look.FontFallback)
            || (SameFont(font, "") && installedFonts is { } installed
                && !Has(installed, look.Design.Font?.Family ?? "x") && !Has(installed, look.FontFallback));
        if (!fontOk) return false;
        return current.Ink is { Kind: DeskInkKind.Auto } || SameInk(current.Ink, look.Design.Ink);
    }

    /// <summary>The Desk is this fit of the look: nothing would change if it were applied again.</summary>
    public static bool IsExactly(DeskLook look, DeskLookFitResult fit, DeskDesign current) =>
        SameArrangement(look, current)
        && SameFont(current.Font?.Family, fit.Design.Font?.Family)
        && SameInk(current.Ink, fit.Design.Ink)
        && (current.Plate ?? false) == (fit.Design.Plate ?? false);

    /// <summary>The look the Desk is, untouched, or null when it is the user's own.</summary>
    public static DeskLook? Untouched(IEnumerable<DeskLook> looks, DeskDesign current, IReadOnlyCollection<string>? installedFonts) =>
        looks.FirstOrDefault(l => IsLook(l, current, installedFonts));

    /// <summary>The pieces the look shows that have nothing to draw yet, in the order Settings lists them. The Claude line and the
    /// meters are not here: they say so themselves on the Desk when no one is signed in.</summary>
    public static IReadOnlyList<DeskLookNeed> Needs(DeskLook look, bool weatherHasPlace, bool launcherHasButtons, bool hasWatcher)
    {
        var needs = new List<DeskLookNeed>();
        if (look.Shows("weather") && !weatherHasPlace) needs.Add(new DeskLookNeed("weather", "Weather needs a place."));
        if (look.Shows("launcher") && !launcherHasButtons) needs.Add(new DeskLookNeed("launcher", "The Launcher needs buttons."));
        if (look.Shows("watchers") && !hasWatcher) needs.Add(new DeskLookNeed("watchers", "Watchers needs a watcher."));
        return needs;
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~DeskLookFitTests|FullyQualifiedName~DeskDesignValidatorTests"`
Expected: PASS. Worked out ahead with the plate at its 0.30 opacity: the amber test ink (`#ffd08a`, luminance 0.68) reads bare up to a wallpaper brightness of 0.18, on the plate from 0.20 to 0.42, and needs automatic ink above that, so all three steps occur in the sweep and 0.25 is the plate step.

- [ ] **Step 6: Run every suite and commit**

```bash
cd windows-dotnet && dotnet test
git add src/Sanduhr.Core/DeskDesignValidator.cs src/Sanduhr.Core/DeskLookFit.cs tests/Sanduhr.Tests/DeskLookFitTests.cs
git commit -m "feat(windows): fit a ready-made look to this PC's fonts and wallpaper

The font falls back, the ink steps to the plate and then to automatic,
judged by the validator's own contrast rule. A fitted look always
validates.

Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>"
```

---

### Task 9: The six looks

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/Desk/Looks/quiet-corner.json`, `ice.json`, `marquee.json`, `terminal.json`, `editorial.json`, `dashboard.json`
- Create: `windows-dotnet/src/Sanduhr.App/Desk/DeskLookFiles.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj` (one `ItemGroup`)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DeskLookFilesTests.cs` (new)

**Interfaces:**
- Consumes: `DeskLookCatalog.Load`, `DeskLook`, `DeskLookFit.Fit` (Tasks 7 and 8).
- Produces: `internal static class DeskLookFiles` (namespace `Sanduhr.App.Desk`) with `static DeskLookCatalog Catalog` (read once) and `static DeskLookCatalog Read()`. Embedded resource names `DeskLooks/<id>.json`.

Two font names changed from the spec's table. WPF lists "Segoe UI" as one family and does not list "Segoe UI Light" or "Segoe UI Semibold" (checked on the owner's PC, 2026-11-20), and the spec says a name the installed-font list does not hold is replaced before the look is written. Quiet corner takes **Corbel** (a light humanist sans that ships with Windows), Dashboard takes **Segoe UI**. Both are the owner's to change at the audition in Task 13.

The `glow` piece (the spot Claude Code's glow lights) and the `companion` are hidden in all six: a look's own ink is the show. Also the owner's to change at the audition.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/DeskLookFilesTests.cs`:

```csharp
using System.Windows.Media;
using Microsoft.Win32;
using Sanduhr.App.Desk;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>The six looks that ship inside the app (5.0 spec 5, sections 3 and 10): every file loads, keeps the rules, and fits every
/// kind of wallpaper into something the validator passes. The positions and sizes are not pinned: they are tuned by eye.</summary>
public sealed class DeskLookFilesTests
{
    private static DeskLookCatalog Catalog => DeskLookFiles.Read();

    /// <summary>A one-monitor wallpaper whose every anchor has this brightness, but for the ones overridden.</summary>
    private static WallpaperOverview Flat(double brightness, Dictionary<DeskAnchor, double>? overrides = null)
    {
        var readings = DeskAnchors.All
            .Select(a => new AnchorReading(a, overrides is not null && overrides.TryGetValue(a, out var b) ? b : brightness, 0.2))
            .ToList();
        var summary = new WallpaperSummary([new DominantColor("#808080", 1)], brightness, readings);
        return new WallpaperOverview(summary.Dominant, brightness, [new WallpaperMonitorSummary(0, true, 2560, 1440, summary)]);
    }

    private static IEnumerable<(string Name, WallpaperOverview Wall)> Wallpapers()
    {
        yield return ("dark", Flat(0.03));
        yield return ("light", Flat(0.95));
        yield return ("mid-grey", Flat(0.22));
        yield return ("busy", Flat(0.3, new()
        {
            [DeskAnchor.TL] = 0.9, [DeskAnchor.TC] = 0.05, [DeskAnchor.TR] = 0.6, [DeskAnchor.ML] = 0.15,
            [DeskAnchor.MR] = 0.8, [DeskAnchor.BL] = 0.5, [DeskAnchor.BC] = 0.95, [DeskAnchor.BR] = 0.1,
        }));
        var dark = Flat(0.03).Monitors[0];
        var bright = Flat(0.95).Monitors[0] with { Index = 1, Primary = false };
        yield return ("two monitors, opposite", new WallpaperOverview([], 0.5, [dark, bright]));
    }

    private static DeskDesign UsersDesk() => DeskState.Current(DeskSettings.Default, DeskInkSettings.Default,
        new LauncherModel(LauncherOrientation.Row, [LauncherButton.Create(LauncherKind.Link, "https://example.com", label: "Home")!]), null);

    [Fact]
    public void All_six_load_in_the_gallerys_order_and_none_is_skipped()
    {
        Assert.Empty(Catalog.Skipped);
        Assert.Equal(DeskLookCatalog.Order, Catalog.Looks.Select(l => l.Id));
    }

    [Fact]
    public void Each_passes_the_Desks_own_rules_as_written()
    {
        foreach (var look in Catalog.Looks)
        {
            var verdict = DeskDesign.Validate(look.Design, DeskDesignContext.Unknown);
            Assert.True(verdict.Ok, $"{look.Id}: {string.Join("; ", verdict.Reasons)}");
            Assert.Empty(DeskLook.Rules(look.Design));
        }
    }

    [Fact]
    public void Each_fits_every_wallpaper_into_a_design_the_validator_passes()
    {
        foreach (var look in Catalog.Looks)
            foreach (var (name, wall) in Wallpapers())
            {
                var ctx = new DeskDesignContext(DeskLook.WindowsFonts.ToList(), wall, null, UsersDesk());
                var fit = DeskLookFit.Fit(look, ctx);
                var verdict = DeskDesign.Validate(fit.Design, ctx);
                Assert.True(verdict.Ok, $"{look.Id} on {name}: {string.Join("; ", verdict.Reasons)}");
                Assert.True(fit.Notes.Count <= 2, $"{look.Id} on {name}: {fit.Notes.Count} notes");
            }
    }

    [Fact]
    public void Each_still_applies_on_a_PC_with_no_fonts_of_its_own()
    {
        foreach (var look in Catalog.Looks)
        {
            var ctx = new DeskDesignContext(new[] { "Wingdings" }, Flat(0.03), null, UsersDesk());
            Assert.Equal(DeskDesignStatus.Ok, DeskDesign.Validate(DeskLookFit.Fit(look, ctx).Design, ctx).Status);
        }
    }

    [Fact]
    public void The_glow_belongs_to_Marquee_and_the_shadow_to_the_other_five()
    {
        foreach (var look in Catalog.Looks)
        {
            bool glows = look.Design.Glow is { On: true };
            Assert.Equal(look.Id == "marquee", glows);
            Assert.Equal(!glows, look.Design.Shadow);
        }
        Assert.Equal("#ffd08a", Catalog.Find("marquee")!.Design.Ink!.Colors.Single());
    }

    [Fact]
    public void Names_and_blurbs_are_the_gallerys_copy()
    {
        Assert.Equal(["Quiet corner", "Ice", "Marquee", "Terminal", "Editorial", "Dashboard"], Catalog.Looks.Select(l => l.Name));
        Assert.Equal("Clock up top, neon glow.", Catalog.Find("marquee")!.Blurb);
        Assert.All(Catalog.Looks, l => Assert.EndsWith(".", l.Blurb));
    }

    [Fact]
    public void Each_shows_a_clock_and_no_two_are_the_same_arrangement()
    {
        Assert.All(Catalog.Looks, l => Assert.True(l.Shows("clock"), l.Id));
        var arrangements = Catalog.Looks.Select(l => DeskDesignApply.Layout(l.Design.Layout!)).ToList();
        Assert.Equal(arrangements.Count, arrangements.Distinct().Count());
    }

    [Fact]
    public void The_cached_catalog_is_the_one_read()
    {
        Assert.Same(DeskLookFiles.Catalog, DeskLookFiles.Catalog);
        Assert.Equal(6, DeskLookFiles.Catalog.Looks.Count);
    }

    [SkippableFact]
    public void Every_font_a_look_names_is_a_family_this_Windows_lists()
    {
        // The spec's check: each font name against the installed-font list the validator reads (WPF's families). Client Windows
        // only: a server image (the CI runner) is not a Windows the package supports and lacks some of these.
        string? kind = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "InstallationType", null) as string;
        Skip.IfNot(kind == "Client", "not a client Windows");
        var installed = Fonts.SystemFontFamilies.Select(f => f.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var look in Catalog.Looks)
        {
            string font = look.Design.Font!.Family;
            Assert.True(BundledFonts.Bundled(font) is not null || installed.Contains(font), $"{look.Id}: {font} is not a family here");
            Assert.True(BundledFonts.Bundled(look.FontFallback) is not null || installed.Contains(look.FontFallback), $"{look.Id}: fallback {look.FontFallback} is not a family here");
        }
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskLookFilesTests"`
Expected: the build fails with "The name 'DeskLookFiles' does not exist".

- [ ] **Step 3: Write the six files**

Each file lists every piece Windows draws. The order of the shown pieces at one anchor is their stacking order, top to bottom.

`windows-dotnet/src/Sanduhr.App/Desk/Looks/quiet-corner.json`:

```json
{
  "schema_version": 1,
  "id": "quiet-corner",
  "name": "Quiet corner",
  "blurb": "A small clock and little else.",
  "font_fallback": "Segoe UI",
  "design": {
    "layout": {
      "clock": { "anchor": "bl", "size": 0.8 },
      "claude": { "anchor": "bl", "size": 0.8 },
      "nowPlaying": { "anchor": "bl", "size": 0.8 },
      "message": { "hidden": true },
      "meters": { "hidden": true },
      "telemetry": { "hidden": true },
      "weather": { "hidden": true },
      "watchers": { "hidden": true },
      "launcher": { "hidden": true },
      "glow": { "hidden": true },
      "companion": { "hidden": true }
    },
    "font": "Corbel",
    "ink": "auto",
    "plate": false,
    "shadow": true,
    "glow": false,
    "follows_theme": false
  }
}
```

`windows-dotnet/src/Sanduhr.App/Desk/Looks/ice.json`:

```json
{
  "schema_version": 1,
  "id": "ice",
  "name": "Ice",
  "blurb": "Handwriting in a cold gradient.",
  "font_fallback": "Segoe UI",
  "design": {
    "layout": {
      "message": { "anchor": "bl", "size": 1 },
      "clock": { "anchor": "bl", "size": 1.3 },
      "weather": { "anchor": "tr", "size": 1 },
      "claude": { "hidden": true },
      "meters": { "hidden": true },
      "nowPlaying": { "hidden": true },
      "telemetry": { "hidden": true },
      "watchers": { "hidden": true },
      "launcher": { "hidden": true },
      "glow": { "hidden": true },
      "companion": { "hidden": true }
    },
    "font": "EsteFont Pro",
    "ink": ["#8f5bd6", "#3a63e0", "#33fdff"],
    "plate": false,
    "shadow": true,
    "glow": false,
    "follows_theme": false
  }
}
```

`windows-dotnet/src/Sanduhr.App/Desk/Looks/marquee.json`:

```json
{
  "schema_version": 1,
  "id": "marquee",
  "name": "Marquee",
  "blurb": "Clock up top, neon glow.",
  "font_fallback": "Segoe UI",
  "design": {
    "layout": {
      "clock": { "anchor": "tc", "size": 1.4 },
      "message": { "anchor": "tc", "size": 1 },
      "nowPlaying": { "anchor": "bc", "size": 1 },
      "claude": { "hidden": true },
      "meters": { "hidden": true },
      "telemetry": { "hidden": true },
      "weather": { "hidden": true },
      "watchers": { "hidden": true },
      "launcher": { "hidden": true },
      "glow": { "hidden": true },
      "companion": { "hidden": true }
    },
    "font": "Bahnschrift",
    "ink": "#ffd08a",
    "plate": false,
    "shadow": false,
    "glow": 0.7,
    "follows_theme": false
  }
}
```

`windows-dotnet/src/Sanduhr.App/Desk/Looks/terminal.json`:

```json
{
  "schema_version": 1,
  "id": "terminal",
  "name": "Terminal",
  "blurb": "Monospace, green, all numbers.",
  "font_fallback": "Courier New",
  "design": {
    "layout": {
      "clock": { "anchor": "tl", "size": 0.9 },
      "claude": { "anchor": "tl", "size": 1, "style": "ticks" },
      "meters": { "anchor": "ml", "size": 1, "style": "ticks" },
      "telemetry": { "anchor": "bl", "size": 1, "style": "ticks" },
      "message": { "hidden": true },
      "nowPlaying": { "hidden": true },
      "weather": { "hidden": true },
      "watchers": { "hidden": true },
      "launcher": { "hidden": true },
      "glow": { "hidden": true },
      "companion": { "hidden": true }
    },
    "font": "Consolas",
    "ink": "#7cfc9a",
    "plate": false,
    "shadow": true,
    "glow": false,
    "follows_theme": false
  }
}
```

`windows-dotnet/src/Sanduhr.App/Desk/Looks/editorial.json`:

```json
{
  "schema_version": 1,
  "id": "editorial",
  "name": "Editorial",
  "blurb": "A big serif clock and a pull quote.",
  "font_fallback": "Times New Roman",
  "design": {
    "layout": {
      "clock": { "anchor": "tl", "size": 1.5 },
      "message": { "anchor": "tc", "size": 1.1 },
      "weather": { "anchor": "bl", "size": 1 },
      "claude": { "hidden": true },
      "meters": { "hidden": true },
      "nowPlaying": { "hidden": true },
      "telemetry": { "hidden": true },
      "watchers": { "hidden": true },
      "launcher": { "hidden": true },
      "glow": { "hidden": true },
      "companion": { "hidden": true }
    },
    "font": "Georgia",
    "ink": "light",
    "plate": false,
    "shadow": true,
    "glow": false,
    "follows_theme": false
  }
}
```

`windows-dotnet/src/Sanduhr.App/Desk/Looks/dashboard.json`:

```json
{
  "schema_version": 1,
  "id": "dashboard",
  "name": "Dashboard",
  "blurb": "Rings, watchers and a launcher on plates.",
  "font_fallback": "Segoe UI",
  "design": {
    "layout": {
      "clock": { "anchor": "tr", "size": 1 },
      "meters": { "anchor": "mr", "size": 1, "style": "ring" },
      "telemetry": { "anchor": "mr", "size": 1, "style": "ring" },
      "watchers": { "anchor": "br", "size": 1 },
      "launcher": { "anchor": "bc", "size": 1 },
      "message": { "hidden": true },
      "claude": { "hidden": true },
      "nowPlaying": { "hidden": true },
      "weather": { "hidden": true },
      "glow": { "hidden": true },
      "companion": { "hidden": true }
    },
    "font": "Segoe UI",
    "ink": "auto",
    "plate": true,
    "shadow": true,
    "glow": false,
    "follows_theme": false
  }
}
```

- [ ] **Step 4: Embed them and read them**

In `windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj`, add:

```xml
  <!-- The ready-made Desk looks (5.0 spec 5): six JSON files inside the app. Nothing is downloaded and nothing is written. -->
  <ItemGroup>
    <None Remove="Desk\Looks\*.json" />
    <EmbeddedResource Include="Desk\Looks\*.json" LogicalName="DeskLooks/%(Filename)%(Extension)" />
  </ItemGroup>
```

Create `windows-dotnet/src/Sanduhr.App/Desk/DeskLookFiles.cs`:

```csharp
using System.IO;
using System.Text;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// The ready-made looks that ship inside the app (5.0 spec 5): the embedded <c>DeskLooks/&lt;id&gt;.json</c> resources, read into Core's
/// <see cref="DeskLookCatalog"/>. A file that does not parse or breaks a rule cannot ship (a test loads all six); at run time a bad
/// one is skipped, named in the debug log, and the gallery shows the others.
/// </summary>
internal static class DeskLookFiles
{
    private const string Prefix = "DeskLooks/";
    private const string Suffix = ".json";

    private static readonly Lazy<DeskLookCatalog> Loaded = new(Read);

    /// <summary>The looks, read once.</summary>
    public static DeskLookCatalog Catalog => Loaded.Value;

    /// <summary>Reads the embedded files now.</summary>
    internal static DeskLookCatalog Read()
    {
        var assembly = typeof(DeskLookFiles).Assembly;
        var files = new List<(string Name, string Json)>();
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(Prefix, StringComparison.Ordinal) || !resource.EndsWith(Suffix, StringComparison.Ordinal)) continue;
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null) continue;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            files.Add((resource[Prefix.Length..^Suffix.Length], reader.ReadToEnd()));
        }
        var catalog = DeskLookCatalog.Load(files);
        foreach (var skipped in catalog.Skipped)
            System.Diagnostics.Debug.WriteLine($"[Sanduhr] Desk look skipped: {skipped}");
        return catalog;
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskLookFilesTests"`
Expected: PASS, with `Every_font_a_look_names_is_a_family_this_Windows_lists` passing on the owner's PC (not skipped). If it names a missing family, replace that font in the look's file with one from `DeskLook.WindowsFonts` that this PC lists, and say which in the commit message.

Then check the package tests see no surprise: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~PackageManifest|FullyQualifiedName~ThirdPartyNotices|FullyQualifiedName~Reset"`
Expected: PASS, unchanged. The looks add no file to the install and no font.

- [ ] **Step 6: Run every suite and commit**

```bash
cd windows-dotnet && dotnet test
git add src/Sanduhr.App/Desk/Looks src/Sanduhr.App/Desk/DeskLookFiles.cs src/Sanduhr.App/Sanduhr.App.csproj tests/Sanduhr.App.Tests/DeskLookFilesTests.cs
git commit -m "feat(windows): six ready-made Desk looks, embedded

Quiet corner, Ice, Marquee, Terminal, Editorial, Dashboard. Starting
compositions; positions and sizes are tuned at the audition.

Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>"
```

---

### Task 10: Applying a look, and the two rules that keep browsing tidy

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskDesignService.cs` (records near line 32; a new region before "applying and saved designs"; `SwitchToCore` at 712-741; `PutBack` at 744-750)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/WallpaperInk.cs` (a new method after `Overview`)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (after line 744)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DeskLookApplyTests.cs` (new)

**Interfaces:**
- Consumes: `DeskLookCatalog`, `DeskLook` (Task 7); `DeskLookFit.Fit`, `IsExactly`, `Untouched`, `LayoutOver`, `Needs`, `DeskLookFitResult`, `DeskLookNeed` (Task 8); `DeskLookFiles.Catalog` (Task 9); the service's existing `SaveCurrentFirst`, `WriteDesign`, `HostApplied`, `AfterChange`, `DismissReview`, `DrawersReviewing`, `CurrentDesign`.
- Produces, on `DeskDesignService`:
  - `public DesignActionResult ApplyLook(string id)`
  - `internal DeskLookCatalog LookCatalog { get; set; }`, `internal Func<WallpaperOverview?>? LookWallpaper { get; set; }`, `internal Func<bool> HasWatcher { get; set; }`
  - `internal DeskDesignContext LookContext()`
  - `internal DeskLooksSnapshot LooksSnapshot()` and `internal BitmapSource? LookPicture()`
  - `internal sealed record DeskLookCardData(DeskLook Look, DeskLookFitResult Fit, bool Current, IReadOnlyList<DeskLookNeed> Needs)` and `internal sealed record DeskLooksSnapshot(IReadOnlyList<DeskLookCardData> Cards, WallpaperOverview? Wallpaper)` (namespace `Sanduhr.App.Desk`)
- Produces, on `WallpaperInk`: `public BitmapSource? Picture()`.

`ApplyLook`, step by step (spec section 5, with rulings 1 to 5):

1. Disposed, or Reset has run: "Sanduhr is closing." An unknown id: "That look is gone." A drawers review is open: "Finish the drawers review on your Desk first." A design under review is dismissed.
2. Fit the look to this PC.
3. The Desk already is exactly that fit: with the Desk on, "<name> is your Desk already."; with it off, switch it on and answer "<name> is on your Desk. The Desk was switched on."
4. Rule A: unless the Desk is an untouched built-in look, save it first ("Before <name>", or the saved design that already is that Desk). A save that fails stops here: "The Desk could not be saved first, so nothing was changed."
5. Write the fitted design, its layout in the file's order (`DeskLookFit.LayoutOver`), through `WriteDesign`.
6. Switch the Desk on if it was off: `desk_enabled`, and setup's "A nicer desktop".
7. Rule B: no `KeepApplied`. Point Put my Desk back at what step 4 saved, when it saved; tell the host; answer "<name> is on your Desk." and, when step 6 did anything, " The Desk was switched on."

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/DeskLookApplyTests.cs`:

```csharp
using System.IO;
using System.Text.Json.Nodes;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// One click applies a ready-made look (5.0 spec 5, section 5): the Desk is written, the Desk it replaced is saved once however many
/// looks are tried, a built-in look takes no saved-design slot, Put my Desk back returns to the Desk from before the first look, and
/// the user's content is never touched. Over a scratch AppData, with the looks, the fonts and the wallpaper set by the test.
/// </summary>
public sealed class DeskLookApplyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-looks-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;
    private double _wall = 0.03;

    private const string UsersLayout = "clock:tr:1.4 claude:bl";

    public DeskLookApplyTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        _store = new SettingsStore(_paths);
        _store.SaveSetup(new SetupSettings(true, new Dictionary<string, bool>()));
        _store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = UsersLayout, Font = "Segoe UI" });
        _store.SaveLauncher(new LauncherModel(LauncherOrientation.Row, [LauncherButton.Create(LauncherKind.Link, "https://example.com", label: "Home")!]));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string LookJson(string id, string name, string clockAnchor, bool glow) => $$"""
        {
          "schema_version": 1, "id": "{{id}}", "name": "{{name}}", "blurb": "A test look.", "font_fallback": "Segoe UI",
          "design": {
            "layout": {
              "clock": { "anchor": "{{clockAnchor}}", "size": 1.3 },
              "message": { "anchor": "{{clockAnchor}}", "size": 1 },
              "launcher": { "anchor": "br", "size": 1 },
              "claude": { "hidden": true }, "meters": { "hidden": true }, "nowPlaying": { "hidden": true },
              "telemetry": { "hidden": true }, "weather": { "hidden": true }, "watchers": { "hidden": true },
              "glow": { "hidden": true }, "companion": { "hidden": true }
            },
            "font": "Georgia", "ink": "#ffd08a", "plate": false,
            "shadow": {{(glow ? "false" : "true")}}, "glow": {{(glow ? "0.7" : "false")}}, "follows_theme": false
          }
        }
        """;

    private static readonly DeskLookCatalog Catalog = DeskLookCatalog.Load([
        ("alpha", LookJson("alpha", "Alpha", "tc", glow: true)),
        ("beta", LookJson("beta", "Beta", "tl", glow: true)),
        ("gamma", LookJson("gamma", "Gamma", "bc", glow: false)),
    ]);

    private static WallpaperOverview Flat(double brightness)
    {
        var readings = DeskAnchors.All.Select(a => new AnchorReading(a, brightness, 0)).ToList();
        var summary = new WallpaperSummary([new DominantColor("#808080", 1)], brightness, readings);
        return new WallpaperOverview(summary.Dominant, brightness, [new WallpaperMonitorSummary(0, true, 2560, 1440, summary)]);
    }

    private void WithService(Action<DeskDesignService> body) => Sta.Run(() =>
    {
        using var service = new DeskDesignService(_paths, _store)
        {
            LookCatalog = Catalog,
            InstalledFonts = () => ["Georgia", "Segoe UI"],
            Monitors = () => [],
            DrawersReviewing = () => false,
        };
        service.LookWallpaper = () => Flat(_wall);
        body(service);
    });

    private string LayoutOf(string id) => DeskDesignApply.Layout(Catalog.Find(id)!.Design.Layout!);

    /// <summary>A layout as its placements, whatever order the string lists them in: Put my Desk back merges a saved design in, and
    /// a merge orders a stack by Settings order. A look's own order is asserted on the raw string instead.</summary>
    private static string Shape(string layout) => string.Join(" ", new DeskArrangement(layout).Pieces
        .OrderBy(p => p.Widget, StringComparer.Ordinal)
        .Select(p => $"{p.Widget}:{p.Anchor}:{p.Scale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}"));

    [Fact]
    public void The_test_looks_are_good_looks() => Assert.Empty(Catalog.Skipped);

    [Fact]
    public void A_click_writes_the_look_saves_the_Desk_it_replaced_and_points_put_back_at_it() => WithService(s =>
    {
        var launcher = _store.LoadLauncher().ToJson().ToJsonString();

        var result = s.ApplyLook("alpha");

        Assert.True(result.Ok);
        Assert.Equal("Alpha is on your Desk.", result.Message);
        Assert.Equal(LayoutOf("alpha"), _store.LoadDesk().Layout);
        Assert.Equal("Georgia", _store.LoadDesk().Font);
        var ink = _store.LoadDeskInk();
        Assert.Equal(DeskInkMode.Custom, ink.Mode);
        Assert.Equal("ffd08a", ink.Color);
        Assert.True(ink.Glow);
        Assert.Equal(0.7, ink.GlowStrength);
        Assert.False(ink.Shadow);
        Assert.False(_store.LoadDeskFollowsTheme());
        // The user's content is theirs.
        Assert.Equal(launcher, _store.LoadLauncher().ToJson().ToJsonString());

        var saved = Assert.Single(s.Designs());
        Assert.Equal("Before Alpha", saved.Name);
        Assert.True(s.CanPutBack);
    });

    [Fact]
    public void Flipping_through_looks_keeps_one_backup_and_put_back_returns_to_the_first_Desk() => WithService(s =>
    {
        Assert.True(s.ApplyLook("alpha").Ok);
        Assert.True(s.ApplyLook("beta").Ok);
        Assert.True(s.ApplyLook("gamma").Ok);
        Assert.Equal(LayoutOf("gamma"), _store.LoadDesk().Layout);
        // Rule A: one backup. Rule B: no built-in look in the saved designs.
        Assert.Equal(["Before Alpha"], s.Designs().Select(d => d.Name));

        var back = s.PutBack();
        Assert.True(back.Ok);
        Assert.Equal("Your Desk is back as it was.", back.Message);
        Assert.Equal(Shape(UsersLayout), Shape(_store.LoadDesk().Layout));
        Assert.Equal("Segoe UI", _store.LoadDesk().Font);
        Assert.False(_store.LoadDeskInk().Glow);
        Assert.Single(s.Designs());

        // A second press no longer toggles: it came from a look, which was never saved.
        var again = s.PutBack();
        Assert.True(again.Ok);
        Assert.Equal("Your Desk is already as it was.", again.Message);
        Assert.Equal(Shape(UsersLayout), Shape(_store.LoadDesk().Layout));
    });

    [Fact]
    public void A_look_the_user_changed_is_their_own_Desk_and_is_saved_when_the_next_one_replaces_it() => WithService(s =>
    {
        s.ApplyLook("alpha");
        var desk = _store.LoadDesk();
        var moved = new DeskArrangement(desk.Layout);
        moved.Place("clock", DeskAnchor.BR);
        _store.SaveDesk(desk with { Layout = moved.Layout });

        s.ApplyLook("beta");

        Assert.Equal(["Before Alpha", "Before Beta"], s.Designs().Select(d => d.Name).Order());
        // Put my Desk back now returns to the changed Alpha, the Desk that was just replaced.
        s.PutBack();
        Assert.Equal(Shape(moved.Layout), Shape(_store.LoadDesk().Layout));
    });

    [Fact]
    public void Changing_only_the_users_own_content_leaves_the_look_untouched() => WithService(s =>
    {
        s.ApplyLook("alpha");
        _store.SaveLauncher(LauncherModel.Empty);
        s.ApplyLook("beta");
        Assert.Single(s.Designs());
    });

    [Fact]
    public void The_same_look_twice_changes_nothing_and_says_so() => WithService(s =>
    {
        s.ApplyLook("alpha");
        var again = s.ApplyLook("alpha");
        Assert.True(again.Ok);
        Assert.Equal("Alpha is your Desk already.", again.Message);
        Assert.Single(s.Designs());
    });

    [Fact]
    public void A_click_switches_the_Desk_on() => WithService(s =>
    {
        _store.SaveDesk(_store.LoadDesk() with { Enabled = false });
        var result = s.ApplyLook("alpha");
        Assert.Equal("Alpha is on your Desk. The Desk was switched on.", result.Message);
        Assert.True(_store.LoadDesk().Enabled);
        Assert.Equal(LayoutOf("alpha"), _store.LoadDesk().Layout);
    });

    [Fact]
    public void A_click_ticks_a_nicer_desktop_again_when_setup_left_only_drawers() => WithService(s =>
    {
        _store.SaveSetup(new SetupSettings(false, new Dictionary<string, bool>()));
        var result = s.ApplyLook("alpha");
        Assert.EndsWith("The Desk was switched on.", result.Message);
        Assert.True(_store.LoadSetup()!.Desktop);
    });

    [Fact]
    public void A_Desk_that_already_is_the_look_but_is_off_comes_on() => WithService(s =>
    {
        s.ApplyLook("alpha");
        _store.SaveDesk(_store.LoadDesk() with { Enabled = false });
        var result = s.ApplyLook("alpha");
        Assert.Equal("Alpha is on your Desk. The Desk was switched on.", result.Message);
        Assert.True(_store.LoadDesk().Enabled);
        Assert.Single(s.Designs());
    });

    [Fact]
    public void A_Desk_that_cannot_be_saved_first_is_left_as_it_is() => WithService(s =>
    {
        File.WriteAllText(_paths.DesignsDir, "not a folder");   // streamer: notname - a scratch path in a test
        var result = s.ApplyLook("alpha");
        Assert.False(result.Ok);
        Assert.Equal("The Desk could not be saved first, so nothing was changed.", result.Message);
        Assert.Equal(Shape(UsersLayout), Shape(_store.LoadDesk().Layout));
        Assert.False(_store.LoadDeskInk().Glow);
        Assert.False(s.CanPutBack);
    });

    [Fact]
    public void A_drawers_review_refuses_the_click_and_changes_nothing() => WithService(s =>
    {
        s.DrawersReviewing = () => true;
        var result = s.ApplyLook("alpha");
        Assert.False(result.Ok);
        Assert.Equal("Finish the drawers review on your Desk first.", result.Message);
        Assert.Equal(Shape(UsersLayout), Shape(_store.LoadDesk().Layout));
        Assert.Empty(s.Designs());
    });

    private sealed class FakeHost : IDeskDesignHost
    {
        public int Shown, Ended, Applied;
        public void ShowPreview(DeskDesignSettings settings, ThemeDefinition? theme, DeskReviewModel model, Action apply, Action dismiss) => Shown++;
        public void EndPreview() => Ended++;
        void IDeskDesignHost.Applied(string? themeKey) => Applied++;
    }

    [Fact]
    public void A_design_under_review_is_dismissed_by_the_click() => WithService(s =>
    {
        var host = new FakeHost();
        s.Host = host;
        s.ProposePasted(s.ValidatePasted(JsonNode.Parse("""{"plate":true}""")!));
        Assert.True(s.Reviewing);

        var result = s.ApplyLook("alpha");

        Assert.True(result.Ok);
        Assert.False(s.Reviewing);
        Assert.True(host.Ended >= 1);
        Assert.Equal(1, host.Applied);
        Assert.False(_store.LoadDesk().Plate);   // the reviewed design was never written
        s.Host = null;
    });

    [Fact]
    public void A_piece_on_an_unplugged_monitor_keeps_its_monitor() => WithService(s =>
    {
        const string gone = @"\\?\DISPLAY#GONE#1";
        _store.SaveDesk(_store.LoadDesk() with { Layout = new DeskArrangement([new DeskPlacement("clock", DeskAnchor.TR, 1.4, gone)]).Layout });
        s.Monitors = () => [new DeskMonitorInfo(@"\\?\DISPLAY#HERE#1", "1920×1080, main", 1920, 1080, true, 0, 0)];

        Assert.True(s.ApplyLook("alpha").Ok);

        var clock = new DeskArrangement(_store.LoadDesk().Layout).Placement("clock")!;
        Assert.Equal(DeskAnchor.TC, clock.Anchor);
        Assert.True(DeskMonitors.Same(gone, clock.Monitor));
    });

    [Fact]
    public void After_the_wallpaper_changes_the_same_look_is_fitted_again_and_nothing_more_is_saved() => WithService(s =>
    {
        s.ApplyLook("alpha");
        Assert.Equal(DeskInkMode.Custom, _store.LoadDeskInk().Mode);

        _wall = 0.98;
        var result = s.ApplyLook("alpha");
        Assert.Equal("Alpha is on your Desk.", result.Message);
        Assert.Equal(DeskInkMode.Auto, _store.LoadDeskInk().Mode);
        Assert.True(_store.LoadDeskInk().Glow);

        // Still that look: the next one saves nothing new.
        s.ApplyLook("beta");
        Assert.Single(s.Designs());
    });

    [Fact]
    public void A_missing_font_takes_the_fallback() => WithService(s =>
    {
        s.InstalledFonts = () => ["Segoe UI"];   // before the service first reads the list
        s.ApplyLook("alpha");
        Assert.Equal("Segoe UI", _store.LoadDesk().Font);
    });

    [Fact]
    public void A_look_that_is_gone_and_a_service_that_is_closing_say_so()
    {
        WithService(s => Assert.Equal("That look is gone.", s.ApplyLook("nope").Message));
        Sta.Run(() =>
        {
            var service = new DeskDesignService(_paths, _store) { LookCatalog = Catalog };
            service.Dispose();
            var result = service.ApplyLook("alpha");
            Assert.False(result.Ok);
            Assert.Equal(Shape(UsersLayout), Shape(_store.LoadDesk().Layout));
        });
    }

    [Fact]
    public void Between_two_of_the_users_own_designs_put_back_still_toggles() => WithService(s =>
    {
        Assert.True(s.SaveBeforeChange("a change"));
        string first = s.Designs().Single().Id;
        _store.SaveDesk(_store.LoadDesk() with { Layout = "clock:bl" });
        Assert.True(s.SaveBeforeChange("another"));
        Assert.Equal(2, s.Designs().Count);

        Assert.True(s.SwitchTo(first).Ok);
        Assert.Equal(Shape(UsersLayout), Shape(_store.LoadDesk().Layout));
        Assert.Equal("Your Desk is back as it was.", s.PutBack().Message);
        Assert.Equal(Shape("clock:bl"), Shape(_store.LoadDesk().Layout));
        Assert.Equal("Your Desk is back as it was.", s.PutBack().Message);
        Assert.Equal(Shape(UsersLayout), Shape(_store.LoadDesk().Layout));
    });

    [Fact]
    public void Leaving_an_untouched_look_for_a_saved_design_saves_nothing_and_keeps_the_pointer() => WithService(s =>
    {
        Assert.True(s.SaveBeforeChange("a change"));   // the user's Desk, saved
        _store.SaveDesk(_store.LoadDesk() with { Layout = "clock:bl" });
        s.ApplyLook("alpha");                          // saves "Before Alpha" (clock:bl) and points at it
        string usersFirst = s.Designs().Single(d => d.Name != "Before Alpha").Id;

        Assert.True(s.SwitchTo(usersFirst).Ok);        // leaving Alpha, untouched

        Assert.Equal(2, s.Designs().Count);
        s.PutBack();
        Assert.Equal(Shape("clock:bl"), Shape(_store.LoadDesk().Layout));   // still the Desk from before the look
    });

    [Fact]
    public void The_gallerys_data_marks_the_look_on_the_Desk_and_says_what_a_piece_needs() => WithService(s =>
    {
        _store.SaveLauncher(LauncherModel.Empty);
        var before = s.LooksSnapshot();
        Assert.Equal(["alpha", "beta", "gamma"], before.Cards.Select(c => c.Look.Id));
        Assert.DoesNotContain(before.Cards, c => c.Current);
        Assert.All(before.Cards, c => Assert.Equal(["The Launcher needs buttons."], c.Needs.Select(n => n.Line)));
        Assert.Equal(0.03, before.Wallpaper!.Brightness);

        s.ApplyLook("beta");
        Assert.Equal(["beta"], s.LooksSnapshot().Cards.Where(c => c.Current).Select(c => c.Look.Id));

        _wall = 0.98;
        var bright = s.LooksSnapshot().Cards.Single(c => c.Look.Id == "alpha");
        Assert.Equal([DeskLookFit.AutoInkNote], bright.Fit.Notes);

        // A Desk that is off shows no look as on it.
        _store.SaveDesk(_store.LoadDesk() with { Enabled = false });
        Assert.DoesNotContain(s.LooksSnapshot().Cards, c => c.Current);
    });
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskLookApplyTests"`
Expected: the build fails with "'DeskDesignService' does not contain a definition for 'LookCatalog'".

- [ ] **Step 3: The wallpaper as a picture**

In `windows-dotnet/src/Sanduhr.App/Desk/WallpaperInk.cs`, after `Overview()`, add:

```csharp
    /// <summary>
    /// The primary monitor's wallpaper as that monitor shows it (fit mode and background color), about
    /// <see cref="SummaryWidth"/> pixels wide, for the ready-made look cards (5.0 spec 5): the same decoded image the summary
    /// reads, drawn once and frozen. Null when the desktop is a plain color, when the picture cannot be read (a slideshow between
    /// pictures, a file that is gone), or on any failure; the caller then falls back to a color. It stays in memory: nothing is
    /// written, and nothing leaves this process.
    /// </summary>
    public BitmapSource? Picture()
    {
        try
        {
            if (!_read)
            {
                _info = DeskSensorsNative.Wallpaper();
                _read = true;
            }
            if (_info is not { } info || info.Monitors.Count == 0) return null;
            var m = info.Monitors.FirstOrDefault(x => x.Left == 0 && x.Top == 0) ?? info.Monitors[0];
            if (m.Path is null) return null;
            var monitor = new PxRect(m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top);
            if (monitor.IsEmpty || Decode(m.Path) is not { } image) return null;
            var bg = ((byte)(info.Background & 0xFF), (byte)((info.Background >> 8) & 0xFF), (byte)((info.Background >> 16) & 0xFF));
            var drawn = WallpaperRender.Monitor(image.Pixels, image.Width, image.Height, image.Stride,
                image.NaturalWidth, image.NaturalHeight, monitor, (WallpaperPosition)info.Position, VirtualScreen(info.Monitors), bg, SummaryWidth);
            if (drawn is not { } d) return null;
            var picture = BitmapSource.Create(d.Width, d.Height, 96, 96, PixelFormats.Bgr32, null, d.Bgra, d.Width * 4);
            picture.Freeze();
            return picture;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException
                                      or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
```

This mirrors the loop body of `Overview()` for one monitor; keep its calls identical to the ones there (the same `Decode`, `WallpaperRender.Monitor` and `VirtualScreen`). The lines that handle `m.Path` are IO on a file location, never drawn; if `StreamerReachTests` or a source scan asks for it, mark them the way the neighbouring lines in this file are marked.

- [ ] **Step 4: The service**

In `windows-dotnet/src/Sanduhr.App/Desk/DeskDesignService.cs`:

1. Add `using System.Windows.Media.Imaging;` to the usings.

2. After the `DesignActionResult` record, add:

```csharp
/// <summary>One ready-made look as the gallery draws it: the look, how it fits this PC (the design to draw and the notes), whether the
/// Desk is that look now, and what its shown pieces still need.</summary>
internal sealed record DeskLookCardData(DeskLook Look, DeskLookFitResult Fit, bool Current, IReadOnlyList<DeskLookNeed> Needs);

/// <summary>The gallery's data: a card per look in the gallery's order, and the wallpaper's summary (null when unknown) for a card's
/// backdrop color and its automatic ink.</summary>
internal sealed record DeskLooksSnapshot(IReadOnlyList<DeskLookCardData> Cards, WallpaperOverview? Wallpaper);
```

3. Directly above the comment line `// -- applying and saved designs ---`, add this region:

```csharp
    // -- ready-made looks (5.0 spec 5) ---------------------------------------------------------------------

    /// <summary>The ready-made looks (tests set it).</summary>
    internal DeskLookCatalog LookCatalog { get; set; } = DeskLookFiles.Catalog;

    /// <summary>The wallpaper the looks are fitted to (tests set it); null reads this PC's.</summary>
    internal Func<WallpaperOverview?>? LookWallpaper { get; set; }

    /// <summary>Whether a watcher is on the board, for a card's "Watchers needs a watcher." (the App sets it).</summary>
    internal Func<bool> HasWatcher { get; set; } = () => false;

    /// <summary>The installed fonts as a look's rules read them: null when the list could not be read.</summary>
    private IReadOnlyCollection<string>? FontsKnown()
    {
        _fonts ??= InstalledFonts();
        return _fonts.Count == 0 ? null : _fonts;
    }

    /// <summary>What fitting a look knows about this PC: the fonts, the wallpaper and the Desk as it is. No Start-menu list (a look
    /// carries no Launcher) and no connected-monitor list (a look names no monitor).</summary>
    internal DeskDesignContext LookContext()
    {
        WallpaperOverview? wallpaper;
        if (LookWallpaper is { } read) wallpaper = read();
        else
        {
            _wallpaper.Refresh();
            wallpaper = _wallpaper.Overview().Overview;
        }
        return new DeskDesignContext(FontsKnown(), wallpaper, null, CurrentDesign(), null);
    }

    /// <summary>The primary monitor's wallpaper, small, for the look cards; null when there is no picture to show. In memory only.</summary>
    internal BitmapSource? LookPicture()
    {
        if (LookWallpaper is not null) return null;   // a test's wallpaper has no picture
        _wallpaper.Refresh();
        return _wallpaper.Picture();
    }

    /// <summary>Every look fitted to this PC now, for the gallery. The look that is the Desk is marked only while the Desk is on.</summary>
    internal DeskLooksSnapshot LooksSnapshot()
    {
        var context = LookContext();
        bool weather = _store.LoadDeskWeather().Place is not null;
        bool launcher = !_store.LoadLauncher().IsEmpty;
        bool watcher = HasWatcher();
        var on = DeskIsOn() ? DeskLookFit.Untouched(LookCatalog.Looks, context.Current!, context.InstalledFonts) : null;
        var cards = LookCatalog.Looks
            .Select(l => new DeskLookCardData(l, DeskLookFit.Fit(l, context), ReferenceEquals(l, on), DeskLookFit.Needs(l, weather, launcher, watcher)))
            .ToList();
        return new DeskLooksSnapshot(cards, context.Wallpaper);
    }

    /// <summary>The Desk draws its pieces: the Desk switch is on, and setup's "A nicer desktop" was not unticked.</summary>
    private bool DeskIsOn() => _store.LoadDesk().Enabled && _store.LoadSetup() is not { Desktop: false };

    private void SwitchDeskOn()
    {
        if (_store.LoadSetup() is { Desktop: false } setup) _store.SaveSetup(setup with { Desktop = true });
        var desk = _store.LoadDesk();
        if (!desk.Enabled) _store.SaveDesk(desk with { Enabled = true });
    }

    /// <summary>The Desk is a built-in look nobody has changed: it is in the gallery, so it is never saved (rule A).</summary>
    private bool OnALook(DeskDesign current) => DeskLookFit.Untouched(LookCatalog.Looks, current, FontsKnown()) is not null;

    /// <summary>
    /// Applies a ready-made look in one click (5.0 spec 5, section 5). No review bar: the bar exists to check an agent's work, and
    /// these looks are ours. The look is fitted to this PC first, so what is written always passes the Desk's rules. Two rules keep
    /// browsing from filling the saved designs: leaving an untouched look saves nothing (so Put my Desk back still returns to the
    /// Desk from before the first look), and a built-in look takes no saved-design slot. The Desk is switched on if it was off;
    /// Put my Desk back does not switch it off again.
    /// </summary>
    public DesignActionResult ApplyLook(string id)
    {
        try { return ApplyLookCore(id); }
        catch (Exception e) when (IsRecoverable(e))
        {
            Log("look", e);
            return new(false, "That did not work (" + e.GetType().Name + "). Your Desk was left as it is.");
        }
    }

    private DesignActionResult ApplyLookCore(string id)
    {
        if (_disposed || ResetGuard.Done) return new(false, "Sanduhr is closing.");
        if (LookCatalog.Find(id) is not { } look) return new(false, "That look is gone.");
        if (DrawersReviewing()) return new(false, "Finish the drawers review on your Desk first.");
        if (_review is not null) DismissReview();

        var context = LookContext();
        var before = context.Current!;
        var fit = DeskLookFit.Fit(look, context);
        bool wasOff = !DeskIsOn();
        if (DeskLookFit.IsExactly(look, fit, before))
        {
            if (!wasOff) return new(true, $"{look.Name} is your Desk already.");
            SwitchDeskOn();
            HostApplied(null);
            AfterChange();
            return new(true, $"{look.Name} is on your Desk. The Desk was switched on.");
        }

        SavedDesign? saved = null;
        if (!OnALook(before))
        {
            saved = SaveCurrentFirst(before, look.Name);
            if (saved is null) return new(false, "The Desk could not be saved first, so nothing was changed.");
        }
        try
        {
            // The layout is the file's own order, each piece on the monitor the user gave it; everything else merges as any design does.
            var merged = DeskDesign.Merge(before, fit.Design) with { Layout = [.. DeskLookFit.LayoutOver(look, before)] };
            WriteDesign(merged, withTheme: false);
            if (wasOff) SwitchDeskOn();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(false, "The look could not be written (" + e.GetType().Name + ").");
        }
        if (saved is not null) _store.SaveDeskPutBack(saved.Id);
        HostApplied(null);
        AfterChange();
        return new(true, $"{look.Name} is on your Desk." + (wasOff ? " The Desk was switched on." : ""));
    }
```

4. Rule A in `SwitchToCore`. Replace

```csharp
        var previous = SaveCurrentFirst(before, target.Name);
        if (previous is null) return new(false, "The Desk could not be saved first, so nothing was changed.");
```

with

```csharp
        // Rule A (5.0 spec 5): an untouched built-in look is never saved, and "Put my Desk back" keeps pointing where it did.
        SavedDesign? previous = null;
        if (!OnALook(before))
        {
            previous = SaveCurrentFirst(before, target.Name);
            if (previous is null) return new(false, "The Desk could not be saved first, so nothing was changed.");
        }
```

and, further down in the same method, replace `_store.SaveDeskPutBack(previous.Id);` with `if (previous is not null) _store.SaveDeskPutBack(previous.Id);`.

5. The second press. Replace the body of `PutBack` with:

```csharp
    public DesignActionResult PutBack()
    {
        if (_store.LoadDeskPutBack() is not { } id || _designs.Load(id) is not { } target)
            return new(false, "There is nothing to put back yet.");
        // Back from a look, the Desk already is the saved one (a look is never saved, so there is nothing to toggle to).
        if (!_disposed && !ResetGuard.Done && DeskDesigns.FindSame([target], CurrentDesign()) is not null)
            return new(true, "Your Desk is already as it was.");
        var result = SwitchTo(id);
        return result.Ok ? new(true, "Your Desk is back as it was.") : result;
    }
```

   and extend its doc comment: `Pressed again after it returned from a ready-made look, it changes nothing and says so.`

6. In the class doc comment's third bullet, after "and "Put my Desk back"", add `, and applies a ready-made look (<see cref="ApplyLook"/>)`.

- [ ] **Step 5: Tell the service about watchers**

In `windows-dotnet/src/Sanduhr.App/App.xaml.cs`, after `Desk.DeskDesignService.Current = _deskDesign;` (line 744), add:

```csharp
        // A look card says "Watchers needs a watcher." while the board is empty (5.0 spec 5).
        _deskDesign.HasWatcher = () => _watchers?.Shown.Count > 0;
```

`_watchers` is the app's `Desk.WatcherStore?` field.

- [ ] **Step 6: Run the tests**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskLookApplyTests|FullyQualifiedName~DeskDesign|FullyQualifiedName~DesignsSettings"`
Expected: PASS. The older design tests prove `SwitchTo` and Put my Desk back behave as before between the user's own designs. If one of them pinned "a second Put my Desk back toggles" on a Desk that is one of the six shipped looks, it now meets rule A; none should, since their Desks are not looks.

- [ ] **Step 7: Run every suite and commit**

```bash
cd windows-dotnet && dotnet test
git add src/Sanduhr.App tests/Sanduhr.App.Tests/DeskLookApplyTests.cs
git commit -m "feat(windows): one click applies a ready-made look

Fitted to this PC first. Flipping through looks keeps one backup and a
built-in look takes no saved-design slot. The Desk is switched on.

Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>"
```

---

### Task 11: The gallery in Settings

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (beside `PiecesSection`, line 1130), `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (the Desk anchors near line 99, the Desk entries near 287, `JumpLinks` at 907)
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/DeskLooksViewModel.cs`
- Create: `windows-dotnet/src/Sanduhr.App/Views/DeskLookCard.cs`
- Create: `windows-dotnet/src/Sanduhr.App/Views/DeskLooksPanel.xaml`, `DeskLooksPanel.xaml.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs` (beside `Designs`, lines 253-260 and 342)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (after the Desk switch block, line 2467)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskDesignService.cs` (one event), `windows-dotnet/src/Sanduhr.App/App.xaml.cs:764`
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DeskLooksGalleryTests.cs` (new), `windows-dotnet/tests/Sanduhr.Tests/SettingsIndexTests.cs`

**Interfaces:**
- Consumes: `DeskDesignService.ApplyLook`, `LooksSnapshot`, `LookPicture`, `PutBack`, `CanPutBack`, `DeskLookCardData`, `DeskLooksSnapshot` (Task 10); `DeskGlowLayer.Middle` (Task 4); `DeskInk.Resolve`, `DeskDesignApply.Ink`, `DeskDesignApply.FontSetting`; `DeskGeometry.Point`; `DeskFonts.Families`; `SettingsLinkTo`, `LiveRegion.Announces`.
- Produces:
  - `SettingsNames.LooksSection = "Looks"`, `LooksRowName = "Ready-made looks"`, `OnYourDesk = "On your Desk"`, `LooksCaption`.
  - `SettingsIndex.DeskLooks = "looks"`; the Desk page's first jump link.
  - `public sealed record DeskLookNeedLink(string Text, string Link)`; `public sealed partial class DeskLookCardViewModel` with `Id`, `Name`, `Blurb`, `Fitted` (`DeskDesign`), `Notes` (`IReadOnlyList<string>`), `Needs` (`IReadOnlyList<DeskLookNeedLink>`), `Current` (`bool`), `Wallpaper` (`ImageSource?`), `BackdropHex` (`string`), `Brightness` (`double?`), `AccessibleName`, `ApplyCommand`; `public sealed partial class DeskLooksViewModel` with `Cards`, `Status`, `HasStatus`, `CanPutBack`, `PutBackCommand`, `Reload()`.
  - `DeskSettingsViewModel.Looks` (`DeskLooksViewModel`).
  - `internal sealed class DeskLookCard : Canvas` with dependency properties `Design`, `Wallpaper`, `BackdropHex`, `Brightness`, `LookName` and constants `CardWidth = 208`, `CardHeight = 117`.
  - `public sealed class DeskLooksRow : ItemsControl` (a Group to UI Automation, named "Ready-made looks").
  - `DeskDesignService.LooksChanged` (`event Action?`).

How a card is drawn (spec section 6, "A card"): the top is the user's wallpaper (or the summary's first dominant color, or the theme's background); over it the look's arrangement in the fitted look's ink, drawn with the same Core call the layout map uses (`DeskGeometry.Point`). **The look's name stands where the look puts its clock**, in the look's font, with its glow when it has one; every other shown piece is a short bar. The name and the map never collide that way. Below: the name, the blurb, at most two notes (fit notes first, then one line of what shown pieces need, each a link), and "On your Desk" with a check mark on the current one.

Cards are buttons, not a list: Tab and the arrow keys move focus only; Enter, Space, a click or UI Automation's Invoke applies. After a click the same card objects are updated in place, so the keyboard stays on the card that was pressed.

- [ ] **Step 1: Write the failing tests**

In `windows-dotnet/tests/Sanduhr.Tests/SettingsIndexTests.cs`, add:

```csharp
    [Fact]
    public void The_Desk_pages_first_jump_link_is_Looks()
    {
        var links = SettingsIndex.JumpLinks(SettingsPage.Desk);
        Assert.Equal((SettingsNames.LooksSection, SettingsIndex.DeskLooks), links[0]);
        Assert.Equal(5, links.Count);
    }

    [Theory]
    [InlineData("looks")]
    [InlineData("gallery")]
    [InlineData("presets")]
    [InlineData("styles")]
    [InlineData("Marquee")]
    public void Search_finds_the_looks(string query) =>
        Assert.Contains(SettingsIndex.Search(query), h => h.Entry.Page == SettingsPage.Desk && h.Entry.Anchor == SettingsIndex.DeskLooks);
```

`SettingsIndex.Search` returns `Hit`s; a hit's `Entry` is a `SettingsEntry(Title, Page, Anchor, Synonyms, …)`.

Create `windows-dotnet/tests/Sanduhr.App.Tests/DeskLooksGalleryTests.cs`:

```csharp
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Sanduhr.App.Tests;

/// <summary>The Looks section of Settings, Desk (5.0 spec 5, section 6): six cards that are buttons, their accessible names, the look
/// on the Desk marked, a live status line, and a card drawn from the fitted look.</summary>
public sealed class DeskLooksGalleryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-gallery-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;
    private double _wall = 0.03;

    public DeskLooksGalleryTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        _store = new SettingsStore(_paths);
        _store.SaveSetup(new SetupSettings(true, new Dictionary<string, bool>()));
        _store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:tr:1.4 claude:bl", Font = "Segoe UI" });
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>A look that shows the clock and the message at one anchor and <paramref name="extraShown"/> bottom right.</summary>
    private static string LookJson(string id, string name, string clockAnchor, string extraShown)
    {
        string hidden = string.Join(", ", new[] { "claude", "meters", "nowPlaying", "telemetry", "weather", "watchers", "launcher", "glow", "companion" }
            .Where(p => p != extraShown).Select(p => "\"" + p + "\": { \"hidden\": true }"));
        return $$"""
            {
              "schema_version": 1, "id": "{{id}}", "name": "{{name}}", "blurb": "A test look.", "font_fallback": "Segoe UI",
              "design": {
                "layout": {
                  "clock": { "anchor": "{{clockAnchor}}", "size": 1.3 },
                  "message": { "anchor": "{{clockAnchor}}", "size": 1 },
                  "{{extraShown}}": { "anchor": "br", "size": 1 },
                  {{hidden}}
                },
                "font": "Georgia", "ink": "#ffd08a", "plate": false, "shadow": false, "glow": 0.7, "follows_theme": false
              }
            }
            """;
    }

    private static readonly DeskLookCatalog Catalog = DeskLookCatalog.Load([
        ("alpha", LookJson("alpha", "Alpha", "tc", "launcher")),
        ("beta", LookJson("beta", "Beta", "tl", "weather")),
        ("gamma", LookJson("gamma", "Gamma", "bc", "watchers")),
    ]);

    private static WallpaperOverview Flat(double brightness)
    {
        var readings = DeskAnchors.All.Select(a => new AnchorReading(a, brightness, 0)).ToList();
        var summary = new WallpaperSummary([new DominantColor("#203040", 1)], brightness, readings);
        return new WallpaperOverview(summary.Dominant, brightness, [new WallpaperMonitorSummary(0, true, 2560, 1440, summary)]);
    }

    private void WithGallery(Action<DeskLooksViewModel, DeskDesignService> body) => Sta.Run(() =>
    {
        using var service = new DeskDesignService(_paths, _store)
        {
            LookCatalog = Catalog,
            InstalledFonts = () => ["Georgia", "Segoe UI"],
            Monitors = () => [],
            DrawersReviewing = () => false,
        };
        service.LookWallpaper = () => Flat(_wall);
        body(new DeskLooksViewModel(() => service), service);
    });

    private static IEnumerable<T> Visuals<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T hit) yield return hit;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var found in Visuals<T>(VisualTreeHelper.GetChild(node, i)))
                yield return found;
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, () => { });

    [Fact]
    public void The_test_looks_are_good_looks() => Assert.Empty(Catalog.Skipped);

    // ---- the view model ----

    [Fact]
    public void There_is_a_card_per_look_with_what_a_screen_reader_says() => WithGallery((vm, _) =>
    {
        Assert.Equal(["Alpha", "Beta", "Gamma"], vm.Cards.Select(c => c.Name));
        var alpha = vm.Cards[0];
        Assert.Equal("A test look.", alpha.Blurb);
        Assert.False(alpha.Current);
        Assert.Empty(alpha.Notes);
        Assert.Equal(["The Launcher needs buttons."], alpha.Needs.Select(n => n.Text));
        Assert.Equal("Alpha look. A test look. The Launcher needs buttons.", alpha.AccessibleName);
        Assert.Equal("Weather needs a place.", vm.Cards[1].Needs.Single().Text);
        Assert.Equal("Watchers needs a watcher.", vm.Cards[2].Needs.Single().Text);
        Assert.All(vm.Cards.SelectMany(c => c.Needs), n => Assert.False(string.IsNullOrEmpty(n.Link)));
        Assert.Equal(SettingsIndex.PageId(SettingsPage.Launcher) + "#" + SettingsIndex.LauncherButtons, alpha.Needs.Single().Link);
        Assert.Equal("203040", alpha.BackdropHex);
        Assert.Equal(0.03, alpha.Brightness);
        Assert.Null(alpha.Wallpaper);   // a test's wallpaper has no picture
        Assert.False(vm.HasStatus);
        Assert.False(vm.CanPutBack);
    });

    [Fact]
    public void A_card_applies_its_look_marks_it_and_says_so_without_replacing_the_cards() => WithGallery((vm, _) =>
    {
        var cards = vm.Cards.ToList();
        var statuses = new List<string>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Status)) statuses.Add(vm.Status); };

        vm.Cards[1].ApplyCommand.Execute(null);

        Assert.Equal("Beta is on your Desk.", vm.Status);
        Assert.Equal(["", "Beta is on your Desk."], statuses);   // cleared first, so the same line twice is still announced
        Assert.True(vm.Cards[1].Current);
        Assert.False(vm.Cards[0].Current);
        Assert.Equal("Beta look. A test look. Weather needs a place. On your Desk.", vm.Cards[1].AccessibleName);
        Assert.True(cards.SequenceEqual(vm.Cards));   // the same objects: the keyboard stays on the pressed card
        Assert.True(vm.CanPutBack);
        Assert.True(vm.PutBackCommand.CanExecute(null));

        vm.Cards[1].ApplyCommand.Execute(null);
        Assert.Equal("Beta is your Desk already.", vm.Status);

        vm.PutBackCommand.Execute(null);
        Assert.Equal("Your Desk is back as it was.", vm.Status);
        Assert.DoesNotContain(vm.Cards, c => c.Current);
    });

    [Fact]
    public void A_card_carries_at_most_two_notes_fit_notes_first() => WithGallery((vm, service) =>
    {
        service.InstalledFonts = () => ["Segoe UI"];   // no Georgia: a font note
        _wall = 0.98;                                   // and an ink note
        vm.Reload();
        var alpha = vm.Cards[0];
        Assert.Equal(["Uses Segoe UI on this PC.", DeskLookFit.AutoInkNote], alpha.Notes);
        Assert.Empty(alpha.Needs);   // both lines are taken
        Assert.Equal("Alpha look. A test look. Uses Segoe UI on this PC. Uses automatic ink on your wallpaper.", alpha.AccessibleName);
    });

    [Fact]
    public void With_no_service_the_gallery_is_empty_and_harmless() => Sta.Run(() =>
    {
        var vm = new DeskLooksViewModel(() => null);
        Assert.Empty(vm.Cards);
        Assert.False(vm.CanPutBack);
        vm.Reload();
    });

    // ---- the panel ----

    private static DeskLooksPanel Shown(DeskLooksViewModel vm)
    {
        var panel = new DeskLooksPanel { DataContext = vm };
        panel.Measure(new Size(700, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 700, panel.DesiredSize.Height));
        panel.UpdateLayout();
        Pump();
        return panel;
    }

    private static List<Button> LookButtons(DeskLooksPanel panel) =>
        [.. Visuals<Button>(panel).Where(b => b.DataContext is DeskLookCardViewModel && b.Command is not null && b.Content is not string)];

    [Fact]
    public void The_cards_are_buttons_with_their_names_and_no_list_selects_them() => WithGallery((vm, _) =>
    {
        var panel = Shown(vm);
        var buttons = LookButtons(panel);

        Assert.Equal(3, buttons.Count);
        Assert.Equal(vm.Cards.Select(c => c.AccessibleName), buttons.Select(AutomationProperties.GetName));
        Assert.All(buttons, b => Assert.True(b.Focusable));
        Assert.Empty(Visuals<Selector>(panel));   // a selecting list would apply a look on every arrow key

        var row = Assert.Single(Visuals<DeskLooksRow>(panel));
        var peer = UIElementAutomationPeer.CreatePeerForElement(row);
        Assert.Equal(AutomationControlType.Group, peer.GetAutomationControlType());
        Assert.Equal("Ready-made looks", peer.GetName());
        Assert.Equal(3, Visuals<DeskLookCard>(panel).Count());
    });

    [Fact]
    public void Focus_alone_changes_nothing_and_Invoke_applies() => WithGallery((vm, _) =>
    {
        var panel = Shown(vm);
        var button = LookButtons(panel)[2];
        string before = _store.LoadDesk().Layout;

        button.Focus();
        Pump();
        Assert.Equal(before, _store.LoadDesk().Layout);
        Assert.False(vm.HasStatus);

        var invoke = (IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke);
        invoke.Invoke();
        Pump();
        Assert.Equal("Gamma is on your Desk.", vm.Status);
        Assert.NotEqual(before, _store.LoadDesk().Layout);
        Assert.Equal(vm.Cards[2].AccessibleName, AutomationProperties.GetName(button));   // the name follows the state
        Assert.EndsWith("On your Desk.", AutomationProperties.GetName(button));
    });

    [Fact]
    public void The_panel_has_put_back_a_live_status_line_and_need_links()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Views", "SettingsWindow.xaml")))
            dir = dir.Parent;
        string views = Path.Combine(dir!.FullName, "src", "Sanduhr.App", "Views");
        string panel = File.ReadAllText(Path.Combine(views, "DeskLooksPanel.xaml"));
        string window = File.ReadAllText(Path.Combine(views, "SettingsWindow.xaml"));

        Assert.Contains("Command=\"{Binding PutBackCommand}\"", panel);
        Assert.Contains("AutomationProperties.Name=\"{x:Static core:SettingsNames.PutMyDeskBack}\"", panel);
        Assert.Contains("views:LiveRegion.Announces=\"True\"", panel);
        Assert.Contains("Text=\"{Binding Status}\"", panel);
        Assert.Contains("AutomationProperties.HeadingLevel=\"Level2\"", panel);
        Assert.Contains("views:SettingsLinkTo.Link=\"{Binding Link}\"", panel);
        Assert.Contains("{x:Static core:SettingsNames.OnYourDesk}", panel);
        Assert.DoesNotContain("<ListBox", panel);

        // On the Desk page, under the Desk switch and above Arrange and the pieces; greyed while Arrange Desk runs.
        int desk = window.IndexOf("views:SettingsAnchor.Id=\"desk\"", StringComparison.Ordinal);
        int looks = window.IndexOf("views:SettingsAnchor.Id=\"looks\"", StringComparison.Ordinal);
        int arrange = window.IndexOf("views:SettingsAnchor.Id=\"arrange\"", StringComparison.Ordinal);
        int pieces = window.IndexOf("views:SettingsAnchor.Id=\"pieces\"", StringComparison.Ordinal);
        Assert.True(desk > 0 && desk < looks && looks < arrange && arrange < pieces);
        Assert.Contains("<views:DeskLooksPanel DataContext=\"{Binding Looks}\" />", window);
        string section = window[looks..arrange];
        Assert.Contains("IsEnabled=\"{Binding NotArranging}\"", section);
    }

    // ---- a card's picture ----

    private static DeskLookCard Card(DeskDesign design, string hex = "203040", double? brightness = 0.03)
    {
        DeskLookCard.FontsForTests = new DeskFonts(["Georgia", "Segoe UI"]);
        var card = new DeskLookCard { LookName = "Alpha", BackdropHex = hex, Brightness = brightness, Design = design };
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        card.Arrange(new Rect(card.DesiredSize));
        card.UpdateLayout();
        return card;
    }

    [Fact]
    public void A_card_draws_the_name_where_the_clock_is_and_a_bar_for_every_other_piece() => Sta.Run(() =>
    {
        var look = Catalog.Find("alpha")!;   // clock and message at tc, launcher at br
        var card = Card(look.Design);

        Assert.Equal(DeskLookCard.CardWidth, card.Width);
        Assert.Equal(DeskLookCard.CardHeight, card.Height);
        var name = Assert.Single(card.Children.OfType<TextBlock>());
        Assert.Equal("Alpha", name.Text);
        Assert.Equal("Georgia", name.FontFamily.Source);
        // Top center: the name's middle is the card's middle, in its upper half.
        double left = Canvas.GetLeft(name), top = Canvas.GetTop(name);
        Assert.Equal(DeskLookCard.CardWidth / 2, left + name.DesiredSize.Width / 2, 1);
        Assert.True(top < DeskLookCard.CardHeight / 2);
        // The amber ink, glowing.
        Assert.Equal(DeskView.Rgb("ffd08a"), Assert.IsType<SolidColorBrush>(name.Foreground).Color);
        Assert.Equal(0, Assert.IsType<DropShadowEffect>(name.Effect).ShadowDepth);

        var bars = card.Children.OfType<Rectangle>().Where(r => (r.Tag as string) == "piece").ToList();
        Assert.Equal(2, bars.Count);   // message and launcher
        foreach (var bar in bars)
        {
            Assert.InRange(Canvas.GetLeft(bar), 0, DeskLookCard.CardWidth - bar.Width);
            Assert.InRange(Canvas.GetTop(bar), 0, DeskLookCard.CardHeight - bar.Height);
        }
        // The backdrop is the wallpaper's first color when there is no picture.
        var backdrop = card.Children.OfType<Rectangle>().First(r => (r.Tag as string) == "backdrop");
        Assert.Equal(DeskView.Rgb("203040"), Assert.IsType<SolidColorBrush>(backdrop.Fill).Color);
    });

    [Fact]
    public void A_plated_look_draws_its_plates_and_an_automatic_ink_follows_the_wallpaper() => Sta.Run(() =>
    {
        var plated = Catalog.Find("alpha")!.Design with { Plate = true, Ink = DeskInkChoice.Of(DeskInkKind.Auto), Glow = DeskGlowChoice.Off, Shadow = true };

        var dark = Card(plated, brightness: 0.03);
        Assert.Equal(2, dark.Children.OfType<Rectangle>().Count(r => (r.Tag as string) == "plate"));   // one per stack: tc and br
        Assert.Equal(Colors.White, ((SolidColorBrush)dark.Children.OfType<TextBlock>().Single().Foreground).Color);

        var bright = Card(plated, brightness: 0.95);
        Assert.NotEqual(Colors.White, ((SolidColorBrush)bright.Children.OfType<TextBlock>().Single().Foreground).Color);
    });

    [Fact]
    public void A_card_with_no_design_or_no_color_still_draws() => Sta.Run(() =>
    {
        DeskLookCard.FontsForTests = new DeskFonts(["Segoe UI"]);
        var empty = new DeskLookCard { LookName = "Alpha", BackdropHex = "" };
        empty.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Single(empty.Children);   // the backdrop alone, in the theme's background
        Assert.Empty(empty.Children.OfType<TextBlock>());
    });
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskLooksGalleryTests"`
Expected: the build fails with "The type or namespace name 'DeskLooksViewModel' could not be found".

- [ ] **Step 3: The names and the index**

In `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, beside `PiecesSection`, add:

```csharp
    public const string LooksSection = "Looks";
    public const string LooksRowName = "Ready-made looks";
    public const string OnYourDesk = "On your Desk";
    public const string LooksCaption = "Pick one and your whole Desk changes. Put my Desk back undoes it. Your Launcher buttons, your messages and your weather place stay as they are.";
```

In `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`:

1. With the Desk anchors, add `public const string DeskLooks = "looks";` and the doc comment `/// <summary>Desk, Looks (5.0 spec 5): six ready-made looks; the page's first section after the switch.</summary>`.
2. Directly after the `E(SettingsNames.DeskSwitch, SettingsPage.Desk, DeskOn, …)` entry, add:

```csharp
        E(SettingsNames.LooksSection, SettingsPage.Desk, DeskLooks,
            ["looks", "look", "gallery", "presets", "preset", "styles", "style", "ready-made", "templates", "skins",
             "Quiet corner", "Ice", "Marquee", "Terminal", "Editorial", "Dashboard", SettingsNames.PutMyDeskBack, "one click", "neon"]),
```

3. In `JumpLinks`, replace the Desk line with `SettingsPage.Desk => Links(page, DeskLooks, DeskPieces, DeskClicks, DeskDesigns, DeskAdvanced),`.

- [ ] **Step 4: The view models**

Create `windows-dotnet/src/Sanduhr.App/ViewModels/DeskLooksViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Desk;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>What a shown piece still needs ("Weather needs a place."), as a link to that piece's Settings page.</summary>
public sealed record DeskLookNeedLink(string Text, string Link);

/// <summary>
/// One ready-made look in the gallery (5.0 spec 5, section 6). The card is a button: pressing it applies the look. Everything it
/// shows is fixed copy (a look's name, its blurb, the fit's notes), so streamer mode has nothing to mask here; the wallpaper on the
/// card is the user's own desktop, already on their screen.
/// </summary>
public sealed partial class DeskLookCardViewModel : ObservableObject
{
    private readonly Action<string> _apply;

    internal DeskLookCardViewModel(string id, Action<string> apply)
    {
        Id = id;
        _apply = apply;
    }

    public string Id { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(AccessibleName))] private string _name = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AccessibleName))] private string _blurb = "";
    /// <summary>The look as fitted to this PC: what the card draws and what a press applies.</summary>
    [ObservableProperty] private DeskDesign? _fitted;
    /// <summary>What the fit changed, at most two lines.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AccessibleName))] private IReadOnlyList<string> _notes = [];
    /// <summary>What the look's shown pieces still need; empty when the fit's notes took both lines.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AccessibleName))] private IReadOnlyList<DeskLookNeedLink> _needs = [];
    /// <summary>The Desk is this look now, untouched.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AccessibleName))] private bool _current;
    /// <summary>The user's wallpaper, small; null when there is no picture to show.</summary>
    [ObservableProperty] private ImageSource? _wallpaper;
    /// <summary>The wallpaper's first dominant color (six hex digits) for a card without a picture; "" for the theme's background.</summary>
    [ObservableProperty] private string _backdropHex = "";
    /// <summary>The wallpaper's overall brightness, which automatic ink follows on the card; null when unknown.</summary>
    [ObservableProperty] private double? _brightness;

    /// <summary>What a screen reader says for the card: "Marquee look. Clock up top, neon glow. Uses its plate on your wallpaper. On your Desk."</summary>
    public string AccessibleName => string.Join(" ",
        new[] { $"{Name} look.", Blurb }.Concat(Notes).Concat(Needs.Select(n => n.Text)).Concat(Current ? [SettingsNames.OnYourDesk + "."] : [])
            .Where(s => s.Length > 0));

    [RelayCommand]
    private void Apply() => _apply(Id);

    /// <summary>The page for a piece that has nothing to draw yet.</summary>
    internal static string LinkFor(string piece) => piece switch
    {
        "weather" => SettingsIndex.PageId(SettingsPage.Weather),
        "launcher" => SettingsIndex.PageId(SettingsPage.Launcher) + "#" + SettingsIndex.LauncherButtons,
        "watchers" => SettingsIndex.PageId(SettingsPage.Watchers) + "#" + SettingsIndex.Watchers,
        _ => SettingsIndex.PageId(SettingsPage.Desk) + "#" + SettingsIndex.DeskPieces,
    };

    internal void Update(DeskLookCardData data, ImageSource? wallpaper, string backdropHex, double? brightness)
    {
        Name = data.Look.Name;
        Blurb = data.Look.Blurb;
        Fitted = data.Fit.Design;
        Notes = [.. data.Fit.Notes.Take(2)];
        // At most two notes, fit notes first: what the pieces need is one more line, when there is room for it.
        Needs = Notes.Count < 2 ? [.. data.Needs.Select(n => new DeskLookNeedLink(n.Line, LinkFor(n.Piece)))] : [];
        Current = data.Current;
        Wallpaper = wallpaper;
        BackdropHex = backdropHex;
        Brightness = brightness;
    }
}

/// <summary>
/// Settings, Desk, Looks (5.0 spec 5, section 6): a card per ready-made look, each applied in one click, the same Put my Desk back as
/// Designs and the tray, and a line that says what the last press did. The cards are kept and updated in place, so the keyboard stays
/// on the one that was pressed.
/// </summary>
public sealed partial class DeskLooksViewModel : ObservableObject
{
    private readonly Func<DeskDesignService?> _service;

    /// <param name="service">The app's design service (null where there is none: the gallery is empty).</param>
    internal DeskLooksViewModel(Func<DeskDesignService?> service)
    {
        _service = service;
        Reload();
    }

    public ObservableCollection<DeskLookCardViewModel> Cards { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = "";

    public bool HasStatus => Status.Length > 0;

    public bool CanPutBack => _service()?.CanPutBack == true;

    /// <summary>Fits every look again and reads the wallpaper again (after a press, when the page shows, when the wallpaper, the fonts
    /// or the saved designs change).</summary>
    public void Reload()
    {
        if (_service() is not { } service)
        {
            Cards.Clear();
        }
        else
        {
            DeskLooksSnapshot snapshot;
            ImageSource? picture;
            try
            {
                snapshot = service.LooksSnapshot();
                picture = service.LookPicture();
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return;   // the gallery keeps what it showed
            }
            string backdrop = InkHex.Normalize(snapshot.Wallpaper?.Dominant.FirstOrDefault()?.Hex) ?? "";
            double? brightness = snapshot.Wallpaper?.Brightness;
            var keep = Cards.ToDictionary(c => c.Id, StringComparer.Ordinal);
            for (int i = 0; i < snapshot.Cards.Count; i++)
            {
                var data = snapshot.Cards[i];
                if (!keep.TryGetValue(data.Look.Id, out var card)) card = new DeskLookCardViewModel(data.Look.Id, Apply);
                card.Update(data, picture, backdrop, brightness);
                if (i >= Cards.Count) Cards.Add(card);
                else if (!ReferenceEquals(Cards[i], card)) Cards[i] = card;
            }
            while (Cards.Count > snapshot.Cards.Count) Cards.RemoveAt(Cards.Count - 1);
        }
        OnPropertyChanged(nameof(CanPutBack));
        PutBackCommand.NotifyCanExecuteChanged();
    }

    private void Say(DesignActionResult result)
    {
        Status = "";   // the same line twice is one change to a live region: clear it first
        Status = result.Message;   // streamer: cant - fixed copy and a built-in look's name, never the user's text
        Reload();
    }

    private void Apply(string id)
    {
        if (_service() is not { } s) return;
        Say(s.ApplyLook(id));
    }

    [RelayCommand(CanExecute = nameof(CanPutBack))]
    private void PutBack()
    {
        if (_service() is not { } s) return;
        Say(s.PutBack());
    }
}
```

In `windows-dotnet/src/Sanduhr.App/ViewModels/DeskSettingsViewModel.cs`, beside the line that creates `Designs` (line 253), add `Looks = new DeskLooksViewModel(() => Sanduhr.App.Desk.DeskDesignService.Current);`; beside the `Designs` property add:

```csharp
    /// <summary>Settings, Desk, Looks: the ready-made looks (5.0 spec 5).</summary>
    public DeskLooksViewModel Looks { get; }
```

and where this class calls `Designs.Reload();` (line 342), add `Looks.Reload();` on the next line.

- [ ] **Step 5: The card's picture**

Create `windows-dotnet/src/Sanduhr.App/Views/DeskLookCard.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Sanduhr.App.Desk;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// The top of a ready-made look's card (5.0 spec 5, section 6): the user's wallpaper, or its first color, with the look's arrangement
/// over it in the fitted look's ink. The look's name stands where the look puts its clock, in the look's font, with its glow when it
/// has one; every other shown piece is a short bar; a plated look gets a soft plate behind each stack. Placed with the same Core
/// call the layout map uses (<see cref="DeskGeometry.Point"/>), for one screen. It suggests the look; the real Desk is the preview.
/// Decorative: the button around it carries the accessible name.
/// </summary>
internal sealed class DeskLookCard : Canvas
{
    public const double CardWidth = 208;
    public const double CardHeight = 117;

    private const double NameSize = 17;
    private const double BarWidth = 34, BarHeight = 3.5, Gap = 3;

    /// <summary>The fonts the cards draw with (tests set it); null reads this PC's, once.</summary>
    internal static DeskFonts? FontsForTests { get; set; }

    private static readonly Lazy<DeskFonts> PcFonts = new(() => DeskFonts.ForThisPc(new Paths()));

    public static readonly DependencyProperty DesignProperty = Register<DeskDesign?>(nameof(Design), null);
    public static readonly DependencyProperty WallpaperProperty = Register<ImageSource?>(nameof(Wallpaper), null);
    public static readonly DependencyProperty BackdropHexProperty = Register(nameof(BackdropHex), "");
    public static readonly DependencyProperty BrightnessProperty = Register<double?>(nameof(Brightness), null);
    public static readonly DependencyProperty LookNameProperty = Register(nameof(LookName), "");

    private static DependencyProperty Register<T>(string name, T initial) => DependencyProperty.Register(
        name, typeof(T), typeof(DeskLookCard), new PropertyMetadata(initial, (d, _) => ((DeskLookCard)d).Redraw()));

    public DeskDesign? Design { get => (DeskDesign?)GetValue(DesignProperty); set => SetValue(DesignProperty, value); }
    public ImageSource? Wallpaper { get => (ImageSource?)GetValue(WallpaperProperty); set => SetValue(WallpaperProperty, value); }
    public string BackdropHex { get => (string)GetValue(BackdropHexProperty); set => SetValue(BackdropHexProperty, value); }
    public double? Brightness { get => (double?)GetValue(BrightnessProperty); set => SetValue(BrightnessProperty, value); }
    public string LookName { get => (string)GetValue(LookNameProperty); set => SetValue(LookNameProperty, value); }

    public DeskLookCard()
    {
        Width = CardWidth;
        Height = CardHeight;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        Focusable = false;
        IsHitTestVisible = false;   // the button takes the click
        Redraw();
    }

    private void Redraw()
    {
        Children.Clear();
        Children.Add(Backdrop());
        if (Design is not { Layout: { } layout } design) return;

        // The ink as the Desk would resolve it over this wallpaper: automatic ink follows the brightness.
        var ink = DeskInk.Resolve(DeskDesignApply.Ink(DeskInkSettings.Default, design), null, Brightness);
        var brush = DeskView.InkBrush(ink.Stops);
        var content = new DeskRect(CardWidth * 0.07, CardHeight * 0.09, CardWidth * 0.86, CardHeight * 0.82);
        var shown = layout.Where(p => !p.IsHidden && p.Anchor is not null).ToList();
        bool clockShown = shown.Any(p => p.Piece == "clock");

        foreach (var stack in shown.GroupBy(p => p.Anchor!.Value))
        {
            var marks = stack.Select(p => Mark(p, design, ink, brush)).ToList();
            foreach (var m in marks) m.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double height = marks.Sum(m => m.DesiredSize.Height) + Gap * (marks.Count - 1);
            double width = marks.Max(m => m.DesiredSize.Width);
            var (px, py) = DeskGeometry.Point(stack.Key, content);
            double top = DeskAnchors.Row(stack.Key) switch
            {
                DeskRow.Top => py,
                DeskRow.Middle => py - height / 2,
                _ => py - height,
            };
            double Left(double w) => Math.Clamp(DeskAnchors.Column(stack.Key) switch
            {
                DeskColumn.Left => px,
                DeskColumn.Center => px - w / 2,
                _ => px - w,
            }, 0, Math.Max(0, CardWidth - w));
            top = Math.Clamp(top, 0, Math.Max(0, CardHeight - height));

            if (design.Plate == true)
            {
                var plate = new Rectangle
                {
                    Width = width + 8, Height = height + 6, RadiusX = 4, RadiusY = 4, Tag = "plate",
                    Fill = new SolidColorBrush(DeskView.Rgb(DeskPlate.ColorHex(ink.DarkInk))) { Opacity = DeskPlate.Opacity },
                };
                SetLeft(plate, Left(width) - 4);
                SetTop(plate, top - 3);
                Children.Add(plate);
            }
            foreach (var m in marks)
            {
                SetLeft(m, Left(m.DesiredSize.Width));
                SetTop(m, top);
                Children.Add(m);
                top += m.DesiredSize.Height + Gap;
            }
        }

        if (!clockShown)
        {
            // A look without a clock: its name in the middle.
            var name = Name(1, design, ink, brush);
            name.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            SetLeft(name, (CardWidth - name.DesiredSize.Width) / 2);
            SetTop(name, (CardHeight - name.DesiredSize.Height) / 2);
            Children.Add(name);
        }
    }

    private FrameworkElement Backdrop()
    {
        if (Wallpaper is { } picture)
            return new Image { Source = picture, Width = CardWidth, Height = CardHeight, Stretch = Stretch.UniformToFill, Tag = "backdrop" };
        var fill = new Rectangle { Width = CardWidth, Height = CardHeight, Tag = "backdrop" };
        if (InkHex.Normalize(BackdropHex) is { } hex) fill.Fill = new SolidColorBrush(DeskView.Rgb(hex));
        else fill.SetResourceReference(Shape.FillProperty, "Sanduhr.Brush.Bg");
        return fill;
    }

    private FrameworkElement Mark(DeskPieceLayout p, DeskDesign design, ResolvedInk ink, Brush brush)
    {
        double size = p.Size ?? 1;
        if (p.Piece == "clock") return Name(size, design, ink, brush);
        var bar = new Rectangle
        {
            Width = BarWidth * size, Height = BarHeight * size, RadiusX = BarHeight * size / 2, RadiusY = BarHeight * size / 2,
            Fill = brush, Opacity = 0.85 * ink.TextOpacity, Tag = "piece",
        };
        bar.Effect = Lit(ink);
        return bar;
    }

    private TextBlock Name(double size, DeskDesign design, ResolvedInk ink, Brush brush)
    {
        var fonts = FontsForTests ?? PcFonts.Value;
        var (_, bold) = fonts.Families(design.Font is { } f ? DeskDesignApply.FontSetting(f) : null);
        return new TextBlock
        {
            Text = LookName,   // streamer: notname - a built-in look's name, fixed copy
            FontFamily = bold,
            FontWeight = FontWeights.Bold,
            FontSize = NameSize * size,
            Foreground = brush,
            Opacity = ink.TextOpacity,
            Effect = Lit(ink),
        };
    }

    /// <summary>The look's light at card size: its glow in the ink's middle color, or a small shadow; neither when it has neither.</summary>
    private static Effect? Lit(ResolvedInk ink)
    {
        if (ink.Glow > 0)
            return new DropShadowEffect
            {
                Color = DeskView.Rgb(DeskGlowLayer.Middle(ink.Stops)), ShadowDepth = 0,
                BlurRadius = 3 + 7 * ink.Glow, Opacity = DeskGlow.Opacity(ink.Glow),
            };
        if (ink.ShadowOpacity > 0)
            return new DropShadowEffect { Color = DeskView.Rgb(ink.ShadowHex), ShadowDepth = 1, BlurRadius = 3, Direction = 270, Opacity = ink.ShadowOpacity };
        return null;
    }
}
```

`fonts.Families(string? setting)` is the call `DeskView`'s constructor makes; it returns the regular and the bold family. The `Name` method above shadows `FrameworkElement.Name`; if the compiler warns, rename it `NameMark` in both places it is called.

- [ ] **Step 6: The panel**

Create `windows-dotnet/src/Sanduhr.App/Views/DeskLooksPanel.xaml`:

```xml
<UserControl x:Class="Sanduhr.App.Views.DeskLooksPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:core="clr-namespace:Sanduhr.Core;assembly=Sanduhr.Core"
             xmlns:views="clr-namespace:Sanduhr.App.Views"
             Focusable="False">
    <!-- Settings, Desk, Looks (5.0 spec 5): six ready-made looks, each a button that applies it in one click; the same Put my Desk back
         as Designs and the tray; a live line for what the last press did. Its own control so the names Narrator reads can be tested.
         The Settings window supplies the FlatButton and SettingsLinkButton styles (found at run time) and the theme brushes. -->
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BoolVis" />
        <!-- A look's card: a quiet bordered button. Hover lifts it; the keyboard ring is the text ink, as on FlatButton (WF38). -->
        <Style x:Key="LookCardButton" TargetType="Button">
            <Setter Property="FocusVisualStyle" Value="{x:Null}" />
            <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.Text}" />
            <Setter Property="Background" Value="{DynamicResource Sanduhr.Brush.Glass}" />
            <Setter Property="BorderBrush" Value="{DynamicResource Sanduhr.Brush.Border}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="Padding" Value="7" />
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="HorizontalContentAlignment" Value="Stretch" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="bg" CornerRadius="8"
                                Background="{TemplateBinding Background}"
                                BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}"
                                Padding="{TemplateBinding Padding}">
                            <ContentPresenter />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="bg" Property="Background" Value="{DynamicResource Sanduhr.Brush.GlassHover}" />
                            </Trigger>
                            <Trigger Property="IsKeyboardFocused" Value="True">
                                <Setter TargetName="bg" Property="BorderBrush" Value="{DynamicResource Sanduhr.Brush.Text}" />
                                <Setter TargetName="bg" Property="BorderThickness" Value="2" />
                                <Setter TargetName="bg" Property="Padding" Value="6" />
                            </Trigger>
                            <Trigger Property="IsEnabled" Value="False">
                                <Setter Property="Opacity" Value="0.4" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </UserControl.Resources>
    <StackPanel>
        <DockPanel LastChildFill="False">
            <TextBlock DockPanel.Dock="Left" Text="{x:Static core:SettingsNames.LooksSection}" FontSize="13" FontWeight="SemiBold"
                       VerticalAlignment="Center" AutomationProperties.HeadingLevel="Level2"
                       Foreground="{DynamicResource Sanduhr.Brush.Text}" />
            <Button DockPanel.Dock="Right" x:Name="LooksPutBack" Style="{DynamicResource FlatButton}" Padding="12,6" Margin="0"
                    Content="{x:Static core:SettingsNames.PutMyDeskBack}"
                    Command="{Binding PutBackCommand}"
                    AutomationProperties.Name="{x:Static core:SettingsNames.PutMyDeskBack}"
                    AutomationProperties.HelpText="Switches to the design saved before the last change" />
        </DockPanel>
        <TextBlock FontSize="11" TextWrapping="Wrap" Margin="0,6,0,0"
                   Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                   Text="{x:Static core:SettingsNames.LooksCaption}" />
        <views:DeskLooksRow x:Name="LooksRow" ItemsSource="{Binding Cards}" Margin="0,12,0,0" Focusable="False"
                            KeyboardNavigation.DirectionalNavigation="Contained" KeyboardNavigation.TabNavigation="Continue">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <WrapPanel />
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <StackPanel Width="224" Margin="0,0,12,12">
                        <Button Style="{StaticResource LookCardButton}" Command="{Binding ApplyCommand}"
                                AutomationProperties.Name="{Binding AccessibleName}">
                            <StackPanel>
                                <Border CornerRadius="5" ClipToBounds="True" HorizontalAlignment="Left">
                                    <views:DeskLookCard Design="{Binding Fitted}" Wallpaper="{Binding Wallpaper}"
                                                        BackdropHex="{Binding BackdropHex}" Brightness="{Binding Brightness}"
                                                        LookName="{Binding Name}" />
                                </Border>
                                <DockPanel Margin="0,7,0,0" LastChildFill="False">
                                    <TextBlock DockPanel.Dock="Left" Text="{Binding Name}" FontSize="12" FontWeight="SemiBold"
                                               Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                    <TextBlock DockPanel.Dock="Right" FontSize="11"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                                               Visibility="{Binding Current, Converter={StaticResource BoolVis}}">
                                        <Run Text="&#x2713; " /><Run Text="{x:Static core:SettingsNames.OnYourDesk}" />
                                    </TextBlock>
                                </DockPanel>
                                <TextBlock Text="{Binding Blurb}" FontSize="11" TextWrapping="Wrap" Margin="0,2,0,0"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                <ItemsControl ItemsSource="{Binding Notes}" Focusable="False">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <TextBlock Text="{Binding}" FontSize="11" TextWrapping="Wrap" Margin="0,2,0,0"
                                                       Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
                        </Button>
                        <!-- What a shown piece needs: links, outside the card button (a button inside a button takes no keyboard). -->
                        <ItemsControl ItemsSource="{Binding Needs}" Focusable="False" Margin="8,2,0,0">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Button Style="{DynamicResource SettingsLinkButton}" HorizontalAlignment="Left"
                                            Content="{Binding Text}" views:SettingsLinkTo.Link="{Binding Link}"
                                            AutomationProperties.Name="{Binding Text}" />
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </StackPanel>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </views:DeskLooksRow>
        <TextBlock x:Name="LooksStatus" FontSize="11" TextWrapping="Wrap" Margin="0,2,0,0"
                   views:LiveRegion.Announces="True"
                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                   Text="{Binding Status}"
                   Visibility="{Binding HasStatus, Converter={StaticResource BoolVis}}" />
    </StackPanel>
</UserControl>
```

Create `windows-dotnet/src/Sanduhr.App/Views/DeskLooksPanel.xaml.cs`:

```csharp
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>Settings, Desk, Looks (5.0 spec 5): the ready-made looks. The wallpaper may have changed while the page was away, so the
/// looks are fitted again each time it shows.</summary>
public partial class DeskLooksPanel : UserControl
{
    public DeskLooksPanel()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) (DataContext as DeskLooksViewModel)?.Reload();
        };
    }
}

/// <summary>
/// The row of look cards. An ItemsControl has no automation peer of its own, so UI Automation would hoist the buttons and never say
/// what they are a row of; this one is a Group named "Ready-made looks". It is not a list: nothing is selected, and the arrow keys
/// only move focus between the buttons.
/// </summary>
public sealed class DeskLooksRow : ItemsControl
{
    public DeskLooksRow() => AutomationProperties.SetName(this, SettingsNames.LooksRowName);

    protected override AutomationPeer OnCreateAutomationPeer() => new DeskLooksRowAutomationPeer(this);
}

public sealed class DeskLooksRowAutomationPeer(DeskLooksRow owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(DeskLooksRow);

    protected override bool IsControlElementCore() => true;
}
```

- [ ] **Step 7: The Desk page, and the changes that refresh the gallery**

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`, on the Desk tab, right after the closing `</StackPanel>` of the block with `views:SettingsAnchor.Id="desk"` (the Desk switch, line 2467) and before the separator that precedes `views:SettingsAnchor.Id="arrange"`, add:

```xml

                                    <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />

                                    <!-- Looks (5.0 spec 5): six ready-made looks, one click each. Greyed while Arrange Desk runs, as Pieces is. -->
                                    <StackPanel views:SettingsAnchor.Id="looks" IsEnabled="{Binding NotArranging}">
                                        <views:DeskLooksPanel DataContext="{Binding Looks}" />
                                    </StackPanel>
```

Update the comment above the Desk tab: "the switch, Arrange, the map on top" becomes "the switch, the ready-made looks, Arrange, the map".

In `windows-dotnet/src/Sanduhr.App/Desk/DeskDesignService.cs`, beside `DesignsChanged`, add:

```csharp
    /// <summary>What a look card shows may have changed without a design changing: the wallpaper, the monitors or the installed fonts.</summary>
    public event Action? LooksChanged;
```

Raise it where those changes arrive: in `OnUserPreferenceChanged`, inside the `if`, after `Refresh();` add `LooksChanged?.Invoke();`; in `OnDisplayChanged` make the body `{ Refresh(); LooksChanged?.Invoke(); }`; in `FontsChanged`, after `Refresh();` add `LooksChanged?.Invoke();`. `SystemEvents` raises on its own thread for some of these; the handler below marshals to the UI thread.

In `windows-dotnet/src/Sanduhr.App/App.xaml.cs`, replace

```csharp
        _deskDesign.DesignsChanged += () => _settingsWindow?.ViewModel.Desk.Designs.Reload();
```

with

```csharp
        _deskDesign.DesignsChanged += () =>
        {
            _settingsWindow?.ViewModel.Desk.Designs.Reload();
            _settingsWindow?.ViewModel.Desk.Looks.Reload();
        };
        // The wallpaper, the monitors or the fonts changed: the look cards are fitted and drawn again, on the UI thread.
        _deskDesign.LooksChanged += () => Dispatcher.BeginInvoke(new Action(() => _settingsWindow?.ViewModel.Desk.Looks.Reload()));
```

- [ ] **Step 8: Run the tests**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~DeskLooksGalleryTests|FullyQualifiedName~Settings|FullyQualifiedName~DeskSplit|FullyQualifiedName~Streamer|FullyQualifiedName~Designs"` and `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsIndex|FullyQualifiedName~SettingsNames"`
Expected: PASS. The Settings guard tests now meet a new section. Each failure names what it wants; give it that in the way the page's other sections have it:

- `SettingsDeskSplitIndexTests` and any test that lists the Desk page's sections or jump links in order: add `looks` after `desk`.
- `SettingsWindowNamesTests`, `SettingsControlStatesTests`, `SettingsContrastTests`, `SettingsReachTests`: every new control already has a name, a focus ring in the text ink and theme brushes; a test that walks a fixed list of controls gets the new ones added.
- `SettingsSourceScanTests` and the streamer scans: the two lines that draw a look's name carry `// streamer: notname` or `// streamer: cant` as written above; a line a scan still flags gets the comment that scan's message asks for, and only when the line truly draws no user name or path.

Do not loosen a guard test.

- [ ] **Step 9: Run every suite and commit**

```bash
cd windows-dotnet && dotnet test
git add src tests
git commit -m "feat(windows): the Looks gallery on the Desk page

Six cards that are buttons, each drawn from the look as it fits this
PC, with Put my Desk back and a live status line.

Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>"
```

---

### Task 12: Start with a look, in setup

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SetupSettings.cs:51` (`SetupChoices`), `windows-dotnet/src/Sanduhr.Core/SetupCopy.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Services/SetupService.cs` (`ISetupHost`, `SetupService.Apply`, `AppSetupHost`)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SetupViewModel.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SetupWindow.xaml` (after `DesignWithChatCaption`, line 122)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (where `SetupViewModel` is built, line 1237)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/SetupLookTests.cs` (new)

**Interfaces:**
- Consumes: `DeskDesignService.ApplyLook`, `LooksSnapshot`, `LookPicture` (Task 10); `DeskLookCardViewModel`, `DeskLookCard` (Task 11).
- Produces:
  - `SetupChoices(bool Desktop, bool Drawers, bool ClaudeUsage, bool CodexUsage = false, string? Look = null)`.
  - `ISetupHost.ApplyLook(string id)` returning `string?`: null when the look was applied, else the line to show.
  - `SetupCopy.StartWithALook = "Start with a look"`, `SetupCopy.StartWithALookCaption`.
  - `SetupViewModel.Looks` (`ObservableCollection<SetupLookViewModel>`), `LooksEnabled`, `HasLooks`, `PickedLook` (`string?`); `internal sealed partial class SetupLookViewModel` with `Card` (`DeskLookCardViewModel`), `Picked` (`bool`), `PickCommand`, `AccessibleName`.
  - The `SetupViewModel` constructor gains `Func<DeskDesignService?>? looks = null` as its last parameter.

The rules (spec section 6, "In setup"): the row is enabled only while "A nicer desktop" is ticked; picking a card marks it and picking it again clears it; nothing changes until Continue; Continue with a look picked applies it after the Desk is switched on, and before the first drawers review opens (a drawers review refuses a look); with none picked, setup behaves as it does today.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.App.Tests/SetupLookTests.cs`:

```csharp
using System.IO;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Set up Sanduhr, "Start with a look" (5.0 spec 5, section 6): a pick is a choice like the ticks, applied only on Continue.</summary>
public sealed class SetupLookTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-setuplook-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    public SetupLookTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        _store = new SettingsStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>A setup host that records what was asked of it, in order.</summary>
    private sealed class Host : ISetupHost
    {
        public List<string> Calls { get; } = [];
        public string? LookSays { get; set; }
        public bool DrawersOn { get; set; }
        public bool DrawersEverSorted => false;
        public int DrawerFiles => 0;
        public bool AddOnsInstalled => false;
        public bool UsageOn => false;
        public bool HasAccount => false;
        public SetupDrawersOff TurnOffDrawers() { Calls.Add("drawers off"); DrawersOn = false; return new SetupDrawersOff(true, null); }
        public void TurnOnDrawers() { Calls.Add("drawers on"); }
        public string? SortNow() { Calls.Add("sort"); return null; }
        public void SetUsage(bool on) => Calls.Add("usage " + on);
        public void ReloadDesk() => Calls.Add("reload");
        public string? ApplyLook(string id) { Calls.Add("look " + id); return LookSays; }
    }

    [Fact]
    public void A_picked_look_is_applied_after_the_Desk_is_on_and_before_the_drawers_review()
    {
        var host = new Host();
        var result = new SetupService(_store, host).Apply(new SetupChoices(true, true, false, false, "marquee"));

        Assert.True(_store.LoadDesk().Enabled);
        Assert.True(_store.LoadSetup()!.Desktop);
        Assert.Equal(["drawers on", "reload", "look marquee", "sort"], host.Calls);
        Assert.DoesNotContain(result.Lines, l => l.Contains("Marquee"));
    }

    [Fact]
    public void With_no_look_picked_setup_does_what_it_always_did()
    {
        var host = new Host();
        new SetupService(_store, host).Apply(new SetupChoices(true, false, false));
        Assert.Equal(["reload"], host.Calls);
    }

    [Fact]
    public void A_look_is_not_applied_when_a_nicer_desktop_is_unticked()
    {
        var host = new Host();
        new SetupService(_store, host).Apply(new SetupChoices(false, true, false, false, "marquee"));
        Assert.DoesNotContain(host.Calls, c => c.StartsWith("look"));
    }

    [Fact]
    public void A_look_that_could_not_be_applied_says_why()
    {
        var host = new Host { LookSays = "The Desk could not be saved first, so nothing was changed." };
        var result = new SetupService(_store, host).Apply(new SetupChoices(true, false, false, false, "marquee"));
        Assert.Contains("The Desk could not be saved first, so nothing was changed.", result.Lines);
    }

    [Fact]
    public void A_setup_that_could_not_save_applies_no_look()
    {
        var host = new Host();
        File.WriteAllText(_paths.SettingsFile, "{}");
        new FileInfo(_paths.SettingsFile).IsReadOnly = true;   // streamer: notname - a scratch settings file in a test
        try
        {
            var result = new SetupService(_store, host).Apply(new SetupChoices(true, false, false, false, "marquee"));
            Assert.Contains(SetupCopy.SaveFailed, result.Lines);
            Assert.DoesNotContain(host.Calls, c => c.StartsWith("look"));
        }
        finally { new FileInfo(_paths.SettingsFile).IsReadOnly = false; }
    }

    // ---- the view model ----

    private static string LookJson(string id, string name) => $$"""
        {
          "schema_version": 1, "id": "{{id}}", "name": "{{name}}", "blurb": "A test look.", "font_fallback": "Segoe UI",
          "design": {
            "layout": {
              "clock": { "anchor": "tc", "size": 1.3 },
              "message": { "hidden": true }, "claude": { "hidden": true }, "meters": { "hidden": true }, "nowPlaying": { "hidden": true },
              "telemetry": { "hidden": true }, "weather": { "hidden": true }, "watchers": { "hidden": true }, "launcher": { "hidden": true },
              "glow": { "hidden": true }, "companion": { "hidden": true }
            },
            "font": "Georgia", "ink": "auto", "plate": false, "shadow": true, "glow": false, "follows_theme": false
          }
        }
        """;

    private void WithSetup(bool firstRun, Action<SetupViewModel, Host> body) => Sta.Run(() =>
    {
        using var service = new DeskDesignService(_paths, _store)
        {
            LookCatalog = DeskLookCatalog.Load([("alpha", LookJson("alpha", "Alpha")), ("beta", LookJson("beta", "Beta"))]),
            InstalledFonts = () => ["Georgia", "Segoe UI"],
            Monitors = () => [],
            DrawersReviewing = () => false,
        };
        service.LookWallpaper = () => null;
        var host = new Host();
        body(new SetupViewModel(new SetupService(_store, host), firstRun, hasAccount: () => false, codexInstalled: () => false, looks: () => service), host);
    });

    [Fact]
    public void The_row_lists_the_looks_and_nothing_is_picked() => WithSetup(true, (vm, _) =>
    {
        Assert.True(vm.HasLooks);
        Assert.Equal(["Alpha", "Beta"], vm.Looks.Select(l => l.Card.Name));
        Assert.Null(vm.PickedLook);
        Assert.True(vm.LooksEnabled);   // a first run ticks a nicer desktop
        Assert.Equal("Alpha look. A test look. Not picked.", vm.Looks[0].AccessibleName);
    });

    [Fact]
    public void Picking_marks_a_card_picking_it_again_clears_it_and_one_is_picked_at_a_time() => WithSetup(true, (vm, host) =>
    {
        vm.Looks[0].PickCommand.Execute(null);
        Assert.Equal("alpha", vm.PickedLook);
        Assert.True(vm.Looks[0].Picked);
        Assert.Equal("Alpha look. A test look. Picked.", vm.Looks[0].AccessibleName);

        vm.Looks[1].PickCommand.Execute(null);
        Assert.Equal("beta", vm.PickedLook);
        Assert.False(vm.Looks[0].Picked);

        vm.Looks[1].PickCommand.Execute(null);
        Assert.Null(vm.PickedLook);
        Assert.DoesNotContain(vm.Looks, l => l.Picked);
        Assert.Empty(host.Calls);   // nothing changes until Continue
        Assert.False(File.Exists(_paths.DesignsDir));
    });

    [Fact]
    public void Continue_carries_the_pick_and_unticking_the_desktop_leaves_it_out() => WithSetup(true, (vm, host) =>
    {
        vm.Looks[1].PickCommand.Execute(null);
        vm.Desktop = false;
        Assert.False(vm.LooksEnabled);
        vm.ContinueCommand.Execute(null);
        Assert.DoesNotContain(host.Calls, c => c.StartsWith("look"));
    });

    [Fact]
    public void Continue_with_a_pick_asks_for_that_look() => WithSetup(true, (vm, host) =>
    {
        vm.Looks[1].PickCommand.Execute(null);
        vm.ContinueCommand.Execute(null);
        Assert.Contains("look beta", host.Calls);
        Assert.True(vm.Saved);
    });

    [Fact]
    public void Without_a_design_service_there_is_no_row() => Sta.Run(() =>
    {
        var vm = new SetupViewModel(new SetupService(_store, new Host()), true, hasAccount: () => false, codexInstalled: () => false);
        Assert.False(vm.HasLooks);
        Assert.Empty(vm.Looks);
    });

    [Fact]
    public void The_window_has_the_row_under_the_desktop_tile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Views", "SetupWindow.xaml")))
            dir = dir.Parent;
        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Sanduhr.App", "Views", "SetupWindow.xaml"));

        int chat = xaml.IndexOf("x:Name=\"DesignWithChatCaption\"", StringComparison.Ordinal);
        int row = xaml.IndexOf("x:Name=\"StartWithALookRow\"", StringComparison.Ordinal);
        int drawers = xaml.IndexOf("x:Name=\"DrawersBox\"", StringComparison.Ordinal);
        Assert.True(chat > 0 && chat < row && row < drawers);
        Assert.Contains("{x:Static core:SetupCopy.StartWithALook}", xaml);
        Assert.Contains("IsEnabled=\"{Binding LooksEnabled}\"", xaml);
        Assert.Contains("Command=\"{Binding PickCommand}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"{Binding AccessibleName}\"", xaml);
        Assert.Contains("IsChecked=\"{Binding Picked, Mode=OneWay}\"", xaml);
    }
}
```

A read-only `settings.json` makes the store's write throw `UnauthorizedAccessException`, which is how the existing `SetupServiceTests` reaches `SetupCopy.SaveFailed`.

- [ ] **Step 2: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~SetupLookTests"`
Expected: the build fails with "'SetupChoices' does not contain a constructor that takes 5 arguments" and "'ISetupHost' does not contain a definition for 'ApplyLook'".

- [ ] **Step 3: The choice and the copy**

In `windows-dotnet/src/Sanduhr.Core/SetupSettings.cs`, change the `SetupChoices` record's declaration to:

```csharp
/// <summary>The three ticks on the setup screen (spec 2), and the ready-made look picked to start with (5.0 spec 5; null for none).
/// Desktop reads as ticked only while the Desk runs (spec 1, the Desk page's own switch). The look is a choice for Continue, never
/// stored: <see cref="ToSettings"/> leaves it out.</summary>
public sealed record SetupChoices(bool Desktop, bool Drawers, bool ClaudeUsage, bool CodexUsage = false, string? Look = null)
```

Nothing else in the record changes. `SetupChange.Lines` compares the ticks member by member; if it compares two `SetupChoices` whole anywhere, make that comparison ignore `Look` (`a with { Look = null } == b with { Look = null }`).

In `windows-dotnet/src/Sanduhr.Core/SetupCopy.cs`, beside `DesignWithChat`, add:

```csharp
    public const string StartWithALook = "Start with a look";
    public const string StartWithALookCaption = "Pick one and Continue puts it on your Desk. You can change it, or put your Desk back, in Settings, Desk.";
```

- [ ] **Step 4: The service**

In `windows-dotnet/src/Sanduhr.App/Services/SetupService.cs`:

1. Add to `ISetupHost`:

```csharp
    /// <summary>Applies a ready-made look (5.0 spec 5). Null when it is on the Desk; otherwise the line that says why not.</summary>
    string? ApplyLook(string id);
```

2. In `SetupService.Apply`, replace `host.ReloadDesk();` (the one after the drawers are turned on, before the first review) with:

```csharp
        host.ReloadDesk();
        // A look picked on the setup screen (5.0 spec 5): after the Desk is on, and before the first drawers review opens,
        // which would refuse it.
        if (now.Desktop && now.Look is { } look && host.ApplyLook(look) is { } said) lines.Add(said);
```

3. Update the class's doc comment: "…usage, one Desk reload, a picked look, and last the first drawers review."

4. Add to `AppSetupHost`:

```csharp
    public string? ApplyLook(string id)
    {
        if (Sanduhr.App.Desk.DeskDesignService.Current is not { } designs) return null;   // no Desk service: nothing to apply to
        var result = designs.ApplyLook(id);
        return result.Ok ? null : result.Message;   // streamer: cant - fixed copy and a built-in look's name
    }
```

Any other class that implements `ISetupHost` (a test's fake) gets `public string? ApplyLook(string id) => null;`.

- [ ] **Step 5: The view model**

In `windows-dotnet/src/Sanduhr.App/ViewModels/SetupViewModel.cs`:

1. Add `using Sanduhr.App.Desk;` and, above `SetupViewModel`, the row's item:

```csharp
/// <summary>One look in setup's "Start with a look" row: the gallery's card, picked or not. Picking is a choice, like a tick: nothing
/// changes until Continue.</summary>
internal sealed partial class SetupLookViewModel : ObservableObject
{
    private readonly Action<SetupLookViewModel> _pick;

    internal SetupLookViewModel(DeskLookCardViewModel card, Action<SetupLookViewModel> pick)
    {
        Card = card;
        _pick = pick;
    }

    public DeskLookCardViewModel Card { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(AccessibleName))] private bool _picked;

    /// <summary>"Marquee look. Clock up top, neon glow. Picked." No notes here: the gallery in Settings carries those.</summary>
    public string AccessibleName => $"{Card.Name} look. {Card.Blurb} {(Picked ? "Picked." : "Not picked.")}";

    [RelayCommand]
    private void Pick() => _pick(this);
}
```

2. Change the constructor's signature to end with `Func<bool>? codexInstalled = null, Func<DeskDesignService?>? looks = null)` and, before its closing `Refresh();`, add:

```csharp
        LoadLooks(looks?.Invoke());
```

3. Add to the class:

```csharp
    /// <summary>The ready-made looks to start with (5.0 spec 5): empty where there is no Desk service to fit them.</summary>
    public ObservableCollection<SetupLookViewModel> Looks { get; } = [];
    public bool HasLooks => Looks.Count > 0;

    /// <summary>The row is live only while "A nicer desktop" is ticked: a look is a Desk.</summary>
    public bool LooksEnabled => Desktop;

    /// <summary>The picked look's id, null for none.</summary>
    public string? PickedLook => Looks.FirstOrDefault(l => l.Picked)?.Card.Id;

    private void LoadLooks(DeskDesignService? service)
    {
        if (service is null) return;
        try
        {
            var snapshot = service.LooksSnapshot();
            var picture = service.LookPicture();
            string backdrop = InkHex.Normalize(snapshot.Wallpaper?.Dominant.FirstOrDefault()?.Hex) ?? "";
            foreach (var data in snapshot.Cards)
            {
                var card = new DeskLookCardViewModel(data.Look.Id, _ => { });
                card.Update(data, picture, backdrop, snapshot.Wallpaper?.Brightness);
                Looks.Add(new SetupLookViewModel(card, Toggle));
            }
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Looks.Clear();   // setup without the row is setup as it always was
        }
    }

    /// <summary>Picking a card marks it; picking it again clears it; one at a time.</summary>
    private void Toggle(SetupLookViewModel look)
    {
        bool pick = !look.Picked;
        foreach (var l in Looks) l.Picked = pick && ReferenceEquals(l, look);
        OnPropertyChanged(nameof(PickedLook));
    }
```

4. Replace `private SetupChoices Choices => new(Desktop, Drawers, ClaudeUsage, CodexUsage);` with:

```csharp
    private SetupChoices Choices => new(Desktop, Drawers, ClaudeUsage, CodexUsage, Desktop ? PickedLook : null);
```

5. Replace `partial void OnDesktopChanged(bool value) => Refresh();` with:

```csharp
    partial void OnDesktopChanged(bool value)
    {
        OnPropertyChanged(nameof(LooksEnabled));
        Refresh();
    }
```

In `windows-dotnet/src/Sanduhr.App/App.xaml.cs`, where the `SetupViewModel` is built (line 1237), add the last argument: `looks: () => _deskDesign`.

- [ ] **Step 6: The window**

In `windows-dotnet/src/Sanduhr.App/Views/SetupWindow.xaml`:

1. Add `xmlns:views="clr-namespace:Sanduhr.App.Views"` to the `Window` element if it is not there, and to `Window.Resources` a `BooleanToVisibilityConverter` under the key `BoolVis` if the window has none under that key.
2. Add this style to `Window.Resources`, after `TickTile`:

```xml
        <!-- A look to start with: a small card that can be picked. The accent ring is the pick; the text-ink ring is the keyboard. -->
        <Style x:Key="LookPick" TargetType="ToggleButton">
            <Setter Property="FocusVisualStyle" Value="{x:Null}" />
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="Margin" Value="0,0,8,8" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ToggleButton">
                        <Border x:Name="ring" CornerRadius="7" Padding="4" BorderThickness="2" BorderBrush="Transparent"
                                Background="{DynamicResource Sanduhr.Brush.Glass}">
                            <ContentPresenter />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsChecked" Value="True">
                                <Setter TargetName="ring" Property="BorderBrush" Value="{DynamicResource Sanduhr.Brush.Accent}" />
                            </Trigger>
                            <Trigger Property="IsKeyboardFocused" Value="True">
                                <Setter TargetName="ring" Property="BorderBrush" Value="{DynamicResource Sanduhr.Brush.Text}" />
                            </Trigger>
                            <Trigger Property="IsEnabled" Value="False">
                                <Setter Property="Opacity" Value="0.4" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
```

3. After the `DesignWithChatCaption` text block (line 122) and before `DrawersBox`, add:

```xml
        <!-- Start with a look (5.0 spec 5): the ready-made looks, small. A pick is a choice for Continue, like the ticks. -->
        <StackPanel x:Name="StartWithALookRow" Margin="28,12,0,0" IsEnabled="{Binding LooksEnabled}"
                    Visibility="{Binding HasLooks, Converter={StaticResource BoolVis}}">
            <TextBlock Text="{x:Static core:SetupCopy.StartWithALook}" FontSize="12" FontWeight="SemiBold"
                       AutomationProperties.HeadingLevel="Level2"
                       Foreground="{DynamicResource Sanduhr.Brush.Text}" />
            <TextBlock Text="{x:Static core:SetupCopy.StartWithALookCaption}" TextWrapping="Wrap" FontSize="11" Margin="0,2,0,8"
                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
            <ItemsControl ItemsSource="{Binding Looks}" Focusable="False" KeyboardNavigation.DirectionalNavigation="Contained">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                        <WrapPanel />
                    </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <ToggleButton Style="{StaticResource LookPick}" Command="{Binding PickCommand}"
                                      IsChecked="{Binding Picked, Mode=OneWay}"
                                      AutomationProperties.Name="{Binding AccessibleName}">
                            <StackPanel Width="139">
                                <Viewbox Width="139" Height="78" HorizontalAlignment="Left">
                                    <views:DeskLookCard Design="{Binding Card.Fitted}" Wallpaper="{Binding Card.Wallpaper}"
                                                        BackdropHex="{Binding Card.BackdropHex}" Brightness="{Binding Card.Brightness}"
                                                        LookName="{Binding Card.Name}" />
                                </Viewbox>
                                <TextBlock Text="{Binding Card.Name}" FontSize="11" FontWeight="SemiBold" Margin="0,4,0,0"
                                           Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                <TextBlock Text="{Binding Card.Blurb}" FontSize="10" TextWrapping="Wrap"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                            </StackPanel>
                        </ToggleButton>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>
```

The window sizes to its content; six cards at 139 DIPs wrap to two rows of three in the window's width. If the window has a fixed height that now clips the usage ticks, raise it by the row's height rather than shrinking anything; `SetupWindowTests` holds the window's reach.

- [ ] **Step 7: Run the tests**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~Setup"` and `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~Setup"`
Expected: PASS, the older setup suites unchanged: with no look picked, `Apply` makes the same calls in the same order.

- [ ] **Step 8: Run every suite and commit**

```bash
cd windows-dotnet && dotnet test
git add src tests
git commit -m "feat(windows): setup offers a look to start with

Picked like a tick, applied on Continue after the Desk is on.

Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>"
```

---

### Task 13: What's New, the record, and the checks that need a real Desk

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/WhatsNewCards.cs` (`Release500`, and the anchors `Anchored` reads), `windows-dotnet/tests/Sanduhr.Tests/WhatsNewCardsTests.cs`
- Modify: `CHANGELOG.md` (under `## Unreleased`, `### New`)
- Modify: `docs/superpowers/specs/2026-11-20-desk-looks-design.md`, `docs/superpowers/specs/2026-11-17-windows-5-0-umbrella.md`, `docs/BACKLOG.md`
- Not changed: `docs/PRIVACY.md` and `docs/store/hub-privacy-5.0.0.md`. The looks and the glow add no data, destination or file, so neither gets a paragraph.

**Interfaces:**
- Consumes: `SettingsIndex.DeskLooks` (Task 11), `SettingsPage.Desk`.
- Produces: the What's New card `ready-made-looks` for 5.0.0.

- [ ] **Step 1: The What's New card, test first**

In `windows-dotnet/tests/Sanduhr.Tests/WhatsNewCardsTests.cs`, replace `Release500_ShowsTheNotificationsCard` with:

```csharp
    [Fact]
    public void Release500_ShowsTheNotificationsCardAndTheLooksCard()
    {
        var cards = WhatsNew.Cards("4.9.0", "5.0.0");
        Assert.Equal(["agent-notifications", "ready-made-looks"], cards.Select(c => c.Id));

        var notify = cards[0];
        Assert.Equal("Know when your agent is done", notify.Title);
        Assert.StartsWith("A notification, a Desk card or a glow when Claude Code, Codex or another agent finishes a long turn.", notify.Body);
        Assert.Equal(SettingsPage.Notifications, notify.Destination);
        Assert.Equal(SettingsIndex.NotifyAgents, notify.Anchor);

        var looks = cards[1];
        Assert.Equal("Six looks for your Desk", looks.Title);
        Assert.Equal("Pick a ready-made look in Settings, Desk and your whole Desk changes in one click. Put my Desk back undoes it. There's a neon glow for the ink, too.", looks.Body);
        Assert.Equal(SettingsPage.Desk, looks.Destination);
        Assert.Equal(SettingsIndex.DeskLooks, looks.Anchor);
        Assert.False(looks.NamesCodex);
    }
```

In the same file, the test that lists every card's id in order (near line 20) gets `"ready-made-looks"` right after `"agent-notifications"`.

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~WhatsNew"`
Expected: FAIL: one card where two are expected.

In `windows-dotnet/src/Sanduhr.Core/WhatsNewCards.cs`, add to `Release500`, after the notifications card:

```csharp
        // Spec 5, ready-made looks. Show me lands on Settings ▸ Desk ▸ Looks. Names no agent, so it needs no trademark line.
        WhatsNewCard.Of("5.0.0", "ready-made-looks", "Six looks for your Desk",
            "Pick a ready-made look in Settings, Desk and your whole Desk changes in one click. Put my Desk back undoes it. There's a neon glow for the ink, too.",
            "", // Color
            WhatsNewPreview.None, SettingsPage.Desk),
```

and give the card its anchor in `AnchorFor` (the switch that maps `"agent-notifications"` to `SettingsIndex.NotifyAgents`): add `"ready-made-looks" => SettingsIndex.DeskLooks,`. Update the comment above `Release500`: the looks card has joined.

Run the same filter again. Expected: PASS. If a What's New test caps a release at a number of cards or checks every glyph against a list of known glyphs, follow what it asks; `` is Segoe MDL2's Color glyph, and any glyph the list already allows will do.

- [ ] **Step 2: The changelog**

In `CHANGELOG.md`, under `## Unreleased`, `### New`, add after the notifications entries:

```markdown
- **Six ready-made looks for the Desk.** Settings ▸ Desk ▸ Looks shows Quiet corner, Ice, Marquee, Terminal, Editorial and Dashboard, each drawn over your own wallpaper. One click puts a look on your Desk: its pieces, their places and sizes, its font and its ink. Your Launcher buttons, your messages, your weather place and which monitor each piece is on stay as they are. A look adapts to your wallpaper: where its ink would be hard to read it turns its plate on or takes automatic ink, and the card says so. Put my Desk back returns to the Desk you had before the first look, however many you try, and a built-in look never takes one of your ten saved designs. Set up Sanduhr offers the same six to start with.
- **A neon glow for the Desk's ink.** Settings ▸ Desk Look ▸ Ink and color has Neon glow and a Glow strength slider. The glow is in the ink's own colors, so a gradient ink glows as a gradient, and it covers the message too. It takes the place of the drop shadow: turning one on turns the other off. An agent can set it with `glow` in `propose_desk`.
```

If the measurement in Task 4 left `UnderIconsOneColor` true, add to the second entry: ` Under the desktop icons the glow is one color.`

- [ ] **Step 3: Amend the spec to match what was built**

In `docs/superpowers/specs/2026-11-20-desk-looks-design.md`:

1. Change the status line to `**Status:** design approved by Este, 2026-11-20; amended with the plan (2026-11-20-desk-looks.md) where the code map changed it. Windows only.`
2. Section 3, the table: Quiet corner's font becomes `Corbel (Segoe UI)`, Dashboard's `Segoe UI`. Replace the paragraph that begins "Bahnschrift, Consolas and Georgia exist on every supported Windows" with: "WPF lists Segoe UI as one family; weights are not families there, so no look names Segoe UI Light or Segoe UI Semibold (checked 2026-11-20). Every font a look or its fallback names is in `DeskLook.WindowsFonts`, the families that ship with every supported Windows, or is bundled; a test holds that, and another checks each name against this PC's own list."
3. Section 3, "What a look's design must and must not carry": add "The pieces at one anchor stack in the order the file lists them."
4. Section 4: add the third font step (the system font when the fallback is missing too, with its note) and that the fit is judged over the Desk as it is, so the user's labelled Launcher buttons count.
5. Section 5: replace steps 1 to 7 with the plan's Task 10 steps (the drawers review line; "already" needs the Desk on; "off" means either switch; the layout is written in the file's order by `LayoutOver`, not merged).
6. Section 6: the Looks section sits under the Desk switch; the look's name stands where the look puts its clock; what a piece needs is a link under the card, outside the button.
7. Section 7: the measurement's numbers and the value of `UnderIconsOneColor`; the glow covers a followed widget theme's ink; Launcher labels glow in one color; drawer labels neither glow nor keep their shadow while the glow is on; `desk_ink` is written as before while the glow is off; the padding is 16 at strength 0.2 and 26 at 1.0.
8. Section 10: the every-look-file test lives in the App tests.

In `docs/superpowers/specs/2026-11-17-windows-5-0-umbrella.md`, under "For the release paperwork", add:

```markdown
- **Ready-made looks and the glow (spec 5).** No new data, destination, capability or file: the looks are embedded, the wallpaper on
  a card stays in memory, the glow is two values inside `desk_ink`. Reviewer letter: nothing required. Store What's New: a line for
  the six looks and one for the neon glow. Trailer: flip through the looks in Settings ▸ Desk. The Mac's MCP refuses `glow` by design.
```

In `docs/BACKLOG.md`, add:

```markdown
- **For the Mac agent: the Desk design field `glow` (Windows 5.0).** `false`, `true` (on at the strength already set) or a number from
  0.2 to 1.0; on with `shadow: true` is refused. Stored on Windows as `desk_ink.glow` and `desk_ink.glow_strength`. The Mac's MCP
  refuses the field today, as it refuses every field the Mac does not draw. Nothing to do unless the Mac ever draws a glow.
- **After 5.0: looks that bring their own wallpaper** (dashboard task `task-1795192163146-ab9ypxtf5`). The look file's wrapper
  ignores unknown fields, so a `wallpaper` member can be added without breaking a 5.0 build.
- **Parked from the looks plan:** an agent's design applied over an untouched look still saves "Before <name>" (rule A covers looks
  and Switch to only); a preview on hover; the user's own looks in the gallery.
```

- [ ] **Step 4: Run every suite, then commit**

```bash
cd windows-dotnet && dotnet test
cd .. && git add CHANGELOG.md docs windows-dotnet
git commit -m "docs(windows): the looks and the glow in What's New, the changelog and the spec

Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>"
```

- [ ] **Step 5: Stop for the owner: the checks that need a real Desk**

Nothing in this step is run by an agent. A dev build shares its data folders with the installed app, so it is never launched on this PC; these checks run on an installed test build, which the owner asks for and starts. Report that Tasks 1 to 13 are committed, give the three suite counts against the baseline, and hand over this list:

1. **Audition the six.** For each look, on a dark and a light wallpaper, with "Desktop icons" drawing over the Desk on and off: does it look right? Positions, sizes, fonts and the hidden `glow` and `companion` pieces are all the owner's to change; each change is an edit to one JSON file in `windows-dotnet/src/Sanduhr.App/Desk/Looks/`, and the tests pin the rules, not the positions. A look can also be tried on the installed app before any build through `propose_desk` (leave `glow` out on a 4.9 install, which refuses it); `propose_desk` stacks the pieces at one anchor in Settings order, so Marquee's clock-above-message shows reversed there.
2. **The glow.** Both ends of the slider, on a gradient ink and on one color, with the plate on and off. Nothing clipped at the island's edge at full strength.
3. **The glow under the icons, through a clock minute.** With "Desktop icons" on, watch the clock change: the glow must follow the new digits. If it shows the old ones, set `DeskGlowLayer.UnderIconsOneColor = true`, whatever the measurement said: the copy under the icons has kept a stale brush before (2026-11-05), and the one-color glow has no brush to go stale.
4. **Browsing.** Flip through all six, then Put my Desk back: the Desk from before returns, Designs lists one new entry, and a second press says "Your Desk is already as it was."
5. **With the Desk off:** a card switches it on.
6. **Narrator** on the Looks section: the row's name, each card's name with its notes, the status line after a press.

Not checked by hand: setup on a clean profile (covered by the App tests and by the release smoke on a fresh Store install), and the Windows 10 font fallback (unit tests only; there is no Windows 10 machine).

---

## Self-review

**Spec coverage.** Section 2's decisions and section 3's file and rules: Tasks 7 and 9. Section 4 (fitting): Task 8. Section 5 (applying, rules A and B, the Desk switched on): Task 10. Section 6 (the gallery, cards as buttons, setup): Tasks 11 and 12. Section 7 (the glow's drawing, the padding, under the icons, Settings, the design field): Tasks 1 to 6. Section 8's units map one to one onto the File Structure table. Section 9's errors: a bad look file (Tasks 7 and 9), a Desk that cannot be saved and a design that cannot be written (Task 10), a wallpaper that cannot be read (Tasks 8, 10 and 11), a glow that fails to draw (Tasks 4 and 5). Section 10's tests: each bullet has a named test; the three that moved or changed are rulings 10 and 11 and the "untouched" tests in Task 8. Section 11: Task 13. Section 12 is out of scope and stays out; two items are parked in the backlog.

**Where the plan knowingly leaves the page open.** The older guard tests around Settings, the design's field list and What's New pin text this plan changes; each task names which and says what the fix is, and none may be loosened. The six looks' compositions are drafts by design.

**Types, checked across tasks.** `DeskGlow.Pad(double)`, `DeskInkSettings.WithGlow/WithShadow/WithGlowStrength`, `ResolvedInk.Glow/GlowOneColor`, `DeskInk.Glowing` (Task 1) are used with those names in Tasks 2, 4, 5, 6 and 11. `DeskGlowChoice(bool On, double? Strength)` (Task 2) in Tasks 7, 8 and 11. `DeskGlowLayer.On/Wrap/Copy/OneColor/Middle/Failed/UnderIconsOneColor` and `GlowHost` (Task 4) in Tasks 5, 6 and 11. `DeskLook`, `DeskLookCatalog.Load/Find/Order`, `DeskLook.WindowsFonts/Rules/Shows` (Task 7) in Tasks 8 to 12. `DeskLookFit.Fit/IsLook/IsExactly/Untouched/LayoutOver/Needs`, `DeskLookFitResult`, `DeskLookNeed(Piece, Line)` (Task 8) in Tasks 9 to 12. `DeskDesignService.ApplyLook/LooksSnapshot/LookPicture/LookCatalog/LookWallpaper/HasWatcher`, `DeskLookCardData`, `DeskLooksSnapshot` (Task 10) in Tasks 11 and 12. `DeskLookCardViewModel.Update(DeskLookCardData, ImageSource?, string, double?)` and its `(string id, Action<string> apply)` constructor (Task 11) in Task 12. `SetupChoices.Look` and `ISetupHost.ApplyLook` (Task 12) nowhere else.

