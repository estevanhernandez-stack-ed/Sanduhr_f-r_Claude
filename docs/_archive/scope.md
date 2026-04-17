# Claude Usage Tracker

## Idea
A standalone desktop widget that shows your Claude.ai (and Gemini API) usage with pacing indicators, so power users can manage their rate limits without opening a browser.

## Who It's For
Claude Pro/Team/Enterprise subscribers and Gemini API developers who burn through their usage limits. Power users who live in Claude Code, Cowork, or AI Studio all day and want a glanceable dashboard showing how fast they're consuming their quota — and whether they need to slow down to make it through the week.

Distribution: DevPost, GDC Discord general chat, GitHub repo link.

## Inspiration & References
- Claude.ai settings/usage page (https://claude.ai/settings/usage) — the source data
- Google AI Studio billing dashboard — Gemini usage/cost tracking
- Rainmeter desktop widgets — always-on-top, minimal, glanceable
- macOS menu bar apps (like iStat Menus) — info-dense, unobtrusive

Design energy: Dark theme matching Claude's aesthetic. #1a1a1a backgrounds, muted text (#c4c0b8), color-coded progress bars (green/yellow/orange/red). Clean, functional, high-contrast. No chrome, no fluff.

## Goals
1. Ship tonight as a working, installable tool people can grab from a blog post
2. Track Claude.ai subscription usage (session limits, weekly limits, Sonnet/Opus breakdowns)
3. Track Gemini API usage (quota consumption, billing tier, cost)
4. Show pacing — are you ahead or behind for the week?
5. Secure — no hardcoded secrets, encrypted config, clear setup instructions
6. Easy install — single Python file, auto-installs dependencies, cross-platform

## What "Done" Looks Like
A polished Python desktop widget that:
- Shows Claude usage bars with countdowns and pacing for all active tiers
- Shows Gemini API usage (requests remaining, tokens used, cost if available)
- Has a settings UI for entering/updating API keys securely
- Looks professional enough to screenshot for a dev post
- Has a clear README with GIF/screenshot, one-line install, and setup instructions
- Lives on GitHub as a public repo

## What's Explicitly Cut
- Chrome extension version — not needed, this is standalone
- Electron/web app version — too heavy, Python is the right call
- Claude API (Anthropic developer API) usage — different audience, different auth. Could add later.
- System tray mode — nice-to-have, not tonight
- Auto-start on boot — nice-to-have, not tonight
- Historical usage charts — just show current state

## Loose Implementation Notes
- Python 3 + tkinter (stdlib) for UI — no heavy deps
- cloudscraper for Claude.ai API (bypasses Cloudflare)
- google-auth + google-api-python-client OR direct REST calls for Gemini quota/billing
- Config at ~/.claude-usage-widget/config.json — store session key + Gemini API key
- Consider keyring library for OS-level secret storage (more secure than plaintext JSON)
- 5-minute auto-refresh cycle
- Draggable, always-on-top, frameless window with custom title bar
