# Drawers look and placement: design

Windows, 4.5.0. Brainstormed with the owner on 2026-10-31, after Set up Sanduhr was built (`cart/setup-4-5`).
Dashboard decision: `ZK0vni3vEHTYZS4bNIuu` (approach 1). Builds on the drawers spec
(`2026-10-28-drawers-design.md`) and its free-panel ruling (`d9u8eTTkYsJFjjA5ujBY`), and meets the setup spec's
section 6 (whatever drawers can do, a pasted AI chat answer can set).

## Why

Drawers shipped (PR 399) looking and sitting one way:

- **Look.** Every drawer draws the Desk plate (at least 55% opaque), the Desk ink, the accent and the Desk font
  (`DrawerLook`). All drawers look the same, and none can blend in more or less.
- **Placement.** Free drag by the title; the only rule keeps the title bar on screen (`DrawerPlacement.Clamp`).
  New drawers stack top right of the main monitor (`DrawerPlacement.Arrive`). Nothing snaps, nothing lines up,
  nothing keeps drawers off the desktop icons.

The owner: "I want the user to be able to have styling options: how they blend into the desktop and where they are
placed."

## The goal

The user chooses how drawers blend into the desktop and where they sit, without a wall of sliders. A desktop with
several drawers looks arranged, not scattered, and fits the Desk look in force.

## Out of scope

- Frames drawn under Explorer's own icons, and moving icons (drawers stage 2).
- Resizing drawers, list views and icon sizes.
- Per-drawer colours: colours stay the Desk look's (or the widget theme's with Match on).
- `propose_drawers` and the MCP (plan 3 of the drawers spec). `propose_desk` does not change.
- The Mac.

## 1. Looks

One setting, **Drawer look**, on the Drawers page, for all drawers:

| Look | Frame | Title | Items |
|---|---|---|---|
| **Solid** | The plate colour at the opacity setting (today's look) | Ink on the plate | On the plate |
| **Glass** | A lighter tinted plate with a thin edge highlight; blurred behind if the spike allows | Ink | On the plate |
| **Outline** | No fill; a 1 px border in the ink | Ink with a soft shadow | On the wallpaper, each label shadowed |
| **Bare** | None until hover, then a faint outline | Small, above the items | On the wallpaper, each label shadowed |

- **Opacity:** one slider, 20 to 100%, for Solid and Glass's fill (default 55%, today's plate). Hidden for Outline
  and Bare. Below 55%, labels draw on the readable chips described next whenever the contrast check fails.
- **Readable on any wallpaper:** Outline and Bare check the ink against the wallpaper under the drawer with the Desk's
  3:1 contrast check (`WallpaperOverview`). Where it fails, labels draw on a small shadowed chip instead of bare text.
- **Clicks:** Outline and Bare keep a fill at 1% opacity, so a click on the drawer lands on the drawer, never on the
  desktop behind it.
- **Per drawer:** each drawer's menu gets **Look: Same as the others, Solid, Glass, Outline, Bare**. Stored by drawer
  id; a drawer removed takes its entry with it.
- **Glass's blur** depends on spike question 1 (section 5). Without blur, Glass is the tinted plate and edge highlight
  alone. Either way it is called Glass.

## 2. Placement

Drawers stay free panels. While dragging, a drawer snaps, nearest rule first, within 12 px:

1. **To other drawers:** edges align, and a gap snaps to `DrawerPlacement.Gap` (12 px).
2. **To Desk pieces:** a drawer docks beside a piece's stack at the drawer gap (12 px).
3. **To the screen:** work-area edges and corners at `DrawerPlacement.Margin` (24 px).
4. **To the grid:** 8 px steps when nothing else is near.

Holding **Alt** while dragging turns snapping off for that drag. **Snap drawers** on the Drawers page turns it off.

**Keep clear of desktop icons** (on by default):

- The icon area is the union of the desktop icons' rectangles, read from the Shell's desktop folder view
  (`IShellWindows.FindWindowSW(SWC_DESKTOP)` → `IFolderView.GetItemPosition` and `GetSpacing`), read-only, plus
  the spacing as a margin. It is read on Desk reload, after an Explorer restart, and when a drag starts.
- When it can't be read (Explorer restarting, icons hidden, an error), the area falls back to the left-hand columns
  Windows fills by default: as many icon columns as the desktop's item count needs at the system icon spacing.
- A drag that ends overlapping the area slides to the nearest free spot that keeps the drawer on its monitor.
- Arrival and Tidy never place a drawer in the area.
- Icons that move later (a new file lands) do not push drawers. The area applies at the next drag, arrival or Tidy.
- Off: drawers sit anywhere.

**Tidy drawers**, on the Drawers page and in every drawer's menu:

- A **corner** (top left, top right, bottom left, bottom right) and a **direction** (column down, row across).
  Remembered; the default is top right, column (today's arrival).
- Every drawer on the monitor lines up from the corner in its current order (by distance from the corner), at the
  Desk margin and the drawer gap, avoiding the icon area. Folded drawers keep their folded height.
- When a column or row is full, it wraps into the next one, toward the screen's centre.
- It moves panels only. The result line says how many moved and offers **Undo**, which puts every place back.

**Arrival:** a new drawer takes the next Tidy slot for the remembered corner and direction on the main monitor.

**Per monitor:** places keep their monitor id (`DrawerPlace.Monitor`). A drawer dragged across monitors snaps and
clamps on the monitor where it lands. Tidy works on one monitor: the one the menu's drawer is on, or the main monitor
from the Drawers page.

## 3. Settings, the AI chat and the edges

**Stored** in the existing `drawers` object, decoded entry by entry (an unreadable entry takes its default):

| Key | Values | Default |
|---|---|---|
| `look` | `solid`, `glass`, `outline`, `bare` | `solid` |
| `opacity` | 20 to 100 | 55 (today's drawer plate: `DrawerLook.MinPlateOpacity`) |
| `looks` | drawer id → look | empty |
| `snap` | bool | true |
| `keep_clear` | bool | true |
| `tidy` | `{ corner: tl|tr|bl|br, direction: column|row }` | `{ tr, column }` |

An upgrader's drawers look and sit exactly as before: Solid, their places untouched.

**AI chat:** the pasted answer's `drawers` part grows beside `rules`. Every member stays optional:

```json
"drawers": { "look": "glass", "opacity": 70,
             "arrange": { "corner": "tr", "direction": "column" },
             "rules": [ ... ] }
```

- The prompt (`SetupPrompt`) lists the look words, the opacity range and the arrange words from the same Core
  constants the reader checks, and its drift test pins them.
- `PastedSetup` refuses, by field, a look outside the list, an opacity out of range, and unknown arrange words.
- The chat window gets **Use these drawer settings** beside Use these drawer rules: it saves the look and opacity
  and runs Tidy with the arrange words, then shows a result line with Undo. It never moves a file, so it needs no
  review. Rules still open the drawers review.
- A chat cannot set a per-drawer look: it cannot see drawer names.

**Edges:**

- **Explorer restarts:** the icon area is read again on the Desk's existing restart path; until then, the fallback
  columns apply.
- **Resolution or scale changes:** `DrawerPlacement.Clamp` keeps every title bar on screen; snapping uses each
  monitor's work area at its own DPI.
- **A drawer review or a design review is open:** Tidy and Use these drawer settings answer with the existing busy
  line, so a preview is never rearranged under it.
- **Desktop off** (setup): drawers are the only pieces, so snapping to Desk pieces has nothing to dock to; the other
  rules still apply.
- **Streamer mode:** looks and places carry no names or paths. Nothing new is drawn from data.
- **Accessibility:** every new control has an accessible name; Outline and Bare keep the drawer's group name
  ("Screenshots drawer, 14 items"); the Look menu items are checkable with their state read.

## 4. Testing

**Core (pure):**

- Each look's model, and the readable fallback when contrast fails.
- Every snap rule with invented rectangles, its 12 px threshold, rule order, and Alt off.
- The icon area from rectangles, and the fallback columns from an item count and spacing.
- The nearest free spot from a drop on the icon area.
- Tidy's slots in all four corners and both directions, with wrapping on a small screen and folded heights.
- Arrival from Tidy's next slot.
- Decoding a 4.0.1 `drawers` object, and every new key unreadable in turn.
- The chat words pinned to the constants; the reader's refusals by field.

**App:**

- Each look builds; Outline and Bare take clicks.
- Every new control has an accessible name.
- Tidy then Undo restores every place.
- A paste holding only `look` changes only the look.
- Tidy refuses while a review is open.

**Harness:** the four looks over a busy and a plain wallpaper, in obsidian and aurora.

**Live (the owner):**

- Each look over the real wallpaper.
- Drag snapping near the clock and near another drawer, and Alt to override.
- Tidy in all four corners.
- Keep clear with icons on the left; then turned off.
- An Explorer restart while drawers are up.

## 5. Order

1. **Spike** (throwaway, a day at most):
   - Does a blur-behind backdrop (`DWMWA_SYSTEMBACKDROP_TYPE`, or the composition accent the Desk can use) render on a
     drawer window at the Desk layer on 24H2?
   - Does `IFolderView` report icon positions reliably: after an Explorer restart, on mixed-DPI monitors, with
     auto-arrange on and off?
   - Verdicts go in the checklist before step 2.
2. Core: looks, snapping, the icon area, Tidy, arrival, settings.
3. The drawer's look in `DrawerPiece`, from `DrawerLook` extended with the look and opacity.
4. Placement in the app: snapping during a drag, the icon reader with its fallback, Tidy with Undo.
5. The Drawers page and each drawer's menu.
6. The AI chat: prompt, reader, Use these drawer settings.
7. Harness shots.

## Release

This builds on `cart/setup-4-5`, and 4.5.0 ships setup, drawers and this together (the owner's order, 2026-10-31).
