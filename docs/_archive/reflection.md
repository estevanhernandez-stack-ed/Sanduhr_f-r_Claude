<!-- This reflection is AI-generated. It ships with the project
     as part of the documentation. The note at the top is intentional
     and should not be removed. -->

# Reflection — Sanduhr fur Claude

> **Note:** This feedback is AI-generated. It's observational, not authoritative. Use what's useful.

## Scope & Idea Clarity

**What landed:** The scope doc nails the user in one line: "Power users who live in Claude Code, Cowork, or AI Studio all day and want a glanceable dashboard." That's specific and buildable. The cuts are real too — Chrome extension, Electron, system tray, auto-start all named and killed with rationale. No hedging.

**What to tighten:** The scope included Gemini API tracking as a goal ("Track Gemini API usage — quota consumption, billing tier, cost") but the investigation hit a wall (gRPC-Web auth). Future scopes should flag technical unknowns as risks, not goals, until the API access is confirmed. "Investigate Gemini quota API feasibility" is more honest than "Track Gemini API usage."

## Requirements Thinking

**What landed:** The 7 user stories map cleanly to shipped features. US-2 (pacing) has genuinely testable acceptance criteria — "Linear pace calculation comparing elapsed time fraction to usage fraction" and "Burn rate projection warns if usage will exhaust before reset." You can look at the widget and verify each one.

**What to tighten:** The PRD was written post-build as documentation, not as a planning tool. That means it describes what exists rather than what should exist — no edge cases were surfaced through the PRD process (e.g., what happens when the session key expires mid-refresh? what if the API returns unexpected tier keys?). The code handles these, but the PRD didn't drive that thinking. On a bigger project, writing requirements first catches gaps cheaper.

## Technical Decisions

**What landed:** The cloudscraper pivot was the most consequential technical decision of the night. The initial approach (plain `requests` with session cookie) hit Cloudflare's 403. Rather than abandoning the architecture, the builder tested `browser_cookie3` (failed — Chrome locks its DB), then pivoted to cloudscraper in a single test script. Problem identified, three approaches tried, solution confirmed in under 10 minutes. The spec documents this decision path clearly.

**What to tighten:** The burn rate algorithm went through two iterations before the framing was right. First version said "Hits 100% in ~70h" which was technically correct but misleading — the user would panic even though the reset was in 3 days. The builder caught this and reframed it to only warn when exhaustion happens BEFORE reset. This kind of UX-aware algorithm design should happen in the spec phase, not during live testing. A "how will this read to the user?" pass on the spec's algorithm section would catch framing issues earlier.

## Plan vs. Reality

**What landed:** All 7 user stories shipped and are verifiable in the running widget. The build also delivered features that weren't in the original scope — sparklines, burn rate projection, compact mode, 5 themes (scope said "a couple"), and the "Use Sonnet" link. Scope expanded during the build and the builder drove every expansion.

**What to tighten:** The process ran in reverse — build first, then scope, PRD, and spec as post-hoc documentation. This worked for a small, single-session project where the builder had full clarity going in. But the docs became a paper trail rather than a planning tool. The VC process adds the most value when the docs drive the build, not when they describe it after the fact. The builder acknowledged this directly: "This plugin is good for adapting apps to the correct workflow, especially in prototype stage."

## How You Worked

**What landed:** The builder drove every meaningful decision. Expanded scope to Gemini unprompted. Caught the decimal display issue, the burn rate framing problem, and the theme visibility issue before being prompted. Course-corrected with surgical precision — "I don't want the decimal," "the burn rate might scare users," "the theme button is hard to see." Every correction made the product better. Zero deepening rounds needed because the vision was already fully formed.

**What to tighten:** The compressed flow meant some VC commands ran as documentation exercises rather than discovery tools. That's fine for a 3-hour rapid build, but the builder should be aware of the tradeoff: when you document after building, you lose the "the spec caught a problem before I coded it" benefit. On the LADDER project (11-feature autonomous build), the full sequential flow produced a cleaner build. Match the process depth to the project stakes.

## Your Goals

You said you wanted to "ship tonight as a working, installable tool people can grab from a blog post." You shipped. The widget is live on GitHub with a README, a dev post draft, and a Discord-length version ready to paste. It runs, it's polished, it has 5 themes and features (burn rate, sparklines, pace markers) that go beyond what most v1 dev tools ship with.

The Antigravity tracking didn't land — that was an ambitious reach goal and the technical wall (gRPC-Web + Google internal auth) was real. You investigated it thoroughly, documented the findings, and deferred to v2. That's the right call.

## Reflection

The builder observed that Vibe Cartographer adapts well to different project stages — it can formalize a prototype's decisions just as well as it can plan a greenfield build. The key insight from this session is that the VC process is flexible enough to run in reverse (build → document) without losing its value as a documentation and review framework.

The biggest platform finding: `/clear` doesn't exist in Cowork mode. The VC plugin assumes context resets between commands, but in Cowork the full conversation persists. This needs a Cowork-aware branch in the plugin's handoff instructions — either skip the `/clear` prompt or replace it with "move to the next command."

The builder also identified the `/evolve` command as a missing piece. Session logs are being collected (Level 2 of the Self-Evolving Plugin Framework) but the command that reads them and proposes improvements (Level 3) hasn't been built yet. Tonight's session data is captured for when it ships.

## Milestone Completion

- /onboard: complete
- /scope: complete
- /prd: complete (post-build)
- /spec: complete (post-build)
- /checklist: skipped (build was iterative, not checklist-driven)
- /build: 7/7 user stories shipped
- /iterate: 4+ polish rounds (glassmorphism, themes, pace markers, burn rate reframe, bar height, Matrix theme)
- /reflect: complete
