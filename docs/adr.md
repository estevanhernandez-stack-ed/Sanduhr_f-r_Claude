# Architecture Decision Records — Sanduhr für Claude

## ADR-001: Single-file Python architecture

**Decision:** Ship the entire application as one Python file (`sanduhr.py`, ~615 lines).

**Context:** This is a rapid-ship desktop widget, not a framework. Distribution is "clone and run." Splitting into modules adds import paths, packaging complexity, and zero user value for a tool this size.

**Consequence:** Refactoring to multi-file becomes necessary if the codebase exceeds ~1,500 lines. Currently well under that threshold.

---

## ADR-002: cloudscraper over plain requests

**Decision:** Use `cloudscraper` for all HTTP calls to claude.ai.

**Context:** Plain `requests` with the session cookie returns Cloudflare 403s. Three approaches were tested in sequence:
1. `requests` with cookie header — blocked by Cloudflare
2. `browser_cookie3` to extract Chrome cookies directly — Chrome locks its cookie DB on Windows
3. `cloudscraper` — handles Cloudflare JS challenges transparently

Solved the problem in under 10 minutes. cloudscraper is the only external dependency.

**Risk:** Cloudflare may change challenge methods. cloudscraper tracks upstream changes independently. If it breaks, the fix is `pip install --upgrade cloudscraper`.

---

## ADR-003: Burn rate warns only before reset

**Decision:** The burn rate projection only displays a warning when projected exhaustion occurs *before* the usage period resets.

**Context:** First implementation said "Hits 100% in ~70h" which caused false alarm — the reset was in 3 days, so usage would never actually hit 100%. Reframed to compare projected exhaustion time against reset time: if exhaustion comes first, warn; otherwise, stay silent.

**Consequence:** No false alarms. Users only see burn rate warnings when they need to change behavior.

---

## ADR-004: tkinter over Electron/web

**Decision:** Use tkinter (Python stdlib) for the GUI instead of Electron, web, or a native framework.

**Context:** Goals were: single file, cross-platform, zero build step, minimal dependencies. tkinter ships with Python. Electron would add 200MB+ of Chromium. A web app would need a server. tkinter gets the widget to "clone and run" in one command.

**Tradeoff:** No true glassmorphism blur — simulated via muted colors and alpha transparency. Visual quality is good enough for a developer tool; wouldn't pass for a consumer app.

---

## ADR-005: Graceful in-place UI updates

**Decision:** Tier widgets are created once and updated via `configure()` and `place()` — never destroyed and recreated on data refresh.

**Context:** Early version rebuilt the entire widget tree on each 5-minute refresh, causing visible flicker (widgets disappear then reappear). Switched to in-place updates where labels, bars, and markers are reconfigured with new values. Stale tiers (tiers that disappear from the API response) are destroyed individually.

**Consequence:** Zero flicker on refresh. The UI feels alive rather than rebuilt.

---

## ADR-006: Gemini tracking deferred

**Decision:** Defer Antigravity/Gemini IDE quota tracking to a future version.

**Context:** The original scope included Gemini API usage tracking. Investigation revealed that Google AI Studio's quota API uses gRPC-Web with Google-internal auth that can't be replicated with a session cookie approach. The auth wall is real and would require a different architecture (OAuth, service account, or scraping).

**Consequence:** Listed on roadmap. Will revisit if Google exposes a public quota API or if an OAuth flow is worth the complexity.

---

## ADR-007: Theme system as flat dicts

**Decision:** Themes are stored as flat dictionaries with 14 named color keys each, not as a class hierarchy or CSS-like system.

**Context:** 5 themes, 14 colors each. A flat dict is the simplest structure that works. Every widget reads `self.t["key"]` directly. Theme switching rebuilds the entire widget tree (acceptable cost since it's user-initiated, not on refresh).

**Consequence:** Adding a new theme is one dict. Changing a color is one line. No abstraction overhead.
