# Run Report: drawers-look-4-5

12 merged, 0 held, 0 parked, 0 stopped.

Drawers for 4.5.0 now have four looks: Solid, Glass with real blur, Outline and Bare. They have an opacity setting and a per-drawer look. Drawers snap to each other, to Desk pieces and to the screen edges, keep clear of the desktop icons, and line up with Tidy, which has Undo. An AI chat can set the look, opacity and arrangement. Every item had a fresh implementer and a fresh reviewer, except two the orchestrator reviewed directly (D4, D11). The whole-branch review found one upgrade bug and four minors, all fixed in item 12 and re-reviewed.

## Ready to push

- **Integration branch:** `cart/drawers-look-4-5`. It is built on `cart/setup-4-5` with `docs/setup-spec` merged in (`5da9c567`). This run added 32 commits across 71 files, +5478 / -119.
- **Checks on the integration tip `6c1d0a5f`:**
  - `dotnet build windows-dotnet/Sanduhr.slnx -c Release`: 0 errors.
  - `dotnet test` in Release: Core 5048/5048, App 1426/1426, Mcp 300/300.
  - TrustBoundaryTests are green and no MCP file changed.
  - Harness shots of the four looks over busy, plain and bright wallpapers in obsidian and aurora: 24 shots, checked by eye, all readable. They are untracked in the scratchpad.
- **Main has moved:** `origin/main` gained 27 commits (the Mac 2.39.0 release, `mac/` and docs). Merge it in before the PR.
- **Close-out gates:** vibe-test and vibe-sec were not run. The full Release suites stand in (D1, as the previous run did).

This branch carries the setup work too (`cart/setup-4-5`), so one PR ships 4.5.0's code:

```bash
git push -u origin cart/drawers-look-4-5
gh pr create --base main --head cart/drawers-look-4-5 --title "Windows 4.5.0: Set up Sanduhr, and drawer looks and placement"
```

Pushing waits for your say-so.

## Waiting on you

Nothing is held. Before 4.5.0, the live checks below need you at the PC. The run's tests can't see the Desk.

1. **Glass over the real wallpaper.** Try it once with Windows Transparency effects on and once with them off. The spike verified blur only over a test pattern (D3).
2. **Each look over the real wallpaper.** Outline and Bare should take clicks on their empty parts, and the hover outline should show on Bare.
3. **Dragging.** Snap near the clock and near another drawer; hold Alt to drop a drawer freely; drag across your two monitors.
4. **Keep clear.** With icons on the left, drop a drawer on them: it should slide off. Then turn Keep clear off.
5. **Tidy.** Run it in all four corners and Undo once. Run it again from a drawer's right-click menu.
6. **Upgrade.** Copy a 4.0.1 profile that has drawers, launch on it, and check the drawers sit where they did.
7. **Explorer restart** while drawers are up.
8. **Still pending from the setup run:** a no-account first run (her PC or a clean Windows user) and a real AI chat paste, now including look and arrange.

**Release steps:**

- Bump the version to 4.5.0: the csproj trio and the manifest.
- Write the reviewer letter's paragraphs on setup, the AI chat and drawers.
- Add a release-notes line: a 4.0.x build that saves drawers after a 4.5 build resets the new look settings to their defaults. Nothing breaks.

## Parked

None.

## Stopped

None.

## Decisions to skim

The full ledger is `decisions.md`, D1 to D12. The highest stakes:

- **D3:** Glass blur goes through `SetWindowCompositionAttribute` (acrylic, flags 0). The documented backdrop drew grey on a Desk window. A Glass drawer's window has no padding and round corners, and the blur falls back to a tinted plate if Windows refuses it.
- **D7:** The icon reader pings Explorer, giving up after 250 ms, so a hung Explorer can't freeze the Desk. Icons are read before the render touches any state, and Alt is read with `GetAsyncKeyState`.
- **D11:** Outline and Bare labels always get an opposite-tone shadow, so they stay readable over busy wallpapers.
- **D12:** Arrival places are saved once, with the old rule for an upgrader's first save. An unwritable settings file gets one try per session, so the Desk never loops.
- **D8 and D10:** Undo, for Tidy and for chat settings, restores only what it changed, and skips anything you changed since.

## Left untouched

Uncommitted files in your tree at run start: `.playwright-mcp/`, `dist/`, and the previous run's folder `docs/cart-runs/2026-10-31-setup-4-5/`. This run's folder is uncommitted too.
