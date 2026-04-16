# Sanduhr für Claude

*"Hourglass for Claude" — watch your usage sand drain.*

A glassmorphism-styled desktop widget that shows your Claude.ai subscription usage with burn-rate projections, pace markers, and sparklines. Know at a glance whether you're burning through your limits too fast — or have room to push harder.

**Built for power users who live in Claude all day.**

---

## Platforms

| Platform | Source | Install |
|---|---|---|
| **Windows** (v2, native) | `windows/` | Download installer from [Releases](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases) |
| **macOS** (native SwiftUI) | `mac/` | Download `.dmg` from [Releases](https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases) |
| **Windows/Mac/Linux** (v1, Python) | `sanduhr.py` | `python sanduhr.py` |

Each platform release is tagged: `v2.0.0-windows`, `v1.0.0-mac`, etc.

---

## Features

- **Real-time usage bars** — Session (5hr), Weekly All Models, Sonnet, Opus, Cowork, Routines
- **Burn rate projection** — "Hits 100% in ~4h 22m" warns you before you run dry
- **Usage sparklines** — Tiny inline trend chart showing your consumption velocity over the last 2 hours
- **Pace markers** — Colored tick on each bar showing where "on pace" is right now. Glanceable without reading text
- **Pacing engine** — Are you ahead, behind, or on pace? Color-coded indicators tell you instantly
- **Reset date/time** — Shows both the countdown ("3d 6h") AND the actual reset day ("Sun 1:00 AM")
- **Compact mode** — Double-click the title bar to collapse to just the highest-usage tier
- **4 themes** — Obsidian, Aurora, Ember, Mint. Click the theme button to cycle
- **Always-on-top** — Sits on your desktop while you work. Pin/unpin as needed
- **Draggable** — Click the title bar and drag anywhere
- **Glassmorphism cards** — Frosted glass design with accent borders
- **Graceful updates** — UI updates in place, no flicker. Countdowns tick every 30s
- **Auto-refresh** — Pulls fresh data every 5 minutes
- **Extra usage tracking** — Shows spend if you have overage credits enabled
- **Use Sonnet link** — Quick link in footer to open Claude with Sonnet selected
- **Zero config files to edit** — Paste your key once, done

## Quick Start

```bash
# Just run it (dependencies auto-install)
python sanduhr.py
```

On first launch, a setup dialog walks you through grabbing your session key:

1. Go to [claude.ai](https://claude.ai) and log in
2. Open DevTools (`F12`)
3. Go to **Application** > **Cookies** > **claude.ai**
4. Copy the `sessionKey` value
5. Paste it into the dialog

That's it. The widget appears and starts tracking.

## Themes

| Theme | Vibe |
|-------|------|
| **Obsidian** | Deep black with purple accent. The default. |
| **Aurora** | Dark blue with cyan glow. Night sky energy. |
| **Ember** | Dark red with orange warmth. Cozy. |
| **Mint** | Dark green with teal accent. Terminal vibes. |

Click **Theme: [name]** in the title bar to cycle. Your choice persists between sessions.

## How It Works

Sanduhr f\u00fcr Claude calls two Claude.ai API endpoints (the same ones the settings page uses):

- `GET /api/organizations` — Gets your org ID
- `GET /api/organizations/{orgId}/usage` — Returns utilization % and reset timestamps

Uses [cloudscraper](https://github.com/VeNoMouS/cloudscraper) to handle Cloudflare. Your session key is stored locally at `~/.claude-usage-widget/config.json` and never leaves your machine.

## Pacing Logic

Linear pace calculation based on how far into the current period you are:

- **On pace** (green) — Within 5% of where you'd expect to be
- **Ahead** (orange) — Using faster than linear. The pace marker on the bar shows where you *should* be
- **Under** (blue) — Plenty of headroom

The **burn rate projection** takes it a step further — if your current velocity would exhaust the limit before the reset, it tells you exactly when you'll hit 100%.

## Controls

| Action | What it does |
|--------|-------------|
| Theme: [name] | Cycle through 4 color themes |
| Key | Update your session key |
| Refresh | Force a data refresh |
| X | Close the widget |
| Pin | Toggle always-on-top |
| Drag title bar | Reposition the widget |
| Double-click title | Toggle compact mode |
| Use Sonnet (footer) | Open claude.ai with Sonnet selected |

## Requirements

- Python 3.8+
- `cloudscraper` (auto-installed on first run)
- Claude Pro, Team, or Enterprise subscription

## Session Key Expiration

Your key expires when you log out or after extended inactivity. If Sanduhr f\u00fcr Claude shows "Session expired", click **Key** and paste a fresh one.

## Roadmap

- [ ] Antigravity (Google Gemini IDE) quota tracking
- [ ] System tray mode
- [ ] Auto-start on boot
- [ ] Historical usage dashboard

## Why "Sanduhr für Claude"?

*Sanduhr* (ZAHND-oor) is German for "hourglass" — Sand + Uhr (sand clock). *Für* = "for." You're watching the sand drain on your Claude usage, pacing yourself so you don't run out before the reset.

## License

MIT — do whatever you want with it.

---

Built by [626Labs](https://github.com/626labs) with Claude.
