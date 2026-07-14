# Notes for certification — reviewer letter (v3.2.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission
> options** → **Notes for certification**. That field caps at **~2000 characters** — this letter is
> front-loaded so the disclosure detail survives any truncation (see playbook gotcha #8).
>
> Framing: v3.1.0 is the approved .NET build on the same listing. v3.2.0 adds ONE
> disclosure-surface change — a new class of at-rest data, the **opt-in local usage history
> vault** — so the letter leads with it: what is stored (token totals only), where (local
> AppData), the consent gate, and the erase path. Everything else is framed as preserved.
> Same Identity Name (`626LabsLLC.SanduhrfrClaude`) and Publisher CN — an update, not a new app.

---

```
Hello reviewer,

This updates the .NET build you approved as v3.1.0 — same Identity
(626LabsLLC.SanduhrfrClaude), same Publisher CN, same listing. One
disclosure-surface change, detailed first. No telemetry. Declares ONLY
runFullTrust.

Disclosure-surface change (new at-rest data, opt-in): a local usage history
vault. Claude Code (Anthropic's developer CLI) deletes its own local session
logs after ~30 days. With explicit opt-in — a first-use consent prompt with
per-folder checkboxes, changeable any time in Settings — Sanduhr summarizes
those logs into token TOTALS (counts per day, model, and project) stored
under %LOCALAPPDATA%\Sanduhr\vault, on this machine only. It never stores
conversation content, prompts, code, or file contents, and nothing is
uploaded anywhere. The user can pause archiving per folder or erase the
entire vault from Settings at any time; declining the prompt leaves every
feature working. The new Claude Usage tab (overview, trends, sessions,
calendar) reads this vault plus the same local logs v2.3/v3.1 already read.

Preserved from v3.1.0 — no regression on prior review points:
- Sign-in unchanged: WebView2 loads the real claude.ai/login; we never read
  the password, only our own cookie jar's sessionKey, stored in Windows
  Credential Manager. Manual paste fallback retained — the expected Google
  path (Google blocks sign-in inside embedded webviews; not a bug).
- Navigation: every feature on the always-visible bottom tool strip with
  accessible names + tooltips; nothing is right-click-only.
- Dialogs: themed, legible in light OR dark mode.
- runFullTrust covers %APPDATA%\Sanduhr\ and %LOCALAPPDATA%\Sanduhr\ file
  access and the DWM Mica API only. No data leaves the device.

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively.
Sanduhr für Claude is an independent third-party tool, not affiliated with,
endorsed by, or associated with Anthropic PBC.

Estevan Hernandez
626 Labs LLC
```

---

## If your field is even shorter (sub-1000 char fallback)

```
Updates the approved v3.1.0 — same Identity (626LabsLLC.SanduhrfrClaude) and
Publisher CN. No telemetry; declares ONLY runFullTrust.

One disclosure change (opt-in, at-rest): a local usage history vault. With
explicit consent (first-use prompt, per-folder, changeable in Settings),
Sanduhr stores token TOTALS from Claude Code's local session logs — counts
per day/model/project — under %LOCALAPPDATA%\Sanduhr\vault, local only.
Never conversation content; nothing uploaded; erasable any time; declining
changes nothing else.

Unchanged from v3.1.0: WebView2 sign-in (own cookie jar's sessionKey only,
Credential Manager; paste fallback is the expected Google path), tool-strip
navigation with accessible names, themed light/dark-legible dialogs.
runFullTrust is for local AppData access + the DWM Mica API only.

"Claude"/"claude.ai" are Anthropic PBC trademarks, used nominatively. Sanduhr
für Claude is an independent third-party tool, not affiliated with Anthropic.

Estevan Hernandez, 626 Labs LLC
```

---

## Pre-submission sanity check (v3.2.0.0-specific)

- [ ] `<Version>` / `<AssemblyVersion>` / `<FileVersion>` in `Sanduhr.App.csproj` are `3.2.0.0`
- [ ] `<Identity Version>` in `Package.appxmanifest` is `3.2.0.0` (4th component `.0`)
- [ ] `dist/Sanduhr-Store-v3.2.0.0.msix` built off the `v3.2.0.0` tag, **unsigned**
- [ ] App declares ONLY `runFullTrust` — no `broadFileSystemAccess`, no `internetClient`
- [ ] **Branded Store tiles still in place** (unchanged since 3.1.0)
- [ ] **Vault consent flow verified on the MSIX build** — first-use prompt shows, "Not now"
      declines cleanly, Settings ▸ Claude Usage toggles per-home consent, Erase archive works
- [ ] **MSIX virtualization re-check** (smoke scenario 12) — vault writes land at the REAL
      `%LOCALAPPDATA%\Sanduhr\vault`, not a virtualized copy (verified off on 3.1.0; re-verify)
- [ ] `docs/PRIVACY.md` carries the vault row (totals only, local only, erase path) — it does
- [ ] Trademark disclaimer present on all six surfaces (Store description, Copyright field,
      manifest Description, privacy policy, README, About box)
- [ ] **Navigation preserved** — Claude Usage tab reachable from Settings; widget features
      unchanged on the bottom tool strip
- [ ] **Dialogs legible on a light-mode Windows host** — include the vault consent dialog and
      the verb-labeled erase dialogs ("Erase it / Keep data", "Erase everything / Cancel")
- [ ] No `sessionKey` / `cf_clearance` in any committed file or log (run the secrets grep);
      `sanduhr.log` carries operation + exception type only — no paths, labels, or log content
- [ ] **Publish-size delta vs 3.1.0 noted** — self-contained MSIX; flag any unexplained jump
- [ ] Reviewer letter (the `---` block above) pasted into Partner Center → Notes for certification
- [ ] Store listing → "What's new in this version" filled — see `listing-copy-3.2.0.md`
- [ ] `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj` green (434 tests)

## Source

This file is the v3.2.0.0 reviewer letter — the first Store update after the .NET debut
(v3.1.0.0, approved). It supersedes [`reviewer-letter-3.1.0.0.md`](./reviewer-letter-3.1.0.0.md).
The full submission lessons (10.1.4.4) live in
[`../ms-store-submission-playbook.md`](../ms-store-submission-playbook.md). Public listing copy is
in [`listing-copy-3.2.0.md`](./listing-copy-3.2.0.md).
