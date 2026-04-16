# Installation & Usage Guide

## Requirements

- Python 3.8 or newer
- A Claude Pro, Team, or Enterprise subscription
- Windows, macOS, or Linux (any OS with tkinter — it ships with Python)

## Install

```bash
git clone https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude.git
cd Sanduhr_f-r_Claude
python sanduhr.py
```

No virtual environment needed. No `pip install`. The only dependency (`cloudscraper`) auto-installs on first run.

## First Launch — Session Key Setup

On first run, a dialog walks you through authentication:

1. Open [claude.ai](https://claude.ai) in your browser and log in
2. Open DevTools (`F12`)
3. Navigate to **Application** > **Cookies** > **claude.ai**
4. Find the `sessionKey` cookie and copy its value
5. Paste it into the setup dialog

The key is stored locally at `~/.claude-usage-widget/config.json` and never leaves your machine.

## Controls

| Action | What it does |
|--------|-------------|
| **Theme buttons** | Switch between Obsidian, Aurora, Ember, Mint, Matrix |
| **Key** | Update your session key |
| **Refresh** | Force a data refresh (auto-refreshes every 5 min) |
| **X** | Close the widget |
| **Pin** | Toggle always-on-top |
| **Drag title bar** | Reposition the widget |
| **Double-click title** | Toggle compact mode (shows only highest-usage tier) |
| **Use Sonnet** (footer) | Opens claude.ai with Sonnet preselected |

## Session Key Expiration

Your key expires when you log out of claude.ai or after extended inactivity. If the widget shows "Session expired", click **Key** and paste a fresh one.

## Data Storage

All data lives in `~/.claude-usage-widget/`:

| File | Purpose |
|------|---------|
| `config.json` | Session key + theme preference |
| `history.json` | Sparkline data (last 2 hours of usage snapshots) |

## Troubleshooting

| Problem | Fix |
|---------|-----|
| "Session expired" | Click **Key**, paste a fresh sessionKey from browser cookies |
| Widget won't start | Verify Python 3.8+: `python --version` |
| Cloudflare errors | Update cloudscraper: `pip install --upgrade cloudscraper` |
| No tiers showing | Your subscription may not have active usage yet — wait for a refresh |
| tkinter missing (Linux) | Install: `sudo apt install python3-tk` (Ubuntu/Debian) or `sudo dnf install python3-tkinter` (Fedora) |
