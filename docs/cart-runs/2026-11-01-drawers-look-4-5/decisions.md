<!-- The ledger. Append only. -->

# Decisions: drawers-look-4-5

## D1
- **Decision:** The run's base is `cart/setup-4-5` with `docs/setup-spec` merged (`5da9c567`), not `origin/main`.
- **Why:** 4.5.0 ships setup, drawers and this together; the run builds on setup's branch. `origin/main` is 27 commits ahead with the Mac 2.39.0 release (mac/ and docs only); the PR will merge main in.
- **Alternative not taken:** `--merge origin/main` now.
- **Item:** run
- **How to reverse:** Merge `origin/main` into `cart/drawers-look-4-5` before the PR.

## D2
- **Decision:** Every item is `go`; subagent-driven as the previous run (fresh implementer, fresh reviewer, whole-branch review at close).
- **Why:** Nothing touches money, auth, permissions or destructive data; the icon reader is read-only.
- **Alternative not taken:** `hold` on item 6 (Desk interop).
- **Item:** all
- **How to reverse:** Review item 6's commits before pushing.

## D3
- **Decision:** The spike (db1d4d3b) passed both questions, so the run takes them as the plan said. Glass blurs through `SetWindowCompositionAttribute` (accent 4 acrylic, or 3, with AccentFlags 0); the documented `DWMSBT_TRANSIENTWINDOW` draws flat grey on a Desk window, because the window never activates. Icons are read through IFolderView: 49 of 49 matched UI Automation's positions and survived an Explorer restart. Positions are list-view client pixels, so always map them with `MapWindowPoints(listView, NULL)`, inside `DeskNative.PerMonitor`. Constraints for item 5: the blur fills the whole window rectangle (SetWindowRgn has no effect on a layered window), so a Glass drawer's window must be the panel's own size with no 16 px pad, and its corners set with `DWMWA_WINDOW_CORNER_PREFERENCE = ROUND`. The undocumented call must be guarded and fall back to the tinted plate. Untested: the blur over the real wallpaper (apps covered it), Transparency effects off, auto-arrange on, a second monitor at another scale.
- **Why:** The plan has the spike settle these without the owner.
- **Alternative not taken:** Tinted Glass only.
- **Item:** 1
- **How to reverse:** Drop the DrawerBackdrop call; Glass falls back to its tinted plate.

## D4
- **Decision:** Item 3 (snapping, pure Core, the plan's code taken verbatim) merged on the orchestrator's review, not a separate reviewer agent. The plan's test arithmetic was checked when the plan was written. Accepted behaviour: each axis snaps on its own, so a drawer can align with another drawer's edge across the screen, as alignment guides do.
- **Why:** No change beyond plan code that was already checked. The App items, where integration risk lives, keep fresh reviewers.
- **Alternative not taken:** A reviewer agent for item 3.
- **Item:** 3
- **How to reverse:** The whole-branch review covers it.

## D5
- **Decision:** Item 4 merged after two review rounds. Its own deviations from the plan's code were accepted (five plan bugs: null tidy, the hit test, the row wrap, the guard, Keep in NearestFree). The review fixes: a left-out drawer no longer strands later ones (the line state is restored), NearestFree's candidates follow Keep's on-screen rule and cover L-shaped icon areas (a cross product of edges), unfit arrivals cascade, rows wrap to an obstacle's edge, and a bottom corner with no height reads as the top corner on the same side. Deferred: a worst case of about 190 ms for 300 scattered icons, once per drag end; and a cascade can land on a placed drawer once the screen is full.
- **Why:** The fixes cover Review Focus 1 and 2, the riskiest placement inputs.
- **Alternative not taken:** A cap on the icon count.
- **Item:** 4
- **How to reverse:** None needed.

## D6
- **Decision:** Item 5 merged after 6facffc3. Accepted deviations: the Glass blur tint is near-clear (the spike only verified flags 0 with a near-clear tint), and the panel's brush does the tint. Drawers no longer take the theme's plate opacity; the drawer opacity setting replaces it, and the effective value was always 55 anyway. Outline and Bare both shadow the title. Bare keeps the standard title bar height so every look is the same size. Fixed before merge: every look is identical in outer size, so upgraders see no 2 px shift; the hit fill is 0.02, which survives OLED dimming; Glass goes square-cornered when Windows 10 can't round the window; a refused blur isn't retried each redraw.
- **Why:** A drawer that changes size on upgrade, a click that falls through, or blur that spills past round corners would all be visible on someone's desktop.
- **Alternative not taken:** A full-alpha tint under flags 0 (unverified).
- **Item:** 5
- **How to reverse:** Change `NearClearTint` in `DeskController.WithDrawers`.

## D7
- **Decision:** Item 6 went back for three Important findings. (1) A hung Explorer could freeze the Desk on a drop, because outgoing STA COM has no timeout; a 250 ms `SendMessageTimeout` ping to the icons host now gates the read, and the fallback columns cover a refusal. (2) The COM read inside `RenderOnce` could re-enter the controller through COM's message pump; the icon cache is warmed before render touches state, the screens list is held in a local, and a dropped render is re-queued. (3) Alt read through `Keyboard.Modifiers` may be stale on a Desk window that isn't active, so it now uses `GetAsyncKeyState(VK_MENU)`, latched from the last move. Accepted from item 6: icon membership by screen bounds (changed to the screen holding the icon's centre), the icon cache cleared on every drop, and conservative icon cells (the full cell from GetSpacing).
- **Why:** A desktop freeze or a silently dropped render would be the worst failures on this path.
- **Alternative not taken:** Reading icons on a background STA thread (more machinery than a ping).
- **Item:** 6
- **How to reverse:** Drop the ping in `DesktopIcons`.

## D8
- **Decision:** Item 7 (Tidy with Undo) merged after 22d1e6a0. Undo restores only the drawers Tidy wrote, from their drawn place (a drawer that had no saved place included), and skips any drawer the user dragged or removed since. A sort started while the Tidy bar is up replaces the bar, matching today's file-sort result bar. Tidy with drawers off says so instead of "already lined up".
- **Why:** Before the fix, Undo could revert a later drag, resurrect a removed drawer, or put a never-placed drawer in the new corner.
- **Alternative not taken:** Restoring the old Tidy corner on Undo, which would lose the user's new choice.
- **Item:** 7
- **How to reverse:** None needed.

## D9
- **Decision:** Item 8 merged after 2dcf1c56. "Put drawers back top right" is gone; Tidy does the job, and nothing else referenced it. The opacity slider saves once after a 200 ms settle, the Desk page's pattern. A Tidy message shows by the Tidy row. A drawer's menu gains a Look submenu (Same as the others, Solid, Glass, Outline, Bare) and Tidy drawers, which tidies that drawer's monitor.
- **Why:** A save per slider tick rebuilt the Desk about 80 times in one drag.
- **Alternative not taken:** Keeping Put back beside Tidy.
- **Item:** 8
- **How to reverse:** None needed.

## D10
- **Decision:** Item 9 merged. An AI chat can set the look, opacity and arrange. Undo restores places (only what Tidy wrote) and the look, opacity and remembered Tidy, each only if the user hasn't changed it since. An arrange that moves nothing and saves no look shows a plain line, with no bar. The prompt says to leave `rules` out to keep the user's rules; the orchestrator corrected it from "leave drawers out", which was no longer accurate. Noted: 13 focus and keyboard App tests failed in full-suite runs on both this commit and its base, and passed in isolation. That fits foreground interference while the owner uses the PC; they are re-checked at close.
- **Why:** A user's own change after applying must survive Undo, the same rule as places.
- **Alternative not taken:** Undo restoring the look unconditionally.
- **Item:** 9
- **How to reverse:** None needed.

## D11
- **Decision:** The harness shots found a real readability bug. Outline and Bare labels vanished over the light stripes of a busy wallpaper, because chips switch on from the region's average luminance, and a striped wallpaper averages dark. The fix (163fe7a3) does what Windows does for desktop icon labels: Outline and Bare labels and title always carry a strong drop shadow in the opposite tone to the ink. Chips still cover failed contrast. The harness now waits for icons to load and renders busy (light and dark stripes), plain and bright wallpapers: 24 shots, all readable.
- **Why:** Spec section 1 promises readable on any wallpaper, and the average-luminance check alone could not keep that promise.
- **Alternative not taken:** A variance measure of the wallpaper region.
- **Item:** 10
- **How to reverse:** Drop `LabelShadow` in DrawerLook.

## D12
- **Decision:** The whole-branch review returned FIX FIRST on one Important finding. Drawers with no saved place re-arrive on every render through the new Tidy and icon-aware rule, so an upgrader's unplaced drawers can move (a third open drawer on 1080p wraps to a second column), and later icon changes move them (against spec "icons that move later do not push drawers"). Item 12 persists arrival places once: the first save for a pre-4.5 settings file uses the legacy column rule, later arrivals use Tidy's slots, and every drawer is saved once placed. It also folds in M1 (Tidy and chat may replace a non-review result bar), M2 (no icon COM reads while drawers are off), M4 (the opacity slider shows when any effective look has a fill) and M5 (a chat look is saved even when drawers are off, and the line says so). Left: M6 (a live snap at a monitor edge, for the manual two-monitor check) and M7 (a chat arrange that moves nothing still remembers its corner).
- **Why:** The spec promises upgraders see no change, and an unsaved arrival recomputed every render can't keep that promise.
- **Alternative not taken:** An in-memory per-session arrival cache.
- **Item:** 12
- **How to reverse:** Revert item 12's merge.
