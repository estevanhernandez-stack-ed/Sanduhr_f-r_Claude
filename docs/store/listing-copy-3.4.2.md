# Store listing copy — v3.4.2 (shipped)

> Archive of what went to the Store for 3.4.2.0 (certified 2026-09-15, submission
> 1152921505701890716). Source of truth is `Store-Listing-Console/apps/sanduhr/copy/`
> (commit c7945e8); this file is the record of what shipped, per release-runbook Phase 7 step 8.
>
> First release where the package and the corrected listing text shipped in the SAME
> submission. 3.4.0 published with 3.3's description because it was committed from Partner
> Center, and 3.4.1's text fix rode a follow-up submission that was cancelled once 3.4.2
> superseded it — so there is no `listing-copy-3.4.1.md`, and this file is the first
> archive since 3.4.0 that matches what the Store actually serves.

## What's new in this version

```
Version 3.4.2

- Sanduhr notices when a Store update is waiting. Windows cannot replace
  files a running program holds, so an update that arrives while the
  widget is open fails and quietly leaves you on the old version. Now
  Sanduhr says an update is waiting and offers a button that opens the
  Store's downloads page and closes the widget so the install can
  finish.
- Usage counts a subfolder as its repository. A Claude Code session
  started in a subfolder of a repository used to appear as its own
  project in the Claude Usage tab; it now counts toward the repository,
  the way sessions in a worktree already did. A repository inside
  another keeps its own name.
- The theme editor scrolls. In Settings, Themes, Studio, the list of
  color tokens could push "Save & apply" below the bottom of the window,
  and the only way to reach it was to drag the window taller. It scrolls
  now, and the installed themes stay put below it.

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used
nominatively. Sanduhr für Claude is an independent third-party tool, not
affiliated with, endorsed by, or associated with Anthropic PBC.
```

---

## Full listing sheet as published

## Short description

```
Native Windows 11 widget that projects when you'll hit your Claude usage cap — burn rate, pace markers, live token-burn, glass themes. Independent tool, not affiliated with Anthropic. Needs an active Claude Pro/Team/Enterprise subscription.
```

## Long description

```
Know when you'll hit your Claude weekly cap — before you do. 

Sanduhr für Claude is a native Windows 11 glass widget for Claude Pro, Team, and
Enterprise users. It turns your claude.ai usage into something you can actually pace
yourself by — not a mirror of the numbers on the settings page, but the analysis
layer on top.

WHY PEOPLE KEEP IT OPEN

• Burn-rate projection — "You'll hit your weekly cap in ~4h 22m at current pace."
  Know before you run dry.
• Pace ghost on every bar — a tick shows where you should be right now. Sit left of
  it and you're under pace, right of it and you're ahead. Pace by eye, no math.
• Live token-burn from your local Claude Code sessions — reads your local Claude Code
  session logs to show a live token-burn delta on every tier card and in the footer,
  with daily-quota tracking for the Routines tier.
• 2-hour horizon sparkline — velocity trend inline on every bar, denser than a line
  chart at the same pixel budget.
• Deep-work focus timer — swap the tier cards for a digitised pixel hourglass that
  drains in real time when you want to lock in.
• Cooldown snake game — for when you've burned through your budget and need to kill a
  few minutes.
• Five hand-tuned glass themes — Obsidian, Aurora, Ember, Mint, Matrix — on a Win11
  Mica backdrop (solid-color fallback on Win10). Author your own too: paste a JSON
  palette, or hand the built-in AI-agent prompt to Claude (or any LLM) with a
  reference image and drop the result straight in.
• Multi-account support — track Personal and Work Claude accounts in one install,
  each with its own history, and switch from the widget.
• 30-day local history with CSV export — see your usage trend over time and analyze
  it anywhere.
• Built to live on your desktop — every feature one click away on the bottom tool
  strip; drag from anywhere, pin always-on-top, compact mode, edge-drag resize.
  Window position and theme persist between sessions. Full keyboard surface:
  Ctrl+R refresh, Ctrl+, settings, Ctrl+D compact, Ctrl+H help.

WHAT YOU NEED

Your own active Claude Pro, Team, or Enterprise subscription. On first run, sign in
on the real Claude page right inside the app — no DevTools, no cookie-pasting; the app
walks you through it. (Power users can still paste a session key by hand if they
prefer.) That's it.

PRIVACY BY DESIGN

Your credentials live only in Windows Credential Manager (service com.626labs.sanduhr)
and are wiped when you uninstall. No account is created with 626Labs. No server, no
telemetry, no analytics, no ads. Sanduhr reads claude.ai — and your local Claude Code
session logs — using your own session. Nothing about your usage comes back to us.
If you want it somewhere, send it to yourself: an optional daily publish, off by default,
posts token counts per project name to an endpoint you choose.

—

Independent third-party tool. Sanduhr für Claude is not affiliated with, endorsed by,
or associated with Anthropic PBC. "Claude" and "claude.ai" are trademarks of Anthropic
PBC, used nominatively to describe what this tool integrates with.

Built by 626 Labs for Claude power users who want to pace themselves, not just track.
```

## Product features (20)

- Per-model weekly meters: your Claude allowance per model tracked automatically; when Anthropic adds a limit for a new model its meter appears with no app update
- Pace markers with ahead / on-pace / under scoring on every tier
- Burn-rate projection: when you will hit each cap at your current pace
- Live token-burn delta on every tier card, read from your local Claude Code logs to bridge the lag of Anthropic's usage endpoint
- Claude Usage tab: today and 30-day totals with a sent/received split, a sessions ledger by project and agent, weekly trends, a usage calendar, CSV export
- Opt-in local usage vault: history survives Claude Code's 30-day log cleanup; totals only, stored on your machine, erasable any time
- Usage alerts: a toast and a soft chime at your warn and urgent thresholds and at 100%
- Routines tier: daily run quota for Claude Code's cloud-hosted scheduled runs, shown as a count card
- Statusline and MCP bridge: the widget writes a usage snapshot that the Claude Code statusline and the Sanduhr MCP tools read
- Optional daily publish, off by default: send token counts per project name to an endpoint you choose, with per-home consent
- Cards tab: drag to reorder tier cards, uncheck to hide
- 2-hour sparkline history per tier
- Five built-in themes plus unlimited user themes via JSON, applied live across every open dialog
- AI theme prompt: generate a theme from a vibe or a reference image
- Windows 11 Mica glass backdrop with a Windows 10 solid-color fallback
- Deep-work focus hourglass and a cooldown snake game while you wait for the reset
- Guided sign-in including Google accounts: email-code login inside the app, no cookie hunting
- Credentials stored in Windows Credential Manager, cleared on uninstall
- Full keyboard shortcuts with an in-app Help tab
- Independent third-party tool: no telemetry, no analytics, no ads, and nothing about your usage comes back to us

## Search terms (6)

- Claude subscription monitor
- Claude desktop widget
- glassmorphism windows widget
- Fable 5
- Claude usage tracker
- Sanduhr for Claude

## Copyright and trademark info

```
© 2026 626Labs LLC. Licensed under MIT. "Claude" and "claude.ai" are trademarks of Anthropic PBC. Sanduhr für Claude is not affiliated with or endorsed by Anthropic PBC.
```

## Developer studio

```
626Labs LLC
```

## Screenshot captions (10)

- `Sanduhr - Main.png` — Every Claude tier at a glance, with pace markers and a burn-rate projection
- `Sanduhr - CC Usage.png` — Claude Code token burn, live from your local session logs
- `Screenshot 2026-04-20 171524.png` — Cooldown to even pace.
- `Screenshot 2026-04-20 172126.png` — Matrix Theme
- `Screenshot 2026-04-20 172334.png` — Simplified prompt based theming.
- `Screenshot 2026-04-20 171448.png` — Obsidian Theme
- `Screenshot 2026-04-20 172440.png` — Snakes, why'd it have to be snakes?
- `Screenshot 2026-05-06 143922.png` — reorganize tier cards
- `Sanduhr - Add Accounts.png` — Add Accounts
- `Screenshot 2026-04-20 172243.png` — Tracks surplus to maximize use.

## License terms

```
MIT License

Copyright (c) 2026 626Labs LLC

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```
