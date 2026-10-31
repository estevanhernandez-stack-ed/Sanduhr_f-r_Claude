# Drawer-island spike verdict

Windows 11 25H2, build 26220 (Neb). Monitors: two, 3440x1440 (main) and 1920x1080 to its right. Stardock
Fences running, with fences under the island. Date: 2026-10-30. Run by Este; four builds (v1 is the plan's
code, v2 to v4 are the fixes below). Quotes are from `drawer-island.log`.

| # | Check | Result | Evidence (log line) |
|---|---|---|---|
| 1 | OLE drop from Explorer | PASS | `drop: moved rororoblox-20261023.log in`; again after the restart and on the second monitor |
| 2 | OLE drag out, accepted as a move | PASS | `drag out: rororoblox-20261023.log -> Move`; again on the second monitor |
| 3 | Explorer's context menu | PASS with v2 | `menu: IContextMenu3 yes`, `menu: beta.txt -> command 19` (Properties opened). v1 logged `(dismissed)` for every pick |
| 4 | Focus on click, given back on click away | PASS with v2 and v4 | `activate: SetForegroundWindow True, foreground now True`, `key: F2 (focus held: True)`, `activate: gave back, sunk to bottom`. v1: `focus: got (foreground False)`, no keys |
| 5 | Survives an Explorer restart | PASS | `explorer: restarted, re-owning`, `own: Progman 0x503E6`; drop and menu worked after |
| + | Second monitor (different size) | PASS | `move: at 3731,95`, then a drop in and a drag out there |

Verdict: approach 1 holds (a window owned by `Progman` at the bottom of the z-order), with four changes plan 2 must carry:

1. **Activate on click.** `WS_EX_NOACTIVATE` keeps keys away for good. Clear it on mouse down,
   `SetForegroundWindow`, focus the list; put it back on `Deactivated`. Keys arrive (arrows moved the
   selection; F2 reached the window, and plan 2 adds inline rename).
2. **Pin the z-order.** Activation raises the window after any `SetWindowPos` made at click time (v3 still
   came in front). Rewriting `WINDOWPOS.hwndInsertAfter` to `HWND_BOTTOM` in `WM_WINDOWPOSCHANGING` held it
   under the apps (v4). The App's Desk host should already do the same; check it before adding a second pin.
3. **Shell menu rules.** `SetForegroundWindow(owner)` before `TrackPopupMenuEx` and `WM_NULL` after, or every
   pick is dismissed; forward `WM_INITMENUPOPUP`, `WM_DRAWITEM`, `WM_MEASUREITEM`, `WM_MENUCHAR` to
   `IContextMenu3.HandleMenuMsg2` for Open with and Send to.
4. **Dark menus.** uxtheme ordinal 135 `SetPreferredAppMode(1)` plus 136 `FlushMenuThemes` at startup made
   the menu dark. Undocumented; guard it and fall back to a light menu. `AllowDarkModeForWindow` (133)
   returned False and was not needed. The Properties dialog stays light: that is Windows', not ours.

Also for plan 2:

- A drag that lands back on its own drawer reported `drag out: beta.txt -> Copy, Move` after the drop handler
  skipped it (`beta.txt exists, skipped`). Ignore drops from the drawer onto itself and never act on the
  result as a move.
- Fences: an island and a fence share the desktop layer and either can sit above the other. Nothing broke.
  Whether a drawer avoids fences, or ignores them, is a ruling for plan 2.
- The window moves by its title only; the real drawer needs its own move and size handles.
