# Run List: mac-parity-3-6-0

`src/` and `tests/` paths are under `windows-dotnet/`; every other path is from the repo root. Every item: conventional commit on `feat/mac-parity`, `dotnet test windows-dotnet` green, quit `Sanduhr.exe` before any publish, never `sed -i` a tracked file (CRLF tree).

- [x] **1. Spike: the Desk drawn behind the desktop icons, with clicks through a layer above** (verdict in `windows-dotnet/docs/spec.md > 5.0b`; item 6 dropped by Este 2026-10-21)
  Spec ref: `windows-dotnet/docs/spec.md > 5.0 The WorkerW spike` (criteria (a) and (b)); `CHANGELOG.md > v2.27.0-mac "The Desk can sit under your files"`; `mac/Sources/Sanduhr/Desk/DeskClickLayer.swift`
  What to build: A throwaway spike (not wired into the app) on Neb: a layered window parented to the `WorkerW` child of `Progman` (the wallpaper host below `SHELLDLL_DefView` on 24H2+) that draws a visible test piece, plus a V3-style owned tool window above the icons that takes clicks only over the piece's rect. Measure (a) the piece's pixels are visible on the real desktop (screenshot, not PrintWindow), icons draw over it, normal windows cover it; (b) clicks on the piece reach the click layer and clicks elsewhere reach the desktop (icon selection, desktop menu, rubber band); (c) it survives an Explorer restart by rebuild. Size M.
  Acceptance: Done when `windows-dotnet/docs/spec.md` gains a "5.0b Under-icons spike" section with a PASS/FAIL per criterion and evidence (screenshots under `docs/cart-runs/2026-10-21-mac-parity-3-6-0/`), and the spike code is removed or kept only under a `spikes/` path outside the build.
  Verify: Screenshot of the desktop showing the test piece behind an icon; a logged click reaching the click layer; `dotnet test windows-dotnet` still green. STOP for Este with the verdict.
  Stop class: go

- [x] **2. The tour knows the Desk: a Desk card and a Desk-only tour after Just the Desk**
  Spec ref: `CHANGELOG.md > v2.29.0-mac "A tour for the Desk on its own"`; `docs/welcome-tour-spec.md`; `mac/Sources/Sanduhr/Models/WelcomeTour.swift`, `WelcomeTourSteps.swift`; `src/Sanduhr.Core/WelcomeTour.cs:36` (stale "The Desk is not on Windows yet")
  What to build: Add the Desk card to the full tour (Windows equivalent of the Mac's step: the desktop, with a Show me that opens Settings ▸ Desk). Add the Desk-only variant: chosen Just the Desk opens a short tour a moment later (the desktop, the tray, Sign in any time whose button opens Accounts); Take the Tour… shows the Desk-only steps while Just the Desk is on. Fix the stale comment. Trademark notice wherever the card names Claude. Size S.
  Acceptance: Done when the full tour includes the Desk card, a Just the Desk first run opens the Desk-only tour, Take the Tour… picks the variant from the Just the Desk state, and Core tests cover step lists for both variants.
  Verify: `dotnet test windows-dotnet` green with new `WelcomeTour` tests; run the app with `--demo` and open Take the Tour… in both states.
  Stop class: go

- [ ] **3. A plate behind every Desk piece**
  Spec ref: `CHANGELOG.md > v2.27.0-mac "A plate behind every Desk piece"`; `src/Sanduhr.App/Desk/DeskClicks.cs:47` (hit-test brush only); Mac Desk Look settings
  What to build: Desk Look gains **Plate behind the text**: a soft dark rounded plate behind each piece. On for a new install, untouched (off) for an existing one, via a settings key that defaults by install age the way other Desk keys do. Keep the hit-test brush behavior unchanged. Size S.
  Acceptance: Done when the switch exists in Settings ▸ Desk Look, the plate renders behind each piece in light and dark ink, the new-vs-existing default holds (test), and settings round-trip.
  Verify: `dotnet test windows-dotnet` green; the Desk render harness (or the app in `--demo`) shows the plate on and off.
  Stop class: go

- [ ] **4. Quit from Settings, and hide/show pieces while arranging the Desk**
  Spec ref: `CHANGELOG.md > v2.19.0-mac "Quit from any Settings page"`; `CHANGELOG.md > v2.27.0-mac "Arrange Desk hides and shows pieces"`; `src/Sanduhr.App/App.xaml.cs:408` (tray Quit); `src/Sanduhr.Core/DeskArrangement.cs:188`; `src/Sanduhr.App/Desk/DeskArrangeBar.xaml`; `mac/Sources/Sanduhr/Desk/DeskArrange.swift`
  What to build: (a) **Quit Sanduhr** at the foot of the Settings sidebar, same path as the tray Quit, with an accessible name. (b) In Arrange Desk, each piece gets Hide while arranging, and a Hidden strip brings a piece back; Done writes the layout once, Cancel drops it. Size S + S-M.
  Acceptance: Done when Settings has a working Quit at the sidebar foot (accessible name set), arranging can hide and restore pieces, Done persists hidden pieces and Cancel discards them (Core tests on the arrangement draft), and keyboard navigation reaches the new controls.
  Verify: `dotnet test windows-dotnet` green; app run: hide a piece, Cancel (piece back), hide, Done (piece gone, layout saved), Quit from Settings exits.
  Stop class: go

- [ ] **5. The statusline names its folder**
  Spec ref: `CHANGELOG.md > v2.19.0-mac "The statusline names the account"` (behavior), decision `jKcxlY6KNcch787LzH5y` (Windows design differs: no label on disk); `src/Sanduhr.Core/StatuslineScript.cs:123`; `src/Sanduhr.Core/SeatFiles.cs:14`; `docs/PRIVACY.md`
  What to build: A new **Account** segment in the statusline script, computed at render time: `env.SANDUHR_LABEL` from the session's environment when set, else the basename of `CLAUDE_CONFIG_DIR` with a leading `.claude-` stripped (`.claude-personal` → `personal`); plain `.claude` or no `CLAUDE_CONFIG_DIR` shows no label. It leads the line (`personal | 5h 4% | …`), is a selectable segment like the others, and a combined statusline whose segments were picked before keeps exactly those. `seats.json` and snapshots stay label-free. PRIVACY.md gains one sentence: the statusline shows the folder's name or the label you set in that folder's settings. Size S-M.
  Acceptance: Done when the script renders the label per the rules (Core tests over the generated script's logic or a pwsh run in tests), existing picked-segment installs are unchanged, nothing new is written to `~\.sanduhr`, and PRIVACY.md is updated.
  Verify: `dotnet test windows-dotnet` green; run the generated `sanduhr-statusline.ps1` with `CLAUDE_CONFIG_DIR` set to `.claude-personal`, to `.claude`, and with `SANDUHR_LABEL=Work`, and see `personal | …`, no label, `Work | …`.
  Stop class: go

- [x] ~~**6. Keep the Desk under desktop icons (only if item 1 passes)**~~ DROPPED (Este, 2026-10-21): needs an opaque second paint path for a narrow gain; see spec 5.0b.
  Spec ref: item 1 verdict; `CHANGELOG.md > v2.27.0-mac`; `src/Sanduhr.App/Desk/DesktopLayerHost.cs`; `mac/Sources/Sanduhr/Desk/DeskClickLayer.swift`, `DeskController.swift`
  What to build: Settings ▸ Desk ▸ Clicks gains **Keep the Desk under desktop icons**, off by default. On: the Desk's visuals move to the spike's behind-icons host; meeting rows, watchers, now playing and the account name keep their clicks through a transparent click layer at the current V3 level; the clock's, message's and meters' menus wait until the option is off. Rebuilds on `TaskbarCreated`, display and work-area changes as today. Size M-L.
  Acceptance: Done when the option toggles live without restart, icons draw over the Desk with it on, clickable pieces still click, desktop clicks elsewhere still reach the desktop, Explorer restart recovers, and off restores today's behavior exactly.
  Verify: `dotnet test windows-dotnet` green; live run on Neb with screenshots on and off; `taskkill /f /im explorer.exe` and restart, Desk returns.
  Stop class: go (dropped if item 1 fails)

- [ ] **7. Watchers and Desk-message suggestions over MCP (one trust-boundary change)**
  Spec ref: `windows-dotnet/docs/spec.md:362` ("Held for Este"); `src/Sanduhr.App/Desk/DeskHandoffSeams.cs:20,70` (`Enabled = false`); `src/Sanduhr.Mcp/ToolCatalog.cs`, `ToolLogic.cs`; `tests/*/TrustBoundaryTests.cs`; `mac/integrations/sanduhr_mcp.py` (`watch_start`, `watch_update`, `watch_end`, `propose_desk_messages`, `get_desk_messages`); `docs/PRIVACY.md`
  What to build: Add the five MCP tools with the Mac's argument shapes and limits, writing request files into `%USERPROFILE%\.sanduhr` the way `propose_theme` does (temp-then-rename, size caps, no network). Turn both seams' `Enabled` on. Widen the `Sanduhr.Mcp.csproj` source-link allowlist only by the Core files these tools need, and update `TrustBoundaryTests` to the new allowlist. PRIVACY.md rows for the request and result files. smoke-mcp gains the new tools (a rejected bad request each). Size S-M.
  Acceptance: Done when the five tools appear in `tools/list`, a watch shows on the Desk's watchers piece and fades on end, a proposed message reaches Settings ▸ Message for Add/Review/Dismiss, the MCP stays network-free with no ProjectReference/PackageReference, TrustBoundaryTests passes on the new allowlist, and PRIVACY.md covers the files.
  Verify: `dotnet test windows-dotnet` green; `pwsh windows-dotnet/scripts/smoke-mcp.ps1` passes against a fresh publish; live: `watch_start` from a Claude Code session shows on the Desk. STOP for Este before committing the widened allowlist.
  Stop class: hold

## Not this run

- Meetings on the Desk + Join shortcut (`CHANGELOG.md > v2.1.0-mac`): needs the Appointments manifest capability; Store-review risk deserves its own cycle.
- Mod authoring and sharing (`CHANGELOG.md > v2.20.0-mac..v2.26.0-mac`): MCP write tools that run `git` and `claude`; a trust-boundary cycle of its own.
- Mods & Config Catalog / By Seat / Compare, Mod Studio, statusline styling and preview per folder (`v2.18-v2.20-mac`): medium-to-large; next parity slice.
