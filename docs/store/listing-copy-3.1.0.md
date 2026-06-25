# Store listing copy — v3.1.0

Public-facing copy for the Partner Center listing. Distinct from the **Notes for certification**
(that's [`reviewer-letter-3.1.0.0.md`](./reviewer-letter-3.1.0.0.md), reviewer-only).

The **Description** below is the cert-passed v2.3 copy, kept verbatim except for the 3.1 deltas
called out in `> NOTE` lines (the first-run sign-in flow, the new bottom tool strip). The
**disclaimer is unchanged** from the approved listing — do not reword trademark language on a
resubmission.

---

## What's new in this version

```
Sanduhr is rebuilt from the ground up as a native Windows app — same Sanduhr,
faster and lighter, with a self-contained installer that needs no extra runtime.

Easier sign-in
- Most sign-in methods now work on the real Claude page right inside the app —
  no DevTools, no cookie hunt.
- Signing in with Google? Google blocks that inside apps, so Sanduhr now walks
  you through a quick session-key paste with clear, step-by-step guidance.
- If your session ever expires, a one-tap card signs you back in — your history
  and accounts stay put.

Also new
- The taskbar button now follows your pin: pinned and on top, it stays out of your
  taskbar; unpinned, it's there when you need it.
- Sharper, native window chrome.

Everything you already rely on — burn-rate projection, the pace ghost, the 30-day
graph, the focus timer, your themes, multi-account, and local Claude Code
token-burn — carries over unchanged.

Independent third-party tool. Sanduhr für Claude is not affiliated with, endorsed
by, or associated with Anthropic PBC. "Claude" and "claude.ai" are trademarks of
Anthropic PBC, used nominatively to describe what this tool integrates with.
```

---

## Short description (~200 char — shows in search results)

```
Know when you'll hit your Claude weekly cap before you do. A native Windows 11 glass
widget that turns your claude.ai usage into burn-rate, pace, and 30-day history at a glance.
```

---

## Description (full — cert-passed v2.3 copy + 3.1 deltas)

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
on the real Claude page right inside the app — no DevTools needed. (Signing in with
Google? Google blocks that inside apps, so Sanduhr walks you through a quick one-time
session-key paste, step by step.) That's it.

PRIVACY BY DESIGN

Your credentials live only in Windows Credential Manager (service com.626labs.sanduhr)
and are wiped when you uninstall. No account is created with 626Labs. No server, no
telemetry, no analytics, no ads. Sanduhr reads claude.ai — and your local Claude Code
session logs — using your own session; nothing about your usage ever leaves your
machine.

—

Independent third-party tool. Sanduhr für Claude is not affiliated with, endorsed by,
or associated with Anthropic PBC. "Claude" and "claude.ai" are trademarks of Anthropic
PBC, used nominatively to describe what this tool integrates with.

Built by 626 Labs for Claude power users who want to pace themselves, not just track.
```

> Deltas from the approved v2.3 description:
> 1. **WHAT YOU NEED** — "one-time paste of your claude.ai session cookie" → in-app sign-in on the
>    real Claude page for most methods, with **Google routed to a guided session-key paste** (Google
>    blocks OAuth in embedded webviews — the gap is real, so we name it). The old blanket "paste"
>    wording was wrong for non-Google users; the new copy is honest both ways.
> 2. **"Built to live on your desktop"** — prepended "every feature one click away on the bottom
>    tool strip" so the public copy matches the new navigation (and the screenshots).
> 3. Dropped the stale "(v2.3.0)" tag on the Claude Code token-burn bullet.
>
> Everything else — including the disclaimer — is the approved copy, untouched. If you want the
> graph-cycle (Classic/Horizon) or themed dialogs sold explicitly, say so and I'll add a bullet;
> I kept the touch light on cert-passed text.

---

## Keywords (search terms)

```
claude, claude.ai, usage, usage tracker, rate limit, token usage, AI usage,
widget, desktop widget, burn rate, focus timer, productivity
```

## Screenshots to capture (replace the stale 2.x shots)

1. Default widget — rings/bars + the bottom tool strip visible
2. A theme swap (the swatch flyout open, or a vivid theme applied)
3. The Horizon graph mode
4. Compact mode (collapsed to the busiest tier)
5. The focus-timer hourglass
6. (optional) The themed sign-in or settings dialog

Minimum 3, ideally 6. 1366×768 or larger; PNG.
