# Store listing — Product features (source of truth)

Partner Center → Store listing → **Product features**. Cap: **20 bullets, ~200 chars each,
plain text** (no markdown — the `**`-artifact bullets in the live listing came from a
markdown paste; this file is the paste-ready source). Sync this file at every release cut
(runbook Phase 7).

Last synced: v3.3.0.0 (2026-07-19). NEW/CHANGED vs the pre-3.3 live listing are marked; the
live listing's 18 bullets consolidate to 15 here to make room under the cap.

---

1. Burn-rate projection tells you when you'll run out before reset
2. Pace markers + ahead/on-pace/under scoring on every tier
3. NEW — Per-model weekly meters: your Claude Fable 5 allowance tracked automatically, and when Anthropic adds a limit for any new model its meter appears with no app update
4. NEW — Usage alerts: toast + soft chime at your warn/urgent thresholds and at 100%; per-model meters alert silently by default
5. NEW — Claude Usage tab: today + 30-day totals with sent/received split, a sessions ledger with per-project stacking and agent roll-up, weekly trends, a clickable usage calendar, CSV export
6. NEW — Opt-in local usage vault: your usage history survives Claude Code's ~30-day log cleanup — totals only, stored on your machine, erasable any time
7. NEW — Guided sign-in including Google accounts: email-code login right inside the app — no cookie hunting
8. Live token-burn delta on every tier card and in the footer, sourced from local Claude Code logs (agent runs included) — bridges the lag of the claude.ai usage endpoint
9. Routines tier: daily run-quota for Claude Code's cloud-hosted scheduled runs, shown as a count card (3/15)
10. 2-hour sparkline history per tier, plus history charts with gridlines, area fills, and "% left / resets in" labels
11. Five built-in themes + unlimited user themes via JSON or an AI prompt from a vibe or reference image — applied live across every open window
12. Win11 Mica glass backdrop, Win10 solid-color fallback
13. Cards tab: drag to reorder tier cards, uncheck to hide
14. Custom soft chimes for save / error / info — no Windows system beeps, ever
15. Multi-account: track Personal and Work Claude accounts in one install and switch from the widget
16. Deep-work focus timer (pixel hourglass) + cooldown snake game
17. Full keyboard shortcuts + Help tab
18. Credentials stored in Windows Credential Manager, cleared on uninstall
19. No telemetry, no analytics, no ads, no 626Labs account needed
20. Independent third-party tool; not affiliated with Anthropic

---

## Consolidation map (what happened to the old 18)

- "2-hour sparkline" + "History tab polish" → bullet 10
- "Five built-in themes…" + "AI-agent prompt to generate themes" + "Themes apply across all
  open dialogs" → bullet 11
- "Win11 taskbar icon now binds reliably" → dropped (a fix, not a feature)
- "Local CC settings tab" → superseded by bullet 5 (the tab is named Claude Usage now and
  grew the ledger/trends/calendar/split)
- Multi-account (15) and focus timer + snake (16) were in the Store description but missing
  from the live features list — added here because they're cert-load-bearing features
  (10.1.4.4 unique-value names both).
- Everything else carried over verbatim or lightly tightened.
