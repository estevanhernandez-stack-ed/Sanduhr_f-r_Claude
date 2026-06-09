# Notes for certification — reviewer letter (v3.0.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission
> options** → **Notes for certification**.
>
> v3.0.0.0 is a **full platform rebuild** of the shipped Sanduhr für Claude: the app was rewritten
> from Python/PySide6 to .NET 10 / WPF. It submits into the SAME Store listing — identical Identity
> Name (`626LabsLLC.SanduhrfrClaude`) and Publisher CN — so it's an update, not a new app. The
> disclosure surface is the same as the Python build with ONE addition called out first: an
> embedded WebView2 sign-in window that captures the claude.ai session cookie locally. No
> telemetry, no new at-rest data leaving the device, runFullTrust only.

---

```
Hello reviewer,

Thank you for your time on v3.0.0.0. This release is a full rebuild of
Sanduhr für Claude from Python/PySide6 to .NET 10 / WPF. It is the same
product — a desktop widget that shows the signed-in user their own
Claude.ai subscription usage — with the same features and the same data
posture. One item touches the disclosure surface and is detailed first.
No telemetry. No new at-rest data leaves the device. The app declares
ONLY runFullTrust.

WHAT THE APP DOES

  Sanduhr is a floating desktop widget that displays the user's OWN
  Claude.ai usage: how much of their five-hour and seven-day limits they
  have used, a burn-rate projection, pacing markers, and 30-day history
  sparklines. It also includes a deep-work focus timer (a digitised
  hourglass), a small cooldown game for the wait state, five themes, and
  Win11 Mica glass. It reads the user's usage from Claude.ai using the
  user's own logged-in session; it does not send the user's data anywhere
  else.

  "Claude" and "claude.ai" are trademarks of Anthropic PBC, used
  nominatively to describe integration. Sanduhr für Claude is an
  independent third-party tool, not affiliated with, endorsed by, or
  sponsored by Anthropic. (Disclaimer also on the Store description,
  Copyright field, manifest Description, README, About box, and privacy
  policy.)

WHAT CHANGED IN v3.0.0.0

  1. EMBEDDED WEBVIEW2 SIGN-IN (the disclosure-surface change).

     The Python build asked power users to paste their claude.ai
     session key by hand. v3.0.0.0 adds a "Sign in to Claude" window
     that hosts Microsoft's WebView2 control, navigates to the real
     https://claude.ai/login, and lets the user sign in normally
     (Google / email / passkey — all handled by Anthropic's own login
     page). Disclosure posture, stated plainly:

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
     - The manual session-key paste is retained as a fallback for power
       users. No browser-store prying anywhere.
     - The captured cookie is the user's own Claude.ai session, used to
       read the user's own usage. It is never transmitted to 626 Labs or
       any third party.

  2. PLATFORM REBUILD (Python/PySide6 -> .NET 10 / WPF).

     Same features, reimplemented natively. The pacing math, tier model,
     history schema, account/credential storage slots, and Claude.ai
     usage endpoints are ported 1:1 — an existing user's
     %APPDATA%\Sanduhr\ data and Credential Manager entries carry over
     untouched. The MSIX is now self-contained (bundles the .NET 10
     Desktop Runtime) so it installs cleanly on a fresh Windows box.

     The cert-load-bearing "unique lasting value" features from the 2.x
     line are all present and were rebuilt, not dropped: burn-rate
     projection + advanced pacing, the deep-work focus hourglass (rebuilt
     as a branded thin-line glass vessel with a visible falling stream),
     the cooldown snake game, five themes plus user-authored JSON themes
     plus the AI theme-prompt, Win11 Mica glass, and OS-native credential
     storage. Every feature is reachable from a visible tool strip with
     accessible names — no feature hides behind a right-click-only menu.

  3. AUTO-UPDATE (GitHub channel only; not the Store install).

     The GitHub-distributed build uses Velopack for auto-update
     (Setup.exe + delta). The MSIX/Store install does NOT use this path —
     it updates through the Store as normal; the in-app update probe
     no-ops on the packaged install. The probe is best-effort, debounced
     to once per 24h, and never blocks the UI. It contacts only the app's
     own GitHub Releases feed and carries no telemetry.

WHAT STAYED THE SAME

  - The app declares ONLY runFullTrust. No broadFileSystemAccess, no
    internetClient (outgoing HTTPS needs no declaration for a full-trust
    desktop app). runFullTrust is needed because the app reads/writes
    %APPDATA%\Sanduhr\ directly and calls the DWM API for Mica glass.
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

Thank you again for your time and consideration.

Estevan Hernandez
626 Labs LLC
```

---

## Pre-submission sanity check (v3.0.0.0-specific)

- [ ] `<Version>` / `<AssemblyVersion>` / `<FileVersion>` in `Sanduhr.App.csproj` are `3.0.0.0`
- [ ] `<Identity Version>` in `Package.appxmanifest` is `3.0.0.0` (4th component `.0`)
- [ ] `dist/Sanduhr-Store-v3.0.0.0.msix` built off the `v3.0.0.0` tag, **unsigned**
- [ ] App declares ONLY `runFullTrust` — no `broadFileSystemAccess`, no `internetClient`
- [ ] **Branded Store tiles in place** — placeholders replaced via the `626labs-design` skill
      (Square150x150 / Square44x44 / Wide310x150 / splash). Pattern (x): no placeholders to Store.
- [ ] Trademark disclaimer present on all six surfaces (Store description, Copyright field, manifest
      Description, privacy policy, README, About box)
- [ ] WebView2 sign-in: cookie read ONLY from the `claude.ai` origin; stored ONLY in Credential
      Manager; isolated user-data folder; manual paste fallback works
- [ ] No `sessionKey` / `cf_clearance` in any committed file or log (run the secrets grep)
- [ ] Every feature reachable from a visible tool strip with accessible names; dialogs legible on
      light-mode Windows (re-verify the WPF dialogs)
- [ ] Reviewer letter (this file's `---` block) pasted into Partner Center → Notes for certification
- [ ] Store listing → "What's new in this version" filled (public field, separate from the above)
- [ ] `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj` green

## Source

This file is the v3.0.0.0 reviewer letter — the first for the .NET rebuild. Model future
per-version letters after it; lead each with that release's disclosure-surface delta. The original
Python-build submission lessons (10.1.4.4) live in
[`../ms-store-submission-playbook.md`](../ms-store-submission-playbook.md).
