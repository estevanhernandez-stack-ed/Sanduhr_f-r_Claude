# Windows port plan: Mac 2.3.3 to 2.6.0, and one thing back

Mac Sanduhr gained most of its features between 2.3.3 and 2.6.0 (2026-10-02 to 2026-10-04). This plan sorts each one for Windows (3.4.2), names the Mac code and spec to read first, and lists the one Windows feature the Mac should take in return: sign-in. The mirror of `docs/mac-parity-plan.md`, which went the other way.

Effort: S is an evening, M is a weekend, L is a week or more. IDs (W1…) are for the checklist and commits.

## The verdict

Most of it ports. Six items are logic with an obvious home in the Windows code and make a release on their own. Now playing is easier on Windows than it was on the Mac, because Windows has a public API for it. The Desk is the large piece, and the reason to pick Sanduhr over a Rainmeter skin: a desktop-layer window, the same technique Rainmeter uses, with the meters, clock, message, Claude line and meetings. Once the Desk exists, Claude-written messages, their effects and the layout work come with it.

Rules that hold for every item: the MCP process stays network-free (handoff files through `%APPDATA%\Sanduhr`, as `propose_theme` and `publish_usage` already do), the 10.1.4.4 acceptance tests pass on every surface that names Claude, and nothing changes the Velopack packId or the vault's location.

## Already on Windows (Windows had them first)

Accounts and switching, the usage vault, Claude Code activity (calendar, trends, history chart), CSV export, `propose_theme` and the theme lint, the MCP server's usage tools, the statusline and MCP installers, embedded sign-in. Nothing to do.

## Wave 1: logic ports, one release

| ID | Feature | Windows today | Mac reference | Effort |
|---|---|---|---|---|
| W1 | **What's New after an update**, and the welcome tour on the same cards | none | `docs/welcome-tour-spec.md` (shared); `mac/Sources/Sanduhr/Models/WhatsNew.swift`, `WhatsNewCards.swift`, `Views/WhatsNewWindow.swift` | M |
| W2 | **Limits that come and go**: spot a temporary limit (a promo or special weekly), offer Hide only for those, bring it back when it resets or refills, Stop warnings per limit | none; every limit always shows | `Models/LimitLifetime.swift`, `Models/LimitMenu.swift`, `Desk/MeterVisibility.swift`; `docs/mac-accounts-spec.md` | S |
| W3 | **What the tray shows**: session, weekly, whichever is higher, or both in turn | the highest only (`TrayPercentChanged`) | `Models/MenuBarMode.swift` | S |
| W4 | **Follow the account you're using**: switch to the account whose Claude Code folder is active, off by default, never flapping between two in use | manual switching only | `Services/AccountFollow.swift`, `Services/AccountFollower.swift`; `docs/mac-accounts-spec.md` | M |
| W5 | **Steady sparklines draw a level line**, not a solid slab, when readings sit within 2 points | horizon chart, likely the same slab at a steady 90%+ | `Views/SparklineView.swift` (`drawn(_:values:)`, `paintLevel`), tests in `SparklineModeTests.swift` | S |
| W6 | **EsteFont 26 built in** (Regular and Bold; licensed for use by 626Labs LLC), registered for the app only, first in the font lists | system and theme fonts | `Helpers/BundledFonts.swift`, `mac/Resources/Fonts/`, the font license in the third-party notices | S |
| W7 | **Home discovery**: every `~/.claude*` folder with `projects/` or `.claude.json`, plus `CLAUDE_CONFIG_DIR`, skipping `*.config-backup-*` | a fixed list of two names (gap G1) | `Services/ClaudeCodeFolders.swift`; `docs/superpowers/research/2026-10-05-claude-config-history-gaps.md` | S |

W7 is a bug fix more than a port: a config home outside the two names drops out of the burn, model usage and vault. Ship it first. The backup skip applies to the Mac too.

## Wave 2: now playing

| ID | Feature | Windows approach | Mac reference | Effort |
|---|---|---|---|---|
| W8 | **Now playing in the widget**: "Title · Artist" with a play-state glyph, click to play or pause, Next while paused, hide while paused, a switch per app | `GlobalSystemMediaTransportControlsSessionManager` (WinRT, public, no permission): sessions, media properties, timeline, playback controls. Spotify, browsers and most players publish to it | `Services/NowPlaying.swift` (the pure half: merged track, the tracker that keeps a skip from blinking, hide rules, app exclusion), `docs/mac-now-playing-spike.md` | M |

No adapter, no helper process, no fallback chain: the Mac needed all three because MediaRemote answers Apple-signed hosts only. Titles stay in memory, never saved or logged, as on the Mac. On the Desk it becomes a line like the others (W9).

## Wave 3: the Desk for Windows

| ID | Feature | Windows approach | Mac reference | Effort |
|---|---|---|---|---|
| W9 | **The Desk**: clock and date, meters, the message line, the Claude line, meetings, now playing, in corners, drawn in the Desk ink and font, click-through except where something is drawn | a borderless, transparent window parented into the desktop layer (`Progman` / `WorkerW`, as Rainmeter does), hit-tested per element; one per monitor; survives Explorer restarts (re-parent on `TaskbarCreated`) | `mac/Sources/Sanduhr/Desk/` (`DeskView`, `DeskModel`, `DeskHitTest`, `DeskMeters`, `MeterWarning`), the window-server click rule in the Mac smoke docs | L |
| W10 | **Taskbar clearance**: corners on the taskbar's side sit clear of it; with an auto-hiding taskbar they rise with it | the work area (`SPI_GETWORKAREA`, `WM_SETTINGCHANGE`) and `SHAppBarMessage(ABM_GETSTATE / ABM_GETTASKBARPOS)` for auto-hide; simpler than the Mac's Dock watching | `Desk/DockClearance.swift` (the layout half), `Desk/DockFollower.swift` (timing) | S |
| W11 | **Desk messages, with effects**: `messages.txt`, `Mon:` and `10-31:` prefixes, rotation, pinning; `{ink}`, `{glow}`, `{size}`, `{write}`, `{shimmer}` per line | the same file format and parser rules, drawn in WPF | `Desk/Message.swift`, `Desk/MessageEffects.swift` (pure parser), `Desk/DeskMessageLine.swift` | M |
| W12 | **Claude writes your Desk messages**: `get_desk_messages`, `propose_desk_messages`, approve first or the direct opt-in | the `propose_theme` handoff pattern already in `Sanduhr.Mcp` | `Desk/MessageProposal.swift`, `Desk/DeskMessageHandoff.swift`, `mac/integrations/sanduhr_mcp.py` (tool descriptions teach the syntax) | M |
| W13 | **Meetings on the Desk**, with a join link | `Windows.ApplicationModel.Appointments` (asks for calendar access once); never writes to the calendar | `DeskModel.refreshEvents`, the demo mode for screenshots | M |
| W14 | **Desk layout**: anchors, order, size, a preview map, then arrange on the desktop | build to checklist items 59 and 60 directly; one design for both apps | `docs/checklist.md` items 59–60 | M, then L |

## Wave 4: Claude Code tells you it needs you

| ID | Feature | Windows approach | Mac reference | Effort |
|---|---|---|---|---|
| W15 | **"Claude needs you" glow**: Claude Code hooks (`Notification`, `Stop`) installed per folder, a public URL event, rate-limited, skipped while a terminal is in front | the hook installer ports as is (it writes Claude Code settings). The signal: a glow at the top center of the screen, plus taskbar flash and an optional quiet toast. The top-center glow is the same design as the no-notch island in issue #119; solve them together | `Desk/ClaudeCodeGlow.swift`, `Services/IntegrationInstaller.swift` | M |

## Doesn't port

The notch and its wings, the camera light, the Mac now-playing adapter and its Perl watchdog, Sparkle, Dock specifics. The Windows equivalents above replace them.

## Back to the Mac: sign-in

Windows signs in inside the app (WebView2, `SignInWindow`), with a Google notice and a paste fallback. The Mac still asks for the `sessionKey` cookie through DevTools, which is the worst first minute either app has.

| ID | Feature | Mac approach | Windows reference | Effort |
|---|---|---|---|---|
| M1 | **Embedded sign-in**: the real claude.ai login in a window, the session captured on success, paste as the fallback | `WKWebView` with a non-persistent data store, capture `sessionKey` from `WKHTTPCookieStore`, then drop the store | `Views/SignInWindow.xaml.cs`, `Services/SignInCoordinator.cs`, `Core/ClaudeSignIn.cs`, `Core/ReauthRouting.cs` | M |
| M2 | **Google accounts**: Google refuses embedded web views (`disallowed_useragent`, since 2021) | the same notice Windows shows when the view lands on `accounts.google.com`: go back and choose **Continue with email**. A Google-created Claude account can sign in by email code | `UpdateOAuthNotice`, `OnGoogleNoticeBackClick`; `docs/usage-api-audit-2026-07-19.md` §4 | S |

**Google is still the hard part on both apps.** The audit's finding stands: route Google users to claude.ai's email sign-in inside the embedded view; it never touches Google's OAuth, so the block never fires. Two live checks are still open and decide whether the notice can say "this will work" rather than "try this": (a) a Google-created account accepts email sign-in with no unlink step, and (b) the 6-digit code field renders in the embedded view when the link is opened on another device. Do these once, by hand, before writing the copy. Confirmed dead ends, not to revisit: reading the browser's cookies (App-Bound Encryption on Chrome and Edge; Safari needs Full Disk Access), user-agent spoofing (against Google's terms), loopback OAuth, passkeys inside the view.

An improvement worth testing on both: a magic-link email opened on the same machine lands in the default browser, not the app. The code entry path avoids that; the copy should steer to "enter the code" rather than "click the link".

## Order

1. W7 (bug), then Wave 1 as one Windows release, with W1 built from the shared tour spec.
2. M1 and M2 on the Mac, in parallel; the Mac welcome tour (`docs/welcome-tour-spec.md`) starts with them.
3. W8, now playing in the widget.
4. Wave 3, the Desk. W9 and W10 first, then W11, W12, W13, then W14 with the Mac's items 59–60.
5. W15 with issue #119.

## Open questions

- The Desk on Windows: one window spanning monitors, or one per monitor? The Mac draws one per screen; per monitor is simpler with mixed DPI.
- Store certification for a desktop-layer window: nothing in 10.1.4.4 forbids it, but a reviewer letter should name it and show how to turn it off.
- W13's calendar permission on Windows: the Appointments API needs the `appointments` capability in the manifest; confirm it doesn't widen the Store review.
