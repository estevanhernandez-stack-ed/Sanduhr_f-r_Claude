# Architecture Decision Records — Sanduhr für Claude

## Format

Each entry carries these five fields, one line each, in this order after the heading.

- **Status:** accepted, or superseded with a pointer to what replaced it.
- **Context:** the situation and the options that were on the table.
- **Decision:** what was chosen, in one sentence.
- **Consequences:** what exists, or no longer exists, because of the decision.
- **Date:** the day the decision was made (ISO date).

The seven entries below predate this block and carry no date; their Status and Consequences lines were added on 2026-10-17 from the entry text and the code.

## ADR-001: Single-file Python architecture

**Status:** superseded. The single-file `sanduhr.py` was retired on 2026-09-13 (kept at tag `legacy/python-v2.3.0`, see `CLAUDE.md`); the shipping apps are the three-project `windows-dotnet/` solution and the `mac/` SwiftUI app, by way of the modular PySide6 package in `docs/generated/adr.md` ADR-001 and ADR-003.
**Decision:** Ship the entire application as one Python file (`sanduhr.py`, ~615 lines).

**Context:** This is a rapid-ship desktop widget, not a framework. Distribution is "clone and run." Splitting into modules adds import paths, packaging complexity, and zero user value for a tool this size.

**Consequence:** Refactoring to multi-file becomes necessary if the codebase exceeds ~1,500 lines. Currently well under that threshold.

**Consequences:** The threshold was crossed by the rewrite rather than by growth: nothing current depends on the single file, and `windows-dotnet/src` is split into `Sanduhr.App`, `Sanduhr.Core` and `Sanduhr.Mcp`.

---

## ADR-002: cloudscraper over plain requests

**Status:** superseded. `cloudscraper` left with the Python apps; `windows-dotnet/src/Sanduhr.Core/ClaudeApiClient.cs` calls claude.ai through `HttpClient` and `mac/Sources/Sanduhr/Services/ClaudeAPI.swift` through `URLSession`, both with an optional `cf_clearance` cookie instead of a challenge solver.
**Decision:** Use `cloudscraper` for all HTTP calls to claude.ai.

**Context:** Plain `requests` with the session cookie returns Cloudflare 403s. Three approaches were tested in sequence:
1. `requests` with cookie header — blocked by Cloudflare
2. `browser_cookie3` to extract Chrome cookies directly — Chrome locks its cookie DB on Windows
3. `cloudscraper` — handles Cloudflare JS challenges transparently

Solved the problem in under 10 minutes. cloudscraper is the only external dependency.

**Risk:** Cloudflare may change challenge methods. cloudscraper tracks upstream changes independently. If it breaks, the fix is `pip install --upgrade cloudscraper`.

**Consequences:** The only external dependency is gone, and what survived is the error split it forced: `ClaudeApiClient.cs` carries `SessionExpiredException`, `CloudflareBlockedException` and `NetworkException` as a 1:1 port of the Python `api.py` errors, and `ClaudeAPI.swift` carries the same cases.

---

## ADR-003: Burn rate warns only before reset

**Status:** accepted.
**Decision:** The burn rate projection only displays a warning when projected exhaustion occurs *before* the usage period resets.

**Context:** First implementation said "Hits 100% in ~70h" which caused false alarm — the reset was in 3 days, so usage would never actually hit 100%. Reframed to compare projected exhaustion time against reset time: if exhaustion comes first, warn; otherwise, stay silent.

**Consequence:** No false alarms. Users only see burn rate warnings when they need to change behavior.

**Consequences:** Both ports keep the rule: `Pacing.BurnProjection` in `windows-dotnet/src/Sanduhr.Core/Pacing.cs` and `secondsUntilFull` in `mac/Sources/Sanduhr/Helpers/UsageMath.swift` return null when the projected time to 100% is at or past the reset, and the pace alert shares that core.

---

## ADR-004: tkinter over Electron/web

**Status:** superseded. tkinter left with v1; the Windows app is WPF (`windows-dotnet/src/Sanduhr.App`) and the Mac app is SwiftUI (`mac/`), after `docs/generated/adr.md` ADR-001 rejected upgrading the tkinter build.
**Decision:** Use tkinter (Python stdlib) for the GUI instead of Electron, web, or a native framework.

**Context:** Goals were: single file, cross-platform, zero build step, minimal dependencies. tkinter ships with Python. Electron would add 200MB+ of Chromium. A web app would need a server. tkinter gets the widget to "clone and run" in one command.

**Tradeoff:** No true glassmorphism blur — simulated via muted colors and alpha transparency. Visual quality is good enough for a developer tool; wouldn't pass for a consumer app.

**Consequences:** The clone-and-run, zero-build-step goals did not survive the native ports; each platform now has its own build, signing and update path (Velopack and the Store MSIX on Windows, Sparkle and a Homebrew tap on Mac).

---

## ADR-005: Graceful in-place UI updates

**Status:** superseded. The tkinter `configure()`/`place()` mechanism left with v1; the rule itself carried into `WidgetViewModel` in `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs`.
**Decision:** Tier widgets are created once and updated via `configure()` and `place()` — never destroyed and recreated on data refresh.

**Context:** Early version rebuilt the entire widget tree on each 5-minute refresh, causing visible flicker (widgets disappear then reappear). Switched to in-place updates where labels, bars, and markers are reconfigured with new values. Stale tiers (tiers that disappear from the API response) are destroyed individually.

**Consequence:** Zero flicker on refresh. The UI feels alive rather than rebuilt.

**Consequences:** `WidgetViewModel` keeps one `TierCardViewModel` per tier key, calls `Update(...)` on the existing card each refresh, and removes stale tiers individually with `Tiers.Remove`, so a refresh still never rebuilds the card set.

---

## ADR-006: Gemini tracking deferred

**Status:** accepted.
**Decision:** Defer Antigravity/Gemini IDE quota tracking to a future version.

**Context:** The original scope included Gemini API usage tracking. Investigation revealed that Google AI Studio's quota API uses gRPC-Web with Google-internal auth that can't be replicated with a session cookie approach. The auth wall is real and would require a different architecture (OAuth, service account, or scraping).

**Consequence:** Listed on roadmap. Will revisit if Google exposes a public quota API or if an OAuth flow is worth the complexity.

**Consequences:** Still deferred: `README.md` lists "Antigravity (Google Gemini IDE) quota tracking" as an open roadmap item, and neither app contains Gemini quota code (the Mac app's Gemini references are config export for Gemini CLI, not usage tracking).

---

## ADR-007: Theme system as flat dicts

**Status:** superseded. `themes.py` left with the Python apps; themes are the `ThemeDefinition` record in `windows-dotnet/src/Sanduhr.Core/ThemeModel.cs` and the `Theme`/`Palette` struct in `mac/Sources/Sanduhr/Models/Theme.swift`, plus user JSON themes (`docs/generated/adr.md` ADR-008).
**Decision:** Themes are stored as flat dictionaries with 14 named color keys each, not as a class hierarchy or CSS-like system.

**Context:** 5 themes, 14 colors each. A flat dict is the simplest structure that works. Every widget reads `self.t["key"]` directly. Theme switching rebuilds the entire widget tree (acceptable cost since it's user-initiated, not on refresh).

**Consequence:** Adding a new theme is one dict. Changing a color is one line. No abstraction overhead.

**Consequences:** The flat key set outlived the dicts: `ThemeCatalog.RequiredColorFields` in `ThemeModel.cs` lists the color keys verbatim from `themes.py` (now 15, with `glass_on_mica`) as the contract a user theme JSON must meet on both platforms.
