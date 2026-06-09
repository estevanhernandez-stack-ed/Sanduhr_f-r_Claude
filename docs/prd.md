# Sanduhr — .NET Rebuild PRD (parity)

- **Date:** 2026-06-06 · parity rewrite, not greenfield. Pairs with `docs/scope.md`.
- **Source of requirements:** the **shipped Python app + its 286 tests are the binding acceptance criteria.** The original PRD (`docs/_archive/prd.md`, US-1..US-7) still holds; this doc layers on the features added since (multi-account, history, Routines, Local CC, focus timer, game, themes/sounds) plus the rebuild deltas. When this PRD and a Python test disagree, the test wins.

## Requirements (all PARITY unless marked NEW)

- **US-1..US-7 (from `_archive/prd.md`):** at-a-glance tier usage, pacing (ahead/on/under + burn projection), reset countdown + datetime, sparklines, themes, compact mode, "Use Sonnet" link.
- **Multi-account:** Credential Manager slots (`sessionKey:{label}`, `cf_clearance:{label}`), active-account switch, per-account history, account-scoped sign-out.
- **Tiers:** five_hour, seven_day + sonnet/opus/cowork/omelette/oauth_apps, extra_usage, **Routines** (count card), speculative-tier "future use" tags, drag-reorder + hide.
- **Advanced pacing:** cooldown, surplus, pace ghost, burn projection, velocity. *(cert-load-bearing)*
- **Focus timer** (deep-work hourglass) + **cooldown game**. *(cert-load-bearing "unique lasting value")*
- **History:** 30-day per-tier charts, per-account / all-accounts overlay, CSV export.
- **Local CC:** read local Claude Code session JSONLs for live token-burn delta vs the lagging `/usage` endpoint.
- **Glass widget:** borderless top-most Mica panel, pin/float, frame persistence, taskbar-icon binding.
- **Themes** (5 palettes + user JSON drop-ins) + **sound chimes**.
- **Tier badge** (NEW this session, PR #25 design): footer plan badge + rotating easter-egg tooltip.
- **Auto-start** (NEW this session, PR #26 design): off-by-default opt-in.
- **Embedded "Sign in to Claude" login (NEW — the headline):** user logs in inside an app-owned WebView2 window; Sanduhr reads `sessionKey` from its own CookieManager. Manual paste retained as fallback. This is the reason for the rebuild — non-technical users, zero DevTools.

## Data-compat requirement (load-bearing)

The .NET build reads/writes the **same** `%APPDATA%\Sanduhr\` files (settings.json, history.{account}.json, themes/, sounds/) and the **same** Windows Credential Manager slots as the Python build, so an existing user who updates **keeps their accounts, history, and settings** with zero migration. Schemas match the Python app.

## Acceptance

1. Ported xUnit suite (the 286 Python test intents) green.
2. Feature-for-feature parity with the shipped widget.
3. Non-technical user: "Sign in to Claude" → logged in → tracking, no DevTools.
4. Existing user's `%APPDATA%\Sanduhr\` data + Credential Manager accounts carry over untouched.
5. Clears MS Store 10.1.4.4; ships dual-channel (Store MSIX + GitHub/Velopack).
