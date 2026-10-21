# Store listing copy — v3.5.0 (shipped)

> Archive of what went to the Store for 3.5.0.0 (certified 2026-10-21 on the third try, submission
> 1152921505702187903, resubmitted from Partner Center). Source of truth is
> `store-listing-console/apps/sanduhr/copy/` (branch fix/sanduhr-no-model-keywords, 00ae67a); this file is
> the record of what shipped, per release-runbook Phase 7 step 8.
>
> Two failures before it certified: 3.4.3 asked for `rescap:unvirtualizedResources` and was denied
> (2026-10-20), and the first 3.5.0.0 submission failed on the keyword "Fable 5" (a model name). Este
> removed the keyword in Partner Center and resubmitted; the console copy below matches that.

---


> DRAFT 2026-10-12 for Este's read before the console commit. Sources: the Sanduhr repo's
> CHANGELOG (v3.5.0), release notes and reviewer letter for 3.5.0.0, and the parity-cycle PRs
> #204 to #214, in the register of `voice.md`. Opens with the version line and leads with the
> Desk; closes with the trademark statement the 3.4 notes carry. Second-reader check: one new
> outbound destination, opt-in, named with what it receives (Open-Meteo: the city you type and
> rounded coordinates); now playing stays in memory; the MCP reads a Claude Code folder only
> once you tick it. No new capability (Package.appxmanifest unchanged since 3.4.3 except the
> version).

---

## English

```
Version 3.5.0

- The Desk: a clock, your usage meters, your own messages, now
  playing, CPU, memory and disk, and weather drawn on your desktop
  behind your windows. Off by default; turn it on in Settings > Desk,
  then drag pieces where you want them. Weather comes from Open-Meteo
  for a city you type: only the city name and rounded coordinates are
  sent. Now playing stays in memory.
- No Claude account? Choose Just the Desk on the first-run card.
- What's New after each update, and a short tour on a new install.
- A shortcut opens Settings from any app (off until you set it).
- Pick what the tray number shows: the highest limit, session, week,
  or a rotation. Hide a temporary limit until it resets.
- Settings has a grouped sidebar and search.
- A Claude Code page: link folders to accounts, choose which folders
  the MCP server may read, and install the statusline, MCP server and
  meters mod with a backup and exact undo.
- An optional glow on the window you are using when Claude Code needs
  you. Off by default.
- Reset Sanduhr in Settings > About removes what you pick, then quits.

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used
nominatively. Sanduhr für Claude is an independent third-party tool, not
affiliated with, endorsed by, or associated with Anthropic PBC.
```

---


> **RECOVERED 2026-09-13, verbatim.** Every fenced block below is poured from the LIVE published
> submission (1152921505701449114, v3.3.0.0) through the submission API, which is the only source
> that returns features, keywords and copyright. Screenshots were downloaded from the public
> catalog and ordered by joining their captions against the submission, not guessed. Graphics
> are the repo's own promotional files (Sanduhr_f-r_Claude @ 1a49b23), matching the live
> listing's file names.
>
> This sheet is approved NEXT-SUBMISSION copy only once Este says so. Until then it mirrors the
> Store, defects included, so a plan diff against the live listing is empty.

## Touch-ups needed

> Applied 2026-09-13 on Este's go: 1 (copyright), 3 (MIT license terms), 2 (23 features trimmed to 20, markdown residue and duplicates removed, 3.4.0 items added), 5 and 6 (captions 1 and 2), 7 (the 3.4.0 what's-new opens with the version), plus the privacy sentence in the description reworded for the optional publish. Original findings kept below for the record.

1. **The copyright field is truncated mid-disclaimer.** Live text is exactly 200 characters and
   ends "is an independent third-party tool and i". Same defect RoRoRo carried, with Anthropic as
   the trademark holder. The full non-affiliation statement survives only because the release
   notes repeat it. A version that fits, for Este's approval rather than applied:
   *© 2026 626Labs LLC. Licensed under MIT. "Claude" and "claude.ai" are trademarks of Anthropic
   PBC. Sanduhr für Claude is not affiliated with or endorsed by Anthropic PBC.*
2. **23 product features live against a stated cap of 20.** Either the Store accepts more than
   the form says, or three are not rendering. Unverified; check the live page.
3. **Additional license terms is EMPTY** while the copyright line says "Licensed under MIT".
4. **6 of 7 keyword slots used.**
5. **Screenshot 2 has no caption**, which is also its alt text. The sheet carries a placeholder
   so captions stay aligned by position; the placeholder is never written to the Store, since
   the API path never touches media.
6. **Screenshot 1's caption says "Windows v2.3.0"** on a 3.3.0.0 listing.
7. **The what's-new does not open with the version.** The engine warns on this; the next
   submission prepends a version line per the house rule.
8. **The catalog copies of the screenshots are small** (277x115 to 875x673), under the 1366x768
   the form states. They may be catalog downscales rather than the originals. If screenshots are
   ever re-uploaded, use full-resolution captures from the repo, not these.
9. **No reviewer letter on file.** `notesForCertification` is not readable through the API.
   Pull it from Partner Center at the next submission; the repo's
   `docs/ms-store-submission-playbook.md` carries the Notes-to-Publisher template and the
   10.1.4.4 history.

## Short description

```
Native Windows 11 widget that projects when you'll hit your Claude usage cap — burn rate, pace markers, live token-burn, glass themes. Meters need a Claude Pro/Team/Enterprise plan; the desktop Desk works without one. Independent tool, not affiliated with Anthropic.
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

No Claude account? Choose Just the Desk and use Sanduhr's Desk on its own: a clock,
your messages, now playing, system readings and weather drawn on your desktop. Sign in
any time to bring back the Claude meters.

PRIVACY BY DESIGN

Your credentials live only in Windows Credential Manager (service com.626labs.sanduhr).
Sign out to remove them; uninstalling alone leaves them in place. No account is created with 626Labs. No 626Labs
server, no telemetry, no analytics, no ads. Sanduhr reads claude.ai — and your local Claude Code
session logs — using your own session. Nothing about your usage comes back to us.
Weather on the Desk is opt-in: it asks Open-Meteo, a third-party forecast service, using only
the city name you type and that place's coordinates rounded to about 1 km; nothing about your
usage goes with it.
If you want it somewhere, send it to yourself: an optional daily publish, off by default,
posts token counts per project name to an endpoint you choose.

—

Independent third-party tool. Sanduhr für Claude is not affiliated with, endorsed by,
or associated with Anthropic PBC. "Claude" and "claude.ai" are trademarks of Anthropic
PBC, used nominatively to describe what this tool integrates with.

Built by 626 Labs for Claude power users who want to pace themselves, not just track.
```

## Product features

```
Per-model weekly meters: your Claude allowance per model tracked automatically; when Anthropic adds a limit for a new model its meter appears with no app update
Pace markers with ahead / on-pace / under scoring and a 2-hour sparkline on every tier
Burn-rate projection: when you will hit each cap at your current pace
Live token-burn delta on every tier card, read from your local Claude Code logs to bridge the lag of Anthropic's usage endpoint
Claude Usage tab: today and 30-day totals with a sent/received split, a sessions ledger by project and agent, weekly trends, a usage calendar, CSV export
Opt-in local usage vault: history survives Claude Code's 30-day log cleanup; totals only, stored on your machine, erasable any time
Usage alerts: a toast and a soft chime at your warn and urgent thresholds and at 100%
Routines tier: daily run quota for Claude Code's cloud-hosted scheduled runs, shown as a count card
Statusline and MCP bridge: the widget writes a usage snapshot that the Claude Code statusline and the Sanduhr MCP tools read
Optional daily publish, off by default: send token counts per project name to an endpoint you choose, with per-home consent
Cards tab: drag to reorder tier cards, uncheck to hide
Five built-in themes plus unlimited user themes via JSON or an AI prompt from a vibe or reference image, applied live across every open dialog
Windows 11 Mica glass backdrop with a Windows 10 solid-color fallback
Deep-work focus hourglass and a cooldown snake game while you wait for the reset
Guided sign-in including Google accounts: email-code login inside the app, no cookie hunting
Credentials stored in Windows Credential Manager, removed when you sign out
Full keyboard shortcuts with an in-app Help tab
Optional Desk, off by default: a clock, your usage meters, now playing and weather drawn on your desktop behind your windows
Just the Desk: use the Desk without a Claude account, and sign in any time to bring back the Claude meters
Independent third-party tool: no telemetry, no analytics, no ads, and nothing about your usage comes back to us
```

## Keywords

```
Claude subscription monitor
Claude desktop widget
glassmorphism windows widget
Claude usage tracker
Sanduhr for Claude
```

## Copyright

> LIVE and truncated at exactly 200. See touch-up 1.

```
© 2026 626Labs LLC. Licensed under MIT. "Claude" and "claude.ai" are trademarks of Anthropic PBC. Sanduhr für Claude is not affiliated with or endorsed by Anthropic PBC.
```

## Trademark info

> Not a separate Store field. This is the full statement from the live release notes, kept
> here as the reference the description and copyright line should agree with.

```
Independent third-party tool. Sanduhr für Claude is not affiliated with,
endorsed by, or associated with Anthropic PBC. "Claude" and "claude.ai" are
trademarks of Anthropic PBC, used nominatively to describe what this tool
integrates with.
```

## Additional license terms

> Applied 2026-09-13 (touch-up 3): the copyright line says MIT, so the field carries the MIT text.

```
MIT License

Copyright (c) 2026 626Labs LLC

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## Developed by

```
626Labs LLC
```

## Screenshot captions

> Captions are media fields: the API writer never sends them. Captions 1 and 2 below were changed 2026-09-13 and must be pasted by hand in Partner Center with the package.
> One per line, in carousel order. Position 2 is a placeholder; see touch-up 5.

```
Every Claude tier at a glance, with pace markers and a burn-rate projection
Claude Code token burn, live from your local session logs
Cooldown to even pace.
Matrix Theme
Simplified prompt based theming.
Obsidian Theme
Snakes, why'd it have to be snakes?
reorganize tier cards
Add Accounts
Tracks surplus to maximize use.
```
