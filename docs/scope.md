# Sanduhr — .NET 10 / WPF Rebuild — Scope

- **Date:** 2026-06-06
- **Type:** Full-parity platform rewrite of a shipped product (not greenfield).
- **Decision of record:** dashboard decision `p94zZYRThNY4M3asqss2`; bridged to the Architect for the strategic read.
- **Lineage:** Sanduhr was originally Cart-built (Python). Original artifacts in `docs/_archive/{scope,prd,spec,reflection,test-plan}.md`. This re-Carts it onto .NET.

## Idea

Rebuild Sanduhr's **Windows** build on **.NET 10 / WPF** to **full behavioral parity** with the shipped Python/PySide6 app, and add an **embedded WebView2 "Sign in to Claude" login** as the primary credential-capture path. The next Microsoft Store submission is the .NET build; the Python `windows/` tree stays as reference until parity is reached, then becomes legacy. The macOS SwiftUI build is untouched.

## User

- Existing Sanduhr users (Claude subscribers watching their usage burn-down).
- **The non-technical user who could not get past the `sessionKey` copy-paste** — the person this rebuild is *for*. "Sign in to Claude," no DevTools.
- 626 Labs brand-spread: every install carries the brand to a non-626 audience.

## Problem

1. **Credential capture is a dead end for non-technical users.** sessionKey lives behind DevTools; browser-cookie auto-import is blocked (verified spike: Chrome + Edge both run App-Bound Encryption — `v20`, key bound to the browser exe via the SYSTEM elevation service; CDP backdoor closed in Chrome 136; only Firefox is clean, and the user is on Chrome/Edge). The clean path is to **own the cookie jar** via an embedded login window — native on .NET WebView2, ~100MB-painful on PySide6.
2. **Sanduhr is the Python odd-one-out** in a .NET 10 desktop family (RORORO, Ur-OCR, rororo-ur-task, 626-mod-launcher). Every release adapts the playbook instead of lifting it. Unifying on .NET compounds tooling, skills, and the release runbook.

## In scope — FULL PARITY + the new front door

Everything the Python app does, rebuilt in .NET to behavioral parity, **plus** embedded login and this session's two designed features. No paring down ("100% + parity").

**Parity (ported from Python, verified against the 286-test behavioral spec):**
- claude.ai usage fetch — `/organizations` (org + `rate_limit_tier`/`billing_type`/`capabilities`) + `/organizations/{id}/usage`; Cloudflare-aware (cloudscraper → HttpClient + CF handling); typed errors (session-expired / CF-blocked / network).
- Tier cards — `five_hour`, `seven_day` + sub-tiers (sonnet/opus/cowork/omelette/oauth_apps), `extra_usage`, **Routines** daily-quota (count card), speculative-tier "future use" tags; utilization + reset countdowns; drag-reorder + hide.
- **Advanced pacing** (pure math ports: `pace_frac`, `pace_info`, cooldown, surplus, `burn_projection`, velocity) — *cert-load-bearing*.
- **Focus timer** (deep-work hourglass) — *cert-load-bearing*.
- **Cooldown game** (snake) — kept (full parity; part of the original "unique value" story).
- Glass/Mica floating widget — borderless top-most WPF panel + DWM Mica (native in WPF), pin/float toggle, frame persistence (move-only), taskbar-icon binding.
- Themes (5 palettes + user JSON drop-ins) + custom sound chimes.
- Multi-account (Windows Credential Manager slots, switch active, per-account history, account-scoped sign-out).
- 30-day history + charts (per-account / all-accounts overlay); CSV export.
- Local CC log reader (token-burn delta vs the lagging `/usage` endpoint).
- **Subscription tier badge** — this session's design (PR #25): read `rate_limit_tier`, footer badge + rotating easter-egg tooltip.
- **Auto-start on boot** — this session's design (PR #26): off-by-default; native MSIX `windows.startupTask` + the unpackaged path.

**New:**
- **Embedded WebView2 "Sign in to Claude" login** — user logs in inside an app-owned WebView2 window; Sanduhr reads `sessionKey` from its own `CoreWebView2` CookieManager. Manual sessionKey paste retained as a fallback. (RORORO's `CookieCaptureWindow` pattern.)

## Constraints

- **Stack:** .NET 10 LTS + C# + **WPF** (RORORO's documented call — tray + Win32 + Mica interop more battle-tested than WinUI 3; Sanduhr is exactly that shape) + WebView2 + Velopack (auto-update) + MSIX `wapproj` + xUnit.
- **Location:** new `windows-dotnet/` dir in the `Sanduhr_f-r_Claude` repo, beside `windows/` (Python) + `mac/` (Swift). **Same Store identity** `626LabsLLC.SanduhrfrClaude` / Publisher `CN=177BCE59-...`.
- **Parity bar:** the 286 Python tests' intents ported to xUnit; pure logic (pacing/tiers/usage shapes) is the highest-fidelity port.
- **Cert:** MUST clear MS Store **10.1.4.4 (unique lasting value)** — the focus timer + advanced pacing + game carry the "pacing companion, not just a display" weight that earned the original pass. Do not regress to "just a usage display."
- **Reuse RORORO 1:1:** `CookieCaptureWindow`, Velopack, MSIX wapproj, the freshened release playbook (4th-version-component-MUST-be-`.0`; Partner Center; reviewer letter; draft-release discipline; listing "What's new" step), GitNexus. Merge with Sanduhr's own `docs/ms-store-submission-playbook.md`.
- **Versioning:** the .NET build likely takes **v3.0.0** to signal the platform shift (TBD at release).

## Explicit cuts / not in scope

- **No new features** beyond embedded login + the badge + auto-start. Parity + those three only — no scope creep.
- **Mac SwiftUI build:** untouched, separate track.
- **Shared "626 .NET desktop starter"** extraction: out of scope for this effort (it's the Architect's open strategic question — if it says "extract," it informs *how* we structure the shared bits, but the rebuild proceeds regardless; do not block on it).
- **winget manifest, Antigravity quota tracking:** still deferred.
- **Python v2.4 (badge PR #25, auto-start PR #26):** not shipped to the Store — they are *parity inputs*, not a release.

## Success criteria

1. .NET build reaches **behavioral parity** — the ported xUnit suite is green and the widget matches the Python app feature-for-feature.
2. **A non-technical user captures their session with zero DevTools** — "Sign in to Claude" → login → tracking, end to end.
3. **Clears MS Store cert** (10.1.4.4) and ships as the next submission, dual-channel (Store MSIX + GitHub/Velopack) per the playbook.
4. Mac SwiftUI build still builds + ships independently (untouched).

## Next (downstream Cart stages)

`spec` (project structure + per-module Python→C# map + WebView2 login design + MSIX/Velopack/GitNexus setup) → `checklist` (sequenced build plan, milestone-gated) → `build`. Scaffold `windows-dotnet/` as the first build step.
