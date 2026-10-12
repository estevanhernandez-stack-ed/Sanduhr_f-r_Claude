# Sanduhr for Windows: Mac parity

## Idea
The WPF app (3.4.3, in Store certification) catches up to the Mac (2.16.0 on main) in seven stages, each a releasable increment that merges to main on its own, so Este can cut a Windows release whenever a stage lands. The stages are ordered to snowball: the What's New cards come first because they announce every later release, the tour reuses those cards, Settings v2 gives the later pages a home, and the Desk builds on all of it.

## Who It's For
Windows Sanduhr users, who today get a widget that reads well but stops there: no word about what changed after an update, no first-run guide, one tray readout, temporary limits that never go away, a burn total that silently drops any Claude Code home outside `.claude` and `.claude-personal`, and none of the Desk that makes the Mac the reason to pick Sanduhr over a Rainmeter skin. Also Este, who runs both apps and wants one product across two platforms rather than a Mac app with a Windows cousin.

## Inspiration & References
- **The Mac app is the reference**, 1:1 in behavior: `mac/Sources/Sanduhr/` (Models, Services, Desk, Views) and its tests. Where Windows has an OS-native route the plan names it (`docs/windows-port-plan.md`: WinRT media sessions for now playing, `Progman`/`WorkerW` for the desktop layer, `SPI_GETWORKAREA` and `SHAppBarMessage` for taskbar clearance, `RegisterHotKey` for shortcuts).
- **Shared specs** written for both apps: `docs/welcome-tour-spec.md`, `docs/settings-v2-spec.md`, `docs/mods-and-config-spec.md`, `docs/BACKLOG.md` #3 and #4.
- **Rainmeter**: the desktop-layer window technique the Desk on Windows borrows (a borderless window parented into the desktop's `WorkerW`, re-parented when Explorer restarts). https://docs.rainmeter.net/
- **Windows media sessions**: `GlobalSystemMediaTransportControlsSessionManager`, public and permission-free. https://learn.microsoft.com/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager
- **RegisterHotKey**: a combination another app already holds fails with `ERROR_HOTKEY_ALREADY_REGISTERED`, which is the only conflict check Windows offers. https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-registerhotkey

Design energy: the Windows app's own look (glass widget, Mica, themed dialogs, the tool strip, the theme palette), carrying the Mac's layouts and copy. Copy follows the Mac's words where the feature is the same, so the guide and the tour read the same on both.

## Goals
- Every stage releasable on its own and merged through a PR with 0 warnings, green tests and CI, and a clean independent review.
- No regressions to the 3.4.x contracts: the Velopack packId, the vault's location, Credential Manager slots, the MSIX AppData opt-out, the MCP's network-free source-link allowlist.
- The 10.1.4.4 bar on every surface a Store reviewer sees: the trademark disclaimer wherever Claude is named, the unique-value features live, tool-strip navigation with accessible names, light-mode-legible dialogs.
- One vocabulary with the Mac: the same names for the same controls, so the shared guide, the tour and What's New hold on both.

## What "Done" Looks Like

| Stage | Ships | Mac reference | Port-plan IDs |
|---|---|---|---|
| **S1. What's New and quick wins** | What's New after an update, from a Windows card table with Show me routing; global shortcuts with a recorder and a RegisterHotKey-failure check; Claude Code home discovery for every `~/.claude*` folder on the read paths; hide temporary limits; tray readout modes; the steady-sparkline level line | WhatsNew*, HotKeys, HotKeyCombo, ShortcutRecorder, ClaudeCodeFolders, LimitLifetime, LimitMenu, MeterVisibility, MenuBarMode, SparklineView | W1, W2, W3, W5, W7 |
| **S2. Welcome tour and a clearer first run** | The tour on S1's cards (steps hidden when their feature is missing), the signed-out widget saying "Not signed in", Not Now on the sign-in step, Highlights on a first install, demo mode for screenshots and previews | WelcomeTour*, OnboardingSheet, SignedOut, DemoMode | W1 (tour half) |
| **S3. Settings v2** | Sidebar groups, one home and one name per control (a names table), search with synonyms, deep links to sections, live previews, the settings migrations that keep every choice, Reset Sanduhr… | SettingsNames, SettingsIndex, SettingsReach, SettingsMigrations, Reset*, SettingsPreview* | none (shared spec) |
| **S4. The Claude Code side** | Mods & Config (read first, edits through receipts), Sanduhr's own meters mod above the prompt, statusline combine with segments and looks, the "Claude needs you" glow hooks, plus follow the account in use and per-seat meters | ModInventory, ModSwitch, ModCheck, JSONEdit, ReceiptLog, BandFile, StatuslineCombine/Chips/Look, ClaudeCodeGlow, IntegrationInstaller, AccountFollow*, Seats, SeatMeters, SeatSnapshots | W4, W15 |
| **S5. Desk foundation** | A desktop-layer window per monitor (WorkerW spike first), clock and date, meters and the Claude line, taskbar clearance, EsteFont built in, the eight-place layout and its map | Desk/DeskView, DeskModel, DeskHitTest, DeskMeters, DockClearance, DeskArrangement, DeskLayoutMap, BundledFonts | W6, W9, W10, W14 (first half) |
| **S6. Desk content** | Messages with effects, the message editor and special days, get/propose Desk messages through the MCP handoff, now playing (widget first, then a Desk line), meetings with a join link, watchers and their MCP tools | Message*, MessageEditor*, MessageProposal, DeskMessageHandoff, NowPlaying*, DeskModel.refreshEvents, Watchers, WatcherStore | W8, W11, W12, W13 |
| **S7. Desk polish** | Arrange by dragging, Desk clicks, sign-in reminders on the Desk, Just the Desk (Sanduhr without a Claude account), a spike on camera and mic indicators | DeskArrange*, DeskHitTest, SignedOut, JustTheDesk, AVIndicators | W14 (second half) |

**Reordering against the brief, and why:**
- **Follow the account in use (W4) and per-seat meters (Mac 2.16) join S4.** The brief's stages had no home for either. Both sit on S1's folder discovery and on the Claude Code folder list S4 builds, and per-seat meters change what the statusline, the meters mod and the MCP answer, which is S4's surface.
- **Demo mode (Mac 2.16) joins S2.** The tour's previews and the Store screenshots both need sample numbers, and the Mac built demo mode for exactly that.
- **Now playing starts in the widget (W8) inside S6**, not only as a Desk line: the port plan ships it in the widget first because Windows has a public API for it. It stays in S6 because the Desk line and the widget share the tracker.
- **The watcher MCP tools (`watch_start`, `watch_update`, `watch_end`) ride with watchers in S6**, and the meters-mod band rows for watchers wait for S6 too.
- **The settings migrations ride with S3**: the restructure moves controls, and an existing install must keep every choice.

## What's Explicitly Cut
- **The notch, its wings and the camera fill light**: Mac hardware. The Desk and the top-center glow replace them.
- **Reading other apps' menus for shortcut conflicts (Mac 2.14)**: it needs the macOS Accessibility API. Windows gets the RegisterHotKey-failure check, which is what the OS offers.
- **Song looks (`propose_now_playing_looks`, Mac 2.10)**: cut from this cycle. It needs now playing in place and adds an MCP tool; a later cycle can take it.
- **Sparkle and Dock specifics**: Velopack and the Store already update Windows.
- **Agent-styled text across Sanduhr (`{font:}` tags, theme title styles)**: only what S6's message effects need; the rest waits.
- **Anything release-shaped**: version bumps, tags, packages, Partner Center, Store copy and the store-listing-console. Este cuts every release; the 3.4.3 submission in certification is untouched.

## Escalations: decided by Este, not by the build
- **The MCP's root set (S1, S4).** `Sanduhr.Mcp/McpConfig.cs` reads a closed list of two home names behind per-root consent ("no free-form paths, ever"). Widening it to discovered homes changes what the MCP process may read. S1 widens the widget's read paths only; the MCP list and the installers stay on the closed set until Este rules.
- **New MCP tools (S6)**: `get_desk_messages`, `propose_desk_messages`, `watch_*` change the tool surface.
- **The Appointments capability for meetings (S6)**: a manifest change.
- **A desktop-layer window in a Store build (S5)**: the reviewer letter has to name it and show how to turn it off.
- **Reset Sanduhr… (S3)**: it deletes credentials, the vault and Claude Code entries; the wording of what it removes is a privacy claim.
- **Per-seat meters (S4)**: if it adds a seat snapshot the MCP reads, that is a trust-boundary change.

## What parity covers and what's left (cycle closed 2026-10-12)

**Covered, all seven stages merged:** What's New and quick wins (#204), the welcome tour, signed-out first run and demo mode (#205), Settings v2 (#206), the Claude Code side with per-folder meters, account follow and Reset (#208), the Desk foundation on Rainmeter's "On desktop" route (#209), Desk content with messages, now playing, system readings, weather, ink and glow targets (#213), and Desk polish with covered pause, arrange by dragging, the glow's Desk spot, Desk clicks, sign-in reminders and Just the Desk (#214). Each stage passed 0 warnings, two green test runs, CI and an independent review; the Desk stages also passed Este's live runs.

**Held, each for its reason:**
- **The Desk and watcher MCP tools** (`get_desk_messages`, `propose_desk_messages`, `watch_start`, `watch_update`, `watch_end`): a trust-boundary decision. They change the MCP's tool surface and what it reads; the Desk side is wired and off (`DeskMessageHandoffService.Enabled`, `WatcherStore.Enabled`), so each tool is a thin addition once Este rules.
- **Meetings and the Join shortcut**: `Windows.ApplicationModel.Appointments` is a manifest capability, a Store-reviewer surface.
- **Watchers from Claude Code's background work** (the Stop hook's `background_tasks`): a privacy claim, since it changes what the S4 hooks read.
- **A user-settable theme success color**: a trust-boundary item. `ThemeModel.cs` and `ThemeLint.cs` are compiled into the MCP for `propose_theme`, so a new theme field changes what the MCP compiles; built-in themes carry their success hue in the App and user themes fall back to the healthy green.

**Cut:**
- **The camera and mic indicators**: cut from the cycle by Este; no Windows spike was run.
- Earlier cuts stand: the notch and its wings, app-menu shortcut scanning, song looks, Sparkle and Dock specifics, agent-styled text beyond message effects.

**Follow-ups:**
- **The arrange resize handle**: Core's `DeskArrange.Scale` and `Handle` are ported and tested; Windows draws no handle, so sizes stay in Settings.
- **The meters' right-click limit menu on the Desk**: the Mac's two-finger menu on a meter row is not ported; Desk clicks cover the clock, message, Claude line, now playing and the weather credit.
- **The open issues recorded in `spec.md > Open Issues`**, S6 and S7 included: the shortcut map read as off after a downgrade to a pre-S6 build (a release-note line), `SaveClaudeCodeGlow`'s switches tested only through Core, the glow spot placed during an arrange session being overwritten by Done, the right-click menu anchoring to the first piece of a repeated kind, a redraw cutting a Desk glow short, and the unexplained spread in steady-state idle cost (0.09 to 0.42% of one core, all under the 0.5% target).
- **The Rainmeter adapter** (dashboard task `qu9fh8ot0`): post-parity, its own cycle.

## Loose Implementation Notes
- Pure logic goes in `Sanduhr.Core` with xUnit tests ported from the Mac's tests; WPF stays in `Sanduhr.App`. Anything the MCP needs stays out of the four source-linked files unless Este widens the allowlist.
- The What's New card model is the shared seam: S2's tour, S3's names table and every later stage's cards ride on it. Build it with the tour's fields (`step`, `requires`, `choice`) present from the start.
- Settings persist in `%APPDATA%\Sanduhr\settings.json` through `SettingsStore`; new keys default so an existing install reads unchanged.
- The Desk stages (S5 to S7) are lighter in this cycle's PRD and spec and get one deepening pass before their build: the WorkerW behavior on Windows 11 24H2 and later, mixed-DPI monitors and click-through hit-testing are the unknowns.
