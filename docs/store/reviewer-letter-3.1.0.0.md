# Notes for certification — reviewer letter (v3.1.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission
> options** → **Notes for certification**. That field caps at **~2000 characters**, so this is the
> trimmed version — cert-load-bearing points only (past findings, the sign-in disclosure,
> runFullTrust, trademark), front-loaded so the critical info survives any truncation.
>
> Context: the last version in the Store was the Python build **v2.3**; v3.0.0 (the .NET rebuild)
> was never submitted, so this is the .NET app's debut (v2.3 → v3.1.0). Same Identity Name
> (`626LabsLLC.SanduhrfrClaude`) and Publisher CN — an update, not a new app.

---

```
Hello reviewer,

This updates the same app last shipped as the Python build v2.3 — same
Identity (626LabsLLC.SanduhrfrClaude), same Publisher CN, same listing.
Changes since v2.3: a rebuild to .NET 10 / WPF plus a navigation and feature
overhaul. Same product: a desktop widget showing the signed-in user their
OWN claude.ai usage. No telemetry. It declares ONLY runFullTrust.

Past review findings addressed:

- NAVIGATION: every feature now sits on one always-visible bottom tool strip
  (Theme, Settings, Graph, Compact, Focus, Snake, Refresh, Pin), each with an
  AutomationProperties.Name accessible label and a tooltip. Nothing is
  reachable only by right-click.

- DIALOG LEGIBILITY: all dialogs were replaced with a themed dialog that
  paints its own colors, so it renders legibly in light OR dark mode (no
  system-palette fall-through).

Disclosure-surface change (sign-in): a WebView2 window loads the real
claude.ai/login. We do NOT intercept or read the password — we only read
back our own cookie jar's claude.ai sessionKey after a successful sign-in
and store it in the Windows Credential Manager (DPAPI). The WebView2 profile
is an app-owned, isolated folder; no other browser's cookies are read. A
manual session-key paste is retained as a fallback.

runFullTrust is needed to read/write %APPDATA%\Sanduhr\ (user-editable
themes + 30-day history) and to call the DWM API for the Win11 Mica glass.
No other capability is requested. No data leaves the device.

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively.
Sanduhr für Claude is an independent third-party tool, not affiliated with,
endorsed by, or associated with Anthropic PBC.

Estevan Hernandez
626 Labs LLC
```

---

## If your field is even shorter (sub-1000 char fallback)

```
Updates the same app last shipped as Python v2.3 — same Identity
(626LabsLLC.SanduhrfrClaude) and Publisher CN. Now rebuilt to .NET 10/WPF
with a navigation overhaul. Same product: a widget showing the user's OWN
claude.ai usage. No telemetry; declares ONLY runFullTrust.

Past findings fixed: (1) every feature is on one visible bottom tool strip
with accessible names + tooltips, nothing right-click-only; (2) dialogs are
themed and legible in light or dark mode.

Sign-in: a WebView2 window loads the real claude.ai/login; we never read the
password, only our own cookie jar's sessionKey, stored in Windows Credential
Manager. Manual paste retained as fallback.

runFullTrust is for %APPDATA%\Sanduhr\ file access + the DWM Mica API only.

"Claude"/"claude.ai" are Anthropic PBC trademarks, used nominatively.
Sanduhr für Claude is an independent third-party tool, not affiliated with
or endorsed by Anthropic PBC.

Estevan Hernandez, 626 Labs LLC
```

---

## Pre-submission sanity check (v3.1.0.0-specific)

- [ ] `<Version>` / `<AssemblyVersion>` / `<FileVersion>` in `Sanduhr.App.csproj` are `3.1.0.0`
- [ ] `<Identity Version>` in `Package.appxmanifest` is `3.1.0.0` (4th component `.0`)
- [ ] `dist/Sanduhr-Store-v3.1.0.0.msix` built off the `v3.1.0.0` tag, **unsigned**
- [ ] App declares ONLY `runFullTrust` — no `broadFileSystemAccess`, no `internetClient`
- [ ] **Branded Store tiles in place** — confirmed branded (626Labs hourglass), not placeholders
- [ ] Trademark disclaimer present on all six surfaces (Store description, Copyright field, manifest
      Description, privacy policy, README, About box)
- [ ] **Navigation: every feature reachable from the visible bottom tool strip** with accessible
      names + tooltips; no feature is right-click-only (the past 10.1.4.4(c) finding)
- [ ] **Dialogs legible on a light-mode Windows host** — re-verify the themed dialog + theme flyout
- [ ] WebView2 sign-in: cookie read ONLY from the `claude.ai` origin; stored ONLY in Credential
      Manager; isolated user-data folder; manual paste fallback works; Expired/Blocked re-auth card
- [ ] No `sessionKey` / `cf_clearance` in any committed file or log (run the secrets grep)
- [ ] **Fresh listing screenshots of the v3.1.0 UI** (tool strip, a theme change, the Horizon graph,
      Compact mode) — the old screenshots show the 2.x Python UI
- [ ] Reviewer letter (the `---` block above) pasted into Partner Center → Notes for certification
- [ ] Store listing → "What's new in this version" filled — see `listing-copy-3.1.0.md`
- [ ] `dotnet test windows-dotnet/Sanduhr.slnx` green

## Source

This file is the v3.1.0.0 reviewer letter — the .NET app's Store debut (the Store was on Python
v2.3). It supersedes [`reviewer-letter-3.0.0.0.md`](./reviewer-letter-3.0.0.0.md), which was prepped
for a 3.0.0 submission that never went out. The full submission lessons (10.1.4.4) live in
[`../ms-store-submission-playbook.md`](../ms-store-submission-playbook.md). Public listing copy is in
[`listing-copy-3.1.0.md`](./listing-copy-3.1.0.md).
