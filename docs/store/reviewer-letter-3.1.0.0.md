# Notes for certification — reviewer letter (v3.1.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission
> options** → **Notes for certification**. That field caps at **~2000 characters**, so this is the
> trimmed version (the full letter truncates on paste — see playbook gotcha #8). It is front-loaded
> so the disclosure detail survives any truncation.
>
> Framing: the last Store version was the Python build **v2.3**, which already passed the 2.x
> navigation + dialog findings. v3.1.0 is a **rebuild of v2.3 to .NET 10 / WPF — same features,
> same data posture** — so the letter leads with the one disclosure-surface change (in-app sign-in)
> and frames navigation/dialogs as **preserved, no regression**, not as new fixes. Same Identity
> Name (`626LabsLLC.SanduhrfrClaude`) and Publisher CN — an update, not a new app.

---

```
Hello reviewer,

This updates the same app last shipped as the Python build v2.3 — same
Identity (626LabsLLC.SanduhrfrClaude), same Publisher CN, same listing. It is
a full rebuild of v2.3 from Python to .NET 10 / WPF: the SAME features and the
SAME data posture, reimplemented natively. The one disclosure-surface change is
detailed first. No telemetry. It declares ONLY runFullTrust.

Disclosure-surface change (sign-in): a WebView2 window loads the real
claude.ai/login. We do NOT intercept or read the password — we only read back
our own cookie jar's claude.ai sessionKey after a successful sign-in and store
it in the Windows Credential Manager (DPAPI). The WebView2 profile is an
app-owned, isolated folder; no other browser's cookies are read. A manual
session-key paste is retained as a fallback — and is the intended path for
Google sign-in, which Google blocks inside embedded webviews: the app detects
the bounce and guides the user to paste (expected behavior, not a bug). The
Python build used that manual paste as its primary path.

Preserved from v2.x — no regression on your prior review points:
- Navigation: every feature stays on one always-visible bottom tool strip, each
  button with an AutomationProperties.Name accessible label and a tooltip;
  nothing is reachable only by right-click.
- Dialogs: themed, and legible in light OR dark mode (no system-palette
  fall-through).
- runFullTrust covers %APPDATA%\Sanduhr\ file access (user-editable themes +
  30-day history) and the DWM Mica API only. No other capability; no data
  leaves the device.

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
(626LabsLLC.SanduhrfrClaude) and Publisher CN. A rebuild of v2.3 to .NET 10 /
WPF: same features, same data posture. No telemetry; declares ONLY runFullTrust.

Main change: sign-in. A WebView2 window loads the real claude.ai/login; we never
read the password, only our own cookie jar's sessionKey, stored in Windows
Credential Manager. Manual paste is retained as fallback — and is the expected
path for Google sign-in, which Google blocks in embedded webviews (not a bug).

Preserved from v2.x (no regression): every feature on one visible tool strip with
accessible names; dialogs themed + legible in light/dark mode. runFullTrust is
for %APPDATA%\Sanduhr\ access + the DWM Mica API only.

"Claude"/"claude.ai" are Anthropic PBC trademarks, used nominatively. Sanduhr für
Claude is an independent third-party tool, not affiliated with or endorsed by
Anthropic PBC.

Estevan Hernandez, 626 Labs LLC
```

---

## Pre-submission sanity check (v3.1.0.0-specific)

- [ ] `<Version>` / `<AssemblyVersion>` / `<FileVersion>` in `Sanduhr.App.csproj` are `3.1.0.0`
- [ ] `<Identity Version>` in `Package.appxmanifest` is `3.1.0.0` (4th component `.0`)
- [ ] `dist/Sanduhr-Store-v3.1.0.0.msix` built off the `v3.1.0.0` tag, **unsigned**
- [ ] App declares ONLY `runFullTrust` — no `broadFileSystemAccess`, no `internetClient`
- [ ] **Branded Store tiles in place** — confirmed branded (626Labs hourglass), not placeholders
- [ ] **Feature parity with the live v2.3 confirmed** — burn-rate, pace ghost, 30-day graph, focus
      timer, snake, themes, multi-account, CSV export, and **local Claude Code token-burn**
      (`CcLogReader`) + **Routines daily-quota** (`GetRoutineBudgetAsync`) all present in the .NET build
- [ ] Trademark disclaimer present on all six surfaces (Store description, Copyright field, manifest
      Description, privacy policy, README, About box)
- [ ] **Navigation preserved** — every feature reachable from the visible bottom tool strip with
      accessible names + tooltips; no feature is right-click-only
- [ ] **Dialogs legible on a light-mode Windows host** — re-verify the themed dialog + theme flyout
- [ ] WebView2 sign-in: cookie read ONLY from the `claude.ai` origin; stored ONLY in Credential
      Manager; isolated user-data folder; manual paste fallback works; Expired/Blocked re-auth card
- [ ] No `sessionKey` / `cf_clearance` in any committed file or log (run the secrets grep)
- [ ] **Screenshots** — the live 2.x shots already show the bottom tool strip + the same layout, so
      they're broadly representative; refresh only if the glass styling/themes drifted noticeably
- [ ] Reviewer letter (the `---` block above) pasted into Partner Center → Notes for certification
- [ ] Store listing → "What's new in this version" filled — see `listing-copy-3.1.0.md`
- [ ] `dotnet test windows-dotnet/Sanduhr.slnx` green

## Source

This file is the v3.1.0.0 reviewer letter — the .NET app's Store debut (the Store was on Python
v2.3). It supersedes [`reviewer-letter-3.0.0.0.md`](./reviewer-letter-3.0.0.0.md), which was prepped
for a 3.0.0 submission that never went out. The full submission lessons (10.1.4.4) live in
[`../ms-store-submission-playbook.md`](../ms-store-submission-playbook.md). Public listing copy is in
[`listing-copy-3.1.0.md`](./listing-copy-3.1.0.md).
