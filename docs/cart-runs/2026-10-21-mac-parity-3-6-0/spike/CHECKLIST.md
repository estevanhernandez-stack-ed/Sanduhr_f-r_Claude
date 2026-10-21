# Under-icons spike: Este's manual pass

Run `pwsh docs/cart-runs/2026-10-21-mac-parity-3-6-0/spike/run-spike.ps1` (3 minutes; Ctrl+C ends early). It puts a 360x120 magenta rounded piece reading "UNDER-ICONS SPIKE" at the top-left of the primary work area (where icons start), as a child of the wallpaper WorkerW, below the icons. A nearly invisible click layer (alpha 1/255, same rect) sits at the bottom of the normal band above the icons and logs every click. Pass `-X 3700 -Y 700` to move the piece to an empty spot, `-Mode ulw` or `-Mode plain` to try the other paint routes.

## Already measured on Neb (build 26220)

- Progman holds `SHELLDLL_DefView` over `WorkerW`; the piece's chain is piece -> WorkerW -> Progman.
- The click layer is directly above Progman in the top-level z-order, owned by Progman, and the same size as the piece.
- Hit test at the piece centre: `WindowFromPoint` returns the click layer. Outside it: `SysListView32` (the icons view).
- Pixels: a layered child painted with `SetLayeredWindowAttributes` + `WM_PAINT` (the default `-Mode attr`) showed on screen. `UpdateLayeredWindow` per-pixel alpha (`-Mode ulw`) and a plain child (`-Mode plain`) drew nothing.

## (a) Show the desktop your own way

1. Is the magenta piece visible on the wallpaper?
2. Do desktop icons draw over it (drag an icon, or run with `-X`/`-Y` over icons you have)?
3. Does a normal window cover it?
4. After Show desktop, is it still there?

## (b) Clicks

1. Left-click the piece. Expect a `CLICK LBUTTONDOWN` line in the log and on the console.
2. Right-click the piece. Expect `CLICK RBUTTONDOWN`. Note whether the desktop menu appears anyway (it should not).
3. Click empty desktop elsewhere: no log line. Right-click gives the desktop menu, drag gives the rubber band.
4. Click an icon that sits over the piece, and drag one across it. Note which one takes the click: the icon or the click layer. A click on the piece area is expected to go to the click layer, not the icon.
5. Does anything else about the desktop feel off (focus stealing, icon hover/tooltips lost over the piece area)?

## (c) Optional: Explorer restart

Run with `-SimulateRestart` first if you want the rebuild path without touching Explorer (already passed on Neb). For the real thing: Task Manager, Windows Explorer, Restart. Both windows should return; the log shows `TaskbarCreated received` then `rebuild ok`.

## Record

PASS or FAIL per item, anything surprising, and the log (`under-icons-run.log`, in this folder). The verdict goes into `windows-dotnet/docs/spec.md` as 5.0b after this pass.
