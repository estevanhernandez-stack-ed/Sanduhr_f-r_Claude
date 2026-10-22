# Desk pieces per monitor — design (3.7.0)

Windows only. Today every Desk piece is drawn on every monitor (one layout, mirrored). Rainmeter
places each skin on the monitor you choose, so a Desk that mirrors can't replace it. Este approved
per-piece monitors for 3.7.0 on 2026-10-21 (dashboard `Yy2buQJnutGnG90uEV6n`).

## Behaviour

- Each placed piece has a **monitor**: **All monitors** (today's behaviour, and the default for every
  existing layout), **Main monitor** (whichever is primary now), or **a specific monitor**.
- A specific monitor is named in the UI by size and position: "3440×1440, main", "1920×1080, right of
  main". Its stored identity is the monitor's device path (the same id `IDesktopWallpaper` uses), so
  it survives reboots and resolution changes.
- **A disconnected monitor:** its pieces move to the main monitor while it is away (same anchor), and
  return when it reconnects. Nothing is lost or rewritten.
- The anchor and size stay per piece (not per monitor): a piece on one monitor sits at its anchor
  there.

## Layout storage

- The layout string keeps its form, with an optional `@monitor` suffix per piece: `clock:tl@main`,
  `launcher:br@\\?\DISPLAY#...` (the id escaped so `,`, `:` and `@` can't break the list).
  No suffix = All monitors, so every saved layout reads exactly as before.
- Core `DeskArrangement` gains the monitor per placement; parsing, writing and the stack model per
  (monitor, anchor). Pure, tested, including old strings round-tripping unchanged.

## Where you set it

- **Settings ▸ Desk ▸ Where each piece sits:** a **Monitor** dropdown beside each piece's place (All
  monitors, Main monitor, then each connected monitor by name).
- **Arrange Desk:** each monitor shows only its pieces plus the All-monitors pieces. Dragging a
  single-monitor piece onto another monitor's surface moves it there. A piece's arrange chrome gains
  **Show on** (All monitors / This monitor). Done writes once; Cancel drops it, as today.

## The agent-designed Desk

- `DeskDesign.layout` pieces gain `monitor`: `"all"` | `"main"` | a monitor id from `get_desk`.
- `get_desk` lists `monitors`: `{ id, name, width, height, primary, left, top }`, and each wallpaper
  summary monitor carries its `id`, so Claude can design per monitor (e.g. the clock on the
  touchscreen's calm corner, the Launcher on the main screen).
- The validator refuses an unknown monitor id; the contrast rule judges each piece against its own
  monitor's anchor (All monitors = the worst of them, as today).
- Diff lines name the monitor ("Clock and date: moved to 1920×1080, right of main").

## Testing

- Core: layout parse/write with and without suffixes, escaping, old strings unchanged, stacks per
  (monitor, anchor), fallback of a missing monitor to main, DeskDesign monitor validation and Diff.
- WPF: the Monitor dropdown's automation names; Arrange's Show on control.
- Live on Neb (two monitors): clock on the 1920×1080 only, Launcher on the main only, Arrange drag
  across monitors, a design that splits pieces across monitors, then unplug or disable a monitor and
  confirm its pieces fall back to main and return.

## Out of scope

- Different anchors or sizes for the same piece on different monitors; more than one copy of a piece
  with different content.
