# Sanduhr for Windows: Mac parity — Product Requirements

## Problem Statement
A Windows Sanduhr user updates and learns nothing about what changed; a new one signs in and gets a bare widget. Promo limits stay on the widget after they stop mattering, the tray shows one number chosen one way, a steady 96% sparkline draws as a slab or as nothing, and any Claude Code home outside `.claude` and `.claude-personal` silently drops out of the burn, model usage and vault. Past those, the Windows app lacks the Mac's Settings v2, its Claude Code side (mods, combined statusline, the glow) and the Desk. Every gap costs Windows users the features that make Sanduhr worth installing, and costs Este a second vocabulary to maintain.

Stages S1 to S4 carry full acceptance criteria. S5 to S7 (the Desk) are lighter, and each gets one deepening pass before its build. Each stage ships as its own PR to main and is releasable on its own.

## User Stories

### S1. What's New after an update (W1)

- As a Windows user who just updated, I want a window that shows what changed, so that I find the new features without reading release notes.
  - [ ] On the first launch of a version newer than the last one seen, with an account signed in, a What's New window opens two seconds after launch (as on the Mac) with the cards of every release newer than the last seen one, up to the current version, newest first, at most 8.
  - [ ] An update from a build before What's New (no last-seen version, but an existing install) shows every card up to the current version.
  - [ ] A fresh install records the version and shows nothing (the sign-in step and, from S2, the tour cover it).
  - [ ] While signed out (no account), nothing shows and nothing is recorded; the cards wait for a launch with an account.
  - [ ] The version is recorded as seen when the window is shown, so a crash or quit never shows it twice.
  - [ ] The header reads "New in 3.5.0" for one release, or "New in 3.4.0 – 3.5.0" (en dash) for a range.
  - [ ] Each card: art (a glyph or a small live preview), a title, at most two sentences, and **Show me**, which closes the window and opens Settings on the card's tab.
  - [ ] **Don't show after updates** (off by default) stops the automatic window only; the tray menu and Settings still open it.
  - [ ] **Done** is the default button (Enter); Escape closes. Every card, button and the checkbox has an accessible name; Tab reaches each Show me in order.
  - [ ] The window carries the trademark disclaimer and stays legible whether Windows is in light or dark mode (it draws its own themed surface, like ThemedDialog).
- As a user, I want to open What's New any time, so that I can read it again.
  - [ ] **What's New…** in the tray menu and on Settings, General opens it. On a fresh install before any update it shows "Highlights in <version>" (that version's own cards); otherwise every card under its range.

### S1. Global shortcut with a recorder

- As a keyboard user, I want a global shortcut that opens Settings from any app, so that I don't hunt for the tray icon.
  - [ ] Settings, General has a **Shortcuts** section: a switch and a keys button for "<keys> opens Settings". Off by default (default — confirm on next interactive run); default keys Ctrl+Alt+S.
  - [ ] Clicking the keys button records: it reads "Type keys…", the shortcut is unregistered while recording, Escape cancels, any other key combination is checked and saved.
  - [ ] A combination without Ctrl, Alt or Win is refused: "Add Ctrl, Alt or Win: Shift alone isn't enough."
  - [ ] When Windows refuses the registration (another app holds the keys), the row says "Another app already uses <keys>. Pick another." and the switch stays as chosen.
  - [ ] **Reset to Ctrl+Alt+S** appears only when the keys differ from the default.
  - [ ] The choice survives a restart; storing the default stores nothing.
  - [ ] The caption says the keys work in every app whenever Sanduhr runs, and that while on, they go to Sanduhr instead of the app in front.

### S1. Every Claude Code home counts (W7)

- As someone with several Claude Code config folders, I want every one of them counted, so that the burn, model usage and vault aren't missing a folder.
  - [ ] Every folder in the user profile named `.claude` or `.claude-<something>` that has a `projects` folder or its own `.claude.json` is found; `.claude-`, `.claudette`, plain files and backups (`*.config-backup-*`, `.claude-backup-*`) are not.
  - [ ] `CLAUDE_CONFIG_DIR`, when it names a folder directly in the user profile that looks like a home, is included.
  - [ ] `.claude` sorts first, the rest in natural order.
  - [ ] Local burn and model usage read every found home.
  - [ ] The vault and publish lists show every found home as a toggle; a new one starts unconsented (off), so nothing new is read into the vault or published without the person turning it on.
  - [ ] The MCP server, the statusline installer and the MCP installer are unchanged (escalated; see Open Questions).

### S1. Limits that come and go (W2)

- As a user with a promo or special weekly limit, I want to hide it until it matters again, so that the widget shows what I'm pacing against.
  - [ ] A limit is temporary when it is the special slot (`iguana_necktie`), when its reset is more than 8 days away, or when it is a limit Sanduhr doesn't know and was first seen less than 14 days ago. Session and weekly all-models are never temporary.
  - [ ] A temporary limit's card menu offers **Hide until it resets**; it comes back on its own when a new window starts (reset moves more than an hour later) or it refills by 10 points or more.
  - [ ] A temporary limit hidden this way comes back if it stops being temporary.
  - [ ] The existing **Hide this tier** keeps working for every limit as today (a manual hide stays until shown).
  - [ ] A **Hidden limits** submenu on the card menu lists every hidden limit and shows it again (there is no way to un-hide today).
  - [ ] **Stop warnings for this limit** / **Warn again for this limit** on each card mutes or restores that limit's alert toasts; other limits keep alerting.
  - [ ] A limit hidden until it resets stays out of the widget, the Highest-of-any tray reading and alerts (as on the Mac); a manually hidden one keeps alerting, as today ("a cap is real whether or not its card is shown"). Both still appear in `snapshot.json`, history and the MCP answers.

### S1. What the tray shows (W3)

- As a user who watches the tray, I want to choose what its number means, so that it tracks the limit I care about.
  - [ ] Settings, General and the tray menu's **Tray Shows** submenu offer: Highest of any limit (today's behavior, the default), Session, Weekly, Whichever is higher (session or weekly), Rotate (session and weekly).
  - [ ] Session and Weekly fall back to the other when theirs is missing; Whichever is higher picks weekly only when strictly higher; Rotate switches every 8 seconds.
  - [ ] The tooltip names what the number is ("Session 12%", "Weekly 96%"); with no data the icon shows "--" as today.
  - [ ] The choice survives a restart.

### S1. Steady sparklines (W5)

- As a user at a steady 96% all week, I want the sparkline to show a level line at 96, so that it reads as high and steady rather than as noise or an empty chart.
  - [ ] In Horizon style, when every value sits within less than 2 points, the card draws a level line at the mean on a 0 to 100 scale with a light wash beneath it.
  - [ ] A spread of exactly 2 points keeps the horizon chart; Classic style never changes.

### S2. Welcome tour and a clearer first run

- As a new user, I want a short tour after I sign in, so that I know what Sanduhr does beyond the meters.
  - [ ] After the first successful fetch on a fresh install, a tour opens on the What's New window's look: "Welcome to Sanduhr · 1 of N", Back, Next, **Skip the tour** always visible.
  - [ ] Steps per `docs/welcome-tour-spec.md`; a step whose feature is missing hides itself (the Desk step until S5).
  - [ ] Choices made in the tour (the tray readout) are real settings, written when chosen.
  - [ ] Shown once; never on update; never during sign-in; reopenable from Settings and the tray ("Take the Tour…").
  - [ ] Finishing or skipping records the version as seen, so What's New doesn't follow it.
- As a signed-out user, I want the widget to say so plainly, so that I don't wait on "Connecting...".
  - [ ] Signed out, the widget reads "Not signed in" with Sign In; model controls stay hidden until there are numbers.
  - [ ] What's New on a fresh install reads "Highlights in <version>".
- As Este taking Store screenshots, I want demo mode, so that the widget, tour and Settings fill with sample numbers that name no real account.
  - [ ] Demo mode pauses fetches, fills the meters, cards, tray and previews with sample data, and shows two sample accounts.

### S3. Settings v2

- As a user, I want Settings organized by task with one home per control, so that I find things where their name says they are.
  - [ ] A sidebar with groups replaces the tab strip, with accessible names on every entry and full keyboard navigation (the 10.1.4.4 (c) bar).
  - [ ] A names table: each control has one name across Settings, the tray menu, the tour and What's New; a test checks the tray menu and card strings against it.
  - [ ] Search filters the sidebar to pages and controls by name and synonyms and scrolls to the control.
  - [ ] Every existing choice reads the same after the update (migrations, each idempotent).
  - [ ] Live previews on the pages that change something visible.
  - [ ] About: version, What's New…, Take the Tour…, the trademark disclaimer, Third-Party Notices, where settings live.
  - [ ] Reset Sanduhr… (escalated wording): removes Claude Code entries through their receipts first, then accounts and keys, history, themes and settings; reports failures without stopping.

### S4. The Claude Code side

- As a Claude Code user, I want Sanduhr's Claude Code pieces in one place with exact undo, so that I trust what it writes into my folders.
  - [ ] Mods & Config: a read-only inventory per Claude Code folder first (settings and precedence, CLAUDE.md, extensions, MCP servers, history), then edits only through receipts with byte-for-byte Remove.
  - [ ] Sanduhr's meters mod above the prompt, per folder: On, Off, Update, Remove, with a consent dialog on first install.
  - [ ] Statusline combine: keep the person's own statusline and add Sanduhr's segments, with a segment picker and looks.
  - [ ] The "Claude needs you" glow: `Notification` and `Stop` hooks per folder, a top-center glow plus taskbar flash, rate-limited, skipped while a terminal is in front.
  - [ ] Follow the account in use (off by default, never flapping between two in use).
  - [ ] Per-seat meters: a folder linked to an account shows that account's meters in its statusline and mod.

### S5. Desk foundation (lighter; deepening pass before build)

- As a Windows user, I want a clock, my meters and the Claude line drawn on the desktop itself, so that Sanduhr lives where I look without a window in the way.
  - [ ] A spike proves a click-through window in the desktop layer on Windows 11 (WorkerW), surviving an Explorer restart, before any Desk feature is built.
  - [ ] One Desk window per monitor; pieces in eight places; taskbar clearance (including auto-hide); EsteFont built in for the app only; a layout map in Settings.
  - [ ] Off by default; one switch turns it off; the reviewer letter names it (escalated).

### S6. Desk content (lighter; deepening pass before build)

- As a user, I want messages, now playing, meetings and watchers on the Desk, so that it carries what the Mac's does.
  - [ ] `messages.txt` with the Mac's grammar and effects, the message editor and special days.
  - [ ] Now playing in the widget first (WinRT media sessions), then as a Desk line; titles never stored or logged.
  - [ ] Meetings with a join link (escalated: Appointments capability).
  - [ ] Watchers and the Desk-message MCP tools (escalated: tool surface).

### S7. Desk polish (lighter; deepening pass before build)

- As a user, I want to arrange the Desk by dragging and use it without a Claude account, so that it fits my desktop and stands on its own.
  - [ ] Arrange by dragging, always endable (Enter keeps, Escape restores).
  - [ ] Desk clicks; sign-in reminders with a switch; Just the Desk; a camera and mic indicator spike with a written verdict.

## What We're Building
Stage 1 in full this run, as specified above and in `docs/spec.md`. Stages 2 to 7 have their requirements here and their blueprints in the spec; each is built in a later run from the latest main.

Cross-stage requirements, every stage:
- `dotnet build windows-dotnet` with 0 warnings; `dotnet test windows-dotnet` green; CI green; an independent review with no unresolved correctness findings.
- Pure logic in `Sanduhr.Core` with xUnit tests ported from the Mac's tests where the Mac has them.
- No change to the Velopack packId, the vault's location, Credential Manager slots, `Package.appxmanifest`, the MCP's source-link allowlist or its tool surface without Este's ruling.
- New Store-visible surfaces: trademark disclaimer where Claude is named, accessible names, keyboard reachable, legible whether Windows is in light or dark mode.

## What We'd Add With More Time
- A Test It button for shortcuts (press the keys, Sanduhr says whether they arrived), as the Mac has.
- A reserved-combination list naming the Windows shortcut a combination collides with (Win+L, Alt+Tab).
- Horizon sparklines on an absolute 0 to 100 scale like the Mac (Windows normalizes to the series' own range).
- Song looks and theme title styles.
- `CLAUDE_CONFIG_DIR` outside the user profile (needs path-keyed roots in the vault and publish maps).

## Non-Goals
- **Releases**: no version bumps, tags, packages, Partner Center, Store copy or store-listing-console. Este cuts releases.
- **The notch, camera fill light and app-menu shortcut scanning**: Mac-only hardware or APIs.
- **Changing what the MCP reads or exposes** without Este's ruling.
- **Touching the 3.4.3 submission** in certification.

## Open Questions
1. **The MCP root set** (needs Este before S4): should the MCP, the statusline installer and the MCP installer follow discovered homes? S1 leaves them on the closed two-name set.
2. **The in-app trademark disclaimer** (needs Este; can wait for S3): no current in-app surface carries it; only the manifest does. S1's What's New window does. Should the widget, Settings and sign-in get it now?
3. **Shortcut default** (can wait): off with Ctrl+Alt+S, chosen because Alt+S is Send in Outlook and a menu accelerator in many apps. Este may prefer on for fresh installs, as the Mac does.
4. **Card versions** (needs Este at release): S1's cards are tagged 3.5.0. If the release takes another number, the table's versions change with it.
5. **Tray default** (can wait): Highest of any limit keeps today's behavior; the Mac's default is Whichever is higher (session or weekly).
6. **S5 to S7** need their deepening pass: the WorkerW route on current Windows 11 builds, per-monitor DPI, click-through hit testing, and the reviewer letter.
