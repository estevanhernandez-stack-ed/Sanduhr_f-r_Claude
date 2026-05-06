# Sanduhr für Claude — Windows v2.3.0

Feature release. Live token-burn deltas from local Claude Code session
logs, the previously-parked Routines tier, drag-reorder tier cards,
custom dialog chimes replacing OS beeps, and a fix for the Win11
taskbar-icon binding bug on frameless + always-on-top windows.

## What's new

- **Routines (Daily Routines) tier.** Daily run-quota for Claude Code's
  cloud-hosted scheduled / API / GitHub-triggered runs. Renders as a
  count card (`3/15`) instead of a percentage.
- **Local CC settings tab.** Reads token usage from local Claude Code
  session JSONLs in `~/.claude*/projects/`. Today + Last 30 days numbers,
  daily-bar strip, top projects + skills tables. Loads asynchronously so
  the tab opens instantly even on heavy CC users.
- **Live token-burn delta** on every tier card and in the footer. Sourced
  from the local logs and reset on each successful API fetch — bridges
  the minutes-long lag of Anthropic's `/usage` endpoint.
- **Cards tab — drag and drop to reorder tier cards.** Settings → Cards
  (renamed from Pacing). Uncheck to hide; drag to reorder.
- **Custom dialog chimes** for save / error / info / toggle. Replaces
  every Windows system beep with short PCM tones rendered to
  `%APPDATA%\Sanduhr\sounds\`.
- **Themes apply across all open dialogs.** Apply a theme from the
  Settings → Themes tab and the change propagates everywhere — no
  reopen required.
- **Win11 taskbar icon now binds reliably** on frameless + always-on-top
  windows. Pin preference persists across launches.
- **History tab polish.** Gridlines, area fills, time-axis ticks, and a
  right-edge stacked label showing "% left" + "resets in Yd Zh".

See [CHANGELOG.md](../CHANGELOG.md) for the full list and the post-v2.2.0
test additions.

## Install

1. Download `Sanduhr-Setup-v2.3.0.exe` from GitHub Releases (or install
   via the Microsoft Store package).
2. If SmartScreen warns *"Windows protected your PC,"* click **More
   info** → **Run anyway**. Expected for unsigned `.exe` builds; the
   Store-distributed MSIX is signed by Microsoft on ingestion.
3. Step through the installer.
4. Launch Sanduhr from the Start Menu and paste your `claude.ai`
   sessionKey when prompted.

## Upgrade notes

- Per-account history files (`history.{Account}.json`) introduced in
  v2.2.0 carry over unchanged. No migration required.
- The Cards tab replaces the Pacing tab; existing pacing-tools and
  reminder toggles preserved as-is.
- New settings keys: `pinned`, `tier_order`, `local_cc_show_breakdowns`.
  Defaults are sensible — no manual config needed.
