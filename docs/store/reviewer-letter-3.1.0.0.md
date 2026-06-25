# Notes for certification — reviewer letter (v3.1.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission
> options** → **Notes for certification**.
>
> Context the reviewer needs up front: the last version that shipped to the Store was the
> **Python/PySide6 build, v2.3**. v3.0.0 (the .NET rebuild) was prepared but never submitted, so
> **this is the first time the reviewer sees the .NET app** — it jumps v2.3 → v3.1.0 in one
> submission. The letter therefore covers BOTH deltas since v2.3: the platform rebuild (whose only
> disclosure-surface change is an embedded WebView2 sign-in) AND the feature overhaul. It is led by
> the **navigation** story (a single visible tool strip) because 10.1.4.4(c) is where the 2.x line
> was rejected twice. Same Store listing — identical Identity Name (`626LabsLLC.SanduhrfrClaude`)
> and Publisher CN — so it is an update, not a new app.

---

```
Hello reviewer,

Thank you for your time on v3.1.0.0. The last version in the Store was the
Python build, v2.3. This submission updates the SAME app (same Identity
Name 626LabsLLC.SanduhrfrClaude, same Publisher CN, same Store listing) with
two changes since v2.3: a full platform rebuild to .NET 10 / WPF, and a
feature + navigation overhaul. It is the same product — a desktop widget
that shows the signed-in user their OWN Claude.ai subscription usage — with
the same data posture. No telemetry. No new at-rest data leaves the device.
The app declares ONLY runFullTrust. The items that touch your past review
findings (navigation, dialog legibility) and the one disclosure-surface
change (embedded sign-in) are detailed first.

WHAT THE APP DOES

  Sanduhr is a floating desktop widget that displays the user's OWN
  Claude.ai usage: how much of their five-hour and seven-day limits they
  have used, a burn-rate projection, pacing markers, and 30-day history
  graphs. It also includes a deep-work focus timer (a digitised hourglass),
  a small cooldown game for the wait state, five themes plus user-authored
  themes, and Win11 Mica glass. It reads the user's usage from Claude.ai
  using the user's own logged-in session; it does not send the user's data
  anywhere else.

  "Claude" and "claude.ai" are trademarks of Anthropic PBC, used
  nominatively to describe integration. Sanduhr für Claude is an
  independent third-party tool, not affiliated with, endorsed by, or
  sponsored by Anthropic. (Disclaimer also on the Store description,
  Copyright field, manifest Description, README, About box, and privacy
  policy.)

WHAT CHANGED SINCE v2.3

  1. NAVIGATION — EVERY FEATURE ON ONE VISIBLE TOOL STRIP (addresses the
     past 10.1.4.4 navigation finding).

     Every top-level action now lives on a single, always-visible icon
     strip along the bottom of the widget: Theme, Settings, Graph, Compact,
     Focus timer, Snake, Refresh, and Pin. Nothing is reachable only via a
     right-click menu. Each button has:

     - an accessible name (AutomationProperties.Name) — screen readers and
       the review tooling read a real label, not an emoji codepoint;
     - a tooltip describing the action;
     - keyboard access for the high-traffic actions (e.g. Ctrl+D toggles
       Compact, Esc exits the focus/cooldown views).

     A right-click context menu still exists as a convenience, but it
     duplicates the strip — it is never the only path to a feature.

  2. DIALOG LEGIBILITY ON LIGHT-MODE WINDOWS (addresses the past dialog
     finding).

     All in-app dialogs were replaced with a single themed dialog that
     paints its own background and foreground from the active theme. It does
     not fall through to the system palette, so it renders legibly whether
     the host is in light or dark mode. Re-verified on a light-mode install.

  3. EMBEDDED WEBVIEW2 SIGN-IN (the only disclosure-surface change since
     the Python v2.3 build).

     The Python build asked power users to paste their claude.ai session key
     by hand (via browser DevTools). The .NET app adds a "Sign in to Claude"
     window that hosts Microsoft's WebView2 control, navigates to the real
     https://claude.ai/login, and lets the user sign in normally. Disclosure
     posture, stated plainly:

     - The login happens on Anthropic's real page inside WebView2. Sanduhr
       does NOT intercept, proxy, or read the user's password — it only
       reads back the resulting session cookie from its own cookie jar
       after a successful sign-in.
     - WebView2 runs in an app-owned, isolated user-data folder under
       %APPDATA%\Sanduhr\webview2\. It does NOT read the user's Chrome,
       Edge, or any other browser's cookie store. We own our cookie jar
       only.
     - On a successful sign-in, Sanduhr reads the claude.ai sessionKey
       (and cf_clearance if present) ONLY from the claude.ai origin, and
       stores it via the Windows Credential Manager (DPAPI-protected,
       per-user, per-machine). The cookie never lands in a file in the
       repo, a config file, or a log.
     - If a session later expires or is challenged by Cloudflare, the widget
       shows a "Sign in again" card that re-runs this same flow in place.
     - The manual session-key paste is retained as a fallback for power
       users. No browser-store prying anywhere.
     - The captured cookie is the user's own Claude.ai session, used to
       read the user's own usage. It is never transmitted to 626 Labs or
       any third party.

  4. PLATFORM REBUILD (Python/PySide6 -> .NET 10 / WPF).

     Same features, reimplemented natively. The pacing math, tier model,
     history schema, account/credential storage slots, and Claude.ai usage
     endpoints are ported 1:1 — an existing user's %APPDATA%\Sanduhr\ data
     and Credential Manager entries carry over untouched. The MSIX is
     self-contained (bundles the .NET 10 Desktop Runtime) so it installs
     cleanly on a fresh Windows box.

  5. UNIQUE LASTING VALUE — added in this release.

     On top of the rebuilt 2.x value features (burn-rate projection,
     advanced pacing, the deep-work focus hourglass, the cooldown snake,
     five themes + user-authored themes, Win11 Mica glass, OS-native
     credential storage), v3.1.0 adds: a graph mode toggle (Classic line vs
     a layered Horizon band view of the 30-day history) and a Compact mode
     that collapses the widget to its busiest limit. All reachable from the
     visible tool strip above.

WHAT STAYED THE SAME

  - The app declares ONLY runFullTrust. No broadFileSystemAccess, no
    internetClient (outgoing HTTPS needs no declaration for a full-trust
    desktop app).
  - No telemetry, analytics, or crash reporting. No data comes back to
    626 Labs.
  - The user's Claude.ai session credential is stored only in the Windows
    Credential Manager (DPAPI), never in a file or log.
  - Identity Name (626LabsLLC.SanduhrfrClaude), Publisher CN, and the
    Store listing are UNCHANGED — this is an update to the existing app.

WHY runFullTrust

  Sanduhr reads and writes its settings, 30-day history, logs, and
  user-dropped theme JSON directly under %APPDATA%\Sanduhr\ (so power
  users can hand-edit themes and inspect history), and it calls the
  Desktop Window Manager API to render the Win11 Mica backdrop. A
  sandboxed MSIX process can do neither. No other elevated capability is
  requested.

TRADEMARK NOTICE

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used
nominatively to describe integration. Sanduhr für Claude is an
independent third-party tool, not affiliated with, endorsed by, or
sponsored by Anthropic PBC.

PRIVACY

No usage data, credentials, or telemetry are transmitted to 626 Labs or
any third party. The user's Claude.ai session is used solely to read the
user's own usage from Claude.ai and is stored locally in the Windows
Credential Manager.

If anything in this submission is unclear, please reach out and we will
respond same-day.

Estevan Hernandez
626 Labs LLC
```

---

## Pre-submission sanity check (v3.1.0.0-specific)

- [ ] `<Version>` / `<AssemblyVersion>` / `<FileVersion>` in `Sanduhr.App.csproj` are `3.1.0.0`
- [ ] `<Identity Version>` in `Package.appxmanifest` is `3.1.0.0` (4th component `.0`)
- [ ] `dist/Sanduhr-Store-v3.1.0.0.msix` built off the `v3.1.0.0` tag, **unsigned**
- [ ] App declares ONLY `runFullTrust` — no `broadFileSystemAccess`, no `internetClient`
- [ ] **Branded Store tiles in place** — no placeholders to Store (Square150x150 / Square44x44 /
      Wide310x150 / splash)
- [ ] Trademark disclaimer present on all six surfaces (Store description, Copyright field, manifest
      Description, privacy policy, README, About box)
- [ ] **Navigation: every feature reachable from the visible bottom tool strip** with accessible
      names + tooltips; no feature is right-click-only (the past 10.1.4.4(c) finding)
- [ ] **Dialogs legible on a light-mode Windows host** — re-verify the themed dialog + theme flyout
- [ ] WebView2 sign-in: cookie read ONLY from the `claude.ai` origin; stored ONLY in Credential
      Manager; isolated user-data folder; manual paste fallback works; Expired/Blocked re-auth card
- [ ] No `sessionKey` / `cf_clearance` in any committed file or log (run the secrets grep)
- [ ] **Fresh listing screenshots of the v3.1.0 UI** (tool strip, a theme change, the Horizon graph,
      Compact mode) — minimum three, ideally six; the old screenshots show the 2.x Python UI
- [ ] Reviewer letter (this file's `---` block) pasted into Partner Center → Notes for certification
- [ ] Store listing → "What's new in this version" filled (public field, separate from the above)
- [ ] `dotnet test windows-dotnet/Sanduhr.slnx` green

## Source

This file is the v3.1.0.0 reviewer letter — the .NET app's Store debut (the Store was on Python
v2.3). It supersedes [`reviewer-letter-3.0.0.0.md`](./reviewer-letter-3.0.0.0.md), which was prepped
for a 3.0.0 submission that never went out. Lead each future per-version letter with that release's
disclosure-surface delta AND any open review-finding it closes. The original Python-build submission
lessons (10.1.4.4) live in [`../ms-store-submission-playbook.md`](../ms-store-submission-playbook.md).
