# Drawers Look and Placement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give drawers four looks (Solid, Glass, Outline, Bare) with an opacity setting and a per-drawer override, and give placement snapping, a clear lane around the desktop icons, Tidy with Undo, and AI chat words for both.

**Architecture:** Core holds the pure parts: the new drawer settings, the snap rules, the icon area and its fallback, Tidy's slots, and the chat words. The App extends `DrawerLook`/`DrawerPiece` to draw each look, gives `DrawerService` a snap callback that `DeskController` answers from the drawers' and Desk pieces' rectangles, reads desktop icon positions through the Shell (read-only), and adds Tidy with an Undo bar, the Drawers page controls, a Look submenu, and "Use these drawer settings" in the chat window.

**Tech Stack:** .NET 10, WPF, classic `[ComImport]` interop (as `ShellNative`), xUnit.

**Spec:** `docs/superpowers/specs/2026-10-31-drawers-look-placement-design.md`. Builds on `cart/setup-4-5` (Set up Sanduhr); read `docs/superpowers/specs/2026-10-31-setup-design.md` section 3 for the AI chat parts.

## Global Constraints

- Windows only, `windows-dotnet/`. CRLF working tree, LF index: never `sed -i` a tracked file.
- Settings stay in the `drawers` object, decoded entry by entry; an unreadable entry takes its default. Upgraders see no change: look `solid`, opacity 55, places untouched.
- Placement units: `DrawerPlace` X/Y are work-area DIPs per monitor (`DrawerScreens.ToPlace`); every new Core rule works in those units.
- Constants: `DrawerPlacement.Width` 360, `OpenHeight` 330, `FoldedHeight` 42, `Margin` 24, `Gap` 12. Snap threshold 12, grid 8.
- The MCP project does not change (no new `Compile Include`; `TrustBoundaryTests` unchanged). `propose_desk` does not change.
- Icon reading is read-only. Nothing moves or writes desktop icons.
- Every control has an accessible name; light mode via theme brushes; no em dashes in copy; copy strings in `SettingsNames` or `SetupCopy`.
- Streamer mode: nothing new draws a name or path.
- Tests: Core `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter <Name>`; App `dotnet test windows-dotnet/tests/Sanduhr.App.Tests/Sanduhr.App.Tests.csproj -c Release --filter <Name>`.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **A drawer dropped where snapping and the icon lane disagree** (snap pulls it onto icons): keep-clear wins; the drawer ends off the icons and on screen (Task 3, `DrawerIconArea.NearestFree` after `DrawerSnap.Snap`).
2. **Tidy on a monitor too small for all drawers:** wrap toward the centre, and any drawer that still doesn't fit keeps its place rather than going off screen (Task 3).
3. **Outline or Bare over a busy wallpaper:** labels stay readable (chips) and a click on an empty part of the drawer still lands on the drawer (Task 4).
4. **Tidy or chat settings while a review is open:** refused with the busy line, nothing moved (Task 6, Task 8).
5. **The icon reader failing** (Explorer restarting, COM error, not STA): the fallback columns apply and nothing throws (Task 5).

---

### Task 0: Spike: blur behind a Desk window, and reading icon positions (throwaway)

**Files:**
- Create: `spikes/drawer-look/` (a console or WPF exe, not in `Sanduhr.slnx`, not in CI)
- Modify: `windows-dotnet/docs/checklist.md` (one verdict line)

Answer two questions, each PASS or FAIL with evidence:

1. **Blur.** On a per-pixel layered `HwndSource` window owned like the Desk's (`DesktopLayerHost.Create`: `WS_POPUP`, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, `UsesPerPixelTransparency = true`, owner = `GetShellWindow()`), does the wallpaper blur behind a semi-transparent area? Try in order: `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE = 38, DWMSBT_TRANSIENTWINDOW = 3)` with `DwmExtendFrameIntoClientArea(-1)` (see `Interop/MicaHelper.cs`); then `SetWindowCompositionAttribute` with `ACCENT_ENABLE_ACRYLICBLURBEHIND = 4`. Capture a screenshot of each attempt over a busy wallpaper.
2. **Icons.** Through `IShellWindows.FindWindowSW(SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1)` → `IServiceProvider.QueryService(SID_STopLevelBrowser, IID_IShellBrowser)` → `IShellBrowser.QueryActiveShellView` → `IFolderView`: read `ItemCount(SVGIO_ALLVIEW)`, each item's `GetItemPosition`, and `GetSpacing`. Compare with what is on screen. Repeat after `taskkill /f /im explorer.exe && start explorer`, with auto-arrange on and off, and on a second monitor at a different scale. Note the coordinate space (DefView client pixels vs screen).

- [ ] **Step 1:** Build the probe; record each attempt's result with a screenshot path.
- [ ] **Step 2:** Keep the verified `[ComImport]` declarations (GUIDs and method order) in the spike; Task 5 copies them.
- [ ] **Step 3:** Append to `windows-dotnet/docs/checklist.md` under the Desk spikes: `- [x] Drawer look spike: blur <PASS via X | FAIL>; icons <PASS | FAIL: reason>; spikes/drawer-look/`.
- [ ] **Step 4:** Commit the spike and the checklist line.

```bash
git add spikes/drawer-look windows-dotnet/docs/checklist.md
git commit -m "spike(drawers): blur behind a Desk window and reading desktop icon positions"
```

---

### Task 1: The new drawer settings (Core)

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/DrawerStyle.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/DrawerSettings.cs` (record init props, `Decode`, `Encode`)
- Test: `windows-dotnet/tests/Sanduhr.Tests/DrawerStyleTests.cs`

**Interfaces:**
- Produces:
  - `public enum DrawerLookKind { Solid, Glass, Outline, Bare }`
  - `public enum DrawerCorner { TopLeft, TopRight, BottomLeft, BottomRight }`, `public enum DrawerFlow { Column, Row }`
  - `public sealed record DrawerTidy(DrawerCorner Corner, DrawerFlow Flow)` with `static DrawerTidy Default` = `(TopRight, Column)`
  - `public static class DrawerStyle`: `MinOpacity = 20`, `MaxOpacity = 100`, `DefaultOpacity = 55`, `LookWords` (`solid, glass, outline, bare`), `CornerWords` (`tl, tr, bl, br`), `FlowWords` (`column, row`), `static string Word(DrawerLookKind)`, `static DrawerLookKind? Look(string?)`, `static string Word(DrawerCorner)`, `static DrawerCorner? Corner(string?)`, `static string Word(DrawerFlow)`, `static DrawerFlow? Flow(string?)`, `static bool HasFill(DrawerLookKind k) => k is Solid or Glass`
  - `DrawerSettings` init props: `DrawerLookKind Look = Solid`, `int Opacity = 55`, `IReadOnlyDictionary<string, DrawerLookKind> Looks = {}`, `bool Snap = true`, `bool KeepClear = true`, `DrawerTidy Tidy = DrawerTidy.Default`, and `DrawerLookKind LookFor(string id) => Looks.TryGetValue(id, out var k) ? k : Look`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerStyleTests
{
    [Fact]
    public void An_older_drawers_object_reads_as_todays_look()
    {
        var s = DrawerSettings.Decode(JsonNode.Parse("""{"enabled":true,"drawers":[],"rules":[]}"""));
        Assert.Equal(DrawerLookKind.Solid, s.Look);
        Assert.Equal(55, s.Opacity);
        Assert.Empty(s.Looks);
        Assert.True(s.Snap);
        Assert.True(s.KeepClear);
        Assert.Equal(DrawerTidy.Default, s.Tidy);
    }

    [Fact]
    public void Every_new_key_round_trips()
    {
        var s = DrawerSettings.Default with
        {
            Look = DrawerLookKind.Glass, Opacity = 70, Snap = false, KeepClear = false,
            Looks = new Dictionary<string, DrawerLookKind> { ["images"] = DrawerLookKind.Bare },
            Tidy = new DrawerTidy(DrawerCorner.BottomLeft, DrawerFlow.Row),
        };
        var back = DrawerSettings.Decode(JsonNode.Parse(s.Encode().ToJsonString()));
        Assert.Equal(DrawerLookKind.Glass, back.Look);
        Assert.Equal(70, back.Opacity);
        Assert.False(back.Snap);
        Assert.False(back.KeepClear);
        Assert.Equal(DrawerLookKind.Bare, back.LookFor("images"));
        Assert.Equal(DrawerLookKind.Glass, back.LookFor("documents"));
        Assert.Equal(new DrawerTidy(DrawerCorner.BottomLeft, DrawerFlow.Row), back.Tidy);
    }

    [Theory]
    [InlineData("""{"look":"neon"}""")]
    [InlineData("""{"opacity":5}""")]
    [InlineData("""{"opacity":"x"}""")]
    [InlineData("""{"snap":"yes"}""")]
    [InlineData("""{"tidy":{"corner":"middle","direction":"row"}}""")]
    [InlineData("""{"looks":{"images":"neon","BAD ID":"bare"}}""")]
    public void Each_unreadable_key_takes_its_default_alone(string json)
    {
        var s = DrawerSettings.Decode(JsonNode.Parse(json));
        Assert.Equal(DrawerLookKind.Solid, s.Look);
        Assert.InRange(s.Opacity, DrawerStyle.MinOpacity, DrawerStyle.MaxOpacity);
        Assert.True(s.Snap);
        Assert.Empty(s.Looks);
    }

    [Fact]
    public void An_opacity_out_of_range_is_clamped_and_the_tidy_parts_fall_back_one_by_one()
    {
        Assert.Equal(100, DrawerSettings.Decode(JsonNode.Parse("""{"opacity":140}""")).Opacity);
        var t = DrawerSettings.Decode(JsonNode.Parse("""{"tidy":{"corner":"bl"}}""")).Tidy;
        Assert.Equal(new DrawerTidy(DrawerCorner.BottomLeft, DrawerFlow.Column), t);
    }

    [Fact]
    public void Words_round_trip_and_only_solid_and_glass_have_a_fill()
    {
        foreach (var w in DrawerStyle.LookWords) Assert.Equal(w, DrawerStyle.Word(DrawerStyle.Look(w)!.Value));
        foreach (var w in DrawerStyle.CornerWords) Assert.Equal(w, DrawerStyle.Word(DrawerStyle.Corner(w)!.Value));
        foreach (var w in DrawerStyle.FlowWords) Assert.Equal(w, DrawerStyle.Word(DrawerStyle.Flow(w)!.Value));
        Assert.True(DrawerStyle.HasFill(DrawerLookKind.Glass));
        Assert.False(DrawerStyle.HasFill(DrawerLookKind.Outline));
    }
}
```

The `opacity: 5` row expects the default (55), not a clamp to 20: a value below the minimum is a damaged entry. `140` clamps to 100 because it is a readable number past the top (a slider typed past its end). Keep both behaviours exactly as the tests say.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter DrawerStyleTests`
Expected: FAIL to build.

- [ ] **Step 3: Write `DrawerStyle.cs`**

```csharp
namespace Sanduhr.Core;

/// <summary>How drawers blend into the desktop (drawers look and placement spec, section 1).</summary>
public enum DrawerLookKind { Solid, Glass, Outline, Bare }

/// <summary>Where Tidy lines drawers up, and which way (spec, section 2).</summary>
public enum DrawerCorner { TopLeft, TopRight, BottomLeft, BottomRight }

public enum DrawerFlow { Column, Row }

public sealed record DrawerTidy(DrawerCorner Corner, DrawerFlow Flow)
{
    /// <summary>Top right, a column down: where drawers have always arrived.</summary>
    public static DrawerTidy Default { get; } = new(DrawerCorner.TopRight, DrawerFlow.Column);
}

/// <summary>The words settings.json and an AI chat use for looks, corners and directions, and the opacity range.</summary>
public static class DrawerStyle
{
    public const int MinOpacity = 20;
    public const int MaxOpacity = 100;
    /// <summary>Today's drawer plate (<c>DrawerLook.MinPlateOpacity</c>, 0.55): an upgrade looks the same.</summary>
    public const int DefaultOpacity = 55;

    public static IReadOnlyList<string> LookWords { get; } = ["solid", "glass", "outline", "bare"];
    public static IReadOnlyList<string> CornerWords { get; } = ["tl", "tr", "bl", "br"];
    public static IReadOnlyList<string> FlowWords { get; } = ["column", "row"];

    public static string Word(DrawerLookKind k) => LookWords[(int)k];
    public static DrawerLookKind? Look(string? w) => Index(LookWords, w) is { } i ? (DrawerLookKind)i : null;
    public static string Word(DrawerCorner c) => CornerWords[(int)c];
    public static DrawerCorner? Corner(string? w) => Index(CornerWords, w) is { } i ? (DrawerCorner)i : null;
    public static string Word(DrawerFlow f) => FlowWords[(int)f];
    public static DrawerFlow? Flow(string? w) => Index(FlowWords, w) is { } i ? (DrawerFlow)i : null;

    /// <summary>Solid and Glass draw a plate; Outline and Bare draw none (the opacity setting hides for them).</summary>
    public static bool HasFill(DrawerLookKind k) => k is DrawerLookKind.Solid or DrawerLookKind.Glass;

    private static int? Index(IReadOnlyList<string> words, string? w)
    {
        for (int i = 0; i < words.Count; i++) if (string.Equals(words[i], w, StringComparison.Ordinal)) return i;
        return null;
    }
}
```

- [ ] **Step 4: Extend `DrawerSettings`**

Add init props after `Hidden` (line ~22):

```csharp
    /// <summary>How every drawer blends in, unless <see cref="Looks"/> names one (look and placement spec, section 1).</summary>
    public DrawerLookKind Look { get; init; } = DrawerLookKind.Solid;

    /// <summary>Solid's and Glass's fill, 20 to 100 percent.</summary>
    public int Opacity { get; init; } = DrawerStyle.DefaultOpacity;

    /// <summary>A drawer's own look, by id, when it differs from <see cref="Look"/>.</summary>
    public IReadOnlyDictionary<string, DrawerLookKind> Looks { get; init; } = new Dictionary<string, DrawerLookKind>();

    /// <summary>Drawers snap while dragged (spec, section 2).</summary>
    public bool Snap { get; init; } = true;

    /// <summary>Drawers keep clear of the desktop icons when dropped, arriving or tidied.</summary>
    public bool KeepClear { get; init; } = true;

    /// <summary>Where Tidy and arrival line drawers up.</summary>
    public DrawerTidy Tidy { get; init; } = DrawerTidy.Default;

    public DrawerLookKind LookFor(string id) => Looks.TryGetValue(id, out var k) ? k : Look;
```

In `Decode`'s object initializer, add:

```csharp
        Look = DrawerStyle.Look(SettingsJson.String(o, "look", "")) ?? DrawerLookKind.Solid,
        Opacity = DecodeOpacity(o["opacity"]),
        Looks = DecodeLooks(o["looks"]),
        Snap = SettingsJson.Bool(o, "snap", true),
        KeepClear = SettingsJson.Bool(o, "keep_clear", true),
        Tidy = DecodeTidy(o["tidy"]),
```

and the helpers:

```csharp
    private static int DecodeOpacity(JsonNode? n)
    {
        if (n is not JsonValue v || !v.TryGetValue<double>(out var d) || !double.IsFinite(d) || d < DrawerStyle.MinOpacity)
            return DrawerStyle.DefaultOpacity;
        return (int)Math.Round(Math.Min(d, DrawerStyle.MaxOpacity));
    }

    private static IReadOnlyDictionary<string, DrawerLookKind> DecodeLooks(JsonNode? n)
    {
        var looks = new Dictionary<string, DrawerLookKind>(StringComparer.Ordinal);
        if (n is JsonObject o)
            foreach (var (id, v) in o)
                if (DrawerIds.IsValid(id) && v is JsonValue s && s.TryGetValue<string>(out var w) && DrawerStyle.Look(w) is { } k)
                    looks[id] = k;
        return looks;
    }

    private static DrawerTidy DecodeTidy(JsonNode? n)
    {
        if (n is not JsonObject o) return DrawerTidy.Default;
        return new DrawerTidy(
            DrawerStyle.Corner(SettingsJson.String(o, "corner", "")) ?? DrawerTidy.Default.Corner,
            DrawerStyle.Flow(SettingsJson.String(o, "direction", "")) ?? DrawerTidy.Default.Flow);
    }
```

In `Encode`, after `hidden`:

```csharp
        ["look"] = DrawerStyle.Word(Look),
        ["opacity"] = Opacity,
        ["looks"] = new JsonObject(Looks.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, (JsonNode?)DrawerStyle.Word(p.Value)))),
        ["snap"] = Snap,
        ["keep_clear"] = KeepClear,
        ["tidy"] = new JsonObject { ["corner"] = DrawerStyle.Word(Tidy.Corner), ["direction"] = DrawerStyle.Word(Tidy.Flow) },
```

Match `Encode`'s real construction style (an object initializer or sequential assignments) when adding these.

- [ ] **Step 5: Run the tests**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "DrawerStyleTests|DrawerSettingsTests|DrawerPlacementTests"`
Expected: PASS (the existing drawer settings tests still pass).

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/DrawerStyle.cs windows-dotnet/src/Sanduhr.Core/DrawerSettings.cs windows-dotnet/tests/Sanduhr.Tests/DrawerStyleTests.cs
git commit -m "feat(drawers): look, opacity, per-drawer looks, snap, keep clear and tidy in the drawers settings"
```

---

### Task 2: Snapping (Core)

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/DrawerSnap.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/DrawerSnapTests.cs`

**Interfaces:**
- Produces:
  - `public readonly record struct DrawerRect(double X, double Y, double W, double H)` with `Right`, `Bottom`, `bool Overlaps(DrawerRect o)` (strict: touching edges do not overlap), `DrawerRect At(double x, double y)`, `DrawerRect Inflate(double d)`
  - `public static class DrawerSnap` with `const double Threshold = 12`, `const double Grid = 8`, `static DrawerRect Snap(DrawerRect moving, DrawerRect work, IReadOnlyList<DrawerRect> drawers, IReadOnlyList<DrawerRect> pieces)` and `static DrawerRect Keep(DrawerRect r, DrawerRect work)` (keeps the title bar on the work area, as `DrawerPlacement.Clamp`)

Rules, each axis on its own, the first category with a candidate within `Threshold` wins, nearest within it:
1. Other drawers: align left↔left, right↔right, and gap (`left = other.Right + Gap`, `right = other.X - Gap`); vertically the same with top/bottom.
2. Desk pieces: the same four candidates against each piece rect.
3. Screen: `X = work.X + Margin`, `Right = work.Right - Margin`; same for Y.
4. Grid: round the offset from `work` to a multiple of 8.
Then `Keep`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerSnapTests
{
    private static readonly DrawerRect Work = new(0, 0, 1920, 1032);
    private static DrawerRect D(double x, double y, double h = DrawerPlacement.OpenHeight) => new(x, y, DrawerPlacement.Width, h);

    [Fact]
    public void A_drawer_near_another_lines_up_and_takes_the_gap()
    {
        var other = D(1000, 100);
        var s = DrawerSnap.Snap(D(1005, 100 + DrawerPlacement.OpenHeight + 20), Work, [other], []);
        Assert.Equal(1000, s.X);                                                   // left edges align
        Assert.Equal(100 + DrawerPlacement.OpenHeight + DrawerPlacement.Gap, s.Y); // gap snaps to 12
    }

    [Fact]
    public void Drawers_win_over_pieces_and_the_screen()
    {
        var other = D(1543, 300);
        var piece = new DrawerRect(1540, 700, 200, 100);
        var s = DrawerSnap.Snap(D(1536, 300), Work, [other], [piece]);
        Assert.Equal(1543, s.X);
    }

    [Fact]
    public void A_drawer_docks_beside_a_desk_piece_at_the_gap()
    {
        var clock = new DrawerRect(24, 700, 400, 200);
        var s = DrawerSnap.Snap(D(430, 705), Work, [], [clock]);
        Assert.Equal(clock.Right + DrawerPlacement.Gap, s.X);
        Assert.Equal(700, s.Y);   // tops align
    }

    [Fact]
    public void Near_a_screen_edge_it_snaps_to_the_margin()
    {
        var s = DrawerSnap.Snap(D(1920 - 360 - 30, 30), Work, [], []);
        Assert.Equal(1920 - DrawerPlacement.Margin - DrawerPlacement.Width, s.X);
        Assert.Equal(DrawerPlacement.Margin, s.Y);
    }

    [Fact]
    public void With_nothing_near_it_lands_on_the_grid()
    {
        var s = DrawerSnap.Snap(D(803, 501), Work, [], []);
        Assert.Equal(0, s.X % DrawerSnap.Grid);
        Assert.Equal(0, s.Y % DrawerSnap.Grid);
    }

    [Fact]
    public void Beyond_the_threshold_nothing_but_the_grid_applies()
    {
        var other = D(1000, 100);
        var s = DrawerSnap.Snap(D(1000 + 40, 600), Work, [other], []);
        Assert.Equal(1040, s.X);   // 40 away: the grid, already on it
    }

    [Fact]
    public void Keep_brings_a_drawer_dragged_off_screen_back_by_its_title()
    {
        var k = DrawerSnap.Keep(D(1900, 1100), Work);
        Assert.Equal(1920 - DrawerPlacement.Width, k.X);
        Assert.Equal(1032 - DrawerPlacement.FoldedHeight, k.Y);
    }

    [Fact]
    public void Touching_edges_do_not_overlap()
    {
        Assert.False(new DrawerRect(0, 0, 10, 10).Overlaps(new DrawerRect(10, 0, 10, 10)));
        Assert.True(new DrawerRect(0, 0, 10, 10).Overlaps(new DrawerRect(9, 9, 10, 10)));
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter DrawerSnapTests`
Expected: FAIL to build.

- [ ] **Step 3: Write `DrawerSnap.cs`**

```csharp
namespace Sanduhr.Core;

/// <summary>A rectangle in a monitor's work-area DIPs, the units <see cref="DrawerPlace"/> uses.</summary>
public readonly record struct DrawerRect(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public bool Overlaps(DrawerRect o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
    public DrawerRect At(double x, double y) => this with { X = x, Y = y };
    public DrawerRect Inflate(double d) => new(X - d, Y - d, W + 2 * d, H + 2 * d);
}

/// <summary>
/// Where a dragged drawer settles (look and placement spec, section 2): each axis on its own, the first rule with a
/// candidate within <see cref="Threshold"/> wins (other drawers, then Desk pieces, then the screen's margin), nearest
/// within it; with none, the 8 DIP grid. The title bar always stays on the work area.
/// </summary>
public static class DrawerSnap
{
    public const double Threshold = 12;
    public const double Grid = 8;

    public static DrawerRect Snap(DrawerRect moving, DrawerRect work, IReadOnlyList<DrawerRect> drawers, IReadOnlyList<DrawerRect> pieces)
    {
        double x = Axis(moving.X, moving.W, work.X, work.Right,
            drawers.Select(d => (d.X, d.Right)), pieces.Select(p => (p.X, p.Right)));
        double y = Axis(moving.Y, moving.H, work.Y, work.Bottom,
            drawers.Select(d => (d.Y, d.Bottom)), pieces.Select(p => (p.Y, p.Bottom)));
        return Keep(moving.At(x, y), work);
    }

    public static DrawerRect Keep(DrawerRect r, DrawerRect work) => r.At(
        Math.Clamp(r.X, work.X, Math.Max(work.X, work.Right - r.W)),
        Math.Clamp(r.Y, work.Y, Math.Max(work.Y, work.Bottom - DrawerPlacement.FoldedHeight)));

    private static double Axis(double start, double size, double lo, double hi,
        IEnumerable<(double Start, double End)> drawers, IEnumerable<(double Start, double End)> pieces)
    {
        foreach (var group in new[] { drawers, pieces })
            if (Nearest(start, Candidates(size, group)) is { } hit) return hit;
        if (Nearest(start, [lo + DrawerPlacement.Margin, hi - DrawerPlacement.Margin - size]) is { } edge) return edge;
        return lo + Math.Round((start - lo) / Grid) * Grid;
    }

    private static IEnumerable<double> Candidates(double size, IEnumerable<(double Start, double End)> others)
    {
        foreach (var (s, e) in others)
        {
            yield return s;                              // start edges align
            yield return e - size;                       // end edges align
            yield return e + DrawerPlacement.Gap;        // after it, at the gap
            yield return s - DrawerPlacement.Gap - size; // before it, at the gap
        }
    }

    private static double? Nearest(double at, IEnumerable<double> candidates)
    {
        double? best = null;
        foreach (var c in candidates)
            if (Math.Abs(c - at) <= Threshold && (best is null || Math.Abs(c - at) < Math.Abs(best.Value - at))) best = c;
        return best;
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter DrawerSnapTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/DrawerSnap.cs windows-dotnet/tests/Sanduhr.Tests/DrawerSnapTests.cs
git commit -m "feat(drawers): snapping to drawers, Desk pieces, the screen margin and an 8 DIP grid"
```

---

### Task 3: The icon area, the nearest free spot, Tidy and arrival (Core)

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/DrawerIconArea.cs`, `windows-dotnet/src/Sanduhr.Core/DrawerTidyPlan.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/DrawerPlacement.cs` (`Arrive` uses Tidy's slots)
- Test: `windows-dotnet/tests/Sanduhr.Tests/DrawerIconAreaTests.cs`, `DrawerTidyPlanTests.cs`; keep `DrawerPlacementTests` green

**Interfaces:**
- Consumes: `DrawerRect`, `DrawerSnap.Keep` (Task 2); `DrawerTidy`, `DrawerCorner`, `DrawerFlow` (Task 1).
- Produces:
  - `public static class DrawerIconArea`: `static IReadOnlyList<DrawerRect> From(IReadOnlyList<DrawerRect> icons, double margin)` (each icon inflated by `margin`), `static IReadOnlyList<DrawerRect> Fallback(DrawerRect work, int itemCount, double cellW, double cellH)` (the left columns Windows fills by default), `static DrawerRect NearestFree(DrawerRect r, DrawerRect work, IReadOnlyList<DrawerRect> blocked)` (r itself when free; else the nearest candidate that is on the work area and overlaps nothing; else r kept on screen)
  - `public static class DrawerTidyPlan`: `static IReadOnlyList<string> Order(IReadOnlyDictionary<string, DrawerRect> current, DrawerRect work, DrawerCorner corner)` (nearest the corner first, ties by id), `static IReadOnlyDictionary<string, DrawerRect> Slots(IReadOnlyList<(string Id, double H)> ordered, DrawerRect work, DrawerTidy tidy, IReadOnlyList<DrawerRect> blocked)` (a drawer that cannot fit is left out of the result)
  - `DrawerPlacement.Arrive` gains optional `DrawerTidy? tidy = null, double workHeight = double.PositiveInfinity, IReadOnlyList<DrawerRect>? blocked = null`; with defaults it behaves exactly as today.

- [ ] **Step 1: Write the failing tests**

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerIconAreaTests
{
    private static readonly DrawerRect Work = new(0, 0, 1920, 1032);

    [Fact]
    public void Icons_become_blocked_areas_with_a_margin()
    {
        var a = DrawerIconArea.From([new DrawerRect(10, 10, 76, 86)], 8);
        Assert.Equal(new DrawerRect(2, 2, 92, 102), Assert.Single(a));
    }

    [Fact]
    public void The_fallback_is_the_left_columns_the_items_need()
    {
        var a = Assert.Single(DrawerIconArea.Fallback(Work, itemCount: 25, cellW: 76, cellH: 100));
        Assert.Equal(0, a.X);
        Assert.Equal(3 * 76, a.W);   // 10 rows fit in 1032, so 25 items need 3 columns
    }

    [Fact]
    public void No_items_means_no_fallback_area()
    {
        Assert.Empty(DrawerIconArea.Fallback(Work, 0, 76, 100));
    }

    [Fact]
    public void A_drop_on_the_icons_slides_to_the_nearest_free_spot_on_screen()
    {
        var icons = new DrawerRect(0, 0, 300, 1032);
        var r = new DrawerRect(100, 200, DrawerPlacement.Width, DrawerPlacement.OpenHeight);
        var free = DrawerIconArea.NearestFree(r, Work, [icons]);
        Assert.False(free.Overlaps(icons));
        Assert.Equal(icons.Right + DrawerPlacement.Gap, free.X);
        Assert.Equal(200, free.Y);
    }

    [Fact]
    public void A_free_drop_stays_where_it_is()
    {
        var r = new DrawerRect(800, 200, DrawerPlacement.Width, DrawerPlacement.OpenHeight);
        Assert.Equal(r, DrawerIconArea.NearestFree(r, Work, [new DrawerRect(0, 0, 300, 1032)]));
    }
}

public class DrawerTidyPlanTests
{
    private static readonly DrawerRect Work = new(0, 0, 1920, 1032);
    private const double W = DrawerPlacement.Width, H = DrawerPlacement.OpenHeight, F = DrawerPlacement.FoldedHeight,
        M = DrawerPlacement.Margin, G = DrawerPlacement.Gap;

    [Fact]
    public void Top_right_column_stacks_down_from_the_corner()
    {
        var s = DrawerTidyPlan.Slots([("a", H), ("b", F)], Work, DrawerTidy.Default, []);
        Assert.Equal(new DrawerRect(1920 - M - W, M, W, H), s["a"]);
        Assert.Equal(new DrawerRect(1920 - M - W, M + H + G, W, F), s["b"]);
    }

    [Theory]
    [InlineData(DrawerCorner.TopLeft, DrawerFlow.Row)]
    [InlineData(DrawerCorner.BottomLeft, DrawerFlow.Column)]
    [InlineData(DrawerCorner.BottomRight, DrawerFlow.Row)]
    public void Every_corner_starts_at_its_margin(DrawerCorner corner, DrawerFlow flow)
    {
        var first = DrawerTidyPlan.Slots([("a", H)], Work, new DrawerTidy(corner, flow), [])["a"];
        bool left = corner is DrawerCorner.TopLeft or DrawerCorner.BottomLeft;
        bool top = corner is DrawerCorner.TopLeft or DrawerCorner.TopRight;
        Assert.Equal(left ? M : 1920 - M - W, first.X);
        Assert.Equal(top ? M : 1032 - M - H, first.Y);
    }

    [Fact]
    public void A_full_column_wraps_toward_the_centre()
    {
        var s = DrawerTidyPlan.Slots([("a", H), ("b", H), ("c", H)], Work, DrawerTidy.Default, []);
        Assert.Equal(1920 - M - W - W - G, s["c"].X);   // two open drawers fill 1032; the third starts a new column to the left
        Assert.Equal(M, s["c"].Y);
    }

    [Fact]
    public void Slots_skip_the_icon_area()
    {
        var icons = new DrawerRect(1920 - 400, 0, 400, 500);
        var s = DrawerTidyPlan.Slots([("a", H)], Work, DrawerTidy.Default, [icons]);
        Assert.False(s["a"].Overlaps(icons));
    }

    [Fact]
    public void A_drawer_that_cannot_fit_anywhere_is_left_out()
    {
        var tiny = new DrawerRect(0, 0, W + 2 * M, H + 2 * M);
        var s = DrawerTidyPlan.Slots([("a", H), ("b", H)], tiny, DrawerTidy.Default, []);
        Assert.True(s.ContainsKey("a"));
        Assert.False(s.ContainsKey("b"));
    }

    [Fact]
    public void Order_puts_the_drawer_nearest_the_corner_first()
    {
        var current = new Dictionary<string, DrawerRect>
        {
            ["far"] = new(100, 600, W, H),
            ["near"] = new(1500, 30, W, H),
        };
        Assert.Equal(["near", "far"], DrawerTidyPlan.Order(current, Work, DrawerCorner.TopRight));
    }
}
```

Add to `DrawerPlacementTests`:

```csharp
    [Fact]
    public void Arrival_with_a_tidy_and_icons_takes_the_next_free_slot()
    {
        var drawers = new[] { new DrawerDef("a", "A", @"C:\D\A", true) };
        var icons = new DrawerRect(1920 - 400, 0, 400, 500);
        var places = DrawerPlacement.Arrive(drawers, new Dictionary<string, DrawerPlace>(), 1920, [],
            DrawerTidy.Default, 1032, [icons]);
        var p = places["a"];
        Assert.False(new DrawerRect(p.X, p.Y, DrawerPlacement.Width, DrawerPlacement.OpenHeight).Overlaps(icons));
    }
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "DrawerIconAreaTests|DrawerTidyPlanTests|DrawerPlacementTests"`
Expected: FAIL to build.

- [ ] **Step 3: Write `DrawerIconArea.cs`**

```csharp
namespace Sanduhr.Core;

/// <summary>Where the desktop icons sit, as areas drawers keep clear of (look and placement spec, section 2).</summary>
public static class DrawerIconArea
{
    public static IReadOnlyList<DrawerRect> From(IReadOnlyList<DrawerRect> icons, double margin) =>
        [.. icons.Select(i => i.Inflate(margin))];

    /// <summary>When the icons can't be read: the left-hand columns Windows fills by default for <paramref name="itemCount"/> items.</summary>
    public static IReadOnlyList<DrawerRect> Fallback(DrawerRect work, int itemCount, double cellW, double cellH)
    {
        if (itemCount <= 0 || cellW <= 0 || cellH <= 0) return [];
        int rows = Math.Max(1, (int)Math.Floor(work.H / cellH));
        int cols = (itemCount + rows - 1) / rows;
        return [new DrawerRect(work.X, work.Y, Math.Min(work.W, cols * cellW), work.H)];
    }

    /// <summary><paramref name="r"/> when it overlaps nothing blocked; else the nearest spot beside a blocked area that stays on the
    /// work area and overlaps nothing; else <paramref name="r"/> kept on screen.</summary>
    public static DrawerRect NearestFree(DrawerRect r, DrawerRect work, IReadOnlyList<DrawerRect> blocked)
    {
        if (!blocked.Any(r.Overlaps)) return DrawerSnap.Keep(r, work);
        var candidates = new List<DrawerRect>();
        foreach (var b in blocked)
        {
            candidates.Add(r.At(b.Right + DrawerPlacement.Gap, r.Y));
            candidates.Add(r.At(b.X - DrawerPlacement.Gap - r.W, r.Y));
            candidates.Add(r.At(r.X, b.Bottom + DrawerPlacement.Gap));
            candidates.Add(r.At(r.X, b.Y - DrawerPlacement.Gap - r.H));
        }
        var free = candidates
            .Where(c => c.X >= work.X && c.Right <= work.Right && c.Y >= work.Y && c.Bottom <= work.Bottom && !blocked.Any(c.Overlaps))
            .OrderBy(c => Math.Abs(c.X - r.X) + Math.Abs(c.Y - r.Y))
            .ToList();
        return free.Count > 0 ? free[0] : DrawerSnap.Keep(r, work);
    }
}
```

- [ ] **Step 4: Write `DrawerTidyPlan.cs`**

```csharp
namespace Sanduhr.Core;

/// <summary>Tidy drawers (look and placement spec, section 2): slots from a corner, a column or a row at a time, wrapping toward
/// the centre and stepping past the icon area. A drawer that fits nowhere is left out and keeps its place.</summary>
public static class DrawerTidyPlan
{
    public static IReadOnlyList<string> Order(IReadOnlyDictionary<string, DrawerRect> current, DrawerRect work, DrawerCorner corner)
    {
        var (cx, cy) = Point(work, corner);
        return [.. current.OrderBy(p => Math.Abs(Near(p.Value, corner).X - cx) + Math.Abs(Near(p.Value, corner).Y - cy))
            .ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key)];
    }

    public static IReadOnlyDictionary<string, DrawerRect> Slots(IReadOnlyList<(string Id, double H)> ordered, DrawerRect work,
        DrawerTidy tidy, IReadOnlyList<DrawerRect> blocked)
    {
        bool left = tidy.Corner is DrawerCorner.TopLeft or DrawerCorner.BottomLeft;
        bool top = tidy.Corner is DrawerCorner.TopLeft or DrawerCorner.TopRight;
        double m = DrawerPlacement.Margin, g = DrawerPlacement.Gap, w = DrawerPlacement.Width;
        double lineX = left ? work.X + m : work.Right - m - w;   // the current column (or, in a row, the next x)
        double lineY = top ? work.Y + m : work.Bottom - m;       // the current row's edge (or, in a column, the next y edge)
        double rowStart = left ? work.X + m : work.Right - m;    // a row's first edge
        double cursor = tidy.Flow == DrawerFlow.Column ? lineY : rowStart;
        double lane = 0;                                         // the tallest drawer in the current row
        var placed = new List<DrawerRect>(blocked);
        var slots = new Dictionary<string, DrawerRect>(StringComparer.Ordinal);

        foreach (var (id, h) in ordered)
        {
            for (int guard = 0; guard < 400; guard++)
            {
                DrawerRect r = tidy.Flow == DrawerFlow.Column
                    ? new DrawerRect(lineX, top ? cursor : cursor - h, w, h)
                    : new DrawerRect(left ? cursor : cursor - w, top ? lineY : lineY - h, w, h);
                bool inside = r.X >= work.X + m - 0.5 && r.Right <= work.Right - m + 0.5 && r.Y >= work.Y + m - 0.5 && r.Bottom <= work.Bottom - m + 0.5;
                if (!inside)
                {
                    // the line is full: wrap toward the centre
                    if (tidy.Flow == DrawerFlow.Column)
                    {
                        lineX += left ? w + g : -(w + g);
                        cursor = lineY;
                        if (lineX < work.X + m - 0.5 || lineX + w > work.Right - m + 0.5) break;
                    }
                    else
                    {
                        lineY += top ? lane + g : -(lane + g);
                        cursor = rowStart; lane = 0;
                        if (lineY < work.Y + m - 0.5 || lineY > work.Bottom - m + 0.5) break;
                    }
                    continue;
                }
                if (placed.FirstOrDefault(r.Overlaps) is { W: > 0 } hit)
                {
                    // step past whatever is in the way along the line
                    cursor = tidy.Flow == DrawerFlow.Column
                        ? (top ? hit.Bottom + g : hit.Y - g)
                        : (left ? hit.Right + g : hit.X - g);
                    continue;
                }
                slots[id] = r;
                placed.Add(r);
                if (tidy.Flow == DrawerFlow.Column) cursor = top ? r.Bottom + g : r.Y - g;
                else { cursor = left ? r.Right + g : r.X - g; lane = Math.Max(lane, h); }
                break;
            }
        }
        return slots;
    }

    private static (double X, double Y) Point(DrawerRect work, DrawerCorner c) => c switch
    {
        DrawerCorner.TopLeft => (work.X, work.Y),
        DrawerCorner.TopRight => (work.Right, work.Y),
        DrawerCorner.BottomLeft => (work.X, work.Bottom),
        _ => (work.Right, work.Bottom),
    };

    private static (double X, double Y) Near(DrawerRect r, DrawerCorner c) => c switch
    {
        DrawerCorner.TopLeft => (r.X, r.Y),
        DrawerCorner.TopRight => (r.Right, r.Y),
        DrawerCorner.BottomLeft => (r.X, r.Bottom),
        _ => (r.Right, r.Bottom),
    };
}
```

The `placed.FirstOrDefault(r.Overlaps) is { W: > 0 } hit` check relies on `default(DrawerRect)` having `W == 0`; keep that pattern or use an explicit `int idx = placed.FindIndex(r.Overlaps)`.

- [ ] **Step 5: `Arrive` through Tidy**

Change `DrawerPlacement.Arrive` to:

```csharp
    public static IReadOnlyDictionary<string, DrawerPlace> Arrive(IReadOnlyList<DrawerDef> drawers, IReadOnlyDictionary<string, DrawerPlace> placed,
        double workWidth, IReadOnlyCollection<string> folded, DrawerTidy? tidy = null, double workHeight = double.PositiveInfinity,
        IReadOnlyList<DrawerRect>? blocked = null)
    {
        if (tidy is null || tidy == DrawerTidy.Default && blocked is null or { Count: 0 } && double.IsPositiveInfinity(workHeight))
            return ArriveColumn(drawers, placed, workWidth, folded);   // today's rule, unchanged
        var work = new DrawerRect(0, 0, workWidth, double.IsPositiveInfinity(workHeight) ? 100_000 : workHeight);
        var held = placed.Where(p => p.Value.Monitor == DeskMonitors.Main)
            .Select(p => new DrawerRect(p.Value.X, p.Value.Y, Width, Height(folded.Contains(p.Key))));
        var fresh = drawers.Where(d => !placed.ContainsKey(d.Id)).Select(d => (d.Id, Height(folded.Contains(d.Id)))).ToList();
        var slots = DrawerTidyPlan.Slots(fresh, work, tidy, [.. blocked ?? [], .. held]);
        var places = new Dictionary<string, DrawerPlace>(placed, StringComparer.Ordinal);
        foreach (var (id, _) in fresh)
            places[id] = slots.TryGetValue(id, out var r) ? new DrawerPlace(DeskMonitors.Main, r.X, r.Y)
                : new DrawerPlace(DeskMonitors.Main, Math.Max(0, workWidth - Margin - Width), Margin);
        return places;
    }
```

and rename today's body to `private static IReadOnlyDictionary<string, DrawerPlace> ArriveColumn(...)` unchanged, so every existing `DrawerPlacementTests` case still passes.

- [ ] **Step 6: Run the tests**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "DrawerIconAreaTests|DrawerTidyPlanTests|DrawerPlacementTests|DrawerSnapTests"`
Expected: PASS. If a Tidy case fails, fix `Slots`, not the test: the tests are the spec.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/DrawerIconArea.cs windows-dotnet/src/Sanduhr.Core/DrawerTidyPlan.cs windows-dotnet/src/Sanduhr.Core/DrawerPlacement.cs windows-dotnet/tests/Sanduhr.Tests/DrawerIconAreaTests.cs windows-dotnet/tests/Sanduhr.Tests/DrawerTidyPlanTests.cs windows-dotnet/tests/Sanduhr.Tests/DrawerPlacementTests.cs
git commit -m "feat(drawers): the icon area and its fallback, the nearest free spot, Tidy's slots, and arrival through them"
```

---

### Task 4: The four looks on the Desk

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerLook.cs`, `DrawerPiece.xaml`, `DrawerPiece.xaml.cs`, `windows-dotnet/src/Sanduhr.App/Desk/DeskController.cs` (`WithDrawers` ~841-860)
- Create (only if Task 0's blur verdict is PASS): `windows-dotnet/src/Sanduhr.App/Interop/DrawerBackdrop.cs`
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DrawerLookTests.cs`

**Interfaces:**
- Consumes: `DrawerSettings.LookFor`, `Opacity`, `DrawerStyle.HasFill` (Task 1); `DeskDesign.MinContrast`, `DeskDesign.Ratio`, `InkHex.Luminance`.
- Produces: `DrawerLook` gains `DrawerLookKind Kind`, `bool Chips` (labels on chips) and `static DrawerLook From(ResolvedInk ink, string? plateHex, string? accentHex, FontFamily font, DrawerLookKind kind, int opacity, double wallpaperLuminance)`; the old `From` overload stays and maps to `(Solid, 55)`.

Rules:
- **Solid:** plate rgb as today, alpha = `opacity / 100`.
- **Glass:** plate rgb blended 35% toward white (dark ink) or black (light ink), alpha = `opacity / 100 * 0.8`, plus a 1 px top/left edge highlight of the ink at 25%. With Task 0's blur PASS, `DrawerBackdrop.Apply(hwnd)` is called for a drawer's host window when its look is Glass.
- **Outline:** plate alpha = 0.01 (clicks still land), border 1 px ink at 70%, title gets the ink shadow.
- **Bare:** plate alpha = 0.01, border 0 until `IsMouseOver`, then 1 px ink at 35%.
- **Chips:** for Outline and Bare, or any fill below 0.55, when `DeskDesign.Ratio(ink luminance, wallpaperLuminance) < DeskDesign.MinContrast`, labels draw on a chip (`Drawer.Chip` = the plate rgb at 0.7, corner radius 4).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Windows.Media;
using Sanduhr.App.Desk;
using Sanduhr.App.Desk.Drawers;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

public sealed class DrawerLookTests
{
    private static readonly FontFamily Font = new("Segoe UI");
    private static DrawerLook L(DrawerLookKind k, int opacity, double wall) =>
        DrawerLook.From(DeskInkMap.Light.Default, null, null, Font, k, opacity, wall);

    [Fact]
    public void Solid_uses_the_opacity_setting()
    {
        Assert.Equal((byte)Math.Round(0.7 * 255), L(DrawerLookKind.Solid, 70, 0.1).Plate.A);
    }

    [Fact]
    public void Outline_and_bare_keep_a_fill_that_still_takes_clicks()
    {
        foreach (var k in new[] { DrawerLookKind.Outline, DrawerLookKind.Bare })
        {
            var look = L(k, 70, 0.1);
            Assert.InRange(look.Plate.A, (byte)1, (byte)5);
        }
    }

    [Fact]
    public void Labels_go_on_chips_when_the_wallpaper_is_too_close_to_the_ink()
    {
        // light ink on a light wallpaper: contrast fails, so chips
        Assert.True(L(DrawerLookKind.Bare, 55, 0.9).Chips);
        Assert.False(L(DrawerLookKind.Bare, 55, 0.02).Chips);
        Assert.False(L(DrawerLookKind.Solid, 80, 0.9).Chips);   // a plate at 80% carries its own contrast
    }

    [Fact]
    public void Glass_is_lighter_than_solid_at_the_same_setting()
    {
        Assert.True(L(DrawerLookKind.Glass, 80, 0.1).Plate.A < L(DrawerLookKind.Solid, 80, 0.1).Plate.A);
    }

    [Fact]
    public void Every_look_builds_a_piece_with_its_group_name() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var s = box.Service();
        foreach (var k in Enum.GetValues<DrawerLookKind>())
        {
            var piece = new DrawerPiece(s.Get("images")!, s, L(k, 55, 0.1), 1);
            piece.Measure(new System.Windows.Size(400, 400));
            Assert.Contains("drawer", System.Windows.Automation.AutomationProperties.GetName(piece), StringComparison.OrdinalIgnoreCase);
        }
    });
}
```

Check `DrawerSandbox`'s real API (`tests/Sanduhr.App.Tests/DrawerSandbox.cs`) and `DeskInkMap.Light.Default`'s name before compiling; build the service the way `DrawerPieceTests.cs:27` does.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.App.Tests/Sanduhr.App.Tests.csproj -c Release --filter DrawerLookTests`
Expected: FAIL to build.

- [ ] **Step 3: Extend `DrawerLook`**

```csharp
internal sealed record DrawerLook(Color Plate, Color Ink, Color Accent, Effect? Shadow, FontFamily Font)
{
    public const double MinPlateOpacity = 0.55;
    /// <summary>Outline's and Bare's fill: invisible, but a per-pixel layered window still hands the click to the drawer.</summary>
    public const double HitFill = 0.01;

    public DrawerLookKind Kind { get; init; } = DrawerLookKind.Solid;
    /// <summary>Labels draw on chips: the ink would not read against the wallpaper without a plate behind it.</summary>
    public bool Chips { get; init; }
    public Color Border { get; init; } = Colors.Transparent;
    public Color HoverBorder { get; init; } = Colors.Transparent;
    public Color Edge { get; init; } = Colors.Transparent;

    public static DrawerLook From(ResolvedInk ink, string? plateHex, double? plateOpacity, string? accentHex, FontFamily font) =>
        From(ink, plateHex, accentHex, font, DrawerLookKind.Solid,
            (int)Math.Round(Math.Max(plateOpacity ?? DeskPlate.Opacity, MinPlateOpacity) * 100), 0);

    public static DrawerLook From(ResolvedInk ink, string? plateHex, string? accentHex, FontFamily font,
        DrawerLookKind kind, int opacity, double wallpaperLuminance)
    {
        var rgb = plateHex is { } h ? DeskView.Rgb(h) : ink.DarkInk ? Colors.White : Colors.Black;
        var inkColor = DeskView.Rgb(ink.Stops[0]);
        var accent = accentHex is { } x ? DeskView.Rgb(x) : DeskView.Rgb(ink.Stops[^1]);
        Effect? shadow = null;
        if (ink.ShadowOpacity > 0) { var s = DeskView.Shadow(ink); s.Freeze(); shadow = s; }

        double fill = kind switch
        {
            DrawerLookKind.Solid => opacity / 100.0,
            DrawerLookKind.Glass => opacity / 100.0 * 0.8,
            _ => HitFill,
        };
        if (kind == DrawerLookKind.Glass) rgb = Blend(rgb, ink.DarkInk ? Colors.White : Colors.Black, 0.35);
        bool chips = fill < MinPlateOpacity
            && DeskDesign.Ratio(InkHex.Luminance(ink.Stops[0]), wallpaperLuminance) < DeskDesign.MinContrast;

        return new DrawerLook(Color.FromArgb((byte)Math.Round(fill * 255), rgb.R, rgb.G, rgb.B), inkColor, accent, shadow, font)
        {
            Kind = kind,
            Chips = chips,
            Border = kind == DrawerLookKind.Outline ? WithAlpha(inkColor, 0.7) : Colors.Transparent,
            HoverBorder = kind switch { DrawerLookKind.Outline => WithAlpha(inkColor, 0.7), DrawerLookKind.Bare => WithAlpha(inkColor, 0.35), _ => Colors.Transparent },
            Edge = kind == DrawerLookKind.Glass ? WithAlpha(inkColor, 0.25) : Colors.Transparent,
        };
    }

    private static Color WithAlpha(Color c, double a) => Color.FromArgb((byte)Math.Round(a * 255), c.R, c.G, c.B);
    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
}
```

Check that `InkHex.Luminance` takes the hex string `ink.Stops[0]` is (the stops are hex strings, per `DeskView.Rgb(ink.Stops[0])`), and `DeskDesign.Ratio(double, double)`'s argument order.

- [ ] **Step 4: Draw it in `DrawerPiece`**

In the constructor, after the existing `Resources[...]` lines:

```csharp
        Resources["Drawer.Border"] = Brush(look.Border);
        Resources["Drawer.HoverBorder"] = Brush(look.HoverBorder);
        Resources["Drawer.Edge"] = Brush(look.Edge);
        Resources["Drawer.Chip"] = look.Chips ? Brush(Color.FromArgb(178, look.Plate.R, look.Plate.G, look.Plate.B)) : Brushes.Transparent;
```

In `DrawerPiece.xaml`:
- `Frame`: add `BorderThickness="1" BorderBrush="{DynamicResource Drawer.Border}"`, and a trigger (or code in `IsMouseOver` changed) that swaps to `Drawer.HoverBorder` on hover.
- Add, as the Frame's first child layer, a `Border` for Glass's edge: `BorderThickness="1,1,0,0" BorderBrush="{DynamicResource Drawer.Edge}" CornerRadius="10" IsHitTestVisible="False"` (wrap the DockPanel in a Grid to layer it).
- The tile's `Label`: wrap in a `Border Background="{DynamicResource Drawer.Chip}" CornerRadius="4" Padding="3,1"`.
- `TitleText`: put the same chip `Border` around it.
- For Bare, `TitleText.FontSize` 11 and `TitleBar.Height` 22: set in code from `look.Kind` after `InitializeComponent()`.

- [ ] **Step 5: Feed the look from settings in `DeskController.WithDrawers`**

Replace the `DrawerLook.From(ink, frame.PlateHex, frame.PlateOpacity, frame.AccentHex, ...)` call with:

```csharp
var settings = _drawers.Settings;
var kind = settings.LookFor(state.Def.Id);
double wall = Luminance(screen, rectPx);   // the same rect DrawerInk samples (DeskController.cs ~864); reuse its value
DrawerLook.From(ink, frame.PlateHex, frame.AccentHex, _fonts.Families(looks[i].Settings.Font).Regular, kind, settings.Opacity, wall)
```

Read `WithDrawers` and `DrawerInk` first: `DrawerInk` already samples the wallpaper under the drawer's rect to pick its ink; take the luminance from the same sample rather than sampling twice.

With Task 0's blur PASS: in `DesktopLayerHost`, after a drawer island's window is placed (`IsDrawer(src)`), call `DrawerBackdrop.Apply(src.Handle, island.Glass)`, where `DeskIsland` gains `public bool Glass { get; init; }` set from `kind == DrawerLookKind.Glass` in `WithDrawers`. `DrawerBackdrop` copies the spike's verified call. With FAIL, skip this paragraph and note it in the commit message.

- [ ] **Step 6: Run the tests**

Run: `dotnet test windows-dotnet/tests/Sanduhr.App.Tests/Sanduhr.App.Tests.csproj -c Release --filter "DrawerLookTests|FullyQualifiedName~DrawerPiece|FullyQualifiedName~DrawerFreePanel|FullyQualifiedName~DeskController"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App windows-dotnet/tests/Sanduhr.App.Tests/DrawerLookTests.cs
git commit -m "feat(drawers): Solid, Glass, Outline and Bare, with readable chips and clicks that still land"
```

---

### Task 5: Snapping while dragging, and keeping clear of the icons

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/Interop/DesktopIcons.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerService.cs` (snap callback), `DrawerPiece.xaml.cs` (`TitleMove`, `Place`), `windows-dotnet/src/Sanduhr.App/Desk/DeskController.cs` (`OnPlaceRequested` ~812, `DrawerPlacesNow` ~826, `RenderOnce` ~935-961, `OnScreensChanged` ~630), `windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerScreens.cs`
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DrawerPlacementLiveTests.cs`

**Interfaces:**
- Consumes: `DrawerSnap`, `DrawerIconArea`, `DrawerRect` (Tasks 2-3); `DrawerSettings.Snap`, `KeepClear`, `Tidy` (Task 1); the spike's verified COM declarations (Task 0).
- Produces:
  - `internal static class DesktopIcons { static IReadOnlyList<PxRect>? Read(); static (int Count, PxPoint Cell)? Spacing(); }` (screen pixels; null on any failure; STA only)
  - `DrawerService.Snapper` : `Func<string, PxPoint, PxPoint>?` (id, proposed top-left px → snapped top-left px)
  - `DeskController.IconArea(DeskScreen)` : `IReadOnlyList<DrawerRect>` in that screen's work DIPs (read, else fallback, cached until `OnScreensChanged`)
  - `DeskController` keeps `_pieceRects` : `Dictionary<string, IReadOnlyList<DrawerRect>>` by screen device, captured from `DeskView.StackRects()` in `RenderOnce` before `TakeIslands()`

- [ ] **Step 1: Write the failing tests**

```csharp
using Sanduhr.App.Desk;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

public sealed class DrawerPlacementLiveTests
{
    [Fact]
    public void A_drop_snaps_then_keeps_clear_of_the_icons() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (controller, service) = box.Desk();   // DeskController over a fake host with one 1920x1080 screen, scale 1
        controller.IconAreaOverride = [new DrawerRect(0, 0, 300, 1032)];
        service.RequestPlace("images", new PxPoint(110, 200));
        var p = service.Settings.Places["images"];
        Assert.True(p.X >= 300);   // slid off the icons
    });

    [Fact]
    public void With_snap_off_a_drop_lands_where_it_was_dropped() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (controller, service) = box.Desk();
        service.Save(service.Settings with { Snap = false, KeepClear = false });
        service.RequestPlace("images", new PxPoint(803, 501));
        var p = service.Settings.Places["images"];
        Assert.Equal(803, p.X);
        Assert.Equal(501 - 0, p.Y);   // work area starts at 0 on the fake screen
    });

    [Fact]
    public void The_live_snapper_answers_while_dragging() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (_, service) = box.Desk();
        Assert.NotNull(service.Snapper);
        var snapped = service.Snapper!("images", new PxPoint(1920 - 360 - 30, 30));
        Assert.Equal(1920 - 24 - 360, snapped.X);
    });

    [Fact]
    public void A_failed_icon_read_falls_back_and_never_throws() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (controller, _) = box.Desk();
        controller.IconReader = () => throw new System.Runtime.InteropServices.COMException("explorer restarting");
        var area = controller.IconArea(controller.ScreensForTests[0]);
        Assert.NotNull(area);   // fallback (or empty when the desktop has no items), never an exception
    });
}
```

`DrawerSandbox.Desk()`, `IconAreaOverride`, `IconReader` and `ScreensForTests` are test seams this task adds: `DrawerSandbox.Desk()` builds a `DeskController` over the fake `IDeskHost` the existing `DrawerFreePanelTests` use (read that file and reuse its fake host; do not invent a second one). `IconReader` is `internal Func<IReadOnlyList<PxRect>?> IconReader { get; set; } = DesktopIcons.Read;` and `IconAreaOverride` an `internal IReadOnlyList<DrawerRect>? IconAreaOverride { get; set; }` used before reading.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.App.Tests/Sanduhr.App.Tests.csproj -c Release --filter DrawerPlacementLiveTests`
Expected: FAIL to build.

- [ ] **Step 3: `DesktopIcons.cs`**

Copy the spike's verified `[ComImport]` declarations (IShellWindows, IServiceProvider, IShellBrowser, IShellView, IFolderView, and the ShellWindows coclass) into a nested `Native` class, in the hand-written style of `Interop/ShellNative.cs`. `Read()`:

```csharp
    /// <summary>The desktop icons' rectangles in screen pixels, read-only through the Shell's desktop folder view; null when the
    /// Shell does not answer (Explorer restarting, icons hidden, not on an STA thread). Never moves an icon.</summary>
    public static IReadOnlyList<PxRect>? Read()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) return null;
        object? windows = null, view = null;
        try
        {
            // 1. ShellWindows → FindWindowSW(SWC_DESKTOP, SWFO_NEEDDISPATCH) → IServiceProvider
            // 2. QueryService(SID_STopLevelBrowser) → IShellBrowser.QueryActiveShellView → IFolderView
            // 3. GetSpacing → the cell; ItemCount(SVGIO_ALLVIEW); Item(i) → pidl; GetItemPosition(pidl) → a point in the
            //    coordinate space the spike recorded; convert to screen px (MapWindowPoints from the DefView when it is client space)
            // 4. Each icon = (point, cell)
            ...the spike's verified sequence, unchanged...
        }
        catch (Exception e) when (e is COMException or InvalidCastException or ArgumentException or UnauthorizedAccessException) { return null; }
        finally
        {
            if (view is not null) Marshal.FinalReleaseComObject(view);
            if (windows is not null) Marshal.FinalReleaseComObject(windows);
        }
    }
```

The numbered comments are the sequence; the body is the spike's code (Task 0 Step 2 kept it). Free each pidl with `Marshal.FreeCoTaskMem`. `Spacing()` returns the item count and cell for the fallback.

- [ ] **Step 4: The controller answers placement**

In `DeskController`:

```csharp
    internal Func<IReadOnlyList<PxRect>?> IconReader { get; set; } = DesktopIcons.Read;
    internal IReadOnlyList<DrawerRect>? IconAreaOverride { get; set; }
    private readonly Dictionary<string, IReadOnlyList<DrawerRect>> _iconArea = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<DrawerRect>> _pieceRects = new(StringComparer.Ordinal);

    /// <summary>The icon area on <paramref name="screen"/>, in its work DIPs: read through the Shell, else the default columns.</summary>
    internal IReadOnlyList<DrawerRect> IconArea(DeskScreen screen)
    {
        if (IconAreaOverride is { } fixedArea) return fixedArea;
        if (_iconArea.TryGetValue(screen.Device, out var cached)) return cached;
        IReadOnlyList<DrawerRect> area;
        try
        {
            var rects = IconReader();
            area = rects is null ? Fallback(screen) : DrawerIconArea.From(
                [.. rects.Where(r => screen.Work.Contains(r)).Select(r => ToWorkDips(r, screen))], DrawerPlacement.Gap);
        }
        catch (Exception e) when (e is COMException or InvalidCastException) { area = Fallback(screen); }
        _iconArea[screen.Device] = area;
        return area;
    }
```

`Fallback(screen)` uses `DesktopIcons.Spacing()` (or, when that is null too, the Desktop's item count from `DrawerService` listings and `SystemParameters.IconHorizontalSpacing`/`IconVerticalSpacing`) with `DrawerIconArea.Fallback`, on the primary screen only (Windows fills icons there); other screens get `[]`. `ToWorkDips` converts a screen-pixel rect to the screen's work DIPs as `DrawerScreens.ToPlace` does (`(x - Work.X) / Scale`). Add `PxRect.Contains(PxRect)` if `PxRect` lacks one (in Core `GlowPlacement.cs`), with a test.

Clear `_iconArea` in `OnScreensChanged` (it already runs on Explorer's restart, `TaskbarCreated`) and at the start of `Reload()`.

In `RenderOnce`, before `view.TakeIslands()`, capture `_pieceRects[screen.Device] = view.StackRects().Values.Select(r => ToWorkDipsFromScreenDips(r, screen)).ToList()` (StackRects are screen DIPs from the bounds; subtract the work area's offset in DIPs). With desktop off (`!DrawsDesktop`), store `[]`.

`OnPlaceRequested(id, topLeft)` becomes:

```csharp
        if (!_drawers.Settings.Drawers.Any(d => d.Id == id) || DrawerScreens.ToPlace(topLeft, _screens) is not { } place) { Render(); return; }
        var screen = DrawerScreens.Find(place.Monitor, _screens)!;
        var work = DrawerScreens.WorkDips(screen);
        var workRect = new DrawerRect(0, 0, work.Width, work.Height);
        var r = new DrawerRect(place.X, place.Y, DrawerPlacement.Width, DrawerPlacement.Height(_drawers.Get(id)?.Folded == true));
        var s = _drawers.Settings;
        if (s.Snap) r = DrawerSnap.Snap(r, workRect, OtherDrawers(id, place.Monitor), PieceRects(screen));
        if (s.KeepClear) r = DrawerIconArea.NearestFree(r, workRect, IconArea(screen));
        _drawers.SetPlace(id, place with { X = r.X, Y = r.Y });
```

`OtherDrawers(id, monitor)` = every other drawer's place on that monitor from `DrawerPlacesNow()`, as `DrawerRect` with its folded height. Alt is handled by the piece (Step 5): the piece passes a flag; extend `RequestPlace(string id, PxPoint topLeft, bool snap = true)` and `PlaceRequested` to carry it, and skip `DrawerSnap.Snap` (not keep-clear) when false.

`DrawerPlacesNow()` passes `s.Tidy`, the primary work height, and `s.KeepClear ? IconArea(primary) : []` to `DrawerPlacement.Arrive`.

Set `_drawers.Snapper = (id, px) => SnapLive(id, px)` in the constructor (cleared in `Dispose`): it converts `px` to a place, applies `DrawerSnap.Snap` only (no keep-clear while moving), and converts back to screen px.

- [ ] **Step 5: The piece snaps while moving**

In `DrawerPiece.TitleMove`, before `DeskNative.MoveTo(h, WindowAt(t, cursor))`:

```csharp
        var w = WindowAt(t, cursor);
        bool snap = (Keyboard.Modifiers & ModifierKeys.Alt) == 0 && _service.Settings.Snap;
        if (snap && _service.Snapper is { } snapper)
        {
            var top = snapper(_state.Def.Id, new PxPoint(w.X + t.Offset.X, w.Y + t.Offset.Y));
            w = new PxPoint(top.X - t.Offset.X, top.Y - t.Offset.Y);
        }
        DeskNative.MoveTo(h, w);
```

and in `Place`, pass `snap` to `_service.RequestPlace(id, topLeft, snap)`.

- [ ] **Step 6: Run the tests**

Run: `dotnet test windows-dotnet/tests/Sanduhr.App.Tests/Sanduhr.App.Tests.csproj -c Release --filter "DrawerPlacementLiveTests|FullyQualifiedName~DrawerFreePanel|FullyQualifiedName~DrawerInteraction|FullyQualifiedName~DeskController"` and `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "Drawer|GlowPlacement"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src windows-dotnet/tests
git commit -m "feat(drawers): snap while dragging (Alt to override), keep clear of the desktop icons, and arrive through Tidy's slots"
```

---

### Task 6: Tidy drawers, with Undo

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerReviewService.cs` (a public result bar), `DrawerService.cs` (`Tidy`), `windows-dotnet/src/Sanduhr.App/Desk/DeskController.cs` (`TidyPlaces`), `windows-dotnet/src/Sanduhr.Core/DrawerReview.cs` (copy)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DrawerTidyTests.cs`

**Interfaces:**
- Consumes: `DrawerTidyPlan.Order`, `Slots` (Task 3); `IconArea`, `PieceRects` (Task 5); `DrawerReview.Result`, `IDrawerReviewHost.Bar`.
- Produces:
  - `DrawerReviewService.ShowResult(string line, Action undo) : string?` (null when shown; `DrawerReview.Busy` while a review or another bar is up). It sets `BarOpen`; Undo runs `undo`, then shows the result again without Undo; Done closes.
  - `DeskController.Tidy(DrawerTidy tidy, string? monitor) : (int Moved, IReadOnlyDictionary<string, DrawerPlace> Before)?` (null when busy)
  - `DrawerReview.Tidied(int n)` = "1 drawer lined up." / "N drawers lined up."; `DrawerReview.TidyNothing` = "The drawers are already lined up."
  - `App.TidyDrawers(DrawerTidy? tidy = null, string? monitor = null) : string?` (the line to show, or null when the bar opened)

- [ ] **Step 1: Write the failing tests**

```csharp
using Sanduhr.App.Desk;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

public sealed class DrawerTidyTests
{
    [Fact]
    public void Tidy_lines_drawers_up_and_undo_puts_every_place_back() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (controller, service) = box.Desk();
        service.SetPlace("images", new DrawerPlace(DeskMonitors.Main, 700, 400));
        service.SetPlace("documents", new DrawerPlace(DeskMonitors.Main, 100, 600));
        var before = new Dictionary<string, DrawerPlace>(service.Settings.Places);

        var result = controller.Tidy(DrawerTidy.Default, DeskMonitors.Main);
        Assert.NotNull(result);
        Assert.True(result!.Value.Moved >= 2);
        Assert.NotEqual(before["images"], service.Settings.Places["images"]);

        controller.RestorePlaces(result.Value.Before);
        Assert.Equal(before["images"], service.Settings.Places["images"]);
        Assert.Equal(before["documents"], service.Settings.Places["documents"]);
    });

    [Fact]
    public void Tidy_refuses_while_a_review_is_open() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (controller, service) = box.Desk();
        box.OpenDrawersReview();   // the sandbox's way to put a review up (reuse what DrawerReviewServiceTests do)
        var before = new Dictionary<string, DrawerPlace>(service.Settings.Places);
        Assert.Null(controller.Tidy(DrawerTidy.Default, DeskMonitors.Main));
        Assert.Equal(before, service.Settings.Places);
    });

    [Fact]
    public void A_drawer_that_cannot_fit_keeps_its_place() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox(screenWidth: 500, screenHeight: 450);
        var (controller, service) = box.Desk();
        service.SetPlace("images", new DrawerPlace(DeskMonitors.Main, 10, 10));
        service.SetPlace("documents", new DrawerPlace(DeskMonitors.Main, 20, 20));
        controller.Tidy(DrawerTidy.Default, DeskMonitors.Main);
        var places = service.Settings.Places;
        Assert.True(places.Values.All(p => p.X >= 0 && p.Y >= 0));   // nothing pushed off screen
    });
}
```

Add `screenWidth`/`screenHeight` options to `DrawerSandbox` if it lacks them, and `OpenDrawersReview()` by calling `DrawerReviewService.SortNow()` with a listing that matches a rule (as the existing review tests do).

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.App.Tests/Sanduhr.App.Tests.csproj -c Release --filter DrawerTidyTests`
Expected: FAIL to build.

- [ ] **Step 3: `DeskController.Tidy` and `RestorePlaces`**

```csharp
    /// <summary>Tidy drawers on one monitor (look and placement spec, section 2): null while a review or bar is up.</summary>
    internal (int Moved, IReadOnlyDictionary<string, DrawerPlace> Before)? Tidy(DrawerTidy tidy, string? monitor)
    {
        if (DrawerReviewService.Current is { Reviewing: true } or { BarOpen: true } || DeskDesignService.Current?.Reviewing == true) return null;
        var screen = DrawerScreens.Find(monitor ?? DeskMonitors.Main, _screens) ?? DrawerScreens.Primary(_screens);
        if (screen is null) return (0, _drawers.Settings.Places);
        string mon = screen.Primary ? DeskMonitors.Main : screen.Id!;
        var work = DrawerScreens.WorkDips(screen);
        var workRect = new DrawerRect(0, 0, work.Width, work.Height);
        var now = DrawerPlacesNow().Where(p => p.Value.Monitor == mon)
            .ToDictionary(p => p.Key, p => new DrawerRect(p.Value.X, p.Value.Y, DrawerPlacement.Width,
                DrawerPlacement.Height(_drawers.Get(p.Key)?.Folded == true)), StringComparer.Ordinal);
        var order = DrawerTidyPlan.Order(now, workRect, tidy.Corner);
        var blocked = _drawers.Settings.KeepClear ? IconArea(screen) : [];
        var slots = DrawerTidyPlan.Slots([.. order.Select(id => (id, now[id].H))], workRect, tidy, [.. blocked, .. PieceRects(screen)]);
        var before = new Dictionary<string, DrawerPlace>(_drawers.Settings.Places, StringComparer.Ordinal);
        var next = new Dictionary<string, DrawerPlace>(before, StringComparer.Ordinal);
        int moved = 0;
        foreach (var (id, r) in slots)
        {
            var p = new DrawerPlace(mon, r.X, r.Y);
            if (!next.TryGetValue(id, out var old) || Math.Abs(old.X - p.X) > 0.5 || Math.Abs(old.Y - p.Y) > 0.5) moved++;
            next[id] = p;
        }
        _drawers.Save(_drawers.Settings with { Places = next, Tidy = tidy });
        return (moved, before);
    }

    internal void RestorePlaces(IReadOnlyDictionary<string, DrawerPlace> before) =>
        _drawers.Save(_drawers.Settings with { Places = new Dictionary<string, DrawerPlace>(before, StringComparer.Ordinal) });
```

Use the real names for `DrawerScreens.Find`'s and `Primary`'s returns (nullable or not) and `DrawerService.Get`.

- [ ] **Step 4: `DrawerReviewService.ShowResult`**

```csharp
    /// <summary>A result line with Undo on the drawers bar, for changes that move no file (Tidy, chat drawer settings): null when it
    /// showed, else the line to show instead. It holds the bar like a review's result, so a sort waits for it.</summary>
    public string? ShowResult(string line, Action undo)
    {
        if (Reviewing || BarOpen || _host.OtherReviewOpen) return DrawerReview.Busy;
        ShowBar(DrawerReview.Result(line, canUndo: true),
            () => { undo(); ShowBar(DrawerReview.Result(line, canUndo: false), null, null, CloseBar); },
            null, CloseBar);
        return null;
    }
```

Match `ShowBar`/`CloseBar`'s real signatures (the research found `ShowBar(model, apply, edit, dismiss)`, both private): call them as they are called for the review's own result.

- [ ] **Step 5: `App.TidyDrawers`**

```csharp
    private string? TidyDrawers(DrawerTidy? tidy = null, string? monitor = null)
    {
        if (_desk is null || _drawers is null || _drawerReview is null) return SettingsNames.DrawersSortUnavailable;
        var t = tidy ?? _drawers.Settings.Tidy;
        if (_desk.Tidy(t, monitor) is not { } r) return DrawerReview.Busy;
        if (r.Moved == 0) return DrawerReview.TidyNothing;
        return _drawerReview.ShowResult(DrawerReview.Tidied(r.Moved), () => _desk.RestorePlaces(r.Before));
    }
```

Add `Tidied` and `TidyNothing` to `DrawerReview.cs` (Core) with a test in `DrawerReviewTests` if one pins the copy.

- [ ] **Step 6: Run the tests**

Run: `dotnet test windows-dotnet/tests/Sanduhr.App.Tests/Sanduhr.App.Tests.csproj -c Release --filter "DrawerTidyTests|FullyQualifiedName~DrawerReview"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src windows-dotnet/tests
git commit -m "feat(drawers): Tidy drawers from a corner, with Undo on the drawers bar"
```

---

### Task 7: The Drawers page, and a Look menu on each drawer

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/DrawersSettingsViewModel.cs`, `windows-dotnet/src/Sanduhr.App/Views/DrawersPage.xaml`, `windows-dotnet/src/Sanduhr.App/Interop/ShellMenu.cs` (a checked submenu), `windows-dotnet/src/Sanduhr.App/Desk/Drawers/DrawerPiece.xaml.cs` (menu), `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`, `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs`, `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (wire Tidy)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/DrawersLookPageTests.cs`

**Interfaces:**
- Consumes: `DrawerSettings` new props (Task 1); `App.TidyDrawers` (Task 6).
- Produces:
  - `DrawersSettingsViewModel`: `[ObservableProperty] int LookIndex` (0..3 → `DrawerLookKind`), `int Opacity`, `bool ShowOpacity`, `bool Snap`, `bool KeepClear`, `int TidyCornerIndex`, `int TidyFlowIndex`, `IRelayCommand TidyCommand`, `Func<DrawerTidy, string?>? TidyRequested`
  - `ShellMenuItem` gains `bool Checked` and `IReadOnlyList<ShellMenuItem>? Children` (a submenu)
  - Copy in `SettingsNames`: `DrawersLook = "Drawer look"`, `DrawersLookWords = ["Solid", "Glass", "Outline", "Bare"]`, `DrawersOpacity = "Opacity"`, `DrawersSnap = "Snap drawers"`, `DrawersSnapHint = "Hold Alt while dragging to place a drawer freely."`, `DrawersKeepClear = "Keep clear of desktop icons"`, `DrawersTidy = "Tidy drawers"`, `DrawersTidyCorner = "Corner"`, `DrawersTidyCornerWords = ["Top left", "Top right", "Bottom left", "Bottom right"]`, `DrawersTidyFlow = "Direction"`, `DrawersTidyFlowWords = ["Column", "Row"]`, `DrawerLookMenu = "Look"`, `DrawerLookSame = "Same as the others"`
  - `SettingsIndex` anchor `DrawersLookSection = "drawers-look"` with an index entry `E(SettingsNames.DrawersLook, SettingsPage.Drawers, DrawersLookSection, "style blend glass outline transparent opacity snap tidy align arrange icons")`

- [ ] **Step 1: Write the failing tests**

```csharp
using Sanduhr.App.ViewModels;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

public sealed class DrawersLookPageTests
{
    [Fact]
    public void The_page_reads_and_saves_every_new_setting() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var s = box.Service();
        var vm = new DrawersSettingsViewModel(s, null, box.Store);
        Assert.Equal(0, vm.LookIndex);
        Assert.Equal(55, vm.Opacity);
        Assert.True(vm.ShowOpacity);

        vm.LookIndex = (int)DrawerLookKind.Outline;
        Assert.False(vm.ShowOpacity);
        vm.Opacity = 70;
        vm.Snap = false;
        vm.KeepClear = false;
        vm.TidyCornerIndex = (int)DrawerCorner.BottomLeft;
        vm.TidyFlowIndex = (int)DrawerFlow.Row;

        var saved = s.Settings;
        Assert.Equal(DrawerLookKind.Outline, saved.Look);
        Assert.Equal(70, saved.Opacity);
        Assert.False(saved.Snap);
        Assert.False(saved.KeepClear);
        Assert.Equal(new DrawerTidy(DrawerCorner.BottomLeft, DrawerFlow.Row), saved.Tidy);
    });

    [Fact]
    public void Tidy_asks_the_app_with_the_chosen_corner_and_direction() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var vm = new DrawersSettingsViewModel(box.Service(), null, box.Store);
        DrawerTidy? asked = null;
        vm.TidyRequested = t => { asked = t; return null; };
        vm.TidyCornerIndex = (int)DrawerCorner.TopLeft;
        vm.TidyCommand.Execute(null);
        Assert.Equal(new DrawerTidy(DrawerCorner.TopLeft, DrawerFlow.Column), asked);
    });
}
```

Also add the page's new controls to whatever `AppWindowNames`/`SettingsWindowNames` test walks the Drawers page for accessible names, and run it.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.App.Tests/Sanduhr.App.Tests.csproj -c Release --filter DrawersLookPageTests`
Expected: FAIL to build.

- [ ] **Step 3: The view model**

Follow the existing `_enabled` pattern (`[ObservableProperty]` + `partial void On…Changed` guarded by `_loading`, `Refresh()` reloading from `_drawers.Settings`). Each change saves through `_drawers.Save(_drawers.Settings with { … })`. `ShowOpacity => DrawerStyle.HasFill((DrawerLookKind)LookIndex)`, raised when `LookIndex` changes. `Opacity` clamps to `DrawerStyle.MinOpacity..MaxOpacity`.

```csharp
    public Func<DrawerTidy, string?>? TidyRequested { get; set; }

    [RelayCommand]
    private void Tidy() =>
        Status = TidyRequested?.Invoke(new DrawerTidy((DrawerCorner)TidyCornerIndex, (DrawerFlow)TidyFlowIndex)) ?? "";
```

- [ ] **Step 4: The page**

In `DrawersPage.xaml`, a new section after "drawers-sort" (`views:SettingsAnchor.Id="drawers-look"`), separated by the page's 1 px Border:
- a `TextBlock` heading `SettingsNames.DrawersLook`;
- a `ComboBox` bound to `LookIndex` with the four look words, `AutomationProperties.Name="{x:Static core:SettingsNames.DrawersLook}"`;
- a `Slider` 20..100 bound to `Opacity`, `Visibility` bound to `ShowOpacity`, `AutomationProperties.Name="{x:Static core:SettingsNames.DrawersOpacity}"`, with the value shown beside it as "55%";
- `CheckBox` Snap (with the Alt hint under it), `CheckBox` Keep clear, both in the `ThemedCheckBox` style as the page's existing switch;
- a row: Corner `ComboBox`, Direction `ComboBox`, and a `FlatButton` "Tidy drawers" bound to `TidyCommand`;
- replace the "Put drawers back top right" button (`ResetPlacesCommand`) with Tidy: the same job, done right. Remove `DrawersPutBack` and `ResetPlaces` only if no test or index entry still needs them; otherwise keep them and leave the button out.

Wire `svm.Drawers.TidyRequested = t => TidyDrawers(t);` in `App.ShowSettingsAsync` next to the other Drawers wiring.

- [ ] **Step 5: The drawer's Look submenu**

`ShellMenuItem` gets `bool Checked = false` and `IReadOnlyList<ShellMenuItem>? Children = null`. In `ShellMenu.Show`, an item with children is appended with `CreatePopupMenu()` + `AppendMenuW(h, MF_POPUP | MF_STRING, (nuint)sub, text)`; a checked item adds `MF_CHECKED` (0x8). Destroy submenus with the parent (`DestroyMenu` on the parent destroys attached popups).

In `DrawerPiece.OnRightClick`, add before Settings:

```csharp
var current = _service.Settings.Looks.TryGetValue(_state.Def.Id, out var own) ? (DrawerLookKind?)own : null;
new ShellMenuItem(LookId, SettingsNames.DrawerLookMenu, Children:
[
    new(LookSameId, SettingsNames.DrawerLookSame, Checked: current is null),
    .. Enum.GetValues<DrawerLookKind>().Select(k => new ShellMenuItem(LookFirstId + (int)k, SettingsNames.DrawersLookWords[(int)k], Checked: current == k)),
]),
```

with ids after `SettingsId` (`LookId = OwnFirst + 4`, `LookSameId = + 5`, `LookFirstId = + 6`). On a choice, save `Looks` with the drawer's entry set or removed. The menu follows the existing `ShellMenuItem` constructor; adjust to its real parameter names.

- [ ] **Step 6: Run the tests**

Run: `dotnet test windows-dotnet/tests/Sanduhr.App.Tests/Sanduhr.App.Tests.csproj -c Release --filter "DrawersLookPageTests|FullyQualifiedName~DrawersSettings|FullyQualifiedName~ShellMenu|AppWindowNames|SettingsWindow"` and `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "SettingsIndex|SettingsNames"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src windows-dotnet/tests
git commit -m "feat(drawers): Drawer look, opacity, snap, keep clear and Tidy on the Drawers page, and a Look menu on each drawer"
```

---

### Task 8: The AI chat: look, opacity and arrange

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/PastedSetup.cs` (`ReadBlock`'s drawers part, `PastedSetupRead`), `windows-dotnet/src/Sanduhr.Core/SetupPrompt.cs` (drawers section), `windows-dotnet/src/Sanduhr.Core/SetupCopy.cs`, `windows-dotnet/src/Sanduhr.App/ViewModels/ChatDesignViewModel.cs`, `windows-dotnet/src/Sanduhr.App/Views/ChatDesignWindow.xaml`, `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (`OpenChatDesign`)
- Test: `windows-dotnet/tests/Sanduhr.Tests/PastedSetupTests.cs`, `SetupPromptTests.cs`; `windows-dotnet/tests/Sanduhr.App.Tests/ChatDesignTests.cs`

**Interfaces:**
- Consumes: `DrawerStyle` words and ranges (Task 1); `App.TidyDrawers` (Task 6).
- Produces:
  - `public sealed record PastedDrawerLook(DrawerLookKind? Look, int? Opacity, DrawerTidy? Arrange)` with `bool Any`
  - `PastedSetupRead` gains `PastedDrawerLook? DrawerLook` (null when the paste has none), and `Ok` counts it as content
  - `ChatDesignViewModel`: a fifth constructor argument `Func<PastedDrawerLook, string?> useDrawerSettings`, `[ObservableProperty] bool CanUseDrawerSettings`, `IRelayCommand UseDrawerSettingsCommand`, `string DrawerSettingsLine`
  - `SetupCopy.ChatUseDrawerSettings = "Use these drawer settings"`, `SetupCopy.ChatDrawerSettingsDone = "Drawer look saved."`

Rules: in the `drawers` part, members are `rules`, `look`, `opacity`, `arrange`, each optional, at least one present; `rules` when present keeps today's rules (1 to 20). `look` must be a `DrawerStyle.LookWords` word; `opacity` a whole number 20..100; `arrange` an object with optional `corner` (`CornerWords`) and `direction` (`FlowWords`), at least one, missing parts taken from the user's current Tidy at apply time. Any other member is refused by field.

- [ ] **Step 1: Write the failing tests**

Add to `PastedSetupTests`:

```csharp
    [Fact]
    public void A_drawers_part_with_only_a_look_is_read_and_keeps_the_rules()
    {
        var r = PastedSetup.Read("""{"drawers":{"look":"glass","opacity":70,"arrange":{"corner":"bl","direction":"row"}}}""");
        Assert.True(r.Ok);
        Assert.Null(r.Rules);
        Assert.Equal(DrawerLookKind.Glass, r.DrawerLook!.Look);
        Assert.Equal(70, r.DrawerLook.Opacity);
        Assert.Equal(new DrawerTidy(DrawerCorner.BottomLeft, DrawerFlow.Row), r.DrawerLook.Arrange);
    }

    [Theory]
    [InlineData("""{"drawers":{"look":"neon"}}""", "drawers.look")]
    [InlineData("""{"drawers":{"opacity":5}}""", "drawers.opacity")]
    [InlineData("""{"drawers":{"opacity":70.5}}""", "drawers.opacity")]
    [InlineData("""{"drawers":{"arrange":{"corner":"middle"}}}""", "drawers.arrange.corner")]
    [InlineData("""{"drawers":{"arrange":{}}}""", "drawers.arrange")]
    [InlineData("""{"drawers":{"colour":"red"}}""", "drawers.colour")]
    [InlineData("""{"drawers":{}}""", "drawers")]
    public void Bad_drawer_settings_are_refused_by_field(string json, string field)
    {
        var r = PastedSetup.Read(json);
        Assert.False(r.Ok);
        Assert.Contains(r.Reasons, x => x.Field == field);
    }
```

Add to `SetupPromptTests`:

```csharp
    [Fact]
    public void The_prompt_teaches_every_look_corner_and_direction_and_the_opacity_range()
    {
        var p = SetupPrompt.Build([]);
        foreach (var w in DrawerStyle.LookWords.Concat(DrawerStyle.CornerWords).Concat(DrawerStyle.FlowWords)) Assert.Contains(w, p);
        Assert.Contains($"{DrawerStyle.MinOpacity} to {DrawerStyle.MaxOpacity}", p);
    }
```

Add to `ChatDesignTests` (match its existing `Make` helper; add the new delegate):

```csharp
    [Fact]
    public void Drawer_settings_enable_their_button_and_apply_through_the_app()
    {
        var calls = new List<string>();
        var vm = MakeWithDrawerSettings(calls);
        vm.PasteText = """{"drawers":{"look":"outline","arrange":{"corner":"tl"}}}""";
        Assert.True(vm.CanUseDrawerSettings);
        Assert.False(vm.CanUseRules);
        vm.UseDrawerSettingsCommand.Execute(null);
        Assert.Equal(["drawer-settings:outline"], calls);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "PastedSetupTests|SetupPromptTests"` and the App filter `ChatDesignTests`
Expected: FAIL.

- [ ] **Step 3: The reader**

In `ReadBlock`'s drawers branch, replace "only `rules`, required" with:

```csharp
if (block["drawers"] is not JsonObject dobj || dobj.Count == 0)
    reasons.Add(new DeskReason("drawers", "must hold rules, look, opacity or arrange"));
else
{
    foreach (var (key, _) in dobj)
        if (key is not ("rules" or "look" or "opacity" or "arrange"))
            reasons.Add(new DeskReason("drawers." + Clip(key, 24), "is not part of drawers; it holds rules, look, opacity and arrange"));
    if (dobj.ContainsKey("rules")) { /* today's rules reading, unchanged */ }
    drawerLook = ReadDrawerLook(dobj, reasons);
}
```

```csharp
private static PastedDrawerLook? ReadDrawerLook(JsonObject d, List<DeskReason> reasons)
{
    DrawerLookKind? look = null; int? opacity = null; DrawerTidy? arrange = null;
    if (d.ContainsKey("look"))
    {
        look = DrawerStyle.Look(Str(d["look"]));
        if (look is null) reasons.Add(new DeskReason("drawers.look", "must be one of " + string.Join(", ", DrawerStyle.LookWords)));
    }
    if (d.ContainsKey("opacity"))
    {
        if (WholeNumber(d["opacity"]) is { } n && n >= DrawerStyle.MinOpacity && n <= DrawerStyle.MaxOpacity) opacity = n;
        else reasons.Add(new DeskReason("drawers.opacity", $"must be a whole number from {DrawerStyle.MinOpacity} to {DrawerStyle.MaxOpacity}"));
    }
    if (d.ContainsKey("arrange"))
    {
        if (d["arrange"] is not JsonObject a || a.Count == 0)
            reasons.Add(new DeskReason("drawers.arrange", "must be { \"corner\": ..., \"direction\": ... }"));
        else
        {
            foreach (var (key, _) in a)
                if (key is not ("corner" or "direction")) reasons.Add(new DeskReason("drawers.arrange." + Clip(key, 24), "is not part of arrange"));
            DrawerCorner? corner = null; DrawerFlow? flow = null;
            if (a.ContainsKey("corner") && (corner = DrawerStyle.Corner(Str(a["corner"]))) is null)
                reasons.Add(new DeskReason("drawers.arrange.corner", "must be one of " + string.Join(", ", DrawerStyle.CornerWords)));
            if (a.ContainsKey("direction") && (flow = DrawerStyle.Flow(Str(a["direction"]))) is null)
                reasons.Add(new DeskReason("drawers.arrange.direction", "must be one of " + string.Join(", ", DrawerStyle.FlowWords)));
            arrange = new DrawerTidy(corner ?? DrawerTidy.Default.Corner, flow ?? DrawerTidy.Default.Flow);
            arrangeParts = (corner is not null, flow is not null);
        }
    }
    var result = new PastedDrawerLook(look, opacity, arrange);
    return result.Any ? result : null;
}
```

`arrangeParts` is how the App fills missing parts from the user's current Tidy: carry it on `PastedDrawerLook` as `bool HasCorner, bool HasFlow` (defaults true) instead of a loose variable, and set them here. Use `WholeNumber` as item 9 defined it (accepts `60.0`, refuses `70.5`). When any reason exists, null the drawer look as the rules are nulled ("nothing applies partly").

- [ ] **Step 4: The prompt**

In `SetupPrompt`'s drawers section, after the rules lines, and change the shape line to `{ "desk": { ... }, "drawers": { "look": ..., "opacity": ..., "arrange": { ... }, "rules": [ ... ] } }`:

```csharp
Line("\"drawers\" can also set how drawers look and where they line up; leave any of these out to keep mine:");
Line("- look: " + string.Join(", ", DrawerStyle.LookWords) + " (solid is a plate, glass a lighter plate, outline a thin frame, bare no frame).");
Line($"- opacity: a whole number from {DrawerStyle.MinOpacity} to {DrawerStyle.MaxOpacity}, for solid and glass.");
Line("- arrange: { corner: " + string.Join(", ", DrawerStyle.CornerWords) + ", direction: " + string.Join(", ", DrawerStyle.FlowWords) + " } lines the drawers up from that corner.");
```

Keep the existing "1 to 20 rules; leave drawers out to keep my rules" line, reworded to "leave rules out to keep my rules as they are".

- [ ] **Step 5: The chat window**

`ChatDesignViewModel`: read `read.DrawerLook`; `CanUseDrawerSettings = read.DrawerLook is not null && no findings`; `DrawerSettingsLine` describes it ("Look: glass, 70%. Lines drawers up from the bottom left, in a row."). The command calls `_useDrawerSettings(look)` and sets `Status` to its line or `SetupCopy.ChatDrawerSettingsDone`. The strict rule stays: any problem disables every button.

`ChatDesignWindow.xaml`: a third button in the button row, `Content="{x:Static core:SetupCopy.ChatUseDrawerSettings}"`, `Command="{Binding UseDrawerSettingsCommand}"`, `IsEnabled="{Binding CanUseDrawerSettings}"`, with its accessible name, and a `TextBlock` for `DrawerSettingsLine` beside `RulesText`.

`App.OpenChatDesign` passes:

```csharp
look =>
{
    if (_drawers is null || _desk is null) return SettingsNames.DrawersSortUnavailable;
    if (_drawerReview is { Reviewing: true } or { BarOpen: true } || DeskDesignService.Current?.Reviewing == true) return DrawerReview.Busy;
    var s = _drawers.Settings;
    var before = s;
    _drawers.Save(s with { Look = look.Look ?? s.Look, Opacity = look.Opacity ?? s.Opacity });
    string? line = null;
    if (look.Arrange is { } a)
    {
        var tidy = new DrawerTidy(look.HasCorner ? a.Corner : s.Tidy.Corner, look.HasFlow ? a.Flow : s.Tidy.Flow);
        if (_desk.Tidy(tidy, null) is { } r)
            line = _drawerReview!.ShowResult(DrawerReview.Tidied(r.Moved), () => { _desk.RestorePlaces(r.Before); _drawers.Save(_drawers.Settings with { Look = before.Look, Opacity = before.Opacity }); });
    }
    return line;
}
```

So Undo on the bar restores both places and the look. Without `arrange`, there is no bar; the look change shows in the window's status line and can be changed back on the Drawers page.

- [ ] **Step 6: Run the tests**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "PastedSetupTests|SetupPromptTests|SetupCopy"` and `dotnet test windows-dotnet/tests/Sanduhr.App.Tests/Sanduhr.App.Tests.csproj -c Release --filter "ChatDesignTests|SetupPromptPiecesTests"`, then `dotnet test windows-dotnet/tests/Sanduhr.Mcp.Tests/Sanduhr.Mcp.Tests.csproj --filter TrustBoundary`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src windows-dotnet/tests
git commit -m "feat(drawers): an AI chat can set the drawer look, opacity and arrangement"
```

---

### Task 9: Harness shots of the four looks

**Files:**
- Create: `windows-dotnet/tools/Sanduhr.RenderHarness/DrawerLooksScenario.cs`
- Modify: `windows-dotnet/tools/Sanduhr.RenderHarness/Program.cs` (`opt` list ~114, the header comment, the dispatch ~377)

- [ ] **Step 1: Write the scenario**

Follow `ThemeMatchScenario.RenderThemeMatch` and the App tests' `DrawerPiece` construction (`DrawerInteractionTests.cs:22-24`): a `DrawerService` over a scratch sandbox with a few files in one drawer, then for each theme in `["obsidian", "aurora"]`, for each wallpaper stand-in (`busy`: a generated gradient with stripes; `plain`: `#1e2430`), for each `DrawerLookKind`, build `DrawerLook.From(...)` with that look, opacity 55, and the stand-in's luminance, then a `DrawerPiece`, and composite it over the stand-in with `Composite` and `SaveScaled(..., $"drawer-look-{theme}-{wall}-{look}", 0.5)`. Sixteen PNGs.

- [ ] **Step 2: Run it**

Run: `SANDUHR_DEV_PROFILE=<a fresh scratch folder> dotnet run --project windows-dotnet/tools/Sanduhr.RenderHarness -c Release -- --scenario drawer-looks --out <scratch>/drawer-looks`
Expected: 16 PNGs. Open each: labels readable on the busy stand-in for Outline and Bare (chips), the frame visible for Solid and Glass, nothing clipped.

- [ ] **Step 3: Commit**

```bash
git add windows-dotnet/tools/Sanduhr.RenderHarness
git commit -m "test(drawers): harness shots of the four drawer looks in obsidian and aurora"
```

---

### Task 10: The whole suite

- [ ] **Step 1:** `dotnet build windows-dotnet/Sanduhr.slnx -c Release`, then `dotnet test windows-dotnet/Sanduhr.slnx -c Release --no-build`. Expected: 0 failures in Core, App and Mcp.
- [ ] **Step 2 (the owner, live):**
  1. Each look over the real wallpaper.
  2. Drag snapping near the clock and near another drawer, then with Alt held.
  3. Tidy in all four corners, then Undo.
  4. Keep clear with icons on the left, then turned off.
  5. An Explorer restart while drawers are up.
