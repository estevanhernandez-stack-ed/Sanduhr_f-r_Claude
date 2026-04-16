# Changelog

## v1.1 — 2026-04-15

### Added
- **Matrix theme** — green-on-black terminal aesthetic
- **Burn rate projection** — warns when current pace will exhaust usage before the reset period ends
- **Sparklines** — inline trend charts showing usage velocity over the last 2 hours (24 data points at 5-min intervals)
- **Pace markers** — colored tick on each progress bar showing where "on pace" is right now
- `.gitignore` — excludes config, docs, and Python cache from repo

### Changed
- Theme selector moved from title bar button to a dedicated strip below the title bar with all themes visible
- Burn rate framing revised: only warns when exhaustion happens before reset, not raw "hits 100% in X hours"

## v1.0 — 2026-04-15

### Added
- Real-time usage bars for all active Claude.ai tiers (Session 5hr, Weekly All Models, Sonnet, Opus, Cowork, Routines)
- Linear pacing engine with "On pace" / "X% ahead" / "X% under" indicators
- Reset countdown ("3d 5h 44m") and reset date/time ("Sun 1:00 AM") display
- 4 themes: Obsidian, Aurora, Ember, Mint
- Compact mode (double-click title bar)
- Always-on-top with pin/unpin toggle
- Draggable frameless window with custom title bar
- Extra usage / overage credit tracking
- "Use Sonnet" quick link in footer
- Auto-refresh every 5 minutes, countdown tick every 30 seconds
- Graceful in-place UI updates (no flicker)
- Auto-install of cloudscraper dependency on first run
- Session key setup dialog on first launch
