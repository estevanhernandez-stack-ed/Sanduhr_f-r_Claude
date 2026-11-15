# Mac Desk pieces per monitor — design (2.44.0)

Mac parity for Windows 3.7.0's per-monitor Desk (`2026-10-21-desk-per-monitor-design.md`). Approved by the owner 2026-11-14.

## Where the Mac is today

- The Desk is **one window on one screen**: `DeskController.buildWindow()` covers `NSScreen.main`, the
  screen that holds the key window when the window is built (launch, a display change). It is not the
  menu bar screen by rule, so on a two-display Mac the Desk can land on either.
- Windows started from "every piece on every monitor" (mirrored). The Mac starts from "every piece on
  one screen". The default below follows from that.
- `NSScreen.main` appears 18 times in 10 files: the Desk window, the click layer, the arrange bar, the
  drawers, OLED care, the Dock follower, the camera badge and the notch glow. The island (notch wings,
  pill) is per screen by its own rules and stays as it is.
- The MCP refuses `monitor`, and `get_desk` has no `monitors`. The desk-setup skill (sanduhr-mods 0.2.0)
  routes a `get_desk` without `monitors` to its Mac branch.

## Behaviour

- Each placed piece has a **monitor**: **Main display** (the menu bar screen, `NSScreen.screens.first`),
  **All displays**, or **a specific display**.
- **Existing layouts read as Main display**, so nothing moves on update. This is the Mac's own default,
  the reverse of Windows, where no suffix means every monitor.
- A specific display is stored by its **display UUID** (`CGDisplayCreateUUIDFromDisplayID`), which
  survives reboots and resolution changes, unlike a `CGDirectDisplayID`. It is named by size and position,
  as on Windows: "3024×1964, main", "2560×1440, right of main".
- **A display that is disconnected:** its pieces draw on the main display at the same anchor until it
  comes back. Nothing is rewritten. This is Windows' rule.
- Anchor, size, tilt and style stay per piece, not per display.

## Layout storage

- The desk preference `layout` keeps its form and gains Windows' optional `@monitor` suffix:
  `clock:tl@all`, `claude:bl@<uuid>`. No suffix means Main display on the Mac. Old strings round-trip
  unchanged.
- Stacks group by (display, anchor), so two pieces at `tl` on different displays don't stack together.

## Drawing

- **One Desk window per display that has pieces to draw.** A display with no pieces gets no window and
  costs nothing. Each window's `DeskView` draws its own pieces and the All-displays pieces.
- **Per-window state:** `deskSize`, `topInset`, `notchRect` and the Dock clearance move from `DeskModel`
  to a per-display value. The model's data (usage, meetings, now playing, weather) stays shared, read once.
- **The click layer and arrange mode** run per window. Arrange shows the bar on the display under the
  pointer; dragging a piece onto another display's window moves it there. **Show on** (This display / All
  displays) is added to a piece's arrange chrome.
- **Kept on the main display:** Drawers, the companion's roam, the camera badge. Those are later items.
- **OLED care** drifts and dims every Desk window. **Idle CPU:** a second window must not double the
  breathing glass or the timelines. The bar is backlog #7 (under 1 percent), measured with two windows.

## Settings

- Settings ▸ Desk ▸ Layout: a **Display** picker beside each placed piece (Main display, All displays,
  then each connected display by name). It shows only when more than one display is connected, or when a
  piece is already placed on a display that is not connected (named "A display that is not connected (on
  the main display for now)").

## The agent-designed Desk

- `get_desk` gains `monitors`: `{id, name, width, height, primary, left, top}`, ids as UUID strings, and
  every placed piece reports its `monitor` (`"main"`, `"all"` or an id). The key names are Windows'.
- `propose_desk` accepts `layout.<piece>.monitor`, refusing an id that is not connected. The Python
  validator and `DeskDesign.swift` keep their shared fixtures in step (`Fixtures/desk-designs/`).
- **No wallpaper summary on the Mac yet.** The skill's Mac branch must switch on each key separately:
  `monitors` present means ask which screen; `wallpaper` absent still means ask about the wallpaper and
  quote no ratio. That is a sanduhr-mods 0.2.1 change with a Mac two-display eval fixture, merged on
  2.44.0's release day (`hold-for-release`).

## Testing

- Swift: layout parse and write with and without suffixes, the Main-display default, UUID round trip,
  stacks per (display, anchor), the fallback of a missing display, naming, design validation and the diff
  lines naming the display.
- Python: `get_desk` `monitors`, the `monitor` field accepted and an unknown id refused, the shared
  fixtures.
- Smoke: a new section for two displays.
- **Live:** needs a second display. This Mac has only its built-in screen now, so the live pass needs an
  external display plugged in (or the old Mac with one). Then: a piece per display, Arrange dragging
  across, a design that splits pieces, unplugging and replugging, sleep and wake, and the idle-CPU
  measurement with two windows.

## Out of scope

- A wallpaper summary on the Mac (its own item; it is what would let the skill check 3:1 on a Mac).
- Different anchors per display for the same piece; two copies of one piece.
- Drawers, the companion's roam and the island on non-main displays.

## Answered by the owner (2026-11-14)

1. Existing layouts default to **Main display**: yes.
2. "Main" is **the menu bar display**: yes.
3. The owner plugs in an external display for the live pass.
4. Run it **automated**: a Cart `/take` run on a local branch in a worktree, nothing pushed until the owner says.
