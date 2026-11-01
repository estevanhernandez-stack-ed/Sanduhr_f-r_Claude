# Drawer look spike verdict

Windows 11 Insider, build 26220.9836 (Dunder). Monitors: two, both 100% (96 dpi): `DISPLAY2` 3440x1440 primary
at 0,0, `DISPLAY1` 1920x1080 at 3440,0. Transparency effects on. Windhawk's `translucent-windows` mod is installed
but disabled (checked; it would otherwise inject backdrops). Date 2026-11-01, run unattended by the Cart spike
agent. Raw output lands in `out/probe.log` (ignored); the kept captures are in `shots/`.

## 1. Blur behind a Desk window: PASS via `SetWindowCompositionAttribute`, `AccentFlags = 0`

The probe window is the Desk's: `HwndSource`, `WS_POPUP`, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`,
`UsesPerPixelTransparency = true`, owner set to `GetShellWindow()` (logged: exstyle `8080080`, owner = shell
window). The panel is 360x330, a `#591E1E24` fill (35%), 12 px corners.

**Deviation, stated plainly:** no wallpaper was visible anywhere (both monitors carry a maximized app), and the
owner's windows were not moved. So each attempt ran topmost over an opaque stand-in for the wallpaper (random
colored blocks and black text, `shots/blur-0-background.png`), not at `HWND_BOTTOM` over the real wallpaper.
DWM samples what is behind the window either way; the bottom-of-z-order case over the real wallpaper is untested.

Metric: mean neighbour luminance difference inside the panel, before vs after (no blur predicts ~0.65, the
fill's alpha scaling).

| Attempt | Result | Ratio | Shot |
|---|---|---|---|
| none (control) | sharp, dimmed | 0.651 | `shots/blur-1-none.png` |
| (a) `DWMWA_SYSTEMBACKDROP_TYPE = 3` + `DwmExtendFrameIntoClientArea(-1)` | **FAIL**: hr 0, reads back 3, but draws a flat gray fill; nothing behind shows. The transient backdrop needs an active window, and a Desk window never activates | 0.000 | `shots/blur-2-sbt-transient.png` |
| (a) same, non-layered window (control) | FAIL: flat black | 0.000 | `shots/blur-7-sbt-nonlayered-control.png` |
| (b) `ACCENT_ENABLE_ACRYLICBLURBEHIND = 4`, `AccentFlags = 2`, `GradientColor = 0x01000000` | FAIL: flat near-black (with tint `0x40FFFFFF`: flat gray) | 0.036 | `shots/blur-3-acrylic-flags2.png` |
| (b) `ACCENT_ENABLE_ACRYLICBLURBEHIND = 4`, `AccentFlags = 0` | **PASS**: the blocks behind read as soft colour fields, text gone | 0.035 | `shots/blur-4-acrylic-flags0.jpg` |
| (b) `ACCENT_ENABLE_BLURBEHIND = 3`, `AccentFlags = 0` | **PASS**, looks the same as 4 here | 0.035 | `shots/blur-5-blurbehind-flags0.jpg` |
| (b) acrylic on a non-layered window (control) | FAIL: flat black | 0.000 | (log only) |

The Desk's layered window is the case that works; a non-layered WPF window shows black, not blur.

Shape of the blur (`shots/blur-corners-zoom.png`: background, plain, `SetWindowRgn`, corner preference):

- The blur fills the **whole window rectangle**, not the panel's alpha shape: the area outside the panel's
  rounded corner is blurred too. Any shadow margin around a drawer would blur as well, so a glass drawer's
  window must be exactly the panel (or the margin accepted).
- `SetWindowRgn(CreateRoundRectRgn)` has no effect (layered window).
- `DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND (2)` does clip the blur to DWM's round corner (about 8 px),
  `shots/blur-6-acrylic-flags0-roundcorner.jpg`. Match the panel's corner radius to it.

Risks for the build: `SetWindowCompositionAttribute` is undocumented user32 (`WCA_ACCENT_POLICY = 19`); guard it
and fall back to the plain Glass fill. Untested with Transparency effects off. Struct shapes used are `ACCENT_POLICY { int AccentState, AccentFlags,
GradientColor, AnimationId }` and `WINDOWCOMPOSITIONATTRIBDATA { int Attrib; IntPtr Data; int SizeOfData }` in
`Program.cs` (class `N`).

## 2. Reading icon positions: PASS

`DesktopIconsNative.cs` (the file Task 5 copies): `IShellWindows.FindWindowSW(VT_EMPTY, VT_EMPTY, SWC_DESKTOP,
SWFO_NEEDDISPATCH)` -> `IServiceProvider.QueryService(SID_STopLevelBrowser, IID_IShellBrowser)` ->
`QueryActiveShellView` -> `IFolderView`, on the STA main thread, out of process to Explorer. Every call returned
S_OK.

- `ItemCount(SVGIO_ALLVIEW)` = 49, view mode 1 (icon), `GetSpacing` = 76x98, auto-arrange off.
- Each item: `Item(i)` -> pidl, `GetItemPosition`, name via `SHGetNameFromIDList(SIGDN_NORMALDISPLAY)`.
- Check against UI Automation (the list view's own item boxes, screen pixels): **49/49 names match, and every
  one sits at the same offset**: box = `(pos.X - 14, pos.Y + 3)`, 76 wide, 70 or 85 tall (one or two label
  lines). 14 = (76 cell - 48 icon) / 2, so `GetItemPosition` is the icon image's top-left, not the cell's.
  Samples: Recycle Bin pos (53,428) box (39,431); Visual Studio Code pos (235,701) box (221,704); junk-3.txt pos
  (14,1289) box (0,1292); NVIDIA PAIR, on the **second monitor**, pos (3454,2) box (3440,5).
- **After an Explorer restart** (`taskkill /f /im explorer.exe`, `start explorer.exe`): new Progman, DefView and
  list view handles, same 49 items at identical positions, same offset. `GetDefaultSpacing` changed from 96x49
  to 96x16 across the restart: don't use it; `GetSpacing` held.

What could not be tested: auto-arrange on (toggling it would rearrange the owner's desktop); a second monitor at
a different scale (both are 100%); a monitor left of or above the primary.

Pixel check, and why it is indirect: no capture path shows the icons on 26220. `PrintWindow` of the list view,
DefView or Progman (flags 0, 1, 2) and a DWM thumbnail of Progman all return the wallpaper without icons
(Progman is `WS_EX_NOREDIRECTIONBITMAP`, DefView is a layered child), and the live screen is covered by
maximized apps. `shots/icons-uia-boxes-over-desktop.jpg` is the thumbnail with the reported positions (red) and
UIA boxes (cyan) drawn on it; the grid matches the layout the UIA tree reports, but the icons themselves are not
in the picture.

## Coordinate space

`GetItemPosition` is in the list view's client pixels. Here the list view, DefView and Progman all span the
virtual screen from (0,0) (5360x1440), so `MapWindowPoints(listView, NULL)` maps (0,0) to (0,0) and the client
coordinates equal screen coordinates. That is only because nothing sits left of or above the primary: with a
monitor at negative coordinates the client origin is the virtual screen's top-left. **Always map list view
client -> screen with `MapWindowPoints`**; it is free and correct in both cases. The positions are physical
pixels when the reader is per-monitor aware (this probe set PMv2 for the process; the Desk reads them inside
`DeskNative.PerMonitor`).
